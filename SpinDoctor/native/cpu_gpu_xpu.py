"""CLI/import entry point: native full-RAM bulk waves + selected CPU/GPU work."""
from bulk_wave_xpu import Session,plan_budget,probe,memory_status
import argparse,hashlib,json,time
from pathlib import Path

def main():
    parser=argparse.ArgumentParser(description="Completed native RAM write waves; only selected bits receive CPU/GPU arithmetic.")
    parser.add_argument("--probe",action="store_true");parser.add_argument("--auto-ram",action="store_true");parser.add_argument("--budget-mib",type=int);parser.add_argument("--reserve-gib",type=float,default=2);parser.add_argument("--cpu-workers",type=int,default=8);parser.add_argument("--gpu-fraction",type=float,default=.5);parser.add_argument("--read-depth",type=int,default=1);parser.add_argument("--sample-count",type=int,default=1060);parser.add_argument("--wave-count",type=int,default=1);parser.add_argument("--deadline-ms",type=float,default=2);parser.add_argument("--chunk-mib",type=int);parser.add_argument("--seed",type=int,default=1);parser.add_argument("--a",type=int,default=1);parser.add_argument("--b",type=int,default=1);parser.add_argument("--report",default="cpugpu-execution-report.json");args=parser.parse_args()
    if args.probe:print(json.dumps(probe(),indent=2));return
    config={name:getattr(args,name) for name in ("reserve_gib","cpu_workers","gpu_fraction","read_depth","sample_count","wave_count","deadline_ms","seed","a","b")};config["auto_ram"]=args.auto_ram or args.budget_mib is None
    if args.budget_mib is not None:config["budget_mib"]=args.budget_mib
    if args.chunk_mib is not None:config["chunk_mib"]=args.chunk_mib
    plan=plan_budget(config);print(json.dumps({"plannedReservoirBytes":plan["totalReservoirBytes"],"plannedWaveBytes":plan["effectiveWaveBytes"],"fullReservoirWave":plan["fullReservoirWave"],"reserveBytes":plan["memory"]["reserveBytes"],"gpuStagingBytes":plan["memory"]["gpuDeviceStagingBytes"]}),flush=True);last=[0.,""]
    def progress(snapshot):
        now=time.monotonic()
        if now-last[0]>=2 or snapshot["phase"]!=last[1] or snapshot["status"]!="RUNNING":
            print(json.dumps({"phase":snapshot["phase"],"status":snapshot["status"],"allocatedBytes":snapshot["currentAllocatedBytes"],"initializedBytes":snapshot["initializedBytes"],"actualBulkBytesWritten":snapshot["bulkBytesWritten"],"sampledMultiplyAddOperations":snapshot["sampledMultiplyAddOperations"],"completedWaves":snapshot["completedWaves"]}),flush=True);last[:]=[now,snapshot["phase"]]
    session=Session(plan,progress)
    try:
        report=session.execute();report["sourceSha256"]={name:hashlib.sha256((Path(__file__).parent/name).read_bytes()).hexdigest() for name in ("cpu_gpu_xpu.py","bulk_wave_xpu.py","full_traversal_reference.py","opencl_gpu.py")};report["releaseSnapshot"]=session.release();Path(args.report).write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
        print(json.dumps({"status":report["status"],"report":str(Path(args.report).resolve()),"waves":[{"completionMs":wave["completionMs"],"targetMet":wave["targetMet"],"bytesWrittenThisWave":wave["bytesWrittenThisWave"],"fullReservoirWave":wave["fullReservoirWave"]} for wave in report["waves"]],"released":True}),flush=True)
        if report["status"]!="COMPLETE":raise RuntimeError("Run did not fully complete: "+report["status"])
    finally:
        if not session.running and session.phase!="released":session.release()

if __name__=="__main__":main()
