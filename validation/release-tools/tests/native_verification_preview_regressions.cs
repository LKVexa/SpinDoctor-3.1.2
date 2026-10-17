using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class NativeVerificationPreviewRegressions
{
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
    private static int checks;
    private static Dictionary<string,object> Map(object value){return (Dictionary<string,object>)value;}
    private static Dictionary<string,object> D(params object[] values){return SpinDoctorCalculators.D(values);}
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception("FAIL: "+message);}
    private static double Number(object value){return Convert.ToDouble(value,CultureInfo.InvariantCulture);}
    private static Dictionary<string,object> Execute(TerminalSession s,string command){Dictionary<string,object> response=Map(s.Execute(command));Check((bool)response["ok"],command+" accepted");return Map(response["result"]);}
    private static Dictionary<string,object> State(string root){return Map(Json.DeserializeObject(File.ReadAllText(Path.Combine(root,"quantum-session-state.json"))));}
    private static void Save(string root,Dictionary<string,object> state){File.WriteAllText(Path.Combine(root,"quantum-session-state.json"),Json.Serialize(state));}
    private static void Rejected(string root,string saved,Action<Dictionary<string,object>> mutate,string message)
    {
        Dictionary<string,object> state=Map(Json.DeserializeObject(saved));mutate(state);Save(root,state);bool refused=false;try{using(TerminalSession s=new TerminalSession(root,false)){} }catch(InvalidDataException){refused=true;}Check(refused,message);Check(File.ReadAllText(Path.Combine(root,"quantum-session-state.json"))==Json.Serialize(state),"corrupt state preserved");
    }
    public static void Run(string scratch,bool realGpu)
    {
        Directory.CreateDirectory(scratch);checks=0;string failedRoot=Path.Combine(scratch,"missing-gpu");
        using(TerminalSession s=new TerminalSession(failedRoot,false))
        {
            Dictionary<string,object> result=Execute(s,"q.verify samples 4 errors-only");Check((string)result["phase"]=="failed"&&!(bool)result["passed"],"missing GPU fails truthfully");Check(((IList)result["samplePreview"]).Count==0,"no GPU output means no synthetic compared sample rows");Check((string)result["completedSamples"]=="0"&&(string)result["gpuStates"]=="0","no unavailable GPU/sample credit");Check((string)result["completedUtc"]!=""&&(string)result["receivedUtc"]!="","failed work records observed start/stop UTC");Check((string)result["omittedSampleRows"]=="0","empty preview coverage");
            Dictionary<string,object> assessment=Map(result["sampleAssessment"]);Check((int)assessment["capturedPreviewRows"]==0&&(int)assessment["distinctCpuPreviewVectors"]==0&&!(bool)assessment["cpuPreviewConstant"],"empty compared preview makes no synthetic variation or constant-state assertion");Check(Map(((IList)assessment["componentRanges"])[0])["cpuMinimum"]==null,"empty preview ranges remain absent rather than zero");
        }
        using(TerminalSession s=new TerminalSession(failedRoot,false)){Dictionary<string,object> restored=Map(Map(s.GetStatus())["lastVerificationJob"]);Check((string)restored["phase"]=="failed"&&((IList)restored["samplePreview"]).Count==0,"failure evidence restores");}
        string syncRoot=Path.Combine(scratch,"synchronous-archives");
        using(TerminalSession s=new TerminalSession(syncRoot,false))
        {
            Dictionary<string,object> submission=Map(s.StartSequence(D("commands",new string[]{"q.math norm amplitudes=[5,12]"})));Dictionary<string,object> job=Map(Map(submission["result"])["job"]);string id=(string)job["id"];
            for(int i=0;i<200;i++){job=Map(Map(Map(s.GetStatus())["sequence"])["job"]);if(!(bool)job["active"])break;Thread.Sleep(10);}Check((string)job["phase"]=="completed","synchronous sequence finished");
            Dictionary<string,object> archived=Map(Json.DeserializeObject(s.SequenceJson(id,0,"accepted")));string stopped=(string)archived["completedUtc"];Check(stopped.Length>0,"initial synchronous archive records returned time");
            MethodInfo resolve=typeof(TerminalSession).GetMethod("ResolvePdfRecords",BindingFlags.Instance|BindingFlags.NonPublic);
            Dictionary<string,object> client=D("command","forged","completedUtc","forged","receiptReady",true,"verificationPhase","passed","nativeArchive",D("type","sequence","id",id,"index",0,"stage","accepted"));
            object[] resolved=(object[])resolve.Invoke(s,new object[]{new object[]{client}});Dictionary<string,object> record=Map(resolved[0]);Check((string)record["completedUtc"]==stopped,"matched synchronous completed UTC survives PDF resolution");Check(!(bool)record["verificationRequired"]&&(!(bool)record["receiptReady"]||(string)record["receiptKind"]=="unverified")&&(string)record["verificationPhase"]!="passed","client forged verification metadata replaced by native receipt evidence");Check(!record.ContainsKey("finalNativeResponse"),"synchronous evidence does not fabricate async completion response");
        }
        HugePlanCancel(scratch);
        if(realGpu){Gpu(scratch);BoundedModels(scratch);}
        Console.WriteLine("PASS native verification preview/regression assertions: "+checks+"; real GPU requested="+realGpu);
    }
    private static void Gpu(string scratch)
    {
        string root=Path.Combine(scratch,"actual-gpu");Dictionary<string,object> result;double[] matrix;
        using(TerminalSession s=new TerminalSession(root,true))
        {
            Check((bool)Map(s.GetStatus())["gpuAvailable"],"actual OpenCL GPU is available (no simulated fallback)");Execute(s,"q.prepare grid 128 128");Execute(s,"q.migrate t 1");Dictionary<string,object> gate=Execute(s,"q.gate ry 0.3 all");matrix=(double[])gate["matrix"];
            result=Execute(s,"q.verify samples 128 errors-only");Check((string)result["phase"]=="completed"&&!(bool)result["active"]&&(bool)result["passed"],"genuine native CPU/GPU verification completed");Check((bool)result["hasPrepared"]&&Number(result["migrationT"])==1,"captured actual nontrivial model context");
            Dictionary<string,object> assessment=Map(result["sampleAssessment"]);Check((int)assessment["capturedPreviewRows"]==32&&(int)assessment["distinctCpuPreviewVectors"]>1&&(int)assessment["finiteCpuPreviewRows"]==32,"actual migrated preview assessment exposes bounded variation");Check((string)assessment["modelAddressDependence"]=="address-dependent-migration-rule"&&((string)assessment["scope"]).Contains("neither omitted rows nor the whole model domain"),"variation assessment scope excludes unsampled/omitted states");
            IList rows=(IList)result["samplePreview"];Check(rows.Count==32,"preview bounded at32 actual compared rows");Check(Int64.Parse((string)result["omittedSampleRows"])+32==Int64.Parse((string)result["completedSamples"]),"omitted rows counted exactly");
            HashSet<string> ids=new HashSet<string>();
            foreach(object raw in rows)
            {
                Dictionary<string,object> row=Map(raw);string address=(string)row["address"];Check(ids.Add(address),"preview addresses distinct");double[] expected=QuantumMath.State(UInt64.Parse(address),128,128,1,matrix);IList cpu=(IList)row["cpu"],gpu=(IList)row["gpu"];Check(cpu.Count==9&&gpu.Count==9,"nine actual components copied per backend");
                double maximum=0;for(int j=0;j<9;j++){Check(Number(cpu[j])==expected[j],"CPU preview byte-value matches genuine address calculation");Check(Math.Abs(Number(gpu[j])-expected[j])<=2e-9,"GPU preview matches independently selected source address");maximum=Math.Max(maximum,Math.Abs(Number(cpu[j])-Number(gpu[j])));}
                Check(Number(row["maxError"])==maximum&&(bool)row["passed"],"sample verdict/residual calculated from copied arrays");
            }
            Check((string)result["completedUtc"]!=""&&(string)result["receivedUtc"]!="","successful native verification records actual timestamps");Check(((string)result["verificationPurpose"]).Contains("show what that model predicts"),"verification explains model predictions");Check(((string)result["predictionMeaning"]).Contains("not experimental"),"implementation agreement is not promoted to physical validation");File.WriteAllText(Path.Combine(scratch,"actual-native-verification.json"),Json.Serialize(result));
        }
        string saved=File.ReadAllText(Path.Combine(root,"quantum-session-state.json"));Check(new FileInfo(Path.Combine(root,"quantum-session-state.json")).Length<2097152,"bounded preview remains below2MiB saved metadata size");
        using(TerminalSession s=new TerminalSession(root,false)){Dictionary<string,object> restored=Map(Map(s.GetStatus())["lastVerificationJob"]);Check(Json.Serialize(restored["samplePreview"])==Json.Serialize(result["samplePreview"]),"real native sample preview survives authenticated checkpoint reload");}
        Rejected(root,saved,delegate(Dictionary<string,object> state){Dictionary<string,object> job=Map(state["lastVerificationJob"]);job["omittedSampleRows"]="999999";},"mismatched preview coverage rejected");
        Rejected(root,saved,delegate(Dictionary<string,object> state){Dictionary<string,object> job=Map(state["lastVerificationJob"]);Map(((IList)job["samplePreview"])[0])["cpu"]=new object[]{0};},"truncated CPU vector rejected");
        Rejected(root,saved,delegate(Dictionary<string,object> state){Dictionary<string,object> job=Map(state["lastVerificationJob"]);Map(((IList)job["samplePreview"])[0])["maxError"]=1;},"fabricated residual rejected");
        Rejected(root,saved,delegate(Dictionary<string,object> state){Dictionary<string,object> job=Map(state["lastVerificationJob"]);Map(((IList)job["samplePreview"])[0])["address"]="16384";},"out-of-domain sample rejected");
        Rejected(root,saved,delegate(Dictionary<string,object> state){Dictionary<string,object> job=Map(state["lastVerificationJob"]);job["samplePreviewAddressDigest"]=new string('0',64);},"tampered preview address digest rejected");
        Rejected(root,saved,delegate(Dictionary<string,object> state){Map(Map(state["lastVerificationJob"])["sampleAssessment"])["distinctCpuPreviewVectors"]=999;},"fabricated preview variation assessment rejected");
        Rejected(root,saved,delegate(Dictionary<string,object> state){Map(state["lastVerificationJob"])["failure"]="retained native failure";},"saved success with nonempty failure rejected");
        Rejected(root,saved,delegate(Dictionary<string,object> state){Map(state["lastVerificationJob"])["checkpointError"]="retained checkpoint failure";},"saved success with checkpoint failure rejected");
        Rejected(root,saved,delegate(Dictionary<string,object> state){Dictionary<string,object> row=Map(((IList)Map(state["lastVerificationJob"])["samplePreview"])[0]);row["cpu"]=new object[]{1,0,0,0,0,0,2,1,0};row["gpu"]=new object[]{1,0,0,0,0,0,2,1,0};row["maxError"]=0;row["normError"]=0;row["spinError"]=3;row["passed"]=false;},"saved success containing mathematically failed retained sample rejected");
        // A3.0.5-shaped job without preview fields remains a supported historical checkpoint.
        Dictionary<string,object> legacy=Map(Json.DeserializeObject(saved));foreach(string key in new string[]{"lastVerificationJob","lastVerification"}){Dictionary<string,object> job=Map(legacy[key]);foreach(string added in new string[]{"samplePreview","samplePreviewLimit","omittedSampleRows","samplePreviewAddressDigest","samplePreviewAddressDigestEncoding","sampleComponentOrder","samplePreviewScope","sampleAssessment","verificationMethod","receivedUtc","completedUtc","hasPrepared","migrationT","modelGeneration","gridM","gridL","stateCount"})job.Remove(added);}Save(root,legacy);
        using(TerminalSession s=new TerminalSession(root,false)){Check((bool)Map(Map(s.GetStatus())["lastVerificationJob"])["passed"],"historical no-preview verification job remains loadable");}
        Save(root,Map(Json.DeserializeObject(saved)));
    }
    private static void HugePlanCancel(string scratch)
    {
        string root=Path.Combine(scratch,"huge-plan-cancel");
        using(TerminalSession session=new TerminalSession(root,false))
        {
            Dictionary<string,object> before=Map(session.GetStatus());Dictionary<string,object> queued=Execute(session,"q.verify samples 16384000000 errors-only");Check((bool)queued["queued"],"16.384billion sample request queues rather than allocates the plan");Dictionary<string,object> initial=Map(queued["job"]);Check((string)initial["requestedSamples"]=="16384000000"&&(string)initial["plannedSamples"]=="16384000012","64bit native request/plan counters remain exact beyondInt32");Check((int)initial["batchLimit"]==16384,"large request retains bounded16384state RAM batches");
            Execute(session,"q.verify cancel");Dictionary<string,object> stopped=null;for(int i=0;i<1000;i++){stopped=Map(Map(session.GetStatus())["lastVerificationJob"]);if(!(bool)stopped["active"])break;Thread.Sleep(10);}Check(stopped!=null&&!(bool)stopped["active"]&&!(bool)stopped["passed"]&&(string)stopped["completedUtc"]!="","bounded cancel records genuine stop and no verification PASS");Check(UInt64.Parse((string)stopped["cpuStates"])<=16384&&UInt64.Parse((string)stopped["gpuStates"])==0,"plan-start/cancel test performs at most one bounded CPU batch, no full workload");Check((string)Map(session.GetStatus())["descriptorHash"]==(string)before["descriptorHash"],"huge-plan cancellation does not prepare or mutate model");File.WriteAllText(Path.Combine(scratch,"huge-plan-cancel.json"),Json.Serialize(stopped));
        }
        using(TerminalSession session=new TerminalSession(root,false)){Dictionary<string,object> restored=Map(Map(session.GetStatus())["lastVerificationJob"]);Check((string)restored["requestedSamples"]=="16384000000"&&(string)restored["plannedSamples"]=="16384000012"&&!(bool)restored["passed"],"cancelled64bit plan checkpoint restores exact counts without automatic resume");}
    }
    private static void BoundedModels(string scratch)
    {
        using(TerminalSession session=new TerminalSession(Path.Combine(scratch,"bounded-models"),true))
        {
            Check((bool)Map(session.GetStatus())["gpuAvailable"],"bounded model cases use actual OpenCL GPU");Dictionary<string,object> before=Map(session.GetStatus());Dictionary<string,object> result=Execute(session,"q.verify samples 4 errors-only"),assessment=Map(result["sampleAssessment"]);Check((bool)result["passed"]&&!(bool)result["hasPrepared"]&&(string)assessment["modelAddressDependence"]=="address-constant-at-t0","unprepared default identity model is sampled truthfully without silent preparation");Check((int)assessment["distinctCpuPreviewVectors"]==1&&(bool)assessment["cpuPreviewConstant"],"actual default preview has one exact vector");Check((string)Map(session.GetStatus())["descriptorHash"]==(string)before["descriptorHash"]&&!(bool)Map(session.GetStatus())["hasPrepared"],"bounded verification preserves preparation and descriptor");
            Execute(session,"q.gate ry 0.3 all");result=Execute(session,"q.verify samples 4 errors-only");assessment=Map(result["sampleAssessment"]);IList rows=(IList)result["samplePreview"];IList cpu=(IList)Map(rows[0])["cpu"];Check((bool)result["passed"]&&(string)assessment["modelAddressDependence"]=="address-constant-at-t0"&&(int)assessment["distinctCpuPreviewVectors"]==1,"t0 plus sharedRY correctly stays addressconstant");Check(Number(cpu[8])>0&&Number(cpu[4])>0&&Number(cpu[6])<1,"addressconstant t0 rotation is not mislabeled identity/northpole");Dictionary<string,object> probability=Map(((IList)assessment["componentRanges"])[8]);Check(Number(probability["cpuMinimum"])==Number(cpu[8])&&Number(probability["cpuMaximum"])==Number(cpu[8]),"constant rotated prediction ranges come from actual returned rows");File.WriteAllText(Path.Combine(scratch,"actual-t0-rotation.json"),Json.Serialize(result));
            object nativeJob=typeof(TerminalSession).GetField("verificationJob",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);nativeJob.GetType().GetField("CheckpointError").SetValue(nativeJob,"test checkpoint diagnostic");Check(!(bool)Map(Map(session.GetStatus())["lastVerificationJob"])["passed"],"observed checkpoint error prevents native status PASS");nativeJob.GetType().GetField("CheckpointError").SetValue(nativeJob,"");nativeJob.GetType().GetField("Failure").SetValue(nativeJob,"test native diagnostic");Check(!(bool)Map(Map(session.GetStatus())["lastVerificationJob"])["passed"],"observed native failure prevents native status PASS");nativeJob.GetType().GetField("Failure").SetValue(nativeJob,"");
            Execute(session,"q.prepare grid 1 1");Execute(session,"q.migrate t 1");result=Execute(session,"q.verify samples 16384000000 errors-only");assessment=Map(result["sampleAssessment"]);Check((bool)result["passed"]&&(string)result["requestedSamples"]=="16384000000"&&(string)result["plannedSamples"]=="1"&&(bool)result["requestedClampedToDomain"],"huge request on single-address domain performs only one actual comparison");Check((string)assessment["modelAddressDependence"]=="single-address-domain"&&(int)assessment["capturedPreviewRows"]==1,"single-address assessment declares actual bounded scope");
        }
    }
}
