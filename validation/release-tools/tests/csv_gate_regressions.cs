using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class CsvGateRegressions
{
    static int checks;
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 33554432, RecursionLimit = 100 };
    static Dictionary<string, object> D(params object[] pairs) { return SpinDoctorCalculators.D(pairs); }
    static Dictionary<string, object> M(object raw) { return (Dictionary<string, object>)raw; }
    static void Check(bool yes, string label) { checks++; if (!yes) throw new Exception("FAIL " + label); }
    static void Near(object raw, double expected, string label) { Check(Math.Abs(Convert.ToDouble(raw, CultureInfo.InvariantCulture) - expected) < 1e-12, label); }
    static string Hash(byte[] bytes) { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static void Refused(InputAttachmentManager inputs, string content, string fragment, string label)
    {
        Dictionary<string, object> attachment = inputs.Upload("invalid-" + checks + ".csv", content);
        Check(!(bool)attachment["parseValid"] && !(bool)attachment["executable"] && ((string)attachment["error"]).IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0, label);
    }
    static Dictionary<string, object> Native(TerminalSession s, string command) { Dictionary<string, object> response = M(s.Execute(command)); Check((bool)response["ok"], "Native command accepted: " + command); return response; }
    static Dictionary<string, object> Finished(TerminalSession s, string id)
    {
        DateTime stop = DateTime.UtcNow.AddSeconds(30); Dictionary<string, object> job;
        do { job = M(M(s.GetStatus())["sequence"])["job"] as Dictionary<string, object>; if (job != null && !(bool)job["active"]) { Check((string)job["id"] == id && (string)job["phase"] == "completed", "CSV circuit sequence completed with exact native identity"); return job; } Thread.Sleep(10); } while (DateTime.UtcNow < stop);
        throw new Exception("CSV circuit did not finish in bounded test time.");
    }
    public static void Run(string source, string scratch, string fixtures)
    {
        if (Directory.Exists(scratch)) throw new ArgumentException("Use fresh gate CSV scratch."); Directory.CreateDirectory(scratch);
        InputAttachmentManager inputs = new InputAttachmentManager(Path.Combine(scratch, "parser"));
        string bellCsv = File.ReadAllText(Path.Combine(fixtures, "gate-bell.csv"), Utf8), rotatedCsv = File.ReadAllText(Path.Combine(fixtures, "gate-rotated-qubit.csv"), Utf8);
        Dictionary<string, object> bell = inputs.Upload("gate-bell.csv", bellCsv);
        Check((bool)bell["parseValid"] && (bool)bell["executable"] && !(bool)bell["requiresBase"] && (string)bell["format"] == "csv-gates", "Explicit gate CSV is a validated inert circuit input");
        Check((int)bell["qubits"] == 2 && (int)bell["gateCount"] == 2 && (int)M(bell["hints"])["shots"] == 1024 && (string)M(bell["hints"])["basis"] == "z", "Gate metadata/1024 shot hint survive upload preview");
        Check(inputs.ExportText((string)bell["id"]) == bellCsv && (string)bell["sha256"] == Hash(Utf8.GetBytes(bellCsv)), "Original gate CSV bytes and source SHA preserved");
        Dictionary<string, object> spec = M(inputs.ImportSpec((string)bell["id"], null)); IList gates = (IList)spec["gates"];
        Check(gates.Count == 2 && (string)M(gates[0])["name"] == "h" && (string)M(gates[1])["name"] == "cx", "CSV gate order exactly preserved");
        int[] operands = (int[])M(gates[1])["qubits"]; Check(operands.Length == 2 && operands[0] == 0 && operands[1] == 1, "CSV quoted control-first operand order exactly preserved");
        Dictionary<string, object> plain = inputs.Upload("defaults.csv", "gate,n,targets,angles\nh,2,[0],\ncx,,\"[0,1]\",\n");
        Check((bool)plain["parseValid"] && (int)M(plain["hints"])["shots"] == 1024 && (string)plain["initialBasis"] == "0", "Minimal gate schema defaults 1024 Z shots and initial zero without changing JSON defaults");
        Dictionary<string, object> upper = inputs.Upload("upper.csv", "GATE,N,TARGETS,ANGLES,shots,basis,initialBasis\nRY,1,all,pi/2,100000,Y,1\n");
        Check((bool)upper["parseValid"] && (int)M(upper["hints"])["shots"] == 100000 && (string)M(upper["hints"])["basis"] == "y", "Case-insensitive gate headers/basis and existing100000 max accepted without running");
        Dictionary<string, object> rotated = inputs.Upload("gate-rotated-qubit.csv", rotatedCsv); Dictionary<string, object> rotatedSpec = M(inputs.ImportSpec((string)rotated["id"], null));
        Near(((double[])M(((IList)rotatedSpec["gates"])[0])["angles"])[0], Math.PI / 2, "Gate CSV safe angle expression resolves radians");
        Dictionary<string, object> repeated = inputs.Upload("repeated.csv", "gate,n,targets,angles,shots,basis\nh,02,0,,8,z\ncx,2,\"0,1\",,08,Z\n"); Check((bool)repeated["parseValid"] && (int)M(repeated["hints"])["shots"] == 8, "Equivalent repeated metadata allowed after canonical validation");
        Dictionary<string, object> multiAngle = inputs.Upload("u.csv", "gate,n,targets,angles\nu,1,0,\"pi/2,0,pi\"\n"); Check((bool)multiAngle["parseValid"] && ((double[])M(((IList)M(inputs.ImportSpec((string)multiAngle["id"], null))["gates"])[0])["angles"]).Length == 3, "Quoted multi-angle gate CSV uses native angle arity");
        Dictionary<string, object> incomplete = inputs.Upload("incomplete.csv", "gate,n,targets\nh,1,0\n");
        Check((bool)incomplete["requiresBase"] && !(bool)incomplete["executable"] && (string)incomplete["format"] == "csv-parameters", "Incomplete gate schema is inert parameter data needing an explicit base, not a guessed circuit");
        Refused(inputs, "gate,n,targets,angles\nh,,0,\n", "first", "Register declaration required in first row");
        Refused(inputs, "gate,n,targets,angles\nh,2,0,\nx,3,1,\n", "consistent", "Conflicting register metadata refused");
        Refused(inputs, "gate,n,targets,angles,shots\nh,2,0,,2\nx,,1,,3\n", "consistent", "Conflicting shot hints refused");
        Refused(inputs, "gate,n,targets,angles\nh,30,0,\n", "range", "Thirty coherent qubits refused");
        Refused(inputs, "gate,n,targets,angles,shots\nh,1,0,,100001\n", "range", "Over-ceiling shot hint refused before run");
        Refused(inputs, "gate,n,targets,angles,initialBasis\nh,1,0,,2\n", "initialBasis", "Initial basis outside register refused");
        Refused(inputs, "gate,n,targets,angles\nh,2,2,\n", "range", "Out of range qubit operand refused");
        Refused(inputs, "gate,n,targets,angles\ncx,2,\"0,0\",\n", "distinct", "Repeated control/target operand refused");
        Refused(inputs, "gate,n,targets,angles\ncx,2,all,\n", "only to local", "Entangling gate cannot silently broadcast all");
        Refused(inputs, "gate,n,targets,angles\nry,1,0,\n", "angle count", "Missing rotation angle refused");
        Refused(inputs, "gate,n,targets,angles\nh,1,0,pi\n", "angle count", "Extra gate angle refused");
        Refused(inputs, "gate,n,targets,angles\nry,1,0,System.IO.File.Delete(1)\n", "arithmetic", "Host code is not an angle expression");
        Refused(inputs, "gate,n,targets,angles\ncmd,1,0,\n", "Unsupported gate", "Unknown host-looking gate refused");
        Refused(inputs, "gate,n,targets,angles,unexpected\nh,1,0,,x\n", "Unknown", "Unknown gate CSV field refused");
        Refused(inputs, "gate,n,targets,angles,Gate\nh,1,0,,h\n", "Duplicate", "Duplicate case-insensitive gate header refused");
        // Two small actual CSV circuits: one rotated single qubit and one Bell pair.
        string workspace = Path.Combine(scratch, "native"); Directory.CreateDirectory(Path.Combine(workspace, "quantum"));
        foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) File.Copy(path, Path.Combine(workspace, "quantum", Path.GetFileName(path)));
        foreach (string name in new string[] { "SPINDOCTOR_PRESETS.json", "EquationCatalogue.json", "QUANTUM_TERMINAL.html" }) File.Copy(Path.Combine(source, name), Path.Combine(workspace, name));
        List<object> nativeRuns = new List<object>();
        using (TerminalSession session = new TerminalSession(workspace, false))
        {
            Dictionary<string, object> initial = M(session.GetStatus());
            string[] csvs = new string[] { rotatedCsv.Replace("1024", "1"), bellCsv.Replace("1024", "8") };
            for (int i = 0; i < csvs.Length; i++)
            {
                Dictionary<string, object> attach = M(M(session.AttachInput("native-gates-" + i + ".csv", csvs[i]))["result"]);
                Dictionary<string, object> imported = Native(session, "q.input import " + attach["id"]); Check((string)M(M(M(M(imported["result"])["program"])["inputImport"])["source"])["sha256"] == (string)attach["sha256"], "Explicit native import provenance matches uploaded CSV");
                Dictionary<string, object> accepted = Native(session, "q.input run " + attach["id"]); string jobId = (string)M(M(accepted["result"])["job"])["id"]; Dictionary<string, object> job = Finished(session, jobId);
                int last = (int)job["totalCommands"] - 1; Dictionary<string, object> step = M(Json.DeserializeObject(session.SequenceJson(jobId, last, "completed"))), result = M(M(step["finalResponse"])["result"]);
                int expected = i == 0 ? 1 : 8; Check((string)result["phase"] == "completed" && Convert.ToInt32(result["completedShots"]) == expected && !(bool)result["active"], "Declared tiny CSV shots complete before receipt");
                Dictionary<string, object> query = M(Native(session, "q.query reduced-density qubits=[0]")["result"]);
                if (i == 0) { Near(((IList)query["bloch"])[0], 1, "Actual rotated CSV circuit produces +X Bloch direction"); Near(((IList)query["bloch"])[2], 0, "Rotated CSV sphere is not stuck at north pole"); }
                else { foreach (object component in (IList)query["bloch"]) Near(component, 0, "Bell CSV actual local Bloch components vanish correctly"); Near(query["purity"], .5, "Bell CSV local state is mixed rather than an uninitialized arrow"); }
                string txt = session.SequencePrintout(jobId, last, "completed"); Check(txt.Contains("Completed UTC:") && txt.Contains("completedShots") && txt.Contains("Native phase: completed"), "Completed CSV circuit TXT contains real shot result and stop timestamp");
                nativeRuns.Add(D("inputSourceSha256", attach["sha256"], "sequenceJobId", jobId, "runIndex", last, "runArchive", step, "reducedDensity", query));
            }
            Dictionary<string, object> final = M(session.GetStatus()); Check(Convert.ToString(initial["measurementShots"]) == Convert.ToString(final["measurementShots"]) && Convert.ToString(final["quantumHardwareGates"]) == "0", "Native CSV circuit work stays distinct from procedural/physical quantum counters");
            StringBuilder larger = new StringBuilder("gate,n,targets,angles\n"); for (int i = 0; i < 125; i++) larger.Append("i,").Append(i == 0 ? "1" : "").Append(",0,\n");
            Dictionary<string, object> largeAttach = M(M(session.AttachInput("large-circuit.csv", larger.ToString()))["result"]); Check((bool)largeAttach["parseValid"], "Large gate list remains importable circuit data");
            Dictionary<string, object> rejected = M(session.Execute("q.input run " + largeAttach["id"])); Check(!(bool)rejected["ok"] && ((string)rejected["error"]).Contains("128"), "Existing128command sequence ceiling refuses oversize expanded CSV before execution");
        }
        Dictionary<string, object> hashes = new Dictionary<string, object>(); foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) hashes[Path.GetFileName(path)] = Hash(File.ReadAllBytes(path));
        File.WriteAllText(Path.Combine(scratch, "csv-gate-proof.json"), Json.Serialize(D("passed", true, "checks", checks, "productVersion", TerminalSession.ProductVersion, "sourceHashes", hashes, "nativeRuns", nativeRuns,
            "scope", "Explicit inert CSV gate parsing and isolated actual one-qubit1shot plus Bell8shot workflows. No GPU or stress workload, no29-qubitexecution, no fullpresetcatalog run, no original workspace touched.")), Utf8);
        Console.WriteLine("PASS " + checks + " CSV gate-list parse/import/run/provenance/native Bloch assertions. " + scratch);
    }
}
