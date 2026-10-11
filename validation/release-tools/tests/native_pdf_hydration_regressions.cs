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
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class NativePdfHydrationRegressions
{
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
    private static int checks;
    private static Dictionary<string,object> Map(object value){return (Dictionary<string,object>)value;}
    private static Dictionary<string,object> D(params object[] values){return SpinDoctorCalculators.D(values);}
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception("FAIL: "+message);}
    private static string Hash(string value){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
    private static Dictionary<string,object> Read(string path){return Map(Json.DeserializeObject(File.ReadAllText(path)));}
    private static Dictionary<string,object> Resolve(TerminalSession session,Dictionary<string,object> request)
    {return Map(((object[])typeof(TerminalSession).GetMethod("ResolvePdfRecords",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session,new object[]{new object[]{request}}))[0]);}
    private static void Refused(Action action,string message)
    {bool failed=false;try{action();}catch(TargetInvocationException error){failed=error.InnerException is InvalidDataException||error.InnerException is InvalidOperationException||error.InnerException is ArgumentException;}catch(InvalidDataException){failed=true;}catch(InvalidOperationException){failed=true;}catch(ArgumentException){failed=true;}Check(failed,message);}
    private static Dictionary<string,object> Effective(Dictionary<string,object> record)
    {object value;if(record.TryGetValue("finalNativeResponse",out value)&&value is Dictionary<string,object>)return Map(value);return Map(record["nativeResponse"]);}
    private static Dictionary<string,object> Reference(string type,string id,string stage,int index)
    {Dictionary<string,object> reference=D("type",type,"id",id,"stage",stage);if(type=="sequence")reference["index"]=index;return D("nativeArchive",reference);}
    private static void Mutate(string path,Action<Dictionary<string,object>> mutate,Action test)
    {string raw=File.ReadAllText(path);try{Dictionary<string,object> changed=Map(Json.DeserializeObject(raw));mutate(changed);File.WriteAllText(path,Json.Serialize(changed),new UTF8Encoding(false));test();}finally{File.WriteAllText(path,raw,new UTF8Encoding(false));}}
    public static void Run(string workspace,string output)
    {
        Directory.CreateDirectory(output);Dictionary<string,object> expected=Read(Path.Combine(workspace,"expected-answers.json"));string parentId=(string)expected["parentId"],reportId=(string)expected["reportId"];
        IList answers=(IList)expected["answers"];string parentPath=Path.Combine(workspace,"sequence-jobs",parentId+".json");Dictionary<string,object> parent=Read(parentPath);
        Dictionary<string,object> request=Reference("command",reportId,"completed",0);
        // Test setup reads immutable native archives; GPU disabled, no Execute calls.
        using(TerminalSession session=new TerminalSession(workspace,false))
        {
            Dictionary<string,object> resolved=Resolve(session,request),hydration=Map(resolved["commandHydration"]);IList records=(IList)resolved["hydratedCommandRecords"];
            Check(records.Count==23&&answers.Count==23,"all23 actual native answers hydrated");Check((bool)hydration["complete"]&&!(bool)hydration["partial"]&&(int)hydration["missingCount"]==0,"complete declared only after exact command coverage");
            Dictionary<string,object> nativeParent=Map(Effective(resolved)["result"]);Check(((IList)nativeParent["records"]).Count==0,"original immutable parent records remain empty and unchanged");
            Check(Json.Serialize(nativeParent)==Json.Serialize(Map(Read(Path.Combine(workspace,"quantum-command-reports",reportId+".completed.json"))["response"])["result"]),"original complete parent result remains byte-equivalent parsed payload");
            List<object> proof=new List<object>();Dictionary<string,object> last=null;
            for(int i=0;i<answers.Count;i++)
            {
                Dictionary<string,object> wanted=Map(answers[i]),record=Map(records[i]),result=Map(Effective(record)["result"]);Check((string)record["command"]==(string)wanted["command"],"original command ordered "+i);
                Check((int)record["sourceFileIndex"]==(int)wanted["sourceFileIndex"]&&(int)record["commandIndex"]==(int)wanted["commandIndex"],"original file/index ordered "+i);
                Check((string)record["sourceFilename"]==(string)wanted["sourceFilename"]&&(string)record["sequenceJobId"]==(string)wanted["sequenceId"]&&(string)record["parentSequenceJobId"]==parentId,"trusted source/parent identity "+i);
                Dictionary<string,object> originalAnswer=Read(Path.Combine(workspace,"sequence-reports",(string)wanted["archiveJsonFile"]));Check(Json.Serialize(result)==Json.Serialize(Map(originalAnswer["response"])["result"]),"exact actual calculator result survives hydration "+i);
                Check((string)record["completedUtc"]==(string)originalAnswer["completedUtc"],"original outer sequence command completion UTC retained "+i);
                Check((bool)record["verificationRequired"]&&(bool)record["receiptReady"]&&(string)record["verificationPhase"]=="passed","matched guarded verification metadata "+i);
                Check((string)record["jobId"]=="","synchronous calculator never borrows verifier job identity "+i);
                proof.Add(D("sourceFileIndex",record["sourceFileIndex"],"sourceFilename",record["sourceFilename"],"commandIndex",record["commandIndex"],"command",record["command"],"result",result,"nativeArchive",record["nativeArchive"]));last=record;
            }
            Dictionary<string,object> norm=Map(Effective(last)["result"]);Check(Convert.ToDouble(norm["normSquared"])==169&&Convert.ToDouble(norm["norm"])==13&&!(bool)norm["normalized"],"last original norm[5,12] has169/13/false");
            File.WriteAllText(Path.Combine(output,"resolved-parent.json"),Json.Serialize(resolved),new UTF8Encoding(false));File.WriteAllText(Path.Combine(output,"hydrated-answer-proof.json"),Json.Serialize(D("scope","Existing3.0.7 archive replay; GPU disabled, no native commands/calculations executed.","parentId",parentId,"archiveId",reportId,"commandHydration",hydration,"answers",proof.ToArray())),new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(output,"SpinDoctor-3.0.7-Actual-Folder-Answers-Repaired.pdf"),session.ReportPdf(D("kind","selected","view","details","records",new object[]{request},"status",D())));
            Dictionary<string,object> forged=new Dictionary<string,object>(request);forged["hydratedCommandRecords"]=new object[]{D("command","forged","nativeResponse",D("result",D("norm",999)))};forged["commandHydration"]=D("complete",true,"resolvedRecords",1);Check(((IList)Resolve(session,forged)["hydratedCommandRecords"]).Count==23,"client parent hydration replaced by native23answers");
            Dictionary<string,object> imported=Resolve(session,D("command","q.math norm amplitudes=[5,12]","nativeResponse",D("ok",true,"result",D("norm",13)),"hydratedCommandRecords",new object[]{D("command","forged")},"commandHydration",D("complete",true),"hydratedCommandScope","forged"));Check(!imported.ContainsKey("hydratedCommandRecords")&&!imported.ContainsKey("commandHydration")&&!imported.ContainsKey("hydratedCommandScope"),"client import cannot claim trusted folder hydration");
            Dictionary<string,object> lastRef=Map(last["nativeArchive"]),stale=Reference("sequence",(string)lastRef["id"],"accepted",(int)lastRef["index"]);stale["jobId"]=Map(Map(parent["files"] is IList?((IList)parent["files"])[0]:null)["verification"])["nativeJobIdAfter"];stale["kind"]="verification";
            Dictionary<string,object> canonical=Resolve(session,stale);Check((string)canonical["jobId"]==""&&(string)canonical["kind"]=="calculator"&&Convert.ToDouble(Map(Effective(canonical)["result"])["norm"])==13,"stale caller jobId/kind cannot replace a saved calculator answer");
            File.WriteAllBytes(Path.Combine(output,"individual-norm-5-12-details.pdf"),session.ReportPdf(D("kind","selected","view","details","records",new object[]{stale},"status",D())));
            SpinDoctorCommandReports history=(SpinDoctorCommandReports)typeof(TerminalSession).GetField("commandReports",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(session);
            foreach(string statusCommand in new string[]{"q.folder status","q.sequence status"})
            {Dictionary<string,object> entry=history.Record(statusCommand,D("ok",true,"result",D("job",parent),"status",D()));Dictionary<string,object> statusRecord=Resolve(session,Reference("command",(string)entry["id"],"accepted",0));Check(!statusRecord.ContainsKey("hydratedCommandRecords")&&(string)statusRecord["jobId"]=="","status PDF retains behavior without run ownership: "+statusCommand);Check(session.ReportPdf(D("kind","selected","records",new object[]{Reference("command",(string)entry["id"],"accepted",0)},"view","summary","status",D())).Length>1000,"status query PDF renders without hydration ownership error");}
            string childId=(string)Map(answers[0])["sequenceId"],childPath=Path.Combine(workspace,"sequence-jobs",childId+".json");
            Mutate(childPath,delegate(Dictionary<string,object> child){child["parentJobId"]="00000000000000000000000000000000";},delegate{Refused(delegate{Resolve(session,request);},"wrong child parent rejected");});
            Mutate(childPath,delegate(Dictionary<string,object> child){child["sourceSha256"]=new string('a',64);},delegate{Refused(delegate{Resolve(session,request);},"wrong child source SHA rejected");});
            Mutate(childPath,delegate(Dictionary<string,object> child){child["completedCommands"]=0;},delegate{Refused(delegate{Resolve(session,request);},"wrong child completion counts rejected");});
            Dictionary<string,object> childSaved=Read(childPath),step=Map(((IList)childSaved["records"])[0]);string archivePath=Path.Combine(workspace,"sequence-reports",(string)step["reportJsonFile"]),originalArchive=File.ReadAllText(archivePath);
            try{File.AppendAllText(archivePath," ");Refused(delegate{Resolve(session,request);},"altered immutable calculator bytes rejected by archive hash");}finally{File.WriteAllText(archivePath,originalArchive,new UTF8Encoding(false));}
            Mutate(parentPath,delegate(Dictionary<string,object> p){p["active"]=true;p["phase"]="executing";p["receiptReady"]=false;p["verificationPhase"]="pending";},delegate{Refused(delegate{Resolve(session,request);},"required pending parent receipt cannot hydrate or export");});
            // The completed historical archive is not rewritten for this mismatch fixture.
            Mutate(parentPath,delegate(Dictionary<string,object> p){p["completedUtc"]="2000-01-01T00:00:00.0000000Z";},delegate{Refused(delegate{Resolve(session,request);},"stale/different parent completion snapshot rejected");});
            Mutate(parentPath,delegate(Dictionary<string,object> p){p["active"]=false;p["phase"]="failed";p["receiptReady"]=false;p["verificationPhase"]="failed";p["verificationError"]="Explicit diagnostic replay fixture; no native failure or calculation invented.";},delegate
            {
                Dictionary<string,object> diagnosticParent=Read(parentPath),entry=history.Record("q.folder run path=\"fixture\"",D("ok",true,"result",D("queued",true,"job",D("id",parentId,"active",true))),parentId);string diagnosticId=(string)entry["id"];history.BindJob(diagnosticId,"sequence",parentId);history.Complete(diagnosticId,D("ok",false,"result",diagnosticParent,"error","Explicit synthetic diagnostic archive test around unchanged actual calculator returns."));
                Dictionary<string,object> diagnosticRequest=Reference("command",diagnosticId,"completed",0),diagnostic=Resolve(session,diagnosticRequest),metadata=Map(diagnostic["commandHydration"]);
                Check(((IList)diagnostic["hydratedCommandRecords"]).Count==23&&!(bool)metadata["complete"]&&(bool)metadata["partial"],"stopped diagnostic preserves actual archived answers with explicit partial scope");Check((string)diagnostic["receiptKind"]=="diagnostic-unverified"&&!(bool)diagnostic["receiptReady"],"partial folder never promoted to successful receipt");Check(session.ReportPdf(D("kind","selected","view","summary","records",new object[]{diagnosticRequest},"status",D())).Length>1000,"partial diagnostic PDF remains available with truthful labeling");
                Mutate(childPath,delegate(Dictionary<string,object> child){Map(((IList)child["records"])[0])["reportJsonFile"]="";},delegate{Dictionary<string,object> partial=Resolve(session,diagnosticRequest),coverage=Map(partial["commandHydration"]);Check(((IList)partial["hydratedCommandRecords"]).Count==22&&(int)coverage["missingCount"]==1&&((IList)coverage["errors"]).Count==1&&!(bool)coverage["complete"],"partial missing archived command explicitly declared rather than invented");});
            });
            Mutate(childPath,delegate(Dictionary<string,object> child){Dictionary<string,object> s=Map(((IList)child["records"])[0]);Dictionary<string,object> changed=Map(Json.DeserializeObject(originalArchive));changed["sequenceId"]="00000000000000000000000000000000";string wrong=Json.Serialize(changed);File.WriteAllText(archivePath,wrong,new UTF8Encoding(false));s["reportJsonSha256"]=Hash(wrong);},delegate{Refused(delegate{Resolve(session,request);},"hash-valid wrong archived sequence identity rejected");});File.WriteAllText(archivePath,originalArchive,new UTF8Encoding(false));
            Check(Json.Serialize(parent)==Json.Serialize(Read(parentPath)),"isolated parent restored exactly after tamper fixtures");
            Check(File.ReadAllText(archivePath)==originalArchive,"isolated immutable answer restored exactly after tamper fixtures");
        }
        Console.WriteLine("PASS native PDF archive hydration assertions: "+checks+"; actual12file/23command3.0.7 replay; GPU disabled;0new calculations.");
    }
}
