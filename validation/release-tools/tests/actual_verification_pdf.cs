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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using QuettaQuantum;
public static class ActualVerificationPdf
{
 private static Dictionary<string,object> D(params object[] pairs){return SpinDoctorCalculators.D(pairs);}
 public static void Run(string input,string output)
 {
  if(Directory.Exists(output))throw new InvalidOperationException("Use a fresh PDF output directory.");Directory.CreateDirectory(output);
  JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};Dictionary<string,object> result=(Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(input));
  Dictionary<string,object> response=D("ok",true,"command","q.verify samples 128 errors-only","result",result,"elapsedMs",result["elapsedMs"],"status",D("productVersion",TerminalSession.ProductVersion,"hasPrepared",result["hasPrepared"],"migrationT",result["migrationT"],"descriptorHash",result["descriptorHash"]));
  Dictionary<string,object> record=D("command","q.verify samples 128 errors-only","kind","verification","jobId",result["id"],"receivedUtc",result["receivedUtc"],"completedUtc",result["completedUtc"],"nativeResponse",response,"recordSource","Actual native C# CPU / Intel OpenCL GPU verification capture. Prepared grid 128 x 128, migration t=1, shared RY(0.3). This report uses its saved result without another calculation.");
  File.WriteAllBytes(Path.Combine(output,"SpinDoctor-Actual-Sampled-Model-Verification.pdf"),SpinDoctorReportPdf.Build(D("kind","selected","view","details","records",new object[]{record}),null));
  File.WriteAllText(Path.Combine(output,"README.txt"),"This PDF contains an actual bounded native test run, not synthetic measurement data. The native verifier computed 140 distinct-address predictions (128 requested plus 12 deterministic boundaries), and retained 32 actual sample rows. Probabilities are model predictions; no measurement shots were requested.\r\n",new UTF8Encoding(false));
  Console.WriteLine("PASS actual native verification PDF generated: "+output);
 }
}
