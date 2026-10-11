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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class CsvHttpOfflineAudit
{
    private const string Authority="127.0.0.1:7",Origin="http://127.0.0.1:7",Token="isolated-offline-test-token";
    private static int checks;
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=80};
    private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    private static Dictionary<string,object> D(params object[] a){Dictionary<string,object> d=new Dictionary<string,object>();for(int i=0;i<a.Length;i+=2)d.Add((string)a[i],a[i+1]);return d;}
    private static void Check(bool pass,string label){if(!pass)throw new Exception("FAIL: "+label);checks++;Console.WriteLine("PASS: "+label);}
    private static void Field(object instance,string name,object value){typeof(TerminalHost).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(instance,value);}
    private sealed class DuplexMemory:Stream
    {
        private readonly MemoryStream input;private readonly MemoryStream output=new MemoryStream();private int readTimeout,writeTimeout;
        public int BytesRead;
        public DuplexMemory(byte[] bytes){input=new MemoryStream(bytes,false);}
        public string Output(){return Utf8.GetString(output.ToArray());}
        public override int Read(byte[] b,int o,int n){int got=input.Read(b,o,n);BytesRead+=got;return got;}
        public override int ReadByte(){int b=input.ReadByte();if(b>=0)BytesRead++;return b;}
        public override void Write(byte[] b,int o,int n){output.Write(b,o,n);}
        public override bool CanRead{get{return true;}}public override bool CanWrite{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanTimeout{get{return true;}}
        public override int ReadTimeout{get{return readTimeout;}set{readTimeout=value;}}public override int WriteTimeout{get{return writeTimeout;}set{writeTimeout=value;}}
        public override long Length{get{return input.Length;}}public override long Position{get{return input.Position;}set{throw new NotSupportedException();}}
        public override void Flush(){}public override long Seek(long x,SeekOrigin y){throw new NotSupportedException();}public override void SetLength(long x){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){if(disposing){input.Dispose();output.Dispose();}base.Dispose(disposing);}
    }
    private static string Request(TerminalHost host,string method,string path,string extra,byte[] body,out int read)
    {
        string head=method+" "+path+" HTTP/1.1\r\nHost: "+Authority+"\r\n"+(extra??"")+(body==null?"":"Content-Length: "+body.Length+"\r\n")+"Connection: close\r\n\r\n";
        byte[] h=Encoding.ASCII.GetBytes(head),all=new byte[h.Length+(body==null?0:body.Length)];Buffer.BlockCopy(h,0,all,0,h.Length);if(body!=null)Buffer.BlockCopy(body,0,all,h.Length,body.Length);
        using(DuplexMemory stream=new DuplexMemory(all))
        {
            try{typeof(TerminalHost).GetMethod("Handle",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(host,new object[]{stream});}
            catch(TargetInvocationException e){typeof(TerminalHost).GetMethod("Fail",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(host,new object[]{stream,400,e.InnerException.Message});}
            read=stream.BytesRead;return stream.Output();
        }
    }
    private static string Request(TerminalHost h,string m,string p,string e,byte[] b){int read;return Request(h,m,p,e,b,out read);}
    private static void Status(TerminalHost h,string m,string p,string extra,byte[] body,int code,string label){string response=Request(h,m,p,extra,body);Check(response.StartsWith("HTTP/1.1 "+code+" ",StringComparison.Ordinal),label);}
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);
        using(TerminalSession session=new TerminalSession(root,false))
        {
            // Skip the host constructor: it starts a listener. The protocol uses
            // the same initialized fields but operates entirely in memory here.
            TerminalHost host=(TerminalHost)FormatterServices.GetUninitializedObject(typeof(TerminalHost));Field(host,"root",root);Field(host,"token",Token);Field(host,"origin",Origin);Field(host,"authority",Authority);Field(host,"session",session);Field(host,"json",Json);
            string auth="Authorization: Bearer "+Token+"\r\n",same=auth+"Origin: "+Origin+"\r\n",json=same+"Content-Type: application/json; charset=utf-8\r\n";
            Dictionary<string,object> returned=(Dictionary<string,object>)session.Execute("q.math norm amplitudes=[3,4]");Check((bool)returned["ok"],"small arithmetic native record available without shots");string id=(string)returned["reportId"],command="/api/history/"+id+"/csv?stage=accepted";
            string response=Request(host,"GET",command,auth,null);Check(response.StartsWith("HTTP/1.1 200 ")&&response.Contains("Content-Type: text/csv; charset=utf-8\r\n")&&response.Contains("Cache-Control: no-store\r\n")&&response.Contains("X-Content-Type-Options: nosniff\r\n"),"command CSV GET has UTF-8 CSV type and private response headers");
            Check(response.Contains("/norm\"")&&response.Contains("\"5\""),"command CSV GET dispatches actual native norm");
            Status(host,"GET",command,null,null,403,"CSV GET rejects missing bearer secret");Status(host,"GET",command,"Authorization: Bearer wrong\r\n",null,403,"CSV GET rejects wrong bearer secret");Status(host,"GET",command,auth+"Origin: https://example.invalid\r\n",null,403,"CSV GET rejects cross-origin read");
            Status(host,"POST",command,json,Utf8.GetBytes("{}"),405,"command CSV resource is GET only");
            Status(host,"GET",command+"&stage=completed",auth,null,400,"CSV GET rejects duplicate stage field");Status(host,"GET",command.Replace("accepted","unknown"),auth,null,400,"CSV GET rejects unknown report stage");Status(host,"GET",command+"&preview=true",auth,null,400,"CSV GET rejects preview query on CSV");
            Dictionary<string,object> report=D("kind","selected","view","details","records",new object[]{D("id","offline","nativeArchive",D("type","command","id",id,"stage","accepted"))},"status",D());byte[] body=Utf8.GetBytes(Json.Serialize(report));
            response=Request(host,"POST","/api/reports/export.csv",json,body);Check(response.StartsWith("HTTP/1.1 200 ")&&response.Contains("Content-Type: text/csv; charset=utf-8\r\n")&&response.Contains("/norm\""),"selected PDF-companion CSV POST resolves the same explicit accepted archive");
            Status(host,"POST","/api/reports/export.csv",auth+"Content-Type: application/json\r\n",body,403,"CSV POST requires same Origin");Status(host,"POST","/api/reports/export.csv","Origin: "+Origin+"\r\nContent-Type: application/json\r\n",body,403,"CSV POST rejects missing bearer secret");Status(host,"POST","/api/reports/export.csv",same+"Content-Type: text/csv\r\n",body,415,"CSV report request accepts JSON only");Status(host,"POST","/api/reports/export.csv",same+"Content-Type: application/jsonp\r\n",body,415,"CSV POST rejects JSON-prefix media type confusion");Status(host,"POST","/api/reports/export.csv",same+"Content-Type: application/json; charset=latin1\r\n",body,415,"CSV POST rejects non-UTF8 charset");Status(host,"POST","/api/reports/export.csv",json,new byte[]{255},400,"CSV POST rejects malformed UTF-8");Status(host,"POST","/api/reports/export.csv",json,Utf8.GetBytes("[]"),400,"CSV POST rejects invalid report shape");Status(host,"GET","/api/reports/export.csv",same,null,405,"CSV report generator is POST only");
            int read;string largeHeader="Content-Length: 16385\r\n";response=Request(host,"POST","/api/reports/export.csv",largeHeader,null,out read);Check(response.StartsWith("HTTP/1.1 403 "),"large CSV POST rejects unauthenticated body before reading");Check(read<400,"large CSV POST consumes headers only before rejection");Status(host,"POST","/api/reports/export.csv",json+"Content-Length: 33554433\r\n",null,413,"CSV POST retains exact 32 MiB request bound");Status(host,"POST","/api/reports/export.csv",json+"Transfer-Encoding: chunked\r\n",body,400,"CSV POST rejects ambiguous chunked framing");
            foreach(string kind in new string[]{"session","circuit"}){report["kind"]=kind;response=Request(host,"POST","/api/reports/export.csv",json,Utf8.GetBytes(Json.Serialize(report)));Check(response.StartsWith("HTTP/1.1 200 ")&&response.Contains("Content-Type: text/csv; charset=utf-8\r\n"),kind+" CSV POST supports UI kind contract");}
            Dictionary<string,object> queued=(Dictionary<string,object>)session.StartSequence(D("commands",new string[]{"q.math norm amplitudes=[5,12]"},"presetId","","parameters",D()));Dictionary<string,object> job=(Dictionary<string,object>)((Dictionary<string,object>)queued["result"])["job"];string sequenceId=(string)job["id"];DateTime until=DateTime.UtcNow.AddSeconds(4);do{Thread.Sleep(10);Dictionary<string,object> status=(Dictionary<string,object>)session.GetStatus();job=(Dictionary<string,object>)((Dictionary<string,object>)status["sequence"])["job"];}while((bool)job["active"]&&DateTime.UtcNow<until);Check(!(bool)job["active"]&&(string)job["phase"]=="completed","one-step arithmetic-only sequence completed without shots");
            string sequence="/api/sequences/"+sequenceId;response=Request(host,"GET",sequence+"/csv",auth,null);Check(response.StartsWith("HTTP/1.1 200 ")&&response.Contains("Content-Type: text/csv; charset=utf-8\r\n")&&response.Contains("/norm\"")&&response.Contains("\"13\""),"whole-file CSV route streams the actual saved command values");response=Request(host,"GET",sequence+"/commands/0/csv?stage=accepted",auth,null);Check(response.StartsWith("HTTP/1.1 200 ")&&response.Contains("/norm\""),"sequence-step CSV GET accepts UI index/stage grammar");Status(host,"GET",sequence+"/commands/128/csv?stage=accepted",auth,null,400,"CSV command index128 rejected before report lookup");Status(host,"GET",sequence+"/csv?preview=true",auth,null,400,"whole-file CSV GET rejects JSON-only preview query");Status(host,"POST",sequence+"/csv",json,Utf8.GetBytes("{}"),405,"whole-file CSV stream is GET only");
            report["kind"]="selected";report["records"]=new object[]{D("id","sequence-step","nativeArchive",D("type","sequence","id",sequenceId,"index",0,"stage","accepted"))};response=Request(host,"POST","/api/reports/export.csv",json,Utf8.GetBytes(Json.Serialize(report)));Check(response.StartsWith("HTTP/1.1 200 ")&&response.Contains("/norm\"")&&response.Contains("\"13\""),"automatic sequence command CSV POST resolves indexed accepted archive");

            Dictionary<string,object> attached=(Dictionary<string,object>)session.AttachInput("equation-http.csv","x,y\n-2,-3\n-1,-1\n0,1\n1,3\n2,5\n");Check((bool)attached["ok"],"tiny numeric source stored inertly for native equation protocol fixture");Dictionary<string,object> metadata=(Dictionary<string,object>)attached["result"];string inputId=(string)metadata["id"],sourceSha=(string)metadata["sha256"];
            Dictionary<string,object> fitted=(Dictionary<string,object>)session.Execute("q.input fit "+inputId+" target=1 features=[0]");Check((bool)fitted["ok"],"tiny five-row native fit produced an archived equation without quantum shots");string fitId=(string)fitted["reportId"];
            report=D("kind","selected","view","details","records",new object[]{D("id","equation-fit","nativeArchive",D("type","command","id",fitId,"stage","accepted"),"nativeResponse",D("ok",true,"result",D("resultKind","empirical-csv-fit","equation","FORGED EQUATION","intercept",999,"coefficients",new object[0])))},"status",D());body=Utf8.GetBytes(Json.Serialize(report));
            Dictionary<string,object> before=(Dictionary<string,object>)session.GetStatus();
            response=Request(host,"POST","/api/reports/equations",json,body);Check(response.StartsWith("HTTP/1.1 200 ")&&response.Contains("Content-Type: application/json; charset=utf-8\r\n")&&response.Contains("Cache-Control: no-store\r\n")&&response.Contains("X-Content-Type-Options: nosniff\r\n"),"equation POST matches authenticated UTF-8 private JSON route contract");
            string responseBody=response.Substring(response.IndexOf("\r\n\r\n",StringComparison.Ordinal)+4);Dictionary<string,object> equations=Json.DeserializeObject(responseBody)as Dictionary<string,object>;Check(equations!=null&&(string)equations["schema"]=="SPINDOCTOR/REPORT-EQUATIONS/1"&&(int)equations["count"]==1,"equation POST returns native representation schema and count");Dictionary<string,object> equation=(Dictionary<string,object>)((object[])equations["representations"])[0];
            Check(((string)equation["equationText"]).Contains("y_hat")&&((string)equation["mathNotation"]).Contains("\\widehat")&&!responseBody.Contains("FORGED EQUATION"),"equation POST hydrates actual fitted equation and ignores browser forged coefficients");Check((string)equation["sourceSha256"]==sourceSha&&(string)equation["selectedArchiveStage"]=="accepted"&&((string)equation["nativeArchiveSha256"]).Length==64,"equation POST preserves native source SHA archive SHA and explicit accepted stage");
            Status(host,"POST","/api/reports/equations",auth+"Content-Type: application/json\r\n",body,403,"equation POST requires same-origin Origin");Status(host,"POST","/api/reports/equations","Origin: "+Origin+"\r\nContent-Type: application/json\r\n",body,403,"equation POST rejects missing bearer secret");Status(host,"POST","/api/reports/equations",same+"Content-Type: text/plain\r\n",body,415,"equation POST accepts JSON request only");Status(host,"POST","/api/reports/equations",same+"Content-Type: application/jsonp\r\n",body,415,"equation POST rejects JSON-prefix type confusion");Status(host,"POST","/api/reports/equations",json,new byte[]{255},400,"equation POST rejects malformed UTF-8");Status(host,"POST","/api/reports/equations",json,Utf8.GetBytes("[]"),400,"equation POST rejects invalid report shape");Status(host,"GET","/api/reports/equations",same,null,405,"native equation endpoint is POST only");
            response=Request(host,"POST","/api/reports/equations","Content-Length: 16385\r\n",null,out read);Check(response.StartsWith("HTTP/1.1 403 ")&&read<400,"large equation request authenticates before reading or allocating body");Status(host,"POST","/api/reports/equations",json+"Content-Length: 33554433\r\n",null,413,"equation POST retains exact32MiB report-request bound");Status(host,"POST","/api/reports/equations",json+"Transfer-Encoding: chunked\r\n",body,400,"equation POST rejects ambiguous chunked framing");
            ((Dictionary<string,object>)((Dictionary<string,object>)((object[])report["records"])[0])["nativeArchive"])["stage"]="wrong";Status(host,"POST","/api/reports/equations",json,Utf8.GetBytes(Json.Serialize(report)),400,"equation POST rejects unknown archived stage without latest substitution");
            Dictionary<string,object> after=(Dictionary<string,object>)session.GetStatus();foreach(string counter in new string[]{"actualCpuStates","actualGpuStates","measurementShots","verifiedSamples","descriptorHash"})Check(Convert.ToString(before[counter])==Convert.ToString(after[counter]),"equation export and rejection do not change native "+counter);
            Check((bool)((Dictionary<string,object>)session.Execute("q.circuit new http_eq_"+Guid.NewGuid().ToString("N").Substring(0,12)+" 2"))["ok"],"small equation fixture circuit created with native name bound");Check((bool)((Dictionary<string,object>)session.Execute("q.circuit add h 0"))["ok"],"H equation fixture instruction stored without execution");Check((bool)((Dictionary<string,object>)session.Execute("q.circuit add cx 0 1"))["ok"],"CX equation fixture instruction stored without execution");Dictionary<string,object> shown=(Dictionary<string,object>)session.Execute("q.circuit show");Check((bool)shown["ok"],"native circuit definition retained without simulation shots");report=D("kind","selected","view","details","records",new object[]{D("id","equation-circuit","nativeArchive",D("type","command","id",shown["reportId"],"stage","accepted"))},"status",D());response=Request(host,"POST","/api/reports/equations",json,Utf8.GetBytes(Json.Serialize(report)));Check(response.StartsWith("HTTP/1.1 200 ")&&response.Contains("Local symbolic operator")&&response.Contains("CX")&&response.Contains("gateOperators"),"equation POST preserves actual circuit gate order operands and symbolic operators without shots");

        }
        return checks;
    }
}
