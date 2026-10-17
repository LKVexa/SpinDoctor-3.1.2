"""Actual CPU NumPy + OpenCL GPU packed binary lanes in one shared-RAM budget.

The GPU has disjoint word ownership. It is never emulated by a CPU fallback.
Read depth is a count of real, barrier-separated full recurrence rounds.
"""
from __future__ import annotations
import argparse, ctypes as C, hashlib, json, math, os, threading, time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import numpy as np
from opencl_gpu import OpenCL, GPUContext, OpenCLError

MASK=0xFFFFFFFF
CHUNK_WORDS=1_048_576
BANK_BYTES=64*1024*1024
GIB=1024**3

def memory_status():
    if os.name=="nt":
        class Status(C.Structure):
            _fields_=[("length",C.c_ulong),("load",C.c_ulong),("total",C.c_ulonglong),("available",C.c_ulonglong),("totalPage",C.c_ulonglong),("availablePage",C.c_ulonglong),("totalVirtual",C.c_ulonglong),("availableVirtual",C.c_ulonglong),("availableExtended",C.c_ulonglong)]
        status=Status();status.length=C.sizeof(status)
        if not C.windll.kernel32.GlobalMemoryStatusEx(C.byref(status)):raise OSError("Cannot obtain actual free physical RAM.")
        return {"physicalTotalBytes":status.total,"availableBytes":status.available,"loadPercent":status.load}
    entries={}
    for line in Path("/proc/meminfo").read_text().splitlines():
        key,value,*_=line.split();entries[key.rstrip(":")]=int(value)*1024
    return {"physicalTotalBytes":entries["MemTotal"],"availableBytes":entries["MemAvailable"]}

def exact(value,name):
    if isinstance(value,bool):raise ValueError(name+" must be an exact nonnegative integer.")
    if isinstance(value,int) and value>=0:return value
    if isinstance(value,str) and len(value)<=20 and (value=="0" or value.isdecimal() and value[0]!="0" and value.isascii()):return int(value)
    raise ValueError(name+" must be an exact nonnegative integer.")

def known_word(seed,index):
    x=(seed^(index&MASK)^((index>>32)&MASK))&MASK
    x=((x^(x>>16))*0x7feb352d)&MASK
    x=((x^(x>>15))*0x846ca68b)&MASK
    return (x^(x>>16))&MASK

def public_device(device):return {key:value for key,value in device.items() if key!="handle"}

def _gpu_test(cl,device):
    gpu=GPUContext(cl,device)
    try:
        state=gpu.allocate(4);product=gpu.allocate(4)
        gpu.dispatch("initialize",[C.c_void_p(state),C.c_uint(7),C.c_ulonglong(0),C.c_uint(0),C.c_uint(1),C.c_ulonglong(0),C.c_uint(MASK)],1)
        gpu.dispatch("product",[C.c_void_p(state),C.c_void_p(product),C.c_uint(MASK),C.c_ulonglong(0),C.c_uint(0),C.c_uint(1),C.c_ulonglong(0),C.c_uint(MASK)],1)
        gpu.dispatch("answer",[C.c_void_p(product),C.c_void_p(state),C.c_uint(MASK),C.c_ulonglong(0),C.c_uint(0),C.c_uint(1),C.c_ulonglong(0),C.c_uint(MASK)],1)
        actual=gpu.read_word(state,0);expected=known_word(7,0)^MASK
        if actual!=expected:raise OpenCLError("Actual GPU probe result differs from independent expected shape.")
        return {"status":"PASS","actualStateWord":actual,"expectedStateWord":expected,"initializedWords":1,"productWords":1,"answerWords":1,"creditedExecutionOperations":"0","scope":"Separate tiny GPU compile/dispatch/readback probe, excluded from subsequent run counters."}
    finally:gpu.release()

def probe():
    result={"schema":"CPU-GPU-XPU/PROBE/1","cpu":{"logicalProcessors":os.cpu_count() or 1,"numpyVersion":np.__version__},"memory":memory_status(),"available":False,"gpuDevices":[]}
    try:
        cl=OpenCL();devices=cl.devices();result["gpuDevices"]=[public_device(device) for device in devices]
        supported=[device for device in devices if device["hostUnifiedMemory"]]
        if not supported:raise OpenCLError("A real integrated GPU with shared host memory is required; no CPU fallback is used.")
        result["gpuDevice"]=public_device(supported[0]);result["actualGpuProbe"]=_gpu_test(cl,supported[0]);result["available"]=True
    except Exception as error:result["error"]=str(error)
    return result

def plan_budget(config=None):
    config=dict(config or {});allowed={"auto_ram","reserve_gib","budget_mib","cpu_workers","gpu_fraction","read_depth","seed","a","b","bit_count","bank_bytes"}
    if set(config)-allowed:raise ValueError("Unknown planner options: "+", ".join(sorted(set(config)-allowed)))
    observed=probe()
    if not observed["available"]:raise OpenCLError(observed.get("error","Actual GPU probe failed."))
    workers=exact(config.get("cpu_workers",4),"cpu_workers")
    if workers==0:workers=os.cpu_count() or 1
    if workers<1 or workers>64:raise ValueError("cpu_workers must be1 through64, or0 for all detected logical processors.")
    depth=exact(config.get("read_depth",1),"read_depth")
    if depth<1 or depth>16:raise ValueError("read_depth must be1 through16 actual full rounds.")
    seed=exact(config.get("seed",1),"seed")
    if seed>MASK:raise ValueError("seed must fit uint32.")
    a=exact(config.get("a",1),"a");b=exact(config.get("b",1),"b")
    if a not in (0,1) or b not in (0,1):raise ValueError("a and b must be binary.")
    fraction=config.get("gpu_fraction",.5)
    if isinstance(fraction,bool) or not isinstance(fraction,(int,float)) or not math.isfinite(fraction) or fraction<=0 or fraction>=1:raise ValueError("gpu_fraction must be strictly between0 and1.")
    reserve=config.get("reserve_gib",2)
    if isinstance(reserve,bool) or not isinstance(reserve,(int,float)) or not math.isfinite(reserve) or reserve<2 or reserve>1024:raise ValueError("reserve_gib must be finite and at least2GiB.")
    reserve_bytes=math.ceil(reserve*GIB);overhead=512*1024**2+workers*16*1024**2
    available=observed["memory"]["availableBytes"];usable=available-reserve_bytes-overhead
    if usable<16:raise MemoryError("Insufficient free physical RAM after reserve and bounded CPU scratch overhead.")
    auto=config.get("auto_ram","budget_mib" not in config)
    if not isinstance(auto,bool):raise ValueError("auto_ram must be Boolean.")
    budget=usable if auto else exact(config.get("budget_mib",64),"budget_mib")*1024**2
    if budget<16 or budget>usable:raise MemoryError("Requested payload exceeds free physical RAM minus reserve/overhead.")
    requested_bits=exact(config["bit_count"],"bit_count") if "bit_count" in config else (budget//8)*32
    if requested_bits<33 or requested_bits>2**63-1:raise ValueError("At least33 bit lanes are needed for disjoint real CPU/GPU word ownership.")
    words=(requested_bits+31)//32;payload=words*8
    if payload>budget:raise MemoryError("Requested bit domain exceeds payload budget.")
    gpu_words=max(1,min(words-1,int(words*fraction)));gpu_cap=int(observed["gpuDevice"]["globalMemoryBytes"]*.75)//8
    if gpu_words>gpu_cap:gpu_words=gpu_cap
    if gpu_words<1:raise MemoryError("GPU shared allocation cap cannot support a word pair.")
    cpu_words=words-gpu_words
    bank_bytes=exact(config.get("bank_bytes",BANK_BYTES),"bank_bytes")
    maximum=min(BANK_BYTES,observed["gpuDevice"]["maxAllocationBytes"])
    if bank_bytes<4 or bank_bytes>maximum or bank_bytes%4:raise ValueError("bank_bytes must be a multiple of4 within the GPU/bounded64MiB limit.")
    bank_words=bank_bytes//4;banks=[]
    for worker in range(workers):
        start=worker*cpu_words//workers;end=(worker+1)*cpu_words//workers
        while start<end:
            take=min(bank_words,end-start);banks.append({"backend":"cpu","workerIndex":worker,"baseWord":start,"wordCount":take,"bytesPerBuffer":take*4});start+=take
    start=cpu_words
    while start<words:
        take=min(bank_words,words-start);banks.append({"backend":"gpu","workerIndex":0,"baseWord":start,"wordCount":take,"bytesPerBuffer":take*4});start+=take
    normalized={"auto_ram":auto,"reserve_gib":reserve,"budget_mib":budget//1024**2,"cpu_workers":workers,"gpu_fraction":fraction,"read_depth":depth,"seed":seed,"a":a,"b":b}
    return {"schema":"CPU-GPU-XPU/PLAN/1","config":normalized,"bitCount":str(requested_bits),"wordCount":words,"cpuWordCount":cpu_words,"gpuWordCount":gpu_words,"cpuWorkers":workers,"readDepth":depth,"bankBytes":bank_bytes,"chunkWords":CHUNK_WORDS,"tailActiveBits":requested_bits%32 or 32,"paddingBits":words*32-requested_bits,"banks":banks,"gpuDevice":observed["gpuDevice"],"probe":observed,"memory":{**observed["memory"],"reserveBytes":reserve_bytes,"overheadBytes":overhead,"payloadBudgetBytes":budget,"payloadBytes":payload,"cpuPayloadBytes":cpu_words*8,"gpuPayloadBytes":gpu_words*8,"sharedPhysicalBudgetCountedOnce":True,"gpuGlobalCapBytes":gpu_cap*8,"scope":"Both CPU arrays and integrated GPU host-backed buffers consume the same free physical RAM budget. Driver overhead/scratch and a2GiB-or-larger reserve are excluded from payload."}}

class PressurePause(RuntimeError):pass

class Session:
    def __init__(self,plan,on_progress=None,cancel_event=None):
        self.plan=json.loads(json.dumps(plan));self.on_progress=on_progress;self.cancel_event=cancel_event or threading.Event();self.lock=threading.RLock();self.running=False;self.phase="allocation";self.status="READY";self.completed_rounds=0;self.error=None;self.banks=[];self.context=None;self.allocated_bytes=0;self.elapsed_ms=0.;self._samples=[];self.sample_reads=0;self.sample_hashes=0;self.sample_calls=0;self._stage_samples=[]
        if self.plan.get("schema")!="CPU-GPU-XPU/PLAN/1":raise ValueError("Use an actual plan_budget output.")
        self.bit_count=exact(self.plan["bitCount"],"bitCount");self.tail_mask=(1<<self.plan["tailActiveBits"])-1;self.config=self.plan["config"]
        self.counters={backend:{"initializedWords":0,"productWords":0,"answerWords":0,"materializedBits":0,"productBitOperations":0,"answerBitOperations":0,"numpyCalls":0,"kernelDispatches":0,"kernelEventMs":0.,"allocationBytes":0} for backend in ("cpu","gpu")}
    def _pressure(self):
        if memory_status()["availableBytes"]<self.plan["memory"]["reserveBytes"]:raise PressurePause("Physical free RAM fell below the configured reserve floor.")
    def _lanes(self,base,count):return max(0,min(self.bit_count,(base+count)*32)-min(self.bit_count,base*32))
    def snapshot(self):
        with self.lock:
            cpu=dict(self.counters["cpu"]);gpu=dict(self.counters["gpu"]);materialized=cpu["materializedBits"]+gpu["materializedBits"];operations=cpu["productBitOperations"]+cpu["answerBitOperations"]+gpu["productBitOperations"]+gpu["answerBitOperations"]
            return {"schema":"CPU-GPU-XPU/SESSION/1","status":self.status,"phase":self.phase,"plan":self.plan,"cpu":{key:str(value) if isinstance(value,int) else value for key,value in cpu.items()},"gpu":{key:str(value) if isinstance(value,int) else value for key,value in gpu.items()},"completedRounds":str(self.completed_rounds),"materializedStoredWorkerBits":str(materialized),"historicalInitializedBits":str(cpu["initializedWords"]*32+gpu["initializedWords"]*32-self.plan["paddingBits"] if cpu["initializedWords"]+gpu["initializedWords"]==self.plan["wordCount"] else materialized),"currentAllocatedBytes":self.allocated_bytes,"completedBitOperations":str(operations),"completedSteps":str(cpu["answerBitOperations"]+gpu["answerBitOperations"]),"packedAndExpressions":str(cpu["productWords"]+gpu["productWords"]),"packedXorExpressions":str(cpu["answerWords"]+gpu["answerWords"]),"modeledPhaseReadBytes":str((cpu["productWords"]+cpu["answerWords"]+gpu["productWords"]+gpu["answerWords"])*4),"modeledPhaseWriteBytes":str((cpu["productWords"]+cpu["answerWords"]+gpu["productWords"]+gpu["answerWords"])*4),"elapsedConcurrentHostMs":self.elapsed_ms,"sampling":{"calls":self.sample_calls,"actualWordReadBytes":self.sample_reads,"knownShapeEvaluations":self.sample_hashes,"creditedBitOperations":"0","scope":"Actual selected CPU/GPU RAM word readbacks, with selected bit/nibble observations; no full-memory proof."},"bankProgress":[{"backend":bank["backend"],"baseWord":bank["baseWord"],"wordCount":bank["wordCount"],"initializedWords":bank["initCursor"],"productCursor":bank["productCursor"],"answerCursor":bank["answerCursor"]} for bank in self.banks],"error":self.error,"accounting":"Separate actual CPU NumPy and GPU completion-event acknowledgments. AND/XOR lane work, packed expressions, samples and concurrent wall time have separate units; no FLOP/s claim."}
    def _notify(self):
        if self.on_progress:self.on_progress(self.snapshot())
    def _allocate(self):
        if self.context is None:
            cl=OpenCL();devices=cl.devices();device=next((device for device in devices if device["name"]==self.plan["gpuDevice"]["name"] and device["hostUnifiedMemory"]),None)
            if device is None:raise OpenCLError("The planned real GPU is no longer available.")
            self.context=GPUContext(cl,device)
        for spec in self.plan["banks"][len(self.banks):]:
            if self.cancel_event.is_set():return False
            self._pressure();bank=dict(spec);bank.update(initCursor=0,productCursor=0,answerCursor=0)
            if spec["backend"]=="cpu":bank["state"]=np.empty(spec["wordCount"],dtype=np.uint32);bank["product"]=np.empty(spec["wordCount"],dtype=np.uint32)
            else:bank["state"]=self.context.allocate(spec["bytesPerBuffer"]);bank["product"]=self.context.allocate(spec["bytesPerBuffer"])
            with self.lock:self.banks.append(bank);self.allocated_bytes+=spec["bytesPerBuffer"]*2;self.counters[spec["backend"]]["allocationBytes"]+=spec["bytesPerBuffer"]*2
            self._notify()
        return True
    def _ack(self,bank,stage,count,base,kernel_ms=0.,numpy_calls=0):
        with self.lock:
            backend=bank["backend"];stats=self.counters[backend];stats[stage+"Words"]+=count
            if stage=="initialized":stats["materializedBits"]+=self._lanes(base,count)
            else:stats[stage+"BitOperations"]+=self._lanes(base,count)
            stats["numpyCalls"]+=numpy_calls
            if backend=="gpu":stats["kernelDispatches"]+=1;stats["kernelEventMs"]+=kernel_ms
        self._notify()
    def _cpu_bank(self,bank,stage):
        cursor_key={"initialized":"initCursor","product":"productCursor","answer":"answerCursor"}[stage]
        while bank[cursor_key]<bank["wordCount"]:
            if self.cancel_event.is_set():return
            self._pressure();offset=bank[cursor_key];base=bank["baseWord"]+offset;count=min(CHUNK_WORDS,bank["wordCount"]-offset,(1<<32)-(base&MASK));state=bank["state"][offset:offset+count];scratch=bank["product"][offset:offset+count]
            if stage=="initialized":
                state[:]=np.arange(base&MASK,(base&MASK)+count,dtype=np.uint32)
                np.bitwise_xor(state,np.uint32(self.config["seed"]^(base>>32)),out=state)
                np.right_shift(state,16,out=scratch);np.bitwise_xor(state,scratch,out=state);np.multiply(state,np.uint32(0x7feb352d),out=state)
                np.right_shift(state,15,out=scratch);np.bitwise_xor(state,scratch,out=state);np.multiply(state,np.uint32(0x846ca68b),out=state)
                np.right_shift(state,16,out=scratch);np.bitwise_xor(state,scratch,out=state);calls=10
            elif stage=="product":np.bitwise_and(state,np.uint32(MASK if self.config["a"] else 0),out=scratch);calls=1
            else:np.bitwise_xor(scratch,np.uint32(MASK if self.config["b"] else 0),out=state);calls=1
            if base+count==self.plan["wordCount"] and self.plan["paddingBits"]:
                destination=scratch if stage=="product" else state;destination[-1]=np.uint32(int(destination[-1])&self.tail_mask)
            with self.lock:bank[cursor_key]+=count
            self._ack(bank,stage,count,base,numpy_calls=calls)
    def _cpu_stage(self,stage):
        def task(worker):
            for bank in self.banks:
                if bank["backend"]=="cpu" and bank["workerIndex"]==worker:self._cpu_bank(bank,stage)
        with ThreadPoolExecutor(max_workers=self.plan["cpuWorkers"],thread_name_prefix="xpu-cpu") as executor:
            futures=[executor.submit(task,worker) for worker in range(self.plan["cpuWorkers"])]
            for future in futures:future.result()
    def _gpu_stage(self,stage):
        cursor_key={"initialized":"initCursor","product":"productCursor","answer":"answerCursor"}[stage];kernel="initialize" if stage=="initialized" else stage
        for bank in self.banks:
            if bank["backend"]!="gpu":continue
            while bank[cursor_key]<bank["wordCount"]:
                if self.cancel_event.is_set():return
                self._pressure();offset=bank[cursor_key];count=min(CHUNK_WORDS,bank["wordCount"]-offset);base=bank["baseWord"]
                if stage=="initialized":args=[C.c_void_p(bank["state"]),C.c_uint(self.config["seed"]),C.c_ulonglong(base),C.c_uint(offset),C.c_uint(count),C.c_ulonglong(self.plan["wordCount"]-1),C.c_uint(self.tail_mask)]
                else:
                    first=bank["state"] if stage=="product" else bank["product"];second=bank["product"] if stage=="product" else bank["state"];coefficient=self.config["a"] if stage=="product" else self.config["b"]
                    args=[C.c_void_p(first),C.c_void_p(second),C.c_uint(MASK if coefficient else 0),C.c_ulonglong(base),C.c_uint(offset),C.c_uint(count),C.c_ulonglong(self.plan["wordCount"]-1),C.c_uint(self.tail_mask)]
                measured=self.context.dispatch(kernel,args,count)
                with self.lock:bank[cursor_key]+=count
                self._ack(bank,stage,count,base+offset,kernel_ms=measured)
    def _stage(self,stage):
        # Both ownership partitions run concurrently; this is one host wall interval.
        with ThreadPoolExecutor(max_workers=2,thread_name_prefix="xpu-backend") as executor:
            cpu=executor.submit(self._cpu_stage,stage);gpu=executor.submit(self._gpu_stage,stage)
            try:cpu.result();gpu.result()
            except Exception:self.cancel_event.set();raise
        key={"initialized":"initCursor","product":"productCursor","answer":"answerCursor"}[stage]
        return all(bank[key]==bank["wordCount"] for bank in self.banks)
    def execute(self):
        with self.lock:
            if self.running:raise RuntimeError("Session is already running.")
            if self.phase=="released":raise RuntimeError("Released storage requires a fresh Session.")
            if self.phase=="complete":return self.report()
            if self.phase=="error":raise RuntimeError("Failed session requires release and a fresh Session.")
            self.running=True;self.status="RUNNING";self.error=None
        begin=time.perf_counter()
        try:
            while not self.cancel_event.is_set():
                if self.phase=="allocation":
                    if not self._allocate():break
                    self.phase="initialization"
                elif self.phase=="initialization":
                    if not self._stage("initialized"):break
                    self.phase="product"
                    self._stage_samples.append({"phase":"after-initialization","round":"0","sample":self._sample(None,internal=True)})
                elif self.phase=="product":
                    if not self._stage("product"):break
                    self.phase="answer"
                    self._stage_samples.append({"phase":"after-product","round":str(self.completed_rounds+1),"sample":self._sample(None,internal=True)})
                elif self.phase=="answer":
                    if not self._stage("answer"):break
                    self.completed_rounds+=1
                    self.phase="complete" if self.completed_rounds==self.plan["readDepth"] else "product"
                    self._stage_samples.append({"phase":"after-answer","round":str(self.completed_rounds),"sample":self._sample(None,internal=True)})
                    if self.phase=="complete":break
                    for bank in self.banks:bank["productCursor"]=0;bank["answerCursor"]=0
                self._notify()
            self.status="COMPLETE" if self.phase=="complete" else "PAUSED"
        except PressurePause as error:self.status="PAUSED";self.error=str(error)
        except Exception as error:self.status="ERROR";self.phase="error";self.error=str(error);self._free_storage()
        finally:
            with self.lock:self.running=False;self.elapsed_ms+=(time.perf_counter()-begin)*1000
            self._notify()
        return self.report()
    def _bank_for(self,word):
        for bank in self.banks:
            if bank["baseWord"]<=word<bank["baseWord"]+bank["wordCount"]:return bank
        raise ValueError("Selected address has not been allocated.")
    def _selected_ids(self):
        ranges=[];total=0
        for bank in self.banks:
            if bank["initCursor"]:
                start=bank["baseWord"]*32;length=self._lanes(bank["baseWord"],bank["initCursor"]);ranges.append((total,start,length));total+=length
        if total==0:return []
        count=min(1060,total);positions=[0] if count==1 else [i*(total-1)//(count-1) for i in range(count)];result=[]
        for position in positions:
            for begin,start,length in ranges:
                if begin<=position<begin+length:result.append(str(start+position-begin));break
        return result
    def _sample(self,bit_ids,internal=False):
        if not internal and self.running:raise RuntimeError("Pause and await the current chunks before sampling live RAM.")
        if self.phase in ("released","error"):raise RuntimeError("No live RAM storage is available.")
        bit_ids=self._selected_ids() if bit_ids is None else bit_ids
        if not isinstance(bit_ids,list) or len(bit_ids)>1060:raise ValueError("At most1060 exact bit IDs are accepted.")
        parsed=[]
        for value in bit_ids:
            address=exact(value,"bitId")
            if address>=self.bit_count:raise ValueError("Selected bit ID is outside the configured domain.")
            bank=self._bank_for(address//32);offset=address//32-bank["baseWord"]
            if offset>=bank["initCursor"]:raise ValueError("Selected bit has not been initialized.")
            parsed.append((address,bank,offset))
        entries=[]
        for address,bank,offset in parsed:
            state_round=self.completed_rounds+(1 if self.phase=="answer" and offset<bank["answerCursor"] else 0)
            product_round=self.completed_rounds+1 if self.phase=="answer" or self.phase=="product" and offset<bank["productCursor"] else self.completed_rounds
            word=address//32;bit_offset=address%32
            if bank["backend"]=="cpu":state_word=int(bank["state"][offset]);product_word=int(bank["product"][offset]) if product_round else None
            else:state_word=self.context.read_word(bank["state"],offset);product_word=self.context.read_word(bank["product"],offset) if product_round else None
            self.sample_reads+=4+(4 if product_word is not None else 0);initial=known_word(self.config["seed"],word);self.sample_hashes+=1
            def transform(round):return initial if round==0 else (MASK if self.config["b"] else 0) if self.config["a"]==0 else initial^(MASK if self.config["b"] and round%2 else 0)
            expected_state_word=transform(state_round);expected_product_word=None if product_round==0 else transform(product_round-1)&(MASK if self.config["a"] else 0)
            if word==self.plan["wordCount"]-1:expected_state_word&=self.tail_mask;expected_product_word=None if expected_product_word is None else expected_product_word&self.tail_mask
            width=min(4,32-bit_offset,self.bit_count-address);nibble=(state_word>>bit_offset)&((1<<width)-1)
            entries.append({"bitId":str(address),"wordIndex":str(word),"backend":bank["backend"],"bitOffset":bit_offset,"stateWord":state_word,"productWord":product_word,"expectedStateWord":expected_state_word,"expectedProductWord":expected_product_word,"state":(state_word>>bit_offset)&1,"product":None if product_word is None else (product_word>>bit_offset)&1,"expectedState":(expected_state_word>>bit_offset)&1,"expectedProduct":None if expected_product_word is None else (expected_product_word>>bit_offset)&1,"stateMatches":state_word==expected_state_word,"productMatches":None if product_word is None else product_word==expected_product_word,"nibble":nibble,"nibbleWidth":width,"digit":nibble%10})
        self.sample_calls+=1;result={"entries":entries,"allSamplesMatch":all(entry["stateMatches"] and entry["productMatches"] is not False for entry in entries),"sampledWords":str(len(entries)),"scope":"Selected actual CPU/GPU RAM word readbacks only, not a certificate of all stored memory and not credited recurrence operations."};self._samples=entries
        if not result["allSamplesMatch"]:raise RuntimeError("Known-shape actual RAM sample check failed.")
        return result
    def sample(self,bit_ids=None):
        with self.lock:return self._sample(bit_ids)
    def report(self):
        return {"schema":"CPU-GPU-XPU/EXECUTION/1","status":self.status,"snapshot":self.snapshot(),"stageSamples":self._stage_samples,"recentSamples":self._samples,"memoryTelemetry":memory_status(),"rateScope":"completedBitOperations / active concurrent host wall time, including allocation/initialization/sampling. Summed GPU event time is diagnostic and is not the combined rate denominator.","scope":"Actual disjoint CPU NumPy and OpenCL integrated GPU word passes. Shared RAM counted once; packed AND/XOR binary lane work is not floating-point FLOPS. Selected readbacks do not prove every stored bit."}
    def _free_storage(self):
        self.banks=[]
        if self.context:self.context.release();self.context=None
        self.allocated_bytes=0
        for stats in self.counters.values():stats["materializedBits"]=0;stats["allocationBytes"]=0
    def release(self):
        with self.lock:
            if self.running:raise RuntimeError("Pause and await current actual chunks before releasing.")
            self._free_storage();self.phase="released";self.status="RELEASED";return self.snapshot()

def main():
    parser=argparse.ArgumentParser(description="Actual shared-RAM CPU NumPy + integrated OpenCL GPU bit-lane execution.")
    parser.add_argument("--probe",action="store_true");parser.add_argument("--auto-ram",action="store_true");parser.add_argument("--budget-mib",type=int);parser.add_argument("--reserve-gib",type=float,default=2);parser.add_argument("--cpu-workers",type=int,default=4);parser.add_argument("--gpu-fraction",type=float,default=.5);parser.add_argument("--read-depth",type=int,default=1);parser.add_argument("--seed",type=int,default=1);parser.add_argument("--a",type=int,default=1);parser.add_argument("--b",type=int,default=1);parser.add_argument("--report",default="cpugpu-execution-report.json");arguments=parser.parse_args()
    if arguments.probe:print(json.dumps(probe(),indent=2));return
    config={"auto_ram":arguments.auto_ram or arguments.budget_mib is None,"reserve_gib":arguments.reserve_gib,"cpu_workers":arguments.cpu_workers,"gpu_fraction":arguments.gpu_fraction,"read_depth":arguments.read_depth,"seed":arguments.seed,"a":arguments.a,"b":arguments.b}
    if arguments.budget_mib is not None:config["budget_mib"]=arguments.budget_mib
    plan=plan_budget(config);last=[0.,""]
    def progress(snapshot):
        now=time.monotonic()
        if now-last[0]>=2 or snapshot["phase"]!=last[1] or snapshot["status"]!="RUNNING":
            print(json.dumps({"phase":snapshot["phase"],"status":snapshot["status"],"allocatedBytes":snapshot["currentAllocatedBytes"],"materializedBits":snapshot["materializedStoredWorkerBits"],"actualBitOperations":snapshot["completedBitOperations"],"cpuProductWords":snapshot["cpu"]["productWords"],"gpuProductWords":snapshot["gpu"]["productWords"]}),flush=True);last[:]=[now,snapshot["phase"]]
    session=Session(plan,progress)
    try:
        report=session.execute();report["sourceSha256"]={name:hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in ("cpu_gpu_xpu.py","opencl_gpu.py")};report["releaseSnapshot"]=session.release();Path(arguments.report).write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
        if report["status"]!="COMPLETE":raise RuntimeError("Full planned execution did not complete: "+report["status"])
    finally:
        if not session.running and session.phase!="released":session.release()

if __name__=="__main__":main()
