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
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;
public static class NativeSpinPreviewRegressions
{
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
 static int assertions;
 static Dictionary<string,object> D(params object[] values){return SpinDoctorCalculators.D(values);}
 static Dictionary<string,object> M(object value){return (Dictionary<string,object>)value;}
 static void Check(bool value,string label){assertions++;if(!value)throw new Exception("FAIL "+label);}
 static double N(object value){return Convert.ToDouble(value,CultureInfo.InvariantCulture);}
 static string S(object value){return Convert.ToString(value,CultureInfo.InvariantCulture);}
 static void Near(double actual,double expected,string label){Check(Math.Abs(actual-expected)<2e-12,label+": "+actual+" expected "+expected);}
 static Dictionary<string,object> Execute(TerminalSession session,string command){Dictionary<string,object> response=M(session.Execute(command));Check((bool)response["ok"],command+" native return: "+Json.Serialize(response));return response;}
 static Dictionary<string,object> Result(TerminalSession session){return M(M(M(session.GetStatus())["circuit"])["result"]);}
 static Dictionary<string,object> Wait(TerminalSession session,string id){DateTime until=DateTime.UtcNow.AddSeconds(20);while(DateTime.UtcNow<until){Dictionary<string,object> result=Result(session);Check(S(result["id"])==id,"exact job identity");if(!(bool)result["active"]){Check(S(result["phase"])=="completed","completed native circuit");return result;}Thread.Sleep(10);}throw new Exception("Circuit timed out");}
 static Dictionary<string,object> Ref(Dictionary<string,object> response){return D("nativeArchive",D("type","command","id",response["reportId"],"stage","accepted"));}
 static void State(Dictionary<string,object> value,double[] expected,bool mixed,string label)
 {
  IList spin=(IList)value["bloch"];Check(spin.Count==3,label+" vector3");for(int k=0;k<3;k++)Near(N(spin[k]),expected[k],label+" Pauli"+k);
  double length=expected[0]*expected[0]+expected[1]*expected[1]+expected[2]*expected[2];Near(N(value["blochLengthSquared"]),length,label+" length");Near(N(value["purity"]),(1+length)/2,label+" purity");Check((bool)value["mixed"]==mixed&&(bool)value["pure"]!=mixed,label+" pure/mixed");Check((bool)value["directionResolvedAtTolerance"]==(length>0),label+" resolved direction");bool exact=N(spin[0])==0&&N(spin[1])==0&&N(spin[2])==0;Check((bool)value["exactlyZeroBloch"]==exact&&(bool)value["directionDefined"]!=exact,label+" exact versus nonzero components");Check((bool)value["premeasurement"],label+" premeasurement source");Check(S(value["stateSource"]).Contains("coherent circuit")&&S(value["spinScope"]).Contains("not the separately saved procedural model"),label+" separate state source");
 }
 static string Hash(byte[] data){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
 static void CopySource(string source,string workspace){Directory.CreateDirectory(Path.Combine(workspace,"quantum"));foreach(string path in Directory.GetFiles(Path.Combine(source,"quantum"),"*.cs"))File.Copy(path,Path.Combine(workspace,"quantum",Path.GetFileName(path)));foreach(string name in new string[]{"SPINDOCTOR_PRESETS.json","EquationCatalogue.json","QUANTUM_TERMINAL.html"})File.Copy(Path.Combine(source,name),Path.Combine(workspace,name));}
 static void RestoreCase(string workspace,string output,string label,string checkpoint,Action<Dictionary<string,object>> mutate,bool rejected)
 {
  string folder=Path.Combine(output,"restore-"+label);Directory.CreateDirectory(folder);File.Copy(Path.Combine(workspace,"quantum-session-state.json"),Path.Combine(folder,"quantum-session-state.json"));Dictionary<string,object> data=Json.Deserialize<Dictionary<string,object>>(checkpoint);mutate(data);File.WriteAllText(Path.Combine(folder,"spindoctor-circuits.json"),Json.Serialize(data),new UTF8Encoding(false));bool failed=false;try{using(TerminalSession restored=new TerminalSession(folder,false)){Check(!(bool)M(M(restored.GetStatus())["circuit"])["coherentOutputAvailable"],label+" restored preview does not materialize state");Check(!(bool)M(restored.Execute("q.query reduced-density qubits=[0]"))["ok"],label+" restored query rejects absent retained state");}}catch(InvalidDataException){failed=true;}Check(failed==rejected,label+" restore validation");
 }
 public static void Run(string source,string output)
 {
  if(Directory.Exists(output))throw new Exception("Use a fresh native spin validation directory.");Directory.CreateDirectory(output);string workspace=Path.Combine(output,"isolated-native-workspace");CopySource(source,workspace);List<object> reportRefs=new List<object>(),responses=new List<object>();string checkpoint;Dictionary<string,object> initial,final;ulong actualCompared;
  foreach(double tiny in new double[]{1e-9,1e-300}){Dictionary<string,object> near=SpinDoctorCalculators.BlochStateMetadata(new double[]{tiny,0,0},0);Check(S(near["stateCharacter"])=="near-maximally-mixed-single-qubit","nonzero small vector is not declared exactly maximally mixed");Check(!(bool)near["exactlyZeroBloch"]&&(bool)near["directionDefined"]&&!(bool)near["directionResolvedAtTolerance"],"small finite nonzero vector preserves mathematical direction versus tolerance");Check(N(((IList)near["bloch"])[0])==tiny,"tiny actual component unchanged");Near(N(near["directionTolerance"]),2e-9,"explicit native direction tolerance");}
  Dictionary<string,object> zero=SpinDoctorCalculators.BlochStateMetadata(new double[]{0,0,0},0);Check((bool)zero["exactlyZeroBloch"]&&!(bool)zero["directionDefined"]&&S(zero["stateCharacter"])=="maximally-mixed-single-qubit","exact zero distinctly labelled");
  using(TerminalSession session=new TerminalSession(workspace,true))
  {
   initial=M(session.GetStatus());Check((bool)initial["gpuAvailable"],"real native GPU available");Execute(session,"q.prepare grid 16 16");Execute(session,"q.migrate t 1");Execute(session,"q.gate ry 0.3 all");Dictionary<string,object> verify=Execute(session,"q.verify samples 32 errors-only"),verified=M(verify["result"]);Check((bool)verified["passed"],"separate saved-model actual CPU/GPU comparison passed");actualCompared=UInt64.Parse(S(verified["verifiedCount"]),CultureInfo.InvariantCulture);Check(actualCompared>0&&actualCompared<=44,"small bounded native GPU address count");Check(((IList)verified["samplePreview"]).Count==32,"32 actual saved-model CPU/GPU compared rows");responses.Add(verify);reportRefs.Add(Ref(verify));string proceduralDescriptor=S(initial["descriptorHash"]),afterModel=S(M(session.GetStatus())["descriptorHash"]);
   string[] names={"north","south","plusx","plusy","bell"};double[][] vectors={new double[]{0,0,1},new double[]{0,0,-1},new double[]{1,0,0},new double[]{0,1,0},new double[]{0,0,0}};long draws=0;
   for(int i=0;i<names.Length;i++)
   {
    int qubits=i==4?2:1;Execute(session,"q.circuit new "+names[i]+" "+qubits);if(i==1)Execute(session,"q.circuit input 1");if(i>=2)Execute(session,"q.circuit add h 0");if(i==3)Execute(session,"q.circuit add s 0");if(i==4)Execute(session,"q.circuit add cx 0 1");
    Dictionary<string,object> queued=M(Execute(session,"q.circuit run shots 4 basis z")["result"]),result=Wait(session,S(M(queued["job"])["id"]));Check(S(result["completedShots"])=="4"&&S(result["measuredQubits"])==(4*qubits).ToString(CultureInfo.InvariantCulture),names[i]+" independent completed measurements");Dictionary<string,object> counts=M(result["counts"]);Check(S(counts["uniformDraws"])==(4*qubits).ToString(CultureInfo.InvariantCulture),names[i]+" preview adds no random draws");Check(S(counts["queryCount"])==(5*qubits).ToString(CultureInfo.InvariantCulture),names[i]+" actual preview queries accounted");draws+=4*qubits;
    Dictionary<string,object> preview=M(result["coherentSpinPreview"]);Check(S(preview["programHash"])==S(result["programHash"])&&(bool)preview["premeasurement"],names[i]+" snapshot descriptor source");Check(!(bool)preview["independentVerification"]&&S(preview["verificationScope"]).Contains("does not independently verify"),names[i]+" no false q.verify circuit claim");Check(Convert.ToInt32(preview["qubitLimit"])==4&&Convert.ToInt32(preview["maximumFactorEntries"])==65536,names[i]+" bounded traversal policy");Check(Convert.ToInt32(preview["returnedQubits"])==qubits&&Convert.ToInt32(preview["omittedQubits"])==0&&((IList)preview["omissions"]).Count==0,names[i]+" explicit coverage");IList rows=(IList)preview["rows"];for(int q=0;q<qubits;q++){Dictionary<string,object> row=M(rows[q]);Check(Convert.ToInt32(row["qubit"])==q,names[i]+" row qubit");State(row,vectors[i],i==4,names[i]+" preview"+q);}
    Dictionary<string,object> receipt=Execute(session,"q.circuit result");responses.Add(receipt);reportRefs.Add(Ref(receipt));
    for(int q=0;q<qubits;q++){Dictionary<string,object> query=Execute(session,"q.query reduced-density qubits=["+q+"]"),qr=M(query["result"]);State(qr,vectors[i],i==4,names[i]+" query"+q);Check(S(qr["actualStateQueries"])=="1",names[i]+" enriched query still performs one actual state query");Check(S(qr["descriptorHash"])==S(result["programHash"]),names[i]+" query descriptor identity");responses.Add(query);reportRefs.Add(Ref(query));}
    Check(S(M(session.GetStatus())["descriptorHash"])==afterModel,names[i]+" circuit does not change separate saved model");
   }
   Check(draws==24,"only24 measurement random draws across five tiny circuits");checkpoint=File.ReadAllText(Path.Combine(workspace,"spindoctor-circuits.json"));Check((bool)M(M(session.GetStatus())["circuit"])["coherentOutputAvailable"],"Bell coherent output retained after independent shot collapse");
   for(int i=0;i<3;i++){Dictionary<string,object> math=Execute(session,new string[]{"q.math bloch amplitudes=[1,0]","q.math bloch amplitudes=[0,1]","q.math bloch amplitudes=[1/sqrt(2),1/sqrt(2)]"}[i]);responses.Add(math);reportRefs.Add(Ref(math));}
   Check(!(bool)M(session.Execute("q.circuit run shots 100001 basis z"))["ok"],"measurement maximum100000 unchanged;100001 rejected before work");Check(!(bool)M(session.Execute("q.circuit run shots 500000 basis z"))["ok"],"500000 refusal preserved with no inferred measurement preview");Check(S(Result(session)["completedShots"])=="4", "rejected runs preserve actual previous completed count");
   Execute(session,"q.circuit add z 0");Check(Result(session)["coherentSpinPreview"]==null,"circuit mutation invalidates mutable saved preview");Check(!(bool)M(M(session.GetStatus())["circuit"])["coherentOutputAvailable"],"circuit mutation drops retained current state");Check(!(bool)M(session.Execute("q.query reduced-density qubits=[0]"))["ok"],"query cannot reuse stale old state after mutation");final=M(session.GetStatus());Check(S(M(M(final["circuit"])["lifetime"])["uniformDraws"])=="24","preview/query/mutation/export adds zero measurement draws");Check(S(final["verifiedSamples"])==actualCompared.ToString(CultureInfo.InvariantCulture),"circuit spin never credits procedural verification");
  }
  RestoreCase(workspace,output,"valid",checkpoint,delegate(Dictionary<string,object> d){},false);
  RestoreCase(workspace,output,"old-compatible",checkpoint,delegate(Dictionary<string,object> d){M(d["job"]).Remove("coherentSpinPreview");},false);
  string[] changes={"wrong-program","out-of-ball","false-pure","false-independent-verification","duplicate-qubit","wrong-query-count","wrong-source","wrong-timestamp","wrong-coverage"};
  foreach(string change in changes)RestoreCase(workspace,output,change,checkpoint,delegate(Dictionary<string,object> data){Dictionary<string,object> j=M(data["job"]),p=M(j["coherentSpinPreview"]),r=M(((IList)p["rows"])[0]);if(change=="wrong-program")p["programHash"]=new string('0',64);else if(change=="out-of-ball")r["bloch"]=new double[]{2,0,0};else if(change=="false-pure")r["pure"]=true;else if(change=="false-independent-verification")p["independentVerification"]=true;else if(change=="duplicate-qubit")M(((IList)p["rows"])[1])["qubit"]=0;else if(change=="wrong-query-count")M(j["counts"])["queryCount"]="0";else if(change=="wrong-source")r["stateSource"]="Saved procedural model";else if(change=="wrong-timestamp")p["capturedUtc"]="yesterday";else p["omittedQubits"]=1;},true);
  using(TrancheRegister register=new TrancheRegister(1,new double[][]{new double[]{1/Math.Sqrt(2),0,1/Math.Sqrt(2),0}}))
  {
   MethodInfo bounded=typeof(TrancheRegister).GetMethod("BoundedSpinSnapshot",BindingFlags.Instance|BindingFlags.NonPublic);Dictionary<string,object> skipped=M(bounded.Invoke(register,new object[]{0,1,null}));Check(!(bool)skipped["captured"]&&!skipped.ContainsKey("spin"),"over-bound factor is omitted, never replaced with zero");Check(S(register.Status()["queryCount"])=="0","omitted factor performs no spin query");Dictionary<string,object> accepted=M(bounded.Invoke(register,new object[]{0,2,null}));Check((bool)accepted["captured"]&&S(register.Status()["queryCount"])=="1","bounded actual factor read performs one query");
  }
  Dictionary<string,object> hashes=D();foreach(string path in Directory.GetFiles(Path.Combine(source,"quantum"),"*.cs"))hashes[Path.GetFileName(path)]=Hash(File.ReadAllBytes(path));File.WriteAllText(Path.Combine(output,"native-spin-fixture.json"),Json.Serialize(D("records",reportRefs.ToArray(),"responses",responses.ToArray(),"scope","Native immutable receipts from five small 1/2-qubit coherent runs plus separate actual saved-model CPU/GPU verification.20 completed circuit shots,24 measured qubits and24 per-qubit draws. No stress test or user checkpoint mutation.")),new UTF8Encoding(false));File.WriteAllText(Path.Combine(output,"native-spin-proof.json"),Json.Serialize(D("passed",true,"assertions",assertions,"version",TerminalSession.ProductVersion,"sourceHashes",hashes,"gpuDevice",initial["gpuDevice"],"actualComparedModelAddresses",actualCompared.ToString(CultureInfo.InvariantCulture),"actualCircuitShots",20,"actualCircuitMeasuredQubits",24,"actualCircuitRandomDraws",24,"modelVerificationCreditsFromCircuit",0,"restoredPreviewNeverMaterializesState",true,"oldJobCompatibility",true,"finalNativeStatus",final)),new UTF8Encoding(false));Console.WriteLine("PASS "+assertions+" native spin-preview assertions; five 1/2-qubit runs,20 actual shots,24 draws; "+actualCompared+" separate actual GPU/model comparisons. "+output);
 }
 public static void ReplayPdf(string source,string output)
 {ReplayPdf(source,output,output);}
 public static void ReplayPdf(string source,string output,string pdfOutput)
 {
  Directory.CreateDirectory(pdfOutput);
  Dictionary<string,object> fixture=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(output,"native-spin-fixture.json"))),capture=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(output,"native-spin-proof.json"))),hashes=D();foreach(string path in Directory.GetFiles(Path.Combine(source,"quantum"),"*.cs"))hashes[Path.GetFileName(path)]=Hash(File.ReadAllBytes(path));foreach(string name in new string[]{"QuantumTerminal.cs","SpinDoctorCircuits.cs","SpinDoctorTranches.cs","SpinDoctorCalculators.cs"})Check(S(hashes[name])==S(M(capture["sourceHashes"])[name]),"replay uses frozen native numerical source "+name);
  IList refs=(IList)fixture["records"],responses=(IList)fixture["responses"];List<object> selected=new List<object>();object bell=null,model=null;
  for(int i=0;i<responses.Count;i++){Dictionary<string,object> response=M(responses[i]);string command=S(response["command"]);if(command.StartsWith("q.math bloch ",StringComparison.Ordinal))selected.Add(refs[i]);else if(command=="q.circuit result"&&S(M(response["result"])["programName"])=="bell")bell=refs[i];else if(command.StartsWith("q.verify samples ",StringComparison.Ordinal))model=refs[i];}
  Check(selected.Count==3&&bell!=null&&model!=null,"five actual matched example records");selected.Add(bell);selected.Add(model);Dictionary<string,object> request=D("kind","session","view","summary","records",selected.ToArray(),"status",D());File.WriteAllText(Path.Combine(pdfOutput,"native-spin-example-request.json"),Json.Serialize(request),new UTF8Encoding(false));
  using(TerminalSession session=new TerminalSession(Path.Combine(output,"isolated-native-workspace"),false)){byte[] pdf=session.ReportPdf(request);string path=Path.Combine(pdfOutput,"SpinDoctor-3.0.9-Actual-Circuit-and-Model-Spin-Examples.pdf");File.WriteAllBytes(path,pdf);File.WriteAllText(Path.Combine(pdfOutput,"native-spin-pdf-proof.json"),Json.Serialize(D("passed",true,"sourceHashes",hashes,"captureProofPath",Path.Combine(output,"native-spin-proof.json"),"captureProofSha256",Hash(File.ReadAllBytes(Path.Combine(output,"native-spin-proof.json"))),"fixturePath",Path.Combine(output,"native-spin-fixture.json"),"fixtureSha256",Hash(File.ReadAllBytes(Path.Combine(output,"native-spin-fixture.json"))),"pdfPath",path,"pdfSha256",Hash(pdf),"pdfBytes",pdf.Length,"scope","Five matched native records: calculator +Z/-Z/+X, completed Bell circuit with actual premeasurement marginals, separate saved-model CPU/GPU verification. Immutable archive replay only;0 new model comparisons,0 circuit shots,0 random draws.")),new UTF8Encoding(false));Console.WriteLine("PASS immutable native archive replay PDF, no new GPU calculations/shots; SHA256 "+Hash(pdf)+" "+path);}
 }
}
