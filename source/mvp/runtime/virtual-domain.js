/* Domain-wide TQL-1 virtual epoch commitments.
 * A commit applies the universal procedural definition to the entire domain.
 * Its hash records that model transition; it is not a physical iteration proof.
 */
(function(root,factory){
  'use strict';
  var api=factory();
  if(typeof module==='object'&&module.exports)module.exports=api;
  else root.VirtualDomain=api;
}(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  var LANES=5000000000000000000000000n,OPS=LANES*2n,MAX_EPOCH=4294967295n;
  var VERSION='1.0.0',SCHEMA='QUETTAFLOP/VIRTUAL-DOMAIN/1';
  var SEMANTICS='procedural whole-domain epoch transition; values materialized on query';
  function decimal(value,name){
    if(typeof value==='bigint'&&value>=0n&&value<(1n<<256n))return value;
    if(typeof value==='number'&&Number.isSafeInteger(value)&&value>=0)return BigInt(value);
    if(typeof value==='string'&&value.length<=78&&/^(0|[1-9][0-9]*)$/.test(value)){var result=BigInt(value);if(result<(1n<<256n))return result;}
    throw new TypeError(name+' must be an exact nonnegative decimal integer.');
  }
  function hashValue(value,name){
    if(typeof value!=='string'||!/^[0-9a-f]{64}$/i.test(value))throw new TypeError(name+' must contain 64 hexadecimal characters.');
    return value.toLowerCase();
  }
  function copy(value){return JSON.parse(JSON.stringify(value));}
  function digest(value,hashText){
    if(typeof hashText!=='function')throw new TypeError('A synchronous SHA-256 text callback is required.');
    return hashValue(hashText(JSON.stringify(value)),'SHA-256 callback result');
  }
  function definition(word,binding,seed,initial){
    return {
      schema:'QUETTAFLOP/VIRTUAL-DOMAIN-DEFINITION/1',version:VERSION,
      semantics:SEMANTICS,
      function:'TQL-1 f(lane,epoch) = a(lane,epoch) * b(lane,epoch) + c(lane,epoch)',
      operandBinding:'Canonical lane operands depend on wordSha256, lane and epoch; sessionSeed selects materialized queries.',
      operationIdentity:'For each epoch, operation 2*lane multiplies a and b; operation 2*lane+1 adds c to that product.',
      wordSha256:word,carrierBinding:binding,sessionSeed:seed,initialEpoch:initial.toString(),
      domain:{laneStart:'0',laneEndExclusive:LANES.toString(),lanes:LANES.toString(),operationStart:'0',operationEndExclusive:OPS.toString(),operationsPerEpoch:OPS.toString()},
      executionClaim:'Full virtual model dispatch by universal definition. Physical sample evaluation is separate; this chain does not prove full host arithmetic execution.'
    };
  }
  function commitFields(sequence,start,count,previous){
    return {
      sequence:sequence.toString(),epochStart:start.toString(),epochEnd:(start+count-1n).toString(),epochCount:count.toString(),
      virtualLanesPerEpoch:LANES.toString(),virtualOperationsPerEpoch:OPS.toString(),virtualOperations:(OPS*count).toString(),previousHash:previous
    };
  }
  function ownerOfOperation(operationId,cells){
    var id=decimal(operationId,'operationId'),bins=decimal(cells===undefined?1060:cells,'cells');
    if(id>=OPS)throw new RangeError('operationId is outside the virtual domain.');
    if(bins<1n||bins>LANES)throw new RangeError('cells must be between 1 and the domain lane count.');
    var lane=id/2n;
    return ((lane+1n)*bins+LANES-1n)/LANES-1n;
  }
  function create(params,hashText){
    if(!params||typeof params!=='object'||Array.isArray(params))throw new TypeError('Domain parameters must be an object.');
    var allowed=['wordSha256','carrierBinding','sessionSeed','initialEpoch'];
    Object.keys(params).forEach(function(key){if(allowed.indexOf(key)<0)throw new TypeError('Unknown parameter: '+key);});
    var word=hashValue(params.wordSha256,'wordSha256'),binding=hashValue(params.carrierBinding,'carrierBinding'),seed=hashValue(params.sessionSeed,'sessionSeed');
    var initial=decimal(params.initialEpoch,'initialEpoch');
    if(initial>MAX_EPOCH)throw new RangeError('initialEpoch must be between 0 and 4294967295.');
    var model=definition(word,binding,seed,initial),initialHash=digest(model,hashText);
    var ledger={
      schema:SCHEMA,version:VERSION,semantics:SEMANTICS,
      wordSha256:word,carrierBinding:binding,sessionSeed:seed,initialEpoch:initial.toString(),
      definition:model,initialHash:initialHash,currentEpoch:initial.toString(),
      virtualLanesPerEpoch:LANES.toString(),virtualOperationsPerEpoch:OPS.toString(),
      epochTransitions:'0',virtualOperations:'0',commits:[]
    };
    function snapshot(){return copy(ledger);}
    function advance(epochCount){
      var count=decimal(epochCount,'epochCount'),current=BigInt(ledger.currentEpoch);
      if(count<1n)throw new RangeError('epochCount must be positive.');
      if(current+count>MAX_EPOCH)throw new RangeError('The transition exceeds the canonical 32-bit epoch range.');
      var previous=ledger.commits.length?ledger.commits[ledger.commits.length-1].hash:initialHash;
      var fields=commitFields(BigInt(ledger.commits.length)+1n,current+1n,count,previous);
      var record=Object.assign({},fields,{hash:digest(fields,hashText)});
      ledger.commits.push(record);
      ledger.currentEpoch=(current+count).toString();
      ledger.epochTransitions=(BigInt(ledger.epochTransitions)+count).toString();
      ledger.virtualOperations=(BigInt(ledger.virtualOperations)+count*OPS).toString();
      return {commit:copy(record),snapshot:snapshot()};
    }
    var job={advance:advance,snapshot:snapshot,ownerOfOperation:ownerOfOperation};
    Object.defineProperty(job,'currentEpoch',{enumerable:true,get:function(){return ledger.currentEpoch;}});
    return job;
  }
  function verify(actual,hashText){
    var errors=[],expected;
    try{
      if(!actual||typeof actual!=='object'||Array.isArray(actual))throw new TypeError('Snapshot must be an object.');
      var replay=create({wordSha256:actual.wordSha256,carrierBinding:actual.carrierBinding,sessionSeed:actual.sessionSeed,initialEpoch:actual.initialEpoch},hashText);
      if(!Array.isArray(actual.commits))throw new TypeError('commits must be an array.');
      for(var i=0;i<actual.commits.length;i++){
        if(!actual.commits[i]||typeof actual.commits[i]!=='object')throw new TypeError('Commit '+i+' has invalid structure.');
        replay.advance(actual.commits[i].epochCount);
      }
      expected=replay.snapshot();
    }catch(error){return {ok:false,errors:[error.message]};}
    function compare(value,wanted,path){
      if(errors.length>=50)return;
      if(wanted===null||typeof wanted!=='object'){
        if(value!==wanted)errors.push(path+' does not match the regenerated virtual commitment.');
        return;
      }
      if(!value||typeof value!=='object'||Array.isArray(value)!==Array.isArray(wanted)){errors.push(path+' has invalid structure.');return;}
      if(Array.isArray(wanted)&&value.length!==wanted.length)errors.push(path+' has an incorrect length.');
      var keys=Object.keys(wanted),actualKeys=Object.keys(value);
      if(keys.length!==actualKeys.length||actualKeys.some(function(key){return !Object.prototype.hasOwnProperty.call(wanted,key);}))errors.push(path+' has unexpected or missing fields.');
      keys.forEach(function(key){compare(value[key],wanted[key],path+'.'+key);});
    }
    compare(actual,expected,'snapshot');
    return {ok:errors.length===0,errors:errors};
  }
  return {version:VERSION,schema:SCHEMA,domainLanes:LANES.toString(),domainOperations:OPS.toString(),create:create,verify:verify,ownerOfOperation:ownerOfOperation};
}));
