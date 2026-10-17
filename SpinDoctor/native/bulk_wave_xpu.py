"""Full-RAM native bulk waves; arithmetic runs only on selected CPU/GPU bits."""
from __future__ import annotations
import ctypes as C,json,math,os,threading,time
from concurrent.futures import ThreadPoolExecutor
import numpy as np
from opencl_gpu import OpenCL,GPUContext,OpenCLError
from full_traversal_reference import memory_status,exact,probe,known_word

GIB=1024**3
MIB=1024**2
MASK=0xFFFFFFFF
NATIVE_CHUNK_BYTES=64*MIB

def plan_budget(config=None):
    config=dict(config or {});allowed={"auto_ram","reserve_gib","budget_mib","cpu_workers","gpu_fraction","read_depth","seed","a","b","wave_count","sample_count","deadline_ms","chunk_mib"}
    if set(config)-allowed:raise ValueError("Unknown planner options: "+", ".join(sorted(set(config)-allowed)))
    observed=probe()
    if not observed["available"]:raise OpenCLError(observed.get("error","A real GPU is required; no CPU fallback is used."))
    threads=exact(config.get("cpu_workers",8),"cpu_workers") or (os.cpu_count() or 1)
    if not 1<=threads<=64:raise ValueError("cpu_workers must be1..64 or0 for all logical processors.")
    depth=exact(config.get("read_depth",1),"read_depth");waves=exact(config.get("wave_count",1),"wave_count");count=exact(config.get("sample_count",1060),"sample_count")
    if not 1<=depth<=16 or not 1<=waves<=16 or not 1<=count<=1060:raise ValueError("read_depth/wave_count must be1..16; sample_count1..1060 digits of4 selected bits.")
    seed=exact(config.get("seed",1),"seed");a=exact(config.get("a",1),"a");b=exact(config.get("b",1),"b")
    if seed>MASK or a not in (0,1) or b not in (0,1):raise ValueError("seed is uint32 and a/b are binary coefficients.")
    reserve=config.get("reserve_gib",2);fraction=config.get("gpu_fraction",.5);deadline=config.get("deadline_ms",2)
    for value,name in ((reserve,"reserve_gib"),(fraction,"gpu_fraction"),(deadline,"deadline_ms")):
        if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value):raise ValueError(name+" must be finite.")
    if reserve<2 or reserve>1024 or not 0<fraction<1 or deadline<=0 or deadline>1000:raise ValueError("Reserve>=2GiB, GPU sample share strictly0..1, deadline>0..1000ms.")
    reserve_bytes=math.ceil(reserve*GIB);overhead=512*MIB+threads*4*MIB;available=observed["memory"]["availableBytes"];usable=available-reserve_bytes-overhead
    auto=config.get("auto_ram","budget_mib" not in config)
    if not isinstance(auto,bool):raise ValueError("auto_ram must be Boolean.")
    payload=usable if auto else exact(config.get("budget_mib",64),"budget_mib")*MIB
    payload=payload//65536*65536
    if payload<65536 or payload>usable:raise MemoryError("Payload must fit actual free physicalRAM minus reserve/overhead.")
    wave_bytes=payload if config.get("chunk_mib") is None else min(payload,exact(config["chunk_mib"],"chunk_mib")*MIB)
    if wave_bytes<65536:raise ValueError("Explicit burst must contain at least64KiB.")
    gpu_samples=max(1,min(count*4-1,int(count*4*fraction)));cpu_samples=count*4-gpu_samples
    if gpu_samples*16>observed["gpuDevice"]["maxAllocationBytes"] or gpu_samples*24>observed["gpuDevice"]["globalMemoryBytes"]:raise MemoryError("Selected-bit GPU staging exceeds the actual device allocation/global-memory limits.")
    initial=(seed^0xA5)&255
    if initial in (0,255):initial=0xA5
    normalized={"auto_ram":auto,"reserve_gib":reserve,"cpu_workers":threads,"gpu_fraction":fraction,"read_depth":depth,"seed":seed,"a":a,"b":b,"wave_count":waves,"sample_count":count,"deadline_ms":deadline}
    if not auto:normalized["budget_mib"]=exact(config.get("budget_mib",64),"budget_mib")
    if config.get("chunk_mib") is not None:normalized["chunk_mib"]=exact(config["chunk_mib"],"chunk_mib")
    return {"schema":"CPU-GPU-XPU/BULK-PLAN/1","mode":"bulk-fill-selected-compute","config":normalized,"bitCount":str(payload*8),"wordCount":payload//4,"totalReservoirBytes":payload,"effectiveWaveBytes":wave_bytes,"fullReservoirWave":wave_bytes==payload,"cpuWorkers":threads,"readDepth":depth,"waveCount":waves,"sampleCount":count,"selectedBitsPerPass":count*4,"cpuSelectedBitsPerPass":cpu_samples,"gpuSelectedBitsPerPass":gpu_samples,"initialPatternByte":initial,"gpuDevice":observed["gpuDevice"],"probe":observed,"memory":{**observed["memory"],"reserveBytes":reserve_bytes,"overheadBytes":overhead,"payloadBytes":payload,"payloadBudgetBytes":payload,"sharedPhysicalBudgetCountedOnce":True,"gpuDeviceStagingBytes":gpu_samples*24,"hostSampleArrayBytes":count*4*8+gpu_samples*16,"scope":"One page-aligned host RAM reservoir only. Integrated GPU receives bounded selected-bit staging, never a full-reservoir mirror. Both consume this one physical memory budget."}}

class Reservoir:
    def __init__(self,size):
        self.size=size;self.pointer=None
        if os.name=="nt":
            self.kernel=C.WinDLL("kernel32",use_last_error=True);self.kernel.VirtualAlloc.argtypes=[C.c_void_p,C.c_size_t,C.c_ulong,C.c_ulong];self.kernel.VirtualAlloc.restype=C.c_void_p;self.kernel.VirtualFree.argtypes=[C.c_void_p,C.c_size_t,C.c_ulong];self.kernel.VirtualFree.restype=C.c_int
            self.pointer=self.kernel.VirtualAlloc(None,size,0x3000,4)
            if not self.pointer:raise MemoryError("Actual VirtualAlloc failed: "+str(C.get_last_error()))
        else:
            import mmap
            self.mapping=mmap.mmap(-1,size);self.anchor=(C.c_ubyte*1).from_buffer(self.mapping);self.pointer=C.addressof(self.anchor)
        self.library=C.CDLL("msvcrt.dll") if os.name=="nt" else C.CDLL(None);self.library.memset.argtypes=[C.c_void_p,C.c_int,C.c_size_t];self.library.memset.restype=C.c_void_p
    def fill(self,offset,length,pattern):self.library.memset(self.pointer+offset,pattern,length)
    def byte(self,index):return C.c_ubyte.from_address(self.pointer+index).value
    def release(self):
        if self.pointer:
            if os.name=="nt":
                if not self.kernel.VirtualFree(self.pointer,0,0x8000):raise OSError("VirtualFree failed.")
            else:self.anchor=None;self.mapping.close()
            self.pointer=None

class SampleGPU:
    def __init__(self,count,planned):
        cl=OpenCL();device=next((device for device in cl.devices() if device["hostUnifiedMemory"] and device["name"]==planned["name"]),None)
        if device is None:raise OpenCLError("Actual planned GPU unavailable; no CPU fallback.")
        self.context=GPUContext(cl,device);self.count=count
        try:self.before=self.context.allocate(count*4);self.observed=self.context.allocate(count*4);self.output=self.context.allocate(count*16)
        except Exception:self.context.release();raise
    def compute(self,before,observed,a,b):
        cl=self.context.cl;count=len(before)
        for buffer,array in ((self.before,before),(self.observed,observed)):cl.check(cl.clEnqueueWriteBuffer(self.context.queue,buffer,1,0,array.nbytes,C.c_void_p(array.ctypes.data),0,None,None),"actual selected-bit GPU input transfer")
        measured=self.context.dispatch("samples",[C.c_void_p(self.before),C.c_void_p(self.observed),C.c_void_p(self.output),C.c_uint(a),C.c_uint(b),C.c_uint(count)],count)
        traces=np.empty((count,4),dtype=np.uint32);cl.check(cl.clEnqueueReadBuffer(self.context.queue,self.output,1,0,traces.nbytes,C.c_void_p(traces.ctypes.data),0,None,None),"completed selected-bit GPU result readback")
        return traces,measured
    def release(self):self.context.release()

class PressurePause(RuntimeError):pass

class Session:
    def __init__(self,plan,on_progress=None,cancel_event=None):
        self.plan=json.loads(json.dumps(plan));self.config=self.plan["config"];self.on_progress=on_progress;self.cancel_event=cancel_event or threading.Event();self.lock=threading.RLock();self.running=False;self.status="READY";self.phase="allocation";self.error=None;self.ram=None;self.gpu=None;self.pool=None;self.elapsed_ms=0.;self.init_bytes=0;self.bulk_bytes=0;self.wave_done_bytes=0;self.waves=[];self.current_pattern=plan["initialPatternByte"];self._before=None;self._active_wave=None;self._recent=[];self._fill_cursors={};self.sample_reads=0;self.sample_passes=0;self.sample_calls=0;self.template_ops=0
        if plan.get("schema")!="CPU-GPU-XPU/BULK-PLAN/1":raise ValueError("Use an actual bulk plan_budget output.")
        self.counters={backend:{"sampledBits":0,"multiplications":0,"additions":0,"reductions":0,"kernelDispatches":0,"kernelEventMs":0.} for backend in ("cpu","gpu")}
        self._ids=self._addresses(plan["effectiveWaveBytes"]*8,plan["selectedBitsPerPass"])
        self._previous_wave_start=None
    def _addresses(self,bit_count,count):
        seed=self.config["seed"];return [str(((known_word(seed,index*2+1)<<32)|known_word(seed,index*2+2))%bit_count) for index in range(count)]
    def _pressure(self):
        if memory_status()["availableBytes"]<self.plan["memory"]["reserveBytes"]:raise PressurePause("Free physical RAM fell below the reserve floor; no further native chunks are dispatched.")
    def snapshot(self):
        with self.lock:
            initialized=self.init_bytes if self.ram else 0;operations=sum(value["multiplications"]+value["additions"] for value in self.counters.values())
            return {"schema":"CPU-GPU-XPU/BULK-SESSION/1","status":self.status,"phase":self.phase,"plan":self.plan,"mode":self.plan["mode"],"cpu":{key:str(value) if isinstance(value,int) else value for key,value in self.counters["cpu"].items()},"gpu":{key:str(value) if isinstance(value,int) else value for key,value in self.counters["gpu"].items()},"completedWaves":str(len(self.waves)),"completedRounds":"0","materializedStoredWorkerBits":str(initialized*8),"currentAllocatedBytes":self.plan["totalReservoirBytes"] if self.ram else 0,"totalReservoirBytes":self.plan["totalReservoirBytes"],"fullReservoirWave":self.plan["fullReservoirWave"],"effectiveWaveBytes":self.plan["effectiveWaveBytes"],"initializedBytes":str(self.init_bytes),"bulkBytesWritten":str(self.bulk_bytes),"bulkBitsWritten":str(self.bulk_bytes*8),"bytesWrittenThisWave":str(self.wave_done_bytes),"sampledMultiplyAddOperations":str(operations),"completedBitOperations":"0","completedSteps":"0","packedAndExpressions":"0","packedXorExpressions":"0","sampledBitEvaluations":str(sum(value["sampledBits"] for value in self.counters.values())),"sampledReductions":str(sum(value["reductions"] for value in self.counters.values())),"templateByteOperations":str(self.template_ops),"currentPatternByte":self.current_pattern,"elapsedConcurrentHostMs":self.elapsed_ms,"sampling":{"actualSelectedByteReads":str(self.sample_reads),"passes":str(self.sample_passes),"calls":str(self.sample_calls),"hostSampleArrayBytes":self.plan["memory"]["hostSampleArrayBytes"],"gpuDeviceStagingBytes":self.plan["memory"]["gpuDeviceStagingBytes"],"fullReservoirArithmeticCredit":"0"},"error":self.error,"waves":self.waves,"accounting":"Bulk fills count actual written storage bytes/bits only. CPU and real GPU arithmetic is confined to selected bits; no full-RAM recurrence credit, floating-point throughput or enqueue-only completion claim."}
    def _notify(self):
        if self.on_progress:self.on_progress(self.snapshot())
    def _fill(self,length,pattern,initialization=False):
        def worker(index):
            start=index*length//self.plan["cpuWorkers"];end=(index+1)*length//self.plan["cpuWorkers"];cursor=self._fill_cursors.get(index,start)
            while cursor<end:
                if self.cancel_event.is_set():return
                self._pressure();take=min(NATIVE_CHUNK_BYTES,end-cursor);self.ram.fill(cursor,take,pattern)
                with self.lock:
                    cursor+=take;self._fill_cursors[index]=cursor
                    if initialization:self.init_bytes+=take
                    else:self.bulk_bytes+=take;self.wave_done_bytes+=take
                self._notify()
        futures=[self.pool.submit(worker,index) for index in range(self.plan["cpuWorkers"])]
        first_error=None
        for future in futures:
            try:future.result()
            except Exception as error:self.cancel_event.set();first_error=first_error or error
        if first_error:raise first_error
        return all(self._fill_cursors.get(index,index*length//self.plan["cpuWorkers"])==(index+1)*length//self.plan["cpuWorkers"] for index in range(self.plan["cpuWorkers"]))
    def _read(self,ids):
        values=np.empty(len(ids),dtype=np.uint32)
        for index,value in enumerate(ids):address=int(value);values[index]=(self.ram.byte(address//8)>>(address%8))&1
        self.sample_reads+=len(ids);return values
    def _compute(self,before,observed,a,b,record_ids):
        gpu_count=max(1,min(len(before)-1,int(len(before)*self.config["gpu_fraction"])));cpu_count=len(before)-gpu_count
        if cpu_count<1 or gpu_count<1:raise ValueError("Both CPU and GPU require at least one real selected bit.")
        def cpu():
            product=np.multiply(before[:cpu_count],np.uint32(a));raw=np.add(product,np.uint32(b));result=np.bitwise_and(raw,np.uint32(1));return np.column_stack((product,raw,result,result==observed[:cpu_count])).astype(np.uint32)
        with ThreadPoolExecutor(max_workers=2,thread_name_prefix="xpu-samples") as executor:
            cpu_future=executor.submit(cpu);gpu_future=executor.submit(self.gpu.compute,np.ascontiguousarray(before[cpu_count:]),np.ascontiguousarray(observed[cpu_count:]),a,b)
            results={};failure=None
            for backend,count,future in (("cpu",cpu_count,cpu_future),("gpu",gpu_count,gpu_future)):
                try:
                    results[backend]=future.result()
                    with self.lock:
                        for key in ("sampledBits","multiplications","additions","reductions"):self.counters[backend][key]+=count
                        if backend=="gpu":self.counters["gpu"]["kernelDispatches"]+=1;self.counters["gpu"]["kernelEventMs"]+=results[backend][1]
                except Exception as error:failure=failure or error
            if failure:raise failure
            cpu_traces=results["cpu"];gpu_traces,event_ms=results["gpu"]
        # Successful branch arithmetic is retained even if another branch/comparison fails.
        with self.lock:self.sample_passes+=1
        traces=np.vstack((cpu_traces,gpu_traces))
        if not bool(np.all(traces[:,3])):raise RuntimeError("Actual selected CPU/GPU results disagree with stored RAM bits.")
        return [{"bitId":record_ids[index],"backend":"cpu" if index<cpu_count else "gpu","previous":int(before[index]),"observed":int(observed[index]),"product":int(trace[0]),"rawSum":int(trace[1]),"result":int(trace[2]),"matches":bool(trace[3])} for index,trace in enumerate(traces)]
    def _digits(self,traces):
        return [{"index":index//4,"bitIds":[item["bitId"] for item in traces[index:index+4]],"bits":[item["observed"] for item in traces[index:index+4]],"value":sum(item["observed"]<<offset for offset,item in enumerate(traces[index:index+4])),"digit":sum(item["observed"]<<offset for offset,item in enumerate(traces[index:index+4]))%10} for index in range(0,len(traces),4)]
    def execute(self):
        with self.lock:
            if self.running:raise RuntimeError("Already running.")
            if self.phase in ("released","error"):raise RuntimeError("Create a fresh Session after release/error.")
            if self.phase=="complete":return self.report()
            self.running=True;self.status="RUNNING";self.error=None
        started=time.perf_counter()
        try:
            while not self.cancel_event.is_set():
                if self.phase=="allocation":
                    self._pressure();self.ram=Reservoir(self.plan["totalReservoirBytes"]);self.gpu=SampleGPU(self.plan["gpuSelectedBitsPerPass"],self.plan["gpuDevice"]);self.pool=ThreadPoolExecutor(max_workers=self.plan["cpuWorkers"],thread_name_prefix="xpu-native-fill");self.phase="initialization";self._notify()
                elif self.phase=="initialization":
                    if not self._fill(self.plan["totalReservoirBytes"],self.plan["initialPatternByte"],True):break
                    self._fill_cursors={};self.phase="wave";self._notify()
                elif self.phase=="wave":
                    if self._active_wave is None:
                        self.wave_done_bytes=0;self._active_wave={"index":len(self.waves)+1,"start":time.perf_counter(),"previousPatternByte":self.current_pattern,"nextPatternByte":((self.current_pattern&(255 if self.config["a"] else 0))^(255 if self.config["b"] else 0))};self.template_ops+=2;self._before=self._read(self._ids)
                    fill_start=time.perf_counter()
                    try:filled=self._fill(self.plan["effectiveWaveBytes"],self._active_wave["nextPatternByte"])
                    finally:self._active_wave["fillMs"]=self._active_wave.get("fillMs",0)+(time.perf_counter()-fill_start)*1000
                    if not filled:break
                    fill_ms=self._active_wave["fillMs"];traces=None
                    for _ in range(self.plan["readDepth"]):
                        observed=self._read(self._ids);traces=self._compute(self._before,observed,self.config["a"],self.config["b"],self._ids)
                    completed_ms=(time.perf_counter()-self._active_wave["start"])*1000;deadline=self.config["deadline_ms"]
                    wave={key:value for key,value in self._active_wave.items() if key!="start"};wave.update(bytesWrittenThisWave=self.wave_done_bytes,bitsWrittenThisWave=str(self.wave_done_bytes*8),totalReservoirBytes=self.plan["totalReservoirBytes"],fullReservoirWave=self.plan["fullReservoirWave"],fillOnlyMs=fill_ms,completionMs=completed_ms,deadlineMs=deadline,targetMet=completed_ms<deadline,completed=True,measuredWriteBandwidthBitsPerSecond=self.wave_done_bytes*8*1000/completed_ms if completed_ms>0 else None,sampledMultiplyAddOperations=str(self.plan["selectedBitsPerPass"]*2*self.plan["readDepth"]),scope="Completion waits all actual native fill futures and requested CPU/GPU selected-bit compute/readback passes. This writes storage bits, not one arithmetic operation per filled bit.")
                    cadence_ms=None if self._previous_wave_start is None else (self._active_wave["start"]-self._previous_wave_start)*1000
                    wave.update(latencyTargetMet=completed_ms<deadline,cadenceMs=cadence_ms,cadenceTargetMet=None if cadence_ms is None else cadence_ms<deadline,targetMet=completed_ms<deadline and cadence_ms is not None and cadence_ms<deadline,cadenceScope="Issue-to-issue wall interval, including prior completion and intervening bookkeeping. First wave has no observed cadence.")
                    self._previous_wave_start=self._active_wave["start"]
                    self.waves.append(wave);self.current_pattern=wave["nextPatternByte"];self._recent=traces;self._active_wave=None;self._before=None;self._fill_cursors={};self.phase="complete" if len(self.waves)==self.plan["waveCount"] else "wave";self._notify()
                    if self.phase=="complete":break
            self.status="COMPLETE" if self.phase=="complete" else "PAUSED"
        except PressurePause as error:self.status="PAUSED";self.error=str(error)
        except Exception as error:self.status="ERROR";self.phase="error";self.error=str(error);self._free()
        finally:
            with self.lock:self.running=False;self.elapsed_ms+=(time.perf_counter()-started)*1000
            self._notify()
        return self.report()
    def sample(self,bit_ids=None):
        with self.lock:
            if self.running or not self.ram or self.init_bytes!=self.plan["totalReservoirBytes"]:raise RuntimeError("Pause and fully initialize live RAM before selected computation.")
            if bit_ids is not None and not isinstance(bit_ids,list):raise ValueError("Selected bit IDs must be a list.")
            ids=self._ids if bit_ids is None else [str(exact(value,"bitId")) for value in bit_ids]
            if len(ids)<2 or len(ids)>self.plan["selectedBitsPerPass"] or any(int(value)>=self.plan["totalReservoirBytes"]*8 for value in ids):raise ValueError("Provide2 through configured selected-bit count live addresses in range.")
            observed=self._read(ids);before=np.empty(len(ids),dtype=np.uint32)
            for index,value in enumerate(ids):
                byte=int(value)//8;pattern=self.current_pattern if byte<self.plan["effectiveWaveBytes"] else self.plan["initialPatternByte"]
                if self._active_wave and byte<self.plan["effectiveWaveBytes"]:
                    for worker,cursor in self._fill_cursors.items():
                        start=worker*self.plan["effectiveWaveBytes"]//self.plan["cpuWorkers"]
                        if start<=byte<cursor:pattern=self._active_wave["nextPatternByte"];break
                before[index]=(pattern>>(int(value)%8))&1
            traces=self._compute(before,observed,1,0,ids);self.sample_calls+=1;self._recent=traces
            return {"entries":traces,"digits":self._digits(traces),"allSamplesMatch":True,"sampledMultiplyAddOperations":str(len(ids)*2),"scope":"Fresh actual selected RAM reads checked against the known byte template by CPU/GPU identity multiply-add. Queries add selected arithmetic only, never full-reservoir credit."}
    def report(self):return {"schema":"CPU-GPU-XPU/BULK-EXECUTION/1","status":self.status,"snapshot":self.snapshot(),"waves":self.waves,"recentSamples":self._recent,"digits":self._digits(self._recent),"memoryTelemetry":memory_status(),"scope":"Native bulk RAM writes plus genuine selected CPU/GPU arithmetic. Full-wave deadlines use completed wall latency. GPU inputs are explicitly staged selected bits; no direct full-reservoir GPU ownership or full-RAM arithmetic credit."}
    def _free(self):
        if self.pool:self.pool.shutdown(wait=True);self.pool=None
        if self.gpu:self.gpu.release();self.gpu=None
        if self.ram:self.ram.release();self.ram=None
    def release(self):
        with self.lock:
            if self.running:raise RuntimeError("Pause and wait for current chunks before releasing.")
            self._free();self.phase="released";self.status="RELEASED";return self.snapshot()
