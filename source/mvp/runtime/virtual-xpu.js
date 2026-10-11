/* Contained software XPU terminal. The command grammar is fixed: no eval,
 * shell, filesystem, network, or arbitrary JavaScript commands are accepted.
 */
(function(root,factory){
  'use strict';
  var loadDomain=typeof module==='object'&&module.exports?function(){return require('./virtual-domain.js');}:null;
  var api=factory(root,loadDomain);
  if(typeof module==='object'&&module.exports)module.exports=api;
  else root.VirtualXPU=api;
}(typeof globalThis!=='undefined'?globalThis:this,function(root,loadDomain){
  'use strict';
  var QUETTA_OPERATIONS=1000000000000000000000000000000n;
  var EPOCHS_PER_QUETTA=100000n;
  var VIRTUAL_OPERATIONS_PER_EPOCH=10000000000000000000000000n;
  var DEFAULT_QUETTA='4';
  var HELP='help | status | advance N | qflop [N] | eval LANE [EPOCH] | op ID [EPOCH] | fma A B C | verify | registers | clear\nqflop defaults to 4 and commits N * 100000 epochs: exactly N * 10^30 virtual operations. This is a work quantity, not a measured physical FLOPS rate.\nEpoch queries are restricted to committed virtual epochs. Queries materialize one canonical multiply/add pair. Virtual dispatch and host materialization counts are separate.';
  function decimal(text,name){
    if(typeof text!=='string'||!/^(0|[1-9][0-9]*)$/.test(text))throw new TypeError(name+' must be a nonnegative decimal integer.');
    return BigInt(text);
  }
  function formatQuettaOperations(value){
    var count;
    if(typeof value==='bigint'&&value>=0n)count=value;
    else if(typeof value==='number'&&Number.isSafeInteger(value)&&value>=0)count=BigInt(value);
    else count=decimal(value,'operation count');
    var whole=count/QUETTA_OPERATIONS,remainder=count%QUETTA_OPERATIONS;
    if(remainder===0n)return whole.toString();
    return whole.toString()+'.'+remainder.toString().padStart(30,'0').replace(/0+$/,'');
  }
  function create(config){
    if(!config||typeof config!=='object'||Array.isArray(config))throw new TypeError('VirtualXPU configuration must be an object.');
    ['getDomain','evaluateLane','queryOperation','hashText'].forEach(function(key){if(typeof config[key]!=='function')throw new TypeError(key+' must be a function.');});
    if(config.onAdvance!==undefined&&typeof config.onAdvance!=='function')throw new TypeError('onAdvance must be a function.');
    if(config.verifyDomain!==undefined&&typeof config.verifyDomain!=='function')throw new TypeError('verifyDomain must be a function.');
    if(config.now!==undefined&&typeof config.now!=='function')throw new TypeError('now must be a function.');
    var clock=config.now||function(){return root.performance&&typeof root.performance.now==='function'?root.performance.now():Date.now();};
    var state={lastLane:null,lastEpoch:null,lastResult:null,lastWordSha256:null,commandsExecuted:0n,totalHostOperations:0n};
    function domainState(){
      var domain=config.getDomain();
      if(!domain||typeof domain.snapshot!=='function'||typeof domain.advance!=='function')throw new Error('The virtual domain is unavailable.');
      var snapshot=domain.snapshot();
      if(!snapshot||!snapshot.definition||!snapshot.definition.domain)throw new Error('The virtual domain snapshot is invalid.');
      return {domain:domain,snapshot:snapshot};
    }
    function registers(snapshot){
      return {lastLane:state.lastLane,lastEpoch:state.lastEpoch,lastResult:state.lastResult,lastWordSha256:state.lastWordSha256,
        commandsExecuted:state.commandsExecuted.toString(),totalHostOperations:state.totalHostOperations.toString(),
        currentEpoch:snapshot.currentEpoch,virtualOperations:snapshot.virtualOperations,
        hostAccounting:'Terminal materialized multiply/add pairs only; UI rendering, hashing and address work are excluded.'};
    }
    function verify(snapshot){
      var verifier=config.verifyDomain||(root.VirtualDomain&&root.VirtualDomain.verify);
      if(!verifier&&loadDomain)verifier=loadDomain().verify;
      if(typeof verifier!=='function')throw new Error('The virtual-domain verifier is unavailable.');
      return verifier(snapshot,config.hashText);
    }
    function queryEpoch(text,snapshot){
      var epoch=text===undefined?BigInt(snapshot.currentEpoch):decimal(text,'EPOCH');
      if(epoch<=BigInt(snapshot.initialEpoch)||epoch>BigInt(snapshot.currentEpoch))throw new RangeError('EPOCH must be in the committed span ('+snapshot.initialEpoch+', '+snapshot.currentEpoch+']. Run advance N to commit new epochs.');
      return epoch.toString();
    }
    function arity(tokens,min,max){if(tokens.length<min||tokens.length>max)throw new TypeError('Wrong argument count. Type help for the command grammar.');}
    function smallInteger(text){
      if(typeof text!=='string'||! /^-?(0|[1-9][0-9]*)$/.test(text))throw new TypeError('fma operands must be integers from -1024 through 1023.');
      var value=Number(text);
      if(!Number.isInteger(value)||value < -1024||value > 1023)throw new RangeError('fma operands must be integers from -1024 through 1023.');
      return value;
    }
    function execute(line){
      var started=Number(clock()),command='',host=0n,committed=0n,output='',data=null,clear=false,nextRegisters=null,advanceResult=null;
      function result(ok,error){
        var ended=Number(clock()),elapsed=Number.isFinite(started)&&Number.isFinite(ended)?Math.max(0,ended-started):0;
        var response={ok:ok,command:command,output:error?'ERROR: '+error:output,data:data,elapsedMs:elapsed,hostOperations:host.toString(),virtualOperationsCommitted:committed.toString()};
        if(clear)response.clear=true;
        return response;
      }
      try{
        if(typeof line!=='string'||line.length>2048)throw new TypeError('Command must be text with at most 2048 characters.');
        var trimmed=line.trim();if(!trimmed)throw new TypeError('Enter a command. Type help for the command grammar.');
        var tokens=trimmed.split(/\s+/);if(tokens.length>8)throw new TypeError('At most 8 command tokens are allowed.');
        command=tokens[0].toLowerCase();
        if(['help','status','advance','qflop','eval','op','verify','registers','fma','clear'].indexOf(command)<0)throw new TypeError('Unknown command. Type help for the contained command grammar.');
        var live=domainState(),domain=live.domain,snapshot=live.snapshot,word=snapshot.definition.wordSha256;
        if(command==='help'){arity(tokens,1,1);output=HELP;}
        else if(command==='status'){
          arity(tokens,1,1);data={virtualLanes:snapshot.definition.domain.lanes,virtualOperationsPerEpoch:snapshot.virtualOperationsPerEpoch,currentEpoch:snapshot.currentEpoch,virtualOperations:snapshot.virtualOperations,virtualQuettaOperations:formatQuettaOperations(snapshot.virtualOperations),epochTransitions:snapshot.epochTransitions,terminalHostOperations:state.totalHostOperations.toString(),semantics:snapshot.semantics};
          output='VIRTUAL XPU READY\nDomain lanes: '+data.virtualLanes+'\nVirtual operations per epoch: '+data.virtualOperationsPerEpoch+'\nCommitted epoch: '+data.currentEpoch+'\nCommitted virtual operations: '+data.virtualOperations+'\nCommitted virtual work: '+data.virtualQuettaOperations+' quettaFLOP-equivalent operations (quantity; physical FLOPS rate not measured)\nTerminal host materializations: '+data.terminalHostOperations+' source operations\n'+data.semantics;
        }else if(command==='advance'){
          arity(tokens,2,2);var count=decimal(tokens[1],'N');if(count<1n)throw new RangeError('N must be positive.');
          advanceResult=domain.advance(count);committed=BigInt(advanceResult.commit.virtualOperations);data=advanceResult;
          output='Committed virtual epochs '+advanceResult.commit.epochStart+' through '+advanceResult.commit.epochEnd+' across the full domain.\nVirtual operations committed: '+committed.toString()+'. Physical lane arithmetic is materialized on query.';
        }else if(command==='qflop'){
          arity(tokens,1,2);var quetta=decimal(tokens.length===1?DEFAULT_QUETTA:tokens[1],'N');if(quetta<1n)throw new RangeError('N must be positive.');
          var quettaEpochs=quetta*EPOCHS_PER_QUETTA;
          advanceResult=domain.advance(quettaEpochs);committed=BigInt(advanceResult.commit.virtualOperations);data=advanceResult;
          data.virtualQuettaOperations=formatQuettaOperations(committed);
          data.totalVirtualQuettaOperations=formatQuettaOperations(advanceResult.snapshot.virtualOperations);
          output='Committed '+data.virtualQuettaOperations+' quettaFLOP-equivalent virtual operations: '+committed.toString()+' exact operations.\nEpochs '+advanceResult.commit.epochStart+' through '+advanceResult.commit.epochEnd+' ('+quettaEpochs.toString()+' epochs).\nTotal committed virtual work: '+data.totalVirtualQuettaOperations+' quettaFLOP-equivalent operations ('+advanceResult.snapshot.virtualOperations+' exact operations).\nThis is a virtual work quantity, not a measured physical FLOPS rate.';
        }else if(command==='eval'){
          arity(tokens,2,3);var lane=decimal(tokens[1],'LANE');if(lane>=BigInt(snapshot.definition.domain.lanes))throw new RangeError('LANE is outside the virtual domain.');
          var epoch=queryEpoch(tokens[2],snapshot);data=config.evaluateLane({wordSha256:word,epoch:epoch,lane:lane.toString()});host=2n;
          nextRegisters={lastLane:lane.toString(),lastEpoch:epoch,lastResult:data.result,lastWordSha256:word};
          output='lane '+lane.toString()+' @ epoch '+epoch+': '+data.a+' * '+data.b+' = '+data.product+'; '+data.product+' + '+data.c+' = '+data.result+'; digit '+data.digit+'\nHost materialized source operations: 2. Virtual commitments unchanged.';
        }else if(command==='op'){
          arity(tokens,2,3);var id=decimal(tokens[1],'ID');if(id>=BigInt(snapshot.definition.domain.operationEndExclusive))throw new RangeError('ID is outside the virtual operation domain.');
          var opEpoch=queryEpoch(tokens[2],snapshot);data=config.queryOperation({wordSha256:word,epoch:opEpoch,operationId:id.toString()});host=2n;
          nextRegisters={lastLane:data.lane,lastEpoch:opEpoch,lastResult:data.operation.result,lastWordSha256:word};
          output='op '+data.operation.id+' @ epoch '+opEpoch+' ['+data.operation.kind+']: '+data.operation.left+' '+(data.operation.kind==='multiply'?'*':'+')+' '+data.operation.right+' = '+data.operation.result+'\nThe canonical lane pair materialized 2 host source operations. Virtual commitments unchanged.';
        }else if(command==='fma'){
          arity(tokens,4,4);var a=smallInteger(tokens[1]),b=smallInteger(tokens[2]),c=smallInteger(tokens[3]);
          var product=Math.fround(a*b),value=Math.fround(product+c);host=2n;
          data={a:a,b:b,c:c,product:product,result:value,operationModel:'separate FP32-rounded multiply then add; no fused instruction'};
          nextRegisters={lastLane:null,lastEpoch:null,lastResult:value,lastWordSha256:null};
          output=a+' * '+b+' = '+product+'; '+product+' + '+c+' = '+value+'\nHost source operations: 2. Virtual commitments unchanged.';
        }else if(command==='verify'){
          arity(tokens,1,1);data=verify(snapshot);output=data.ok?'PASS: virtual definition, binding, ranges, counts and commitment chain verified.':'FAIL: '+data.errors.join('; ');
        }else if(command==='registers'){arity(tokens,1,1);data=registers(snapshot);output=JSON.stringify(data,null,2);}
        else if(command==='clear'){arity(tokens,1,1);clear=true;output='';}
        // Commit terminal registers only after a valid command completed.
        if(nextRegisters)Object.keys(nextRegisters).forEach(function(key){state[key]=nextRegisters[key];});
        state.commandsExecuted++;state.totalHostOperations+=host;
        if(advanceResult&&config.onAdvance){
          try{config.onAdvance(advanceResult);}catch(refreshError){output+='\nDisplay refresh failed: '+refreshError.message;}
        }
        return result(true);
      }catch(error){
        host=0n;committed=0n;data=null;return result(false,error.message);
      }
    }
    return {execute:execute,registers:function(){return registers(domainState().snapshot);}};
  }
  return {version:'1.1.0',create:create,formatQuettaOperations:formatQuettaOperations,quettaOperations:QUETTA_OPERATIONS.toString(),epochsPerQuetta:EPOCHS_PER_QUETTA.toString(),virtualOperationsPerEpoch:VIRTUAL_OPERATIONS_PER_EPOCH.toString(),defaultQuetta:DEFAULT_QUETTA};
}));
