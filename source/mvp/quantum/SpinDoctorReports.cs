using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Web.Script.Serialization;

namespace QuettaQuantum
{
    // Symbolic presentation of recorded coefficients/programs. This never calls
    // a calculator, gate matrix evaluator, state query or simulation backend.
    public static class SpinDoctorReportEquations
    {
        public const string Schema="SPINDOCTOR/REPORT-EQUATIONS/1";
        private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        private static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
        public static Dictionary<string,object> Build(Dictionary<string,object> snapshot)
        {
            List<object> representations=new List<object>();int omitted=0;long bytes=0;
            object records=V(snapshot,"records");if(records!=null)Collect(Items(records),representations,ref omitted,ref bytes,0);
            else if(M(V(snapshot,"commandReport")).Count>0)Collect(new object[]{ArchiveRecord(M(V(snapshot,"commandReport")))},representations,ref omitted,ref bytes,0);
            else if(snapshot.ContainsKey("response"))Collect(new object[]{ArchiveRecord(snapshot)},representations,ref omitted,ref bytes,0);
            else if(snapshot.ContainsKey("nativeResponse"))Collect(new object[]{snapshot},representations,ref omitted,ref bytes,0);
            return D("schema",Schema,"representations",representations.ToArray(),"count",representations.Count,"omittedRepresentations",omitted,"scope","Report-layer symbolic/text representations of returned coefficients or matched retained circuit programs. These are editable text for a local draft, not new native answers, measurements, physical-law discovery or independent verification. Original native archive fields are unchanged.");
        }
        public static Dictionary<string,object> ForArchive(Dictionary<string,object> archive)
        { return Build(D("records",new object[]{ArchiveRecord(archive)})); }
        private static Dictionary<string,object> ArchiveRecord(Dictionary<string,object> archive)
        {
            Dictionary<string,object> record=D("command",V(archive,"normalizedCommand")??V(archive,"command"),"nativeResponse",V(archive,"response"));if(archive.ContainsKey("finalResponse"))record["finalNativeResponse"]=archive["finalResponse"];return record;
        }
        private static void Collect(IEnumerable records,List<object> output,ref int omitted,ref long bytes,int depth)
        {
            if(depth>2)return;foreach(object raw in records)
            {
                Dictionary<string,object> record=M(raw),response=M(V(record,"finalNativeResponse"));if(response.Count==0)response=M(V(record,"nativeResponse")??V(record,"response"));
                Dictionary<string,object> result=M(V(response,"result")),job=M(V(result,"job"));if(job.Count>0)result=job;
                Dictionary<string,object> representation=Describe(S(V(record,"command")),result);
                if(representation.Count>0)
                {
                    foreach(string key in new string[]{"sourceSha256","sourceFilename","nativeArchiveSha256","selectedArchiveStage"})if(record.ContainsKey(key))representation[key]=record[key];
                    representation["recordProvenance"]=S(V(record,"recordSource")).Length>0?record["recordSource"]:"Supplied recorded fields; native archive provenance not independently verified by this helper.";
                    long size=Encoding.UTF8.GetByteCount(Json.Serialize(representation));if(output.Count>=256||size>523264-bytes)omitted++;else{output.Add(representation);bytes+=size;}
                }
                Collect(Items(V(record,"hydratedCommandRecords")),output,ref omitted,ref bytes,depth+1);
            }
        }
        public static Dictionary<string,object> Describe(string command,Dictionary<string,object> result)
        {
            if(S(V(result,"resultKind"))=="empirical-csv-fit")
            {
                List<object> coefficients=Items(V(result,"coefficients")),variables=new List<object>();if(coefficients.Count>16)throw new ArgumentException("Equation display supports at most16 fitted features.");
                StringBuilder equation=new StringBuilder("y_hat = "+N(V(result,"intercept")));int index=0;
                foreach(object raw in coefficients){Dictionary<string,object> c=M(raw);index++;variables.Add(D("symbol","x"+index,"column",V(c,"feature"),"coefficientSymbol","b"+index,"coefficient",V(c,"coefficient"),"unit",V(c,"unit")));equation.Append(" + (").Append(N(V(c,"coefficient"))).Append(") * x").Append(index);}
                StringBuilder text=new StringBuilder();text.AppendLine(equation.ToString());text.AppendLine("y_hat is the fitted target: "+S(V(M(V(result,"target")),"name"))+"; b0 = "+N(V(result,"intercept")));
                foreach(object raw in variables){Dictionary<string,object> variable=M(raw);text.AppendLine(S(V(variable,"symbol"))+" = CSV column "+Json.Serialize(V(variable,"column"))+"; "+S(V(variable,"coefficientSymbol"))+" = "+N(V(variable,"coefficient"))+"; unit = "+S(V(variable,"unit")));}
                text.AppendLine("Recorded input-column equation: "+S(V(result,"equation")));if(result.ContainsKey("centeredEquation"))text.AppendLine("Stable centered form: "+S(result["centeredEquation"]));
                Dictionary<string,object> source=M(V(result,"source"));return D("schema",Schema,"command",command,"kind","empirical-fit","equationText",text.ToString(),"mathNotation",@"\widehat{y}=b_0+\sum_{j=1}^{p}b_jx_j", "featureCount",coefficients.Count,"intercept",V(result,"intercept"),"variables",variables.ToArray(),"sourceSha256",V(source,"sha256"),"sourceFilename",V(source,"filename"),"scope","Empirical in-sample fitted equation. Symbols map only to the actual returned CSV coefficients and column names; no circuit is inferred from measurement data.");
            }
            Dictionary<string,object> program=M(V(result,"programSnapshot"));string hash=S(V(result,"programHash"));
            if(program.Count>0&&hash.Length>0&&S(V(program,"descriptorHash")??V(program,"programHash"))!=hash)throw new InvalidDataException("Equation program snapshot does not match the returned circuit hash.");
            if(program.Count==0)program=SpinDoctorReportDetails.CircuitDefinitionProgram(command,result);
            if(program.Count==0||!program.ContainsKey("gates")||!program.ContainsKey("qubits"))return D();
            List<object> gates=Items(V(program,"gates"));if(gates.Count>8192)throw new ArgumentException("Equation gate list exceeds the native8192 instruction bound.");
            List<object> operators=new List<object>();StringBuilder circuitText=new StringBuilder();string initial=S(V(program,"initialBasis")),ket=BasisKet(initial,S(V(program,"qubits")));
            string product=gates.Count==0?"":gates.Count==1?"U[1] ":gates.Count==2?"U[2] U[1] ":"U["+gates.Count+"] ... U[1] ";circuitText.AppendLine("|psi_out> = "+product+"|"+ket+">");
            circuitText.AppendLine("Computational-basis index b = "+initial+"; register width n = "+S(V(program,"qubits"))+"; |b> = |"+ket+">.");
            circuitText.AppendLine("Gate rows are chronological; the first row acts first (rightmost in the operator product). Global bitstrings have qubit0 at the right. Local two-operand matrices use |operand1,operand0> = |00>,|01>,|10>,|11>: the first listed operand is the local least-significant bit, including a controlled gate's control.");
            int shown=Math.Min(256,gates.Count);for(int i=0;i<shown;i++)
            {
                Dictionary<string,object> gate=M(gates[i]);string name=S(V(gate,"name")??V(gate,"gate")).ToLowerInvariant();Dictionary<string,object> matrix=Matrix(name);List<object> angles=Items(V(gate,"angles"));
                Dictionary<string,object> op=D("index",i+1,"gate",name,"qubits",V(gate,"qubits"),"all",V(gate,"all"),"angles",V(gate,"angles"),"matrix",matrix);operators.Add(op);
                circuitText.Append("U[").Append(i+1).Append("] = ").Append(name.ToUpperInvariant()).Append("; operands = ").Append(Json.Serialize(V(gate,"qubits"))).Append("; all = ").Append(S(V(gate,"all"))).Append("; angle radians = ").Append(Json.Serialize(angles)).AppendLine();
                circuitText.AppendLine("  Local symbolic operator: "+S(V(matrix,"equationText")));for(int j=0;j<angles.Count;j++)circuitText.AppendLine("  a"+(j+1)+" = "+N(angles[j])+" radians");
            }
            if(shown<gates.Count)circuitText.AppendLine("First256 operator representations shown; "+(gates.Count-shown)+" further actual gate rows remain in the unchanged native program archive.");
            circuitText.AppendLine("Born: P(s | basis) = |<s;basis|psi_out>|^2 = |<s| B_basis |psi_out>|^2; B_basis is the measurement rotation (basis-ket matrix adjoint): B_Z = I, B_X = tensor(H), B_Y = tensor(H*S_dagger). These are mathematical basis equivalents, not extra executed gates. Recorded measurement basis: "+S(V(result,"basis")));
            circuitText.AppendLine("Local Bloch: rho_q = partial_trace_other_qubits(|psi_out><psi_out|); x=Tr(rho_q X)=2Re(rho_q[0,1]); y=Tr(rho_q Y)=-2Im(rho_q[0,1]); z=Tr(rho_q Z)=rho_q[0,0]-rho_q[1,1]. Entangled local states can have x=y=z=0.");
            string evolution=@"|\psi_{\mathrm{out}}\rangle="+(gates.Count==0?"":gates.Count==1?@"U_1":gates.Count==2?@"U_2U_1":@"U_m\cdots U_1")+@"|b\rangle";
            return D("schema",Schema,"command",command,"kind","declared-circuit","equationText",circuitText.ToString(),"mathNotation",evolution+@";\quad P(s\mid B)=|\langle s|B|\psi_{\mathrm{out}}\rangle|^2;\quad r_{q,k}=\mathrm{Tr}(\rho_q\sigma_k)","programHash",hash.Length>0?hash:S(V(program,"descriptorHash")),"initialBasis",V(program,"initialBasis"),"computationalBasisKet",ket,"qubits",V(program,"qubits"),"gateCount",gates.Count,"gateOperators",operators.ToArray(),"operatorRepresentationsComplete",shown==gates.Count,"omittedOperatorRepresentations",gates.Count-shown,"basis",V(result,"basis"),"scope","Symbolic declared circuit evolution from actual retained chronological program operands. Local matrices are native-convention formulas with recorded angle bindings; no full-register matrix, numerical state, probability, amplitude or extra shot is calculated by export. Histogram and spin figures remain separately recorded actual results.");
        }
        private static Dictionary<string,object> Matrix(string name)
        {
            string[][] rows=null;string expression="",factor="";
            switch(name)
            {
                case "i":rows=new string[][]{new string[]{"1","0"},new string[]{"0","1"}};break;
                case "h":factor="1/sqrt(2)";rows=new string[][]{new string[]{"1","1"},new string[]{"1","-1"}};break;
                case "x":rows=new string[][]{new string[]{"0","1"},new string[]{"1","0"}};break;
                case "y":rows=new string[][]{new string[]{"0","-i"},new string[]{"i","0"}};break;
                case "z":rows=new string[][]{new string[]{"1","0"},new string[]{"0","-1"}};break;
                case "s":case "sdg":case "t":case "tdg":case "p":rows=new string[][]{new string[]{"1","0"},new string[]{"0",name=="s"?"i":name=="sdg"?"-i":name=="t"?"exp(i*pi/4)":name=="tdg"?"exp(-i*pi/4)":"exp(i*a1)"}};break;
                case "sx":case "sxdg":factor="1/2";rows=new string[][]{new string[]{name=="sx"?"1+i":"1-i",name=="sx"?"1-i":"1+i"},new string[]{name=="sx"?"1-i":"1+i",name=="sx"?"1+i":"1-i"}};break;
                case "rx":rows=new string[][]{new string[]{"cos(a1/2)","-i sin(a1/2)"},new string[]{"-i sin(a1/2)","cos(a1/2)"}};break;
                case "ry":rows=new string[][]{new string[]{"cos(a1/2)","-sin(a1/2)"},new string[]{"sin(a1/2)","cos(a1/2)"}};break;
                case "rz":rows=new string[][]{new string[]{"exp(-i*a1/2)","0"},new string[]{"0","exp(i*a1/2)"}};break;
                case "u":rows=new string[][]{new string[]{"cos(a1/2)","-exp(i*a3) sin(a1/2)"},new string[]{"exp(i*a2) sin(a1/2)","exp(i*(a2+a3)) cos(a1/2)"}};break;
                case "cx":rows=new string[][]{new string[]{"1","0","0","0"},new string[]{"0","0","0","1"},new string[]{"0","0","1","0"},new string[]{"0","1","0","0"}};break;
                case "swap":rows=new string[][]{new string[]{"1","0","0","0"},new string[]{"0","0","1","0"},new string[]{"0","1","0","0"},new string[]{"0","0","0","1"}};break;
                case "cy":case "cz":case "ch":case "cp":case "crx":case "cry":case "crz":expression="P0(control=operand0) I(target=operand1) + P1(control=operand0) "+name.Substring(1).ToUpperInvariant()+"(target=operand1; a1 when applicable)";break;
                case "cu":expression="P0(control=operand0) I(target=operand1) + P1(control=operand0) exp(i*a4) U(a1,a2,a3; target=operand1)";break;
                case "ccx":expression="I8 + P1(operand0) P1(operand1) (X(operand2)-I); labelled operators are embedded on the stated operands and multiplied, not tensor-listed in control-first order.";break;
                case "cswap":expression="P0(control=operand0) I(targets=operand1,operand2) + P1(control=operand0) SWAP(targets=operand1,operand2); labelled commuting embedded products, not tensor-listed in control-first order.";break;
                case "rxx":case "ryy":case "rzz":case "rzx":expression="cos(a1/2) I4 - i sin(a1/2) "+Char.ToUpperInvariant(name[1])+"(operand0) "+Char.ToUpperInvariant(name[2])+"(operand1)";break;
                default:return D("available",false,"equationText","Display formula unavailable for this recorded operator; operands are retained without a guessed matrix.");
            }
            if(rows!=null)expression=(factor.Length>0?factor+" * ":"")+Json.Serialize(rows);
            string basis=name=="ccx"||name=="cswap"?"|operand2,operand1,operand0>; operand0 is local least-significant bit. Labelled operators act on their stated operands in this embedding; no full-register matrix.":rows!=null&&rows.Length==2?"|0>,|1>":"|operand1,operand0> = |00>,|01>,|10>,|11>; operand0 is local least-significant bit and control where controlled. No global register matrix.";
            return D("available",true,"equationText",expression,"prefactor",factor,"rows",rows,"basis",basis);
        }
        private static Dictionary<string,object> D(params object[] a){return SpinDoctorCalculators.D(a);}
        private static object V(Dictionary<string,object> map,string key){object value;return map!=null&&map.TryGetValue(key,out value)?value:null;}
        private static Dictionary<string,object> M(object value){return value as Dictionary<string,object>??new Dictionary<string,object>();}
        private static string S(object value){return value==null?"":Convert.ToString(value,Inv);}
        private static string N(object value){return value is double?((double)value).ToString("R",Inv):value is float?((float)value).ToString("R",Inv):value==null?"Not returned":S(value);}
        private static string BasisKet(string initial,string width){long basis;int n;if(Int64.TryParse(initial,NumberStyles.None,Inv,out basis)&&basis>=0&&Int32.TryParse(width,NumberStyles.None,Inv,out n)&&n>=1&&n<=29&&basis<(1L<<n))return Convert.ToString(basis,2).PadLeft(n,'0');return "b";}
        private static List<object> Items(object value){List<object> items=new List<object>();IEnumerable list=value as IEnumerable;if(list!=null&&!(value is string)&&!(value is IDictionary))foreach(object item in list)items.Add(item);return items;}
    }
    // A lossless table of recorded values. JSON string literals keep arbitrary
    // strings out of the spreadsheet formula namespace without changing them.
    // Container rows preserve empty objects/arrays; an absent native path is a
    // missing field, whereas a present null has an explicit null row.
    public static class SpinDoctorReportCsv
    {
        public const string Schema = "SPINDOCTOR/CSV-REPORT/1";
        public const long MaximumStringBytes = 33554432L, MaximumStreamBytes = 536870912L;
        public const int MaximumRows = 1048575, MaximumDepth = 100;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string Build(object payload,string kind,string id,string stage)
        { return Build(payload,kind,id,stage,""); }
        public static string Build(object payload,string kind,string id,string stage,string sourceSha256)
        {
            StringBuilder text=new StringBuilder();using(StringWriter target=new StringWriter(text,Inv))
            { Writer csv=new Writer(target,MaximumStringBytes);csv.Document(payload,kind,id,stage,sourceSha256); }
            return text.ToString();
        }
        public static void Write(TextWriter target,object payload,string kind,string id,string stage)
        { Writer csv=new Writer(target,MaximumStreamBytes);csv.Document(payload,kind,id,stage,""); }
        public sealed class Writer
        {
            private readonly TextWriter target;
            private readonly long maximumBytes;
            private readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=33554432,RecursionLimit=100};
            private long bytes;
            private int rows;
            private string kind="",id="",stage="";
            private readonly HashSet<object> ancestors=new HashSet<object>();
            public Writer(TextWriter target,long maximumBytes)
            {
                if(target==null)throw new ArgumentNullException("target");if(maximumBytes<1 ||maximumBytes>MaximumStreamBytes)throw new ArgumentOutOfRangeException("maximumBytes");
                this.target=target;this.maximumBytes=maximumBytes;
                Line(new string[]{"schema","report_kind","record_id_json","report_stage_json","path","presence","value_type","item_count","value_json"});
                Context("export","","format");
                Node("/export/schema",Schema,0);
                Node("/export/rendererVersion",TerminalSession.ProductVersion,0);
                Node("/export/valueEncoding","RFC 4180 UTF-8 CSV with CRLF rows. value_json is an invariant JSON scalar, not spreadsheet formula text; decode it to recover each original string or number. Object/array rows contain item_count; child paths use RFC 6901 JSON Pointer escaping. Row order is deterministic. No native calculations or verification are performed by export.",0);
                Node("/export/missingFieldRule","A native path with no row was not returned. A present null is value_type=null and value_json=null; an explicit zero remains numeric zero or its original string. Empty containers have item_count=0. Export provenance that is unavailable is marked presence=missing, never zero.",0);
            }
            public void Context(string kind,string id,string stage)
            {
                if(!System.Text.RegularExpressions.Regex.IsMatch(kind??"",@"^[a-z][a-z0-9_-]{0,39}$"))throw new ArgumentException("Invalid CSV report kind.");
                this.kind=kind;this.id=json.Serialize(id??"");this.stage=json.Serialize(stage??"");
            }
            public void Document(object payload,string kind,string id,string stage,string sourceSha256)
            {
                Context(kind,id,stage);Provenance("/export/sourceArchiveSha256",sourceSha256);Node("/native",payload,0);Dictionary<string,object> map=payload as Dictionary<string,object>;if(map!=null){Dictionary<string,object> equations=SpinDoctorReportEquations.Build(map);if((int)equations["count"]>0)Node("/export/equationRepresentations",equations,0);}
            }
            public void Provenance(string path,string sha256)
            {
                if(String.IsNullOrEmpty(sha256)){Missing(path);return;}
                if(!System.Text.RegularExpressions.Regex.IsMatch(sha256,@"^[a-f0-9]{64}$"))throw new ArgumentException("Invalid CSV archive SHA-256 provenance.");Node(path,sha256,0);
            }
            public void Missing(string path){Path(path);Row(path,"missing","missing","","");}
            public void Value(string path,object value){Path(path);Node(path,value,0);}
            private static void Path(string path){if(path==null ||!path.StartsWith("/",StringComparison.Ordinal))throw new ArgumentException("CSV value paths must be rooted JSON Pointers.");}
            private void Node(string path,object value,int depth)
            {
                if(depth>MaximumDepth)throw new InvalidDataException("CSV nesting exceeds 100 levels; export a smaller recorded object.");
                if(value==null){Row(path,"present","null","","null");return;}
                IDictionary map=value as IDictionary;
                if(map!=null)
                {
                    Enter(value);try{List<string> keys=new List<string>();foreach(object key in map.Keys){if(!(key is string))throw new InvalidDataException("CSV objects require string field names.");keys.Add((string)key);}keys.Sort(StringComparer.Ordinal);Row(path,"present","object",keys.Count.ToString(Inv),"");foreach(string key in keys)Node(path+"/"+key.Replace("~","~0").Replace("/","~1"),map[key],depth+1);}finally{ancestors.Remove(value);}return;
                }
                IList list=value as IList;
                if(list!=null)
                {
                    Enter(value);try{Row(path,"present","array",list.Count.ToString(Inv),"");for(int i=0;i<list.Count;i++)Node(path+"/"+i.ToString(Inv),list[i],depth+1);}finally{ancestors.Remove(value);}return;
                }
                if(value is string){Row(path,"present","string","",json.Serialize(value));return;}
                if(value is bool){Row(path,"present","boolean","",(bool)value?"true":"false");return;}
                if(value is double){double number=(double)value;if(Double.IsNaN(number)||Double.IsInfinity(number))throw new InvalidDataException("CSV cannot publish a non-finite native JSON number.");Row(path,"present","number","",number==0 &&BitConverter.DoubleToInt64Bits(number)<0?"-0.0":number.ToString("R",Inv));return;}
                if(value is float){float number=(float)value;if(Single.IsNaN(number)||Single.IsInfinity(number))throw new InvalidDataException("CSV cannot publish a non-finite native JSON number.");Row(path,"present","number","",number.ToString("R",Inv));return;}
                if(value is decimal){Row(path,"present","number","",((decimal)value).ToString(Inv));return;}
                if(value is sbyte ||value is byte ||value is short ||value is ushort ||value is int ||value is uint ||value is long ||value is ulong ||value is BigInteger){Row(path,"present","integer","",Convert.ToString(value,Inv));return;}
                throw new InvalidDataException("CSV supports recorded JSON values only; unsupported native type "+value.GetType().Name+".");
            }
            private void Enter(object value){if(!ancestors.Add(value))throw new InvalidDataException("CSV cannot serialize cyclic native objects.");}
            private void Row(string path,string presence,string type,string count,string encoded)
            {
                if(++rows>MaximumRows)throw new InvalidDataException("CSV exceeds 1,048,575 data rows; export individual files or commands.");
                Line(new string[]{Schema,kind,id,stage,path,presence,type,count,encoded});
            }
            private void Line(string[] cells)
            {
                StringBuilder line=new StringBuilder();for(int i=0;i<cells.Length;i++){if(i>0)line.Append(',');line.Append('"').Append((cells[i]??"").Replace("\"","\"\"")).Append('"');}line.Append("\r\n");
                string raw=line.ToString();long count=Encoding.UTF8.GetByteCount(raw);if(count>maximumBytes-bytes)throw new InvalidDataException("CSV exceeds the bounded export byte budget; export individual files or commands. No native report was changed.");bytes+=count;target.Write(raw);
            }
        }
    }
    // A bounded human interpretation of recorded fields. This layer neither
    // re-executes a command nor modifies or replaces its complete native return.
    public static class SpinDoctorReportDetails
    {
        public const int MaximumTextCharacters = 8192;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string Truncation = "\n[Readable detail limit reached; complete exact fields remain in the raw native return.]\n";
        private sealed class Lines
        {
            private readonly StringBuilder value = new StringBuilder();
            private bool full;
            public void Add(string label, string text)
            {
                if(full)return;
                string line = label.Length == 0 ? text : label + ": " + text;
                int room = MaximumTextCharacters - Truncation.Length - value.Length;
                if(line.Length + Environment.NewLine.Length > room)
                { if(room>0)value.Append(line.Substring(0,room)); value.Append(Truncation);full=true;return; }
                value.AppendLine(line);
            }
            public override string ToString(){return value.ToString();}
        }
        public static string Text(string command, Dictionary<string,object> response)
        {
            try{return TextCore(command,response);}
            catch(Exception error){return "READABLE NATIVE REPORT DETAIL\nReadable detail unavailable: "+Clean(error.Message,320)+"\nThe unchanged complete native return remains the authoritative record. No calculation or validation was inferred.\n";}
        }
        private static string TextCore(string command, Dictionary<string,object> response)
        {
            Dictionary<string,object> root=response ?? new Dictionary<string,object>(),result=Map(Get(root,"result")),status=Map(Get(root,"status"));
            Dictionary<string,object> job=Map(Get(result,"job")); if(job.Count>0)result=job;
            string[] words=(command??"").Trim().Split(new char[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            string ns=words.Length>0?words[0].ToLowerInvariant():"", action=words.Length>1?words[1].ToLowerInvariant():"";
            Dictionary<string,object> definition=CircuitDefinitionProgram(command,result);
            bool defined=definition.Count>0 && IsTrue(Get(root,"ok")) && !(Get(root,"nativeCompletionKnown") is bool&&!IsTrue(Get(root,"nativeCompletionKnown"))) && !(Get(result,"nativeCompletionKnown") is bool&&!IsTrue(Get(result,"nativeCompletionKnown")));
            Lines b=new Lines();b.Add("","READABLE NATIVE REPORT DETAIL");
            b.Add("Recorded command / exact inputs",Clean(command??"",1024));
            if(defined){b.Add("Circuit defined, not run","This command returned or edited a selected circuit definition. It did not execute that circuit; any earlier run is separate.");b.Add("Next execution command","q.circuit run shots 1024 basis z (an explicit next step, not executed by this report)");}
            b.Add("Input scope","The recorded command is the input evidence. Native echoed operands appear below where returned; omitted/default operands are not reconstructed by this report.");
            b.Add("Returned operation",Fields(result,new string[]{"namespace","action","subaction","backend","resultKind","complexLayout"},640));
            bool pending=IsTrue(Get(result,"active")) || IsTrue(Get(result,"queued")) || IsTrue(Get(Map(Get(root,"result")),"queued"));
            string phase=Scalar(Get(result,"phase"),100),known=Scalar(Get(root,"nativeCompletionKnown")??Get(result,"nativeCompletionKnown"),100);
            b.Add("Native command ok",root.ContainsKey("ok")?Scalar(root["ok"],100):"not returned");
            b.Add("Native phase",phase.Length>0?phase:"not returned (no asynchronous phase inferred)");
            b.Add("Completion meaning",known=="false"?"Native completion is UNKNOWN. This is an orchestration failure/cancellation, not proof that the native job failed or completed.":pending?"Accepted or active asynchronous work; successful completion has not been established by this return.":phase=="failed"||phase=="cancelled"||phase=="interrupted"||phase.StartsWith("orchestration-",StringComparison.Ordinal)?"The returned job snapshot is incomplete or unsuccessful; only its recorded finished work is credited.":"A synchronous return or recorded final snapshot is shown. Command acceptance alone is not independent verification.");
            b.Add(defined?"Definition command completion stamp":"Execution completion stamp",Fields(CompletionMetadata(root,result),new string[]{"executionOutcome","commandCompletedUtc","completionScope","completedUtc"},900));
            if(defined)b.Add("Definition completion meaning","The stamp records completion of this definition, selection or inspection command. It is not a circuit-run completion or a numerical verification stamp.");
            if(result.ContainsKey("receivedUtc")||result.ContainsKey("completedUtc"))b.Add("Native run start / stop UTC",Fields(result,new string[]{"receivedUtc","completedUtc","phase","active"},500));
            Dictionary<string,object> receipt=Map(Get(root,"receiptMetadata"));if(receipt.Count==0)receipt=root;
            bool required=IsTrue(Get(receipt,"verificationRequired"))||IsTrue(Get(result,"verificationRequired"));
            string verificationPhase=Scalar(Get(receipt,"verificationPhase")??Get(result,"verificationPhase"),100);
            Dictionary<string,object> verification=Map(Get(receipt,"verification")??Get(result,"verification"));
            if(required||verification.Count>0||receipt.ContainsKey("verificationRequired")||result.ContainsKey("verificationRequired"))
            {
                bool disabled=!required&&(verificationPhase=="skipped"||verificationPhase=="disabled");
                b.Add("Receipt verification status",(required?"Required procedural CPU/GPU sampling: ":verificationPhase=="unverified-import"?"Unverified imported snapshot: ":verificationPhase=="not-requested"?"Folder receipt verification not required: ":disabled?"Explicit unverified mode: ":"Verification policy not established: ")+(verificationPhase.Length>0?verificationPhase:"not recorded")+"; receiptReady="+Scalar(Get(receipt,"receiptReady")??Get(result,"receiptReady"),80));
                if(verificationPhase=="unverified-import")b.Add("Imported snapshot provenance","Native archive provenance was not verified. No sample verification PASS is inferred from imported fields.");
                b.Add("Verification scope","This materializes sampled current-model predictions (amplitudes, spin and Born probabilities) and checks CPU / available GPU agreement. A PASS applies to that model and these sampled addresses; it does not independently certify arbitrary q.math calculator results or the entire address domain.");
                Dictionary<string,object> vr=Map(Get(verification,"response")),vs=Map(Get(vr,"nativeStatusSnapshot")??Get(vr,"status")??Get(verification,"modelBefore")),vv=Map(Get(vr,"result"));if(Map(Get(vv,"job")).Count>0)vv=Map(Get(vv,"job"));if(vv.Count==0)vv=verification;
                if(IsTrue(Get(result,"folder"))&&verification.Count==0)
                {
                    Dictionary<string,object> summary=FolderVerificationSummary(result);
                    b.Add("Per-file sampling evidence",Fields(summary,new string[]{"recordedFiles","filesWithEvidence","passedFiles","failedFiles","cpuStates","gpuStates","verifiedCount","plannedSamples","completedSamples"},900));
                    b.Add("Evidence location","The folder keeps matched verification evidence under each files[].verification. Its empty parent verification object does not mean no sampling occurred. Aggregate counts above are sums of recorded per-file comparisons; they are not unique addresses across the run.");
                    b.Add("Per-file model scope",Fields(summary,new string[]{"trivialModelFiles","boundarySamples","receivedUtc","completedUtc"},600));
                    if(IsTrue(Get(summary,"hasTrivialModel")))b.Add("TRIVIAL MODEL WARNING","Recorded per-file checks include an unprepared or t=0 model, which can be address-constant. Its actual returned spin may depend on the shared gate. q.math inputs are separate calculator states and do not change this model.");
                }
                else
                {
                    b.Add("Verification start / stop UTC",Fields(verification,new string[]{"receivedUtc","completedUtc"},300));
                    b.Add("Actual sampling evidence",Fields(vv,new string[]{"passed","requestedSamples","plannedSamples","sampleCount","completedSamples","verifiedCount","cpuStates","gpuStates","errorCount","maxError","tolerance","boundarySamples","descriptorHash","addressDigest"},1100));
                    b.Add("Sampled model preparation",Fields(vs.Count>0?vs:vv,new string[]{"hasPrepared","migrationT","descriptorHash"},500));
                    if(Get(vv,"hasPrepared") is bool&&!IsTrue(Get(vv,"hasPrepared"))||Get(vs,"hasPrepared") is bool&&!IsTrue(Get(vs,"hasPrepared"))||Scalar(Get(vv,"migrationT")??Get(vs,"migrationT"),80)=="0")b.Add("TRIVIAL MODEL WARNING","This sampled model is unprepared or at t=0 and can be address-constant. Its arrow uses the actual returned model spin, including any shared gate; it does not depict a different state supplied to a q.math calculator.");
                }
                b.Add("Sampling limitation","An unprepared or t=0 procedural model can produce the same trivial state at every sampled address. Passing that comparison does not establish varied state data, random measurement shots, or correctness of independent calculator outputs.");
                List<object> preview=Items(Get(vv,"samplePreview")??Get(vv,"samplePreviewRows"),4);if(preview.Count>0)b.Add("Actual sample preview (bounded)",Preview(preview,0));
            }
            b.Add("Failure stage / scope",Fields(root,new string[]{"failureStage","failureScope","nativeCompletionKnown","error","checkpointError"},640));
            if(result.ContainsKey("error")||result.ContainsKey("failure")||result.ContainsKey("checkpointError")||result.ContainsKey("awaitedJobId"))b.Add("Job failure / awaited identity",Fields(result,new string[]{"failureStage","failureScope","nativeCompletionKnown","error","failure","checkpointError","awaitedJobType","awaitedJobId"},640));
            Dictionary<string,object> equationRepresentation=SpinDoctorReportEquations.Describe(command,result);if(equationRepresentation.Count>0){b.Add("Equation text (editable local draft)",Convert.ToString(equationRepresentation["equationText"],Inv));b.Add("Conventional mathematical notation (LaTeX)",Convert.ToString(equationRepresentation["mathNotation"],Inv));b.Add("Equation representation scope",Convert.ToString(equationRepresentation["scope"],Inv));}
            b.Add("Documented method for the requested operation",ns=="q.circuit"&&action=="preset"&&!defined ?"Construct the named Bell, GHZ or QFT program and request a circuit run with the returned shot count and basis. "+Method("q.circuit","run"):Method(ns,action));
            b.Add("Native method / convention",Fields(result,new string[]{"method","definition","equation","formula","convention","factorOrder","bitOrder","localBitOrder","orientation","assumption","scope","note"},1200));
            b.Add("Native echoed operands",Fields(result,new string[]{"address","qubit","qubits","selectedQubits","partition","basis","gate","anglesExact","angles","n","representation","base","time","hbar","parameter","parameterValue","targets","input","initialBasis","requestedShots","shots","requestedSamples","plannedSamples","epsilon","delta","databaseSize","marked","blockSizes"},640));
            string keyResults=Fields(defined?definition:result,ResultFields(ns,action),1400);b.Add("Key returned results",keyResults=="not returned"&&result.Count>0?"Native field preview: "+SmallMap(result,10,1400):keyResults);
            if(defined)b.Add("Planned instruction meaning","gateCount is the returned number of stored program instructions, not executed gates. Any gates[] summary counts those returned instructions once each; an all-qubit instruction is not expanded into performed work. Missing instruction arrays are not reconstructed.");
            Interpretation(b,ns,action,result);
            b.Add("Complex values","[real, imaginary] stores both components of a complex number. An exactly returned [0,0] means zero amplitude/overlap; its phase is undefined. Probabilities are squared magnitudes, while density/operator entries are not individual probabilities.");
            b.Add("Returned native checks",Fields(result,new string[]{"normalized","valid","unitary","equivalent","passed","pure","validStabilizers","allStabilize","mutuallyCommuting","tolerance","maxError","maximumElementError","unitarityError","maxNormError","maxSpinError","hermitianError","trace","minimumEigenvalue","imaginaryResidual","lengthSquared","residualSumSquares"},900));
            b.Add("Validation boundary","Returned booleans/residuals describe native checks at the returned tolerance and scope. ok=true is a command return/acceptance flag. Independent verification is not established unless a matched native verification result and its coverage are explicitly returned; the exporter does not recompute or certify it.");
            b.Add("Command-local work returned",Fields(result,new string[]{"actualStateQueries","actualAmplitudeWrites","actualComplexMultiplications","actualPreparedStateVectors","simulatedGateApplications","simulatedCircuitGateApplications","leftGateApplications","rightGateApplications","logicalGateApplications","physicalQuantumOperations","proceduralFamilyGateCredit","stateAllocations","allocationPerformed","sampledShots","executedQuantumGates","executedMeasurements","completedShots","completedSamples","verifiedCount","cpuStates","gpuStates","errorCount"},900));
            if(Map(Get(result,"counts")).Count>0)b.Add("Returned job work counters",SmallMap(Map(Get(result,"counts")),12,900));
            if(IsCalculator(ns))
            {
                b.Add("Why gate / shot / allocation zeros can be correct","q.math performs classical numerical algebra; q.resource uses estimates and allocates no represented state. Other calculator families use the bounded model stated in their scope. A returned physicalQuantumOperations=0 means no physical quantum hardware; proceduralFamilyGateCredit=0 means no family-wide gate credit. sampledShots=0 denotes deterministic probabilities/algebra rather than random measurement draws. These zeros do not mean no arithmetic was performed.");
                b.Add("Measurement seed", "Not applicable to deterministic calculator output. Returned measurementSeedChanged="+Present(result,"measurementSeedChanged")+"; measurementCounterChanged="+Present(result,"measurementCounterChanged")+". Missing mutation flags are not fabricated.");
            }
            else b.Add("Measurement seed / counters",Fields(result,new string[]{"seed","counterStart","counterEnd","collapsed","collapseOutcome"},640));
            b.Add("Accounting boundary","Command-local returned work, represented model credits and saved lifetime counters are different quantities. Status counters are snapshots, not this command's delta; this report does not derive FLOPS, throughput or unreturned work.");
            b.Add("Saved session snapshot",Fields(status,new string[]{"stateCount","representedPreparations","representedGateApplications","lifetimeRepresentedPreparations","lifetimeRepresentedGateApplications","actualCpuStates","actualGpuStates","verifiedSamples","measurementShots","measurementCounter","ruleChecks","quantumHardwareGates"},800));
            b.Add("Elapsed time scope","Native response elapsedMs="+Present(root,"elapsedMs")+"; result elapsedMs="+Present(result,"elapsedMs")+"; result totalMs="+Present(result,"totalMs")+". These are returned command/job timing scopes, not export time or a measured operation rate.");
            b.Add("Recorded provenance",Fields(root,new string[]{"schema","reportId","id","capturedUtc","receivedUtc","completedUtc","recordSource","sourceSha256","nativeArchiveSha256","descriptorHash"},540));
            b.Add("Native identity / source hashes",Fields(result,new string[]{"id","descriptorHash","programHash","sourceSha256","selectionSeed","addressDigest"},540));
            b.Add("Saved native product",Fields(status,new string[]{"productName","productVersion","schema","descriptorHash","fieldSeed"},540));
            b.Add("Report renderer version",TerminalSession.ProductVersion+"; renderer version does not prove the version of an imported historical response.");
            b.Add("Archive and detail scope",IsTrue(Get(root,"previewOnly"))||IsTrue(Get(result,"previewOnly"))?"This is a bounded preview. Missing fields/arrays are not reconstructed; consult the matched complete TXT/JSON native archive.":"Human detail is bounded to 8192 characters; long arrays are summarized. The unchanged complete structured native return follows or remains available in the matched TXT/JSON archive. Hashes establish byte identity, not independent numerical validation.");
            return b.ToString();
        }
        private static string Method(string ns,string action)
        {
            if(ns=="q.input"&&action=="table")return "Inspect the archived CSV table header, row counts and numeric data preview without fitting a model or drawing quantum measurement shots.";
            if(ns=="q.input"&&action=="fit")return "Empirical ordinary least-squares fit of the selected CSV target column against the selected numeric feature columns with an intercept, using centered/scaled column-pivoted Householder QR. Rows and exclusions are returned explicitly. Training fit metrics do not establish causality or validated forecast accuracy.";
            if(ns=="q.circuit")switch(action)
            {
                case "new":return "Create and select an empty circuit program with the returned qubit count and initial basis. No circuit state or measurement shots are executed.";
                case "preset":return "Construct and select the named Bell, GHZ or QFT instruction sequence with input basis 0. Stored instructions define a future circuit run; this command does not execute them.";
                case "select":return "Select an existing saved circuit definition. Selection is separate from any earlier run and does not execute its gates or draw shots.";
                case "add":return "Validate and append one named gate instruction and its exact operands to the selected program. Editing the program performs no circuit-state calculation.";
                case "input":return "Set the selected program's computational-basis input index. This edits the preparation specification and does not prepare or execute a circuit state.";
                case "clear":return "Clear the selected program's stored gate instructions. This changes the definition, not its earlier run history or lifetime work counters.";
                case "show":case "export":return "Return the selected program definition and its recorded operands without running it. Any accompanying saved status or earlier run result remains separate from the definition.";
                case "run":return "Replay the selected circuit independently for each requested joint measurement shot using seeded Born sampling; a separate coherent-output execution is retained. Acceptance is not completion; matched job-local work and histogram fields describe actual execution.";
                case "state":case "spin":return "Query the retained coherent premeasurement output of a matching completed circuit program. This is an actual state query, not a definition edit or a new measurement shot.";
                case "status":case "result":return "Return saved circuit status or the recorded circuit job result. A status/result query does not itself run another circuit or draw new shots.";
                case "cancel":return "Request cancellation of the active circuit job and return its recorded state. Cancellation acceptance is not proof of completed execution.";
                case "delete":case "prune":case "capacity":case "memory":case "list":case "help":return "Return or update circuit program storage, resource policy or help metadata. This operation does not request a circuit measurement run.";
            }
            if(ns=="q.folder"||ns=="q.sequence"||ns=="q.preset")return "Ordered native command execution with matched asynchronous completion barriers. Folder verification samples the saved procedural model before each file receipt is released; input command totals and verification work remain separate.";
            if(ns=="q.math")switch(action)
            {
                case "norm":return "Squared norm = sum_j |a_j|^2; norm = sqrt(squared norm). Normalized is checked against the native tolerance.";
                case "normalize":return "Scale-safe Euclidean normalization: scale by the largest real/imaginary component, then divide by the resulting norm. A zero vector is rejected.";
                case "probabilities":return "Require a normalized state and return p_j = |a_j|^2; no random samples are drawn.";
                case "overlap":return "Complex inner product <a|b> = sum_j conjugate(a_j) b_j after checking both states are normalized.";
                case "fidelity":return "Pure-state squared fidelity F = |<a|b>|^2 after checking both states are normalized.";
                case "bloch":case "from-bloch":return "Single-qubit Bloch components X=2 Re(conj(alpha) beta), Y=2 Im(conj(alpha) beta), Z=|alpha|^2-|beta|^2. Angles use alpha=cos(theta/2), beta=exp(i phi) sin(theta/2).";
                case "validate-bloch":return "Check X^2+Y^2+Z^2 <= 1+tolerance; pure means squared length is within tolerance of 1.";
                case "density":return "Pure-state density matrix rho = |psi><psi| from a normalized supplied vector.";
                case "validate-density":return "Check Hermiticity, trace near 1 and nonnegative Hermitian eigenvalues within tolerance.";
                case "purity":return "Density purity Tr(rho^2) after native density validity checks; values characterize mixing, not sampled outcomes.";
                case "entropy":return "Von Neumann entropy -sum(lambda_j log_base(lambda_j)) from density eigenvalues; zero eigenvalues contribute zero.";
                case "trace-distance":return "Trace distance = half the sum of absolute eigenvalues of Hermitian rho-sigma after validating both density matrices.";
                case "eigen":return "Hermitian eigenspectrum via a converged real-symmetric embedding Jacobi eigensolver; returned eigenvectors are supplied only for the native supported case.";
                case "unitary":case "gate-matrix":return "Construct/read the finite complex matrix and check the maximum element residual of U-dagger U minus identity.";
                case "compare-gates":return "Compare finite P and RZ matrices with the requested global-phase convention; maximum element difference determines returned equivalence.";
                case "expectation":return "Hermitian expectation <psi|O|psi>; the returned imaginary residual exposes numerical round-off.";
                case "tensor":return "Kronecker product left tensor right; right-factor indices occupy the lower-order positions.";
                case "commutator":return "Finite matrix commutator AB-BA.";
                case "canonical-phase":return "Display-only removal of the phase of the first amplitude with magnitude greater than 1e-15; original state amplitudes are retained.";
            }
            if(ns=="q.query")switch(action)
            {
                case "amplitudes":case "probabilities":return "Read a bounded index range of the completed circuit's retained coherent premeasurement output. Return p=|a|^2 without renormalizing a prefix or drawing shots.";
                case "expectation":return "Evaluate the supplied Pauli operator expectation on the selected pure coherent output; return its real value and imaginary residual.";
                case "stabilizers":return "Evaluate supplied candidate Pauli generators on the selected pure coherent output; candidates are checked for +1 expectation and mutual commutation.";
                case "reduced-density":case "purity":case "entropy":case "marginal":return "Reduce the selected pure output by tracing over omitted qubits; one-qubit reductions use returned spin components. Purity, entropy or basis probabilities are derived from that reduced density.";
                case "fidelity":return "Squared overlap between the retained coherent output and the supplied normalized target state.";
                case "concurrence":return "Entire pure two-qubit register: concurrence = 2 |a00 a11 - a01 a10|.";
                case "negativity":return "Schmidt-spectrum identity for the globally pure selected output: ((sum sqrt(lambda))^2-1)/2 for the specified partition.";
            }
            if(ns=="q.resource")switch(action)
            {
                case "memory":return "Complex128 dense payload: 16*2^n bytes for a statevector, or 16*4^n for a density matrix/unitary. Returned peak formulas add their stated multiplicities; no allocation occurs.";
                case "max-n":case "headroom":case "auto":return "Find the largest raw/planned dense qubit count fitting the stated payload budget and peak factor. OS available memory, where returned, is a momentary snapshot.";
                case "factor-memory":return "Sum 16*2^blockSize bytes for independent tensor factors; future entangling gates can merge factors.";
                case "tranche-plan":return "Independent address groups: tranches = ceil(addresses/width). This is a plan, not execution.";
                case "sampling":return "Bernoulli standard error = sqrt(p*(1-p)/shots), assuming independent samples; no shots are drawn by this estimate.";
                case "shots":return "Two-sided Hoeffding bound: ceil(log(2/delta)/(2*epsilon^2)); assumes independent bounded Bernoulli samples.";
                case "grover":return "Asymptotic oracle-query estimate pi/4*sqrt(N/M), excluding oracle implementation and state preparation.";
                case "qec":return "Multiply the user-supplied logical-qubit count by physical-per-logical overhead; no code-distance/error guarantee is inferred.";
            }
            if(ns=="q.noise")switch(action){case "apply":case "circuit":return "Explicit finite density evolution using the named local Kraus channel; returned probabilities are not sampled counts.";case "t1":case "t2":return "Exponential population/coherence relaxation exp(-time/T), with returned microsecond units and model assumptions.";case "readout":return "Classical forward confusion-matrix multiplication, rows observed / columns true; no inverse mitigation or sampled readout.";case "zne":return "Ordinary least-squares linear extrapolation of supplied observations to zero noise scale; returned residual and variance amplification quantify that fit.";}
            if(ns=="q.compile")switch(action){case "identity":return "Compare bounded circuit matrices, retaining the requested global-phase convention and maximum element error.";case "simplify":return "Multiply adjacent gates on identical operands and cancel a pair only when its matrix differs from identity by at most 1e-12 per element, preserving global phase. Returned removed/remaining instructions describe editing, not state execution.";case "count":case "depth":case "classify":return "Logical instruction analysis under the native returned connectivity/gate assumptions; no hardware execution or timing estimate is inferred.";}
            if(ns=="q.algorithm"&&action=="phase-estimation")return "Bounded coherent statevector phase estimation with a supported controlled phase eigenstate and inverse-QFT convention. Probabilities come from amplitudes; sampledShots=0 is expected.";
            if(ns=="q.qec"&&(action=="repetition3"||action=="phase-repetition3"))return "Finite ideal three-qubit repetition encoding and specified Pauli-error syndrome/recovery algebra; protection is limited to the declared error class. Deterministic syndromes are not sampled syndrome shots.";
            if(ns=="q.verify"&&action=="samples")return "Native finite distinct-address verification compares computed CPU/GPU states against the returned plan/tolerance. A sample check does not enumerate or verify the entire represented domain.";
            if(ns=="q.verify"&&action=="rule")return "Native rule/identity audit under its returned scope; distinguish algebraic checks from individually calculated model addresses.";
            return "A specific numerical method is not provided by this report mapping. Consult the returned native method/definition/scope and exact command; no algorithm is inferred.";
        }
        public static string[] KeyResultFields(string command)
        {string[] words=(command??" ").Trim().Split(new char[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries);return ResultFields(words.Length>0?words[0].ToLowerInvariant():"",words.Length>1?words[1].ToLowerInvariant():"");}
        public static string OperationMethod(string command)
        {string[] words=(command??" ").Trim().Split(new char[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries);return Method(words.Length>0?words[0].ToLowerInvariant():"",words.Length>1?words[1].ToLowerInvariant():"");}
        private static string[] ResultFields(string ns,string action)
        {
            if(ns=="q.input"&&action=="fit")return new string[]{"equation","centeredEquation","equationConvention","target","features","intercept","coefficients","metrics","rowCount","usedRows","excludedRows","rank","parameterCount","qrDiagonalRatio","residualDegreesOfFreedom","source","warnings"};
            if(ns=="q.input"&&action=="table")return new string[]{"columns","rowCount","rowsPreview","rowsPreviewComplete","source","fittingPerformed","scope"};
            if(ns=="q.circuit")switch(action)
            {
                case "new":case "select":case "preset":case "input":case "clear":case "show":case "export":return new string[]{"name","qubits","initialBasis","gateCount","descriptorHash"};
                case "add":return new string[]{"name","qubits","initialBasis","gateCount","descriptorHash","program","instruction"};
                case "run":case "status":case "result":case "cancel":return new string[]{"id","available","queued","active","phase","programName","programHash","qubits","initialBasis","basis","requestedShots","completedShots","measuredQubits","counts","counterStart","counterEnd","cancelRequested","error","checkpointError"};
                case "state":case "spin":return new string[]{"programHash","premeasurement","basisIndex","amplitude","probability","qubit","spin"};
                case "list":return new string[]{"selected","circuits"};
                case "delete":return new string[]{"deleted","selected"};
                case "prune":return new string[]{"pruned","prunedCount","keptPresetCircuits","selected","capacity","scope"};
                case "capacity":return new string[]{"programCapacity","programCount","freeSlots","presetOwnedCount","userOwnedCount","maximumCapacity","defaultCapacity","scope"};
                case "memory":return new string[]{"memoryLimitBytes","scope"};
            }
            if(ns=="q.folder"||ns=="q.sequence"||ns=="q.preset")return new string[]{"phase","active","completedFiles","totalFiles","completedCommands","totalCommands","totalCommandsKnown","receivedUtc","completedUtc","verificationRequired","verificationSamples","verificationPhase","receiptReady"};
            if(ns=="q.math")switch(action){case "norm":return new string[]{"normSquared","norm","normalized","tolerance"};case "normalize":return new string[]{"originalNormSquared","originalNormSquaredRepresentable","amplitudes"};case "overlap":return new string[]{"overlap"};case "fidelity":return new string[]{"fidelity","convention"};case "entropy":return new string[]{"entropy","base","eigenvalues"};case "purity":return new string[]{"purity"};case "probabilities":return new string[]{"probabilities","sum"};case "bloch":case "from-bloch":return new string[]{"bloch","amplitudes"};}
            return new string[]{"normSquared","norm","purity","entropy","entropyBits","overlap","fidelity","stateFidelity","concurrence","negativity","traceDistance","expectation","probability","probabilityOne","zero","one","probabilities","eigenvalues","bloch","blochLengthSquared","mixed","directionDefined","exactlyZeroBloch","directionResolvedAtTolerance","directionTolerance","stateCharacter","qubit","premeasurement","amplitudes","rho","matrix","rows","bytes","MiB","GiB","maximumRawQubits","maximumPlannedDenseQubits","requiredPeakBytes","fitsCurrentPlan","minimumShots","standardError","asymptoticQueries","estimatedPhysicalQubits","tranches","lastTrancheQubits","excitedProbability","groundProbability","coherence","magnitude","decayFactor","observedProbabilities","encodedQubits","distance","correctableErrors","mostLikelyOutcome","mostLikelyPhase","zeroNoiseEstimate","logicalGateApplications","idealLogicalDepth","cliffordAtTolerance","explanation","lengthSquared","normalized","valid","unitary","equivalent","passed","pure","trace","minimumEigenvalue","tolerance","maxError","maximumElementError","unitarityError","hermitianError","imaginaryResidual"};
        }
        private static void Interpretation(Lines b,string ns,string action,Dictionary<string,object> result)
        {
            double value;
            if(result.ContainsKey("overlap")){List<object> pair=Items(result["overlap"],3);double real,imag;if(pair.Count==2&&Number(pair[0],out real)&&Number(pair[1],out imag)&&real==0&&imag==0)b.Add("Zero overlap meaning","The returned overlap is exactly [0,0]: no overlap is resolved at returned FP64 precision. This is consistent with orthogonality at that precision, but does not establish exact mathematical orthogonality or exclude underflow. It is a calculated result, not missing data.");}
            if(Number(Get(result,"normSquared"),out value)&&value==0)b.Add("Zero norm meaning","The recorded squared norm is 0 at FP64 precision; this can mean a literal zero vector or underflow. The scale-safe native normalizer rejects only an actually zero input. Inspect the exact inputs; no normalization failure is inferred from this field.");
            if(Number(Get(result,"entropy")??Get(result,"entropyBits"),out value)&&value==0)b.Add("Zero entropy meaning","The returned entropy is exactly 0 at native numerical precision: no spectral mixing is resolved under the returned density/model scope. This is distinct from no calculation or missing entropy.");
            if(Number(Get(result,"purity"),out value))b.Add("Purity meaning","Tr(rho^2) measures density mixing: 1 is pure, 1/d is maximally mixed for a valid d-dimensional density. Returned value="+value.ToString("R",Inv)+"; this report does not invent a density dimension or recalculate purity.");
            if(result.ContainsKey("probabilities")||result.ContainsKey("returnedProbabilityMass"))b.Add("Probability meaning","Dimensionless Born probabilities, not sampled outcome counts. A returned 0 is a calculated zero at native precision; omitted indices are missing, not zero. Prefix queries remain unnormalized.");
            if(result.ContainsKey("originalNormSquaredRepresentable")&&!IsTrue(result["originalNormSquaredRepresentable"]))b.Add("Unrepresentable original norm","The normalized output was returned, but the original squared norm was not representable as finite nonzero FP64. A null originalNormSquared is not a fabricated zero or normalization failure.");
            if(result.ContainsKey("returnedStates")||result.ContainsKey("omittedStates"))b.Add("Array / coverage scope",Fields(result,new string[]{"start","returnedStates","totalStates","complete","omittedStates","returnedProbabilityMass","renormalized"},600));
        }
        private static bool IsCalculator(string ns){return ns=="q.math"||ns=="q.query"||ns=="q.resource"||ns=="q.noise"||ns=="q.qec"||ns=="q.compile"||ns=="q.model"||ns=="q.lesson"||ns=="q.algorithm";}
        public static Dictionary<string,object> CircuitDefinitionProgram(string command,Dictionary<string,object> result)
        {
            string[] words=(command??"").Trim().Split(new char[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            if(words.Length<2||!words[0].Equals("q.circuit",StringComparison.OrdinalIgnoreCase))return new Dictionary<string,object>();
            string action=words[1].ToLowerInvariant();
            if(action!="new"&&action!="select"&&action!="preset"&&action!="input"&&action!="add"&&action!="clear"&&action!="show"&&action!="export")return new Dictionary<string,object>();
            if(IsTrue(Get(result,"active"))||IsTrue(Get(result,"queued"))||Get(result,"nativeCompletionKnown") is bool&&!IsTrue(Get(result,"nativeCompletionKnown")))return new Dictionary<string,object>();
            string phase=Scalar(Get(result,"phase"),100);if(phase.Length>0&&phase!="completed"&&phase!="ready")return new Dictionary<string,object>();
            Dictionary<string,object> program=Map(Get(result,"program"));if(program.Count==0)program=result ?? new Dictionary<string,object>();
            int qubits,gates;string name=Scalar(Get(program,"name"),128),hash=Scalar(Get(program,"descriptorHash"),128);
            if(name.Length==0||hash.Length!=64||!Int32.TryParse(Scalar(Get(program,"qubits"),16),NumberStyles.None,Inv,out qubits)||qubits<1||qubits>29||!Int32.TryParse(Scalar(Get(program,"gateCount"),16),NumberStyles.None,Inv,out gates)||gates<0||gates>8192)return new Dictionary<string,object>();
            return program;
        }
        public static Dictionary<string,object> CompletionMetadata(Dictionary<string,object> root,Dictionary<string,object> result)
        {
            Dictionary<string,object> archive=Map(Get(root,"archive")),metadata=new Dictionary<string,object>();
            foreach(string key in new string[]{"executionOutcome","commandCompletedUtc","completionScope","completedUtc"})
            {object value=Get(root,key);if(Scalar(value,900).Length==0)value=Get(archive,key);if(Scalar(value,900).Length>0)metadata[key]=value;}
            if(Get(root,"nativeCompletionKnown") is bool&&!IsTrue(Get(root,"nativeCompletionKnown"))||Get(result,"nativeCompletionKnown") is bool&&!IsTrue(Get(result,"nativeCompletionKnown"))){metadata.Remove("commandCompletedUtc");metadata.Remove("completedUtc");metadata["completionScope"]="Native completion unknown; only failure diagnostics were captured.";return metadata;}
            if(!metadata.ContainsKey("commandCompletedUtc")&&Scalar(Get(result,"completedUtc"),120).Length>0){metadata["commandCompletedUtc"]=Get(result,"completedUtc");if(!metadata.ContainsKey("executionOutcome")&&Scalar(Get(result,"phase"),100).Length>0)metadata["executionOutcome"]=Get(result,"phase");if(!metadata.ContainsKey("completionScope"))metadata["completionScope"]="Recorded native result stop UTC; not export time or independent verification.";}
            if(!metadata.ContainsKey("commandCompletedUtc")&&Scalar(Get(metadata,"completedUtc"),120).Length>0){metadata["commandCompletedUtc"]=metadata["completedUtc"];if(!metadata.ContainsKey("executionOutcome")&&Scalar(Get(result,"phase"),100).Length>0)metadata["executionOutcome"]=Get(result,"phase");if(!metadata.ContainsKey("completionScope"))metadata["completionScope"]="Recorded selected archive completion barrier UTC; not export time or independent numerical verification.";}
            return metadata;
        }
        public static Dictionary<string,object> FolderVerificationSummary(Dictionary<string,object> result)
        {
            List<object> files=Items(Get(result,"files"),100000);Dictionary<string,object> summary=new Dictionary<string,object>();summary["recordedFiles"]=files.Count;
            string[] keys=new string[]{"cpuStates","gpuStates","verifiedCount","plannedSamples","completedSamples","boundarySamples"};BigInteger[] sums=new BigInteger[keys.Length];bool[] known=new bool[keys.Length];for(int k=0;k<known.Length;k++)known[k]=files.Count>0;
            int evidenceCount=0,passed=0,failed=0,trivial=0;DateTime first=DateTime.MaxValue,last=DateTime.MinValue;string firstUtc="",lastUtc="";
            foreach(object raw in files)
            {
                Dictionary<string,object> file=Map(raw),evidence=Map(Get(file,"verification")),response=Map(Get(evidence,"response")),value=Map(Get(response,"result"));if(Map(Get(value,"job")).Count>0)value=Map(Get(value,"job"));if(value.Count>0)evidenceCount++;
                if(Scalar(Get(file,"verificationPhase"),80)=="passed"&&IsTrue(Get(value,"passed")))passed++;if(Scalar(Get(file,"verificationPhase"),80)=="failed"||Get(value,"passed") is bool&&!IsTrue(Get(value,"passed")))failed++;
                Dictionary<string,object> status=Map(Get(response,"nativeStatusSnapshot")??Get(response,"status")??Get(evidence,"modelBefore"));if(Get(value,"hasPrepared") is bool&&!IsTrue(Get(value,"hasPrepared"))||Get(status,"hasPrepared") is bool&&!IsTrue(Get(status,"hasPrepared"))||Scalar(Get(value,"migrationT")??Get(status,"migrationT"),80)=="0")trivial++;
                for(int k=0;k<keys.Length;k++){BigInteger count;if(!BigInteger.TryParse(Scalar(Get(value,keys[k]),100),NumberStyles.None,Inv,out count)||count<0)known[k]=false;else sums[k]+=count;}
                string received=Scalar(Get(evidence,"receivedUtc")??Get(value,"receivedUtc"),120),completed=Scalar(Get(evidence,"completedUtc")??Get(value,"completedUtc"),120);DateTime utc;if(DateTime.TryParse(received,Inv,DateTimeStyles.RoundtripKind,out utc)&&utc<first){first=utc;firstUtc=received;}if(DateTime.TryParse(completed,Inv,DateTimeStyles.RoundtripKind,out utc)&&utc>last){last=utc;lastUtc=completed;}
            }
            summary["filesWithEvidence"]=evidenceCount;summary["passedFiles"]=passed;summary["failedFiles"]=failed;summary["trivialModelFiles"]=trivial;summary["hasTrivialModel"]=trivial>0;if(firstUtc.Length>0)summary["receivedUtc"]=firstUtc;if(lastUtc.Length>0)summary["completedUtc"]=lastUtc;for(int k=0;k<keys.Length;k++)if(known[k])summary[keys[k]]=sums[k].ToString(Inv);return summary;
        }
        private static string Present(Dictionary<string,object> d,string key){return d.ContainsKey(key)?Preview(d[key],0):"not returned";}
        private static string Fields(Dictionary<string,object> d,string[] keys,int limit)
        { StringBuilder b=new StringBuilder();foreach(string key in keys){if(!d.ContainsKey(key))continue;string item=Clean(key,64)+"="+Preview(d[key],0);if(b.Length+item.Length>limit){b.Append("; further fields in raw return");break;}if(b.Length>0)b.Append("; ");b.Append(item);}return b.Length>0?b.ToString():"not returned"; }
        private static string SmallMap(Dictionary<string,object> d,int maximum,int limit)
        {List<string> keys=new List<string>();foreach(string key in d.Keys){keys.Add(key);if(keys.Count>=maximum)break;}return Fields(d,keys.ToArray(),limit);}
        private static string Preview(object value,int depth)
        {
            if(value==null)return "null (returned)";
            Dictionary<string,object> map=Map(value);if(map.Count>0){if(depth>=1)return "{structured fields; raw return}";StringBuilder d=new StringBuilder("{");int count=0;foreach(KeyValuePair<string,object> item in map){if(count++>=4){d.Append("; more in raw return");break;}if(count>1)d.Append("; ");d.Append(Clean(item.Key,48)).Append('=').Append(Preview(item.Value,depth+1));}return d.Append('}').ToString();}
            if(value is IDictionary)return "{empty/structured object; raw return}";
            IEnumerable sequence=value as IEnumerable;if(sequence!=null&&!(value is string))
            {List<object> items=Items(value,5);StringBuilder b=new StringBuilder("[");int take=Math.Min(4,items.Count);for(int i=0;i<take;i++){if(i>0)b.Append(", ");b.Append(depth<1?Preview(items[i],depth+1):Scalar(items[i],80));}if(items.Count>take)b.Append(", ... (more in raw return)");return b.Append(']').ToString();}
            return Scalar(value,240);
        }
        private static List<object> Items(object value,int maximum){List<object> r=new List<object>();IEnumerable e=value as IEnumerable;if(e!=null&&!(value is string)&&!(value is IDictionary))foreach(object item in e){r.Add(item);if(r.Count>=maximum)break;}return r;}
        private static string Scalar(object value,int maximum){if(value==null)return "";if(value is bool)return (bool)value?"true":"false";if(value is IDictionary||value is IEnumerable&&!(value is string))return "structured data (raw return)";return Clean(Convert.ToString(value,Inv),maximum);}
        private static string Clean(string value,int maximum){StringBuilder b=new StringBuilder();int i=0;for(;i<value.Length&&b.Length<maximum;i++){char c=value[i];if(Char.IsControl(c)||Char.GetUnicodeCategory(c)==UnicodeCategory.Format)b.Append("\\u"+((int)c).ToString("x4",Inv));else b.Append(c);}if(b.Length>maximum)b.Length=maximum;if(i<value.Length)b.Append("... [truncated; exact value in raw return]");return b.ToString();}
        private static bool Number(object value,out double result){result=0;return value!=null&&!(value is bool)&&(!(value is IEnumerable)||value is string)&&Double.TryParse(Convert.ToString(value,Inv),NumberStyles.Float,Inv,out result)&&!Double.IsNaN(result)&&!Double.IsInfinity(result);}
        private static bool IsTrue(object value){return value is bool&&(bool)value;}
        private static object Get(Dictionary<string,object> value,string key){object r;return value.TryGetValue(key,out r)?r:null;}
        private static Dictionary<string,object> Map(object value){return value as Dictionary<string,object>??new Dictionary<string,object>();}
    }

    // Self-contained vector PDF export. No printer driver, browser dialog,
    // external executable or remotely loaded font is required by the terminal.
    public static class SpinDoctorReportPdf
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double Width = 612, Height = 792, Left = 42, Right = 570, Bottom = 48;
        private const int MaximumPdfPages = 512, MaximumPdfTextCharacters = 4194304, MaximumPdfBytes = 33554432;
        private static readonly string[] Counts = { "stateCount", "representedPreparations", "representedGateApplications", "lifetimeRepresentedPreparations", "lifetimeRepresentedGateApplications", "actualCpuStates", "actualGpuStates", "verifiedSamples", "measurementShots", "ruleChecks", "errorCount", "quantumHardwareGates" };
        [ThreadStatic] private static JavaScriptSerializer threadJson;
        private static JavaScriptSerializer Json { get { if(threadJson==null)threadJson=CreateJson();return threadJson; } }
        private static JavaScriptSerializer CreateJson() { JavaScriptSerializer j = new JavaScriptSerializer(); j.MaxJsonLength = 33554432; j.RecursionLimit = 100; return j; }

        public static byte[] Build(Dictionary<string, object> request, string fullCircuitPrintout)
        {
            if (request == null) throw new ArgumentException("A PDF report request is required.");
            string kind = Str(Get(request, "kind"));
            if (kind != "selected" && kind != "session" && kind != "circuit") throw new ArgumentException("PDF kind must be selected, session or circuit.");
            List<object> records = Items(Get(request, "records"));
            if (records.Count > 5000) throw new ArgumentException("A PDF export contains at most 5000 command records; export additional sessions separately.");
            if (kind == "selected" && records.Count != 1) throw new ArgumentException("A selected PDF requires exactly one command record.");
            string view = Str(Get(request, "view"));
            if(view.Length==0)view="summary";
            if(view!="summary"&&view!="details"&&view!="raw")throw new ArgumentException("PDF view must be summary, details or raw.");
            Dictionary<string, Dictionary<string, object>> programs = ProgramIndex(records);
            Report p = new Report(kind == "circuit" ? "Circuit measurements" : kind == "session" ? "Session calculation reports" : "Selected calculation report");
            p.Paragraph("Classical quantum-state simulation. Reported calculations, sampled outcomes and represented model credits use separate units. Physical quantum gates executed: 0.");
            p.Paragraph("PDF generated UTC: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ". Report renderer: " + TerminalSession.ProductVersion + ". This is export time, separate from the original native calculation completion. Re-exporting archived results performs no new calculation. Visuals use returned native values.");
            p.Key("Presentation", view=="summary"?"Summary: returned figures and accounting":view=="details"?"Detailed: readable meaning, returned fields and figures":"Complete: detailed figures plus exact recorded payloads");
            p.Paragraph("Export limits: 512 pages, 4 MiB of report text and 32 MiB of PDF bytes. Exports exceeding these limits are refused; use complete TXT/JSON or smaller command batches.");
            if (kind == "circuit")
            {
                Dictionary<string, object> status = Map(Get(request, "status"));
                Dictionary<string, object> circuit = Map(Get(status, "circuit"));
                Dictionary<string, object> result = Map(Get(circuit, "result"));
                p.Heading("Native circuit result");
                if (result.Count == 0) p.Paragraph("No circuit measurement run is available. This export does not run a calculation.");
                else
                {
                    bool same = MatchesNativePrintout(result, fullCircuitPrintout);
                    if(view=="details"||view=="raw")Readable(p,SpinDoctorReportDetails.Text("Recorded circuit snapshot (original command not returned)",D("result",result,"status",status)));
                    Circuit(p, result, same ? ParseProgram(fullCircuitPrintout) : RecordedProgram(result, programs), same ? FullHistogram(fullCircuitPrintout) : null);
                    Accounting(p, status, view != "raw");
                    if(view=="details"){p.Heading("Native circuit result field guide");CompactObject(p,result,0,120);p.Paragraph("The field guide is bounded; full circuit outcome rows and replay information remain in the matched native TXT/JSON.");}
                    if(view=="raw"){p.Paragraph("Raw appendices preserve all supplied fields. Non-ASCII JSON characters use reversible Unicode escapes; whitespace wraps for printing. TXT/JSON archives retain the authoritative bytes.");p.Heading("Complete supplied native circuit result snapshot");p.Mono(PrintableJson(result));p.Heading("Complete supplied saved native status");p.Mono(PrintableJson(status));if(same){p.Heading("Complete matching native circuit TXT printout (printable rendering)");p.Mono(fullCircuitPrintout);}else p.Paragraph("No complete native TXT printout matching this result was supplied. This export does not reconstruct one or include unreturned circuit rows.",true);}
                }
            }
            else
            {
                p.Key("Retained command records", records.Count.ToString(Inv));
                p.Paragraph("These reports describe recorded responses and job snapshots. Exporting them does not execute another measurement or calculation. Complete raw native responses remain available through the JSON and TXT downloads.");
                int index = 0;
                foreach (object raw in records)
                {
                    Dictionary<string, object> record = Map(raw);
                    if (record.Count == 0) throw new ArgumentException("Every PDF record must be an object.");
                    if (index++ != 0) p.NewPage();
                    Record(p, record, view, fullCircuitPrintout, programs);
                }
            }
            return p.Finish();
        }

        private static void Record(Report p, Dictionary<string, object> r, string view, string nativeText, Dictionary<string, Dictionary<string, object>> programs)
        {
            Dictionary<string, object> initial = Map(Get(r, "nativeResponse")), latest = Map(Get(r, "finalNativeResponse"));
            if (initial.Count == 0) initial = Map(Get(r, "response"));
            if (latest.Count == 0) latest = Map(Get(r, "latestStatusResponse"));
            Dictionary<string, object> status = Map(Get(latest, "status"));
            if (status.Count == 0) status = Map(Get(initial, "status"));
            Dictionary<string, object> result = Map(Get(latest.Count == 0 ? initial : latest, "result"));
            string command = Str(Get(r, "command")), kind = Str(Get(r, "kind")), jobId = Str(Get(r, "jobId"));
            // Job acceptance is not job success. Resolve the matched final result
            // before selecting any figures, including failed and cancelled jobs.
            Dictionary<string, object> queued = Map(Get(result, "job"));
            if (queued.Count != 0) { result = queued; if (jobId.Length == 0) jobId = Str(Get(queued, "id")); }
            Dictionary<string,object> completionEvidence=latest.Count==0?initial:latest;
            bool nativeUnknown=Get(completionEvidence,"nativeCompletionKnown") is bool&&!(bool)Get(completionEvidence,"nativeCompletionKnown")||Get(result,"nativeCompletionKnown") is bool&&!(bool)Get(result,"nativeCompletionKnown")||Str(Get(completionEvidence,"failureScope"))=="sequence-orchestration"||Str(Get(result,"phase")).StartsWith("orchestration-",StringComparison.Ordinal);
            if (jobId.Length != 0 && !nativeUnknown)
            {
                Dictionary<string, object> candidate = kind == "circuit" || command.StartsWith("q.circuit run", StringComparison.OrdinalIgnoreCase) ? Map(Get(Map(Get(status, "circuit")), "result")) : kind == "tranche" ? Map(Get(Map(Get(status, "tranche")), "job")) : Map(Get(status, "lastVerificationJob"));
                if (Str(Get(candidate, "id")) == jobId && (Str(Get(result,"id"))!=jobId || !result.ContainsKey("histogram")))
                {
                    result = kind == "tranche" && Map(Get(candidate, "result")).Count != 0 ? Map(Get(candidate, "result")) : candidate;
                }
            }
            p.Heading(command.Length == 0 ? "Command return" : command);
            Dictionary<string, object> effective = latest.Count == 0 ? initial : latest;
            bool definition=!nativeUnknown && Bool(Get(effective,"ok")) && SpinDoctorReportDetails.CircuitDefinitionProgram(command,result).Count>0;
            bool inputRun=Str(Get(Map(Get(r,"commandHydration")),"kind"))=="input-run";
            if(!Bool(Get(result,"folder"))&&!inputRun)NativeAnswer(p,result,command,true,definition);
            else p.Key(inputRun?"Input run identity":"Folder run identity",Str(Get(result,"id")??Get(r,"parentSequenceJobId")??Get(r,"sequenceJobId")));
            p.Key("Original native product version",Str(Get(status,"productVersion")));
            p.Key("Recorded UTC", Str(Get(r, "receivedUtc")));
            Dictionary<string,object> completionSource=new Dictionary<string,object>(effective);foreach(string key in new string[]{"completedUtc","commandCompletedUtc","completionScope","executionOutcome","nativeCompletionKnown"})if(r.ContainsKey(key))completionSource[key]=r[key];Dictionary<string,object> completion=SpinDoctorReportDetails.CompletionMetadata(completionSource,result);
            p.Key(definition?"Definition command completion UTC":"Execution completion UTC",Str(Get(completion,"commandCompletedUtc")??Get(completion,"completedUtc")));
            p.Key("Completion scope",Str(Get(completion,"completionScope")));
            if(definition)p.Paragraph("This stamp records completion of the definition, selection or inspection command. It is not a circuit-run completion or a numerical verification stamp.");
            p.Key("Command elapsed (ms)", Str(Get(initial, "elapsedMs")));
            p.Key("Saved procedural descriptor SHA-256", Str(Get(status, "descriptorHash")));
            if(definition)p.Key("Circuit definition descriptor SHA-256",Str(Get(SpinDoctorReportDetails.CircuitDefinitionProgram(command,result),"descriptorHash")));
            p.Key("Field seed", Str(Get(status, "fieldSeed")));
            p.Key("Measurement seed", Str(Get(status, "measurementSeed")));
            p.Key("Record provenance", Str(Get(r,"recordSource"))==""?"Supplied recorded payload; not independently archive-verified by this exporter":Str(Get(r,"recordSource")));
            if(Str(Get(r,"sourceSha256")).Length==64&&!Bool(Get(result,"folder"))&&!inputRun)
            {p.Key("Original input filename",Str(Get(r,"sourceFilename")));p.Key("Original input format",Str(Get(r,"sourceFormat")));p.Key("Original input SHA-256",Str(Get(r,"sourceSha256")));}
            if(Str(Get(r,"nativeArchiveSha256")).Length==64)p.Key("Selected native archive SHA-256",Str(Get(r,"nativeArchiveSha256")));
            if(Str(Get(r,"selectedArchiveStage")).Length>0)p.Key("Selected native archive stage",Str(Get(r,"selectedArchiveStage")));
            p.Key("Response data scope", Bool(Get(initial, "previewOnly")) || Bool(Get(latest, "previewOnly")) ? "BOUNDED PREVIEW; missing arrays are not reconstructed" : "Recorded native payload; chart scope is labeled per figure");
            if (Get(effective, "ok") is bool && !(bool)Get(effective, "ok")) p.Paragraph("COMMAND OR JOB ERROR: " + Str(Get(effective, "error")), true);
            if (result.ContainsKey("phase"))
            {
                p.Key("Job phase", Str(Get(result, "phase")));
                p.Key("Job elapsed (ms)", Str(Get(result, "elapsedMs") ?? Get(result, "totalMs")));
                string phase = Str(Get(result, "phase"));
                if (Bool(Get(result, "active")) || phase == "cancelled" || phase == "failed" || phase == "interrupted" || phase == "queued")
                    p.Paragraph("Partial job snapshot: uncompleted work is not credited as completed or verified.", true);
            }
            Dictionary<string, object> nested = Map(Get(result, "result"));
            if (nested.Count != 0 && (nested.ContainsKey("amplitude") || nested.ContainsKey("spin"))) result = nested;
            if(Bool(Get(result,"folder"))||inputRun)
            {
                FolderRun(p,r,result,status,view,nativeText,programs);
                return;
            }
            Dictionary<string,object> readable=null;
            if(view=="details"||view=="raw")
            {
                readable=new Dictionary<string,object>(effective);readable["result"]=result;readable["status"]=status;
                foreach(string key in new string[]{"recordSource","receivedUtc","completedUtc","commandCompletedUtc","completionScope","executionOutcome","verificationRequired","verificationPhase","receiptReady","verification","verificationError","sourceSha256","nativeArchiveSha256","reportId"})if(r.ContainsKey(key))readable[key]=r[key];
                // The requested numeric answer and its figures precede long explanatory evidence.
                if(view=="raw")Readable(p,SpinDoctorReportDetails.Text(command,readable));
            }
            if(Str(Get(result,"resultKind"))=="empirical-csv-fit")EmpiricalFit(p,result);
            else if(Str(Get(result,"resultKind"))=="csv-data-table")EmpiricalTable(p,result);
            else if(result.ContainsKey("namespace")||command.StartsWith("q.math ",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.query ",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.noise ",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.qec ",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.compile ",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.resource ",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.model ",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.lesson ",StringComparison.OrdinalIgnoreCase)||command.StartsWith("q.algorithm ",StringComparison.OrdinalIgnoreCase)) Calculator(p,result,command);
            else if (result.ContainsKey("amplitudes") || result.ContainsKey("spin")) State(p, result);
            else if (result.ContainsKey("histogram") && result.ContainsKey("requestedShots"))
            {
                bool same = MatchesNativePrintout(result, nativeText);
                if (!same) p.Paragraph("No complete native TXT snapshot matches this recorded result. The histogram below uses this response's own rows and completed-shot count.");
                Circuit(p, result, same ? ParseProgram(nativeText) : RecordedProgram(result, programs), same ? FullHistogram(nativeText) : null);
            }
            else if (result.ContainsKey("zero") && result.ContainsKey("one")) Measurement(p, result);
            else if (result.ContainsKey("plannedSamples") || result.ContainsKey("verifiedCount") || command.StartsWith("q.verify samples", StringComparison.OrdinalIgnoreCase)) Verification(p, result);
            else if (result.ContainsKey("basisIndex") && result.ContainsKey("amplitude"))
            {
                p.Key("Joint basis index", Str(Get(result, "basisIndex")));
                p.Key("Complex amplitude [real, imaginary]", Json.Serialize(Get(result, "amplitude")));
                p.Key("Probability", Str(Get(result, "probability")));
                List<object> amplitude=Items(Get(result,"amplitude"));if(amplitude.Count==2 && IsNumericVector(amplitude))p.ComplexVector(new List<object>{amplitude.ToArray()},"Recorded joint-basis amplitude (local chart row 0)");
                double probability;if(TryNumber(Get(result,"probability"),out probability))p.Gauge("Basis probability / unit probability",probability,1);else p.Key("Basis probability","Not returned; no bar inferred");
            }
            else if (Map(Get(result, "instruction")).Count != 0)
            {
                Dictionary<string, object> program = Map(Get(result, "program"));
                Diagram(p, D("qubits", Get(program,"qubits"), "initialBasis", Get(program,"initialBasis"), "gates", new object[]{Get(result,"instruction")}));
                p.Paragraph("Recorded circuit edit. This diagram describes the appended instruction; editing a program executes no quantum-state calculation.");
            }
            else if (result.ContainsKey("gate"))
            {
                List<object> flat=Items(Get(result,"matrix"));if(flat.Count==8 && IsNumericVector(flat))p.ComplexMatrix(new List<object>{new object[]{new object[]{flat[0],flat[1]},new object[]{flat[2],flat[3]}},new object[]{new object[]{flat[4],flat[5]},new object[]{flat[6],flat[7]}}},"Returned composed single-qubit unitary");
                List<object> targets = Items(Get(result, "qubits"));
                int n = targets.Count == 0 ? 1 : MaxQubit(targets) + 1;
                Dictionary<string, object> gate = new Dictionary<string, object>(result); gate["name"] = Get(result, "gate");
                Dictionary<string, object> program = D("qubits", n, "initialBasis", "current state; no new preparation", "gates", new object[] { gate });
                p.Paragraph(command.StartsWith("q.gate", StringComparison.OrdinalIgnoreCase) ? "Shared unitary descriptor: represents independent family applications; no full-domain enumeration is implied." : "Actual selected-register gate call; controls and targets appear in the gate layer below.");
                Diagram(p, program);
            }
            else if (definition)
            {
                Dictionary<string,object> program=SpinDoctorReportDetails.CircuitDefinitionProgram(command,result);
                if(program.ContainsKey("gates"))Diagram(p,program);
                else p.Paragraph("A full gate diagram is unavailable because this return did not include the stored gates[] array. The declared gateCount is not used to reconstruct missing operands.");
            }
            else if (result.ContainsKey("gates") && result.ContainsKey("qubits")) Diagram(p, result);
            else if (result.ContainsKey("totalCommands") && result.ContainsKey("completedCommands"))
            {
                p.Heading("Sequence progress and execution outcome");
                double completed,total;bool known=!result.ContainsKey("totalCommandsKnown")||Bool(Get(result,"totalCommandsKnown"));
                if(TryNumber(Get(result,"completedCommands"),out completed)&&TryNumber(Get(result,"totalCommands"),out total))p.Gauge(known?"Completed commands / final planned commands":"Completed commands / discovered commands (final total unknown)",completed,total);
                else p.Paragraph("Command progress values were not returned as finite numbers; no progress bar is inferred.",true);
                if(!known)p.Paragraph("Only compiled files contribute to the discovered command total. The final folder total remains unknown until every file has been compiled; this percentage is not overall folder completion.");
                CompactObject(p,result,0,90);
                if(Bool(Get(result,"folder")))FolderDetails(p,result);
                p.Paragraph("Sequence progress counts command completion, not successful quantum gates or verified samples. Individual command snapshots provide their own calculation figures.");
            }
            else
            {
                p.Paragraph("This command did not return a new measurement histogram. Any measurement data in the status snapshot belongs to its recorded earlier run.");
                CompactObject(p, result, 0, 70);
            }
            string error = Str(Get(result, "error") ?? Get(result, "failure"));
            if (error.Length != 0) p.Paragraph("Execution error: " + error, true);
            string checkpoint = Str(Get(result, "checkpointError"));
            if (checkpoint.Length != 0) p.Paragraph("Checkpoint error: " + checkpoint, true);
            if(view=="details"&&readable!=null)Readable(p,ConciseDetail(SpinDoctorReportDetails.Text(command,readable)));
            ReceiptVerification(p,r,result);
            if(view=="details")p.Paragraph("The command result and returned checks are shown above. Complete accepted/final native fields, lifetime counters, hashes and verification rows remain in the matched TXT/JSON or Complete PDF view.");
            Accounting(p, status, view != "raw");
            if (view == "raw")
            {
                {
                    bool preview=Bool(Get(initial,"previewOnly"))||Bool(Get(Map(Get(initial,"result")),"previewOnly"))||Bool(Get(latest,"previewOnly"))||Bool(Get(Map(Get(latest,"result")),"previewOnly"));
                    p.Paragraph("Raw appendices preserve all supplied fields. Non-ASCII JSON characters use reversible Unicode escapes; whitespace wraps for printing. TXT/JSON archives retain the authoritative bytes.");
                    p.Heading(preview?"Complete supplied response preview (full payload not supplied)":"Complete recorded accepted native response");
                    if(preview)p.Paragraph("The supplied record is a bounded preview. This export reproduces that preview exactly and does not claim to include unreturned full native fields. Retrieve its matched native archive for complete TXT/JSON.",true);
                    p.Mono(PrintableJson(initial));
                    if(latest.Count!=0){p.Heading("Complete latest recorded native status / completion response");p.Mono(PrintableJson(latest));}
                }
            }
        }

        private static Dictionary<string,object> RecordedResult(Dictionary<string,object> record)
        {
            Dictionary<string,object> response=Map(Get(record,"finalNativeResponse")??Get(record,"latestStatusResponse"));if(response.Count==0)response=Map(Get(record,"nativeResponse")??Get(record,"response"));
            Dictionary<string,object> result=Map(Get(response,"result")),job=Map(Get(result,"job"));if(job.Count>0)result=job;
            Dictionary<string,object> nested=Map(Get(result,"result"));if(nested.Count>0&&(nested.ContainsKey("amplitude")||nested.ContainsKey("spin")))result=nested;return result;
        }
        private static void NativeAnswer(Report p,Dictionary<string,object> result,string command){NativeAnswer(p,result,command,true);}
        private static void NativeAnswer(Report p,Dictionary<string,object> result,string command,bool explain)
        {NativeAnswer(p,result,command,explain,true);}
        private static void NativeAnswer(Report p,Dictionary<string,object> result,string command,bool explain,bool allowDefinition)
        {
            Dictionary<string,object> definition=allowDefinition?SpinDoctorReportDetails.CircuitDefinitionProgram(command,result):new Dictionary<string,object>();
            if(definition.Count>0){CircuitDefinition(p,result,definition);return;}
            p.Heading("Actual native command answer");int returned=0;
            foreach(string key in SpinDoctorReportDetails.KeyResultFields(command))if(result.ContainsKey(key))
            {string value=Json.Serialize(result[key]);if(value.Length>4096)value=value.Substring(0,4096)+" [bounded numeric preview; complete values in native TXT/JSON]";p.Key(key,value);returned++;}
            if(returned==0){p.Paragraph(command.StartsWith("q.circuit ",StringComparison.OrdinalIgnoreCase)?"Returned circuit operation metadata; a measurement run is shown only when its own native execution result was returned.":"No mapped numerical answer fields were returned for this operation. Its actual native fields below remain separate from the model verification result.");CompactObject(p,result,0,20);}
            else if(explain)p.Paragraph("Values above come from this command's returned result. A returned zero or false is data."+(result.ContainsKey("normalized")?" normalized=false describes the input norm and is not a command failure.":"")+" Model verification below checks a separate procedural snapshot.");
        }
        private static void CircuitDefinition(Report p,Dictionary<string,object> result,Dictionary<string,object> program)
        {
            p.Heading("Circuit defined, not run");
            p.Paragraph("This command returned or edited a selected circuit definition. It did not execute that circuit; any earlier run and its counters remain separate.");
            foreach(string key in new string[]{"name","qubits","initialBasis","gateCount","descriptorHash"})p.Key(key,program.ContainsKey(key)?Str(program[key]):"Not returned");
            p.Paragraph("gateCount is the number of stored program instructions, not executed gates, backend applications or measurement shots. Circuit definition work is not credited as native state execution.");
            p.Key("Next execution command","q.circuit run shots 1024 basis z");
            p.Paragraph("Run this explicitly if you want measured circuit results. The report does not run it. Resource limits and the selected circuit still determine whether execution can proceed.");
            EquationForms(p,SpinDoctorReportEquations.Describe("q.circuit show",D("programSnapshot",program,"programHash",Get(program,"descriptorHash"))));
            if(program.ContainsKey("gates"))
            {
                List<object> gates=Items(program["gates"]);if(gates.Count>8192)throw new ArgumentException("Circuit definition instruction array exceeds 8192; use its original TXT/JSON.");
                if(!(program["gates"] is IEnumerable)||program["gates"] is string||program["gates"] is IDictionary){p.Key("Returned instruction array","Not returned as an array; no planned gate summary inferred");return;}
                p.Keep(100);p.Heading("Planned instructions from the returned definition");p.Key("Returned instruction rows",gates.Count.ToString(Inv));
                if(Str(Get(program,"gateCount"))!=gates.Count.ToString(Inv))p.Paragraph("The returned instruction-array length differs from the native gateCount. The summary covers only returned rows; a complete sequence is not inferred.",true);
                SortedDictionary<string,int> counts=new SortedDictionary<string,int>(StringComparer.Ordinal);
                foreach(object value in gates){Dictionary<string,object> gate=Map(value);string name=Str(Get(gate,"name")??Get(gate,"gate"));if(name.Length==0)name="Name not returned";name=name.ToUpperInvariant();int count;counts.TryGetValue(name,out count);counts[name]=count+1;}
                List<string[]> rows=new List<string[]>();foreach(KeyValuePair<string,int> entry in counts)rows.Add(new string[]{entry.Key,entry.Value.ToString(Inv)});
                p.Table(new string[]{"Stored instruction type","Returned instruction count"},rows,new double[]{335,193});
                p.Paragraph("This is a count of the exact returned gates[] rows. Each stored instruction counts once, including an all-qubit instruction; no future shot replay or backend work is multiplied into it. All returned operands remain in the detailed sequence below and in the exact native archive.");
            }
            else
            {
                p.Key("Full instruction array","Not returned; no complete planned sequence reconstructed");
                Dictionary<string,object> instruction=Map(Get(result,"instruction"));if(instruction.Count>0){p.Key("Returned appended instruction",Json.Serialize(instruction));p.Paragraph("Only this appended instruction was returned. Its operands do not reconstruct the rest of the saved program.");}
            }
        }
        private static string ConciseDetail(string full)
        {
            StringBuilder text=new StringBuilder();foreach(string line in full.Replace("\r","").Split('\n'))
            {if(line.Length==0)continue;string label=line.Split(':')[0];if(label=="READABLE NATIVE REPORT DETAIL"||label=="Documented method for the requested operation"||label=="Native method / convention"||label=="Native echoed operands"||label=="Returned native checks"||label=="Command-local work returned"||label=="Validation boundary"||label=="Elapsed time scope"||label=="Completion meaning"||label=="Complex values"||label.EndsWith(" meaning",StringComparison.Ordinal)||label.StartsWith("Zero ",StringComparison.Ordinal)||label=="Unrepresentable original norm"||label=="Array / coverage scope")text.AppendLine(line);}
            return text.ToString();
        }
        private static void FolderRun(Report p,Dictionary<string,object> record,Dictionary<string,object> result,Dictionary<string,object> status,string view,string nativeText,Dictionary<string,Dictionary<string,object>> programs)
        {
            bool folder=Bool(Get(result,"folder"));string runLabel=folder?"Folder":"Input";
            p.Heading(runLabel+" run: actual archived command answers");
            p.Key("Native execution phase",Str(Get(result,"phase")));bool known=!result.ContainsKey("totalCommandsKnown")||Bool(Get(result,"totalCommandsKnown"));double completed,total;if(TryNumber(Get(result,"completedCommands"),out completed)&&TryNumber(Get(result,"totalCommands"),out total))p.Gauge(known?"Completed commands / final planned commands":"Completed commands / discovered commands (final total unknown)",completed,total);if(!known)p.Paragraph("Only compiled files contribute to the discovered command total; final total unknown until every file is compiled. This is not overall folder completion.");
            foreach(string errorKey in new string[]{"error","failure","checkpointError"})if(Str(Get(result,errorKey)).Length>0)p.Paragraph(errorKey+": "+Str(Get(result,errorKey)),true);
            if(folder)p.Key("Native file completion",Str(Get(result,"completedFiles"))+" / "+Str(Get(result,"totalFiles")));p.Key("Native command completion",Str(Get(result,"completedCommands"))+" / "+Str(Get(result,"totalCommands")));p.Key("Original run completion UTC",Str(Get(result,"completedUtc")));
            if(folder)p.Key("Input folder",Str(Get(result,"folderPath")??Get(result,"path")));
            else{p.Key("Original input filename",Str(Get(record,"sourceFilename")??Get(result,"sourceFilename")));p.Key("Original input format",Str(Get(record,"sourceFormat")??Get(result,"sourceFormat")));p.Key("Original input SHA-256",Str(Get(record,"sourceSha256")??Get(result,"sourceSha256")));}
            List<object> commands=Items(Get(record,"hydratedCommandRecords"));Dictionary<string,object> hydration=Map(Get(record,"commandHydration"));
            if(commands.Count>5000)throw new ArgumentException("A run PDF contains at most5000 hydrated command records; use smaller batches or TXT/JSON.");
            if(hydration.Count>0&&!Bool(Get(hydration,"complete")))
            {
                if(Str(Get(result,"phase"))=="completed"||!Bool(Get(hydration,"partial")))throw new ArgumentException(runLabel+" command hydration is incomplete; complete run PDF refused.");
                p.Paragraph("PARTIAL / DIAGNOSTIC RUN: only actual archived command returns are attached. Planned or unreturned commands are not counted as successful results.",true);foreach(string key in new string[]{"requestedRecords","resolvedRecords","missingCount"})p.Key("Hydration "+key,Str(Get(hydration,key)));foreach(object error in Items(Get(hydration,"errors")))p.Paragraph("Missing command evidence: "+Str(error),true);
            }
            p.Key("Attached archived command results",commands.Count.ToString(Inv));p.Paragraph(Str(Get(record,"hydratedCommandScope")??Get(hydration,"scope")));p.Paragraph(folder?"The answer index uses the original ordered command inputs and returned numbers. Calculator input states and saved-model sampling are separate. Verification is shown after the command answers and figures. Exporting this archived run executes no new native work.":"The answer index uses the original ordered command inputs and returned numbers. Receipt policy below states whether model verification was requested. Exporting this archived run executes no new native work.");
            if(commands.Count==0)p.Paragraph("Command result archives were not attached to this supplied run snapshot. An empty parent records array is not proof of missing calculations. Retrieve the matched native archive; this exporter does not reconstruct unreturned answers.",true);
            int index=0;foreach(object raw in commands)
            {
                Dictionary<string,object> child=Map(raw);p.Keep(110);p.Heading("Answer "+(++index).ToString(Inv)+" / "+commands.Count.ToString(Inv)+" - "+Str(Get(child,"sourceFilename")));p.Key("Exact command / inputs",Str(Get(child,"command")));p.Key("Native completion UTC",Str(Get(child,"commandCompletedUtc")??Get(child,"completedUtc")));NativeAnswer(p,RecordedResult(child),Str(Get(child,"command")),false);
            }
            if(commands.Count>0)
            {
                p.NewPage();p.Heading("Figures for the recorded command answers");p.Paragraph(folder?"Each figure uses that command's original returned values. The model-verification spin arrow appears later and may depict a different state.":"Circuit spheres use native retained coherent output and local reduced states; histograms use completed shots. A vector at the center can legitimately describe a mixed local state. Each figure uses that command's original returned values.");index=0;
                foreach(object raw in commands){Dictionary<string,object> child=Map(raw),value=RecordedResult(child);string command=Str(Get(child,"command"));p.Keep(90);p.Heading("Command figure "+(++index).ToString(Inv)+" / "+commands.Count.ToString(Inv)+" - "+Str(Get(child,"sourceFilename")));p.Key("Exact command",command);
                    if(value.ContainsKey("namespace"))Calculator(p,value,command,false);else if(value.ContainsKey("amplitudes")||value.ContainsKey("spin"))State(p,value);else if(value.ContainsKey("histogram")&&value.ContainsKey("requestedShots"))Circuit(p,value,RecordedProgram(value,programs),null);else if(value.ContainsKey("zero")&&value.ContainsKey("one"))Measurement(p,value);else CompactObject(p,value,0,30);
                }
            }
            p.NewPage();ReceiptVerification(p,record,result);if(folder)FolderVerificationDetails(p,result,commands);Accounting(p,status,view != "raw");
            if(view=="raw")
            {p.Paragraph("Complete appendices retain every supplied parent and attached child native response. Graph grouping affects presentation only; exact individual evidence remains below and in the native TXT/JSON.");p.Heading("Complete archived parent accepted native response");p.Mono(PrintableJson(Get(record,"nativeResponse")));if(record.ContainsKey("finalNativeResponse")){p.Heading("Complete archived parent completion response");p.Mono(PrintableJson(Get(record,"finalNativeResponse")));}
                index=0;foreach(object raw in commands){Dictionary<string,object> child=Map(raw);p.Heading("Complete attached child command "+(++index).ToString(Inv)+": "+Str(Get(child,"command")));p.Mono(PrintableJson(child));}
            }
        }
        private static string DisplayReturned(object value){return value==null?"Not returned":Str(value);}
        private static string VerificationFigureKey(Dictionary<string,object> file)
        {
            if(Str(Get(file,"verificationPhase"))!="passed")return "";Dictionary<string,object> evidence=Map(Get(file,"verification")),response=Map(Get(evidence,"response")),v=Map(Get(response,"result")),status=Map(Get(response,"nativeStatusSnapshot")??Get(response,"status")??Get(evidence,"modelBefore"));if(Map(Get(v,"job")).Count>0)v=Map(Get(v,"job"));
            if(!Bool(Get(v,"passed"))||Str(Get(v,"descriptorHash")).Length!=64||Str(Get(v,"addressDigest")).Length!=64||Items(Get(v,"samplePreview")).Count==0)return "";
            Dictionary<string,object> identity=new Dictionary<string,object>();foreach(string key in new string[]{"descriptorHash","addressDigest","selectionSeed","requestedSamples","plannedSamples","completedSamples","verifiedCount","cpuStates","gpuStates","maxError","maxNormError","maxSpinError","tolerance","samplePreview","sampleAssessment"})if(v.ContainsKey(key))identity[key]=v[key];identity["hasPrepared"]=Get(v,"hasPrepared")??Get(status,"hasPrepared");identity["migrationT"]=Get(v,"migrationT")??Get(status,"migrationT");return Json.Serialize(identity);
        }
        private static void FolderVerificationDetails(Report p,Dictionary<string,object> result,List<object> commands)
        {
            p.Heading("Per-file execution, verification and receipt evidence");List<object> files=Items(Get(result,"files"));List<string[]> rows=new List<string[]>();
            foreach(object raw in files){Dictionary<string,object> f=Map(raw),e=Map(Get(f,"verification")),v=Map(Get(Map(Get(e,"response")),"result"));if(Map(Get(v,"job")).Count>0)v=Map(Get(v,"job"));rows.Add(new string[]{Str(Get(f,"filename")),Str(Get(f,"executionPhase")??Get(f,"phase")),Str(Get(f,"verificationPhase")),DisplayReturned(Get(v,"cpuStates"))+" / "+DisplayReturned(Get(v,"gpuStates")),DisplayReturned(Get(v,"verifiedCount")),DisplayReturned(Get(e,"completedUtc")??Get(v,"completedUtc"))});}
            p.Table(new string[]{"Input file","Execution","Verify","CPU / GPU","Compared","Verification stop UTC"},rows,new double[]{115,62,56,86,70,139});
            p.Paragraph("Every row is a separate recorded file check. Comparison counts sum repeated operations and are not distinct union coverage of the model. Shared verification figures below are presentation only; no file verdict or evidence is merged.");
            Dictionary<string,Dictionary<string,object>> childEvidence=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);foreach(object raw in commands){Dictionary<string,object> child=Map(raw);string id=Str(Get(child,"sequenceJobId"));if(id.Length>0&&Map(Get(child,"verification")).Count>0&&!childEvidence.ContainsKey(id))childEvidence[id]=child;}
            Dictionary<string,int> groups=new Dictionary<string,int>(StringComparer.Ordinal);List<Dictionary<string,object>> representatives=new List<Dictionary<string,object>>();List<int> counts=new List<int>();
            foreach(object raw in files){Dictionary<string,object> f=Map(raw);p.Keep(70);p.Key("Source file",Str(Get(f,"path")??Get(f,"filename")));p.Key("Source SHA-256",Str(Get(f,"sourceSha256")));foreach(string key in new string[]{"childJobId","executionCompletedUtc","reportArchivedUtc","purgeCompletedUtc","reportPhase","purgePhase","error","verificationError","reportError","purgeError"})if(Str(Get(f,key)).Length>0)p.Key(key,Str(Get(f,key)));Dictionary<string,object> e=Map(Get(f,"verification"));p.Key("Verification evidence SHA-256",Str(Get(e,"evidenceSha256")));p.Key("Verification evidence archive",Str(Get(e,"evidenceFile")));
                Dictionary<string,object> representative=f,child;if(childEvidence.TryGetValue(Str(Get(f,"childJobId")),out child))representative=child;string keyForFigure=VerificationFigureKey(representative);int group;if(keyForFigure.Length>0&&groups.TryGetValue(keyForFigure,out group)){counts[group]++;continue;}group=representatives.Count;representatives.Add(representative);counts.Add(1);if(keyForFigure.Length>0)groups[keyForFigure]=group;
            }
            for(int i=0;i<representatives.Count;i++){p.Keep(90);p.Heading("Model verification figures "+(i+1).ToString(Inv)+" / "+representatives.Count.ToString(Inv));p.Key("Representative file",Str(Get(representatives[i],"sourceFilename")??Get(representatives[i],"filename")));p.Key("File checks sharing these figures",counts[i].ToString(Inv));p.Paragraph(counts[i]>1?"These file checks returned identical descriptor, address plan, bounded sample rows, assessment, comparison counts and residuals. One set of figures is printed. Each independent verification completion and receipt archive is listed above; this is not new union coverage.":"Figures use this file's recorded model comparison. No similarity to other checks is assumed.");ReceiptVerification(p,representatives[i],representatives[i]);}
            foreach(object warning in Items(Get(result,"warnings")))p.Paragraph("Folder warning: "+Str(warning),true);
        }

        private static void Readable(Report p,string text)
        {
            p.Heading("Native interpretation and evidence boundaries");
            foreach(string line in text.Replace("\r","").Split('\n'))
            {if(line.Length==0||line=="READABLE NATIVE REPORT DETAIL")continue;int colon=line.IndexOf(": ",StringComparison.Ordinal);if(colon>0)p.Key(line.Substring(0,colon),line.Substring(colon+2));else p.Paragraph(line);}
        }
        private static string PrintableJson(object value){string text=Json.Serialize(value);StringBuilder b=new StringBuilder();foreach(char c in text){if(c<32||c>126)b.Append("\\u").Append(((int)c).ToString("x4",Inv));else b.Append(c);if(b.Length>MaximumPdfTextCharacters)throw new ArgumentException("Printable raw PDF text exceeds 4 MiB; use complete native TXT/JSON.");}return b.ToString();}
        private static void ReceiptVerification(Report p,Dictionary<string,object> record,Dictionary<string,object> result)
        {
            object requiredRaw=Get(record,"verificationRequired")??Get(result,"verificationRequired");Dictionary<string,object> evidence=Map(Get(record,"verification")??Get(result,"verification"));
            if(requiredRaw==null&&evidence.Count==0)return;
            string phase=Str(Get(record,"verificationPhase")??Get(result,"verificationPhase"));bool required=Bool(requiredRaw),ready=Bool(Get(record,"receiptReady")??Get(result,"receiptReady"));
            bool disabled=!required&&(phase=="skipped"||phase=="disabled");p.Heading("Procedural model verification receipt");p.Key("Receipt sampling",required?"REQUIRED":phase=="unverified-import"?"UNVERIFIED IMPORTED SNAPSHOT":phase=="not-requested"?"NATIVE SAMPLE VERIFICATION NOT REQUESTED":disabled?"EXPLICIT UNVERIFIED MODE":"VERIFICATION POLICY NOT RECORDED");p.Key("Verification phase",phase.Length>0?phase:"Not returned");p.Key("Receipt release",ready?(required?"Released by native verification/archive barrier":"Archived native return available; no sample verification inferred"):"WITHHELD / diagnostic snapshot");
            p.Paragraph("The verification materializes sampled current-model predictions (amplitudes, spin and Born probabilities) and checks CPU / available GPU agreement. These are snapshot predictions, not measurement observations. Its PASS applies to this descriptor and sampled addresses; it does not independently certify arbitrary calculator outputs or every model address.");
            if(!required){p.Paragraph(phase=="unverified-import"?"Native archive provenance was not verified. No sample verification PASS is inferred from imported fields.":phase=="not-requested"?"Folder receipt verification is not required for this command; inspect its own returned verification result if present.":disabled?"Procedural sampling was explicitly disabled. This is an unverified receipt, not a verification PASS.":"No explicit sampling policy was recorded for this historical command. Sampling cannot be claimed as enabled or deliberately disabled, and no verification PASS is inferred.",true);return;}
            if(Bool(Get(result,"folder"))&&evidence.Count==0){Dictionary<string,object> summary=SpinDoctorReportDetails.FolderVerificationSummary(result);p.Heading("Recorded per-file verification summary");foreach(KeyValuePair<string,object> field in summary)if(field.Key!="hasTrivialModel")p.Key(field.Key,Str(field.Value));p.Paragraph("Evidence is recorded under files[].verification; the parent verification object is intentionally empty. Aggregate counts are sums of recorded file comparisons, not distinct addresses across the run. Missing counts are not filled from lifetime counters.");if(Bool(Get(summary,"hasTrivialModel")))p.Paragraph("TRIVIAL MODEL WARNING: recorded checks include an unprepared or t=0 model that can be address-constant, including any shared gate. Calculator input states are separate; inspect their own returned Bloch vector.",true);return;}
            if(evidence.Count==0){p.Paragraph("Matching sample verification evidence was not returned. No verification PASS is inferred.",true);return;}
            p.Key("Verification received UTC",Str(Get(evidence,"receivedUtc")));p.Key("Verification completed UTC",Str(Get(evidence,"completedUtc")));
            Dictionary<string,object> response=Map(Get(evidence,"response")),v=Map(Get(response,"result")),status=Map(Get(response,"nativeStatusSnapshot")??Get(response,"status")??Get(evidence,"modelBefore"));if(Map(Get(v,"job")).Count>0)v=Map(Get(v,"job"));if(v.Count==0)v=evidence;
            p.Key("Sampled model prepared",Str(Get(v,"hasPrepared")??Get(status,"hasPrepared")));p.Key("Sampled model migration t",Str(Get(v,"migrationT")??Get(status,"migrationT")));p.Key("Sampled descriptor SHA256",Str(Get(v,"descriptorHash")??Get(status,"descriptorHash")));
            if(Get(status,"hasPrepared") is bool&&!Bool(Get(status,"hasPrepared"))||Get(v,"hasPrepared") is bool&&!Bool(Get(v,"hasPrepared"))||Str(Get(v,"migrationT")??Get(status,"migrationT"))=="0")p.Paragraph("TRIVIAL MODEL WARNING: the model was unprepared or at t=0. Sampled addresses can return the same state. A successful comparison does not establish varied state data or verify independent arithmetic results.",true);
            p.Key("Scoped verification verdict",phase=="passed"&&Get(v,"passed") is bool&&Bool(Get(v,"passed"))?"PASS for the recorded sampled procedural model":phase=="failed"||Get(v,"passed") is bool&&!Bool(Get(v,"passed"))?"FAIL / verified receipt withheld":"NOT ESTABLISHED; no matching successful verification phase");
            Verification(p,v);
        }
        private static void VerificationSampleFigures(Report p,Dictionary<string,object> v)
        {
            List<object> preview=Items(Get(v,"samplePreview")??Get(v,"samplePreviewRows"));if(preview.Count>0)
            {
                SamplePrediction(p,Map(preview[0]));
                List<string[]> rows=new List<string[]>();int n=0;foreach(object raw in preview){if(n++>=32)break;Dictionary<string,object> row=Map(raw);rows.Add(new string[]{Str(Get(row,"address")),SampleValue(row,new string[]{"cpuAmplitudes","cpu","reference"}),SampleValue(row,new string[]{"gpuAmplitudes","gpu","actual"}),SampleValue(row,new string[]{"maxError","error","residual"})});}
                p.Heading("Actual bounded sample data preview");p.Paragraph("CPU/GPU arrays: alpha real/imaginary, beta real/imaginary, spin X/Y/Z, norm squared, predicted P(1), in that order. These are numerical predictions, not observed shots.");p.Table(new string[]{"Address","CPU state components","GPU state components","Residual"},rows,new double[]{100,154,154,120});p.Paragraph("Each CPU/GPU component array contains alpha real/imaginary, beta real/imaginary, spin X/Y/Z, norm squared and probability one, in that order. Only these returned sample rows are printed, up to 32. Aggregate compared-point counts include the full native verification plan. Errors-only persistent logging is separate from this bounded actual-data preview.");
            }
            else p.Paragraph("Actual sample rows were not returned; only aggregate verification evidence is available. No amplitudes are reconstructed.");
        }
        private static void SamplePrediction(Report p,Dictionary<string,object> row)
        {
            List<object> cpu=Items(Get(row,"cpu")??Get(row,"cpuAmplitudes"));if(cpu.Count!=9||!IsNumericVector(cpu))return;
            p.Heading("Materialized model prediction at sampled address "+Str(Get(row,"address")));p.Key("Figure source","Verified model snapshot at sampled address "+Str(Get(row,"address"))+"; separate from calculator input states.");
            p.Paragraph("This figure uses the first actual returned CPU sample. It shows the current descriptor's predicted state, which the verifier compared with the GPU result where available. It is not a measurement observation or whole-domain verification.");
            double ar=Number(cpu[0]),ai=Number(cpu[1]),p0=ar*ar+ai*ai;
            p.Key("Predicted P(0)",p0.ToString("R",Inv)+" (squared magnitude of returned alpha)");p.Key("Predicted P(1)",Str(cpu[8])+" (native Born probability)");
            State(p,D("address",Get(row,"address"),"amplitudes",D("alpha",new object[]{cpu[0],cpu[1]},"beta",new object[]{cpu[2],cpu[3]}),"spin",new object[]{cpu[4],cpu[5],cpu[6]},"normSquared",cpu[7],"p1",cpu[8]),"Sampled procedural model prediction at native address "+Str(Get(row,"address"))+"; separate from calculator input states");
            List<object> gpu=Items(Get(row,"gpu")??Get(row,"gpuAmplitudes"));if(gpu.Count==9&&IsNumericVector(gpu)){double gar=Number(gpu[0]),gai=Number(gpu[1]);p.Metrics(D("CPU P(0)",p0,"GPU P(0)",gar*gar+gai*gai,"CPU P(1)",cpu[8],"GPU P(1)",gpu[8]),new string[]{"CPU P(0)","GPU P(0)","CPU P(1)","GPU P(1)"},"CPU / GPU predicted Born probabilities","Compared predicted probabilities at this sampled address; no observed shots or measurement outcomes are inferred");}
        }
        private static string SampleValue(Dictionary<string,object> row,string[] keys){foreach(string key in keys)if(row.ContainsKey(key))return row[key] is IDictionary||row[key] is IEnumerable&&!(row[key] is string)?Json.Serialize(row[key]):Str(row[key]);return "Not returned";}
        private static void FolderDetails(Report p,Dictionary<string,object> result)
        {
            p.Heading("Folder execution, verification, archive and cleanup outcomes");
            p.Key("Folder completion UTC",Str(Get(result,"completedUtc")));p.Key("Completed files / planned",Str(Get(result,"completedFiles"))+" / "+Str(Get(result,"totalFiles")));
            p.Paragraph("Input command completion, procedural sample verification, receipt archive creation and transient cleanup are separate events. Empty parent command arrays refer to archived child results; they do not mean no numerical data was returned.");
            List<string[]> rows=new List<string[]>();foreach(object raw in Items(Get(result,"files"))){Dictionary<string,object> f=Map(raw);rows.Add(new string[]{Str(Get(f,"filename")??Get(f,"path")),Str(Get(f,"executionPhase")??Get(f,"phase")),Str(Get(f,"verificationPhase")),Str(Get(f,"reportPhase")),Str(Get(f,"purgePhase")),Str(Get(f,"completedCommands"))+" / "+Str(Get(f,"totalCommands"))});}
            p.Table(new string[]{"Input file","Execution","Verification","TXT archive","Cleanup","Commands"},rows,new double[]{143,75,85,75,75,75});
            foreach(object raw in Items(Get(result,"files"))){Dictionary<string,object> f=Map(raw);p.Key("Source file",Str(Get(f,"path")));p.Key("Source SHA-256",Str(Get(f,"sourceSha256")));ReceiptVerification(p,f,f);foreach(string key in new string[]{"childJobId","executionCompletedUtc","reportArchivedUtc","purgeCompletedUtc","error","verificationError","reportError","purgeError"})if(Str(Get(f,key)).Length>0)p.Key(key,Str(Get(f,key)));}
            List<object> warnings=Items(Get(result,"warnings"));foreach(object warning in warnings)p.Paragraph("Folder warning: "+Str(warning),true);
            p.Paragraph("Execution completion, archive creation and transient-buffer cleanup are separate outcomes. Archive/cleanup warnings do not rewrite a completed native calculation; completed command archives retain their own evidence.");
        }
        private static Dictionary<string, Dictionary<string, object>> ProgramIndex(List<object> records)
        {
            Dictionary<string, Dictionary<string, object>> result = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            foreach(object raw in records)
            {
                Dictionary<string, object> record=Map(raw);
                foreach(string field in new string[]{"nativeResponse","finalNativeResponse","latestStatusResponse","response"})
                {
                    Dictionary<string, object> response=Map(Get(record,field)), value=Map(Get(response,"result"));
                    AddProgram(result,value); AddProgram(result,Map(Get(value,"programSnapshot"))); AddProgram(result,Map(Get(value,"program")));
                    AddProgram(result,Map(Get(Map(Get(value,"job")),"programSnapshot")));
                    AddProgram(result,Map(Get(Map(Get(Map(Get(response,"status")),"circuit")),"programSnapshot")));
                }
                AddProgram(result,Map(Get(record,"programSnapshot")));
                foreach(KeyValuePair<string,Dictionary<string,object>> program in ProgramIndex(Items(Get(record,"hydratedCommandRecords"))))AddProgram(result,program.Value);
            }
            return result;
        }
        private static void AddProgram(Dictionary<string, Dictionary<string, object>> index, Dictionary<string, object> program)
        {
            string hash=Str(Get(program,"descriptorHash") ?? Get(program,"programHash"));
            if(hash.Length!=64 || !program.ContainsKey("gates") || !program.ContainsKey("qubits"))return;
            Dictionary<string, object> prior;if(index.TryGetValue(hash,out prior) && (Str(Get(prior,"qubits"))!=Str(Get(program,"qubits")) || Json.Serialize(Get(prior,"gates"))!=Json.Serialize(Get(program,"gates"))))
                throw new ArgumentException("Conflicting recorded circuit descriptors share a hash; export refused.");
            index[hash]=program;
        }
        private static Dictionary<string, object> RecordedProgram(Dictionary<string, object> result, Dictionary<string, Dictionary<string, object>> programs)
        {
            Dictionary<string, object> snapshot=Map(Get(result,"programSnapshot"));
            string hash=Str(Get(result,"programHash")), recorded=Str(Get(snapshot,"descriptorHash") ?? Get(snapshot,"programHash"));
            if(snapshot.ContainsKey("gates") && hash.Length==64 && recorded==hash)return snapshot;
            Dictionary<string, object> program;if(hash.Length==64 && programs.TryGetValue(hash,out program))return program;
            return new Dictionary<string,object>();
        }
        private static void Calculator(Report p,Dictionary<string,object> result,string command){Calculator(p,result,command,true);}
        private static void Calculator(Report p,Dictionary<string,object> result,string command,bool showFieldGuide)
        {
            p.Heading("Calculation, model and backend");p.Key("Documented operation method",SpinDoctorReportDetails.OperationMethod(command));foreach(string key in new string[]{"namespace","action","backend","resultKind","scope","note","measurementSeedChanged","physicalQuantumOperations","proceduralFamilyGateCredit"})if(result.ContainsKey(key))p.Key(key,Str(result[key]));
            p.Metrics(result,new string[]{"normSquared","norm","purity","fidelity","stateFidelity","concurrence","negativity","traceDistance","expectation","sum","lengthSquared"},"Returned dimensionless calculation values","Dimensionless values; field names identify different quantities. Linear signed axis, scaled to the maximum absolute returned value. These are calculated values, not sampled counts.");
            p.Metrics(result,new string[]{"entropy","entropyBits"},"Returned spectral entropy","Entropy units follow the returned logarithm base; entropyBits is in bits. A zero bar is an actual returned zero, not missing entropy.");
            p.Metrics(result,new string[]{"unitarityError","maximumElementError","maxError","maxNormError","maxSpinError","hermitianError","imaginaryResidual","residualSumSquares"},"Returned native numerical residuals","Each named residual is plotted in its own returned units. Zero means the returned residual is exactly zero at native precision; no missing check is drawn as zero. This figure does not perform an independent verification.");
            if(Str(Get(result,"resultKind"))=="resource-estimate")
            {p.Metrics(result,new string[]{"bytes","requiredPeakBytes","usablePeakBytes","availablePayloadBytes"},"Returned resource memory plan (bytes)","Linear byte axis. Bars show formulas/plans rather than allocations; OS headroom is momentary. Exact integer byte strings are retained alongside the figures and in TXT/JSON.");p.Metrics(result,new string[]{"standardError"},"Returned Bernoulli standard-error estimate","Dimensionless standard error under the returned independent-sample assumption. No random shots were drawn by this estimate.");}
            if(Str(Get(result,"namespace"))=="q.math"&&Str(Get(result,"action"))=="bloch"){p.Keep(520);p.Heading("Calculator input state: returned Bloch vector");p.Key("Figure source",command);p.Paragraph("This spin arrow uses the calculator's returned Bloch X/Y/Z for the state supplied in this command. q.math does not modify the saved procedural model. Its separate verified-model arrow may point in a different direction.");}
            if(Str(Get(result,"namespace"))=="q.query"&&result.ContainsKey("bloch")){p.Keep(590);p.Heading("Selected register: returned local Bloch vector");SpinSource(p,result,"Actual retained register query");}
            int figures=0; Visuals(p,result,"calculator result",0,ref figures); bool visual=figures>0;
            List<object> overlap=Items(Get(result,"overlap"));if(overlap.Count==2&&IsNumericVector(overlap)){p.ComplexVector(new List<object>{overlap.ToArray()},"Returned complex overlap (one scalar)",false);p.Paragraph("The plotted component is <a|b>; its squared magnitude is pure-state fidelity. [0,0] has undefined phase and resolves no overlap at returned precision; no exact mathematical zero is certified.");visual=true;}
            foreach(string key in new string[]{"purity","entropy","entropyBits","fidelity","concurrence","negativity","maxError","unitarityError","traceDistance"})if(result.ContainsKey(key))p.Key(key,Str(result[key]));
            if(!visual)p.Paragraph("This return has no state, density matrix, spectrum or sampled histogram to plot. The returned theory/resource values below are not measurements.");
            if(showFieldGuide){p.Heading("Returned values and exact accounting");CompactObject(p,result,0,160);}p.Paragraph("Figures show only the bounded values actually returned. Omitted/prefix states are not inferred as calculated. Exact native arrays are retained in TXT/JSON.");
        }
        private static void SpinSource(Report p,Dictionary<string,object> value,string fallback)
        {
            p.Key("Spin figure source",value.ContainsKey("stateSource")?Str(Get(value,"stateSource")):fallback);
            foreach(string key in new string[]{"qubit","premeasurement","stateCharacter","purity","blochLengthSquared","pure","mixed","exactlyZeroBloch","directionDefined","directionResolvedAtTolerance","directionTolerance","spinScope"})if(value.ContainsKey(key))p.Key(key,Str(Get(value,key)));
            p.Paragraph("This is a retained coherent-state prediction, separate from observed shot counts and the saved procedural model checked by q.verify. A zero local vector has no preferred direction; |0> is the north-pole basis label, not a zero vector.");
        }
        private static void CircuitSpinPreview(Report p,Dictionary<string,object> result)
        {
            Dictionary<string,object> preview=Map(Get(result,"coherentSpinPreview"));
            if(preview.Count==0){p.Paragraph("No retained premeasurement local-spin snapshot is attached to this circuit return. A direction cannot be inferred from its histogram or a later circuit status.");return;}
            if(Str(Get(preview,"programHash")).Length!=64 || Str(Get(preview,"programHash"))!=Str(Get(result,"programHash")))throw new ArgumentException("Retained circuit spin snapshot does not match the result program hash.");
            if(Str(Get(preview,"schema"))!="SPINDOCTOR/PREMEASUREMENT-SPIN-PREVIEW/1" || !Bool(Get(preview,"premeasurement")))throw new ArgumentException("Retained circuit spin snapshot lacks its native premeasurement schema.");
            if(!(Get(preview,"independentVerification") is bool) || Bool(Get(preview,"independentVerification")))throw new ArgumentException("Retained circuit spin snapshot must explicitly disclaim independent verification.");
            p.Heading("Retained circuit state before measurement");
            foreach(string key in new string[]{"programHash","capturedUtc","premeasurement","selectedQubits","returnedQubits","omittedQubits","unrequestedQubits","qubitLimit","maximumFactorEntries","scope","verificationScope"})if(preview.ContainsKey(key))p.Key(key,Str(Get(preview,key)));
            p.Paragraph("These local Pauli expectations were captured from this run's actual coherent output before shot measurements. They are not reconstructed from the histogram, do not describe a later edited circuit, and are separate from procedural q.verify sampling.");
            List<object> rows=Items(Get(preview,"rows"));if(rows.Count>4)throw new ArgumentException("Retained circuit spin preview exceeds its four-qubit bound.");
            HashSet<int> qubits=new HashSet<int>();double width;bool validWidth=TryNumber(Get(result,"qubits"),out width)&&width>=1&&width<=29&&width==Math.Floor(width);
            foreach(object raw in rows){Dictionary<string,object> row=Map(raw);double q;if(!validWidth||!TryNumber(Get(row,"qubit"),out q)||q!=Math.Floor(q)||q<0||q>=width||!qubits.Add((int)q)||!Bool(Get(row,"premeasurement")))throw new ArgumentException("Retained circuit spin rows require unique in-range premeasurement qubit identities.");List<object> vector=Items(Get(row,"bloch"));if(vector.Count!=3||!IsNumericVector(vector))throw new ArgumentException("Retained circuit spin row lacks a finite native Bloch vector.");p.Keep(590);p.Heading("Circuit local spin / qubit "+Str(Get(row,"qubit")));SpinSource(p,row,"Actual premeasurement circuit marginal");p.Spin(Number(vector[0]),Number(vector[1]),Number(vector[2]));}
            foreach(object raw in Items(Get(preview,"omissions")))p.Key("Unplotted qubit / reason",Json.Serialize(raw));
            if(Items(Get(preview,"omissions")).Count>0)p.Paragraph("An omitted qubit is missing evidence, not a zero spin vector. Its state is not invented by the report.",true);
        }
        private static void Visuals(Report p,Dictionary<string,object> value,string path,int depth,ref int figures)
        {
            if(depth>3 || figures>=64)return;
            foreach(KeyValuePair<string,object> field in value)
            {
                string key=field.Key,label=path+"."+key;List<object> a=Items(field.Value); bool rendered=false;
                if((key=="bloch"||key=="spin") && a.Count==3&&IsNumericVector(a))
                {p.Keep(340);p.Heading(label+" / returned single-qubit spin");p.Spin(Number(a[0]),Number(a[1]),Number(a[2]));p.Paragraph("Unit sphere: Cartesian Pauli expectations. Arrow length uses the recorded vector; a mixed-state vector may end inside the sphere. The figure is an orthographic projection, not a new state calculation.");rendered=true;}
                else if((key=="bloch"||key=="spin")&&IsNumericVector(new List<object>{Get(Map(field.Value),"x"),Get(Map(field.Value),"y"),Get(Map(field.Value),"z")}))
                {Dictionary<string,object> spin=Map(field.Value);p.Keep(340);p.Heading(label+" / returned single-qubit spin");p.Spin(Number(Get(spin,"x")),Number(Get(spin,"y")),Number(Get(spin,"z")));rendered=true;}
                else if(IsVectorName(key) && a.Count>0 && IsComplexVector(a))
                {p.ComplexVector(a,label);rendered=true;}
                else if(IsMatrixName(key) && a.Count>0 && IsComplexMatrix(a))
                {p.ComplexMatrix(a,label);rendered=true;}
                else if((key.IndexOf("probabilit",StringComparison.OrdinalIgnoreCase)>=0 || key=="eigenvalues"||key=="spectrum"||key=="populations"||key=="values"||key=="observed"||key=="corrected"||key=="scales"||key=="expectations") && a.Count>0 && IsNumericVector(a))
                {p.Values(a,label);rendered=true;}
                else if(key=="rows" && a.Count>0 && Map(a[0]).ContainsKey("basisIndex"))
                {p.BasisRows(a,label);foreach(string scope in new string[]{"start","returnedStates","totalStates","complete","omittedStates","returnedProbabilityMass","renormalized"})if(value.ContainsKey(scope))p.Key(scope,Str(value[scope]));rendered=true;}
                else if(key=="rows" && a.Count>0 && Map(a[0]).Count>0)
                {p.ResultRows(a,label);rendered=true;}
                else if(key=="gates" && value.ContainsKey("qubits"))
                {Diagram(p,value);rendered=true;}
                if(rendered){figures++;continue;}
                Dictionary<string,object> nested=Map(field.Value);
                if(nested.Count>0)Visuals(p,nested,label,depth+1,ref figures);
                else if(a.Count>0 && a.Count<=32 && Map(a[0]).Count>0)
                    for(int i=0;i<a.Count && figures<64;i++)Visuals(p,Map(a[i]),label+"["+i.ToString(Inv)+"]",depth+1,ref figures);
                else if((IsVectorName(key)||IsMatrixName(key)) && a.Count>0)
                    p.Paragraph(label+": no figure generated because returned data is not a complete finite numerical array.",true);
            }
        }
        private static bool IsVectorName(string key){return key.IndexOf("amplitude",StringComparison.OrdinalIgnoreCase)>=0||key=="state"||key=="encoded"||key=="recovered"||key=="inputState"||key=="outputState";}
        private static bool IsMatrixName(string key){return key.IndexOf("matrix",StringComparison.OrdinalIgnoreCase)>=0||key=="rho"||key=="density"||key=="reducedDensity"||key=="outputDensity"||key=="unitary"||key=="commutator"||key=="hamiltonian"||key=="operator"||key=="confusion";}
        private static bool IsNumericVector(List<object> a){foreach(object item in a){double v;if(!TryNumber(item,out v))return false;}return true;}
        private static bool IsComplexVector(List<object> a){foreach(object item in a){List<object> pair=Items(item);if(pair.Count!=2||!IsNumericVector(pair))return false;}return true;}
        private static bool IsComplexMatrix(List<object> a){int cols=Items(a[0]).Count;if(cols<1)return false;foreach(object raw in a){List<object> row=Items(raw);if(row.Count!=cols||!IsComplexVector(row))return false;}return true;}

        private static void State(Report p, Dictionary<string, object> result){State(p,result,"Returned native state at address "+Str(Get(result,"address")));}
        private static void State(Report p, Dictionary<string, object> result,string figureSource)
        {
            p.Keep(340);
            p.Heading("State and spin");
            if (result.ContainsKey("address")) p.Key("Address", Str(Get(result, "address")));
            if (result.ContainsKey("qubit")) p.Key("Qubit", Str(Get(result, "qubit")));
            Dictionary<string, object> amplitudes = Map(Get(result, "amplitudes"));
            if (amplitudes.Count != 0)
            {
                p.Key("Alpha amplitude", Json.Serialize(Get(amplitudes, "alpha")));
                p.Key("Beta amplitude", Json.Serialize(Get(amplitudes, "beta")));
                List<object> a=Items(Get(amplitudes,"alpha")),b=Items(Get(amplitudes,"beta"));
                if(a.Count==2 && b.Count==2 && IsNumericVector(a) && IsNumericVector(b))p.ComplexVector(new List<object>{a.ToArray(),b.ToArray()},"Single-qubit amplitudes");
            }
            object spinRaw = Get(result, "spin"); Dictionary<string, object> spin = Map(spinRaw); List<object> values = Items(spinRaw);
            if(values.Count==3||spin.ContainsKey("x")&&spin.ContainsKey("y")&&spin.ContainsKey("z")){
            double x = values.Count == 3 ? Number(values[0]) : Number(Get(spin, "x")), y = values.Count == 3 ? Number(values[1]) : Number(Get(spin, "y")), z = values.Count == 3 ? Number(values[2]) : Number(Get(spin, "z"));
            p.Keep(340);p.Key("Spin figure source",figureSource);p.Spin(x, y, z);
            p.Key("X / Y / Z", x.ToString("R", Inv) + " / " + y.ToString("R", Inv) + " / " + z.ToString("R", Inv));}
            else p.Paragraph("Spin expectations were not returned; no Bloch vector is inferred.",true);
            p.Paragraph("Bloch projection and component bars use the returned spin expectations. Reduced spins of entangled qubits may lie inside the sphere; they are not automatically pure states.");
            foreach (string key in new string[] { "norm", "normSquared", "probabilityOne", "p1", "theta", "phi", "scope" }) if (result.ContainsKey(key)) p.Key(key, Str(Get(result, key)));
        }
        private static void Measurement(Report p, Dictionary<string, object> result)
        {
            p.Heading("Measurement outcomes");
            foreach (string key in new string[] { "address", "qubit", "basis", "shots", "probabilityOne", "meanSpin", "seed", "counterStart", "counterEnd", "collapsed", "collapseOutcome" }) if (result.ContainsKey(key)) p.Key(key, Str(Get(result, key)));
            List<Outcome> rows = new List<Outcome> { new Outcome("0", Str(Get(result, "zero"))), new Outcome("1", Str(Get(result, "one"))) };
            p.Histogram(rows, "Actually returned outcome counts");
            p.Table(new string[] { "Outcome", "Returned count" }, ToRows(rows), new double[] { 240, 288 });
            p.Paragraph("Seeded classical Born-rule samples. These counts describe simulated measurements; exporting this report does not sample again.");
        }
        private static void Verification(Report p, Dictionary<string, object> result)
        {
            p.Heading("Verification coverage");
            foreach (string key in new string[] { "requestedSamples", "plannedSamples", "completedSamples", "verifiedCount", "cpuStates", "gpuStates", "errorCount", "passed", "maxError", "maxNormError", "maxSpinError", "tolerance", "boundarySamples", "hasPrepared", "migrationT", "receivedUtc", "completedUtc", "elapsedMs", "totalMs", "descriptorHash", "selectionSeed", "addressDigest", "verificationMethod", "verificationPurpose", "predictionMeaning", "scope" }) if (result.ContainsKey(key)) p.Key(key, Str(Get(result, key)));
            object planned = Get(result, "plannedSamples") ?? Get(result, "sampleCount");
            double plan;if(TryNumber(planned,out plan) && plan>0){
            if(result.ContainsKey("cpuStates"))p.Gauge("CPU states / planned", Number(Get(result, "cpuStates")), plan);
            if(result.ContainsKey("gpuStates"))p.Gauge("GPU states / planned", Number(Get(result, "gpuStates")), plan);
            object compared=Get(result,"verifiedCount") ?? Get(result,"completedSamples");if(compared!=null)p.Gauge("Compared points / planned", Number(compared), plan);}
            else p.Paragraph("No finite sample plan was returned; a coverage percentage cannot be inferred.");
            p.Paragraph("These gauges measure completion of the returned sampling plan, not a percentage of the whole model. Unsampled model addresses are not individually verified. Rule audits, source calculations and compared samples remain distinct.");
            BigInteger checkedAddresses,domain;if(BigInteger.TryParse(Str(Get(result,"verifiedCount")),NumberStyles.None,Inv,out checkedAddresses)&&BigInteger.TryParse(Str(Get(result,"stateCount")),NumberStyles.None,Inv,out domain)&&checkedAddresses>=0&&domain>0)p.Key("Actual checked addresses / model domain",checkedAddresses.ToString(Inv)+" / "+domain.ToString(Inv)+" (exact fraction for this native job only; no omitted points claimed)");else p.Key("Whole-model coverage","Domain or checked-address count was not returned; no percentage inferred.");
            if(result.ContainsKey("boundarySamples"))p.Paragraph("The native plan includes the returned deterministic boundary samples in addition to the requested random sample budget, after duplicate addresses and finite-domain clamping. Repeating the same descriptor, selection seed and request can repeat the same addresses; comparison totals are not new union coverage.");
            Dictionary<string,object> assessment=Map(Get(result,"sampleAssessment"));if(assessment.Count>0){p.Heading("Assessment of the actual captured preview");foreach(string key in new string[]{"capturedPreviewRows","finiteCpuPreviewRows","finiteGpuPreviewRows","distinctCpuPreviewVectors","distinctGpuPreviewVectors","cpuPreviewConstant","gpuPreviewConstant","modelAddressDependence","modelInterpretation","scope","countMeaning"})if(assessment.ContainsKey(key))p.Key(key,Str(assessment[key]));p.Paragraph("This assessment describes the original captured preview, which can contain up to32 rows. A parent metadata preview may display only four of those rows. Neither view represents omitted states or additional measurement shots.");}
            p.Metrics(result,new string[]{"maxError","maxNormError","maxSpinError","tolerance"},"CPU / GPU numerical residual and tolerance","Returned dimensionless numerical errors and tolerance; linear relative scale. Zero bars mean returned zero, not missing values.");
            VerificationSampleFigures(p,result);
        }
        private static void Circuit(Report p, Dictionary<string, object> result, Dictionary<string, object> program, List<Outcome> full)
        {
            foreach (string key in new string[] { "programName", "programHash", "qubits", "initialBasis", "basis", "phase", "requestedShots", "completedShots", "measuredQubits", "elapsedMs", "seed", "counterStart", "counterEnd", "error", "checkpointError" }) if (result.ContainsKey(key)) p.Key(key, Str(Get(result, key)));
            if (Bool(Get(result, "active")) || Str(Get(result, "phase")) != "completed") p.Paragraph("This result is partial, failed, cancelled or still running. Only completed joint shots may appear in its histogram; partial-shot work is accounted separately.", true);
            if(Str(Get(result,"error")).Length>0)p.Paragraph("CIRCUIT EXECUTION ERROR: "+Str(Get(result,"error")),true);
            if (program.Count != 0) Diagram(p, program);
            else p.Paragraph("The exact circuit descriptor was not retained with this historical return. A circuit diagram cannot be reconstructed from a name or gate count. Export the matched native archive where available.",true);
            if(program.Count>0)EquationForms(p,SpinDoctorReportEquations.Describe("q.circuit run",result));
            List<Outcome> rows = full ?? HistogramRows(Get(result, "histogram"));
            if(rows.Count>65536)throw new ArgumentException("PDF histogram appendix supports<=65536 distinct rows; use TXT/JSON for larger distributions.");
            bool complete=full!=null || Bool(Get(result,"histogramComplete"));BigInteger total=BigInteger.Zero;HashSet<string> labels=new HashSet<string>(StringComparer.Ordinal);
            int qubits=(int)Number(Get(result,"qubits"));if(qubits<1||qubits>29)throw new ArgumentException("Recorded circuit qubit width must be1..29.");
            foreach(Outcome row in rows){if(row.Label.Length!=qubits || !labels.Add(row.Label))throw new ArgumentException("Histogram bitstrings must have the recorded qubit width and be unique.");foreach(char bit in row.Label)if(bit!='0'&&bit!='1')throw new ArgumentException("Histogram labels must be binary bitstrings.");total+=DecimalCount(row.Count,"Histogram count");}
            BigInteger shots=DecimalCount(Str(Get(result,"completedShots")),"Circuit completed-shot count");
            BigInteger requested=DecimalCount(Str(Get(result,"requestedShots")),"Circuit requested-shot count");if(shots>requested)throw new ArgumentException("Completed-shot count exceeds the recorded nonnegative requested-shot count.");
            if(total>shots || complete && total!=shots)throw new ArgumentException("Histogram rows do not match the recorded completed-shot count; PDF refused instead of publishing inconsistent figures.");
            p.Key("Histogram scope",complete?"Complete recorded outcome distribution":"Recorded preview; omitted outcomes remain unplotted");
            p.Key("Returned histogram shot mass",total.ToString(Inv)+" / "+shots.ToString(Inv));
            if (!complete) p.Paragraph("This historical JSON histogram is a preview with " + Str(Get(result, "omittedOutcomes")) + " omitted rows. Full native circuit TXT/PDF is available for the latest matching run. This report does not invent omitted outcomes.", true);
            p.Gauge("Completed joint shots / requested",Number(Get(result,"completedShots")),Number(Get(result,"requestedShots")));
            p.Histogram(rows, "Observed joint outcomes - most frequent 24 shown");
            p.Heading("Circuit work accounting");
            p.Paragraph("Job-local actual work for this recorded circuit run, identified by its native job id and programHash. These counts remain separate from the saved circuit lifetime totals and from the procedural model's CPU/GPU verification counters.");
            Dictionary<string, object> counts = Map(Get(result, "counts"));
            foreach (KeyValuePair<string, object> count in counts) p.Key(count.Key, Str(count.Value));
            p.Paragraph("Actual work includes independent shot replays and the separately retained coherent-output execution. Exact classical-input controlled-phase reductions are recorded. These units are not physical quantum gate throughput or FLOPS.");
            CircuitSpinPreview(p,result);
            p.Heading(full == null ? "Returned histogram rows" : "Complete native histogram");
            p.Key("Histogram rows included", rows.Count.ToString(Inv));
            p.Table(new string[] { "Bitstring (qubit 0 at right)", "Completed joint shots" }, ToRows(rows), new double[] { 330, 198 });
        }
        private static void Diagram(Report p, Dictionary<string, object> program)
        {
            int n = (int)Double(Get(program, "qubits")); if (n < 1 || n > 29) return;
            List<object> gates = Items(Get(program, "gates"));
            p.Keep(130 + n * (n > 16 ? 12 : n > 8 ? 17 : 26));
            p.Heading("Circuit gate sequence"); p.Key("Qubits / input basis", n.ToString(Inv) + " / " + Str(Get(program, "initialBasis")));
            int at = 0;
            do
            {
                int count = Math.Min(14, gates.Count - at); p.Diagram(n, gates, at, count); at += count;
                if (at < gates.Count) p.NewPage();
            } while (at < gates.Count);
            p.Paragraph("Wires are indexed q0 through q" + (n - 1).ToString(Inv) + ". Controls are dots; targets are labeled boxes. Gate angles below each layer are in radians. Diagram layers follow program order.");
            List<string[]> exact = new List<string[]>(); int index = 0;
            foreach (object raw in gates)
            {
                Dictionary<string, object> gate = Map(raw);
                exact.Add(new string[] { (++index).ToString(Inv) + ". " + Str(Get(gate, "name") ?? Get(gate, "gate")), Bool(Get(gate, "all")) ? "all" : Json.Serialize(Get(gate, "qubits")), Json.Serialize(Get(gate, "anglesExact") ?? Get(gate, "angles")) });
            }
            p.Keep(90);p.Heading("Exact gate operands"); p.Table(new string[] { "Layer / gate", "Qubit indices", "Angles (radians)" }, exact, new double[] { 120, 125, 283 });
        }
        private static void Accounting(Report p, Dictionary<string, object> status, bool compactInactive)
        {
            if (status.Count == 0) return;
            p.Keep(85);
            p.Heading("Recorded accounting");
            p.Paragraph("Scope: saved native status accompanying this recorded return. These values are not this command's work delta and are not guaranteed to describe the current live session. An acceptance snapshot can precede later job or per-file verification evidence; those results retain their own job-local counts.");
            p.Paragraph("A recorded 0 is a genuine zero in this snapshot. Not returned means the field was absent; missing work is never replaced with 0. Defining a circuit does not add execution counts. Represented credits, actual procedural calculations, circuit execution and selected-register work are separate quantities, not FLOPS or physical quantum throughput.");
            if(compactInactive)p.Paragraph("Inactive counter groups are compacted below when every returned value in that scope is exactly 0 or false. Mathematical results, nonzero values and unknown fields are not changed. Complete PDF, TXT and JSON retain the exact individual counter rows.");
            AccountingGroup(p,"Procedural domain (descriptor, not allocation)",status,new string[][]{
                new string[]{"gridM","Address-grid width in the saved procedural descriptor."},
                new string[]{"gridL","Address-grid height in the saved procedural descriptor."},
                new string[]{"stateCount","Number of addressable single-qubit family states; not allocated amplitudes, calculated states or jointly entangled qubits."},
                new string[]{"hasPrepared","Whether procedural preparation is committed in this saved model; independent of a circuit definition."},
                new string[]{"migrationT","Saved procedural preparation parameter; not the circuit's input state or run time."}},false,false);
            AccountingGroup(p,"Represented procedural credits",status,new string[][]{
                new string[]{"representedPreparations","Current-generation descriptor preparation credit; not individually materialized states."},
                new string[]{"representedPreparationRotations","Reported rotation-equivalent descriptor credit; not executed backend instructions."},
                new string[]{"representedGateApplications","Current-generation shared-gate descriptor credit; no full-domain execution inferred."},
                new string[]{"lifetimeRepresentedPreparations","Saved lifetime descriptor preparation credit across model generations."},
                new string[]{"lifetimeRepresentedGateApplications","Saved lifetime descriptor gate credit; separate from actual numerical work."}},false,compactInactive);
            AccountingGroup(p,"Procedural model lifetime actual work",status,new string[][]{
                new string[]{"actualCpuStates","Procedural states actually materialized on CPU, including state reads, measurement preparation and verification batches; not circuit or q.math work."},
                new string[]{"actualGpuStates","Procedural states actually calculated by the OpenCL GPU verifier; not circuit gates or arbitrary calculator arithmetic."},
                new string[]{"verifiedSamples","Completed procedural CPU/GPU sample comparisons across jobs; repeated addresses can be counted again, not a unique-address union."},
                new string[]{"measurementShots","Seeded single-qubit procedural measurement draws; separate from circuit joint shots and deterministic probabilities."},
                new string[]{"ruleChecks","Recorded procedural rule/identity checks; not individually sampled family states."},
                new string[]{"errorCount","Saved procedural validation failures; not a universal count of all engine or orchestration errors."},
                new string[]{"quantumHardwareGates","Physical quantum hardware operations recorded; classical simulation records 0."}},false,compactInactive);
            Dictionary<string,object> tranche=Map(Get(status,"tranche")),circuit=Map(Get(status,"circuit"));
            AccountingGroup(p,"Selected tranche snapshot",tranche,new string[][]{
                new string[]{"prepared","Whether a selected tranche definition exists in this saved status; inspect suspended/checkpoint fields for live-state availability."},
                new string[]{"qubits","Width of the selected coherent tranche, independent of the procedural family domain."},
                new string[]{"actualPreparedSingleQubits","Cumulative committed selected-tranche single-qubit preparations; separate from represented family credits."},
                new string[]{"committedGateApplications","Cumulative committed selected-register logical gate calls; not amplitude writes or circuit instructions."},
                new string[]{"allocatedDenseStateBytes","Dense storage bytes for the selected live register at the saved snapshot; not lifetime work or full-family allocation."},
                new string[]{"estimatedSparseStateBytes","Estimated sparse storage bytes for the selected live register at the saved snapshot; not measured lifetime arithmetic."}},false,compactInactive);
            AccountingGroup(p,"Tranche lifetime actual work",Map(Get(tranche,"lifetimeActualWork")),new string[][]{
                new string[]{"actualAmplitudeWrites","Selected-register amplitude writes, including replay and partial/cancelled work; not FLOPS."},
                new string[]{"actualTensorProducts","Selected-register tensor-product arithmetic work recorded by the backend."},
                new string[]{"measurementShots","Selected-tranche measurement draws; not circuit joint shots or procedural-family shots."},
                new string[]{"queryCount","Actual selected-register state/spin queries recorded by the backend."},
                new string[]{"actualCheckpointAmplitudeLoads","Amplitudes actually restored from selected-tranche checkpoint data."}},true,compactInactive);
            AccountingGroup(p,"Circuit lifetime actual work",Map(Get(circuit,"lifetime")),new string[][]{
                new string[]{"actualPreparedQubits","Qubits actually prepared for circuit shot replays and retained coherent-output executions."},
                new string[]{"executedCircuitInstructions","Stored program instructions actually traversed by circuit executions; not the definition's gateCount."},
                new string[]{"backendGateApplications","Actual local/joint backend gate calls; separate from program instruction count and exact CP reductions."},
                new string[]{"actualAmplitudeWrites","Actual circuit backend amplitude writes; not physical gates or FLOPS."},
                new string[]{"actualTensorProducts","Actual circuit backend tensor-product arithmetic work."},
                new string[]{"completedShots","Completed joint circuit measurement shots across recorded jobs; not probabilities or requested shots."},
                new string[]{"measuredQubits","Completed circuit qubit measurements, including work from partial shots."},
                new string[]{"uniformDraws","Seeded random variates actually consumed by circuit measurements; a cancelled partial draw can still count."},
                new string[]{"exactCPReductions","Recorded exact controlled-phase reductions for known computational-basis operands."},
                new string[]{"queryCount","Actual retained-circuit state/spin queries, including bounded report previews."},
                new string[]{"coherentOutputExecutions","Separate circuit executions actually performed to retain the coherent premeasurement output."}},true,compactInactive);
        }
        private static void AccountingGroup(Report p,string title,Dictionary<string,object> values,string[][] fields,bool includeAdditional,bool compactInactive)
        {
            bool present=includeAdditional&&values.Count>0;foreach(string[] field in fields)if(values.ContainsKey(field[0]))present=true;if(!present)return;
            List<string[]> rows=new List<string[]>();HashSet<string> known=new HashSet<string>(StringComparer.Ordinal);
            foreach(string[] field in fields){known.Add(field[0]);rows.Add(new string[]{field[0],values.ContainsKey(field[0])?(values[field[0]]==null?"null (explicitly returned)":Str(values[field[0]])):"Not returned",field[1]});}
            if(includeAdditional)foreach(KeyValuePair<string,object> field in values)if(!known.Contains(field.Key))rows.Add(new string[]{field.Key,field.Value==null?"null (explicitly returned)":Str(field.Value),"Additional native counter in this saved engine scope; its unit was not mapped by this renderer."});
            int zeroFields=0,missingFields=0;bool inactive=true;
            foreach(string[] field in fields)
            {
                if(!values.ContainsKey(field[0])){missingFields++;continue;}
                if(ExactlyZeroAccountingValue(values[field[0]]))zeroFields++;else inactive=false;
            }
            if(includeAdditional)foreach(KeyValuePair<string,object> field in values)if(!known.Contains(field.Key))inactive=false;
            if(compactInactive && inactive && zeroFields>0)
            {
                p.Keep(58);p.Heading(title);
                p.Paragraph("Inactive snapshot: all "+zeroFields.ToString(Inv)+" returned counters/flags in this scope are exactly 0 or false; "+missingFields.ToString(Inv)+" mapped fields were not returned. No nonzero activity was recorded in those returned fields. Exact individual values remain in Complete PDF, TXT and JSON.");
                return;
            }
            p.Keep(95);p.Heading(title);p.Table(new string[]{"Counter","Recorded value","Meaning"},rows,new double[]{165,88,275});
        }
        private static void EmpiricalFit(Report p,Dictionary<string,object> result)
        {
            p.Heading("Empirical CSV equation and fitted data");p.Key("Fitted equation",Str(Get(result,"equation")));if(result.ContainsKey("centeredEquation"))p.Key("Centered equation",Str(result["centeredEquation"]));p.Key("Equation convention",Str(Get(result,"equationConvention")));p.Key("Native fitting method",Str(Get(result,"method")));p.Key("Model scope",Str(Get(result,"scope")));
            EquationForms(p,SpinDoctorReportEquations.Describe("q.input fit",result));
            p.Paragraph("This equation fits the recorded numeric CSV rows. It is an empirical relationship within these data, not a physical law, a causal claim or a validated prediction. Export uses the completed fit and performs no fitting, sampling or quantum calculation.");
            Dictionary<string,object> target=Map(Get(result,"target")),source=Map(Get(result,"source")),metrics=Map(Get(result,"metrics"));
            p.Key("Target column / recorded unit",Str(Get(target,"name"))+" / "+(Str(Get(target,"unit")).Length>0?Str(Get(target,"unit")):"unit not supplied"));p.Key("Original CSV filename",Str(Get(source,"filename")));p.Key("Original CSV SHA-256",Str(Get(source,"sha256")));p.Key("Original CSV bytes",Str(Get(source,"bytes")));
            foreach(string key in new string[]{"rowCount","usedRows","excludedRows","rank","parameterCount","qrDiagonalRatio","residualDegreesOfFreedom"})p.Key(key,Str(Get(result,key)));
            p.Key("Intercept",result.ContainsKey("intercept")?Str(result["intercept"]):"Not returned");List<string[]> coefficients=new List<string[]>();
            foreach(object item in Items(Get(result,"coefficients"))){Dictionary<string,object> c=Map(item);coefficients.Add(new string[]{Str(Get(c,"feature")),Str(Get(c,"coefficient")),Str(Get(c,"unit"))});}
            p.Table(new string[]{"Feature","Recorded coefficient","Coefficient unit"},coefficients,new double[]{180,178,170});
            p.Heading("Training fit metrics");foreach(string key in new string[]{"rSquared","rmse","mae","residualSumSquares","residualSumSquaresRepresentable","targetMean","constantTarget","trainingOnly","holdoutRows"})if(metrics.ContainsKey(key))p.Key(key,metrics[key]==null?"null (not defined for this returned fit scope)":Str(metrics[key]));
            p.Paragraph("R-squared, RMSE and MAE describe the rows used to fit this equation. Lower residuals on these rows do not establish accuracy on unseen data. A null R-squared is retained as undefined; it is not replaced with zero.");
            List<string[]> ranges=new List<string[]>();Dictionary<string,object> targetRange=Map(Get(result,"targetStatistics"));ranges.Add(new string[]{"Target: "+Str(Get(target,"name")),Str(Get(targetRange,"min")),Str(Get(targetRange,"max")),Str(Get(targetRange,"mean"))});
            foreach(object item in Items(Get(result,"featureStatistics"))){Dictionary<string,object> f=Map(item);ranges.Add(new string[]{Str(Get(f,"name")),Str(Get(f,"min")),Str(Get(f,"max")),Str(Get(f,"mean"))});}p.Table(new string[]{"Used column","Minimum","Maximum","Mean"},ranges,new double[]{195,111,111,111});
            List<object> data=Items(Get(result,"dataRows"));List<double> observed=new List<double>(),fitted=new List<double>(),residuals=new List<double>();int invalid=0;
            const int maximumPoints=256;int selected=Math.Min(maximumPoints,data.Count);
            for(int i=0;i<selected;i++)
            {
                int index=selected<=1?0:(int)((long)i*(data.Count-1)/(selected-1));Dictionary<string,object> row=Map(data[index]);double o,f,r;
                if(!TryNumber(Get(row,"observed"),out o)||!TryNumber(Get(row,"fitted"),out f)||!TryNumber(Get(row,"residual"),out r)){invalid++;continue;}observed.Add(o);fitted.Add(f);residuals.Add(r);
            }
            p.Heading("Recorded observations and residual figures");p.Key("Recorded fitted data rows",data.Count.ToString(Inv));p.Key("Actual rows shown per figure",observed.Count.ToString(Inv));
            if(data.Count>maximumPoints)p.Paragraph("Figures show at most 256 actual rows selected evenly by returned row order, including the first and last rows. No synthetic points are added. Every returned row remains in the CSV/TXT/JSON companion and Complete PDF fields.");
            if(invalid>0)p.Paragraph("Some returned plot rows were missing finite numeric observed/fitted/residual fields and were omitted from both figures. No missing values are treated as zero.",true);
            if(observed.Count>0){p.Scatter(fitted,observed,"Observed values versus fitted values","Fitted target","Observed target",true,false);p.Scatter(fitted,residuals,"Residuals versus fitted values","Fitted target","Recorded residual",false,true);}
            else p.Paragraph("No finite recorded fitted data rows were supplied; no plot is inferred.");
            p.Paragraph("The diagonal is the equality reference, not fitted or additional data. The residual figure uses the native returned residuals; the horizontal zero reference is not an observation.");
            List<object> excluded=Items(Get(result,"excluded"));if(excluded.Count>0){p.Heading("Excluded input rows");List<string[]> rows=new List<string[]>();for(int i=0;i<Math.Min(32,excluded.Count);i++){Dictionary<string,object> e=Map(excluded[i]);rows.Add(new string[]{Str(Get(e,"rowIndex")),Str(Get(e,"reason"))});}p.Table(new string[]{"Recorded source row index","Exclusion reason"},rows,new double[]{180,348});if(excluded.Count>32)p.Paragraph("First 32 recorded exclusions shown; every excluded row and reason is retained in CSV/TXT/JSON.");}
            foreach(object warning in Items(Get(result,"warnings")))p.Paragraph("Native fit warning: "+Str(warning),true);
        }
        private static void EquationForms(Report p,Dictionary<string,object> equations)
        {
            if(equations.Count==0)return;p.Heading("Equation representations from recorded data");p.Paragraph(Str(Get(equations,"scope")));p.Heading("Editable equation text");p.Mono(Str(Get(equations,"equationText")));p.Paragraph("Copy this text to an editable local draft. Editing a draft does not change the original CSV, native circuit or archived computation. The PDF text is selectable; the visual mathematics below is a presentation of the same symbols.");
            p.Heading("Conventional mathematical form");bool fit=Str(Get(equations,"kind"))=="empirical-fit";
            if(fit)
            {
                p.FitEquation((int)Get(equations,"featureCount"));List<string[]> rows=new List<string[]>();rows.Add(new string[]{"b0",Str(Get(equations,"intercept")),"Intercept","Target unit"});
                foreach(object raw in Items(Get(equations,"variables"))){Dictionary<string,object> variable=Map(raw);rows.Add(new string[]{Str(Get(variable,"coefficientSymbol")),Str(Get(variable,"coefficient")),Str(Get(variable,"symbol"))+" = "+Str(Get(variable,"column")),Str(Get(variable,"unit"))});}
                p.Table(new string[]{"Symbol","Native coefficient","Native CSV column mapping","Unit"},rows,new double[]{55,140,223,110});p.Paragraph("The hat denotes a fitted estimate. p is the number of explicitly selected features. Each indexed variable maps to the native returned column and coefficient above; empirical fit data are not a declared circuit.");
            }
            else
            {
                p.CircuitEquations((int)Get(equations,"gateCount"),Str(Get(equations,"computationalBasisKet")));p.Paragraph("Operators act from right to left; U1 is the first archived gate instruction when any gates are present. B is the mathematical measurement rotation, equivalent to projecting onto the selected basis kets; it does not add gates executed by this exporter. Local reduced density matrices allow zero Bloch vectors for entangled mixed marginal states.");
                HashSet<string> matrices=new HashSet<string>(StringComparer.Ordinal);foreach(object raw in Items(Get(equations,"gateOperators"))){Dictionary<string,object> op=Map(raw),matrix=Map(Get(op,"matrix"));string gate=Str(Get(op,"gate"));if(!matrices.Add(gate))continue;p.Heading(gate.ToUpperInvariant()+" - local mathematical operator");p.Key("Basis / operand convention",Str(Get(matrix,"basis")));List<object> rows=Items(Get(matrix,"rows"));if(rows.Count>0)p.SymbolicMatrix(rows,Str(Get(matrix,"prefactor")));else p.Mono(Str(Get(matrix,"equationText")));}
            }
            p.Key("Copyable conventional notation (LaTeX)",Str(Get(equations,"mathNotation")));
        }
        private static void EmpiricalTable(Report p,Dictionary<string,object> result)
        {
            p.Heading("Archived CSV table inspection");p.Key("Returned data rows",Str(Get(result,"rowCount")));p.Key("Fitting performed",Str(Get(result,"fittingPerformed")));p.Key("Numeric parsing policy",Str(Get(result,"numericPolicy")));p.Key("Native table scope",Str(Get(result,"scope")));
            Dictionary<string,object> source=Map(Get(result,"source"));p.Key("Original CSV filename",Str(Get(source,"filename")));p.Key("Original CSV SHA-256",Str(Get(source,"sha256")));List<string[]> rows=new List<string[]>();
            foreach(object item in Items(Get(result,"columns"))){Dictionary<string,object> column=Map(item);rows.Add(new string[]{Str(Get(column,"index"))+": "+Str(Get(column,"name")),Str(Get(column,"numericCount")),Str(Get(column,"missingCount")),Str(Get(column,"invalidCount")),Get(column,"min")==null?"null":Str(column["min"]),Get(column,"max")==null?"null":Str(column["max"])});}
            p.Table(new string[]{"Column","Numeric","Missing","Invalid","Minimum","Maximum"},rows,new double[]{178,60,60,60,85,85});p.Key("Original data preview",Json.Serialize(Get(result,"rowsPreview")));p.Key("Preview covers every row",Str(Get(result,"rowsPreviewComplete")));
            p.Paragraph("Table inspection does not fit an equation. Choose one target and numeric feature columns explicitly with q.input fit. Header names do not establish units, causality or forecast validity; nonnumeric cells are not silently converted to zero.");
        }
        private static bool ExactlyZeroAccountingValue(object value)
        {
            if(value is bool)return !(bool)value;
            if(value is string)return System.Text.RegularExpressions.Regex.IsMatch((string)value,@"^[+-]?0+(?:\.0+)?$");
            if(value is int ||value is long ||value is uint ||value is ulong ||value is short ||value is byte ||value is decimal)return Convert.ToDecimal(value,Inv)==0;
            if(value is double)return (double)value==0;
            if(value is float)return (float)value==0;
            return false;
        }
        private static void CompactObject(Report p, Dictionary<string, object> value, int depth, int max)
        {
            int count = 0;
            foreach (KeyValuePair<string, object> item in value)
            {
                if (count++ >= max) { p.Paragraph("Additional fields remain in the complete JSON/TXT return."); break; }
                if (item.Value == null || item.Value is string || item.Value is bool || item.Value is ValueType){string text=item.Value==null?"null (explicitly returned)":Str(item.Value);p.Key(item.Key,text.Length>768?text.Substring(0,768)+" [bounded field guide; exact value in TXT/JSON]":text);}
                else if (depth < 1 && item.Value is Dictionary<string, object>) { p.Heading(item.Key); CompactObject(p, Map(item.Value), depth + 1, 16); }
                else p.Key(item.Key, "Structured data; see JSON/TXT or Details/Raw PDF view.");
            }
        }
        private sealed class Outcome { public string Label, Count; public Outcome(string label, string count) { Label = label; Count = count; } }
        private static List<Outcome> HistogramRows(object raw) { List<Outcome> rows = new List<Outcome>(); foreach (object value in Items(raw)) { Dictionary<string, object> r = Map(value); rows.Add(new Outcome(Str(Get(r, "bitstring")), Str(Get(r, "count")))); } return rows; }
        private static List<string[]> ToRows(List<Outcome> rows) { List<string[]> result = new List<string[]>(); foreach (Outcome row in rows) result.Add(new string[] { row.Label, row.Count }); return result; }
        private static Dictionary<string, object> ParseProgram(string text)
        {
            if (String.IsNullOrEmpty(text)) return new Dictionary<string, object>();
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i + 1 < lines.Length; i++) if (lines[i] == "Exact program descriptor:") { try { return Map(Json.DeserializeObject(lines[i + 1])); } catch { return new Dictionary<string, object>(); } }
            return new Dictionary<string, object>();
        }
        private static List<Outcome> FullHistogram(string text)
        {
            List<Outcome> result = new List<Outcome>(); bool active = false;
            if (String.IsNullOrEmpty(text)) return result;
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                if (line.StartsWith("FULL HISTOGRAM", StringComparison.Ordinal)) { active = true; continue; }
                if (!active) continue; if (line.StartsWith("Distinct outcomes:", StringComparison.Ordinal)) break;
                string[] fields = line.Split('\t'); if (fields.Length == 2) result.Add(new Outcome(fields[0], fields[1]));
            }
            return result;
        }
        private static string PrintoutField(string text, string field) { if (text == null) return ""; foreach (string line in text.Replace("\r", "").Split('\n')) if (line.StartsWith(field + ": ", StringComparison.Ordinal)) return line.Substring(field.Length + 2); return ""; }
        private static bool MatchesNativePrintout(Dictionary<string, object> result, string text)
        {
            if (String.IsNullOrEmpty(text) || Str(Get(result, "id")).Length == 0) return false;
            foreach (string key in new string[] { "id", "programHash", "basis", "completedShots", "counterEnd", "phase" })
                if (Str(Get(result, key)) != PrintoutField(text, key)) return false;
            return true;
        }
        private static object Get(Dictionary<string, object> d, string key) { object value; return d != null && d.TryGetValue(key, out value) ? value : null; }
        private static Dictionary<string, object> Map(object v) { return v as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        private static List<object> Items(object v) { List<object> a = new List<object>(); IEnumerable e = v as IEnumerable; if (e != null && !(v is string) && !(v is IDictionary)) foreach (object x in e) a.Add(x); return a; }
        private static bool Bool(object v) { return v is bool && (bool)v; }
        private static string Str(object v) { return v == null ? "" : Convert.ToString(v, Inv); }
        private static double Double(object v) { double d; return System.Double.TryParse(Str(v), NumberStyles.Float, Inv, out d) && !System.Double.IsNaN(d) && !System.Double.IsInfinity(d) ? d : 0; }
        private static bool TryNumber(object v,out double d){d=0;return v!=null && !(v is bool) && !(v is IDictionary) && !(v is IList) && System.Double.TryParse(Str(v),NumberStyles.Float,Inv,out d) && !System.Double.IsNaN(d) && !System.Double.IsInfinity(d);}
        private static double Number(object v){double d=0;if(!TryNumber(v,out d))throw new ArgumentException("A chart value is missing, nonnumeric or nonfinite; PDF refused instead of plotting a fabricated zero.");return d;}
        private static BigInteger DecimalCount(string text,string label){BigInteger n;if(text.Length>1024||!BigInteger.TryParse(text,NumberStyles.None,Inv,out n)||n<0)throw new ArgumentException(label+" must be an exact nonnegative integer of at most1024 digits.");return n;}
        private static int MaxQubit(List<object> a) { int max = 0; foreach (object v in a) max = Math.Max(max, (int)Double(v)); return Math.Min(max, 28); }
        private static Dictionary<string, object> D(params object[] a) { Dictionary<string, object> d = new Dictionary<string, object>(); for (int i = 0; i < a.Length; i += 2) d[a[i].ToString()] = a[i + 1]; return d; }

        private sealed class Report
        {
            private readonly List<StringBuilder> pages = new List<StringBuilder>(); private StringBuilder page; private double y; private readonly string title;private int textCharacters;
            public Report(string title) { this.title = title; NewPage(); }
            public void NewPage()
            {
                if(pages.Count>=MaximumPdfPages)throw new ArgumentException("PDF exceeds 512 pages. Use full native TXT/JSON, a per-command PDF or smaller recorded-command batches.");
                page = new StringBuilder(); pages.Add(page); y = 715;
                Fill(0.07, 0.18, 0.24); Rect(0, 744, Width, 48, true); Text(Left, 766, "SPINDOCTOR", 17, true, false, true); Text(200, 766, title, 10, false, false, true);
                Stroke(0.65, 0.76, 0.8); Line(Left, 40, Right, 40); Text(Left, 27, "Classical simulation | native values | no physical quantum gate credit", 7, false, false, false); Text(528, 27, "Page " + pages.Count.ToString(Inv), 8, false, false, false);
            }
            private void Space(double h) { if (y - h < Bottom) NewPage(); }
            public void Keep(double h) { Space(Math.Min(h, 650)); }
            public void Heading(string text) { List<string> lines = Wrap(text, 60); Space(60 + lines.Count * 16); y -= 10; foreach (string line in lines) { Text(Left, y, line, 12, true, false, false); y -= 16; } }
            public void Paragraph(string text, bool warn = false)
            {
                List<string> lines = Wrap(text, 96); Space(Math.Min(650, lines.Count * 13 + 6));
                foreach (string line in lines) { Space(15); Text(Left, y, line, 9, warn, false, false); y -= 13; } y -= 6;
            }
            public void Key(string name, string value)
            {
                if (String.IsNullOrEmpty(value)) value = "Not returned";
                List<string> lines = Wrap(value, 68), names = Wrap(name, 36); int count = Math.Max(lines.Count, names.Count); Space(Math.Min(600,count * 11 + 8));
                for (int i = 0; i < count; i++) { Space(14);if (i < names.Count) Text(Left, y, names[i], 8, true, false, false); if (i < lines.Count) Text(226, y, lines[i], 8, false, true, false); y -= 11; } y -= 4;
            }
            public void Mono(string text) {if(text.Length>MaximumPdfTextCharacters)throw new ArgumentException("Raw PDF text exceeds 4 MiB. Use complete TXT/JSON or a smaller recorded command.");foreach (string line in Wrap(text, 102)) { Space(12); Text(Left, y, line, 8, false, true, false); y -= 11; } y -= 8; }
            private void MathText(double x,double yy,string text,double size)
            {string clean=Clean(text);if(clean.Length>MaximumPdfTextCharacters-textCharacters)throw new ArgumentException("Equation text exceeds the PDF document budget.");textCharacters+=clean.Length;page.Append("0.08 0.19 0.25 rg\nBT /F4 ").Append(N(size)).Append(" Tf 1 0 0 1 ").Append(N(x)).Append(' ').Append(N(yy)).Append(" Tm (").Append(Escape(clean)).Append(") Tj ET\n");}
            private void Psi(double x,double yy,double height)
            {Stroke(.08,.19,.25);double r=height*.34;Line(x,yy+height*.75,x,yy-height*.4);page.Append(N(x-r)).Append(' ').Append(N(yy+height*.6)).Append(" m\n");Curve(x-r,yy-height*.05,x-r*.4,yy-height*.05,x,yy-height*.05);Curve(x+r*.4,yy-height*.05,x+r,yy-height*.05,x+r,yy+height*.6);page.Append("S\n");}
            private void Sigma(double x,double yy,double height)
            {Stroke(.08,.19,.25);double w=height*.75;Line(x+w,yy+height,x,yy+height);Line(x,yy+height,x+w*.6,yy+height*.5);Line(x+w*.6,yy+height*.5,x,yy);Line(x,yy,x+w,yy);}
            private void Rho(double x,double yy,double height)
            {Stroke(.08,.19,.25);page.Append(N(x)).Append(' ').Append(N(yy-height*.4)).Append(" m\n");Curve(x+height*.06,yy+height*.05,x+height*.02,yy+height*.65,x+height*.34,yy+height*.65);Curve(x+height*.78,yy+height*.65,x+height*.74,yy,x+height*.35,yy);Curve(x+height*.12,yy,x+height*.08,yy+height*.16,x+height*.08,yy+height*.3);page.Append("S\n");}
            private void SmallSigma(double x,double yy,double height)
            {Stroke(.08,.19,.25);page.Append(N(x+height*.52)).Append(' ').Append(N(yy+height*.6)).Append(" m\n");Curve(x+height*.06,yy+height*.6,x-height*.02,yy,x+height*.31,yy);Curve(x+height*.7,yy,x+height*.78,yy+height*.43,x+height*.52,yy+height*.6);page.Append("S\n");Line(x+height*.34,yy+height*.6,x+height*.87,yy+height*.6);}
            public void FitEquation(int features)
            {
                Keep(100);double at=Left+12,baseline=y-30;MathText(at,baseline,"y",22);Stroke(.08,.19,.25);Line(at-1,baseline+22,at+5,baseline+26);Line(at+5,baseline+26,at+11,baseline+22);MathText(at+28,baseline,"=",20);MathText(at+56,baseline,"b",22);MathText(at+67,baseline-5,"0",10);MathText(at+87,baseline,"+",20);Sigma(at+116,baseline-2,25);MathText(at+118,baseline+29,features.ToString(Inv),10);MathText(at+111,baseline-16,"j = 1",10);MathText(at+151,baseline,"b",22);MathText(at+162,baseline-5,"j",10);MathText(at+176,baseline,"x",22);MathText(at+188,baseline-5,"j",10);y-=92;
            }
            public void CircuitEquations(int gates,string inputBasis)
            {
                Keep(190);double at=Left+12,baseline=y-25;MathText(at,baseline,"|",22);Psi(at+16,baseline,24);MathText(at+22,baseline-7,"out",9);MathText(at+41,baseline,"> =",22);if(gates>0){MathText(at+95,baseline,"U",22);MathText(at+111,baseline-5,gates.ToString(Inv),10);if(gates==1)MathText(at+137,baseline,"|"+inputBasis+">",20);else if(gates==2){MathText(at+137,baseline,"U",22);MathText(at+153,baseline-5,"1",10);MathText(at+180,baseline,"|"+inputBasis+">",20);}else{MathText(at+137,baseline,"... U",22);MathText(at+185,baseline-5,"1",10);MathText(at+205,baseline,"|"+inputBasis+">",20);}}else MathText(at+95,baseline,"|"+inputBasis+">",20);
                baseline-=58;MathText(at,baseline,"P(s | B) = |<s| B |",19);Psi(at+165,baseline,22);MathText(at+172,baseline-7,"out",9);MathText(at+190,baseline,">|",20);MathText(at+211,baseline+10,"2",10);
                baseline-=52;MathText(at,baseline,"r",22);MathText(at+10,baseline-5,"q,k",10);MathText(at+40,baseline,"= Tr(",19);Rho(at+89,baseline,21);MathText(at+105,baseline-5,"q",10);SmallSigma(at+124,baseline,21);MathText(at+144,baseline-5,"k",10);MathText(at+160,baseline,")",19);y-=183;
            }
            public void SymbolicMatrix(List<object> rows,string prefactor)
            {
                int n=rows.Count,columns=Items(rows[0]).Count;if(n>4||columns>4)throw new ArgumentException("Local symbolic matrix display is bounded to4x4; full-register matrices are not generated.");
                Keep(n*30+60);double width=488.0/columns,start=Left+25,top=y-18,bottom=top-n*30+10;Stroke(.08,.19,.25);Line(start-10,top+14,start-10,bottom);Line(start-10,top+14,start-3,top+14);Line(start-10,bottom,start-3,bottom);Line(Right-9,top+14,Right-9,bottom);Line(Right-16,top+14,Right-9,top+14);Line(Right-16,bottom,Right-9,bottom);
                for(int i=0;i<n;i++){List<object> row=Items(rows[i]);if(row.Count!=columns)throw new ArgumentException("Symbolic local matrix must be rectangular.");for(int j=0;j<columns;j++){string cell=Str(row[j]);double font=Math.Min(11,(width-16)/Math.Max(1,cell.Length*.7));if(font<7.5)throw new ArgumentException("Local matrix formula exceeds the readable cell bound; export the labelled symbolic operator instead.");MathText(start+j*width,top-i*30,cell,font);}}
                y-=n*30+25;if(prefactor.Length>0)Key("Matrix prefactor",prefactor);
            }
            public void Table(string[] headers, List<string[]> rows, double[] widths)
            {
                if (rows.Count == 0) { Paragraph("No returned rows."); return; }
                Space(58);
                Action head = delegate() { Space(27); Fill(0.9, 0.95, 0.96); Rect(Left, y - 16, Right - Left, 23, true); double x = Left + 7; for (int c = 0; c < headers.Length; c++) { Text(x, y - 8, headers[c], 8, true, false, false); x += widths[c]; } y -= 27; };
                head();
                foreach (string[] row in rows)
                {
                    List<string>[] cell = new List<string>[headers.Length]; int lines = 1;
                    for (int c = 0; c < headers.Length; c++) { cell[c] = Wrap(c < row.Length ? row[c] : "", Math.Max(8, (int)((widths[c] - 15) / 4.8))); lines = Math.Max(lines, cell[c].Count); }
                    int first=0;while(first<lines){int available=Math.Max(0,(int)((y-Bottom-10)/11));if(available<1){NewPage();head();continue;}int take=Math.Min(lines-first,available);double h=take*11+7;if(first==0 && take<lines && h<100){NewPage();head();continue;}
                    double x = Left + 7; for (int c = 0; c < headers.Length; c++) { for (int j = 0; j < take && first+j<cell[c].Count; j++) Text(x, y - j * 11 - 5, cell[c][first+j], 8, false, c != 0, false); x += widths[c]; }
                    Stroke(0.85, 0.9, 0.92); Line(Left, y - h + 5, Right, y - h + 5); y -= h;first+=take;if(first<lines){NewPage();head();}}
                }
                y -= 9;
            }
            public void Gauge(string label, double value, double total)
            {
                Space(39); Text(Left, y, label, 9, true, false, false); double ratio = total > 0 ? Math.Max(0, Math.Min(1, value / total)) : 0;
                Fill(0.9, 0.95, 0.96); Rect(Left, y - 20, 370, 10, true); Fill(0.04, 0.55, 0.67); Rect(Left, y - 20, 370 * ratio, 10, true); Text(426, y - 19, total>0?(ratio * 100).ToString("0.000", Inv) + "%":"No planned total", 9, false, true, false); y -= 38;
            }
            public void Scatter(List<double> xs,List<double> ys,string title,string xLabel,string yLabel,bool equality,bool zeroReference)
            {
                if(xs.Count==0 ||xs.Count!=ys.Count)return;double xmin=xs[0],xmax=xs[0],ymin=ys[0],ymax=ys[0];
                for(int i=0;i<xs.Count;i++){xmin=Math.Min(xmin,xs[i]);xmax=Math.Max(xmax,xs[i]);ymin=Math.Min(ymin,ys[i]);ymax=Math.Max(ymax,ys[i]);}
                if(equality){xmin=ymin=Math.Min(xmin,ymin);xmax=ymax=Math.Max(xmax,ymax);}if(zeroReference){ymin=Math.Min(0,ymin);ymax=Math.Max(0,ymax);}
                if(xmin==xmax)ExpandPlotRange(ref xmin,ref xmax);if(ymin==ymax)ExpandPlotRange(ref ymin,ref ymax);
                Space(278);double top=y,left=Left+62,right=Right-14,bottom=top-210,upper=top-28;Text(Left,top,title,10,true,false,false);
                Fill(.97,.985,.99);Rect(left,bottom,right-left,upper-bottom,true);Stroke(.5,.64,.7);Line(left,bottom,right,bottom);Line(left,bottom,left,upper);
                for(int tick=0;tick<=4;tick++)
                {
                    double ratio=tick/4.0,xx=left+(right-left)*ratio,yy=bottom+(upper-bottom)*ratio;
                    Stroke(.86,.91,.93);Line(xx,bottom,xx,upper);Line(left,yy,right,yy);Text(xx-12,bottom-14,Blend(xmin,xmax,ratio).ToString("G5",Inv),7,false,true,false);Text(Left,yy-3,Blend(ymin,ymax,ratio).ToString("G5",Inv),7,false,true,false);
                }
                if(equality){Stroke(.55,.35,.7);Line(left,bottom,right,upper);}if(zeroReference){double yy=bottom+(upper-bottom)*Fraction(0,ymin,ymax);Stroke(.68,.34,.34);Line(left,yy,right,yy);}
                Fill(.03,.5,.64);for(int i=0;i<xs.Count;i++)Ellipse(left+(right-left)*Fraction(xs[i],xmin,xmax),bottom+(upper-bottom)*Fraction(ys[i],ymin,ymax),2.1,2.1,true);
                Text(left+90,bottom-32,xLabel,8,true,false,false);Text(Left,top-250,yLabel+"; "+xs.Count.ToString(Inv)+" actual recorded points",8,false,false,false);y=top-280;
            }
            private static double Blend(double low,double high,double fraction){return low*(1-fraction)+high*fraction;}
            private static void ExpandPlotRange(ref double low,ref double high)
            {
                double value=low,delta=Math.Max(1,Math.Abs(value)*0.05),lower=value-delta,upper=value+delta;
                low=System.Double.IsInfinity(lower)?value:lower;high=System.Double.IsInfinity(upper)?value:upper;
            }
            private static double Fraction(double value,double low,double high){double scale=Math.Max(1,Math.Max(Math.Abs(low),Math.Abs(high)));return Math.Max(0,Math.Min(1,(value/scale-low/scale)/(high/scale-low/scale)));}
            public void Spin(double x, double yy, double z)
            {
                double scale=Math.Max(Math.Abs(x),Math.Max(Math.Abs(yy),Math.Abs(z)));
                double length=scale==0?0:scale*Math.Sqrt((x/scale)*(x/scale)+(yy/scale)*(yy/scale)+(z/scale)*(z/scale));
                if(System.Double.IsNaN(length)||System.Double.IsInfinity(length)||length>1.000001)throw new ArgumentException("Returned Bloch vector lies outside the unit sphere.");
                Space(264); double top = y, cx = Left + 104, cy = top - 104, r = 74;
                Text(Left,top,"Bloch sphere and spin arrow",10,true,false,false);
                Fill(0.96,0.985,0.99);Ellipse(cx,cy,r,r,true);
                Stroke(0.36,0.61,0.68);page.Append("0.7 w\n");Ellipse(cx,cy,r,r);
                SphereGuide(cx,cy,r,0);SphereGuide(cx,cy,r,1);SphereGuide(cx,cy,r,2);
                page.Append("[] 0 d\n");Stroke(0.58,0.69,0.74);
                double ux=0.7071067811865476, vy=-0.4082482904638631, vz=0.8164965809277261;
                Line(cx-r*ux,cy-r*vy,cx+r*ux,cy+r*vy);
                Line(cx+r*ux,cy-r*vy,cx-r*ux,cy+r*vy);
                Line(cx,cy-r*vz,cx,cy+r*vz);
                Text(cx+r*ux+8,cy+r*vy-3,"+X",8,true,false,false);
                Text(cx-r*ux-22,cy+r*vy-3,"+Y",8,true,false,false);
                Text(cx+8,cy+r*vz+10,"+Z  |0>",8,true,false,false);
                Text(cx+8,cy-r*vz-13,"-Z  |1>",8,true,false,false);
                double px=cx+r*ux*(x-yy),py=cy+r*(vy*(x+yy)+vz*z);
                Stroke(0.02,0.42,0.63);Fill(0.02,0.42,0.63);page.Append("2.5 w\n");
                double dx=px-cx,dy=py-cy,projection=Math.Sqrt(dx*dx+dy*dy);
                bool viewAxis=length>=1e-12 && projection<=0.001 && projection/(r*length)<1e-5;
                if(projection>0.001){
                    Line(cx,cy,px,py);double ax=dx/projection,ay=dy/projection;
                    page.Append(N(px)).Append(' ').Append(N(py)).Append(" m ").Append(N(px-9*ax+4*ay)).Append(' ').Append(N(py-9*ay-4*ax)).Append(" l ").Append(N(px-9*ax-4*ay)).Append(' ').Append(N(py-9*ay+4*ax)).Append(" l h f\n");
                }else if(viewAxis){Ellipse(cx,cy,7,7);if(x+yy+z>=0)Ellipse(cx,cy,2,2,true);else{Line(cx-4,cy-4,cx+4,cy+4);Line(cx-4,cy+4,cx+4,cy-4);}}else{Ellipse(px,py,3,3,true);}
                page.Append("0.7 w\n");Ellipse(cx,cy,2,2,true);
                string[] names = { "X", "Y", "Z" }; double[] values = { x, yy, z };
                for (int i = 0; i < 3; i++)
                {
                    double by = top - 39 - i * 43, zero = 400, half = 65, value = Math.Max(-1, Math.Min(1, values[i]));
                    Text(273, by + 5, names[i] + " spin", 9, true, false, false); Fill(0.9, 0.95, 0.96); Rect(zero - half, by - 11, 2 * half, 10, true); Fill(0.04, 0.55, 0.67); Rect(value < 0 ? zero + half * value : zero, by - 11, Math.Abs(value) * half, 10, true);
                    Stroke(0.4, 0.52, 0.6); Line(zero, by - 14, zero, by + 1); Text(475, by - 9, values[i]!=0 &&Math.Abs(values[i])<1e-6 ?values[i].ToString("G9",Inv):values[i].ToString("0.000000", Inv), 8, false, true, false);
                }
                Text(273,top-175,"Vector length = "+(length!=0 &&length<1e-6 ?length.ToString("G9",Inv):length.ToString("0.000000",Inv)),9,true,false,false);
                Text(Left,top-212,"Figure: orthographic view of the reported X/Y/Z expectations.",8,false,false,false);
                Text(Left,top-225,viewAxis?"Nonzero vector along the view axis: ring/dot or cross shows depth.":"Arrow length is preserved in 3D; projection may look shorter.",8,false,false,false);
                Text(Left,top-238,length==0?"Zero vector: no preferred local direction; center marker, not |0>.":Math.Abs(x)<1e-12&&Math.Abs(yy)<1e-12&&Math.Abs(z-1)<1e-12?"North pole |0>: basis label 0; spin Z = +1, not a zero vector.":Math.Abs(x)<1e-12&&Math.Abs(yy)<1e-12&&Math.Abs(z+1)<1e-12?"South pole |1>: spin Z = -1, a nonzero vector.":length<1e-12?"Near-zero vector: below drawing precision; exact XYZ retained.":length<0.999999?"Interior vector: mixed or reduced state; no pure state is inferred.":"Near-unit vector: close to the surface at displayed precision.",8,false,false,false);
                Text(Left,top-251,"Recorded XYZ = ("+x.ToString("R",Inv)+", "+yy.ToString("R",Inv)+", "+z.ToString("R",Inv)+")",7.5,false,true,false);
                y -= 263;
            }
            private void SphereGuide(double cx,double cy,double r,int plane)
            {
                const int steps=80;
                for(int i=0;i<steps;i++){
                    double a=2*Math.PI*i/steps,b=2*Math.PI*(i+1)/steps;
                    double x1=plane==2?0:Math.Cos(a),y1=plane==0?Math.Sin(a):plane==2?Math.Cos(a):0,z1=plane==0?0:Math.Sin(a);
                    double x2=plane==2?0:Math.Cos(b),y2=plane==0?Math.Sin(b):plane==2?Math.Cos(b):0,z2=plane==0?0:Math.Sin(b);
                    bool back=(x1+y1+z1+x2+y2+z2)<0;
                    page.Append(back?"[2 3] 0 d\n":"[] 0 d\n");Stroke(back?0.73:0.48,back?0.83:0.68,back?0.87:0.74);
                    Line(cx+r*0.7071067811865476*(x1-y1),cy+r*(-0.4082482904638631*(x1+y1)+0.8164965809277261*z1),cx+r*0.7071067811865476*(x2-y2),cy+r*(-0.4082482904638631*(x2+y2)+0.8164965809277261*z2));
                }
                page.Append("[] 0 d\n");
            }
            public void Histogram(List<Outcome> raw, string label)
            {
                Space(Math.Min(24, raw.Count) * 20 + 95);
                Heading(label); if (raw.Count == 0) { Paragraph("No completed outcome rows are available."); return; }
                List<Outcome> rows = new List<Outcome>(raw); rows.Sort(delegate(Outcome a, Outcome b) { return Double(b.Count).CompareTo(Double(a.Count)); });
                int count = Math.Min(24, rows.Count); double max = 1; for (int i = 0; i < count; i++) max = Math.Max(max, Double(rows[i].Count));
                Space(count * 20 + 35);
                for (int i = 0; i < count; i++)
                {
                    Text(Left, y - 5, rows[i].Label, 7, false, true, false); Fill(0.88, 0.94, 0.96); Rect(211, y - 10, 270, 12, true); Fill(0.04, 0.55, 0.67); Rect(211, y - 10, 270 * Double(rows[i].Count) / max, 12, true); Text(489, y - 7, rows[i].Count, 8, false, true, false); y -= 20;
                }
                if(rows.Count>count){BigInteger rest=BigInteger.Zero;for(int i=count;i<rows.Count;i++)rest+=BigInteger.Parse(rows[i].Count,Inv);Key("Additional recorded outcomes",(rows.Count-count).ToString(Inv)+" rows; "+rest.ToString(Inv)+" completed shots. Their exact rows follow.");}
                Paragraph("Bars show returned counts, not probability amplitudes. Exact rows follow; omitted preview rows are explicitly identified.");
            }
            public void Diagram(int n, List<object> gates, int first, int count)
            {
                double stepY = n > 16 ? 12 : n > 8 ? 17 : 26, h = n * stepY + 72; Space(h); double top = y - 12, start = Left + 58, stepX = 31;
                for (int q = 0; q < n; q++) { double by = top - q * stepY; Text(Left, by - 3, "q" + q.ToString(Inv), 8, false, true, false); Stroke(0.6, 0.73, 0.78); Line(start - 13, by, Right - 9, by); }
                for (int k = 0; k < count; k++)
                {
                    Dictionary<string, object> gate = Map(gates[first + k]); string name = Str(Get(gate, "name") ?? Get(gate, "gate")).ToUpperInvariant(); List<object> targets = Items(Get(gate, "qubits"));
                    bool all = Bool(Get(gate, "all")); if (targets.Count == 0 && all) for (int q = 0; q < n; q++) targets.Add(q);
                    if (targets.Count == 0) targets.Add(0);
                    double x = start + k * stepX; int low = n, high = 0; foreach (object t in targets) { low = Math.Min(low, (int)Double(t)); high = Math.Max(high, (int)Double(t)); }
                    low = Math.Max(0, Math.Min(n - 1, low)); high = Math.Max(0, Math.Min(n - 1, high));
                    bool local = ("|I|ID|IDENTITY|H|X|Y|Z|S|SDG|T|TDG|SX|SXDG|RX|RY|RZ|P|U|U3|").Contains("|" + name + "|");
                    if (!local) { Stroke(0.05, 0.38, 0.5); Line(x, top - low * stepY, x, top - high * stepY); }
                    bool controlled = name.StartsWith("C", StringComparison.Ordinal) && name != "CSWAP";
                    for (int t = 0; t < targets.Count; t++)
                    {
                        int q = (int)Double(targets[t]); if (q < 0 || q >= n) continue; double by = top - q * stepY;
                        if ((controlled && t < targets.Count - 1) || (name == "CSWAP" && t == 0)) { Fill(0.03, 0.38, 0.5); Ellipse(x, by, 2.4, 2.4, true); }
                        else { Fill(0.91, 0.96, 0.97); Rect(x - 12, by - 5, 24, 11, true); Stroke(0.12, 0.48, 0.57); Rect(x - 12, by - 5, 24, 11, false); Text(x - 10, by - 2.5, name == "CX" || name == "CCX" ? "X" : name, 6, true, false, false); }
                    }
                    Text(x - 8, top + 13, (first + k + 1).ToString(Inv), 6, false, true, false);
                    List<object> angles = Items(Get(gate, "angles")); if (angles.Count != 0) { string a = ""; foreach (object angle in angles) a += (a.Length == 0 ? "" : ",") + Double(angle).ToString("0.###", Inv); foreach (string line in Wrap(a, 5)) { Text(x - 11, top - n * stepY - 14, line, 5, false, true, false); break; } }
                }
                Text(Left, top - n * stepY - 35, count==0?"No gates in this recorded program.":"Layers " + (first + 1).ToString(Inv) + "-" + (first + count).ToString(Inv) + "; full exact angles are retained in the program descriptor.", 8, false, false, false); y -= h;
            }
            public void Values(List<object> values,string label)
            {
                if(values.Count>4096)throw new ArgumentException("Numerical charts support at most 4096 returned entries; use smaller query ranges.");
                double maximum=0;for(int i=0;i<values.Count;i++)maximum=Math.Max(maximum,Math.Abs(Number(values[i])));double max=maximum==0?1:maximum;
                for(int first=0;first<values.Count;first+=24){int n=Math.Min(24,values.Count-first);Keep(n*19+90);Heading(label+" / entries "+first+"-"+(first+n-1));
                for(int i=first;i<first+n;i++){double v=Number(values[i]);Text(Left,y-5,i.ToString(Inv),8,false,true,false);Stroke(.6,.73,.78);Line(300,y-10,300,y+3);Fill(v<0?.7:.04,v<0?.2:.55,v<0?.3:.67);double w=160*(Math.Abs(v)/max);Rect(v<0?300-w:300,y-9,w,11,true);Text(474,y-6,v.ToString("G7",Inv),8,false,true,false);y-=19;}}
                Paragraph("Linear axis centered at zero; maximum absolute returned value "+maximum.ToString("R",Inv)+(maximum==0?". All returned values are zero; the display scale uses one to keep a finite axis":"")+". All "+values.Count+" returned entries plotted in labeled pages; values omitted by the calculation are not inferred.");
            }
            public void Metrics(Dictionary<string,object> value,string[] fields,string label,string caption)
            {
                List<string> names=new List<string>();List<double> numbers=new List<double>();List<string[]> exact=new List<string[]>();double maximum=0;
                foreach(string field in fields){if(!value.ContainsKey(field))continue;double number;if(!TryNumber(value[field],out number)){Key(field+" figure","Returned value is null, nonnumeric or outside finite chart range; no zero/bar is inferred. Exact value: "+Str(value[field]));continue;}names.Add(field);numbers.Add(number);maximum=Math.Max(maximum,Math.Abs(number));exact.Add(new string[]{field,Str(value[field])});}
                if(names.Count==0)return;Keep(names.Count*22+100);Heading(label);double scale=maximum==0?1:maximum;
                for(int i=0;i<names.Count;i++){double number=numbers[i];Text(Left,y-3,names[i],8,true,false,false);Stroke(.55,.7,.77);Line(315,y-13,315,y+3);Fill(number<0?.7:.04,number<0?.2:.55,number<0?.3:.67);double w=130*(Math.Abs(number)/scale);Rect(number<0?315-w:315,y-9,w,10,true);Text(455,y-6,number.ToString("G8",Inv),8,false,true,false);y-=22;}
                Paragraph(caption+" Maximum absolute returned value: "+maximum.ToString("R",Inv)+(maximum==0?"; all displayed values are returned zero":"")+".");
                Table(new string[]{"Native field / unit in caption","Exact returned value"},exact,new double[]{288,240});
            }
            public void ComplexVector(List<object> vector,string label,bool basisIndices=true)
            {
                if(vector.Count>4096)throw new ArgumentException("Complex-state charts support at most 4096 returned entries; export smaller query ranges.");
                double max=1e-16;for(int i=0;i<vector.Count;i++){List<object>a=Items(vector[i]);if(a.Count!=2)throw new ArgumentException("Complex vector entry must contain real/imaginary values.");max=Math.Max(max,Math.Max(Math.Abs(Number(a[0])),Math.Abs(Number(a[1]))));}
                for(int first=0;first<vector.Count;first+=16){int n=Math.Min(16,vector.Count-first);Keep(n*26+100);Heading(label+" / complex entries "+first+"-"+(first+n-1));
                for(int i=first;i<first+n;i++){List<object>a=Items(vector[i]);double real=Number(a[0]),imag=Number(a[1]),prob=real*real+imag*imag;if(System.Double.IsInfinity(prob)||System.Double.IsNaN(prob))throw new ArgumentException("Returned complex magnitude overflows the finite chart range.");Text(Left,y-3,basisIndices?"|"+i+">":"component "+i,8,false,true,false);Stroke(.5,.7,.8);Line(244,y-15,244,y+5);double wr=110*(Math.Abs(real)/max),wi=110*(Math.Abs(imag)/max);Fill(.04,.55,.67);Rect(real<0?244-wr:244,y-3,wr,6,true);Fill(.62,.28,.67);Rect(imag<0?244-wi:244,y-12,wi,6,true);Text(380,y-3,"|z|^2="+prob.ToString("G7",Inv),8,false,true,false);Text(380,y-13,real==0&&imag==0?"phase undefined at zero":"phase="+Math.Atan2(imag,real).ToString("0.######",Inv),7,false,true,false);y-=26;}}
                Paragraph("Cyan: real component; purple: imaginary component. |z|^2 is squared magnitude, a basis probability only for a normalized state vector. Phase in radians; q0 is least significant for state indices. All "+vector.Count+" returned entries plotted; global phase is retained. Unreturned state entries are not synthesized.");
                List<string[]> exact=new List<string[]>();for(int i=0;i<Math.Min(32,vector.Count);i++){List<object>a=Items(vector[i]);exact.Add(new string[]{i.ToString(Inv),Str(a[0]),Str(a[1])});}Table(new string[]{basisIndices?"Returned state index":"Returned component","Exact real component","Exact imaginary component"},exact,new double[]{148,190,190});if(vector.Count>32)Paragraph("Exact-value table shows the first 32 of "+vector.Count+" returned entries; all returned entries are graphed. Complete exact arrays remain in TXT/JSON.");
            }
            public void BasisRows(List<object> rows,string label)
            {
                if(rows.Count>4096)throw new ArgumentException("Basis-row figures support<=4096 recorded rows.");
                List<string[]> table=new List<string[]>();List<object> probabilities=new List<object>();List<object> vector=new List<object>();bool complex=true;
                foreach(object raw in rows){Dictionary<string,object> row=Map(raw);double probability=Number(Get(row,"probability"));if(probability<0||probability>1.000001)throw new ArgumentException("Returned basis probability lies outside [0,1].");probabilities.Add(probability);List<object> amplitude=Items(Get(row,"amplitude"));if(amplitude.Count==2&&IsNumericVector(amplitude))vector.Add(amplitude.ToArray());else complex=false;table.Add(new string[]{Str(Get(row,"basisIndex")),Str(Get(row,"bitstring")),probability.ToString("R",Inv),amplitude.Count==2?Json.Serialize(amplitude):"Not returned",Get(row,"phaseRadians")==null?"Undefined/not returned":Str(Get(row,"phaseRadians"))});}
                Values(probabilities,label+" / returned-row probabilities (row offset)");if(complex)ComplexVector(vector,label+" / returned-row amplitudes (row offset)",false);
                Keep(110);Heading("Exact queried basis indices and phase");Table(new string[]{"Basis index","Bitstring q0 right","Probability","Amplitude [re, im]","Phase rad"},table,new double[]{90,120,100,145,73});
                Paragraph("Chart row offsets refer to the exact basis-index table above. A prefix/range query is not renormalized, and missing joint states are not reconstructed.");
            }
            public void ResultRows(List<object> rows,string label)
            {
                if(rows.Count>4096)throw new ArgumentException("Numerical row tables support<=4096 returned rows.");
                List<string> keys=new List<string>();foreach(object raw in rows)foreach(string key in Map(raw).Keys)if(!keys.Contains(key)&&keys.Count<6)keys.Add(key);
                if(keys.Count==0)return;List<string[]> exact=new List<string[]>();double[] widths=new double[keys.Count];for(int i=0;i<widths.Length;i++)widths[i]=528.0/keys.Count;
                foreach(object raw in rows){Dictionary<string,object> row=Map(raw);string[] cells=new string[keys.Count];for(int i=0;i<keys.Count;i++){object item=Get(row,keys[i]);cells[i]=item==null?"Not returned":item is IDictionary || item is IList || item is Array?Json.Serialize(item):Str(item);}exact.Add(cells);}
                Keep(110);Heading(label+" / recorded rows");Table(keys.ToArray(),exact,widths);
                foreach(string field in new string[]{"expectation","correlation","probability"}){List<object> numbers=new List<object>();bool complete=true;foreach(object raw in rows){double v;if(TryNumber(Get(Map(raw),field),out v))numbers.Add(v);else complete=false;}if(complete)Values(numbers,label+"."+field+" (row offset)");}
                Paragraph("Exact returned rows; chart row offsets follow table order. Algebraic checks and probabilities are not sampled measurement counts.");
            }
            public void ComplexMatrix(List<object> rows,string label)
            {
                int nr=Math.Min(rows.Count,16),nc=Math.Min(Items(rows[0]).Count,16);double cell=Math.Min(26,360.0/Math.Max(1,nc)),h=nr*cell+60;Keep(h+50);Heading(label+" / magnitude and phase");Space(h);double top=y-15,start=Left+75;
                for(int j=0;j<nc;j++)Text(start+j*cell+3,top+8,j.ToString(Inv),7,false,true,false);
                for(int i=0;i<nr;i++){Text(Left+48,top-i*cell-cell/2,i.ToString(Inv),7,false,true,false);List<object> row=Items(rows[i]);if(row.Count<nc)throw new ArgumentException("Matrix rows must be rectangular.");for(int j=0;j<nc;j++){List<object> a=Items(row[j]);if(a.Count!=2)throw new ArgumentException("Complex matrix entry must be [real,imaginary].");double re=Number(a[0]),im=Number(a[1]),scale=Math.Max(Math.Abs(re),Math.Abs(im)),mag=scale==0?0:scale*Math.Sqrt((re/scale)*(re/scale)+(im/scale)*(im/scale)),phase=Math.Atan2(im,re),v=Math.Min(1,mag);if(System.Double.IsInfinity(mag))throw new ArgumentException("Returned matrix magnitude exceeds finite chart range; use complete TXT/JSON.");Fill(1-.8*v,1-.4*v,1-.25*v);Rect(start+j*cell,top-(i+1)*cell,cell-1,cell-1,true);if(mag>0){Stroke(.12,.15,.3);double cx=start+j*cell+cell/2,cy=top-(i+.5)*cell,r=cell*.3;Line(cx,cy,cx+r*Math.Cos(phase),cy+r*Math.Sin(phase));}}}
                y-=h;Paragraph("Cell darkness encodes magnitude clipped at 1; direction encodes complex phase in radians (right = 0, up = pi/2). Zero entries have no phase arrow. Plotted "+nr+"x"+nc+" of returned matrix "+rows.Count+"x"+Items(rows[0]).Count+"; exact values remain in TXT/JSON.");
                Keep(110);Heading("Exact plotted matrix entries [real, imaginary]");
                for(int first=0;first<nc;first+=4){int columns=Math.Min(4,nc-first);string[] headers=new string[columns+1];double[] widths=new double[columns+1];headers[0]="Row";widths[0]=40;for(int j=0;j<columns;j++){headers[j+1]="Column "+(first+j).ToString(Inv);widths[j+1]=488.0/columns;}List<string[]> exact=new List<string[]>();for(int i=0;i<nr;i++){string[] cells=new string[columns+1];cells[0]=i.ToString(Inv);List<object> row=Items(rows[i]);for(int j=0;j<columns;j++){List<object> a=Items(row[first+j]);cells[j+1]="["+Number(a[0]).ToString("R",Inv)+", "+Number(a[1]).ToString("R",Inv)+"]";}exact.Add(cells);}Table(headers,exact,widths);}
            }
            private void Text(double x, double yy, string text, double size, bool bold, bool mono, bool white)
            {
                string clean=Clean(text);if(clean.Length>MaximumPdfTextCharacters-textCharacters)throw new ArgumentException("PDF text exceeds the 4 MiB document budget. Use complete TXT/JSON, per-command PDFs or smaller recorded batches.");textCharacters+=clean.Length;
                page.Append(white ? "1 1 1 rg\n" : "0.08 0.19 0.25 rg\n"); page.Append("BT /").Append(mono ? "F3" : bold ? "F2" : "F1").Append(' ').Append(N(size)).Append(" Tf 1 0 0 1 ").Append(N(x)).Append(' ').Append(N(yy)).Append(" Tm (").Append(Escape(clean)).Append(") Tj ET\n");
            }
            private void Fill(double r, double g, double b) { page.Append(N(r)).Append(' ').Append(N(g)).Append(' ').Append(N(b)).Append(" rg\n"); }
            private void Stroke(double r, double g, double b) { page.Append(N(r)).Append(' ').Append(N(g)).Append(' ').Append(N(b)).Append(" RG\n0.7 w\n"); }
            private void Rect(double x, double yy, double w, double h, bool fill) { page.Append(N(x)).Append(' ').Append(N(yy)).Append(' ').Append(N(w)).Append(' ').Append(N(h)).Append(fill ? " re f\n" : " re S\n"); }
            private void Line(double x1, double y1, double x2, double y2) { page.Append(N(x1)).Append(' ').Append(N(y1)).Append(" m ").Append(N(x2)).Append(' ').Append(N(y2)).Append(" l S\n"); }
            private void Ellipse(double cx, double cy, double rx, double ry, bool fill = false)
            {
                double k = .5522847498;
                page.Append(N(cx + rx)).Append(' ').Append(N(cy)).Append(" m\n");
                Curve(cx + rx, cy + k * ry, cx + k * rx, cy + ry, cx, cy + ry); Curve(cx - k * rx, cy + ry, cx - rx, cy + k * ry, cx - rx, cy); Curve(cx - rx, cy - k * ry, cx - k * rx, cy - ry, cx, cy - ry); Curve(cx + k * rx, cy - ry, cx + rx, cy - k * ry, cx + rx, cy); page.Append(fill ? "f\n" : "S\n");
            }
            private void Curve(params double[] a) { foreach (double d in a) page.Append(N(d)).Append(' '); page.Append("c\n"); }
            public byte[] Finish()
            {
                List<byte[]> objects = new List<byte[]>(); objects.Add(null); objects.Add(null);
                objects.Add(Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")); objects.Add(Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>")); objects.Add(Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"));
                objects.Add(Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Italic >>"));
                List<int> pageIds = new List<int>();int streamBytes=0;
                foreach (StringBuilder content in pages)
                {
                    if(content.Length>MaximumPdfBytes-streamBytes)throw new ArgumentException("PDF exceeds the 32 MiB document budget. Use complete TXT/JSON or smaller recorded batches.");byte[] stream = Bytes(content.ToString());streamBytes+=stream.Length; int streamId = objects.Count + 1;
                    using (MemoryStream m = new MemoryStream()) { byte[] h = Bytes("<< /Length " + stream.Length.ToString(Inv) + " >>\nstream\n"); m.Write(h, 0, h.Length); m.Write(stream, 0, stream.Length); byte[] e = Bytes("\nendstream"); m.Write(e, 0, e.Length); objects.Add(m.ToArray()); }
                    int pageId = objects.Count + 1; pageIds.Add(pageId);
                    objects.Add(Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R /F4 6 0 R >> >> /Contents " + streamId.ToString(Inv) + " 0 R >>"));
                }
                StringBuilder kids = new StringBuilder(); foreach (int id in pageIds) kids.Append(id.ToString(Inv)).Append(" 0 R ");
                objects[0] = Bytes("<< /Type /Catalog /Pages 2 0 R >>"); objects[1] = Bytes("<< /Type /Pages /Kids [" + kids + "] /Count " + pages.Count.ToString(Inv) + " >>");
                using (MemoryStream pdf = new MemoryStream())
                {
                    Write(pdf, "%PDF-1.4\n% SpinDoctor vector report\n"); List<long> offsets = new List<long>();
                    for (int i = 0; i < objects.Count; i++) {if(pdf.Length+objects[i].Length+64>MaximumPdfBytes)throw new ArgumentException("PDF exceeds the 32 MiB document budget. Use full native TXT/JSON or smaller command batches.");offsets.Add(pdf.Position); Write(pdf, (i + 1).ToString(Inv) + " 0 obj\n"); pdf.Write(objects[i], 0, objects[i].Length); Write(pdf, "\nendobj\n"); }
                    long xref = pdf.Position; Write(pdf, "xref\n0 " + (objects.Count + 1).ToString(Inv) + "\n0000000000 65535 f \n"); foreach (long offset in offsets) Write(pdf, offset.ToString("0000000000", Inv) + " 00000 n \n");
                    Write(pdf, "trailer\n<< /Size " + (objects.Count + 1).ToString(Inv) + " /Root 1 0 R >>\nstartxref\n" + xref.ToString(Inv) + "\n%%EOF\n");if(pdf.Length>MaximumPdfBytes)throw new ArgumentException("PDF exceeds the 32 MiB document budget; use complete native TXT/JSON."); return pdf.ToArray();
                }
            }
            private static void Write(Stream s, string text) { byte[] b = Bytes(text); s.Write(b, 0, b.Length); }
            private static byte[] Bytes(string text) { return Encoding.ASCII.GetBytes(text); }
            private static string N(double v) { return v.ToString("0.###", Inv); }
            private static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)"); }
            private static string Clean(string text)
            {
                if (text == null) return "";
                text = text.Replace("α", "alpha").Replace("β", "beta").Replace("θ", "theta").Replace("φ", "phi").Replace("λ", "lambda").Replace("γ", "gamma").Replace("π", "pi").Replace("ψ", "psi").Replace("−", "-").Replace("–", "-").Replace("—", "-").Replace("×", "x").Replace("→", "->").Replace("…", "...").Replace("²", "^2").Replace("³", "^3");
                StringBuilder a = new StringBuilder(); foreach (char c in text) if (c == '\r' || c == '\n' || c == '\t') a.Append(' '); else if (c >= 32 && c <= 126) a.Append(c); else a.Append("[U+").Append(((int)c).ToString("X4", Inv)).Append(']'); return a.ToString();
            }
            private static List<string> Wrap(string raw, int length)
            {
                string text = Clean(raw); List<string> result = new List<string>();int start=0;
                while (text.Length-start > length)
                {
                    int at = text.LastIndexOf(' ', start+length, length+1); if (at-start < length / 3) at = start+length;
                    result.Add(text.Substring(start, at-start));start=at;while(start<text.Length&&Char.IsWhiteSpace(text[start]))start++;
                }
                result.Add(text.Substring(start)); return result;
            }
        }
    }
}
