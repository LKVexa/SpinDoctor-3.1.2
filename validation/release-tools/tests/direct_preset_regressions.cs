using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class DirectPresetRegressions
{
    static int checks, actualShots, actualDraws;
    static string reportOutput;
    static Dictionary<string,object> pdfProof;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 33554432, RecursionLimit = 100 };
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static Dictionary<string,object> M(object value) { return (Dictionary<string,object>)value; }
    static Dictionary<string,object> D(params object[] value) { return SpinDoctorCalculators.D(value); }
    static string S(object value) { return Convert.ToString(value, CultureInfo.InvariantCulture); }
    static string[] W(string value) { return value.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); }
    static void Check(bool passed, string why) { checks++; if (!passed) throw new Exception("FAIL " + why); }
    static void Reject(Action action, string why) { try { action(); } catch (ArgumentException) { Check(true, why); return; } throw new Exception("FAIL missing refusal: " + why); }
    static string Hash(string path) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static Dictionary<string,object> Circuit(TerminalSession session) { return M(M(session.GetStatus())["circuit"]); }
    static Dictionary<string,object> Execute(TerminalSession session, string command)
    { var response = M(session.Execute(command)); Check((bool)response["ok"], command + " succeeds: " + S(response.ContainsKey("error") ? response["error"] : "")); return response; }
    static Dictionary<string,object> Wait(TerminalSession session, string id)
    {
        DateTime until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            var job = M(Circuit(session)["result"]);
            if (S(job["id"]) != id) throw new Exception("Circuit job identity changed while waiting.");
            if (!(bool)job["active"]) { Check(S(job["phase"]) == "completed", "matched native circuit completed"); return job; }
            Thread.Sleep(10);
        }
        throw new Exception("Tiny circuit did not finish in thirty seconds.");
    }
    static Dictionary<string,object> Completed(TerminalSession session, string command, int shots, int qubits, string basis)
    {
        var response = Execute(session, command); var accepted = M(response["result"]); var queued = M(accepted["job"]); string id = S(queued["id"]), reportId = S(response["reportId"]);
        Check((bool)accepted["queued"] && (bool)queued["active"], "accepted preset captures queued active job before worker completion");
        Check(S(queued["requestedShots"]) == S(shots) && S(queued["basis"]) == basis, "exact requested shots and basis in acceptance");
        Check(SpinDoctorCommandReports.AwaitingCompletion(command, response), "preset receipt awaits native completion");
        var archive = M(response["archive"]); Check(S(archive["executionOutcome"]) == "pending" && S(archive["commandCompletedUtc"]) == "", "no acceptance completion stamp");
        string originalAccepted = session.CommandJson(reportId, "accepted");
        var final = Wait(session, id); var counts = M(final["counts"]);
        Check(S(final["requestedShots"]) == S(shots) && S(final["completedShots"]) == S(shots), "all requested preset shots actually completed");
        Check(S(final["basis"]) == basis && Convert.ToInt32(final["qubits"]) == qubits, "completed program and basis match request");
        Check(S(counts["completedShots"]) == S(shots) && S(counts["uniformDraws"]) == S(shots * qubits), "actual shot and draw counts truthful");
        int instructions = Convert.ToInt32(M(final["programSnapshot"])["gateCount"]);
        Check(S(counts["executedCircuitInstructions"]) == S((shots + 1) * instructions), "actual instructions include one separately retained coherent execution");
        Check(S(counts["coherentOutputExecutions"]) == "1", "one real coherent output execution");
        int histogramTotal = 0; foreach (object row in (IEnumerable)final["histogram"]) histogramTotal += Convert.ToInt32(M(row)["count"], CultureInfo.InvariantCulture);
        Check(histogramTotal == shots, "native histogram totals actual shots");
        var completed = Json.Deserialize<Dictionary<string,object>>(session.CommandJson(reportId, "completed")); var completedResponse = M(completed["response"]); var completedResult = M(completedResponse["result"]);
        Check((bool)completedResponse["ok"] && S(completedResult["id"]) == id && S(completedResult["completedShots"]) == S(shots), "completed immutable receipt matched by native job identity");
        Check(S(completed["executionOutcome"]) == "completed" && S(completed["commandCompletedUtc"]).Length > 0, "completion stamp captured only after native stop");
        Check(session.CommandJson(reportId, "accepted") == originalAccepted, "accepted archive remains byte-identical after completion");
        Check(SpinDoctorReportDetails.CircuitDefinitionProgram(command, accepted).Count == 0 && SpinDoctorReportDetails.CircuitDefinitionProgram(command, final).Count == 0, "automatic run never classified as definition-only");
        Check(S(M(session.GetStatus())["verifiedSamples"]) == "0", "circuit measurements earn no procedural verification credit");
        if (shots == 1024 && qubits == 2)
        {
            var request = D("kind", "selected", "view", "details", "records", new object[] { D("nativeArchive", D("type", "command", "id", reportId, "stage", "completed")) }, "status", D());
            string pdfPath = Path.Combine(reportOutput, "SpinDoctor-3.1.0-Automatic-Bell-1024-Shots.pdf");
            string counterBefore = S(Circuit(session)["counter"]); File.WriteAllBytes(pdfPath, session.ReportPdf(request));
            Check(S(Circuit(session)["counter"]) == counterBefore, "PDF native archive replay adds no shots or random draws");
            File.WriteAllText(Path.Combine(reportOutput, "automatic-bell-pdf-request.json"), Json.Serialize(request), new UTF8Encoding(false));
            pdfProof = D("pdfPath", pdfPath, "pdfSha256", Hash(pdfPath), "pdfBytes", new FileInfo(pdfPath).Length, "archiveId", reportId, "jobId", id, "nativeCommandCompletedUtc", completed["commandCompletedUtc"], "requestedShots", shots, "completedShots", shots, "executedCircuitInstructions", counts["executedCircuitInstructions"], "uniformDraws", counts["uniformDraws"], "generationUsesMatchedNativeArchive", true, "newShotsFromExport", 0);
        }
        actualShots += shots; actualDraws += shots * qubits; return final;
    }
    static void Parser()
    {
        foreach (string preset in new string[] { "bell", "ghz", "qft" })
        {
            var options = CircuitTerminal.ParsePresetOptions(W("q.circuit preset " + preset));
            Check(options.Run && options.Shots == 1024 && options.Basis == "z" && options.Name == null, preset + " automatic defaults");
            Check(options.Qubits == (preset == "bell" ? 2 : 29), preset + " register default");
        }
        var explicitOptions = CircuitTerminal.ParsePresetOptions(W("q.circuit preset qft 3 basis=y shots=2 as fresh run=true"));
        Check(explicitOptions.Run && explicitOptions.Shots == 2 && explicitOptions.Qubits == 3 && explicitOptions.Basis == "y" && explicitOptions.Name == "fresh", "mixed option syntax and order preserved");
        var definition = CircuitTerminal.ParsePresetOptions(W("q.circuit preset qft 29 run=false shots 100000 basis x"));
        Check(!definition.Run && definition.Shots == 100000 && definition.Basis == "x", "explicit definition-only may retain validated future run hints");
        foreach (string basis in new string[] { "x", "y", "z" }) foreach (int shots in new int[] { 1, 100000 })
        { var p = CircuitTerminal.ParsePresetOptions(W("q.circuit preset bell shots " + shots + " basis " + basis)); Check(p.Shots == shots && p.Basis == basis, "bounded shot override parsed without execution"); }
        foreach (string command in new string[] {
            "q.circuit preset", "q.circuit preset unknown", "q.circuit preset bell 1", "q.circuit preset ghz 0", "q.circuit preset qft 30", "q.circuit preset qft 3.0",
            "q.circuit preset bell shots 0", "q.circuit preset bell shots -1", "q.circuit preset bell shots 100001", "q.circuit preset bell shots 500000", "q.circuit preset bell shots 1.5", "q.circuit preset bell shots 1e3", "q.circuit preset bell shots 999999999999999999999999",
            "q.circuit preset bell shots", "q.circuit preset bell shots=", "q.circuit preset bell shots 1 shots=2", "q.circuit preset bell basis invalid", "q.circuit preset bell basis x basis=y",
            "q.circuit preset bell run=maybe", "q.circuit preset bell run=false run=true", "q.circuit preset bell backend gpu", "q.circuit preset bell as bad/name", "q.circuit preset bell as a as b", "q.circuit preset bell garbage x" })
            Reject(delegate { CircuitTerminal.ParsePresetOptions(W(command)); }, "invalid preset preflight: " + command);
    }
    static void PreservesOnFailure(TerminalSession session, string workspace)
    {
        var before = Circuit(session); string name = S(before["selected"]), hash = S(before["coherentOutputProgramHash"]), jobId = S(M(before["job"])["id"]), counter = S(before["counter"]); int programs = Convert.ToInt32(before["programCount"]);
        foreach (string command in new string[] { "q.circuit preset bell shots 500000", "q.circuit preset bell basis bad", "q.circuit preset qft 3 as " + name })
        {
            Check(!(bool)M(session.Execute(command))["ok"], "invalid direct command refused before mutation"); var after = Circuit(session);
            Check(S(after["selected"]) == name && S(after["coherentOutputProgramHash"]) == hash && (bool)after["coherentOutputAvailable"] && Convert.ToInt32(after["programCount"]) == programs && S(after["counter"]) == counter && S(M(after["job"])["id"]) == jobId, "invalid command preserves selected program/output/job/counter");
        }
        var native = (CircuitTerminal)typeof(TerminalSession).GetField("circuit", Private).GetValue(session);
        FieldInfo counterField = typeof(CircuitTerminal).GetField("counter", Private), jobField = typeof(CircuitTerminal).GetField("job", Private); object oldCounter = counterField.GetValue(native), job = jobField.GetValue(native); FieldInfo active = job.GetType().GetField("Active", BindingFlags.Public | BindingFlags.Instance);
        try { counterField.SetValue(native, UInt64.MaxValue); Check(!(bool)M(session.Execute("q.circuit preset qft 29"))["ok"], "mock exhausted counter refuses large definition before any worker"); }
        finally { counterField.SetValue(native, oldCounter); }
        try { active.SetValue(job, true); Check(!(bool)M(session.Execute("q.circuit preset qft 29 run=false"))["ok"], "mock active worker refuses program mutation"); }
        finally { active.SetValue(job, false); }
        string checkpoint = Path.Combine(workspace, "spindoctor-circuits.json");
        using (FileStream held = new FileStream(checkpoint, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(!(bool)M(session.Execute("q.circuit preset qft 2 as rollback_test shots 1"))["ok"], "locked checkpoint refuses queue atomically");
        var restored = Circuit(session);
        Check(S(restored["selected"]) == name && S(restored["coherentOutputProgramHash"]) == hash && (bool)restored["coherentOutputAvailable"] && S(M(restored["job"])["id"]) == jobId && S(restored["counter"]) == counter && Convert.ToInt32(restored["programCount"]) == programs, "preflight/queue persistence failures retain prior actual output and counters");
        Check(M(restored["result"])["coherentSpinPreview"] != null, "failed queue retains previous captured spin evidence");
    }
    static void SequenceIntegration(TerminalSession session)
    {
        string beforeCounter = S(Circuit(session)["counter"]);
        var refused = M(M(session.StartSequence(D("commands", new string[] { "q.circuit preset qft 2 run=false", "q.query reduced-density qubits=[0]" })))["result"]);
        Check(!(bool)refused["queued"] && S(Circuit(session)["counter"]) == beforeCounter, "definition-only query sequence refused before execution");
        var response = M(session.StartSequence(D("commands", new string[] { "q.circuit preset bell basis x", "q.query reduced-density qubits=[0]" }, "parameters", D("shots", 1, "basis", "z"))));
        var accepted = M(response["result"]); Check((bool)accepted["queued"], "auto-preset then coherent query preflight accepts execution context"); string id = S(M(accepted["job"])["id"]); Dictionary<string,object> job = null; DateTime until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until) { job = M(M(M(session.GetStatus())["sequence"])["job"]); Check(S(job["id"]) == id, "sequence identity retained"); if (!(bool)job["active"]) break; Thread.Sleep(10); }
        Check(job != null && !(bool)job["active"] && S(job["phase"]) == "completed" && Convert.ToInt32(job["completedCommands"]) == 2, "sequence waits for automatic native preset before query");
        var first = Json.Deserialize<Dictionary<string,object>>(session.SequenceJson(id, 0, "latest")); var firstAccepted = M(first["response"]); var final = M(first["finalResponse"]); var actual = M(final["result"]);
        Check((bool)M(firstAccepted["result"])["queued"] && (bool)final["ok"] && S(actual["completedShots"]) == "1" && S(actual["basis"]) == "z", "preset parameter overrides passed to awaited native run");
        Check(S(first["completedUtc"]).Length > 0 && S(first["normalizedCommand"]).Contains("shots 1 basis z as "), "sequence completion stamp and fresh normalized circuit identity");
        var second = Json.Deserialize<Dictionary<string,object>>(session.SequenceJson(id, 1, "latest")); var query = M(M(second["response"])["result"]); IList vector = (IList)query["bloch"];
        Check(Convert.ToDouble(vector[0]) == 0 && Convert.ToDouble(vector[1]) == 0 && Convert.ToDouble(vector[2]) == 0 && (bool)query["mixed"], "query consumes actual completed Bell premeasurement marginal");
        Check(String.CompareOrdinal(S(first["completedUtc"]), S(second["receivedUtc"])) <= 0, "query begins after native preset completion");
        Check(S(Circuit(session)["counter"]) == S(UInt64.Parse(beforeCounter, CultureInfo.InvariantCulture) + 2), "sequence executes only one override shot,not duplicate default shots");
        actualShots++; actualDraws += 2;
    }
    public static void Run(string source, string output)
    {
        if (Directory.Exists(output)) throw new Exception("Use a fresh direct-preset test output directory."); Directory.CreateDirectory(output); reportOutput = output; string workspace = Path.Combine(output, "isolated-native-workspace"); Directory.CreateDirectory(workspace); Directory.CreateDirectory(Path.Combine(workspace, "quantum"));
        foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) File.Copy(path, Path.Combine(workspace, "quantum", Path.GetFileName(path)));
        foreach (string name in new string[] { "SPINDOCTOR_PRESETS.json", "EquationCatalogue.json", "QUANTUM_TERMINAL.html" }) File.Copy(Path.Combine(source, name), Path.Combine(workspace, name));
        Parser(); Dictionary<string,object> finalCounts; List<object> actualRuns = new List<object>();
        using (TerminalSession session = new TerminalSession(workspace, false))
        {
            Check(!(bool)M(session.GetStatus())["gpuAvailable"], "isolated circuit tests use no GPU");
            var bell = Completed(session, "q.circuit preset bell", 1024, 2, "z"); actualRuns.Add(D("program", bell["programName"], "shots", bell["completedShots"], "counts", bell["counts"]));
            foreach (object row in (IEnumerable)bell["histogram"]) Check(S(M(row)["bitstring"]) == "00" || S(M(row)["bitstring"]) == "11", "Bell joint outcomes preserve correlation");
            Check((bool)M(bell["coherentSpinPreview"])["premeasurement"], "automatic Bell retains actual premeasurement spin figures");
            PreservesOnFailure(session, workspace);
            var ghz = Completed(session, "q.circuit preset ghz 3 shots=1 basis=y as ghz_small", 1, 3, "y"); actualRuns.Add(D("program", ghz["programName"], "shots", ghz["completedShots"], "counts", ghz["counts"]));
            var qft = Completed(session, "q.circuit preset qft 3 basis x shots 2", 2, 3, "x"); actualRuns.Add(D("program", qft["programName"], "shots", qft["completedShots"], "counts", qft["counts"]));
            Check(((IList)qft["histogram"]).Count == 1 && S(M(((IList)qft["histogram"])[0])["bitstring"]) == "000", "QFT zero input gives all-plus premeasurement state sampled in X");
            SequenceIntegration(session);
            string beforeCounter = S(Circuit(session)["counter"]), beforeJob = S(M(Circuit(session)["job"])["id"]); var definedResponse = Execute(session, "q.circuit preset qft 29 run=false shots=2 basis=y as definition29"); var defined = M(definedResponse["result"]);
            Check(Convert.ToInt32(defined["gateCount"]) == 449 && Convert.ToInt32(defined["qubits"]) == 29 && !(bool)defined["executionRequested"], "29-qubit definition only,449 stored instructions, no worker");
            Check(!SpinDoctorCommandReports.AwaitingCompletion("q.circuit preset qft 29 run=false", definedResponse), "definition-only never awaits unrelated earlier circuit job");
            Check(SpinDoctorReportDetails.CircuitDefinitionProgram("q.circuit preset qft 29 run=false", defined).Count > 0, "definition-only report classification retained");
            Check(S(Circuit(session)["counter"]) == beforeCounter && S(M(Circuit(session)["job"])["id"]) == beforeJob, "definition-only adds no shots,draws,or replacement job");
            var definedArchive = M(definedResponse["archive"]); Check(S(definedArchive["executionOutcome"]) == "completed" && S(definedArchive["commandCompletedUtc"]).Length > 0, "definition completion stamp remains separate from run completion");
            Execute(session, "q.circuit preset qft 1 run=false as explicit_input"); Execute(session, "q.circuit input 1"); var run = Completed(session, "q.circuit run shots 1 basis x", 1, 1, "x"); actualRuns.Add(D("program", run["programName"], "shots", run["completedShots"], "counts", run["counts"]));
            Check(S(M(((IList)run["histogram"])[0])["bitstring"]) == "1", "definition-only preserves explicit input before final run; QFT1 maps |1> to |-x>");
            Check(S(M(Execute(session, "q.help")["result"])["circuitPresetDefaultShots"]) == "1024", "terminal help exposes new default");
            finalCounts = M(Circuit(session)["lifetime"]); Check(S(finalCounts["completedShots"]) == S(actualShots) && S(finalCounts["uniformDraws"]) == S(actualDraws), "lifetime totals equal only bounded executed work");
            Check(actualShots == 1029 && actualDraws == 2060, "test performs only tiny1/2/3-qubit shots; no29-qubit execution");
        }
        using (TerminalSession restored = new TerminalSession(workspace, false))
        { var result = M(Circuit(restored)["result"]); Check(S(result["completedShots"]) == "1" && S(result["programName"]) == "explicit_input" && !(bool)result["active"], "completed native receipt state survives restart without rerun"); Check(S(M(Circuit(restored)["lifetime"])["completedShots"]) == "1029", "restart does not execute any additional default shots"); }
        var hashes = D(); foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) hashes[Path.GetFileName(path)] = Hash(path);
        File.WriteAllText(Path.Combine(output, "direct-preset-proof.json"), Json.Serialize(D("passed", true, "assertions", checks, "version", TerminalSession.ProductVersion, "sourceHashes", hashes, "actualCircuitShots", actualShots, "actualCircuitDraws", actualDraws, "maximumActuallyRunQubits", 3, "largePresetOnlyDefined", true, "defaultPresetShots", CircuitTerminal.DefaultPresetShots, "shotMaximumUnchanged", 100000, "verificationDefaultUnchanged", 10240, "liveUserSessionTouched", false, "stressTestPerformed", false, "actualRuns", actualRuns.ToArray(), "sequenceIntegrationShots", 1, "finalLifetimeCounts", finalCounts, "pdfExample", pdfProof)), new UTF8Encoding(false));
        Console.WriteLine("PASS " + checks + " direct-preset dispatch/receipt/preflight assertions; " + actualShots + " actual tiny-register shots," + actualDraws + " draws; no29-qubit execution or live user session changes.");
    }
}
