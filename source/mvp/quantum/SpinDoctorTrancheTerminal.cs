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
    // One selected register. Its coherent joint state never earns family-wide credit.
    public sealed class SpinDoctorTrancheTerminal : IDisposable
    {
        private readonly object sync = new object();
        private readonly string metaPath, statePath, seed;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private TrancheRegister register;
        private Dictionary<string, object> snapshot = new Dictionary<string, object>();
        private readonly List<object> journal = new List<object>();
        private readonly Dictionary<string, BigInteger> lifetime = new Dictionary<string, BigInteger>();
        private readonly Dictionary<string, BigInteger> baseline = new Dictionary<string, BigInteger>();
        private static readonly string[] ActualKeys = { "actualAmplitudeWrites", "actualTensorProducts", "measurementShots", "queryCount" };
        private string checkpointHash = "";
        private Dictionary<string, object> binding;
        private double[][] initialSpinors;
        private int qubits;
        private ulong memoryLimit = 512UL * 1024 * 1024, randomCounter;
        private BigInteger preparedPoints = BigInteger.Zero, committedGates = BigInteger.Zero, checkpointLoads = BigInteger.Zero;
        private bool suspended, checkpointAvailable, disposed;
        private Thread thread;
        private Work job;
        private sealed class Work
        {
            public string Id = Guid.NewGuid().ToString("N");
            public readonly Stopwatch Watch = Stopwatch.StartNew();
            public double PreviousElapsedMs;
            public volatile bool Cancel;
            public bool Active = true;
            public ulong Completed, Total;
            public string Phase = "running", Operation, Error = "", CheckpointError = "";
            public object Result;
        }

        public SpinDoctorTrancheTerminal(string workspace, string measurementSeed)
        {
            if (String.IsNullOrEmpty(workspace) || String.IsNullOrEmpty(measurementSeed)) throw new ArgumentException("Tranche workspace and persistent measurement seed are required.");
            metaPath = Path.Combine(workspace, "spindoctor-tranche.json");
            statePath = Path.Combine(workspace, "spindoctor-tranche-state.bin");
            seed = Hash(measurementSeed + "|SpinDoctor|selected-tranche-measurements");
            json.MaxJsonLength = 16777216; json.RecursionLimit = 80;
            foreach (string key in ActualKeys) lifetime[key] = BigInteger.Zero;
            if (File.Exists(metaPath)) LoadMetadata();
        }

        public Dictionary<string, object> GetStatus()
        {
            lock (sync)
            {
                Dictionary<string, object> value = new Dictionary<string, object>(snapshot);
                value["product"] = "SpinDoctor"; value["prepared"] = qubits > 0;
                value["suspended"] = suspended; value["qubits"] = qubits;
                value["basisStateCount"] = qubits == 0 ? "0" : S(1UL << qubits);
                value["memoryLimitBytes"] = S(memoryLimit);
                value["worstDenseStateBytes"] = qubits == 0 ? "0" : S(16UL << qubits);
                if (register == null)
                {
                    value["allocatedDenseStateBytes"] = "0"; value["estimatedSparseStateBytes"] = "0";
                    value["allocatedStateBytesEstimate"] = "0";
                }
                value["checkpointAvailable"] = checkpointAvailable;
                value["checkpointStateRestored"] = register != null;
                value["seed"] = seed; value["counter"] = S(randomCounter);
                value["actualPreparedSingleQubits"] = S(preparedPoints);
                value["committedGateApplications"] = S(committedGates);
                Dictionary<string, object> actual = new Dictionary<string, object>();
                foreach (string key in ActualKeys) actual[key] = S(lifetime[key]);
                actual["actualCheckpointAmplitudeLoads"] = S(checkpointLoads);
                value["lifetimeActualWork"] = actual;
                value["accountingScope"] = "Lifetime actual work survives release, reset and restart, including cancelled gate work and replay calculations. Register counters describe its current backend history; replay does not add committed logical gate credit. Amplitude writes and tensor products are source-level work units, not FLOPS or physical quantum operations.";
                value["modelBinding"] = binding;
                value["job"] = JobStatus();
                value["scope"] = "One selected coherent register of up to 29 qubits. Tensor factors and joint-state blocks retain entanglement. Logical gate calls and actual amplitude work are separate from the procedural family and from physical quantum gates.";
                value["supportedGates"] = TrancheRegister.SupportedGates;
                return value;
            }
        }

        public object Execute(string[] words, ulong m, ulong l, double t, double[] matrix, string descriptor)
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("SpinDoctorTrancheTerminal");
                if (words.Length < 2) return Help();
                string action = words[1].ToLowerInvariant();
                if (action == "status") { Exact(words, 2); return GetStatus(); }
                if (action == "help") { Exact(words, 2); return Help(); }
                if (action == "cancel")
                {
                    Exact(words, 2); bool active = job != null && job.Active;
                    if (active) job.Cancel = true;
                    return D("cancelRequested", active, "job", JobStatus());
                }
                if (job != null && job.Active) throw new InvalidOperationException("A tranche operation is active. Use q.tranche status or q.tranche cancel.");
                if (action == "prepare")
                {
                    Exact(words, 4); Need(words[3], "zero");
                    int next = Int(words[2]); if (next < 1 || next > 29) throw new ArgumentException("A tranche contains 1-29 qubits.");
                    Replace(new TrancheRegister(next), next, null, null);
                    Save(); return D("prepared", true, "qubits", next, "initialState", "zero", "note", "Selected register prepared in |0...0>. No family-wide gate credit is issued.");
                }
                if (action == "from")
                {
                    Exact(words, 3); ulong id = UInt(words[2]), total = QuantumMath.ValidateGrid(m, l);
                    ulong count = (total + 28) / 29;
                    if (id >= count) throw new ArgumentException("Tranche index must be less than " + S(count) + ".");
                    ulong start = checked(id * 29), length = Math.Min(29UL, total - start);
                    double[][] spinors = new double[(int)length][];
                    for (int i = 0; i < spinors.Length; i++)
                    {
                        double[] v = QuantumMath.State(start + (ulong)i, m, l, t, matrix);
                        spinors[i] = new double[] { v[0], v[1], v[2], v[3] };
                    }
                    Dictionary<string, object> nextBinding = D("trancheIndex", S(id), "firstFamilyAddress", S(start), "lastFamilyAddress", S(start + length - 1), "familyDescriptorHash", descriptor, "familyStateCount", S(total), "totalTranches", S(count), "partialFinalTranche", length < 29);
                    Replace(new TrancheRegister((int)length, spinors), (int)length, spinors, nextBinding);
                    preparedPoints += length; Save();
                    return D("prepared", true, "qubits", (int)length, "actualPreparedSingleQubits", S(length), "modelBinding", binding, "note", "Selected consecutive family states form an initial product register. Subsequent tranche gates operate on this snapshot independently of the family.");
                }
                if (action == "release")
                {
                    Exact(words, 2); if (register != null) register.Dispose(); register = null;
                    qubits = 0; initialSpinors = null; binding = null; snapshot.Clear(); baseline.Clear(); journal.Clear(); suspended = false; checkpointAvailable = false;
                    Save(); return D("released", true, "seedPreserved", true, "counter", S(randomCounter));
                }
                if (action == "memory")
                {
                    Exact(words, 3); ulong mib = UInt(words[2]);
                    if (mib == 0 || mib > UInt64.MaxValue / (1024UL * 1024)) throw new ArgumentException("Memory budget must be a positive whole number of MiB.");
                    ulong bytes = mib * 1024UL * 1024;
                    if (register != null) register.SetMemoryLimit(bytes);
                    memoryLimit = bytes; UpdateSnapshot(); Save();
                    return D("memoryLimitBytes", S(memoryLimit), "note", "This is a storage budget, not a qubit-operation count or measured throughput. The backend refuses an allocation beyond the budget.");
                }
                if (action == "resume")
                {
                    Exact(words, 2); if (qubits == 0) throw new InvalidOperationException("No saved tranche is selected.");
                    if (register != null) return D("resumed", true, "alreadyLive", true, "status", GetStatus());
                    return Start("resume", delegate(Work work) { Restore(work); return D("resumed", true, "status", GetStatus()); });
                }
                RequireRegister();
                if (action == "gate")
                {
                    if (journal.Count >= 65536) throw new InvalidOperationException("Tranche replay journal reached 65,536 operations. Reset or prepare a register before adding another gate.");
                    if (words.Length < 4) throw new ArgumentException("Use q.tranche gate NAME [angles] TARGETS or all.");
                    string name = Alias(words[2]); Dictionary<string, object> spec = GateSpec(name);
                    int na = Convert.ToInt32(spec["angleCount"], CultureInfo.InvariantCulture), nt = Convert.ToInt32(spec["targetCount"], CultureInfo.InvariantCulture);
                    bool all = words[words.Length - 1].Equals("all", StringComparison.OrdinalIgnoreCase);
                    if (all && nt != 1) throw new ArgumentException("Controlled and multi-qubit gates require explicit distinct qubit operands.");
                    if (words.Length != 3 + na + (all ? 1 : nt)) throw new ArgumentException("Incorrect operands. " + name.ToUpperInvariant() + " takes " + na + " angles and " + nt + " qubit operands (angles first).");
                    double[] angles = new double[na]; for (int i = 0; i < na; i++) angles[i] = Number(words[3 + i]);
                    int[] targets = new int[all ? 0 : nt]; HashSet<int> unique = new HashSet<int>();
                    for (int i = 0; i < targets.Length; i++) { targets[i] = Qubit(words[3 + na + i]); if (!unique.Add(targets[i])) throw new ArgumentException("Qubit operands must be distinct."); }
                    object record = D("operation", "gate", "name", name, "anglesExact", ExactNumbers(angles), "targets", targets, "all", all);
                    return Start("gate " + name, delegate(Work work)
                    {
                        object result = register.Gate(name, angles, targets, all, delegate() { return work.Cancel; }, delegate(ulong done, ulong planned) { lock (sync) { work.Completed = done; work.Total = planned; } });
                        lock (sync) { journal.Add(record); committedGates += all ? qubits : 1; }
                        return result;
                    });
                }
                if (action == "state") { Exact(words, 3); object result = register.State(UInt(words[2])); UpdateSnapshot(); SaveMetadata(); return result; }
                if (action == "spin") { Exact(words, 3); int q = Qubit(words[2]); double[] spin = register.Spin(q); UpdateSnapshot(); SaveMetadata(); return D("qubit", q, "spin", D("x", spin[0], "y", spin[1], "z", spin[2]), "scope", "Reduced single-qubit spin expectation in the selected coherent register; entangled qubits can have a mixed reduced state."); }
                if (action == "measure")
                {
                    if (words.Length != 7 && words.Length != 8) throw new ArgumentException("Use q.tranche measure Q basis x|y|z shots N [collapse].");
                    int q = Qubit(words[2]); Need(words[3], "basis"); Need(words[5], "shots");
                    string basis = words[4].ToLowerInvariant(); int shots = Int(words[6]); bool collapse = words.Length == 8;
                    if (collapse) Need(words[7], "collapse");
                    if (collapse && journal.Count >= 65536) throw new InvalidOperationException("Tranche replay journal reached 65,536 operations. Reset or prepare a register before another collapse.");
                    if (shots < 1 || shots > 100000 || (collapse && shots != 1)) throw new ArgumentException("Use 1-100000 independent shots, or exactly one shot with collapse.");
                    if (UInt64.MaxValue - randomCounter < (ulong)shots) throw new InvalidOperationException("Tranche measurement counter exhausted.");
                    ulong start = randomCounter;
                    object result;
                    try
                    {
                        result = register.Measure(q, basis, shots, delegate()
                        {
                            // A failed transactional collapse can still consume a
                            // draw. Never reuse that draw on the next measurement.
                            ulong cursor = randomCounter++;
                            using (SHA256 hash = SHA256.Create())
                            {
                                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(seed + "|tranche-measurement|" + S(cursor)));
                                ulong n = 0; for (int j = 0; j < 8; ++j) n = (n << 8) | bytes[j];
                                return (n >> 11) * (1.0 / 9007199254740992.0);
                            }
                        }, collapse);
                        Dictionary<string, object> measured = (Dictionary<string, object>)result;
                        measured["seed"] = seed; measured["counterStart"] = S(start); measured["counterEnd"] = S(randomCounter);
                    }
                    catch { UpdateSnapshot(); SaveMetadata(); throw; }
                    if (collapse) journal.Add(D("operation", "collapse", "qubit", q, "basis", basis, "counter", S(start)));
                    UpdateSnapshot(); if (collapse) Save(); else SaveMetadata(); return result;
                }
                if (action == "reset")
                {
                    Exact(words, 2); register.Reset(); initialSpinors = null; binding = null; journal.Clear();
                    UpdateSnapshot(); Save(); return D("reset", true, "qubits", qubits, "seedPreserved", true, "counter", S(randomCounter));
                }
                if (action == "export")
                {
                    Exact(words, 2); object result = register.Export(); UpdateSnapshot(); SaveMetadata();
                    return D("register", result, "modelBinding", binding, "seed", seed, "counter", S(randomCounter), "status", GetStatus(), "replayJournal", journal.ToArray());
                }
                if (action == "save")
                {
                    if (words.Length != 2 && words.Length != 3) throw new ArgumentException("Use q.tranche save [dense].");
                    bool dense = words.Length == 3; if (dense) Need(words[2], "dense");
                    Checkpoint(dense); SaveMetadata(); return D("saved", true, "stateFile", Path.GetFileName(statePath), "largeDenseCheckpointAllowed", dense);
                }
                throw new ArgumentException("Unknown tranche command. Use q.tranche help.");
            }
        }

        private object Start(string operation, Func<Work, object> calculate)
        {
            job = new Work { Operation = operation }; Work next = job;
            thread = new Thread(delegate()
            {
                try
                {
                    next.Result = calculate(next);
                    lock (sync) { next.Phase = "completed"; suspended = false; UpdateSnapshot(); }
                }
                catch (OperationCanceledException)
                {
                    lock (sync) { next.Phase = "cancelled"; UpdateSnapshot(); }
                }
                catch (Exception ex)
                {
                    lock (sync) { next.Phase = "failed"; next.Error = ex.GetType().Name + ": " + ex.Message; UpdateSnapshot(); }
                }
                finally
                {
                    lock (sync)
                    {
                        next.Active = false; next.Watch.Stop();
                        try { if (next.Phase == "completed") Save(); else SaveMetadata(); }
                        catch (Exception ex) { next.CheckpointError = ex.GetType().Name + ": " + ex.Message; }
                    }
                }
            });
            thread.IsBackground = true;
            try { SaveMetadata(); }
            catch (Exception ex) { next.Active = false; next.Phase = "failed"; next.Error = "Could not checkpoint queued operation: " + ex.Message; next.Watch.Stop(); throw; }
            try { thread.Start(); }
            catch (Exception ex) { next.Active = false; next.Phase = "failed"; next.Error = "Could not start tranche worker: " + ex.Message; next.Watch.Stop(); SaveMetadata(); throw; }
            return D("queued", true, "job", JobStatus(), "note", "Selected-register operation is running. Poll q.tranche status; gate cancellation rolls back the affected state blocks.");
        }

        private void Replace(TrancheRegister next, int n, double[][] spinors, Dictionary<string, object> source)
        {
            try { next.SetMemoryLimit(memoryLimit); }
            catch { next.Dispose(); throw; }
            if (register != null) register.Dispose(); register = next; qubits = n;
            initialSpinors = spinors; binding = source; journal.Clear(); suspended = false; checkpointAvailable = false;
            baseline.Clear();
            UpdateSnapshot();
        }

        private void Restore(Work work)
        {
            TrancheRegister next = null;
            try
            {
                if (checkpointAvailable && File.Exists(statePath) && FileHash(statePath) == checkpointHash)
                {
                    next = TrancheRegister.Load(statePath, memoryLimit);
                    lock (sync) { checkpointLoads += next.RestoredAmplitudeLoads; SetBaseline(next.Status()); }
                }
                else
                {
                    lock (sync) { checkpointAvailable = false; baseline.Clear(); }
                    next = initialSpinors == null ? new TrancheRegister(qubits) : new TrancheRegister(qubits, initialSpinors);
                    next.SetMemoryLimit(memoryLimit); ulong completed = 0;
                    foreach (object raw in journal)
                    {
                        if (work.Cancel) throw new OperationCanceledException();
                        Dictionary<string, object> entry = (Dictionary<string, object>)raw;
                        string op = (string)entry["operation"];
                        if (op == "gate") next.Gate((string)entry["name"], ReadNumbers(entry["anglesExact"]), ReadInts(entry["targets"]), (bool)entry["all"], delegate() { return work.Cancel; }, null);
                        else if (op == "collapse") next.Measure(Convert.ToInt32(entry["qubit"], CultureInfo.InvariantCulture), (string)entry["basis"], 1, seed, UInt((string)entry["counter"]), true);
                        else throw new InvalidDataException("Unknown saved tranche replay operation.");
                        completed++; lock (sync) { work.Completed = completed; work.Total = (ulong)journal.Count; }
                    }
                }
                if (work.Cancel) throw new OperationCanceledException();
                lock (sync) { register = next; next = null; suspended = false; UpdateSnapshot(); }
            }
            finally { if (next != null) { lock (sync) { Accumulate(next.Status()); baseline.Clear(); } next.Dispose(); } }
        }

        private void RequireRegister()
        {
            if (qubits == 0) throw new InvalidOperationException("Prepare a register with q.tranche prepare 29 zero, or select q.tranche from INDEX.");
            if (register == null) throw new InvalidOperationException("The saved tranche is suspended. Use q.tranche resume to restore its coherent state.");
        }
        private int Qubit(string text) { int q = Int(text); if (q < 0 || q >= qubits) throw new ArgumentException("Qubit operand must be in 0.." + (qubits - 1) + "."); return q; }
        private void UpdateSnapshot() { if (register != null) { snapshot = register.Status(); Accumulate(snapshot); } }
        private static BigInteger Counter(Dictionary<string, object> value, string key)
        { object raw; BigInteger result; if (!value.TryGetValue(key, out raw) || !BigInteger.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out result)) return BigInteger.Zero; return result; }
        private static BigInteger SavedCounter(Dictionary<string, object> value, string key)
        {
            object raw; BigInteger result;
            if (value == null || !value.TryGetValue(key, out raw) || !(raw is string) || !BigInteger.TryParse((string)raw, NumberStyles.None, CultureInfo.InvariantCulture, out result) || result < 0)
                throw new InvalidDataException("Invalid saved tranche work counter: " + key);
            return result;
        }
        private void SetBaseline(Dictionary<string, object> value) { baseline.Clear(); foreach (string key in ActualKeys) baseline[key] = Counter(value, key); }
        private void Accumulate(Dictionary<string, object> value)
        {
            foreach (string key in ActualKeys)
            {
                BigInteger now = Counter(value, key), previous;
                if (!baseline.TryGetValue(key, out previous)) previous = BigInteger.Zero;
                if (now > previous) lifetime[key] += now - previous;
                baseline[key] = now;
            }
        }
        private object JobStatus()
        {
            if (job == null) return null;
            return D("id", job.Id, "phase", job.Active && job.Cancel ? "cancelling" : job.Phase, "active", job.Active, "operation", job.Operation, "completed", S(job.Completed), "total", S(job.Total), "elapsedMs", job.PreviousElapsedMs + job.Watch.Elapsed.TotalMilliseconds, "result", job.Result, "error", job.Error, "checkpointError", job.CheckpointError, "cancelRequested", job.Cancel);
        }
        private void Save()
        {
            if (register != null)
            {
                checkpointAvailable = false;
                if (register.CanCompactCheckpoint) Checkpoint(false);
            }
            SaveMetadata();
        }
        private void Checkpoint(bool dense)
        {
            string temp = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { register.Save(temp, dense); string hash = FileHash(temp); Install(temp, statePath); checkpointHash = hash; checkpointAvailable = true; }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        private void SaveMetadata()
        {
            List<object> initial = null;
            if (initialSpinors != null) { initial = new List<object>(); foreach (double[] v in initialSpinors) initial.Add(ExactNumbers(v)); }
            Dictionary<string, object> actual = new Dictionary<string, object>(); foreach (string key in ActualKeys) actual[key] = S(lifetime[key]); actual["actualCheckpointAmplitudeLoads"] = S(checkpointLoads);
            object value = D("schema", "SPINDOCTOR/TRANCHE-SESSION/1", "qubits", qubits, "memoryLimitBytes", S(memoryLimit), "measurementSeed", seed, "measurementCounter", S(randomCounter), "initialSpinorsExact", initial, "modelBinding", binding, "journal", journal, "checkpointAvailable", checkpointAvailable, "checkpointSha256", checkpointHash, "snapshot", snapshot, "job", JobStatus(), "preparedPoints", S(preparedPoints), "committedGates", S(committedGates), "lifetimeActualWork", actual);
            string temp = metaPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, json.Serialize(value), new UTF8Encoding(false)); Install(temp, metaPath); }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
        private void LoadMetadata()
        {
            try
            {
                if (new FileInfo(metaPath).Length > json.MaxJsonLength) throw new InvalidDataException("Tranche metadata too large.");
                Dictionary<string, object> value = json.DeserializeObject(File.ReadAllText(metaPath)) as Dictionary<string, object>;
                if (value == null || (string)value["schema"] != "SPINDOCTOR/TRANCHE-SESSION/1" || (string)value["measurementSeed"] != seed) throw new InvalidDataException("Tranche schema or seed differs.");
                qubits = Int(Convert.ToString(value["qubits"], CultureInfo.InvariantCulture));
                if (qubits < 0 || qubits > 29) throw new InvalidDataException("Invalid saved tranche size.");
                memoryLimit = UInt((string)value["memoryLimitBytes"]); if (memoryLimit < 1024UL * 1024) throw new InvalidDataException("Invalid saved memory budget.");
                randomCounter = UInt((string)value["measurementCounter"]);
                preparedPoints = BigInteger.Parse((string)value["preparedPoints"], CultureInfo.InvariantCulture); committedGates = BigInteger.Parse((string)value["committedGates"], CultureInfo.InvariantCulture);
                if (preparedPoints < 0 || committedGates < 0) throw new InvalidDataException("Invalid tranche accounting.");
                snapshot = value["snapshot"] as Dictionary<string, object> ?? new Dictionary<string, object>();
                Dictionary<string, object> totals;
                object rawTotals;
                totals = value.TryGetValue("lifetimeActualWork", out rawTotals) ? rawTotals as Dictionary<string, object> : null;
                foreach (string key in ActualKeys) lifetime[key] = SavedCounter(totals, key);
                checkpointLoads = SavedCounter(totals, "actualCheckpointAmplitudeLoads");
                binding = value["modelBinding"] as Dictionary<string, object>;
                object[] init = value["initialSpinorsExact"] as object[];
                if (init != null) { if (init.Length != qubits ||qubits==0) throw new InvalidDataException("Initial spinor count differs."); initialSpinors = new double[init.Length][]; for (int i = 0; i < init.Length; i++) initialSpinors[i] = ReadNumbers(init[i]); using (TrancheRegister validated = new TrancheRegister(qubits, initialSpinors)) { } }
                object[] history = value["journal"] as object[]; if (history == null) throw new InvalidDataException("Missing tranche replay journal.");
                if (history.Length > 65536 ||qubits==0&&history.Length!=0) throw new InvalidDataException("Invalid tranche journal count.");
                foreach (object raw in history)
                {
                    Dictionary<string, object> entry = raw as Dictionary<string, object>;
                    if (entry == null) throw new InvalidDataException("Invalid tranche journal record.");
                    string op = (string)entry["operation"];
                    if (op == "gate")
                    {
                        string name = (string)entry["name"]; Dictionary<string, object> spec = GateSpec(name);
                        double[] angles = ReadNumbers(entry["anglesExact"]); int[] targets = ReadInts(entry["targets"]); bool all = (bool)entry["all"];
                        if (angles.Length != (int)spec["angleCount"] ||all&&!(bool)spec["local"] ||!all&&targets.Length!=(int)spec["targetCount"] ||all&&targets.Length!=0) throw new InvalidDataException("Invalid saved gate operands.");
                        HashSet<int> distinct = new HashSet<int>(); foreach (int q in targets) if(q<0||q>=qubits||!distinct.Add(q))throw new InvalidDataException("Invalid saved gate qubits.");
                    }
                    else if (op == "collapse")
                    {
                        int q = Int(Convert.ToString(entry["qubit"], CultureInfo.InvariantCulture)); string basis = (string)entry["basis"];
                        if(q<0||q>=qubits ||basis!="x"&&basis!="y"&&basis!="z" ||UInt((string)entry["counter"])>=randomCounter)throw new InvalidDataException("Invalid saved collapse operands.");
                    }
                    else throw new InvalidDataException("Unknown saved tranche replay operation.");
                    journal.Add(raw);
                }
                object rawHash; checkpointHash = value.TryGetValue("checkpointSha256", out rawHash) ? Convert.ToString(rawHash, CultureInfo.InvariantCulture) : "";
                checkpointAvailable = (bool)value["checkpointAvailable"] && File.Exists(statePath) && checkpointHash.Length == 64;
                suspended = qubits > 0;
                Dictionary<string, object> oldJob = value["job"] as Dictionary<string, object>;
                if (oldJob != null)
                {
                    object checkpointError;
                    job = new Work { Id = (string)oldJob["id"], Operation = (string)oldJob["operation"], Active = false, Phase = (bool)oldJob["active"] ? "interrupted" : (string)oldJob["phase"], Error = (string)oldJob["error"], CheckpointError = oldJob.TryGetValue("checkpointError", out checkpointError) ? Convert.ToString(checkpointError, CultureInfo.InvariantCulture) : "", Completed = UInt((string)oldJob["completed"]), Total = UInt((string)oldJob["total"]), PreviousElapsedMs = Convert.ToDouble(oldJob["elapsedMs"], CultureInfo.InvariantCulture), Cancel = (bool)oldJob["cancelRequested"], Result = oldJob["result"] };
                }
                if (job != null) { job.Watch.Stop(); if(!QuantumMath.IsFinite(job.PreviousElapsedMs)||job.PreviousElapsedMs<0||job.Completed>job.Total)throw new InvalidDataException("Invalid saved tranche job progress."); }
            }
            catch (Exception ex) { throw new InvalidDataException("Saved SpinDoctor tranche was rejected and preserved: " + ex.Message, ex); }
        }
        private object Help()
        {
            return D("commands", new string[] { "q.tranche prepare 29 zero", "q.tranche from 0", "q.tranche gate h all", "q.tranche gate cx 0 1", "q.tranche gate u 0.2 0.3 0.4 0", "q.tranche gate cu 0.2 0.3 0.4 0.5 0 1", "q.tranche state 0", "q.tranche spin 0", "q.tranche measure 0 basis z shots 100", "q.tranche measure 0 basis z shots 1 collapse", "q.tranche status", "q.tranche cancel", "q.tranche memory 8192", "q.tranche export", "q.tranche save dense", "q.tranche resume", "q.tranche reset", "q.tranche release" }, "gates", TrancheRegister.SupportedGates, "bitOrder", "little-endian: qubit 0 is the least-significant basis bit", "scope", "One selected register; multi-qubit operands entangle its coherent factors. Compact states checkpoint automatically; large dense states retain a replay journal or can be explicitly checkpointed with q.tranche save dense.");
        }
        private static Dictionary<string, object> GateSpec(string name) { foreach (Dictionary<string, object> spec in TrancheRegister.SupportedGates) if (((string)spec["name"]).Equals(name, StringComparison.OrdinalIgnoreCase)) return spec; throw new ArgumentException("Unsupported tranche gate. Use q.tranche help for available operators."); }
        private static string Alias(string name) { name = name.ToLowerInvariant(); return name == "cnot" ? "cx" : name == "toffoli" ? "ccx" : name == "fredkin" ? "cswap" : name == "id" || name == "identity" ? "i" : name == "u3" ? "u" : name; }
        private static string[] ExactNumbers(double[] values) { string[] result = new string[values.Length]; for (int i = 0; i < values.Length; i++) result[i] = values[i].ToString("R", CultureInfo.InvariantCulture); return result; }
        private static double[] ReadNumbers(object raw) { Array a = raw as Array; if (a == null) throw new InvalidDataException("Invalid numeric sequence."); double[] result = new double[a.Length]; for (int i = 0; i < a.Length; i++) result[i] = Number((string)a.GetValue(i)); return result; }
        private static int[] ReadInts(object raw) { Array a = raw as Array; if (a == null) throw new InvalidDataException("Invalid operand sequence."); int[] result = new int[a.Length]; for (int i = 0; i < a.Length; i++) result[i] = Int(Convert.ToString(a.GetValue(i), CultureInfo.InvariantCulture)); return result; }
        private static void Install(string temp, string path) { if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
        private static void Exact(string[] w, int n) { if (w.Length != n) throw new ArgumentException("Incorrect number of tranche command arguments."); }
        private static void Need(string word, string expected) { if (!word.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected '" + expected + "'."); }
        private static int Int(string value) { int n; if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) throw new ArgumentException("Expected a nonnegative integer operand."); return n; }
        private static ulong UInt(string value) { ulong n; if (!UInt64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) throw new ArgumentException("Expected an unsigned decimal integer."); return n; }
        private static double Number(string value) { double n; if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || Double.IsNaN(n) || Double.IsInfinity(n) || Math.Abs(n) > 1e12) throw new ArgumentException("Angle must be finite and within +/-1e12."); return n; }
        private static string S(ulong n) { return n.ToString(CultureInfo.InvariantCulture); }
        private static string S(BigInteger n) { return n.ToString(CultureInfo.InvariantCulture); }
        private static string Hash(string value) { using (SHA256 h = SHA256.Create()) { StringBuilder text = new StringBuilder(); foreach (byte b in h.ComputeHash(Encoding.UTF8.GetBytes(value))) text.Append(b.ToString("x2", CultureInfo.InvariantCulture)); return text.ToString(); } }
        private static string FileHash(string path) { using (SHA256 h = SHA256.Create()) using (FileStream f = File.OpenRead(path)) { StringBuilder text = new StringBuilder(); foreach (byte b in h.ComputeHash(f)) text.Append(b.ToString("x2", CultureInfo.InvariantCulture)); return text.ToString(); } }
        private static Dictionary<string, object> D(params object[] values) { Dictionary<string, object> d = new Dictionary<string, object>(); for (int i = 0; i < values.Length; i += 2) d.Add((string)values[i], values[i + 1]); return d; }
        public void Dispose()
        {
            Thread pending;
            lock (sync) { if (disposed) return; disposed = true; if (job != null && job.Active) job.Cancel = true; pending = thread; }
            if (pending != null && pending.IsAlive &&pending!=Thread.CurrentThread) pending.Join();
            lock (sync) { try { Save(); } finally { if (register != null) register.Dispose(); register = null; disposed = true; } }
        }
    }
}
