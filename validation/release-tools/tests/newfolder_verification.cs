// SPDX-License-Identifier: GPL-3.0-or-later
// SpinDoctor 3.1.2 - Quantum Virtual Terminal
// Copyright (C) 2026 RUSSELL PHILIP SMITHSON <russellsmithson77@outlook.com>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;
public static class NewFolderVerification
{
    static int checks;
    static Dictionary<string,object> lastVerification;
    static JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
    static Dictionary<string,object> D(params object[] p){Dictionary<string,object> d=new Dictionary<string,object>();for(int i=0;i<p.Length;i+=2)d.Add((string)p[i],p[i+1]);return d;}
    static Dictionary<string,object> M(object v){return (Dictionary<string,object>)v;}
    static List<Dictionary<string,object>> Rows(object value){List<Dictionary<string,object>> r=new List<Dictionary<string,object>>();foreach(object o in (IEnumerable)value)r.Add(M(o));return r;}
    static void Check(bool condition,string why){checks++;if(!condition)throw new Exception(why);}
    static void Throws(Action action,string text){try{action();}catch(Exception e){Check(e.Message.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0,"unexpected rejection "+e.Message);return;}throw new Exception("missing rejection "+text);}
    static Dictionary<string,object> Finish(SequenceTerminal t){DateTime end=DateTime.UtcNow.AddSeconds(90);for(;;){Dictionary<string,object> j=M(t.GetStatus()["job"]);if(!(bool)j["active"])return j;if(DateTime.UtcNow>end)throw new Exception("timeout");Thread.Sleep(5);}}
    static string Inputs(string root){string p=Path.Combine(root,"inputs");Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"one.txt"),"q.math norm amplitudes=[5,12]\r\n",new UTF8Encoding(false));return p;}
    static void Configure(SequenceTerminal t){t.SetCapabilityResolver(delegate(string c){return D("available",true);});t.SetFileCompiler(delegate(string p){return D("commands",new string[]{"q.math norm amplitudes=[5,12]"},"sourceSha256",new string('c',64),"sourceFormat","txt-sequence","inputName","one.txt");});}
    static Dictionary<string,object> Status(){return D("descriptorHash",new string('a',64),"hasPrepared",true,"migrationT",1,"modelGeneration","1","gpuAvailable",true,"gpuDevice","test mock; no physical GPU executed","actualCpuStates","0","actualGpuStates","0","verifiedSamples","0","lastVerificationJob",lastVerification);}
    static Dictionary<string,object> Result(long request)
    {
        long planned=request+1;List<object> preview=new List<object>();for(int i=0;i<Math.Min(32,planned);i++)preview.Add(D("address",i.ToString(CultureInfo.InvariantCulture),"cpu",new object[]{1d,0d,0d,0d,0d,0d,1d,1d,0d},"gpu",new object[]{1d,0d,0d,0d,0d,0d,1d,1d,0d},"maxError",0d,"normError",0d,"spinError",0d,"passed",true,"tolerance",2E-09));
        string digest;using(MemoryStream stream=new MemoryStream()){for(ulong index=0;index<(ulong)preview.Count;index++)for(int shift=56;shift>=0;shift-=8)stream.WriteByte((byte)(index>>shift));using(SHA256 sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();}
        string count=planned.ToString(CultureInfo.InvariantCulture);return D("id",Guid.NewGuid().ToString("N"),"active",false,"phase","completed","passed",true,"requestedSamples",request.ToString(CultureInfo.InvariantCulture),"plannedSamples",count,"completedSamples",count,"verifiedCount",count,"cpuStates",count,"gpuStates",count,"sampleCount",count,"errorCount","0","descriptorHash",new string('a',64),"stateCount","500000000000000","samplePreviewAddressDigest",digest,"failure","","checkpointError","","tolerance",2E-09,"maxError",0d,"samplePreview",preview.ToArray(),"samplePreviewLimit",32,"omittedSampleRows",(planned-preview.Count).ToString(CultureInfo.InvariantCulture),"receivedUtc",DateTime.UtcNow.ToString("o"),"completedUtc",DateTime.UtcNow.ToString("o"),"elapsedMs",.1);
    }
    static object Return(string c,Dictionary<string,object> r){if(c.StartsWith("q.verify samples ") &&r.ContainsKey("id"))lastVerification=r;return D("ok",true,"command",c,"result",r,"status",Status());}
    static void Passed(string root)
    {
        string input=Inputs(root),ws=Path.Combine(root,"workspace"),id="",child="",evidence="";int inputCalls=0,verifyCalls=0,purges=0;
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){if(c.StartsWith("q.verify samples ")){verifyCalls++;return Return(c,Result(10240));}inputCalls++;return Return(c,D("norm",13));},delegate(){return Status();}))
        {
            Configure(t);t.SetFileCleanup(delegate(Dictionary<string,object> cleanup){Check((string)cleanup["verificationPhase"]=="passed" &&(string)cleanup["reportPhase"]=="archived","purge before verification/archive");purges++;});t.RunFolder(input,true,true);Dictionary<string,object> j=Finish(t);id=(string)j["id"];Dictionary<string,object> f=Rows(j["files"])[0];child=(string)f["childJobId"];
            Check((string)j["phase"]=="completed" &&(bool)j["receiptReady"],"default verified parent completion");Check(inputCalls==1 &&verifyCalls==1 &&purges==1,"dispatch/accounting separation");Check(Convert.ToInt32(j["completedCommands"])==1 &&Convert.ToInt32(j["totalCommands"])==1 &&(string)t.GetStatus()["executedCommands"]=="1","verifier changed input counts");
            Dictionary<string,object> meta=t.SequenceReceiptMetadata(child),v=M(meta["verification"]);evidence=(string)v["evidenceFile"];Check((bool)meta["receiptReady"] &&(string)meta["verificationPhase"]=="passed","child verified metadata");Check(Rows(M(M(v["response"])["result"])["samplePreview"]).Count==32,"complete actual child preview");Check(Rows(M(M(M(f["verification"])["response"])["result"])["samplePreview"]).Count==4,"bounded parent preview");
            Check(DateTime.Parse((string)v["completedUtc"],null,DateTimeStyles.RoundtripKind)<=DateTime.Parse((string)f["reportArchivedUtc"],null,DateTimeStyles.RoundtripKind) &&DateTime.Parse((string)f["reportArchivedUtc"],null,DateTimeStyles.RoundtripKind)<=DateTime.Parse((string)f["purgeCompletedUtc"],null,DateTimeStyles.RoundtripKind),"verification/archive/purge timestamp order");
            string text=t.SequencePrintout(child,null);Check(text.Contains("NATIVE PROCEDURAL CPU/GPU SAMPLE VERIFICATION PASSED") &&text.Contains("not independent certification of q.math"),"scope stamp");using(FileStream s=t.OpenSequencePrintout(id,"latest")){Check(s.Length>0,"verified stream");}
        }
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){throw new Exception("must not replay");},delegate(){return Status();})){Check((bool)t.SequenceReceiptMetadata(id)["receiptReady"],"restart lost parent proof");Check((bool)t.SequenceReceiptMetadata(child)["receiptReady"],"restart lost child proof");File.AppendAllText(Path.Combine(ws,"sequence-reports",evidence)," ");Throws(delegate(){t.SequenceReceiptMetadata(child);},"hash mismatch");}
    }
    static void Rejected(string root,string mode)
    {
        string input=Inputs(root);int purges=0;
        using(SequenceTerminal t=new SequenceTerminal(Path.Combine(root,"workspace"),delegate(string c){if(!c.StartsWith("q.verify samples "))return Return(c,D("norm",13));Dictionary<string,object> r=Result(10240);if(mode=="count")r["gpuStates"]="0";if(mode=="gpu"){r["passed"]=false;r["phase"]="failed";r["failure"]="No actual FP64 OpenCL GPU";}if(mode=="preview")r["samplePreview"]=new object[0];if(mode=="mismatch"){Dictionary<string,object> row=Rows(r["samplePreview"])[0];row["gpu"]=new object[]{0d,0d,0d,0d,0d,0d,1d,1d,0d};}if(mode=="descriptor")r["descriptorHash"]=new string('b',64);if(mode=="stale")r["id"]=lastVerification["id"];if(mode=="address")Rows(r["samplePreview"])[0]["address"]=r["stateCount"];if(mode=="digest")r["samplePreviewAddressDigest"]=new string('0',64);return Return(c,r);},delegate(){return Status();}))
        {
            Configure(t);t.SetFileCleanup(delegate(Dictionary<string,object> cleanup){purges++;});t.RunFolder(input,true,true);Dictionary<string,object> j=Finish(t),f=Rows(j["files"])[0];Check((string)j["phase"]=="failed" &&!(bool)j["receiptReady"],mode+" must fail parent");Check(Convert.ToInt32(j["completedCommands"])==1 &&(string)f["executionPhase"]=="completed",mode+" must retain successful input calculation");Check(purges==0 &&(string)f["reportPhase"]=="withheld-verification" &&!(bool)f["receiptReady"],mode+" must withhold receipt/purge");string child=(string)f["childJobId"];Check((string)t.SequenceReceiptMetadata(child)["receiptKind"]=="diagnostic-unverified","failed diagnostic metadata");Check(t.SequencePrintout(child,null).Contains("DIAGNOSTIC / UNVERIFIED"),"failed diagnostic stamp");Check(File.Exists(Path.Combine(root,"workspace","sequence-reports",(string)M(f["verification"])["evidenceFile"])),"failed genuine evidence retained");
        }
    }
    static void PendingCancel(string root)
    {
        string input=Inputs(root);ManualResetEvent entered=new ManualResetEvent(false),release=new ManualResetEvent(false);int purges=0;
        using(SequenceTerminal t=new SequenceTerminal(Path.Combine(root,"workspace"),delegate(string c){if(c.StartsWith("q.verify samples ")){entered.Set();release.WaitOne();return Return(c,Result(10240));}return Return(c,D("norm",13));},delegate(){return Status();}))
        {
            Configure(t);t.SetFileCleanup(delegate(Dictionary<string,object> f){purges++;});t.RunFolder(input,true,true);try{Check(entered.WaitOne(30000),"verifier not reached within fixture timeout");Dictionary<string,object> j=M(t.GetStatus()["job"]);string child=(string)j["currentChildJobId"],parent=(string)j["id"];
            Check(Convert.ToInt32(j["completedCommands"])==1 &&!(bool)j["receiptReady"],"pending verification double-counted input");Throws(delegate(){t.SequenceReceiptMetadata(child);},"withheld");Throws(delegate(){t.SequencePrintout(child,0);},"withheld");Throws(delegate(){t.SequenceJson(child,0);},"withheld");Throws(delegate(){using(FileStream s=t.OpenSequencePrintout(parent,"latest")){}},"withheld");Check(t.SequencePreviewJson(child).Contains("verifying"),"pending metadata unavailable");t.Cancel();release.Set();j=Finish(t);Check((string)j["phase"]=="cancelled" &&purges==0 &&Convert.ToInt32(j["completedCommands"])==1,"cancel must preserve input and skip purge");Check((string)t.SequenceReceiptMetadata(child)["receiptKind"]=="diagnostic-unverified","cancel diagnostic");}finally{release.Set();}
        }
    }
    static void Async(string root)
    {
        string input=Inputs(root),nativeId=Guid.NewGuid().ToString("N");bool begun=false;int polls=0;
        using(SequenceTerminal t=new SequenceTerminal(Path.Combine(root,"workspace"),delegate(string c){if(c.StartsWith("q.verify samples ")){begun=true;Dictionary<string,object> j=Result(16384);j["id"]=nativeId;j["active"]=true;j["phase"]="queued";return Return(c,D("queued",true,"job",j));}return Return(c,D("norm",13));},delegate(){Dictionary<string,object> s=Status();if(begun){Dictionary<string,object> j=Result(16384);j["id"]=nativeId;if(++polls<3){j["active"]=true;j["phase"]="running";}s["lastVerificationJob"]=j;}return s;}))
        {Configure(t);t.RunFolder(input,false,false,true,16384);Dictionary<string,object> j=Finish(t);Check((string)j["phase"]=="completed" &&(bool)j["receiptReady"] &&polls>=3,"maximum request async barrier");Check(Convert.ToInt32(j["completedCommands"])==1,"async verification not input command");}
    }
    static void OptoutAndFields(string root)
    {
        string input=Inputs(root);int verify=0;
        using(SequenceTerminal t=new SequenceTerminal(Path.Combine(root,"workspace"),delegate(string c){if(c.StartsWith("q.verify"))verify++;return Return(c,D("norm",13));},delegate(){return Status();}))
        {
            Configure(t);foreach(string tail in new string[]{"samples=0","samples=16384000001","samples=1.5","samples=-1","samples=2 samples=3","verify=true verify=false","verify=maybe"}){string c="q.folder run path=\""+input+"\" "+tail;Throws(delegate(){t.Execute(c.Split(' '),c);},tail.StartsWith("samples") &&!tail.Contains(" samples=") ?"samples":tail.Contains(" ") ?"Duplicate":"Boolean");}Check(verify==0 &&t.GetStatus()["job"]==null,"invalid fields executed work");t.RunFolder(input,false,false,false,10240);Dictionary<string,object> j=Finish(t);Check((string)j["phase"]=="completed" &&!(bool)j["verificationRequired"] &&verify==0,"explicit optout");Check(t.SequencePrintout((string)j["id"],null).Contains("UNVERIFIED — NATIVE SAMPLE VERIFICATION NOT REQUIRED"),"optout not labeled");
        }
    }
    static void InterruptedAndLegacy(string root)
    {
        string input=Inputs(root),ws=Path.Combine(root,"workspace"),parent="",child="";
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){return c.StartsWith("q.verify") ?Return(c,Result(10240)):Return(c,D("norm",13));},delegate(){return Status();})){Configure(t);t.RunFolder(input,false,false);Dictionary<string,object> j=Finish(t);parent=(string)j["id"];child=(string)Rows(j["files"])[0]["childJobId"];}
        string parentPath=Path.Combine(ws,"sequence-jobs",parent+".json"),childPath=Path.Combine(ws,"sequence-jobs",child+".json");Dictionary<string,object> saved=M(json.DeserializeObject(File.ReadAllText(parentPath)));saved["active"]=true;saved["phase"]="verifying";saved["receiptReady"]=false;saved["verificationPhase"]="verifying";Dictionary<string,object> file=Rows(saved["files"])[0];file["verificationPhase"]="verifying";file["receiptReady"]=false;File.WriteAllText(parentPath,json.Serialize(saved));Dictionary<string,object> savedChild=M(json.DeserializeObject(File.ReadAllText(childPath)));savedChild["verificationPhase"]="verifying";savedChild["receiptReady"]=false;File.WriteAllText(childPath,json.Serialize(savedChild));
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){throw new Exception("no replay");},delegate(){return Status();})){Dictionary<string,object> j=M(t.GetStatus()["job"]);Check((string)j["phase"]=="interrupted" &&Convert.ToInt32(j["completedCommands"])==1,"interruption lost completed input count");Check((string)t.SequenceReceiptMetadata(child)["verificationPhase"]=="interrupted" &&(string)t.SequenceReceiptMetadata(child)["receiptKind"]=="diagnostic-unverified","interrupted child evidence fabricated completion");Check(t.SequenceJson(parent,null).Contains("interrupted"),"interrupted diagnostic JSON unavailable");}
        foreach(string path in new string[]{parentPath,childPath}){Dictionary<string,object> old=M(json.DeserializeObject(File.ReadAllText(path)));old["active"]=false;old["phase"]="completed";foreach(string key in new string[]{"verificationRequired","verificationSamples","verificationPhase","verificationError","verification","receiptReady"})old.Remove(key);foreach(Dictionary<string,object> oldFile in Rows(old["files"]))foreach(string key in new string[]{"verificationRequired","verificationSamples","verificationPhase","verificationError","verification","receiptReady"})oldFile.Remove(key);File.WriteAllText(path,json.Serialize(old));}
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){throw new Exception("no replay");},delegate(){return Status();})){Check(!(bool)t.SequenceReceiptMetadata(parent)["verificationRequired"] &&(string)t.SequenceReceiptMetadata(parent)["verificationPhase"]=="legacy-unverified","legacy falsely upgraded toverified");Check(t.SequencePrintout(child,null).Contains("UNVERIFIED"),"legacy stamp");}
    }
    static void MaximumFiles(string root)
    {
        string input=Path.Combine(root,"inputs");Directory.CreateDirectory(input);for(int i=0;i<128;i++)File.WriteAllText(Path.Combine(input,i.ToString("000")+".txt"),"q.math norm amplitudes=[5,12]");
        using(SequenceTerminal t=new SequenceTerminal(Path.Combine(root,"workspace"),delegate(string c){return c.StartsWith("q.verify") ?Return(c,Result(10240)):Return(c,D("norm",13));},delegate(){return Status();}))
        {Configure(t);t.RunFolder(input,false,false);Dictionary<string,object> j=Finish(t);Check((string)j["phase"]=="completed" &&Convert.ToInt32(j["completedCommands"])==128,"maximum files gate");int bytes=Encoding.UTF8.GetByteCount(json.Serialize(j));Check(bytes<2097152,"parent preview exceeded2MiB: "+bytes);Console.WriteLine("Maximum128-file parent metadata bytes: "+bytes);}
    }
    static void PreviewAnswers(string root)
    {
        string ws=Path.Combine(root,"workspace");Dictionary<string,object> status=Status();status["capabilityCatalog"]=new string('x',20000);
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){return D("ok",true,"command",c,"result",c.Contains("bloch") ?D("bloch",new object[]{0d,0d,-1d},"namespace","q.math","action","bloch"):D("norm",13d,"normSquared",169d,"normalized",false,"namespace","q.math","action","norm"),"status",status);},delegate(){return status;}))
        {
            t.SetCapabilityResolver(delegate(string c){return D("available",true);});t.Run(new string[]{"q.math norm amplitudes=[5,12]","q.math bloch amplitudes=[0,1]"},null,null);Dictionary<string,object> j=Finish(t);List<Dictionary<string,object>> records=Rows(j["records"]);Dictionary<string,object> nr=M(records[0]["response"]),br=M(records[1]["response"]);
            Check(Convert.ToDouble(M(nr["result"])["norm"])==13 &&Convert.ToDouble(M(nr["result"])["normSquared"])==169 &&!(bool)M(nr["result"])["normalized"],"oversized status erased actual non-unit norm answer");Check(Convert.ToDouble(((IList)M(br["result"])["bloch"])[2])==-1,"oversized status erased Blochdown");Check((bool)nr["resultComplete"] &&!(bool)nr["resultTruncated"] &&(bool)nr["statusTruncated"] &&!M(nr["result"]).ContainsKey("previewOnly"),"small complete result marked truncated");Check(!M(nr["status"]).ContainsKey("capabilityCatalog"),"status catalog not compacted");
            string text=t.SequencePrintout((string)j["id"],0);Check(text.Contains("SPINDOCTOR 3.1.2") &&text.Contains("COMMAND ANSWERS") &&text.Contains("\"norm\":13") &&!text.Contains("Command 1:"),"command answer first/selected index scope");Check(text.IndexOf("COMMAND ANSWERS")<text.IndexOf("COMPLETE NATIVE COMMAND REPORT"),"answer summary must precede full command/status payload");Check(text.Contains("Execution completion stamp: executionOutcome=completed") &&text.Contains("Recorded sequence command completion barrier"),"known outer completion evidence omitted from human detail");Check(t.SequenceJson((string)j["id"],0).Contains(new string('x',20000)),"unchanged durable native status missing");
        }
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){throw new Exception("no replay");},delegate(){return status;})){Dictionary<string,object> j=M(t.GetStatus()["job"]);Check(Convert.ToDouble(M(M(Rows(j["records"])[0]["response"])["result"])["norm"])==13,"small answer lost after reload");}
        using(SequenceTerminal t=new SequenceTerminal(Path.Combine(root,"large-result"),delegate(string c){return D("ok",true,"command",c,"result",D("large",new string('n',10000),"namespace","q.math","action","norm"),"status",status);},delegate(){return status;}))
        {t.SetCapabilityResolver(delegate(string c){return D("available",true);});t.Run(new string[]{"q.math norm amplitudes=[5,12]"},null,null);Dictionary<string,object> j=Finish(t),r=M(Rows(j["records"])[0]["response"]);Check(!(bool)r["resultComplete"] &&(bool)r["resultTruncated"] &&(bool)M(r["result"])["previewOnly"],"genuinely large result missing honest truncation flag");Check(t.SequenceJson((string)j["id"],0).Contains(new string('n',10000)),"large result complete archive lost");}
    }
    static void LargeSamples(string root)
    {
        const long maximum=16384000000L;string input=Inputs(root),ws=Path.Combine(root,"workspace"),nativeId=Guid.NewGuid().ToString("N"),parent="",child="";bool begun=false;int polls=0,inputCalls=0;
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){if(c.StartsWith("q.verify samples ")){Check(c=="q.verify samples 16384000000 errors-only","large request decimal command lost precision");begun=true;Dictionary<string,object> r=Result(maximum);r["id"]=nativeId;r["active"]=true;r["phase"]="queued";return D("ok",true,"command",c,"result",D("queued",true,"job",r),"status",Status());}inputCalls++;return Return(c,D("norm",13));},delegate(){Dictionary<string,object> s=Status();if(begun){Dictionary<string,object> r=Result(maximum);r["id"]=nativeId;if(++polls<3){r["active"]=true;r["phase"]="running";}s["lastVerificationJob"]=r;}return s;}))
        {
            Configure(t);foreach(string tail in new string[]{"samples=16384000001","samples=9223372036854775807","samples=18446744073709551616","samples=1e10","samples=16384000000 samples=2"}){string c="q.folder run path=\""+input+"\" "+tail;Throws(delegate(){t.Execute(c.Split(' '),c);},tail.Contains(" samples=") ?"Duplicate":"samples");}Check(inputCalls==0 &&t.GetStatus()["job"]==null,"large invalid request ran inputwork");
            string command="q.folder run path=\""+input+"\" downloads=false purge=false samples=16384000000";t.Execute(command.Split(' '),command);Dictionary<string,object> j=Finish(t);parent=(string)j["id"];Dictionary<string,object> f=Rows(j["files"])[0];child=(string)f["childJobId"];Check((string)j["phase"]=="completed" &&(bool)j["receiptReady"] &&Convert.ToInt64(j["verificationSamples"])==maximum,"64-bit request gate failed");Check(Convert.ToInt32(j["completedCommands"])==1 &&inputCalls==1 &&polls>=3,"large verifier changed inputcounts or skipped barrier");Dictionary<string,object> v=M(t.SequenceReceiptMetadata(child)["verification"]),r=M(M(v["response"])["result"]);Check((string)r["plannedSamples"]=="16384000001" &&(string)r["completedSamples"]=="16384000001" &&(string)r["omittedSampleRows"]=="16383999969","large exact resultcounts overflowed");Check(Rows(r["samplePreview"]).Count==32 &&t.SequencePrintout(child,null).Contains("VERIFICATION APPENDIX"),"bounded large preview/appendix missing");string txt=t.SequencePrintout(child,null);Check(txt.IndexOf("Returned answer:")<txt.IndexOf("VERIFICATION APPENDIX"),"large full evidence precedes command answer");Check(json.Serialize(j).Length<20000,"large request grew metadata with samplecount");
        }
        using(SequenceTerminal t=new SequenceTerminal(ws,delegate(string c){throw new Exception("no replay");},delegate(){return Status();})){Check(Convert.ToInt64(M(t.GetStatus()["job"])["verificationSamples"])==maximum &&(bool)t.SequenceReceiptMetadata(parent)["receiptReady"] &&(bool)t.SequenceReceiptMetadata(child)["receiptReady"],"large proof cannot reload/validate");}
        string parentPath=Path.Combine(ws,"sequence-jobs",parent+".json"),original=File.ReadAllText(parentPath);foreach(object invalid in new object[]{10240.5d,"0010240",16384000001L}){Dictionary<string,object> corrupt=M(json.DeserializeObject(original));corrupt["verificationSamples"]=invalid;File.WriteAllText(parentPath,json.Serialize(corrupt));try{Throws(delegate(){using(SequenceTerminal restored=new SequenceTerminal(ws,delegate(string c){throw new Exception("no replay");},delegate(){return Status();})){}},"Invalid saved verification sample request");}finally{File.WriteAllText(parentPath,original);}}
        string cancelRoot=Path.Combine(root,"cancel"),cancelInput=Inputs(cancelRoot);bool active=false,cancelled=false;ManualResetEvent entered=new ManualResetEvent(false);string cancelId=Guid.NewGuid().ToString("N");int purges=0;
        using(SequenceTerminal t=new SequenceTerminal(Path.Combine(cancelRoot,"workspace"),delegate(string c){if(c=="q.verify cancel"){cancelled=true;return D("ok",true,"command",c,"result",D("cancelled",true));}if(c.StartsWith("q.verify samples ")){active=true;Dictionary<string,object> r=Result(maximum);r["id"]=cancelId;r["active"]=true;r["phase"]="queued";return D("ok",true,"command",c,"result",D("queued",true,"job",r));}return Return(c,D("norm",13));},delegate(){Dictionary<string,object> s=Status();if(active){Dictionary<string,object> r=Result(maximum);r["id"]=cancelId;r["active"]=!cancelled;r["phase"]=cancelled ?"cancelled":"running";r["passed"]=false;s["lastVerificationJob"]=r;entered.Set();}return s;}))
        {Configure(t);t.SetFileCleanup(delegate(Dictionary<string,object> f){purges++;});t.RunFolder(cancelInput,false,true,true,maximum);Check(entered.WaitOne(30000),"large verifier pending barrier not observed");Dictionary<string,object> pending=M(t.GetStatus()["job"]);Check(!(bool)pending["receiptReady"] &&Convert.ToInt32(pending["completedCommands"])==1 &&Convert.ToInt64(pending["verificationSamples"])==maximum,"large pending truthful progress");t.Cancel();Dictionary<string,object> j=Finish(t);Check(cancelled &&(string)j["phase"]=="cancelled" &&purges==0 &&Convert.ToInt32(j["completedCommands"])==1 &&!(bool)j["receiptReady"],"large cancellation failed to retain input/skippurge");Check((string)t.SequenceReceiptMetadata((string)Rows(j["files"])[0]["childJobId"])["receiptKind"]=="diagnostic-unverified","large cancelled job released successfulreceipt");}
    }
    public static void Run(string root){Run(root,true);}
    public static void Run(string root,bool maximum){Directory.CreateDirectory(root);Passed(Path.Combine(root,"pass"));foreach(string mode in new string[]{"count","gpu","preview","mismatch","descriptor","stale","address","digest"})Rejected(Path.Combine(root,mode),mode);PendingCancel(Path.Combine(root,"pending"));Async(Path.Combine(root,"async"));OptoutAndFields(Path.Combine(root,"optout"));InterruptedAndLegacy(Path.Combine(root,"restart"));PreviewAnswers(Path.Combine(root,"preview"));LargeSamples(Path.Combine(root,"large-samples"));if(maximum)MaximumFiles(Path.Combine(root,"maximum"));Console.WriteLine("PASS new folder verification gate: "+checks+" assertions; mock native responses, no real GPU measurement claimed. Large counts are mocked, not an executed billion-sample workload.");}
}
