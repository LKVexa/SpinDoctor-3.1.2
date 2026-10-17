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

public static class CsvInputRegressions
{
    static int checks;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 33554432, RecursionLimit = 100 };
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    static Dictionary<string, object> D(params object[] pairs) { return SpinDoctorCalculators.D(pairs); }
    static Dictionary<string, object> M(object value) { return (Dictionary<string, object>)value; }
    static void Check(bool condition, string label) { checks++; if (!condition) throw new Exception("FAIL " + label); }
    static void Near(object value, double expected, double tolerance, string label) { double number = Convert.ToDouble(value, CultureInfo.InvariantCulture); Check(QuantumMath.IsFinite(number) && Math.Abs(number - expected) <= tolerance, label); }
    static string Hash(byte[] bytes) { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static Dictionary<string, object> Upload(InputAttachmentManager inputs, string name, string content) { return inputs.Upload(name, content); }
    static Dictionary<string, object> Fit(InputAttachmentManager inputs, Dictionary<string, object> attachment, params string[] fields)
    {
        List<string> words = new List<string>(new string[] { "q.input", "fit", (string)attachment["id"] }); words.AddRange(fields); return M(inputs.Execute(words.ToArray()));
    }
    static void Refused(Action action, string fragment, string label)
    {
        bool refused = false; try { action(); } catch (ArgumentException e) { refused = e.Message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0; } Check(refused, label);
    }
    static string Quote(string cell) { return "\"" + cell.Replace("\"", "\"\"") + "\""; }
    static Dictionary<string, object> Native(TerminalSession session, string command)
    {
        Dictionary<string, object> response = M(session.Execute(command)); Check((bool)response["ok"], "native command accepted: " + command); return response;
    }
    public static void Run(string source, string scratch, string fixtures, bool pdf)
    {
        if (Directory.Exists(scratch)) throw new ArgumentException("Use fresh CSV test scratch."); Directory.CreateDirectory(scratch);
        InputAttachmentManager inputs = new InputAttachmentManager(Path.Combine(scratch, "parser"));
        string commandCsv = "\uFEFFcommand\r\n" + Quote("q.math norm amplitudes=[5,12]") + "\r\n" + Quote("q.math bloch amplitudes=[0,1]") + "\r\n";
        Dictionary<string, object> command = Upload(inputs, "commands.csv", commandCsv);
        Check((bool)command["parseValid"] && (bool)command["executable"] && (string)command["format"] == "csv-sequence", "BOM/quoted commas preserve command CSV");
        Check((string)command["sha256"] == Hash(Utf8.GetBytes(commandCsv)) && inputs.ExportText((string)command["id"]) == commandCsv, "exact uploaded UTF-8 source and hash preserved");
        Dictionary<string, object> compiled = inputs.CompileSource("commands.csv", Utf8.GetBytes(commandCsv)); string[] commands = (string[])compiled["commands"];
        Check(commands.Length == 2 && commands[0] == "q.math norm amplitudes=[5,12]" && commands[1] == "q.math bloch amplitudes=[0,1]", "compile returns exact inert command cells");
        string escaped = "q.lesson show \"literal\""; string escapedCsv = "command\n" + Quote(escaped);
        Check(((string[])inputs.CompileSource("escaped.csv", Utf8.GetBytes(escapedCsv))["commands"])[0] == escaped, "escaped RFC4180 quotes round-trip without execution");
        string longCommand = "q.help " + new string('x', 4089);
        Check(((string[])inputs.CompileSource("long.csv", Utf8.GetBytes("command\n" + Quote(longCommand)))["commands"])[0].Length == 4096, "command CSV allows the same 4096-character sequence bound as TXT/JSON");
        Refused(delegate { inputs.CompileSource("long.csv", Utf8.GetBytes("command\n" + Quote(longCommand + "x"))); }, "4096", "oversized command CSV is bounded");
        Refused(delegate { inputs.CompileSource("bad.csv", Utf8.GetBytes("command\nq.math norm amplitudes=[5,12]")); }, "exactly one", "unquoted comma command reports usable quoting guidance");
        Refused(delegate { inputs.CompileSource("bad.csv", Utf8.GetBytes("command\n\"q.help\nq.status\"")); }, "one bounded", "multiline command cell cannot execute two commands");
        Refused(delegate { inputs.CompileSource("bad.csv", Utf8.GetBytes("command\n\"q.help")); }, "Unterminated", "unterminated quote refused");
        Refused(delegate { inputs.CompileSource("bad.csv", Utf8.GetBytes("command\n\"q.help\"garbage")); }, "Unexpected data", "trailing data after closing quote refused");
        Refused(delegate { inputs.CompileSource("shell.csv", Utf8.GetBytes("command\ncmd /c dir")); }, "documented quantum", "CSV cannot request host-shell execution");
        Refused(delegate { inputs.CompileSource("nested.csv", Utf8.GetBytes("command\nq.folder run path=D:/x")); }, "nested", "nested folder commands refused");
        string parameters = "theta\npi/4\npi/2\npi";
        Dictionary<string, object> sweep = Upload(inputs, "sweep.csv", parameters);
        Check((bool)sweep["parseValid"] && (bool)sweep["requiresBase"] && !(bool)sweep["executable"], "parameter CSV remains distinct and inert");
        Dictionary<string, object> legacyGate = Upload(inputs, "legacy-gate-parameter.csv", "gate\npi/4\npi/2");
        Check((bool)legacyGate["parseValid"] && (bool)legacyGate["requiresBase"] && (string)legacyGate["format"] == "csv-parameters", "Legacy scalar circuit parameter named gate remains a parameter sweep");
        Dictionary<string, object> legacyGateN = Upload(inputs, "legacy-gate-n-parameters.csv", "gate,n\npi/4,2\npi/2,3");
        Check((bool)legacyGateN["parseValid"] && (bool)legacyGateN["requiresBase"] && (string)legacyGateN["format"] == "csv-parameters", "Legacy gate,n parameter sweep is not misclassified as executable gate data");
        Refused(delegate { inputs.CompileSource("sweep.csv", Utf8.GetBytes(parameters)); }, "explicit base", "parameter CSV is not guessed into a folder circuit");
        Dictionary<string, object> baseCircuit = Upload(inputs, "base.json", "{\"name\":\"csv_base\",\"qubits\":1,\"parameters\":{\"theta\":\"0\"},\"gates\":[{\"name\":\"ry\",\"angles\":[\"theta\"],\"qubits\":[0]}],\"shots\":1}");
        Dictionary<string, object> evaluated = M(inputs.Execute(new string[] { "q.input", "evaluate", (string)sweep["id"], "row", "1", "base", (string)baseCircuit["id"] }));
        Near(((Dictionary<string, double>)evaluated["parameterValues"])["theta"], Math.PI / 2, 1e-15, "explicit parameter row uses preserved safe arithmetic grammar");
        string tooLongExpression = "0" + new string(' ', 1024); Dictionary<string, object> badParameter = Upload(inputs, "parameter-limit.csv", "theta\n" + Quote(tooLongExpression));
        Check(!(bool)badParameter["parseValid"] && ((string)badParameter["error"]).Contains("1024"), "parameter expressions retain smaller 1024-character cell ceiling");
        string data = "date,humidity,pressure,temperature\r\n";
        for (int i = 0; i < 20; i++) { double humidity = 40 + (i % 5) * 6, pressure = 990 + (i / 5) * 8, temperature = 15 - .12 * humidity + .03 * pressure; data += "2026-10-" + (i + 1).ToString("00") + "," + humidity.ToString("R", CultureInfo.InvariantCulture) + "," + pressure.ToString("R", CultureInfo.InvariantCulture) + "," + temperature.ToString("R", CultureInfo.InvariantCulture) + "\r\n"; }
        data += "unknown,,1000,20\r\nunknown,NaN,1000,20\r\nunknown,55,Infinity,20\r\nunknown,55,1000,1/sqrt(2)\r\n";
        Dictionary<string, object> weather = Upload(inputs, "weather.csv", "\uFEFF" + data), table = M(inputs.Execute(new string[] { "q.input", "table", (string)weather["id"] }));
        Check((bool)weather["tableValid"] && !(bool)weather["executable"] && !(bool)table["fittingPerformed"], "measurement weather CSV is available as data but does not execute or fit on upload/inspection");
        Check((int)table["rowCount"] == 24 && ((IList)table["rowsPreview"]).Count == 8 && !(bool)table["rowsPreviewComplete"], "table preview is bounded and total rows exact");
        Dictionary<string, object> dateColumn = M(((IList)table["columns"])[0]); Check((int)dateColumn["numericCount"] == 0 && (int)dateColumn["invalidCount"] == 24, "date strings are not silently converted or selected");
        Dictionary<string, object> fit = Fit(inputs, weather, "target=temperature", "features=[humidity,pressure]", "target-unit=degC", "feature-units=[percent,hPa]");
        Check((string)fit["resultKind"] == "empirical-csv-fit" && (int)fit["usedRows"] == 20 && (int)fit["excludedRows"] == 4 && ((IList)fit["excluded"]).Count == 4, "complete-case numeric fit records all exclusions");
        Near(fit["intercept"], 15, 1e-10, "independent known multivariate intercept");
        Near(M(((IList)fit["coefficients"])[0])["coefficient"], -.12, 1e-12, "independent known humidity coefficient");
        Near(M(((IList)fit["coefficients"])[1])["coefficient"], .03, 1e-12, "independent known pressure coefficient");
        Near(M(fit["metrics"])["rSquared"], 1, 1e-12, "exact deterministic fixture R squared"); Near(M(fit["metrics"])["rmse"], 0, 1e-12, "exact deterministic fixture RMSE");
        Check(((IList)fit["dataRows"]).Count == 20 && (bool)fit["dataRowsComplete"] && (int)fit["physicalQuantumOperations"] == 0 && (int)fit["measurementShots"] == 0, "every actual fit row retained without quantum or shot credit");
        Check((string)M(fit["source"])["sha256"] == (string)weather["sha256"] && (string)M(fit["source"])["id"] == (string)weather["id"], "fitted provenance uses the exact original attachment");
        foreach (object raw in (IList)fit["dataRows"]) { Dictionary<string, object> row = M(raw); Near(row["fitted"], Convert.ToDouble(row["observed"]), 1e-11, "actual fitted row matches known equation"); Near(row["residual"], Convert.ToDouble(row["observed"]) - Convert.ToDouble(row["fitted"]), 0, "residual sign is observed minus fitted"); }
        Dictionary<string, object> indexed = Fit(inputs, weather, "target=3", "features=[1,2]"); Near(indexed["intercept"], 15, 1e-10, "column indexes reproduce named fit"); Check((string)M(indexed["target"])["unit"] == "unknown", "units not guessed");
        string large = "offset,value\n"; for (int i = 0; i < 12; i++) large += (1e12 + i).ToString("R", CultureInfo.InvariantCulture) + "," + (7 + 2 * i).ToString(CultureInfo.InvariantCulture) + "\n";
        Dictionary<string, object> largeFit = Fit(inputs, Upload(inputs, "offset.csv", large), "target=value", "features=[offset]"); Near(M(((IList)largeFit["coefficients"])[0])["coefficient"], 2, 1e-12, "centering recovers large-offset slope"); Near(M(largeFit["metrics"])["rmse"], 0, 1e-10, "centered fitted evaluation avoids large-offset cancellation");
        Dictionary<string, object> constant = Fit(inputs, Upload(inputs, "constant.csv", "x,y\n1,7\n2,7\n3,7\n4,7"), "target=y", "features=[x]");
        Check((bool)M(constant["metrics"])["constantTarget"] && M(constant["metrics"])["rSquared"] == null, "constant target R squared explicitly undefined"); Near(constant["intercept"], 7, 1e-12, "constant target actual intercept");
        Dictionary<string, object> noisy = Fit(inputs, Upload(inputs, "noisy.csv", "x,y\n1,3\n2,5.5\n3,6\n4,8.5\n5,10"), "target=y", "features=[x]");
        Near(noisy["intercept"], 1.5, 1e-12, "independent analytic noisy intercept"); Near(M(((IList)noisy["coefficients"])[0])["coefficient"], 1.7, 1e-12, "independent analytic noisy slope");
        Near(M(noisy["metrics"])["rmse"], .4, 1e-12, "independent analytic noisy RMSE"); Near(M(noisy["metrics"])["mae"], .32, 1e-12, "independent analytic noisy MAE"); Near(M(noisy["metrics"])["rSquared"], 1 - .8 / 29.7, 1e-12, "independent analytic noisy R squared");
        Dictionary<string, object> tinyTarget = Fit(inputs, Upload(inputs, "tiny-target.csv", "x,y\n1,3e-200\n2,5.5e-200\n3,6e-200\n4,8.5e-200\n5,1e-199"), "target=y", "features=[x]");
        Near(M(tinyTarget["metrics"])["rSquared"], 1 - .8 / 29.7, 1e-12, "scaled norms preserve tiny nonconstant target R squared"); Check(Convert.ToDouble(M(tinyTarget["metrics"])["rmse"]) > 0 && !(bool)M(tinyTarget["metrics"])["residualSumSquaresRepresentable"], "underflow RSS does not hide genuine nonzero RMSE and carries explicit representability flag");
        StringBuilder wide = new StringBuilder(); for (int j = 1; j <= 16; j++) wide.Append("x").Append(j).Append(","); wide.Append("target\n");
        for (int i = 0; i < 32; i++) { double targetValue = 7; for (int j = 1; j <= 16; j++) { int bits = i & j, parity = 0; while (bits != 0) { parity ^= bits & 1; bits >>= 1; } int value = parity == 0 ? 1 : -1; wide.Append(value).Append(','); targetValue += value * j / 10.0; } wide.Append(targetValue.ToString("R", CultureInfo.InvariantCulture)).Append('\n'); }
        Dictionary<string, object> widest = Fit(inputs, Upload(inputs, "sixteen.csv", wide.ToString()), "target=16", "features=[0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15]");
        Check((int)widest["rank"] == 17 && ((IList)widest["coefficients"]).Count == 16, "sixteen-predictor bound solves a full-rank independent Walsh fixture");
        for (int j = 0; j < 16; j++) Near(M(((IList)widest["coefficients"])[j])["coefficient"], (j + 1) / 10.0, 1e-12, "known independent Walsh coefficient " + j);
        Refused(delegate { Fit(inputs, weather, "target=temperature", "features=[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]"); }, "1..16", "seventeen predictors refused before allocation/fit");
        Refused(delegate { Fit(inputs, Upload(inputs, "collinear.csv", "a,b,y\n1,2,1\n2,4,2\n3,6,3\n4,8,4\n5,10,5"), "target=y", "features=[a,b]"); }, "rank deficient", "collinear predictors do not publish an arbitrary equation");
        Refused(delegate { Fit(inputs, Upload(inputs, "constant-feature.csv", "a,y\n1,2\n1,3\n1,4"), "target=y", "features=[a]"); }, "constant", "constant predictor refused");
        Refused(delegate { Fit(inputs, Upload(inputs, "tiny.csv", "a,y\n1,2\n2,3"), "target=y", "features=[a]"); }, "more complete", "insufficient rows refused");
        Refused(delegate { Fit(inputs, weather, "target=temperature", "features=[temperature]"); }, "target", "target leakage feature refused");
        Refused(delegate { Fit(inputs, weather, "target=temperature", "features=[humidity,humidity]"); }, "Duplicate", "duplicate features refused");
        Refused(delegate { Fit(inputs, weather, "target=missing", "features=[humidity]"); }, "not found", "unknown target refused");
        Refused(delegate { Fit(inputs, weather, "target=temperature", "features=[date]"); }, "more complete", "dates require an explicitly numeric feature rather than guessed elapsed time");
        Refused(delegate { Fit(inputs, weather, "target=temperature", "features=[humidity]", "feature-units=[one,two]"); }, "match", "unit order/count validated");
        Dictionary<string, object> duplicateHeader = Upload(inputs, "duplicate.csv", "X,x,y\n1,2,3"); Check(!(bool)duplicateHeader["tableValid"], "ambiguous duplicate headers refused");
        Dictionary<string, object> malformed = Upload(inputs, "width.csv", "x,y\n1,2,3"); Check(!(bool)malformed["tableValid"], "inconsistent data-table width refused");
        string headerCsv = "\"Humidity (%)\",\"Pressure, hPa\",\"Temp C\",notes\n40,990,39.9,\"line one\nline two\"\n45,995,39.45,\"a \"\"quote\"\"\"\n50,1002,39.06,note\n55,1001,38.43,note\n";
        Dictionary<string, object> labelled = Upload(inputs, "labels.csv", headerCsv), labelledFit = Fit(inputs, labelled, "target=2", "features=[0,1]", "target-unit=\"degrees C\"", "feature-units=[\"relative percent\",\"hPa\"]");
        Check((string)M(labelledFit["target"])["name"] == "Temp C" && (string)M(labelledFit["target"])["unit"] == "degrees C", "indexed headers with spaces/commas and quoted unit labels supported");
        Check((int)labelledFit["usedRows"] == 4, "quoted multiline nonselected text remains inert table data");
        Dictionary<string, object> namedLabels = Fit(inputs, labelled, "target=\"Temp C\"", "features=[\"Humidity (%)\",\"Pressure, hPa\"]", "feature-units=[\"percent, relative\",\"hPa\"]");
        Check((string)M(((IList)namedLabels["features"])[1])["name"] == "Pressure, hPa" && (string)M(((IList)namedLabels["features"])[0])["unit"] == "percent, relative", "quoted selector/unit lists preserve embedded commas");
        string many = "x,y\n"; for (int i = 0; i < 4097; i++) many += i.ToString() + ",1\n";
        Dictionary<string, object> manyRows = Upload(inputs, "many.csv", many); Check(!(bool)manyRows["tableValid"], "table row ceiling is checked before any fit");
        // A bounded native run demonstrates command CSV calculators and actual empirical fitting.
        string workspace = Path.Combine(scratch, "native"); Directory.CreateDirectory(Path.Combine(workspace, "quantum"));
        foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) File.Copy(path, Path.Combine(workspace, "quantum", Path.GetFileName(path)));
        foreach (string name in new string[] { "SPINDOCTOR_PRESETS.json", "EquationCatalogue.json", "QUANTUM_TERMINAL.html" }) File.Copy(Path.Combine(source, name), Path.Combine(workspace, name));
        Dictionary<string, object> nativeFit = null; string nativeFitReportId = "", commandJobId = ""; byte[] pdfBytes = null;
        using (TerminalSession session = new TerminalSession(workspace, false))
        {
            Dictionary<string, object> before = M(session.GetStatus());
            string fixture = File.ReadAllText(Path.Combine(fixtures, "weather-example.csv"), Utf8);
            Dictionary<string, object> attachment = M(M(session.AttachInput("weather-example.csv", fixture))["result"]);
            Check((bool)attachment["tableValid"], "native weather upload has inert numeric-table preview");
            string fitCommand = "q.input fit " + attachment["id"] + " target=temperature features=[humidity,pressure] target-unit=degC feature-units=[percent,hPa]";
            Dictionary<string, object> response = Native(session, fitCommand); nativeFit = M(response["result"]); nativeFitReportId = (string)response["reportId"];
            Check((int)nativeFit["usedRows"] == 24 && (int)nativeFit["excludedRows"] == 2, "native sample has 24 actual generated rows plus two recorded omissions");
            string printout = session.CommandPrintout(nativeFitReportId, "completed"); Check(printout.Contains("COMMAND EXECUTION: COMPLETED") && printout.Contains("empirical-csv-fit") && printout.Contains("residualSumSquares"), "native fit TXT contains actual equation/diagnostics and completion stamp");
            File.WriteAllText(Path.Combine(scratch, "SpinDoctor-3.1.2-Weather-Equation-Example.txt"), printout, Utf8);
            File.WriteAllText(Path.Combine(scratch, "SpinDoctor-3.1.2-Weather-Equation-Example.csv"), session.CommandCsv(nativeFitReportId, "completed"), Utf8);
            File.WriteAllText(Path.Combine(scratch, "native-weather-fit-archive.json"), session.CommandJson(nativeFitReportId, "completed"), Utf8);
            Check(session.CommandJson(nativeFitReportId, "latest").Contains((string)M(nativeFit["source"])["sha256"]), "native fit immutable archive keeps exact source SHA");
            Dictionary<string, object> nativeCommand = M(M(session.AttachInput("commands.csv", commandCsv))["result"]), accepted = Native(session, "q.input run " + nativeCommand["id"]);
            commandJobId = (string)M(M(accepted["result"])["job"])["id"]; DateTime limit = DateTime.UtcNow.AddSeconds(30); Dictionary<string, object> job;
            do { job = M(M(session.GetStatus())["sequence"])["job"] as Dictionary<string, object>; if (!(bool)job["active"]) break; Thread.Sleep(10); } while (DateTime.UtcNow < limit);
            Check(job != null && !(bool)job["active"] && (string)job["phase"] == "completed" && (int)job["completedCommands"] == 2, "actual command CSV completes ordered two-calculation sequence");
            Dictionary<string, object> norm = M(M(M(Json.DeserializeObject(session.SequenceJson(commandJobId, 0, "latest")))["response"])["result"]); Near(norm["norm"], 13, 1e-12, "CSV native norm prints actual nonzero number");
            Dictionary<string, object> bloch = M(M(M(Json.DeserializeObject(session.SequenceJson(commandJobId, 1, "latest")))["response"])["result"]); Near(((IList)bloch["bloch"])[2], -1, 1e-12, "CSV native Bloch arrow uses actual down input");
            Dictionary<string, object> after = M(session.GetStatus()); Check(Convert.ToString(before["measurementShots"]) == Convert.ToString(after["measurementShots"]) && Convert.ToString(after["quantumHardwareGates"]) == "0", "CSV inspection/fits/calculators do not invent measurement or quantum-gate credits");
            if (pdf)
            {
                Dictionary<string, object> request = D("kind", "selected", "view", "details", "records", new object[] { D("nativeArchive", D("type", "command", "id", nativeFitReportId, "stage", "completed")) }, "status", D());
                pdfBytes = session.ReportPdf(request); File.WriteAllBytes(Path.Combine(scratch, "SpinDoctor-3.1.2-Weather-Equation-Example.pdf"), pdfBytes);
                Check(pdfBytes.Length > 1000, "actual native weather fitted equation exports PDF without rerunning fit");
                Check(Json.Serialize(nativeFit) == Json.Serialize(M(M(Json.DeserializeObject(session.CommandJson(nativeFitReportId, "latest")))["response"])["result"]), "PDF export does not alter native fitted data");
            }
        }
        Dictionary<string, object> hashes = new Dictionary<string, object>(); foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) hashes[Path.GetFileName(path)] = Hash(File.ReadAllBytes(path));
        File.WriteAllText(Path.Combine(scratch, "csv-input-proof.json"), Json.Serialize(D("passed", true, "checks", checks, "productVersion", TerminalSession.ProductVersion, "sourceHashes", hashes, "nativeFit", nativeFit, "nativeFitReportId", nativeFitReportId, "commandCsvJobId", commandJobId,
            "pdfGenerated", pdf, "pdfSha256", pdfBytes == null ? null : Hash(pdfBytes), "scope", "Bounded isolated classical CSV parsing/OLS regression and two calculator commands. No GPU, stress workload, external data lookup, user workspace, automatic upload execution or new quantum shots.")), Utf8);
        Console.WriteLine("PASS " + checks + " CSV input/table/empirical-fit/native archive assertions. " + scratch);
    }
}
