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
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace QuettaQuantum
{
    // Attachments are inert UTF-8 data. Parsing never executes terminal commands,
    // source code, includes, shell statements, or instructions in document text.
    public sealed class InputAttachmentManager
    {
        private readonly object sync = new object();
        private readonly string directory, catalogPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly List<Entry> entries = new List<Entry>();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private const int MaximumBytes = 5 * 1024 * 1024, MaximumFiles = 32, MaximumTotalBytes = 30 * 1024 * 1024;
        private sealed class Entry
        {
            public string Id, Filename, Sha, UploadedUtc;
            public int Bytes;
            public Dictionary<string, object> Preview;
        }
        private sealed class GatePlan
        {
            public string Name;
            public string[] Angles;
            public int[] Qubits;
            public bool All;
        }
        private sealed class Plan
        {
            public string Format, Name;
            public int Qubits, Shots = 1;
            public BigInteger InitialBasis;
            public string Basis = "z";
            public readonly Dictionary<string, string> Parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<GatePlan> Gates = new List<GatePlan>();
            public readonly List<string> Warnings = new List<string>();
            public string[] Columns;
            public List<string[]> Rows;
            public string[] ScriptCommands;
        }
        private sealed class MissingVariable : ArgumentException
        {
            public MissingVariable(string name) : base("Missing parameter binding: " + name) { }
        }
        private sealed class NumericTable
        {
            public string[] Columns;
            public List<string[]> Rows;
        }

        public InputAttachmentManager(string workspace)
        {
            if (String.IsNullOrEmpty(workspace)) throw new ArgumentException("Input workspace is required.");
            directory = Path.Combine(workspace, "inputs"); catalogPath = Path.Combine(directory, "catalog.json");
            Directory.CreateDirectory(directory); json.MaxJsonLength = 16777216; json.RecursionLimit = 80;
            if (File.Exists(catalogPath)) LoadCatalog();
        }

        public Dictionary<string, object> Upload(string filename, string content)
        {
            ValidateFilename(filename); if (content == null) throw new ArgumentException("UTF-8 attachment content is required.");
            byte[] bytes = Utf8.GetBytes(content); if (bytes.Length > MaximumBytes) throw new ArgumentOutOfRangeException("content", "Attachment limit is5 MiB of UTF-8 data.");
            lock (sync)
            {
                if (entries.Count >= MaximumFiles) throw new InvalidOperationException("At most32 input attachments are supported. Delete an attachment first.");
                if (TotalBytes() > MaximumTotalBytes - bytes.Length) throw new InvalidOperationException("Input attachments exceed the30 MiB combined UTF-8 source limit. Delete an attachment first.");
                Entry entry = new Entry { Id = Guid.NewGuid().ToString("N"), Filename = filename, Sha = Hash(bytes), Bytes = bytes.Length, UploadedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) };
                entry.Preview = Preview(entry, content);
                string path = SourcePath(entry.Id); File.WriteAllBytes(path, bytes); entries.Add(entry);
                try { SaveCatalog(); }
                catch { entries.Remove(entry); File.Delete(path); throw; }
                return Metadata(entry);
            }
        }
        public Dictionary<string, object> UploadBinary(string filename, byte[] bytes)
        {
            ValidateFilename(filename);
            if (Path.GetExtension(filename).ToLowerInvariant() != ".pdf") throw new ArgumentException("Binary attachments currently supportPDF only.");
            if (bytes == null || bytes.Length >4194304) throw new ArgumentException("PDF source limit is4MiB.");
            lock(sync)
            {
                if(entries.Count>=MaximumFiles||TotalBytes()>MaximumTotalBytes-bytes.Length)throw new ArgumentException("Input catalog file/combined-byte capacity exceeded.");
                Entry entry=new Entry{Id=Guid.NewGuid().ToString("N"),Filename=filename,Sha=Hash(bytes),Bytes=bytes.Length,UploadedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)};
                try
                {
                    SpinDoctorPdfInputs.Extraction extracted=SpinDoctorPdfInputs.Extract(bytes);
                    entry.Preview=Preview(new Entry{Filename=extracted.Filename,Id=entry.Id},extracted.Content);
                    entry.Preview["format"]="pdf/"+entry.Preview["format"];
                    entry.Preview["pdfExtraction"]=D("method",extracted.Method,"programFilename",extracted.Filename,"programSha256",Hash(Utf8.GetBytes(extracted.Content)),"textStreams",extracted.TextStreams);
                }
                catch(Exception error){entry.Preview=D("parseValid",false,"executable",false,"format","pdf","error",error.Message,"warnings",new string[0]);}
                entry.Preview["sourceEncoding"]="binary-pdf";
                string path=SourcePath(entry.Id);File.WriteAllBytes(path,bytes);entries.Add(entry);
                try{SaveCatalog();}catch{entries.Remove(entry);File.Delete(path);throw;}
                return Metadata(entry);
            }
        }
        public byte[] ExportBytes(string id) { lock(sync){return ReadBytes(Find(id));} }
        public string ExportMediaType(string id) { lock(sync){return Path.GetExtension(Find(id).Filename).ToLowerInvariant()==".pdf"?"application/pdf":"text/plain; charset=utf-8";} }
        public string Filename(string id){lock(sync){return Find(id).Filename;}}
        public Dictionary<string, object> GetStatus()
        {
            lock (sync)
            {
                List<object> attachments = new List<object>(); foreach (Entry entry in entries) attachments.Add(Summary(entry));
                return D("attachments", attachments.ToArray(), "count", entries.Count, "maxFileBytes", MaximumBytes, "maxPdfBytes",4194304,"maxFiles", MaximumFiles, "totalBytes", TotalBytes(), "maxTotalBytes", MaximumTotalBytes,
                    "formats", new string[] { "json", "openqasm2", "text-circuit", "csv-parameters", "csv-gates", "csv-data-table", "json-sequence","txt-sequence","csv-sequence","pdf-embedded-or-tagged-program" },
                    "scope", "Original UTF-8 and binary PDF source data with preserved SHA256. Preview and compilation do not execute. Explicit import selects a circuit; q.input run executes a validated program or command sequence with native job barriers. q.input table inspects CSV measurements; q.input fit performs a bounded empirical linear regression only after explicit target/features selection.");
            }
        }
        public object Execute(string[] words)
        {
            if (words == null || words.Length < 2 || !Eq(words[0], "q.input")) throw new ArgumentException("Use q.input help.");
            string command = words[1].ToLowerInvariant();
            lock (sync)
            {
                if (command == "help") { Exact(words, 2); return Help(); }
                if (command == "list") { Exact(words, 2); return GetStatus(); }
                if (command == "table")
                {
                    Exact(words, 3); Entry tableEntry = Find(words[2]);
                    return TableView(tableEntry, ReadTable(tableEntry));
                }
                if (command == "fit")
                {
                    if (words.Length < 5) throw new ArgumentException("Use q.input fit ID target=COLUMN features=[COLUMN,...] [target-unit=UNIT] [feature-units=[UNIT,...]].");
                    Entry fitEntry = Find(words[2]);
                    string[] fields = new string[words.Length - 1]; fields[0] = "q.input"; fields[1] = "fit";
                    Array.Copy(words, 3, fields, 2, words.Length - 3);
                    Dictionary<string, string> options = SpinDoctorCalculators.ParseFields(fields);
                    SpinDoctorCalculators.CheckFields(options, "target features target-unit feature-units");
                    if (!options.ContainsKey("target") || !options.ContainsKey("features")) throw new ArgumentException("Choose target and features explicitly; no equation is inferred during upload.");
                    return FitTable(fitEntry, ReadTable(fitEntry), options);
                }
                if (command == "show")
                {
                    Exact(words, 3); Entry entry = Find(words[2]); string content;
                    if(Path.GetExtension(entry.Filename).Equals(".pdf",StringComparison.OrdinalIgnoreCase)&&!(bool)entry.Preview["parseValid"])return D("attachment",Metadata(entry),"sourcePreview",null,"sourcePreviewComplete",false,"fullExportAvailable",true,"note","The stored original PDF remains available as a binary download. No supported program could be extracted.");
                    content = Read(entry);
                    return D("attachment", Metadata(entry), "sourcePreview", content.Substring(0, Math.Min(32768, content.Length)), "sourcePreviewComplete", content.Length <= 32768, "fullExportAvailable", true);
                }
                if (command == "export")
                {
                    Exact(words, 3); Entry entry = Find(words[2]);
                    if(Path.GetExtension(entry.Filename).Equals(".pdf",StringComparison.OrdinalIgnoreCase))return D("attachment",Metadata(entry),"fullExportAvailable",true,"contentIncluded",false,"mediaType","application/pdf","note","Use the authenticated binary download for the exact original PDF bytes. q.input show displays only the safely extracted program preview.");
                    string content = Read(entry);
                    Dictionary<string, object> result = D("attachment", Metadata(entry), "fullExportAvailable", true, "contentIncluded", entry.Bytes <= 524288);
                    if (entry.Bytes <= 524288) result["content"] = content; else result["note"] = "Use the authenticated raw attachment download for the complete original source.";
                    return result;
                }
                if (command == "delete")
                {
                    Exact(words, 3); Entry entry = Find(words[2]); int index = entries.IndexOf(entry); entries.RemoveAt(index);
                    try { SaveCatalog(); } catch { entries.Insert(index, entry); throw; }
                    // The database commit removes access before deleting this exact
                    // manager-owned file; no user-supplied path reaches the filesystem.
                    File.Delete(SourcePath(entry.Id)); return D("deleted", entry.Id, "filename", entry.Filename);
                }
                if (command == "evaluate")
                {
                    if (words.Length < 3) throw new ArgumentException("Use q.input evaluate ID [row N] [base ID] [name=expression...].");
                    Entry entry = Find(words[2]); int? row = null; string baseId = null; Dictionary<string, string> definitions = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int j = 3; j < words.Length; ++j)
                    {
                        if (Eq(words[j], "row")) { if (row.HasValue || ++j >= words.Length) throw new ArgumentException("Specify one row index."); row = Integer(words[j], 0, 4095); }
                        else if (Eq(words[j], "base")) { if (baseId != null || ++j >= words.Length) throw new ArgumentException("Specify one base attachment."); baseId = words[j]; }
                        else
                        {
                            int equals = words[j].IndexOf('='); if (equals <= 0 || equals == words[j].Length - 1) throw new ArgumentException("Bindings use name=expression without spaces.");
                            string name = ParameterName(words[j].Substring(0, equals)); string expression = words[j].Substring(equals + 1);
                            if (definitions.ContainsKey(name)) throw new ArgumentException("Duplicate parameter override."); SafeExpression.Validate(expression); definitions.Add(name, expression);
                        }
                    }
                    Plan plan = Parse(entry, Read(entry));
                    if (plan.Rows != null && baseId == null) throw new ArgumentException("CSV evaluation requires base BASE_ID and a zero-based row index.");
                    Plan basePlan = plan.Rows == null ? plan : Parse(Find(baseId), Read(Find(baseId)));
                    Dictionary<string, double> defaults = Resolve(basePlan.Parameters, null, true);
                    foreach (string key in definitions.Keys) defaults.Remove(key);
                    Dictionary<string, double> bindings = Resolve(definitions, defaults, false);
                    Dictionary<string, double> overrides = new Dictionary<string, double>(StringComparer.Ordinal); foreach (string key in definitions.Keys) overrides.Add(key, bindings[key]);
                    Dictionary<string, object> spec = Import(entry, row, overrides, baseId);
                    return D("validated", true, "spec", spec, "parameterValues", spec["parameters"], "attachment", Metadata(entry));
                }
                throw new ArgumentException("Unknown input command. Use q.input help.");
            }
        }

        public string ExportText(string id) { lock (sync) { return Read(Find(id)); } }
        public object ImportSpec(string id, int? row) { return ImportSpec(id, row, null, null); }
        public object ImportSpec(string id, int? row, Dictionary<string, double> bindings, string baseId)
        {
            lock (sync) { return Import(Find(id), row, bindings, baseId); }
        }
        public static double EvaluateExpression(string expression, Dictionary<string, double> variables) { return SafeExpression.Evaluate(expression, variables); }

        // Folder runs parse in memory: they neither add persistent attachments nor execute document prose.
        public Dictionary<string, object> CompileSource(string filename, byte[] bytes)
        {
            if(bytes==null || bytes.Length>4194304)throw new ArgumentException("Folder input exceeds 4 MiB.");
            string parseName=filename,content;
            if(Path.GetExtension(filename).Equals(".pdf",StringComparison.OrdinalIgnoreCase)){SpinDoctorPdfInputs.Extraction e=SpinDoctorPdfInputs.Extract(bytes);parseName=e.Filename;content=e.Content;}
            else content=new UTF8Encoding(false,true).GetString(bytes);
            using(SHA256 hash=SHA256.Create())
            {
                Entry entry=new Entry{Id="transient",Filename=parseName,Sha=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()};
                Plan plan=Parse(entry,content);if(plan.ScriptCommands!=null)return D("commands",plan.ScriptCommands,"source",D("sha256",entry.Sha,"filename",filename,"format",plan.Format));if(plan.Rows!=null)throw new ArgumentException("A parameter CSV needs an explicit base circuit. Folder CSVs instead use a command column; see Input formats page.");
                Dictionary<string,double> parameters=Resolve(plan.Parameters,null,false);List<object> resolved=new List<object>();
                foreach(GatePlan g in plan.Gates){double[] angles=new double[g.Angles.Length];for(int j=0;j<angles.Length;j++){angles[j]=SafeExpression.Evaluate(g.Angles[j],parameters);if(Math.Abs(angles[j])>1e12)throw new ArgumentException("Gate angle exceeds +/-1e12 radians.");}resolved.Add(D("name",g.Name,"angles",angles,"qubits",g.Qubits,"all",g.All));}
                return D("name",plan.Name,"qubits",plan.Qubits,"initialBasis",plan.InitialBasis.ToString(CultureInfo.InvariantCulture),"parameters",parameters,"gates",resolved.ToArray(),"hints",D("shots",plan.Shots,"basis",plan.Basis),"source",D("sha256",entry.Sha,"filename",filename,"format",plan.Format),"warnings",plan.Warnings.ToArray());
            }
        }

        private Dictionary<string, object> Import(Entry entry, int? row, Dictionary<string, double> bindings, string baseId)
        {
            Plan plan = Parse(entry, Read(entry)); if(plan.ScriptCommands!=null)throw new ArgumentException("This attachment contains a command sequence. Use q.input run ID to execute it asynchronously."); Dictionary<string, double> combined = bindings == null ? new Dictionary<string, double>(StringComparer.Ordinal) : new Dictionary<string, double>(bindings, StringComparer.Ordinal);
            foreach (KeyValuePair<string, double> binding in combined) { ParameterName(binding.Key); Finite(binding.Value); }
            Dictionary<string, object> source = D("id", entry.Id, "filename", entry.Filename, "sha256", entry.Sha, "format", plan.Format, "row", row.HasValue ? (object)row.Value : null, "baseId", baseId);
            if (plan.Rows != null)
            {
                if (!row.HasValue || row.Value < 0 || row.Value >= plan.Rows.Count) throw new ArgumentException("CSV import requires a valid zero-based row index.");
                if (baseId == null) throw new ArgumentException("CSV import requires base BASE_ID pointing to a circuit attachment.");
                Entry baseEntry = Find(baseId); Plan circuit = Parse(baseEntry, Read(baseEntry)); if (circuit.Rows != null) throw new ArgumentException("A CSV cannot serve as its own circuit base.");
                Dictionary<string, double> defaults = Resolve(circuit.Parameters, combined, true);
                Dictionary<string, string> cells = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int j = 0; j < plan.Columns.Length; ++j)
                {
                    if (!circuit.Parameters.ContainsKey(plan.Columns[j])) throw new ArgumentException("CSV column is not a declared base-circuit parameter: " + plan.Columns[j]);
                    cells.Add(plan.Columns[j], plan.Rows[row.Value][j]);
                }
                foreach (string key in cells.Keys) if (!combined.ContainsKey(key)) defaults.Remove(key);
                Dictionary<string, double> values = Resolve(cells, defaults, false);
                foreach (string key in cells.Keys) if (!combined.ContainsKey(key)) combined.Add(key, values[key]);
                source["baseSha256"] = baseEntry.Sha; source["baseFilename"] = baseEntry.Filename; plan = circuit;
            }
            else if (row.HasValue || baseId != null) throw new ArgumentException("row/base options apply only to CSV parameter sweeps.");
            foreach (string key in combined.Keys) if (!plan.Parameters.ContainsKey(key)) throw new ArgumentException("Override is not a declared circuit parameter: " + key);
            Dictionary<string, double> parameters = Resolve(plan.Parameters, combined, false);
            List<object> gates = new List<object>();
            foreach (GatePlan gate in plan.Gates)
            {
                double[] angles = new double[gate.Angles.Length]; for (int j = 0; j < angles.Length; ++j) { angles[j] = SafeExpression.Evaluate(gate.Angles[j], parameters); if (Math.Abs(angles[j]) > 1e12) throw new ArgumentException("Gate angle exceeds +/-1e12 radians."); }
                gates.Add(D("name", gate.Name, "angles", angles, "qubits", gate.Qubits, "all", gate.All));
            }
            Dictionary<string, object> equations = new Dictionary<string, object>(); foreach (KeyValuePair<string, string> parameter in plan.Parameters) equations.Add(parameter.Key, parameter.Value);
            return D("name", plan.Name, "qubits", plan.Qubits, "initialBasis", plan.InitialBasis.ToString(CultureInfo.InvariantCulture), "parameters", parameters,
                "gates", gates.ToArray(), "hints", D("shots", plan.Shots, "basis", plan.Basis), "source", source, "equations", equations,
                "warnings", plan.Warnings.ToArray(), "scope", "Validated classical coherent-circuit input data. Import creates/selects a program; running and measuring remain explicit actions.");
        }

        private Dictionary<string, object> Preview(Entry entry, string content)
        {
            Dictionary<string, object> preview = D("parseValid", false, "executable", false, "requiresBase", false, "requiresBindings", false, "format", "unsupported", "warnings", new string[0], "error", "");
            try
            {
                Plan plan = Parse(entry, content); preview["parseValid"] = true; preview["format"] = plan.Format; preview["warnings"] = plan.Warnings.ToArray();
                if(plan.ScriptCommands!=null){preview["executable"]=true;preview["commandCount"]=plan.ScriptCommands.Length;preview["commandsPreview"]=plan.ScriptCommands;preview["note"]="Explicit structured command data. q.input run executes in order with background-job barriers; upload alone never executes.";}
                else if (plan.Rows != null)
                {
                    preview["requiresBase"] = true; preview["columns"] = plan.Columns; preview["rowCount"] = plan.Rows.Count;
                    preview["rowsPreview"] = plan.Rows.GetRange(0, Math.Min(8, plan.Rows.Count)).ToArray(); preview["note"] = "Choose a base circuit attachment and a zero-based row, then evaluate/import explicitly.";
                }
                else
                {
                    preview["name"] = plan.Name; preview["qubits"] = plan.Qubits; preview["initialBasis"] = plan.InitialBasis.ToString(CultureInfo.InvariantCulture);
                    preview["gateCount"] = plan.Gates.Count; preview["parameters"] = plan.Parameters; preview["hints"] = D("shots", plan.Shots, "basis", plan.Basis);
                    Dictionary<string, double> parameters = Resolve(plan.Parameters, null, false);
                    List<object> gates = new List<object>(); int index = 0;
                    foreach (GatePlan gate in plan.Gates)
                    {
                        double[] angles = new double[gate.Angles.Length]; for (int j = 0; j < angles.Length; ++j) { angles[j] = SafeExpression.Evaluate(gate.Angles[j], parameters); if (Math.Abs(angles[j]) > 1e12) throw new ArgumentException("Gate angle exceeds +/-1e12 radians."); }
                        if (index++ < 32) gates.Add(D("name", gate.Name, "angles", angles, "qubits", gate.Qubits, "all", gate.All));
                    }
                    preview["gatesPreview"] = gates.ToArray(); preview["parameterValues"] = parameters; preview["executable"] = true;
                }
            }
            catch (MissingVariable error) { preview["requiresBindings"] = true; preview["error"] = error.Message; }
            catch (Exception error) { preview["error"] = error.Message; }
            if (Path.GetExtension(entry.Filename).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                // Data-table validity is separate from executable-program validity.
                // Weather observations need not contain commands or circuit parameters.
                try
                {
                    NumericTable table = ParseTable(content);
                    preview["tableValid"] = true; preview["dataTable"] = TableView(entry, table);
                    preview["tableNote"] = "Numeric data can be inspected with q.input table and fitted only after an explicit target/features choice. Upload does not execute or fit a model.";
                }
                catch (Exception error) { preview["tableValid"] = false; preview["tableError"] = error.Message; }
            }
            return preview;
        }
        private Dictionary<string, object> Metadata(Entry entry)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(entry.Preview);
            result["id"] = entry.Id; result["filename"] = entry.Filename; result["sha256"] = entry.Sha; result["bytes"] = entry.Bytes; result["uploadedUtc"] = entry.UploadedUtc;
            result["preview"] = new Dictionary<string, object>(entry.Preview); result["fullExportAvailable"] = true;
            result["scope"] = "Inert original source with SHA256. PDF imports require an embedded or explicitly tagged circuit program; document prose is data. No shell, document instructions, external includes, guessed equations or automatic execution."; return result;
        }
        private static Dictionary<string, object> Summary(Entry entry)
        {
            Dictionary<string, object> result = D("id", entry.Id, "filename", entry.Filename, "sha256", entry.Sha, "bytes", entry.Bytes, "uploadedUtc", entry.UploadedUtc, "fullPreviewAvailable", true);
            foreach (string key in new string[] { "format", "parseValid", "executable", "requiresBase", "requiresBindings", "warnings", "error", "name", "qubits", "initialBasis", "gateCount", "columns", "rowCount", "tableValid", "tableError" })
            {
                object value; if (entry.Preview.TryGetValue(key, out value)) result[key] = key == "error" && ((string)value).Length > 1024 ? ((string)value).Substring(0, 1024) : value;
            }
            return result;
        }

        private Plan Parse(Entry entry, string content)
        {
            if (content.Length > 0 && content[0] == '\uFEFF') content = content.Substring(1);
            string extension = Path.GetExtension(entry.Filename).ToLowerInvariant();
            if(extension==".pdf")
            {
                SpinDoctorPdfInputs.Extraction extracted=SpinDoctorPdfInputs.Extract(ReadBytes(entry));
                Plan parsed=Parse(new Entry{Filename=extracted.Filename,Id=entry.Id},extracted.Content);
                parsed.Format="pdf/"+parsed.Format;parsed.Warnings.Add("PDF source imported using "+extracted.Method+". Only the explicit program is interpreted.");return parsed;
            }
            if (extension == ".json") return ParseJson(content, entry);
            if (extension == ".qasm" || extension == ".qasm2") return ParseQasm(content, entry);
            if (extension == ".csv")
            {
                // Command cells use the same 4,096-character ceiling as TXT/JSON
                // sequence entries. Parameter expressions keep their separate,
                // smaller 1,024-character bound when ParseCsv validates them.
                List<string[]> rows = CsvRows(content, 4096);
                if (rows.Count > 0 && rows[0].Length == 1 && rows[0][0].Trim().Equals("command", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> commands = new List<string>();
                    for (int i = 1; i < rows.Count; i++)
                    {
                        if (rows[i].Length != 1) throw new ArgumentException("Command CSV requires exactly one column headed command. Quote any command containing commas, and double each literal quote.");
                        commands.Add(rows[i][0]);
                    }
                    return Script(commands.ToArray(), "csv-sequence");
                }
                if (rows.Count > 0)
                {
                    // Only the complete reserved schema denotes a gate program.
                    // Older parameter sweeps may legitimately use gate or gate,n.
                    HashSet<string> headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string column in rows[0]) headers.Add(column.Trim());
                    if (headers.Contains("gate") && headers.Contains("n") && headers.Contains("targets") && headers.Contains("angles")) return ParseGateCsv(rows, entry);
                }
                return ParseCsv(content);
            }
            if (extension == ".txt" || extension == ".circuit") { bool script=content.IndexOf("SPINDOCTOR-SEQUENCE",StringComparison.OrdinalIgnoreCase)>=0; foreach(string raw in content.Replace('\r','\n').Split('\n')){string line=raw.Trim();if(line.StartsWith("q.",StringComparison.OrdinalIgnoreCase) && !line.StartsWith("q.circuit new ",StringComparison.OrdinalIgnoreCase) && !line.StartsWith("q.circuit add ",StringComparison.OrdinalIgnoreCase) && !line.StartsWith("q.circuit input ",StringComparison.OrdinalIgnoreCase))script=true;}if(script){List<string> commands=new List<string>();foreach(string raw in content.Replace('\r','\n').Split('\n')){string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith("//"))continue;commands.Add(line);}return Script(commands.ToArray(),"txt-sequence");}return ParseText(content, entry); }
            throw new ArgumentException("Unsupported input format. Use JSON, QASM, TXT, CSV, or a PDF containing one embedded/explicitly tagged program. Scans and prose are not executable equations.");
        }
        private Plan ParseJson(string content, Entry entry)
        {
            Dictionary<string, object> data = json.Deserialize<Dictionary<string, object>>(content); if (data == null) throw new ArgumentException("JSON circuit must be an object.");
            if(data.ContainsKey("commands")){foreach(string key in data.Keys)if(key!="commands"&&key!="schema"&&key!="description")throw new ArgumentException("Sequence JSON only accepts commands, schema and description.");List<string> commands=new List<string>();foreach(object value in Sequence(data["commands"])){if(!(value is string))throw new ArgumentException("Sequence commands must be strings.");commands.Add((string)value);}return Script(commands.ToArray(),"json-sequence");}
            foreach (string key in data.Keys) if (key != "name" && key != "qubits" && key != "initialBasis" && key != "parameters" && key != "gates" && key != "shots" && key != "basis" && key != "description" && key != "hints" && key != "schema") throw new ArgumentException("Unsupported JSON circuit field: " + key);
            Plan plan = new Plan { Format = "json", Name = data.ContainsKey("name") ? CircuitName(Text(data["name"])) : DefaultName(entry), Qubits = Integer(Text(Required(data, "qubits")), 1, 298) };
            if (data.ContainsKey("initialBasis")) plan.InitialBasis = BasisIndex(Text(data["initialBasis"]));
            if (data.ContainsKey("shots")) plan.Shots = Integer(Text(data["shots"]), 1, 100000);
            if (data.ContainsKey("basis")) plan.Basis = Basis(Text(data["basis"]));
            if (data.ContainsKey("hints"))
            {
                Dictionary<string, object> hints = Object(data["hints"], "hints"); foreach (string key in hints.Keys) if (key != "shots" && key != "basis") throw new ArgumentException("Unsupported circuit hint: " + key);
                if (hints.ContainsKey("shots")) plan.Shots = Integer(Text(hints["shots"]), 1, 100000); if (hints.ContainsKey("basis")) plan.Basis = Basis(Text(hints["basis"]));
            }
            if (data.ContainsKey("parameters"))
            {
                Dictionary<string, object> parameters = Object(data["parameters"], "parameters"); if (parameters.Count > 64) throw new ArgumentException("At most64 declared parameters are supported.");
                foreach (KeyValuePair<string, object> parameter in parameters)
                {
                    string name = ParameterName(parameter.Key), expression = parameter.Value == null ? null : ExpressionText(parameter.Value);
                    if (expression != null) SafeExpression.Validate(expression); plan.Parameters.Add(name, expression);
                }
            }
            foreach (object value in Sequence(Required(data, "gates")))
            {
                if (plan.Gates.Count >= 8192) throw new ArgumentException("At most8192 circuit instructions are supported.");
                Dictionary<string, object> gate = Object(value, "gate"); foreach (string key in gate.Keys) if (key != "name" && key != "angles" && key != "qubits" && key != "all") throw new ArgumentException("Unsupported gate field: " + key);
                List<string> angles = new List<string>(); if (gate.ContainsKey("angles")) foreach (object angle in Sequence(gate["angles"])) angles.Add(ExpressionText(angle));
                List<int> qubits = new List<int>(); if (gate.ContainsKey("qubits")) foreach (object qubit in Sequence(gate["qubits"])) qubits.Add(Integer(Text(qubit), 0, plan.Qubits - 1));
                bool all = gate.ContainsKey("all") && Boolean(gate["all"]);
                if (all && qubits.Count > 0) throw new ArgumentException("Gate all and explicit qubits are mutually exclusive.");
                plan.Gates.Add(Gate(Text(Required(gate, "name")), angles.ToArray(), qubits.ToArray(), all, plan.Qubits));
            }
            ValidatePlan(plan); return plan;
        }
        private static Plan Script(string[] commands,string format)
        {
            if(commands.Length<1||commands.Length>128)throw new ArgumentException("Input sequences contain 1..128 commands.");
            HashSet<string> allowed=new HashSet<string>("q.prepare q.migrate q.state q.gate q.verify q.measure q.status q.export q.tranche q.circuit q.math q.query q.noise q.qec q.compile q.resource q.model q.lesson q.algorithm q.preset q.help".Split(' '),StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<commands.Length;i++){commands[i]=commands[i].Trim();if(commands[i].Length<1||commands[i].Length>4096||commands[i].IndexOf('\n')>=0||commands[i].IndexOf('\r')>=0)throw new ArgumentException("Each sequence entry is one bounded terminal command.");string[] words=commands[i].Split(new char[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);if(!allowed.Contains(words[0]))throw new ArgumentException("Input sequences accept documented quantum commands only; shell, file-management and nested folder/input commands are refused.");if(words.Length>1 && (words[1].Equals("delete",StringComparison.OrdinalIgnoreCase)||words[1].Equals("reset",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Input sequences do not delete/reset existing user state.");}
            return new Plan{Format=format,ScriptCommands=commands};
        }
        private static bool KnownTextGate(string word)
        { try { GateSpec(Alias(word)); return true; } catch (Exception) { return false; } }
        private static string Clip(string word)
        { string value = word ?? ""; if (value.Length > 40) value = value.Substring(0, 40) + "..."; return "'" + value + "'"; }
        private Plan ParseText(string content, Entry entry)
        {
            Plan plan = new Plan { Format = "text-circuit", Name = DefaultName(entry) }; List<string[]> gates = new List<string[]>(); bool declared = false, named = false;
            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int lineNumber = 0, contentLines = 0, firstOperandLine = 0; string firstOperandWord = null; // 3.1.2: explain empty/prose text files
            foreach (string raw in lines)
            {
                lineNumber++; string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                contentLines++;
                if (line.Length > 8192) throw new ArgumentException("Text circuit line exceeds8192 characters.");
                string[] words = Regex.Split(line, "\\s+"); int start = 0;
                if (Eq(words[0], "q.circuit"))
                {
                    if (words.Length < 2) throw new ArgumentException("Incomplete q.circuit data line.");
                    if (Eq(words[1], "new")) { if (words.Length != 4 || declared) throw new ArgumentException("Use exactly one q.circuit new NAME QUBITS declaration."); plan.Name = CircuitName(words[2]); plan.Qubits = Integer(words[3], 1, 298); declared = named = true; continue; }
                    if (Eq(words[1], "input")) { if (words.Length != 3) throw new ArgumentException("Use q.circuit input BASIS_INDEX."); plan.InitialBasis = BasisIndex(words[2]); continue; }
                    if (!Eq(words[1], "add")) throw new ArgumentException("Text attachments permit q.circuit new/input/add data only; run, delete, other terminal commands and shell statements are rejected."); start = 2;
                }
                else if (Eq(words[0], "qubits")) { if (words.Length != 2 || declared) throw new ArgumentException("Declare qubits exactly once."); plan.Qubits = Integer(words[1], 1, 298); declared = true; continue; }
                else if (Eq(words[0], "name")) { if (words.Length != 2 || named) throw new ArgumentException("Declare a circuit name at most once."); plan.Name = CircuitName(words[1]); named = true; continue; }
                else if (Eq(words[0], "input")) { if (words.Length != 2) throw new ArgumentException("Use input BASIS_INDEX."); plan.InitialBasis = BasisIndex(words[1]); continue; }
                else if (Eq(words[0], "shots")) { if (words.Length != 2) throw new ArgumentException("Use shots N."); plan.Shots = Integer(words[1], 1, 100000); continue; }
                else if (Eq(words[0], "basis")) { if (words.Length != 2) throw new ArgumentException("Use basis x|y|z."); plan.Basis = Basis(words[1]); continue; }
                else if (Eq(words[0], "param") || Eq(words[0], "parameter"))
                {
                    string declaration = line.Substring(words[0].Length).Trim(); int equals = declaration.IndexOf('=');
                    if (equals < 1 || equals == declaration.Length - 1) throw new ArgumentException("Use param NAME=EXPRESSION.");
                    string name = ParameterName(declaration.Substring(0, equals).Trim()), expression = declaration.Substring(equals + 1).Trim(); SafeExpression.Validate(expression);
                    if (plan.Parameters.Count >= 64 || plan.Parameters.ContainsKey(name)) throw new ArgumentException("Duplicate parameter or parameter limit exceeded."); plan.Parameters.Add(name, expression); continue;
                }
                else if (Eq(words[0], "gate")) start = 1;
                if (gates.Count >= 8192) throw new ArgumentException("At most8192 text gate instructions are supported.");
                string[] operands = new string[words.Length - start]; Array.Copy(words, start, operands, 0, operands.Length); gates.Add(operands);
                if (firstOperandWord == null) { firstOperandWord = operands.Length > 0 ? operands[0] : ""; firstOperandLine = lineNumber; }
            }
            if (!declared && contentLines == 0) throw new ArgumentException("Text input is empty: every line is blank or a # or // comment. Add q.* command lines (command file) or qubits N plus gate lines (text circuit).");
            if (!declared && firstOperandWord != null && !KnownTextGate(firstOperandWord)) throw new ArgumentException("Text input line " + firstOperandLine.ToString(CultureInfo.InvariantCulture) + " is neither a q.* command nor a known gate (" + Clip(firstOperandWord) + "). Command files need q.* lines; text circuits need qubits N (or q.circuit new NAME N) and gate lines. Prose and Markdown fences are not accepted.");
            if (!declared) throw new ArgumentException("Text circuit requires qubits N or q.circuit new NAME N.");
            foreach (string[] words in gates)
            {
                if (words.Length == 0) throw new ArgumentException("Missing gate name."); string name = Alias(words[0]); Dictionary<string, object> spec = GateSpec(name); int ac = (int)spec["angleCount"], qc = (int)spec["targetCount"];
                bool all = words.Length == ac + 2 && Eq(words[words.Length - 1], "all"); if (words.Length != 1 + ac + (all ? 1 : qc)) throw new ArgumentException("Incorrect gate angle/target count. Text angle expressions must be single tokens; angles precede targets.");
                string[] angles = new string[ac]; Array.Copy(words, 1, angles, 0, ac); int[] qubits = new int[all ? 0 : qc];
                for (int j = 0; j < qubits.Length; ++j) qubits[j] = Integer(words[1 + ac + j], 0, plan.Qubits - 1);
                plan.Gates.Add(Gate(name, angles, qubits, all, plan.Qubits));
            }
            ValidatePlan(plan); return plan;
        }

        private Plan ParseQasm(string content, Entry entry)
        {
            Plan plan = new Plan { Format = "openqasm2", Name = DefaultName(entry) };
            plan.Warnings.Add("Supported QASM gates use SpinDoctor/Qiskit standard-gate matrix phase conventions. Alternate include definitions and custom gate definitions are unsupported.");
            string cleaned = StripComments(content);
            string[] statements = cleaned.Split(';'); string quantum = null, classical = null; bool header = false, gatesStarted = false, measuring = false;
            HashSet<int> measured = new HashSet<int>();
            for (int index = 0; index < statements.Length; ++index)
            {
                string statement = statements[index].Trim(); if (statement.Length == 0) continue;
                if (index == statements.Length - 1) throw new ArgumentException("OpenQASM2 statements must end with semicolons.");
                if (!header) { if (statement != "OPENQASM 2.0") throw new ArgumentException("Require OPENQASM 2.0 header."); header = true; continue; }
                if (statement.StartsWith("include", StringComparison.Ordinal))
                {
                    if (gatesStarted || !Regex.IsMatch(statement, "^include\\s+\"qelib1\\.inc\"$")) throw new ArgumentException("Only include \"qelib1.inc\" is recognized symbolically; external includes are never read.");
                    continue;
                }
                Match declaration = Regex.Match(statement, "^(qreg|creg)\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\[\\s*([0-9]+)\\s*\\]$");
                if (declaration.Success)
                {
                    if (gatesStarted) throw new ArgumentException("Registers must be declared before gates."); int width = Integer(declaration.Groups[3].Value, 1, 298);
                    if (declaration.Groups[1].Value == "qreg") { if (quantum != null) throw new ArgumentException("Supported QASM2 subset has one qreg of1..298 qubits."); quantum = declaration.Groups[2].Value; plan.Qubits = width; }
                    else { if (classical != null || quantum == null || width != plan.Qubits || declaration.Groups[2].Value == quantum) throw new ArgumentException("Supported creg must follow qreg and have the same width and a distinct name."); classical = declaration.Groups[2].Value; }
                    continue;
                }
                if (quantum == null) throw new ArgumentException("Declare qreg before gate or measurement statements."); gatesStarted = true;
                if (statement.StartsWith("measure", StringComparison.Ordinal))
                {
                    if (classical == null) throw new ArgumentException("Final measurement requires a matching creg."); measuring = true;
                    Match measurement = Regex.Match(statement, "^measure\\s+(.+?)\\s*->\\s*(.+)$"); if (!measurement.Success) throw new ArgumentException("Invalid measure statement.");
                    string q = measurement.Groups[1].Value.Trim(), c = measurement.Groups[2].Value.Trim();
                    if (q == quantum && c == classical)
                    {
                        if (measured.Count != 0) throw new ArgumentException("Duplicate final measurement."); for (int bit = 0; bit < plan.Qubits; ++bit) measured.Add(bit);
                    }
                    else
                    {
                        int qb = QasmIndex(q, quantum, plan.Qubits), cb = QasmIndex(c, classical, plan.Qubits);
                        if (qb != cb || !measured.Add(qb)) throw new ArgumentException("Final indexed measurements must map each qubit to the same classical index exactly once.");
                    }
                    continue;
                }
                if (measuring) throw new ArgumentException("Mid-circuit measurement is unsupported; measurements must form the final complete group.");
                if (statement.StartsWith("barrier", StringComparison.Ordinal))
                {
                    string arguments = statement.Substring(7).Trim(); if (arguments.Length == 0) throw new ArgumentException("Barrier needs a register/targets.");
                    foreach (string target in arguments.Split(',')) if (target.Trim() != quantum) QasmIndex(target.Trim(), quantum, plan.Qubits);
                    if (!plan.Warnings.Contains("QASM barriers are presentation markers and produce no state-operation credit.")) plan.Warnings.Add("QASM barriers are presentation markers and produce no state-operation credit."); continue;
                }
                if (Regex.IsMatch(statement, "^(reset|if|gate|opaque|snapshot)\\b")) throw new ArgumentException("Unsupported QASM2 statement (reset/if/custom gate/opaque/snapshot). Only supported unitary gates and final complete measurements are accepted.");
                Match operation = Regex.Match(statement, "^([A-Za-z_][A-Za-z0-9_]*)\\s*(?:\\((.*)\\))?\\s+(.+)$");
                if (!operation.Success) throw new ArgumentException("Invalid or unsupported QASM2 gate statement.");
                string name = Alias(operation.Groups[1].Value); List<string> angles = new List<string>();
                if (operation.Groups[2].Success) angles.AddRange(SplitArguments(operation.Groups[2].Value));
                if (name == "u1") name = "p";
                else if (name == "u2") { if (angles.Count != 2) throw new ArgumentException("u2 requires two angles."); name = "u"; angles.Insert(0, "pi/2"); }
                else if (name == "cu1") name = "cp";
                else if (name == "cu3") { if (angles.Count != 3) throw new ArgumentException("cu3 requires three angles."); name = "cu"; angles.Add("0"); }
                string[] operands = operation.Groups[3].Value.Split(','); bool all = operands.Length == 1 && operands[0].Trim() == quantum;
                int[] qubits = new int[all ? 0 : operands.Length]; for (int j = 0; j < qubits.Length; ++j) qubits[j] = QasmIndex(operands[j].Trim(), quantum, plan.Qubits);
                if (plan.Gates.Count >= 8192) throw new ArgumentException("At most8192 QASM gate instructions are supported."); plan.Gates.Add(Gate(name, angles.ToArray(), qubits, all, plan.Qubits));
            }
            if (!header || quantum == null) throw new ArgumentException("Missing QASM2 header/qreg.");
            if (measuring && measured.Count != plan.Qubits) throw new ArgumentException("Final measurements must cover every qubit exactly once.");
            if (measuring) plan.Warnings.Add("Final QASM Z measurements are recorded as a basis hint; importing never runs or measures the circuit.");
            ValidatePlan(plan); return plan;
        }
        private static string StripComments(string content)
        {
            StringBuilder result = new StringBuilder(content.Length); bool quote = false;
            for (int j = 0; j < content.Length; ++j)
            {
                char ch = content[j]; if (ch == '"') { quote = !quote; result.Append(ch); continue; }
                if (!quote && ch == '/' && j + 1 < content.Length && content[j + 1] == '/') { while (j < content.Length && content[j] != '\n') j++; result.Append('\n'); continue; }
                if (!quote && ch == '/' && j + 1 < content.Length && content[j + 1] == '*')
                {
                    j += 2; bool ended = false; for (; j + 1 < content.Length; ++j) if (content[j] == '*' && content[j + 1] == '/') { j++; ended = true; break; }
                    if (!ended) throw new ArgumentException("Unterminated QASM block comment."); result.Append(' '); continue;
                }
                result.Append(ch);
            }
            if (quote) throw new ArgumentException("Unterminated QASM quote."); return result.ToString();
        }
        private static int QasmIndex(string operand, string name, int width)
        {
            Match index = Regex.Match(operand, "^" + Regex.Escape(name) + "\\s*\\[\\s*([0-9]+)\\s*\\]$");
            if (!index.Success) throw new ArgumentException("Only the declared register and indexed qubits are accepted: " + operand); return Integer(index.Groups[1].Value, 0, width - 1);
        }
        private static string[] SplitArguments(string value)
        {
            List<string> arguments = new List<string>(); int depth = 0, start = 0;
            for (int j = 0; j < value.Length; ++j) { if (value[j] == '(') depth++; else if (value[j] == ')') depth--; else if (value[j] == ',' && depth == 0) { arguments.Add(value.Substring(start, j - start).Trim()); start = j + 1; } if (depth < 0) throw new ArgumentException("Unbalanced QASM parameter parentheses."); }
            if (depth != 0) throw new ArgumentException("Unbalanced QASM parameter parentheses."); arguments.Add(value.Substring(start).Trim()); return arguments.ToArray();
        }

        private static string[] GateCsvList(string text)
        {
            text = text.Trim(); if (text.Length == 0) return new string[0];
            return CellList(text[0] == '[' ? text : "[" + text + "]", "CSV gate operand list");
        }
        private static Plan ParseGateCsv(List<string[]> rows, Entry entry)
        {
            if (rows.Count < 2) throw new ArgumentException("Gate-list CSV requires header gate,n,targets,angles and at least one gate row.");
            Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> allowed = new HashSet<string>("gate n targets angles name initialbasis shots basis".Split(' '), StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < rows[0].Length; j++)
            {
                string name = rows[0][j].Trim();
                if (!allowed.Contains(name)) throw new ArgumentException("Unknown gate-list CSV column: " + name + ". Use gate,n,targets,angles with optional name,initialBasis,shots,basis.");
                if (columns.ContainsKey(name)) throw new ArgumentException("Duplicate gate-list CSV header: " + name + "."); columns.Add(name, j);
            }
            foreach (string name in new string[] { "gate", "n", "targets", "angles" }) if (!columns.ContainsKey(name)) throw new ArgumentException("Gate-list CSV requires columns gate,n,targets,angles.");
            for (int i = 1; i < rows.Count; i++) if (rows[i].Length != rows[0].Length) throw new ArgumentException("Gate-list CSV row width differs from its header.");
            if (rows[1][columns["n"]].Trim().Length == 0) throw new ArgumentException("Declare the register size n in the first gate-list CSV row.");
            Plan plan = new Plan { Format = "csv-gates", Name = DefaultName(entry), Shots = 1024, Qubits = Integer(rows[1][columns["n"]].Trim(), 1, 298) };
            Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < rows.Count; i++)
            {
                foreach (string field in new string[] { "n", "name", "initialbasis", "shots", "basis" })
                {
                    int position; if (!columns.TryGetValue(field, out position)) continue; string value = rows[i][position].Trim(); if (value.Length == 0) continue;
                    string canonical;
                    if (field == "n") canonical = Integer(value, 1, 298).ToString(CultureInfo.InvariantCulture);
                    else if (field == "name") canonical = CircuitName(value);
                    else if (field == "initialbasis") canonical = BasisIndex(value).ToString(CultureInfo.InvariantCulture);
                    else if (field == "shots") canonical = Integer(value, 1, 100000).ToString(CultureInfo.InvariantCulture);
                    else canonical = Basis(value);
                    string previous; if (metadata.TryGetValue(field, out previous) && previous != canonical) throw new ArgumentException("Gate-list CSV circuit metadata must be consistent across rows: " + field + ".");
                    metadata[field] = canonical;
                }
            }
            if (metadata.ContainsKey("name")) plan.Name = metadata["name"];
            if (metadata.ContainsKey("initialbasis")) plan.InitialBasis = BasisIndex(metadata["initialbasis"]);
            if (metadata.ContainsKey("shots")) plan.Shots = Integer(metadata["shots"], 1, 100000);
            if (metadata.ContainsKey("basis")) plan.Basis = Basis(metadata["basis"]);
            for (int i = 1; i < rows.Count; i++)
            {
                string name = rows[i][columns["gate"]].Trim(), targetText = rows[i][columns["targets"]].Trim();
                string[] angles = GateCsvList(rows[i][columns["angles"]]); bool all = targetText.Equals("all", StringComparison.OrdinalIgnoreCase);
                string[] targets = all ? new string[0] : GateCsvList(targetText); int[] qubits = new int[targets.Length];
                for (int j = 0; j < qubits.Length; j++) qubits[j] = Integer(targets[j], 0, plan.Qubits - 1);
                plan.Gates.Add(Gate(name, angles, qubits, all, plan.Qubits));
            }
            ValidatePlan(plan); plan.Warnings.Add("CSV gate rows are explicit ordered circuit data. Upload and import do not run; q.input run uses declared shots/basis (default 1024 shots in Z), with native job barriers. Classical simulation, not physical quantum hardware.");
            return plan;
        }

        private static Plan ParseCsv(string content)
        {
            List<string[]> rows = CsvRows(content, 1024); if (rows.Count < 2) throw new ArgumentException("CSV requires a command header and command rows, or a parameter header and at least one data row with an explicit base circuit.");
            string[] columns = rows[0]; if (columns.Length == 0 || columns.Length > 64) throw new ArgumentException("CSV supports1..64 parameter columns."); HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int j = 0; j < columns.Length; ++j) { columns[j] = ParameterName(columns[j].Trim()); if (!names.Add(columns[j])) throw new ArgumentException("Duplicate CSV parameter column."); }
            rows.RemoveAt(0); if (rows.Count > 4096) throw new ArgumentException("CSV parameter sweeps support at most4096 rows.");
            foreach (string[] row in rows)
            {
                if (row.Length != columns.Length) throw new ArgumentException("CSV row width differs from the header."); for (int j = 0; j < row.Length; ++j) { row[j] = row[j].Trim(); SafeExpression.Validate(row[j]); }
            }
            return new Plan { Format = "csv-parameters", Columns = columns, Rows = rows };
        }
        private static List<string[]> CsvRows(string content, int maximumCellCharacters)
        {
            List<string[]> rows = new List<string[]>(); List<string> row = new List<string>(); StringBuilder cell = new StringBuilder(); bool quoted = false, closed = false;
            for (int j = 0; j <= content.Length; ++j)
            {
                char ch = j == content.Length ? '\n' : content[j];
                if (quoted)
                {
                    if (j == content.Length) throw new ArgumentException("Unterminated CSV quoted cell.");
                    if (ch == '"') { if (j + 1 < content.Length && content[j + 1] == '"') { cell.Append('"'); j++; } else { quoted = false; closed = true; } } else cell.Append(ch);
                }
                else if (ch == '"') { if (cell.Length != 0 || closed) throw new ArgumentException("CSV quote must begin a cell."); quoted = true; }
                else if (ch == ',' || ch == '\n' || ch == '\r')
                {
                    row.Add(cell.ToString()); cell.Length = 0; closed = false;
                    if (ch != ',')
                    {
                        if (!(row.Count == 1 && row[0].Trim().Length == 0)) rows.Add(row.ToArray()); row.Clear();
                        if (ch == '\r' && j + 1 < content.Length && content[j + 1] == '\n') j++;
                        if (rows.Count > 4097) throw new ArgumentException("CSV parameter sweep exceeds4096 rows.");
                    }
                }
                else { if (closed && !Char.IsWhiteSpace(ch)) throw new ArgumentException("Unexpected data after a CSV closing quote."); if (!closed) cell.Append(ch); }
                if (cell.Length > maximumCellCharacters) throw new ArgumentException("CSV cell exceeds " + maximumCellCharacters.ToString(CultureInfo.InvariantCulture) + " characters.");
                if (row.Count > 64) throw new ArgumentException("CSV has more than64 columns.");
            }
            return rows;
        }

        private NumericTable ReadTable(Entry entry)
        {
            if (!Path.GetExtension(entry.Filename).Equals(".csv", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Data-table inspection and fitting require an original CSV attachment.");
            return ParseTable(Read(entry));
        }
        private static NumericTable ParseTable(string content)
        {
            if (content.Length > 0 && content[0] == '\uFEFF') content = content.Substring(1);
            List<string[]> rows = CsvRows(content, 4096);
            if (rows.Count < 2) throw new ArgumentException("A data CSV requires one header row and at least one data row.");
            string[] names = rows[0]; if (names.Length < 1 || names.Length > 64) throw new ArgumentException("Data CSV supports 1..64 columns.");
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < names.Length; j++)
            {
                names[j] = names[j].Trim();
                if (names[j].Length < 1 || names[j].Length > 128) throw new ArgumentException("Data CSV headers require 1..128 characters.");
                foreach (char ch in names[j]) if (Char.IsControl(ch)) throw new ArgumentException("Data CSV headers cannot contain control characters or line breaks.");
                if (!unique.Add(names[j])) throw new ArgumentException("Data CSV headers must be unique, ignoring case.");
            }
            rows.RemoveAt(0); if (rows.Count > 4096) throw new ArgumentException("Data CSV supports at most 4096 rows.");
            for (int i = 0; i < rows.Count; i++) if (rows[i].Length != names.Length) throw new ArgumentException("Data CSV row " + i.ToString(CultureInfo.InvariantCulture) + " has a different width from its header.");
            return new NumericTable { Columns = names, Rows = rows };
        }
        private static bool Numeric(string text, out double value)
        {
            return Double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && QuantumMath.IsFinite(value) && Math.Abs(value) <= 1e150;
        }
        private static Dictionary<string, object> Source(Entry entry)
        {
            return D("id", entry.Id, "filename", entry.Filename, "sha256", entry.Sha, "bytes", entry.Bytes);
        }
        private static Dictionary<string, object> TableView(Entry entry, NumericTable table)
        {
            List<object> columns = new List<object>(); List<int> numericColumns = new List<int>();
            for (int j = 0; j < table.Columns.Length; j++)
            {
                int numeric = 0, missing = 0, invalid = 0; double min = Double.PositiveInfinity, max = Double.NegativeInfinity, mean = 0;
                foreach (string[] row in table.Rows)
                {
                    double number; if (String.IsNullOrWhiteSpace(row[j])) { missing++; continue; }
                    if (!Numeric(row[j], out number)) { invalid++; continue; }
                    numeric++; min = Math.Min(min, number); max = Math.Max(max, number); mean += (number - mean) / numeric;
                }
                if (numeric > 0) numericColumns.Add(j);
                columns.Add(D("name", table.Columns[j], "index", j, "numericCount", numeric, "missingCount", missing, "invalidCount", invalid,
                    "min", numeric > 0 ? (object)min : null, "max", numeric > 0 ? (object)max : null, "mean", numeric > 0 ? (object)mean : null, "unit", "unknown"));
            }
            return D("resultKind", "csv-data-table", "columns", columns.ToArray(), "numericColumns", numericColumns.ToArray(), "rowCount", table.Rows.Count,
                "rowsPreview", table.Rows.GetRange(0, Math.Min(8, table.Rows.Count)).ToArray(), "rowsPreviewComplete", table.Rows.Count <= 8,
                "source", Source(entry), "fittingPerformed", false, "physicalQuantumOperations", 0, "measurementShots", 0,
                "numericPolicy", "Invariant finite decimal/scientific numbers with absolute magnitude <= 1e150. Empty, nonnumeric and nonfinite selected cells are excluded from fitting with recorded reasons.",
                "scope", "Inert CSV data inspection. Header labels and units are not inferred as equations, causality, dates or future predictions. Choose target/features explicitly with q.input fit.");
        }
        private static string[] CellList(string text, string label)
        {
            text = text.Trim(); if (text.Length < 2 || text[0] != '[' || text[text.Length - 1] != ']') throw new ArgumentException(label + " requires a bracketed comma-separated list.");
            string[] cells = SpinDoctorCalculators.SplitList(text); if (cells.Length == 0) throw new ArgumentException(label + " cannot be empty.");
            for (int i = 0; i < cells.Length; i++) { cells[i] = SpinDoctorCalculators.Unquote(cells[i].Trim()); if (cells[i].Length == 0) throw new ArgumentException(label + " contains an empty entry."); }
            return cells;
        }
        private static int Column(NumericTable table, string selector)
        {
            int index;
            if (Int32.TryParse(selector, NumberStyles.None, CultureInfo.InvariantCulture, out index))
            {
                if (index < 0 || index >= table.Columns.Length) throw new ArgumentException("CSV column index is outside the header."); return index;
            }
            for (int j = 0; j < table.Columns.Length; j++) if (String.Equals(table.Columns[j], selector, StringComparison.OrdinalIgnoreCase)) return j;
            throw new ArgumentException("CSV column was not found: " + selector + ". Use the exact header name or a zero-based index.");
        }
        private static string Unit(string unit)
        {
            if (unit.Length < 1 || unit.Length > 64) throw new ArgumentException("Unit labels require 1..64 characters.");
            foreach (char ch in unit) if (Char.IsControl(ch)) throw new ArgumentException("Unit labels cannot contain control characters."); return unit;
        }
        private static double Norm(double[,] matrix, int column, int start, int count)
        {
            double scale = 0, sum = 1;
            for (int i = start; i < count; i++)
            {
                double value = Math.Abs(matrix[i, column]); if (value == 0) continue;
                if (scale < value) { double ratio = scale / value; sum = 1 + sum * ratio * ratio; scale = value; }
                else { double ratio = value / scale; sum += ratio * ratio; }
            }
            return scale == 0 ? 0 : scale * Math.Sqrt(sum);
        }
        private static double Norm(double[] values)
        {
            double scale = 0, sum = 1;
            foreach (double item in values)
            {
                double value = Math.Abs(item); if (value == 0) continue;
                if (scale < value) { double ratio = scale / value; sum = 1 + sum * ratio * ratio; scale = value; }
                else { double ratio = value / scale; sum += ratio * ratio; }
            }
            return scale == 0 ? 0 : scale * Math.Sqrt(sum);
        }
        private static double[] SolveQr(double[,] matrix, double[] rhs, int n, int p, out double diagonalRatio)
        {
            int[] permutation = new int[p]; for (int j = 0; j < p; j++) permutation[j] = j;
            double threshold = Math.Sqrt(n) * 1e-12, largest = 0, smallest = Double.PositiveInfinity;
            for (int k = 0; k < p; k++)
            {
                int pivot = k; double best = -1;
                for (int j = k; j < p; j++) { double norm = Norm(matrix, j, k, n); if (norm > best) { best = norm; pivot = j; } }
                if (best <= threshold) throw new ArgumentException("Selected CSV features are rank deficient or numerically indistinguishable. Remove constant/duplicate/collinear predictors; no unique fitted equation is published.");
                if (pivot != k)
                {
                    for (int i = 0; i < n; i++) { double value = matrix[i, k]; matrix[i, k] = matrix[i, pivot]; matrix[i, pivot] = value; }
                    int order = permutation[k]; permutation[k] = permutation[pivot]; permutation[pivot] = order;
                }
                double initial = matrix[k, k], diagonal = initial >= 0 ? -best : best, divisor = initial - diagonal;
                double tau = (diagonal - initial) / diagonal;
                matrix[k, k] = diagonal; for (int i = k + 1; i < n; i++) matrix[i, k] /= divisor;
                for (int j = k + 1; j < p; j++)
                {
                    double projection = matrix[k, j]; for (int i = k + 1; i < n; i++) projection += matrix[i, k] * matrix[i, j]; projection *= tau;
                    matrix[k, j] -= projection; for (int i = k + 1; i < n; i++) matrix[i, j] -= matrix[i, k] * projection;
                }
                double dot = rhs[k]; for (int i = k + 1; i < n; i++) dot += matrix[i, k] * rhs[i]; dot *= tau;
                rhs[k] -= dot; for (int i = k + 1; i < n; i++) rhs[i] -= matrix[i, k] * dot;
                largest = Math.Max(largest, Math.Abs(diagonal)); smallest = Math.Min(smallest, Math.Abs(diagonal));
            }
            double[] answer = new double[p], pivoted = new double[p];
            for (int k = p - 1; k >= 0; k--) { double value = rhs[k]; for (int j = k + 1; j < p; j++) value -= matrix[k, j] * pivoted[j]; pivoted[k] = value / matrix[k, k]; Finite(pivoted[k]); }
            for (int j = 0; j < p; j++) answer[permutation[j]] = pivoted[j]; diagonalRatio = largest / smallest; return answer;
        }
        private static Dictionary<string, object> FitTable(Entry entry, NumericTable table, Dictionary<string, string> options)
        {
            int target = Column(table, options["target"]); string[] selectors = CellList(options["features"], "features");
            if (selectors.Length > 16) throw new ArgumentException("CSV linear fits support 1..16 predictors.");
            int[] selected = new int[selectors.Length]; HashSet<int> unique = new HashSet<int>();
            for (int j = 0; j < selected.Length; j++) { selected[j] = Column(table, selectors[j]); if (selected[j] == target) throw new ArgumentException("The target column cannot also be a feature."); if (!unique.Add(selected[j])) throw new ArgumentException("Duplicate CSV feature selection."); }
            string targetUnit = options.ContainsKey("target-unit") ? Unit(options["target-unit"]) : "unknown";
            string[] units = new string[selected.Length]; for (int j = 0; j < units.Length; j++) units[j] = "unknown";
            if (options.ContainsKey("feature-units")) { units = CellList(options["feature-units"], "feature-units"); if (units.Length != selected.Length) throw new ArgumentException("feature-units must match the feature count and order."); for (int j = 0; j < units.Length; j++) units[j] = Unit(units[j]); }
            List<double[]> values = new List<double[]>(); List<int> indexes = new List<int>(); List<object> excluded = new List<object>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                double[] row = new double[selected.Length + 1]; List<string> reasons = new List<string>();
                if (!Numeric(table.Rows[i][target], out row[0])) reasons.Add("target " + table.Columns[target] + " is missing, nonnumeric, nonfinite or outside +/-1e150");
                for (int j = 0; j < selected.Length; j++) if (!Numeric(table.Rows[i][selected[j]], out row[j + 1])) reasons.Add("feature " + table.Columns[selected[j]] + " is missing, nonnumeric, nonfinite or outside +/-1e150");
                if (reasons.Count > 0) excluded.Add(D("rowIndex", i, "reason", String.Join("; ", reasons.ToArray()))); else { values.Add(row); indexes.Add(i); }
            }
            int n = values.Count, p = selected.Length + 1;
            if (n <= p) throw new ArgumentException("CSV linear fit needs more complete numeric rows than fitted parameters (intercept plus predictors). Used " + n + ", required at least " + (p + 1) + ".");
            double[] means = new double[p], minima = new double[p], maxima = new double[p], scales = new double[p];
            for (int j = 0; j < p; j++) { minima[j] = Double.PositiveInfinity; maxima[j] = Double.NegativeInfinity; for (int i = 0; i < n; i++) { double value = values[i][j]; means[j] += (value - means[j]) / (i + 1); minima[j] = Math.Min(minima[j], value); maxima[j] = Math.Max(maxima[j], value); } for (int i = 0; i < n; i++) scales[j] = Math.Max(scales[j], Math.Abs(values[i][j] - means[j])); }
            for (int j = 1; j < p; j++) if (scales[j] == 0) throw new ArgumentException("CSV feature is constant: " + table.Columns[selected[j - 1]] + ". Remove it before fitting; the intercept already represents a constant.");
            double[,] design = new double[n, p]; double[] rhs = new double[n];
            for (int i = 0; i < n; i++) { design[i, 0] = 1; rhs[i] = values[i][0] - means[0]; for (int j = 1; j < p; j++) design[i, j] = (values[i][j] - means[j]) / scales[j]; }
            double diagonalRatio; double[] centered = SolveQr(design, rhs, n, p, out diagonalRatio);
            double intercept = means[0] + centered[0]; List<object> featureObjects = new List<object>(), coefficientObjects = new List<object>(), featureStats = new List<object>(); double[] slopes = new double[selected.Length];
            for (int j = 0; j < selected.Length; j++)
            {
                slopes[j] = centered[j + 1] / scales[j + 1]; Finite(slopes[j]); intercept -= slopes[j] * means[j + 1];
                featureObjects.Add(D("name", table.Columns[selected[j]], "columnIndex", selected[j], "unit", units[j]));
                coefficientObjects.Add(D("feature", table.Columns[selected[j]], "coefficient", slopes[j], "unit", targetUnit == "unknown" || units[j] == "unknown" ? "unknown" : targetUnit + "/(" + units[j] + ")"));
                featureStats.Add(D("name", table.Columns[selected[j]], "min", minima[j + 1], "max", maxima[j + 1], "mean", means[j + 1], "scale", scales[j + 1], "unit", units[j]));
            }
            Finite(intercept); List<object> dataRows = new List<object>(); double sumAbsolute = 0; double[] residuals = new double[n], targetDeviations = new double[n];
            for (int i = 0; i < n; i++)
            {
                double fitted = means[0] + centered[0]; double[] features = new double[selected.Length];
                for (int j = 0; j < features.Length; j++) { features[j] = values[i][j + 1]; fitted += centered[j + 1] * ((features[j] - means[j + 1]) / scales[j + 1]); }
                double observed = values[i][0], residual = observed - fitted, deviation = observed - means[0]; Finite(fitted); Finite(residual);
                residuals[i] = residual; targetDeviations[i] = deviation; sumAbsolute += Math.Abs(residual);
                dataRows.Add(D("rowIndex", indexes[i], "observed", observed, "fitted", fitted, "residual", residual, "features", features));
            }
            double residualNorm = Norm(residuals), targetNorm = Norm(targetDeviations), sumSquared = residualNorm * residualNorm;
            Finite(sumSquared); Finite(sumAbsolute); bool constantTarget = maxima[0] == minima[0];
            double rmse = residualNorm / Math.Sqrt(n), mae = sumAbsolute / n;
            double relativeResidual = constantTarget ? 0 : residualNorm / targetNorm;
            object rSquared = constantTarget ? null : (object)(1 - relativeResidual * relativeResidual);
            if (rSquared != null) Finite((double)rSquared);
            StringBuilder equation = new StringBuilder(table.Columns[target] + " = " + intercept.ToString("R", CultureInfo.InvariantCulture));
            for (int j = 0; j < slopes.Length; j++) equation.Append(" + (").Append(slopes[j].ToString("R", CultureInfo.InvariantCulture)).Append(") * [").Append(table.Columns[selected[j]]).Append("]");
            StringBuilder stableEquation = new StringBuilder(table.Columns[target] + " = " + (means[0] + centered[0]).ToString("R", CultureInfo.InvariantCulture));
            for (int j = 0; j < slopes.Length; j++) stableEquation.Append(" + (").Append(slopes[j].ToString("R", CultureInfo.InvariantCulture)).Append(") * ([").Append(table.Columns[selected[j]]).Append("] - (").Append(means[j + 1].ToString("R", CultureInfo.InvariantCulture)).Append("))");
            List<string> warnings = new List<string>(); warnings.Add("Coefficients describe an empirical linear relationship in the supplied rows. They do not establish physical causality or validated future forecasts."); warnings.Add("R-squared, RMSE and MAE use the same rows used to fit the model; no independent holdout validation was performed.");
            if (excluded.Count > 0) warnings.Add("Rows with missing, nonnumeric, nonfinite or out-of-range selected cells were excluded; see every row index and reason.");
            if (constantTarget) warnings.Add("Target is constant. R-squared is undefined and is returned as null, rather than an invented zero or perfect score.");
            if (targetUnit == "unknown" || Array.IndexOf(units, "unknown") >= 0) warnings.Add("At least one unit is unknown. Units are user-supplied labels and are not inferred from the data.");
            if (residualNorm > 0 && sumSquared == 0) warnings.Add("Residual sum of squares is below the representable FP64 range and rounds to zero; RMSE and R-squared were computed using scaled norms.");
            if (diagonalRatio > 1e8) warnings.Add("QR diagonal ratio is large; fitted coefficients may be sensitive to rounding or small input changes. This ratio is a diagnostic, not a full condition-number estimate.");
            return D("resultKind", "empirical-csv-fit", "model", "ordinary-least-squares-linear", "equation", equation.ToString(), "centeredEquation", stableEquation.ToString(),
                "equationConvention", "target = intercept + sum(coefficients[i].coefficient * features[i]); fitted rows are evaluated in centered/scaled coordinates to reduce cancellation.",
                "target", D("name", table.Columns[target], "columnIndex", target, "unit", targetUnit), "features", featureObjects.ToArray(), "intercept", intercept, "coefficients", coefficientObjects.ToArray(),
                "metrics", D("rSquared", rSquared, "rmse", rmse, "mae", mae, "residualSumSquares", sumSquared, "residualSumSquaresRepresentable", residualNorm == 0 || sumSquared > 0, "targetMean", means[0], "constantTarget", constantTarget, "trainingOnly", true, "holdoutRows", 0),
                "rowCount", table.Rows.Count, "usedRows", n, "excludedRows", excluded.Count, "excluded", excluded.ToArray(), "dataRows", dataRows.ToArray(), "dataRowsComplete", true,
                "featureStatistics", featureStats.ToArray(), "targetStatistics", D("min", minima[0], "max", maxima[0], "mean", means[0], "unit", targetUnit), "source", Source(entry),
                "method", "centered/scaled column-pivoted Householder QR", "rank", p, "parameterCount", p, "qrDiagonalRatio", diagonalRatio, "residualDegreesOfFreedom", n - p, "warnings", warnings.ToArray(),
                "fittingPerformed", true, "physicalQuantumOperations", 0, "measurementShots", 0, "scope", "Bounded classical empirical CSV regression using actual supplied numeric rows. In-sample diagnostics only; no physical-law discovery, causal proof, quantum computation, holdout validation or future prediction is claimed.");
        }

        private static Dictionary<string, double> Resolve(Dictionary<string, string> definitions, Dictionary<string, double> bindings, bool allowMissing)
        {
            Dictionary<string, double> values = bindings == null ? new Dictionary<string, double>(StringComparer.Ordinal) : new Dictionary<string, double>(bindings, StringComparer.Ordinal);
            foreach (KeyValuePair<string, double> binding in values) Finite(binding.Value);
            HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal); foreach (string name in definitions.Keys) if (!values.ContainsKey(name)) pending.Add(name);
            for (int pass = 0; pending.Count > 0 && pass <= definitions.Count; ++pass)
            {
                bool changed = false; List<string> names = new List<string>(pending);
                foreach (string name in names)
                {
                    if (definitions[name] == null) continue;
                    try { values.Add(name, SafeExpression.Evaluate(definitions[name], values)); pending.Remove(name); changed = true; } catch (MissingVariable) { }
                }
                if (!changed) break;
            }
            if (pending.Count > 0 && !allowMissing) throw new MissingVariable(String.Join(",", new List<string>(pending).ToArray()) + " (undefined or cyclic parameter equations)");
            return values;
        }
        private static GatePlan Gate(string name, string[] angles, int[] qubits, bool all, int n)
        {
            name = Alias(name); Dictionary<string, object> spec = GateSpec(name);
            if (angles.Length != (int)spec["angleCount"]) throw new ArgumentException("Incorrect angle count for gate " + name + ".");
            if (all && !(bool)spec["local"]) throw new ArgumentException("all applies only to local gates.");
            if ((!all && qubits.Length != (int)spec["targetCount"]) || (all && qubits.Length != 0)) throw new ArgumentException("Incorrect qubit count for gate " + name + ".");
            HashSet<int> unique = new HashSet<int>(); foreach (int qubit in qubits) if (qubit < 0 || qubit >= n || !unique.Add(qubit)) throw new ArgumentException("Gate targets must be distinct and within the selected register.");
            foreach (string angle in angles) SafeExpression.Validate(angle); return new GatePlan { Name = name, Angles = angles, Qubits = qubits, All = all };
        }
        private static void ValidatePlan(Plan plan)
        {
            if (plan.Qubits < 1 || plan.Qubits > 298 || plan.InitialBasis >= (BigInteger.One << plan.Qubits)) throw new ArgumentException("Circuit requires1..298 qubits and initialBasis below2^qubits.");
            if (plan.Gates.Count > 8192) throw new ArgumentException("Circuit exceeds8192 instructions.");
        }
        private static Dictionary<string, object> GateSpec(string name) { foreach (Dictionary<string, object> gate in TrancheRegister.SupportedGates) if ((string)gate["name"] == name) return gate; throw new ArgumentException("Unsupported gate name: " + name); }
        private static string Alias(string name) { name = name.ToLowerInvariant(); if (name == "cnot") return "cx"; if (name == "toffoli") return "ccx"; if (name == "fredkin") return "cswap"; if (name == "id" || name == "identity") return "i"; if (name == "u3") return "u"; return name; }
        private static Dictionary<string, object> Help()
        {
            return D("commands", new string[] { "q.input list", "q.input show ID", "q.input table ID", "q.input fit ID target=COLUMN features=[COLUMN,...] [target-unit=UNIT] [feature-units=[UNIT,...]]", "q.input evaluate ID [row N] [base BASE_ID] [name=expression...]", "q.input import ID [row N] [base BASE_ID] [name=NUMERIC_VALUE...]","q.input run ID", "q.input delete ID", "q.input export ID", "q.input help" },
                "formats", "UTF-8 JSON circuit specs or explicit commands arrays; OpenQASM2 unitary subset with one qreg and optional final complete Z measurement; TXT circuit configuration or documented q.commands; CSV parameter sweeps with an explicit base attachment, an explicit gate-list circuit, numeric measurement tables with explicit empirical fitting, or a single command column. PDF binary sources must embed one UTF-8 program or contain one explicit SPINDOCTOR-JSON/QASM/TXT/CSV-BEGIN and matching END tagged text block. Prose, scanned images and unsupported PDF encodings are not guessed or OCRed.",
                "jsonExample", "{\"name\":\"my-equation\",\"qubits\":2,\"initialBasis\":\"0\",\"parameters\":{\"theta\":\"pi/4*sqrt(2)\"},\"gates\":[{\"name\":\"ry\",\"angles\":[\"theta\"],\"qubits\":[0]},{\"name\":\"cx\",\"qubits\":[0,1]}],\"shots\":100,\"basis\":\"z\"}",
                "expressions", "Case-sensitive declared parameters; pi,e,+,-,*,/,^; sin,cos,tan,sqrt,exp,log,abs,asin,acos,atan. Angles are radians. No code execution or external includes.",
                "csv", "A single command header accepts 1..128 documented q.commands, each 1..4096 characters. Quote cells containing commas; double literal quotes. For parameter sweeps, header names must be declared parameters in an explicit base circuit: 1..64 columns, 1..4096 rows, expressions up to 1024 characters, zero-based row indices. Generic tables and arbitrary algebra are not inferred as equations.",
                "sequenceExample","{\"commands\":[\"q.math norm amplitudes=[1,0]\",\"q.status\"]}",
                "dataCsv", "Measurement tables are inert data. Inspect with q.input table ID, then explicitly choose one target and 1..16 predictors with q.input fit. Select headers by name or zero-based column index. Up to 4096 rows; selected values must be invariant finite numbers with magnitude <=1e150. Missing/invalid selected cells are excluded with recorded row reasons. Units remain unknown unless supplied. Result is an empirical in-sample linear fit with native coefficients, diagnostics and residuals; no physical causality or validated forecasting is inferred.",
                "gateCsv", "Header gate,n,targets,angles; optional name,initialBasis,shots,basis. Declare n=1..298 in the first gate row; circuit metadata may be blank later or repeat consistently. Targets and angles use comma-separated lists, optionally bracketed and CSV-quoted; target all is allowed only for local gates. Angles use supported safe expressions in radians. Gate order/control operands are preserved and validated. q.input import defines; q.input run or folder mode executes explicitly with declared shots/basis, default 1024 Z shots, maximum 100000 shots.",
                "pdfExport","Original PDF bytes are preserved and exported through the authenticated binary download; q.input show displays only the safely extracted program preview.",
                "limits", D("fileBytes", MaximumBytes,"pdfBytes",4194304,"folderFileBytes",4194304,"sequenceCommands",128, "files", MaximumFiles, "totalBytes", MaximumTotalBytes, "qubits", 298, "circuitInstructions", 8192, "parameters", 64),
                "scope", "Upload, preview, evaluate and transient compilation read data only. Import atomically creates/selects a circuit. q.input run explicitly executes an attachment program or structured commands in order, waiting for native asynchronous calculations. Attachments never execute automatically.");
        }

        private Entry Find(string id)
        {
            ValidateId(id); foreach (Entry entry in entries) if (entry.Id == id) return entry; throw new ArgumentException("Unknown attachment ID.");
        }
        private int TotalBytes() { int total = 0; foreach (Entry entry in entries) total += entry.Bytes; return total; }
        private string SourcePath(string id) { ValidateId(id); return Path.Combine(directory, id + ".utf8"); }
        private string Read(Entry entry)
        {
            byte[] bytes=ReadBytes(entry);
            if(Path.GetExtension(entry.Filename).ToLowerInvariant()==".pdf")return SpinDoctorPdfInputs.Extract(bytes).Content;
            return Utf8.GetString(bytes);
        }
        private byte[] ReadBytes(Entry entry)
        {
            byte[] bytes=ReadBounded(SourcePath(entry.Id),MaximumBytes);
            if (bytes.Length != entry.Bytes || Hash(bytes) != entry.Sha) throw new InvalidDataException("Stored attachment content/hash mismatch."); return bytes;
        }
        private void SaveCatalog()
        {
            List<object> rows = new List<object>(); foreach (Entry entry in entries) rows.Add(D("id", entry.Id, "filename", entry.Filename, "sha256", entry.Sha, "bytes", entry.Bytes, "uploadedUtc", entry.UploadedUtc));
            string payload = json.Serialize(rows.ToArray()); string data = json.Serialize(D("schema", "SPINDOCTOR/INPUTS/1", "entries", rows.ToArray(), "entriesSha256", Hash(Utf8.GetBytes(payload))));
            string temporary = catalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary,data,Utf8);if(File.Exists(catalogPath))File.Replace(temporary,catalogPath,null);else File.Move(temporary,catalogPath); }
            finally { try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){} }
        }
        private void LoadCatalog()
        {
            Dictionary<string, object> data = json.Deserialize<Dictionary<string, object>>(Utf8.GetString(ReadBounded(catalogPath,262144)));
            if(data==null)throw new InvalidDataException("Attachment catalog must be an object.");
            if (Text(Required(data, "schema")) != "SPINDOCTOR/INPUTS/1") throw new InvalidDataException("Unsupported attachment catalog.");
            object[] rows = ToArray(Required(data, "entries")); if (rows.Length > MaximumFiles || Hash(Utf8.GetBytes(json.Serialize(rows))) != Text(Required(data, "entriesSha256"))) throw new InvalidDataException("Attachment catalog checksum/count mismatch.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (object value in rows)
            {
                Dictionary<string, object> row = Object(value, "catalog entry"); Entry entry = new Entry { Id = Text(Required(row, "id")), Filename = Text(Required(row, "filename")), Sha = Text(Required(row, "sha256")), Bytes = Integer(Text(Required(row, "bytes")), 0, MaximumBytes), UploadedUtc = Text(Required(row, "uploadedUtc")) };
                ValidateId(entry.Id); ValidateFilename(entry.Filename); if (!ids.Add(entry.Id) || !Regex.IsMatch(entry.Sha, "^[0-9a-f]{64}\\z")) throw new InvalidDataException("Invalid attachment identity/hash.");
                if (TotalBytes() > MaximumTotalBytes - entry.Bytes) throw new InvalidDataException("Saved attachment sources exceed the30 MiB aggregate limit.");
                bool pdf=Path.GetExtension(entry.Filename).Equals(".pdf",StringComparison.OrdinalIgnoreCase);
                if(pdf){if(entry.Bytes>4194304)throw new InvalidDataException("Saved PDF exceeds its 4 MiB source limit.");ReadBytes(entry);}
                string content = pdf?"":Read(entry); entry.Preview = Preview(entry, content); entries.Add(entry);
            }
        }
        private static byte[] ReadBounded(string path,int maximum)
        {
            using(FileStream stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(stream.Length>maximum)throw new InvalidDataException("Stored attachment exceeds its bounded read size.");
                byte[] bytes=new byte[(int)stream.Length];int at=0;while(at<bytes.Length){int count=stream.Read(bytes,at,bytes.Length-at);if(count==0)throw new InvalidDataException("Stored attachment was truncated.");at+=count;}return bytes;
            }
        }
        private static void ValidateId(string id) { if (id == null || !Regex.IsMatch(id, "^[0-9a-f]{32}\\z")) throw new ArgumentException("Expected an opaque32-character attachment ID."); }
        private static void ValidateFilename(string filename)
        {
            if (String.IsNullOrEmpty(filename) || filename.Length > 180 || filename != Path.GetFileName(filename) || filename == "." || filename == "..") throw new ArgumentException("Filename must be a plain attachment name without a path.");
            foreach (char ch in filename) if (Char.IsControl(ch) || ch == '/' || ch == '\\' || ch == ':' || ch == '<' || ch == '>' || ch == '"' || ch == '|' || ch == '?' || ch == '*') throw new ArgumentException("Invalid attachment filename character.");
        }
        private static string DefaultName(Entry entry) { string name = Path.GetFileNameWithoutExtension(entry.Filename); try { return CircuitName(name); } catch (ArgumentException) { return "imported-" + entry.Id.Substring(0, 8); } }
        private static string CircuitName(string name) { if (name == null || !Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_-]{0,31}\\z")) throw new ArgumentException("Circuit names use an ASCII letter followed by up to31 letters/digits/-/_."); return name; }
        private static string ParameterName(string name) { if (name == null || !Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]{0,63}\\z") || name == "pi" || name == "e") throw new ArgumentException("Parameter names are identifiers up to64 characters; pi/e are reserved constants."); return name; }
        private static string ExpressionText(object value) { if (value == null || value is bool || value is IDictionary || value is IEnumerable && !(value is string)) throw new ArgumentException("Angle/parameter must be a finite number or safe arithmetic expression string."); return Text(value); }
        private static string Basis(string basis) { basis = basis.ToLowerInvariant(); if (basis != "x" && basis != "y" && basis != "z") throw new ArgumentException("Measurement basis must be x/y/z."); return basis; }
        private static bool Boolean(object value) { if (!(value is bool)) throw new ArgumentException("all must be a JSON boolean."); return (bool)value; }
        private static int Integer(string value, int min, int max) { int result; if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) || result < min || result > max) throw new ArgumentException("Integer is outside the supported range."); return result; }
        private static BigInteger BasisIndex(string value) { BigInteger result; if (String.IsNullOrEmpty(value) || !Regex.IsMatch(value,@"^[0-9]+$") || !BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) || result < BigInteger.Zero) throw new ArgumentException("Expected a nonnegative decimal basis index."); return result; }
        private static ulong Unsigned(string value) { ulong result; if (!UInt64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result)) throw new ArgumentException("Expected an unsigned decimal integer."); return result; }
        private static void Finite(double value) { if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentException("Equation result must be finite."); }
        private static string Text(object value) { if (value == null) throw new ArgumentException("Required value is null."); return Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static Dictionary<string, object> Object(object value, string label) { Dictionary<string, object> result = value as Dictionary<string, object>; if (result == null) throw new ArgumentException(label + " must be a JSON object."); return result; }
        private static object Required(Dictionary<string, object> data, string key) { object value; if (!data.TryGetValue(key, out value)) throw new ArgumentException("Missing field: " + key); return value; }
        private static IEnumerable Sequence(object value) { IEnumerable sequence = value as IEnumerable; if (sequence == null || value is string || value is IDictionary) throw new ArgumentException("Expected an array."); return sequence; }
        private static object[] ToArray(object value) { List<object> result = new List<object>(); foreach (object item in Sequence(value)) result.Add(item); return result.ToArray(); }
        private static bool Eq(string a, string b) { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static void Exact(string[] words, int count) { if (words.Length != count) throw new ArgumentException("Incorrect q.input argument count."); }
        private static string Hash(byte[] bytes) { using (SHA256 hash = SHA256.Create()) { StringBuilder value = new StringBuilder(64); foreach (byte item in hash.ComputeHash(bytes)) value.Append(item.ToString("x2", CultureInfo.InvariantCulture)); return value.ToString(); } }
        private static Dictionary<string, object> D(params object[] values) { Dictionary<string, object> result = new Dictionary<string, object>(); for (int j = 0; j < values.Length; j += 2) result.Add((string)values[j], values[j + 1]); return result; }

        private sealed class SafeExpression
        {
            private readonly string text;
            private readonly Dictionary<string, double> variables;
            private readonly bool syntaxOnly;
            private int position, depth, operations;
            private SafeExpression(string expression, Dictionary<string, double> bindings, bool syntax)
            {
                if (String.IsNullOrWhiteSpace(expression) || expression.Length > 1024) throw new ArgumentException("Arithmetic expressions require1..1024 characters."); text = expression; variables = bindings; syntaxOnly = syntax;
            }
            public static void Validate(string expression) { new SafeExpression(expression, null, true).Parse(); }
            public static double Evaluate(string expression, Dictionary<string, double> bindings) { return new SafeExpression(expression, bindings, false).Parse(); }
            private double Parse() { double result = Add(); White(); if (position != text.Length) throw new ArgumentException("Unexpected arithmetic token at character " + position + "."); if (!syntaxOnly) Finite(result); return result; }
            private double Add()
            {
                double value = Multiply(); while (true) { if (Eat('+')) { double right = Multiply(); value = Value(value + right); } else if (Eat('-')) { double right = Multiply(); value = Value(value - right); } else return value; }
            }
            private double Multiply()
            {
                double value = Unary(); while (true) { if (Eat('*')) { double right = Unary(); value = Value(value * right); } else if (Eat('/')) { double right = Unary(); value = Value(syntaxOnly ? 0 : value / right); } else return value; }
            }
            private double Unary()
            {
                Enter(); try { if (Eat('+')) return Unary(); if (Eat('-')) return Value(-Unary()); return Power(); } finally { depth--; }
            }
            private double Power()
            {
                double value = Atom(); if (Eat('^')) { double power = Unary(); value = Value(syntaxOnly ? 0 : Math.Pow(value, power)); } return value;
            }
            private double Atom()
            {
                White(); if (Eat('(')) { double value = Add(); if (!Eat(')')) throw new ArgumentException("Missing arithmetic closing parenthesis."); return value; }
                if (position >= text.Length) throw new ArgumentException("Unexpected end of arithmetic expression."); char ch = text[position];
                if (ch >= '0' && ch <= '9' || ch == '.')
                {
                    int start = position; bool digit = false; while (position < text.Length && Char.IsDigit(text[position])) { digit = true; position++; }
                    if (position < text.Length && text[position] == '.') { position++; while (position < text.Length && Char.IsDigit(text[position])) { digit = true; position++; } }
                    if (!digit) throw new ArgumentException("Invalid numeric literal.");
                    if (position < text.Length && (text[position] == 'e' || text[position] == 'E')) { position++; if (position < text.Length && (text[position] == '+' || text[position] == '-')) position++; int exponent = position; while (position < text.Length && Char.IsDigit(text[position])) position++; if (position == exponent) throw new ArgumentException("Invalid numeric exponent."); }
                    double number; if (!Double.TryParse(text.Substring(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) throw new ArgumentException("Invalid numeric literal."); Finite(number); return number;
                }
                if (Char.IsLetter(ch) || ch == '_')
                {
                    int start = position++; while (position < text.Length && (Char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
                    string name = text.Substring(start, position - start);
                    if (Eat('('))
                    {
                        double value = Add(); if (!Eat(')')) throw new ArgumentException("Function requires one arithmetic argument."); string function = name.ToLowerInvariant();
                        if (function != "sin" && function != "cos" && function != "tan" && function != "sqrt" && function != "exp" && function != "log" && function != "abs" && function != "asin" && function != "acos" && function != "atan") throw new ArgumentException("Unsupported arithmetic function: " + name);
                        if (syntaxOnly) return 0;
                        switch (function) { case "sin": return Value(Math.Sin(value)); case "cos": return Value(Math.Cos(value)); case "tan": return Value(Math.Tan(value)); case "sqrt": return Value(Math.Sqrt(value)); case "exp": return Value(Math.Exp(value)); case "log": return Value(Math.Log(value)); case "abs": return Value(Math.Abs(value)); case "asin": return Value(Math.Asin(value)); case "acos": return Value(Math.Acos(value)); default: return Value(Math.Atan(value)); }
                    }
                    if (name == "pi") return Math.PI; if (name == "e") return Math.E;
                    if (syntaxOnly) { ParameterName(name); return 0; } double variable; if (variables == null || !variables.TryGetValue(name, out variable)) throw new MissingVariable(name); Finite(variable); return variable;
                }
                throw new ArgumentException("Unsupported arithmetic character: " + ch);
            }
            private double Value(double value) { if (++operations > 512) throw new ArgumentException("Arithmetic expression operation limit exceeded."); if (!syntaxOnly) Finite(value); return syntaxOnly ? 0 : value; }
            private void Enter() { if (++depth > 32) throw new ArgumentException("Arithmetic expression nesting limit exceeded."); }
            private void White() { while (position < text.Length && Char.IsWhiteSpace(text[position])) position++; }
            private bool Eat(char token) { White(); if (position < text.Length && text[position] == token) { position++; return true; } return false; }
        }
    }
}
