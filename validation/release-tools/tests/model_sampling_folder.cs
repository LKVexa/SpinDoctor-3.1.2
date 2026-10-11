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
public static class ModelSamplingFolder
{
 static int checks;
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
 static Dictionary<string,object> D(params object[] values){return SpinDoctorCalculators.D(values);}
 static Dictionary<string,object> M(object value){return (Dictionary<string,object>)value;}
 static List<Dictionary<string,object>> Rows(object value){List<Dictionary<string,object>> result=new List<Dictionary<string,object>>();foreach(object row in (IEnumerable)value)result.Add(M(row));return result;}
 static void Check(bool condition,string message){checks++;if(!condition)throw new Exception("FAIL "+message);}
 static string S(object value){return Convert.ToString(value,CultureInfo.InvariantCulture);}
 static void Near(object value,double expected,string message){double actual=Convert.ToDouble(value,CultureInfo.InvariantCulture);Check(!Double.IsNaN(actual)&&Math.Abs(actual-expected)<1e-12,message+": "+actual.ToString("R",CultureInfo.InvariantCulture)+" expected "+expected.ToString("R",CultureInfo.InvariantCulture));}
 static DateTime Time(object value){return DateTime.Parse(S(value),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);}
 static Dictionary<string,object> Job(TerminalSession session){return M(M(session.GetStatus())["sequence"])["job"] as Dictionary<string,object>;}
 static Dictionary<string,object> Step(TerminalSession session,string child,int index){return M(Json.DeserializeObject(session.SequenceJson(child,index,"latest")));}
 static Dictionary<string,object> Returned(Dictionary<string,object> step){return M(M(step.ContainsKey("finalResponse")?step["finalResponse"]:step["response"])["result"]);}
 static ulong ReplayOne(string seed,ulong start,ulong shots,double p)
 {
  ulong one=0;using(SHA256 hash=SHA256.Create())for(ulong counter=start;counter<start+shots;counter++){byte[] bytes=hash.ComputeHash(Encoding.UTF8.GetBytes(seed+"|measurement|"+counter.ToString(CultureInfo.InvariantCulture)));ulong bits=0;for(int i=0;i<8;i++)bits=(bits<<8)|bytes[i];double uniform=(bits>>11)*(1.0/9007199254740992.0);if(uniform<p)one++;}return one;
 }
 public static void Run(string source,string inputRoot,string scratch)
 {
  checks=0;Directory.CreateDirectory(scratch);string workspace=Path.Combine(scratch,"fresh-native-workspace");Directory.CreateDirectory(Path.Combine(workspace,"quantum"));
  foreach(string path in Directory.GetFiles(Path.Combine(source,"quantum"),"*.cs"))File.Copy(path,Path.Combine(workspace,"quantum",Path.GetFileName(path)));
  foreach(string name in new string[]{"SPINDOCTOR_PRESETS.json","EquationCatalogue.json","QUANTUM_TERMINAL.html"})File.Copy(Path.Combine(source,name),Path.Combine(workspace,name));
  string[] names=Directory.GetFiles(inputRoot,"*.txt");Array.Sort(names,StringComparer.OrdinalIgnoreCase);Check(names.Length==3,"exactly three TXT input files");foreach(string path in names)Check(File.ReadAllText(path).IndexOf("q.verify",StringComparison.OrdinalIgnoreCase)<0,"no explicit verifier in input "+path);
  Dictionary<string,object> finalJob,finalStatus,state,measurement,descriptor,reportProof=null;List<object> verificationSummaries=new List<object>();string folderId;
  using(TerminalSession session=new TerminalSession(workspace,true))
  {
   Dictionary<string,object> initial=M(session.GetStatus());Check((bool)initial["gpuAvailable"],"actual FP64 OpenCL GPU is mandatory: "+S(initial["gpuError"]));Check(S(initial["actualCpuStates"])=="0"&&S(initial["actualGpuStates"])=="0","fresh native workspace starts without completed state work");
   Dictionary<string,object> accepted=M(session.Execute("q.folder run path=\""+Path.GetFullPath(inputRoot).Replace('\\','/')+"\" downloads=false purge=true"));Check((bool)accepted["ok"],"folder native acceptance");Dictionary<string,object> acceptedJob=M(M(accepted["result"])["job"]);folderId=S(acceptedJob["id"]);Check((bool)acceptedJob["verificationRequired"]&&S(acceptedJob["verificationSamples"])=="10240","default procedural verification requested without q.verify inputs");
   DateTime deadline=DateTime.UtcNow.AddSeconds(120);while((finalJob=Job(session))["active"] is bool&&(bool)finalJob["active"]){if(DateTime.UtcNow>deadline)throw new Exception("FAIL model folder verification timeout");Thread.Sleep(15);}finalStatus=M(session.GetStatus());
   Check(S(finalJob["phase"])=="completed","folder completed: "+S(finalJob["error"]));Check((bool)finalJob["receiptReady"],"parent receipt ready after all required verification and archives");Check(S(finalJob["verificationPhase"])=="passed","parent procedural verification phase passed");Check(S(finalJob["completedFiles"])=="3"&&S(finalJob["totalFiles"])=="3","all three model files completed");Check(S(finalJob["completedCommands"])=="6"&&S(finalJob["totalCommands"])=="6","automatic verifier work excluded from six input commands");Check(Time(finalJob["receivedUtc"])<Time(finalJob["completedUtc"]),"parent start precedes completion stamp");
   List<Dictionary<string,object>> files=Rows(finalJob["files"]);Check(files.Count==3,"three final file records");string previousPurge=null;
   for(int i=0;i<files.Count;i++)
   {
    Dictionary<string,object> file=files[i];Check(S(file["executionPhase"])=="completed"&&S(file["verificationPhase"])=="passed"&&S(file["reportPhase"])=="archived"&&S(file["purgePhase"])=="completed","native execution/verification/archive/purge completed for file "+i);Check((bool)file["receiptReady"],"verified receipt ready for file "+i);
    string child=S(file["childJobId"]);Dictionary<string,object> saved=M(Json.DeserializeObject(session.SequenceJson(child,null,"latest"))),childJob=M(saved["job"]),evidence=M(childJob["verification"]),verify=M(M(evidence["response"])["result"]);
    Check((bool)verify["passed"]&&S(verify["phase"])=="completed","actual native verifier passed "+i);foreach(string key in new string[]{"plannedSamples","completedSamples","verifiedCount","cpuStates","gpuStates"})Check(S(verify[key])=="4096","exhaustive finite64x64 actual "+key+" file "+i);Check((bool)verify["exhaustive"],"finite model fully checked "+i);Check((bool)verify["hasPrepared"]&&S(verify["migrationT"])=="1","sampled current model prepared and migrated "+i);Check(Rows(verify["samplePreview"]).Count==32,"32 actual compared sample previews "+i);Check(Time(childJob["completedUtc"])<=Time(evidence["receivedUtc"]),"input execution precedes automatic verification "+i);Check(Time(evidence["receivedUtc"])<=Time(evidence["completedUtc"]),"verification completion stamp follows start "+i);Check(Time(evidence["completedUtc"])<=Time(file["reportArchivedUtc"]),"verification finishes before receipt archive "+i);Check(Time(file["reportArchivedUtc"])<=Time(file["purgeCompletedUtc"]),"receipt archive precedes purge "+i);if(previousPurge!=null)Check(Time(previousPurge)<=Time(childJob["receivedUtc"]),"next file waits for preceding verification/archive/purge "+i);previousPurge=S(file["purgeCompletedUtc"]);
    string report=session.SequencePrintout(child,null,"latest");Check(report.Contains("NATIVE PROCEDURAL CPU/GPU SAMPLE VERIFICATION PASSED"),"native scoped verification stamp in file receipt "+i);Check(report.Contains("not independent certification of q.math"),"receipt verification scope limitation "+i);Check(report.Contains(S(evidence["completedUtc"])),"verification UTC printed "+i);
    for(int command=0;command<2;command++){Dictionary<string,object> step=Step(session,child,command);Check(Time(step["receivedUtc"])<=Time(step["completedUtc"]),"command completion stamp "+i+":"+command);Check((bool)M(step["response"])["ok"],"native input command successful "+i+":"+command);}
    verificationSummaries.Add(D("filename",file["filename"],"childId",child,"verification",verify,"executionCompletedUtc",childJob["completedUtc"],"verificationReceivedUtc",evidence["receivedUtc"],"verificationCompletedUtc",evidence["completedUtc"],"reportArchivedUtc",file["reportArchivedUtc"],"purgeCompletedUtc",file["purgeCompletedUtc"]));
   }
   state=Returned(Step(session,S(files[1]["childJobId"]),1));measurement=Returned(Step(session,S(files[2]["childJobId"]),0));descriptor=Returned(Step(session,S(files[2]["childJobId"]),1));
   double b0=Math.Sqrt(2.5/64),a0=Math.Sqrt(1-2.5/64),angle=.15,alpha=Math.Cos(angle)*a0-Math.Sin(angle)*b0,beta=Math.Sin(angle)*a0+Math.Cos(angle)*b0,p1=beta*beta;
   Dictionary<string,object> amplitudes=M(state["amplitudes"]),spin=M(state["spin"]);IList a=(IList)amplitudes["alpha"],b=(IList)amplitudes["beta"];Near(a[0],alpha,"native RY alpha");Near(a[1],0,"native RY alpha imaginary");Near(b[0],beta,"native RY beta");Near(b[1],0,"native RY beta imaginary");Near(state["p1"],p1,"native predicted P1");Near(spin["x"],2*alpha*beta,"native predicted spinX");Near(spin["y"],0,"native predicted spinY");Near(spin["z"],alpha*alpha-beta*beta,"native predicted spinZ");Near(state["norm"],1,"native normalized state");Near(measurement["probabilityOne"],p1,"measurement expected Born probability");
   Check(S(measurement["shots"])=="128"&&S(measurement["counterStart"])=="0"&&S(measurement["counterEnd"])=="128","actual seeded measurement work and draw counter");ulong expectedOne=ReplayOne(S(measurement["seed"]),0,128,p1);Check(S(measurement["one"])==expectedOne.ToString(CultureInfo.InvariantCulture)&&S(measurement["zero"])==(128-expectedOne).ToString(CultureInfo.InvariantCulture),"actual histogram matches independent SHA256 seed replay");
   Check(S(descriptor["stateCount"])=="4096"&&S(descriptor["migrationT"])=="1","export returns prepared finite model");Check(S(finalStatus["verifiedSamples"])=="12288"&&S(finalStatus["actualGpuStates"])=="12288"&&S(finalStatus["actualCpuStates"])=="12290","truthful lifetime accounting: 3x4096 verified CPU/GPU states plus q.state and q.measure CPU queries");Check(S(finalStatus["measurementShots"])=="128"&&S(finalStatus["measurementCounter"])=="128","verification does not fabricate or consume measurement shots");Check(S(finalStatus["representedPreparations"])=="4096"&&S(finalStatus["representedGateApplications"])=="4096","represented preparations and sharedgate credit distinct from actual sample work");Check(S(finalStatus["quantumHardwareGates"])=="0","no physical quantum gates claimed");Check(S(finalStatus["errorCount"])=="0","actual native model checker reports no errors");
   using(FileStream receipt=session.OpenSequencePrintout(folderId,"latest")){Check(receipt.Length>0,"final streamed folder receipt available");}
   reportProof=D("passed",true,"checks",checks,"sourceRoot",source,"inputRoot",inputRoot,"workspace",workspace,"gpuDevice",initial["gpuDevice"],"version",finalStatus["productVersion"],"folderId",folderId,"inputCommands",6,"verificationRuns",3,"verifiedSamples",finalStatus["verifiedSamples"],"actualCpuStates",finalStatus["actualCpuStates"],"actualGpuStates",finalStatus["actualGpuStates"],"measurementShots",finalStatus["measurementShots"],"state",state,"measurement",measurement,"descriptor",descriptor,"fileVerification",verificationSummaries.ToArray(),"scope","Actual native C# CPU/OpenCL GPU folder run in a fresh isolated workspace. No user session or input overwritten. Verification checks current-model predictions and agreement, not physical measurements or unrelated arithmetic.");
  }
  using(TerminalSession reopened=new TerminalSession(workspace,true)){Dictionary<string,object> status=M(reopened.GetStatus());Check(S(status["verifiedSamples"])=="12288"&&S(status["actualCpuStates"])=="12290","actual sample work persists through restart");Check(S(Job(reopened)["phase"])=="completed"&&(bool)Job(reopened)["receiptReady"],"verified completed parent receipt persists through restart");}
  reportProof["checks"]=checks;File.WriteAllText(Path.Combine(scratch,"model-sampling-folder.json"),Json.Serialize(reportProof),new UTF8Encoding(false));
  Console.WriteLine("PASS real GPU model-sampling folder: "+checks+" assertions; three files, six input commands, three automatic exhaustive4096-point verifications; CPU12290/GPU12288/verified12288, shots128. JSON: "+Path.Combine(scratch,"model-sampling-folder.json"));
 }
}
