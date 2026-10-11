"use strict";
const M=window.LUNAR127_TENQ_METRICS||{notRun:true};
const R=SimulatorRates.calculate(M);
const FALLBACK_WORD_SHA256="8222fd2c7b321703b9e6f44fa22a7a7cb069aa2d7cd311fb00f6c2eeb1549b83";
const CARRIER_PROGRAM_BINDING_SHA256="4ef0a6992dfdbe039a7cb6aa4b67b5de98bd3798f1fb4563ca0e6342c32e86b6";
const te=new TextEncoder();
function hexBytes(h){if(!/^[0-9a-f]{64}$/i.test(h||""))return null;const a=new Uint8Array(32);for(let i=0;i<32;i++)a[i]=parseInt(h.slice(i*2,i*2+2),16);return a}
function bytesHex(a){return Array.from(a,b=>b.toString(16).padStart(2,"0")).join("")}
function concatBytes(...parts){let n=0;for(const p of parts)n+=p.length;const out=new Uint8Array(n);let o=0;for(const p of parts){out.set(p,o);o+=p.length}return out}
function u32le(a,o){return (a[o]|a[o+1]<<8|a[o+2]<<16|a[o+3]<<24)>>>0}
function u32leBytes(x){return new Uint8Array([x&255,(x>>>8)&255,(x>>>16)&255,(x>>>24)&255])}
function rol32(x,n){return ((x<<n)|(x>>>(32-n)))>>>0}function mix32(x){x>>>=0;x^=x>>>16;x=Math.imul(x,0x7feb352d)>>>0;x^=x>>>15;x=Math.imul(x,0x846ca68b)>>>0;x^=x>>>16;return x>>>0}
function sha256Bytes(msg){
  const K=[0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];
  const H=[0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19];
  const n=msg.length,total=((n+9+63)>>6)<<6,p=new Uint8Array(total);p.set(msg);p[n]=0x80;const bits=n*8,hi=Math.floor(bits/4294967296),lo=bits>>>0;
  p[total-8]=(hi>>>24)&255;p[total-7]=(hi>>>16)&255;p[total-6]=(hi>>>8)&255;p[total-5]=hi&255;p[total-4]=(lo>>>24)&255;p[total-3]=(lo>>>16)&255;p[total-2]=(lo>>>8)&255;p[total-1]=lo&255;
  const w=new Uint32Array(64);
  for(let o=0;o<total;o+=64){for(let i=0;i<16;i++){const j=o+i*4;w[i]=((p[j]<<24)|(p[j+1]<<16)|(p[j+2]<<8)|p[j+3])>>>0}for(let i=16;i<64;i++){const a=w[i-15],b=w[i-2],s0=(rol32(a,25)^rol32(a,14)^(a>>>3))>>>0,s1=(rol32(b,15)^rol32(b,13)^(b>>>10))>>>0;w[i]=(w[i-16]+s0+w[i-7]+s1)>>>0}
    let [a,b,c,d,e,f,g,h]=H;
    for(let i=0;i<64;i++){const S1=(rol32(e,26)^rol32(e,21)^rol32(e,7))>>>0,ch=((e&f)^((~e)&g))>>>0,t1=(h+S1+ch+K[i]+w[i])>>>0,S0=(rol32(a,30)^rol32(a,19)^rol32(a,10))>>>0,maj=((a&b)^(a&c)^(b&c))>>>0,t2=(S0+maj)>>>0;h=g;g=f;f=e;e=(d+t1)>>>0;d=c;c=b;b=a;a=(t1+t2)>>>0}
    H[0]=(H[0]+a)>>>0;H[1]=(H[1]+b)>>>0;H[2]=(H[2]+c)>>>0;H[3]=(H[3]+d)>>>0;H[4]=(H[4]+e)>>>0;H[5]=(H[5]+f)>>>0;H[6]=(H[6]+g)>>>0;H[7]=(H[7]+h)>>>0;
  }
  const out=new Uint8Array(32);for(let i=0;i<8;i++){out[i*4]=(H[i]>>>24)&255;out[i*4+1]=(H[i]>>>16)&255;out[i*4+2]=(H[i]>>>8)&255;out[i*4+3]=H[i]&255}return out
}
function sha256Hex(msg){return bytesHex(sha256Bytes(msg))}
function newGuidText(){if(globalThis.crypto&&typeof crypto.randomUUID==="function")return crypto.randomUUID();const a=new Uint8Array(16);crypto.getRandomValues(a);a[6]=(a[6]&15)|64;a[8]=(a[8]&63)|128;const h=bytesHex(a);return `${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`}
let runCounter=0;
function generateLaunchSeed(){if(!globalThis.crypto||typeof crypto.getRandomValues!=="function")throw new Error("OS cryptographic RNG is unavailable in this browser context");const rng=new Uint8Array(32);crypto.getRandomValues(rng);runCounter++;const meta=te.encode(`${Date.now()}|${performance.now()}|${runCounter}|${newGuidText()}`);return bytesHex(sha256Bytes(concatBytes(rng,meta)))}

function activeWordHash(){return (/^[0-9a-f]{64}$/i.test(M?.verification?.wordSha256||"")?M.verification.wordSha256:FALLBACK_WORD_SHA256).toLowerCase()}
function activeCarrierBinding(){return (/^[0-9a-f]{64}$/i.test(M?.rasterRandomization?.carrierBindingSha256||"")?M.rasterRandomization.carrierBindingSha256:CARRIER_PROGRAM_BINDING_SHA256).toLowerCase()}
function deriveSessionSeed(launchHex){const l=hexBytes(launchHex),c=hexBytes(activeCarrierBinding()),w=hexBytes(activeWordHash());if(!l||!c||!w)throw new Error("Cannot derive session seed: a 256-bit binding value is invalid");return bytesHex(sha256Bytes(concatBytes(l,c,w)))}

const $=id=>document.getElementById(id),cv=$('cv'),raster=NumberRaster.create(cv);
let fullJob=null,runToken=0,launchSeed='',sessionSeed='',computeMs=0,wallStart=0,wallMs=0,drawMs=0,verificationOps=0,cellEntries=[],lastExport=null;
const addressLimit=5000000000000000000000000n;
function fmt(n,d=3){return Number(n).toLocaleString(undefined,{maximumFractionDigits:d});}
function rateText(rate){if(!Number.isFinite(rate)||rate<=0)return 'timer resolution insufficient';const units=[[1e9,'GFLOP/s'],[1e6,'MFLOP/s'],[1e3,'kFLOP/s'],[1,'FLOP/s']];const u=units.find(v=>rate>=v[0])||units.at(-1);return (rate/u[0]).toFixed(3)+' '+u[1];}
function message(text){$('executionStatus').textContent=text;}
function showCounters(){
  if(!fullJob)return;const s=fullJob.snapshot(),ops=Number(s.executedOperations);
  $('domainSize').textContent=fmt(s.laneCount,0)+' lanes';$('domainOps').textContent=fmt(s.laneCount*2,0)+' counted operations/epoch';
  $('completedEpochs').textContent=s.completedEpochs+' / '+s.epochs;
  $('executedOps').textContent=fmt(ops,0);$('plannedOps').textContent='Planned: '+fmt(s.plannedOperations,0);
  $('coverage').textContent=s.done?'100% of every selected epoch executed.':s.cancelled?'Stopped: partial work counted; unfinished epochs are not complete.':'Partial current epoch: '+fmt(s.partialEpochLanes,0)+' / '+fmt(s.laneCount,0)+' lanes';
  $('executedRate').textContent=computeMs>0?rateText(ops/(computeMs/1000)):'not measured';
  $('executedFormula').textContent=`${fmt(ops,0)} actual counted operations ÷ ${(computeMs/1000).toFixed(6)} s active execution time. ${s.done?'Completed workload.':s.cancelled?'Partial stopped workload.':'In progress; partial rate.'}`;
  $('rangeLabel').textContent=`[${s.start}, ${s.endExclusive}) · epochs ${s.epochStart}..${s.epochEndInclusive}`;
  message(`${s.done?'COMPLETE':s.cancelled?'STOPPED':'EXECUTING'}\nCompleted epochs: ${s.completedEpochs}/${s.epochs}\nExecuted lane evaluations: ${s.executedLanes}\nExecuted multiply/add operations: ${s.executedOperations}\nActive execution time: ${computeMs.toFixed(3)} ms (operand generation + both arithmetic operations + result writes)\nWall time: ${wallMs.toFixed(3)} ms (includes scheduling and final digit render)\nFinal render: ${drawMs.toFixed(3)} ms\nStored final/last complete epoch: ${s.lastCompletedEpoch??'none'}\nSingle workload observation; short runs are not stable peak-performance measurements.\nLaunch seed: ${launchSeed}\nSession seed: ${sessionSeed}\nWord SHA-256: ${activeWordHash()}\nSeparate reference-verification work: ${verificationOps} arithmetic operations`);
}
function renderCompleted(){
  if(!fullJob?.completedBuffers())return;const t=performance.now(),s=fullJob.snapshot(),n=s.laneCount,columns=53,rows=20;
  cellEntries=[];
  for(let i=0;i<columns*rows;i++){const first=Math.floor(i*n/(columns*rows)),end=Math.floor((i+1)*n/(columns*rows));cellEntries.push({index:i,first,end,displayed:first});}
  raster.render(640,360,(c,r)=>fullJob.trace(cellEntries[r*columns+c].displayed).digit);
  drawMs=performance.now()-t;
  $('hud').textContent=`53 × 20 digits · ${fmt(n,0)} stored executed lane results · epoch ${s.lastCompletedEpoch}\n${n===1060?'Every lane is visible.':'Each digit is a representative; every lane in its range was executed and stored.'}\nDrawing reads stored results; 0 new lane arithmetic evaluations.`;
  inspectCell(0);$('operationId').value=String(BigInt(s.start)*2n);
}
function inspectLane(index){
  if(!fullJob?.completedBuffers()){ $('trace').textContent='No completed epoch yet.';return; }
  try{const q=fullJob.trace(index);$('laneIndex').value=String(index);$('trace').textContent=`STORED LANE INDEX ${index}\nEpoch ${q.epoch} · global lane ID ${q.lane}\n${q.operations.map(o=>`ID ${o.id}: ${o.left} ${o.kind==='multiply'?'×':'+'} ${o.right} = ${o.result}`).join('\n')}\nStored digit ${q.digit}\nLookup reads buffers; no lane arithmetic was repeated.`;}catch(e){$('trace').textContent='ERROR: '+e.message;}
}
function inspectCell(index){if(!Number.isInteger(index)||index<0||index>=cellEntries.length){$('trace').textContent='Cell index must be 0..1059.';return;}const c=cellEntries[index];$('cellIndex').value=String(index);inspectLane(c.displayed);$('trace').textContent=`CELL ${index}: executed lane indices [${c.first}, ${c.end}) · ${2*(c.end-c.first)} arithmetic operations in this cell range\n`+$('trace').textContent;}
function workStep(token){
  if(token!==runToken||!fullJob||fullJob.snapshot().cancelled)return;
  try{
    const t=performance.now();fullJob.step(8192);computeMs+=performance.now()-t;
    const s=fullJob.snapshot();wallMs=performance.now()-wallStart;
    if(s.done){renderCompleted();wallMs=performance.now()-wallStart;showCounters();$('overall').textContent='FULL DOMAIN COMPLETE';return;}
    showCounters();requestAnimationFrame(()=>workStep(token));
  }catch(e){fullJob.cancel();wallMs=performance.now()-wallStart;showCounters();$('overall').textContent='EXECUTION ERROR';message('ERROR: '+e.message);}
}
function startExecution(seed){try{
  if(!/^[0-9a-f]{64}$/i.test(seed))throw Error('Replay seed must be 64 hexadecimal characters.');
  const n=Number($('laneCount').value),epochs=Number($('epochCount').value),epoch=$('epochInput').value.trim();
  const nextLaunchSeed=seed.toLowerCase(),nextSessionSeed=deriveSessionSeed(nextLaunchSeed);
  const start=$('automaticRange').checked?(BigInt('0x'+nextSessionSeed.slice(0,24))%(addressLimit-BigInt(n)+1n)).toString():$('startLane').value.trim();
  const candidate=FullDomain.createJob({wordSha256:activeWordHash(),start,laneCount:n,epoch,epochs});
  fullJob?.cancel();fullJob=candidate;launchSeed=nextLaunchSeed;sessionSeed=nextSessionSeed;const token=++runToken;cv.width=640;
  computeMs=0;wallMs=0;drawMs=0;verificationOps=0;cellEntries=[];lastExport=null;
  $('startLane').value=start;$('currentSeed').value=launchSeed;$('overall').textContent='EXECUTING';$('trace').textContent='Executing new range; no completed epoch yet.';$('hud').textContent='Executing all selected lanes…';$('verificationStatus').textContent='Verification has not run.';
  wallStart=performance.now();showCounters();requestAnimationFrame(()=>workStep(token));
}catch(e){message('ERROR: '+e.message);}}
function stopExecution(){if(!fullJob)return;++runToken;fullJob.cancel();wallMs=performance.now()-wallStart;renderCompleted();showCounters();$('overall').textContent=fullJob.snapshot().done?'FULL DOMAIN COMPLETE':'STOPPED';}
function runSimulation(){try{startExecution($('randomizeOnRun').checked?generateLaunchSeed():$('customSeed').value.trim()||launchSeed);}catch(e){message('ERROR: '+e.message);}}
function replaySeed(){startExecution($('customSeed').value.trim()||launchSeed);}
async function copySeed(){try{await navigator.clipboard.writeText(launchSeed);}catch(e){message('Copy the current seed field manually.');}}
function exportEnvelope(){
  if(!fullJob?.completedBuffers())throw Error('No completed epoch to export.');
  const s=fullJob.snapshot(),b=fullJob.completedBuffers();
  const data={schema:'QUETTAFLOP/FULL-FINITE-RESULTS/1',wordSha256:activeWordHash(),carrierProgramBinding:activeCarrierBinding(),launchSeed,sessionSeed,execution:s,timing:{activeComputeMs:computeMs,wallMs,drawMs},retainedEpoch:s.lastCompletedEpoch,retention:'All selected lanes of last completed epoch; earlier epoch results can be regenerated from canonical addresses.',buffers:Object.fromEntries(Object.entries(b).map(([k,v])=>[k,Array.from(v)]))};
  return {schema:'QUETTAFLOP/FULL-FINITE-EXPORT/1',dataSha256:sha256Hex(te.encode(JSON.stringify(data))),data};
}
function verifyAll(data,token,onFinish){
  try{
    if(!data||data.schema!=='QUETTAFLOP/FULL-FINITE-RESULTS/1'||data.wordSha256!==activeWordHash()||data.carrierProgramBinding!==activeCarrierBinding()||deriveSessionSeed(data.launchSeed)!==data.sessionSeed)throw Error('Results do not match the seed/program identity.');
    const s=data.execution,n=s.laneCount;
    const probe=FullDomain.createJob({wordSha256:data.wordSha256,start:s.start,laneCount:n,epoch:s.epochStart,epochs:s.epochs}).snapshot();
    for(const key of ['schema','version','wordSha256','start','endExclusive','laneCount','epochStart','epochEndInclusive','epochs','plannedLanes','plannedOperations','operationModel','retention'])if(s[key]!==probe[key])throw Error('Planned range/count or execution definition mismatch: '+key+'.');
    if(!Number.isSafeInteger(s.completedEpochs)||s.completedEpochs<1||s.completedEpochs>s.epochs||!Number.isSafeInteger(s.partialEpochLanes)||s.partialEpochLanes<0||s.partialEpochLanes>=n||(s.completedEpochs===s.epochs&&s.partialEpochLanes!==0))throw Error('Invalid completion counts.');
    const total=s.completedEpochs*n+s.partialEpochLanes;
    if(s.executedLanes!==String(total)||s.executedOperations!==String(total*2)||data.retainedEpoch!==String(Number(s.epochStart)+s.completedEpochs-1)||s.lastCompletedEpoch!==data.retainedEpoch||s.done!==(s.completedEpochs===s.epochs&&s.partialEpochLanes===0))throw Error('Executed epoch/count mismatch.');
    if(typeof s.cancelled!=='boolean'||(s.done&&s.cancelled)||s.currentEpoch!==(s.done?probe.epochEndInclusive:String(Number(s.epochStart)+s.completedEpochs))||s.cursor!==(s.done?n:s.partialEpochLanes))throw Error('Current execution state mismatch.');
    const keys=['a','b','c','product','result'];for(const k of keys)if(!Array.isArray(data.buffers[k])||data.buffers[k].length!==n)throw Error('Buffer length/type mismatch: '+k);
    let cursor=0;
    function checkChunk(){if(token!==runToken)return;try{
      const end=Math.min(n,cursor+2048);
      for(;cursor<end;cursor++){
        const q=FlopLedger.evaluateLane({wordSha256:data.wordSha256,epoch:data.retainedEpoch,lane:(BigInt(s.start)+BigInt(cursor)).toString()});verificationOps+=2;
        for(const k of keys)if(data.buffers[k][cursor]!==q[k])throw Error('Lane '+cursor+' '+k+' mismatch.');
      }
      $('verificationStatus').textContent=`Verifying every stored result: ${cursor}/${n} lanes · ${verificationOps} separate reference arithmetic operations`;
      if(cursor<n){requestAnimationFrame(checkChunk);return;}
      onFinish({ok:true,lanes:n,referenceOperations:n*2});
    }catch(e){onFinish({ok:false,error:e.message});}}
    requestAnimationFrame(checkChunk);
  }catch(e){onFinish({ok:false,error:e.message});}
}
$('runSimulation').onclick=runSimulation;$('stopExecution').onclick=stopExecution;$('replaySeed').onclick=replaySeed;$('copySeed').onclick=copySeed;
$('automaticRange').onchange=e=>$('startLane').readOnly=e.target.checked;
$('inspectCell').onclick=()=>inspectCell(Number($('cellIndex').value));$('inspectLane').onclick=()=>inspectLane(Number($('laneIndex').value));
cv.addEventListener('click',e=>{const r=cv.getBoundingClientRect(),c=Math.floor((e.clientX-r.left)*cv.width/r.width/12),y=Math.floor((e.clientY-r.top)*cv.height/r.height/18);if(c>=0&&c<53&&y>=0&&y<20)inspectCell(y*53+c);});
$('queryOperation').onclick=()=>{try{if(!fullJob?.completedBuffers())throw Error('No completed epoch.');const id=$('operationId').value.trim();if(!/^(0|[1-9][0-9]*)$/.test(id))throw Error('Operation ID must be a nonnegative decimal integer.');const op=BigInt(id),s=fullJob.snapshot(),index=op/2n-BigInt(s.start);if(index<0n||index>=BigInt(s.laneCount))throw Error('Operation is outside the executed finite domain.');const q=fullJob.trace(Number(index));$('operationTrace').textContent=JSON.stringify({epoch:q.epoch,lane:q.lane,operation:q.operations[Number(op%2n)]},null,2)+'\nRead from retained execution buffers; 0 additional lane arithmetic operations.';}catch(e){$('operationTrace').textContent='ERROR: '+e.message;}};
$('verifyResults').onclick=()=>{try{const data=exportEnvelope().data;verifyAll(data,runToken,r=>{$('verificationStatus').textContent=r.ok?`PASS: all ${r.lanes} stored lanes recomputed; ${r.referenceOperations} separate reference operations. This checks retained values, not historical execution provenance.`:'FAIL: '+r.error;showCounters();});}catch(e){$('verificationStatus').textContent='ERROR: '+e.message;}};
$('exportResults').onclick=()=>{try{lastExport=exportEnvelope();const blob=new Blob([JSON.stringify(lastExport)],{type:'application/json'}),url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=`QuettaFLOP-full-epoch-${lastExport.data.retainedEpoch}.json`;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);}catch(e){$('verificationStatus').textContent='ERROR: '+e.message;}};
$('importResults').onchange=async e=>{try{const f=e.target.files?.[0];if(!f)return;if(f.size>80000000)throw Error('Export exceeds the 80 MB import limit.');const saved=JSON.parse(await f.text());if(saved.schema!=='QUETTAFLOP/FULL-FINITE-EXPORT/1'||sha256Hex(te.encode(JSON.stringify(saved.data)))!==saved.dataSha256)throw Error('Saved results hash/schema mismatch.');verifyAll(saved.data,runToken,r=>{$('verificationStatus').textContent=r.ok?`SAVED RESULTS PASS: all ${r.lanes} retained lanes verified. Current canvas is unchanged. Historical execution counters are recorded metadata, not a signed hardware attestation.`:'SAVED RESULTS FAIL: '+r.error;showCounters();});}catch(e){$('verificationStatus').textContent='ERROR: '+e.message;}};
function loadHostEvidence(){
  $('representedRate').textContent=R.measured?R.representedQ.toFixed(3)+' QFLOP/s-eq':'not measured';$('clock').textContent=R.measured?R.clockMHz.toFixed(6)+' MHz':'not measured';
  $('physical').textContent=R.physicalFlops?rateText(R.physicalFlops):'not measured';$('physicalDetails').textContent=R.physicalFlops?`${fmt(M.physicalScalarFlopDiagnostic.countedFlops,0)} counted FP64 operations / ${M.physicalScalarFlopDiagnostic.elapsedSeconds} s. Separate host scalar diagnostic.`:'Run BOOT.cmd to generate host evidence.';
  $('host').textContent=`Host metrics: ${M.notRun?'not measured':R.consistent?'consistent':'MISMATCH: '+R.reason}\nWord SHA-256: ${activeWordHash()}\nOriginal symbolic domain: 5×10²⁴ lanes; 10²⁵ declared operations/epoch\nThis browser mode executes only the explicit selected finite domain. Its measured rate uses its own completed work and timing.`;
}
loadHostEvidence();
startExecution(/^[0-9a-f]{64}$/i.test(M.rasterRandomization?.launchSeedHex||'')?M.rasterRandomization.launchSeedHex:'0'.repeat(64));
