using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace QuettaQuantum
{
    public sealed class CircuitQueryView
    {
        private readonly Func<ulong, double[]> amplitude;
        private readonly Func<int, double[]> spin;
        private readonly int fullQubitLimit, fullStateLimit, ownerThread;
        private bool active = true;
        public int Qubits { get; private set; }
        public string ProgramHash { get; private set; }
        public ulong BasisStateCount { get { return 1UL << Qubits; } }
        internal CircuitQueryView(int qubits, string hash, int maxQubits, int maxStates, Func<ulong, double[]> a, Func<int, double[]> s)
        { Qubits = qubits; ProgramHash = hash; fullQubitLimit = maxQubits; fullStateLimit = maxStates; amplitude = a; spin = s; ownerThread = Thread.CurrentThread.ManagedThreadId; }
        private void CheckAccess() { if (!active || Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("A circuit query view is valid only inside its synchronous snapshot callback."); }
        public double[] Amplitude(ulong basisIndex) { CheckAccess(); return amplitude(basisIndex); }
        public double[] Spin(int qubit) { CheckAccess(); return spin(qubit); }
        public double[] FullState()
        {
            CheckAccess(); if (Qubits > fullQubitLimit || BasisStateCount > (ulong)fullStateLimit) throw new InvalidOperationException("Full state exceeds this query's explicit output bound. Use a bounded amplitude prefix or reduced single-qubit spin instead.");
            double[] values = new double[checked((int)BasisStateCount *2)];
            for (ulong id = 0; id < BasisStateCount; id++) { double[] a = amplitude(id); values[(int)id *2] = a[0]; values[(int)id *2 +1] = a[1]; }
            return values;
        }
        internal void Close() { active = false; }
    }

    // Selected, genuine coherent circuits. Every measurement shot prepares and
    // executes an independent register. No address-family throughput is credited.
    public sealed class CircuitTerminal : IDisposable
    {
        public const int DefaultPresetShots = 1024;
        public sealed class PresetOptions
        {
            public readonly string Preset, Name, Basis;
            public readonly int Qubits, Shots;
            public readonly bool Run;
            internal PresetOptions(string preset, int qubits, string name, int shots, string basis, bool run)
            { Preset = preset; Qubits = qubits; Name = name; Shots = shots; Basis = basis; Run = run; }
        }
        // Shared by direct dispatch and sequence preflight; parsing never changes a circuit.
        public static PresetOptions ParsePresetOptions(string[] words)
        {
            if (words == null || words.Length < 3 || !Eq(words[0], "q.circuit") || !Eq(words[1], "preset"))
                throw new ArgumentException("Use q.circuit preset bell|ghz|qft [QUBITS] [shots N] [basis x|y|z] [run=false] [as FRESH_NAME].");
            string preset = words[2].ToLowerInvariant();
            if (preset != "bell" && preset != "ghz" && preset != "qft") throw new ArgumentException("Presets are bell, ghz and qft.");
            int at = 3, n = preset == "bell" ? 2 : 29, shots = DefaultPresetShots;
            if (at < words.Length && System.Text.RegularExpressions.Regex.IsMatch(words[at], @"^-?[0-9]+$")) n = Integer(words[at++], 1, 29);
            if (preset == "bell" && n != 2) throw new ArgumentException("The Bell preset has exactly two qubits.");
            string name = null, basis = "z"; bool run = true;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (at < words.Length)
            {
                string token = words[at++], key, value; int equal = token.IndexOf('=');
                if (equal >= 0) { key = token.Substring(0, equal); value = token.Substring(equal + 1); }
                else { key = token; if (at == words.Length) throw new ArgumentException("Missing circuit preset option value."); value = words[at++]; }
                if (!seen.Add(key)) throw new ArgumentException("Duplicate circuit preset option: " + key + ".");
                if (Eq(key, "shots")) shots = Integer(value, 1, 100000);
                else if (Eq(key, "basis")) basis = Basis(value);
                else if (Eq(key, "as")) name = Name(value);
                else if (Eq(key, "run"))
                {
                    if (!Eq(value, "true") && !Eq(value, "false")) throw new ArgumentException("Preset run must be true or false.");
                    run = Eq(value, "true");
                }
                else throw new ArgumentException("Unknown circuit preset option: " + key + ".");
            }
            return new PresetOptions(preset, n, name, shots, basis, run);
        }
        private readonly object sync = new object();
        private readonly string metadataPath, errorsPath, seed;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Dictionary<string, Program> programs = new Dictionary<string, Program>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BigInteger> lifetime = NewCounts();
        private string selected = "";
        // 3.0.3: configurable circuit storage and preset-owned circuit tracking.
        private const int DefaultProgramCapacity = 64, MaximumProgramCapacity = 1024;
        private int programCapacity = DefaultProgramCapacity;
        private readonly Dictionary<string, ulong> presetOwnedSerial = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> presetOwnedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private ulong ownershipSerial;
        private ulong counter, memoryLimit = 512UL * 1024 * 1024;
        private bool disposed;
        private Thread thread;
        private Work job;
        private TrancheRegister coherentOutput;
        private Dictionary<string, BigInteger> coherentBaseline;
        private Dictionary<string, object> coherentSnapshot;
        private string coherentHash = "";
        private const int HistogramPreviewLimit = 4000;
        private static readonly string[] CountKeys = {
            "executedCircuitInstructions", "backendGateApplications", "actualAmplitudeWrites",
            "actualTensorProducts", "actualPreparedQubits", "completedShots", "measuredQubits",
            "uniformDraws", "exactCPReductions", "queryCount", "coherentOutputExecutions"
        };

        private sealed class Instruction
        {
            public string Name;
            public double[] Angles;
            public int[] Qubits;
            public bool All;
        }
        private sealed class Program
        {
            public string Name;
            public int Qubits;
            public ulong InitialBasis;
            public List<Instruction> Gates = new List<Instruction>();
            public Dictionary<string, object> InputImport;
        }
        private sealed class Work
        {
            public string Id = Guid.NewGuid().ToString("N"), Phase = "queued", Basis, Error = "", CheckpointError = "";
            public bool Active = true;
            public volatile bool Cancel;
            public int RequestedShots, CompletedShots, MeasuredQubits;
            public ulong CounterStart, CounterEnd, MemoryLimit;
            public double PreviousElapsedMs;
            public readonly Stopwatch Clock = new Stopwatch();
            public Program Program;
            public string ProgramHash;
            public readonly Dictionary<string, ulong> Histogram = new Dictionary<string, ulong>(StringComparer.Ordinal);
            public readonly Dictionary<string, BigInteger> Counts = NewCounts();
            public Dictionary<string, object> CoherentSnapshot;
            public Dictionary<string, object> SpinPreview;
        }

        public CircuitTerminal(string workspace, string measurementSeed)
        {
            if (String.IsNullOrEmpty(workspace)) throw new ArgumentException("Circuit workspace is required.");
            if (String.IsNullOrEmpty(measurementSeed)) throw new ArgumentException("Persistent measurement seed is required.");
            metadataPath = Path.Combine(workspace, "spindoctor-circuits.json");
            errorsPath = Path.Combine(workspace, "spindoctor-circuit-errors.log");
            seed = Hash(measurementSeed + "|SpinDoctor|coherent-circuit-measurements");
            json.MaxJsonLength = 33554432; json.RecursionLimit = 80;
            if (File.Exists(metadataPath)) Load();
        }

        public Dictionary<string, object> GetStatus()
        {
            lock (sync)
            {
                return D("product", "SpinDoctor", "available", true, "selected", selected,
                    "programCount", programs.Count, "programCapacity", programCapacity, "presetOwnedCount", presetOwnedSerial.Count, "programNames", ProgramNames(), "programSummaries", ProgramSummaries(), "program", selected.Length == 0 ? null : ProgramObject(Current(), false),
                    "measurementSeed", seed, "counter", S(counter), "memoryLimitBytes", S(memoryLimit),
                    "memoryAccountingScope", "Combined estimated retained coherent output and working shot state, including backend transactional work. Managed GC retention and other host memory are reported separately by the backend.",
                    "lifetime", CountsObject(lifetime), "job", job == null ? null : JobObject(job),
                    "result", job == null ? null : ResultObject(job),
                    "coherentOutputAvailable", coherentOutput != null, "coherentOutputProgramHash", coherentHash,
                    "coherentOutputStatus", coherentSnapshot,
                    "scope", "Selected 1..29-qubit coherent circuits, simulated with classical FP64 arithmetic and seeded Born sampling. Shots independently replay the circuit. No physical quantum hardware or whole-family gate execution is claimed.");
            }
        }

        public object WithQueryView(int maximumFullQubits, int maximumFullStates, Func<CircuitQueryView, object> query)
        {
            if (query ==null || maximumFullQubits <1 || maximumFullQubits >16 || maximumFullStates <1 || maximumFullStates >65536) throw new ArgumentException("Query callback and bounded full-state limits (at most 16 qubits/65536 states) are required.");
            lock (sync)
            {
                Alive(); if (job !=null &&job.Active) throw new InvalidOperationException("Wait for the circuit run to finish before querying its premeasurement state.");
                if (coherentOutput ==null ||coherentHash !=ProgramHash(Current())) throw new InvalidOperationException("Run the selected circuit before querying its retained coherent state.");
                CircuitQueryView view = new CircuitQueryView(coherentOutput.Qubits, coherentHash, maximumFullQubits, maximumFullStates, coherentOutput.Amplitude, coherentOutput.Spin);
                try { return query(view); }
                finally { view.Close(); Account(coherentOutput, coherentBaseline, job); coherentSnapshot = coherentOutput.Status(); SaveLocked(); }
            }
        }
        public Dictionary<string,object> ReleaseTransientOutput()
        {
            lock(sync)
            {
                Alive();if(job!=null &&job.Active)throw new InvalidOperationException("Finish or cancel the active circuit before releasing its transient output.");
                bool existed=coherentOutput!=null;if(existed)Account(coherentOutput,coherentBaseline,job);ReleaseCoherent();SaveLocked();
                return D("released",true,"hadCoherentOutput",existed,"programsPreserved",programs.Count,"seedPreserved",true,"counter",S(counter),"histogramPreserved",job!=null,"scope","Managed coherent-output references are released. OS memory reclamation follows the runtime; programs, replay metadata, reports and measurement history remain preserved.");
            }
        }

        public Dictionary<string, object> ImportCircuit(Dictionary<string, object> spec)
        { return ImportCircuit(spec, null); }
        public Dictionary<string, object> ImportCircuit(Dictionary<string, object> spec, Action<Dictionary<string, object>> beforeMutation)
        {
            if (spec == null) throw new ArgumentNullException("spec");
            lock (sync)
            {
                Alive();
                if (job != null && job.Active) throw new InvalidOperationException("A circuit run is active. Finish or cancel it before importing another program.");
                Program p = new Program { Name = Name(Text(spec, "name")), Qubits = Integer(Text(spec, "qubits"), 1, 29), InitialBasis = Unsigned(Text(spec, "initialBasis")) };
                if (p.InitialBasis >= (1UL << p.Qubits)) throw new ArgumentException("Initial basis must be below 2^qubits.");
                foreach (object value in Sequence(spec["gates"]))
                {
                    if (p.Gates.Count >= 8192) throw new ArgumentException("Imported circuit exceeds 8192 instructions.");
                    Dictionary<string, object> gate = value as Dictionary<string, object>;
                    if (gate == null) throw new ArgumentException("Imported gate must be an object.");
                    List<string> words = new List<string>(); words.Add(Text(gate, "name"));
                    foreach (object angle in Sequence(gate["angles"])) words.Add(Convert.ToDouble(angle, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                    if (Convert.ToBoolean(gate["all"], CultureInfo.InvariantCulture)) words.Add("all");
                    else foreach (object q in Sequence(gate["qubits"])) words.Add(Convert.ToString(q, CultureInfo.InvariantCulture));
                    p.Gates.Add(ParseInstruction(words.ToArray(), 0, p.Qubits));
                }
                bool replaced = programs.ContainsKey(p.Name);
                if (!replaced && programs.Count >= programCapacity) throw new InvalidOperationException(CapacityMessage());
                int old = replaced ? programs[p.Name].Gates.Count : 0;
                if (InstructionCount() - old + p.Gates.Count > 65536) throw new InvalidOperationException("Total circuit instruction storage limit reached.");
                Dictionary<string, object> metadata = new Dictionary<string, object>();
                foreach (string key in new string[] { "source", "parameters", "hints", "equations" }) { object v; if (spec.TryGetValue(key, out v)) metadata[key] = v; }
                metadata["appliedProgramHash"] = ProgramHash(p);
                // Detach preview data from the stored immutable program provenance.
                p.InputImport = json.DeserializeObject(json.Serialize(metadata)) as Dictionary<string, object>;
                if (beforeMutation != null && job != null) beforeMutation(GetStatus());
                CommitProgramMutation(delegate() { programs[p.Name] = p; selected = p.Name; presetOwnedSerial.Remove(p.Name); presetOwnedBy.Remove(p.Name); });
                return D("imported", true, "replaced", replaced, "executed", false, "seedPreserved", true, "program", ProgramObject(p, false), "note", "Input is validated and selected. Run it explicitly with q.circuit run.");
            }
        }

        public object Execute(string[] words)
        { return ExecuteAndSnapshot(words, null); }
        // The caller prepares any other-controller status before entry. This callback
        // observes the final old job under the same lock that guards replacement.
        // If its archive cannot be persisted, no program/job/output mutation follows.
        public object ExecuteAndSnapshot(string[] words, Action<Dictionary<string, object>> beforeMutation)
        {
            if (words == null || words.Length < 2 || !Eq(words[0], "q.circuit")) throw new ArgumentException("Use q.circuit help.");
            string command = words[1].ToLowerInvariant();
            lock (sync)
            {
                Alive();
                if (command == "help") { Exact(words, 2); return Help(); }
                if (command == "status") { Exact(words, 2); return GetStatus(); }
                if (command == "capacity" && words.Length == 2) return CapacityObject();
                if (command == "result") { Exact(words, 2); return job == null ? D("available", false) : ResultObject(job); }
                if (command == "cancel")
                {
                    Exact(words, 2); if (job != null && job.Active) { job.Cancel = true; job.Phase = "cancelling"; SaveLocked(); }
                    return D("cancelRequested", job != null && job.Active, "job", job == null ? null : JobObject(job));
                }
                if (job != null && job.Active) throw new InvalidOperationException("A circuit run is active. Use q.circuit status, result or cancel until it stops.");
                if (beforeMutation != null && job != null) beforeMutation(GetStatus());
                if (command == "new")
                {
                    Exact(words, 4); string name = Name(words[2]); int qubits = Integer(words[3], 1, 29);
                    if (programs.ContainsKey(name)) throw new ArgumentException("Circuit name already exists. Select it or delete it first.");
                    if (programs.Count >= programCapacity) throw new InvalidOperationException(CapacityMessage());
                    CommitProgramMutation(delegate() { programs[name] = new Program { Name = name, Qubits = qubits }; selected = name; }); return ProgramObject(Current(), true);
                }
                if (command == "select")
                {
                    Exact(words, 3); Program p; if (!programs.TryGetValue(words[2], out p)) throw new ArgumentException("Unknown circuit name.");
                    CommitProgramMutation(delegate() { selected = p.Name; }); return ProgramObject(p, true);
                }
                if (command == "list")
                {
                    Exact(words, 2); List<object> list = new List<object>(); foreach (Program p in programs.Values) list.Add(ProgramObject(p, false));
                    return D("selected", selected, "circuits", list.ToArray());
                }
                if (command == "show") { Exact(words, 2); return ProgramObject(Current(), true); }
                if (command == "input")
                {
                    Exact(words, 3); Program p = Current(); ulong value = Unsigned(words[2]);
                    if (value >= (1UL << p.Qubits)) throw new ArgumentOutOfRangeException("basisIndex", "Input basis index must be below 2^qubits.");
                    CommitProgramMutation(delegate() { p.InitialBasis = value; }); return ProgramObject(p, false);
                }
                if (command == "add")
                {
                    Program p = Current(); if (p.Gates.Count >= 8192 || InstructionCount() >= 65536) throw new InvalidOperationException("Circuit program storage limit reached (8192 instructions per program, 65536 total).");
                    Instruction instruction = ParseInstruction(words, 2, p.Qubits);
                    CommitProgramMutation(delegate() { p.Gates.Add(instruction); }); return D("program", ProgramObject(p, false), "instruction", InstructionObject(instruction));
                }
                if (command == "clear") { Exact(words, 2); Program p = Current(); CommitProgramMutation(delegate() { p.Gates.Clear(); }); return ProgramObject(Current(), true); }
                if (command == "delete")
                {
                    if (words.Length != 2 && words.Length != 3) throw new ArgumentException("Use q.circuit delete [NAME].");
                    string name = words.Length == 3 ? words[2] : Current().Name;
                    if (!programs.ContainsKey(name)) throw new ArgumentException("Unknown circuit name.");
                    CommitProgramMutation(delegate() { programs.Remove(name); if (Eq(selected, name)) selected = ""; }); return D("deleted", name, "selected", selected);
                }
                if (command == "capacity")
                {
                    Exact(words, 3); int requested = Integer(words[2], 1, MaximumProgramCapacity);
                    if (requested < programs.Count) throw new ArgumentException("Capacity cannot be set below the " + programs.Count.ToString(CultureInfo.InvariantCulture) + " circuits already saved. Use q.circuit delete NAME or q.circuit prune presets first.");
                    CommitProgramMutation(delegate() { programCapacity = requested; }); return CapacityObject();
                }
                if (command == "prune")
                {
                    if ((words.Length != 3 && words.Length != 4) || !Eq(words[2], "presets")) throw new ArgumentException("Use q.circuit prune presets [KEEP]. Only circuits created by q.preset runs are removed, oldest first; user-created circuits are never pruned.");
                    int keep = words.Length == 4 ? Integer(words[3], 0, MaximumProgramCapacity) : 0;
                    List<string> order = PresetOwnedOldestFirst(); List<string> removed = new List<string>();
                    for (int i = 0; i < order.Count - keep; ++i) removed.Add(order[i]);
                    if (removed.Count > 0) CommitProgramMutation(delegate() { foreach (string n in removed) { programs.Remove(n); if (Eq(selected, n)) selected = ""; } });
                    return D("pruned", removed.ToArray(), "prunedCount", removed.Count, "keptPresetCircuits", presetOwnedSerial.Count, "selected", selected, "capacity", CapacityObject(),
                        "scope", "Removes only circuit programs that a q.preset run created (oldest first). User circuits, seeds, counters and the last histogram are preserved.");
                }
                if (command == "preset")
                {
                    PresetOptions options = ParsePresetOptions(words);
                    string isolatedName = options.Name, preset = options.Preset; int n = options.Qubits;
                    string name = isolatedName ?? (preset == "bell" ? "bell" : preset + "-" + n);
                    if (isolatedName !=null &&programs.ContainsKey(name)) throw new ArgumentException("Isolated preset name already exists; supply a fresh name.");
                    if (!programs.ContainsKey(name) && programs.Count >= programCapacity) throw new InvalidOperationException(CapacityMessage());
                    Program p = new Program { Name = name, Qubits = n };
                    if (preset == "bell" || preset == "ghz")
                    {
                        p.Gates.Add(G("h", new double[0], new int[] { 0 }));
                        for (int q = 1; q < n; ++q) p.Gates.Add(G("cx", new double[0], new int[] { 0, q }));
                    }
                    else
                    {
                        // Positive Fourier convention, followed by bit-reversal swaps.
                        for (int q = n - 1; q >= 0; --q)
                        {
                            p.Gates.Add(G("h", new double[0], new int[] { q }));
                            for (int lower = q - 1; lower >= 0; --lower)
                                p.Gates.Add(G("cp", new double[] { Math.PI / Math.Pow(2, q - lower) }, new int[] { q, lower }));
                        }
                        for (int q = 0; q < n / 2; ++q) p.Gates.Add(G("swap", new double[0], new int[] { q, n - 1 - q }));
                    }
                    int existing = programs.ContainsKey(name) ? programs[name].Gates.Count : 0;
                    if (InstructionCount() - existing + p.Gates.Count > 65536) throw new InvalidOperationException("Circuit program storage limit reached.");
                    if (!options.Run)
                    {
                        CommitProgramMutation(delegate() { programs[name] = p; selected = name; });
                        Dictionary<string, object> defined = ProgramObject(p, true);
                        defined["executionRequested"] = false; defined["note"] = "Preset defined and selected only (run=false). No circuit execution or measurement shots were requested.";
                        return defined;
                    }
                    Dictionary<string, object> queued = QueueRun(Clone(p), options.Shots, options.Basis, delegate() { programs[name] = p; selected = name; });
                    queued["preset"] = preset; queued["automaticPresetRun"] = true; queued["program"] = ProgramObject(p, true);
                    queued["defaultShots"] = DefaultPresetShots;
                    return queued;
                }
                if (command == "memory")
                {
                    Exact(words, 3); ulong mib = Unsigned(words[2]);
                    if (mib < 2 || mib > UInt64.MaxValue / (1024UL * 1024)) throw new ArgumentOutOfRangeException("MiB", "Combined circuit budget must be at least 2 MiB.");
                    ulong bytes = mib * 1024 * 1024;
                    if (coherentSnapshot != null && Unsigned(Convert.ToString(coherentSnapshot["allocatedStateBytesEstimate"], CultureInfo.InvariantCulture)) > bytes - 1024 * 1024)
                        throw new InvalidOperationException("Budget must hold the retained coherent output plus at least 1 MiB for a shot. Start a new smaller run before reducing it.");
                    ulong previous = memoryLimit; memoryLimit = bytes;
                    try { SaveLocked(); } catch { memoryLimit = previous; throw; }
                    return D("memoryLimitBytes", S(memoryLimit), "scope", "Combined retained output and working shot transactional state estimate.");
                }
                if (command == "run")
                {
                    if (words.Length != 6 || !Eq(words[2], "shots") || !Eq(words[4], "basis")) throw new ArgumentException("Use q.circuit run shots N basis x|y|z.");
                    int shots = Integer(words[3], 1, 100000); string basis = Basis(words[5]); Program p = Clone(Current());
                    return QueueRun(p, shots, basis, null);
                }
                if (command == "state" || command == "spin")
                {
                    Exact(words, 3); if (coherentOutput == null || coherentHash != ProgramHash(Current())) throw new InvalidOperationException("No retained coherent output for the selected program is available. Run the circuit first; persisted histograms survive restart, while coherent output is recalculated by the next run.");
                    object result;
                    if (command == "state") result = coherentOutput.State(Unsigned(words[2]));
                    else result = D("qubit", Integer(words[2], 0, coherentOutput.Qubits - 1), "spin", coherentOutput.Spin(Integer(words[2], 0, coherentOutput.Qubits - 1)));
                    Account(coherentOutput, coherentBaseline, job); coherentSnapshot = coherentOutput.Status(); SaveLocked();
                    return D("programHash", coherentHash, "premeasurement", true, "result", result);
                }
                if (command == "export")
                {
                    Exact(words, 2); return D("schema", "SPINDOCTOR/CIRCUIT/1", "program", ProgramObject(Current(), true), "measurementSeed", seed, "counter", S(counter), "status", GetStatus());
                }
                throw new ArgumentException("Unknown circuit command. Use q.circuit help.");
            }
        }

        // Called with sync held. The accepted job is captured before the worker can
        // finish, preserving the native identity used by receipts and sequence waits.
        private Dictionary<string, object> QueueRun(Program p, int shots, string basis, Action programChange)
        {
            if (UInt64.MaxValue - counter < (ulong)shots * (ulong)p.Qubits) throw new InvalidOperationException("Measurement counter exhausted.");
            Work previous = job;
            Work target = new Work { Program = p, ProgramHash = ProgramHash(p), RequestedShots = shots, Basis = basis, CounterStart = counter, CounterEnd = counter, MemoryLimit = memoryLimit };
            Thread pending = new Thread(delegate() { Run(target); }); pending.IsBackground = true;
            try
            {
                if (programChange == null) { job = target; SaveLocked(); }
                else CommitProgramMutation(delegate() { programChange(); job = target; });
            }
            catch { job = previous; throw; }
            ReleaseCoherent(); thread = pending;
            try { thread.Start(); }
            catch (Exception error) { target.Active = false; target.Phase = "failed"; target.Error = "Could not start circuit worker: " + error.Message; SaveLocked(); throw; }
            return D("queued", true, "job", JobObject(target));
        }

        private void Run(Work target)
        {
            TrancheRegister current = null;
            Dictionary<string, BigInteger> baseline = null;
            try
            {
                target.Clock.Start(); SetPhase(target, "preparing coherent output"); CheckCancel(target);
                current = Prepare(target, target.MemoryLimit - 1024 * 1024); baseline = BackendBaseline();
                Replay(current, target.Program, target); CheckCancel(target);
                Dictionary<string, object> spinPreview = CaptureSpinPreview(current, target);
                Account(current, baseline, target); CheckCancel(target);
                Dictionary<string, object> snapshot = current.Status();
                ulong cacheBytes = Unsigned(Convert.ToString(snapshot["allocatedStateBytesEstimate"], CultureInfo.InvariantCulture));
                lock (sync)
                {
                    coherentOutput = current; coherentBaseline = baseline; coherentSnapshot = snapshot; coherentHash = target.ProgramHash;
                    target.CoherentSnapshot = snapshot; target.SpinPreview = spinPreview; AddCount(target, "coherentOutputExecutions", 1);
                }
                current = null; baseline = null;
                if (cacheBytes > target.MemoryLimit - 1024 * 1024) throw new InvalidOperationException("Retained output leaves insufficient memory for an independent shot.");
                ulong shotBudget = target.MemoryLimit - cacheBytes;
                double lastSave = target.Clock.Elapsed.TotalMilliseconds;
                for (int shot = 0; shot < target.RequestedShots; ++shot)
                {
                    CheckCancel(target); SetPhase(target, "executing shot " + (shot + 1).ToString(CultureInfo.InvariantCulture));
                    current = Prepare(target, shotBudget); baseline = BackendBaseline();
                    Replay(current, target.Program, target);
                    ulong result = 0;
                    for (int q = 0; q < target.Program.Qubits; ++q)
                    {
                        CheckCancel(target);
                        Dictionary<string, object> measured = current.Measure(q, target.Basis, 1, delegate() { return Uniform(target); }, true, delegate() { return target.Cancel; });
                        if ((int)measured["collapseOutcome"] == 1) result |= 1UL << q;
                        lock (sync) { target.MeasuredQubits++; AddCount(target, "measuredQubits", 1); }
                    }
                    Account(current, baseline, target); current.Dispose(); current = null; baseline = null;
                    lock (sync)
                    {
                        string bits = Bits(result, target.Program.Qubits); ulong old;
                        target.Histogram.TryGetValue(bits, out old); target.Histogram[bits] = old + 1;
                        target.CompletedShots++; AddCount(target, "completedShots", 1); target.CounterEnd = counter;
                        if ((target.CompletedShots & 127) == 0 || target.Clock.Elapsed.TotalMilliseconds - lastSave >= 250)
                        {
                            SaveLocked(); lastSave = target.Clock.Elapsed.TotalMilliseconds;
                        }
                    }
                }
                SetPhase(target, "completed");
            }
            catch (OperationCanceledException) { SetPhase(target, "cancelled"); }
            catch (Exception error)
            {
                lock (sync) { target.Error = error.Message; target.Phase = "failed"; }
                LogError(target, error);
            }
            finally
            {
                if (current != null)
                {
                    try { if (baseline != null) Account(current, baseline, target); }
                    finally { current.Dispose(); }
                }
                lock (sync)
                {
                    target.Clock.Stop(); target.CounterEnd = counter; target.Active = false;
                    try { SaveLocked(); }
                    catch (Exception error) { target.CheckpointError = error.Message; LogError(target, error); }
                }
            }
        }

        private const string SpinPreviewSchema = "SPINDOCTOR/PREMEASUREMENT-SPIN-PREVIEW/1";
        private const int SpinPreviewQubitLimit = 4, SpinPreviewFactorLimit = 65536;
        private const string SpinPreviewScope = "Actual reduced Pauli expectations for the first at most four qubits of the retained coherent circuit output before independent measurement shots. Each queried coherent factor has at most 65536 stored entries. Omitted qubits were not evaluated and are not assigned zero vectors. This capture is separate from the saved procedural model and from postmeasurement shot states.";
        private const string SpinPreviewVerificationScope = "These are actual circuit-state calculations. The procedural-model q.verify CPU/GPU sampling gate does not independently verify these circuit snapshots.";
        private const string SpinPreviewOmission = "Existing coherent factor exceeds the bounded spin-preview traversal; no zero vector is inferred.";

        private static Dictionary<string, object> CaptureSpinPreview(TrancheRegister register, Work target)
        {
            List<object> rows = new List<object>(), omissions = new List<object>();
            int considered = Math.Min(SpinPreviewQubitLimit, target.Program.Qubits);
            for (int q = 0; q < considered; ++q)
            {
                CheckCancel(target);
                Dictionary<string, object> captured = register.BoundedSpinSnapshot(q, SpinPreviewFactorLimit, delegate() { return target.Cancel; });
                if (!(bool)captured["captured"])
                    omissions.Add(D("qubit", q, "reason", SpinPreviewOmission, "factorEntryCount", captured["factorEntryCount"]));
                else
                {
                    Dictionary<string, object> row = SpinDoctorCalculators.BlochStateMetadata((double[])captured["spin"], q);
                    row["factorEntryCount"] = captured["factorEntryCount"]; rows.Add(row);
                }
            }
            return D("schema", SpinPreviewSchema, "programHash", target.ProgramHash, "capturedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                "premeasurement", true, "qubitLimit", SpinPreviewQubitLimit, "maximumFactorEntries", SpinPreviewFactorLimit,
                "selectedQubits", target.Program.Qubits, "returnedQubits", rows.Count, "omittedQubits", target.Program.Qubits - rows.Count,
                "unrequestedQubits", target.Program.Qubits - considered, "rows", rows.ToArray(), "omissions", omissions.ToArray(),
                "scope", SpinPreviewScope, "independentVerification", false, "verificationScope", SpinPreviewVerificationScope);
        }

        private TrancheRegister Prepare(Work target, ulong budget)
        {
            CheckCancel(target);
            double[][] spinors = new double[target.Program.Qubits][];
            for (int q = 0; q < spinors.Length; ++q)
                spinors[q] = (target.Program.InitialBasis & (1UL << q)) == 0 ? new double[] { 1, 0, 0, 0 } : new double[] { 0, 0, 1, 0 };
            TrancheRegister register = new TrancheRegister(spinors.Length, spinors);
            lock (sync) { AddCount(target, "actualPreparedQubits", spinors.Length); }
            try { register.SetMemoryLimit(budget); return register; }
            catch
            {
                Account(register, BackendBaseline(), target); register.Dispose(); throw;
            }
        }

        private void Replay(TrancheRegister register, Program program, Work target)
        {
            int[] known = new int[program.Qubits];
            for (int q = 0; q < known.Length; ++q) known[q] = (program.InitialBasis & (1UL << q)) == 0 ? 0 : 1;
            foreach (Instruction gate in program.Gates)
            {
                CheckCancel(target);
                string name = gate.Name; double[] angles = gate.Angles; int[] qubits = gate.Qubits; bool all = gate.All, reduced = false;
                // Exact controlled-phase reduction: a known basis operand is an
                // unentangled eigenstate. CP then equals I or P on the other bit.
                if (name == "cp" && (known[qubits[0]] >= 0 || known[qubits[1]] >= 0))
                {
                    int knownPosition = known[qubits[0]] >= 0 ? 0 : 1;
                    int bit = known[qubits[knownPosition]]; qubits = new int[] { qubits[1 - knownPosition] };
                    name = bit == 0 ? "i" : "p"; angles = bit == 0 ? new double[0] : angles; all = false; reduced = true;
                }
                register.Gate(name, angles, qubits, all, delegate() { return target.Cancel; }, null);
                lock (sync)
                {
                    AddCount(target, "executedCircuitInstructions", 1);
                    AddCount(target, "backendGateApplications", all ? program.Qubits : 1);
                    if (reduced) AddCount(target, "exactCPReductions", 1);
                }
                UpdateKnown(known, gate);
            }
        }

        private static void UpdateKnown(int[] known, Instruction gate)
        {
            string n = gate.Name;
            if (n == "cp" || n == "cz" || n == "crz" || n == "rzz") return;
            if (n == "swap") { int old = known[gate.Qubits[0]]; known[gate.Qubits[0]] = known[gate.Qubits[1]]; known[gate.Qubits[1]] = old; return; }
            bool local = GateSpec(n)["local"].Equals(true);
            if (local)
            {
                int[] targets = gate.Qubits;
                if (gate.All) { targets = new int[known.Length]; for (int q = 0; q < known.Length; ++q) targets[q] = q; }
                foreach (int q in targets)
                {
                    if (n == "x" || n == "y") { if (known[q] >= 0) known[q] = 1 - known[q]; }
                    else if (n != "i" && n != "z" && n != "s" && n != "sdg" && n != "t" && n != "tdg" && n != "rz" && n != "p") known[q] = -1;
                }
            }
            else foreach (int q in gate.Qubits) known[q] = -1;
        }

        private void Account(TrancheRegister register, Dictionary<string, BigInteger> baseline, Work target)
        {
            Dictionary<string, object> status = register.Status();
            lock (sync)
            {
                foreach (string key in new string[] { "actualAmplitudeWrites", "actualTensorProducts", "queryCount" })
                {
                    BigInteger total = BigInteger.Parse(Convert.ToString(status[key], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    BigInteger delta = total - baseline[key]; if (delta < 0) throw new InvalidOperationException("Backend accounting regressed.");
                    AddCount(target, key, delta); baseline[key] = total;
                }
            }
        }
        private double Uniform(Work target)
        {
            ulong index;
            lock (sync)
            {
                if (counter == UInt64.MaxValue) throw new InvalidOperationException("Measurement counter exhausted.");
                index = counter++; target.CounterEnd = counter; AddCount(target, "uniformDraws", 1);
            }
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(seed + "|circuit-born|" + S(index)));
                ulong word = 0; for (int j = 0; j < 8; ++j) word = (word << 8) | bytes[j];
                return (word >> 11) * (1.0 / 9007199254740992.0);
            }
        }
        private void AddCount(Work target, string key, BigInteger delta)
        {
            lifetime[key] += delta; if (target != null) target.Counts[key] += delta;
        }
        private void SetPhase(Work target, string phase) { lock (sync) { target.Phase = phase; } }
        private static void CheckCancel(Work target) { if (target.Cancel) throw new OperationCanceledException("Circuit run cancelled; completed shots and actually performed work are retained."); }

        private Dictionary<string, object> JobObject(Work target)
        {
            return D("id", target.Id, "active", target.Active, "phase", target.Phase,
                "requestedShots", target.RequestedShots.ToString(CultureInfo.InvariantCulture), "completedShots", target.CompletedShots.ToString(CultureInfo.InvariantCulture),
                "measuredQubits", target.MeasuredQubits.ToString(CultureInfo.InvariantCulture), "basis", target.Basis,
                "programName", target.Program.Name, "qubits", target.Program.Qubits, "initialBasis", S(target.Program.InitialBasis),
                "programHash", target.ProgramHash, "elapsedMs", target.PreviousElapsedMs + target.Clock.Elapsed.TotalMilliseconds,
                "programSnapshot", ProgramObject(target.Program, true),
                "counterStart", S(target.CounterStart), "counterEnd", S(target.CounterEnd),
                "error", target.Error, "checkpointError", target.CheckpointError, "counts", CountsObject(target.Counts), "coherentSpinPreview", target.SpinPreview);
        }
        private Dictionary<string, object> ResultObject(Work target)
        {
            Dictionary<string, object> result = JobObject(target); List<string> keys = new List<string>(target.Histogram.Keys); keys.Sort(StringComparer.Ordinal);
            List<object> preview = new List<object>(); ulong omittedShots = 0;
            for (int j = 0; j < keys.Count; ++j)
                if (j < HistogramPreviewLimit) preview.Add(D("bitstring", keys[j], "count", S(target.Histogram[keys[j]])));
                else omittedShots += target.Histogram[keys[j]];
            result["histogram"] = preview.ToArray(); result["histogramComplete"] = keys.Count <= HistogramPreviewLimit;
            result["distinctOutcomes"] = keys.Count; result["omittedOutcomes"] = Math.Max(0, keys.Count - HistogramPreviewLimit); result["omittedShots"] = S(omittedShots);
            result["fullPrintoutAvailable"] = true; result["coherentOutputAvailable"] = coherentOutput != null && coherentHash == target.ProgramHash;
            result["coherentOutputStatus"] = target.CoherentSnapshot; result["bitOrder"] = "Most significant displayed bit at left; qubit 0 at right.";
            result["seed"] = seed; result["scope"] = "Full joint bitstrings from independently replayed coherent registers and sequential collapsing Born measurements. Histograms contain completed shots only; partial shot work and consumed random draws are counted separately.";
            return result;
        }

        public Dictionary<string, object> ReportSnapshot()
        {
            lock (sync)
            {
                Alive();
                // Both copies observe the same completed-shot histogram and draw counters.
                // The controller lock is reentrant, so the existing accessors remain unchanged.
                return D("status", GetStatus(), "printout", MeasurementPrintout(), "capturedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            }
        }

        // Archive a matched job, including every completed outcome. This captures
        // descriptors and existing counters only; exporting never reruns a shot.
        public Dictionary<string, object> CaptureJobReport(string id)
        {
            lock (sync)
            {
                Alive();
                if (job == null || job.Id != id) throw new InvalidOperationException("The requested circuit report job no longer matches the current run.");
                Dictionary<string, object> result = ResultObject(job);
                List<string> keys = new List<string>(job.Histogram.Keys); keys.Sort(StringComparer.Ordinal);
                List<object> rows = new List<object>();
                foreach (string key in keys) rows.Add(D("bitstring", key, "count", S(job.Histogram[key])));
                result["histogram"] = rows.ToArray(); result["histogramComplete"] = true;
                result["omittedOutcomes"] = 0; result["omittedShots"] = "0";
                result["reportSnapshot"] = true;
                return result;
            }
        }

        public string MeasurementPrintout()
        {
            lock (sync)
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("SPINDOCTOR — COHERENT CIRCUIT MEASUREMENT PRINTOUT");
                text.AppendLine("Classical FP64 quantum-circuit simulation; independently replayed shots; no physical quantum hardware credit.");
                text.AppendLine("Measurement seed: " + seed); text.AppendLine("Current measurement counter: " + S(counter));
                if (job == null) { text.AppendLine("No circuit run has been requested."); return text.ToString(); }
                foreach (KeyValuePair<string, object> field in JobObject(job)) if (field.Key != "counts") text.AppendLine(field.Key + ": " + Convert.ToString(field.Value, CultureInfo.InvariantCulture));
                text.AppendLine("Exact program descriptor:"); text.AppendLine(json.Serialize(ProgramObject(job.Program, true)));
                text.AppendLine("ACTUALLY PERFORMED WORK (includes the separately retained premeasurement output execution):");
                foreach (KeyValuePair<string, BigInteger> count in job.Counts) text.AppendLine(count.Key + ": " + S(count.Value));
                text.AppendLine("Program instructions count submitted circuit instructions. Backend applications count local/joint state gate calls; exact CP reductions use I or P on an operand known to be a computational-basis eigenstate.");
                text.AppendLine("Coherent output retained: " + (coherentOutput != null && coherentHash == job.ProgramHash).ToString());
                text.AppendLine("FULL HISTOGRAM — bitstring (qubit 0 at right)\tcompleted shots");
                List<string> keys = new List<string>(job.Histogram.Keys); keys.Sort(StringComparer.Ordinal);
                ulong total = 0; foreach (string key in keys) { text.AppendLine(key + "\t" + S(job.Histogram[key])); total += job.Histogram[key]; }
                text.AppendLine("Distinct outcomes: " + keys.Count); text.AppendLine("Histogram total: " + S(total));
                text.AppendLine("Incomplete shots are excluded from histogram; measured qubits and random draws from partial shots remain in accounting.");
                return text.ToString();
            }
        }

        public void MarkPresetOwned(string name, string presetId)
        {
            lock (sync)
            {
                Alive(); Program p; if (String.IsNullOrEmpty(name) || !programs.TryGetValue(name, out p)) return;
                if (ownershipSerial == UInt64.MaxValue) throw new InvalidOperationException("Preset ownership counter exhausted.");
                ulong previousSerial; string previousBy; bool existed = presetOwnedSerial.TryGetValue(p.Name, out previousSerial); presetOwnedBy.TryGetValue(p.Name, out previousBy);
                presetOwnedSerial[p.Name] = ++ownershipSerial; presetOwnedBy[p.Name] = presetId ?? "";
                try { SaveLocked(); }
                catch { ownershipSerial--; if(existed) { presetOwnedSerial[p.Name]=previousSerial; presetOwnedBy[p.Name]=previousBy; } else { presetOwnedSerial.Remove(p.Name); presetOwnedBy.Remove(p.Name); } throw; }
            }
        }
        private void DropStaleOwnership()
        {
            foreach (string n in new List<string>(presetOwnedSerial.Keys)) if (!programs.ContainsKey(n)) { presetOwnedSerial.Remove(n); presetOwnedBy.Remove(n); }
        }
        private List<string> PresetOwnedOldestFirst()
        {
            List<KeyValuePair<string, ulong>> items = new List<KeyValuePair<string, ulong>>(presetOwnedSerial);
            items.Sort(delegate(KeyValuePair<string, ulong> a, KeyValuePair<string, ulong> b) { return a.Value.CompareTo(b.Value); });
            List<string> names = new List<string>(); foreach (KeyValuePair<string, ulong> item in items) names.Add(item.Key); return names;
        }
        private object[] PresetOwnedArray()
        {
            List<object> list = new List<object>(); foreach (string n in PresetOwnedOldestFirst()) list.Add(D("name", n, "presetId", presetOwnedBy.ContainsKey(n) ? presetOwnedBy[n] : "", "serial", S(presetOwnedSerial[n]))); return list.ToArray();
        }
        private Dictionary<string, object> CapacityObject()
        {
            return D("programCapacity", programCapacity, "programCount", programs.Count, "freeSlots", Math.Max(0, programCapacity - programs.Count), "presetOwnedCount", presetOwnedSerial.Count,
                "userOwnedCount", programs.Count - presetOwnedSerial.Count, "maximumCapacity", MaximumProgramCapacity, "defaultCapacity", DefaultProgramCapacity, "presetOwned", PresetOwnedOldestFirst().ToArray(),
                "scope", "Saved circuit program slots. Preset-owned circuits were created by q.preset runs and may be pruned automatically (oldest first) when a later preset needs a slot; user circuits are never pruned automatically.");
        }
        private string CapacityMessage()
        {
            return "Circuit storage is full (" + programs.Count.ToString(CultureInfo.InvariantCulture) + "/" + programCapacity.ToString(CultureInfo.InvariantCulture) + " saved programs, " + presetOwnedSerial.Count.ToString(CultureInfo.InvariantCulture) +
                " preset-owned). Free a slot with q.circuit delete NAME or q.circuit prune presets, or raise the limit with q.circuit capacity N (maximum " + MaximumProgramCapacity.ToString(CultureInfo.InvariantCulture) + ").";
        }

        private static Dictionary<string, object> Help()
        {
            return D("commands", new string[] {
                "q.circuit new NAME QUBITS", "q.circuit select NAME", "q.circuit list", "q.circuit show",
                "q.circuit add GATE [angles...] [qubits...|all]", "q.circuit input BASIS_INDEX", "q.circuit clear", "q.circuit delete [NAME]",
                "q.circuit preset bell|ghz|qft [QUBITS] [shots N] [basis x|y|z] [run=false] [as FRESH_NAME]", "q.circuit run shots N basis x|y|z", "q.circuit status", "q.circuit result",
                "q.circuit state INDEX", "q.circuit spin Q", "q.circuit cancel", "q.circuit export", "q.circuit memory MiB", "q.circuit capacity [N]", "q.circuit prune presets [KEEP]", "q.circuit help" },
                "storage", "Default capacity 64 saved circuits (configurable 1..1024 with q.circuit capacity N; 65536 instructions total). Circuits created by q.preset runs are tracked as preset-owned; when a preset needs space, its sequence automatically prunes the oldest preset-owned circuits (never user circuits) and records them. q.circuit prune presets [KEEP] does the same explicitly.",
                "gates", TrancheRegister.SupportedGates, "aliases", D("cnot", "cx", "toffoli", "ccx", "fredkin", "cswap", "id", "i", "identity", "i", "u3", "u"),
                "operandOrder", "Angles first, then qubit indices. Controlled gates: control(s), target. RZX: Z operand, X operand. Local all applies to the selected circuit's qubits only.",
                "qftConvention", "|j> maps to sum_k exp(2*pi*i*j*k/2^n)|k>/sqrt(2^n), with final bit-reversal swaps.",
                "presets", "bell uses 2 qubits; ghz/qft default to 29. Presets replace/select bell, ghz-N or qft-N, reset input to 0, and automatically queue 1024 independent shots in basis z. Override shots (1..100000) or basis (x/y/z); run=false defines/selects only, for scripts that set input before running. Options accept key value or key=value. Existing coherent memory limits and cancellation apply.",
                "run", "1..100000 independent replayed shots. One extra premeasurement coherent execution is retained and separately counted. Full histogram survives restart; coherent output is recalculated by the next run.");
        }

        private static Instruction ParseInstruction(string[] words, int start, int n)
        {
            if (words.Length <= start) throw new ArgumentException("Supply a gate name, its angles and its qubits.");
            string name = Alias(words[start]); Dictionary<string, object> spec = GateSpec(name);
            int ac = (int)spec["angleCount"], qc = (int)spec["targetCount"];
            bool all = words.Length == start + ac + 2 && Eq(words[words.Length - 1], "all");
            if (all && !(bool)spec["local"]) throw new ArgumentException("all supports local gates only.");
            if (words.Length != start + 1 + ac + (all ? 1 : qc)) throw new ArgumentException("Incorrect angle/qubit count for " + name + ". Angles precede qubits.");
            double[] angles = new double[ac]; for (int j = 0; j < ac; ++j) angles[j] = Angle(words[start + 1 + j]);
            int[] qubits = new int[all ? 0 : qc]; HashSet<int> distinct = new HashSet<int>();
            for (int j = 0; j < qubits.Length; ++j) { qubits[j] = Integer(words[start + 1 + ac + j], 0, n - 1); if (!distinct.Add(qubits[j])) throw new ArgumentException("Gate qubits must be distinct."); }
            return new Instruction { Name = name, Angles = angles, Qubits = qubits, All = all };
        }
        private static Dictionary<string, object> GateSpec(string name)
        {
            foreach (Dictionary<string, object> spec in TrancheRegister.SupportedGates) if ((string)spec["name"] == name) return spec;
            throw new ArgumentException("Unknown circuit gate. Use q.circuit help.");
        }
        private static string Alias(string name)
        {
            name = name.ToLowerInvariant(); if (name == "cnot") return "cx"; if (name == "toffoli") return "ccx"; if (name == "fredkin") return "cswap"; if (name == "id" || name == "identity") return "i"; if (name == "u3") return "u"; return name;
        }
        private static Instruction G(string name, double[] angles, int[] qubits) { return new Instruction { Name = name, Angles = angles, Qubits = qubits }; }
        private static Dictionary<string, object> InstructionObject(Instruction gate)
        {
            string[] exact = new string[gate.Angles.Length]; for (int j = 0; j < exact.Length; ++j) exact[j] = gate.Angles[j].ToString("R", CultureInfo.InvariantCulture);
            return D("name", gate.Name, "angles", gate.Angles, "anglesExact", exact, "qubits", gate.Qubits, "all", gate.All);
        }
        private static Dictionary<string, object> ProgramObject(Program p, bool includeGates)
        {
            Dictionary<string, object> result = D("name", p.Name, "qubits", p.Qubits, "initialBasis", S(p.InitialBasis), "gateCount", p.Gates.Count, "descriptorHash", ProgramHash(p));
            if (p.InputImport != null) { result["inputImport"] = p.InputImport; result["inputImportMatchesProgram"] = Text(p.InputImport, "appliedProgramHash") == ProgramHash(p); }
            if (includeGates) { List<object> gates = new List<object>(); foreach (Instruction gate in p.Gates) gates.Add(InstructionObject(gate)); result["gates"] = gates.ToArray(); }
            return result;
        }
        private static string ProgramHash(Program p)
        {
            StringBuilder text = new StringBuilder("SPINDOCTOR/CIRCUIT/1|"); text.Append(p.Name).Append('|').Append(p.Qubits).Append('|').Append(S(p.InitialBasis));
            foreach (Instruction gate in p.Gates)
            {
                text.Append('|').Append(gate.Name).Append('|').Append(gate.All ? "all" : "selected");
                foreach (double angle in gate.Angles) text.Append('|').Append(angle.ToString("R", CultureInfo.InvariantCulture));
                foreach (int q in gate.Qubits) text.Append('|').Append(q.ToString(CultureInfo.InvariantCulture));
            }
            return Hash(text.ToString());
        }
        private static Program Clone(Program p)
        {
            Program copy = new Program { Name = p.Name, Qubits = p.Qubits, InitialBasis = p.InitialBasis, InputImport = p.InputImport };
            foreach (Instruction gate in p.Gates) copy.Gates.Add(new Instruction { Name = gate.Name, Angles = (double[])gate.Angles.Clone(), Qubits = (int[])gate.Qubits.Clone(), All = gate.All });
            return copy;
        }
        private Program Current() { Program p; if (selected.Length == 0 || !programs.TryGetValue(selected, out p)) throw new InvalidOperationException("Create or select a circuit first."); return p; }
        private string[] ProgramNames() { List<string> names = new List<string>(programs.Keys); names.Sort(StringComparer.OrdinalIgnoreCase); return names.ToArray(); }
        private object[] ProgramSummaries() { List<object> summaries = new List<object>(); foreach (string name in ProgramNames()) summaries.Add(D("name", name, "qubits", programs[name].Qubits, "descriptorHash", ProgramHash(programs[name]))); return summaries.ToArray(); }
        private int InstructionCount() { int count = 0; foreach (Program p in programs.Values) count += p.Gates.Count; return count; }
        private void ReleaseCoherent()
        {
            if (coherentOutput != null) coherentOutput.Dispose(); coherentOutput = null; coherentBaseline = null; coherentSnapshot = null; coherentHash = "";
        }
        private void CommitProgramMutation(Action change)
        {
            Dictionary<string, Program> previous = new Dictionary<string, Program>(StringComparer.OrdinalIgnoreCase);
            foreach (Program p in programs.Values) previous.Add(p.Name, Clone(p));
            string previousSelected = selected;
            int previousCapacity = programCapacity; Dictionary<string, ulong> previousSerial = new Dictionary<string, ulong>(presetOwnedSerial, StringComparer.OrdinalIgnoreCase); Dictionary<string, string> previousBy = new Dictionary<string, string>(presetOwnedBy, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, object> previousPreview = job == null ? null : job.SpinPreview;
            try { change(); DropStaleOwnership(); if (job != null) job.SpinPreview = null; SaveLocked(); }
            catch
            {
                programs.Clear(); foreach (KeyValuePair<string, Program> item in previous) programs.Add(item.Key, item.Value);
                selected = previousSelected; programCapacity = previousCapacity;
                if (job != null) job.SpinPreview = previousPreview;
                presetOwnedSerial.Clear(); foreach (KeyValuePair<string, ulong> item in previousSerial) presetOwnedSerial.Add(item.Key, item.Value);
                presetOwnedBy.Clear(); foreach (KeyValuePair<string, string> item in previousBy) presetOwnedBy.Add(item.Key, item.Value); throw;
            }
            ReleaseCoherent();
        }

        private void SaveLocked()
        {
            List<object> savedPrograms = new List<object>(); foreach (Program p in programs.Values) savedPrograms.Add(ProgramObject(p, true));
            Dictionary<string, object> state = D("schema", "SPINDOCTOR/CIRCUITS/1", "seed", seed, "counter", S(counter), "memoryLimitBytes", S(memoryLimit),
                "selected", selected, "programs", savedPrograms.ToArray(), "lifetime", CountsObject(lifetime), "programCapacity", programCapacity, "ownershipSerial", S(ownershipSerial), "presetOwnedPrograms", PresetOwnedArray());
            if (job != null)
            {
                Dictionary<string, object> savedJob = JobObject(job); savedJob["program"] = ProgramObject(job.Program, true);
                savedJob["histogramFull"] = job.Histogram; savedJob["coherentOutputStatus"] = job.CoherentSnapshot; state["job"] = savedJob;
            }
            string data = json.Serialize(state), temporary = metadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, data, new UTF8Encoding(false)); if (File.Exists(metadataPath)) File.Replace(temporary, metadataPath, null); else File.Move(temporary, metadataPath); }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
        private void Load()
        {
            if (new FileInfo(metadataPath).Length > json.MaxJsonLength) throw new InvalidDataException("Circuit metadata exceeds its 32 MiB bound.");
            Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(metadataPath, Encoding.UTF8));
            if (Text(state, "schema") != "SPINDOCTOR/CIRCUITS/1" || Text(state, "seed") != seed) throw new InvalidDataException("Circuit checkpoint schema/seed mismatch.");
            counter = Unsigned(Text(state, "counter")); memoryLimit = Unsigned(Text(state, "memoryLimitBytes"));
            if (memoryLimit < 2UL * 1024 * 1024) throw new InvalidDataException("Invalid circuit memory budget.");
            selected = Text(state, "selected");
            object savedCapacity; if (state.TryGetValue("programCapacity", out savedCapacity)) { int c; if (!Int32.TryParse(Convert.ToString(savedCapacity, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out c) || c < 1 || c > MaximumProgramCapacity) throw new InvalidDataException("Invalid saved circuit capacity."); programCapacity = c; }
            foreach (object value in Sequence(state["programs"]))
            {
                Program p = ReadProgram((Dictionary<string, object>)value); if (programs.ContainsKey(p.Name) || programs.Count >= MaximumProgramCapacity) throw new InvalidDataException("Invalid saved circuit names/count."); programs.Add(p.Name, p);
            }
            if (programs.Count > programCapacity) programCapacity = programs.Count;
            object savedSerial; if (state.TryGetValue("ownershipSerial", out savedSerial)) ownershipSerial = Unsigned(Convert.ToString(savedSerial, CultureInfo.InvariantCulture));
            object savedOwned; if (state.TryGetValue("presetOwnedPrograms", out savedOwned)) foreach (object item in Sequence(savedOwned)) { Dictionary<string, object> o = item as Dictionary<string, object>; if (o == null) continue; string n = Text(o, "name"); if (!programs.ContainsKey(n)) continue; presetOwnedSerial[n] = Unsigned(Text(o, "serial")); presetOwnedBy[n] = Text(o, "presetId"); }
            if (InstructionCount() > 65536 || (selected.Length != 0 && !programs.ContainsKey(selected))) throw new InvalidDataException("Invalid saved circuit selection/instruction count.");
            ReadCounts((Dictionary<string, object>)state["lifetime"], lifetime);
            object saved;
            if (state.TryGetValue("job", out saved))
            {
                Dictionary<string, object> data = (Dictionary<string, object>)saved;
                Program p = ReadProgram((Dictionary<string, object>)data["program"]);
                job = new Work { Id = Text(data, "id"), Program = p, ProgramHash = Text(data, "programHash"), Basis = Basis(Text(data, "basis")),
                    Active = false, Phase = Convert.ToBoolean(data["active"], CultureInfo.InvariantCulture) ? "interrupted" : Text(data, "phase"),
                    RequestedShots = Integer(Text(data, "requestedShots"), 1, 100000), CompletedShots = Integer(Text(data, "completedShots"), 0, 100000),
                    MeasuredQubits = Integer(Text(data, "measuredQubits"), 0, 2900000), CounterStart = Unsigned(Text(data, "counterStart")), CounterEnd = Unsigned(Text(data, "counterEnd")),
                    MemoryLimit = memoryLimit, PreviousElapsedMs = Double.Parse(Text(data, "elapsedMs"), CultureInfo.InvariantCulture), Error = Text(data, "error"), CheckpointError = Text(data, "checkpointError") };
                if (job.ProgramHash != ProgramHash(p) || job.CompletedShots > job.RequestedShots || job.CounterStart > job.CounterEnd || job.CounterEnd != counter || !QuantumMath.IsFinite(job.PreviousElapsedMs) || job.PreviousElapsedMs < 0)
                    throw new InvalidDataException("Invalid circuit job descriptor/counts.");
                ReadCounts((Dictionary<string, object>)data["counts"], job.Counts);
                foreach(string key in CountKeys) if(job.Counts[key]>lifetime[key])throw new InvalidDataException("Circuit job work exceeds lifetime accounting.");
                if (job.Counts["uniformDraws"] != job.CounterEnd - job.CounterStart || job.Counts["completedShots"] != job.CompletedShots || job.Counts["measuredQubits"] != job.MeasuredQubits ||
                    job.MeasuredQubits < job.CompletedShots * p.Qubits || job.MeasuredQubits > (job.CompletedShots + 1) * p.Qubits ||
                    job.Counts["uniformDraws"] < job.MeasuredQubits || job.Counts["uniformDraws"] > job.MeasuredQubits + 1)
                    throw new InvalidDataException("Circuit shot/draw accounting does not match the saved job.");
                Dictionary<string, object> histogram = (Dictionary<string, object>)data["histogramFull"]; ulong total = 0;
                foreach (KeyValuePair<string, object> row in histogram)
                {
                    if (row.Key.Length != p.Qubits) throw new InvalidDataException("Invalid histogram bit width.");
                    foreach (char ch in row.Key) if (ch != '0' && ch != '1') throw new InvalidDataException("Invalid histogram bitstring.");
                    ulong count = Unsigned(Convert.ToString(row.Value, CultureInfo.InvariantCulture)); if (count == 0 || count > 100000 || total > 100000 - count) throw new InvalidDataException("Invalid histogram count.");
                    job.Histogram.Add(row.Key, count); total += count;
                }
                if (total != (ulong)job.CompletedShots) throw new InvalidDataException("Histogram does not match completed shots.");
                object status; if (data.TryGetValue("coherentOutputStatus", out status)) job.CoherentSnapshot = status as Dictionary<string, object>;
                object preview; if (data.TryGetValue("coherentSpinPreview", out preview) && preview != null) job.SpinPreview = ReadSpinPreview(preview, job);
            }
        }
        private static Dictionary<string, object> ReadSpinPreview(object value, Work target)
        {
            Dictionary<string, object> data = value as Dictionary<string, object>;
            DateTime captured;
            if (data == null || Text(data, "schema") != SpinPreviewSchema || Text(data, "programHash") != target.ProgramHash ||
                !DateTime.TryParseExact(Text(data, "capturedUtc"), "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out captured) || captured.Kind != DateTimeKind.Utc ||
                !SavedBoolean(data, "premeasurement") || SavedBoolean(data, "independentVerification") ||
                Integer(Text(data, "qubitLimit"), 4, 4) != SpinPreviewQubitLimit || Integer(Text(data, "maximumFactorEntries"), 65536, 65536) != SpinPreviewFactorLimit ||
                Integer(Text(data, "selectedQubits"), 1, 29) != target.Program.Qubits || Text(data, "scope") != SpinPreviewScope || Text(data, "verificationScope") != SpinPreviewVerificationScope)
                throw new InvalidDataException("Invalid saved premeasurement spin-preview source or scope.");
            int considered = Math.Min(SpinPreviewQubitLimit, target.Program.Qubits), rowCount = 0, omittedCount = 0;
            HashSet<int> qubits = new HashSet<int>();
            foreach (object item in Sequence(data["rows"]))
            {
                if (++rowCount > considered) throw new InvalidDataException("Saved spin-preview row limit exceeded.");
                Dictionary<string, object> row = item as Dictionary<string, object>;
                if (row == null) throw new InvalidDataException("Invalid saved spin-preview row.");
                int q = Integer(Text(row, "qubit"), 0, considered - 1);
                if (!qubits.Add(q) || Unsigned(Text(row, "factorEntryCount")) < 1 || Unsigned(Text(row, "factorEntryCount")) > SpinPreviewFactorLimit)
                    throw new InvalidDataException("Invalid saved spin-preview qubit or traversal size.");
                List<double> spin = new List<double>();
                foreach (object component in Sequence(row["bloch"]))
                {
                    double number;
                    if (spin.Count == 3 || !Double.TryParse(Convert.ToString(component, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number) || !QuantumMath.IsFinite(number))
                        throw new InvalidDataException("Invalid saved spin-preview component.");
                    spin.Add(number);
                }
                Dictionary<string, object> expected = SpinDoctorCalculators.BlochStateMetadata(spin.ToArray(), q);
                foreach (string key in new string[] { "blochLengthSquared", "purity", "directionTolerance" })
                {
                    double number;
                    if (!Double.TryParse(Text(row, key), NumberStyles.Float, CultureInfo.InvariantCulture, out number) || !QuantumMath.IsFinite(number) || (key == "directionTolerance" ? number != (double)expected[key] : Math.Abs(number - (double)expected[key]) > 2e-9))
                        throw new InvalidDataException("Saved spin-preview derived value does not match its vector.");
                }
                foreach (string key in new string[] { "pure", "mixed", "exactlyZeroBloch", "directionDefined", "directionResolvedAtTolerance", "premeasurement" })
                    if (SavedBoolean(row, key) != (bool)expected[key]) throw new InvalidDataException("Saved spin-preview state character does not match its vector.");
                foreach (string key in new string[] { "stateCharacter", "stateSource", "spinScope" })
                    if (Text(row, key) != (string)expected[key]) throw new InvalidDataException("Invalid saved spin-preview state source.");
            }
            foreach (object item in Sequence(data["omissions"]))
            {
                if (++omittedCount > considered) throw new InvalidDataException("Saved spin-preview omission limit exceeded.");
                Dictionary<string, object> row = item as Dictionary<string, object>;
                if (row == null || !qubits.Add(Integer(Text(row, "qubit"), 0, considered - 1)) || Unsigned(Text(row, "factorEntryCount")) <= SpinPreviewFactorLimit || Text(row, "reason") != SpinPreviewOmission)
                    throw new InvalidDataException("Invalid saved spin-preview omission.");
            }
            if (rowCount + omittedCount != considered || Integer(Text(data, "returnedQubits"), 0, considered) != rowCount ||
                Integer(Text(data, "omittedQubits"), 0, target.Program.Qubits) != target.Program.Qubits - rowCount ||
                Integer(Text(data, "unrequestedQubits"), 0, target.Program.Qubits) != target.Program.Qubits - considered ||
                target.Counts["queryCount"] < rowCount || target.Counts["coherentOutputExecutions"] < 1)
                throw new InvalidDataException("Saved spin-preview coverage does not match its actual query accounting.");
            return data;
        }
        private static bool SavedBoolean(Dictionary<string, object> data, string key)
        {
            object value; if (!data.TryGetValue(key, out value) || !(value is bool)) throw new InvalidDataException("Invalid saved boolean " + key + "."); return (bool)value;
        }
        private static Program ReadProgram(Dictionary<string, object> data)
        {
            Program p = new Program { Name = Name(Text(data, "name")), Qubits = Integer(Text(data, "qubits"), 1, 29), InitialBasis = Unsigned(Text(data, "initialBasis")) };
            if (p.InitialBasis >= (1UL << p.Qubits)) throw new InvalidDataException("Invalid initial circuit basis index.");
            foreach (object value in Sequence(data["gates"]))
            {
                if (p.Gates.Count >= 8192) throw new InvalidDataException("Saved circuit instruction limit exceeded.");
                Dictionary<string, object> row = (Dictionary<string, object>)value; List<string> words = new List<string>(); words.Add(Text(row, "name"));
                foreach (object angle in Sequence(row["anglesExact"])) words.Add(Convert.ToString(angle, CultureInfo.InvariantCulture));
                if (Convert.ToBoolean(row["all"], CultureInfo.InvariantCulture)) words.Add("all");
                else foreach (object q in Sequence(row["qubits"])) words.Add(Convert.ToString(q, CultureInfo.InvariantCulture));
                p.Gates.Add(ParseInstruction(words.ToArray(), 0, p.Qubits));
            }
            if (Text(data, "descriptorHash") != ProgramHash(p)) throw new InvalidDataException("Circuit program descriptor hash mismatch.");
            object provenance; if (data.TryGetValue("inputImport", out provenance))
            {
                p.InputImport = provenance as Dictionary<string, object>;
                if (p.InputImport == null || Text(p.InputImport, "appliedProgramHash").Length !=64) throw new InvalidDataException("Invalid input import provenance.");
            }
            return p;
        }
        private static void ReadCounts(Dictionary<string, object> data, Dictionary<string, BigInteger> counts)
        {
            foreach (string key in CountKeys) { BigInteger value; if (!BigInteger.TryParse(Text(data, key), NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 0) throw new InvalidDataException("Invalid circuit accounting counter."); counts[key] = value; }
        }
        private static Dictionary<string, BigInteger> BackendBaseline() { return new Dictionary<string, BigInteger> { { "actualAmplitudeWrites", 0 }, { "actualTensorProducts", 0 }, { "queryCount", 0 } }; }
        private static Dictionary<string, BigInteger> NewCounts() { Dictionary<string, BigInteger> result = new Dictionary<string, BigInteger>(); foreach (string key in CountKeys) result[key] = 0; return result; }
        private static Dictionary<string, object> CountsObject(Dictionary<string, BigInteger> counts) { Dictionary<string, object> result = new Dictionary<string, object>(); foreach (KeyValuePair<string, BigInteger> count in counts) result[count.Key] = S(count.Value); return result; }
        private void LogError(Work target, Exception error)
        {
            try { File.AppendAllText(errorsPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\t" + target.Id + "\t" + error.GetType().Name + "\t" + error.Message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        private static IEnumerable Sequence(object value) { IEnumerable sequence = value as IEnumerable; if (sequence == null || value is string) throw new InvalidDataException("Expected checkpoint array."); return sequence; }
        private static string Text(Dictionary<string, object> data, string key) { object value; if (!data.TryGetValue(key, out value) || value == null) throw new InvalidDataException("Missing circuit field " + key + "."); return Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static string Name(string name)
        {
            if (String.IsNullOrEmpty(name) || name.Length > 32 || !((name[0] >= 'A' && name[0] <= 'Z') || (name[0] >= 'a' && name[0] <= 'z'))) throw new ArgumentException("Circuit names begin with an ASCII letter, use letters/digits/-/_, and have at most 32 characters.");
            foreach (char ch in name) if (!(ch >= 'A' && ch <= 'Z') && !(ch >= 'a' && ch <= 'z') && !(ch >= '0' && ch <= '9') && ch != '-' && ch != '_') throw new ArgumentException("Invalid circuit name."); return name;
        }
        private static int Integer(string value, int minimum, int maximum) { BigInteger parsed; if (String.IsNullOrEmpty(value)||!System.Text.RegularExpressions.Regex.IsMatch(value,@"^-?[0-9]+$")) throw new ArgumentException("Expected an integer value."); if(!BigInteger.TryParse(value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out parsed)||parsed<minimum||parsed>maximum)throw new ArgumentOutOfRangeException("number", "Integer is outside the supported range.");return (int)parsed; }
        private static ulong Unsigned(string value) { ulong n; if (!UInt64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) throw new ArgumentException("Expected an unsigned decimal integer."); return n; }
        private static double Angle(string value) { double n; if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || !QuantumMath.IsFinite(n) || Math.Abs(n) > 1e12) throw new ArgumentException("Angles must be finite within +/-1e12 radians."); return n; }
        private static string Basis(string value) { value = value.ToLowerInvariant(); if (value != "x" && value != "y" && value != "z") throw new ArgumentException("Basis must be x, y or z."); return value; }
        private static void Exact(string[] words, int count) { if (words.Length != count) throw new ArgumentException("Incorrect command argument count. Use q.circuit help."); }
        private static bool Eq(string a, string b) { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static string Bits(ulong value, int width) { char[] chars = new char[width]; for (int q = 0; q < width; ++q) chars[width - 1 - q] = (value & (1UL << q)) == 0 ? '0' : '1'; return new String(chars); }
        private static string S(ulong value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string S(BigInteger value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string Hash(string text) { using (SHA256 hash = SHA256.Create()) { byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text)); StringBuilder result = new StringBuilder(64); foreach (byte value in bytes) result.Append(value.ToString("x2", CultureInfo.InvariantCulture)); return result.ToString(); } }
        private static Dictionary<string, object> D(params object[] values) { Dictionary<string, object> result = new Dictionary<string, object>(); for (int j = 0; j < values.Length; j += 2) result.Add((string)values[j], values[j + 1]); return result; }
        private void Alive() { if (disposed) throw new ObjectDisposedException("CircuitTerminal"); }
        public void Dispose()
        {
            Thread pending;
            lock (sync) { if (disposed) return; disposed = true; if (job != null && job.Active) job.Cancel = true; pending = thread; }
            if (pending != null && pending.IsAlive && Thread.CurrentThread != pending) pending.Join();
            lock (sync) { try { SaveLocked(); } finally { ReleaseCoherent(); } }
        }
    }
}
