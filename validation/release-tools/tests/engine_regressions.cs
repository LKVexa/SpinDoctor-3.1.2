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
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using QuettaQuantum;

public static class EngineRegressions
{
    private static int assertions;
    private static readonly List<string> tests=new List<string>();
    private static void Check(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
    private static void Near(Complex a,Complex b,string message){Check(Complex.Abs(a-b)<3e-10,message+" observed="+a+" expected="+b);}
    private static Dictionary<string,object> D(params object[] p){return SpinDoctorCalculators.D(p);}
    private static ulong Count(Dictionary<string,object> d,string key){return UInt64.Parse(Convert.ToString(d[key],CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);}
    private static void Throws(Action action,string text){try{action();}catch(Exception error){while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;Check(error.Message.IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0,"Exception wording: "+error.Message);return;}throw new Exception("Expected rejection containing "+text);}
    private static void Test(string name,Action action){action();tests.Add(name);Console.WriteLine("PASS "+name);}
    private static Complex[,] ReferenceLocal(string name,double[] a)
    {
        Complex[,] m=new Complex[2,2];Complex ii=Complex.ImaginaryOne;double angle=a.Length==0?0:a[0],c=Math.Cos(angle/2),s=Math.Sin(angle/2),h=1/Math.Sqrt(2);
        switch(name){case "i":m[0,0]=m[1,1]=1;break;case "x":m[0,1]=m[1,0]=1;break;case "y":m[0,1]=-ii;m[1,0]=ii;break;case "z":m[0,0]=1;m[1,1]=-1;break;case "h":m[0,0]=m[0,1]=m[1,0]=h;m[1,1]=-h;break;case "rx":m[0,0]=m[1,1]=c;m[0,1]=m[1,0]=-ii*s;break;case "ry":m[0,0]=m[1,1]=c;m[0,1]=-s;m[1,0]=s;break;case "rz":m[0,0]=Complex.FromPolarCoordinates(1,-angle/2);m[1,1]=Complex.FromPolarCoordinates(1,angle/2);break;case "u":m[0,0]=c;m[0,1]=-s*Complex.FromPolarCoordinates(1,a[2]);m[1,0]=s*Complex.FromPolarCoordinates(1,a[1]);m[1,1]=c*Complex.FromPolarCoordinates(1,a[1]+a[2]);break;case "sx":case "sxdg":Complex t=name=="sx"?ii:-ii;m[0,0]=m[1,1]=(1+t)/2;m[0,1]=m[1,0]=(1-t)/2;break;default:double phase=name=="s"?Math.PI/2:name=="sdg"?-Math.PI/2:name=="t"?Math.PI/4:name=="tdg"?-Math.PI/4:angle;m[0,0]=1;m[1,1]=Complex.FromPolarCoordinates(1,phase);break;}return m;
    }
    private static Complex[,] ReferenceGate(string name,double[] a,int width)
    {
        if(width==1)return ReferenceLocal(name,a);int dim=1<<width;Complex[,] m=new Complex[dim,dim];
        for(int x=0;x<dim;x++)
        {
            if(name=="swap"||name=="cswap"||name=="ccx")
            {int y=x;if(name=="swap")y=((x&1)<<1)|((x&2)>>1);else if(name=="cswap"&&(x&1)!=0)y=(x&1)|((x&2)<<1)|((x&4)>>1);else if(name=="ccx"&&(x&3)==3)y=x^4;m[y,x]=1;continue;}
            if(name=="rxx"||name=="ryy"||name=="rzz"||name=="rzx")
            {int y=x;Complex phase=1;if(name=="rxx")y=x^3;if(name=="ryy"){y=x^3;phase=-((x&1)==0?1:-1)*((x&2)==0?1:-1);}if(name=="rzz")phase=((x&1)==0?1:-1)*((x&2)==0?1:-1);if(name=="rzx"){y=x^2;phase=(x&1)==0?1:-1;}m[x,x]+=Math.Cos(a[0]/2);m[y,x]+=-Complex.ImaginaryOne*Math.Sin(a[0]/2)*phase;continue;}
            if((x&1)==0){m[x,x]=1;continue;}double[] localAngles=name=="cu"?new double[]{a[0],a[1],a[2]}:a;Complex[,] u=ReferenceLocal(name.Substring(1),localAngles);Complex branch=name=="cu"?Complex.FromPolarCoordinates(1,a[3]):Complex.One;for(int y=0;y<2;y++)m[1+2*y,x]=branch*u[y,x>>1];
        }
        return m;
    }
    private static Complex[] Apply(Complex[] state,Complex[,] matrix,int[] operands)
    {
        Complex[] output=new Complex[state.Length];int mask=0;foreach(int q in operands)mask|=1<<q;for(int x=0;x<state.Length;x++){int local=0;for(int k=0;k<operands.Length;k++)if((x&(1<<operands[k]))!=0)local|=1<<k;for(int y=0;y<matrix.GetLength(0);y++){int global=x&~mask;for(int k=0;k<operands.Length;k++)if((y&(1<<k))!=0)global|=1<<operands[k];output[global]+=matrix[y,local]*state[x];}}return output;
    }
    private static TrancheRegister Basis(int n,int value){double[][] spinors=new double[n][];for(int q=0;q<n;q++)spinors[q]=(value&(1<<q))==0?new double[]{1,0,0,0}:new double[]{0,0,1,0};return new TrancheRegister(n,spinors);}
    private static Complex A(TrancheRegister r,int id){double[] a=r.Amplitude((ulong)id);return new Complex(a[0],a[1]);}
    private static double[] Params(int n){double[] a=new double[n];for(int k=0;k<n;k++)a[k]=.317+k*.241;return a;}
    // 3.1.2 update: mcx7 (seven controls + target) has no explicit matrix by design; verify refusal and native basis action on 9 qubits.
    private static void CheckWideControlledX(string name,int width)
    {
        Check(name=="mcx7"&&width==8,"unexpected wide gate "+name+" width "+width);
        bool refused=false;try{SpinDoctorCalculators.GateMatrix(name,new double[0]);}catch(ArgumentException){refused=true;}Check(refused,"mcx7 explicit matrix is refused");
        foreach(int[] ops in new int[][]{new int[]{0,1,2,3,4,5,6,7},new int[]{8,6,4,2,0,1,3,5}})
        {
            int ctrl=0;for(int k=0;k<7;k++)ctrl|=1<<ops[k];int tgt=1<<ops[7];
            foreach(int basis in new int[]{0,ctrl,ctrl|tgt,ctrl&~(1<<ops[3]),tgt,(ctrl&~(1<<ops[0]))|tgt,511,255})
                using(TrancheRegister r=Basis(9,basis)){r.Gate(name,new double[0],ops,false);int want=(basis&ctrl)==ctrl?basis^tgt:basis;for(int id=0;id<512;id++)Near(A(r,id),id==want?Complex.One:Complex.Zero,name+" on "+String.Join(",",ops)+" basis "+basis+" id "+id);}
        }
    }
    private static void CheckGates()
    {
        foreach(Dictionary<string,object> spec in TrancheRegister.SupportedGates)
        {
            string name=(string)spec["name"];int width=(int)spec["targetCount"];if(width>3){CheckWideControlledX(name,width);continue;}double[] a=Params((int)spec["angleCount"]);Complex[,] expected=ReferenceGate(name,a,width),actual=SpinDoctorCalculators.GateMatrix(name,a);for(int x=0;x<expected.GetLength(0);x++)for(int y=0;y<expected.GetLength(0);y++)Near(actual[x,y],expected[x,y],name+" matrix");
            foreach(int[] operands in width==1?new int[][]{new int[]{0},new int[]{2}}:width==2?new int[][]{new int[]{0,2},new int[]{2,0}}:new int[][]{new int[]{0,1,2},new int[]{2,0,1}})
                for(int basis=0;basis<8;basis++)using(TrancheRegister r=Basis(3,basis)){r.Gate(name,a,operands,false);Complex[] initial=new Complex[8];initial[basis]=1;Complex[] output=Apply(initial,expected,operands);for(int id=0;id<8;id++)Near(A(r,id),output[id],name+" on "+String.Join(",",operands)+" column "+basis);}
        }
        using(TrancheRegister r=new TrancheRegister(3))
        {Complex[] state=new Complex[8];state[0]=1;Random random=new Random(4729);for(int iteration=0;iteration<160;iteration++){Dictionary<string,object>[] specs=Array.FindAll(TrancheRegister.SupportedGates,g=>(int)g["targetCount"]<=3);Dictionary<string,object> spec=specs[random.Next(specs.Length)];int width=(int)spec["targetCount"];int[] operands=width==1?new int[]{iteration%3}:width==2?new int[]{2,0}:new int[]{1,2,0};double[] a=Params((int)spec["angleCount"]);r.Gate((string)spec["name"],a,operands,false);state=Apply(state,ReferenceGate((string)spec["name"],a,width),operands);for(int id=0;id<8;id++)Near(A(r,id),state[id],"composed reference gate "+iteration);}}
    }
    private static string Checkpoint(string directory,string filename,int n,bool dense,int support)
    {
        string path=Path.Combine(directory,filename);using(MemoryStream content=new MemoryStream())
        {using(BinaryWriter writer=new BinaryWriter(content,Encoding.UTF8,true)){writer.Write("SPINDOCTOR-TRANCHE-2");writer.Write(n);writer.Write(512UL*1024*1024);for(int k=0;k<5;k++)writer.Write("0");writer.Write(1);writer.Write(n);for(int k=0;k<n;k++)writer.Write(k);writer.Write(dense);writer.Write((ulong)support);for(int k=0;k<support;k++){writer.Write((ulong)k);writer.Write(1/Math.Sqrt(support));writer.Write(0.0);}}WriteSigned(path,content.ToArray());}return path;
    }
    private static void WriteSigned(string path,byte[] data){byte[] digest;using(SHA256 hash=SHA256.Create())digest=hash.ComputeHash(data);using(FileStream file=File.Create(path)){file.Write(data,0,data.Length);file.Write(digest,0,digest.Length);}}
    private static void DenseCancellation(string directory)
    {
        string path=Checkpoint(directory,"dense-cancel.bin",14,true,16384);using(TrancheRegister r=TrancheRegister.Load(path,16UL*1024*1024))
        {ulong before=Count(r.Status(),"actualAmplitudeWrites");bool merged=false;Throws(delegate(){r.Gate("cx",new double[0],new int[]{0,13},false,delegate(){return merged;},delegate(ulong done,ulong total){merged=true;});},"cancelled");ulong delta=Count(r.Status(),"actualAmplitudeWrites")-before;Check(delta<=16384,"Controlled dense gate must poll before eligible pairs; writes="+delta);for(int id=0;id<16384;id++)Near(A(r,id),new Complex(1/128.0,0),"Cancellation rollback");}
    }
    private static void DenseBudget(string directory)
    {
        string path=Checkpoint(directory,"dense-budget.bin",21,true,512);using(TrancheRegister r=TrancheRegister.Load(path,40UL*1024*1024))
        {ulong before=Count(r.Status(),"actualAmplitudeWrites"),bytes=Count(r.Status(),"allocatedStateBytesEstimate");Throws(delegate(){r.Gate("h",new double[0],new int[]{20},false);},"budget");Check(Count(r.Status(),"actualAmplitudeWrites")==before,"Reject new dense tile before amplitude writes");Check(Count(r.Status(),"allocatedStateBytesEstimate")==bytes,"Budget rejection retains storage");Near(A(r,0),new Complex(1/Math.Sqrt(512),0),"Budget rejection preserves amplitude");Near(A(r,1<<20),Complex.Zero,"Budget rejection preserves empty tile");}
    }
    private static void Collapse(string directory)
    {
        foreach(string basis in new string[]{"x","y","z"})foreach(double draw in new double[]{0,.999999})using(TrancheRegister r=new TrancheRegister(3))
        {r.Gate("h",new double[0],new int[]{0},false);r.Gate("cx",new double[0],new int[]{0,1},false);r.Gate("cx",new double[0],new int[]{0,2},false);Dictionary<string,object> result=r.Measure(1,basis,1,delegate(){return draw;},true);int outcome=(int)result["collapseOutcome"];Complex e0,e1;double h=1/Math.Sqrt(2);if(basis=="z"){e0=outcome==0?Complex.One:Complex.Zero;e1=outcome==1?Complex.One:Complex.Zero;}else{e0=h;e1=basis=="x"?new Complex(outcome==0?h:-h,0):new Complex(0,outcome==0?h:-h);}Complex[] original=new Complex[8];original[0]=original[7]=h;Complex[] expected=new Complex[8];double norm=0;for(int x=0;x<8;x++)if((x&2)==0){Complex contraction=Complex.Conjugate(e0)*original[x]+Complex.Conjugate(e1)*original[x|2];expected[x]=e0*contraction;expected[x|2]=e1*contraction;norm+=contraction.Magnitude*contraction.Magnitude;}for(int x=0;x<8;x++)Near(A(r,x),expected[x]/Math.Sqrt(norm),"GHZ "+basis+" collapse branch "+outcome);string checkpoint=Path.Combine(directory,"collapse-"+basis+"-"+outcome+".bin");r.Save(checkpoint);using(TrancheRegister restored=TrancheRegister.Load(checkpoint,4UL*1024*1024)){for(int x=0;x<8;x++)Near(A(restored,x),A(r,x),"Collapse checkpoint roundtrip");}}
    }
    private static void CheckpointRejection(string directory)
    {
        string path=Checkpoint(directory,"corrupt.bin",3,false,8);byte[] data=File.ReadAllBytes(path);data[12]^=0x40;File.WriteAllBytes(path,data);Throws(delegate(){using(TrancheRegister r=TrancheRegister.Load(path,4UL*1024*1024)){}},"checksum");Check(File.ReadAllBytes(path)[12]==data[12],"Corrupt checkpoint preserved");string malicious=Path.Combine(directory,"oversized-string.bin");WriteSigned(malicious,new byte[]{0xff,0xff,0xff,0xff,0x07});Throws(delegate(){using(TrancheRegister r=TrancheRegister.Load(malicious,4UL*1024*1024)){}},"bound");
    }
    private static void MeasurementCancellation(string directory)
    {
        string path=Checkpoint(directory,"measurement-cancel.bin",14,true,16384);using(TrancheRegister r=TrancheRegister.Load(path,16UL*1024*1024))
        {bool drawn=false;ulong before=Count(r.Status(),"actualAmplitudeWrites");Throws(delegate(){r.Measure(7,"z",1,delegate(){drawn=true;return .25;},true,delegate(){return drawn;});},"cancelled");Check(drawn,"Measurement cancellation exercised after a consumed draw");Check(Count(r.Status(),"measurementShots")==1,"Consumed measurement counted");Check(Count(r.Status(),"actualAmplitudeWrites")==before,"Collapse cancellation occurs before amplitude writes");for(int id=0;id<16384;id++)Near(A(r,id),new Complex(1/128.0,0),"Cancelled dense measurement exact rollback");}
    }
    private static void MetadataRejection(string directory)
    {
        string workspace=Path.Combine(directory,"invalid-metadata");Directory.CreateDirectory(workspace);using(SpinDoctorTrancheTerminal t=new SpinDoctorTrancheTerminal(workspace,"seed")){t.Execute(new string[]{"q.tranche","prepare","3","zero"},1,1,1,QuantumMath.Identity,"descriptor");}string path=Path.Combine(workspace,"spindoctor-tranche.json"),original=File.ReadAllText(path);JavaScriptSerializer json=new JavaScriptSerializer();
        foreach(string scenario in new string[]{"size","spinor","journal"}){Dictionary<string,object> state=(Dictionary<string,object>)json.DeserializeObject(original);if(scenario=="size")state["qubits"]=3.5;else if(scenario=="spinor")state["initialSpinorsExact"]=new object[]{new string[]{"1","0"},new string[]{"1","0"},new string[]{"1","0"}};else state["journal"]=new object[]{D("operation","gate","name","x","anglesExact",new string[0],"targets",new object[]{2.5},"all",false)};string corrupted=json.Serialize(state);File.WriteAllText(path,corrupted);Throws(delegate(){using(SpinDoctorTrancheTerminal t=new SpinDoctorTrancheTerminal(workspace,"seed")){}},"rejected and preserved");Check(File.ReadAllText(path)==corrupted,"Invalid tranche metadata preserved without overwrite");}File.WriteAllText(path,original);
    }
    private static void TrancheRandomFailure(string directory)
    {
        string workspace=Path.Combine(directory,"tranche-rng");Directory.CreateDirectory(workspace);string path=Checkpoint(directory,"rng.bin",16,true,256);TrancheRegister r=TrancheRegister.Load(path,4UL*1024*1024);ulong live=Count(r.Status(),"allocatedStateBytesEstimate");r.SetMemoryLimit(live);using(SpinDoctorTrancheTerminal terminal=new SpinDoctorTrancheTerminal(workspace,"seed"))
        {BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;typeof(SpinDoctorTrancheTerminal).GetField("register",flags).SetValue(terminal,r);typeof(SpinDoctorTrancheTerminal).GetField("qubits",flags).SetValue(terminal,16);typeof(SpinDoctorTrancheTerminal).GetField("memoryLimit",flags).SetValue(terminal,live);Throws(delegate(){terminal.Execute(new string[]{"q.tranche","measure","0","basis","x","shots","1","collapse"},1,1,1,QuantumMath.Identity,"descriptor");},"budget");Check((string)terminal.GetStatus()["counter"]=="1","A failed collapse must consume its random draw");Check(File.ReadAllText(Path.Combine(workspace,"spindoctor-tranche.json")).Contains("\"measurementCounter\":\"1\""),"Failed draw persisted");}
        using(SpinDoctorTrancheTerminal restarted=new SpinDoctorTrancheTerminal(workspace,"seed")){Check((string)restarted.GetStatus()["counter"]=="1","Consumed draw survives restart");}
    }
    private static void NumericEdges()
    {
        foreach(double scale in new double[]{1e-300,1e300}){Complex[] v=SpinDoctorCalculators.Normalize(new Complex[]{new Complex(scale,scale),new Complex(scale,-scale)});Near(v[0],new Complex(.5,.5),"Scaled normalization");Near(v[1],new Complex(.5,-.5),"Scaled normalization");double[] eigen=SpinDoctorCalculators.HermitianEigenvalues(new Complex[,]{{0,scale},{scale,0}});Check(Math.Abs(eigen[0]/scale+1)<1e-12,"Tiny/large negative eigenvalue");Check(Math.Abs(eigen[1]/scale-1)<1e-12,"Tiny/large positive eigenvalue");}
        SpinDoctorCalculators calculator=new SpinDoctorCalculators();Dictionary<string,object> normalized=(Dictionary<string,object>)calculator.Execute(new string[]{"q.math","normalize","amplitudes=[1e300,1e300]"});Check(normalized["originalNormSquared"]==null,"Unrepresentable norm disclosed");Dictionary<string,object> grover=(Dictionary<string,object>)calculator.Execute(new string[]{"q.resource","grover","database-size="+"1"+new string('0',318),"marked="+"1"+new string('0',318)});Check(Math.Abs((double)grover["asymptoticQueries"]-Math.PI/4)<1e-12,"Huge exact integer resource ratio");Dictionary<string,object> shots=(Dictionary<string,object>)calculator.Execute(new string[]{"q.resource","shots","epsilon=0.1","delta=1e-310"});Check(BigInteger.Parse((string)shots["minimumShots"])>35000,"Stable tiny delta bound");Throws(delegate(){calculator.Execute(new string[]{"q.math","validate-bloch","x=1e300","y=0","z=0"});},"finite");Throws(delegate(){SpinDoctorCalculators.Integer("abc",0,29);},"Expected an integer");Throws(delegate(){SpinDoctorCalculators.Integer("1.5",0,29);},"Expected an integer");Throws(delegate(){SpinDoctorCalculators.Integer("30",0,29);},"must be in");
        foreach(string literal in new string[]{"1e-300","1e300"}){Dictionary<string,object> result=(Dictionary<string,object>)calculator.Execute(new string[]{"q.math","eigen","operator=[[0,"+literal+"],["+literal+",0]]"});object[] vectors=(object[])result["eigenvectors"];double[] eigen=(double[])result["eigenvalues"];for(int k=0;k<2;k++){object[] v=(object[])vectors[k];Complex[] state=new Complex[2];for(int q=0;q<2;q++){object[] z=(object[])v[q];state[q]=new Complex((double)z[0],(double)z[1]);}double sign=eigen[k]/Double.Parse(literal,CultureInfo.InvariantCulture);Near(state[1],sign*state[0],"Scaled operator eigenvector residual");}}
        Dictionary<string,object> tiny=(Dictionary<string,object>)calculator.Execute(new string[]{"q.math","normalize","amplitudes=[1e-300,1e-300]"});Check(tiny["originalNormSquared"]==null,"Underflowed original norm disclosed");Throws(delegate(){calculator.Execute(new string[]{"q.model","evolve","n=1","initial=[1,0]","hamiltonian=1e15*I","time=1"});},"global phase");
    }
    private static void Circuit(string directory)
    {
        string workspace=Path.Combine(directory,"circuits");Directory.CreateDirectory(workspace);ulong counter;
        using(CircuitTerminal c=new CircuitTerminal(workspace,"engine seed"))
        {c.Execute(new string[]{"q.circuit","preset","bell","run=false"});c.Execute(new string[]{"q.circuit","run","shots","17","basis","z"});Wait(delegate(){return (Dictionary<string,object>)c.GetStatus()["job"];});Dictionary<string,object> result=(Dictionary<string,object>)c.Execute(new string[]{"q.circuit","result"});Check((string)result["phase"]=="completed","Circuit completed");foreach(object item in (object[])result["histogram"]){Dictionary<string,object> row=(Dictionary<string,object>)item;Check((string)row["bitstring"]=="00"||(string)row["bitstring"]=="11","Bell correlated measurement");}counter=Count(c.GetStatus(),"counter");Check(counter==34,"Two draws per completed shot");CircuitQueryView leaked=null;c.WithQueryView(2,4,delegate(CircuitQueryView view){leaked=view;double[] full=view.FullState();Near(new Complex(full[0],full[1]),new Complex(1/Math.Sqrt(2),0),"Retained premeasurement state");return null;});Throws(delegate(){leaked.Spin(0);},"synchronous");c.MarkPresetOwned("bell","qc-001");Dictionary<string,object> spec=D("name","bell","qubits",2,"initialBasis","0","gates",new object[0]);c.ImportCircuit(spec);Check((int)c.GetStatus()["presetOwnedCount"]==0,"User imported program owns replacement");c.Execute(new string[]{"q.circuit","prune","presets"});Check((int)c.GetStatus()["programCount"]==1,"User import survives preset pruning");Throws(delegate(){c.Execute(new string[]{"q.circuit","capacity","abc"});},"Expected an integer");}
        using(CircuitTerminal c=new CircuitTerminal(workspace,"engine seed")){Check(Count(c.GetStatus(),"counter")==counter,"Circuit counter persists");Check((int)c.GetStatus()["programCount"]==1,"Circuit programs persist");}
    }
    private static void Wait(Func<Dictionary<string,object>> status){DateTime deadline=DateTime.UtcNow.AddSeconds(60);while((bool)status()["active"]){if(DateTime.UtcNow>deadline)throw new Exception("Worker timeout");Thread.Sleep(10);}}
    private static void Sequence(string directory,string manifest)
    {
        string workspace=Path.Combine(directory,"sequences");Directory.CreateDirectory(workspace);File.Copy(manifest,Path.Combine(workspace,"SPINDOCTOR_PRESETS.json"),true);List<string> calls=new List<string>();Dictionary<string,object> status=D("stateCount","30","circuit",D("programCount",1,"programCapacity",1,"presetOwnedCount",1,"programSummaries",new object[]{D("name","old","qubits",2)},"selected","old","program",D("qubits",2),"coherentOutputAvailable",false),"tranche",D("qubits",0));
        using(SequenceTerminal sequence=new SequenceTerminal(workspace,delegate(string command){calls.Add(command);return D("ok",true,"command",command,"result",D("pruned",new string[]{"old"}),"status",status);},delegate(){return status;}))
        {sequence.SetCapabilityResolver(delegate(string command){return D("available",true,"conditional",false,"reason","");});Dictionary<string,object> failed=(Dictionary<string,object>)sequence.Run(new string[]{"q.circuit new fresh 2","q.circuit add cx 0 3"},"qc-001",null);Check(!(bool)failed["queued"],"Invalid preset sequence fails preflight");Check(calls.Count==0,"Invalid preflight must preserve saved preset circuits");Dictionary<string,object> partial=(Dictionary<string,object>)sequence.Run(new string[]{"q.tranche from 1","q.tranche gate x 1"},null,null);Check(!(bool)partial["queued"],"Final one-qubit tranche operand rejected in preflight");Check(calls.Count==0,"Out-of-range tranche index preflight has no effects");Dictionary<string,object> valid=(Dictionary<string,object>)sequence.Run(new string[]{"q.circuit new fresh 2"},"qc-001",null);Check((bool)valid["queued"],"Valid preset sequence queued");Wait(delegate(){return (Dictionary<string,object>)sequence.GetStatus()["job"];});Check(calls.Count==2&&calls[0].StartsWith("q.circuit prune presets "),"Valid preset prunes only after preflight");}
    }
    private static void SequenceCheckpointFailure(string directory)
    {
        string workspace=Path.Combine(directory,"sequence-checkpoint-failure");Directory.CreateDirectory(workspace);using(SequenceTerminal sequence=new SequenceTerminal(workspace,delegate(string command){return D("ok",true,"result",D());},delegate(){return D();}))
        {string jobs=Path.Combine(workspace,"sequence-jobs"),held=jobs+".held";Directory.Move(jobs,held);File.WriteAllText(jobs,"blocked");try{Throws(delegate(){sequence.Run(new string[]{"q.status"},null,null);},"path");Dictionary<string,object> job=(Dictionary<string,object>)sequence.GetStatus()["job"];Check(!(bool)job["active"]&&(string)job["phase"]=="failed","Queue checkpoint failure cannot strand an active job");}finally{File.Delete(jobs);Directory.Move(held,jobs);}}
    }
    private static Dictionary<string,object> Native(TerminalSession session,string command)
    {
        Dictionary<string,object> response=(Dictionary<string,object>)session.Execute(command);
        Check((bool)response["ok"],"Native command failed: "+command+" "+(response.ContainsKey("error")?response["error"]:""));
        return (Dictionary<string,object>)response["result"];
    }
    private static Dictionary<string,object> NativeSequence(TerminalSession session,string[] commands,string presetId)
    {
        Dictionary<string,object> request=D("commands",commands);if(presetId!=null)request["presetId"]=presetId;
        Dictionary<string,object> response=(Dictionary<string,object>)session.StartSequence(request);
        return (Dictionary<string,object>)response["result"];
    }
    private static Dictionary<string,object> FamilyStatus(TerminalSession session){return (Dictionary<string,object>)session.GetStatus();}
    private static Dictionary<string,object> SessionJob(TerminalSession session,string name){return (Dictionary<string,object>)((Dictionary<string,object>)FamilyStatus(session)[name])["job"];}
    private static void CompletedSequence(TerminalSession session,Dictionary<string,object> result,int commands)
    {
        Check((bool)result["queued"],"Reset sequence passed preflight: "+(result.ContainsKey("error")?result["error"]:""));
        Wait(delegate(){return SessionJob(session,"sequence");});Dictionary<string,object> job=SessionJob(session,"sequence");
        Check((string)job["phase"]=="completed"&&Convert.ToInt32(job["completedCommands"],CultureInfo.InvariantCulture)==commands,"Reset sequence completed every native step");
    }
    private static void ResetContext(string directory,string manifest)
    {
        string workspace=Path.Combine(directory,"reset-context");Directory.CreateDirectory(workspace);File.Copy(manifest,Path.Combine(workspace,"SPINDOCTOR_PRESETS.json"),true);
        using(TerminalSession session=new TerminalSession(workspace,false))
        {
            CompletedSequence(session,NativeSequence(session,new string[]{"q.circuit new reset_preset 2","q.circuit input 3","q.circuit run shots 1 basis z"},"qc-085"),3);
            Native(session,"q.circuit new reset_user 2");Native(session,"q.circuit input 3");Native(session,"q.circuit run shots 1 basis z");Wait(delegate(){return SessionJob(session,"circuit");});
            Native(session,"q.prepare grid 30 1");Native(session,"q.migrate t 1");Native(session,"q.gate x all");Native(session,"q.measure 0 basis z shots 3");Native(session,"q.tranche from 1");Native(session,"q.tranche measure 0 basis z shots 2");
            double[][] trancheBefore=new double[2][];for(int i=0;i<2;i++)trancheBefore[i]=(double[])Native(session,"q.tranche state "+i)["amplitude"];
            Dictionary<string,object> circuitState=Native(session,"q.circuit state 3");double[] circuitBefore=(double[])((Dictionary<string,object>)circuitState["result"])["amplitude"];
            Dictionary<string,object> before=FamilyStatus(session),tranche=(Dictionary<string,object>)before["tranche"],circuit=(Dictionary<string,object>)before["circuit"];
            string binding=session.Serialize(tranche["modelBinding"]);
            Check((int)tranche["qubits"]==1,"Initial tranche is the partial snapshot of the 30-address family");
            Check((int)circuit["programCount"]==2&&(int)circuit["presetOwnedCount"]==1,"One user circuit and one preset circuit have separate ownership");
            CompletedSequence(session,NativeSequence(session,new string[]{"q.prepare grid 1 1","q.reset","q.state 1","q.state 499999999999999","q.tranche state 1","q.circuit state 3"},null),6);
            Dictionary<string,object> after=FamilyStatus(session),afterTranche=(Dictionary<string,object>)after["tranche"],afterCircuit=(Dictionary<string,object>)after["circuit"];
            Check((string)after["gridM"]=="20000000"&&(string)after["gridL"]=="25000000"&&(string)after["stateCount"]=="500000000000000","Reset restores the native default finite domain");
            Check(!(bool)after["hasPrepared"]&&(double)after["migrationT"]==0,"Reset restores the unprepared identity descriptor");
            Check((string)after["representedPreparations"]=="0"&&(string)after["representedGateApplications"]=="0","Reset clears current-generation represented credit");
            foreach(string key in new string[]{"fieldSeed","measurementSeed","measurementCounter","measurementShots","lifetimeRepresentedPreparations","lifetimeRepresentedGateApplications"})Check(Convert.ToString(after[key],CultureInfo.InvariantCulture)==Convert.ToString(before[key],CultureInfo.InvariantCulture),"Reset preserves family "+key);
            Check(Count(after,"actualCpuStates")==Count(before,"actualCpuStates")+2,"Reset preserves actual family work and counts the two sequence queries");
            Check((int)afterTranche["qubits"]==1&&session.Serialize(afterTranche["modelBinding"])==binding,"Family reset retains the independent original tranche snapshot binding");
            Check((string)afterTranche["seed"]==(string)tranche["seed"]&&(string)afterTranche["counter"]==(string)tranche["counter"],"Family reset preserves tranche random history");
            for(int i=0;i<2;i++){double[] a=(double[])Native(session,"q.tranche state "+i)["amplitude"];Near(new Complex(a[0],a[1]),new Complex(trancheBefore[i][0],trancheBefore[i][1]),"Family reset preserves tranche amplitude "+i);}
            Check((string)afterCircuit["selected"]=="reset_user"&&(int)afterCircuit["programCount"]==2&&(int)afterCircuit["presetOwnedCount"]==1&&(bool)afterCircuit["coherentOutputAvailable"],"Family reset preserves selected circuit, ownership and retained coherent state");
            Check((string)afterCircuit["counter"]==(string)circuit["counter"]&&(string)afterCircuit["measurementSeed"]==(string)circuit["measurementSeed"],"Family reset preserves circuit random history");
            double[] coherent=(double[])((Dictionary<string,object>)Native(session,"q.circuit state 3")["result"])["amplitude"];Near(new Complex(coherent[0],coherent[1]),new Complex(circuitBefore[0],circuitBefore[1]),"Family reset preserves coherent circuit amplitude");
            Dictionary<string,object> state=Native(session,"q.state 499999999999999"),amplitudes=(Dictionary<string,object>)state["amplitudes"];double[] alpha=(double[])amplitudes["alpha"],beta=(double[])amplitudes["beta"];Near(new Complex(alpha[0],alpha[1]),Complex.One,"Reset default alpha");Near(new Complex(beta[0],beta[1]),Complex.Zero,"Reset default beta");
            string generation=(string)FamilyStatus(session)["modelGeneration"];Dictionary<string,object> invalid=NativeSequence(session,new string[]{"q.reset","q.state 500000000000000"},null);
            Check(!(bool)invalid["queued"]&&(string)FamilyStatus(session)["modelGeneration"]==generation,"Default-domain upper bound is rejected before reset changes state");
            invalid=NativeSequence(session,new string[]{"q.prepare grid 1 1","q.reset","q.tranche from 17241379310344","q.tranche spin 24"},null);
            Check(!(bool)invalid["queued"]&&(string)FamilyStatus(session)["modelGeneration"]==generation,"Default final tranche has only 24 qubits and failed preflight has no reset effect");
            CompletedSequence(session,NativeSequence(session,new string[]{"q.prepare grid 1 1","q.reset","q.tranche from 17241379310344","q.tranche spin 23"},null),4);
            afterTranche=(Dictionary<string,object>)FamilyStatus(session)["tranche"];Dictionary<string,object> last=(Dictionary<string,object>)afterTranche["modelBinding"];
            Check((int)afterTranche["qubits"]==24&&(string)last["firstFamilyAddress"]=="499999999999976"&&(string)last["lastFamilyAddress"]=="499999999999999"&&(bool)last["partialFinalTranche"],"Reset preflight and native execution agree on the default final tranche");
        }
    }
    public static string Run(string directory,string manifest)
    {
        Directory.CreateDirectory(directory);Test("all supported gate matrices, reversed/nonadjacent operands and 160-gate composition",CheckGates);Test("controlled dense gate cancellation and exact rollback",delegate(){DenseCancellation(directory);});Test("new dense tile memory budget checked before writes",delegate(){DenseBudget(directory);});Test("GHZ X/Y/Z projective branches, phase and binary roundtrip",delegate(){Collapse(directory);});Test("dense measurement cancellation after random draw and exact rollback",delegate(){MeasurementCancellation(directory);});Test("checkpoint checksum and bounded hostile string lengths",delegate(){CheckpointRejection(directory);});Test("fractional size/operands and malformed spinor metadata rejected intact",delegate(){MetadataRejection(directory);});Test("failed tranche collapse random draw survives restart",delegate(){TrancheRandomFailure(directory);});Test("tiny/huge FP64 normalization, eigenvalues, resource formulas and integer errors",NumericEdges);Test("coherent circuit lifecycle, Bell shots, ownership, callback bounds and restart",delegate(){Circuit(directory);});Test("sequence preflight preserves presets and uses tranche indices",delegate(){Sequence(directory,manifest);});Test("sequence queue checkpoint failure leaves a recoverable inactive job",delegate(){SequenceCheckpointFailure(directory);});Test("reset default domain, independent snapshots and circuit ownership",delegate(){ResetContext(directory,manifest);});return "PASS "+tests.Count+" test groups; "+assertions+" assertions; all 13 production C# sources compiled under Windows PowerShell 5.1.";
    }
}
