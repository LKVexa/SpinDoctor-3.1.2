const fs=require("fs"), crypto=require("crypto"), path=require("path");
const root=__dirname;
const ref=JSON.parse(fs.readFileSync(path.join(root,"reference_vectors.json"),"utf8"));
const prog=JSON.parse(fs.readFileSync(path.join(root,"exaflop_program.json"),"utf8"));
const base=fs.readFileSync(path.join(root,"..","availability","base_page_frame1.bin"));
const HEADER=256,PAYLOAD=261888,SEED0=3779422897>>>0,SEED1=416510389>>>0;
const u32=x=>x>>>0, imul=(a,b)=>Math.imul(a,b)>>>0;
function mix32(x){x>>>=0;x=(x^(x>>>16))>>>0;x=imul(x,0x7feb352d);x=(x^(x>>>15))>>>0;x=imul(x,0x846ca68b);return (x^(x>>>16))>>>0}
function rotl32(x,n){x>>>=0;return ((x<<n)|(x>>>(32-n)))>>>0}
function key(ep){return mix32((SEED0 ^ imul(ep,0x9e3779b1) ^ SEED1)>>>0)}
function avk(n){let a=mix32(SEED0^0xa5a5a5a5),b=mix32(SEED1^0x5a5a5a5a),c=mix32(SEED0^SEED1^0xc3efe9db);for(let ep=1;ep<=n;ep++){const k=key(ep);a=mix32((a^k^ep)>>>0);b=(imul((b^a)>>>0,1664525)+1013904223)>>>0;c=mix32(((c+b)>>>0)^imul(ep,0x27d4eb2d))}return {a,b,c}}
function lane(epoch,g){
  g=BigInt(g); const e=BigInt(epoch), payload=BigInt(PAYLOAD);
  const ia=HEADER+Number((g+e*17n+13n)%payload);
  const ib=HEADER+Number((g*3n+e*31n+29n)%payload);
  const ic=HEADER+Number((g*7n+e*43n+47n)%payload);
  const va=base[ia]^Number((e+g)&255n);
  const vb=base[ib]^Number(((e>>8n)+g*3n)&255n);
  const vc=base[ic]^Number(((e>>16n)+g*7n)&255n);
  const aa=Math.fround((va-128)/64), bb=Math.fround((vb-128)/64), cc=Math.fround((vc-128)/128);
  return Math.fround(Math.fround(aa*bb)+cc);
}
function rangeHash(epoch,start,count){
  const out=Buffer.allocUnsafe(count*4);
  let g=BigInt(start);
  for(let i=0;i<count;i++,g++) out.writeFloatLE(lane(epoch,g),i*4);
  return crypto.createHash("sha256").update(out).digest("hex");
}
function hc(x,y,z,epoch=0){
  x=BigInt(x);y=BigInt(y);z=Number(z);epoch=Number(epoch);
  const xl=Number(x&0xffffffffn)>>>0,xh=Number((x>>32n)&0xffffffffn)>>>0;
  const yl=Number(y&0xffffffffn)>>>0,yh=Number((y>>32n)&0xffffffffn)>>>0;
  const h=mix32((xl^rotl32(yl,11)^imul(xh,0x9e3779b1)^imul(yh,0x85ebca6b)^imul(z,0x27d4eb2d)^imul(epoch,0xc2b2ae35))>>>0);
  const h2=mix32((h^rotl32(xl,7)^rotl32(yh,17)^imul(z+1,0x165667b1)^0x27d4eb2d)>>>0);
  const h3=mix32((h2^rotl32(yl,5)^imul(z+3,0xd3a2646c)^imul(epoch+1,0x9e3779b1))>>>0);
  return [h,h2,h3];
}
function rgba(x,y,z,epoch=0){
  const [h,h2,h3]=hc(x,y,z,epoch);
  const density=(h>>>8)&255, depthBias=Math.trunc(38*(z/999));
  return [
    Math.max(0,Math.min(255,30+(h2&127)+Math.trunc(depthBias/3))),
    Math.max(0,Math.min(255,42+((h2>>>8)&127)+Math.trunc(depthBias/2))),
    Math.max(0,Math.min(255,58+((h3>>>16)&159)+depthBias)),
    Math.max(16,Math.min(255,48+density))
  ];
}
let bad=0;
function ok(name,pass){console.log((pass?"PASS":"FAIL"),name);if(!pass)bad++}
ok("carrier anchor",crypto.createHash("sha256").update(base).digest("hex")===ref.carrierBasePageSha256);
const st=avk(1_000_000); ok("AVK chain",st.a===ref.avkChainAt1000000.a&&st.b===ref.avkChainAt1000000.b&&st.c===ref.avkChainAt1000000.c);
for(const c of ref.computeRangeChallenges) ok(`compute ${c.laneStart}`,rangeHash(c.epoch,BigInt(c.laneStart),c.laneCount)===c.sha256);
ok("physical proof slice",rangeHash(ref.physicalProofSlice.epoch,BigInt(ref.physicalProofSlice.laneStart),ref.physicalProofSlice.laneCount)===ref.physicalProofSlice.sha256);
for(const c of ref.voxelChallenges){
  const rr=rgba(BigInt(c.x),BigInt(c.y),c.z,c.epoch), code=hc(BigInt(c.x),BigInt(c.y),c.z,c.epoch);
  ok(`voxel ${c.x},${c.y},${c.z}`,JSON.stringify(rr)===JSON.stringify(c.rgba)&&JSON.stringify(code)===JSON.stringify(c.code));
}
ok("EXAFLOP envelope",prog.flopEquivalentPerEpoch===1_000_000_000_000&&prog.representedFlopEquivalentPerSecondAt1MHz===1_000_000_000_000_000_000);
process.exit(bad?1:0);
