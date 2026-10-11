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
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class CsvWorkflowExample
{
    static int checks;
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 67108864, RecursionLimit = 100 };
    static Dictionary<string, object> D(params object[] pairs) { return SpinDoctorCalculators.D(pairs); }
    static Dictionary<string, object> M(object value) { return (Dictionary<string, object>)value; }
    static void Check(bool yes, string label) { checks++; if (!yes) throw new Exception("FAIL " + label); }
    static void Near(object value, double expected, string label) { Check(Math.Abs(Convert.ToDouble(value, CultureInfo.InvariantCulture) - expected) < 1e-12, label); }
    static string Hash(byte[] bytes) { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    static bool ReadableStamp(string text, string utc, bool matchedFinal)
    {
        int begin = matchedFinal ? text.LastIndexOf("READABLE MATCHED COMPLETION DETAILS:", StringComparison.Ordinal) : 0;
        if (begin < 0 || utc.Length == 0) return false;
        int stamp = text.IndexOf("Execution completion stamp:", begin, StringComparison.Ordinal);
        if (stamp < 0) return false;
        int end = text.IndexOf('\n', stamp); if (end < 0) end = text.Length;
        return text.Substring(stamp, end - stamp).Contains(utc);
    }
    static Dictionary<string, object> Native(TerminalSession session, string command)
    { Dictionary<string, object> response = M(session.Execute(command)); Check((bool)response["ok"], "Native command accepted: " + command); return response; }
    static Dictionary<string, object> Finished(TerminalSession session, string id)
    {
        DateTime stop = DateTime.UtcNow.AddSeconds(30); Dictionary<string, object> job;
        do { job = M(M(session.GetStatus())["sequence"])["job"] as Dictionary<string, object>; if (job != null && !(bool)job["active"]) { Check((string)job["id"] == id && (string)job["phase"] == "completed", "CSV sequence completed with matching native identity"); return job; } Thread.Sleep(10); } while (DateTime.UtcNow < stop);
        throw new Exception("CSV sequence did not finish within bounded example time.");
    }
    static Dictionary<string, object> WorkCounters(TerminalSession session)
    {
        Dictionary<string, object> status = M(session.GetStatus()), circuit = M(status["circuit"]);
        return D("actualCpuStates", status["actualCpuStates"], "actualGpuStates", status["actualGpuStates"], "verifiedSamples", status["verifiedSamples"], "measurementShots", status["measurementShots"], "quantumHardwareGates", status["quantumHardwareGates"], "circuitCounter", circuit["counter"], "circuitLifetime", circuit["lifetime"]);
    }
    public static void Run(string source, string output, string fixtures)
    {
        if (Directory.Exists(output)) throw new ArgumentException("Use fresh combined CSV example directory."); Directory.CreateDirectory(output); string evidence = Path.Combine(output, "native-evidence"), copiedInputs = Path.Combine(output, "inputs"); Directory.CreateDirectory(evidence); Directory.CreateDirectory(copiedInputs);
        string workspace = Path.Combine(output, "isolated-native-workspace"); Directory.CreateDirectory(Path.Combine(workspace, "quantum"));
        foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) File.Copy(path, Path.Combine(workspace, "quantum", Path.GetFileName(path)));
        foreach (string name in new string[] { "SPINDOCTOR_PRESETS.json", "EquationCatalogue.json", "QUANTUM_TERMINAL.html" }) File.Copy(Path.Combine(source, name), Path.Combine(workspace, name));
        List<object> reportRecords = new List<object>(), fits = new List<object>(), circuitRuns = new List<object>(), reportFiles = new List<object>();
        StringBuilder completeTxt = new StringBuilder("SpinDoctor " + TerminalSession.ProductVersion + " - actual CSV equations and circuits\r\n\r\nMeasurement tables below are synthetic demonstration data, not observed weather or device measurements. Their linear equations are empirical in-sample fits, with no holdout validation, causal proof or validated forecasts. CSV gate lists are explicit ordered circuit data, simulated only by explicit q.input run actions. All exported reports resolve preserved matching native archives; export performs no new fitting or simulation.\r\n\r\n");
        string pdfName = "SpinDoctor-3.1.2-CSV-Equations-and-Circuits.pdf", txtName = "SpinDoctor-3.1.2-CSV-Equations-and-Circuits.txt", csvName = "SpinDoctor-3.1.2-CSV-Equations-and-Circuits.csv";
        Dictionary<string, object> beforeExport, afterExport, equationRepresentations; byte[] pdfBytes;
        using (TerminalSession session = new TerminalSession(workspace, false))
        {
            string[] names = new string[] { "weather-example.csv", "gate-measurements.csv", "circuit-measurements.csv" };
            string[] fields = new string[] { "target=temperature features=[humidity,pressure] target-unit=degC feature-units=[percent,hPa]", "target=success_probability features=[control_amplitude,pulse_ns] target-unit=probability feature-units=[relative,ns]", "target=success_probability features=[depth,two_qubit_gates] target-unit=probability feature-units=[count,count]" };
            for (int i = 0; i < names.Length; i++)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(fixtures, names[i])); File.WriteAllBytes(Path.Combine(copiedInputs, names[i]), bytes);
                Dictionary<string, object> attachment = M(M(session.AttachInput(names[i], Utf8.GetString(bytes)))["result"]); Check((bool)attachment["tableValid"] && (string)attachment["sha256"] == Hash(bytes), "Actual table upload preserves original source SHA");
                Dictionary<string, object> table = Native(session, "q.input table " + attachment["id"]); Check(!(bool)M(table["result"])["fittingPerformed"], "Table inspection does not fit automatically");
                Dictionary<string, object> response = Native(session, "q.input fit " + attachment["id"] + " " + fields[i]), result = M(response["result"]); string id = (string)response["reportId"];
                Check((int)result["usedRows"] == 24 && (int)result["excludedRows"] == (i == 0 ? 2 : 0), "Actual native synthetic fixture fit uses only its complete numeric rows");
                Check((string)M(result["source"])["sha256"] == Hash(bytes) && (int)result["physicalQuantumOperations"] == 0 && (int)result["measurementShots"] == 0, "Fitted equation binds original data with no quantum/shot credit");
                Dictionary<string, object> reference = D("type", "command", "id", id, "stage", "completed"); reportRecords.Add(D("nativeArchive", reference));
                string raw = session.CommandJson(id, "completed"), text = session.CommandPrintout(id, "completed"), csv = session.CommandCsv(id, "completed");
                Check(ReadableStamp(text, (string)M(Json.DeserializeObject(raw))["commandCompletedUtc"], false), "Readable fit TXT agrees with authentic synchronous completion UTC");
                string stem = i == 0 ? "Weather-Equation" : i == 1 ? "Gate-Measurement-Equation" : "Circuit-Measurement-Equation";
                File.WriteAllText(Path.Combine(evidence, stem + "-native.json"), raw, Utf8); File.WriteAllText(Path.Combine(output, stem + ".txt"), text, Utf8); File.WriteAllText(Path.Combine(output, stem + ".csv"), csv, Utf8); completeTxt.AppendLine(text);
                fits.Add(D("input", names[i], "nativeArchive", reference, "sourceSha256", Hash(bytes), "result", result, "archiveSha256", Hash(Utf8.GetBytes(raw))));
                reportFiles.Add(D("txt", stem + ".txt", "txtSha256", Hash(Utf8.GetBytes(text)), "csv", stem + ".csv", "csvSha256", Hash(Utf8.GetBytes(csv))));
            }
            string[] gateNames = new string[] { "gate-rotated-qubit.csv", "gate-bell.csv" };
            for (int i = 0; i < gateNames.Length; i++)
            {
                string content = File.ReadAllText(Path.Combine(fixtures, gateNames[i]), Utf8); if (i == 0) content = content.Replace("1024", "8");
                string originalName = i == 0 ? "gate-rotated-qubit-8-shots.csv" : gateNames[i]; byte[] bytes = Utf8.GetBytes(content); File.WriteAllBytes(Path.Combine(copiedInputs, originalName), bytes);
                Dictionary<string, object> attachment = M(M(session.AttachInput(originalName, content))["result"]); Check((bool)attachment["parseValid"] && (string)attachment["format"] == "csv-gates", "Explicit actual CSV gate input passes inert preview");
                Dictionary<string, object> accepted = Native(session, "q.input run " + attachment["id"]); string parentReportId = (string)accepted["reportId"], jobId = (string)M(M(accepted["result"])["job"])["id"];
                Dictionary<string, object> sequence = Finished(session, jobId); int index = (int)sequence["totalCommands"] - 1;
                Check((string)sequence["sourceSha256"] == Hash(bytes) && (string)sequence["sourceFormat"] == "csv-gates", "Native input-run sequence binds original CSV source SHA and format");
                string raw = session.SequenceJson(jobId, index, "completed"); Dictionary<string, object> archive = M(Json.DeserializeObject(raw)), result = M(M(archive["finalResponse"])["result"]);
                int expectedShots = i == 0 ? 8 : 1024; Check((string)result["phase"] == "completed" && !(bool)result["active"] && Convert.ToInt32(result["completedShots"], CultureInfo.InvariantCulture) == expectedShots, "Actual native CSV circuit uses declared shots and matched completion");
                IList rows = (IList)M(result["coherentSpinPreview"])["rows"]; Check(rows.Count == (i == 0 ? 1 : 2), "Actual CSV simulation retains native premeasurement Bloch rows");
                if (i == 0) { Near(((IList)M(rows[0])["bloch"])[0], 1, "Rotated CSV native Bloch arrow points +X"); Near(M(rows[0])["purity"], 1, "Rotated CSV native local state is pure"); }
                else { foreach (object row in rows) { foreach (object component in (IList)M(row)["bloch"]) Near(component, 0, "Bell CSV native local Bloch zero is legitimate"); Near(M(row)["purity"], .5, "Bell CSV native local state is mixed"); } }
                Dictionary<string, object> reference = D("type", "sequence", "id", jobId, "index", index, "stage", "completed"); reportRecords.Add(D("nativeArchive", reference));
                string text = session.SequencePrintout(jobId, index, "completed"), csv = session.SequenceCsv(jobId, index, "completed"); string stem = i == 0 ? "Rotated-CSV-Circuit-8-Shots" : "Bell-CSV-Circuit-1024-Shots";
                string nativeStop = result.ContainsKey("completedUtc") && Convert.ToString(result["completedUtc"], CultureInfo.InvariantCulture).Length > 0 ? Convert.ToString(result["completedUtc"], CultureInfo.InvariantCulture) : (string)archive["completedUtc"];
                Check(ReadableStamp(text, nativeStop, true), "Readable final CSV circuit TXT agrees with authentic native stop or observed completion barrier UTC");
                File.WriteAllText(Path.Combine(evidence, stem + "-native.json"), raw, Utf8); File.WriteAllText(Path.Combine(evidence, stem + "-parent-native.json"), session.CommandJson(parentReportId, "completed"), Utf8);
                File.WriteAllText(Path.Combine(output, stem + ".txt"), text, Utf8); File.WriteAllText(Path.Combine(output, stem + ".csv"), csv, Utf8); completeTxt.AppendLine(text);
                circuitRuns.Add(D("input", originalName, "sourceSha256", Hash(bytes), "nativeArchive", reference, "parentReportId", parentReportId, "sequence", sequence, "result", result, "archiveSha256", Hash(Utf8.GetBytes(raw))));
                reportFiles.Add(D("txt", stem + ".txt", "txtSha256", Hash(Utf8.GetBytes(text)), "csv", stem + ".csv", "csvSha256", Hash(Utf8.GetBytes(csv))));
            }
            beforeExport = WorkCounters(session);
            Dictionary<string, object> request = D("kind", "session", "view", "details", "records", reportRecords.ToArray(), "status", D());
            equationRepresentations = session.ReportEquations(request);
            Check((string)equationRepresentations["schema"] == "SPINDOCTOR/REPORT-EQUATIONS/1", "Guarded native equation representations have a declared schema");
            IList equations = (IList)equationRepresentations["representations"]; Check(equations.Count == 5, "All three fitted and two simulated native returns have equation representations");
            for (int i = 0; i < equations.Count; i++)
            {
                Dictionary<string, object> equation = M(equations[i]), native = M(i < 3 ? fits[i] : circuitRuns[i - 3]);
                Check(Convert.ToString(equation["equationText"], CultureInfo.InvariantCulture).Length > 0 && Convert.ToString(equation["mathNotation"], CultureInfo.InvariantCulture).Length > 0, "Native equation text and conventional mathematical notation are both present");
                Check((string)equation["sourceSha256"] == (string)native["sourceSha256"] && (string)equation["nativeArchiveSha256"] == (string)native["archiveSha256"], "Both native equation representations bind exact source and immutable archive hashes");
            }
            string exportedCsv = session.ReportCsv(request); pdfBytes = session.ReportPdf(request); afterExport = WorkCounters(session);
            Check(Json.Serialize(beforeExport) == Json.Serialize(afterExport), "Equation/PDF/CSV export does not repeat native fits, simulation, verification or draws");
            File.WriteAllText(Path.Combine(output, csvName), exportedCsv, Utf8); File.WriteAllBytes(Path.Combine(output, pdfName), pdfBytes);
            File.WriteAllText(Path.Combine(evidence, "report-request.json"), Json.Serialize(request), Utf8);
            File.WriteAllText(Path.Combine(evidence, "report-equations.json"), Json.Serialize(equationRepresentations), Utf8);
        }
        File.WriteAllText(Path.Combine(output, txtName), completeTxt.ToString(), Utf8);
        Dictionary<string, object> sourceHashes = new Dictionary<string, object>(); foreach (string path in Directory.GetFiles(Path.Combine(source, "quantum"), "*.cs")) sourceHashes[Path.GetFileName(path)] = Hash(File.ReadAllBytes(path));
        File.WriteAllText(Path.Combine(output, "combined-csv-workflow-proof.json"), Json.Serialize(D("passed", true, "checks", checks, "version", TerminalSession.ProductVersion, "sourceHashes", sourceHashes, "fits", fits.ToArray(), "circuitRuns", circuitRuns.ToArray(), "perReportFiles", reportFiles.ToArray(),
            "pdf", pdfName, "pdfSha256", Hash(pdfBytes), "txt", txtName, "txtSha256", Hash(Utf8.GetBytes(completeTxt.ToString())), "csv", csvName, "csvSha256", Hash(File.ReadAllBytes(Path.Combine(output, csvName))), "equationRepresentations", equationRepresentations, "workBeforeExport", beforeExport, "workAfterExport", afterExport,
            "scope", "One bounded fresh isolated native example: three actual24-row synthetic measurement fits, explicit attached CSV rotated8shot circuit and Bell1024shot circuit. No observedweather/hardwareclaims, no stress/GPU/29-qubit/fullcatalog run; originals untouched. Export resolves matching immutable native archives without new numerical work.")), Utf8);
        File.WriteAllText(Path.Combine(output, "README.txt"), "SpinDoctor 3.1.2 actual native CSV workflow examples. The ONE combined detailed PDF/TXT/CSV shows synthetic weather, gate-measurement and circuit-measurement fitted equations, then actual classical gate-list CSV simulation: rotated RY(pi/2)8shots has a +X Bloch arrow; Bell1024shots has legitimate zero local vectors and purity1/2. Fitting is empirical/in-sample with no holdout or causal/forecast validation. Each TXT has a matching CSV, pinned to the same preserved native archive. Native-derived equation text and conventional mathematical representations retain the original source/archive provenance. The UI permits local editable equation drafts, copy and reset; edits never alter authenticated native reports or execute calculations. To change a calculation, edit the source input and explicitly Fit/Import/Run. Export never reruns native fits or simulations. Exact input bytes and hashes are saved under inputs; native-evidence is validation-only.\r\n", Utf8);
        Console.WriteLine("PASS " + checks + " combined native CSV workflow/example/export assertions. " + output);
    }
}
