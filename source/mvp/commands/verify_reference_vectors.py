#!/usr/bin/env python3
# Independent VCK-1 reference verifier.
import json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
SPEC=json.loads((ROOT/'commands/reference_vectors.json').read_text())
BASE=(ROOT/'availability/base_page_frame1.bin').read_bytes()
M=0xffffffff; H=256; P=len(BASE)-H; CPE=64
S0,S1,S2,S3=SPEC['kernel']['seeds']
def mix32(x):
 x&=M; x^=x>>16; x=(x*0x7feb352d)&M; x^=x>>15; x=(x*0x846ca68b)&M; x^=x>>16; return x&M
def rotl(x,n):
 x&=M;n&=31;return ((x<<n)|(x>>((32-n)&31)))&M
def vsym(ep,addr):
 b=BASE[addr]
 if addr<H:return b
 k=mix32(S0^((ep*0x9e3779b1)&M)^S1); x=mix32(k^((addr*0x85ebca6b)&M)^((b<<24)&M)^0x27d4eb2d)
 return (b^x^(x>>8)^(x>>16)^(x>>24))&255
def init():
 return [mix32(S0^0xa5a5a5a5),mix32(S1^0x5a5a5a5a),mix32(S2^0x3c6ef372),mix32(S3^0xbb67ae85),mix32(S0^S2),mix32(S1^S3),mix32(S0^S1^0xc3efe9db),mix32(S2^S3^0x27d4eb2d)],mix32(S0^S1^S2^S3),bytearray(256),[0]*8
def step(r,chain,scratch,counts,i):
 key=mix32(chain^(i&M)^((i*0x9e3779b1)&M)^S0);op=key&7;x=(key>>3)&7;y=(key>>6)&7;z=(key>>9)&7;imm=mix32(key^S1^((i*0x85ebca6b)&M));ep=((i-1)//CPE)+1
 if op==0:
  addr=H+((r[x]^imm)%P);sym=vsym(ep,addr);r[z]=mix32(r[z]^sym^addr^imm)
 elif op==1:r[z]=rotl(r[x]^r[y]^imm,(key>>12)&31)
 elif op==2:r[z]=(r[x]+(((r[y]^imm)*0x45d9f3b)&M))&M
 elif op==3:r[z]=(H+((r[x]+r[y]+imm)%P))&M
 elif op==4:
  si=(r[x]^key)&255;val=(r[y]^imm)&255;scratch[si]=val;r[z]=mix32(r[z]^val^si^imm)
 elif op==5:r[z]=mix32(((r[z]+imm+r[y])&M) if (r[x]&1) else (r[z]^imm^rotl(r[y],key&31)))
 elif op==6:
  addr=H+((r[x]+r[y]+(imm&0xffff))%P);sym=vsym(ep,addr);r[z]=mix32(r[z]^sym^rotl(addr,(key>>13)&31))
 else:
  chain=mix32(chain^r[x]^rotl(r[y],key&31)^imm^i);r[z]=mix32((r[z]+chain+r[(z+1)&7])&M)
 chain=mix32(chain^r[z]^key^(i&M)^scratch[(key>>16)&255]);counts[op]+=1;return chain
r,chain,scratch,counts=init(); cps={int(k):v for k,v in SPEC['reference']['checkpoints'].items()}; fail=0
for i in range(1,max(cps)+1):
 chain=step(r,chain,scratch,counts,i)
 if i in cps:
  exp=cps[i];blob=b''.join((x&M).to_bytes(4,'little') for x in r)+(chain&M).to_bytes(4,'little')+bytes(scratch)
  obs={'registers':[x&M for x in r],'chain':chain&M,'scratchSha256':hashlib.sha256(scratch).hexdigest(),'stateSha256':hashlib.sha256(blob).hexdigest(),'opcodeCounts':counts[:]}
  ok=obs==exp; print(('PASS' if ok else 'FAIL'),f'{i:,} commands',obs['stateSha256']); fail+=0 if ok else 1
raise SystemExit(1 if fail else 0)
