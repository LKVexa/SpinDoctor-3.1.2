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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace QuettaQuantum
{
    public sealed class TerminalSession : IDisposable
    {
        public const string ProductVersion = "3.1.2";
        private const string Schema = "QUETTAFLOP/PROCEDURAL-PRODUCT-SESSION/1";
        private readonly string root, statePath, errorPath;
        private readonly object sync = new object();
        private readonly JavaScriptSerializer json;
        private readonly FileStream sessionLock;
        private readonly Dictionary<string, object> sourceHashes = new Dictionary<string, object>();
        private QuantumGpu gpu;
        private SpinDoctorTrancheTerminal tranche;
        private CircuitTerminal circuit;
        private InputAttachmentManager inputs;
        private SpinDoctorCalculators calculators;
        private SpinDoctorAdvanced advanced;
        private SequenceTerminal sequences;
        private SpinDoctorCommandReports commandReports;
        private bool disposed, prepared;
        private string gpuError = "", fieldSeed, measurementSeed;
        private ulong m = 20000000UL, l = 25000000UL, measureCounter;
        private double migration;
        private double[] matrix = QuantumMath.Identity;
        private BigInteger generation = BigInteger.Zero;
        private BigInteger preparations = BigInteger.Zero, gates = BigInteger.Zero;
        private BigInteger lifetimePreparations = BigInteger.Zero, lifetimeGates = BigInteger.Zero;
        private BigInteger actualCpu = BigInteger.Zero, actualGpu = BigInteger.Zero;
        private BigInteger verified = BigInteger.Zero, shots = BigInteger.Zero, rules = BigInteger.Zero, errors = BigInteger.Zero;
        private object lastVerification, lastRuleVerification;
        private VerificationJob verificationJob;
        private object restoredVerificationJob;
        private Thread verificationThread;
        private readonly List<object> ledger = new List<object>();
        private bool fullLedgerRetained = true;

        public TerminalSession(string workspace, bool enableGpu)
        {
            root = Path.GetFullPath(workspace);
            Directory.CreateDirectory(root);
            statePath = Path.Combine(root, "quantum-session-state.json");
            errorPath = Path.Combine(root, "quantum-errors.jsonl");
            json = new JavaScriptSerializer();
            json.MaxJsonLength = 33554432;
            json.RecursionLimit = 80;
            sessionLock = new FileStream(Path.Combine(root, "quantum-session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                if (File.Exists(statePath)) Load();
                else
                {
                    fieldSeed = NewSeed();
                    measurementSeed = NewSeed();
                    Save();
                }
                tranche = new SpinDoctorTrancheTerminal(root, measurementSeed);
                circuit = new CircuitTerminal(root, measurementSeed);
                inputs = new InputAttachmentManager(root);
                calculators = new SpinDoctorCalculators(); advanced = new SpinDoctorAdvanced(); commandReports = new SpinDoctorCommandReports(root);
                sequences = new SequenceTerminal(root, Execute, GetStatus); sequences.SetCapabilityResolver(CommandCapability); sequences.SetFileCompiler(CompileFile); sequences.SetFileCleanup(delegate(Dictionary<string,object> file){ circuit.ReleaseTransientOutput(); /* Compiler-owned arrays are local and released after each file. Durable replay metadata and user inputs are retained. */ });
                commandReports.SetReceiptGuard(delegate(string owner){sequences.SequenceReceiptMetadata(owner);});
                sequences.SetReportCapture(delegate(string type, string id) { return type == "circuit" ? circuit.CaptureJobReport(id) : null; });
                sequences.SetPresetOwnership(delegate(string name, string presetId) { circuit.MarkPresetOwned(name, presetId); });
                foreach (string name in new string[] { "QuantumCore.cs", "QuantumTerminal.cs", "SpinDoctorTranches.cs", "SpinDoctorTrancheTerminal.cs", "SpinDoctorCircuits.cs", "SpinDoctorInputs.cs", "SpinDoctorReports.cs", "SpinDoctorPdfInputs.cs", "SpinDoctorCalculators.cs", "SpinDoctorAdvanced.cs", "SpinDoctorSyntax.cs", "SpinDoctorSequences.cs", "SpinDoctorCommandReports.cs", "SPINDOCTOR_PRESETS.json", "EquationCatalogue.json" })
                {
                    string path = Path.Combine(root, name);
                    if (!File.Exists(path)) path = Path.Combine(Path.Combine(root, "quantum"), name);
                    if (File.Exists(path)) using (SHA256 hash = SHA256.Create()) sourceHashes[name] = Hex(hash.ComputeHash(File.ReadAllBytes(path)));
                }
            }
            catch { sessionLock.Dispose(); throw; }
            if (enableGpu)
            {
                try { gpu = new QuantumGpu(); }
                catch (Exception ex) { gpuError = ex.GetType().Name + ": " + ex.Message; }
            }
        }

        public string Serialize(object value) { return json.Serialize(value); }

        public object GetStatus()
        {
            lock (sync) { EnsureAlive();Dictionary<string,object> snapshot=(Dictionary<string,object>)Status();CompleteReports(snapshot);return snapshot; }
        }

        public string CircuitPrintout()
        {
            CircuitTerminal selected;
            lock (sync) { EnsureAlive(); selected = circuit; }
            // The circuit controller snapshots its histogram under its own short lock.
            // Formatting a full download never holds the family/status lock.
            return selected.MeasurementPrintout();
        }

        public object AttachInput(string filename, string content)
        {
            lock (sync)
            {
                EnsureAlive();
                try { return D("ok", true, "result", inputs.Upload(filename, content), "status", Status()); }
                catch (Exception ex) { return D("ok", false, "error", ex.Message, "status", Status()); }
            }
        }

        public object AttachmentStatus() { lock (sync) { EnsureAlive(); return inputs.GetStatus(); } }
        public string AttachmentExport(string id) { lock (sync) { EnsureAlive(); return inputs.ExportText(id); } }
        public object AttachBinary(string filename, byte[] content)
        {
            lock(sync){EnsureAlive();try{return D("ok",true,"result",inputs.UploadBinary(filename,content),"status",Status());}catch(Exception e){return D("ok",false,"error",e.Message,"status",Status());}}
        }
        public byte[] AttachmentBytes(string id){lock(sync){EnsureAlive();return inputs.ExportBytes(id);}}
        public string AttachmentMediaType(string id){lock(sync){EnsureAlive();return inputs.ExportMediaType(id);}}
        public object Presets(){EnsureAlive();return sequences.Presets();}
        public object CommandHistory(){GetStatus();return commandReports.GetStatus();}
        public object CommandHistoryPage(int offset,int limit){GetStatus();return commandReports.Page(offset,limit);}
        public string CommandPrintout(string id){return CommandPrintout(id,"latest");}
        public string CommandPrintout(string id,string stage){GetStatus();return commandReports.Printout(id,stage);}
        public string CommandJson(string id){return CommandJson(id,"latest");}
        public string CommandJson(string id,string stage){GetStatus();return commandReports.Json(id,stage);}
        public string CommandCsv(string id,string stage){GetStatus();return commandReports.Csv(id,stage);}
        public string SequencePrintout(string id,int? index,string stage){EnsureAlive();return sequences.SequencePrintout(id,index,stage);}
        public string SequenceJson(string id,int? index,string stage){EnsureAlive();return sequences.SequenceJson(id,index,stage);}
        public string SequenceCsv(string id,int? index,string stage){EnsureAlive();return sequences.SequenceCsv(id,index,stage);}
        public string SequencePreviewJson(string id){EnsureAlive();return sequences.SequencePreviewJson(id);}
        public FileStream OpenSequencePrintout(string id,string stage){EnsureAlive();return sequences.OpenSequencePrintout(id,stage);}
        public FileStream OpenSequenceCsv(string id,string stage){EnsureAlive();return sequences.OpenSequenceCsv(id,stage);}
        public object EquationCatalogue()
        {
            EnsureAlive();string path=Path.Combine(root,"EquationCatalogue.json");if(!File.Exists(path))path=Path.Combine(Path.Combine(root,"quantum"),"EquationCatalogue.json");if(!File.Exists(path))throw new FileNotFoundException("Indexed equation catalogue is not installed.");return json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8));
        }
        public object StartSequence(Dictionary<string,object> request)
        {
            if(request==null)throw new ArgumentException("Expected sequence object.");foreach(string key in request.Keys)if(key!="commands"&&key!="presetId"&&key!="parameters")throw new ArgumentException("Unknown sequence request field.");
            string[] commands=null;object value;if(request.TryGetValue("commands",out value)){IEnumerable items=value as IEnumerable;if(items==null||value is string)throw new ArgumentException("commands must be an array.");List<string> list=new List<string>();foreach(object item in items){if(!(item is string))throw new ArgumentException("commands must contain strings.");list.Add((string)item);}commands=list.ToArray();}
            string preset=null;if(request.TryGetValue("presetId",out value)){preset=value as string;if(preset==null)throw new ArgumentException("presetId must be a string.");}
            Dictionary<string,object> parameters=null;if(request.TryGetValue("parameters",out value)){parameters=value as Dictionary<string,object>;if(parameters==null)throw new ArgumentException("parameters must be an object.");}
            lock(sync){EnsureAlive();Stopwatch clock=Stopwatch.StartNew();object result=sequences.Run(commands,preset,parameters);clock.Stop();return Report("q.sequence run "+(preset??"edited"),D("ok",true,"result",result,"status",Status(),"elapsedMs",clock.Elapsed.TotalMilliseconds));}
        }
        private object CommandCapability(string command)
        {
            string[] w=(command??"").Trim().Split(new char[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);if(w.Length==0||command.Length>4096)return D("available",false,"reason","Empty or oversized command.");string ns=w[0].ToLowerInvariant(),action=w.Length>1?w[1].ToLowerInvariant():"";
            if(SpinDoctorCalculators.Supports(ns,action)||SpinDoctorAdvanced.Supports(ns,action)){try{SpinDoctorSyntax.ValidateSyntax(w);}catch(Exception e){return D("available",false,"validated",false,"reason","Preflight data validation: "+e.Message);}return D("available",true,"conditional",ns=="q.query","backend",ns=="q.query"?"selected-coherent-register":"bounded-classical-calculator","validated",true,"reason",ns=="q.query"?"Requires a completed coherent circuit; full joint analysis is bounded to 16 qubits. Fields/domains are checked before any sequence mutation.":"Installed bounded calculator validated by native numerical tests; pure preflight rejects malformed fields/domains before any sequence mutation.");}
            HashSet<string> installed=new HashSet<string>("q.prepare q.migrate q.gate q.state q.verify q.measure q.status q.reset q.export q.tranche q.circuit q.input q.help q.history".Split(' '));if(installed.Contains(ns))return D("available",true,"conditional",ns=="q.tranche"||ns=="q.circuit","validated",true,"backend",ns=="q.circuit"||ns=="q.tranche"?"coherent-factor-sparse-dense":"procedural-product-CPU/OpenCL","reason","Native command syntax/context and physical memory limits still apply.");
            string detail=ns=="q.compile"&&action=="synthesize"?" Clifford+T approximate synthesis to a stated operator-norm tolerance needs a number-theoretic synthesizer (for example Ross-Selinger gridsynth) with independent error verification; SpinDoctor does not include one, so no sequence is fabricated. Installed alternatives: q.compile identity, q.compile classify, q.compile count, q.math gate-matrix.":" Installed commands are listed by q.help and the preset menu; proposed extensions stay disabled until implemented and validated.";return D("available",false,"validated",false,"backend","uninstalled","reason","Proposed command is not implemented and validated: "+ns+" "+action+"."+detail);
        }
        private object CompileFile(string path)
        {
            string resolved=Path.GetFullPath(path ?? "");
            if(!Path.IsPathRooted(path ?? "") || resolved.StartsWith("\\\\",StringComparison.Ordinal))throw new ArgumentException("Folder inputs must use an absolute local-drive path.");
            FileInfo file=new FileInfo(resolved);
            for(DirectoryInfo directory=file.Directory;directory!=null;directory=directory.Parent)if((directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Reparse-point input directories are not accepted.");
            if(!file.Exists||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Input is missing or a reparse point.");
            string extension=file.Extension.ToLowerInvariant();
            if(extension!=".json"&&extension!=".qasm"&&extension!=".qasm2"&&extension!=".txt"&&extension!=".circuit"&&extension!=".csv"&&extension!=".pdf")throw new ArgumentException("Unsupported folder input extension.");
            byte[] bytes;
            using(FileStream input=new FileStream(resolved,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                ValidateOpenedInputPath(input.SafeFileHandle,resolved);
                if(input.Length>4194304)throw new ArgumentException("Folder files are limited to 4 MiB each; use bounded batches for larger inputs.");
                bytes=new byte[checked((int)input.Length)];int offset=0;
                while(offset<bytes.Length){int read=input.Read(bytes,offset,bytes.Length-offset);if(read==0)throw new IOException("Folder input ended before its declared length.");offset+=read;}
                if(input.Length!=bytes.Length)throw new IOException("Folder input size changed during compilation.");
            }
            return CompileBytes(file.Name,bytes);
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
        private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,StringBuilder path,uint length,uint flags);
        [System.Runtime.InteropServices.DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=System.Runtime.InteropServices.CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle OpenPathRoot(string name,uint access,uint sharing,IntPtr security,uint disposition,uint flags,IntPtr template);
        private static void ValidateOpenedInputPath(Microsoft.Win32.SafeHandles.SafeFileHandle handle,string resolved)
        {
            StringBuilder actualPath=new StringBuilder(32768);
            uint length=GetFinalPathNameByHandle(handle,actualPath,(uint)actualPath.Capacity,0);
            if(length>0&&length<actualPath.Capacity)
            {
                string actual=actualPath.ToString();if(actual.StartsWith("\\\\?\\",StringComparison.Ordinal))actual=actual.Substring(4);
                if(actual.StartsWith("UNC\\",StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetFullPath(actual),resolved,StringComparison.OrdinalIgnoreCase))throw new IOException("Input path changed or resolves outside the selected local file.");
                return;
            }
            // Restricted Windows tokens may not query DOS-device mappings. Compare
            // normalized NT paths against an opened drive root without weakening identity checks.
            string driveRoot=Path.GetPathRoot(resolved);
            using(Microsoft.Win32.SafeHandles.SafeFileHandle rootHandle=OpenPathRoot(driveRoot,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero))
            {
                if(rootHandle.IsInvalid)throw new IOException("Could not verify the selected input drive.");
                StringBuilder rootPath=new StringBuilder(32768);actualPath.Length=0;
                uint rootLength=GetFinalPathNameByHandle(rootHandle,rootPath,(uint)rootPath.Capacity,2);
                length=GetFinalPathNameByHandle(handle,actualPath,(uint)actualPath.Capacity,2);
                if(rootLength==0||rootLength>=rootPath.Capacity||length==0||length>=actualPath.Capacity)throw new IOException("Could not verify the opened input path.");
                string expected=rootPath.ToString().TrimEnd('\\')+"\\"+resolved.Substring(driveRoot.Length);
                if(!String.Equals(actualPath.ToString(),expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Input path changed or resolves outside the selected local file.");
            }
        }
        private Dictionary<string,object> CompileBytes(string filename,byte[] bytes)
        {
            Dictionary<string,object> spec=inputs.CompileSource(filename,bytes);Dictionary<string,object> source=(Dictionary<string,object>)spec["source"];List<string> commands=new List<string>();
            if(spec.ContainsKey("commands"))commands.AddRange((string[])spec["commands"]);
            else
            {
                string name="file_"+Guid.NewGuid().ToString("N").Substring(0,16);commands.Add("q.circuit memory auto");commands.Add("q.circuit new "+name+" "+spec["qubits"]);commands.Add("q.circuit input "+spec["initialBasis"]);
                foreach(object item in (object[])spec["gates"]){Dictionary<string,object> gate=(Dictionary<string,object>)item;StringBuilder line=new StringBuilder("q.circuit add "+gate["name"]);foreach(double a in (double[])gate["angles"])line.Append(" ").Append(a.ToString("R",CultureInfo.InvariantCulture));if((bool)gate["all"])line.Append(" all");else foreach(int q in (int[])gate["qubits"])line.Append(" ").Append(q);commands.Add(line.ToString());}
                Dictionary<string,object> hints=(Dictionary<string,object>)spec["hints"];commands.Add("q.circuit run shots "+hints["shots"]+" basis "+hints["basis"]);
            }
            if(commands.Count>128)throw new ArgumentException("Folder circuit expands to more than 128 commands. Import the larger circuit directly with q.input import, or split the sequence into files.");
            return D("commands",commands.ToArray(),"sourceSha256",source["sha256"],"inputName",filename,"sourceFormat",source["format"]);
        }

        public Dictionary<string, object> CircuitReportSnapshot()
        {
            CircuitTerminal selected; Dictionary<string, object> familyStatus;
            lock (sync) { EnsureAlive(); selected = circuit; familyStatus = (Dictionary<string, object>)Status(); }
            Dictionary<string, object> capture = selected.ReportSnapshot();
            familyStatus["circuit"] = capture["status"];
            return D("status", familyStatus, "printout", capture["printout"], "capturedUtc", capture["capturedUtc"]);
        }

        public byte[] ReportPdf(Dictionary<string, object> request)
        {
            string printout;
            Dictionary<string,object> snapshot=PrepareVisualReport(request,out printout);
            return SpinDoctorReportPdf.Build(snapshot,printout);
        }
        public string ReportCsv(Dictionary<string,object> request)
        {
            string printout;
            Dictionary<string,object> snapshot=PrepareVisualReport(request,out printout);
            snapshot["nativeCircuitPrintout"]=printout;
            return SpinDoctorReportCsv.Build(snapshot,(string)snapshot["kind"],"","snapshot");
        }
        public Dictionary<string,object> ReportEquations(Dictionary<string,object> request)
        {
            string printout;Dictionary<string,object> snapshot=PrepareVisualReport(request,out printout);
            return SpinDoctorReportEquations.Build(snapshot);
        }
        private Dictionary<string,object> PrepareVisualReport(Dictionary<string,object> request,out string printout)
        {
            object kind, records, status, view;
            if (request == null || (request.Count !=3 && request.Count !=4) || !request.TryGetValue("kind", out kind) || !(kind is string) || !request.TryGetValue("records", out records) || (!(records is object[]) && !(records is ArrayList)) || !request.TryGetValue("status", out status) || !(status is Dictionary<string, object>) || ((string)kind !="selected" && (string)kind !="session" && (string)kind !="circuit")) throw new ArgumentException("Expected report {kind:selected|session|circuit,records:array,status:object,view?:summary|details|raw|errors|accounting}.");
            if (request.TryGetValue("view", out view))
            {
                string presentation = view as string;
                if (presentation !="summary" && presentation !="details" && presentation !="raw" && presentation !="errors" && presentation !="accounting") throw new ArgumentException("Unknown PDF presentation view.");
            }
            else if (request.Count !=3) throw new ArgumentException("Unknown PDF request field.");
            Dictionary<string, object> snapshot = new Dictionary<string, object>(request);
            // Earlier clients derived PDF views from explorer tabs. Keep those
            // legacy summary aliases while the dedicated control uses three views.
            if(view is string && ((string)view=="errors" ||(string)view=="accounting"))snapshot["view"]="summary";
            snapshot["records"] = ResolvePdfRecords(records);
            Dictionary<string, object> capture = CircuitReportSnapshot();
            if ((string)kind =="circuit") snapshot["status"] = capture["status"];
            // Only matching job IDs use this current native full histogram in historical reports.
            // PDF generation itself is outside the family/status lock and executes no gates.
            printout=(string)capture["printout"];
            return snapshot;
        }

        private object[] ResolvePdfRecords(object raw)
        { return ResolvePdfRecordBatch(raw,new PdfArchiveBudget(),true); }
        private sealed class PdfArchiveBudget
        {
            public long Bytes;
            public int Records;
            public void Add(string raw){Bytes+=Encoding.UTF8.GetByteCount(raw);if(Bytes>67108864)throw new ArgumentException("Resolved PDF archives exceed64MiB. Export smaller command batches.");}
        }
        private object[] ResolvePdfRecordBatch(object raw,PdfArchiveBudget budget,bool hydrateFolders)
        {
            List<object> resolved = new List<object>();
            foreach (object item in (System.Collections.IEnumerable)raw)
            {
                if(++budget.Records>5000)throw new ArgumentException("A PDF export contains at most5000 command records, including hydrated folder commands.");
                Dictionary<string, object> record = item as Dictionary<string, object>;
                if (record == null) throw new ArgumentException("Every PDF record must be an object.");
                Dictionary<string, object> copy = new Dictionary<string, object>(record);
                foreach(string key in new string[]{"hydratedCommandRecords","hydratedCommandScope","commandHydration","hydratedCommandCount","nativeArchiveSha256","acceptedArchiveSha256","selectedArchiveStage","sourceSha256","sourceFilename","sourceFormat","sourcePath","sequenceJobId"})copy.Remove(key);
                object archiveRaw;
                Dictionary<string, object> archive = record.TryGetValue("nativeArchive", out archiveRaw) ? archiveRaw as Dictionary<string, object> : null;
                if (archiveRaw != null && archive == null) throw new ArgumentException("nativeArchive must be an object.");
                if (archive == null)
                {
                    // Supplied numerical snapshots remain reviewable, but cannot
                    // release a native receipt or supply its verification evidence.
                    foreach(string evidenceKey in new string[]{"verificationRequired","verificationPhase","verification","receiptReady","verificationError","verificationSamples","receiptKind","receiptOwnerSequenceId","receiptMetadata"})copy.Remove(evidenceKey);
                    copy["verificationRequired"]=false;copy["verificationPhase"]="unverified-import";copy["verification"]=new Dictionary<string,object>();copy["receiptReady"]=false;copy["receiptKind"]="unverified-import";
                    copy["verificationError"]="No matched native archive supplied; client verification claims are not accepted as native sampling evidence.";
                    copy["recordSource"] = "Client-supplied recorded snapshot; native archive provenance was not verified by this export.";
                    resolved.Add(copy); continue;
                }
                foreach(string evidenceKey in new string[]{"executionOutcome","commandCompletedUtc","completionScope","verificationRequired","verificationPhase","verification","receiptReady","verificationError","verificationSamples","receiptKind","receiptOwnerSequenceId"})copy.Remove(evidenceKey);
                object typeRaw, idRaw, stageRaw;
                if (!archive.TryGetValue("type", out typeRaw) || !(typeRaw is string) || !archive.TryGetValue("id", out idRaw) || !(idRaw is string)) throw new ArgumentException("PDF archive reference requires string type and id.");
                string type = (string)typeRaw, id = (string)idRaw;
                string stage = archive.TryGetValue("stage", out stageRaw) ? stageRaw as string : "latest";
                if (stage != "accepted" && stage != "completed" && stage != "latest") throw new ArgumentException("Unknown PDF archive stage.");
                string initialJson, finalJson = null;
                if (type == "command")
                {
                    if (archive.Count != (archive.ContainsKey("stage") ? 3 : 2)) throw new ArgumentException("Unknown command archive reference field.");
                    initialJson = commandReports.Json(id, "accepted");
                    if (stage != "accepted") finalJson = commandReports.Json(id, stage);
                }
                else if (type == "sequence")
                {
                    object indexRaw; int index;
                    if (!archive.TryGetValue("index", out indexRaw) || !(indexRaw is int) || (index = (int)indexRaw) < 0 || index > 127 || archive.Count != (archive.ContainsKey("stage") ? 4 : 3)) throw new ArgumentException("Sequence archive reference requires an integer index 0..127 and no unknown fields.");
                    initialJson = sequences.SequenceJson(id, index, "accepted");
                    if (stage != "accepted") finalJson = sequences.SequenceJson(id, index, stage);
                }
                else throw new ArgumentException("Unknown PDF archive reference type.");
                budget.Add(initialJson);if(finalJson!=null)budget.Add(finalJson);
                JavaScriptSerializer parser = new JavaScriptSerializer { MaxJsonLength = 33554432, RecursionLimit = 100 };
                Dictionary<string, object> initial = parser.DeserializeObject(initialJson) as Dictionary<string, object>;
                if (initial == null || !initial.ContainsKey("response")) throw new InvalidDataException("Native archive has no response snapshot.");
                ValidatePdfArchiveIdentity(initial,type,id,archive);
                copy["acceptedArchiveSha256"]=HashHex(initialJson);
                copy["nativeArchiveSha256"]=HashHex(initialJson);
                copy["selectedArchiveStage"]="accepted";
                copy["nativeResponse"] = initial["response"]; copy["responseComplete"] = true;
                copy["command"] = initial.ContainsKey("normalizedCommand") ? initial["normalizedCommand"] : initial["command"];
                copy["receivedUtc"] = initial.ContainsKey("receivedUtc") ? initial["receivedUtc"] : initial["capturedUtc"];
                copy.Remove("finalNativeResponse"); copy.Remove("latestStatusResponse"); copy.Remove("completedUtc");
                // Archive stage and execution completion are separate. Synchronous
                // returns keep the observed completion time from their native archive.
                object initialStopped;if(initial.TryGetValue("completedUtc",out initialStopped) &&initialStopped is string &&((string)initialStopped).Length>0)copy["completedUtc"]=initialStopped;
                else if(initial.TryGetValue("commandCompletedUtc",out initialStopped) &&initialStopped is string &&((string)initialStopped).Length>0)copy["completedUtc"]=initialStopped;
                CopyReceiptEvidence(initial,copy);
                if (finalJson != null)
                {
                    Dictionary<string, object> final = parser.DeserializeObject(finalJson) as Dictionary<string, object>;
                    if (final == null) throw new InvalidDataException("Native completion archive is not an object.");
                    ValidatePdfArchiveIdentity(final,type,id,archive);
                    copy["nativeArchiveSha256"]=HashHex(finalJson);
                    copy["selectedArchiveStage"]=PdfText(final,"stage")=="completed" ||final.ContainsKey("finalResponse")?"completed":"accepted";
                    if(PdfText(final,"command")!=PdfText(initial,"command") ||PdfText(final,"normalizedCommand")!=PdfText(initial,"normalizedCommand"))throw new InvalidDataException("Native PDF archive stages refer to different commands.");
                    if (final.ContainsKey("finalResponse")) { copy["finalNativeResponse"] = final["finalResponse"]; copy["completedUtc"] = final["completedUtc"]; }
                    else if (final.ContainsKey("stage") && (string)final["stage"] == "completed") { copy["finalNativeResponse"] = final["response"]; copy["completedUtc"] = final["capturedUtc"]; }
                    CopyReceiptEvidence(final,copy);
                    if(type=="sequence" &&final.ContainsKey("finalResponse"))NormalizeSelectedSequenceCompletion(copy,final);
                }
                if(type=="sequence")
                {
                    CopyReceiptEvidence(sequences.SequenceReceiptMetadata(id),copy);
                    Dictionary<string,object> savedSource=sequences.SequencePdfJobMetadata(id);
                    if(PdfText(savedSource,"sourceSha256").Length==64)
                    {foreach(string key in new string[]{"sourceSha256","sourceFilename","sourceFormat","sourcePath"})copy[key]=PdfText(savedSource,key);copy["sequenceJobId"]=id;}
                }
                else if(type=="command")
                {
                    string owner=commandReports.ReceiptOwner(id);
                    if(owner.Length>0){CopyReceiptEvidence(sequences.SequenceReceiptMetadata(owner),copy);copy["receiptOwnerSequenceId"]=owner;}
                    else{copy["verificationRequired"]=false;copy["verificationPhase"]="not-requested";copy["receiptReady"]=true;copy["receiptKind"]="unverified";}
                }
                copy["finalResponseComplete"] = true;
                copy["recordSource"] = "Verified native archive snapshot: " + type + " " + id + "; stage " + stage + ".";
                CanonicalPdfJob(copy,initial);
                if(hydrateFolders)HydrateFolderPdfCommands(copy,type,id,budget);
                resolved.Add(copy);
            }
            return resolved.ToArray();
        }
        private static void ValidatePdfArchiveIdentity(Dictionary<string,object> value,string type,string id,Dictionary<string,object> reference)
        {
            if(type=="command"){if(PdfText(value,"id")!=id)throw new InvalidDataException("Native PDF command archive identity mismatch.");}
            else if(PdfText(value,"sequenceId")!=id ||!value.ContainsKey("index") ||!(value["index"] is int) ||(int)value["index"]!=(int)reference["index"])throw new InvalidDataException("Native PDF sequence command archive identity mismatch.");
        }
        private static void CanonicalPdfJob(Dictionary<string,object> record,Dictionary<string,object> initial)
        {
            string command=PdfText(record,"command").TrimStart(),type=command.StartsWith("q.circuit",StringComparison.OrdinalIgnoreCase)?"circuit":command.StartsWith("q.tranche",StringComparison.OrdinalIgnoreCase)?"tranche":command.StartsWith("q.verify",StringComparison.OrdinalIgnoreCase)?"verification":"sequence";
            record["jobId"]="";record["jobType"]="";
            Dictionary<string,object> response=PdfMap(PdfValue(initial,"response"));
            if(SpinDoctorCommandReports.AwaitingCompletion(command,response))
            {
                Dictionary<string,object> result=PdfMap(PdfValue(response,"result")),job=PdfMap(PdfValue(result,"job"));if(job.Count==0)job=result;
                record["jobId"]=PdfText(job,"id");record["jobType"]=type;
            }
            record["kind"]=type=="circuit"||type=="tranche"||type=="verification"?type:command.StartsWith("q.folder",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.sequence",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.preset",StringComparison.OrdinalIgnoreCase)?"sequence":command.StartsWith("q.math",StringComparison.OrdinalIgnoreCase)?"calculator":"command";
        }
        private void HydrateFolderPdfCommands(Dictionary<string,object> record,string archiveType,string archiveId,PdfArchiveBudget budget)
        {
            Dictionary<string,object> response=PdfMap(PdfValue(record,"finalNativeResponse"));if(response.Count==0)response=PdfMap(PdfValue(record,"nativeResponse"));
            Dictionary<string,object> folder=PdfMap(PdfValue(response,"result")),nested=PdfMap(PdfValue(folder,"job"));if(nested.Count>0)folder=nested;
            if(PdfBool(folder,"active"))return;
            if(!PdfBool(folder,"folder")){HydrateInputPdfCommands(record,archiveType,archiveId,folder,budget);return;}
            string parentId=PdfText(folder,"id"),phase=PdfText(folder,"phase");bool successful=phase=="completed";
            // Status queries may contain a finished folder snapshot but own no run.
            // Only a native accepted asynchronous run binding can authorize hydration.
            if(archiveType!="command" ||PdfText(record,"jobType")!="sequence" ||PdfText(record,"jobId")!=parentId)return;
            if(commandReports.ReceiptOwner(archiveId)!=parentId)throw new InvalidDataException("Folder PDF archive does not have the matching native receipt owner.");
            // ReadJob/RequireReceipt validate saved identity, archive bounds and genuine verification evidence.
            // The folder and all numerical responses below remain the captured historical snapshots.
            Dictionary<string,object> savedParent=sequences.SequencePdfJobMetadata(parentId);budget.Add(json.Serialize(savedParent));
            if(PdfText(savedParent,"id")!=parentId ||!PdfBool(savedParent,"folder") ||PdfText(savedParent,"completedUtc")!=PdfText(folder,"completedUtc") ||PdfText(savedParent,"phase")!=phase)throw new InvalidDataException("Folder PDF completion does not match its saved native job.");
            List<object> files=PdfItems(PdfValue(folder,"files")),savedFiles=PdfItems(PdfValue(savedParent,"files"));int totalFiles=PdfInt(folder,"totalFiles",0,128),requested=PdfInt(folder,"totalCommands",0,16384);
            if(files.Count>128 ||files.Count>totalFiles ||savedFiles.Count!=files.Count ||successful&&files.Count!=totalFiles)throw new InvalidDataException("Folder PDF file coverage is inconsistent.");
            List<object> records=new List<object>(),errors=new List<object>();HashSet<string> children=new HashSet<string>(StringComparer.Ordinal);int lastFile=-1,filePosition=0;
            foreach(object rawFile in files)
            {
                Dictionary<string,object> file=PdfMap(rawFile);int fileIndex=PdfInt(file,"index",0,127);if(fileIndex<=lastFile ||fileIndex>=totalFiles)throw new InvalidDataException("Folder PDF file order is inconsistent.");lastFile=fileIndex;
                Dictionary<string,object> savedFile=PdfMap(savedFiles[filePosition++]);
                foreach(string key in new string[]{"childJobId","sourceSha256","path","filename","executionPhase"})if(PdfText(savedFile,key)!=PdfText(file,key))throw new InvalidDataException("Folder PDF file identity differs from its saved native snapshot.");
                string childId=PdfText(file,"childJobId");if(childId.Length==0){if(successful)throw new InvalidDataException("Completed folder PDF is missing a child command archive.");errors.Add("File "+PdfText(file,"filename")+" has no captured child command archive.");continue;}
                if(!children.Add(childId))throw new InvalidDataException("Folder PDF repeats a child job identity.");
                Dictionary<string,object> child=sequences.SequencePdfJobMetadata(childId);budget.Add(json.Serialize(child));
                if(PdfText(child,"id")!=childId ||PdfText(child,"parentJobId")!=parentId ||PdfText(child,"sourceSha256")!=PdfText(file,"sourceSha256") ||PdfText(child,"sourcePath")!=PdfText(file,"path") ||PdfText(child,"sourceFilename")!=PdfText(file,"filename") ||PdfBool(child,"folder") ||PdfBool(child,"active") ||PdfBool(child,"verificationRequired")!=PdfBool(folder,"verificationRequired"))throw new InvalidDataException("Folder PDF child source/parent identity or receipt policy mismatch.");
                int total=PdfInt(child,"totalCommands",0,128),completed=PdfInt(child,"completedCommands",0,128);List<object> steps=PdfItems(PdfValue(child,"records"));
                if(steps.Count>128 ||steps.Count>total ||total!=PdfInt(file,"totalCommands",0,128) ||completed!=PdfInt(file,"completedCommands",0,128) ||successful&&(PdfText(child,"phase")!="completed" ||steps.Count!=total ||completed!=total ||!PdfBool(child,"receiptReady")))throw new InvalidDataException("Folder PDF child command completion coverage is inconsistent.");
                int lastIndex=-1;
                foreach(object rawStep in steps)
                {
                    Dictionary<string,object> step=PdfMap(rawStep);int index=PdfInt(step,"index",0,127);if(index<=lastIndex ||index>=total ||PdfText(step,"sequenceId")!=childId)throw new InvalidDataException("Folder PDF command identity or order is inconsistent.");lastIndex=index;
                    if(PdfText(step,"reportJsonFile").Length==0){if(successful)throw new InvalidDataException("Completed folder PDF is missing an immutable command return.");errors.Add("File "+PdfText(file,"filename")+", command "+index+" has no archived native return.");continue;}
                    Dictionary<string,object> reference=D("type","sequence","id",childId,"index",index,"stage","latest");
                    Dictionary<string,object> hydrated=(Dictionary<string,object>)ResolvePdfRecordBatch(new object[]{D("nativeArchive",reference)},budget,false)[0];
                    if(PdfText(hydrated,"command")!=PdfText(step,"normalizedCommand"))throw new InvalidDataException("Folder PDF command differs from the native saved sequence command.");
                    Dictionary<string,object> returned=PdfMap(PdfValue(hydrated,"finalNativeResponse"));if(returned.Count==0)returned=PdfMap(PdfValue(hydrated,"nativeResponse"));
                    if(successful&&(!PdfBool(returned,"ok") ||PdfText(step,"phase")!="completed"))throw new InvalidDataException("Completed folder PDF contains a non-completed native command return.");
                    hydrated["sourceFileIndex"]=fileIndex;hydrated["commandIndex"]=index;hydrated["sequenceIndex"]=index;hydrated["sequenceJobId"]=childId;hydrated["parentSequenceJobId"]=parentId;
                    hydrated["sourceFilename"]=PdfText(file,"filename");hydrated["sourcePath"]=PdfText(file,"path");hydrated["sourceSha256"]=PdfText(file,"sourceSha256");records.Add(hydrated);
                }
            }
            if(successful&&(records.Count!=requested ||records.Count!=PdfInt(folder,"completedCommands",0,16384)))throw new InvalidDataException("Completed folder PDF does not resolve every native command answer.");
            string scope="Separate PDF presentation collection loaded in file/command order from guarded, hash-checked immutable child command archives for this captured folder. Original parent native payload and its empty records array are unchanged. No current model/status or client-supplied answers are substituted.";
            record["hydratedCommandRecords"]=records.ToArray();record["hydratedCommandCount"]=records.Count;record["hydratedCommandScope"]=scope;
            record["commandHydration"]=D("requestedRecords",requested,"resolvedRecords",records.Count,"missingCount",Math.Max(0,requested-records.Count),"complete",successful&&records.Count==requested,"partial",!successful,"errors",errors.ToArray(),"scope",scope);
        }
        private void HydrateInputPdfCommands(Dictionary<string,object> record,string archiveType,string archiveId,Dictionary<string,object> observed,PdfArchiveBudget budget)
        {
            if(archiveType!="command" ||!PdfText(record,"command").StartsWith("q.input run ",StringComparison.OrdinalIgnoreCase) ||PdfText(record,"jobType")!="sequence")return;
            string id=PdfText(observed,"id"),phase=PdfText(observed,"phase");
            if(id.Length==0 ||PdfText(record,"jobId")!=id)return;
            if(commandReports.ReceiptOwner(archiveId)!=id)throw new InvalidDataException("Input-run PDF archive has no matching native receipt owner.");
            Dictionary<string,object> saved=sequences.SequencePdfJobMetadata(id);budget.Add(json.Serialize(saved));
            foreach(string key in new string[]{"id","phase","completedUtc","sourceSha256","sourceFilename","sourceFormat"})if(PdfText(saved,key)!=PdfText(observed,key))throw new InvalidDataException("Input-run PDF differs from its saved native source/completion.");
            if(PdfBool(saved,"folder") ||PdfBool(saved,"active") ||PdfText(saved,"sourceSha256").Length!=64)throw new InvalidDataException("Input-run PDF has no completed native source binding.");
            int total=PdfInt(saved,"totalCommands",0,128),completed=PdfInt(saved,"completedCommands",0,128);bool successful=phase=="completed";
            if(total!=PdfInt(observed,"totalCommands",0,128) ||completed!=PdfInt(observed,"completedCommands",0,128))throw new InvalidDataException("Input-run PDF command counts differ from its saved job.");
            List<object> steps=PdfItems(PdfValue(saved,"records")),records=new List<object>(),errors=new List<object>();int previous=-1;
            if(steps.Count>total ||successful&&(steps.Count!=total ||completed!=total))throw new InvalidDataException("Input-run PDF completion coverage is inconsistent.");
            foreach(object raw in steps)
            {
                Dictionary<string,object> step=PdfMap(raw);int index=PdfInt(step,"index",0,127);
                if(index<=previous ||index>=total ||PdfText(step,"sequenceId")!=id)throw new InvalidDataException("Input-run PDF command identity/order is inconsistent.");previous=index;
                if(PdfText(step,"reportJsonFile").Length==0){if(successful)throw new InvalidDataException("Completed input-run PDF is missing an immutable command return.");errors.Add("Command "+index+" has no captured return.");continue;}
                Dictionary<string,object> child=(Dictionary<string,object>)ResolvePdfRecordBatch(new object[]{D("nativeArchive",D("type","sequence","id",id,"index",index,"stage","latest"))},budget,false)[0];
                if(PdfText(child,"command")!=PdfText(step,"normalizedCommand"))throw new InvalidDataException("Input-run PDF command differs from its native sequence.");
                Dictionary<string,object> response=PdfMap(PdfValue(child,"finalNativeResponse"));if(response.Count==0)response=PdfMap(PdfValue(child,"nativeResponse"));
                if(successful&&(!PdfBool(response,"ok") ||PdfText(step,"phase")!="completed"))throw new InvalidDataException("Completed input-run PDF contains a non-completed command return.");
                child["commandIndex"]=index;child["sequenceIndex"]=index;child["parentSequenceJobId"]=id;records.Add(child);
            }
            string scope="Ordered actual command answers from hash-checked native input-run archives for this exact source CSV/program and completed job. Original parent payload is retained. Export performs no calculation.";
            foreach(string key in new string[]{"sourceSha256","sourceFilename","sourceFormat","sourcePath"})record[key]=PdfText(saved,key);
            record["hydratedCommandRecords"]=records.ToArray();record["hydratedCommandCount"]=records.Count;record["hydratedCommandScope"]=scope;
            record["commandHydration"]=D("kind","input-run","requestedRecords",total,"resolvedRecords",records.Count,"missingCount",Math.Max(0,total-records.Count),"complete",successful&&records.Count==total,"partial",!successful,"errors",errors.ToArray(),"scope",scope);
        }
        private static string PdfText(Dictionary<string,object> value,string key){object found;return value.TryGetValue(key,out found)?found as string??"":"";}
        private static object PdfValue(Dictionary<string,object> value,string key){object found;return value.TryGetValue(key,out found)?found:null;}
        private static Dictionary<string,object> PdfMap(object value){return value as Dictionary<string,object>??new Dictionary<string,object>();}
        private static bool PdfBool(Dictionary<string,object> value,string key){object found;return value.TryGetValue(key,out found)&&found is bool&&(bool)found;}
        private static List<object> PdfItems(object value){List<object> result=new List<object>();System.Collections.IEnumerable items=value as System.Collections.IEnumerable;if(items!=null&&!(value is string)&&!(value is System.Collections.IDictionary))foreach(object item in items)result.Add(item);return result;}
        private static int PdfInt(Dictionary<string,object> value,string key,int minimum,int maximum){object found;if(!value.TryGetValue(key,out found)||!(found is int)||(int)found<minimum||(int)found>maximum)throw new InvalidDataException("Invalid native PDF integer field: "+key);return (int)found;}
        private static void CopyReceiptEvidence(Dictionary<string,object> archive,Dictionary<string,object> record)
        {
            foreach(string key in new string[]{"executionOutcome","commandCompletedUtc","completionScope","verificationRequired","verificationPhase","verification","receiptReady","verificationError","verificationSamples","receiptKind"})
            {object value;if(archive.TryGetValue(key,out value))record[key]=value;}
        }

        private static void NormalizeSelectedSequenceCompletion(Dictionary<string,object> record,Dictionary<string,object> selected)
        {
            // The matched completed archive supplies the observation barrier. Its
            // accepted response may still contain the original pending metadata.
            // Normalize presentation fields only; retain every archived byte.
            Dictionary<string,object> response=PdfMap(PdfValue(selected,"finalResponse")),result=PdfMap(PdfValue(response,"result"));
            bool unknown=selected.ContainsKey("nativeCompletionKnown")&&!PdfBool(selected,"nativeCompletionKnown") ||response.ContainsKey("nativeCompletionKnown")&&!PdfBool(response,"nativeCompletionKnown") ||result.ContainsKey("nativeCompletionKnown")&&!PdfBool(result,"nativeCompletionKnown");
            if(unknown)
            {record["nativeCompletionKnown"]=false;record["executionOutcome"]="unknown";record["commandCompletedUtc"]="";record["completionScope"]="Native completion unknown; matched archive contains failure diagnostics only.";return;}
            string stopped=PdfText(selected,"completedUtc");if(response.Count==0 ||stopped.Length==0 ||PdfBool(result,"active") ||PdfBool(result,"queued"))return;
            string phase=PdfText(result,"phase");if(phase.Length==0)phase=PdfText(PdfMap(PdfValue(result,"job")),"phase");
            if(phase!="completed" &&phase!="failed" &&phase!="cancelled" &&phase!="interrupted")phase=PdfBool(response,"ok")?"completed":"failed";
            record["nativeCompletionKnown"]=true;record["executionOutcome"]=phase;record["commandCompletedUtc"]=stopped;
            record["completionScope"]="Matched selected native sequence completion barrier observed at this UTC; this is execution evidence, not independent numerical verification.";
        }

        private object InputCommand(string[] words)
        {
            if(words.Length==3 && String.Equals(words[1],"run",StringComparison.OrdinalIgnoreCase)){Dictionary<string,object> compiled=CompileBytes(inputs.Filename(words[2]),inputs.ExportBytes(words[2]));return sequences.RunSource((string[])compiled["commands"],(string)compiled["sourceSha256"],(string)compiled["inputName"],(string)compiled["sourceFormat"]);}
            if (words.Length <2 || !String.Equals(words[1], "import", StringComparison.OrdinalIgnoreCase)) return inputs.Execute(words);
            if (words.Length <3) throw new ArgumentException("Use q.input import ID [row N] [base BASE_ID] [name=value ...].");
            int? row = null; string baseId = null; Dictionary<string, double> bindings = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int at = 3; at < words.Length; at++)
            {
                if (String.Equals(words[at], "row", StringComparison.OrdinalIgnoreCase))
                {
                    int n; if (row.HasValue || ++at >= words.Length || !Int32.TryParse(words[at], NumberStyles.None, CultureInfo.InvariantCulture, out n) || n <0) throw new ArgumentException("Supply one nonnegative CSV row index.");
                    row = n;
                }
                else if (String.Equals(words[at], "base", StringComparison.OrdinalIgnoreCase))
                {
                    if (baseId !=null || ++at >=words.Length) throw new ArgumentException("Supply one base circuit attachment ID."); baseId = words[at];
                }
                else
                {
                    int equal = words[at].IndexOf('='); double value;
                    if (equal <=0 || equal ==words[at].Length -1 || !Double.TryParse(words[at].Substring(equal +1), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !QuantumMath.IsFinite(value)) throw new ArgumentException("Import bindings use name=finite_number. Use q.input evaluate for expressions.");
                    string name = words[at].Substring(0, equal); if (bindings.ContainsKey(name)) throw new ArgumentException("Duplicate parameter override."); bindings.Add(name, value);
                }
            }
            Dictionary<string, object> spec = inputs.ImportSpec(words[2], row, bindings, baseId) as Dictionary<string, object>;
            if (spec ==null) throw new InvalidDataException("Input did not resolve to a canonical circuit specification.");
            return circuit.ImportCircuit(spec, BeforeCircuitMutation());
        }

        public object Execute(string command)
        {
            lock (sync)
            {
                EnsureAlive();
                CompleteReports((Dictionary<string,object>)Status());
                Stopwatch watch = Stopwatch.StartNew();
                bool persistFamily=false;
                try
                {
                    if (command == null || command.Length > 4096) throw new ArgumentException("Command must be 1-4096 characters.");
                    string[] words = command.Trim().Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0) throw new ArgumentException("Enter a q. command.");
                    string action = words[0].ToLowerInvariant();
                    persistFamily=action=="q.prepare"||action=="q.migrate"||action=="q.gate"||action=="q.state"||action=="q.verify"||action=="q.measure"||action=="q.reset";
                    if(sequences!=null && !sequences.IsWorkerThread && SequenceActive() && !ReadOnlyDuringSequence(words))throw new InvalidOperationException("A sequence owns the calculation context. Wait or use q.sequence cancel before issuing another mutation.");
                    if (JobActive() && (action == "q.prepare" || action == "q.migrate" || action == "q.gate" || action == "q.reset")) throw new InvalidOperationException("Verification is using a snapshot of this descriptor. Cancel or finish it before changing the model.");
                    object result;
                    switch (words[0].ToLowerInvariant())
                    {
                        case "q.prepare": result = Prepare(words); break;
                        case "q.migrate": result = Migrate(words); break;
                        case "q.gate": result = Gate(words); break;
                        case "q.state": result = State(words); break;
                        case "q.verify": result = Verify(words); break;
                        case "q.measure": result = Measure(words); break;
                        case "q.status": Exact(words, 1); result = Status(); break;
                        case "q.export": Exact(words, 2); Need(words[1], "descriptor"); result = Descriptor(); break;
                        case "q.reset": Exact(words, 1); result = Reset(); break;
                        case "q.tranche": result = AutoMemory(words)?SetAutoMemory(true):tranche.Execute(words, m, l, migration, matrix, DescriptorHash()); break;
                        case "q.circuit": result = AutoMemory(words)?SetAutoMemory(false):circuit.ExecuteAndSnapshot(words, BeforeCircuitMutation()); break;
                        case "q.input": result = InputCommand(words); break;
                        case "q.math": case "q.resource": case "q.model": case "q.lesson": result=calculators.Execute(words); break;
                        case "q.query": result=circuit.WithQueryView(SpinDoctorCalculators.MaximumFullQueryQubits,SpinDoctorCalculators.MaximumFullQueryStates,delegate(CircuitQueryView v){return calculators.Execute(words,v.Qubits,v.Amplitude,v.Spin,v.ProgramHash);}); break;
                        case "q.noise": case "q.qec": case "q.compile": case "q.algorithm": result=advanced.Execute(words); break;
                        case "q.sequence": case "q.preset": case "q.folder": result=sequences.Execute(words,command); break;
                        case "q.history": result=commandReports.Execute(words); break;
                        case "q.help": Exact(words, 1); result = Help(); break;
                        default: throw new ArgumentException("Unknown quantum command. Use q.help.");
                    }
                    if(persistFamily)Save();
                    watch.Stop();
                    return Report(command,D("ok", true, "command", command.Trim(), "result", result, "status", Status(), "elapsedMs", watch.Elapsed.TotalMilliseconds));
                }
                catch (Exception ex)
                {
                    // Calculation counters and consumed measurement randomness survive command failure.
                    string failure=ex.Message;
                    if(persistFamily)try{Save();}catch(Exception checkpoint){failure+=" Checkpoint persistence also failed: "+checkpoint.Message;}
                    watch.Stop();
                    return Report(command,D("ok", false, "command", command == null ? "" : command.Trim(), "error", failure, "status", Status(), "elapsedMs", watch.Elapsed.TotalMilliseconds));
                }
            }
        }
        private Dictionary<string,object> Report(string command,Dictionary<string,object> response)
        {
            try{Dictionary<string,object> result=response.ContainsKey("result")?response["result"] as Dictionary<string,object>:null;Dictionary<string,object> job=result!=null&&result.ContainsKey("job")?result["job"] as Dictionary<string,object>:result;
                string receiptOwner=sequences==null?"":sequences.CurrentReceiptOwnerId;
                if(receiptOwner.Length==0&&Regex.IsMatch(command??"",@"^\s*q\.(?:folder|input)\s+run(?:\s|$)",RegexOptions.IgnoreCase)&&job!=null){object id;if(job.TryGetValue("id",out id)&&id is string)receiptOwner=(string)id;}
                // 3.1.2: read-only job polling (q.folder/q.sequence status) archives a compact status
                // instead of repeating the full session snapshot on every poll. The caller still
                // receives the complete live response; q.status archives the full snapshot.
                Dictionary<string,object> archived=response;
                if(response.ContainsKey("status")&&Regex.IsMatch(command??"",@"^\s*q\.(?:folder|sequence)\s+status\s*$",RegexOptions.IgnoreCase)){archived=new Dictionary<string,object>(response);archived["status"]=D("archiveCompacted",true,"note","Full session status omitted from this polling report; result.job is the complete folder/sequence job. Use q.status for a full snapshot.");}
                Dictionary<string,object> report=commandReports.Record(command,archived,receiptOwner);
                if(SpinDoctorCommandReports.AwaitingCompletion(command,response)&&job!=null&&job.ContainsKey("id")&&job.ContainsKey("active")&&(bool)job["active"]){string normalized=(command??"").TrimStart();string type=normalized.StartsWith("q.circuit",StringComparison.OrdinalIgnoreCase)?"circuit":normalized.StartsWith("q.tranche",StringComparison.OrdinalIgnoreCase)?"tranche":normalized.StartsWith("q.verify",StringComparison.OrdinalIgnoreCase)?"verification":"sequence";commandReports.BindJob((string)report["id"],type,(string)job["id"]);}
                response["reportId"]=report["id"];response["archive"]=report;
            }catch(Exception e){response["reportError"]="Calculation return retained; native report persistence failed: "+e.Message;}return response;
        }
        private Action<Dictionary<string,object>> BeforeCircuitMutation()
        {
            // Execute already owns the session lock. Acquire other controller snapshots
            // here, before the native circuit lock, to keep the lock order unchanged.
            Dictionary<string,object> prior=(Dictionary<string,object>)Status();
            return delegate(Dictionary<string,object> circuitStatus)
            {
                prior["circuit"]=circuitStatus;
                CompleteReports(prior,true);
            };
        }
        private void CompleteReports(Dictionary<string,object> status)
        { CompleteReports(status,false); }
        private void CompleteReports(Dictionary<string,object> status,bool requireCircuitArchive)
        {
            foreach(Dictionary<string,object> item in commandReports.Pending())
            {
                string type=(string)item["jobType"];object raw=type=="verification"?status["lastVerificationJob"]:type=="circuit"?((Dictionary<string,object>)status["circuit"])["result"]:type=="tranche"?((Dictionary<string,object>)status["tranche"])["job"]:((Dictionary<string,object>)status["sequence"])["job"];Dictionary<string,object> job=raw as Dictionary<string,object>;if(job==null||!job.ContainsKey("id")||(string)job["id"]!=(string)item["jobId"]||job.ContainsKey("active")&&(bool)job["active"])continue;
                string phase=job.ContainsKey("phase")?(string)job["phase"]:"completed";try{if(type=="circuit")job=circuit.CaptureJobReport((string)item["jobId"]);bool success=phase=="completed"&&(!job.ContainsKey("passed")||(bool)job["passed"])&&(!job.ContainsKey("error")||String.IsNullOrEmpty(Convert.ToString(job["error"],CultureInfo.InvariantCulture)))&&(!job.ContainsKey("checkpointError")||String.IsNullOrEmpty(Convert.ToString(job["checkpointError"],CultureInfo.InvariantCulture)));commandReports.Complete((string)item["id"],D("ok",success,"command",item["command"],"error",success?null:(object)("Native job ended "+phase+". "+(job.ContainsKey("error")?job["error"]:"")),"result",job,"status",status,"elapsedMs",job.ContainsKey("elapsedMs")?job["elapsedMs"]:null));}catch(Exception e){status["commandReportError"]=e.Message;if(requireCircuitArchive&&type=="circuit")throw new InvalidOperationException("The completed circuit receipt must be saved before changing its program or starting another run. "+e.Message,e);}
            }
        }
        private static bool AutoMemory(string[] w){return w.Length==3&&w[1].Equals("memory",StringComparison.OrdinalIgnoreCase)&&w[2].Equals("auto",StringComparison.OrdinalIgnoreCase);}
        private object SetAutoMemory(bool useTranche){Dictionary<string,object> plan=(Dictionary<string,object>)calculators.Execute(new string[]{"q.resource","auto"});ulong bytes=UInt((string)plan["usablePeakBytes"]),mib=bytes/1048576;if(mib<2)throw new InvalidOperationException("Available RAM does not leave 2 MiB after the OS/overhead reserve.");string[] w=new string[]{useTranche?"q.tranche":"q.circuit","memory",mib.ToString(CultureInfo.InvariantCulture)};object applied=useTranche?tranche.Execute(w,m,l,migration,matrix,DescriptorHash()):circuit.ExecuteAndSnapshot(w,BeforeCircuitMutation());return D("mode","auto","plan",plan,"applied",applied,"allocationPerformed",false,"scope","Momentary measured usable-RAM budget; only arrays required for actual computation are allocated. Physical memory is never credited as simulated work.");}
        private bool SequenceActive(){Dictionary<string,object> status=sequences.GetStatus();Dictionary<string,object> job=status["job"] as Dictionary<string,object>;return job!=null && job.ContainsKey("active") && (bool)job["active"];}
        private static bool ReadOnlyDuringSequence(string[] w){string ns=w[0].ToLowerInvariant(),a=w.Length>1?w[1].ToLowerInvariant():"";return ns=="q.status"||ns=="q.help"||ns=="q.history"||ns=="q.export"||((ns=="q.sequence"||ns=="q.folder")&&(a=="status"||a=="cancel"||a=="help"||a=="export"))||ns=="q.preset"&&(a=="list"||a=="show");}

        private object Prepare(string[] words)
        {
            Exact(words, 4); Need(words[1], "grid");
            ulong nextM = UInt(words[2]), nextL = UInt(words[3]);
            ValidateGrid(nextM, nextL);
            m = nextM; l = nextL; migration = 0; matrix = QuantumMath.Identity; prepared = true;
            NewGeneration("prepare");
            return D("gridM", S(m), "gridL", S(l), "stateCount", S(Count()), "migrationT", migration, "modelGeneration", S(generation), "representedCredit", "0", "note", "Grid descriptor defined. q.migrate commits modeled preparations; sampled calculations are counted separately.");
        }

        private object Migrate(string[] words)
        {
            Exact(words, 3); Need(words[1], "t");
            double next = Number(words[2]);
            if (next < 0 || next > 1) throw new ArgumentException("Migration t must be in [0,1].");
            migration = next; matrix = QuantumMath.Identity; prepared = true;
            BigInteger credit = new BigInteger(Count());
            preparations += credit; lifetimePreparations += credit;
            AddLedger("prepare-u-family", D("migrationT", migration, "representedPreparations", S(credit), "matrixReset", true));
            return D("migrationT", migration, "representedPreparations", S(credit), "representedRotationEquivalent", S(credit * 2), "descriptorHash", DescriptorHash(), "note", "Committed one procedural U(theta,phi,0) preparation per address. Earlier composed gates are reset; no domain enumeration occurred.");
        }

        private object Gate(string[] words)
        {
            if (words.Length < 3) throw new ArgumentException("Use q.gate NAME [angles] all.");
            Need(words[words.Length - 1], "all");
            string name = words[1].ToLowerInvariant();
            int nargs = name == "u" || name == "u3" ? 3 : (name == "ry" || name == "rz" ? 1 : 0);
            if (words.Length != nargs + 3) throw new ArgumentException("Incorrect number of gate angles. U/U3 take theta phi lambda; ry/rz take one angle; h/x/y/z/s/t/i take none.");
            double[] angles = new double[nargs];
            for (int i = 0; i < nargs; i++) angles[i] = Number(words[i + 2]);
            double[] gate = QuantumMath.MatrixForGate(name, angles);
            double[] next = QuantumMath.Multiply(gate, matrix);
            double error = QuantumMath.UnitarityError(next);
            if (!Finite(error) || error > 1e-9) throw new InvalidOperationException("Composed matrix failed the numeric unitarity check; command was not committed.");
            matrix = next; prepared = true;
            BigInteger credit = new BigInteger(Count()); gates += credit; lifetimeGates += credit;
            AddLedger("shared-single-qubit-gate", D("gate", name, "angles", angles, "representedGateApplications", S(credit)));
            return D("gate", name, "angles", angles, "matrix", (double[])matrix.Clone(), "unitarityError", error, "representedGateApplications", S(credit), "descriptorHash", DescriptorHash(), "scope", "One shared single-qubit matrix composition represents independent gate application at every address.");
        }

        private object State(string[] words)
        {
            Exact(words, 2); ulong id = Address(words[1]);
            double[] state = QuantumMath.State(id, m, l, migration, matrix); actualCpu++;
            ValidateState(state);
            return StateResult(id, state);
        }

        private object Verify(string[] words)
        {
            if (words.Length == 2 && words[1].Equals("rule", StringComparison.OrdinalIgnoreCase)) return VerifyRule();
            if (words.Length == 2 && words[1].Equals("status", StringComparison.OrdinalIgnoreCase)) return VerificationJobStatus();
            if (words.Length == 2 && words[1].Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                bool active = JobActive();
                if (active) verificationJob.CancelRequested = true;
                return D("cancelRequested", active, "job", VerificationJobStatus());
            }
            Exact(words, 4); Need(words[1], "samples"); Need(words[3], "errors-only");
            BigInteger requested;
            if (words[2].Length>1024 || !BigInteger.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out requested) || requested <= BigInteger.Zero) throw new ArgumentException("Sample count must be a positive decimal integer of at most1024 digits. Distinct coverage is naturally bounded by the grid.");
            if (JobActive()) throw new InvalidOperationException("A verification job is already active. Use q.verify status or q.verify cancel.");
            VerificationPlan plan = new VerificationPlan(m, l, requested, fieldSeed, DescriptorHash());
            verificationJob = new VerificationJob(plan, migration, matrix, prepared, generation);
            restoredVerificationJob = null;
            if (plan.Planned > 16384)
            {
                VerificationJob queued = verificationJob;
                try
                {
                    verificationThread = new Thread(delegate() { RunVerification(queued); });
                    verificationThread.IsBackground = true;verificationThread.Start();
                }
                catch(Exception ex)
                {
                    verificationThread=null;queued.Active=false;queued.Phase="failed";queued.Stage="failed";queued.Failure="Verification worker could not start: "+ex.GetType().Name+": "+ex.Message;queued.CompletedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);queued.Watch.Stop();lastVerification=VerificationJobStatus();throw;
                }
                return D("queued", true, "job", VerificationJobStatus(), "note", "Actual CPU/GPU verification streams in bounded batches. Poll q.verify status or cancel with q.verify cancel; no represented gate credit is added.");
            }
            // Small compatibility runs execute synchronously. Execute holds the session
            // lock; Monitor is reentrant and no request is blocked by a large traversal.
            RunVerification(verificationJob);
            return lastVerification;
        }

        private object VerifyRule()
        {
            Stopwatch watch = Stopwatch.StartNew();
            double error = QuantumMath.UnitarityError(matrix);
            bool passed = Finite(error) && error <= 1e-9;
            Dictionary<string, object> proof = D(
                "schema", "QUETTAFLOP/PRODUCT-RULE-PROOF/1",
                "normalizedSpinor", "cos(t*theta/2)^2 + sin(t*theta/2)^2 = 1; |exp(i*t*phi)| = 1 for real t, theta and phi.",
                "indexing", "id = ell*gridM + k; k = id mod gridM; ell = floor(id/gridM); 0 <= id < gridM*gridL.",
                "sharedGateComposition", "A product of unitary single-qubit matrices is unitary: (VU)^dagger(VU) = I.",
                "spinIdentity", "X=2Re(conj(alpha)beta), Y=2Im(conj(alpha)beta), Z=|alpha|^2-|beta|^2; X^2+Y^2+Z^2=1 for a normalized pure spinor.",
                "symbolicScope", "The indexed real-angle mathematical family and exact unitary compositions; no enumeration or proof of every floating-point implementation result.",
                "numericMatrixTolerance", 1e-9, "numericMatrixUnitarityError", error,
                "passed", passed, "descriptorHash", DescriptorHash(),
                "stateCount", S(Count()), "individuallyVerifiedStateCount", "0");
            string proofHash = HashHex(json.Serialize(proof));
            watch.Stop(); rules++;
            lastRuleVerification = D("passed", passed, "proof", proof, "proofHash", proofHash, "ruleCheckMs", watch.Elapsed.TotalMilliseconds, "scope", "Constant-size algebraic rule audit plus current floating-point 2x2 matrix check.");
            if (!passed) LogError(D("type", "rule-matrix", "descriptorHash", DescriptorHash(), "unitarityError", error));
            return lastRuleVerification;
        }

        private sealed class VerificationPlan
        {
            public readonly ulong M, L, Domain, RequestedClipped, Planned;
            public readonly BigInteger Requested;
            public readonly string Seed, Descriptor;
            public readonly bool Exhaustive;
            public readonly List<ulong> Boundaries = new List<ulong>();
            private readonly HashSet<ulong> boundarySet = new HashSet<ulong>();
            private readonly HashSet<ulong> blockedStrata = new HashSet<ulong>();
            public VerificationPlan(ulong gridM, ulong gridL, BigInteger requested, string seed, string descriptor)
            {
                M = gridM; L = gridL; Domain = checked(M * L); Requested = requested;
                Seed = seed; Descriptor = descriptor; Exhaustive = requested >= new BigInteger(Domain);
                RequestedClipped = Exhaustive ? Domain : (ulong)requested;
                if (Exhaustive) { Planned = Domain; return; }
                ulong[] candidates = new ulong[] { 0, M - 1, M, Domain - M, Domain - 1, Domain / 2, Domain / 2 == 0 ? 0 : Domain / 2 - 1, M / 2, M + M / 2, (L - 1) * M + M / 2, (L / 2) * M + M - 1, (L / 2) * M + M / 2 };
                foreach (ulong id in candidates) if (id < Domain) boundarySet.Add(id);
                Boundaries.AddRange(boundarySet); Boundaries.Sort();
                Dictionary<ulong, int> blockedCounts = new Dictionary<ulong, int>();
                BigInteger domain = new BigInteger(Domain), sampleCount = new BigInteger(RequestedClipped);
                foreach (ulong id in Boundaries)
                {
                    // Invert the floored partition exactly, including its first address.
                    ulong stratum = (ulong)(((new BigInteger(id) + 1) * sampleCount - 1) / domain);
                    int old; blockedCounts.TryGetValue(stratum, out old); blockedCounts[stratum] = old + 1;
                }
                foreach (KeyValuePair<ulong, int> pair in blockedCounts)
                {
                    ulong begin = (ulong)(domain * pair.Key / sampleCount);
                    ulong end = (ulong)(domain * (pair.Key + 1) / sampleCount);
                    if (end - begin == (ulong)pair.Value) blockedStrata.Add(pair.Key);
                }
                Planned = checked(RequestedClipped - (ulong)blockedStrata.Count + (ulong)Boundaries.Count);
                if (Planned > Domain) throw new InvalidOperationException("Sampling coverage exceeded the finite domain.");
            }

            public IEnumerable<ulong> Addresses()
            {
                if (Exhaustive) { for (ulong id = 0; id < Domain; id++) yield return id; yield break; }
                BigInteger domain = new BigInteger(Domain), count = new BigInteger(RequestedClipped);
                using (SHA256 hash = SHA256.Create())
                {
                    for (ulong j = 0; j < RequestedClipped; j++)
                    {
                        if (blockedStrata.Contains(j)) continue;
                        ulong begin = (ulong)(domain * j / count), end = (ulong)(domain * (j + 1) / count);
                        ulong width = end - begin, threshold = unchecked(0UL - width) % width;
                        for (ulong attempt = 0; ; attempt++)
                        {
                            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(Seed + "|stratum|" + Descriptor + "|" + S(j) + "|" + S(attempt)));
                            ulong value = Read64(bytes);
                            if (value < threshold) continue;
                            ulong id = begin + value % width;
                            if (boundarySet.Contains(id)) continue;
                            yield return id; break;
                        }
                    }
                }
                foreach (ulong id in Boundaries) yield return id;
            }
        }

        private sealed class VerificationJob
        {
            public const int PreviewLimit = 32;
            public readonly string Id = Guid.NewGuid().ToString("N");
            public readonly VerificationPlan Plan;
            public readonly double T;
            public readonly double[] Matrix;
            public readonly bool HasPrepared;
            public readonly BigInteger Generation;
            public readonly string ReceivedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);
            public string CompletedUtc="";
            public readonly List<object> SamplePreview=new List<object>();
            public readonly Stopwatch Watch = Stopwatch.StartNew();
            public volatile bool CancelRequested;
            public bool Active = true;
            public string Phase = "running", Stage = "queued", Failure = "", CheckpointError = "", Digest = "";
            public ulong Completed, CpuStates, GpuStates, FailedPoints, FirstAddress, LastAddress;
            public double CpuMs, GpuMs, KernelMs, MaxError, MaxNormError, MaxSpinError;
            public VerificationJob(VerificationPlan plan, double migration, double[] matrix,bool hasPrepared,BigInteger generation) { Plan = plan; T = migration; Matrix = (double[])matrix.Clone();HasPrepared=hasPrepared;Generation=generation; }
        }

        private bool JobActive() { return verificationJob != null && verificationJob.Active; }

        private object VerificationJobStatus()
        {
            if (verificationJob == null) return restoredVerificationJob;
            VerificationJob job = verificationJob;
            bool complete = !job.Active && job.Phase == "completed" &&job.Completed==job.Plan.Planned &&String.IsNullOrEmpty(job.Failure) &&String.IsNullOrEmpty(job.CheckpointError);
            return D("id", job.Id, "phase", job.Active && job.CancelRequested ? "cancelling" : job.Phase, "stage", job.Stage, "active", job.Active,
                "requestedSamples", S(job.Plan.Requested), "plannedSamples", S(job.Plan.Planned), "completedSamples", S(job.Completed), "sampleCount", S(job.Completed),
                "cpuStates", S(job.CpuStates), "gpuStates", S(job.GpuStates), "verifiedCount", S(job.Completed), "errorCount", S(job.FailedPoints),
                "exhaustive", job.Plan.Exhaustive || job.Plan.Planned == job.Plan.Domain, "requestedClampedToDomain", job.Plan.Requested > new BigInteger(job.Plan.Domain),
                "boundarySamples", job.Plan.Boundaries.Count, "batchLimit", QuantumGpu.MaximumBatchStates,
                "passed", complete && job.FailedPoints == 0, "maxError", job.MaxError, "maxNormError", job.MaxNormError, "maxSpinError", job.MaxSpinError, "tolerance", 2e-9,
                "elapsedMs", job.Watch.Elapsed.TotalMilliseconds, "totalMs", job.Watch.Elapsed.TotalMilliseconds, "cpuMs", job.CpuMs, "gpuMs", job.GpuMs, "gpuKernelMs", job.KernelMs,
                "descriptorHash", job.Plan.Descriptor, "selectionSeed", job.Plan.Seed, "addressDigest", job.Digest, "addressDigestEncoding", "SHA-256 of completed compared addresses as big-endian unsigned 64-bit words in traversal order",
                "firstAddress", job.Completed == 0 ? "" : S(job.FirstAddress), "lastAddress", job.Completed == 0 ? "" : S(job.LastAddress),
                "receivedUtc",job.ReceivedUtc,"completedUtc",job.CompletedUtc,"hasPrepared",job.HasPrepared,"migrationT",job.T,"modelGeneration",S(job.Generation),"gridM",S(job.Plan.M),"gridL",S(job.Plan.L),"stateCount",S(job.Plan.Domain),
                "samplePreview",job.SamplePreview.ToArray(),"samplePreviewLimit",VerificationJob.PreviewLimit,"omittedSampleRows",S(job.Completed-(ulong)job.SamplePreview.Count),
                "sampleAssessment",SamplePreviewAssessment(job.SamplePreview,job.HasPrepared,job.T,new BigInteger(job.Plan.Domain)),
                "samplePreviewAddressDigest",SampleAddressDigest(job.SamplePreview),"samplePreviewAddressDigestEncoding","SHA-256 of displayed compared addresses as big-endian unsigned64-bit words in display/traversal order",
                "sampleComponentOrder",new string[]{"alphaReal","alphaImaginary","betaReal","betaImaginary","spinX","spinY","spinZ","normSquared","probabilityOne"},
                "verificationMethod","Actual RAM-resident FP64 CPU and OpenCL GPU batch arrays at distinct procedural model addresses; nine returned components plus norm and pure-spin identity checks at the stated tolerance.",
                "verificationPurpose","Materialize selected addresses of the captured virtual procedural model on CPU and GPU, show what that model predicts, and check agreement and mathematical state identities at the returned tolerance.",
                "predictionMeaning","Displayed amplitudes, X/Y/Z spin expectations and probabilityOne are the captured model's numerical predictions. probabilityOne is a Born probability, not an observed shot frequency. CPU/GPU agreement checks two implementations under the same model assumptions; it is not experimental or physical quantum-hardware validation, proof of unsampled addresses, or verification of unrelated calculator operands/results.",
                "samplePreviewScope","First at most32 actually compared addresses, copied from completed CPU/GPU batch arrays. Omitted success rows remain counted and represented by the address digest; no sample rows are synthesized. This verifies the captured procedural descriptor, not unrelated q.math operands/results or arbitrary physical RAM contents.",
                "cancelRequested", job.CancelRequested, "failure", job.Failure, "checkpointError", job.CheckpointError,
                "selectionRule", job.Plan.Exhaustive ? "Every finite domain address once, in ascending order." : "One unbiased SHA-256 offset per evenly partitioned stratum excluding boundary addresses, followed by unique deterministic boundaries; fully blocked strata are skipped.",
                "scope", "Completed CPU/GPU amplitude, relative-phase, norm, Born P1 and spin comparisons for the captured procedural descriptor. Partial/cancelled work is counted exactly; a bounded sample preview is retained, and no represented gate credit is issued. This is not verification of unrelated calculator results or a scan of arbitrary physical RAM.");
        }

        private static Dictionary<string,object> SamplePreviewAssessment(System.Collections.IList rows,bool hasPrepared,double migration,BigInteger domain)
        {
            string[] names={"alphaReal","alphaImaginary","betaReal","betaImaginary","spinX","spinY","spinZ","normSquared","probabilityOne"};
            double[,] minimum=new double[2,9],maximum=new double[2,9];int[,] finiteCounts=new int[2,9];int[] finiteVectors=new int[2];
            HashSet<string>[] distinct={new HashSet<string>(StringComparer.Ordinal),new HashSet<string>(StringComparer.Ordinal)};
            foreach(object item in rows)
            {
                Dictionary<string,object> row=(Dictionary<string,object>)item;
                for(int backend=0;backend<2;backend++)
                {
                    System.Collections.IList values=(System.Collections.IList)row[backend==0 ?"cpu":"gpu"];StringBuilder key=new StringBuilder();bool finiteVector=values.Count==9;
                    for(int i=0;i<9;i++)
                    {
                        object raw=values[i];double value=raw is string ?Double.NaN:Convert.ToDouble(raw,CultureInfo.InvariantCulture);
                        if(!Finite(value)){finiteVector=false;continue;}
                        if(finiteCounts[backend,i]==0)minimum[backend,i]=maximum[backend,i]=value;
                        else{minimum[backend,i]=Math.Min(minimum[backend,i],value);maximum[backend,i]=Math.Max(maximum[backend,i],value);}
                        finiteCounts[backend,i]++;key.Append(value==0 ?"0":value.ToString("R",CultureInfo.InvariantCulture)).Append('|');
                    }
                    if(finiteVector){finiteVectors[backend]++;distinct[backend].Add(key.ToString());}
                }
            }
            object[] ranges=new object[9];for(int i=0;i<9;i++)ranges[i]=D("component",names[i],"cpuMinimum",finiteCounts[0,i]==0 ?null:(object)minimum[0,i],"cpuMaximum",finiteCounts[0,i]==0 ?null:(object)maximum[0,i],"cpuFiniteRows",finiteCounts[0,i],"gpuMinimum",finiteCounts[1,i]==0 ?null:(object)minimum[1,i],"gpuMaximum",finiteCounts[1,i]==0 ?null:(object)maximum[1,i],"gpuFiniteRows",finiteCounts[1,i]);
            string dependence=domain==BigInteger.One ?"single-address-domain":migration==0 ?"address-constant-at-t0":"address-dependent-migration-rule";
            string meaning=domain==BigInteger.One ?"The captured domain contains one address; its state cannot demonstrate variation across addresses.":migration==0 ?"At migration t=0 every address starts from alpha=1,beta=0; the shared matrix acts on that same state at every address. Address-constant predictions and exact zero components/residuals can therefore be correct.":"The captured nonzero migration rule depends on address. Returned preview variation describes only these retained sample rows and does not establish whole-domain variation or experimental truth.";
            return D("schema","SPINDOCTOR/NATIVE-PREVIEW-ASSESSMENT/1","capturedPreviewRows",rows.Count,"finiteCpuPreviewRows",finiteVectors[0],"finiteGpuPreviewRows",finiteVectors[1],"distinctCpuPreviewVectors",distinct[0].Count,"distinctGpuPreviewVectors",distinct[1].Count,"cpuPreviewConstant",finiteVectors[0]>0 &&distinct[0].Count==1,"gpuPreviewConstant",finiteVectors[1]>0 &&distinct[1].Count==1,"modelAddressDependence",dependence,"modelPreparedAtCapture",hasPrepared,"modelInterpretation",meaning,"componentRanges",ranges,
                "scope","Assessment of the original native captured preview, at most32 actual compared rows. Distinct counts use exact finite nine-component vectors; ranges use finite returned components. These describe neither omitted rows nor the whole model domain. A parent summary can display fewer rows than this native capture.",
                "countMeaning","completedSamples counts comparison operations in this job. Repeating the same descriptor, selection seed and sample request reuses the deterministic address plan; repeated job totals are not newly distinct union coverage.");
        }

        private void RunVerification(VerificationJob job)
        {
            const int batchLimit = 16384;
            const double tolerance = 2e-9;
            SHA256 digest=null;
            try
            {
                    digest=SHA256.Create();
                    using (IEnumerator<ulong> addresses = job.Plan.Addresses().GetEnumerator())
                    {
                        while (!job.CancelRequested)
                        {
                            lock (sync) { job.Stage = "sampling"; }
                            List<ulong> pending = new List<ulong>(batchLimit);
                            while (pending.Count < batchLimit && !job.CancelRequested && addresses.MoveNext()) pending.Add(addresses.Current);
                            if (job.CancelRequested || pending.Count == 0) break;
                            ulong[] ids = pending.ToArray(); pending = null;
                            lock (sync) { job.Stage = "cpu"; }
                            Stopwatch cpuWatch = Stopwatch.StartNew();
                            double[] cpuValues = QuantumMath.States(ids, job.Plan.M, job.Plan.L, job.T, job.Matrix);
                            cpuWatch.Stop();
                            lock (sync)
                            {
                                actualCpu += ids.Length; job.CpuStates += (ulong)ids.Length; job.CpuMs += cpuWatch.Elapsed.TotalMilliseconds;
                                job.Stage = "cpu-complete"; Save();
                            }
                            if (job.CancelRequested) break;
                            if (gpu == null) throw new InvalidOperationException("No actual FP64 OpenCL GPU: " + gpuError + ". CPU calculations are counted; no GPU or compared-sample credit was issued.");
                            lock (sync) { job.Stage = "gpu"; }
                            Stopwatch gpuWatch = Stopwatch.StartNew(); double kernelMs;
                            double[] gpuValues = gpu.Compute(ids, job.Plan.M, job.Plan.L, job.T, job.Matrix, out kernelMs);
                            gpuWatch.Stop();
                            if (gpuValues == null || gpuValues.Length != ids.Length * 9) throw new InvalidOperationException("GPU output length mismatch.");
                            lock (sync)
                            {
                                actualGpu += ids.Length; job.GpuStates += (ulong)ids.Length; job.GpuMs += gpuWatch.Elapsed.TotalMilliseconds; job.KernelMs += kernelMs;
                                job.Stage = "comparison"; Save();
                            }
                            double batchError = 0, batchNorm = 0, batchSpin = 0;
                            List<object> failures = new List<object>();
                            List<object> previews = new List<object>();
                            for (int i = 0; i < ids.Length; i++)
                            {
                                bool finite = true; double pointError = 0;
                                int offset = i * 9;
                                for (int j = 0; j < 9; j++)
                                {
                                    double c = cpuValues[offset + j], g = gpuValues[offset + j];
                                    if (!Finite(c) || !Finite(g)) finite = false;
                                    double diff = Math.Abs(c - g); if (Finite(diff)) pointError = Math.Max(pointError, diff);
                                }
                                double normError = Math.Max(Math.Abs(cpuValues[offset + 7] - 1), Math.Abs(gpuValues[offset + 7] - 1));
                                double spinError = Math.Max(Math.Abs(cpuValues[offset + 4] * cpuValues[offset + 4] + cpuValues[offset + 5] * cpuValues[offset + 5] + cpuValues[offset + 6] * cpuValues[offset + 6] - 1), Math.Abs(gpuValues[offset + 4] * gpuValues[offset + 4] + gpuValues[offset + 5] * gpuValues[offset + 5] + gpuValues[offset + 6] * gpuValues[offset + 6] - 1));
                                batchError = Math.Max(batchError, finite ? pointError : Double.MaxValue);
                                batchNorm = Math.Max(batchNorm, Finite(normError) ? normError : Double.MaxValue); batchSpin = Math.Max(batchSpin, Finite(spinError) ? spinError : Double.MaxValue);
                                bool pointPassed=finite &&pointError<=tolerance &&normError<=tolerance &&spinError<=tolerance;
                                if(job.SamplePreview.Count+previews.Count<VerificationJob.PreviewLimit)
                                {
                                    double[] c=new double[9],g=new double[9];Array.Copy(cpuValues,offset,c,0,9);Array.Copy(gpuValues,offset,g,0,9);
                                    previews.Add(D("address",S(ids[i]),"cpu",SafeNumbers(c),"gpu",SafeNumbers(g),"maxError",finite ?(object)pointError:"nonfinite","normError",Finite(normError)?(object)normError:"nonfinite","spinError",Finite(spinError)?(object)spinError:"nonfinite","tolerance",tolerance,"passed",pointPassed));
                                }
                                if (!finite || pointError > tolerance || normError > tolerance || spinError > tolerance)
                                {
                                    double[] c = new double[9], g = new double[9]; Array.Copy(cpuValues, offset, c, 0, 9); Array.Copy(gpuValues, offset, g, 0, 9);
                                    failures.Add(D("type", "sample-mismatch", "address", S(ids[i]), "descriptorHash", job.Plan.Descriptor, "cpu", SafeNumbers(c), "gpu", SafeNumbers(g), "maxError", pointError, "normError", Finite(normError) ? (object)normError : "nonfinite", "spinError", Finite(spinError) ? (object)spinError : "nonfinite", "tolerance", tolerance));
                                }
                            }
                            byte[] encoded = new byte[ids.Length * 8];
                            for (int j = 0; j < ids.Length; j++) for (int k = 0; k < 8; k++) encoded[j * 8 + k] = (byte)(ids[j] >> (56 - k * 8));
                            digest.TransformBlock(encoded, 0, encoded.Length, encoded, 0);
                            lock (sync)
                            {
                                if (job.Completed == 0) job.FirstAddress = ids[0]; job.LastAddress = ids[ids.Length - 1];
                                verified += ids.Length; job.Completed += (ulong)ids.Length; job.FailedPoints += (ulong)failures.Count;
                                job.MaxError = Math.Max(job.MaxError, batchError); job.MaxNormError = Math.Max(job.MaxNormError, batchNorm); job.MaxSpinError = Math.Max(job.MaxSpinError, batchSpin);
                                job.SamplePreview.AddRange(previews);
                                foreach (object failure in failures) LogError(failure);
                                job.Stage = "batch-complete"; Save();
                            }
                        }
                    }
                    lock (sync) { job.Phase = job.Completed == job.Plan.Planned ? "completed" : "cancelled"; job.Stage = "finished"; }
            }
            catch (Exception ex)
                {
                    lock (sync) { job.Phase = "failed"; job.Stage = "failed"; job.Failure = ex.GetType().Name + ": " + ex.Message; }
                }
            finally
                {
                    string addressDigest="";
                    try{if(digest!=null){digest.TransformFinalBlock(new byte[0],0,0);addressDigest=Hex(digest.Hash);}}
                    catch(Exception ex){lock(sync){job.Phase="failed";job.Stage="failed";job.Failure="Verification digest could not finalize: "+ex.GetType().Name+": "+ex.Message;}}
                    lock (sync)
                    {
                        job.Digest = addressDigest; job.Active = false; job.CompletedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);job.Watch.Stop();
                        lastVerification = VerificationJobStatus();
                        try { Save(); }
                        catch (Exception ex) { job.CheckpointError = ex.GetType().Name + ": " + ex.Message; lastVerification = VerificationJobStatus(); }
                    }
                    if(digest!=null)digest.Dispose();
                }
        }

        private object Measure(string[] words)
        {
            Exact(words, 6); Need(words[2], "basis"); Need(words[4], "shots");
            ulong id = Address(words[1]), count = UInt(words[5]);
            if (count < 1 || count > 100000) throw new ArgumentException("Shots must be 1-100000.");
            string basis = words[3].ToLowerInvariant();
            if (basis != "x" && basis != "y" && basis != "z") throw new ArgumentException("Measurement basis must be x, y or z.");
            if (UInt64.MaxValue - measureCounter < count) throw new InvalidOperationException("Measurement counter exhausted; no shots issued.");
            double[] state = QuantumMath.State(id, m, l, migration, matrix); actualCpu++;
            ValidateState(state);
            double spin = basis == "x" ? state[4] : (basis == "y" ? state[5] : state[6]);
            double probability = Math.Max(0, Math.Min(1, (1 - spin) / 2));
            ulong start = measureCounter, one = 0;
            using (SHA256 hash = SHA256.Create())
            {
                for (ulong j = 0; j < count; j++)
                {
                    byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(measurementSeed + "|measurement|" + measureCounter.ToString(CultureInfo.InvariantCulture)));
                    double uniform = (Read64(bytes) >> 11) * (1.0 / 9007199254740992.0);
                    measureCounter++;
                    if (uniform < probability) one++;
                }
            }
            shots += count;
            return D("address", S(id), "basis", basis, "shots", S(count), "zero", S(count - one), "one", S(one), "probabilityOne", probability, "meanSpin", 1 - 2.0 * one / count, "seed", measurementSeed, "counterStart", S(start), "counterEnd", S(measureCounter), "scope", "Seeded classical samples of single-qubit Born probabilities; descriptor state is retained, no hardware measurement or joint entanglement.", "descriptorHash", DescriptorHash());
        }

        private object Reset()
        {
            m = 20000000UL; l = 25000000UL; migration = 0; matrix = QuantumMath.Identity; prepared = false;
            NewGeneration("reset");
            return D("reset", true, "seedsPreserved", true, "measurementCounterPreserved", S(measureCounter), "modelGeneration", S(generation), "note", "Model and current-generation represented credits reset; lifetime actual counters and seeds retained.");
        }

        private void NewGeneration(string reason)
        {
            generation++; preparations = BigInteger.Zero; gates = BigInteger.Zero;
            AddLedger(reason, D("gridM", S(m), "gridL", S(l), "migrationT", migration, "representedCredit", "0"));
        }

        private object Status()
        {
            return D("productName", "SpinDoctor", "productVersion", ProductVersion, "model", "procedural-product-state", "schema", Schema, "phase", "ready", "hasPrepared", prepared,
                "gridM", S(m), "gridL", S(l), "stateCount", S(Count()), "migrationT", migration, "matrix", (double[])matrix.Clone(),
                "modelGeneration", S(generation), "fieldSeed", fieldSeed, "measurementSeed", measurementSeed, "measurementCounter", S(measureCounter),
                "representedPreparations", S(preparations), "representedGateApplications", S(gates), "representedPreparationRotations", S(preparations * 2),
                "lifetimeRepresentedPreparations", S(lifetimePreparations), "lifetimeRepresentedGateApplications", S(lifetimeGates),
                "actualCpuStates", S(actualCpu), "actualGpuStates", S(actualGpu), "verifiedSamples", S(verified), "measurementShots", S(shots), "ruleChecks", S(rules), "errorCount", S(errors),
                "descriptorHash", DescriptorHash(), "gpuAvailable", gpu != null, "gpuDevice", gpu == null ? "" : gpu.DeviceName, "gpuError", gpuError,
                "runtimeSourceHashes", sourceHashes,
                "coherentRegisterCapabilities", D("updateId", "3.1.2-298-20261010", "maximumLogicalQubits", TrancheRegister.MaximumLogicalQubits, "maximumJointFactorQubits", TrancheRegister.MaximumFactorQubits, "globalIndexEncoding", "BigInteger", "wideCircuitMeasurementBasis", "z", "wideCircuitSampling", "prepared-factor-Born-Z"),
                "quantumHardwareGates", "0", "lastVerification", lastVerification, "lastRuleVerification", lastRuleVerification,
                "lastVerificationJob", VerificationJobStatus(),
                "tranche", tranche == null ? null : tranche.GetStatus(),
                "circuit", circuit == null ? null : circuit.GetStatus(),
                "inputs", inputs == null ? null : inputs.GetStatus(),
                "sequence",sequences==null?null:sequences.GetStatus(),
                "history",commandReports==null?null:commandReports.GetStatus(),
                "calculatorCapabilities",SpinDoctorCalculators.Capabilities,"advancedCapabilities",SpinDoctorAdvanced.Capabilities,
                "memoryPolicy","Procedural families are descriptors, never a 16 PB allocation. Coherent jobs enforce a memory budget including live output/cache/working state and rollback. q.circuit memory auto or q.tranche memory auto uses measured available Windows RAM with an OS reserve; transient local calculator buffers are released after execution.",
                "accountingScope", "Represented single-qubit gate applications are descriptor credits; CPU/GPU calculated states and checked samples are lifetime completed work. No represented gates/s benchmark is inferred.",
                "errorLog", "quantum-errors.jsonl", "ledger", ledger.ToArray(), "completeLedgerRetained", fullLedgerRetained);
        }

        private object Descriptor()
        {
            Dictionary<string, object> value = ModelDescriptor();
            value["descriptorHash"] = DescriptorHash();
            value["measurementCounter"] = S(measureCounter);
            value["representedPreparations"] = S(preparations); value["representedGateApplications"] = S(gates);
            value["modelGeneration"] = S(generation); value["ledger"] = ledger.ToArray(); value["completeLedgerRetained"] = fullLedgerRetained;
            value["accounting"] = D("actualCpuStates", S(actualCpu), "actualGpuStates", S(actualGpu), "verifiedSamples", S(verified), "measurementShots", S(shots), "ruleChecks", S(rules), "errors", S(errors));
            value["verification"] = lastVerification; value["ruleVerification"] = lastRuleVerification;
            value["runtimeSourceHashes"] = sourceHashes;
            value["replay"] = "Recreate grid, migration and matrix; use recorded measurement seed and counter for subsequent deterministic Born sampling. Family and shared gate matrix describe a separable product state; no arbitrary entanglement is encoded.";
            return value;
        }

        private Dictionary<string, object> ModelDescriptor()
        {
            return D("productName", "SpinDoctor", "schema", "QUETTAFLOP/PROCEDURAL-PRODUCT-DESCRIPTOR/1", "gridM", S(m), "gridL", S(l), "stateCount", S(Count()), "migrationT", migration,
                "matrix", (double[])matrix.Clone(), "matrixExact", ExactStrings(matrix), "migrationTExact", migration.ToString("R", CultureInfo.InvariantCulture), "fieldSeed", fieldSeed, "measurementSeed", measurementSeed,
                "indexRule", "k=id%gridM; ell=floor(id/gridM); id=ell*gridM+k", "angleRule", "q=(k+0.5)/gridM; theta=2*asin(sqrt(q)); phi=2*pi*ell/gridL", "stateRule", "alpha=cos(migrationT*theta/2); beta=exp(i*migrationT*phi)*sin(migrationT*theta/2); output=matrix*[alpha,beta]",
                "equationSource", "https://chatgpt.com/s/cx_6ac93a09e8048191a1b90ffb448a453c",
                "independentProductStates", true, "entanglingGatesSupported", false, "complexEncoding", "Interleaved real/imaginary row-major 2x2 matrix; amplitudes retain absolute phase.");
        }

        private string DescriptorHash() { return HashHex(json.Serialize(ModelDescriptor())); }

        private object Help()
        {
            return D("productName", "SpinDoctor", "commands", new string[] { "q.help", "q.prepare grid 20000000 25000000", "q.migrate t 1", "q.state 123456789", "q.gate ry 0.2 all", "q.gate rz 0.2 all", "q.gate u 0.2 0.3 0.4 all", "q.gate u3 0.2 0.3 0.4 all", "q.gate h all", "q.gate x all", "q.gate y all", "q.gate z all", "q.gate s all", "q.gate t all", "q.gate i all", "q.gate identity all", "q.verify rule", "q.verify samples 10240 errors-only", "q.verify status", "q.verify cancel", "q.measure 123456789 basis x shots 100", "q.status", "q.export descriptor", "q.reset", "q.tranche help", "q.circuit help", "q.input help", "q.input import ID", "q.input import CSV_ID row 0 base CIRCUIT_ID" }, "angleUnits", "radians", "verificationLimit", "Finite model domain, with arbitrary positive decimal requests clamped explicitly to distinct coverage.", "verificationBatchLimit", 16384, "measurementShotLimit", 100000,
                "verificationPurpose","q.verify samples materializes selected current-model addresses into real CPU/OpenCL GPU arrays, exposes predicted amplitudes/spin/Born probabilities, and compares both implementations plus normalization/spin identities. Its bounded sample preview shows actual returned values; unsampled addresses are not individually checked.",
                "circuitPresetDefaultShots",CircuitTerminal.DefaultPresetShots,"circuitPresetExecution","q.circuit preset bell|ghz|qft [QUBITS] automatically queues 1024 circuit shots in basis z; override shots/basis or use run=false to define only. Circuit execution is distinct from procedural-model q.verify sampling.",
                "verificationBoundary","CPU/GPU agreement validates implementation agreement under the same captured procedural model assumptions; it is not physical quantum-hardware or experimental validation, a scan of arbitrary RAM contents, or verification of unrelated q.math operands/results. No model preparation is silently performed.");
        }

        private void AddLedger(string kind, Dictionary<string, object> details)
        {
            details["kind"] = kind; details["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            details["modelGeneration"] = S(generation); details["descriptorHash"] = DescriptorHash();
            ledger.Add(details);
            if (ledger.Count > 256) { ledger.RemoveAt(0); fullLedgerRetained = false; }
        }

        private void LogError(object value)
        {
            Dictionary<string, object> record = value as Dictionary<string, object>;
            if (record != null) record["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            File.AppendAllText(errorPath, json.Serialize(value) + Environment.NewLine, new UTF8Encoding(false));
            errors++;
        }

        private void Save()
        {
            Dictionary<string, object> value = D("schema", Schema, "gridM", S(m), "gridL", S(l), "migrationT", migration, "migrationTExact", migration.ToString("R", CultureInfo.InvariantCulture), "matrix", matrix, "matrixExact", ExactStrings(matrix), "hasPrepared", prepared,
                "fieldSeed", fieldSeed, "measurementSeed", measurementSeed, "measurementCounter", S(measureCounter), "modelGeneration", S(generation),
                "representedPreparations", S(preparations), "representedGateApplications", S(gates), "lifetimeRepresentedPreparations", S(lifetimePreparations), "lifetimeRepresentedGateApplications", S(lifetimeGates),
                "actualCpuStates", S(actualCpu), "actualGpuStates", S(actualGpu), "verifiedSamples", S(verified), "measurementShots", S(shots), "ruleChecks", S(rules), "errorCount", S(errors),
                "ledger", ledger, "completeLedgerRetained", fullLedgerRetained, "lastVerification", lastVerification, "lastRuleVerification", lastRuleVerification, "lastVerificationJob", VerificationJobStatus());
            string temp = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string text=json.Serialize(value);if(Encoding.UTF8.GetByteCount(text)>2097152)throw new InvalidDataException("Saved session exceeds its2MiB metadata limit.");
            try { File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(statePath))File.Replace(temp,statePath,null);else File.Move(temp,statePath); }
            finally { try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){} }
        }

        private void Load()
        {
            try
            {
                string saved;
                using(FileStream file=new FileStream(statePath,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    if(file.Length>2097152)throw new InvalidDataException("Saved session is too large.");
                    using(StreamReader reader=new StreamReader(file,new UTF8Encoding(false,true),true))saved=reader.ReadToEnd();
                }
                Dictionary<string, object> value = json.DeserializeObject(saved) as Dictionary<string, object>;
                if (value == null || Text(value, "schema") != Schema) throw new InvalidDataException("Saved session schema is invalid.");
                fieldSeed = Seed(Text(value, "fieldSeed")); measurementSeed = Seed(Text(value, "measurementSeed"));
                m = UInt(Text(value, "gridM")); l = UInt(Text(value, "gridL")); ValidateGrid(m, l);
                // JavaScriptSerializer can deserialize JSON numbers as Decimal and lose a
                // last double bit when converted back. Round-trip strings are authoritative.
                // Numeric fallback is retained only for pre-release draft checkpoints.
                migration = value.ContainsKey("migrationTExact") ? Double.Parse(Text(value, "migrationTExact"), NumberStyles.Float, CultureInfo.InvariantCulture) : Convert.ToDouble(value["migrationT"], CultureInfo.InvariantCulture);
                if (!Finite(migration) || migration < 0 || migration > 1) throw new InvalidDataException("Saved migration is invalid.");
                bool hasExact = value.ContainsKey("matrixExact");
                object[] raw = value[hasExact ? "matrixExact" : "matrix"] as object[];
                if (raw == null || raw.Length != 8) throw new InvalidDataException("Saved matrix is invalid.");
                matrix = new double[8]; for (int i = 0; i < 8; i++) { if (hasExact && !(raw[i] is string)) throw new InvalidDataException("Saved exact matrix components must be strings."); matrix[i] = hasExact ? Double.Parse((string)raw[i], NumberStyles.Float, CultureInfo.InvariantCulture) : Convert.ToDouble(raw[i], CultureInfo.InvariantCulture); if (!Finite(matrix[i])) throw new InvalidDataException("Saved matrix contains nonfinite components."); }
                if (QuantumMath.UnitarityError(matrix) > 1e-9) throw new InvalidDataException("Saved matrix is not unitary.");
                prepared = SavedBoolean(value,"hasPrepared"); measureCounter = UInt(Text(value, "measurementCounter"));
                generation = Counter(value, "modelGeneration"); preparations = Counter(value, "representedPreparations"); gates = Counter(value, "representedGateApplications");
                lifetimePreparations = Counter(value, "lifetimeRepresentedPreparations"); lifetimeGates = Counter(value, "lifetimeRepresentedGateApplications");
                actualCpu = Counter(value, "actualCpuStates"); actualGpu = Counter(value, "actualGpuStates"); verified = Counter(value, "verifiedSamples"); shots = Counter(value, "measurementShots"); rules = Counter(value, "ruleChecks"); errors = Counter(value, "errorCount");
                if (preparations > lifetimePreparations || gates > lifetimeGates || verified > actualCpu || verified > actualGpu || shots > measureCounter) throw new InvalidDataException("Saved accounting is inconsistent.");
                object[] rawLedger = value["ledger"] as object[];
                if (rawLedger == null || rawLedger.Length > 256) throw new InvalidDataException("Saved ledger is invalid.");
                foreach (object entry in rawLedger) { if (!(entry is Dictionary<string, object>)) throw new InvalidDataException("Saved ledger entry is invalid."); ledger.Add(entry); }
                fullLedgerRetained = SavedBoolean(value,"completeLedgerRetained");
                lastVerification = value["lastVerification"]; lastRuleVerification = value["lastRuleVerification"];
                if((lastVerification!=null&&!(lastVerification is Dictionary<string,object>))||(lastRuleVerification!=null&&!(lastRuleVerification is Dictionary<string,object>)))throw new InvalidDataException("Saved verification result must be an object or null.");
                Dictionary<string,object> savedCompleted=lastVerification as Dictionary<string,object>;if(savedCompleted!=null&&savedCompleted.ContainsKey("samplePreview"))ValidateSavedVerification(savedCompleted);
                if (value.ContainsKey("lastVerificationJob"))
                {
                    restoredVerificationJob = value["lastVerificationJob"];
                    Dictionary<string, object> interrupted = restoredVerificationJob as Dictionary<string, object>;
                    if(restoredVerificationJob!=null && interrupted==null)throw new InvalidDataException("Saved verification job must be an object or null.");
                    if(interrupted!=null)ValidateSavedVerification(interrupted);
                    if (interrupted != null && SavedBoolean(interrupted,"active"))
                    {
                        interrupted["active"] = false; interrupted["phase"] = "interrupted"; interrupted["stage"] = "restart"; interrupted["passed"] = false;
                        interrupted["failure"] = "Previous process stopped during verification. Completed counters were restored; no completion or automatic resume is claimed.";
                        lastVerification = interrupted;
                    }
                }
            }
            catch (Exception ex) { throw new InvalidDataException("Saved quantum session was rejected and preserved: " + ex.Message, ex); }
        }

        private static BigInteger Counter(Dictionary<string, object> value, string key)
        {
            BigInteger number;string text=Text(value,key);
            if (text.Length>1024 || !BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 0) throw new InvalidDataException("Invalid saved counter: " + key);
            return number;
        }
        private static bool SavedBoolean(Dictionary<string,object> value,string key){object raw;if(!value.TryGetValue(key,out raw)||!(raw is bool))throw new InvalidDataException("Missing boolean field: "+key);return (bool)raw;}
        private void ValidateSavedVerification(Dictionary<string,object> job)
        {
            string id=Text(job,"id");if(id.Length!=32)throw new InvalidDataException("Invalid saved verification job ID.");foreach(char c in id)if(!(c>='a'&&c<='f')&&!(c>='0'&&c<='9'))throw new InvalidDataException("Invalid saved verification job ID.");
            SavedBoolean(job,"active");SavedBoolean(job,"passed");
            BigInteger planned=Counter(job,"plannedSamples"),completed=Counter(job,"completedSamples"),cpu=Counter(job,"cpuStates"),gpuCount=Counter(job,"gpuStates"),failed=Counter(job,"errorCount");
            if(Counter(job,"requestedSamples")<=0||planned<=0||planned>QuantumMath.MaximumExactStateCount||completed>planned||cpu>planned||gpuCount>cpu||completed>gpuCount||failed>completed||Counter(job,"verifiedCount")!=completed||Counter(job,"sampleCount")!=completed||cpu>actualCpu||gpuCount>actualGpu||completed>verified)throw new InvalidDataException("Saved verification job accounting is inconsistent.");
            string phase=Text(job,"phase");if(phase!="running"&&phase!="cancelling"&&phase!="completed"&&phase!="cancelled"&&phase!="failed"&&phase!="interrupted")throw new InvalidDataException("Invalid saved verification phase.");
            if(SavedBoolean(job,"passed")&&(phase!="completed"||SavedBoolean(job,"active")||completed!=planned||failed!=0||Text(job,"failure").Length>0||Text(job,"checkpointError").Length>0))throw new InvalidDataException("Saved verification success is inconsistent.");
            // Old checkpoints contain no bounded preview. New checkpoints validate
            // all added evidence before it can be presented as native sample data.
            if(job.ContainsKey("samplePreview"))ValidateSavedSamplePreview(job,completed,planned);
            else if(job.ContainsKey("sampleAssessment"))throw new InvalidDataException("Saved sample assessment requires its actual preview rows.");
        }
        private static void ValidateSavedSamplePreview(Dictionary<string,object> job,BigInteger completed,BigInteger planned)
        {
            if(SavedFiniteNumber(job["samplePreviewLimit"])!=VerificationJob.PreviewLimit)throw new InvalidDataException("Invalid saved sample preview limit.");
            System.Collections.IList rows=job["samplePreview"] as System.Collections.IList;
            if(rows==null||rows.Count>VerificationJob.PreviewLimit||rows.Count!=(int)BigInteger.Min(completed,VerificationJob.PreviewLimit)||Counter(job,"omittedSampleRows")!=completed-rows.Count)throw new InvalidDataException("Saved sample preview coverage is inconsistent.");
            BigInteger gridM=Counter(job,"gridM"),gridL=Counter(job,"gridL"),domain=Counter(job,"stateCount");
            if(gridM<=0||gridL<=0||gridM*gridL!=domain||domain>QuantumMath.MaximumExactStateCount||planned>domain)throw new InvalidDataException("Invalid saved sample descriptor grid.");
            SavedBoolean(job,"hasPrepared");Counter(job,"modelGeneration");double migration=SavedFiniteNumber(job["migrationT"]);if(migration<0||migration>1)throw new InvalidDataException("Invalid saved sample migration.");
            string[] componentNames={"alphaReal","alphaImaginary","betaReal","betaImaginary","spinX","spinY","spinZ","normSquared","probabilityOne"};
            System.Collections.IList componentOrder=job["sampleComponentOrder"] as System.Collections.IList;if(componentOrder==null||componentOrder.Count!=9)throw new InvalidDataException("Invalid saved sample component order.");for(int i=0;i<9;i++)if(!(componentOrder[i] is string)||(string)componentOrder[i]!=componentNames[i])throw new InvalidDataException("Invalid saved sample component order.");
            ValidateSavedUtc(Text(job,"receivedUtc"),false);ValidateSavedUtc(Text(job,"completedUtc"),true);
            if(SavedBoolean(job,"active")&&Text(job,"completedUtc").Length>0||(!SavedBoolean(job,"active")&&Text(job,"phase")!="interrupted"&&Text(job,"completedUtc").Length==0))throw new InvalidDataException("Saved verification stop time is inconsistent.");
            if(Text(job,"completedUtc").Length>0&&String.CompareOrdinal(Text(job,"completedUtc"),Text(job,"receivedUtc"))<0)throw new InvalidDataException("Saved verification stop time precedes receipt.");
            foreach(string key in new string[]{"descriptorHash","selectionSeed"})ValidateSavedHex(Text(job,key),64);
            string digest=Text(job,"addressDigest");if(digest.Length>0)ValidateSavedHex(digest,64);
            if(Text(job,"verificationMethod").Length>1024||Text(job,"samplePreviewScope").Length>2048)throw new InvalidDataException("Saved verification scope is too long.");
            foreach(string key in new string[]{"verificationPurpose","predictionMeaning"}){object text;if(job.TryGetValue(key,out text)&&(!(text is string)||((string)text).Length>2048))throw new InvalidDataException("Invalid saved verification purpose/prediction scope.");}
            HashSet<BigInteger> addresses=new HashSet<BigInteger>();
            foreach(object rawRow in rows)
            {
                Dictionary<string,object> row=rawRow as Dictionary<string,object>;if(row==null)throw new InvalidDataException("Invalid saved sample preview row.");BigInteger address=Counter(row,"address");if(address>=domain||!addresses.Add(address))throw new InvalidDataException("Invalid saved sample preview address.");
                double[] cpu=SavedSampleComponents(row,"cpu"),gpu=SavedSampleComponents(row,"gpu");double tolerance=SavedFiniteNumber(row["tolerance"]);if(tolerance!=2e-9)throw new InvalidDataException("Invalid saved sample tolerance.");
                bool finite=true;double pointError=0;for(int i=0;i<9;i++){if(!Finite(cpu[i])||!Finite(gpu[i]))finite=false;double difference=Math.Abs(cpu[i]-gpu[i]);if(Finite(difference))pointError=Math.Max(pointError,difference);}
                double normError=Math.Max(Math.Abs(cpu[7]-1),Math.Abs(gpu[7]-1)),spinError=Math.Max(Math.Abs(cpu[4]*cpu[4]+cpu[5]*cpu[5]+cpu[6]*cpu[6]-1),Math.Abs(gpu[4]*gpu[4]+gpu[5]*gpu[5]+gpu[6]*gpu[6]-1));
                ValidateSavedResidual(row["maxError"],finite?pointError:Double.NaN);ValidateSavedResidual(row["normError"],normError);ValidateSavedResidual(row["spinError"],spinError);
                if(SavedBoolean(row,"passed")!=(finite&&pointError<=tolerance&&normError<=tolerance&&spinError<=tolerance))throw new InvalidDataException("Saved sample verdict is inconsistent with its components.");
                if(SavedBoolean(job,"passed")&&!SavedBoolean(row,"passed"))throw new InvalidDataException("Saved successful verification contains a failed retained sample.");
            }
            string previewDigest=Text(job,"samplePreviewAddressDigest");ValidateSavedHex(previewDigest,64);if(previewDigest!=SampleAddressDigest(rows))throw new InvalidDataException("Saved sample preview address digest mismatch.");
            if(rows.Count>0)
            {
                if(Text((Dictionary<string,object>)rows[0],"address")!=Text(job,"firstAddress"))throw new InvalidDataException("Saved first sample address is inconsistent.");
                if(completed==rows.Count&&Text((Dictionary<string,object>)rows[rows.Count-1],"address")!=Text(job,"lastAddress"))throw new InvalidDataException("Saved last sample address is inconsistent.");
            }
            if(!SavedBoolean(job,"active")&&Text(job,"phase")!="interrupted"&&completed==rows.Count&&Text(job,"addressDigest")!=previewDigest)throw new InvalidDataException("Saved completed address digest disagrees with the full preview.");
            if(job.ContainsKey("sampleAssessment")&&!SameSampleAssessment(job["sampleAssessment"],SamplePreviewAssessment(rows,SavedBoolean(job,"hasPrepared"),migration,domain)))throw new InvalidDataException("Saved sample assessment disagrees with the retained actual preview.");
        }
        private static bool SameSampleAssessment(object actual,object expected)
        {
            if(actual==null||expected==null)return actual==null&&expected==null;
            Dictionary<string,object> wanted=expected as Dictionary<string,object>,given=actual as Dictionary<string,object>;
            if(wanted!=null){if(given==null||given.Count!=wanted.Count)return false;foreach(KeyValuePair<string,object> item in wanted){object value;if(!given.TryGetValue(item.Key,out value)||!SameSampleAssessment(value,item.Value))return false;}return true;}
            System.Collections.IList wantedList=expected as System.Collections.IList,givenList=actual as System.Collections.IList;
            if(wantedList!=null){if(givenList==null||givenList.Count!=wantedList.Count)return false;for(int i=0;i<wantedList.Count;i++)if(!SameSampleAssessment(givenList[i],wantedList[i]))return false;return true;}
            if(expected is string)return actual is string &&(string)actual==(string)expected;
            if(expected is bool)return actual is bool &&(bool)actual==(bool)expected;
            if(!(actual is double)&&!(actual is float)&&!(actual is decimal)&&!(actual is int)&&!(actual is long))return false;
            double number=Convert.ToDouble(actual,CultureInfo.InvariantCulture);return Finite(number)&&number==Convert.ToDouble(expected,CultureInfo.InvariantCulture);
        }
        private static string SampleAddressDigest(System.Collections.IEnumerable rows)
        {
            List<byte> encoded=new List<byte>();foreach(object raw in rows){Dictionary<string,object> row=(Dictionary<string,object>)raw;ulong address=UInt(Text(row,"address"));for(int k=0;k<8;k++)encoded.Add((byte)(address>>(56-k*8)));}using(SHA256 hash=SHA256.Create())return Hex(hash.ComputeHash(encoded.ToArray()));
        }
        private static double[] SavedSampleComponents(Dictionary<string,object> row,string key)
        {
            object raw;if(!row.TryGetValue(key,out raw))throw new InvalidDataException("Missing saved sample components.");System.Collections.IList values=raw as System.Collections.IList;if(values==null||values.Count!=9)throw new InvalidDataException("Saved samples contain exactly9 components per backend.");double[] result=new double[9];for(int i=0;i<9;i++)result[i]=values[i] is string &&(string)values[i]=="nonfinite"?Double.NaN:SavedFiniteNumber(values[i]);return result;
        }
        private static double SavedFiniteNumber(object value)
        {
            if(!(value is double)&&!(value is float)&&!(value is decimal)&&!(value is int)&&!(value is long)&&!(value is uint)&&!(value is ulong))throw new InvalidDataException("Saved sample numeric field has invalid type.");double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);if(!Finite(number))throw new InvalidDataException("Saved sample numeric field must be finite.");return number;
        }
        private static void ValidateSavedResidual(object raw,double expected)
        {
            if(!Finite(expected)){if(!(raw is string)||(string)raw!="nonfinite")throw new InvalidDataException("Saved nonfinite residual marker is missing.");return;}
            double observed=SavedFiniteNumber(raw);if(observed<0||Math.Abs(observed-expected)>Math.Max(1e-15,Math.Abs(expected)*1e-12))throw new InvalidDataException("Saved sample residual disagrees with its components.");
        }
        private static void ValidateSavedUtc(string value,bool allowEmpty)
        {DateTime parsed;if(value.Length==0&&allowEmpty)return;if(!DateTime.TryParseExact(value,"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out parsed)||parsed.Kind!=DateTimeKind.Utc)throw new InvalidDataException("Invalid saved verification UTC timestamp.");}
        private static void ValidateSavedHex(string value,int length)
        {if(value.Length!=length)throw new InvalidDataException("Invalid saved verification hash/seed.");foreach(char c in value)if(!(c>='0'&&c<='9')&&!(c>='a'&&c<='f'))throw new InvalidDataException("Invalid saved verification hash/seed.");}
        private static string Text(Dictionary<string, object> value, string key) { object v; if (!value.TryGetValue(key, out v) || !(v is string)) throw new InvalidDataException("Missing string field: " + key); return (string)v; }
        private ulong Count() { return checked(m * l); }
        private ulong Address(string value) { ulong id = UInt(value); if (id >= Count()) throw new ArgumentException("Address must be between 0 and " + S(Count() - 1) + "."); return id; }
        private static void ValidateGrid(ulong a, ulong b) { QuantumMath.ValidateGrid(a, b); }
        private static double Number(string value) { double d; if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || !Finite(d) || Math.Abs(d) > 1e12) throw new ArgumentException("Angle/parameter must be finite and within +/-1e12."); return d; }
        private static ulong UInt(string value) { ulong n; if (!UInt64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) throw new ArgumentException("Expected an unsigned decimal integer."); return n; }
        private static void Exact(string[] words, int n) { if (words.Length != n) throw new ArgumentException("Incorrect number of command arguments."); }
        private static void Need(string word, string expected) { if (!word.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected '" + expected + "'."); }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static string S(ulong value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string S(BigInteger value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static void ValidateState(double[] value) { if (value == null || value.Length != 9) throw new InvalidOperationException("State result is invalid."); foreach (double d in value) if (!Finite(d)) throw new InvalidOperationException("State calculation produced nonfinite components."); }
        private static object[] SafeNumbers(double[] value) { object[] safe = new object[value.Length]; for (int i = 0; i < value.Length; i++) safe[i] = Finite(value[i]) ? (object)value[i] : "nonfinite"; return safe; }
        private static string[] ExactStrings(double[] value) { string[] exact = new string[value.Length]; for (int i = 0; i < value.Length; i++) exact[i] = value[i].ToString("R", CultureInfo.InvariantCulture); return exact; }
        private static object StateResult(ulong id, double[] value) { return D("address", S(id), "amplitudes", D("alpha", new double[] { value[0], value[1] }, "beta", new double[] { value[2], value[3] }), "spin", D("x", value[4], "y", value[5], "z", value[6]), "norm", value[7], "p1", value[8]); }
        private static string NewSeed() { byte[] bytes = new byte[32]; using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes); return Hex(bytes); }
        private static string Seed(string value) { if (value == null || value.Length != 64) throw new InvalidDataException("Seed must contain 64 lowercase hexadecimal characters."); foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) throw new InvalidDataException("Seed is invalid."); return value; }
        private static ulong Read64(byte[] bytes) { ulong v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | bytes[i]; return v; }
        private static string HashHex(string value) { using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        private static string Hex(byte[] bytes) { StringBuilder text = new StringBuilder(bytes.Length * 2); foreach (byte b in bytes) text.Append(b.ToString("x2", CultureInfo.InvariantCulture)); return text.ToString(); }
        private static Dictionary<string, object> D(params object[] pairs) { Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal); for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]); return result; }
        private void EnsureAlive() { if (disposed) throw new ObjectDisposedException("TerminalSession"); }
        public void Dispose()
        {
            Thread pending;
            lock (sync)
            {
                if (disposed) return;
                if (JobActive()) verificationJob.CancelRequested = true;
                pending = verificationThread;
            }
            // Never hold the status lock while joining a worker that checkpoints there.
            if (pending != null && pending.IsAlive && pending != Thread.CurrentThread && !pending.Join(10000)) throw new InvalidOperationException("Verification batch has not finished; native GPU resources cannot be released safely yet.");
            if(sequences!=null)sequences.Dispose();
            try
            {
                try { if (circuit != null) circuit.Dispose(); }
                finally { if (tranche != null) tranche.Dispose(); }
            }
            finally
            {
                lock (sync)
                {
                    if (!disposed)
                    {
                        try { Save(); }
                        finally
                        {
                            try { if (gpu != null) gpu.Dispose(); }
                            finally { gpu = null; disposed = true; sessionLock.Dispose(); }
                        }
                    }
                }
            }
        }
    }

    public sealed class TerminalHost : IDisposable
    {
        private const int AttachmentBodyLimit = 6 * 1024 *1024, ReportBodyLimit = 32 *1024 *1024;
        private readonly string root, token, origin, authority;
        private readonly TcpListener listener;
        private readonly TerminalSession session;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private volatile bool stopping;
        public int Port { get; private set; }
        public string Token { get { return token; } }
        public string Origin { get { return origin; } }
        public TerminalSession Session { get { return session; } }

        public TerminalHost(string workspace, bool enableGpu)
        {
            root = Path.GetFullPath(workspace);
            if (!File.Exists(Path.Combine(root, "QUANTUM_TERMINAL.html"))) throw new FileNotFoundException("Contained quantum interface missing: QUANTUM_TERMINAL.html.");
            session = new TerminalSession(root, enableGpu);
            byte[] random = new byte[32]; using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            token = BitConverter.ToString(random).Replace("-", "").ToLowerInvariant();
            listener = new TcpListener(IPAddress.Loopback, 0);
            try { listener.Start(8); Port = ((IPEndPoint)listener.LocalEndpoint).Port; }
            catch { session.Dispose(); throw; }
            authority = "127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture); origin = "http://" + authority;
            json.MaxJsonLength = 33554432; json.RecursionLimit = 80;
        }

        public static void Run(string workspace, bool openBrowser)
        {
            using (TerminalHost host = new TerminalHost(workspace, true))
            {
                Console.WriteLine("SPINDOCTOR TERMINAL " + host.Origin + "/");
                Console.WriteLine("Procedural product-state simulator; real CPU/OpenCL GPU samples; no quantum hardware gates.");
                Console.WriteLine("Seeds, counters and errors are saved in the extracted runtime directory. Close this terminal to stop.");
                if (openBrowser)
                {
                    try { Process.Start(host.Origin + "/"); }
                    catch (Exception ex) { Console.WriteLine("Open the URL manually: " + ex.Message); }
                }
                ConsoleCancelEventHandler onCancel = delegate(object sender, ConsoleCancelEventArgs e) { e.Cancel = true; host.Stop(); };
                Console.CancelKeyPress += onCancel;
                try { host.Serve(); } finally { Console.CancelKeyPress -= onCancel; }
            }
        }

        public void Serve()
        {
            while (!stopping)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { if (stopping) break; throw; }
                catch (ObjectDisposedException) { if (stopping) break; throw; }
                using (client)
                {
                    client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
                    try { Handle(client.GetStream()); }
                    catch (Exception ex)
                    {
                        try { Send(client.GetStream(), 400, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string, object> { { "ok", false }, { "error", ex.Message } }))); }
                        catch (Exception) { }
                    }
                }
            }
        }

        private void Handle(NetworkStream stream)
        {
            Stopwatch requestTime = Stopwatch.StartNew();
            List<byte> head = new List<byte>(); int next; bool completeHeader = false;
            while (true)
            {
                SetReadBudget(stream, requestTime); next=stream.ReadByte(); if(next<0)break;
                if (next > 126 || (next < 32 && next != 9 && next != 13 && next != 10)) throw new InvalidDataException("HTTP headers must contain ASCII field text.");
                head.Add((byte)next);
                if (head.Count > 16384) throw new InvalidDataException("Request headers too large.");
                int n = head.Count;
                if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) { completeHeader = true; break; }
            }
            if (!completeHeader) throw new InvalidDataException("Incomplete HTTP request.");
            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length != 3 || !HeaderToken(first[0]) || (first[2] != "HTTP/1.1" && first[2] != "HTTP/1.0") || first[1].Length==0 || first[1][0]!='/' || first[1].IndexOf('#')>=0 || first[1].IndexOf('\\')>=0) throw new InvalidDataException("Invalid HTTP request line.");
            foreach(char c in first[1])if(c<=32||c>=127)throw new InvalidDataException("Invalid HTTP request target.");
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length && lines[i].Length > 0; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) throw new InvalidDataException("Malformed header.");
                string key = lines[i].Substring(0, colon); if(!HeaderToken(key))throw new InvalidDataException("Malformed HTTP field name.");
                string headerValue=lines[i].Substring(colon+1);foreach(char c in headerValue)if(c=='\r'||c=='\n')throw new InvalidDataException("Malformed HTTP field value.");
                if (headers.ContainsKey(key)) throw new InvalidDataException("Duplicate request header.");
                headers.Add(key, headerValue.Trim(' ','\t'));
            }
            // Consume a bounded declared body before rejecting a request. Closing a
            // Windows TCP socket with unread POST bytes can reset the connection and
            // discard an otherwise complete JSON error response in a normal browser.
            // Oversized/unknown-length bodies are rejected without unbounded draining.
            string path = first[1], stage = "latest"; int historyOffset = 0, historyLimit = 128; bool preview = false;
            int question = path.IndexOf('?');
            if (question >= 0)
            {
                string query = path.Substring(question + 1); path = path.Substring(0, question);
                HashSet<string> queryFields = new HashSet<string>(StringComparer.Ordinal);
                foreach (string field in query.Split('&'))
                {
                    string[] pair = field.Split('=');
                    if (pair.Length != 2 || !queryFields.Add(pair[0])) throw new ArgumentException("Invalid or duplicate API query field.");
                    if (path == "/api/history")
                    {
                        int value; if (!Int32.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out value)) throw new ArgumentException("Invalid history page.");
                        if (pair[0] == "offset") historyOffset = value;
                        else if (pair[0] == "limit") historyLimit = value;
                        else throw new ArgumentException("Unknown history field.");
                    }
                    else if (path.StartsWith("/api/history/", StringComparison.Ordinal) || path.StartsWith("/api/sequences/", StringComparison.Ordinal))
                    {
                        if (pair[0] == "stage") stage = pair[1];
                        else if (pair[0] == "preview" && path.StartsWith("/api/sequences/", StringComparison.Ordinal) && path.EndsWith("/json", StringComparison.Ordinal))
                        {
                            if (pair[1] != "true" && pair[1] != "false") throw new ArgumentException("Preview must be true or false.");
                            preview = pair[1] == "true";
                        }
                        else throw new ArgumentException("Unknown report query field.");
                    }
                    else throw new ArgumentException("Unknown API query.");
                }
                if (stage != "accepted" && stage != "completed" && stage != "latest") throw new ArgumentException("Unknown report stage.");
                if (preview && stage != "latest") throw new ArgumentException("Compact preview is the latest saved job only.");
            }
            bool attachmentPost = path == "/api/attachments" && first[0] == "POST", reportPost = (path == "/api/reports" || path == "/api/reports/export.csv" ||path=="/api/reports/equations") && first[0] == "POST", sequencePost=path=="/api/sequences"&&first[0]=="POST";
            int bodyLimit = attachmentPost ? AttachmentBodyLimit : reportPost ? ReportBodyLimit :sequencePost?1048576:16384;
            string lengthText; int length = 0;
            if (headers.TryGetValue("Content-Length", out lengthText) && (!Int32.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0 || length > bodyLimit)) { Fail(stream, 413, "Request exceeds its body limit of " +bodyLimit.ToString(CultureInfo.InvariantCulture) +" bytes."); return; }
            if (length >16384)
            {
                // Only these authenticated same-origin routes may allocate a large body.
                // Unknown resources and ordinary commands retain their 16 KiB bound.
                string largeHost, largeOrigin, largeAuth, largeType;
                if (!headers.TryGetValue("Host", out largeHost) || largeHost !=authority || !headers.TryGetValue("Origin", out largeOrigin) || largeOrigin !=origin || !headers.TryGetValue("Authorization", out largeAuth) || !ConstantTimeEqual(largeAuth, "Bearer " +token)) { Fail(stream, 403, "Authenticated same-origin request required before reading a large body."); return; }
                if (headers.ContainsKey("Transfer-Encoding")) { Fail(stream, 400, "Chunked requests unsupported."); return; }
                if (!headers.TryGetValue("Content-Type", out largeType) || !JsonContentType(largeType)) { Fail(stream, 415, "JSON body required."); return; }
            }
            byte[] body = new byte[length]; int at = 0;
            while (at < body.Length) { SetReadBudget(stream,requestTime);int got = stream.Read(body, at, body.Length - at); if (got <= 0) throw new InvalidDataException("Incomplete request body."); at += got; }
            string host;
            if (!headers.TryGetValue("Host", out host) || host != authority) { Fail(stream, 403, "Host rejected."); return; }
            string requestOrigin;
            if (headers.TryGetValue("Origin", out requestOrigin) && requestOrigin != origin) { Fail(stream, 403, "Origin rejected."); return; }
            if (headers.ContainsKey("Transfer-Encoding")) { Fail(stream, 400, "Chunked requests unsupported."); return; }
            if (path == "/" || path == "/QUANTUM_TERMINAL.html")
            {
                if (first[0] != "GET") { Fail(stream, 405, "Method not allowed."); return; }
                string html = File.ReadAllText(Path.Combine(root, "QUANTUM_TERMINAL.html"), Encoding.UTF8).Replace("__QUANTUM_TOKEN__", token).Replace("__QUANTUM_PORT__", Port.ToString(CultureInfo.InvariantCulture));
                Send(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)); return;
            }
            if (path == "/favicon.ico" && first[0] == "GET") { Send(stream, 204, "image/x-icon", new byte[0]); return; }
            bool attachmentExport = path.StartsWith("/api/attachments/", StringComparison.Ordinal) && path.EndsWith("/export", StringComparison.Ordinal);
            bool historyExport=path.StartsWith("/api/history/",StringComparison.Ordinal),sequenceExport=path.StartsWith("/api/sequences/",StringComparison.Ordinal);
            if (path != "/api/status" && path != "/api/command" && path != "/api/circuit/printout" && path != "/api/attachments" && path != "/api/reports" && path != "/api/reports/export.csv" && path !="/api/reports/equations" && path!="/api/presets"&&path!="/api/equations"&&path!="/api/history"&&path!="/api/sequences"&&!historyExport&&!sequenceExport&&!attachmentExport) { Fail(stream, 404, "Unknown resource."); return; }
            string authorization;
            if (!headers.TryGetValue("Authorization", out authorization) || !ConstantTimeEqual(authorization, "Bearer " + token)) { Fail(stream, 403, "Authorization rejected."); return; }
            if (path == "/api/status")
            {
                if (first[0] != "GET") { Fail(stream, 405, "Method not allowed."); return; }
                SendJson(stream, new Dictionary<string, object> { { "ok", true }, { "status", session.GetStatus() } }); return;
            }
            if(path=="/api/presets"||path=="/api/equations"||path=="/api/history")
            {
                if(first[0]!="GET"){Fail(stream,405,"Method not allowed.");return;}
                if(path=="/api/equations"){SendJson(stream,new Dictionary<string,object>{{"ok",true},{"equations",session.EquationCatalogue()}});return;}
                Dictionary<string,object> payload=(Dictionary<string,object>)(path=="/api/presets"?session.Presets():session.CommandHistoryPage(historyOffset,historyLimit));payload["ok"]=true;SendJson(stream,payload);return;
            }
            if(historyExport||sequenceExport)
            {
                if(first[0]!="GET"){Fail(stream,405,"Method not allowed.");return;}string[] bits=path.Split('/');string output;
                if(historyExport){if(bits.Length!=5||(bits[4]!="printout"&&bits[4]!="json"&&bits[4]!="csv"))throw new ArgumentException("Unknown history resource.");output=bits[4]=="json"?session.CommandJson(bits[3],stage):bits[4]=="csv"?session.CommandCsv(bits[3],stage):session.CommandPrintout(bits[3],stage);}
                else
                {
                    if (bits.Length != 5 && bits.Length != 7) throw new ArgumentException("Unknown sequence resource.");
                    int? index = null;
                    if (bits.Length == 7)
                    {
                        int value;
                        if (bits[4] != "commands" || !Int32.TryParse(bits[5], NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 0 || value > 127) throw new ArgumentException("Invalid command report index.");
                        index = value;
                    }
                    string format = bits[bits.Length - 1];
                    if (format != "printout" && format != "json" && format != "csv") throw new ArgumentException("Unknown report format.");
                    if (preview)
                    {
                        if (index.HasValue) throw new ArgumentException("Compact preview applies to a complete saved job.");
                        output = session.SequencePreviewJson(bits[3]);
                    }
                    else if (format == "printout" && !index.HasValue)
                    {
                        using (FileStream report = session.OpenSequencePrintout(bits[3], stage)) SendStream(stream, 200, "text/plain; charset=utf-8", report);
                        return;
                    }
                    else if (format == "csv" && !index.HasValue)
                    {
                        using(FileStream report=session.OpenSequenceCsv(bits[3],stage))SendStream(stream,200,"text/csv; charset=utf-8",report);
                        return;
                    }
                    else output = format == "json" ? session.SequenceJson(bits[3], index, stage) : format == "csv" ? session.SequenceCsv(bits[3],index,stage) : session.SequencePrintout(bits[3], index, stage);
                }
                Send(stream,200,bits[bits.Length-1]=="json"?"application/json; charset=utf-8":bits[bits.Length-1]=="csv"?"text/csv; charset=utf-8":"text/plain; charset=utf-8",Encoding.UTF8.GetBytes(output));return;
            }
            if (path == "/api/circuit/printout")
            {
                if (first[0] != "GET") { Fail(stream, 405, "Method not allowed."); return; }
                Send(stream, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(session.CircuitPrintout())); return;
            }
            if (path == "/api/attachments" && first[0] == "GET") { SendJson(stream, new Dictionary<string, object> { { "ok", true }, { "inputs", session.AttachmentStatus() } }); return; }
            if (attachmentExport)
            {
                if (first[0] != "GET") { Fail(stream, 405, "Method not allowed."); return; }
                string id = path.Substring("/api/attachments/".Length, path.Length -"/api/attachments/".Length -"/export".Length);
                if (id.Length !=32) { Fail(stream, 404, "Unknown attachment resource."); return; }
                foreach (char c in id) if (!(c >= 'a' && c <= 'f') && !(c >= '0' && c <= '9')) { Fail(stream, 404, "Unknown attachment resource."); return; }
                Send(stream, 200, session.AttachmentMediaType(id), session.AttachmentBytes(id)); return;
            }
            // Browsers omit Origin on some same-origin GETs, but send it for these POSTs.
            if (!headers.TryGetValue("Origin", out requestOrigin) || requestOrigin != origin) { Fail(stream, 403, "Same-origin API request required."); return; }
            if (first[0] != "POST") { Fail(stream, 405, "Method not allowed."); return; }
            string contentType;
            if (!headers.TryGetValue("Content-Type", out contentType) || !JsonContentType(contentType)) { Fail(stream, 415, "JSON body required."); return; }
            string contentEncoding;if(headers.TryGetValue("Content-Encoding",out contentEncoding)&&!contentEncoding.Equals("identity",StringComparison.OrdinalIgnoreCase)){Fail(stream,415,"Encoded request bodies are unsupported.");return;}
            if (!headers.ContainsKey("Content-Length") || length < 1) { Fail(stream, 413, "A bounded nonempty request body is required."); return; }
            JavaScriptSerializer requestJson = new JavaScriptSerializer(); requestJson.MaxJsonLength = bodyLimit; requestJson.RecursionLimit = 80;
            Dictionary<string, object> request = requestJson.DeserializeObject(new UTF8Encoding(false, true).GetString(body)) as Dictionary<string, object>;
            if (attachmentPost)
            {
                object filename, content;
                if (request ==null || request.Count !=2 || !request.TryGetValue("filename", out filename) || !(filename is string)) { Fail(stream, 400, "Expected JSON {filename,content} or {filename,contentBase64} for PDF."); return; }
                if(request.TryGetValue("contentBase64",out content)&&content is string){byte[] decoded=Convert.FromBase64String((string)content);SendJson(stream,session.AttachBinary((string)filename,decoded));return;}
                if(!request.TryGetValue("content",out content)||!(content is string)){Fail(stream,400,"Attachment content must be a string.");return;}SendJson(stream, session.AttachInput((string)filename, (string)content)); return;
            }
            if (reportPost)
            {
                if(path=="/api/reports/equations")SendJson(stream,session.ReportEquations(request));
                else if(path=="/api/reports/export.csv")Send(stream,200,"text/csv; charset=utf-8",Encoding.UTF8.GetBytes(session.ReportCsv(request)));
                else Send(stream, 200, "application/pdf", session.ReportPdf(request)); return;
            }
            if(sequencePost){SendJson(stream,session.StartSequence(request));return;}
            object command;
            if (request == null || request.Count != 1 || !request.TryGetValue("command", out command) || !(command is string)) { Fail(stream, 400, "Expected JSON {command:string}."); return; }
            SendJson(stream, session.Execute((string)command));
        }

        private void SendJson(NetworkStream stream, object value) { Send(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json.Serialize(value))); }
        private void SetReadBudget(NetworkStream stream,Stopwatch requestTime)
        {
            long remaining=15000-requestTime.ElapsedMilliseconds;if(stopping||remaining<=0)throw new InvalidDataException("Request read deadline exceeded.");stream.ReadTimeout=(int)Math.Min(5000,remaining);
        }
        private static bool HeaderToken(string text)
        {
            if(String.IsNullOrEmpty(text))return false;foreach(char c in text)if(!(c>='a'&&c<='z')&&!(c>='A'&&c<='Z')&&!(c>='0'&&c<='9')&&"!#$%&'*+-.^_`|~".IndexOf(c)<0)return false;return true;
        }
        private static bool JsonContentType(string text)
        {
            string[] parts=text.Split(';');if(!parts[0].Trim().Equals("application/json",StringComparison.OrdinalIgnoreCase)||parts.Length>2)return false;
            if(parts.Length==1)return true;string parameter=parts[1].Trim();int at=parameter.IndexOf('=');return at>0&&parameter.Substring(0,at).Trim().Equals("charset",StringComparison.OrdinalIgnoreCase)&&parameter.Substring(at+1).Trim().Trim('"').Equals("utf-8",StringComparison.OrdinalIgnoreCase);
        }
        private void Fail(NetworkStream stream, int code, string error) { Send(stream, code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string, object> { { "ok", false }, { "error", error } }))); }
        private static byte[] ResponseHeader(int code, string type, long length)
        {
            string phrase = code == 200 ? "OK" : (code == 204 ? "No Content" : "Request Rejected");
            string header = "HTTP/1.1 " + code.ToString(CultureInfo.InvariantCulture) + " " + phrase + "\r\nContent-Type: " + type + "\r\nContent-Length: " + length.ToString(CultureInfo.InvariantCulture) + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'\r\nConnection: close\r\n\r\n";
            return Encoding.ASCII.GetBytes(header);
        }
        private static void Send(NetworkStream stream, int code, string type, byte[] body)
        {
            byte[] header = ResponseHeader(code, type, body.LongLength);
            stream.Write(header, 0, header.Length); stream.Write(body, 0, body.Length); stream.Flush();
        }
        private static void SendStream(NetworkStream stream, int code, string type, FileStream body)
        {
            byte[] header = ResponseHeader(code, type, body.Length - body.Position);
            stream.WriteTimeout = 30000; stream.Write(header, 0, header.Length);
            byte[] buffer = new byte[65536]; long remaining = body.Length - body.Position;
            while (remaining > 0)
            {
                int count = body.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (count == 0) throw new IOException("Archived report ended before its declared length.");
                stream.Write(buffer, 0, count); remaining -= count;
            }
            stream.Flush();
        }
        private static bool ConstantTimeEqual(string a, string b) { int diff = a.Length ^ b.Length; for (int i = 0; i < Math.Max(a.Length, b.Length); i++) diff |= (i < a.Length ? a[i] : 0) ^ (i < b.Length ? b[i] : 0); return diff == 0; }
        public void Stop() { stopping = true; listener.Stop(); }
        public void Dispose() { Stop(); session.Dispose(); }
    }
}
