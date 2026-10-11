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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class AccountingReportRegressions
{
    static JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 33554432, RecursionLimit = 100 };
    static int assertions;
    static Dictionary<string,object> D(params object[] pairs) { return SpinDoctorCalculators.D(pairs); }
    static Dictionary<string,object> Map(object value) { return value as Dictionary<string,object> ?? D(); }
    static void Check(bool condition,string message) { assertions++; if(!condition)throw new Exception("FAIL: " + message); }
    static string Hash(byte[] value) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant(); }
    static string Text(byte[] bytes)
    {
        string raw=Encoding.ASCII.GetString(bytes);StringBuilder text=new StringBuilder();
        foreach(Match match in Regex.Matches(raw,@"\(((?:\\.|[^\\)])*)\)\s*Tj"))text.Append(Regex.Replace(match.Groups[1].Value,@"\\([\\()])","$1")).Append(' ');
        return Regex.Replace(text.ToString(),@"\s+"," ");
    }
    static Dictionary<string,object> Record(string command,Dictionary<string,object> response) { return D("command",command,"receivedUtc","2026-10-10T17:12:10.0381745Z","nativeResponse",response,"recordSource","Synthetic report regression; not a user simulation"); }
    static byte[] Pdf(Dictionary<string,object> record,string view) { return SpinDoctorReportPdf.Build(D("kind","selected","view",view,"records",new object[]{record}),null); }
    static void Has(string text,string expected,string message) { Check(text.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0,message); }
    public static void Run(string archive,string output)
    {
        if(Directory.Exists(output))throw new ArgumentException("Fresh PDF output folder required.");Directory.CreateDirectory(output);
        byte[] original=File.ReadAllBytes(archive);string archiveHash=Hash(original);
        Check(archiveHash=="a56749d73e73967ca3f99a878e79eaa83308eac2ff6e7d3effd54bf4afacca75","matched immutable native QFT archive bytes");
        Dictionary<string,object> saved=Map(Json.DeserializeObject(Encoding.UTF8.GetString(original))),response=Map(saved["response"]),result=Map(response["result"]),status=Map(response["status"]);
        Check((string)saved["command"]=="q.circuit preset qft 29","actual definition command retained");Check(Convert.ToInt32(result["qubits"])==29&&Convert.ToInt32(result["gateCount"])==449,"native29qubit449instruction metadata");
        IList gates=(IList)result["gates"];Check(gates.Count==449,"every original gate retained");var composition=new Dictionary<string,int>();
        foreach(object item in gates){string name=Convert.ToString(Map(item)["name"]);if(!composition.ContainsKey(name))composition[name]=0;composition[name]++;}
        Check(composition["h"]==29&&composition["cp"]==406&&composition["swap"]==14,"actual gate composition29/406/14");
        foreach(var counter in Map(Map(status["circuit"])["lifetime"]))Check(Convert.ToString(counter.Value)=="0","native lifetime zero retained: "+counter.Key);
        string before=Json.Serialize(response);var record=Record((string)saved["command"],response);
        record["receivedUtc"]=saved["capturedUtc"];record["commandCompletedUtc"]=saved["commandCompletedUtc"];record["completionScope"]=saved["completionScope"];record["executionOutcome"]=saved["executionOutcome"];
        record["recordSource"]="Read-only replay of matched native3.0.9 command archive756aeae4bef8462eae0d902846b50ed8; original archive SHA-256="+archiveHash+". No new execution or sampling.";
        byte[] detailed=Pdf(record,"details");string detailedText=Text(detailed);
        Has(detailedText,"Circuit defined, not run","definition status prominent");Has(detailedText,"449","actual defined instruction count printed");Has(detailedText,"406","controlled-phase composition printed");
        Has(detailedText,"not this command's work delta","snapshot delta boundary retained");Has(detailedText,"procedural","procedural/circuit distinction printed");
        Has(detailedText,"17:12:10.0381745Z","original synchronous completion stamp retained");Has(detailedText,"3.0.9-candidate","original native version retained");Has(detailedText,"3.1.0-candidate","new renderer version identified");
        Has(detailedText,"449. swap","last exact gate operand printed");Has(detailedText,"q.circuit run","execution workflow printed");Has(detailedText,"1024","requested default shots guidance printed");
        Has(detailedText,"Inactive counter groups are compacted","inactive group presentation policy explicit");
        Has(detailedText,"Inactive snapshot: all 11 returned counters/flags","zero circuit group summarized without pretending it ran");
        string rawText=Text(Pdf(record,"raw"));
        Has(rawText,"executedCircuitInstructions","Complete PDF retains individual native circuit counter");
        Has(rawText,"actualCpuStates","Complete PDF retains individual procedural CPU counter");
        Check(rawText.IndexOf("Inactive snapshot:",StringComparison.Ordinal)<0,"Complete PDF leaves every supplied counter row uncompressed");
        Check(Json.Serialize(response)==before,"report rendering does not mutate any original response/counters/gates");Check(Hash(File.ReadAllBytes(archive))==archiveHash,"original native archive unchanged");
        File.WriteAllBytes(Path.Combine(output,"SpinDoctor-3.1.0-QFT-29-Definition-Report.pdf"),detailed);
        // Missing status is different from an explicitly returned zero.
        var missing=Record("q.circuit preset bell 2",D("ok",true,"result",D("name","bell","qubits",2,"initialBasis","0","gateCount",2,"descriptorHash",new string('a',64),"gates",new object[]{D("name","h","qubits",new int[]{0},"anglesExact",new string[0]),D("name","cx","qubits",new int[]{0,1},"anglesExact",new string[0])})));
        string missingText=Text(Pdf(missing,"summary"));Has(missingText,"Circuit defined, not run","missing status does not suppress definition semantics");Has(missingText,"Not returned","missing snapshot values not fabricated as zero");
        var empty=Record("q.math norm amplitudes=[1,0]",D("ok",true,"result",D("namespace","q.math","norm",1)));
        Check(Text(Pdf(empty,"summary")).IndexOf("Circuit defined, not run",StringComparison.Ordinal)<0,"calculator not mistaken for program definition");
        var queued=Record("q.circuit run shots=1024 basis=z",D("ok",true,"result",D("queued",true,"job",D("id","pending","phase","queued","active",true))));
        string queuedText=Text(Pdf(queued,"summary"));Check(queuedText.IndexOf("Circuit defined, not run",StringComparison.Ordinal)<0,"accepted job not classified as definition");Has(queuedText,"Partial job snapshot","pending execution remains partial");
        var completed=Record("q.circuit run shots=4 basis=z",D("ok",true,"result",D("id","fixture","phase","completed","active",false,"qubits",1,"initialBasis","0","basis","z","requestedShots","4","completedShots","4","histogramComplete",true,"histogram",new object[]{D("bitstring","0","count","4")},"counts",D("executedCircuitInstructions","5","actualAmplitudeWrites","10","completedShots","4")),"status",D("circuit",D("lifetime",D("executedCircuitInstructions","0","completedShots","0")))));
        string completedText=Text(Pdf(completed,"summary"));Check(completedText.IndexOf("Circuit defined, not run",StringComparison.Ordinal)<0,"finished run not classified as definition");Has(completedText,"executedCircuitInstructions","recorded job-local work printed");Has(completedText,"5","actual completed run instruction count retained independently");
        // A definition response may follow earlier actual work. Never reset lifetime values or claim the session never ran.
        var nonzero=Map(Json.DeserializeObject(Json.Serialize(response)));Map(Map(nonzero["status"])["circuit"])["lifetime"]=D("executedCircuitInstructions","123","completedShots","456");
        string nonzeroText=Text(Pdf(Record("q.circuit preset qft 29",nonzero),"summary"));Has(nonzeroText,"123","earlier actual lifetime instructions preserved");Has(nonzeroText,"456","earlier completed lifetime shots preserved");Has(nonzeroText,"Circuit defined, not run","this definition separate from earlier work");
        var tiny=D("ok",true,"result",D("phase","ready"),"status",D("circuit",D("lifetime",D("queryCount",1e-200,"completedShots",null))));
        string tinyText=Text(Pdf(Record("q.circuit status",tiny),"summary"));Has(tinyText,"1E-200","nonzero tiny numeric counter is not rounded into inactive scope");Has(tinyText,"completedShots","explicit unknown counter identity remains printed");Has(tinyText,"null","explicit null value remains unknown");Check(!tinyText.Contains("Inactive snapshot:"),"tiny and unknown values cannot be compacted as zero activity");
        var proof=D("status","PASS","assertions",assertions,"sourceArchiveSha256",archiveHash,"sourceArchiveId",saved["id"],"command",saved["command"],"nativeProductVersion",status["productVersion"],"reportRendererVersion",TerminalSession.ProductVersion,"definedInstructions",449,"h",29,"cp",406,"swap",14,"originalCompletionUtc",saved["commandCompletedUtc"],"newCircuitExecutions",0,"newShots",0,"newVerificationJobs",0,"sourceResponseUnchanged",true,"pdfSha256",Hash(detailed),"pdfBytes",detailed.Length);
        File.WriteAllText(Path.Combine(output,"accounting-report-proof.json"),Json.Serialize(proof),new UTF8Encoding(false));
        Console.WriteLine("PASS accounting/report definition regressions: "+assertions+"; matched actual3.0.9archive;0new executions/shots/verifications.");
    }
}
