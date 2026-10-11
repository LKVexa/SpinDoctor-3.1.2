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
// Command reports are replay records, separate from transient statevectors and source files.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace QuettaQuantum
{
    public sealed class SpinDoctorCommandReports
    {
        private readonly object sync = new object();
        private readonly string root, indexPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 33554432, RecursionLimit = 100 };
        private readonly List<Dictionary<string, object>> entries = new List<Dictionary<string, object>>();
        private Action<string> receiptGuard;
        private const int MaximumArchiveBytes = 33554432;
        public SpinDoctorCommandReports(string workspace)
        {
            root = Path.Combine(workspace, "quantum-command-reports"); Directory.CreateDirectory(root); indexPath = Path.Combine(root, "index.json");
            if (File.Exists(indexPath))
            {
                object[] data = json.DeserializeObject(ReadBounded(indexPath)) as object[];
                if (data == null) throw new InvalidDataException("Command report index is not an array.");
                HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object value in data)
                {
                    Dictionary<string, object> item = value as Dictionary<string, object>;
                    if (item == null || !item.ContainsKey("id") || !(item["id"] is string) || !item.ContainsKey("command") || !(item["command"] is string)) throw new InvalidDataException("Invalid report index entry.");
                    ValidateId((string)item["id"]); if (!ids.Add((string)item["id"])) throw new InvalidDataException("Duplicate command report ID.");
                    ValidateHash(item, "jsonSha256"); ValidateHash(item, "txtSha256");
                    if (item.ContainsKey("completedUtc")) { ValidateHash(item, "completedJsonSha256"); ValidateHash(item, "completedTxtSha256"); }
                    if (item.ContainsKey("jobId") && (!(item["jobId"] is string) || !item.ContainsKey("jobType") || !(item["jobType"] is string))) throw new InvalidDataException("Invalid report job binding.");
                    if(item.ContainsKey("sequenceOwnerId")){if(!(item["sequenceOwnerId"] is string))throw new InvalidDataException("Invalid receipt sequence owner.");ValidateId((string)item["sequenceOwnerId"]);}
                    entries.Add(item);
                }
            }
        }
        public Dictionary<string, object> Record(string command, Dictionary<string, object> response)
        { return Record(command,response,""); }
        public Dictionary<string, object> Record(string command, Dictionary<string, object> response,string sequenceOwnerId)
        {
            lock (sync)
            {
                if(!String.IsNullOrEmpty(sequenceOwnerId))ValidateId(sequenceOwnerId);
                string id = Guid.NewGuid().ToString("N"), utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                bool waiting=AwaitingCompletion(command,response), success=Flag(response,"ok");
                string outcome=waiting?"pending":success?"completed":"failed";
                Dictionary<string, object> payload = D("schema", "SPINDOCTOR/COMMAND-REPORT/1", "id", id, "capturedUtc", utc, "command", command ?? "", "response", response,
                    "executionOutcome",outcome,"commandCompletedUtc",waiting?"":utc,"completionScope",waiting?"Accepted asynchronous work; no matching completion captured.":"Synchronous command returned at this observed UTC; this is not independent numerical verification.",
                    "scope", "Recorded classical calculation or command response. Represented procedural family operations are not executed gates. A status/help/error report does not imply a measurement.");
                if(!String.IsNullOrEmpty(sequenceOwnerId))payload["sequenceOwnerId"]=sequenceOwnerId;
                string raw = json.Serialize(payload), txt = Print(payload);
                string rawPath = Path.Combine(root, id + ".json"), txtPath = Path.Combine(root, id + ".txt");
                ValidateSize(raw); ValidateSize(txt); AtomicWrite(rawPath, raw); AtomicWrite(txtPath, txt);
                Dictionary<string, object> entry = D("id", id, "capturedUtc", utc,"receivedUtc",utc, "command", command ?? "", "ok", success, "acceptedOk", success, "phase",waiting?"accepted":outcome,"executionOutcome",outcome,"commandCompletedUtc",waiting?"":utc,"completionScope",payload["completionScope"], "elapsedMs", response.ContainsKey("elapsedMs") ? response["elapsedMs"] : null,
                    "txtSha256", Hash(Encoding.UTF8.GetBytes(txt)), "jsonSha256", Hash(Encoding.UTF8.GetBytes(raw)), "txtBytes", Encoding.UTF8.GetByteCount(txt), "jsonBytes", Encoding.UTF8.GetByteCount(raw), "downloadAvailable", true);
                if(!String.IsNullOrEmpty(sequenceOwnerId))entry["sequenceOwnerId"]=sequenceOwnerId;
                entries.Add(entry); try { Save(); } catch { entries.Remove(entry); throw; } return entry;
            }
        }
        public Dictionary<string,object> Page(int offset,int limit)
        {
            lock(sync){if(offset<0||limit<1||limit>256)throw new ArgumentException("History offset >=0, limit 1..256.");offset=Math.Min(offset,entries.Count);int take=Math.Min(limit,entries.Count-offset);return D("records",entries.GetRange(offset,take).ToArray(),"totalCommands",entries.Count.ToString(CultureInfo.InvariantCulture),"offset",offset,"nextOffset",offset+take<entries.Count?(object)(offset+take):null,"omitted",entries.Count-take);}
        }
        public void BindJob(string id,string type,string jobId)
        {
            lock(sync)
            {
                if (String.IsNullOrEmpty(type) || String.IsNullOrEmpty(jobId)) throw new ArgumentException("Report job type and ID are required.");
                Dictionary<string,object> e=Find(id), next=new Dictionary<string,object>(e); next["jobType"]=type;next["jobId"]=jobId;next["phase"]="accepted";next["executionOutcome"]="pending";next["commandCompletedUtc"]="";next["completionScope"]="Accepted asynchronous work; no matching completion captured."; ReplaceEntry(e,next);
            }
        }
        public void Complete(string id,Dictionary<string,object> response)
        {
            lock(sync)
            {
                Dictionary<string,object> e=Find(id);if(e.ContainsKey("completedUtc"))return;
                string utc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);
                bool success=Flag(response,"ok");Dictionary<string,object> result=response.ContainsKey("result")?response["result"] as Dictionary<string,object>:null;
                string outcome=result!=null&&result.ContainsKey("phase")?Convert.ToString(result["phase"],CultureInfo.InvariantCulture):(success?"completed":"failed");
                bool unknown=response.ContainsKey("nativeCompletionKnown")&&response["nativeCompletionKnown"] is bool&&!(bool)response["nativeCompletionKnown"];
                string stopped=unknown?"":result!=null&&result.ContainsKey("completedUtc")&&!String.IsNullOrEmpty(Convert.ToString(result["completedUtc"],CultureInfo.InvariantCulture))?Convert.ToString(result["completedUtc"],CultureInfo.InvariantCulture):utc;
                Dictionary<string,object> payload=D("schema","SPINDOCTOR/COMMAND-REPORT/1","id",id,"capturedUtc",utc,"command",e["command"],"stage","completed","response",response,"executionOutcome",outcome,"commandCompletedUtc",stopped,"completionScope",unknown?"Native completion unknown; captured failure diagnostics only.":"Matched native job final snapshot; timestamp is native stop time when supplied, otherwise observation time.","scope","Matched native asynchronous job completion. Partial, failed or cancelled work is reported at its actual completed count; exporting performs no additional calculation.");
                if(e.ContainsKey("sequenceOwnerId"))payload["sequenceOwnerId"]=e["sequenceOwnerId"];
                string raw=json.Serialize(payload),txt=Print(payload);ValidateSize(raw);ValidateSize(txt);
                AtomicWrite(Path.Combine(root,id+".completed.json"),raw);AtomicWrite(Path.Combine(root,id+".completed.txt"),txt);
                Dictionary<string,object> next=new Dictionary<string,object>(e);next["completedUtc"]=utc;next["reportStage"]="completed";
                next["ok"]=success;next["executionOutcome"]=outcome;next["commandCompletedUtc"]=stopped;next["completionScope"]=payload["completionScope"];
                next["phase"]=outcome;next["jobOutcome"]=outcome;next["completedJsonSha256"]=Hash(Encoding.UTF8.GetBytes(raw));next["completedTxtSha256"]=Hash(Encoding.UTF8.GetBytes(txt));ReplaceEntry(e,next);
            }
        }
        public Dictionary<string,object>[] Pending(){lock(sync){return entries.FindAll(delegate(Dictionary<string,object> e){return e.ContainsKey("jobId")&&!e.ContainsKey("completedUtc");}).ToArray();}}
        public Dictionary<string, object> GetStatus()
        {
            lock (sync)
            {
                int first = Math.Max(0, entries.Count - 32); return D("schema", "SPINDOCTOR/COMMAND-HISTORY/1", "totalCommands", entries.Count.ToString(CultureInfo.InvariantCulture), "records", entries.GetRange(first, entries.Count-first).ToArray(), "omitted", first,
                    "scope", "Native reports preserve commands, seeds/counters, result snapshots and replay hashes. Large calculation buffers are independent and released by their backend. Existing browser history and checkpoints are preserved.");
            }
        }
        public string Printout(string id) { return Read(id, ".txt","latest"); }
        public string Json(string id) { return Read(id, ".json","latest"); }
        public string Printout(string id,string stage){return Read(id,".txt",stage);}
        public string Json(string id,string stage){return Read(id,".json",stage);}
        public string Csv(string id){return Csv(id,"latest");}
        public string Csv(string id,string stage)
        {
            // Json applies the immutable archive hash and the same owning
            // sequence verification gate as TXT/PDF before any data is exposed.
            string native=Json(id,stage);Dictionary<string,object> payload=json.DeserializeObject(native) as Dictionary<string,object>;
            if(payload==null)throw new InvalidDataException("Command report archive must be an object.");
            string selected=payload.ContainsKey("stage")&&Convert.ToString(payload["stage"],CultureInfo.InvariantCulture)=="completed"?"completed":"accepted";
            return SpinDoctorReportCsv.Build(D("requestedStage",stage,"selectedStage",selected,"commandReport",payload),"command",id,selected,Hash(Encoding.UTF8.GetBytes(native)));
        }
        public void SetReceiptGuard(Action<string> guard){if(guard==null)throw new ArgumentNullException("guard");lock(sync){receiptGuard=guard;}}
        public string ReceiptOwner(string id)
        {
            lock(sync)
            {
                Dictionary<string,object> entry=Find(id);byte[] bytes=ReadBoundedBytes(Path.Combine(root,id+".json"));if(Hash(bytes)!=(string)entry["jsonSha256"])throw new InvalidDataException("Command owner archive hash mismatch.");
                Dictionary<string,object> payload=json.DeserializeObject(new UTF8Encoding(false,true).GetString(bytes)) as Dictionary<string,object>;
                string owner=payload!=null&&payload.ContainsKey("sequenceOwnerId")?Convert.ToString(payload["sequenceOwnerId"],CultureInfo.InvariantCulture):"";
                string indexed=entry.ContainsKey("sequenceOwnerId")?Convert.ToString(entry["sequenceOwnerId"],CultureInfo.InvariantCulture):"";
                if(owner!=indexed)throw new InvalidDataException("Command receipt owner differs from immutable archive.");if(owner.Length>0)ValidateId(owner);return owner;
            }
        }
        private void RequireReceipt(string id)
        {
            string owner=ReceiptOwner(id);if(owner.Length==0)return;Action<string> guard;lock(sync){guard=receiptGuard;}
            if(guard==null)throw new InvalidOperationException("Owned command receipt requires its native sequence verification policy.");guard(owner);
        }
        public static bool AwaitingCompletion(string command,Dictionary<string,object> response)
        {
            if(!Flag(response,"ok"))return false;object value;Dictionary<string,object> result=response.TryGetValue("result",out value)?value as Dictionary<string,object>:null;
            if(result==null)return false;if(Flag(result,"queued"))return true;
            string[] words=(command??"").Trim().ToLowerInvariant().Split(new char[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            if(words.Length<2||words[0]=="q.status"||words[1]=="status"||words[1]=="cancel"||words[1]=="help"||words[1]=="list"||words[1]=="show"||words[1]=="export")return false;
            Dictionary<string,object> job=result.TryGetValue("job",out value)?value as Dictionary<string,object>:result;
            return job!=null&&job.ContainsKey("id")&&Flag(job,"active");
        }
        private static bool Flag(Dictionary<string,object> value,string key){object raw;return value!=null&&value.TryGetValue(key,out raw)&&raw is bool&&(bool)raw;}
        private Dictionary<string,object> Find(string id){ValidateId(id);Dictionary<string,object> item=entries.Find(delegate(Dictionary<string,object> e){return (string)e["id"]==id;});if(item==null)throw new ArgumentException("Unknown command report ID.");return item;}
        private string Read(string id, string extension,string stage)
        {
            RequireReceipt(id);
            lock (sync) {Dictionary<string,object> item=Find(id);if(stage!="latest"&&stage!="accepted"&&stage!="completed")throw new ArgumentException("Unknown report stage.");if(stage=="completed"&&item.ContainsKey("jobId")&&!item.ContainsKey("completedUtc"))throw new InvalidOperationException("This command has no archived matching completion yet; download stage=accepted.");bool completed=stage!="accepted"&&item.ContainsKey("completedUtc");string path=Path.Combine(root,id+(completed?".completed":"")+extension); byte[] bytes=ReadBoundedBytes(path);string key=completed?(extension==".txt"?"completedTxtSha256":"completedJsonSha256"):(extension==".txt"?"txtSha256":"jsonSha256");if(Hash(bytes)!=(string)item[key])throw new InvalidDataException("Command archive hash mismatch.");return new UTF8Encoding(false,true).GetString(bytes); }
        }
        public object Execute(string[] words)
        {
            if(words.Length==2 && words[1].Equals("list",StringComparison.OrdinalIgnoreCase))return GetStatus();
            if(words.Length==3 && words[1].Equals("show",StringComparison.OrdinalIgnoreCase))return json.DeserializeObject(Json(words[2]));
            if(words.Length==3 && words[1].Equals("export",StringComparison.OrdinalIgnoreCase))return D("id",words[2],"printout",Printout(words[2]),"json",json.DeserializeObject(Json(words[2])));
            if(words.Length==2 && words[1].Equals("help",StringComparison.OrdinalIgnoreCase))return D("commands",new string[]{"q.history list","q.history show ID","q.history export ID"},"scope","All future terminal commands receive a TXT/JSON archive, including failed and non-measurement commands. Downloading is handled by the adjacent report controls.");
            throw new ArgumentException("Use q.history list|show ID|export ID|help.");
        }
        private void Save()
        {
            string text=json.Serialize(entries);ValidateSize(text);AtomicWrite(indexPath,text);
        }
        private void ReplaceEntry(Dictionary<string,object> previous,Dictionary<string,object> next)
        {
            Dictionary<string,object> original=new Dictionary<string,object>(previous);previous.Clear();foreach(KeyValuePair<string,object> pair in next)previous.Add(pair.Key,pair.Value);
            try{Save();}catch{previous.Clear();foreach(KeyValuePair<string,object> pair in original)previous.Add(pair.Key,pair.Value);throw;}
        }
        private static void ValidateHash(Dictionary<string,object> item,string key)
        {
            object raw;if(!item.TryGetValue(key,out raw)||!(raw is string)||((string)raw).Length!=64)throw new InvalidDataException("Invalid report archive hash metadata.");
            foreach(char c in (string)raw)if(!(c>='0'&&c<='9')&&!(c>='a'&&c<='f'))throw new InvalidDataException("Invalid report archive hash metadata.");
        }
        private static void ValidateSize(string text){if(Encoding.UTF8.GetByteCount(text)>MaximumArchiveBytes)throw new InvalidDataException("Report exceeds bounded archive size.");}
        private static string ReadBounded(string path){return new UTF8Encoding(false,true).GetString(ReadBoundedBytes(path));}
        private static byte[] ReadBoundedBytes(string path)
        {
            using(FileStream stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length>MaximumArchiveBytes)throw new InvalidDataException("Report exceeds bounded archive read size.");
                byte[] bytes=new byte[(int)stream.Length];int at=0;
                while(at<bytes.Length){int read=stream.Read(bytes,at,bytes.Length-at);if(read==0)throw new InvalidDataException("Incomplete report archive.");at+=read;}
                return bytes;
            }
        }
        private static string Print(Dictionary<string,object> payload)
        {
            StringBuilder b=new StringBuilder(); b.AppendLine("SPINDOCTOR — COMMAND RESULT");b.AppendLine("Report ID: "+payload["id"]);b.AppendLine("Captured UTC: "+payload["capturedUtc"]);b.AppendLine("Command: "+payload["command"]);
            if(payload.ContainsKey("executionOutcome"))b.AppendLine("COMMAND EXECUTION: "+Convert.ToString(payload["executionOutcome"],CultureInfo.InvariantCulture).ToUpperInvariant());
            if(payload.ContainsKey("commandCompletedUtc"))b.AppendLine("Command completed/stopped UTC: "+(String.IsNullOrEmpty(Convert.ToString(payload["commandCompletedUtc"],CultureInfo.InvariantCulture))?"Not captured - matching native completion pending or unknown":Convert.ToString(payload["commandCompletedUtc"],CultureInfo.InvariantCulture)));
            if(payload.ContainsKey("completionScope"))b.AppendLine("Completion scope: "+payload["completionScope"]);
            if(payload.ContainsKey("sequenceOwnerId"))b.AppendLine("Folder/sequence receipt owner: "+payload["sequenceOwnerId"]+". Command execution and sampled model verification are separate; consult the owning sequence receipt for its verification verdict.");
            b.AppendLine((string)payload["scope"]);b.AppendLine();int detailAt=b.Length;
            b.AppendLine("COMPLETE STRUCTURED RETURN (exact integer count strings retained)");Write(b,payload["response"],0);
            // Preserve the existing complete native printout at the byte cap;
            // optional interpretation must not turn a valid capture into failure.
            string required=b.ToString();int remaining=MaximumArchiveBytes-Encoding.UTF8.GetByteCount(required);
            if(remaining<=0)return required;
            Dictionary<string,object> recordedResponse=payload["response"] as Dictionary<string,object>,detailResponse=recordedResponse==null?new Dictionary<string,object>():new Dictionary<string,object>(recordedResponse);
            foreach(string key in new string[]{"executionOutcome","commandCompletedUtc","completionScope","completedUtc","nativeCompletionKnown"})if(payload.ContainsKey(key))detailResponse[key]=payload[key];
            string detail=SpinDoctorReportDetails.Text(Convert.ToString(payload["command"],CultureInfo.InvariantCulture),detailResponse)+Environment.NewLine;
            if(Encoding.UTF8.GetByteCount(detail)>remaining)detail="Readable detail omitted to preserve the complete native return within the UTF-8 archive byte budget."+Environment.NewLine;
            if(Encoding.UTF8.GetByteCount(detail)<=remaining)b.Insert(detailAt,detail);return b.ToString();
        }
        private static void Write(StringBuilder b,object value,int depth)
        {
            if(depth>90){b.AppendLine("[depth bound]");return;}Dictionary<string,object> dict=value as Dictionary<string,object>;
            if(dict!=null){foreach(KeyValuePair<string,object> kv in dict){b.Append(' ',Math.Min(depth*2,160));b.Append(kv.Key);b.Append(':');if(kv.Value is Dictionary<string,object> || (kv.Value is System.Collections.IEnumerable && !(kv.Value is string))){b.AppendLine();Write(b,kv.Value,depth+1);}else b.AppendLine(" "+Convert.ToString(kv.Value,CultureInfo.InvariantCulture));}return;}
            System.Collections.IEnumerable seq=value as System.Collections.IEnumerable;if(seq!=null && !(value is string)){int i=0;foreach(object item in seq){b.Append(' ',Math.Min(depth*2,160));b.AppendLine("["+(i++).ToString(CultureInfo.InvariantCulture)+"]");Write(b,item,depth+1);}return;}
            b.Append(' ',Math.Min(depth*2,160));b.AppendLine(value==null?"null":Convert.ToString(value,CultureInfo.InvariantCulture));
        }
        private static void ValidateId(string id){if(id==null||id.Length!=32)throw new ArgumentException("Report ID must be 32 hexadecimal characters.");foreach(char c in id)if(!Uri.IsHexDigit(c))throw new ArgumentException("Invalid report ID.");}
        private static string Hash(byte[] data){using(SHA256 h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
        private static Dictionary<string,object> D(params object[] pairs){return SpinDoctorCalculators.D(pairs);}
        // Each file is replaced atomically; index metadata commits only after both files.
        private static void AtomicWrite(string path, string text)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using(FileStream stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                { byte[] bytes=new UTF8Encoding(false).GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){} }
        }
    }
}
