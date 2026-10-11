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
using System.Text;
using System.Web.Script.Serialization;
using QuettaQuantum;
public static class NativeReportExample
{
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
 static Dictionary<string,object> D(params object[] pairs){return SpinDoctorCalculators.D(pairs);}
 static Dictionary<string,object> M(object value){return (Dictionary<string,object>)value;}
 static void Check(bool value,string message){if(!value)throw new Exception("FAIL "+message);}
 static Dictionary<string,object> Execute(TerminalSession session,string command){Dictionary<string,object> response=M(session.Execute(command));Check((bool)response["ok"],command+" returned successfully");return response;}
 static Dictionary<string,object> ReportRecord(Dictionary<string,object> response,string kind){return D("command",response["command"],"kind",kind,"nativeArchive",D("type","command","id",response["reportId"],"stage","accepted"));}
 public static void Run(string source,string output)
 {
  if(Directory.Exists(output))throw new InvalidOperationException("Use a fresh PDF output directory.");Directory.CreateDirectory(output);string workspace=Path.Combine(output,"isolated-native-workspace");Directory.CreateDirectory(Path.Combine(workspace,"quantum"));foreach(string path in Directory.GetFiles(Path.Combine(source,"quantum"),"*.cs"))File.Copy(path,Path.Combine(workspace,"quantum",Path.GetFileName(path)));foreach(string name in new string[]{"SPINDOCTOR_PRESETS.json","EquationCatalogue.json","QUANTUM_TERMINAL.html"})File.Copy(Path.Combine(source,name),Path.Combine(workspace,name));
  using(TerminalSession session=new TerminalSession(workspace,true))
  {
   Dictionary<string,object> initial=M(session.GetStatus());Check((bool)initial["gpuAvailable"],"real GPU required for native example");Execute(session,"q.prepare grid 128 128");Execute(session,"q.migrate t 1");Execute(session,"q.gate ry 0.3 all");Dictionary<string,object> verification=Execute(session,"q.verify samples 128 errors-only"),v=M(verification["result"]);Check((bool)v["passed"],"native CPU/GPU model agreement");Check(Convert.ToString(v["plannedSamples"],CultureInfo.InvariantCulture)=="140"&&Convert.ToString(v["verifiedCount"],CultureInfo.InvariantCulture)=="140","128 requested plus12 deterministic boundaries actually compared");Check(((IList)v["samplePreview"]).Count==32,"actual32-row preview");Check(Convert.ToInt32(M(v["sampleAssessment"])["distinctCpuPreviewVectors"],CultureInfo.InvariantCulture)>1,"nontrivial varied native model preview");
   Dictionary<string,object>[] calculations=new Dictionary<string,object>[] {Execute(session,"q.math bloch amplitudes=[1,0]"),Execute(session,"q.math bloch amplitudes=[0,1]"),Execute(session,"q.math bloch amplitudes=[1/sqrt(2),1/sqrt(2)]")};double[][] expected=new double[][]{new double[]{0,0,1},new double[]{0,0,-1},new double[]{1,0,0}};for(int i=0;i<3;i++){IList spin=(IList)M(calculations[i]["result"])["bloch"];for(int j=0;j<3;j++)Check(Math.Abs(Convert.ToDouble(spin[j],CultureInfo.InvariantCulture)-expected[i][j])<1e-12,"actual calculator Bloch direction "+i+":"+j);}
   Dictionary<string,object> final=M(session.GetStatus());Check(Convert.ToString(final["verifiedSamples"],CultureInfo.InvariantCulture)=="140"&&Convert.ToString(final["measurementShots"],CultureInfo.InvariantCulture)=="0","small real comparison workload and no shots");
   object[] records=new object[]{ReportRecord(calculations[0],"calculator"),ReportRecord(calculations[1],"calculator"),ReportRecord(calculations[2],"calculator"),ReportRecord(verification,"verification")};byte[] pdf=session.ReportPdf(D("kind","session","view","summary","records",records,"status",D()));File.WriteAllBytes(Path.Combine(output,"SpinDoctor-3.0.7-Actual-Bloch-and-Model-Examples.pdf"),pdf);File.WriteAllText(Path.Combine(output,"native-example-results.json"),Json.Serialize(D("passed",true,"version",TerminalSession.ProductVersion,"source",source,"gpuDevice",initial["gpuDevice"],"verification",v,"calculatorResponses",calculations,"pdfBytes",pdf.Length,"scope","Fresh isolated native CPU/OpenCL GPU run.140 actual compared model addresses and three actual calculator inputs. No input PDF records read. No stress test or user session touched.")),new UTF8Encoding(false));File.WriteAllText(Path.Combine(output,"README.txt"),"SpinDoctor3.0.7 native figures example: three calculator input states (+Z,-Z,+X) and a separately prepared128x128,t=1,RY(0.3) model. The verifier actually compared140 addresses (128 requested plus12 unique deterministic boundaries), retaining32 real CPU/GPU rows. Predicted probabilities are not measurement observations; no shots were drawn. Source figures are labelled separately. The PDF uses the immutable native archives created by this small isolated test.\r\n",new UTF8Encoding(false));Console.WriteLine("PASS native archive-resolved PDF example; GPU "+initial["gpuDevice"]+";140 real compared addresses;32 preview rows; actual calculator +Z/-Z/+X; no measurement shots. "+output);
  }
 }
}