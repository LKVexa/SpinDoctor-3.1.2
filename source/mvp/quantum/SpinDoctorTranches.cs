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
// Isolated preview: selected 1..298-qubit coherent registers. Product factors merge into genuine
// entangled joint factors when a connecting gate is applied. Sparse amplitudes
// are exact floating-point values: no threshold truncation or tensor-rank cutoff.
// Global basis addresses use BigInteger. Each joint factor is limited to 59 bits
// for the existing exact ulong local addressing and byte arithmetic.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace QuettaQuantum
{

    // 3.0.3: Process.GetCurrentProcess().PrivateMemorySize64 costs ~6-15 ms per call on
    // Windows. 3.0.2 called it inside every TrancheRegister.Status(), which Gate() and
    // circuit accounting invoke for every gate of every replayed shot. The value is
    // informational host telemetry only (budgets use exact/estimated state bytes), so it is
    // sampled at most every 250 ms. Results, seeds, counters and budgets are unchanged.
    public static class ProcessMemorySampler
    {
        private static readonly object gate = new object();
        private static long last = -1; private static long stamp;
        public const int MaximumAgeMilliseconds = 250;
        public static long PrivateBytes()
        {
            lock (gate)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (last < 0 || (double)(now - stamp) / System.Diagnostics.Stopwatch.Frequency * 1000 >= MaximumAgeMilliseconds)
                {
                    using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess()) last = process.PrivateMemorySize64;
                    stamp = now;
                }
                return last;
            }
        }
    }

    public sealed class TrancheRegister : IDisposable
    {
        public const int MaximumLogicalQubits = 298;
        public const int MaximumFactorQubits = 59;
        private const int TileBits = 20, TileComplex = 1 << TileBits;
        private const ulong CompactCheckpointBytes = 64UL * 1024 * 1024;
        private readonly object sync = new object();
        private List<Factor> factors = new List<Factor>();
        private bool disposed;
        private ulong memoryLimit = 512UL * 1024 * 1024;
        private BigInteger logicalGates, amplitudeWrites, tensorProducts, measurementShots, queryCount;
        public ulong RestoredAmplitudeLoads { get; private set; }
        public int Qubits { get; private set; }
        public ulong MemoryLimitBytes { get { lock (sync) { return memoryLimit; } } }
        public bool CanCompactCheckpoint { get { lock (sync) { return DenseBytes() <= CompactCheckpointBytes && NonzeroCountEstimate() <= CompactCheckpointBytes / 24; } } }

        private sealed class Factor
        {
            public int[] Bits;
            public Dictionary<ulong, Complex> Sparse;
            public double[][] Dense;
            public ulong Length { get { return 1UL << Bits.Length; } }
            public ulong DenseBytes { get { if (Dense == null) return 0; ulong n = 0; foreach (double[] a in Dense) if (a != null) n += (ulong)a.Length * 8; return n; } }
            public ulong EstimatedBytes { get { return Dense == null ? 4096UL + (ulong)Sparse.Count * 96 : 4096UL + (ulong)Dense.Length * 8 + DenseBytes; } }
            public Factor(int[] bits)
            {
                if (bits == null || bits.Length < 1 || bits.Length > MaximumFactorQubits)
                    throw new ArgumentOutOfRangeException("bits", "Each coherent joint factor supports 1..59 qubits in this isolated preview.");
                Bits = bits; Sparse = new Dictionary<ulong, Complex>();
            }
            public Complex Get(ulong index)
            {
                if (Dense == null) { Complex c; return Sparse.TryGetValue(index, out c) ? c : Complex.Zero; }
                double[] tile = Dense[(int)(index >> TileBits)];
                if (tile == null) return Complex.Zero;
                int p = (int)(index & (TileComplex - 1)) * 2;
                return new Complex(tile[p], tile[p + 1]);
            }
            public void Set(ulong index, Complex value)
            {
                if (Dense == null)
                {
                    if (value == Complex.Zero) Sparse.Remove(index); else Sparse[index] = value;
                    return;
                }
                int t = (int)(index >> TileBits), p = (int)(index & (TileComplex - 1)) * 2;
                if (Dense[t] == null)
                {
                    if (value == Complex.Zero) return;
                    Dense[t] = new double[checked((int)Math.Min((ulong)TileComplex, Length - (ulong)t * TileComplex) * 2)];
                }
                Dense[t][p] = value.Real; Dense[t][p + 1] = value.Imaginary;
            }
            public IEnumerable<KeyValuePair<ulong, Complex>> Nonzero(Func<bool> cancel=null)
            {
                if (Dense == null) { int scanned=0;foreach (KeyValuePair<ulong, Complex> e in Sparse){if((scanned++&4095)==0)Cancel(cancel);yield return e;}yield break; }
                for (int t = 0; t < Dense.Length; ++t)
                {
                    double[] tile = Dense[t]; if (tile == null) continue;
                    for (int p = 0; p < tile.Length; p += 2)
                    {
                        if((p&8191)==0)Cancel(cancel);
                        if (tile[p] != 0 || tile[p + 1] != 0)
                            yield return new KeyValuePair<ulong, Complex>((ulong)t * TileComplex + (ulong)(p / 2), new Complex(tile[p], tile[p + 1]));
                    }
                }
            }
            public Factor Clone()
            {
                Factor copy = new Factor((int[])Bits.Clone());
                if (Dense == null) copy.Sparse = new Dictionary<ulong, Complex>(Sparse);
                else
                {
                    copy.Sparse = null; copy.Dense = new double[Dense.Length][];
                    for (int i = 0; i < Dense.Length; ++i) if (Dense[i] != null) copy.Dense[i] = (double[])Dense[i].Clone();
                }
                return copy;
            }
            public int Position(int bit) { for (int i = 0; i < Bits.Length; ++i) if (Bits[i] == bit) return i; throw new InvalidOperationException("Qubit is absent from a factor."); }
        }

        public TrancheRegister(int qubits) : this(qubits, null) { }
        public TrancheRegister(int qubits, double[][] spinors)
        {
            if (qubits < 1 || qubits > MaximumLogicalQubits) throw new ArgumentOutOfRangeException("qubits", "Selected registers support 1..298 logical qubits, with each coherent joint factor limited to 59 qubits.");
            if (spinors != null && spinors.Length != qubits) throw new ArgumentException("Supply one four-component spinor per qubit.");
            Qubits = qubits;
            for (int q = 0; q < qubits; ++q)
            {
                double[] s = spinors == null ? new double[] { 1, 0, 0, 0 } : spinors[q];
                if (s == null || s.Length != 4) throw new ArgumentException("Each spinor has [alpha real,alpha imaginary,beta real,beta imaginary].");
                foreach (double v in s) if (!QuantumMath.IsFinite(v)) throw new ArgumentException("Spinor components must be finite.");
                double norm = s[0]*s[0] + s[1]*s[1] + s[2]*s[2] + s[3]*s[3];
                if (Math.Abs(norm - 1) > 2e-9) throw new ArgumentException("Each initial spinor must be normalized.");
                Factor f = new Factor(new int[] { q });
                f.Set(0, new Complex(s[0],s[1])); f.Set(1,new Complex(s[2],s[3])); factors.Add(f); amplitudeWrites += 2;
            }
        }

        public static Dictionary<string, object>[] SupportedGates
        {
            get
            {
                List<Dictionary<string,object>> list = new List<Dictionary<string,object>>();
                foreach (string n in new string[] { "h","x","y","z","s","sdg","t","tdg","i","sx","sxdg" }) list.Add(D("name",n,"angleCount",0,"targetCount",1,"local",true));
                foreach (string n in new string[] { "rx","ry","rz","p" }) list.Add(D("name",n,"angleCount",1,"targetCount",1,"local",true));
                list.Add(D("name","u","angleCount",3,"targetCount",1,"local",true));
                foreach (string n in new string[] { "cx","cy","cz","ch","swap" }) list.Add(D("name",n,"angleCount",0,"targetCount",2,"local",false));
                foreach (string n in new string[] { "cp","crx","cry","crz","rxx","ryy","rzz","rzx" }) list.Add(D("name",n,"angleCount",1,"targetCount",2,"local",false));
                list.Add(D("name","cu","angleCount",4,"targetCount",2,"local",false));
                foreach (string n in new string[] { "ccx","cswap" }) list.Add(D("name",n,"angleCount",0,"targetCount",3,"local",false));
                list.Add(D("name","mcx7","angleCount",0,"targetCount",8,"local",false));
                return list.ToArray();
            }
        }

        public void SetMemoryLimit(ulong bytes)
        {
            lock (sync) { Alive(); if (bytes < EstimatedBytes() || bytes < 1024*1024) throw new ArgumentOutOfRangeException("bytes", "Budget must hold current state and be at least 1 MiB."); memoryLimit = bytes; }
        }
        private void Budget(ulong extra)
        {
            ulong live = EstimatedBytes();
            if (extra > memoryLimit || live > memoryLimit - extra) throw new InvalidOperationException("Tranche memory budget exceeded before commit. Raise q.tranche memory explicitly; dense 29-qubit state alone is 8 GiB, and transactional work needs additional memory.");
        }
        private static void Cancel(Func<bool> cancel) { if (cancel != null && cancel()) throw new OperationCanceledException("Tranche operation cancelled before state commit."); }
        private static void Progress(Action<ulong,ulong> callback, ulong done, ulong total) { if (callback != null) callback(done,total); }

        public Dictionary<string,object> Gate(string name, double[] angles, int[] qubits, bool all) { return Gate(name,angles,qubits,all,null,null); }
        public Dictionary<string,object> Gate(string name, double[] angles, int[] qubits, bool all, Func<bool> cancel, Action<ulong,ulong> progress)
        {
            lock (sync)
            {
                Alive(); name = (name ?? "").ToLowerInvariant(); if (name == "toffoli") name="ccx";
                Dictionary<string,object> spec=null; foreach (Dictionary<string,object> s in SupportedGates) if ((string)s["name"]==name) spec=s;
                if (spec==null) throw new ArgumentException("Unknown tranche operator. Use q.tranche help.");
                int nargs=(int)spec["angleCount"], targets=(int)spec["targetCount"];
                if (angles==null) angles=new double[0];
                if (angles.Length!=nargs) throw new ArgumentException("Incorrect gate angle count.");
                foreach (double a in angles) if (!QuantumMath.IsFinite(a)||Math.Abs(a)>1e12) throw new ArgumentException("Angles must be finite within +/-1e12 radians.");
                if (all)
                {
                    if (!(bool)spec["local"]) throw new ArgumentException("all is supported for local operators only. Controlled and joint operators require explicit distinct qubits.");
                    qubits=new int[Qubits]; for(int i=0;i<Qubits;++i)qubits[i]=i;
                }
                else if (qubits==null||qubits.Length!=targets) throw new ArgumentException("Incorrect gate qubit count.");
                HashSet<int> distinct=new HashSet<int>(); foreach(int q in qubits) { CheckQubit(q); if(!distinct.Add(q))throw new ArgumentException("Gate qubits must be distinct."); }
                Cancel(cancel);
                Stopwatch clock=Stopwatch.StartNew();
                List<Factor> selected=new List<Factor>();
                foreach(Factor f in factors) foreach(int q in qubits) if(Array.IndexOf(f.Bits,q)>=0) { if(!selected.Contains(f))selected.Add(f); break; }
                List<Factor> work=new List<Factor>();
                BigInteger writes=0, products=0;
                bool accounted=false;
                try
                {
                    if ((bool)spec["local"])
                    {
                        ulong copyBytes=0;foreach(Factor f in selected)copyBytes+=f.EstimatedBytes;Budget(copyBytes);
                        foreach(Factor f in selected)work.Add(f.Clone());
                        Complex[] g=LocalMatrix(name,angles);
                        ulong done=0;
                        foreach(int q in qubits)
                        {
                            Factor f=Find(work,q);ApplySingle(f,q,new int[0],g,cancel,work,ref writes);MaybeDense(f,work);
                            done++;Progress(progress,done,(ulong)qubits.Length);Cancel(cancel);
                        }
                    }
                    else
                    {
                        Factor joint=Merge(selected,cancel,progress,ref products,ref writes);work.Add(joint);
                        if(name=="swap"||name=="cswap")ApplySwap(joint,qubits, name=="cswap",cancel,work,ref writes);
                        else if(name=="rxx"||name=="ryy"||name=="rzz"||name=="rzx")ApplyPauliRotation(joint,name,angles[0],qubits,cancel,work,ref writes);
                        else
                        {
                            int target=qubits[qubits.Length-1];int[] controls=new int[qubits.Length-1];Array.Copy(qubits,controls,controls.Length);
                            string local=name=="ccx"||name=="mcx7"?"x":name.Substring(1);
                            double[] localAngles=angles;
                            if(name=="cu")localAngles=new double[]{angles[0],angles[1],angles[2]};
                            Complex[] g=LocalMatrix(local,localAngles);
                            if(name=="cu") { Complex phase=Complex.FromPolarCoordinates(1,angles[3]);for(int i=0;i<4;++i)g[i]*=phase; }
                            ApplySingle(joint,target,controls,g,cancel,work,ref writes);
                        }
                        MaybeDense(joint,work);Progress(progress,1,1);Cancel(cancel);
                    }
                    Budget(WorkingBytes(work));Cancel(cancel);
                    foreach(Factor f in selected)factors.Remove(f);factors.AddRange(work);factors.Sort(delegate(Factor a,Factor b){return a.Bits[0].CompareTo(b.Bits[0]);});
                    logicalGates+=all?qubits.Length:1;
                    amplitudeWrites+=writes;tensorProducts+=products;accounted=true;
                    clock.Stop();
                    return D("gate",name,"qubits",qubits,"angles",angles,"logicalApplications",(all?qubits.Length:1).ToString(),"actualAmplitudeWrites",S(writes),"actualTensorProducts",S(products),"elapsedMs",clock.Elapsed.TotalMilliseconds,"status",Status());
                }
                finally { if(!accounted){amplitudeWrites+=writes;tensorProducts+=products;} }
            }
        }

        private static Complex[] LocalMatrix(string name,double[] a)
        {
            double c,s;
            if(name=="rx") {c=Math.Cos(a[0]/2);s=Math.Sin(a[0]/2);return new Complex[]{new Complex(c,0),new Complex(0,-s),new Complex(0,-s),new Complex(c,0)};}
            if(name=="p")return new Complex[]{Complex.One,Complex.Zero,Complex.Zero,Complex.FromPolarCoordinates(1,a[0])};
            if(name=="sdg")return new Complex[]{Complex.One,Complex.Zero,Complex.Zero,new Complex(0,-1)};
            if(name=="tdg")return new Complex[]{Complex.One,Complex.Zero,Complex.Zero,Complex.FromPolarCoordinates(1,-Math.PI/4)};
            if(name=="sx"||name=="sxdg") {double sign=name=="sx"?1:-1;return new Complex[]{new Complex(.5,.5*sign),new Complex(.5,-.5*sign),new Complex(.5,-.5*sign),new Complex(.5,.5*sign)};}
            double[] g=QuantumMath.MatrixForGate(name,a);return new Complex[]{new Complex(g[0],g[1]),new Complex(g[2],g[3]),new Complex(g[4],g[5]),new Complex(g[6],g[7])};
        }
        private Factor Merge(List<Factor> selected,Func<bool> cancel,Action<ulong,ulong> progress,ref BigInteger products,ref BigInteger writes)
        {
            List<int> bits=new List<int>();foreach(Factor f in selected)bits.AddRange(f.Bits);bits.Sort();
            if(bits.Count>MaximumFactorQubits)
                throw new InvalidOperationException("Gate would merge a coherent factor beyond the 59-qubit local addressing limit. The selected register is unchanged.");
            Factor merged=new Factor(bits.ToArray());merged.Set(0,Complex.One);
            ulong extra=merged.EstimatedBytes;
            foreach(Factor f in selected)
            {
                Cancel(cancel);Factor next=new Factor(merged.Bits);
                ulong done=0;
                foreach(KeyValuePair<ulong,Complex> old in merged.Nonzero(cancel))
                    foreach(KeyValuePair<ulong,Complex> part in f.Nonzero(cancel))
                    {
                        ulong shifted=0;for(int j=0;j<f.Bits.Length;++j)if((part.Key&(1UL<<j))!=0)shifted|=1UL<<Array.IndexOf(merged.Bits,f.Bits[j]);
                        next.Set(old.Key|shifted,old.Value*part.Value);products++;writes++;
                        if((++done&4095)==0){Budget(extra+next.EstimatedBytes);Cancel(cancel);}
                    }
                Budget(extra+next.EstimatedBytes);merged=next;extra=merged.EstimatedBytes;
                MaybeDense(merged,new List<Factor>{merged});Progress(progress,(ulong)Array.IndexOf(selected.ToArray(),f)+1,(ulong)selected.Count);
            }
            return merged;
        }
        private void MaybeDense(Factor f,List<Factor> working)
        {
            if(f.Dense!=null||f.Length<1024|| (ulong)f.Sparse.Count*96 < f.Length*16) return;
            ulong extra=f.Length*16;foreach(Factor w in working)extra+=w.EstimatedBytes;Budget(extra);
            Dictionary<ulong,Complex> old=f.Sparse;
            f.Dense=new double[checked((int)((f.Length+TileComplex-1)/TileComplex))][];f.Sparse=null;
            foreach(KeyValuePair<ulong,Complex> e in old)f.Set(e.Key,e.Value);
        }
        private static ulong WorkingBytes(List<Factor> working)
        { ulong bytes=0;foreach(Factor f in working)bytes+=f.EstimatedBytes;return bytes; }
        private void DensePairBudget(Factor f,ulong first,Complex firstValue,ulong second,Complex secondValue,List<Factor> working)
        {
            if(f.Dense==null)return;
            int a=(int)(first>>TileBits),b=(int)(second>>TileBits);ulong growth=0;
            if(f.Dense[a]==null&&(firstValue!=Complex.Zero ||b==a&&secondValue!=Complex.Zero))growth+=Math.Min((ulong)TileComplex,f.Length-(ulong)a*TileComplex)*16;
            if(b!=a&&f.Dense[b]==null&&secondValue!=Complex.Zero)growth+=Math.Min((ulong)TileComplex,f.Length-(ulong)b*TileComplex)*16;
            if(growth!=0)Budget(WorkingBytes(working)+growth);
        }
        private void ApplySingle(Factor f,int qubit,int[] controls,Complex[] g,Func<bool> cancel,List<Factor> working,ref BigInteger writes)
        {
            ulong bit=1UL<<f.Position(qubit),mask=0;foreach(int q in controls)mask|=1UL<<f.Position(q);
            if(f.Dense!=null)
            {
                for(ulong i=0;i<f.Length;++i)
                {
                    // Poll on scanned addresses: a low control bit can exclude
                    // every address divisible by 4096 from the updated pairs.
                    if((i&4095)==0)Cancel(cancel);
                    if((i&bit)==0 && (i&mask)==mask)
                    {UpdatePair(f,i,i|bit,g,working,ref writes);}
                }
            }
            else
            {
                HashSet<ulong> pairs=new HashSet<ulong>();ulong selected=0;foreach(ulong i in f.Sparse.Keys){if((i&mask)==mask)pairs.Add(i&~bit);if((++selected&4095)==0){Budget(WorkingBytes(working)+(ulong)pairs.Count*32);Cancel(cancel);}}
                Budget(WorkingBytes(working)+(ulong)pairs.Count*32);
                ulong done=0;foreach(ulong i in pairs){UpdatePair(f,i,i|bit,g,working,ref writes);if((++done&4095)==0){Budget(WorkingBytes(working)+(ulong)pairs.Count*32);Cancel(cancel);}}
                Budget(WorkingBytes(working)+(ulong)pairs.Count*32);
            }
        }
        private void UpdatePair(Factor f,ulong zero,ulong one,Complex[] g,List<Factor> working,ref BigInteger writes)
        {Complex a=f.Get(zero),b=f.Get(one),nextA=g[0]*a+g[1]*b,nextB=g[2]*a+g[3]*b;DensePairBudget(f,zero,nextA,one,nextB,working);f.Set(zero,nextA);f.Set(one,nextB);writes+=2;}
        private void ApplySwap(Factor f,int[] qubits,bool controlled,Func<bool> cancel,List<Factor> working,ref BigInteger writes)
        {
            int offset=controlled?1:0;ulong a=1UL<<f.Position(qubits[offset]),b=1UL<<f.Position(qubits[offset+1]),control=controlled?1UL<<f.Position(qubits[0]):0;
            if(f.Dense!=null)
            {
                for(ulong i=0;i<f.Length;++i){if((i&control)==control&&(i&a)==0&&(i&b)!=0){ulong j=i^a^b;Complex v=f.Get(i),other=f.Get(j);DensePairBudget(f,i,other,j,v,working);f.Set(i,other);f.Set(j,v);writes+=2;}if((i&4095)==0)Cancel(cancel);}return;
            }
            HashSet<ulong> sources=new HashSet<ulong>();ulong selected=0;foreach(KeyValuePair<ulong,Complex> e in f.Nonzero()){if((e.Key&control)==control&&((e.Key&a)==0)!=((e.Key&b)==0))sources.Add((e.Key|b)&~a);if((++selected&4095)==0){Budget(WorkingBytes(working)+(ulong)sources.Count*32);Cancel(cancel);}}
            Budget(WorkingBytes(working)+(ulong)sources.Count*32);
            ulong done=0;foreach(ulong i in sources){ulong j=i^a^b;Complex v=f.Get(i);f.Set(i,f.Get(j));f.Set(j,v);writes+=2;if((++done&4095)==0){Budget(WorkingBytes(working)+(ulong)sources.Count*32);Cancel(cancel);}}
            Budget(WorkingBytes(working)+(ulong)sources.Count*32);
        }
        private void ApplyPauliRotation(Factor f,string name,double angle,int[] qubits,Func<bool> cancel,List<Factor> working,ref BigInteger writes)
        {
            ulong a=1UL<<f.Position(qubits[0]),b=1UL<<f.Position(qubits[1]);double c=Math.Cos(angle/2),s=Math.Sin(angle/2);
            if(name=="rzz")
            {
                if(f.Dense!=null){for(ulong i=0;i<f.Length;++i){int sign=((i&a)==0?1:-1)*((i&b)==0?1:-1);f.Set(i,Complex.FromPolarCoordinates(1,-sign*angle/2)*f.Get(i));writes++;if((i&4095)==0)Cancel(cancel);}return;}
                List<ulong> keys=new List<ulong>();foreach(KeyValuePair<ulong,Complex> e in f.Nonzero()){keys.Add(e.Key);if((keys.Count&4095)==0){Budget(WorkingBytes(working)+(ulong)keys.Count*16);Cancel(cancel);}}Budget(WorkingBytes(working)+(ulong)keys.Count*16);
                ulong done=0;foreach(ulong i in keys){int sign=((i&a)==0?1:-1)*((i&b)==0?1:-1);f.Set(i,Complex.FromPolarCoordinates(1,-sign*angle/2)*f.Get(i));writes++;if((++done&4095)==0)Cancel(cancel);}return;
            }
            ulong flip=name=="rzx"?b:a|b;
            if(f.Dense!=null)
            {
                for(ulong i=0;i<f.Length;++i){ulong j=i^flip;if(i<j){int sign=name=="ryy"?-((i&a)==0?1:-1)*((i&b)==0?1:-1):(name=="rzx"?((i&a)==0?1:-1):1);Complex ai=f.Get(i),aj=f.Get(j),coef=new Complex(0,-s*sign),nextA=c*ai+coef*aj,nextB=c*aj+coef*ai;DensePairBudget(f,i,nextA,j,nextB,working);f.Set(i,nextA);f.Set(j,nextB);writes+=2;}if((i&4095)==0)Cancel(cancel);}return;
            }
            HashSet<ulong> pairs=new HashSet<ulong>();ulong selected=0;foreach(KeyValuePair<ulong,Complex> e in f.Nonzero()){pairs.Add(Math.Min(e.Key,e.Key^flip));if((++selected&4095)==0){Budget(WorkingBytes(working)+(ulong)pairs.Count*32);Cancel(cancel);}}Budget(WorkingBytes(working)+(ulong)pairs.Count*32);
            ulong count=0;foreach(ulong i in pairs)
            {
                ulong j=i^flip;int sign=name=="ryy"?-((i&a)==0?1:-1)*((i&b)==0?1:-1):(name=="rzx"?((i&a)==0?1:-1):1);
                Complex ai=f.Get(i),aj=f.Get(j),coefficient=new Complex(0,-s*sign);
                f.Set(i,c*ai+coefficient*aj);f.Set(j,c*aj+coefficient*ai);writes+=2;
                if((++count&4095)==0){Budget(WorkingBytes(working)+(ulong)pairs.Count*32);Cancel(cancel);}
            }
            Budget(WorkingBytes(working)+(ulong)pairs.Count*32);
        }

        public double[] Amplitude(ulong basisIndex) { return Amplitude(new BigInteger(basisIndex)); }
        public double[] Amplitude(BigInteger basisIndex)
        {
            lock(sync){Alive();if(basisIndex.Sign<0||basisIndex>=(BigInteger.One<<Qubits))throw new ArgumentOutOfRangeException("basisIndex");Complex value=Complex.One;foreach(Factor f in factors){ulong index=0;for(int j=0;j<f.Bits.Length;++j)if((basisIndex&(BigInteger.One<<f.Bits[j]))!=BigInteger.Zero)index|=1UL<<j;value*=f.Get(index);}queryCount++;return new double[]{value.Real,value.Imaginary};}
        }
        public Dictionary<string,object> State(ulong basisIndex) { return State(new BigInteger(basisIndex)); }
        public Dictionary<string,object> State(BigInteger basisIndex)
        {double[] a=Amplitude(basisIndex);return D("basisIndex",basisIndex.ToString(CultureInfo.InvariantCulture),"amplitude",a,"probability",a[0]*a[0]+a[1]*a[1],"bitOrder","little-endian; qubit 0 is least significant");}
        // Independent Born-Z draw from the retained product of coherent factors.
        // Each factor is sampled jointly, preserving correlations inside it.
        // Exactly one uniform is consumed per factor, including fixed factors.
        // No factor amplitude or basis state is changed by this operation.
        public BigInteger SampleZ(Func<double> uniform,Func<bool> cancel)
        {
            if(uniform==null)throw new ArgumentNullException("uniform");
            lock(sync)
            {
                Alive();BigInteger basis=BigInteger.Zero;
                foreach(Factor f in factors)
                {
                    Cancel(cancel);
                    List<KeyValuePair<ulong,Complex>> entries=new List<KeyValuePair<ulong,Complex>>();
                    foreach(KeyValuePair<ulong,Complex> e in f.Nonzero(cancel))
                    {
                        entries.Add(e);
                        if((entries.Count&4095)==0){Budget(4096UL+(ulong)entries.Count*48);Cancel(cancel);}
                    }
                    Budget(4096UL+(ulong)entries.Count*48);
                    entries.Sort(delegate(KeyValuePair<ulong,Complex> a,KeyValuePair<ulong,Complex> b){return a.Key.CompareTo(b.Key);});
                    double norm=0;
                    foreach(KeyValuePair<ulong,Complex> e in entries)
                    {
                        double p=e.Value.Real*e.Value.Real+e.Value.Imaginary*e.Value.Imaginary;
                        if(!QuantumMath.IsFinite(p))throw new InvalidOperationException("Nonfinite coherent-factor probability.");
                        norm+=p;
                    }
                    queryCount++;
                    if(entries.Count==0||!QuantumMath.IsFinite(norm)||norm<=0||Math.Abs(norm-1)>2e-7)
                        throw new InvalidOperationException("Born sampling requires a normalized nonempty coherent factor.");
                    Cancel(cancel);double draw=uniform();
                    if(!QuantumMath.IsFinite(draw)||draw<0||draw>=1)
                        throw new ArgumentOutOfRangeException("uniform","Uniform draws must lie in [0,1).");
                    double threshold=draw*norm,cumulative=0;ulong selected=entries[entries.Count-1].Key;
                    foreach(KeyValuePair<ulong,Complex> e in entries)
                    {
                        cumulative+=e.Value.Real*e.Value.Real+e.Value.Imaginary*e.Value.Imaginary;
                        if(threshold<cumulative){selected=e.Key;break;}
                    }
                    for(int j=0;j<f.Bits.Length;++j)if((selected&(1UL<<j))!=0)basis|=BigInteger.One<<f.Bits[j];
                }
                Cancel(cancel);measurementShots+=Qubits;return basis;
            }
        }
        public double[] Spin(int qubit)
        { return Spin(qubit,null); }
        internal Dictionary<string,object> BoundedSpinSnapshot(int qubit,int maximumFactorEntries,Func<bool> cancel)
        {
            lock(sync)
            {
                Alive();CheckQubit(qubit);if(maximumFactorEntries<1)throw new ArgumentOutOfRangeException("maximumFactorEntries");Factor factor=Find(factors,qubit);
                ulong entries=factor.Dense==null?(ulong)factor.Sparse.Count:factor.Length;
                if(entries>(ulong)maximumFactorEntries)return D("qubit",qubit,"captured",false,"factorEntryCount",entries.ToString(CultureInfo.InvariantCulture),"reason","Existing coherent factor exceeds the bounded spin-preview traversal; no zero vector is inferred.");
                return D("qubit",qubit,"captured",true,"spin",Spin(qubit,cancel),"factorEntryCount",entries.ToString(CultureInfo.InvariantCulture));
            }
        }
        private double[] Spin(int qubit,Func<bool> cancel)
        {
            lock(sync){Alive();CheckQubit(qubit);Factor f=Find(factors,qubit);ulong bit=1UL<<f.Position(qubit);double x=0,y=0,z=0;foreach(KeyValuePair<ulong,Complex> e in f.Nonzero(cancel)){double p=e.Value.Real*e.Value.Real+e.Value.Imaginary*e.Value.Imaginary;z+=(e.Key&bit)==0?p:-p;if((e.Key&bit)==0){Complex cross=Complex.Conjugate(e.Value)*f.Get(e.Key|bit);x+=2*cross.Real;y+=2*cross.Imaginary;}}Cancel(cancel);queryCount++;return new double[]{x,y,z};}
        }
        public Dictionary<string,object> Measure(int qubit,string basis,int shots,string seed,ulong counter,bool collapse)
        {
            if(seed==null||seed.Length!=64)throw new ArgumentException("Measurement seed must be 64 hexadecimal characters.");
            foreach(char ch in seed)if(!Uri.IsHexDigit(ch))throw new ArgumentException("Invalid hexadecimal measurement seed.");
            if(shots<1||shots>100000||UInt64.MaxValue-counter<(ulong)shots)throw new ArgumentOutOfRangeException("shots");
            ulong cursor=counter;
            Func<double> uniform=delegate(){using(SHA256 hash=SHA256.Create()){byte[] bytes=hash.ComputeHash(Encoding.UTF8.GetBytes(seed+"|tranche-measurement|"+cursor.ToString(CultureInfo.InvariantCulture)));cursor++;ulong n=0;for(int j=0;j<8;++j)n=(n<<8)|bytes[j];return(n>>11)*(1.0/9007199254740992.0);}};
            Dictionary<string,object> result=Measure(qubit,basis,shots,uniform,collapse);result["seed"]=seed;result["counterStart"]=counter.ToString();result["counterEnd"]=cursor.ToString();return result;
        }
        public Dictionary<string,object> Measure(int qubit,string basis,int shots,Func<double> uniform,bool collapse)
        { return Measure(qubit,basis,shots,uniform,collapse,null); }
        public Dictionary<string,object> Measure(int qubit,string basis,int shots,Func<double> uniform,bool collapse,Func<bool> cancel)
        {
            lock(sync)
            {
                Alive();CheckQubit(qubit);basis=(basis??"").ToLowerInvariant();if(basis!="x"&&basis!="y"&&basis!="z")throw new ArgumentException("Basis must be x, y or z.");
                if(shots<1||shots>100000||uniform==null)throw new ArgumentOutOfRangeException("shots");if(collapse&&shots!=1)throw new ArgumentException("A collapsing measurement takes exactly one shot. Repeated shots normally sample independent copies without collapse.");
                Cancel(cancel);double[] spin=Spin(qubit,cancel);double probability=Math.Max(0,Math.Min(1,(1-spin[basis=="x"?0:basis=="y"?1:2])/2));int one=0;
                for(int j=0;j<shots;++j){Cancel(cancel);double r=uniform();if(!QuantumMath.IsFinite(r)||r<0||r>=1)throw new ArgumentException("Uniform draw must be in [0,1).");if(r<probability)one++;measurementShots++;}
                if(collapse)Collapse(qubit,basis,one,cancel);
                return D("qubit",qubit,"basis",basis,"shots",shots.ToString(),"zero",(shots-one).ToString(),"one",one.ToString(),"probabilityOne",probability,"collapsed",collapse,"collapseOutcome",collapse?(object)one:null,"scope","Seeded classical Born sampling of the selected coherent register; no physical quantum measurement.");
            }
        }
        private void Collapse(int qubit,string basis,int outcome,Func<bool> cancel)
        {
            // A rank-one projective measurement makes this qubit exactly
            // independent of the unmeasured remainder. Contract <e|psi>
            // directly and retain |e> as its own factor. Rotating a collapsed
            // bit back inside the old joint block would multiply support for
            // every measured X/Y bit (GHZ29 grew to an artificial 2^29 state).
            // This is exact factor separation, without amplitude truncation.
            Factor old=Find(factors,qubit);
            int position=old.Position(qubit);ulong bit=1UL<<position;
            double invRootTwo=1/Math.Sqrt(2);
            Complex e0,e1;
            if(basis=="z"){e0=outcome==0?Complex.One:Complex.Zero;e1=outcome==1?Complex.One:Complex.Zero;}
            else if(basis=="x"){e0=new Complex(invRootTwo,0);e1=new Complex(outcome==0?invRootTwo:-invRootTwo,0);}
            else{e0=new Complex(invRootTwo,0);e1=new Complex(0,outcome==0?invRootTwo:-invRootTwo);}
            Complex bra0=Complex.Conjugate(e0),bra1=Complex.Conjugate(e1);
            Factor measured=new Factor(new int[]{qubit}),remainder=null;
            HashSet<ulong> pairs=null;
            BigInteger writes=0;
            try
            {
                if(old.Bits.Length==1)
                {
                    Complex contraction=bra0*old.Get(0)+bra1*old.Get(1);
                    double norm=contraction.Real*contraction.Real+contraction.Imaginary*contraction.Imaginary;
                    if(!QuantumMath.IsFinite(norm)||norm<=0)throw new InvalidOperationException("Sampled an impossible collapse outcome.");
                    Complex phase=contraction/Math.Sqrt(norm);
                    Budget(4096UL+2*96);measured.Set(0,e0*phase);writes++;measured.Set(1,e1*phase);writes++;
                }
                else
                {
                    List<int> restBits=new List<int>();foreach(int q in old.Bits)if(q!=qubit)restBits.Add(q);
                    remainder=new Factor(restBits.ToArray());
                    ulong pairBytes=0;
                    if(old.Dense==null)
                    {
                        pairs=new HashSet<ulong>();ulong scanned=0;
                        foreach(ulong index in old.Sparse.Keys)
                        {
                            pairs.Add(index&~bit);
                            if((++scanned&4095)==0){Budget(8192UL+(ulong)pairs.Count*32);Cancel(cancel);}
                        }
                        pairBytes=(ulong)pairs.Count*32;Budget(8192UL+pairBytes);
                    }
                    double norm=0;ulong nonzero=0;
                    if(pairs!=null)
                    {
                        ulong scanned=0;foreach(ulong zero in pairs){if((scanned++&4095)==0)Cancel(cancel);Complex c=bra0*old.Get(zero)+bra1*old.Get(zero|bit);double p=c.Real*c.Real+c.Imaginary*c.Imaginary;norm+=p;if(c!=Complex.Zero)nonzero++;}
                    }
                    else
                    {
                        for(ulong index=0;index<remainder.Length;++index){if((index&4095)==0)Cancel(cancel);ulong zero=(index&(bit-1))|((index&~(bit-1))<<1);Complex c=bra0*old.Get(zero)+bra1*old.Get(zero|bit);double p=c.Real*c.Real+c.Imaginary*c.Imaginary;norm+=p;if(c!=Complex.Zero)nonzero++;}
                    }
                    if(!QuantumMath.IsFinite(norm)||norm<=0)throw new InvalidOperationException("Sampled an impossible collapse outcome.");
                    double scale=1/Math.Sqrt(norm);
                    if(remainder.Length>=1024 && nonzero*96>=remainder.Length*16)
                    {
                        ulong tiles=(remainder.Length+TileComplex-1)/TileComplex;
                        Budget(8192UL+pairBytes+tiles*8);
                        remainder.Dense=new double[checked((int)tiles)][];remainder.Sparse=null;
                    }
                    if(pairs!=null)
                    {
                        ulong scanned=0;foreach(ulong zero in pairs)
                        {
                            if((scanned++&4095)==0)Cancel(cancel);
                            Complex c=(bra0*old.Get(zero)+bra1*old.Get(zero|bit))*scale;if(c==Complex.Zero)continue;
                            ulong index=(zero&(bit-1))|((zero>>(position+1))<<position);
                            CollapseWrite(remainder,measured,pairBytes,index,c,ref writes);
                        }
                    }
                    else
                    {
                        for(ulong index=0;index<remainder.Length;++index)
                        {
                            if((index&4095)==0)Cancel(cancel);
                            ulong zero=(index&(bit-1))|((index&~(bit-1))<<1);
                            Complex c=(bra0*old.Get(zero)+bra1*old.Get(zero|bit))*scale;if(c==Complex.Zero)continue;
                            CollapseWrite(remainder,measured,pairBytes,index,c,ref writes);
                        }
                    }
                    Budget(remainder.EstimatedBytes+pairBytes+4096UL+2*96);
                    measured.Set(0,e0);writes++;measured.Set(1,e1);writes++;
                }
                // Commit only after the complete contraction fits and succeeds.
                Cancel(cancel);
                factors.Remove(old);if(remainder!=null)factors.Add(remainder);factors.Add(measured);
                factors.Sort(delegate(Factor a,Factor b){return a.Bits[0].CompareTo(b.Bits[0]);});
            }
            finally { amplitudeWrites+=writes; }
        }
        private void CollapseWrite(Factor remainder,Factor measured,ulong pairBytes,ulong index,Complex value,ref BigInteger writes)
        {
            ulong growth=96;
            if(remainder.Dense!=null)
            {
                int tile=(int)(index>>TileBits);
                growth=remainder.Dense[tile]==null?Math.Min((ulong)TileComplex,remainder.Length-(ulong)tile*TileComplex)*16:0;
            }
            Budget(remainder.EstimatedBytes+measured.EstimatedBytes+pairBytes+growth);
            remainder.Set(index,value);writes++;
        }
        public void Reset()
        {lock(sync){Alive();factors.Clear();for(int q=0;q<Qubits;++q){Factor f=new Factor(new int[]{q});f.Set(0,Complex.One);f.Set(1,Complex.Zero);factors.Add(f);amplitudeWrites+=2;}}}
        public Dictionary<string,object> Status()
        {
            lock(sync)
            {
                Alive();List<object> fs=new List<object>();foreach(Factor f in factors)fs.Add(D("qubits",f.Bits,"basisStateCount",f.Length.ToString(),"storage",f.Dense==null?"sparse-joint":"segmented-FP64-joint","nonzeroAmplitudes",f.Dense==null?(object)f.Sparse.Count.ToString():"not scanned","estimatedBytes",f.EstimatedBytes.ToString()));
                long privateBytes=ProcessMemorySampler.PrivateBytes();
                BigInteger globalStates=BigInteger.One<<Qubits;
                return D("qubits",Qubits,"maximumLogicalQubits",MaximumLogicalQubits,"maximumJointFactorQubits",MaximumFactorQubits,"basisStateCount",S(globalStates),"backend",factors.Count==Qubits?"factorized-product":"coherent-factor-blocks","factors",fs.ToArray(),"allocatedDenseStateBytes",DenseBytes().ToString(),"estimatedSparseStateBytes",SparseBytes().ToString(),"allocatedStateBytesEstimate",EstimatedBytes().ToString(),"memoryLimitBytes",memoryLimit.ToString(),"worstDenseStateBytes",S(globalStates*16),"worstTransactionalDenseGateBytes",S(globalStates*32),"logicalGateApplications",S(logicalGates),"actualAmplitudeWrites",S(amplitudeWrites),"actualTensorProducts",S(tensorProducts),"measurementShots",S(measurementShots),"queryCount",S(queryCount),"restoredAmplitudeLoads",RestoredAmplitudeLoads.ToString(),"canCompactCheckpoint",CanCompactCheckpoint,"processPrivateBytes",privateBytes.ToString(),"memoryAccountingScope","Dense tile bytes are exact; sparse dictionary storage is a conservative managed estimate. Process private bytes are measured for the complete host process (sampled at most every 250 ms; 3.0.3).","bitOrder","little-endian","scope","Isolated preview: one selected register of up to 298 logical qubits; each coherent factor is limited to 59 qubits. Factor blocks preserve genuine entanglement. Global dense-state byte counts are mathematical bounds, not allocations. No physical quantum gates are claimed.");
            }
        }
        public Dictionary<string,object> Export()
        {
            lock(sync)
            {
                Alive();Dictionary<string,object> result=D("schema","SPINDOCTOR/COHERENT-TRANCHE/1","status",Status(),"stateIncluded",false);
                if(DenseBytes()>32768UL*16||NonzeroCountEstimate()>32768){result["note"]="Use a binary checkpoint for this state; a large amplitude array is not inserted into an HTTP response.";return result;}
                List<object> fs=new List<object>();foreach(Factor f in factors){List<object> amplitudes=new List<object>();foreach(KeyValuePair<ulong,Complex> e in f.Nonzero())amplitudes.Add(new object[]{e.Key.ToString(),e.Value.Real.ToString("R",CultureInfo.InvariantCulture),e.Value.Imaginary.ToString("R",CultureInfo.InvariantCulture)});fs.Add(D("qubits",f.Bits,"amplitudes",amplitudes.ToArray()));}result["factors"]=fs.ToArray();result["stateIncluded"]=true;return result;
            }
        }
        public void Save(string path){Save(path,false);}
        public void Save(string path,bool allowLargeDense)
        {
            lock(sync)
            {
                Alive();if(!allowLargeDense&&!CanCompactCheckpoint)throw new InvalidOperationException("This state requires an explicit large binary checkpoint; automatic compact saving is bounded at 64 MiB.");
                string target=Path.GetFullPath(path),temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                using(FileStream file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(BinaryWriter writer=new BinaryWriter(file,Encoding.UTF8))
                {
                    writer.Write("SPINDOCTOR-TRANCHE-2");writer.Write(Qubits);writer.Write(memoryLimit);writer.Write(S(logicalGates));writer.Write(S(amplitudeWrites));writer.Write(S(tensorProducts));writer.Write(S(measurementShots));writer.Write(S(queryCount));writer.Write(factors.Count);
                    foreach(Factor f in factors)
                    {
                        writer.Write(f.Bits.Length);foreach(int q in f.Bits)writer.Write(q);
                        writer.Write(f.Dense!=null);
                        ulong n=0;foreach(KeyValuePair<ulong,Complex> e in f.Nonzero())n++;writer.Write(n);
                        foreach(KeyValuePair<ulong,Complex> e in f.Nonzero()){writer.Write(e.Key);writer.Write(e.Value.Real);writer.Write(e.Value.Imaginary);}
                    }
                }
                byte[] digest;using(SHA256 hash=SHA256.Create())using(FileStream file=File.OpenRead(temp))digest=hash.ComputeHash(file);using(FileStream file=new FileStream(temp,FileMode.Append,FileAccess.Write,FileShare.None))file.Write(digest,0,digest.Length);
                if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
                }
                finally { if(File.Exists(temp))File.Delete(temp); }
            }
        }
        public static TrancheRegister Load(string path,ulong memoryLimitBytes)
        {
            using(FileStream file=File.OpenRead(path))
            {
                byte[] expected=new byte[32],observed;if(file.Length<32)throw new InvalidDataException("Tranche checkpoint is truncated.");long size=file.Length-32;using(SHA256 h=SHA256.Create()){byte[] buffer=new byte[65536];long remain=size;while(remain>0){int n=file.Read(buffer,0,(int)Math.Min(buffer.Length,remain));if(n<=0)throw new EndOfStreamException();h.TransformBlock(buffer,0,n,buffer,0);remain-=n;}h.TransformFinalBlock(new byte[0],0,0);observed=h.Hash;}if(file.Read(expected,0,32)!=32)throw new EndOfStreamException();for(int i=0;i<32;++i)if(expected[i]!=observed[i])throw new InvalidDataException("Tranche checkpoint checksum mismatch.");file.Position=0;
                // Validate and parse the same read-locked file handle.
                using(BinaryReader reader=new BinaryReader(file,Encoding.UTF8))
                {
                string format=ReadBoundedString(reader,64);if(format!="SPINDOCTOR-TRANCHE-1"&&format!="SPINDOCTOR-TRANCHE-2")throw new InvalidDataException("Unknown tranche checkpoint.");TrancheRegister result=new TrancheRegister(reader.ReadInt32());
                try
                {
                    reader.ReadUInt64();result.SetMemoryLimit(memoryLimitBytes);result.logicalGates=ReadCounter(reader);result.amplitudeWrites=ReadCounter(reader);result.tensorProducts=ReadCounter(reader);result.measurementShots=ReadCounter(reader);result.queryCount=ReadCounter(reader);int count=reader.ReadInt32();if(count<1||count>result.Qubits)throw new InvalidDataException("Invalid factor count.");result.factors.Clear();HashSet<int> seen=new HashSet<int>();
                    for(int j=0;j<count;++j)
                    {
                        int width=reader.ReadInt32();if(width<1||width>result.Qubits||width>MaximumFactorQubits)throw new InvalidDataException("Invalid factor width; each joint factor is limited to 59 qubits.");int[] bits=new int[width];for(int k=0;k<width;++k){bits[k]=reader.ReadInt32();result.CheckQubit(bits[k]);if(!seen.Add(bits[k]))throw new InvalidDataException("Repeated qubit in checkpoint.");}Factor f=new Factor(bits);bool dense=format=="SPINDOCTOR-TRANCHE-2"&&reader.ReadBoolean();ulong n=reader.ReadUInt64();if(n<1||n>f.Length||n>(ulong)Math.Max(0,(file.Length-file.Position-32)/24))throw new InvalidDataException("Invalid amplitude count.");if(dense||(BigInteger)n*96>(BigInteger)f.Length*16){ulong tiles=(f.Length+TileComplex-1)/TileComplex;if(tiles>Int32.MaxValue)throw new InvalidDataException("Dense checkpoint index exceeds the segmented array limit.");result.Budget(4096UL+tiles*8);f.Dense=new double[(int)tiles][];f.Sparse=null;}double norm=0;
                        for(ulong k=0;k<n;++k){ulong index=reader.ReadUInt64();double re=reader.ReadDouble(),im=reader.ReadDouble();if(index>=f.Length||!QuantumMath.IsFinite(re)||!QuantumMath.IsFinite(im)||f.Get(index)!=Complex.Zero)throw new InvalidDataException("Invalid amplitude in checkpoint.");Complex value=new Complex(re,im);if(value==Complex.Zero)throw new InvalidDataException("Explicit zero in sparse checkpoint.");if(f.Dense!=null&&f.Dense[(int)(index>>TileBits)]==null)result.Budget(f.EstimatedBytes+Math.Min((ulong)TileComplex,f.Length-(index>>TileBits)*(ulong)TileComplex)*16);f.Set(index,value);result.RestoredAmplitudeLoads++;norm+=re*re+im*im;if((k&4095)==0)result.Budget(f.EstimatedBytes);}
                        if(Math.Abs(norm-1)>2e-7)throw new InvalidDataException("Checkpoint factor is not normalized.");result.Budget(f.EstimatedBytes);result.factors.Add(f);
                    }
                    if(seen.Count!=result.Qubits||file.Position!=file.Length-32)throw new InvalidDataException("Checkpoint coverage or length is invalid.");return result;
                }
                catch{result.Dispose();throw;}
                }
            }
        }
        private static string ReadBoundedString(BinaryReader reader,int maximumBytes)
        {
            uint length=0;int shift=0;
            for(int i=0;i<5;i++)
            {
                byte part=reader.ReadByte();if(i==4&&(part&0xf8)!=0)throw new InvalidDataException("Invalid checkpoint string length.");
                length|=(uint)(part&0x7f)<<shift;
                if((part&0x80)==0)
                {
                    if(length>(uint)maximumBytes)throw new InvalidDataException("Checkpoint string exceeds its bound.");
                    byte[] bytes=reader.ReadBytes((int)length);if(bytes.Length!=(int)length)throw new EndOfStreamException();
                    return new UTF8Encoding(false,true).GetString(bytes);
                }
                shift+=7;
            }
            throw new InvalidDataException("Invalid checkpoint string length.");
        }
        private static BigInteger ReadCounter(BinaryReader reader){string s=ReadBoundedString(reader,4096);BigInteger v;if(!BigInteger.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out v))throw new InvalidDataException("Invalid checkpoint counter.");return v;}
        private ulong NonzeroCountEstimate(){ulong total=0;foreach(Factor f in factors)total+=f.Dense==null?(ulong)f.Sparse.Count:f.DenseBytes/16;return total;}
        private ulong EstimatedBytes(){ulong total=0;foreach(Factor f in factors)total+=f.EstimatedBytes;return total;}
        private ulong DenseBytes(){ulong total=0;foreach(Factor f in factors)total+=f.DenseBytes;return total;}
        private ulong SparseBytes(){ulong total=0;foreach(Factor f in factors)if(f.Dense==null)total+=f.EstimatedBytes;return total;}
        private static Factor Find(List<Factor> list,int q){foreach(Factor f in list)if(Array.IndexOf(f.Bits,q)>=0)return f;throw new InvalidOperationException("Missing qubit factor.");}
        private void CheckQubit(int q){if(q<0||q>=Qubits)throw new ArgumentOutOfRangeException("qubit");}
        private void Alive(){if(disposed)throw new ObjectDisposedException("TrancheRegister");}
        private static string S(BigInteger v){return v.ToString(CultureInfo.InvariantCulture);}
        private static Dictionary<string,object> D(params object[]pairs){Dictionary<string,object>d=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i],pairs[i+1]);return d;}
        public void Dispose(){lock(sync){if(disposed)return;factors.Clear();disposed=true;}}
    }
}
