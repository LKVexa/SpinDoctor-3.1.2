/* Execute every lane of a bounded TQL-1 domain for every chosen epoch.
 * The two counted model operations are an explicitly rounded multiply and
 * add. Hash/address work and storage are separate implementation overhead.
 */
(function (root, factory) {
  'use strict';
  var api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.FullDomain = api;
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  var DOMAIN_LANES = 5000000000000000000000000n;
  var MAX_LANES = 1000000;
  var MAX_EPOCHS = 100;
  var MASK32 = 0xffffffffn;

  function decimal(value, name) {
    if (typeof value === 'bigint' && value >= 0n && value < (1n << 256n)) return value;
    if (typeof value === 'number' && Number.isSafeInteger(value) && value >= 0) return BigInt(value);
    if (typeof value === 'string' && value.length <= 78 && /^(0|[1-9][0-9]*)$/.test(value)) { var result = BigInt(value); if (result < (1n << 256n)) return result; }
    throw new TypeError(name + ' must be an exact nonnegative decimal integer.');
  }
  function integer(value, name, maximum) {
    if (!Number.isSafeInteger(value) || value < 1 || value > maximum) throw new RangeError(name + ' must be an integer between 1 and ' + maximum + '.');
    return value;
  }
  function wordSalt(hash) {
    if (typeof hash !== 'string' || !/^[0-9a-f]{64}$/i.test(hash)) throw new TypeError('wordSha256 must contain exactly 64 hexadecimal characters.');
    var salt = [];
    for (var i = 0; i < 3; i++) {
      var o = i * 8;
      salt.push((parseInt(hash.slice(o,o+2),16) | parseInt(hash.slice(o+2,o+4),16)<<8 | parseInt(hash.slice(o+4,o+6),16)<<16 | parseInt(hash.slice(o+6,o+8),16)<<24) >>> 0);
    }
    return salt;
  }
  function rol32(x,n) { return ((x<<n)|(x>>>(32-n)))>>>0; }
  function mix32(x) {
    x>>>=0; x^=x>>>16; x=Math.imul(x,0x7feb352d)>>>0;
    x^=x>>>15; x=Math.imul(x,0x846ca68b)>>>0;
    x^=x>>>16; return x>>>0;
  }
  function bank(size) {
    return {a:new Float32Array(size),b:new Float32Array(size),c:new Float32Array(size),product:new Float32Array(size),result:new Float32Array(size)};
  }
  function createJob(params) {
    if (!params || typeof params !== 'object' || Array.isArray(params)) throw new TypeError('Job parameters must be an object.');
    var allowed=['wordSha256','start','laneCount','epoch','epochs'];
    Object.keys(params).forEach(function(key){if(allowed.indexOf(key)<0)throw new TypeError('Unknown parameter: '+key);});
    var salt=wordSalt(params.wordSha256), word=params.wordSha256.toLowerCase();
    var start=decimal(params.start===undefined?'0':params.start,'start');
    var laneCount=integer(params.laneCount,'laneCount',MAX_LANES);
    var epoch=decimal(params.epoch,'epoch'), epochs=integer(params.epochs,'epochs',MAX_EPOCHS);
    var finalEpoch=epoch+BigInt(epochs)-1n;
    if(start+BigInt(laneCount)>DOMAIN_LANES)throw new RangeError('The finite lane range exceeds the canonical domain.');
    if(finalEpoch>MASK32)throw new RangeError('All epochs must be between 0 and 4294967295.');
    var firstL0=Number(start&MASK32),firstL1=Number((start>>32n)&MASK32),firstL2=Number((start>>64n)&MASK32);
    var l0=firstL0,l1=firstL1,l2=firstL2;
    var completed=null, scratch=bank(laneCount), spare=bank(laneCount);
    var cursor=0,completedEpochs=0,executedLanes=0n,cancelled=false,done=false;

    function snapshot() {
      return {
        schema:'QUETTAFLOP/FULL-DOMAIN-JOB/1',version:'1.0.0',wordSha256:word,
        start:start.toString(),endExclusive:(start+BigInt(laneCount)).toString(),laneCount:laneCount,
        epochStart:epoch.toString(),epochEndInclusive:finalEpoch.toString(),epochs:epochs,
        currentEpoch:(done?finalEpoch:epoch+BigInt(completedEpochs)).toString(),
        lastCompletedEpoch:completedEpochs?(epoch+BigInt(completedEpochs)-1n).toString():null,
        completedEpochs:completedEpochs,cursor:cursor,
        executedLanes:executedLanes.toString(),executedOperations:(executedLanes*2n).toString(),
        plannedLanes:(BigInt(laneCount)*BigInt(epochs)).toString(),plannedOperations:(BigInt(laneCount)*BigInt(epochs)*2n).toString(),
        partialEpochLanes:done?0:cursor,done:done,cancelled:cancelled,
        operationModel:'Every finite-domain lane executes a separately FP32-rounded multiply and add in every chosen epoch. Counts cover those two source operations; hash, address, and storage overhead are excluded.',
        retention:'Five Float32 buffers retain the last completed epoch; a partial later epoch uses a separate staging bank.'
      };
    }
    function step(maxLanes) {
      integer(maxLanes,'maxLanes',Number.MAX_SAFE_INTEGER);
      if(done||cancelled)return snapshot();
      var budget=maxLanes;
      while(budget>0&&!done) {
        var e=Number(epoch)+completedEpochs;
        var limit=Math.min(laneCount-cursor,budget);
        var stop=cursor+limit;
        for(var i=cursor;i<stop;i++) {
          var h0=mix32(l0^rol32(l1,7)^rol32(l2,13)^e^salt[0]);
          var h1=mix32(l1^rol32(l2,11)^rol32(l0,17)^Math.imul(e,0x9e3779b1)^salt[1]);
          var h2=mix32(l2^rol32(l0,5)^rol32(l1,19)^Math.imul(e,0x85ebca6b)^salt[2]);
          var a=(h0&2047)-1024,b=(h1&2047)-1024,c=(h2&2047)-1024;
          var product=Math.fround(a*b);
          var result=Math.fround(product+c);
          scratch.a[i]=a;scratch.b[i]=b;scratch.c[i]=c;scratch.product[i]=product;scratch.result[i]=result;
          l0=(l0+1)>>>0;if(l0===0){l1=(l1+1)>>>0;if(l1===0)l2=(l2+1)>>>0;}
        }
        executedLanes+=BigInt(limit);cursor=stop;budget-=limit;
        if(cursor===laneCount) {
          var oldCompleted=completed;
          completed=scratch;
          scratch=oldCompleted||spare;
          completedEpochs++;
          if(completedEpochs===epochs)done=true;
          else {cursor=0;l0=firstL0;l1=firstL1;l2=firstL2;}
        }
      }
      return snapshot();
    }
    function cancel() { if(!done)cancelled=true;return snapshot(); }
    function completedBuffers() { return completed; }
    function result() {
      if(!done)throw new Error('The requested finite domain and epochs have not completed.');
      return {metadata:snapshot(),buffers:completed};
    }
    function trace(index) {
      if(!Number.isSafeInteger(index)||index<0||index>=laneCount)throw new RangeError('Trace index is outside the finite domain.');
      if(!completed)throw new Error('No epoch has completed yet.');
      var lane=start+BigInt(index),traceEpoch=epoch+BigInt(completedEpochs)-1n;
      var a=completed.a[index],b=completed.b[index],c=completed.c[index],product=completed.product[index],value=completed.result[index];
      return {
        index:index,epoch:traceEpoch.toString(),lane:lane.toString(),a:a,b:b,c:c,product:product,result:value,
        digit:((value%10)+10)%10,
        operations:[{id:(lane*2n).toString(),kind:'multiply',left:a,right:b,result:product},{id:(lane*2n+1n).toString(),kind:'add',left:product,right:c,result:value}],
        source:'retained completed-epoch Float32 buffers; no arithmetic pair re-evaluated'
      };
    }
    return {step:step,snapshot:snapshot,cancel:cancel,result:result,trace:trace,sampleTrace:trace,completedBuffers:completedBuffers};
  }
  return {version:'1.0.0',domainLanes:DOMAIN_LANES.toString(),maxLanes:MAX_LANES,maxEpochs:MAX_EPOCHS,createJob:createJob};
}));
