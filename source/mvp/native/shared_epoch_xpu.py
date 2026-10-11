"""Shared epochs over actual RAM base bits, with genuine CPU/GPU sampled XOR.

Each completed epoch represents one binary XOR per stored base-bit address.
RAM is initialized once and is not rewritten during epochs.  Virtual credit is
distinct from actual selected XOR calculations, reads, and initialization writes.
"""
from __future__ import annotations
import argparse,ctypes as C,hashlib,json,math,os,secrets,threading,time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import numpy as np
from bulk_wave_xpu import Reservoir
from full_traversal_reference import exact,known_word,memory_status,probe as hardware_probe
from xor_opencl_gpu import XORGPU

GIB=1024**3;MIB=1024**2;MASK=0xFFFFFFFF;CHUNK=64*MIB;EPOCH_SALT=0xA511E9B3

def epoch_seed(seed,epoch):return known_word(seed^EPOCH_SALT,epoch)

def shape_array(seed,indices):
    x=np.uint32(seed)^indices.astype(np.uint32)^(indices>>np.uint64(32)).astype(np.uint32)
    x=(x^(x>>np.uint32(16)))*np.uint32(0x7feb352d)
    x=(x^(x>>np.uint32(15)))*np.uint32(0x846ca68b)
    return x^(x>>np.uint32(16))

def cpu_pairs(base,words,offsets,current_seed,next_seed):
    m0=(shape_array(current_seed,words)>>offsets)&np.uint32(1)
    m1=(shape_array(next_seed,words)>>offsets)&np.uint32(1)
    a=base^m0;b=m0^m1;answer=a^b;expected=base^m1
    return np.column_stack((base,a,b,answer,expected,m0,m1,answer==expected)).astype(np.uint32)

def probe():
    result=hardware_probe();result["xorGpuProbe"]={"status":"UNAVAILABLE"}
    if result["available"]:
        gpu=None
        try:
            gpu=XORGPU(4,result["gpuDevice"])
            base=np.array([0,0,1,1],dtype=np.uint32);words=np.array([0,1,2,3],dtype=np.uint64);offsets=np.array([0,7,16,31],dtype=np.uint32)
            seeds=(epoch_seed(1,0),epoch_seed(1,1));actual,event_ms=gpu.compute(base,words,offsets,*seeds)
            expected=cpu_pairs(base,words,offsets,*seeds)
            if not np.array_equal(actual,expected):raise RuntimeError("Actual XOR GPU probe disagrees with CPU reference.")
            result["xorGpuProbe"]={"status":"PASS","selectedPairs":4,"primaryXorOperations":"4","kernelEventMs":event_ms,"scope":"Probe calculations excluded from session counters."}
        except Exception as error:result["available"]=False;result["error"]=str(error)
        finally:
            if gpu:gpu.release()
    return result

def plan_budget(config=None):
    config=dict(config or {})
    allowed={"auto_ram","reserve_gib","budget_mib","cpu_workers","gpu_fraction","seed","sample_count","duration_seconds","epoch_interval_ms","deadline_ms"}
    if set(config)-allowed:raise ValueError("Unknown shared-epoch options: "+", ".join(sorted(set(config)-allowed)))
    observed=probe()
    if not observed["available"]:raise RuntimeError(observed.get("error","Actual GPU XOR probe failed."))
    workers=exact(config.get("cpu_workers",4),"cpu_workers") or (os.cpu_count() or 1)
    if not 1<=workers<=64:raise ValueError("cpu_workers must be1..64 or0 for all logical CPUs.")
    count=exact(config.get("sample_count",64),"sample_count")
    if not 2<=count<=4096:raise ValueError("sample_count must be2..4096 actual selected pairs per epoch.")
    seed=exact(config["seed"] if "seed" in config else secrets.randbits(32),"seed")
    if seed>MASK:raise ValueError("seed must fit uint32.")
    reserve=config.get("reserve_gib",2);fraction=config.get("gpu_fraction",.5);duration=config.get("duration_seconds",2);interval=config.get("epoch_interval_ms",0);deadline=config.get("deadline_ms",interval or 2)
    for value,name in ((reserve,"reserve_gib"),(fraction,"gpu_fraction"),(duration,"duration_seconds"),(interval,"epoch_interval_ms"),(deadline,"deadline_ms")):
        if isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value):raise ValueError(name+" must be finite.")
    if not 2<=reserve<=1024 or not 0<fraction<1 or not .01<=duration<=300 or not 0<=interval<=1000 or not 0<deadline<=1000:raise ValueError("Reserve>=2GiB; GPU fraction0..1; duration.01..300s; interval0..1000ms; deadline>0..1000ms.")
    auto=config.get("auto_ram","budget_mib" not in config)
    if not isinstance(auto,bool):raise ValueError("auto_ram must be Boolean.")
    reserve_bytes=math.ceil(reserve*GIB);overhead=512*MIB+workers*4*MIB;usable=observed["memory"]["availableBytes"]-reserve_bytes-overhead
    payload=usable if auto else exact(config.get("budget_mib",64),"budget_mib")*MIB
    payload=payload//65536*65536
    if payload<65536 or payload>usable:raise MemoryError("RAM payload must fit actual free physical RAM minus reserve/overhead.")
    gpu_count=max(1,min(count-1,int(count*fraction)));cpu_count=count-gpu_count
    normalized={"auto_ram":auto,"reserve_gib":reserve,"cpu_workers":workers,"gpu_fraction":fraction,"seed":seed,"sample_count":count,"duration_seconds":duration,"epoch_interval_ms":interval,"deadline_ms":deadline}
    if not auto:normalized["budget_mib"]=exact(config.get("budget_mib",64),"budget_mib")
    pattern=(0x0F,0x33,0x55,0x69,0x96,0xAA,0xCC,0xF0)[known_word(seed,0)%8]
    return {"schema":"XOR-PAIR-XPU/PLAN/1","mode":"shared-epoch-black-white-xor","config":normalized,"domainBitWorkers":str(payload*8),"bitCount":str(payload*8),"totalReservoirBytes":payload,"wordCount":payload//4,"cpuWorkers":workers,"selectedPairsPerEpoch":count,"cpuSelectedPairsPerEpoch":cpu_count,"gpuSelectedPairsPerEpoch":gpu_count,"initialPatternByte":pattern,"gpuDevice":observed["gpuDevice"],"probe":observed,"memory":{**observed["memory"],"reserveBytes":reserve_bytes,"overheadBytes":overhead,"payloadBytes":payload,"sharedPhysicalBudgetCountedOnce":True,"gpuStagingCapacityPairs":4096,"gpuStagingBytes":4096*48,"scope":"One physically initialized RAM base-bit reservoir. GPU receives only selected base bits/addresses. OS reserve and bounded overhead are excluded; shared RAM is counted once."},"rule":{"black":0,"white":1,"truthTable":[[0,0,0],[0,1,1],[1,0,1],[1,1,0]],"equation":"A(i,e)=base(i) XOR M(i,e); B(i,e)=M(i,e) XOR M(i,e+1); C=A XOR B=base(i) XOR M(i,e+1)","mask":"M(i,e)=bit(known_word(known_word(seed XOR 0xA511E9B3,e),floor(i/32)),i mod32)","scope":"Each individually acknowledged descriptor transition represents one XOR per address. Only selected pairs are explicitly calculated; no averaging, gray value, full-RAM epoch rewrite, or FLOP claim."}}

class PressurePause(RuntimeError):pass

class Session:
    def __init__(self,plan,on_progress=None,cancel_event=None):
        if plan.get("schema")!="XOR-PAIR-XPU/PLAN/1":raise ValueError("Use plan_budget output.")
        self.plan=json.loads(json.dumps(plan));self.config=self.plan["config"];self.on_progress=on_progress;self.cancel_event=cancel_event or threading.Event();self.lock=threading.RLock();self.running=False;self.status="READY";self.phase="allocation";self.error=None
        self.ram=None;self._ram_words=None;self.gpu=None;self.fill_pool=None;self.sample_pool=None;self.init_cursors={};self.init_bytes=0;self.initialization_ms=0.;self.epochs=0;self.wall_seconds=0.;self._active_wall_start=None;self.sample_reads=0;self.descriptor_hashes=0;self.query_calls=0;self._recent=[];self.runs=[];self.descriptor_seed=epoch_seed(self.config["seed"],0)
        self.counters={backend:{"sampledPrimaryXorOperations":0,"auxiliaryRepresentationXorOperations":0,"maskHashEvaluations":0,"maskHashXorExpressions":0,"maskHashShiftExpressions":0,"maskHashMultiplyExpressions":0,"maskDecodeShiftExpressions":0,"maskDecodeAndExpressions":0,"sampledPairs":0,"epochSampledPairs":0,"querySampledPairs":0,"kernelDispatches":0,"kernelEventMs":0.} for backend in ("cpu","gpu")}
        self._timing={"count":0,"sumMs":0.,"minMs":None,"maxMs":0.,"below2Ms":0,"below10Ms":0,"deadlineMisses":0}
        self._cadence={"count":0,"sumMs":0.,"minMs":None,"maxMs":0.,"below2Ms":0,"below10Ms":0,"deadlineMisses":0}
        self._ids=self._addresses(exact(plan["domainBitWorkers"],"domainBitWorkers"),plan["selectedPairsPerEpoch"])

    def _addresses(self,bit_count,count):
        return np.array([((known_word(self.config["seed"]^0xC0FFEE,index*2+1)<<32)|known_word(self.config["seed"]^0xC0FFEE,index*2+2))%bit_count for index in range(count)],dtype=np.uint64)

    def _pressure(self):
        if memory_status()["availableBytes"]<self.plan["memory"]["reserveBytes"]:raise PressurePause("Free physical RAM fell below the reserve floor; further epoch work is paused.")

    def snapshot(self):
        with self.lock:
            realized=self.init_bytes*8 if self.ram else 0;virtual=exact(self.plan["domainBitWorkers"],"domainBitWorkers")*self.epochs;wall=self.wall_seconds+(time.perf_counter()-self._active_wall_start if self._active_wall_start is not None else 0)
            timing=dict(self._timing);timing["meanMs"]=timing["sumMs"]/timing["count"] if timing["count"] else None
            timing["scope"]="Selected RAM read, CPU/GPU sample calculation/readback and descriptor commit latency; excludes inter-epoch waits and progress publication."
            cadence=dict(self._cadence);cadence["meanMs"]=cadence["sumMs"]/cadence["count"] if cadence["count"] else None;cadence["scope"]="Observed issue-to-issue intervals within each run, including sample completion, cadence wait and intervening progress bookkeeping. First issue has no prior interval."
            return {"schema":"XOR-PAIR-XPU/SESSION/1","status":self.status,"phase":self.phase,"mode":self.plan["mode"],"plan":self.plan,"domainBitWorkers":self.plan["domainBitWorkers"],"materializedStoredWorkerBits":str(realized),"currentAllocatedBytes":self.plan["totalReservoirBytes"] if self.ram else 0,"initializedBytes":str(self.init_bytes),"initializationMs":self.initialization_ms,"acknowledgedEpochs":str(self.epochs),"currentEpoch":str(self.epochs),"virtualXorOperations":str(virtual),"realWallSeconds":wall,"virtualXorPerSecond":virtual/wall if wall>0 else None,"acknowledgedEpochsPerSecond":self.epochs/wall if wall>0 else None,"cpu":{key:str(value) if isinstance(value,int) else value for key,value in self.counters["cpu"].items()},"gpu":{key:str(value) if isinstance(value,int) else value for key,value in self.counters["gpu"].items()},"sampling":{"actualSelectedWordReads":str(self.sample_reads),"actualSelectedReadBytes":str(self.sample_reads*4),"queryCalls":self.query_calls,"descriptorSeedHashEvaluations":str(self.descriptor_hashes),"descriptorSeedXorExpressions":str(self.descriptor_hashes*6),"descriptorSeedShiftExpressions":str(self.descriptor_hashes*4),"descriptorSeedAndExpressions":str(self.descriptor_hashes*6),"descriptorSeedMultiplyExpressions":str(self.descriptor_hashes*2),"addressDecodeShiftExpressions":str(self.sample_reads),"addressDecodeAndExpressions":str(self.sample_reads),"baseReadAndCheckShiftExpressions":str(self.sample_reads*2),"baseReadAndCheckAndExpressions":str(self.sample_reads*2),"scope":"Fresh selected RAM word reads only. Mask hash/decode and representation XORs are source expressions, not hardware instruction counts. Setup address generation, probe, control flow and serialization are separate overhead."},"epochTiming":timing,"epochCadence":cadence,"epochRAMWriteBytes":"0","initializationRAMWriteBytes":str(self.init_bytes),"recordedSeed":self.config["seed"],"currentDescriptor":{"epoch":str(self.epochs),"maskSeed":self.descriptor_seed},"error":self.error,"accounting":"Virtual XOR=N stored bit addresses times individually acknowledged epochs. Actual sampled primary XORs are separate CPU/GPU calculations. No virtual clock multiplier, full-RAM epoch rewrite, gray/averaging, or floating-point FLOP credit."}

    def _notify(self):
        if self.on_progress:self.on_progress(self.snapshot())

    def _initialize(self):
        if self.ram is None:
            self._pressure();self.ram=Reservoir(self.plan["totalReservoirBytes"]);self.gpu=XORGPU(4096,self.plan["gpuDevice"]);self.fill_pool=ThreadPoolExecutor(max_workers=self.plan["cpuWorkers"],thread_name_prefix="xor-base-fill");self.sample_pool=ThreadPoolExecutor(max_workers=2,thread_name_prefix="xor-selected")
            self._ram_words=np.ctypeslib.as_array((C.c_uint32*self.plan["wordCount"]).from_address(self.ram.pointer));self.phase="initialization";self._notify()
        if self.init_bytes==self.plan["totalReservoirBytes"]:return True
        started=time.perf_counter()
        def fill(index):
            total=self.plan["totalReservoirBytes"];start=index*total//self.plan["cpuWorkers"];end=(index+1)*total//self.plan["cpuWorkers"];cursor=self.init_cursors.get(index,start)
            while cursor<end:
                if self.cancel_event.is_set():return
                self._pressure();take=min(CHUNK,end-cursor);self.ram.fill(cursor,take,self.plan["initialPatternByte"])
                with self.lock:cursor+=take;self.init_cursors[index]=cursor;self.init_bytes+=take
                self._notify()
        futures=[self.fill_pool.submit(fill,index) for index in range(self.plan["cpuWorkers"])];failure=None
        for future in futures:
            try:future.result()
            except Exception as error:self.cancel_event.set();failure=failure or error
        self.initialization_ms+=(time.perf_counter()-started)*1000
        if failure:raise failure
        return self.init_bytes==self.plan["totalReservoirBytes"]

    def _read(self,ids):
        words=ids>>np.uint64(5);offsets=(ids&np.uint64(31)).astype(np.uint32)
        actual_words=self._ram_words[words.astype(np.intp)]
        base=(actual_words>>offsets)&np.uint32(1)
        self.sample_reads+=len(ids)
        # Independent known initialization checks inspect selected bits only.
        expected=(np.uint32(self.plan["initialPatternByte"]*0x01010101)>>offsets)&np.uint32(1)
        if not np.array_equal(base,expected):raise RuntimeError("Selected actual RAM base bits disagree with the initialized pattern.")
        return np.ascontiguousarray(base),np.ascontiguousarray(words),np.ascontiguousarray(offsets)

    def _compute(self,ids,transition_epoch,query=False):
        if not 1<=transition_epoch<=2**64-1:raise ValueError("Epoch must fit positive uint64.")
        base,words,offsets=self._read(ids);count=len(ids);gpu_count=max(1,min(count-1,int(count*self.config["gpu_fraction"])));cpu_count=count-gpu_count
        current_seed=epoch_seed(self.config["seed"],transition_epoch-1);next_seed=epoch_seed(self.config["seed"],transition_epoch);self.descriptor_hashes+=2
        cpu_future=self.sample_pool.submit(cpu_pairs,base[:cpu_count],words[:cpu_count],offsets[:cpu_count],current_seed,next_seed)
        gpu_future=self.sample_pool.submit(self.gpu.compute,base[cpu_count:],words[cpu_count:],offsets[cpu_count:],current_seed,next_seed)
        result={};failure=None
        for backend,number,future in (("cpu",cpu_count,cpu_future),("gpu",gpu_count,gpu_future)):
            try:
                result[backend]=future.result()
                with self.lock:
                    counters=self.counters[backend];counters["sampledPrimaryXorOperations"]+=number;counters["auxiliaryRepresentationXorOperations"]+=number*3;counters["maskHashEvaluations"]+=number*2;counters["maskHashXorExpressions"]+=number*10;counters["maskHashShiftExpressions"]+=number*8;counters["maskHashMultiplyExpressions"]+=number*4;counters["maskDecodeShiftExpressions"]+=number*2;counters["maskDecodeAndExpressions"]+=number*2;counters["sampledPairs"]+=number;counters["querySampledPairs" if query else "epochSampledPairs"]+=number
                    if backend=="gpu":counters["kernelDispatches"]+=1;counters["kernelEventMs"]+=result[backend][1]
            except Exception as error:failure=failure or error
        if failure:raise failure
        traces=np.vstack((result["cpu"],result["gpu"][0]))
        if not bool(np.all(traces[:,7])) or not bool(np.all(traces[:,:7]<=1)):raise RuntimeError("Actual selected CPU/GPU XOR result failed binary identity validation.")
        return traces,cpu_count,next_seed

    def _entries(self,ids,traces,cpu_count,transition_epoch):
        return [{"bitId":str(int(ids[index])),"epoch":str(transition_epoch),"backend":"cpu" if index<cpu_count else "gpu","baseBit":int(trace[0]),"inputA":int(trace[1]),"inputB":int(trace[2]),"result":int(trace[3]),"expected":int(trace[4]),"currentMask":int(trace[5]),"nextMask":int(trace[6]),"matches":bool(trace[7]),"inputAColor":"white" if trace[1] else "black","inputBColor":"white" if trace[2] else "black","resultColor":"white" if trace[3] else "black"} for index,trace in enumerate(traces)]

    def execute(self,duration_seconds=None,epoch_interval_ms=None,epoch_count=None):
        duration=self.config["duration_seconds"] if duration_seconds is None else duration_seconds;interval=self.config["epoch_interval_ms"] if epoch_interval_ms is None else epoch_interval_ms
        if isinstance(duration,bool) or not isinstance(duration,(int,float)) or not math.isfinite(duration) or not .01<=duration<=300:raise ValueError("duration_seconds must be.01..300.")
        if isinstance(interval,bool) or not isinstance(interval,(int,float)) or not math.isfinite(interval) or not 0<=interval<=1000:raise ValueError("epoch_interval_ms must be0..1000;0 is uncapped measured execution.")
        if epoch_count is not None:
            epoch_count=exact(epoch_count,"epoch_count")
            if not 1<=epoch_count<=1_000_000:raise ValueError("epoch_count must be1..1,000,000.")
        with self.lock:
            if self.running:raise RuntimeError("Already running.")
            if self.phase in ("released","error"):raise RuntimeError("Create a fresh session after release/error.")
            self.running=True;self.status="RUNNING";self.error=None
        loop_start=None;epoch_start=self.epochs;latest=None;next_due=None;next_pressure=0.;next_notify=0.;durations=[];cadences=[];previous_issue=None;run_wall_start=self.wall_seconds
        try:
            if self._initialize() and not self.cancel_event.is_set():
                self.phase="epochs";self._notify();loop_start=time.perf_counter();self._active_wall_start=loop_start;next_due=loop_start
                while not self.cancel_event.is_set():
                    now=time.perf_counter()
                    if epoch_count is None and now-loop_start>=duration:break
                    if epoch_count is not None and self.epochs-epoch_start>=epoch_count:break
                    if interval and now<next_due:
                        self.cancel_event.wait(max(0,next_due-now));continue
                    if now>=next_pressure:self._pressure();next_pressure=now+.1
                    epoch_started=time.perf_counter();transition=self.epochs+1
                    traces,cpu_count,next_seed=self._compute(self._ids,transition)
                    # The descriptor is individually committed only after both real backends finish.
                    with self.lock:self.epochs=transition;self.descriptor_seed=next_seed
                    if previous_issue is not None:cadences.append((epoch_started-previous_issue)*1000)
                    previous_issue=epoch_started
                    elapsed_ms=(time.perf_counter()-epoch_started)*1000;durations.append(elapsed_ms);latest=(traces,cpu_count,transition)
                    if interval:next_due=epoch_started+interval/1000
                    now=time.perf_counter()
                    if now>=next_notify:
                        self._notify();next_notify=now+.25
                self.status="PAUSED" if self.cancel_event.is_set() else "COMPLETE"
            else:self.status="PAUSED"
        except PressurePause as error:self.status="PAUSED";self.error=str(error)
        except Exception as error:self.status="ERROR";self.error=str(error);self.phase="error"
        finally:
            if loop_start is not None:self.wall_seconds+=time.perf_counter()-loop_start
            self._active_wall_start=None
            if latest:self._recent=self._entries(self._ids,*latest)
            if durations:
                self._timing["count"]+=len(durations);self._timing["sumMs"]+=sum(durations);self._timing["minMs"]=min(durations) if self._timing["minMs"] is None else min(self._timing["minMs"],min(durations));self._timing["maxMs"]=max(self._timing["maxMs"],max(durations));self._timing["below2Ms"]+=sum(value<2 for value in durations);self._timing["below10Ms"]+=sum(value<10 for value in durations);self._timing["deadlineMisses"]+=sum(value>=self.config["deadline_ms"] for value in durations)
            if cadences:
                self._cadence["count"]+=len(cadences);self._cadence["sumMs"]+=sum(cadences);self._cadence["minMs"]=min(cadences) if self._cadence["minMs"] is None else min(self._cadence["minMs"],min(cadences));self._cadence["maxMs"]=max(self._cadence["maxMs"],max(cadences));self._cadence["below2Ms"]+=sum(value<2 for value in cadences);self._cadence["below10Ms"]+=sum(value<10 for value in cadences);self._cadence["deadlineMisses"]+=sum(value>=self.config["deadline_ms"] for value in cadences)
            with self.lock:self.running=False
            if self.phase!="error":self.phase="idle" if self.init_bytes==self.plan["totalReservoirBytes"] else "initialization"
            run_wall=self.wall_seconds-run_wall_start;run_epochs=self.epochs-epoch_start
            self.runs.append({"completedEpochs":str(run_epochs),"virtualXorOperations":str(run_epochs*exact(self.plan["domainBitWorkers"],"domainBitWorkers")),"realWallSeconds":run_wall,"acknowledgedEpochsPerSecond":run_epochs/run_wall if run_wall else None,"durationSeconds":duration,"epochIntervalMs":interval,"status":self.status,"sampledPairsPerEpoch":self.plan["selectedPairsPerEpoch"],"scope":"Elapsed pipeline wall time includes selected RAM reads, CPU/GPU compute/readback, cadence waits and progress callbacks. Initialization and later viewer/query formatting are separate."})
            self._notify()
        return self.report()

    def sample(self,bit_ids=None,count=None):
        with self.lock:
            if self.running or not self.ram or self.init_bytes!=self.plan["totalReservoirBytes"]:raise RuntimeError("Pause and initialize live RAM before fresh selected queries.")
        if bit_ids is None:
            count=1060 if count is None else exact(count,"count")
            if not 2<=count<=4096:raise ValueError("Query count must be2..4096.")
            ids=self._addresses(exact(self.plan["domainBitWorkers"],"domainBitWorkers"),count)
        else:
            if not isinstance(bit_ids,list) or not 2<=len(bit_ids)<=4096:raise ValueError("Query IDs must be a list of2..4096 live bit addresses.")
            values=[exact(value,"bitId") for value in bit_ids]
            if any(value>=exact(self.plan["domainBitWorkers"],"domainBitWorkers") for value in values):raise ValueError("Selected bit address is outside live RAM.")
            ids=np.array(values,dtype=np.uint64)
        transition=self.epochs or 1;traces,cpu_count,_=self._compute(ids,transition,True);self.query_calls+=1;self._recent=self._entries(ids,traces,cpu_count,transition)
        return {"entries":self._recent,"allSamplesMatch":True,"sampleEpoch":str(transition),"committed":self.epochs>0,"virtualXorCredit":"0","scope":"Actual selected CPU/GPU XOR queries. Query calculations do not advance epochs or earn domain-wide virtual credit."}

    def report(self):return {"schema":"XOR-PAIR-XPU/EXECUTION/1","status":self.status,"snapshot":self.snapshot(),"runs":self.runs,"recentSamples":self._recent,"memoryTelemetry":memory_status(),"scope":"Measured real wall time for individually completed shared descriptor epochs and actual selected CPU/GPU XORs. Physical RAM base bits are initialized once, with zero full-reservoir writes per epoch."}

    def release(self):
        if self.running:raise RuntimeError("Pause and await current selected GPU completion before release.")
        if self.sample_pool:self.sample_pool.shutdown(wait=True);self.sample_pool=None
        if self.fill_pool:self.fill_pool.shutdown(wait=True);self.fill_pool=None
        if self.gpu:self.gpu.release();self.gpu=None
        self._ram_words=None
        if self.ram:self.ram.release();self.ram=None
        self.phase="released";self.status="RELEASED";return self.snapshot()

def main():
    parser=argparse.ArgumentParser(description="Strict black/white XOR shared epochs; genuine CPU/GPU selected calculations.")
    parser.add_argument("--probe",action="store_true");parser.add_argument("--auto-ram",action="store_true");parser.add_argument("--budget-mib",type=int);parser.add_argument("--reserve-gib",type=float,default=2);parser.add_argument("--cpu-workers",type=int,default=4);parser.add_argument("--gpu-fraction",type=float,default=.5);parser.add_argument("--seed",type=int);parser.add_argument("--sample-count",type=int,default=64);parser.add_argument("--duration-seconds",type=float,default=2);parser.add_argument("--epoch-interval-ms",type=float,default=0);parser.add_argument("--epoch-count",type=int);parser.add_argument("--report",default="xor-execution-report.json");args=parser.parse_args()
    if args.probe:print(json.dumps(probe(),indent=2));return
    config={name:getattr(args,name) for name in ("reserve_gib","cpu_workers","gpu_fraction","sample_count","duration_seconds","epoch_interval_ms")};config["auto_ram"]=args.auto_ram or args.budget_mib is None
    if args.seed is not None:config["seed"]=args.seed
    if args.budget_mib is not None:config["budget_mib"]=args.budget_mib
    session=Session(plan_budget(config));print(json.dumps({"plannedBytes":session.plan["totalReservoirBytes"],"domainBitWorkers":session.plan["domainBitWorkers"],"sampledPairsPerEpoch":session.plan["selectedPairsPerEpoch"]}),flush=True)
    try:
        session.execute(epoch_count=args.epoch_count)
        if session.status=="COMPLETE":session.sample()
        report=session.report();report["sourceSha256"]={name:hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in ("shared_epoch_xpu.py","xor_opencl_gpu.py","bulk_wave_xpu.py","full_traversal_reference.py","opencl_gpu.py")};report["releaseSnapshot"]=session.release();Path(args.report).write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8");print(json.dumps({"status":report["status"],"report":str(Path(args.report).resolve()),"epochs":report["snapshot"]["acknowledgedEpochs"],"virtualXorPerSecond":report["snapshot"]["virtualXorPerSecond"],"released":True}),flush=True)
        if report["status"]!="COMPLETE":raise RuntimeError("Shared-epoch run did not complete: "+report["status"])
    finally:
        if not session.running and session.phase!="released":session.release()

if __name__=="__main__":main()
