using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class TransportAuditHarness
{
    private static int passed;
    private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432};
    private static Dictionary<string,object> D(params object[] values){Dictionary<string,object>d=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)d.Add((string)values[i],values[i+1]);return d;}
    private static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);passed++;Console.WriteLine("PASS: "+label);}
    private static void Reject(Action action,string label,string text)
    {
        Exception error=null;try{action();}catch(Exception e){error=e;}Check(error!=null&&(text==null||error.Message.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0),label+(error==null?"":" ["+error.GetType().Name+"]"));
    }
    private static string Fresh(string root,string label){string path=Path.Combine(root,label+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    private static byte[] Embedded(byte[] program,bool compressed)
    {
        byte[] header=Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Filespec /F (safe.json) /EF << /F 2 0 R >> >>\nendobj\n2 0 obj\n<< /Length "+program.Length+(compressed?" /Filter /FlateDecode":"")+" >>\nstream\n");
        using(MemoryStream result=new MemoryStream()){result.Write(header,0,header.Length);result.Write(program,0,program.Length);byte[]tail=Encoding.ASCII.GetBytes("\nendstream\nendobj\n%%EOF\n");result.Write(tail,0,tail.Length);return result.ToArray();}
    }
    private static byte[] Zlib(byte[] bytes)
    {
        using(MemoryStream result=new MemoryStream())
        {
            result.WriteByte(0x78);result.WriteByte(0x9c);
            using(DeflateStream compressed=new DeflateStream(result,CompressionMode.Compress,true)){compressed.Write(bytes,0,bytes.Length);}
            uint a=1,b=0;foreach(byte x in bytes){a=(a+x)%65521;b=(b+a)%65521;}uint adler=(b<<16)|a;for(int i=3;i>=0;i--)result.WriteByte((byte)(adler>>(8*i)));return result.ToArray();
        }
    }
    private static void Reports(string root)
    {
        string path=Fresh(root,"reports");SpinDoctorCommandReports reports=new SpinDoctorCommandReports(path);
        Dictionary<string,object> entry=reports.Record("q.status",D("ok",true,"result",D("count","9007199254740993")));string id=(string)entry["id"];
        reports.BindJob(id,"circuit","native-job");Check(reports.Pending().Length==1,"accepted job retains pending completion");
        string index=Path.Combine(path,"quantum-command-reports","index.json");
        using(FileStream locked=new FileStream(index,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            Reject(delegate{reports.Record("q.status",D("ok",true));},"failed index commit rolls back accepted record",null);
            Check((string)reports.GetStatus()["totalCommands"]=="1","record rollback retains exact history count");
            Reject(delegate{reports.Complete(id,D("ok",true,"result",D("phase","completed")));},"failed index commit rolls back completion metadata",null);
            Check(reports.Pending().Length==1&&!entry.ContainsKey("completedUtc"),"failed completion remains retryable");
        }
        reports.Complete(id,D("ok",true,"result",D("phase","completed")));Check(reports.Pending().Length==0,"retry commits completed report");
        Check(reports.Json(id,"accepted").Contains("9007199254740993")&&reports.Json(id,"completed").Contains("completed"),"accepted and completed replay archives remain distinct");
        SpinDoctorCommandReports reload=new SpinDoctorCommandReports(path);Check(reload.Json(id).Contains("completed"),"committed archives survive reload");
        string archive=Path.Combine(path,"quantum-command-reports",id+".completed.json");File.WriteAllText(archive,"tampered",Utf8);
        Reject(delegate{reload.Json(id);},"modified archived bytes fail SHA-256 verification","hash mismatch");
        using(FileStream large=new FileStream(archive,FileMode.Create,FileAccess.Write)){large.SetLength(33554433);}
        Reject(delegate{reload.Json(id);},"oversized archive rejected before bounded read","bounded");
        Dictionary<string,object> duplicate=new Dictionary<string,object>(entry);File.WriteAllText(index,Json.Serialize(new object[]{entry,duplicate}),Utf8);
        Reject(delegate{new SpinDoctorCommandReports(path);},"duplicate persistent report IDs rejected","Duplicate");
        string indexRoot=Fresh(root,"index-limit");Directory.CreateDirectory(Path.Combine(indexRoot,"quantum-command-reports"));
        using(FileStream large=new FileStream(Path.Combine(indexRoot,"quantum-command-reports","index.json"),FileMode.Create,FileAccess.Write)){large.SetLength(33554433);}
        Reject(delegate{new SpinDoctorCommandReports(indexRoot);},"oversized history index rejected before allocation","bounded");
    }
    private static void InputsAndPdf(string root)
    {
        string path=Fresh(root,"inputs");InputAttachmentManager inputs=new InputAttachmentManager(path);string program="{\"qubits\":1,\"gates\":[{\"name\":\"h\",\"qubits\":[0]}]}";
        Dictionary<string,object> record=inputs.Upload("safe.json",program);string id=(string)record["id"];
        Check(inputs.ExportText(id)==program,"UTF-8 sources round-trip exactly");Reject(delegate{inputs.Upload("../escape.json",program);},"attachment filename traversal rejected",null);
        Reject(delegate{inputs.ExportText(id+"\n");},"attachment ID trailing newline rejected","opaque");
        byte[] pdf=Embedded(Utf8.GetBytes(program),false);Check(SpinDoctorPdfInputs.Extract(pdf).Content==program,"uncompressed embedded PDF program imports exactly");
        byte[] compressed=Zlib(Utf8.GetBytes(program));Check(SpinDoctorPdfInputs.Extract(Embedded(compressed,true)).Content==program,"zlib embedded PDF program imports exactly");
        compressed[compressed.Length-1]^=1;Reject(delegate{SpinDoctorPdfInputs.Extract(Embedded(compressed,true));},"PDF compressed stream corruption rejected","checksum");
        byte[] oversized=new byte[2097153];Reject(delegate{SpinDoctorPdfInputs.Extract(Embedded(Zlib(oversized),true));},"single compressed PDF stream bounded to 2 MiB","2MiB");
        byte[] twoMiB=Zlib(new byte[2097152]);using(MemoryStream total=new MemoryStream())
        {
            byte[] h=Encoding.ASCII.GetBytes("%PDF-1.4\n");total.Write(h,0,h.Length);
            for(int n=1;n<=9;n++){byte[] x=Encoding.ASCII.GetBytes(n+" 0 obj\n<< /Length "+twoMiB.Length+" /Filter /FlateDecode >>\nstream\n");total.Write(x,0,x.Length);total.Write(twoMiB,0,twoMiB.Length);x=Encoding.ASCII.GetBytes("\nendstream\nendobj\n");total.Write(x,0,x.Length);}h=Encoding.ASCII.GetBytes("%%EOF\n");total.Write(h,0,h.Length);
            byte[] aggregate=total.ToArray();Reject(delegate{SpinDoctorPdfInputs.Extract(aggregate);},"aggregate decompression bomb bounded to 16 MiB","aggregate");
        }
        Dictionary<string,object> binary=inputs.UploadBinary("safe.pdf",pdf);string binaryId=(string)binary["id"];Check((bool)binary["parseValid"],"PDF upload preview validates explicit program");
        string stored=Path.Combine(path,"inputs",binaryId+".utf8");pdf[8]^=1;File.WriteAllBytes(stored,pdf);
        Reject(delegate{new InputAttachmentManager(path);},"PDF attachment corruption rejects catalog reload","hash mismatch");
    }
    private static string Request(TerminalHost host,string method,string path,string extra,byte[] body)
    {
        using(TcpClient client=new TcpClient("127.0.0.1",host.Port))
        {
            client.ReceiveTimeout=10000;NetworkStream stream=client.GetStream();string request=method+" "+path+" HTTP/1.1\r\nHost: 127.0.0.1:"+host.Port+"\r\n"+extra+(body==null?"":"Content-Length: "+body.Length+"\r\n")+"Connection: close\r\n\r\n";byte[] head=Encoding.ASCII.GetBytes(request);stream.Write(head,0,head.Length);if(body!=null)stream.Write(body,0,body.Length);
            using(MemoryStream response=new MemoryStream()){byte[] chunk=new byte[8192];int n;try{while((n=stream.Read(chunk,0,chunk.Length))>0)response.Write(chunk,0,n);}catch(IOException){if(response.Length==0)throw;}return Encoding.UTF8.GetString(response.ToArray());}
        }
    }
    private static void Http(string root)
    {
        string path=Fresh(root,"host");File.WriteAllText(Path.Combine(path,"QUANTUM_TERMINAL.html"),"<html>__QUANTUM_TOKEN__</html>",Utf8);
        using(TerminalHost host=new TerminalHost(path,false))
        {
            Exception hostFailure=null;Thread thread=new Thread(delegate(){try{host.Serve();}catch(Exception e){hostFailure=e;}});thread.IsBackground=true;thread.Start();
            try
            {
                string auth="Authorization: Bearer "+host.Token+"\r\n",origin="Origin: "+host.Origin+"\r\n";
                Check(Request(host,"GET","/",null,null).Contains(host.Token),"HTML bootstrap injects per-host secret");
                Check(Request(host,"GET","/api/status",auth,null).StartsWith("HTTP/1.1 200"),"authenticated status remains available");
                Check(Request(host,"GET","/api/status",null,null).StartsWith("HTTP/1.1 403"),"missing bearer secret rejected");
                Check(Request(host,"GET","/api/status",auth+"Origin: https://example.invalid\r\n",null).StartsWith("HTTP/1.1 403"),"cross-origin read rejected");
                Check(Request(host,"GET","/api/status",auth+"X-Test : invalid\r\n",null).StartsWith("HTTP/1.1 400"),"whitespace before HTTP header colon rejected");
                Check(Request(host,"GET","/api/status",auth+"X-Test: a\r\nX-Test: b\r\n",null).StartsWith("HTTP/1.1 400"),"duplicate HTTP headers rejected");
                Check(Request(host,"GET","/api/status",auth+"X-Test: value\nInjected: yes\r\n",null).StartsWith("HTTP/1.1 400"),"bare newline header injection rejected");
                byte[] command=Utf8.GetBytes("{\"command\":\"q.status\"}");
                Check(Request(host,"POST","/api/command",auth+origin+"Content-Type: application/jsonp\r\n",command).StartsWith("HTTP/1.1 415"),"JSON prefix media-type confusion rejected");
                Check(Request(host,"POST","/api/command",auth+origin+"Content-Type: application/json; charset=latin1\r\n",command).StartsWith("HTTP/1.1 415"),"non-UTF8 JSON charset rejected");
                Check(Request(host,"POST","/api/command",auth+"Content-Type: application/json\r\n",command).StartsWith("HTTP/1.1 403"),"POST requires same origin");
                Check(Request(host,"POST","/api/command",auth+origin+"Content-Type: application/json; charset=utf-8\r\n",command).StartsWith("HTTP/1.1 200"),"valid UTF-8 same-origin command executes");
                Check(Request(host,"POST","/api/command",auth+origin+"Content-Type: application/json\r\n",new byte[]{0xff}).StartsWith("HTTP/1.1 400"),"malformed UTF-8 body rejected");
                Check(Request(host,"POST","/api/attachments","Content-Length: 16385\r\n",null).StartsWith("HTTP/1.1 403"),"large unauthenticated body rejected before reading");
                Check(Request(host,"POST","/api/command",auth+origin+"Content-Length: 16385\r\n",null).StartsWith("HTTP/1.1 413"),"ordinary command body stays at 16 KiB");
                Check(Request(host,"GET","/api/status",auth+"Transfer-Encoding: chunked\r\n",null).StartsWith("HTTP/1.1 400"),"chunked transfer framing rejected");
            }
            finally{host.Stop();Check(thread.Join(10000)&&hostFailure==null,"loopback host stops cleanly");}
        }
    }
    private static void SessionState(string root)
    {
        string path=Fresh(root,"session");
        using(TerminalSession session=new TerminalSession(path,false))
        {
            Dictionary<string,object> invalid=(Dictionary<string,object>)session.Execute("q.verify samples "+new string('9',1025)+" errors-only");
            Check(!(bool)invalid["ok"],"verification sample integers bound before parsing");
            Dictionary<string,object> accepted=(Dictionary<string,object>)session.Execute("q.verify samples 20000 errors-only");Check((bool)accepted["ok"],"verification worker accepts bounded request");
            Dictionary<string,object> status=null,job=null;DateTime end=DateTime.UtcNow.AddSeconds(10);
            do{Thread.Sleep(25);status=(Dictionary<string,object>)session.GetStatus();job=(Dictionary<string,object>)status["lastVerificationJob"];}while((bool)job["active"]&&DateTime.UtcNow<end);
            Check(!(bool)job["active"]&&(string)job["phase"]=="failed"&&(string)job["cpuStates"]=="16384"&&(string)job["gpuStates"]=="0"&&(string)job["completedSamples"]=="0", "missing GPU fails worker with exact CPU-only accounting");
            session.Execute("q.prepare grid 2 2");
        }
        using(TerminalSession reloaded=new TerminalSession(path,false)){Check((string)((Dictionary<string,object>)reloaded.GetStatus())["stateCount"]=="4","historical verification restores after grid changes");}
        string state=Path.Combine(path,"quantum-session-state.json");string original=File.ReadAllText(state,Utf8);Dictionary<string,object> data=Json.Deserialize<Dictionary<string,object>>(original);
        data["hasPrepared"]="true";File.WriteAllText(state,Json.Serialize(data),Utf8);Reject(delegate{using(TerminalSession s=new TerminalSession(path,false)){}},"restored boolean rejects string coercion","boolean");
        Check(File.ReadAllText(state,Utf8)==Json.Serialize(data),"rejected saved state is preserved byte-for-byte");
        data=Json.Deserialize<Dictionary<string,object>>(original);data["actualCpuStates"]=new string('9',1025);File.WriteAllText(state,Json.Serialize(data),Utf8);Reject(delegate{using(TerminalSession s=new TerminalSession(path,false)){}},"restored counters bound before BigInteger parsing","counter");
        data=Json.Deserialize<Dictionary<string,object>>(original);((Dictionary<string,object>)data["lastVerificationJob"])["completedSamples"]="20000";File.WriteAllText(state,Json.Serialize(data),Utf8);Reject(delegate{using(TerminalSession s=new TerminalSession(path,false)){}},"restored verification accounting rejects invented comparisons","accounting");
        data=Json.Deserialize<Dictionary<string,object>>(original);data["migrationTExact"]="NaN";File.WriteAllText(state,Json.Serialize(data),Utf8);Reject(delegate{using(TerminalSession s=new TerminalSession(path,false)){}},"nonfinite saved migration rejected","migration");
        File.WriteAllText(state,original,Utf8);
        using(TerminalSession session=new TerminalSession(path,false))
        {
            using(FileStream locked=new FileStream(state,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                Dictionary<string,object> result=(Dictionary<string,object>)session.Execute("q.migrate t 0.5");Check(!(bool)result["ok"]&&((string)result["error"]).Contains("Checkpoint persistence also failed"),"repeated checkpoint failure returns explicit native error");
            }
            Check((bool)((Dictionary<string,object>)session.Execute("q.migrate t 0.25"))["ok"],"session remains usable after persistence recovers");
        }
    }
    private static void SyntaxAndReports()
    {
        SpinDoctorSyntax.ValidateSyntax(new string[]{"q.noise","apply","state=[1,0]","channel=DEPOLARIZING","p=0.1","convention=mix-with-I-over-2"});Check(true,"case-insensitive noise channel preflight matches backend");
        Reject(delegate{SpinDoctorSyntax.ValidateSyntax(new string[]{"q.resource","tranche-plan","addresses="+new string('9',1025),"width=2"});},"calculator count preflight enforces digit bound","decimal count");
        Dictionary<string,object> result=D("qubits",1,"phase","completed","histogramComplete",true,"histogram",new object[]{D("bitstring","0","count",new string('9',1025))},"completedShots","1","requestedShots","1");
        Dictionary<string,object> record=D("command","q.circuit run shots 1","nativeResponse",D("ok",true,"result",result));
        Reject(delegate{SpinDoctorReportPdf.Build(D("kind","selected","records",new object[]{record},"status",D()),"");},"PDF histogram integers bounded before parsing","1024 digits");
    }
    public static int Run(string root)
    {
        return Run(root,true);
    }
    public static int Run(string root,bool includeHttp){Reports(root);InputsAndPdf(root);SessionState(root);SyntaxAndReports();if(includeHttp)Http(root);Console.WriteLine("Passed "+passed+" transport/archive/parser assertions.");return passed;}
}
