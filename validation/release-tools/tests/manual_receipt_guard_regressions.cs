using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class ManualReceiptGuardRegressions
{
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
    private static int checks;
    private static Dictionary<string,object> Map(object value){return (Dictionary<string,object>)value;}
    private static Dictionary<string,object> D(params object[] values){return SpinDoctorCalculators.D(values);}
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception("FAIL: "+message);}
    private static void Withheld(Action action,string message)
    {
        bool withheld=false;try{action();}catch(TargetInvocationException e){withheld=e.InnerException is InvalidOperationException&&e.InnerException.Message.Contains("withheld");}catch(InvalidOperationException e){withheld=e.Message.Contains("withheld");}Check(withheld,message);
    }
    private static Dictionary<string,object> Resolve(TerminalSession session,string type,string id)
    {
        Dictionary<string,object> reference=D("type",type,"id",id,"stage","accepted");if(type=="sequence")reference["index"]=0;
        MethodInfo method=typeof(TerminalSession).GetMethod("ResolvePdfRecords",BindingFlags.Instance|BindingFlags.NonPublic);
        object[] resolved=(object[])method.Invoke(session,new object[]{new object[]{D("receiptKind","forged","verificationPhase","passed","receiptOwnerSequenceId","forged","nativeArchive",reference)}});return Map(resolved[0]);
    }
    private static Dictionary<string,object> Job(TerminalSession session){return Map(Map(Map(session.GetStatus())["sequence"])["job"]);}
    private static Dictionary<string,object> Wait(TerminalSession session)
    {
        for(int i=0;i<3000;i++){Dictionary<string,object> job=Job(session);if(!(bool)job["active"])return job;Thread.Sleep(10);}throw new Exception("Folder worker did not stop within30seconds.");
    }
    private static Dictionary<string,object> MathHistory(TerminalSession session)
    {
        foreach(object raw in (IList)Map(session.CommandHistoryPage(0,256))["records"]){Dictionary<string,object> entry=Map(raw);if((string)entry["command"]=="q.math norm amplitudes=[5,12]")return entry;}throw new Exception("No genuine calculator command history archive.");
    }
    private static void Imported(TerminalSession session)
    {
        Dictionary<string,object> evidence=D("response",D("ok",true,"result",D("passed",true,"phase","completed")));
        Dictionary<string,object> supplied=D("command","q.math norm amplitudes=[5,12]","nativeResponse",D("ok",true,"result",D("norm",13,"verificationRequired",true,"verificationPhase","passed","verification",evidence,"receiptReady",true)),"verificationRequired",true,"verificationPhase","passed","verification",evidence,"receiptReady",true,"receiptKind","native-sample-verified","receiptOwnerSequenceId","forged","receiptMetadata",D("verificationRequired",true,"verificationPhase","passed","verification",evidence));
        MethodInfo method=typeof(TerminalSession).GetMethod("ResolvePdfRecords",BindingFlags.Instance|BindingFlags.NonPublic);object[] records=(object[])method.Invoke(session,new object[]{new object[]{supplied}});Dictionary<string,object> record=Map(records[0]);
        Check(!(bool)record["verificationRequired"]&&!(bool)record["receiptReady"]&&(string)record["verificationPhase"]=="unverified-import"&&(string)record["receiptKind"]=="unverified-import","client-supplied snapshots cannot forge a native verification receipt");Check(Map(record["verification"]).Count==0&&!record.ContainsKey("receiptOwnerSequenceId")&&!record.ContainsKey("receiptMetadata"),"client verification evidence and owner claims removed");Check(Convert.ToDouble(Map(Map(record["nativeResponse"])["result"])["norm"])==13,"imported numerical snapshot remains available for review");
        string pdf=Encoding.ASCII.GetString(session.ReportPdf(D("kind","selected","records",new object[]{supplied},"status",D(),"view","summary")));Check(pdf.StartsWith("%PDF-")&&!pdf.Contains("PASS for the recorded sampled procedural model"),"forged imported PASS cannot render a scoped native verification certificate");
    }
    private static void Diagnostic(TerminalSession session,string parentId,string childId,string commandId)
    {
        string raw=session.CommandJson(commandId,"accepted");Dictionary<string,object> payload=Map(Json.DeserializeObject(raw));
        Check((string)payload["sequenceOwnerId"]==childId,"native command immutable archive retains exact child owner");Check((string)payload["executionOutcome"]=="completed","calculator execution completed independently of model verification failure");
        Check(Convert.ToDouble(Map(Map(payload["response"])["result"])["norm"],CultureInfo.InvariantCulture)==13,"actual numerical result survives failed automatic verification");
        Check(session.CommandPrintout(commandId,"accepted").Contains("13"),"terminal failed diagnostic permits preserved raw TXT data");
        Check(session.CommandCsv(commandId,"accepted").Contains("/norm\"")&&session.CommandCsv(commandId,"accepted").Contains("\"13\""),"failed diagnostic CSV retains actual native norm without a verified-success receipt");
        Check(session.CommandJson(parentId,"accepted").Length>0,"parent failed diagnostic permits original acceptance JSON");
        Dictionary<string,object> resolved=Resolve(session,"command",commandId);Check((bool)resolved["verificationRequired"]&&!(bool)resolved["receiptReady"]&&(string)resolved["verificationPhase"]=="failed"&&(string)resolved["receiptKind"]=="diagnostic-unverified","historical command PDF uses native failed diagnostic gate metadata");Check((string)resolved["receiptOwnerSequenceId"]==childId,"historical command PDF owner comes from immutable native archive");
        Check((string)Resolve(session,"command",parentId)["receiptKind"]=="diagnostic-unverified","historical parent PDF also uses failed diagnostic gate metadata");
        Check((bool)Map(session.Execute("q.history export "+commandId))["ok"],"manual history export permits terminal failed diagnostic after gate stops");
    }
    private static void WhitespaceJob(string scratch)
    {
        using(TerminalSession session=new TerminalSession(Path.Combine(scratch,"whitespace-job"),false))
        {
            Dictionary<string,object> response=Map(session.Execute(" \tQ.VERIFY samples 20000 errors-only"));Check((bool)response["ok"]&&(bool)Map(response["result"])["queued"],"whitespace/case-tolerant command starts genuine asynchronous sample job");string id=(string)response["reportId"];
            for(int i=0;i<1000;i++){Dictionary<string,object> status=Map(session.GetStatus());if(!(bool)Map(status["lastVerificationJob"])["active"])break;Thread.Sleep(10);}
            Dictionary<string,object> entry=null;foreach(object raw in (IList)Map(session.CommandHistoryPage(0,256))["records"]){Dictionary<string,object> row=Map(raw);if((string)row["id"]==id)entry=row;}
            Check(entry!=null&&(string)entry["jobType"]=="verification"&&entry.ContainsKey("completedUtc")&&(string)entry["phase"]=="failed","whitespace asynchronous report binds exact backend and captures genuine failed completion");
            Dictionary<string,object> payload=Map(Json.DeserializeObject(session.CommandJson(id,"completed")));Check(!(bool)Map(payload["response"])["ok"]&&(string)Map(Map(payload["response"])["result"])["phase"]=="failed","completion archive retains actual sample failure rather than missing/false success");
        }
    }
    public static void Run(string scratch)
    {
        Directory.CreateDirectory(scratch);checks=0;string workspace=Path.Combine(scratch,"workspace"),inputs=Path.Combine(scratch,"inputs");Directory.CreateDirectory(inputs);File.WriteAllText(Path.Combine(inputs,"01.txt"),"q.math norm amplitudes=[5,12]\nq.math norm amplitudes=[3,4]\n");
        string parentReportId="",childId="",commandId="";
        using(TerminalSession session=new TerminalSession(workspace,false))
        using(ManualResetEventSlim paused=new ManualResetEventSlim(false))
        using(ManualResetEventSlim release=new ManualResetEventSlim(false))
        {
            Imported(session);
            SequenceTerminal sequence=(SequenceTerminal)typeof(TerminalSession).GetField("sequences",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
            FieldInfo executorField=typeof(SequenceTerminal).GetField("execute",BindingFlags.Instance|BindingFlags.NonPublic);Func<string,object> original=(Func<string,object>)executorField.GetValue(sequence);int inputCalls=0;
            // Test instrumentation pauses before the second real input command;
            // every native response and verification result is still unmodified.
            executorField.SetValue(sequence,new Func<string,object>(delegate(string command){if(command.StartsWith("q.math",StringComparison.Ordinal)&&Interlocked.Increment(ref inputCalls)==2){paused.Set();if(!release.Wait(30000))throw new TimeoutException("Test pause timed out.");}return original(command);}));
            try
            {
                Dictionary<string,object> accepted=Map(session.Execute("q.folder run path=\""+inputs.Replace('\\','/')+"\" downloads=true purge=true samples=32"));Check((bool)accepted["ok"],"real folder run accepted with default required verification");parentReportId=(string)accepted["reportId"];Check(paused.Wait(10000),"deterministic pending worker pause reached after first genuine command return");
                Dictionary<string,object> history=MathHistory(session);commandId=(string)history["id"];childId=(string)history["sequenceOwnerId"];Check(childId.Length==32,"worker report belongs to the exact folder child");
                Withheld(delegate{session.CommandPrintout(commandId,"accepted");},"manual command TXT cannot escape pending verification");Withheld(delegate{session.CommandJson(commandId,"accepted");},"manual command JSON cannot escape pending verification");Withheld(delegate{session.CommandPrintout(parentReportId,"accepted");},"parent acceptance TXT cannot escape pending verification");Withheld(delegate{session.CommandJson(parentReportId,"accepted");},"parent acceptance JSON cannot escape pending verification");
                Withheld(delegate{Resolve(session,"command",commandId);},"manual command PDF native archive cannot escape pending verification");Withheld(delegate{Resolve(session,"command",parentReportId);},"parent acceptance PDF cannot escape pending verification");Withheld(delegate{Resolve(session,"sequence",childId);},"sequence PDF native archive cannot escape pending verification");
                Withheld(delegate{session.SequencePrintout(childId,0,"accepted");},"sequence TXT guard remains effective");Withheld(delegate{session.SequenceJson(childId,0,"accepted");},"sequence JSON guard remains effective");
                Withheld(delegate{session.CommandCsv(commandId,"accepted");},"manual command CSV cannot escape pending verification");
                Withheld(delegate{session.CommandCsv(parentReportId,"accepted");},"parent acceptance CSV cannot escape pending verification");
                Withheld(delegate{session.SequenceCsv(childId,0,"accepted");},"sequence step CSV cannot escape pending verification");
                Withheld(delegate{using(FileStream csv=session.OpenSequenceCsv(childId,"latest")){}},"streamed file CSV cannot escape pending verification");
                Withheld(delegate{session.ReportCsv(D("kind","selected","records",new object[]{D("nativeArchive",D("type","command","id",commandId,"stage","accepted"))},"status",D()));},"PDF companion CSV cannot bypass pending native receipt policy");
                Withheld(delegate{session.ReportEquations(D("kind","selected","records",new object[]{D("nativeArchive",D("type","command","id",commandId,"stage","accepted"))},"status",D()));},"equation text export cannot bypass pending native receipt policy");
                Dictionary<string,object> rejected=Map(session.Execute("q.history export "+commandId));Check(!(bool)rejected["ok"]&&Convert.ToString(rejected["error"]).Contains("withheld"),"terminal q.history export cannot bypass native receipt guard");
            }
            finally{release.Set();}
            Dictionary<string,object> stopped=Wait(session);Check((string)stopped["phase"]=="failed"&&!(bool)stopped["receiptReady"],"missing actual GPU prevents a successful folder receipt");Check((int)stopped["completedCommands"]==2,"sampling failure preserves all actually completed input-command credits");
            Dictionary<string,object> file=Map(((IList)stopped["files"])[0]);Check((string)file["reportPhase"]=="withheld-verification"&&(string)file["purgePhase"]=="skipped-verification-failure","failed verification withholds normal archive and transient purge");
            Diagnostic(session,parentReportId,childId,commandId);File.WriteAllText(Path.Combine(scratch,"failed-folder-status.json"),Json.Serialize(stopped));
        }
        using(TerminalSession session=new TerminalSession(workspace,false)){Diagnostic(session,parentReportId,childId,commandId);Check((string)Job(session)["phase"]=="failed","failed receipt policy remains truthful after checkpoint reload");}
        WhitespaceJob(scratch);
        Console.WriteLine("PASS terminal manual receipt barrier/regression assertions: "+checks);
    }
}
