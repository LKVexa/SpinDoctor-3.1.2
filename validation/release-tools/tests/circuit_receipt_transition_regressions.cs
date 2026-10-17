using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class CircuitReceiptTransitionRegressions
{
    static int checks, actualShots;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 33554432, RecursionLimit = 100 };
    static Dictionary<string,object> M(object value) { return (Dictionary<string,object>)value; }
    static Dictionary<string,object> D(params object[] value) { return SpinDoctorCalculators.D(value); }
    static string S(object value) { return Convert.ToString(value, CultureInfo.InvariantCulture); }
    static string[] W(string value) { return value.Split(' '); }
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception("FAIL " + why); }
    static object Field(object owner, string name) { return owner.GetType().GetField(name, Private).GetValue(owner); }
    static string Hash(string path) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static void Complete(TerminalSession session, Dictionary<string,object> status)
    { typeof(TerminalSession).GetMethod("CompleteReports", Private, null, new Type[] { typeof(Dictionary<string,object>), typeof(bool) }, null).Invoke(session, new object[] { status, false }); }
    static Action<Dictionary<string,object>> Guard(TerminalSession session)
    { return (Action<Dictionary<string,object>>)typeof(TerminalSession).GetMethod("BeforeCircuitMutation", Private).Invoke(session, new object[0]); }
    static Dictionary<string,object> NativeWait(CircuitTerminal circuit, string id)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until) { var result = M(circuit.GetStatus()["result"]); Check(S(result["id"]) == id, "exact native identity while waiting outside Terminal polling"); if (!(bool)result["active"]) { Check(S(result["phase"]) == "completed", "actual tiny native worker completed"); actualShots += Convert.ToInt32(result["completedShots"], CultureInfo.InvariantCulture); return result; } Thread.Sleep(10); }
        throw new Exception("Tiny circuit failed to stop.");
    }
    static Dictionary<string,object> PendingOld(TerminalSession session, CircuitTerminal circuit, SpinDoctorCommandReports reports)
    {
        var accepted = M(session.Execute("q.circuit preset bell shots 1")); Check((bool)accepted["ok"], "tiny automatic Bell accepted"); string id = S(M(M(accepted["result"])["job"])["id"]), reportId = S(accepted["reportId"]);
        var actual = NativeWait(circuit, id); Check(reports.Pending().Length == 1, "old completion not yet observed by Terminal");
        // Controlled interleaving: the first session snapshot sees Active=true;
        // the already-finished native job is then dispatched against without another
        // session observation. These are the two sides of the original unlocked gap.
        var stale = Json.Deserialize<Dictionary<string,object>>(Json.Serialize(accepted["status"])); var savedCircuit = M(stale["circuit"]);
        M(savedCircuit["result"])["active"] = true; M(savedCircuit["job"])["active"] = true;
        Complete(session, stale); Check(reports.Pending().Length == 1, "initial active snapshot cannot issue a completion stamp");
        Check(actual["coherentSpinPreview"] != null && (bool)circuit.GetStatus()["coherentOutputAvailable"], "actual old output and captured spin exist before transition");
        return D("jobId", id, "reportId", reportId, "acceptedJson", reports.Json(reportId, "accepted"), "actualResult", actual);
    }
    static TerminalSession Session(string source, string output, string name)
    {
        string workspace = Path.Combine(output, name); Directory.CreateDirectory(workspace); Directory.CreateDirectory(Path.Combine(workspace, "quantum"));
        foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) File.Copy(path, Path.Combine(workspace, "quantum", Path.GetFileName(path)));
        foreach (string file in new string[] { "SPINDOCTOR_PRESETS.json", "EquationCatalogue.json" }) File.Copy(Path.Combine(source, file), Path.Combine(workspace, file));
        return new TerminalSession(workspace, false);
    }
    public static void Run(string source, string output)
    {
        if (Directory.Exists(output)) throw new Exception("Use a fresh transition-regression directory."); Directory.CreateDirectory(output); Dictionary<string,object> legacy, fixedCase, refusal;
        using (TerminalSession session = Session(source, output, "legacy-unprotected-control"))
        {
            var circuit = (CircuitTerminal)Field(session, "circuit"); var reports = (SpinDoctorCommandReports)Field(session, "commandReports"); var old = PendingOld(session, circuit, reports);
            // The original dispatch used this unprotected overload. No live session
            // is involved; this is a deterministic small-register race control.
            var next = M(circuit.Execute(W("q.circuit preset bell shots 1"))); string nextId = S(M(next["job"])["id"]); NativeWait(circuit, nextId); session.GetStatus();
            Check(reports.Pending().Length == 1 && S(reports.Pending()[0]["jobId"]) == S(old["jobId"]), "original dispatch strands old receipt after exact job replacement");
            Check(!File.Exists(Path.Combine(output, "legacy-unprotected-control", "quantum-command-reports", S(old["reportId"]) + ".completed.json")), "lost legacy completion was not silently fabricated");
            legacy = D("reproduced", true, "oldJobId", old["jobId"], "newJobId", nextId, "oldCompletionArchived", false);
        }
        using (TerminalSession session = Session(source, output, "atomic-fixed-transition"))
        {
            var circuit = (CircuitTerminal)Field(session, "circuit"); var reports = (SpinDoctorCommandReports)Field(session, "commandReports"); var old = PendingOld(session, circuit, reports);
            Action<Dictionary<string,object>> guard = Guard(session); bool guardObservedOld = false;
            var next = M(circuit.ExecuteAndSnapshot(W("q.circuit preset bell shots 1"), delegate(Dictionary<string,object> snapshot)
            {
                Check(S(M(snapshot["result"])["id"]) == S(old["jobId"]) && !(bool)M(snapshot["result"])["active"], "callback holds exact stopped old native job"); guard(snapshot);
                var archived = Json.Deserialize<Dictionary<string,object>>(reports.Json(S(old["reportId"]), "completed"));
                Check(S(M(M(archived["response"])["result"])["id"]) == S(old["jobId"]) && S(archived["commandCompletedUtc"]).Length > 0, "old native completion stamp persisted before replacement");
                Check((bool)circuit.GetStatus()["coherentOutputAvailable"], "old coherent output retained until archive capture succeeds"); guardObservedOld = true;
            }));
            string nextId = S(M(next["job"])["id"]); Check(guardObservedOld && nextId != S(old["jobId"]), "new job installed only after guarded old archive"); NativeWait(circuit, nextId);
            var completed = Json.Deserialize<Dictionary<string,object>>(reports.Json(S(old["reportId"]), "completed")); var completedResult = M(M(completed["response"])["result"]);
            Check(reports.Pending().Length == 0, "no stranded old receipt remains after atomic replacement");
            Check(Json.Serialize(completedResult["coherentSpinPreview"]) == Json.Serialize(M(old["actualResult"])["coherentSpinPreview"]), "original actual spin snapshot retained unchanged");
            Check(reports.Json(S(old["reportId"]), "accepted") == S(old["acceptedJson"]), "accepted historical bytes unchanged");
            fixedCase = D("passed", true, "oldJobId", old["jobId"], "newJobId", nextId, "oldCompletionArchivedBeforeReplacement", true, "originalCompletedUtc", completed["commandCompletedUtc"]);
        }
        using (TerminalSession session = Session(source, output, "archive-failure-preservation"))
        {
            var circuit = (CircuitTerminal)Field(session, "circuit"); var reports = (SpinDoctorCommandReports)Field(session, "commandReports"); var old = PendingOld(session, circuit, reports); string counter = S(circuit.GetStatus()["counter"]), hash = S(circuit.GetStatus()["coherentOutputProgramHash"]), originalCheckpoint = Hash(Path.Combine(output, "archive-failure-preservation", "spindoctor-circuits.json"));
            Action<Dictionary<string,object>> guard = Guard(session); bool refused = false;
            using (FileStream held = new FileStream(Path.Combine(output, "archive-failure-preservation", "quantum-command-reports", "index.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { circuit.ExecuteAndSnapshot(W("q.circuit preset qft 1 shots 1"), guard); } catch (InvalidOperationException error) { Check(error.Message.Contains("completed circuit receipt must be saved"), "archive failure reason is actionable"); refused = true; }
                var terminalRefusal = M(session.Execute("q.circuit preset qft 1 shots 1"));
                Check(!(bool)terminalRefusal["ok"] && S(terminalRefusal["error"]).Contains("completed circuit receipt must be saved"), "ordinary Terminal dispatch also refuses replacing unarchived native completion");
            }
            Check(refused, "archive failure blocks new program and worker"); var preserved = circuit.GetStatus();
            Check(S(M(preserved["job"])["id"]) == S(old["jobId"]) && (bool)preserved["coherentOutputAvailable"] && S(preserved["counter"]) == counter && S(preserved["coherentOutputProgramHash"]) == hash && S(preserved["selected"]) == "bell", "failed archive preserves old job/output/program/counter");
            Check(Hash(Path.Combine(output, "archive-failure-preservation", "spindoctor-circuits.json")) == originalCheckpoint && reports.Pending().Length == 1, "failed archive leaves native checkpoint and pending identity intact");
            object oldWork = Field(circuit, "job"); FieldInfo active = oldWork.GetType().GetField("Active", BindingFlags.Public | BindingFlags.Instance); bool invoked = false, rejected = false;
            try { active.SetValue(oldWork, true); try { circuit.ExecuteAndSnapshot(W("q.circuit preset qft 1 run=false"), delegate(Dictionary<string,object> snapshot) { invoked = true; }); } catch (InvalidOperationException) { rejected = true; } }
            finally { active.SetValue(oldWork, false); }
            Check(rejected && !invoked, "active job rejection happens before callback or mutation");
            var defined = M(circuit.ExecuteAndSnapshot(W("q.circuit preset qft 1 run=false"), Guard(session))); Check(!(bool)defined["executionRequested"] && reports.Pending().Length == 0, "retry captures stopped receipt before explicit definition-only mutation");
            var oldArchive = Json.Deserialize<Dictionary<string,object>>(reports.Json(S(old["reportId"]), "completed")); Check(M(M(oldArchive["response"])["result"])["coherentSpinPreview"] != null, "definition mutation cannot erase historical completed spin figure evidence");
            Check(S(circuit.GetStatus()["counter"]) == counter, "refusal/retry/definition export executes no extra shots");
            refusal = D("passed", true, "oldJobId", old["jobId"], "archiveFailureBlockedReplacement", true, "checkpointPreserved", true, "retryCapturedBeforeDefinition", true, "extraShots", 0);
        }
        Check(actualShots == 5, "only five actual two-qubit shots across controlled transition tests");
        var hashes = D(); foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) hashes[Path.GetFileName(path)] = Hash(path);
        File.WriteAllText(Path.Combine(output, "circuit-receipt-transition-proof.json"), Json.Serialize(D("passed", true, "assertions", checks, "version", TerminalSession.ProductVersion, "sourceHashes", hashes, "legacyControl", legacy, "fixedTransition", fixedCase, "archiveFailure", refusal, "actualCircuitShots", actualShots, "maximumActuallyRunQubits", 2, "liveUserSessionTouched", false, "stressTestPerformed", false, "lockOrder", "Synchronous Terminal caller owns family lock before circuit lock. Other-controller status prepared before circuit lock; callback performs reentrant native capture and report-store persistence only. Native worker never calls back into Terminal.")), new UTF8Encoding(false));
        Console.WriteLine("PASS " + checks + " controlled circuit receipt-transition assertions; legacy race reproduced; atomic old-completion capture and archive-failure preservation verified; only5 actual two-qubit shots.");
    }
}
