using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class FolderRegressions
{
    private static int checks;
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
    private static Dictionary<string,object> D(params object[] pairs){Dictionary<string,object> result=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)result.Add((string)pairs[i],pairs[i+1]);return result;}
    private static Dictionary<string,object> M(object value){return (Dictionary<string,object>)value;}
    private static int N(object value){return Convert.ToInt32(value,CultureInfo.InvariantCulture);}
    private static List<Dictionary<string,object>> Rows(object value){List<Dictionary<string,object>> rows=new List<Dictionary<string,object>>();foreach(object item in (IEnumerable)value)rows.Add(M(item));return rows;}
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static void Throws(Action action,string expected){try{action();}catch(Exception error){Check(error.Message.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0,"Unexpected rejection: "+error.Message);return;}throw new Exception("Expected rejection: "+expected);}
    private static Dictionary<string,object> Finished(SequenceTerminal terminal){DateTime end=DateTime.UtcNow.AddSeconds(300);for(;;){Dictionary<string,object> job=M(terminal.GetStatus()["job"]);if(!(bool)job["active"])return job;if(DateTime.UtcNow>end)throw new Exception("Folder worker timed out.");Thread.Sleep(10);}}
    private static object Compile(string path){List<string> commands=new List<string>();foreach(string raw in File.ReadAllLines(path,Encoding.UTF8)){string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith("//"))continue;commands.Add(line);}return D("commands",commands.ToArray(),"sourceSha256",new string('a',64),"sourceFormat","txt-sequence","inputName",Path.GetFileName(path));}
    private static void Configure(SequenceTerminal terminal){terminal.SetCapabilityResolver(delegate(string command){return D("available",true);});terminal.SetFileCompiler(Compile);}
    private static int CountReports(FileStream stream){int count=0;using(StreamReader reader=new StreamReader(stream,new UTF8Encoding(false,true))){string line;while((line=reader.ReadLine())!=null)if(line=="SPINDOCTOR — COMPLETE NATIVE COMMAND REPORT")count++;}return count;}
    private static string Id(Dictionary<string,object> job){return (string)job["id"];}
    private static void LargeFolder(string root,string pack)
    {
        string workspace=Path.Combine(root,"large-reports");Directory.CreateDirectory(workspace);int calls=0,purges=0;string payload=new string('x',350000),jobId="",lastChild="",lastArchive="";
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){Interlocked.Increment(ref calls);return D("ok",true,"command",command,"result",D("payload",payload),"status",D());},delegate(){return D();}))
        {
            Configure(terminal);terminal.SetFileCleanup(delegate(Dictionary<string,object> cleanupFile){Interlocked.Increment(ref purges);});terminal.RunFolder(pack,true,true,false,10240);Dictionary<string,object> final=Finished(terminal);jobId=Id(final);
            Check((string)final["phase"]=="completed","172-command folder should complete: "+final["error"]);Check(calls==172,"Actual test pack must execute 172 commands, observed "+calls);Check(N(final["totalCommands"])==172 &&N(final["completedCommands"])==172,"Aggregate folder totals must both be 172");Check((bool)final["totalCommandsKnown"],"Final total must be known");Check(N(final["completedFiles"])==24 &&N(final["totalFiles"])==24 &&purges==24,"All 24 files must archive and purge");
            Check(Rows(final["records"]).Count==0 &&((string[])final["commands"]).Length==0,"Parent checkpoint must not contain child commands or records");
            List<Dictionary<string,object>> files=Rows(final["files"]);Dictionary<string,object> last=files[23];lastChild=(string)last["childJobId"];Check(N(last["totalCommands"])==128 &&N(last["completedCommands"])==128,"Last child must retain 128-command boundary");Check((string)last["executionPhase"]=="completed" &&(string)last["reportPhase"]=="archived" &&(string)last["purgePhase"]=="completed","Native, archive and purge outcomes must be distinct");
            Dictionary<string,object> compact=M(Json.DeserializeObject(terminal.SequencePreviewJson(lastChild)));Check(!compact.ContainsKey("completeCommandReports") &&Rows(M(compact["job"])["records"]).Count==128,"Recovery metadata must retain compact child records only");
            using(FileStream report=terminal.OpenSequencePrintout(lastChild,"latest")){Check(report.Length>33554432,"Last archive must exceed old 32 MiB aggregate limit");Check(report.Position==0,"Streaming exports must begin at zero");Check(CountReports(report)==128,"Large file export must retain all 128 returns");}
            using(FileStream report=terminal.OpenSequencePrintout(jobId,"latest")){Check(CountReports(report)==172,"Streamed complete folder export must retain all 172 returns");}
            Throws(delegate(){terminal.SequencePrintout(lastChild,null);},"32 MiB");
            Dictionary<string,object> child=M(compact["job"]);lastArchive=Path.Combine(workspace,"sequence-reports",(string)child["sequenceReportFile"]);
        }
        using(SequenceTerminal restored=new SequenceTerminal(workspace,delegate(string command){throw new Exception("Must not rerun completed work");},delegate(){return D();}))
        {
            Dictionary<string,object> final=M(restored.GetStatus()["job"]);Check(Id(final)==jobId &&N(final["completedCommands"])==172 &&N(final["totalCommands"])==172 &&N(final["totalFiles"])==24,"Restart must restore aggregate identity and totals");using(FileStream report=restored.OpenSequencePrintout(lastChild,"latest")){Check(report.Length>33554432,"Restart must retain streamed large report");}
            using(FileStream altered=new FileStream(lastArchive,FileMode.Open,FileAccess.Write,FileShare.None)){altered.Position=0;altered.WriteByte((byte)'X');}
            Throws(delegate(){using(FileStream report=restored.OpenSequencePrintout(lastChild,"latest")){}},"hash mismatch");
        }
        Console.WriteLine("PASS actual 24-file / 172-command pack, streamed >32 MiB final file, archive integrity and restart");
    }
    private static string SingleInput(string root,string name,int count){string folder=Path.Combine(root,name+"-inputs");Directory.CreateDirectory(folder);StringBuilder text=new StringBuilder();for(int i=0;i<count;i++)text.AppendLine("q.math norm vector=[5,12]");File.WriteAllText(Path.Combine(folder,"001.txt"),text.ToString(),new UTF8Encoding(false));return folder;}
    private static void AuxiliaryFailures(string root)
    {
        string folder=SingleInput(root,"purge",1),workspace=Path.Combine(root,"purge-work");Directory.CreateDirectory(workspace);
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){return D("ok",true,"result",D(),"status",D());},delegate(){return D();}))
        {
            Configure(terminal);terminal.SetFileCleanup(delegate(Dictionary<string,object> cleanupFile){throw new IOException("Injected cleanup failure");});terminal.RunFolder(folder,true,true,false,10240);Dictionary<string,object> final=Finished(terminal);Dictionary<string,object> file=Rows(final["files"])[0];Check((string)final["phase"]=="completed" &&N(final["completedCommands"])==1,"Purge failure must not invalidate completed calculation");Check((string)file["purgePhase"]=="failed" &&(string)file["reportPhase"]=="archived" &&((string[])final["warnings"]).Length==1,"Purge failure must be visible separately");
        }
        folder=SingleInput(root,"archive",1);workspace=Path.Combine(root,"archive-work");Directory.CreateDirectory(workspace);SequenceTerminal archiveTerminal=null;
        using(archiveTerminal=new SequenceTerminal(workspace,delegate(string command){Dictionary<string,object> parent=M(archiveTerminal.GetStatus()["job"]);Directory.CreateDirectory(Path.Combine(workspace,"sequence-reports",(string)parent["currentChildJobId"]+"-sequence-latest.txt"));return D("ok",true,"result",D(),"status",D());},delegate(){return D();}))
        {
            Configure(archiveTerminal);int purges=0;archiveTerminal.SetFileCleanup(delegate(Dictionary<string,object> cleanupFile){purges++;});archiveTerminal.RunFolder(folder,true,true,false,10240);Dictionary<string,object> final=Finished(archiveTerminal);Dictionary<string,object> file=Rows(final["files"])[0];Check((string)final["phase"]=="completed" &&N(final["completedCommands"])==1 &&N(final["completedFiles"])==1,"Archive failure must retain calculation completion");Check((string)file["reportPhase"]=="failed" &&(string)file["purgePhase"]=="skipped-report-failure" &&purges==0,"Failed archive must prevent purge and surface warning");using(FileStream report=archiveTerminal.OpenSequencePrintout((string)file["childJobId"],"latest")){Check(CountReports(report)==1,"Failed combined archive must preserve individual reports");}
        }
        Console.WriteLine("PASS independent archive and purge failure outcomes");
    }
    private static void Cancellation(string root)
    {
        string folder=SingleInput(root,"cancel",128),workspace=Path.Combine(root,"cancel-work");Directory.CreateDirectory(workspace);ManualResetEvent started=new ManualResetEvent(false);int calls=0;
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){Interlocked.Increment(ref calls);started.Set();Thread.Sleep(30);return D("ok",true,"result",D(),"status",D());},delegate(){return D();}))
        {
            Configure(terminal);terminal.RunFolder(folder,true,false,false,10240);Check(started.WaitOne(5000),"Cancellation test must start");Dictionary<string,object> running=M(terminal.GetStatus()["job"]);Check(M(running["currentFileJob"])["id"].Equals(running["currentChildJobId"]),"Active child must have separate checkpoint identity");Check(Rows(running["records"]).Count==0 &&Rows(M(running["currentFileJob"])["records"]).Count>=1,"Parent may only expose records under currentFileJob");terminal.Cancel();Dictionary<string,object> final=Finished(terminal);Check((string)final["phase"]=="cancelled" &&calls<128 &&N(final["completedCommands"])==calls,"Cancelled partial native work must retain exact aggregate count");Check(N(final["totalCommands"])==128 &&(bool)final["totalCommandsKnown"],"Discovered totals must survive cancellation");using(FileStream report=terminal.OpenSequencePrintout(Id(final),"latest")){Check(CountReports(report)==calls,"Cancellation must retain all accepted reports");}
        }
        using(SequenceTerminal restored=new SequenceTerminal(workspace,delegate(string command){throw new Exception("Must not resume");},delegate(){return D();})){Dictionary<string,object> job=M(restored.GetStatus()["job"]);Check((string)job["phase"]=="cancelled" &&N(job["totalFiles"])==1 &&N(job["totalCommands"])==128,"Cancellation checkpoint must preserve planned file count");}
        Console.WriteLine("PASS cancellation accounting, child identity and durable partial export");
    }
    private static void Limits(string root)
    {
        string folder=SingleInput(root,"limit",129),workspace=Path.Combine(root,"limit-work");Directory.CreateDirectory(workspace);int calls=0;
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){calls++;return D("ok",true,"result",D());},delegate(){return D();}))
        {
            Configure(terminal);terminal.RunFolder(folder,true,false,false,10240);Dictionary<string,object> final=Finished(terminal);Check((string)final["phase"]=="failed" &&calls==0 &&N(final["completedCommands"])==0 &&!(bool)final["totalCommandsKnown"],"Oversize input must fail preflight without dispatch or fabricated totals");Throws(delegate(){terminal.RunFolder("\\\\server\\share",true,false,false,10240);},"local-drive");Throws(delegate(){terminal.RunFolder("D:relative",true,false,false,10240);},"local-drive");
        }
        string state=Path.Combine(workspace,"spindoctor-sequences.json");Dictionary<string,object> saved=M(Json.DeserializeObject(File.ReadAllText(state)));string latest=(string)saved["latestJobId"],jobPath=Path.Combine(workspace,"sequence-jobs",latest+".json");Dictionary<string,object> job=M(Json.DeserializeObject(File.ReadAllText(jobPath)));job["id"]=new string('0',32);File.WriteAllText(jobPath,Json.Serialize(job));Throws(delegate(){using(SequenceTerminal rejected=new SequenceTerminal(workspace,delegate(string command){return D();},delegate(){return D();})){}},"identity mismatch");
        File.WriteAllText(state,new string('x',33554433));Throws(delegate(){using(SequenceTerminal rejected=new SequenceTerminal(workspace,delegate(string command){return D();},delegate(){return D();})){}},"checkpoint exceeds");
        Console.WriteLine("PASS per-file command bounds, absolute-local paths and bounded checkpoint validation");
    }
    private static void InterruptedCheckpoint(string root)
    {
        string folder=SingleInput(root,"interrupt",2),workspace=Path.Combine(root,"interrupt-work"),copy=Path.Combine(root,"interrupt-copy");Directory.CreateDirectory(workspace);ManualResetEvent second=new ManualResetEvent(false),release=new ManualResetEvent(false);int calls=0;
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){if(Interlocked.Increment(ref calls)==2){second.Set();if(!release.WaitOne(10000))throw new Exception("Interruption fixture timed out");}return D("ok",true,"command",command,"result",D(),"status",D());},delegate(){return D();}))
        {
            Configure(terminal);terminal.RunFolder(folder,true,false,false,10240);Check(second.WaitOne(5000),"Second command must pause after first checkpoint");Dictionary<string,object> running=M(terminal.GetStatus()["job"]);string jobId=Id(running);Dictionary<string,object> disk=M(Json.DeserializeObject(File.ReadAllText(Path.Combine(workspace,"sequence-jobs",jobId+".json"))));Check(N(disk["completedCommands"])==1 &&N(running["completedCommands"])==1,"Parent disk checkpoint must include active child completion");
            Directory.CreateDirectory(copy);Directory.CreateDirectory(Path.Combine(copy,"sequence-jobs"));Directory.CreateDirectory(Path.Combine(copy,"sequence-reports"));File.Copy(Path.Combine(workspace,"spindoctor-sequences.json"),Path.Combine(copy,"spindoctor-sequences.json"));foreach(string file in Directory.GetFiles(Path.Combine(workspace,"sequence-jobs")))File.Copy(file,Path.Combine(copy,"sequence-jobs",Path.GetFileName(file)));foreach(string file in Directory.GetFiles(Path.Combine(workspace,"sequence-reports")))File.Copy(file,Path.Combine(copy,"sequence-reports",Path.GetFileName(file)));
            terminal.Cancel();release.Set();Finished(terminal);
            using(SequenceTerminal restored=new SequenceTerminal(copy,delegate(string command){throw new Exception("Interrupted work must not rerun");},delegate(){return D();}))
            {
                Dictionary<string,object> job=M(restored.GetStatus()["job"]);Check((string)job["phase"]=="interrupted" &&!(bool)job["active"] &&N(job["completedCommands"])==1 &&N(job["totalCommands"])==2,"Interrupted job must restore exact aggregate progress without replay");Dictionary<string,object> file=Rows(job["files"])[0];Check((string)file["executionPhase"]=="interrupted" &&N(file["completedCommands"])==1,"Interrupted file must retain honest partial native progress");using(FileStream report=restored.OpenSequencePrintout(jobId,"latest")){Check(CountReports(report)==1,"Interrupted export must retain completed return and skip absent in-flight report");}
            }
        }
        Console.WriteLine("PASS process-interruption checkpoint propagation and safe partial recovery");
    }
    public static void QuotedFolderFields(string root)
    {
        string folder=SingleInput(root,"quoted  spaces",1),workspace=Path.Combine(root,"quoted-work");Directory.CreateDirectory(workspace);
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){return D("ok",true,"result",D());},delegate(){return D();}))
        {
            Configure(terminal);string command="q.folder run PATH=\""+folder+"\" DOWNLOADS=false PURGE=false VERIFY=false";terminal.Execute(command.Split(new char[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries),command);Dictionary<string,object> final=Finished(terminal);Check((string)final["phase"]=="completed" &&(string)final["folderPath"]==Path.GetFullPath(folder),"Quoted folder path must preserve repeated spaces and case-insensitive fields");Check(!(bool)final["downloads"] &&!(bool)final["purge"],"Explicit uppercase boolean fields must survive parser");string bad=command+" ignored";Throws(delegate(){terminal.Execute(bad.Split(new char[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries),bad);},"field syntax");
        }
        Console.WriteLine("PASS exact quoted paths and strict case-insensitive folder fields; assertions=3");
    }
    public static void AsyncIdentityFailure(string root)
    {
        string workspace=Path.Combine(root,"async-identity-work");Directory.CreateDirectory(workspace);string acceptedId=new string('a',32),replacedId=new string('b',32),details=new string('z',12000);int calls=0;
        Dictionary<string,object> status=D("lastVerificationJob",D("id",replacedId,"active",false,"phase","completed","passed",true));
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){calls++;return D("ok",true,"command",command,"result",D("queued",true,"job",D("id",acceptedId,"active",true,"phase","queued"),"acceptedDetails",details),"status",status);},delegate(){return status;}))
        {
            terminal.Run(new string[]{"q.verify samples 1 errors-only","q.status"},null,null);Dictionary<string,object> job=Finished(terminal);Check((string)job["phase"]=="failed" &&calls==1 &&N(job["completedCommands"])==0,"Wrong async identity must stop dependencies without claiming completion");Dictionary<string,object> record=Rows(job["records"])[0];Check((string)record["phase"]=="failed" &&(string)record["failureScope"]=="sequence-orchestration" &&!(bool)record["nativeCompletionKnown"],"Await failure must distinguish orchestration failure from unknown native outcome");
            Dictionary<string,object> full=M(Json.DeserializeObject(terminal.SequenceJson(Id(job),0,"latest")));Check((string)M(M(full["response"])["result"])["acceptedDetails"]==details,"Failure archive must preserve original large accepted response rather than bounded preview");Dictionary<string,object> final=M(full["finalResponse"]);Check(!(bool)final["nativeCompletionKnown"] &&(string)M(final["result"])["awaitedJobId"]==acceptedId,"Failure response must retain genuine awaited identity and unknown completion");Check(((string)final["error"]).IndexOf("replaced",StringComparison.OrdinalIgnoreCase)>=0,"Failure report must preserve genuine matching error");Check(terminal.SequencePrintout(Id(job),0,"latest").Contains("NATIVE COMPLETION IS UNKNOWN"),"TXT failure heading must not fabricate matched backend completion");Dictionary<string,object> accepted=M(Json.DeserializeObject(terminal.SequenceJson(Id(job),0,"accepted")));Check((bool)M(accepted["response"])["ok"] &&!accepted.ContainsKey("finalResponse"),"Accepted-stage report must remain immutable");
        }
        Console.WriteLine("PASS async identity mismatch, dependency barrier and honest durable orchestration failure; assertions=7");
    }
    public static void LongHttpStatus(string root)
    {
        LongStatus(root,true);
    }
    public static void LongSerializedStatus(string root)
    {
        LongStatus(root,false);
    }
    public static void DetailedTextReports(string root)
    {
        string folder=SingleInput(root,"details",1),workspace=Path.Combine(root,"details-work");Directory.CreateDirectory(workspace);
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){return D("ok",true,"command",command,"elapsedMs",0,"result",D("norm",13),"status",D("actualCpuStates","0","measurementShots","0","quantumHardwareGates","0"));},delegate(){return D();}))
        {
            Configure(terminal);terminal.SetFileCleanup(delegate(Dictionary<string,object> cleanupFile){throw new IOException("Details fixture: requested purge could not finish");});terminal.RunFolder(folder,true,true,false,10240);Dictionary<string,object> final=Finished(terminal);string childId=(string)Rows(final["files"])[0]["childJobId"],report=terminal.SequencePrintout(Id(final),null,"latest");
            Check(report.Contains("SPINDOCTOR 3.1.0 CANDIDATE — DETAILED NATIVE RUN REPORT"),"Folder text must identify report version and meaningful run summary");Check(report.Contains("Successful command barriers: 1 / 1; final command total is known."),"Summary must define and retain truthful completed counts");Check(report.Contains("native execution: completed; full-file TXT archive: archived; transient purge: failed"),"File summary must separate native, archive and purge outcomes");Check(report.Contains("Original source SHA256: "+new string('a',64)) ||report.Contains("original source SHA256: "+new string('a',64)),"Detailed text must carry original source SHA256");Check(report.Contains("Details fixture: requested purge could not finish") &&report.Contains("Warnings: 1"),"A completed calculation with failed purge must retain the genuine warning");Check(report.Contains("READABLE ACCEPTED RESPONSE DETAILS:") &&report.Contains("Key returned results: norm=13") &&report.Contains("COMPLETE ACCEPTED NATIVE RESPONSE:"),"Readable actual returned values must accompany the complete raw accepted response");Check(report.Contains("Recovery: inspect q.sequence status") &&report.Contains("No automatic replay"),"Report must explain safe recovery without repeating native work");
            Dictionary<string,object> child=M(M(Json.DeserializeObject(terminal.SequencePreviewJson(childId)))["job"]);Check((string)child["parentJobId"]==Id(final) &&(string)child["sourceSha256"]==new string('a',64) &&(string)child["sourceFilename"]=="001.txt","Child checkpoint must retain parent/source provenance");
            using(FileStream stream=terminal.OpenSequencePrintout(Id(final),"latest")){using(StreamReader reader=new StreamReader(stream,new UTF8Encoding(false,true))){Check(reader.ReadToEnd()==report,"Bounded and streamed detailed folder reports must agree for an immutable completed snapshot");}}
            Dictionary<string,object> native=M(Json.DeserializeObject(terminal.SequenceJson(childId,0,"accepted")));string raw=Json.Serialize(native["response"]);Check(report.Contains(raw),"Adding readable details must preserve the original full native JSON");Check((string)M(M(native["response"])["status"])["actualCpuStates"]=="0" &&N(M(native["response"])["elapsedMs"])==0 &&report.Contains("actualCpuStates=0") &&report.Contains("Native response elapsedMs=0"),"Explicit zero values and observed zero timing must survive unchanged in raw and readable details");
        }
        Dictionary<string,object> missing=D("id",new string('a',32),"phase","failed","active",false,"completedCommands",0,"totalCommands",1,"totalCommandsKnown",false,"records",new object[]{D("phase","failed","nativeCompletionKnown",false)},"error","Genuine awaited identity was not matched");StringWriter text=new StringWriter(CultureInfo.InvariantCulture);typeof(SequenceTerminal).GetMethod("WriteRunSummary",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{text,missing,"latest"});typeof(SequenceTerminal).GetMethod("WriteFileSummary",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{text,D("phase","failed","executionPhase","failed","completedCommands",0,"totalCommands",0,"totalCommandsKnown",false)});string unknown=text.ToString();Check(unknown.Contains("Observed run wall time: unknown") &&unknown.Contains("Run stopped UTC: unknown"),"Missing timing must be unknown rather than zero or fabricated completion");Check(unknown.Contains("Successful command barriers: 0 / 1; discovered commands only") &&unknown.Contains("0 / 0; discovered commands only — compilation did not establish a final command total."),"Recorded zero and unknown final requested run/file totals must be distinguished");Check(unknown.Contains("1 step(s) have unknown native completion") &&unknown.Contains("no backend outcome is fabricated"),"Orchestration failure must preserve unknown backend completion");
        Console.WriteLine("PASS readable run/file outcomes, source provenance, exact raw JSON, zero/unknown values and streamed summary consistency; assertions=14");
    }
    private static void LongStatus(string root,bool useHttp)
    {
        string workspace=Path.Combine(root,"long-status-work");Directory.CreateDirectory(workspace);File.WriteAllText(Path.Combine(workspace,"QUANTUM_TERMINAL.html"),"<!doctype html><title>Isolated status regression</title>");
        using(TerminalHost host=new TerminalHost(workspace,false))
        {
            FieldInfo field=typeof(TerminalSession).GetField("sequences",BindingFlags.NonPublic|BindingFlags.Instance);((SequenceTerminal)field.GetValue(host.Session)).Dispose();
            SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){return D("ok",true,"command",command,"result",D("retainedPreview",new string('v',7000)),"status",D());},delegate(){return D();});terminal.SetCapabilityResolver(delegate(string command){return D("available",true);});field.SetValue(host.Session,terminal);
            StringBuilder text=new StringBuilder("q.math norm vector=[");for(int i=0;i<96;i++){if(i!=0)text.Append(',');text.Append("0.12345678901234567890123456789012345678");}text.Append(']');string commandText=text.ToString();Check(commandText.Length<=4096 &&commandText.Length>3500,"Long status fixture must use allowed command size");string[] commands=new string[128];for(int i=0;i<commands.Length;i++)commands[i]=commandText;terminal.Run(commands,null,null);Dictionary<string,object> final=Finished(terminal);Check((string)final["phase"]=="completed" &&N(final["completedCommands"])==128,"Long valid commands must retain all completed records");
            object status=host.Session.GetStatus();string serialized=host.Session.Serialize(D("ok",true,"status",status));JavaScriptSerializer responseEncoder=(JavaScriptSerializer)typeof(TerminalHost).GetField("json",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host);string hosted=responseEncoder.Serialize(D("ok",true,"status",status));Check(Encoding.UTF8.GetByteCount(serialized)>2097152,"Long status must exercise response larger than old 2 MiB cap");Check(hosted==serialized,"Host and session serializers must both preserve the same large valid response");
            if(!useHttp){Dictionary<string,object> response=M(Json.DeserializeObject(hosted));Check(Rows(M(M(M(response["status"])["sequence"])["job"])["records"]).Count==128,"Large response must preserve all 128 command records");Console.WriteLine("PASS >2 MiB host/session status serialization for128 maximum-length command records; assertions=5; bytes="+Encoding.UTF8.GetByteCount(hosted));return;}
            Thread server=new Thread(host.Serve);server.IsBackground=true;server.Start();
            try
            {
                string response;using(TcpClient client=new TcpClient("127.0.0.1",host.Port)){client.ReceiveTimeout=30000;client.SendTimeout=30000;using(NetworkStream stream=client.GetStream()){byte[] request=Encoding.ASCII.GetBytes("GET /api/status HTTP/1.1\r\nHost: 127.0.0.1:"+host.Port.ToString(CultureInfo.InvariantCulture)+"\r\nAuthorization: Bearer "+host.Token+"\r\nConnection: close\r\n\r\n");stream.Write(request,0,request.Length);using(StreamReader reader=new StreamReader(stream,new UTF8Encoding(false,true))){response=reader.ReadToEnd();}}}
                int split=response.IndexOf("\r\n\r\n",StringComparison.Ordinal);Check(response.StartsWith("HTTP/1.1 200 "),"Long status HTTP response must succeed: "+response.Substring(0,Math.Min(response.Length,180)));string body=response.Substring(split+4);Check(Encoding.UTF8.GetByteCount(body)>2097152,"Long status must exercise response larger than old 2 MiB cap");Dictionary<string,object> data=M(Json.DeserializeObject(body)),job=M(M(M(data["status"])["sequence"])["job"]);Check((bool)data["ok"] &&Rows(job["records"]).Count==128 &&N(job["completedCommands"])==128,"Large authenticated HTTP status must preserve every bounded record");
            }
            finally{host.Stop();Check(server.Join(5000),"Status server must stop cleanly");}
        }
        Console.WriteLine("PASS authenticated >2 MiB HTTP status for128 maximum-length command records; assertions=8");
    }
    public static void ReportByteBudget(string root)
    {
        string workspace=Path.Combine(root,"report-byte-budget");Directory.CreateDirectory(workspace);const string command="q.math norm vector=[5,12]";string payload="";
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string text){return D("ok",true,"command",text,"result",D("payload",payload),"status",D());},delegate(){return D();}))
        {
            terminal.SetCapabilityResolver(delegate(string text){return D("available",true);});Dictionary<string,object> response=D("ok",true,"command",command,"result",D("payload",""),"status",D()),full=D("sequenceId",new string('a',32),"index",0,"command",command,"normalizedCommand",command,"receivedUtc","2026-10-10T00:00:00.000Z","completedUtc","2026-10-10T00:00:00.000Z","response",response);MethodInfo writer=typeof(SequenceTerminal).GetMethod("ReportText",BindingFlags.NonPublic|BindingFlags.Instance);string small=(string)writer.Invoke(terminal,new object[]{full});int start=small.IndexOf("READABLE ACCEPTED RESPONSE DETAILS:",StringComparison.Ordinal),end=small.IndexOf("COMPLETE ACCEPTED NATIVE RESPONSE:",StringComparison.Ordinal);string baseline=small.Remove(start,end-start);int characters=(33554432-Encoding.UTF8.GetByteCount(baseline)-16)/3;payload=new string('\u6f22',characters);
            terminal.Run(new string[]{command},null,null);Dictionary<string,object> job=Finished(terminal);string report=terminal.SequencePrintout(Id(job),0,"accepted"),native=terminal.SequenceJson(Id(job),0,"accepted");Dictionary<string,object> archived=M(Json.DeserializeObject(native));string accepted=Json.Serialize(archived["response"]);string file=(string)Rows(job["records"])[0]["reportFile"];long bytes=new FileInfo(Path.Combine(workspace,"sequence-reports",file)).Length;
            Check((string)job["phase"]=="completed" &&N(job["completedCommands"])==1 &&bytes<=33554432 &&bytes>33550000 &&report.Contains("Readable details omitted to preserve exact native returns") &&report.Contains(accepted) &&(string)M(M(archived["response"])["result"])["payload"]==payload,"Near-limit Unicode native return must complete, preserve exact TXT/JSON, and omit only optional detail within the 32 MiB UTF-8 archive budget");
        }
        Console.WriteLine("PASS near-limit Unicode native TXT/JSON preserved without readable-detail byte overflow; assertions=1");
    }
    public static void Run(string root,string pack){Directory.CreateDirectory(root);LargeFolder(root,pack);AuxiliaryFailures(root);Cancellation(root);InterruptedCheckpoint(root);QuotedFolderFields(root);AsyncIdentityFailure(root);DetailedTextReports(root);ReportByteBudget(root);Limits(root);Console.WriteLine("FOLDER REGRESSIONS PASSED; assertions="+checks);}
}
