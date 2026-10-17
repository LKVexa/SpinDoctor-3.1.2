using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class CommandReceiptRegressions
{
    private static int checks;
    private static Dictionary<string,object> D(params object[] pairs){return SpinDoctorCalculators.D(pairs);}
    private static void Check(bool condition,string label){checks++;if(!condition)throw new Exception("FAIL "+label);}
    public static void Run(string scratch)
    {
        Directory.CreateDirectory(scratch);JavaScriptSerializer json=new JavaScriptSerializer();
        SpinDoctorCommandReports store=new SpinDoctorCommandReports(scratch);
        Dictionary<string,object> response=D("ok",true,"command","q.math norm amplitudes=[5,12]","result",D("norm",13.0,"normSquared",169.0));string original=json.Serialize(response);
        Dictionary<string,object> item=store.Record("q.math norm amplitudes=[5,12]",response);string id=(string)item["id"];
        Check((string)item["phase"]=="completed","sync archive has completed execution phase");
        Check(!String.IsNullOrEmpty((string)item["commandCompletedUtc"]),"sync command has observed completion UTC");
        Check(!item.ContainsKey("completedUtc"),"sync stamp does not create nonexistent async completion archive");
        string txt=store.Printout(id);Check(txt.Contains("COMMAND EXECUTION: COMPLETED"),"top receipt completion stamp");Check(txt.Contains("Command completed/stopped UTC:"),"top receipt UTC");Check(txt.Contains("not independent numerical verification"),"command completion not verification");
        Check(json.Serialize(((Dictionary<string,object>)json.DeserializeObject(store.Json(id)))["response"])==original,"original numerical response bytes unchanged");
        Check(store.Printout(id,"completed")==txt,"explicit completed sync stage resolves same complete evidence");
        Dictionary<string,object> rejected=store.Record("q.no-such-command",D("ok",false,"error","Unknown command"));Check((string)rejected["phase"]=="failed","rejected sync command not successful stamp");Check(store.Printout((string)rejected["id"]).Contains("COMMAND EXECUTION: FAILED"),"failed stamp explicit");
        Dictionary<string,object> accepted=store.Record("q.verify samples 20000 errors-only",D("ok",true,"result",D("queued",true,"job",D("id","actual-job","active",true,"phase","running"))));string pendingId=(string)accepted["id"];
        Check((string)accepted["executionOutcome"]=="pending"&&(string)accepted["commandCompletedUtc"]=="","queued work has no completion stamp");store.BindJob(pendingId,"verification","actual-job");Check(store.Pending().Length==1,"only matched active work pending");
        Dictionary<string,object> status=D("ok",true,"result",D("id","actual-job","active",true,"phase","running"));Check(!SpinDoctorCommandReports.AwaitingCompletion("q.verify status",status),"status query not bound to sampled work");Check(!SpinDoctorCommandReports.AwaitingCompletion("q.folder status",D("ok",true,"result",D("job",status["result"]))),"folder status query not bound");
        store.Complete(pendingId,D("ok",true,"result",D("id","actual-job","active",false,"phase","completed","completedUtc","2026-10-10T12:00:00.0000000Z","passed",true)));
        Check(store.Printout(pendingId,"completed").Contains("2026-10-10T12:00:00.0000000Z"),"async receipt uses native completed UTC");Check(store.Printout(pendingId,"accepted").Contains("COMMAND EXECUTION: PENDING"),"accepted history not rewritten");
        Dictionary<string,object> uncertain=store.Record("q.verify samples 20000 errors-only",D("ok",true,"result",D("queued",true)));store.BindJob((string)uncertain["id"],"verification","unknown-job");store.Complete((string)uncertain["id"],D("ok",false,"nativeCompletionKnown",false,"result",D("phase","orchestration-failed")));
        Check(store.Printout((string)uncertain["id"]).Contains("Not captured - matching native completion pending or unknown"),"uncertain native completion gets no fabricated UTC");
        SpinDoctorCommandReports restored=new SpinDoctorCommandReports(scratch);Check(restored.Pending().Length==0,"restart restores sync/async stamp metadata");Check(restored.Printout(id)==txt,"restart preserves original sync receipt and hashes");
        string owner=new string('a',32);Dictionary<string,object> owned=store.Record("q.math norm amplitudes=[5,12]",response,owner);string ownedId=(string)owned["id"];
        Check(store.ReceiptOwner(ownedId)==owner,"receipt owner bound at immutable capture");bool held=false;try{store.Json(ownedId);}catch(InvalidOperationException){held=true;}Check(held,"owned receipt fails closed without native policy");
        bool ready=false;store.SetReceiptGuard(delegate(string value){Check(value==owner,"guard receives native owner");if(!ready)throw new InvalidOperationException("Verification pending");});
        held=false;try{store.Printout(ownedId);}catch(InvalidOperationException){held=true;}Check(held,"TXT held before verification");held=false;try{store.Execute(new string[]{"q.history","export",ownedId});}catch(InvalidOperationException){held=true;}Check(held,"q.history export held before verification");
        ready=true;Check(store.Json(ownedId).Contains("sequenceOwnerId"),"verified receipt readable without rewriting evidence");
        SpinDoctorCommandReports ownedRestored=new SpinDoctorCommandReports(scratch);Check(ownedRestored.ReceiptOwner(ownedId)==owner,"owner survives reload");ownedRestored.SetReceiptGuard(delegate(string value){if(value!=owner)throw new Exception("wrong owner");});Check(ownedRestored.Printout(ownedId).Contains(owner),"reloaded owned receipt consults same native policy");
        Console.WriteLine("PASS "+checks+" command receipt completion/identity/restart assertions.");
    }
}
