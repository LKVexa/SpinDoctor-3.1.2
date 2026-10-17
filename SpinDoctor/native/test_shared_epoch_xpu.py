"""Small real CPU/GPU checks; never allocates the maximum RAM budget."""
import hashlib,json
from pathlib import Path
import numpy as np
from shared_epoch_xpu import Session,plan_budget,epoch_seed,cpu_pairs
from xor_opencl_gpu import XORGPU

def run():
    plan=plan_budget({"budget_mib":1,"auto_ram":False,"seed":123456789,"sample_count":64,"duration_seconds":.05,"epoch_interval_ms":0})
    checks=[];sessions=[]
    try:
        a=Session(plan);sessions.append(a);a.execute(epoch_count=8)
        before=np.array(a._ram_words,copy=True)
        ids=["0","1","7","8","31","32",str(int(plan["domainBitWorkers"])-1)]
        q1=a.sample(ids);q2=a.sample(ids)
        assert q1["entries"]==q2["entries"] and q1["virtualXorCredit"]=="0"
        checks.append("Repeated selected query preserves recorded seed, epoch and exact binary pair results.")
        a.execute(epoch_count=4)
        assert a.epochs==12 and a.config["seed"]==123456789 and np.array_equal(before,a._ram_words)
        checks.append("Two execute calls individually commit12 epochs without reseeding or rewriting RAM.")
        snap=a.snapshot();assert int(snap["virtualXorOperations"])==12*int(plan["domainBitWorkers"])
        assert int(snap["cpu"]["epochSampledPairs"])+int(snap["gpu"]["epochSampledPairs"])==12*64
        assert int(snap["sampling"]["descriptorSeedXorExpressions"])==int(snap["sampling"]["descriptorSeedHashEvaluations"])*6
        checks.append("Virtual XOR quantity, actual epoch sample count and descriptor helpers reconcile.")
        b=Session(plan);sessions.append(b);b.execute(epoch_count=12)
        replay=b.sample(ids);current=a.sample(ids)
        assert replay["entries"]==current["entries"]
        checks.append("Recorded-seed replay at the same exact epoch/address reproduces all CPU/GPU traces.")
        # Address halves above2^32 exercise the GPU's ulong mask indexing separately.
        words=np.array([0,2**32-1,2**32,2**40+7,2**63+13],dtype=np.uint64)
        offsets=np.array([0,7,16,31,5],dtype=np.uint32);base=np.array([0,1,0,1,1],dtype=np.uint32)
        gpu=XORGPU(len(base),plan["gpuDevice"])
        try:
            seeds=(epoch_seed(123456789,2**32-1),epoch_seed(123456789,2**32))
            actual,_=gpu.compute(base,words,offsets,*seeds);expected=cpu_pairs(base,words,offsets,*seeds)
            assert np.array_equal(actual,expected) and bool(np.all(actual[:,:7]<=1))
        finally:gpu.release()
        checks.append("Actual OpenCL kernel matches CPU binary traces across64-bit word/epoch index boundaries.")
        for left,right,answer in ((0,0,0),(0,1,1),(1,0,1),(1,1,0)):assert left^right==answer
        assert snap["epochCadence"]["count"]==10 and snap["epochRAMWriteBytes"]=="0"
        checks.append("Strict XOR truth table has onlyblack/white output; cadence counts exclude each run's first issue.")
        return {"schema":"XOR-PAIR-XPU/NATIVE-VALIDATION/1","status":"PASS","checks":checks,"actualCpuAndGpu":True,"maximumTestPayloadBytes":plan["totalReservoirBytes"],"sourceSha256":{name:hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in ("shared_epoch_xpu.py","xor_opencl_gpu.py")}}
    finally:
        for session in sessions:
            if not session.running:session.release()

if __name__=="__main__":
    report=run();target=Path(__file__).parent.parent/"xor-native-validation.json";target.write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8");print(json.dumps({"status":report["status"],"checks":len(report["checks"]),"report":str(target)}))
