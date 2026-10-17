using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class FullFolderIntegration {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    static int checks;
    static void Check(bool yes,string message){checks++;if(!yes)throw new Exception(message);}
    static Dictionary<string,object> Map(object v){return (Dictionary<string,object>)v;}
    static int Num(object v){return Convert.ToInt32(v,CultureInfo.InvariantCulture);}
    static Dictionary<string,object> Job(TerminalSession s){return Map(Map(s.GetStatus())["sequence"])["job"] as Dictionary<string,object>;}
    static void Compare(object a,object b,string message) {
        if(b is bool){Check(a is bool&&(bool)a==(bool)b,message);return;}
        if(b is string){Check(Convert.ToString(a,CultureInfo.InvariantCulture)==(string)b,message);return;}
        IList list=b as IList;
        if(list!=null){IList actual=a as IList;Check(actual!=null&&actual.Count==list.Count,message+" list length");for(int i=0;i<list.Count;i++)Compare(actual[i],list[i],message+"["+i+"]");return;}
        double x=Convert.ToDouble(a,CultureInfo.InvariantCulture),y=Convert.ToDouble(b,CultureInfo.InvariantCulture);Check(!Double.IsNaN(x)&&Math.Abs(x-y)<=1e-9*Math.Max(1,Math.Abs(y)),message);
    }
    static TcpClient Connect(TerminalHost host,string method,string path,byte[] body,bool auth){
        TcpClient c=new TcpClient("127.0.0.1",host.Port);c.ReceiveTimeout=30000;NetworkStream stream=c.GetStream();
        string head=method+" "+path+" HTTP/1.1\r\nHost: 127.0.0.1:"+host.Port+"\r\n";
        if(auth)head+="Authorization: Bearer "+host.Token+"\r\n";
        if(body!=null)head+="Origin: "+host.Origin+"\r\nContent-Type: application/json\r\nContent-Length: "+body.Length+"\r\n";
        head+="Connection: close\r\n\r\n";byte[] bytes=Encoding.ASCII.GetBytes(head);stream.Write(bytes,0,bytes.Length);if(body!=null)stream.Write(body,0,body.Length);return c;
    }
    static string ReadHead(NetworkStream stream){
        List<byte> bytes=new List<byte>();int value;
        while((value=stream.ReadByte())>=0){bytes.Add((byte)value);int n=bytes.Count;if(n>=4&&bytes[n-4]==13&&bytes[n-3]==10&&bytes[n-2]==13&&bytes[n-1]==10)break;if(n>16384)throw new Exception("Oversized HTTP response header.");}
        return Encoding.ASCII.GetString(bytes.ToArray());
    }
    static string Request(TerminalHost host,string method,string path,byte[] body,bool auth,out string header){
        using(TcpClient c=Connect(host,method,path,body,auth)){NetworkStream stream=c.GetStream();header=ReadHead(stream);using(MemoryStream data=new MemoryStream()){byte[] buffer=new byte[65536];int n;while((n=stream.Read(buffer,0,buffer.Length))>0)data.Write(buffer,0,n);return Utf8.GetString(data.ToArray());}}
    }
    static string DigestHttp(TerminalHost host,string path,out long length){
        using(TcpClient c=Connect(host,"GET",path,null,true)){NetworkStream stream=c.GetStream();string header=ReadHead(stream);Check(header.StartsWith("HTTP/1.1 200"),"Streamed TXT endpoint succeeds.");length=-1;foreach(string line in header.Split(new string[]{"\r\n"},StringSplitOptions.None))if(line.StartsWith("Content-Length: "))length=Int64.Parse(line.Substring(16),CultureInfo.InvariantCulture);using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    }
    public static string Run(string source,string scratch,string inputs,string specPath){
        Directory.CreateDirectory(scratch);string workspace=Path.Combine(scratch,"session");Directory.CreateDirectory(Path.Combine(workspace,"quantum"));
        foreach(string p in Directory.GetFiles(Path.Combine(source,"quantum"),"*.cs"))File.Copy(p,Path.Combine(workspace,"quantum",Path.GetFileName(p)));
        foreach(string name in new string[]{"SPINDOCTOR_PRESETS.json","EquationCatalogue.json","QUANTUM_TERMINAL.html"})File.Copy(Path.Combine(source,name),Path.Combine(workspace,name));
        object[] specs=(object[])Json.DeserializeObject(File.ReadAllText(specPath,Utf8));Dictionary<string,object> endJob=null;string folderId=null;long lastBytes=0;string lastHash=null;List<object> perFile=new List<object>();
        Dictionary<string,object> before; long comparedTotal=0; object actualVerification=null;
        using(TerminalHost host=new TerminalHost(workspace,true)){
            Check((bool)Map(host.Session.GetStatus())["gpuAvailable"],"Genuine OpenCL GPU required; no simulated fallback.");
            Check((bool)Map(host.Session.Execute("q.prepare grid 20000000 25000000"))["ok"],"Isolated test explicitly prepares nontrivial model.");
            Check((bool)Map(host.Session.Execute("q.migrate t 1"))["ok"],"Isolated test explicitly migrates model.");
            before=Map(host.Session.GetStatus());Exception serveError=null;Thread server=new Thread(delegate(){try{host.Serve();}catch(Exception e){serveError=e;}});server.IsBackground=true;server.Start();
            try{
                string header;string command="q.folder run path=\""+inputs.Replace("\\","/")+"\" downloads=false purge=true";
                string returned=Request(host,"POST","/api/command",Utf8.GetBytes(Json.Serialize(new Dictionary<string,object>{{"command",command}})),true,out header);
                Check(header.StartsWith("HTTP/1.1 200"),"Folder launches over authenticated HTTP.");
                Dictionary<string,object> acceptance=Map(Json.DeserializeObject(returned));Check((bool)acceptance["ok"],"Folder command accepted.");folderId=Convert.ToString(Map(Map(acceptance["result"])["job"])["id"]);
                DateTime deadline=DateTime.UtcNow.AddMinutes(60);int previous=-1;
                do{endJob=Job(host.Session);int done=Num(endJob["completedCommands"]);Check(done>=previous,"Aggregate progress never decreases.");previous=done;if(!(bool)endJob["active"])break;Thread.Sleep(1000);}while(DateTime.UtcNow<deadline);
                Check(!(bool)endJob["active"]&&(string)endJob["phase"]=="completed","Actual native folder completes.");
                Check(Num(endJob["completedCommands"])==172&&Num(endJob["totalCommands"])==172,"Aggregate is exactly172/172.");
                Check(Num(endJob["completedFiles"])==24&&Num(endJob["totalFiles"])==24,"Exactly24/24 top-level files.");
                Check((bool)endJob["totalCommandsKnown"],"Final aggregate total is known.");
                Check(((IList)endJob["records"]).Count==0&&((IList)endJob["commands"]).Count==0,"Parent never masquerades as a child sequence.");
                IList files=(IList)endJob["files"];
                for(int f=0;f<24;f++){
                    Dictionary<string,object> file=Map(files[f]),spec=Map(specs[f]);string id=Convert.ToString(file["childJobId"]);Check(id!=folderId,"Child archive identity is distinct.");
                    Check((string)file["executionPhase"]=="completed"&&(string)file["reportPhase"]=="archived"&&(string)file["purgePhase"]=="completed","Execution,archive,purge succeeded independently.");
                    Check((bool)file["verificationRequired"]&&(bool)file["receiptReady"]&&(string)file["verificationPhase"]=="passed","Automatic verification gates each receipt.");
                    Dictionary<string,object> evidence=Map(file["verification"]),vresponse=Map(evidence["response"]),verified=Map(vresponse["result"]);
                    long planned=Int64.Parse((string)verified["plannedSamples"]); comparedTotal+=planned;actualVerification=evidence;
                    Check((bool)vresponse["ok"]&&(bool)verified["passed"]&&(string)verified["phase"]=="completed"&&!(bool)verified["active"],"Matching native verification completed successfully.");
                    foreach(string k in new string[]{"completedSamples","verifiedCount","cpuStates","gpuStates"})Check(Int64.Parse((string)verified[k])==planned,"Actual sample coverage consistent: "+k);
                    Check(planned>=10240&&Num(verified["errorCount"])==0,"Default finite native plan compared with no mismatches.");
                    Check((string)evidence["descriptorHashBefore"]==(string)verified["descriptorHash"]&&(string)evidence["descriptorHashAfter"]==(string)verified["descriptorHash"],"Verification bound to unchanged captured descriptor.");
                    Check((bool)verified["hasPrepared"]&&Convert.ToDouble(verified["migrationT"])==1,"Actual samples concern nontrivial test model.");
                    DateTime stopped=DateTime.Parse((string)verified["completedUtc"],null,DateTimeStyles.RoundtripKind),archived=DateTime.Parse((string)file["reportArchivedUtc"],null,DateTimeStyles.RoundtripKind),purged=DateTime.Parse((string)file["purgeCompletedUtc"],null,DateTimeStyles.RoundtripKind);
                    Check(stopped<=archived&&archived<=purged,"Native verification precedes receipt archive and purge.");
                    foreach(object raw in (IList)verified["samplePreview"]){Dictionary<string,object> row=Map(raw);double[] expectedState=QuantumMath.State(UInt64.Parse((string)row["address"]),20000000,25000000,1,new double[]{1,0,0,0,0,0,1,0});IList cpu=(IList)row["cpu"],gpu=(IList)row["gpu"];for(int j=0;j<9;j++){Check(Math.Abs(Convert.ToDouble(cpu[j])-expectedState[j])<=5e-15,"Sampled CPU prediction matches captured address within serialized FP64 precision.");Check(Math.Abs(Convert.ToDouble(gpu[j])-expectedState[j])<=2e-9,"Actual GPU prediction agrees at captured address.");}}
                    string preview=Request(host,"GET","/api/sequences/"+id+"/json?preview=true",null,true,out header);
                    Check(header.StartsWith("HTTP/1.1 200"),"Compact child preview over HTTP.");
                    Dictionary<string,object> view=Map(Json.DeserializeObject(preview));Check(!view.ContainsKey("completeCommandReports"),"Preview excludes full report bodies.");
                    Dictionary<string,object> child=Map(view["job"]);Check((string)child["id"]==id,"Preview identity stable.");
                    object[] commands=(object[])spec["commands"],expected=(object[])spec["expected"];
                    Check(Num(child["completedCommands"])==commands.Length,"Every file command completed.");
                    for(int i=0;i<commands.Length;i++){
                        Dictionary<string,object> report=Map(Json.DeserializeObject(host.Session.SequenceJson(id,i,"latest"))),response=Map(report["response"]),result=Map(response["result"]);
                        Check((bool)response["ok"]&&Convert.ToString(report["normalizedCommand"])==(string)commands[i],"Native command and success match fixture.");
                        foreach(KeyValuePair<string,object> e in Map(expected[i]))Compare(result[e.Key],e.Value,"Native expected field "+e.Key);
                        Compare(result["physicalQuantumOperations"],0,"No physical quantum operations.");Compare(result["measurementSeedChanged"],false,"Arithmetic does not alter random seed.");
                    }
                    long bytes;string hash=DigestHttp(host,"/api/sequences/"+id+"/printout",out bytes);
                    Check(hash==Convert.ToString(file["reportSha256"])&&bytes==Convert.ToInt64(file["reportBytes"],CultureInfo.InvariantCulture),"Downloaded TXT length and hash match immutable archive.");
                    perFile.Add(new Dictionary<string,object>{{"file",file["filename"]},{"commands",commands.Length},{"txtBytes",bytes},{"sha256",hash},{"verificationPhase",file["verificationPhase"]},{"comparedSamples",planned},{"descriptorHash",verified["descriptorHash"]},{"verificationCompletedUtc",verified["completedUtc"]},{"reportArchivedUtc",file["reportArchivedUtc"]},{"purgeCompletedUtc",file["purgeCompletedUtc"]},{"passed",true}});
                    if(f==23){lastBytes=bytes;lastHash=hash;}
                }
                string blocked=Request(host,"GET","/api/sequences/"+folderId+"/json?preview=true",null,false,out header);Check(header.StartsWith("HTTP/1.1 403"),"Compact API requires authentication.");
                Request(host,"GET","/api/sequences/"+folderId+"/json?preview=true&preview=true",null,true,out header);Check(header.StartsWith("HTTP/1.1 400"),"Duplicate preview query rejected.");
                Request(host,"GET","/api/sequences/"+folderId+"/json?preview=true&stage=accepted",null,true,out header);Check(header.StartsWith("HTTP/1.1 400"),"Preview rejects ambiguous historical stage.");
                Request(host,"GET","/api/sequences/"+folderId+"/commands/0/json?preview=true",null,true,out header);Check(header.StartsWith("HTTP/1.1 400"),"Preview cannot reinterpret indexed archive.");
                long folderBytes;DigestHttp(host,"/api/sequences/"+folderId+"/printout",out folderBytes);Check(folderBytes>lastBytes,"Whole folder export includes every file.");
                Dictionary<string,object> after=Map(host.Session.GetStatus());
                foreach(string k in new string[]{"descriptorHash","fieldSeed","measurementSeed","measurementCounter","measurementShots","representedGateApplications"})Compare(after[k],before[k],"Sampling preserves model/measurement context: "+k);
                foreach(string k in new string[]{"actualCpuStates","actualGpuStates","verifiedSamples"})Check(Int64.Parse((string)after[k])-Int64.Parse((string)before[k])==comparedTotal,"Actual verification operations counted: "+k);
                Check((string)after["productVersion"]=="3.1.2","Runtime version bumped.");
                File.WriteAllText(Path.Combine(scratch,"actual-folder-verification.json"),Json.Serialize(actualVerification),Utf8);
                File.WriteAllText(Path.Combine(scratch,"actual-folder-preview.json"),host.Session.SequencePreviewJson(folderId),Utf8);
            }finally{host.Stop();Check(server.Join(10000)&&serveError==null,"Native loopback stops cleanly.");}
        }
        using(TerminalSession reloaded=new TerminalSession(workspace,false)){Dictionary<string,object> job=Job(reloaded);Check((string)job["id"]==folderId&&Num(job["totalCommands"])==172&&Num(job["completedCommands"])==172&&(string)job["phase"]=="completed","Restart preserves successful complete folder.");Check(!reloaded.SequencePreviewJson(folderId).Contains("completeCommandReports"),"Restart compact preview remains bounded.");}
        var summary=new Dictionary<string,object>{{"passed",true},{"assertions",checks},{"nativeFiles",24},{"nativeCommands",172},{"automaticVerificationSteps",24},{"actualComparedOperations",comparedTotal},{"comparedOperationsScope","Per-file sample comparisons; repeated descriptor plans are not distinct union coverage."},{"realOpenClGpu",true},{"nestedFilesSkipped",2},{"lastFileTxtBytes",lastBytes},{"lastFileSha256",lastHash},{"httpStreamingVerified",true},{"restartVerified",true},{"records",perFile}};
        File.WriteAllText(Path.Combine(scratch,"full-folder-integration.json"),Json.Serialize(summary),Utf8);return "PASS "+checks+" full native folder/HTTP/expected-result/restart assertions; 24files/172commands; last TXT "+lastBytes+"bytes.";
    }
}
