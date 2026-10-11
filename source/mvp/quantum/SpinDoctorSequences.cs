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
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace QuettaQuantum
{
    public sealed class SequenceTerminal : IDisposable
    {
        private readonly object sync = new object();
        private readonly Func<string, object> execute;
        private readonly Func<object> getStatus;
        private Func<string, object> capability;
        private Func<string, object> fileCompiler;
        private Action<Dictionary<string, object>> fileCleanup;
        private Func<string, string, object> reportCapture;
        private readonly string statePath, jobsDirectory, reportsDirectory;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly List<Dictionary<string, object>> presets = new List<Dictionary<string, object>>();
        private readonly List<string> recent = new List<string>();
        private string manifestHash = "", manifestError = "";
        private BigInteger totalJobs, totalExecuted;
        private Work job;
        private Thread thread;
        private bool disposed, disposing;
        private const int MaximumCommands = 128, MaximumFolderFiles = 128, MaximumCommandCharacters = 4096, MaximumReportBytes = 33554432;
        private const long MaximumVerificationSamples = 16384000000L;
        private sealed class Work
        {
            public string Id = Guid.NewGuid().ToString("N"), PresetId = "", Phase = "queued", Error = "", ReceivedUtc = Utc(), CompletedUtc = "", AwaitedType = "", AwaitedId = "";
            public bool Active = true;
            public volatile bool Cancel;
            public int CurrentIndex = -1, Completed;
            public string[] Pruned = new string[0];
            public string[] SkippedFiles = new string[0]; public int SkippedFileCount; // 3.1.2: unsupported folder entries are reported, not silently ignored
            public string[] Commands, Normalized;
            public Dictionary<string, object> Parameters;
            public readonly List<Dictionary<string, object>> Records = new List<Dictionary<string, object>>();
            public readonly Stopwatch Clock = new Stopwatch();
            public double PreviousElapsed;
            public Work Parent, CurrentChild;
            public bool Folder, Downloads, Purge;
            public bool VerificationRequired, ReceiptReady, InputCountCommitted;
            public long VerificationSamples = 10240;
            public string VerificationPhase = "not-requested", VerificationError = "";
            public Dictionary<string, object> Verification = new Dictionary<string, object>();
            public string FolderPath = "";
            public string ParentJobId = "", SourcePath = "", SourceFilename = "", SourceSha256 = "", SourceFormat = "";
            public string[] FilePaths;
            public int CurrentFile = -1, CompletedFiles;
            public int DiscoveredCommands, CompiledFiles;
            public string SequenceReportFile = "", SequenceReportSha256 = "";
            public long SequenceReportBytes;
            public readonly List<string> Warnings = new List<string>();
            public readonly List<Dictionary<string, object>> Files = new List<Dictionary<string, object>>();
        }
        public SequenceTerminal(string workspace, Func<string, object> executor, Func<object> status)
        {
            if (String.IsNullOrEmpty(workspace) || executor == null || status == null) throw new ArgumentException("Sequence workspace and native callbacks are required.");
            execute = executor; getStatus = status; statePath = Path.Combine(workspace, "spindoctor-sequences.json");
            jobsDirectory = Path.Combine(workspace, "sequence-jobs"); reportsDirectory = Path.Combine(workspace, "sequence-reports"); Directory.CreateDirectory(jobsDirectory); Directory.CreateDirectory(reportsDirectory);
            json.MaxJsonLength = 33554432; json.RecursionLimit = 100;
            string manifest = Path.Combine(workspace, "SPINDOCTOR_PRESETS.json"); if (!File.Exists(manifest)) manifest = Path.Combine(Path.Combine(workspace, "quantum"), "SPINDOCTOR_PRESETS.json");
            if (File.Exists(manifest)) LoadManifest(manifest); else manifestError = "The preserved 241-entry manifest is not installed in this runtime.";
            if (File.Exists(statePath)) LoadState();
        }
        public bool IsWorkerThread { get { return thread != null && Thread.CurrentThread == thread; } }
        public string CurrentReceiptOwnerId { get { if(!IsWorkerThread)return "";lock(sync){return job==null ?"":job.Folder ?job.CurrentChild==null ?"":job.CurrentChild.Id:job.Id;} } }
        public void SetCapabilityResolver(Func<string, object> resolver) { lock (sync) { Alive(); capability = resolver; } }
        public void SetFileCompiler(Func<string, object> compiler) { lock (sync) { Alive(); fileCompiler = compiler; } }
        public void SetFileCleanup(Action<Dictionary<string, object>> cleanup) { lock (sync) { Alive(); fileCleanup = cleanup; } }
        public void SetReportCapture(Func<string, string, object> capture) { lock (sync) { Alive(); reportCapture = capture; } }
        private Action<string, string> ownership;
        public void SetPresetOwnership(Action<string, string> callback) { lock (sync) { Alive(); ownership = callback; } }
        public Dictionary<string, object> GetStatus()
        {
            lock (sync) { return D("job", job == null ? null : JobObject(job), "recentJobIds", recent.ToArray(), "totalJobs", S(totalJobs), "executedCommands", S(totalExecuted), "maximumCommands", MaximumCommands, "maximumCommandsPerFile", MaximumCommands, "maximumFolderFiles", MaximumFolderFiles, "manifestSha256", manifestHash, "manifestError", manifestError, "scope", "Ordered native commands with matched asynchronous job barriers. Folder command totals accumulate per file; complete TXT exports are streamed from durable reports."); }
        }
        public object Presets()
        {
            Func<string, object> resolver; lock (sync) { Alive(); resolver = capability; }
            List<object> cards = new List<object>(); int available = 0, conditional = 0;
            foreach (Dictionary<string, object> source in presets)
            {
                Dictionary<string, object> card = new Dictionary<string, object>(source); string reason = "", availability = "available"; List<object> capabilities = new List<object>();
                string[] commands = Strings(source["commands"]);
                foreach (string command in commands)
                {
                    Dictionary<string, object> support = Support(command, resolver); capabilities.Add(support);
                    if (!Bool(support, "available")) { availability = "needs-backend"; reason = Text(support, "reason"); break; }
                    if (Bool(support, "conditional")) { availability = "conditional"; reason = Text(support, "reason"); }
                }
                string id = Text(card, "id");
                if (id == "qc-238" && availability != "needs-backend") { availability = "conditional"; reason = "Exact sequential collapse separates measured qubits into independent factors. The default 29-qubit GHZ X program is validated at 512 MiB; edited gates and other states remain subject to the declared memory budget."; }
                if (availability == "available") available++; else if (availability == "conditional") conditional++;
                card["runtimeAvailability"] = availability; card["runtimeReason"] = reason; card["nativeCapabilities"] = capabilities.ToArray(); card["scope"] = Scope(commands); card["parameterSchema"] = ParameterSchema(commands); card["selectionExecutes"] = false; cards.Add(card);
            }
            return D("schema", "SPINDOCTOR/PRESET-REGISTRY/1", "manifestSha256", manifestHash, "manifestError", manifestError, "total", cards.Count, "available", available, "conditional", conditional, "needsBackend", cards.Count -available -conditional, "presets", cards.ToArray());
        }
        public object Execute(string[] words)
        {
            return Execute(words,null);
        }
        public object Execute(string[] words,string originalCommand)
        {
            if (words == null || words.Length <2) throw new ArgumentException("Use q.sequence help or q.preset list.");
            if (Eq(words[0], "q.sequence"))
            {
                if (Eq(words[1], "status") && words.Length ==2) return GetStatus();
                if (Eq(words[1], "cancel") && words.Length ==2) return Cancel();
                if (Eq(words[1], "help") && words.Length ==2) return D("commands", new string[] { "q.sequence status", "q.sequence cancel", "q.sequence export [JOB_ID]", "q.sequence help", "q.preset list", "q.preset show ID", "q.preset run ID [name=value ...]" }, "sequenceSubmission", "POST an array of ordinary commands. Unquoted semicolons/newlines are rejected. Quoted restricted mathematical gate-sequence fields are data.", "jobBarriers", "A dependent command runs only after the matching native job is completed successfully.", "reports", "Every accepted command and asynchronous completion has a preserved JSON/TXT report, including individual failures. Measurement is not invented for read-only commands.");
                if (Eq(words[1], "export") && (words.Length ==2 || words.Length ==3)) return ReadJob(words.Length ==3 ? words[2] : null);
            }
            if (Eq(words[0], "q.preset"))
            {
                if (Eq(words[1], "list") && words.Length ==2) return Presets();
                if (Eq(words[1], "show") && words.Length ==3) return FindPresetCard(words[2]);
                if (Eq(words[1], "run") && words.Length >=3)
                {
                    Dictionary<string, object> parameters = new Dictionary<string, object>(StringComparer.Ordinal);
                    for (int i =3; i <words.Length; i++) { int equals =words[i].IndexOf('='); if (equals <=0 || equals ==words[i].Length -1 || parameters.ContainsKey(words[i].Substring(0,equals))) throw new ArgumentException("Preset overrides use unique name=value arguments."); parameters.Add(words[i].Substring(0,equals), words[i].Substring(equals +1)); }
                    return Run(null, words[2], parameters);
                }
            }
            if (Eq(words[0], "q.folder"))
            {
                if (Eq(words[1], "status") && words.Length ==2) return GetStatus();
                if (Eq(words[1], "cancel") && words.Length ==2) return Cancel();
                if (Eq(words[1], "help") && words.Length ==2) return D("commands",new string[]{ "q.folder run path=\"D:/folder\" downloads=true purge=true verify=true samples=10240", "q.folder run path=\"D:/folder\" verify=false", "q.folder status", "q.folder cancel" },"scope","Nonrecursive inputs: .json .qasm .qasm2 .txt .circuit .csv .pdf (1..128 files, 4 MiB each, 1..128 commands per file); other files are skipped and listed in skippedFiles. The first failing file stops the job. By default every file is followed by native procedural-model CPU/GPU sample verification before successful receipts or purge; this does not independently verify calculator/circuit answers. verify=false explicitly requests unverified receipts. Sample request is 1..16384000000, default10240; native boundary samples and finite-domain clamping determine actual coverage. Large requests remain cancellable asynchronous jobs using native batches of at most16384 states.","maximumVerificationSamples",MaximumVerificationSamples,"defaultVerificationSamples",10240);
                if (Eq(words[1], "run"))
                {
                    Dictionary<string,string> fields=FolderFields(words,originalCommand);string path;if(!fields.TryGetValue("path",out path))throw new ArgumentException("Folder run requires path=\"D:/folder\".");
                    bool downloads=fields.ContainsKey("downloads") ? BooleanField(fields["downloads"]) :true, purge=fields.ContainsKey("purge") ?BooleanField(fields["purge"]) :false;
                    bool verify=fields.ContainsKey("verify") ?BooleanField(fields["verify"]) :true;
                    long samples=fields.ContainsKey("samples") ?VerificationSampleField(fields["samples"]):10240;
                    return RunFolder(path,downloads,purge,verify,samples);
                }
            }
            throw new ArgumentException("Unknown sequence/preset/folder command. Use q.sequence help.");
        }
        public object Run(string[] commands, string presetId, Dictionary<string, object> parameters)
        { return RunInternal(commands,presetId,parameters,"","",""); }
        public object RunSource(string[] commands,string sourceSha256,string sourceFilename,string sourceFormat)
        {
            if(!Regex.IsMatch(sourceSha256 ?? "","^[a-f0-9]{64}$"))throw new ArgumentException("Input run requires the SHA256 of the original source bytes.");
            if(String.IsNullOrEmpty(sourceFilename) ||sourceFilename.Length>255 ||sourceFilename!=Path.GetFileName(sourceFilename) ||sourceFilename.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Input run requires a bounded source filename.");
            if(String.IsNullOrEmpty(sourceFormat) ||sourceFormat.Length>64 ||!Regex.IsMatch(sourceFormat,"^[A-Za-z0-9._/-]+$"))throw new ArgumentException("Input run requires its validated source format.");
            return RunInternal(commands,null,null,sourceSha256,sourceFilename,sourceFormat);
        }
        private object RunInternal(string[] commands,string presetId,Dictionary<string,object> parameters,string sourceSha256,string sourceFilename,string sourceFormat)
        {
            lock (sync) { Alive(); if (job != null && job.Active) throw new InvalidOperationException("A sequence is active. Finish or cancel it before starting another."); }
            Dictionary<string, object> nativeStatus = Map(getStatus()); Work next = new Work { PresetId = presetId ?? "", Parameters = parameters ==null ? new Dictionary<string, object>() : new Dictionary<string, object>(parameters),SourceSha256=sourceSha256,SourceFilename=sourceFilename,SourceFormat=sourceFormat };
            try
            {
                if (presetId !=null && presetId.Length !=0)
                {
                    Dictionary<string, object> card = FindPresetCard(presetId);
                    if (Text(card, "runtimeAvailability") == "needs-backend") throw new ArgumentException("Preset is unavailable: " +Text(card, "runtimeReason"));
                    if (commands ==null || commands.Length ==0) commands = Strings(card["commands"]);
                }
                if (commands ==null || commands.Length <1 || commands.Length >MaximumCommands) throw new ArgumentException("Sequences contain 1..128 ordinary commands.");
                commands=ExpandPresets(commands,nativeStatus);next.Commands = (string[])commands.Clone(); next.Normalized = Normalize(commands, next.Parameters, nativeStatus, !String.IsNullOrEmpty(presetId));
                Func<string, object> resolver; lock (sync) { resolver = capability; }
                for (int i =0; i <next.Normalized.Length; i++) { next.CurrentIndex = i; Dictionary<string, object> supported = Support(next.Normalized[i], resolver); if (!Bool(supported, "available")) throw new ArgumentException("Command " +i +": " +Text(supported, "reason")); }
                ValidateContext(next.Normalized, nativeStatus); next.CurrentIndex = -1;
            }
            catch (Exception error)
            {
                next.Commands = commands ==null ? new string[0] :LimitedCommands(commands); next.Normalized = next.Normalized ?? next.Commands;
                next.Phase = "failed"; next.Active = false; next.Error = "Preflight: " +error.Message; next.CompletedUtc = Utc();
                int index = next.CurrentIndex >=0 &&next.CurrentIndex <next.Commands.Length ? next.CurrentIndex :0;
                string original = next.Commands.Length >index ? next.Commands[index] :"q.preset run " +next.PresetId;
                Dictionary<string, object> failed = D("ok", false, "command", original, "error", next.Error, "status", nativeStatus);
                Dictionary<string, object> record = NewRecord(next, index, original, original);
                lock(sync) { Alive(); if(job !=null &&job.Active)throw new InvalidOperationException("Another sequence started during preflight."); next.Records.Add(record); Install(next); }
                WriteAccepted(next, record, failed); lock(sync){record["phase"] = "preflight-failed";SaveJob(next);return D("queued", false, "job", JobObject(next), "error", next.Error);}
            }
            lock (sync)
            {
                Alive(); if (job !=null &&job.Active) throw new InvalidOperationException("Another sequence started during preflight.");
                Install(next); thread =new Thread(delegate() { RunWorker(next); }); thread.IsBackground =true; StartWorker(next); return D("queued", true, "job", JobObject(next));
            }
        }
        public object RunFolder(string path,bool downloads,bool purge)
        { return RunFolder(path,downloads,purge,true,10240); }
        public object RunFolder(string path,bool downloads,bool purge,bool verify,long samples)
        {
            if(samples<1 ||samples>MaximumVerificationSamples)throw new ArgumentOutOfRangeException("samples","Folder verification samples must be 1..16384000000.");
            Func<string,object> compiler; lock(sync){Alive();if(job!=null &&job.Active)throw new InvalidOperationException("A sequence is active.");compiler=fileCompiler;}
            if(compiler==null)throw new InvalidOperationException("Structured file compilation is not installed.");
            if(!Regex.IsMatch(path ?? "", "^[A-Za-z]:[\\\\/]"))throw new ArgumentException("Folder path must be an absolute local-drive directory.");
            string resolved=Path.GetFullPath(path);DirectoryInfo directory=CheckedDirectory(resolved);
            List<string> files=new List<string>();List<string> skipped=new List<string>();int skippedCount=0;foreach(FileInfo file in directory.EnumerateFiles()) {string extension=file.Extension.ToLowerInvariant();if(extension!=".json" &&extension!=".qasm" &&extension!=".qasm2" &&extension!=".txt" &&extension!=".circuit" &&extension!=".csv" &&extension!=".pdf"){skippedCount++;if(skipped.Count<64)skipped.Add(file.Name);continue;}if((file.Attributes &FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Reparse-point input files are not accepted.");files.Add(file.FullName);if(files.Count>MaximumFolderFiles)throw new ArgumentException("Folder mode supports at most 128 input files per job.");}
            files.Sort(StringComparer.Ordinal);skipped.Sort(StringComparer.Ordinal);if(files.Count==0)throw new ArgumentException("No supported JSON/QASM/QASM2/TXT/CIRCUIT/CSV/PDF input files were found"+(skippedCount>0?" ("+skippedCount.ToString(CultureInfo.InvariantCulture)+" unsupported file(s) skipped, e.g. "+skipped[0]+"; .md and other extensions are not read).":"."));
            Work target=new Work{Folder=true,FolderPath=resolved,Downloads=downloads,Purge=purge,VerificationRequired=verify,VerificationSamples=samples,VerificationPhase=verify ?"pending":"skipped-explicit",FilePaths=files.ToArray(),SkippedFiles=skipped.ToArray(),SkippedFileCount=skippedCount,Commands=new string[0],Normalized=new string[0],Parameters=new Dictionary<string,object>()};
            lock(sync){Alive();if(job!=null &&job.Active)throw new InvalidOperationException("Another job started while enumerating the folder.");Install(target);thread=new Thread(delegate(){RunFolderWorker(target,compiler);});thread.IsBackground=true;StartWorker(target);return D("queued",true,"job",JobObject(target));}
        }
        private void StartWorker(Work target)
        { try { SaveJob(target);thread.Start(); } catch(Exception error) { target.Active=false;target.Phase="failed";target.Error="Could not checkpoint or start sequence worker: "+error.Message;target.CompletedUtc=Utc();try{SaveJob(target);}catch{}throw; } }
        private void RunFolderWorker(Work parent,Func<string,object> compiler)
        {
            try
            {
                parent.Clock.Start();
                for(int fileIndex=0;fileIndex<parent.FilePaths.Length;fileIndex++)
                {
                    if(parent.Cancel)throw new OperationCanceledException();string path=parent.FilePaths[fileIndex];
                    Dictionary<string,object> file=D("index",fileIndex,"path",path,"filename",Path.GetFileName(path),"phase","compiling","executionPhase","compiling","verificationRequired",parent.VerificationRequired,"verificationSamples",parent.VerificationSamples,"verificationPhase",parent.VerificationRequired ?"pending":"skipped-explicit","verificationError","","receiptReady",false,"verification",new Dictionary<string,object>(),"reportPhase","pending","purgePhase",parent.Purge ? "pending":"not-requested","sourceSha256","","sourceFormat","","childJobId","","error","","reportError","","purgeError","","completedCommands",0,"totalCommands",0,"totalCommandsKnown",false,"downloads",parent.Downloads,"purge",parent.Purge);
                    lock(sync){parent.CurrentFile=fileIndex;parent.Phase="compiling";parent.Files.Add(file);SaveJob(parent);}
                    try
                    {
                        CheckedDirectory(parent.FolderPath);FileInfo input=new FileInfo(path);if(!input.Exists ||!String.Equals((input.DirectoryName ?? "").TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),parent.FolderPath.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase) ||(input.Attributes &FileAttributes.ReparsePoint)!=0)throw new IOException("Input changed or became a reparse point before compilation.");
                        Dictionary<string,object> compiled=Map(compiler(path));lock(sync){file["sourceSha256"]=Value(compiled,"sourceSha256");file["sourceFormat"]=Value(compiled,"sourceFormat");file["inputName"]=Value(compiled,"inputName");}string[] commands=Strings(Value(compiled,"commands"));Dictionary<string,object> status=Map(getStatus());commands=ExpandPresets(commands,status);
                        if(commands.Length<1 ||commands.Length>MaximumCommands)throw new ArgumentException("A compiled file must produce 1..128 ordinary commands.");
                        lock(sync){parent.CompiledFiles++;parent.DiscoveredCommands=checked(parent.DiscoveredCommands+commands.Length);file["totalCommands"]=commands.Length;file["totalCommandsKnown"]=true;file["sourceSha256"]=Value(compiled,"sourceSha256");file["sourceFormat"]=Value(compiled,"sourceFormat");file["inputName"]=Value(compiled,"inputName");SaveJob(parent);}
                        Work child=new Work{Parent=parent,ParentJobId=parent.Id,VerificationRequired=parent.VerificationRequired,VerificationSamples=parent.VerificationSamples,VerificationPhase=parent.VerificationRequired ?"pending":"skipped-explicit",SourcePath=path,SourceFilename=input.Name,SourceSha256=Text(compiled,"sourceSha256"),SourceFormat=Text(compiled,"sourceFormat"),Commands=commands,Normalized=Normalize(commands,new Dictionary<string,object>(),status),Parameters=new Dictionary<string,object>()};
                        Func<string,object> resolver;lock(sync){resolver=capability;}
                        foreach(string command in child.Normalized){Dictionary<string,object> support=Support(command,resolver);if(!Bool(support,"available"))throw new ArgumentException(Text(support,"reason"));}ValidateContext(child.Normalized,status);
                        if(parent.Cancel)throw new OperationCanceledException();
                        lock(sync){file["childJobId"]=child.Id;file["phase"]="executing";file["executionPhase"]="executing";parent.CurrentChild=child;parent.Phase="executing";SaveJob(child);SaveJob(parent);}
                        RunWorker(child);
                        lock(sync){file["phase"]=child.Phase;file["executionPhase"]=child.Phase;file["error"]=child.Error;file["completedCommands"]=child.Completed;parent.Completed=checked(parent.Completed+child.Completed);child.InputCountCommitted=true;SaveJob(child);SaveJob(parent);}
                        if(child.Phase=="cancelled" ||parent.Cancel)throw new OperationCanceledException();if(child.Phase!="completed")throw new InvalidOperationException("File "+input.Name+": "+child.Error);
                        VerifyFile(parent,child,file);
                        // Archive one command at a time. Aggregate export limits must not reject completed native work.
                        bool archived=false;
                        try
                        {
                            lock(sync){file["reportPhase"]="archiving";parent.Phase="archiving";SaveJob(parent);}
                            ArchiveSequencePrintout(child);
                            lock(sync){file["reportSha256"]=child.SequenceReportSha256;file["reportBytes"]=child.SequenceReportBytes;file["reportFilename"]="SpinDoctor-File-"+parent.Id+"-"+fileIndex.ToString("000",CultureInfo.InvariantCulture)+".txt";file["printoutUrl"]="/api/sequences/"+child.Id+"/printout";file["reportPhase"]="archived";file["reportArchivedUtc"]=Utc();archived=true;SaveJob(parent);}
                        }
                        catch(Exception error){lock(sync){file["reportPhase"]="failed";file["reportError"]=error.Message;parent.Warnings.Add("File "+input.Name+" completed; combined TXT archive failed: "+error.Message+". Individual command reports are retained.");file["purgePhase"]=parent.Purge ? "skipped-report-failure":"not-requested";SaveJob(parent);}}
                        Action<Dictionary<string,object>> cleanup;lock(sync){cleanup=fileCleanup;}
                        if(parent.Purge &&archived)
                        {
                            try{if(cleanup==null)throw new InvalidOperationException("Transient-output purge is not installed.");lock(sync){file["purgePhase"]="purging";parent.Phase="purging";SaveJob(parent);}cleanup(new Dictionary<string,object>(file));lock(sync){file["transientPurgeCompleted"]=true;file["purgePhase"]="completed";file["purgeCompletedUtc"]=Utc();}}
                            catch(Exception error){lock(sync){file["purgePhase"]="failed";file["purgeError"]=error.Message;parent.Warnings.Add("File "+input.Name+" completed and archived; transient-output purge failed: "+error.Message);}}
                        }
                        lock(sync){parent.CompletedFiles++;parent.CurrentChild=null;SaveJob(parent);}if(parent.Cancel)throw new OperationCanceledException();
                    }
                    catch(Exception error){lock(sync){if(Text(file,"executionPhase")!="completed"){file["phase"]=error is OperationCanceledException ?"cancelled":"failed";file["executionPhase"]=file["phase"];file["error"]=error.Message;}SaveJob(parent);}throw;}
                }
                lock(sync){parent.Phase="completed";parent.VerificationPhase=parent.VerificationRequired ?"passed":"skipped-explicit";parent.ReceiptReady=true;}
            }
            catch(OperationCanceledException){lock(sync){parent.Phase="cancelled";parent.ReceiptReady=false;parent.Error="Folder job cancelled; original files and completed native reports are retained.";}}
            catch(Exception error){lock(sync){parent.Phase="failed";parent.Error=error.Message;}}
            finally{lock(sync){parent.Clock.Stop();parent.Active=false;parent.CompletedUtc=Utc();try{if(parent.CurrentChild!=null &&parent.CurrentChild.VerificationRequired &&parent.CurrentChild.VerificationPhase=="pending"){Work child=parent.CurrentChild;child.VerificationPhase=parent.Phase=="cancelled" ?"cancelled":"failed";child.VerificationError=parent.Error;child.ReceiptReady=false;foreach(Dictionary<string,object> file in parent.Files)if(Text(file,"childJobId")==child.Id){file["verificationPhase"]=child.VerificationPhase;file["verificationError"]=parent.Error;file["receiptReady"]=false;file["reportPhase"]="withheld-verification";file["purgePhase"]=parent.Purge ?"skipped-verification-failure":"not-requested";}SaveJob(child);}SaveJob(parent);}catch(Exception error){parent.Phase="failed";parent.ReceiptReady=false;parent.Error+=" Checkpoint: "+error.Message;}}}
        }
        private static DirectoryInfo CheckedDirectory(string path)
        {
            DirectoryInfo directory=new DirectoryInfo(path);if(!directory.Exists)throw new DirectoryNotFoundException("Input folder does not exist.");
            for(DirectoryInfo cursor=directory;cursor!=null;cursor=cursor.Parent)if((cursor.Attributes &FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Reparse-point directories are not accepted.");return directory;
        }
        private void VerifyFile(Work parent,Work child,Dictionary<string,object> file)
        {
            if(!parent.VerificationRequired)
            {
                lock(sync){child.VerificationPhase="skipped-explicit";child.ReceiptReady=true;file["verificationPhase"]=child.VerificationPhase;file["receiptReady"]=true;SaveJob(child);SaveJob(parent);}return;
            }
            string command="q.verify samples "+parent.VerificationSamples.ToString(CultureInfo.InvariantCulture)+" errors-only";
            Dictionary<string,object> evidence=D("schema","SPINDOCTOR/FOLDER-VERIFICATION/1","sequenceId",child.Id,"parentJobId",parent.Id,"sourceSha256",child.SourceSha256,"command",command,"requestedSamples",parent.VerificationSamples,"receivedUtc",Utc(),"completedUtc","","scope","Samples the current saved procedural model's predicted state amplitudes, spin components and Born probability at actual domain addresses, then compares actual CPU and OpenCL GPU calculation arrays for that model snapshot at the end of this file. This is sampled descriptor verification, not independent certification of q.math, q.model, q.resource, circuit, or tranche answers and not a physical-RAM XOR test.");
            lock(sync){child.Verification=new Dictionary<string,object>(evidence);child.VerificationPhase="verifying";parent.VerificationPhase="verifying";parent.Phase="verifying";file["verificationPhase"]="verifying";file["verification"]=child.Verification;SaveJob(child);SaveJob(parent);}
            Exception failure=null;
            try
            {
                if(Cancelled(child))throw new OperationCanceledException();
                Dictionary<string,object> before=Map(getStatus());evidence["descriptorHashBefore"]=Text(before,"descriptorHash");evidence["nativeJobIdBefore"]=Text(Map(Value(before,"lastVerificationJob")),"id");evidence["modelBefore"]=VerificationStatus(before);
                Dictionary<string,object> returned=Map(execute(command));
                Dictionary<string,object> result=Map(Value(returned,"result"));
                if(Bool(result,"queued"))
                {
                    evidence["initialResponse"]=VerificationResponse(returned);
                    Dictionary<string,object> native=Map(Value(result,"job"));string id=Text(native,"id");CheckId(id);
                    lock(sync){child.AwaitedType="verification";child.AwaitedId=id;SaveJob(child);}
                    bool cancelSent=false;
                    for(;;)
                    {
                        if(Cancelled(child) &&!cancelSent){execute("q.verify cancel");cancelSent=true;}
                        Dictionary<string,object> status=Map(getStatus());native=NativeJob(status,"verification");if(Text(native,"id")!=id)throw new InvalidOperationException("The folder verification job was replaced; successful receipts remain withheld.");
                        if(!Bool(native,"active"))
                        {
                            Func<string,string,object> capture;lock(sync){capture=reportCapture;}if(capture!=null){Dictionary<string,object> captured=Map(capture("verification",id));if(captured.Count!=0)native=captured;}
                            returned=D("ok",Text(native,"phase")=="completed" &&Bool(native,"passed"),"command",command,"result",native,"status",status);break;
                        }
                        Thread.Sleep(10);
                    }
                }
                evidence["response"]=VerificationResponse(returned);Dictionary<string,object> after=Map(getStatus());evidence["descriptorHashAfter"]=Text(after,"descriptorHash");evidence["nativeJobIdAfter"]=Text(Map(Value(after,"lastVerificationJob")),"id");evidence["modelAfter"]=VerificationStatus(after);
                if(Cancelled(child))throw new OperationCanceledException("Folder verification was cancelled; a successful receipt is not released.");
                string error=VerificationFailure(evidence,parent.VerificationSamples);if(error.Length!=0)throw new InvalidOperationException(error);
            }
            catch(Exception error){failure=error;evidence["error"]=error.Message;}
            Dictionary<string,object> finalResult=Map(Value(Map(Value(evidence,"response")),"result"));evidence["completedUtc"]=Text(finalResult,"completedUtc").Length!=0 ?Text(finalResult,"completedUtc"):Utc();evidence["gateCheckedUtc"]=Utc();
            try
            {
                string filename=child.Id+"-folder-verification.json",raw=json.Serialize(evidence);CheckArchiveBytes(raw,"Native verification JSON");Atomic(ReportPath(filename),raw);
                // Store the complete bounded verification result, plus the original durable byte identity.
                Dictionary<string,object> retained=Map(json.DeserializeObject(raw));retained["evidenceFile"]=filename;retained["evidenceSha256"]=Hash(raw);
                lock(sync){child.Verification=retained;file["verification"]=CompactVerification(retained);}
            }
            catch(Exception error){if(failure==null)failure=new InvalidOperationException("Verification evidence could not be archived: "+error.Message);}
            lock(sync)
            {
                child.AwaitedType="";child.AwaitedId="";child.VerificationError=failure==null ?"":failure.Message;child.VerificationPhase=failure==null ?"passed":failure is OperationCanceledException ?"cancelled":"failed";child.ReceiptReady=failure==null;
                file["verificationPhase"]=child.VerificationPhase;file["verificationError"]=child.VerificationError;file["receiptReady"]=child.ReceiptReady;
                if(failure!=null){file["phase"]="verification-"+child.VerificationPhase;file["reportPhase"]="withheld-verification";file["purgePhase"]=parent.Purge ?"skipped-verification-failure":"not-requested";parent.VerificationPhase=child.VerificationPhase;parent.Warnings.Add("File "+child.SourceFilename+" completed its input commands; native sample verification "+child.VerificationPhase+": "+failure.Message+". Successful receipts and purge are withheld; genuine command returns remain available as diagnostics.");}
                SaveJob(child);SaveJob(parent);
            }
            if(failure!=null)throw failure;
        }
        private Dictionary<string,object> VerificationResponse(Dictionary<string,object> response)
        {
            Dictionary<string,object> copy=D("ok",Value(response,"ok"),"command",Value(response,"command"),"error",Value(response,"error"),"elapsedMs",Value(response,"elapsedMs"),"result",Value(response,"result"),"reportId",Value(response,"reportId"),"archive",Value(response,"archive"),"nativeStatusSnapshot",VerificationStatus(Map(Value(response,"status"))));
            return Map(json.DeserializeObject(json.Serialize(copy)));
        }
        private static Dictionary<string,object> CompactVerification(Dictionary<string,object> evidence)
        {
            Dictionary<string,object> compact=new Dictionary<string,object>(evidence),response=new Dictionary<string,object>(Map(Value(evidence,"response"))),result=new Dictionary<string,object>(Map(Value(response,"result")));
            List<object> preview=Items(Value(result,"samplePreview"));if(preview.Count>4)preview=preview.GetRange(0,4);result["samplePreview"]=preview.ToArray();result["previewScope"]="Parent metadata shows at most four actual sample rows. Complete up-to-32-row evidence is retained in the child verification JSON identified by evidenceFile/evidenceSha256.";response["result"]=result;compact["response"]=response;return compact;
        }
        private static Dictionary<string,object> VerificationStatus(Dictionary<string,object> status)
        {
            Dictionary<string,object> copy=StatusSummary(status);copy.Remove("circuit");copy.Remove("tranche");foreach(string key in new string[]{"productVersion","hasPrepared","migrationT","modelGeneration","runtimeSourceHashes"})if(status.ContainsKey(key))copy[key]=status[key];return copy;
        }
        private static string VerificationFailure(Dictionary<string,object> evidence,long samples)
        {
            Dictionary<string,object> response=Map(Value(evidence,"response")),result=Map(Value(response,"result"));
            if(!response.ContainsKey("ok") ||!(response["ok"] is bool) ||!Bool(response,"ok"))return "Native procedural CPU/GPU verification did not return ok=true. "+Text(response,"error");
            if(Text(result,"phase")!="completed" ||!result.ContainsKey("active") ||!(result["active"] is bool) ||Bool(result,"active") ||!result.ContainsKey("passed") ||!(result["passed"] is bool) ||!Bool(result,"passed"))return "Native procedural CPU/GPU verification has not passed its genuine completion barrier. "+Text(result,"failure");
            if(Text(result,"failure").Length!=0 ||Text(result,"checkpointError").Length!=0)return "Native verification reports a failure or checkpoint error.";
            string nativeId=Text(result,"id");if(!Regex.IsMatch(nativeId,"^[a-f0-9]{32}$") ||nativeId==Text(evidence,"nativeJobIdBefore") ||nativeId!=Text(evidence,"nativeJobIdAfter"))return "Native verification is not a fresh matching sample job for this file.";
            string descriptor=Text(evidence,"descriptorHashBefore");if(!Regex.IsMatch(descriptor,"^[a-f0-9]{64}$") ||descriptor!=Text(evidence,"descriptorHashAfter") ||descriptor!=Text(result,"descriptorHash"))return "Verified descriptor does not match the unchanged model snapshot before and after verification.";
            try
            {
                BigInteger planned=Count(Text(result,"plannedSamples"));if(planned<=0 ||planned>samples+12 ||Count(Text(result,"requestedSamples"))!=samples ||Count(Text(result,"errorCount"))!=0)return "Native verification coverage or error count is inconsistent.";
                foreach(string key in new string[]{"cpuStates","gpuStates","verifiedCount","completedSamples","sampleCount"})if(Count(Text(result,key))!=planned)return "Native verification CPU/GPU/completed coverage counts disagree.";
                string previewError=VerificationPreviewFailure(result,planned);if(previewError.Length!=0)return previewError;
            }
            catch(Exception){return "Native verification coverage counters are missing or invalid.";}
            return "";
        }
        private static string VerificationPreviewFailure(Dictionary<string,object> result,BigInteger planned)
        {
            List<object> rows=Items(Value(result,"samplePreview"));if(rows.Count!=(int)BigInteger.Min(32,planned) ||Int(Value(result,"samplePreviewLimit"))!=32 ||Count(Text(result,"omittedSampleRows"))!=planned-rows.Count)return "Native verification actual sample preview is missing or has inconsistent coverage.";
            HashSet<string> addresses=new HashSet<string>(StringComparer.Ordinal);BigInteger domain=Count(Text(result,"stateCount"));if(domain<=0)return "Native verification finite domain is missing.";double tolerance=VerificationNumber(Value(result,"tolerance"));if(tolerance<=0 ||tolerance>2E-09)return "Native verification tolerance is invalid.";MemoryStream addressBytes=new MemoryStream();
            foreach(object item in rows)
            {
                Dictionary<string,object> row=Map(item);string address=Text(row,"address");BigInteger index=Count(address);if(index>=domain ||index>UInt64.MaxValue ||!addresses.Add(address) ||!row.ContainsKey("passed") ||!(row["passed"] is bool) ||!Bool(row,"passed"))return "Native compared sample identity or verdict is invalid.";ulong exact=(ulong)index;for(int shift=56;shift>=0;shift-=8)addressBytes.WriteByte((byte)(exact>>shift));
                List<object> cpu=Items(Value(row,"cpu")),gpu=Items(Value(row,"gpu"));if(cpu.Count!=9 ||gpu.Count!=9)return "Native compared sample does not include all nine CPU/GPU components.";
                double[] c=new double[9],g=new double[9];double error=0;for(int i=0;i<9;i++){c[i]=VerificationNumber(cpu[i]);g[i]=VerificationNumber(gpu[i]);error=Math.Max(error,Math.Abs(c[i]-g[i]));}
                double norm=Math.Max(Math.Abs(c[7]-1),Math.Abs(g[7]-1)),spin=Math.Max(Math.Abs(c[4]*c[4]+c[5]*c[5]+c[6]*c[6]-1),Math.Abs(g[4]*g[4]+g[5]*g[5]+g[6]*g[6]-1));
                if(error>tolerance ||norm>tolerance ||spin>tolerance ||VerificationNumber(Value(row,"tolerance"))!=tolerance ||Math.Abs(VerificationNumber(Value(row,"maxError"))-error)>1E-15 ||Math.Abs(VerificationNumber(Value(row,"normError"))-norm)>1E-15 ||Math.Abs(VerificationNumber(Value(row,"spinError"))-spin)>1E-15)return "Native sample component values disagree with their recorded pass/residual.";
            }
            using(addressBytes)using(SHA256 sha=SHA256.Create()){string digest=BitConverter.ToString(sha.ComputeHash(addressBytes.ToArray())).Replace("-","").ToLowerInvariant();if(digest!=Text(result,"samplePreviewAddressDigest"))return "Native actual sample address preview digest is inconsistent.";}
            return "";
        }
        private static double VerificationNumber(object value)
        {
            if(!(value is double) &&!(value is float) &&!(value is decimal) &&!(value is int) &&!(value is long))throw new InvalidDataException("Verification component is not a finite native number.");double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);if(!QuantumMath.IsFinite(number))throw new InvalidDataException("Verification component is not finite.");return number;
        }
        public Dictionary<string,object> SequenceReceiptMetadata(string jobId)
        {
            Dictionary<string,object> saved=ReadJob(jobId);RequireReceipt(saved);
            return D("verificationRequired",Bool(saved,"verificationRequired"),"verificationSamples",Value(saved,"verificationSamples"),"verificationPhase",Text(saved,"verificationPhase").Length==0 ?"legacy-unverified":Text(saved,"verificationPhase"),"receiptReady",Bool(saved,"receiptReady"),"verification",Value(saved,"verification"),"verificationError",Value(saved,"verificationError"),"receiptKind",ReceiptKind(saved));
        }
        public Dictionary<string,object> SequencePdfJobMetadata(string jobId)
        {
            Dictionary<string,object> saved=ReadJob(jobId);RequireReceipt(saved);
            // A guarded independent metadata snapshot for native PDF archive resolution. Full command bytes are resolved and hash-checked separately.
            return Map(json.DeserializeObject(json.Serialize(saved)));
        }
        private static string ReceiptKind(Dictionary<string,object> saved)
        {
            if(!Bool(saved,"verificationRequired"))return "unverified";return Bool(saved,"receiptReady") ?"native-sample-verified":"diagnostic-unverified";
        }
        private void RequireReceipt(Dictionary<string,object> saved)
        {
            if(!Bool(saved,"verificationRequired"))return;
            if(!Bool(saved,"receiptReady"))
            {
                string phase=Text(saved,"phase"),verificationPhase=Text(saved,"verificationPhase");
                if(!Bool(saved,"active") &&(phase=="failed" ||phase=="cancelled" ||phase=="interrupted" ||verificationPhase=="failed" ||verificationPhase=="cancelled" ||verificationPhase=="interrupted"))return;
                throw new InvalidOperationException("Successful receipt withheld: required native CPU/GPU sample verification has not passed. Inspect status or a terminal failed diagnostic.");
            }
            if(Bool(saved,"folder")){foreach(object item in Items(Value(saved,"files"))){Dictionary<string,object> file=Map(item);if(!Bool(file,"receiptReady") ||Text(file,"verificationPhase")!="passed")throw new InvalidDataException("Folder successful receipt has an unverified file.");ValidateVerificationEvidence(Map(Value(file,"verification")),SavedVerificationSamples(Value(saved,"verificationSamples")),Text(file,"childJobId"));}}
            else ValidateVerificationEvidence(Map(Value(saved,"verification")),SavedVerificationSamples(Value(saved,"verificationSamples")),Text(saved,"id"));
        }
        private void ValidateVerificationEvidence(Dictionary<string,object> evidence,long samples,string sequenceId)
        {
            string raw;using(FileStream source=new FileStream(ReportPath(Text(evidence,"evidenceFile")),FileMode.Open,FileAccess.Read,FileShare.Read)){if(source.Length>MaximumReportBytes)throw new InvalidDataException("Verification evidence exceeds archive bounds.");using(StreamReader reader=new StreamReader(source,new UTF8Encoding(false,true),true))raw=reader.ReadToEnd();}
            if(Hash(raw)!=Text(evidence,"evidenceSha256"))throw new InvalidDataException("Verification evidence hash mismatch.");Dictionary<string,object> original=Map(json.DeserializeObject(raw));if(Text(original,"sequenceId")!=sequenceId ||VerificationFailure(original,samples).Length!=0)throw new InvalidDataException("Successful receipt verification evidence is not a matching completed pass.");
        }
        private static bool Cancelled(Work target){return target.Cancel ||target.Parent!=null &&target.Parent.Cancel;}
        private string[] ExpandPresets(string[] commands,Dictionary<string,object> status)
        {
            List<string> expanded=new List<string>();foreach(string command in commands){CommandText(command);string[] words=Words(command);if(Eq(words[0],"q.preset")){if(words.Length<3 ||!Eq(words[1],"run"))throw new ArgumentException("A preset sequence step must use q.preset run ID.");Dictionary<string,object> card=FindPresetCard(words[2]);if(Text(card,"runtimeAvailability")=="needs-backend")throw new ArgumentException("Preset unavailable: "+Text(card,"runtimeReason"));Dictionary<string,object> parameters=new Dictionary<string,object>();for(int i=3;i<words.Length;i++){int equals=words[i].IndexOf('=');if(equals<=0 ||equals==words[i].Length-1 ||parameters.ContainsKey(words[i].Substring(0,equals)))throw new ArgumentException("Invalid preset override.");parameters.Add(words[i].Substring(0,equals),words[i].Substring(equals+1));}foreach(string literal in Normalize(Strings(card["commands"]),parameters,status)){string[] inner=Words(literal);if(Eq(inner[0],"q.preset") ||Eq(inner[0],"q.sequence") ||Eq(inner[0],"q.folder"))throw new ArgumentException("Recursive preset expansion is unsupported.");expanded.Add(literal);}}else expanded.Add(command);if(expanded.Count>MaximumCommands)throw new ArgumentException("Expanded sequence exceeds 128 ordinary commands.");}return expanded.ToArray();
        }
        private void RunWorker(Work target)
        {
            try
            {
                target.Clock.Start();
                // Validate and claim the sequence before pruning any saved preset.
                // A rejected preflight must never remove an existing program.
                if (Cancelled(target)) throw new OperationCanceledException();
                if (!String.IsNullOrEmpty(target.PresetId))
                {
                    AutoPrune(target.Commands, Map(getStatus()), target);
                    lock (sync) { SaveJob(target); }
                }
                for (int index =0; index <target.Normalized.Length; index++)
                {
                    if (Cancelled(target)) throw new OperationCanceledException();
                    Dictionary<string, object> record =NewRecord(target, index, target.Commands[index], target.Normalized[index]);
                    lock (sync) { target.CurrentIndex =index; target.Phase ="executing"; target.Records.Add(record); SaveJob(target); }
                    Dictionary<string, object> response;
                    try { response =Map(execute(target.Normalized[index])); }
                    catch (Exception error) { response =D("ok", false, "command", target.Normalized[index], "error", error.Message, "status", getStatus()); }
                    lock (sync) { totalExecuted++; }
                    WriteAccepted(target, record, response);
                    if (!Bool(response, "ok")) throw new InvalidOperationException(Text(response, "error"));
                    MarkOwnership(target, target.Normalized[index]);
                    Dictionary<string, object> result =Map(Value(response, "result"));
                    if (Bool(result, "queued"))
                    {
                        Dictionary<string, object> nativeJob =Map(Value(result, "job")); string type =JobType(target.Normalized[index]), id =Text(nativeJob, "id");
                        if (type.Length ==0 || id.Length ==0) throw new InvalidOperationException("Unsupported or missing asynchronous job identity.");
                        lock (sync) { target.AwaitedType =type; target.AwaitedId =id; target.Phase ="waiting"; record["jobId"] =id; record["jobType"] =type; SaveJob(target); }
                        Dictionary<string, object> finalStatus, finalJob;
                        WaitNative(target, type, id, out finalStatus, out finalJob);
                        Func<string, string, object> capture; lock (sync) { capture = reportCapture; }
                        if (capture != null) { Dictionary<string, object> complete = Map(capture(type, id)); if (complete.Count != 0) finalJob = complete; }
                        bool success =Text(finalJob, "phase") =="completed" && (!finalJob.ContainsKey("passed") || Bool(finalJob, "passed")) &&Text(finalJob,"error").Length ==0 &&Text(finalJob,"checkpointError").Length ==0;
                        Dictionary<string, object> final =D("ok", success, "command", target.Normalized[index], "result", finalJob, "status", finalStatus);
                        if (!success) final["error"] ="Native job ended " +Text(finalJob,"phase") +". " +Text(finalJob,"error") +" " +Text(finalJob,"checkpointError");
                        WriteCompleted(target, record, response, final);
                        if (Cancelled(target) ||Text(finalJob,"phase") =="cancelled") throw new OperationCanceledException();
                        if (!success) throw new InvalidOperationException(Text(final,"error"));
                    }
                    else
                    {
                        if(target.Normalized[index].StartsWith("q.verify ",StringComparison.OrdinalIgnoreCase) &&result.ContainsKey("passed") &&!Bool(result,"passed")) { Dictionary<string,object> failed=D("ok",false,"command",target.Normalized[index],"error","Native verification reported failure; dependent commands were stopped.","result",result,"status",Value(response,"status"));WriteCompleted(target,record,response,failed);throw new InvalidOperationException(Text(failed,"error")); }
                        lock (sync) { record["phase"] ="completed"; record["completedUtc"] =Utc(); }
                    }
                    lock (sync) { target.Completed++; target.AwaitedType =""; target.AwaitedId =""; SaveJob(target); }
                }
                lock (sync) { target.Phase ="completed"; }
            }
            catch (OperationCanceledException error) { lock (sync) { target.Phase ="cancelled"; target.Error ="Sequence cancelled; completed native work is retained."; } RecordWorkerFailure(target,error,true); }
            catch (Exception error) { lock (sync) { target.Phase ="failed"; target.Error =error.Message; } RecordWorkerFailure(target,error,false); }
            finally { lock (sync) { target.Clock.Stop(); target.Active =false; target.CompletedUtc =Utc(); target.ReceiptReady=target.Phase=="completed"&&!target.VerificationRequired; try { SaveJob(target); } catch (Exception error) { target.Phase="failed";target.ReceiptReady=false;target.Error += " Checkpoint: " +error.Message; } } }
        }
        private void RecordWorkerFailure(Work target,Exception error,bool cancelled)
        {
            Dictionary<string,object> record;lock(sync){if(target.Records.Count==0)return;record=target.Records[target.Records.Count-1];string phase=Text(record,"phase");if(phase=="completed" ||phase=="failed" ||phase=="preflight-failed")return;record["failureScope"]="sequence-orchestration";record["nativeCompletionKnown"]=false;record["error"]=error.Message;}
            try
            {
                if(Text(record,"reportFile").Length==0){lock(sync){record["phase"]=cancelled ?"cancelled":"failed";record["completedUtc"]=Utc();SaveJob(target);}return;}
                Dictionary<string,object> accepted=Map(json.DeserializeObject(ReadReport(record,"accepted",".json"))),initial=Map(Value(accepted,"response")),status;
                try{status=Map(getStatus());}catch{status=Map(Value(record,"statusSnapshot"));}
                Dictionary<string,object> final=D("ok",false,"command",record["normalizedCommand"],"error",error.Message,"failureScope","sequence-orchestration","nativeCompletionKnown",false,"result",D("phase",cancelled ?"orchestration-cancelled":"orchestration-failed","nativeCompletionKnown",false,"awaitedJobType",target.AwaitedType,"awaitedJobId",target.AwaitedId),"status",status);
                WriteCompleted(target,record,initial,final);
            }
            catch(Exception reportingError){lock(sync){record["phase"]=cancelled ?"cancelled":"failed";record["completedUtc"]=Utc();record["failureReportError"]=reportingError.Message;target.Error+=" Failure report: "+reportingError.Message;}}
        }
        private void WaitNative(Work target, string type, string id, out Dictionary<string, object> status, out Dictionary<string, object> nativeJob)
        {
            bool cancelSent =false;
            for (;;)
            {
                if (Cancelled(target) &&!cancelSent) { execute(CancelCommand(type)); cancelSent =true; }
                status =Map(getStatus()); nativeJob =NativeJob(status, type);
                if (Text(nativeJob,"id") !=id) throw new InvalidOperationException("The awaited native job was replaced; dependent commands were stopped.");
                lock (sync) { if (Cancelled(target)) target.Phase ="cancelling"; }
                if (!Bool(nativeJob,"active")) return;
                Thread.Sleep(10);
            }
        }
        public object Cancel()
        {
            lock (sync) { Alive(); if (job !=null &&job.Active) { job.Cancel =true; job.Phase ="cancelling"; SaveJob(job); } return D("cancelRequested", job !=null &&job.Active, "job", job ==null ? null :JobObject(job)); }
        }
        public string SequencePrintout(string jobId, int? commandIndex) { return SequencePrintout(jobId, commandIndex, "latest"); }
        public string SequencePreviewJson(string jobId) { return json.Serialize(D("job",ReadJob(jobId))); }
        public string SequenceCsv(string jobId,int? commandIndex,string stage)
        {
            CheckStage(stage);Dictionary<string,object> saved=ReadJob(jobId);RequireReceipt(saved);StringBuilder text=new StringBuilder();
            using(StringWriter writer=new StringWriter(text,CultureInfo.InvariantCulture))
            {SpinDoctorReportCsv.Writer csv=new SpinDoctorReportCsv.Writer(writer,SpinDoctorReportCsv.MaximumStringBytes);WriteSequenceCsv(csv,saved,stage,commandIndex,"/native");}
            return text.ToString();
        }
        public FileStream OpenSequenceCsv(string jobId,string stage)
        {
            CheckStage(stage);Dictionary<string,object> saved=ReadJob(jobId);RequireReceipt(saved);
            string temporary=Path.Combine(reportsDirectory,Text(saved,"id")+"-csv-export-"+Guid.NewGuid().ToString("N")+".tmp");
            FileStream snapshot=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536,FileOptions.DeleteOnClose);
            try{using(StreamWriter writer=new StreamWriter(snapshot,new UTF8Encoding(false),65536,true)){SpinDoctorReportCsv.Writer csv=new SpinDoctorReportCsv.Writer(writer,SpinDoctorReportCsv.MaximumStreamBytes);WriteSequenceCsv(csv,saved,stage,null,"/native");}snapshot.Position=0;return snapshot;}catch{snapshot.Dispose();throw;}
        }
        private void WriteSequenceCsv(SpinDoctorReportCsv.Writer csv,Dictionary<string,object> saved,string stage,int? commandIndex,string prefix)
        {
            RequireReceipt(saved);string id=Text(saved,"id");csv.Context(Bool(saved,"folder")?"folder":"sequence",id,stage);csv.Value(prefix+"/job",saved);
            if(Bool(saved,"folder"))
            {
                if(commandIndex.HasValue)throw new ArgumentException("Folder command CSV reports use the file's childJobId.");
                foreach(object item in Items(Value(saved,"files"))){Dictionary<string,object> file=Map(item);string child=Text(file,"childJobId");if(child.Length!=0)WriteSequenceCsv(csv,ReadJob(child),stage,null,prefix+"/files/"+Int(Value(file,"index")).ToString(CultureInfo.InvariantCulture));}return;
            }
            bool found=false;foreach(object item in Items(Value(saved,"records")))
            {
                Dictionary<string,object> record=Map(item);int index=Int(Value(record,"index"));if(commandIndex.HasValue&&index!=commandIndex.Value)continue;if(Text(record,"reportFile").Length==0)continue;found=true;
                string file=ReportFile(record,stage,".json"),raw=ReadReport(record,stage,".json");bool completed=file==Text(record,"completedReportJsonFile");string selected=completed?"completed":"accepted",path=prefix+"/commandReports/"+index.ToString(CultureInfo.InvariantCulture);
                csv.Context("sequence-command",id+":"+index.ToString(CultureInfo.InvariantCulture),selected);csv.Value(path+"/requestedStage",stage);csv.Value(path+"/selectedStage",selected);csv.Value(path+"/archiveFilename",file);csv.Provenance(path+"/sourceArchiveSha256",Hash(raw));
                Dictionary<string,object> archive=Map(json.DeserializeObject(raw));csv.Value(path+"/archiveHashAuthenticated",record.ContainsKey(completed?"completedReportJsonSha256":"reportJsonSha256"));csv.Value(path+"/commandReport",archive);Dictionary<string,object> equations=SpinDoctorReportEquations.ForArchive(archive);if(Int(Value(equations,"count"))>0)csv.Value(path+"/equationRepresentations",equations);
            }
            if(commandIndex.HasValue&&!found)throw new ArgumentException("This sequence command has not produced a report.");
        }
        public FileStream OpenSequencePrintout(string jobId,string stage)
        {
            CheckStage(stage);Dictionary<string,object> saved=ReadJob(jobId);RequireReceipt(saved);string archived=Text(saved,"sequenceReportFile");
            if(stage=="latest" &&archived.Length!=0)
            {
                string path=ReportPath(archived);FileStream source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                try{if(source.Length!=Long(Value(saved,"sequenceReportBytes")) ||HashStream(source)!=Text(saved,"sequenceReportSha256"))throw new InvalidDataException("Sequence report hash mismatch; the archived return was altered.");source.Position=0;return source;}catch{source.Dispose();throw;}
            }
            // A temporary streamed snapshot also supports interrupted jobs and bounded-memory folder exports.
            string temporary=Path.Combine(reportsDirectory,Text(saved,"id")+"-export-"+Guid.NewGuid().ToString("N")+".tmp");
            FileStream snapshot=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536,FileOptions.DeleteOnClose);
            try{using(StreamWriter writer=new StreamWriter(snapshot,new UTF8Encoding(false),65536,true)){WriteSequencePrintout(writer,saved,stage);}snapshot.Position=0;return snapshot;}catch{snapshot.Dispose();throw;}
        }
        private void ArchiveSequencePrintout(Work target)
        {
            Dictionary<string,object> saved;lock(sync){saved=JobObject(target);}string filename=target.Id+"-sequence-latest.txt",path=ReportPath(filename),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(StreamWriter writer=new StreamWriter(temporary,false,new UTF8Encoding(false),65536)){WriteSequencePrintout(writer,saved,"latest");}
                string checksum;long bytes;using(FileStream source=new FileStream(temporary,FileMode.Open,FileAccess.Read,FileShare.Read)){bytes=source.Length;checksum=HashStream(source);}
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
                lock(sync){target.SequenceReportFile=filename;target.SequenceReportSha256=checksum;target.SequenceReportBytes=bytes;SaveJob(target);}
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        private void WriteSequencePrintout(TextWriter writer,Dictionary<string,object> saved,string stage)
        {
            CheckStage(stage);WriteRunSummary(writer,saved,stage);
            if(Bool(saved,"folder"))
            {
                foreach(object item in Items(Value(saved,"files"))){Dictionary<string,object> file=Map(item);WriteFileSummary(writer,file);if(Text(file,"childJobId").Length!=0)WriteSequencePrintout(writer,ReadJob(Text(file,"childJobId")),stage);}return;
            }
            foreach(object item in Items(Value(saved,"records"))){Dictionary<string,object> record=Map(item);if(Text(record,"reportFile").Length!=0){writer.Write(ReadReport(record,stage,".txt"));writer.WriteLine();}}
            WriteVerificationAppendix(writer,saved);
        }
        private static void WriteRunSummary(TextWriter writer,Dictionary<string,object> saved,string stage,int? commandIndex=null)
        {
            bool folder=Bool(saved,"folder"),known=saved.ContainsKey("totalCommandsKnown") ?Bool(saved,"totalCommandsKnown"):!folder;
            string phase=Reported(saved,"phase"),id=Reported(saved,"id");
            writer.WriteLine("SPINDOCTOR " + TerminalSession.ProductVersion.Replace("-candidate", " CANDIDATE").ToUpperInvariant() + " — DETAILED NATIVE RUN REPORT");
            writer.WriteLine("RECEIPT STAMP: "+(Bool(saved,"verificationRequired") ?Bool(saved,"receiptReady") ?"NATIVE PROCEDURAL CPU/GPU SAMPLE VERIFICATION PASSED":"DIAGNOSTIC / UNVERIFIED — SUCCESSFUL RECEIPT WITHHELD":"UNVERIFIED — NATIVE SAMPLE VERIFICATION NOT REQUIRED"));
            writer.WriteLine("Run identity: "+id+"; kind: "+(folder ?"folder":"command sequence")+"; report stage: "+stage);
            writer.WriteLine("Stage meaning: accepted preserves the initial native return, including a rejected command; completed preserves a matched completion or explicit orchestration-failure return; latest selects the newest available archived stage. The run metadata below describes the saved run state, independently of the selected raw archive stage.");
            writer.WriteLine("Run outcome: "+phase+"; active: "+(saved.ContainsKey("active") ?Bool(saved,"active").ToString().ToLowerInvariant():"unknown"));
            writer.WriteLine("Outcome meaning: "+PhaseMeaning(phase));
            writer.WriteLine("Successful command barriers: "+Reported(saved,"completedCommands")+" / "+Reported(saved,"totalCommands")+(known ?"; final command total is known.":"; discovered commands only — the final requested total is unknown."));
            writer.WriteLine("Count definition: a completed command returned successfully and, when asynchronous, passed its matching native completion barrier. Acceptance alone, rejection, cancellation and unknown outcomes are not credited as successful completion.");
            writer.WriteLine("Counter scope: command/file progress is orchestration metadata. Read-only queries, represented family credits, actual classical work and physical quantum hardware activity remain separate in the native responses.");
            writer.WriteLine("Received UTC: "+Reported(saved,"receivedUtc"));writer.WriteLine((phase=="completed" ?"Execution completed UTC: ":"Run stopped UTC: ")+Reported(saved,"completedUtc"));writer.WriteLine("Observed run wall time: "+Elapsed(saved));
            WriteVerificationSummary(writer,saved);
            WriteCommandAnswerSummary(writer,saved,commandIndex);
            writer.WriteLine("Failure detail: "+NoError(saved,"error"));
            if(folder)
            {
                writer.WriteLine("Input folder: "+Reported(saved,"folderPath"));writer.WriteLine("Files processed: "+Reported(saved,"completedFiles")+" / "+Reported(saved,"totalFiles")+"; files compiled: "+Reported(saved,"compiledFiles"));
                writer.WriteLine("File count definition: processed files completed native execution and finished archive/purge handling, including separately reported warnings. Future files and interrupted files are not counted as processed.");
                writer.WriteLine("Requested file downloads: "+Flag(saved,"downloads")+"; transient-output purge: "+Flag(saved,"purge"));
                writer.WriteLine("Memory scope: purge releases supported transient coherent-output references after durable report capture. Inputs, seeds, counters, checkpoints and native history remain preserved; OS reclamation is not a measured result.");
                writer.WriteLine("Each file below records execution, native sample verification, archive and purge outcomes independently. Verification samples are extra work and do not alter input-command completion totals.");
            }
            else
            {
                if(Text(saved,"parentJobId").Length!=0)writer.WriteLine("Parent folder run: "+Text(saved,"parentJobId"));
                if(Text(saved,"sourceSha256").Length!=0 ||Text(saved,"sourceFilename").Length!=0)
                {writer.WriteLine("Source file: "+(Text(saved,"sourcePath").Length!=0 ?Reported(saved,"sourcePath"):Reported(saved,"sourceFilename")));writer.WriteLine("Source format: "+Reported(saved,"sourceFormat"));writer.WriteLine("Original source SHA256: "+Reported(saved,"sourceSha256"));}
                if(Text(saved,"parentJobId").Length!=0)writer.WriteLine("Archive/purge timing: this child report describes native execution. The parent folder report records subsequent full-file archive and transient-purge outcomes.");
                int accepted=0,unknown=0,pending=0;foreach(object item in Items(Value(saved,"records"))){Dictionary<string,object> record=Map(item);if(Text(record,"reportFile").Length!=0)accepted++;if(record.ContainsKey("nativeCompletionKnown") &&!Bool(record,"nativeCompletionKnown"))unknown++;else if(Text(record,"phase")!="completed" &&Text(record,"phase")!="failed" &&Text(record,"phase")!="preflight-failed")pending++;}
                writer.WriteLine("Durable initial-return archives: "+accepted.ToString(CultureInfo.InvariantCulture)+"; unfinished report steps: "+pending.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine("Native completion knowledge: "+(unknown>0 ?unknown.ToString(CultureInfo.InvariantCulture)+" step(s) have unknown native completion after an orchestration failure. Inspect genuine accepted returns and native status; no backend outcome is fabricated.":phase=="completed" ?"every counted command passed its native completion barrier.":"partial/stopped run; completion is asserted only for counted successful barriers. Inspect each command's returned and completion stages."));
            }
            List<object> warnings=Items(Value(saved,"warnings"));writer.WriteLine("Warnings: "+(!saved.ContainsKey("warnings") ?"unknown (not recorded)":warnings.Count==0 ?"none recorded":warnings.Count.ToString(CultureInfo.InvariantCulture)));foreach(object warning in warnings)writer.WriteLine("  - "+Convert.ToString(warning,CultureInfo.InvariantCulture));
            writer.WriteLine("Recovery: inspect q.sequence status (or q.folder status) and the durable command returns before retrying stopped work. No automatic replay is performed; rerunning an input may repeat measurements or state changes.");
            writer.WriteLine("Report access: q.sequence export "+id+" returns saved metadata. Complete TXT/JSON command archives remain available through the command report links; accepted and completed stages retain their original native responses.");
            writer.WriteLine("Values: an explicit zero is retained as a recorded value; absent fields are shown as unknown. No measurements, arithmetic work or elapsed timing are inferred from missing fields.");writer.WriteLine();
        }
        private static void WriteFileSummary(TextWriter writer,Dictionary<string,object> file)
        {
            writer.WriteLine("FILE: "+Reported(file,"path"));writer.WriteLine("File index (zero-based): "+Reported(file,"index")+"; filename: "+Reported(file,"filename"));writer.WriteLine("Child native run: "+Reported(file,"childJobId"));
            writer.WriteLine("File outcome: "+Reported(file,"phase")+"; native execution: "+Reported(file,"executionPhase")+"; full-file TXT archive: "+Reported(file,"reportPhase")+"; transient purge: "+Reported(file,"purgePhase"));
            bool totalKnown=file.ContainsKey("totalCommandsKnown") ?Bool(file,"totalCommandsKnown"):Int(Value(file,"totalCommands"))>0;
            writer.WriteLine("Successful command barriers in this file: "+Reported(file,"completedCommands")+" / "+Reported(file,"totalCommands")+(totalKnown ?"; compiled command total is known.":"; discovered commands only — compilation did not establish a final command total."));writer.WriteLine("Source format: "+Reported(file,"sourceFormat")+"; original source SHA256: "+Reported(file,"sourceSha256"));
            writer.WriteLine("Execution/compilation error: "+NoError(file,"error"));writer.WriteLine("Archive error: "+NoError(file,"reportError"));writer.WriteLine("Purge error: "+NoError(file,"purgeError"));
            WriteVerificationSummary(writer,file);
            writer.WriteLine("Full-file report name: "+Reported(file,"reportFilename")+"; bytes: "+Reported(file,"reportBytes")+"; SHA256: "+Reported(file,"reportSha256"));
            if(Text(file,"printoutUrl").Length!=0)writer.WriteLine("Full-file TXT resource: "+Text(file,"printoutUrl"));
            if(Text(file,"childJobId").Length!=0)writer.WriteLine("Recovery metadata: q.sequence export "+Text(file,"childJobId")+". Individual command TXT/JSON returns remain available if the combined archive failed.");writer.WriteLine();
        }
        private static string Reported(Dictionary<string,object> value,string key){object item=Value(value,key);return item==null ||item is string &&((string)item).Length==0 ?"unknown":Convert.ToString(item,CultureInfo.InvariantCulture);}
        private static void WriteVerificationSummary(TextWriter writer,Dictionary<string,object> saved)
        {
            writer.WriteLine("Required native sample verification: "+Flag(saved,"verificationRequired")+"; verification phase: "+Reported(saved,"verificationPhase")+"; receipt ready: "+Flag(saved,"receiptReady"));writer.WriteLine("Verification error: "+NoError(saved,"verificationError"));
            Dictionary<string,object> evidence=Map(Value(saved,"verification"));if(evidence.Count==0){writer.WriteLine("Verification scope: no passed native sampling evidence is attached to this record. Unverified output is not a verification certificate.");return;}
            writer.WriteLine("Verification received UTC: "+Reported(evidence,"receivedUtc")+"; completed UTC: "+Reported(evidence,"completedUtc"));writer.WriteLine("Verification command: "+Reported(evidence,"command"));writer.WriteLine("Verification scope: "+Reported(evidence,"scope"));
            Dictionary<string,object> result=Map(Value(Map(Value(evidence,"response")),"result")),model=Map(Value(evidence,"modelBefore"));writer.WriteLine("Verified descriptor SHA256: "+Reported(result,"descriptorHash"));
            writer.WriteLine("Actual compared samples: CPU="+Reported(result,"cpuStates")+"; GPU="+Reported(result,"gpuStates")+"; verified="+Reported(result,"verifiedCount")+"; completed/planned="+Reported(result,"completedSamples")+" / "+Reported(result,"plannedSamples")+"; tolerance="+Reported(result,"tolerance")+"; max error="+Reported(result,"maxError"));
            writer.WriteLine("Model at verification: prepared="+Flag(model,"hasPrepared")+"; migrationT="+Reported(model,"migrationT")+"; generation="+Reported(model,"modelGeneration")+". An unprepared/T=0 identity model is a valid but limited test; it does not demonstrate migration/gate behavior or certify the supplied calculator results.");
            writer.WriteLine("Verification timing: "+Reported(result,"elapsedMs")+" ms (native sample job); complete evidence JSON: "+Reported(evidence,"evidenceFile")+"; SHA256: "+Reported(evidence,"evidenceSha256"));
            writer.WriteLine("Full native sample rows: recorded verification response follows the command details in the verification appendix; the independent JSON evidence and its byte hash above remain authoritative.");
        }
        private static void WriteCommandAnswerSummary(TextWriter writer,Dictionary<string,object> saved,int? commandIndex)
        {
            if(Bool(saved,"folder"))return;JavaScriptSerializer serializer=new JavaScriptSerializer{MaxJsonLength=MaximumReportBytes,RecursionLimit=100};bool heading=false;
            foreach(object item in Items(Value(saved,"records")))
            {
                Dictionary<string,object> record=Map(item);int index=Int(Value(record,"index"));if(commandIndex.HasValue &&index!=commandIndex.Value)continue;Dictionary<string,object> response=Map(Value(record,"finalResponse"));if(response.Count==0)response=Map(Value(record,"response"));if(response.Count==0)continue;
                if(!heading){writer.WriteLine();writer.WriteLine("COMMAND ANSWERS — ACTUAL RETURNED DATA");writer.WriteLine("These answers use each command's supplied inputs. Procedural-model sample verification is a separate check; it does not replace or independently certify these answers.");heading=true;}
                writer.WriteLine("Command "+index.ToString(CultureInfo.InvariantCulture)+": "+Reported(record,"command"));Dictionary<string,object> result=Map(Value(response,"result"));
                writer.WriteLine("Returned answer: "+serializer.Serialize(Value(response,"result")));
                if(Bool(result,"previewOnly"))writer.WriteLine("Answer detail is truncated in this summary; consult the complete unchanged native command response below or its TXT/JSON archive.");
                if(Text(result,"namespace")=="q.math")writer.WriteLine("Data source: bounded classical calculator result for these operands. This command does not prepare or mutate the saved procedural model; a verification Bloch sphere describes that saved model, not these operands.");
            }
            if(heading)writer.WriteLine();
        }
        private static void WriteVerificationAppendix(TextWriter writer,Dictionary<string,object> saved)
        {
            Dictionary<string,object> evidence=Map(Value(saved,"verification"));if(evidence.Count==0)return;writer.WriteLine();writer.WriteLine("VERIFICATION APPENDIX — SAVED PROCEDURAL MODEL SAMPLE DATA");writer.WriteLine("This is the full recorded native sampling response, including actual CPU/GPU preview rows. These states describe the saved procedural model snapshot; they are separate from the command operands and answers above.");JavaScriptSerializer serializer=new JavaScriptSerializer{MaxJsonLength=MaximumReportBytes,RecursionLimit=100};writer.WriteLine(serializer.Serialize(Value(evidence,"response")));writer.WriteLine();
        }
        private static string Flag(Dictionary<string,object> value,string key){return !value.ContainsKey(key) ||!(value[key] is bool) ?"unknown":((bool)value[key] ?"true":"false");}
        private static string NoError(Dictionary<string,object> value,string key){return !value.ContainsKey(key) ?"unknown (not separately recorded)":Text(value,key).Length==0 ?"none recorded":Text(value,key);}
        private static string Elapsed(Dictionary<string,object> value){double milliseconds;return value.ContainsKey("elapsedMs") &&Double.TryParse(Convert.ToString(value["elapsedMs"],CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out milliseconds) &&QuantumMath.IsFinite(milliseconds) &&milliseconds>=0 ?milliseconds.ToString("0.###",CultureInfo.InvariantCulture)+" ms (observed run wall time; not CPU/GPU gate timing)":"unknown";}
        private static string PhaseMeaning(string phase){switch(phase){case "completed":return "Native command execution finished. Archive/purge warnings, if present, describe separate reporting or cleanup outcomes.";case "failed":return "The run stopped on a genuine command, barrier, compilation or checkpoint error; successful earlier barriers remain counted and retained.";case "cancelled":return "Cancellation stopped further dispatch. Accepted or partial native work remains in its reports and checkpoint.";case "interrupted":return "The process ended before the run completion barrier. No automatic replay or fabricated completion occurred.";case "archiving":return "Native execution for the current file finished; durable full-file report capture is in progress.";case "purging":return "The current file's durable report is captured; requested transient-output cleanup is in progress.";case "waiting":return "A genuine native asynchronous job was accepted; dependent commands wait for its matching completion.";default:return "The saved phase is reported as observed. Unfinished work has no asserted final native outcome.";}}
        private string ReportPath(string filename)
        {
            if(String.IsNullOrEmpty(filename) ||Path.GetFileName(filename)!=filename ||filename.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("Invalid report file.");CheckedDirectory(reportsDirectory);string path=Path.Combine(reportsDirectory,filename);if(File.Exists(path) &&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Reparse-point report files are not accepted.");return path;
        }
        public string SequencePrintout(string jobId, int? commandIndex, string stage)
        {
            CheckStage(stage);Dictionary<string, object> saved =ReadJob(jobId);RequireReceipt(saved); List<object> records =Items(saved["records"]); StringBuilder text =new StringBuilder();
            using(StringWriter writer=new StringWriter(text,CultureInfo.InvariantCulture)){WriteRunSummary(writer,saved,stage,commandIndex);}
            if(Bool(saved,"folder")) { if(commandIndex.HasValue)throw new ArgumentException("Folder command reports use the file's childJobId.");foreach(object item in Items(Value(saved,"files"))){Dictionary<string,object> file=Map(item);using(StringWriter writer=new StringWriter(text,CultureInfo.InvariantCulture)){WriteFileSummary(writer,file);}if(Text(file,"childJobId").Length!=0){string report=SequencePrintout(Text(file,"childJobId"),null,stage);if(text.Length+report.Length>33554432)throw new InvalidOperationException("Combined folder report exceeds 32 MiB; export each file separately.");text.Append(report);}}return text.ToString(); }
            bool found =false; foreach (object item in records) { Dictionary<string, object> record =Map(item); int index =Int(Value(record,"index")); if (commandIndex.HasValue &&index !=commandIndex.Value) continue; found =true; string report = ReadReport(record, stage, ".txt"); if (text.Length + report.Length > 33554432) throw new InvalidOperationException("Combined report exceeds 32 MiB; export each command separately."); text.AppendLine(report); }
            if (commandIndex.HasValue &&!found) throw new ArgumentException("This sequence command has not produced a report.");using(StringWriter writer=new StringWriter(text,CultureInfo.InvariantCulture)){WriteVerificationAppendix(writer,saved);}if(text.Length>33554432)throw new InvalidOperationException("Combined report exceeds 32 MiB; export each command separately.");return text.ToString();
        }
        public string SequenceJson(string jobId, int? commandIndex) { return SequenceJson(jobId,commandIndex,"latest"); }
        public string SequenceJson(string jobId, int? commandIndex, string stage)
        {
            Dictionary<string, object> saved =ReadJob(jobId);RequireReceipt(saved); if (!commandIndex.HasValue) { List<object> complete=new List<object>();long size = 0;if(Bool(saved,"folder")){foreach(object item in Items(Value(saved,"files"))){Dictionary<string,object> file=Map(item);if(Text(file,"childJobId").Length!=0){string report=SequenceJson(Text(file,"childJobId"),null,stage);size+=report.Length;if(size>33554432)throw new InvalidOperationException("Combined report exceeds 32 MiB; export each file separately.");complete.Add(json.DeserializeObject(report));}}}else foreach(object value in Items(saved["records"])) { Dictionary<string,object> record=Map(value);if(Text(record,"reportFile").Length!=0){string report=ReadReport(record,stage,".json");size+=report.Length;if(size>33554432)throw new InvalidOperationException("Combined report exceeds 32 MiB; export each command separately.");complete.Add(json.DeserializeObject(report));} }return json.Serialize(D("job",saved,"completeCommandReports",complete.ToArray())); }
            foreach (object item in Items(saved["records"])) { Dictionary<string, object> record =Map(item); if (Int(Value(record,"index")) ==commandIndex.Value) return ReadReport(record,stage,".json"); }
            throw new ArgumentException("This sequence command has not produced a report.");
        }
        private void WriteAccepted(Work target, Dictionary<string, object> record, Dictionary<string, object> response)
        {
            string stem =target.Id +"-" +Int(record["index"]).ToString("000",CultureInfo.InvariantCulture) +"-accepted";
            Dictionary<string, object> full =D("schema","SPINDOCTOR/SEQUENCE-COMMAND/1","sequenceId",target.Id,"index",record["index"],"command",record["command"],"normalizedCommand",record["normalizedCommand"],"receivedUtc",record["receivedUtc"],"completedUtc",Bool(response,"ok")&&!Bool(Map(Value(response,"result")),"queued")?Utc():"","completionNote",!Bool(response,"ok")?"not completed: command rejected":Bool(Map(Value(response,"result")),"queued")?"pending: asynchronous job accepted; see the completed-stage report":"returned synchronously","response",response);
            string native =json.Serialize(full);CheckArchiveBytes(native,"Native JSON");string text =ReportText(full);CheckArchiveBytes(text,"Native TXT");Atomic(Path.Combine(reportsDirectory,stem +".json"),native); Atomic(Path.Combine(reportsDirectory,stem +".txt"),text);
            lock(sync) { record["reportFile"] =stem +".txt"; record["reportJsonFile"] =stem +".json"; record["reportSha256"] =Hash(text);record["reportJsonSha256"]=Hash(native); record["reportFilename"] ="SpinDoctor-" +stem +".txt"; record["response"] =PreviewResponse(response); record["responseComplete"] =json.Serialize(response).Length <=8192; record["statusSnapshot"] =StatusSummary(Map(Value(response,"status"))); record["phase"] =Bool(response,"ok") ? "returned" :"failed"; SaveJob(target); }
        }
        private void WriteCompleted(Work target, Dictionary<string, object> record, Dictionary<string, object> initial, Dictionary<string, object> final)
        {
            string stem =target.Id +"-" +Int(record["index"]).ToString("000",CultureInfo.InvariantCulture) +"-completed", utc =Utc();
            Dictionary<string, object> full =D("schema","SPINDOCTOR/SEQUENCE-COMMAND/1","sequenceId",target.Id,"index",record["index"],"command",record["command"],"normalizedCommand",record["normalizedCommand"],"receivedUtc",record["receivedUtc"],"completedUtc",utc,"response",initial,"finalResponse",final);
            string native =json.Serialize(full);CheckArchiveBytes(native,"Native JSON");string text =ReportText(full);CheckArchiveBytes(text,"Native TXT");Atomic(Path.Combine(reportsDirectory,stem +".json"),native); Atomic(Path.Combine(reportsDirectory,stem +".txt"),text);
            lock(sync) { record["completedReportFile"] =stem +".txt"; record["completedReportJsonFile"] =stem +".json"; record["completedReportFilename"] ="SpinDoctor-" +stem +".txt"; record["completedReportSha256"] =Hash(text);record["completedReportJsonSha256"]=Hash(native); record["finalResponse"] =PreviewResponse(final);record["finalResponseComplete"]=json.Serialize(final).Length<=8192; record["statusSnapshot"] =StatusSummary(Map(Value(final,"status"))); record["completedUtc"] =utc; record["phase"] =Bool(final,"ok") ? "completed" :"failed"; SaveJob(target); }
        }
        private string ReportText(Dictionary<string, object> data)
        {
            string command =Text(data,"normalizedCommand"); StringBuilder text =new StringBuilder(); text.AppendLine("SPINDOCTOR — COMPLETE NATIVE COMMAND REPORT");
            text.AppendLine("Sequence: " +Text(data,"sequenceId") +"; command index: " +Text(data,"index")); text.AppendLine("Received UTC: " +Text(data,"receivedUtc")); text.AppendLine("Completed UTC: " +(Text(data,"completedUtc").Length!=0 ?Text(data,"completedUtc") :Text(data,"completionNote").Length!=0 ?"("+Text(data,"completionNote")+")" :"")); text.AppendLine("Original command: " +Text(data,"command")); text.AppendLine("Normalized command: " +command);
            text.AppendLine("Convention: q0 is the least-significant bit; chronological gates compose leftward; radians; classical FP64 coherent simulation where stated by the native backend.");
            bool measurement =command.StartsWith("q.circuit run ",StringComparison.OrdinalIgnoreCase) ||command.StartsWith("q.tranche measure ",StringComparison.OrdinalIgnoreCase) ||command.StartsWith("q.measure ",StringComparison.OrdinalIgnoreCase);
            if(command.StartsWith("q.circuit preset ",StringComparison.OrdinalIgnoreCase))
            {
                Dictionary<string,object> accepted=Map(Value(Map(Value(data,"response")),"result")),requestedJob=Map(Value(accepted,"job"));
                measurement=Bool(accepted,"queued") ||requestedJob.ContainsKey("requestedShots");
            }
            text.AppendLine(measurement ? "Measurement: requested classical Born sampling; requested/completed counts and partial work are in the native responses below." :"Measurement: not performed by this command.");
            text.AppendLine("Physical quantum hardware gates: 0. Represented family credits, actual classical queries/gates and samples are separate native counters.");
            int detailPosition=text.Length;
            text.AppendLine("COMPLETE ACCEPTED NATIVE RESPONSE:"); text.AppendLine(json.Serialize(Value(data,"response")));
            if(data.ContainsKey("finalResponse")) { Dictionary<string,object> final=Map(data["finalResponse"]);text.AppendLine(final.ContainsKey("nativeCompletionKnown") &&!Bool(final,"nativeCompletionKnown") ?"SEQUENCE ORCHESTRATION FAILURE; NATIVE COMPLETION IS UNKNOWN:":"COMPLETE MATCHED ASYNCHRONOUS COMPLETION RESPONSE:");text.AppendLine(json.Serialize(data["finalResponse"])); }
            const string footer="JSON companion is retained. Actual returned verification preview rows, when present, are preserved; no sample rows are synthesized by this report.";text.AppendLine(footer);string rawReport=text.ToString();CheckArchiveBytes(rawReport,"Native TXT");
            StringBuilder details=new StringBuilder();details.AppendLine("READABLE ACCEPTED RESPONSE DETAILS:");details.AppendLine(SpinDoctorReportDetails.Text(command,DetailResponse(data,Map(Value(data,"response")))));
            if(data.ContainsKey("finalResponse")){Dictionary<string,object> detailedFinal=Map(data["finalResponse"]);details.AppendLine(detailedFinal.ContainsKey("nativeCompletionKnown") &&!Bool(detailedFinal,"nativeCompletionKnown") ?"READABLE ORCHESTRATION FAILURE DETAILS — NATIVE COMPLETION UNKNOWN:":"READABLE MATCHED COMPLETION DETAILS:");details.AppendLine(SpinDoctorReportDetails.Text(command,DetailResponse(data,detailedFinal)));}
            string readable=details.ToString();if((long)Encoding.UTF8.GetByteCount(rawReport)+Encoding.UTF8.GetByteCount(readable)>MaximumReportBytes)
            {
                readable="Readable details omitted to preserve exact native returns within the 32 MiB UTF-8 TXT limit."+Environment.NewLine;
                // The optional footer supplies space even when the preexisting complete return fills its byte budget.
                if((long)Encoding.UTF8.GetByteCount(rawReport)+Encoding.UTF8.GetByteCount(readable)>MaximumReportBytes)rawReport=rawReport.Substring(0,rawReport.Length-footer.Length-Environment.NewLine.Length);
            }
            string report=rawReport.Insert(detailPosition,readable);CheckArchiveBytes(report,"Native TXT");return report;
        }
        private static Dictionary<string,object> DetailResponse(Dictionary<string,object> data,Dictionary<string,object> response)
        {
            Dictionary<string,object> detail=new Dictionary<string,object>(response),result=Map(Value(response,"result"));
            if(Text(data,"completedUtc").Length!=0 &&Bool(response,"ok") &&!Bool(result,"queued") &&!Bool(result,"active") &&!(response.ContainsKey("nativeCompletionKnown") &&!Bool(response,"nativeCompletionKnown")) &&!(result.ContainsKey("nativeCompletionKnown") &&!Bool(result,"nativeCompletionKnown")))
            {
                // Attach known orchestration metadata only to the human interpretation. Immutable native return bytes are unchanged.
                Dictionary<string,object> archive=Map(Value(response,"archive"));if(Text(response,"commandCompletedUtc").Length==0 &&Text(archive,"commandCompletedUtc").Length==0 &&Text(result,"completedUtc").Length==0){detail["commandCompletedUtc"]=Value(data,"completedUtc");detail["executionOutcome"]="completed";detail["completionScope"]="Recorded sequence command completion barrier observed at this UTC; this is execution evidence, not independent numerical verification.";}
            }
            return detail;
        }
        private static void CheckArchiveBytes(string value,string kind){if(Encoding.UTF8.GetByteCount(value)>MaximumReportBytes)throw new InvalidDataException(kind+" reporting archive exceeds the 32 MiB UTF-8 byte limit. This is an archive failure; no native calculation outcome is inferred.");}
        private void Install(Work next) { job =next; totalJobs++; recent.Remove(next.Id); recent.Insert(0,next.Id); if(recent.Count >20)recent.RemoveAt(recent.Count -1); }
        private void SaveJob(Work target)
        {
            Atomic(Path.Combine(jobsDirectory,target.Id +".json"),json.Serialize(JobObject(target)));
            // Carry child progress into the folder checkpoint so interruption cannot restore stale totals.
            if(target.Parent!=null &&Object.ReferenceEquals(job,target.Parent))Atomic(Path.Combine(jobsDirectory,target.Parent.Id+".json"),json.Serialize(JobObject(target.Parent)));
            Atomic(statePath,json.Serialize(D("schema","SPINDOCTOR/SEQUENCES/1","latestJobId",job ==null ? "" :job.Id,"recentJobIds",recent.ToArray(),"totalJobs",S(totalJobs),"executedCommands",S(totalExecuted))));
        }
        private Dictionary<string, object> JobObject(Work w)
        {
            Dictionary<string,object> result=JobObjectCore(w);
            if(w.Folder){result["skippedFiles"]=w.SkippedFiles ??new string[0];result["skippedFileCount"]=w.SkippedFileCount;}
            return result;
        }
        private Dictionary<string, object> JobObjectCore(Work w)
        {
            List<object> records=new List<object>();if(!w.Folder)foreach(Dictionary<string,object> record in w.Records)records.Add(new Dictionary<string,object>(record));
            List<object> files=new List<object>();foreach(Dictionary<string,object> file in w.Files)files.Add(new Dictionary<string,object>(file));
            int totalFiles=w.FilePaths==null ?w.Files.Count:w.FilePaths.Length,completed=w.Completed+(w.Folder &&w.CurrentChild!=null &&!w.CurrentChild.InputCountCommitted ?w.CurrentChild.Completed:0);
            return D("id",w.Id,"active",w.Active,"phase",w.Phase,"currentIndex",w.CurrentIndex,"totalCommands",w.Folder ?w.DiscoveredCommands:w.Commands==null ?0:w.Commands.Length,"discoveredCommandTotal",w.Folder ?w.DiscoveredCommands:w.Commands==null ?0:w.Commands.Length,"totalCommandsKnown",!w.Folder ||w.CompiledFiles==totalFiles,"compiledFiles",w.CompiledFiles,"completedCommands",completed,"commands",w.Folder ?new string[0]:w.Commands ??new string[0],"normalizedCommands",w.Folder ?new string[0]:w.Normalized ??new string[0],"presetId",w.PresetId,"parameters",w.Parameters ??new Dictionary<string,object>(),"parentJobId",w.Parent==null ?w.ParentJobId:w.Parent.Id,"sourcePath",w.SourcePath,"sourceFilename",w.SourceFilename,"sourceSha256",w.SourceSha256,"sourceFormat",w.SourceFormat,"verificationRequired",w.VerificationRequired,"verificationSamples",w.VerificationSamples,"verificationPhase",w.VerificationPhase,"verificationError",w.VerificationError,"receiptReady",w.ReceiptReady,"verification",w.Verification,"records",records.ToArray(),"receivedUtc",w.ReceivedUtc,"completedUtc",w.CompletedUtc,"elapsedMs",w.PreviousElapsed+w.Clock.Elapsed.TotalMilliseconds,"error",w.Error,"warnings",w.Warnings.ToArray(),"awaitedJobType",w.Folder &&w.CurrentChild!=null ?w.CurrentChild.AwaitedType:w.AwaitedType,"awaitedJobId",w.Folder &&w.CurrentChild!=null ?w.CurrentChild.AwaitedId:w.AwaitedId,"folder",w.Folder,"folderPath",w.FolderPath,"downloads",w.Downloads,"purge",w.Purge,"currentFile",w.CurrentFile,"totalFiles",totalFiles,"filePaths",w.FilePaths ??new string[0],"completedFiles",w.CompletedFiles,"files",files.ToArray(),"currentChildJobId",w.CurrentChild==null ?"":w.CurrentChild.Id,"currentFileJob",w.Folder &&w.CurrentChild!=null ?JobObject(w.CurrentChild):null,"currentFileCompletedCommands",w.CurrentChild==null ?0:w.CurrentChild.Completed,"currentFileTotalCommands",w.CurrentChild==null ?0:w.CurrentChild.Commands.Length,"sequenceReportFile",w.SequenceReportFile,"sequenceReportSha256",w.SequenceReportSha256,"sequenceReportBytes",w.SequenceReportBytes,"prunedPresetCircuits",w.Pruned ??new string[0]);
        }
        private Dictionary<string, object> ReadJob(string id)
        {
            lock(sync) { Alive(); if(String.IsNullOrEmpty(id)) { if(job ==null)throw new ArgumentException("No sequence exists."); id =job.Id; } CheckId(id); if(job!=null &&job.Id==id)return JobObject(job);if(job!=null &&job.CurrentChild!=null &&job.CurrentChild.Id==id)return JobObject(job.CurrentChild);Dictionary<string,object> saved=ReadCheckpoint(Path.Combine(jobsDirectory,id+".json"));if(Text(saved,"id")!=id)throw new InvalidDataException("Sequence checkpoint identity mismatch.");return saved; }
        }
        private void LoadState()
        {
            Dictionary<string,object> state=ReadCheckpoint(statePath);if(Text(state,"schema") !="SPINDOCTOR/SEQUENCES/1")throw new InvalidDataException("Invalid sequence checkpoint schema.");
            totalJobs =Count(Text(state,"totalJobs")); totalExecuted =Count(Text(state,"executedCommands")); foreach(string id in Strings(state["recentJobIds"])) { CheckId(id); if(recent.Count >=20 ||recent.Contains(id))throw new InvalidDataException("Invalid recent sequence IDs."); recent.Add(id); }
            string latest =Text(state,"latestJobId"); if(latest.Length ==0)return; CheckId(latest); Dictionary<string,object> saved=ReadCheckpoint(Path.Combine(jobsDirectory,latest+".json"));if(Text(saved,"id")!=latest)throw new InvalidDataException("Sequence checkpoint identity mismatch.");
            job =new Work { Id=latest, Active=false, Phase=Text(saved,"phase"), Commands=Strings(saved["commands"]), Normalized=Strings(saved["normalizedCommands"]), PresetId=Text(saved,"presetId"), Parameters=Map(Value(saved,"parameters")), CurrentIndex=Int(saved["currentIndex"]), Completed=Int(saved["completedCommands"]), Error=Text(saved,"error"), ReceivedUtc=Text(saved,"receivedUtc"), CompletedUtc=Text(saved,"completedUtc"), PreviousElapsed=Convert.ToDouble(saved["elapsedMs"],CultureInfo.InvariantCulture), AwaitedType=Text(saved,"awaitedJobType"), AwaitedId=Text(saved,"awaitedJobId") };
            job.Folder=Bool(saved,"folder");job.FolderPath=Text(saved,"folderPath");job.Downloads=Bool(saved,"downloads");job.Purge=Bool(saved,"purge");job.CurrentFile=Int(Value(saved,"currentFile"));job.CompletedFiles=Int(Value(saved,"completedFiles"));foreach(object value in Items(Value(saved,"files")))job.Files.Add(Map(value));
            job.VerificationRequired=Bool(saved,"verificationRequired");job.VerificationSamples=saved.ContainsKey("verificationSamples") ?SavedVerificationSamples(saved["verificationSamples"]):10240;job.VerificationPhase=saved.ContainsKey("verificationPhase") ?Text(saved,"verificationPhase"):"legacy-unverified";job.VerificationError=Text(saved,"verificationError");job.Verification=Map(Value(saved,"verification"));job.ReceiptReady=saved.ContainsKey("receiptReady") ?Bool(saved,"receiptReady"):Text(saved,"phase")=="completed";
            job.ParentJobId=Text(saved,"parentJobId");if(job.ParentJobId.Length!=0)CheckId(job.ParentJobId);job.SourcePath=Text(saved,"sourcePath");job.SourceFilename=Text(saved,"sourceFilename");job.SourceSha256=Text(saved,"sourceSha256");job.SourceFormat=Text(saved,"sourceFormat");
            job.FilePaths=Strings(Value(saved,"filePaths"));if(job.Folder &&job.FilePaths.Length==0){List<string> legacyPaths=new List<string>();foreach(Dictionary<string,object> file in job.Files)legacyPaths.Add(Text(file,"path"));while(legacyPaths.Count<Int(Value(saved,"totalFiles")) &&legacyPaths.Count<MaximumFolderFiles)legacyPaths.Add("");job.FilePaths=legacyPaths.ToArray();}
            job.SequenceReportFile=Text(saved,"sequenceReportFile");job.SequenceReportSha256=Text(saved,"sequenceReportSha256");job.SequenceReportBytes=Long(Value(saved,"sequenceReportBytes"));foreach(string warning in Strings(Value(saved,"warnings")))job.Warnings.Add(warning);
            if(job.Commands.Length >MaximumCommands ||job.Commands.Length !=job.Normalized.Length ||job.Completed <0 ||job.Completed >(job.Folder ?MaximumCommands*MaximumFolderFiles:job.Commands.Length) ||!QuantumMath.IsFinite(job.PreviousElapsed) ||job.PreviousElapsed <0 ||job.Files.Count>MaximumFolderFiles ||job.FilePaths.Length>MaximumFolderFiles ||job.CompletedFiles<0 ||job.CompletedFiles>job.Files.Count ||job.SequenceReportBytes<0 ||job.Warnings.Count>MaximumFolderFiles*2)throw new InvalidDataException("Invalid saved sequence counts.");
            HashSet<int> indices=new HashSet<int>();foreach(Dictionary<string,object> file in job.Files){int index=Int(Value(file,"index")),total=Int(Value(file,"totalCommands")),completed=Int(Value(file,"completedCommands"));if(index<0 ||index>=job.FilePaths.Length ||!indices.Add(index) ||total<0 ||total>MaximumCommands ||completed<0 ||completed>total)throw new InvalidDataException("Invalid saved folder file counts.");string childId=Text(file,"childJobId");if(childId.Length!=0)CheckId(childId);job.DiscoveredCommands=checked(job.DiscoveredCommands+total);if(total>0)job.CompiledFiles++;}
            if(job.Folder &&job.Completed>job.DiscoveredCommands)throw new InvalidDataException("Invalid aggregate folder command count.");
            indices.Clear();foreach(object value in Items(saved["records"])) { Dictionary<string,object> record=Map(value);int index=Int(record["index"]);if(index<0 ||index>=MaximumCommands ||!indices.Add(index) ||(job.Commands.Length!=0 &&index>=job.Commands.Length))throw new InvalidDataException("Invalid sequence command index.");if(!job.Folder &&Text(record,"sequenceId")!=job.Id)throw new InvalidDataException("Sequence command identity mismatch.");ValidateReport(record,"reportFile","reportSha256");if(record.ContainsKey("reportJsonSha256"))ValidateReport(record,"reportJsonFile","reportJsonSha256");if(record.ContainsKey("completedReportFile"))ValidateReport(record,"completedReportFile","completedReportSha256");if(record.ContainsKey("completedReportJsonSha256"))ValidateReport(record,"completedReportJsonFile","completedReportJsonSha256");if(!job.Folder)job.Records.Add(record); }
            // 3.0.4 folder checkpoints sometimes projected the current child's arrays under the parent ID.
            // Preserve child reports, but restore only aggregate parent identity and counts.
            if(job.Folder){job.Commands=new string[0];job.Normalized=new string[0];job.CurrentIndex=-1;}
            if(Bool(saved,"active"))
            {
                job.Phase="interrupted";job.Error="Process stopped before the sequence completion barrier. Completed native work remains in its own checkpoint; no automatic resume or fabricated completion is performed.";job.CompletedUtc=Utc();
                Dictionary<string,object> child=Map(Value(saved,"currentFileJob"));foreach(Dictionary<string,object> file in job.Files){string phase=Text(file,"executionPhase");if(phase.Length==0)phase=Text(file,"phase");if(phase=="executing" ||phase=="compiling"){file["phase"]="interrupted";file["executionPhase"]="interrupted";file["error"]=job.Error;if(Text(file,"childJobId")==Text(child,"id") &&child.Count!=0)file["completedCommands"]=Int(Value(child,"completedCommands"));}if(Bool(file,"verificationRequired") &&Text(file,"verificationPhase")!="passed"){file["verificationPhase"]="interrupted";file["verificationError"]=job.Error;file["receiptReady"]=false;}}
                job.ReceiptReady=false;if(job.VerificationRequired &&job.VerificationPhase!="passed")job.VerificationPhase="interrupted";
                foreach(Dictionary<string,object> file in job.Files)if(Bool(file,"verificationRequired") &&Text(file,"verificationPhase")=="interrupted" &&Text(file,"childJobId").Length!=0){string childPath=Path.Combine(jobsDirectory,Text(file,"childJobId")+".json");Dictionary<string,object> savedChild=ReadCheckpoint(childPath);if(Text(savedChild,"id")!=Text(file,"childJobId"))throw new InvalidDataException("Interrupted child identity mismatch.");savedChild["verificationPhase"]="interrupted";savedChild["verificationError"]=job.Error;savedChild["receiptReady"]=false;Atomic(childPath,json.Serialize(savedChild));}
                SaveJob(job);
            }
        }
        private Dictionary<string,object> ReadCheckpoint(string path)
        {
            using(FileStream source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(source.Length>MaximumReportBytes)throw new InvalidDataException("Sequence checkpoint exceeds 32 MiB.");using(StreamReader reader=new StreamReader(source,new UTF8Encoding(false,true),true)){Dictionary<string,object> saved=json.DeserializeObject(reader.ReadToEnd()) as Dictionary<string,object>;if(saved==null)throw new InvalidDataException("Sequence checkpoint must be an object.");return saved;}}
        }
        private void ValidateReport(Dictionary<string,object> record,string fileKey,string hashKey) { string file=Text(record,fileKey);if(file.Length==0)return;using(FileStream source=new FileStream(ReportPath(file),FileMode.Open,FileAccess.Read,FileShare.Read)){if(source.Length>MaximumReportBytes)throw new InvalidDataException("Command report exceeds 32 MiB.");if(HashStream(source)!=Text(record,hashKey))throw new InvalidDataException("Sequence report hash mismatch.");} }
        private string ReadReport(Dictionary<string, object> record, string stage, string extension)
        {
            string file=ReportFile(record,stage,extension),value;using(FileStream source=new FileStream(ReportPath(file),FileMode.Open,FileAccess.Read,FileShare.Read)){if(source.Length>MaximumReportBytes)throw new InvalidDataException("Command report exceeds 32 MiB.");using(StreamReader reader=new StreamReader(source,new UTF8Encoding(false,true),true)){value=reader.ReadToEnd();}}
            bool complete = file == Text(record, extension == ".txt" ? "completedReportFile" : "completedReportJsonFile");
            string hashKey = complete ? (extension == ".txt" ? "completedReportSha256" : "completedReportJsonSha256") : (extension == ".txt" ? "reportSha256" : "reportJsonSha256");
            if (record.ContainsKey(hashKey) && Hash(value) != Text(record, hashKey)) throw new InvalidDataException("Sequence report hash mismatch; the archived return was altered.");
            return value;
        }
        private void LoadManifest(string path)
        {
            if(new FileInfo(path).Length >1048576)throw new InvalidDataException("Preset manifest exceeds 1 MiB."); string source=File.ReadAllText(path,Encoding.UTF8); manifestHash=Hash(source); Dictionary<string,object> data=Map(json.DeserializeObject(source));
            if(Text(data,"schemaVersion") !="1.0")throw new InvalidDataException("Preset manifest schema mismatch."); HashSet<string> ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(object item in Items(data["presets"])) { Dictionary<string,object> card=Map(item); string id=Text(card,"id"); if(!Regex.IsMatch(id,"^qc-[0-9]{3}$") ||!ids.Add(id))throw new InvalidDataException("Invalid or duplicate preset ID."); string[] commands=Strings(card["commands"]); if(commands.Length <1 ||commands.Length >MaximumCommands)throw new InvalidDataException("Invalid preset command count."); foreach(string command in commands)CommandText(command); presets.Add(card); }
            if(presets.Count !=241)throw new InvalidDataException("Expected the preserved 241-entry manifest.");
        }
        private Dictionary<string,object> FindPresetCard(string id) { foreach(object item in Items(Map(Presets())["presets"])) { Dictionary<string,object> card=Map(item); if(Text(card,"id") ==id)return card; } throw new ArgumentException("Unknown preset ID."); }
        private static Dictionary<string,object> Support(string command,Func<string,object> resolver)
        {
            try { CommandText(command); string[] words=Words(command); string first=words[0].ToLowerInvariant(); if(first=="q.sequence" ||first=="q.preset" ||first=="q.folder" ||first=="q.input" &&words.Length>1 &&Eq(words[1],"run"))return D("available",false,"reason","Recursive sequence/folder/input-run commands are not valid sequence steps; preset run must be expanded before dispatch.");
                if(IsLegacy(first)) { ValidateLegacy(words); return D("available",true,"conditional",first=="q.tranche" ||(first=="q.verify" &&words.Length >1 &&Eq(words[1],"samples")),"reason",first=="q.tranche" ? "Requires the appropriate selected register/context and memory budget." :first=="q.verify" ? "GPU comparison availability is reported by the native checker." :"", "backend",first=="q.circuit" ||first=="q.tranche" ? "coherent-factor-blocks" :"procedural-independent-states"); }
                if(resolver !=null) { object value=resolver(command); Dictionary<string,object> result=value as Dictionary<string,object>; if(result !=null)return result; if(value is bool)return D("available",(bool)value,"reason",(bool)value ? "" :"Native calculator handler is unavailable."); }
                return D("available",false,"reason","This proposed command needs an installed and validated native handler.");
            } catch(Exception error) { return D("available",false,"reason",error.Message); }
        }
        private static bool IsLegacy(string first) { return first=="q.circuit" ||first=="q.tranche" ||first=="q.prepare" ||first=="q.migrate" ||first=="q.gate" ||first=="q.state" ||first=="q.verify" ||first=="q.measure" ||first=="q.status" ||first=="q.export" ||first=="q.reset" ||first=="q.help"; }
        private static void ValidateLegacy(string[] w)
        {
            string first=w[0].ToLowerInvariant();
            if(first=="q.circuit")
            {
                if(w.Length <2)throw new ArgumentException("Missing circuit action."); string action=w[1].ToLowerInvariant();
                if(action=="new") { Exact(w,4); Name(w[2]); Number(w[3],1,TrancheRegister.MaximumLogicalQubits); return; }
                if(action=="add") { Gate(w,2,TrancheRegister.MaximumLogicalQubits); return; }
                if(action=="preset") { CircuitTerminal.ParsePresetOptions(w);return; }
                if(action=="run") { Exact(w,6); Need(w[2],"shots");Number(w[3],1,100000);Need(w[4],"basis");Basis(w[5]);return; }
                if(action=="state" ||action=="input") { Exact(w,3);Count(w[2]);return; }
                if(action=="spin") { Exact(w,3);Number(w[2],0,TrancheRegister.MaximumLogicalQubits-1);return; }
                if(action=="select") { Exact(w,3);Name(w[2]);return; }
                if(action=="memory") { Exact(w,3);if(!Eq(w[2],"auto") &&Unsigned(w[2]) <2)throw new ArgumentException("Circuit memory minimum is 2 MiB.");return; }
                if(action=="delete") { if(w.Length !=2 &&w.Length !=3)throw new ArgumentException("Invalid delete arguments."); if(w.Length==3)Name(w[2]);return; }
                if(action=="clear" ||action=="show" ||action=="list" ||action=="status" ||action=="result" ||action=="cancel" ||action=="export" ||action=="help") { Exact(w,2);return; }
                throw new ArgumentException("Unknown circuit action.");
            }
            if(first=="q.tranche")
            {
                if(w.Length<2)throw new ArgumentException("Missing tranche action."); string action=w[1].ToLowerInvariant();
                if(action=="prepare") {Exact(w,4);Number(w[2],1,TrancheRegister.MaximumLogicalQubits);Need(w[3],"zero");return;}
                if(action=="from") {Exact(w,3);Unsigned(w[2]);return;}
                if(action=="gate") {Gate(w,2,TrancheRegister.MaximumLogicalQubits);return;}
                if(action=="state") {Exact(w,3);Count(w[2]);return;}
                if(action=="spin") {Exact(w,3);Number(w[2],0,TrancheRegister.MaximumLogicalQubits-1);return;}
                if(action=="measure") {if(w.Length!=7 &&w.Length!=8)throw new ArgumentException("Invalid tranche measure arguments.");Number(w[2],0,TrancheRegister.MaximumLogicalQubits-1);Need(w[3],"basis");Basis(w[4]);Need(w[5],"shots");int n=Number(w[6],1,100000);if(w.Length==8){Need(w[7],"collapse");if(n!=1)throw new ArgumentException("Collapse requires one shot.");}return;}
                if(action=="memory") {Exact(w,3);if(!Eq(w[2],"auto"))Unsigned(w[2]);return;}
                if(action=="save") {if(w.Length!=2 &&w.Length!=3)throw new ArgumentException("Invalid tranche save arguments.");if(w.Length==3)Need(w[2],"dense");return;}
                if(action=="status" ||action=="cancel" ||action=="export" ||action=="resume" ||action=="reset" ||action=="release" ||action=="help") {Exact(w,2);return;}
                throw new ArgumentException("Unknown tranche action.");
            }
            if(first=="q.prepare") {Exact(w,4);Need(w[1],"grid");QuantumMath.ValidateGrid(Unsigned(w[2]),Unsigned(w[3]));return;}
            if(first=="q.migrate") {Exact(w,3);Need(w[1],"t");QuantumMath.ValidateMigration(Real(w[2]));return;}
            if(first=="q.gate") { string name=w.Length>1 ? w[1].ToLowerInvariant() :"";int ac=name=="u" ||name=="u3" ?3 :name=="ry" ||name=="rz" ?1 :0;Exact(w,3+ac);Need(w[w.Length-1],"all");double[] angles=new double[ac];for(int i=0;i<ac;i++)angles[i]=Real(w[2+i]);QuantumMath.MatrixForGate(name=="u3" ? "u" :name,angles);return;}
            if(first=="q.state") {Exact(w,2);Unsigned(w[1]);return;}
            if(first=="q.verify") {if(w.Length==2 &&(Eq(w[1],"rule") ||Eq(w[1],"status") ||Eq(w[1],"cancel")))return;if(w.Length==4 &&Eq(w[1],"samples") &&Eq(w[3],"errors-only")){if(Count(w[2]) <=0)throw new ArgumentException("Sample count must be positive.");return;}throw new ArgumentException("Invalid verification command.");}
            if(first=="q.measure") {Exact(w,6);Unsigned(w[1]);Need(w[2],"basis");Basis(w[3]);Need(w[4],"shots");Number(w[5],1,100000);return;}
            if(first=="q.export") {Exact(w,2);Need(w[1],"descriptor");return;}
            Exact(w,1);
        }
        private static void ValidateContext(string[] commands,Dictionary<string,object> status)
        {
            Dictionary<string,object> circuit=Map(Value(status,"circuit")),current=Map(Value(circuit,"program"));int n=current.Count==0 ?0:Int(Value(current,"qubits"));string selected=Text(circuit,"selected");bool output=Bool(circuit,"coherentOutputAvailable");Dictionary<string,int> programs=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);foreach(object item in Items(Value(circuit,"programSummaries"))){Dictionary<string,object> p=Map(item);programs[Text(p,"name")]=Int(Value(p,"qubits"));}if(n>0)programs[selected]=n;
            Dictionary<string,object> ts=Map(Value(status,"tranche"));int tranche=Int(Value(ts,"qubits"));Dictionary<string,object> register=Map(Value(ts,"register"));if(tranche==0)tranche=Int(Value(register,"qubits"));ulong m=UnsignedOrZero(Value(status,"gridM")),l=UnsignedOrZero(Value(status,"gridL"));if(m==0)m=UnsignedOrZero(Value(Map(Value(status,"grid")),"m"));if(l==0)l=UnsignedOrZero(Value(Map(Value(status,"grid")),"l"));ulong domain=m>0 &&l>0 &&m<=UInt64.MaxValue/l ?m*l:UnsignedOrZero(Value(status,"stateCount"));
            foreach(string command in commands)
            {
                string[] w=Words(command);
                if(Eq(w[0],"q.prepare")){domain=Unsigned(w[2])*Unsigned(w[3]);}
                else if(Eq(w[0],"q.reset")){domain=20000000UL*25000000UL;}
                else if(Eq(w[0],"q.state") ||Eq(w[0],"q.measure")){if(domain!=0 &&Unsigned(w[1])>=domain)throw new ArgumentException("Family address lies outside its declared domain.");}
                else if(Eq(w[0],"q.circuit"))
                {
                    string action=w[1].ToLowerInvariant();
                    if(action=="new"){n=Number(w[3],1,TrancheRegister.MaximumLogicalQubits);selected=w[2];programs[selected]=n;output=false;}
                    else if(action=="preset"){var options=CircuitTerminal.ParsePresetOptions(w);n=options.Qubits;selected=options.Name ??(options.Preset=="bell" ?"bell":options.Preset+"-"+options.Qubits.ToString(CultureInfo.InvariantCulture));programs[selected]=n;output=options.Run;}
                    else if(action=="select"){if(!programs.TryGetValue(w[2],out n))throw new ArgumentException("Circuit selection does not exist in this sequence context.");selected=w[2];output=false;}
                    else if(action=="delete"){string name=w.Length==3 ?w[2]:selected;if(!programs.Remove(name))throw new ArgumentException("Circuit deletion target does not exist.");if(Eq(name,selected)){selected="";n=0;output=false;}}
                    else if(action=="add"){if(n==0)throw new ArgumentException("No selected circuit for a gate.");Gate(w,2,n);output=false;}
                    else if(action=="input" ||action=="state"){if(n==0 ||Count(w[2]) >=(BigInteger.One<<n))throw new ArgumentException("Circuit basis index is outside the declared register.");if(action=="input")output=false;else if(!output)throw new ArgumentException("Run the current circuit before its state query.");}
                    else if(action=="spin"){if(n==0 ||!output)throw new ArgumentException("Run the current circuit before its spin query.");Number(w[2],0,n-1);}
                    else if(action=="run"){if(n==0)throw new ArgumentException("No selected circuit to run.");output=true;}
                    else if(action=="clear" ||action=="show" ||action=="export"){if(n==0)throw new ArgumentException("No selected circuit.");if(action=="clear")output=false;}
                }
                else if(Eq(w[0],"q.query"))
                {
                    if(n==0 ||!output)throw new ArgumentException("Run the selected circuit before its coherent query.");Dictionary<string,string> f=SpinDoctorCalculators.ParseFields(w);string action=w[1].ToLowerInvariant();string list=f.ContainsKey("qubits") ?f["qubits"]:f.ContainsKey("partition") ?f["partition"]:null;int[] selectedQubits=list==null ?new int[0]:SpinDoctorCalculators.Qubits(list,n,4);
                    if(action=="concurrence"&&(n!=2||selectedQubits.Length!=2))throw new ArgumentException("Concurrence requires the complete pure two-qubit circuit.");if(action=="negativity"&&selectedQubits.Length==n)throw new ArgumentException("Negativity needs a nonempty complement.");
                    bool full=action=="expectation"||action=="stabilizers"||action=="fidelity"||selectedQubits.Length>1;if(full&&n>16)throw new ArgumentException("This full joint query is bounded to 16 qubits; use a single-qubit reduction or bounded prefix.");
                    if(f.ContainsKey("start")&&Count(f["start"])>=(BigInteger.One<<n))throw new ArgumentException("Query prefix starts outside the selected register.");if(action=="fidelity"&&SpinDoctorCalculators.ParseComplexVector(f["state"]).Length!=(1<<n))throw new ArgumentException("Fidelity target length differs from the selected circuit.");if(f.ContainsKey("pauli"))SpinDoctorCalculators.ParsePauli(f["pauli"],n);if(f.ContainsKey("generators"))foreach(string generator in SpinDoctorCalculators.SplitList(f["generators"]))SpinDoctorCalculators.ParsePauli(SpinDoctorCalculators.Unquote(generator),n);
                }
                else if(Eq(w[0],"q.tranche"))
                {
                    string action=w[1].ToLowerInvariant();if(action=="prepare")tranche=Number(w[2],1,TrancheRegister.MaximumLogicalQubits);else if(action=="from"){ulong index=Unsigned(w[2]);if(domain!=0 &&index>=(domain+28)/29)throw new ArgumentException("Tranche index is outside the declared family.");ulong start=checked(index*29);tranche=domain==0 ?29:(int)Math.Min(29UL,domain-start);}else if(action=="release")tranche=0;else if(action=="gate"){if(tranche==0)throw new ArgumentException("No prepared tranche for a gate.");Gate(w,2,tranche);}else if(action=="state"){if(tranche==0 ||Count(w[2]) >=(BigInteger.One<<tranche))throw new ArgumentException("Tranche basis index is outside the declared register.");}else if(action=="spin" ||action=="measure"){if(tranche==0)throw new ArgumentException("No prepared tranche.");Number(w[2],0,tranche-1);}
                }
            }
        }
        private Dictionary<string,object> AutoPrune(string[] commands,Dictionary<string,object> status,Work next)
        {
            // 3.0.3: a preset run that needs circuit slots first removes the oldest circuits created by
            // earlier preset runs. User circuits are never touched; the removal is an ordinary,
            // reported q.circuit prune command and the pruned names are recorded on the sequence.
            Dictionary<string,object> circuit=Map(Value(status,"circuit"));int capacity=Int(Value(circuit,"programCapacity"));if(capacity<=0)return status;
            int existing=Int(Value(circuit,"programCount")),owned=Int(Value(circuit,"presetOwnedCount")),added=0;
            foreach(string c in commands){string[] w=Words(c);if(w.Length>1&&Eq(w[0],"q.circuit")&&(Eq(w[1],"new")||Eq(w[1],"preset")))added++;}
            int deficit=existing+added-capacity;if(deficit<=0||owned<deficit)return status;
            Dictionary<string,object> response=Map(execute("q.circuit prune presets "+(owned-deficit).ToString(CultureInfo.InvariantCulture)));
            if(!Bool(response,"ok"))throw new ArgumentException("Automatic preset-circuit pruning failed: "+Text(response,"error"));
            List<string> names=new List<string>();foreach(object o in Items(Value(Map(Value(response,"result")),"pruned")))names.Add(Convert.ToString(o,CultureInfo.InvariantCulture));next.Pruned=names.ToArray();
            return Map(getStatus());
        }
        private void MarkOwnership(Work target,string command)
        {
            string preset=target.PresetId;if((preset==null||preset.Length==0)&&target.Parent!=null)preset=target.Parent.PresetId;if(preset==null||preset.Length==0)return;
            string[] w=Words(command);string name=null;
            if(w.Length>=4&&Eq(w[0],"q.circuit")&&Eq(w[1],"new"))name=w[2];else if(w.Length>=5&&Eq(w[0],"q.circuit")&&Eq(w[1],"preset")&&Eq(w[w.Length-2],"as"))name=w[w.Length-1];
            if(name==null)return;Action<string,string> callback;lock(sync){callback=ownership;}if(callback!=null)callback(name,preset);
        }
        private static ulong UnsignedOrZero(object value){ulong n;return UInt64.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.None,CultureInfo.InvariantCulture,out n) ?n:0;}
        private static string[] Normalize(string[] commands,Dictionary<string,object> parameters,Dictionary<string,object> status,bool allowPresetPrune=false)
        {
            Dictionary<string,object> schema=ParameterSchema(commands);foreach(string key in parameters.Keys)if(!schema.ContainsKey(key))throw new ArgumentException("Unknown/inapplicable preset parameter: " +key);
            HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(object name in Items(Value(Map(Value(status,"circuit")),"programNames")))names.Add(Convert.ToString(name,CultureInfo.InvariantCulture));Dictionary<string,string> mapping=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);string[] result=new string[commands.Length];int added=0;
            for(int i=0;i<commands.Length;i++)
            {
                CommandText(commands[i]);string[] w=Words(commands[i]);bool legacy=IsLegacy(w[0].ToLowerInvariant());if(!legacy){result[i]=commands[i].Trim();continue;}
                if(Eq(w[0],"q.circuit") &&Eq(w[1],"preset"))
                {
                    var options=CircuitTerminal.ParsePresetOptions(w);string original=options.Name ??(options.Preset=="bell" ?"bell":options.Preset+"-"+options.Qubits.ToString(CultureInfo.InvariantCulture));string fresh=Fresh(original,names);mapping[original]=fresh;
                    List<string> canonical=new List<string>(new string[]{"q.circuit","preset",options.Preset,options.Qubits.ToString(CultureInfo.InvariantCulture)});
                    if(options.Run){canonical.Add("shots");canonical.Add(options.Shots.ToString(CultureInfo.InvariantCulture));canonical.Add("basis");canonical.Add(options.Basis);}
                    else canonical.Add("run=false");
                    canonical.Add("as");canonical.Add(fresh);w=canonical.ToArray();added++;
                }
                int shotOperand=ShotOperandIndex(w);if(parameters.ContainsKey("shots") &&shotOperand>=0)w[shotOperand+1]=Number(Convert.ToString(parameters["shots"],CultureInfo.InvariantCulture),1,Int(Value(Map(Value(schema,"shots")),"maximum"))).ToString(CultureInfo.InvariantCulture);
                if(parameters.ContainsKey("basis"))for(int p=0;p<w.Length-1;p++)if(Eq(w[p],"basis"))w[p+1]=Basis(Convert.ToString(parameters["basis"],CultureInfo.InvariantCulture));
                if(Eq(w[0],"q.circuit") &&Eq(w[1],"new")) {if(parameters.ContainsKey("qubits"))w[3]=Number(Convert.ToString(parameters["qubits"],CultureInfo.InvariantCulture),1,TrancheRegister.MaximumLogicalQubits).ToString(CultureInfo.InvariantCulture);string original=Name(w[2]),fresh=Fresh(original,names);mapping[original]=fresh;w[2]=fresh;added++;}
                
                if(Eq(w[0],"q.circuit") &&Eq(w[1],"select")){string mapped;if(mapping.TryGetValue(w[2],out mapped))w[2]=mapped;}
                if(parameters.ContainsKey("input") &&Eq(w[0],"q.circuit") &&Eq(w[1],"input"))w[2]=Count(Convert.ToString(parameters["input"],CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                int gateStart=Eq(w[0],"q.circuit") ||Eq(w[0],"q.tranche") ?w.Length>1 &&(Eq(w[1],"add") ||Eq(w[1],"gate")) ?2 :-1 :Eq(w[0],"q.gate") ?1 :-1;
                if(gateStart>=0){int ac=GateAngles(w[gateStart]);for(int a=0;a<ac;a++){string expression=parameters.ContainsKey("angle") ?Convert.ToString(parameters["angle"],CultureInfo.InvariantCulture) :w[gateStart+1+a];w[gateStart+1+a]=Real(expression).ToString("R",CultureInfo.InvariantCulture);}}
                result[i]=String.Join(" ",w);
            }
            Dictionary<string,object> circuitStatus=Map(Value(status,"circuit"));int capacity=Int(Value(circuitStatus,"programCapacity"));if(capacity<=0)capacity=64;int existing=Int(Value(circuitStatus,"programCount")),owned=Int(Value(circuitStatus,"presetOwnedCount"));bool plannedPrune=allowPresetPrune&&added<=capacity&&existing+added-capacity<=owned;if(existing+added>capacity&&!plannedPrune)throw new ArgumentException("Sequence needs "+added.ToString(CultureInfo.InvariantCulture)+" new circuit program(s) but only "+Math.Max(0,capacity-existing).ToString(CultureInfo.InvariantCulture)+" of "+capacity.ToString(CultureInfo.InvariantCulture)+" circuit slots are free ("+owned.ToString(CultureInfo.InvariantCulture)+" preset-owned). Free slots with q.circuit prune presets or q.circuit delete NAME, or raise the limit with q.circuit capacity N (maximum 1024).");return result;
        }
        private static Dictionary<string,object> ParameterSchema(string[] commands)
        {
            Dictionary<string,object> result=new Dictionary<string,object>();int angles=0;
            foreach(string command in commands)
            {
                string[] w=Words(command);int shotOperand=ShotOperandIndex(w);
                bool automaticPreset=w.Length>2 &&Eq(w[0],"q.circuit") &&Eq(w[1],"preset") &&CircuitTerminal.ParsePresetOptions(w).Run;
                if(shotOperand>=0 ||automaticPreset)
                {
                    bool collapse=Eq(w[0],"q.tranche") &&w.Length==8 &&Eq(w[7],"collapse");int maximum=collapse ?1:100000;
                    int nativeDefault=automaticPreset ?CircuitTerminal.ParsePresetOptions(w).Shots:Number(w[shotOperand+1],1,maximum);Dictionary<string,object> shots=Map(Value(result,"shots"));
                    if(shots.Count==0)shots=D("type","integer","minimum",1,"maximum",maximum,"default",nativeDefault);
                    else {shots["maximum"]=Math.Min(Int(Value(shots,"maximum")),maximum);if(!shots.ContainsKey("default") ||Int(Value(shots,"default"))!=nativeDefault)shots.Remove("default");}
                    bool singleCollapse=Int(Value(shots,"maximum"))==1;shots["singleCollapse"]=singleCollapse;
                    shots["description"]=singleCollapse ?"A collapsing measurement changes the selected state and requires exactly one shot.":"Independent sampling shots. The preset's recorded command supplies its default; editable range 1..100000.";
                    result["shots"]=shots;
                }
                if(automaticPreset)result["basis"]=D("type","choice","values",new string[]{"x","y","z"});
                for(int i=0;i<w.Length;i++)if(Eq(w[i],"basis"))result["basis"]=D("type","choice","values",new string[]{"x","y","z"});
                if(w.Length>1 &&Eq(w[0],"q.circuit") &&Eq(w[1],"new"))result["qubits"]=D("type","integer","minimum",1,"maximum",TrancheRegister.MaximumLogicalQubits);
                if(w.Length>1 &&Eq(w[0],"q.circuit") &&Eq(w[1],"input"))result["input"]=D("type","basis-index","ordering","little-endian");
                int at=(Eq(w[0],"q.circuit") ||Eq(w[0],"q.tranche")) &&w.Length>2 &&(Eq(w[1],"add") ||Eq(w[1],"gate")) ?2 :Eq(w[0],"q.gate") &&w.Length>1 ?1 :-1;if(at>=0)angles+=GateAngles(w[at]);
            }
            if(angles==1)result["angle"]=D("type","radians","expression","Restricted native arithmetic; finite magnitude<=1e12.");return result;
        }
        private static int ShotOperandIndex(string[] words)
        {
            int at=words.Length>1 &&Eq(words[0],"q.circuit") &&Eq(words[1],"run") ?2:words.Length>1 &&Eq(words[0],"q.tranche") &&Eq(words[1],"measure") ?5:words.Length>0 &&Eq(words[0],"q.measure") ?4:-1;
            if(words.Length>2 &&Eq(words[0],"q.circuit") &&Eq(words[1],"preset") &&CircuitTerminal.ParsePresetOptions(words).Run)
                for(int i=3;i<words.Length-1;i++)if(Eq(words[i],"shots"))return i;
            return at>=0 &&words.Length>at+1 &&Eq(words[at],"shots") ?at:-1;
        }
        private static string Fresh(string original,HashSet<string> names) {Name(original);string fresh=original;while(names.Contains(fresh))fresh=(original.Length>23 ?original.Substring(0,23) :original)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);names.Add(fresh);return fresh;}
        private static void Gate(string[] w,int at,int n){string name=Alias(w[at]);Dictionary<string,object> spec=GateSpec(name);int ac=(int)spec["angleCount"],qc=(int)spec["targetCount"];bool all=w.Length==at+ac+2 &&Eq(w[w.Length-1],"all");if(all &&!(bool)spec["local"])throw new ArgumentException("all accepts local gates only.");if(w.Length!=at+1+ac+(all ?1 :qc))throw new ArgumentException("Incorrect gate operand count.");for(int i=0;i<ac;i++)Real(w[at+1+i]);HashSet<int> distinct=new HashSet<int>();if(!all)for(int i=0;i<qc;i++)if(!distinct.Add(Number(w[at+1+ac+i],0,n-1)))throw new ArgumentException("Gate qubits must be distinct.");}
        private static int GateAngles(string name){return (int)GateSpec(Alias(name))["angleCount"];}
        private static Dictionary<string,object> GateSpec(string name){foreach(Dictionary<string,object> spec in TrancheRegister.SupportedGates)if((string)spec["name"]==name)return spec;throw new ArgumentException("Unsupported gate: " +name);}
        private static string Alias(string value){value=value.ToLowerInvariant();return value=="cnot" ?"cx" :value=="toffoli" ?"ccx" :value=="fredkin" ?"cswap" :value=="id" ||value=="identity" ?"i" :value=="u3" ?"u" :value;}
        private static string Scope(string[] commands){foreach(string c in commands)if(c.StartsWith("q.circuit",StringComparison.OrdinalIgnoreCase)||c.StartsWith("q.tranche",StringComparison.OrdinalIgnoreCase)||c.StartsWith("q.query",StringComparison.OrdinalIgnoreCase))return "Selected coherent register/circuit; no whole-family entanglement.";return "Native declared command scope; represented family applications remain separate from actual arithmetic.";}
        private static Dictionary<string,object> PreviewResponse(Dictionary<string,object> response)
        {
            JavaScriptSerializer encoder=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};if(encoder.Serialize(response).Length<=8192)return response;
            object nativeResult=Value(response,"result");bool resultComplete=encoder.Serialize(nativeResult).Length<=4096;Dictionary<string,object> result=Map(nativeResult);
            object returnedResult=resultComplete ?nativeResult:D("previewOnly",true,"fields",new List<string>(result.Keys).ToArray(),"id",Value(result,"id"),"phase",Value(result,"phase"),"queued",Value(result,"queued"),"completedShots",Value(result,"completedShots"),"completedSamples",Value(result,"completedSamples"),"scope","This large result is summarized; the unchanged complete native response remains in its TXT/JSON archive.");
            Dictionary<string,object> status=Map(Value(response,"status")),summary=StatusSummary(status),boundedStatus=new Dictionary<string,object>();List<string> omitted=new List<string>();
            foreach(KeyValuePair<string,object> field in summary){Dictionary<string,object> candidate=new Dictionary<string,object>(boundedStatus);candidate[field.Key]=field.Value;if(encoder.Serialize(candidate).Length<=2048)boundedStatus[field.Key]=field.Value;else omitted.Add(field.Key);}
            Dictionary<string,object> preview=D("ok",Value(response,"ok"),"command",Value(response,"command"),"error",Value(response,"error"),"elapsedMs",Value(response,"elapsedMs"),"result",returnedResult,"resultComplete",resultComplete,"resultTruncated",!resultComplete,"status",boundedStatus,"statusTruncated",true,"statusOmittedFields",omitted.ToArray(),"responsePreviewOnly",true,"fullNativeResponseArchived",true);
            // The numerical answer has its own budget. Large status catalogs must never erase a small result.
            return preview;
        }
        private static Dictionary<string,object> StatusSummary(Dictionary<string,object> status){Dictionary<string,object> summary=new Dictionary<string,object>();foreach(string key in new string[]{"productName","descriptorHash","stateCount","representedPreparations","representedGateApplications","actualCpuStates","actualGpuStates","verifiedSamples","measurementShots","measurementCounter","measurementSeed","fieldSeed","errorCount","quantumHardwareGates","gpuDevice","gpuAvailable"})if(status.ContainsKey(key))summary[key]=status[key];foreach(string key in new string[]{"circuit","tranche"}){Dictionary<string,object> native=Map(Value(status,key)),sub=new Dictionary<string,object>();foreach(string field in new string[]{"selected","qubits","counter","measurementSeed","programCount","committedGateApplications","actualPreparedSingleQubits","lifetime"})if(native.ContainsKey(field))sub[field]=native[field];Dictionary<string,object> nativeJob=Map(Value(native,"job"));if(nativeJob.Count!=0)sub["job"]=D("id",Value(nativeJob,"id"),"active",Value(nativeJob,"active"),"phase",Value(nativeJob,"phase"),"completedShots",Value(nativeJob,"completedShots"),"counts",Value(nativeJob,"counts"));summary[key]=sub;}return summary;}
        private static Dictionary<string,object> NewRecord(Work target,int index,string original,string normalized){return D("sequenceId",target.Id,"index",index,"command",original,"normalizedCommand",normalized,"phase","executing","receivedUtc",Utc(),"completedUtc","","response",null,"finalResponse",null,"statusSnapshot",null,"jobId","","jobType","","reportId",target.Id+"-"+index.ToString("000",CultureInfo.InvariantCulture),"responseComplete",false,"finalResponseComplete",false,"printoutUrl","/api/sequences/"+target.Id+"/commands/"+index+"/printout","jsonUrl","/api/sequences/"+target.Id+"/commands/"+index+"/json");}
        private static Dictionary<string,object> NativeJob(Dictionary<string,object> status,string type){return type=="verification" ?Map(Value(status,"lastVerificationJob")) :Map(Value(Map(Value(status,type)),"job"));}
        private static string JobType(string command){return command.StartsWith("q.circuit ",StringComparison.OrdinalIgnoreCase) ?"circuit" :command.StartsWith("q.tranche ",StringComparison.OrdinalIgnoreCase) ?"tranche" :command.StartsWith("q.verify ",StringComparison.OrdinalIgnoreCase) ?"verification" :"";}
        private static string CancelCommand(string type){return type=="verification" ?"q.verify cancel" :"q."+type+" cancel";}
        private static string ReportFile(Dictionary<string,object> record,string stage,string extension){if(stage!="accepted" &&stage!="completed" &&stage!="latest")throw new ArgumentException("Report stage is accepted, completed or latest.");bool complete=stage=="completed" ||stage=="latest" &&record.ContainsKey("completedReportFile");string key=complete ?extension==".txt" ?"completedReportFile" :"completedReportJsonFile" :extension==".txt" ?"reportFile" :"reportJsonFile";string file=Text(record,key);if(file.Length==0 ||Path.GetFileName(file)!=file)throw new ArgumentException("Requested report stage is not available.");return file;}
        private static void CommandText(string command){if(String.IsNullOrWhiteSpace(command) ||command.Length>MaximumCommandCharacters)throw new ArgumentException("Each command is 1..4096 characters.");bool quoted=false,escaped=false;foreach(char c in command){if(escaped){escaped=false;continue;}if(c=='\\' &&quoted){escaped=true;continue;}if(c=='\"'){quoted=!quoted;continue;}if((c==';' ||c=='\r' ||c=='\n') &&!quoted)throw new ArgumentException("Submit ordinary commands as an array; unquoted macro separators are unsupported.");if(c=='\0')throw new ArgumentException("NUL is not valid command data.");}if(quoted)throw new ArgumentException("Unterminated command quote.");if(!command.TrimStart().StartsWith("q.",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Only native q.* commands are accepted.");}
        private static string[] Words(string command){return command.Trim().Split(new char[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);}
        private static Dictionary<string,string> FolderFields(string[] words,string originalCommand)
        {
            string body=String.Join(" ",words,2,words.Length-2);if(originalCommand!=null){Match header=Regex.Match(originalCommand,"^\\s*q\\.folder\\s+run(?:\\s+|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);if(!header.Success)throw new ArgumentException("Invalid folder command syntax.");body=originalCommand.Substring(header.Length).Trim();}
            Dictionary<string,string> fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);MatchCollection matches=Regex.Matches(body,"(?:^|\\s+)([A-Za-z]+)=(?:\"([^\"]*)\"|([^\\s]+))");int consumed=0;foreach(Match match in matches){if(match.Index!=consumed)throw new ArgumentException("Invalid folder field syntax.");string key=match.Groups[1].Value.ToLowerInvariant();if(key!="path" &&key!="downloads" &&key!="purge" &&key!="verify" &&key!="samples")throw new ArgumentException("Unknown folder field: "+key);if(fields.ContainsKey(key))throw new ArgumentException("Duplicate folder field.");fields.Add(key,match.Groups[2].Success ?match.Groups[2].Value:match.Groups[3].Value);consumed=match.Index+match.Length;}if(consumed!=body.Length)throw new ArgumentException("Invalid folder field syntax.");return fields;
        }
        private static long VerificationSampleField(string value){long result;if(String.IsNullOrEmpty(value) ||!Regex.IsMatch(value,"^[0-9]{1,11}$") ||!Int64.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out result) ||result<1 ||result>MaximumVerificationSamples)throw new ArgumentException("Folder verification samples must be a decimal integer in 1..16384000000.");return result;}
        private static long SavedVerificationSamples(object value)
        {
            // JSON integer counts from 3.0.6 deserialize as Int32; larger exact 3.0.7 counts as Int64. Never round a fractional number.
            if(!(value is int) &&!(value is long) &&!(value is string))throw new InvalidDataException("Invalid saved verification sample request: an exact decimal integer is required.");string text=Convert.ToString(value,CultureInfo.InvariantCulture);long parsed;if(!Regex.IsMatch(text ?? "","^[1-9][0-9]{0,10}$") ||!Int64.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out parsed) ||parsed<1 ||parsed>MaximumVerificationSamples)throw new InvalidDataException("Invalid saved verification sample request: expected a canonical integer in 1..16384000000.");return parsed;
        }
        private static bool BooleanField(string value){if(Eq(value,"true"))return true;if(Eq(value,"false"))return false;throw new ArgumentException("Boolean folder fields accept true/false.");}
        private static string Name(string value){if(!Regex.IsMatch(value,"^[A-Za-z][A-Za-z0-9_-]{0,31}$"))throw new ArgumentException("Invalid circuit name.");return value;}
        private static int Number(string value,int minimum,int maximum){BigInteger n;if(String.IsNullOrEmpty(value)||!Regex.IsMatch(value,@"^-?[0-9]+$"))throw new ArgumentException("Expected an integer value.");if(!BigInteger.TryParse(value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out n)||n<minimum||n>maximum)throw new ArgumentOutOfRangeException("number","Integer outside allowed range.");return (int)n;}
        private static ulong Unsigned(string value){ulong n;if(!UInt64.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out n))throw new ArgumentException("Expected unsigned decimal integer.");return n;}
        private static double Real(string expression){double value=InputAttachmentManager.EvaluateExpression(expression,new Dictionary<string,double>());if(!QuantumMath.IsFinite(value)||Math.Abs(value)>1e12)throw new ArgumentException("Angle/value must be finite within +/-1e12.");return value;}
        private static string Basis(string basis){basis=basis.ToLowerInvariant();if(basis!="x" &&basis!="y" &&basis!="z")throw new ArgumentException("Measurement basis must be x/y/z.");return basis;}
        private static void Exact(string[] w,int n){if(w.Length!=n)throw new ArgumentException("Incorrect command argument count.");}
        private static void Need(string got,string expected){if(!Eq(got,expected))throw new ArgumentException("Expected "+expected+".");}
        private static bool Eq(string a,string b){return String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
        private static object Value(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)?value:null;}
        private static string Text(Dictionary<string,object> data,string key){return Convert.ToString(Value(data,key),CultureInfo.InvariantCulture)??"";}
        private static bool Bool(Dictionary<string,object> data,string key){return Value(data,key) is bool &&(bool)data[key];}
        private static int Int(object value){return value==null ?0 :Convert.ToInt32(value,CultureInfo.InvariantCulture);}
        private static long Long(object value){return value==null ?0:Convert.ToInt64(value,CultureInfo.InvariantCulture);}
        private static Dictionary<string,object> Map(object value){return value as Dictionary<string,object> ??new Dictionary<string,object>();}
        private static List<object> Items(object value){List<object> list=new List<object>();IEnumerable sequence=value as IEnumerable;if(sequence==null ||value is string)return list;foreach(object item in sequence)list.Add(item);return list;}
        private static string[] Strings(object value){List<string> result=new List<string>();foreach(object item in Items(value)){if(!(item is string))throw new InvalidDataException("Expected command/ID strings.");result.Add((string)item);}return result.ToArray();}
        private static string[] LimitedCommands(string[] commands){int count=Math.Min(MaximumCommands,commands.Length);string[] limited=new string[count];Array.Copy(commands,limited,count);return limited;}
        private static BigInteger Count(string text){BigInteger n;if(!BigInteger.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0)throw new InvalidDataException("Invalid nonnegative sequence counter.");return n;}
        private static string S(BigInteger value){return value.ToString(CultureInfo.InvariantCulture);}
        private static string Utc(){return DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);}
        private static void CheckId(string id){if(id==null ||!Regex.IsMatch(id,"^[0-9a-f]{32}$"))throw new ArgumentException("Invalid sequence ID.");}
        private static string Hash(string text){using(SHA256 hash=SHA256.Create()){StringBuilder result=new StringBuilder();foreach(byte b in hash.ComputeHash(Encoding.UTF8.GetBytes(text)))result.Append(b.ToString("x2",CultureInfo.InvariantCulture));return result.ToString();}}
        private static string HashStream(Stream source){using(SHA256 hash=SHA256.Create()){StringBuilder result=new StringBuilder();foreach(byte b in hash.ComputeHash(source))result.Append(b.ToString("x2",CultureInfo.InvariantCulture));return result.ToString();}}
        private static void CheckStage(string stage){if(stage!="accepted" &&stage!="completed" &&stage!="latest")throw new ArgumentException("Report stage is accepted, completed or latest.");}
        private static void Atomic(string path,string value){string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temporary,value,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
        private static Dictionary<string,object> D(params object[] pairs){Dictionary<string,object> result=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)result.Add((string)pairs[i],pairs[i+1]);return result;}
        private void Alive(){if(disposed||disposing)throw new ObjectDisposedException("SequenceTerminal");}
        public void Dispose(){Thread pending;lock(sync){if(disposed||disposing)return;disposing=true;if(job!=null &&job.Active)job.Cancel=true;pending=thread;}try{if(pending!=null &&pending.IsAlive &&pending!=Thread.CurrentThread &&!pending.Join(10000))throw new InvalidOperationException("The native sequence command has not finished; keep its session alive until cancellation completes.");lock(sync){disposed=true;if(job!=null)SaveJob(job);}}finally{lock(sync){disposing=false;}}}
    }
}


