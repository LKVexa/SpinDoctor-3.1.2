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

public static class PresetShotRegressions
{
    static int checks, normalizedCards, actualShotCards, ordinaryShotCards, legacyCommands, installedExtensionCommands;
    static JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
    static Dictionary<string,object> D(params object[] values){Dictionary<string,object> result=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)result.Add((string)values[i],values[i+1]);return result;}
    static Dictionary<string,object> M(object value){return (Dictionary<string,object>)value;}
    static string S(object value){return Convert.ToString(value,CultureInfo.InvariantCulture);}
    static string[] Words(string value){return value.Split(new char[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);}
    static List<Dictionary<string,object>> Maps(object values){List<Dictionary<string,object>> result=new List<Dictionary<string,object>>();foreach(object value in (IEnumerable)values)result.Add(M(value));return result;}
    static string[] Strings(object values){List<string> result=new List<string>();foreach(object value in (IEnumerable)values)result.Add((string)value);return result.ToArray();}
    static void Check(bool value,string why){checks++;if(!value)throw new Exception("FAIL: "+why);}
    static object Private(string name,params object[] values){try{return typeof(SequenceTerminal).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,values);}catch(TargetInvocationException error){throw error.InnerException;}}
    static void Reject(Action action,string why){try{action();}catch(ArgumentException){Check(true,why);return;}throw new Exception("FAIL: did not reject "+why);}
    static string Hash(string path){using(SHA256 sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    static int ShotAt(string[] words){if(words[0]=="q.measure")return 4;if(words.Length>1 &&words[0]=="q.tranche"&&words[1]=="measure")return 5;if(words.Length>1&&words[0]=="q.circuit"&&words[1]=="run")return 2;if(words.Length>2&&words[0]=="q.circuit"&&words[1]=="preset"&&CircuitTerminal.ParsePresetOptions(words).Run){for(int i=3;i<words.Length-1;i++)if(words[i]=="shots")return i;}return -1;}
    static Dictionary<string,object> Status(){return D("gridM","20000000","gridL","25000000","stateCount","500000000000000","tranche",D("qubits",29),"circuit",D("programCapacity",1024,"programCount",1,"presetOwnedCount",0,"selected","fixture","program",D("qubits",29),"programNames",new string[]{"fixture"},"programSummaries",new object[]{D("name","fixture","qubits",29)},"coherentOutputAvailable",true));}
    static string[] Compile(string[] commands,Dictionary<string,object> parameters){return (string[])Private("Normalize",commands,parameters,Status(),false);}
    static object Capability(string command)
    {
        string[] words=Words(command);bool installed=words.Length>1 &&(SpinDoctorCalculators.Supports(words[0],words[1])||SpinDoctorAdvanced.Supports(words[0],words[1]));
        if(installed){try{SpinDoctorSyntax.ValidateSyntax(words);return D("available",true,"reason","");}catch(ArgumentException error){return D("available",false,"reason",error.Message);}}
        return D("available",false,"reason","No installed extension; design record retained.");
    }
    static Dictionary<string,object> Finish(SequenceTerminal terminal){DateTime deadline=DateTime.UtcNow.AddSeconds(30);for(;;){Dictionary<string,object> job=M(terminal.GetStatus()["job"]);if(!(bool)job["active"])return job;if(DateTime.UtcNow>deadline)throw new Exception("Mock dispatch timeout.");Thread.Sleep(5);}}
    static void CheckPublicPreflight(SequenceTerminal terminal,List<string> dispatched,string id,object shots,bool shouldPass)
    {
        int before=dispatched.Count;Dictionary<string,object> submitted=M(terminal.Run(null,id,shots==null ?null:D("shots",shots)));
        if(!shouldPass){Check(!(bool)submitted["queued"],id+" must reject invalid shots before dispatch.");Check(dispatched.Count==before,id+" rejection must not execute any command.");return;}
        Check((bool)submitted["queued"],id+" mock dispatch should queue.");Dictionary<string,object> completed=Finish(terminal);Check((string)completed["phase"]=="completed",id+" mock dispatch completion.");
        int expected=id=="qc-015"?1:shots==null?1024:Int32.Parse(S(shots),CultureInfo.InvariantCulture);int count=0;
        for(int i=before;i<dispatched.Count;i++){string[] words=Words(dispatched[i]);int at=ShotAt(words);if(at>=0){count++;Check(words[at+1]==S(expected),id+" dispatched actual native shot operand.");}}
        Check(count==1,id+" must dispatch its single measurement command.");
        Check(terminal.SequencePrintout((string)completed["id"],0).StartsWith("SPINDOCTOR 3.1.2",StringComparison.Ordinal),"Candidate report header.");
    }
    public static string Run(string root,string manifest,string proofPath)
    {
        Directory.CreateDirectory(root);Dictionary<string,object> data=M(json.DeserializeObject(File.ReadAllText(manifest,Encoding.UTF8)));List<Dictionary<string,object>> source=Maps(data["presets"]);Check(source.Count==241,"Preserved catalog count.");Check((string)data["release"]=="3.1.2","Catalog release label.");
        Dictionary<string,int> expectedCategories=new Dictionary<string,int>{{"Procedural grid",9},{"Tranche controls",12},{"Single-qubit gates",40},{"Two-qubit gates and entanglement",35},{"Multi-qubit circuits",20},{"Algorithm demonstrations",15},{"State and matrix mathematics",22},{"Joint-state analysis",14},{"Noise and mitigation",12},{"Error-correction demonstrations",10},{"Compilation and gate synthesis",10},{"Memory and resource estimates",12},{"Alternative computation models",8},{"Guided lessons and article corrections",8},{"Circuit presets and controls",10},{"Large structured-register demonstrations",3},{"Algorithm extensions",1}};
        Dictionary<string,int> categories=new Dictionary<string,int>();Dictionary<string,Dictionary<string,object>> byId=new Dictionary<string,Dictionary<string,object>>();Dictionary<string,object> schemasById=new Dictionary<string,object>();
        for(int i=0;i<source.Count;i++){Dictionary<string,object> preset=source[i];string id=(string)preset["id"],category=(string)preset["category"];Check(id=="qc-"+(i+1).ToString("000",CultureInfo.InvariantCulture),"Ordered preserved ID "+id);Check(!byId.ContainsKey(id),"Unique ID "+id);byId.Add(id,preset);if(!categories.ContainsKey(category))categories[category]=0;categories[category]++;}
        Check(categories.Count==17,"Preserved category count.");foreach(KeyValuePair<string,int> expected in expectedCategories)Check(categories.ContainsKey(expected.Key)&&categories[expected.Key]==expected.Value,"Category count "+expected.Key);
        List<string> dispatched=new List<string>();string workspace=Path.Combine(root,"metadata-and-mock-dispatch");Directory.CreateDirectory(workspace);File.Copy(manifest,Path.Combine(workspace,"SPINDOCTOR_PRESETS.json"));
        using(SequenceTerminal terminal=new SequenceTerminal(workspace,delegate(string command){dispatched.Add(command);return D("ok",true,"command",command,"result",D("scope","Mock dispatch only; no native measurements executed."),"status",Status());},delegate(){return Status();}))
        {
            terminal.SetCapabilityResolver(Capability);Dictionary<string,object> registry=M(terminal.Presets());List<Dictionary<string,object>> cards=Maps(registry["presets"]);Check(cards.Count==241,"Native registry retains every design record.");
            foreach(Dictionary<string,object> card in cards)
            {
                string id=(string)card["id"];string[] commands=Strings(card["commands"]);Check(commands.Length>=1&&commands.Length<=128,id+" bounded ordinary command count.");Dictionary<string,object> schema=M(card["parameterSchema"]);schemasById.Add(id,schema);int measurementCommands=0;bool collapse=false;
                foreach(string command in commands)
                {
                    Check(command.Length>0&&command.Length<=4096&&command.StartsWith("q.",StringComparison.Ordinal),id+" bounded native command text.");string[] words=Words(command);int at=ShotAt(words);
                    if(at>=0){measurementCommands++;collapse=words[words.Length-1]=="collapse";Check(words[at]=="shots",id+" native positional shot field.");Check(words[at+1]==(collapse?"1":"1024"),id+" semantic default shot count.");}
                    bool legacy=(bool)Private("IsLegacy",words[0]);if(legacy){Private("ValidateLegacy",(object)words);legacyCommands++;}else if(SpinDoctorCalculators.Supports(words[0],words[1])||SpinDoctorAdvanced.Supports(words[0],words[1])){SpinDoctorSyntax.ValidateSyntax(words);installedExtensionCommands++;}
                }
                Check(schema.ContainsKey("shots")== (measurementCommands>0),id+" no invented or phantom editable measurement-shot schema.");
                string[] normalized=Compile(commands,new Dictionary<string,object>());Check(normalized.Length==commands.Length,id+" compiler retains command count.");Private("ValidateContext",normalized,Status());normalizedCards++;
                if(measurementCommands>0)
                {
                    actualShotCards++;Dictionary<string,object> shots=M(schema["shots"]);Check((int)shots["minimum"]==1,id+" minimum remains1.");Check((int)shots["maximum"]==(collapse?1:100000),id+" maximum consistent with native semantics.");Check((int)shots["default"]==(collapse?1:1024),id+" native default metadata.");Check((bool)shots["singleCollapse"]==collapse,id+" collapse semantics metadata.");
                    foreach(int value in collapse?new int[]{1}:new int[]{1,1024,100000}){string[] compiled=Compile(commands,D("shots",value));foreach(string command in compiled){string[] words=Words(command);int at=ShotAt(words);if(at>=0)Check(words[at+1]==S(value),id+" exact compiled override "+value);}Private("ValidateContext",compiled,Status());}
                    Reject(delegate(){Compile(commands,D("shots",collapse?2:100001));},id+" upper boundary rejected.");if(!collapse)ordinaryShotCards++;
                }
            }
            Check(normalizedCards==241,"Every catalog command sequence was structurally compiled, without running it.");Check(ordinaryShotCards==132&&actualShotCards==133,"132 independent sampling defaults and one single-collapse preset.");Check(dispatched.Count==0,"Registry and all241 structural compilations execute no native commands.");
            Check(!M(cards[207]["parameterSchema"]).ContainsKey("shots")&&!M(cards[208]["parameterSchema"]).ContainsKey("shots"),"Hypothetical sampling estimate and shot-planning action expose no measurement-shot override.");
            string[] ordinary=Strings(byId["qc-022"]["commands"]);foreach(object invalid in new object[]{0,-1,100001,10.5,"1e4","1024.0","not-a-number"})Reject(delegate(){Compile(ordinary,D("shots",invalid));},"Malformed/out-of-range override "+S(invalid));
            Dictionary<string,object> mixed=M(Private("ParameterSchema",(object)new string[]{"q.measure 0 basis z shots 1024","q.tranche measure 0 basis z shots 1 collapse"}));Check((int)M(mixed["shots"])["maximum"]==1&&!M(mixed["shots"]).ContainsKey("default"),"Mixed shared override respects collapse and does not invent a common default.");Reject(delegate(){Compile(new string[]{"q.measure 0 basis z shots 1024","q.tranche measure 0 basis z shots 1 collapse"},D("shots",1024));},"Mixed shared shot override cannot break collapse semantics.");
            foreach(string id in new string[]{"qc-007","qc-014","qc-022","qc-015","qc-228","qc-229"})CheckPublicPreflight(terminal,dispatched,id,null,true);
            foreach(string id in new string[]{"qc-007","qc-014","qc-022"}){CheckPublicPreflight(terminal,dispatched,id,100000,true);CheckPublicPreflight(terminal,dispatched,id,100001,false);}
            CheckPublicPreflight(terminal,dispatched,"qc-015",1,true);CheckPublicPreflight(terminal,dispatched,"qc-015",1024,false);CheckPublicPreflight(terminal,dispatched,"qc-208",1024,false);CheckPublicPreflight(terminal,dispatched,"qc-209",1024,false);CheckPublicPreflight(terminal,dispatched,"qc-132",1024,false);
            int before=dispatched.Count;Dictionary<string,object> rejected=M(terminal.Run(new string[]{"q.preset run qc-022 shots=100001"},null,null));Check(!(bool)rejected["queued"]&&dispatched.Count==before,"Nested preset invalid shots fail before dispatch.");rejected=M(terminal.Run(new string[]{"q.preset run qc-022 shots=1024 shots=1"},null,null));Check(!(bool)rejected["queued"]&&dispatched.Count==before,"Duplicate nested override rejects before dispatch.");
        }
        Dictionary<string,object> estimate=M(new SpinDoctorCalculators().Execute(Words(Strings(byId["qc-208"]["commands"])[0])));Check((double)estimate["standardError"]==Math.Sqrt(.25/1024),"Updated hypothetical uncertainty example matches native arithmetic.");Check((double)estimate["shots"]==1024,"Resource result retains declared hypothetical count.");Check((string)estimate["resultKind"]=="resource-estimate"&&(int)estimate["physicalQuantumOperations"]==0,"Resource count is an arithmetic estimate with no quantum operation claim.");
        Dictionary<string,object> proof=D("passed",true,"assertions",checks,"catalogSha256",Hash(manifest),"sequenceSourceSha256",Hash(Path.Combine(Path.Combine(Path.GetDirectoryName(manifest),"quantum"),"SpinDoctorSequences.cs")),"catalogCount",241,"normalizedCatalogCards",normalizedCards,"ordinarySamplingPresetDefaults",ordinaryShotCards,"singleCollapsePresets",actualShotCards-ordinaryShotCards,"ordinaryDefaultShots",1024,"ordinaryMaximumShots",100000,"legacyCommandsPurelyValidated",legacyCommands,"installedExtensionCommandsPurelyValidated",installedExtensionCommands,"fullNativePresetRuns",0,"measurementWorkloadsExecuted",0,"largeShotBoundaryTests","Pure compiler checks and mock dispatch; no measurement workload of 100000 shots executed.","hypotheticalStandardError",estimate["standardError"],"categories",categories,"parameterSchemaById",schemasById);
        File.WriteAllText(proofPath,json.Serialize(proof),new UTF8Encoding(false));return "PASS "+checks+" assertions; all 241 catalog entries structurally compiled; 132 sampling defaults 1024; ordinary ceiling 100000; collapse 1; no full native preset run or stress workload.";
    }
}
