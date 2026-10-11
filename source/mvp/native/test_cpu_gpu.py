"""Small real CPU/OpenCL tests. Never allocates the full available-RAM budget."""
import ctypes as C,hashlib,json,os,threading
from pathlib import Path
from unittest.mock import patch
import numpy as np
import bulk_wave_xpu as backend

def main():
    hardware=backend.probe();assert hardware["available"] and hardware["actualGpuProbe"]["status"]=="PASS"
    checks=["real Intel/OpenCL GPU compile, dispatch, completion and readback probe"]
    observations=[]
    for a,b in ((0,0),(0,1),(1,0),(1,1)):
        plan=backend.plan_budget({"auto_ram":False,"budget_mib":1,"sample_count":32,"read_depth":2,"wave_count":2,"cpu_workers":4,"seed":17,"a":a,"b":b})
        job=backend.Session(plan);assert job.snapshot()["currentAllocatedBytes"]==0
        try:
            result=job.execute();snapshot=job.snapshot();assert result["status"]=="COMPLETE"
            assert snapshot["initializedBytes"]==str(plan["totalReservoirBytes"])
            assert snapshot["bulkBytesWritten"]==str(plan["totalReservoirBytes"]*2)
            assert snapshot["bulkBitsWritten"]==str(plan["totalReservoirBytes"]*2*8)
            assert snapshot["completedBitOperations"]=="0" and snapshot["completedSteps"]=="0"
            assert snapshot["sampledMultiplyAddOperations"]==str(32*4*2*2*2)
            assert int(snapshot["cpu"]["sampledBits"])+int(snapshot["gpu"]["sampledBits"])==32*4*2*2
            assert int(snapshot["gpu"]["kernelDispatches"])==4
            pattern=plan["initialPatternByte"]
            for _ in range(2):pattern=(pattern&(255 if a else 0))^(255 if b else 0)
            actual=np.ctypeslib.as_array((C.c_ubyte*plan["totalReservoirBytes"]).from_address(job.ram.pointer))
            assert bool(np.all(actual==pattern)),"Every byte in the small actual reservoir must match the independent template."
            for trace in result["recentSamples"]:
                assert trace["product"]==a*trace["previous"] and trace["rawSum"]==trace["product"]+b
                assert trace["result"]==trace["rawSum"]%2==trace["observed"] and trace["matches"]
            for wave in result["waves"]:
                assert wave["bytesWrittenThisWave"]==plan["totalReservoirBytes"] and wave["fullReservoirWave"]
                assert wave["completionMs"]>=wave["fillOnlyMs"]>=0
                assert wave["latencyTargetMet"]==(wave["completionMs"]<wave["deadlineMs"])
                assert wave["targetMet"]==(wave["latencyTargetMet"] and wave["cadenceMs"] is not None and wave["cadenceMs"]<wave["deadlineMs"])
                if wave["cadenceMs"] is not None:assert wave["cadenceTargetMet"]==(wave["cadenceMs"]<wave["deadlineMs"])
            before=job.snapshot();query=job.sample(["0",str(plan["bitCount"] and int(plan["bitCount"])-1)]);after=job.snapshot()
            assert query["allSamplesMatch"] and int(after["sampledMultiplyAddOperations"])-int(before["sampledMultiplyAddOperations"])==4
            assert before["bulkBytesWritten"]==after["bulkBytesWritten"]
            try:job.sample("12");raise AssertionError("String sample IDs should be rejected.")
            except ValueError:pass
            observations.append({"a":a,"b":b,"waveCompletionMs":[wave["completionMs"] for wave in result["waves"]],"sampledOperations":snapshot["sampledMultiplyAddOperations"]})
        finally:
            released=job.release();assert released["currentAllocatedBytes"]==0 and released["materializedStoredWorkerBits"]=="0"
    checks+= ["all four binary coefficient pairs with complete actual byte-array oracle","separate bulk write quantities versus sampled integer multiply/add counters","two true waves and two real sampled CPU/GPU compute passes per wave","actual GPU event/readback traces match independent scalar arithmetic","latency/cadence strict deadline fields use completed wall intervals","fresh known-shape live sampling adds sample work only","release removes current storage and preserves historical actual writes"]
    plan=backend.plan_budget({"auto_ram":False,"budget_mib":1,"sample_count":1060,"wave_count":1});job=backend.Session(plan)
    try:
        report=job.execute();assert set(item["digit"] for item in report["digits"])==set(range(10))
        event=threading.Event();paused=backend.Session(plan,lambda snap:event.set() if int(snap["initializedBytes"])>0 else None,event)
        try:
            partial=paused.execute();assert partial["status"]=="PAUSED" and partial["snapshot"]["bulkBytesWritten"]=="0"
            event.clear();paused.on_progress=None;assert paused.execute()["status"]=="COMPLETE"
        finally:paused.release()
    finally:job.release()
    checks+= ["mixed seeded fill pattern and four independent sampled addresses yield digits0 through9","pause stops subsequent bounded native chunks and resume completes actual remaining bytes"]
    fake={"available":True,"memory":{"physicalTotalBytes":32*backend.GIB,"availableBytes":16*backend.GIB},"gpuDevice":{"name":"fake-for-planner-only","hostUnifiedMemory":True,"globalMemoryBytes":8*backend.GIB,"maxAllocationBytes":backend.MIB}}
    with patch.object(backend,"probe",return_value=fake):
        plan=backend.plan_budget({"auto_ram":True,"cpu_workers":8});assert plan["totalReservoirBytes"]<=fake["memory"]["availableBytes"]-plan["memory"]["reserveBytes"]-plan["memory"]["overheadBytes"]
        assert plan["memory"]["sharedPhysicalBudgetCountedOnce"] and plan["bitCount"]==str(plan["totalReservoirBytes"]*8)
        assert backend.plan_budget(plan["config"])["config"]==plan["config"]
        fake["gpuDevice"]["maxAllocationBytes"]=4
        try:backend.plan_budget({"auto_ram":False,"budget_mib":1});raise AssertionError("Staging limit must reject plan.")
        except MemoryError:pass
    with patch.object(backend,"probe",return_value={"available":False,"error":"NoGPU"}):
        try:backend.plan_budget({"budget_mib":1});raise AssertionError("No CPU fallback.")
        except backend.OpenCLError:pass
    checks+= ["one actual free-RAM budget with reserve/overhead and exact1byte8bit reservoir","replayable normalized planner configuration and GPU staging limits","unavailable real GPU is a fatal honest error without CPU fallback"]
    plan=backend.plan_budget({"auto_ram":False,"budget_mib":1,"sample_count":16});job=backend.Session(plan)
    with patch.object(backend,"memory_status",return_value={"availableBytes":0}):assert job.execute()["status"]=="PAUSED"
    assert job.snapshot()["currentAllocatedBytes"]==0;job.release();checks.append("reserve pressure stops before allocation/writes")
    root=Path(__file__).resolve().parent.parent
    hashes={name:hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in ("cpu_gpu_xpu.py","bulk_wave_xpu.py","full_traversal_reference.py","opencl_gpu.py")}
    report={"status":"PASS","checks":checks,"realGpuDevice":hardware["gpuDevice"],"smallConfigurations":4,"perConfigurationReservoirBytes":backend.MIB,"observations":observations,"sourceSha256":hashes,"scope":"Small real native CPU/GPU runs and planner/error checks. The full available-RAM execution report is produced separately by root."}
    (root/"cpugpu-core-validation.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8");print(json.dumps(report,indent=2))

if __name__=="__main__":main()
