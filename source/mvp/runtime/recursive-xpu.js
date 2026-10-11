/* Exact configurable modular recurrence with binary affine fast-forwarding.
 * Virtual multiply/add work is distinct from actual host BigInt arithmetic.
 */
(function(root,factory){
  'use strict';
  var api=factory(root);
  if(typeof module==='object'&&module.exports)module.exports=api;
  else root.RecursiveXPU=api;
}(typeof globalThis!=='undefined'?globalThis:this,function(root){
  'use strict';
  var RATE=4000000000000000000000000000000n;
  var STEPS_PER_NS=2000000000000000000000n;
  var MAX_NS=1000000000000000000000n;
  var MAX_MODULUS=(1n<<64n)-1n;
  var DEFAULTS={a:'48271',b:'1',modulus:'2305843009213693951',seed:'1'};
  var SCHEMA='RECURSIVE-VIRTUAL-XPU/1',VERSION='1.0.0';
  function copy(value){return JSON.parse(JSON.stringify(value));}
  function decimal(value,name){
    var integer;
    if(typeof value==='bigint'&&value>=0n)integer=value;
    else if(typeof value==='number'&&Number.isSafeInteger(value)&&value>=0)integer=BigInt(value);
    else if(typeof value==='string'&&value.length<=78&&/^(0|[1-9][0-9]*)$/.test(value))integer=BigInt(value);
    else throw new TypeError(name+' must be an exact nonnegative decimal integer.');
    if(integer>=(1n<<256n))throw new RangeError(name+' exceeds the 256-bit input limit.');
    return integer;
  }
  function nanoseconds(seconds){
    if(typeof seconds!=='string'||seconds.length>128||!/^(0|[1-9][0-9]*)(\.[0-9]{1,9})?$/.test(seconds))throw new TypeError('SECONDS must be a decimal string with at most nine fractional digits.');
    var parts=seconds.split('.'),ns=BigInt(parts[0])*1000000000n+BigInt((parts[1]||'').padEnd(9,'0'));
    if(ns<1n||ns>MAX_NS)throw new RangeError('Duration must be positive and at most 1000000000000 virtual seconds.');
    return ns;
  }
  function formatVirtualSeconds(value){
    var ns=decimal(value,'virtualNanoseconds'),seconds=ns/1000000000n,fraction=ns%1000000000n;
    return seconds.toString()+(fraction?'.'+fraction.toString().padStart(9,'0').replace(/0+$/,''):'');
  }
  function work(){return {multiplications:0n,additions:0n,reductions:0n,compositions:0n};}
  function serializedWork(value){return {multiplications:value.multiplications.toString(),additions:value.additions.toString(),reductions:value.reductions.toString(),compositions:value.compositions.toString()};}
  function addWork(target,source){Object.keys(target).forEach(function(key){target[key]+=BigInt(source[key]);});}
  function compose(left,right,m,counter){
    var multiplier=left[0]*right[0]%m;
    var increment=(left[0]*right[1]+left[1])%m;
    counter.multiplications+=2n;counter.additions++;counter.reductions+=2n;counter.compositions++;
    return [multiplier,increment];
  }
  function power(a,b,n,m,counter){
    var result=[1n,0n],base=[a,b];
    while(n>0n){
      if(n&1n)result=compose(base,result,m,counter);
      n>>=1n;
      if(n>0n)base=compose(base,base,m,counter);
    }
    return result;
  }
  function apply(transform,state,m,counter){
    var result=(transform[0]*state+transform[1])%m;
    counter.multiplications++;counter.additions++;counter.reductions++;
    return result;
  }
  function optionsValue(options){
    if(typeof options==='function')options={hashText:options};
    if(!options||typeof options.hashText!=='function')throw new TypeError('A synchronous SHA-256 hashText callback is required.');
    if(options.now!==undefined&&typeof options.now!=='function')throw new TypeError('now must be a function.');
    return options;
  }
  function digest(value,hashText){
    var hash=hashText(JSON.stringify(value));
    if(typeof hash!=='string'||!/^[0-9a-f]{64}$/i.test(hash))throw new TypeError('hashText must synchronously return 64 SHA-256 hexadecimal characters.');
    return hash.toLowerCase();
  }
  function create(params,options){
    params=params||{};options=optionsValue(options);
    if(typeof params!=='object'||Array.isArray(params))throw new TypeError('Parameters must be an object.');
    Object.keys(params).forEach(function(key){if(['a','b','modulus','seed'].indexOf(key)<0)throw new TypeError('Unknown parameter: '+key);});
    var m=decimal(params.modulus===undefined?DEFAULTS.modulus:params.modulus,'modulus');
    if(m<2n||m>MAX_MODULUS)throw new RangeError('modulus must be between 2 and 18446744073709551615.');
    var a=decimal(params.a===undefined?DEFAULTS.a:params.a,'a'),b=decimal(params.b===undefined?DEFAULTS.b:params.b,'b'),seed=decimal(params.seed===undefined?DEFAULTS.seed:params.seed,'seed');
    if(a>=m||b>=m||seed>=m)throw new RangeError('a, b and seed must be in [0, modulus).');
    var normalized={a:a.toString(),b:b.toString(),modulus:m.toString(),seed:seed.toString()};
    var definition={schema:SCHEMA,version:VERSION,params:normalized,recurrence:'x[n+1] = (a*x[n] + b) mod modulus',virtualOperationsPerStep:'2',rateVirtualOperationsPerSecond:RATE.toString(),stepsPerVirtualNanosecond:STEPS_PER_NS.toString(),maxVirtualNanoseconds:MAX_NS.toString(),arithmetic:'Exact modular BigInt recurrence. Binary affine composition evaluates the final state; virtual work counts the recurrence multiply and add. Host integer work excludes bookkeeping and hashes.'};
    var initialHash=digest(definition,options.hashText);
    var state=seed,time=0n,totalSteps=0n,receipts=[],transitionWork=work(),lastTrace=null,commandsExecuted=0n;
    var clock=options.now||function(){return root.performance&&typeof root.performance.now==='function'?root.performance.now():Date.now();};
    function elapsed(start){var end=Number(clock());return Number.isFinite(start)&&Number.isFinite(end)?Math.max(0,end-start):0;}
    function snapshot(){
      return copy({schema:SCHEMA,version:VERSION,definition:definition,params:normalized,initialState:seed.toString(),state:state.toString(),virtualNanoseconds:time.toString(),totalSteps:totalSteps.toString(),totalVirtualOperations:(totalSteps*2n).toString(),rateVirtualOperationsPerSecond:RATE.toString(),initialHash:initialHash,transitionHostIntegerWork:serializedWork(transitionWork),receipts:receipts});
    }
    function advance(seconds){
      var duration=nanoseconds(seconds);if(time+duration>MAX_NS)throw new RangeError('Cumulative virtual time exceeds 1000000000000 seconds.');
      var started=Number(clock()),steps=duration*STEPS_PER_NS,counter=work(),transform=power(a,b,steps,m,counter),after=apply(transform,state,m,counter);
      var body={sequence:(BigInt(receipts.length)+1n).toString(),virtualTimeStartNanoseconds:time.toString(),virtualTimeEndNanoseconds:(time+duration).toString(),virtualDurationNanoseconds:duration.toString(),steps:steps.toString(),operations:(steps*2n).toString(),stateBefore:state.toString(),stateAfter:after.toString(),affineMultiplier:transform[0].toString(),affineIncrement:transform[1].toString(),hostIntegerWork:serializedWork(counter),previousHash:receipts.length?receipts[receipts.length-1].hash:initialHash};
      var receipt=Object.assign({},body,{hash:digest(body,options.hashText)});
      receipt.elapsedMs=elapsed(started); // Untrusted host telemetry, outside the hash.
      receipts.push(receipt);state=after;time+=duration;totalSteps+=steps;addWork(transitionWork,counter);
      return {receipt:copy(receipt),snapshot:snapshot(),elapsedMs:receipt.elapsedMs,hostIntegerWork:serializedWork(counter)};
    }
    function query(stepValue){
      var step=decimal(stepValue,'STEP');if(step>totalSteps)throw new RangeError('STEP exceeds the completed recurrence.');
      var started=Number(clock()),counter=work(),trace;
      if(step===0n)trace={step:'0',state:seed.toString(),previous:null,a:a.toString(),b:b.toString(),modulus:m.toString(),product:null,rawSum:null,result:seed.toString(),digit:Number(seed%10n),operations:[],initialPreview:true};
      else{
        var previous=apply(power(a,b,step-1n,m,counter),seed,m,counter);
        var product=a*previous,rawSum=product+b,result=rawSum%m;
        counter.multiplications++;counter.additions++;counter.reductions++;
        trace={step:step.toString(),state:result.toString(),previous:previous.toString(),a:a.toString(),b:b.toString(),modulus:m.toString(),product:product.toString(),rawSum:rawSum.toString(),result:result.toString(),digit:Number(result%10n),operations:[{id:((step-1n)*2n).toString(),kind:'multiply',left:a.toString(),right:previous.toString(),result:product.toString()},{id:((step-1n)*2n+1n).toString(),kind:'add',left:product.toString(),right:b.toString(),result:rawSum.toString()}],reduction:{modulus:m.toString(),result:result.toString()},initialPreview:false};
      }
      trace.hostIntegerWork=serializedWork(counter);trace.elapsedMs=elapsed(started);return trace;
    }
    function queryOperation(idValue){
      var id=decimal(idValue,'operationId');if(id>=totalSteps*2n)throw new RangeError('operationId exceeds the completed recurrence.');
      var trace=query(id/2n+1n);
      return {operation:trace.operations[Number(id%2n)],trace:trace,step:trace.step,state:trace.state,hostIntegerWork:trace.hostIntegerWork,elapsedMs:trace.elapsedMs};
    }
    function sample(count){
      if(!Number.isSafeInteger(count)||count<1||count>1060)throw new RangeError('Sample count must be between 1 and 1060.');
      var started=Number(clock()),counter=work(),entries=[],bins=BigInt(count);
      if(totalSteps===0n){
        for(var preview=0;preview<count;preview++)entries.push({index:preview,stepRangeStart:'0',stepRangeEndInclusive:'0',stepRangeCount:'0',operationStart:'0',operationEndExclusive:'0',representedOperations:'0',sampleStep:'0',state:seed.toString(),sampledState:seed.toString(),digit:Number(seed%10n),initialPreview:true});
      }else{
        if(totalSteps<bins)throw new RangeError('The recurrence has fewer steps than sample bins.');
        var spacing=totalSteps/bins,regular=power(a,b,spacing,m,counter),extra=compose([a,b],regular,m,counter),previousEnd=0n,currentState=seed;
        for(var i=0;i<count;i++){
          var start=BigInt(i)*totalSteps/bins+1n,end=BigInt(i+1)*totalSteps/bins,delta=end-previousEnd;
          currentState=apply(delta===spacing?regular:extra,currentState,m,counter);
          entries.push({index:i,stepRangeStart:start.toString(),stepRangeEndInclusive:end.toString(),stepRangeCount:(end-start+1n).toString(),operationStart:((start-1n)*2n).toString(),operationEndExclusive:(end*2n).toString(),representedOperations:((end-start+1n)*2n).toString(),sampleStep:end.toString(),state:currentState.toString(),sampledState:currentState.toString(),digit:Number(currentState%10n),initialPreview:false});
          previousEnd=end;
        }
      }
      var counts=serializedWork(counter);
      return {entries:entries,accounting:{representedSteps:totalSteps.toString(),representedOperations:(totalSteps*2n).toString(),sampledStates:count.toString(),initialPreview:totalSteps===0n,hostIntegerWork:counts,coverage:'Exact contiguous partition of every completed recurrence step; each upper endpoint state is evaluated with affine jumps.'},hostIntegerWork:counts,elapsedMs:elapsed(started)};
    }
    function execute(line){
      var started=Number(clock()),command='',data=null,output='',counts=serializedWork(work()),committed='0',clear=false;
      try{
        if(typeof line!=='string'||line.length>2048)throw new TypeError('Command must have at most 2048 characters.');
        var tokens=line.trim().split(/\s+/);if(!line.trim()||tokens.length>4)throw new TypeError('Enter a contained command with at most four tokens.');
        command=tokens[0].toLowerCase();
        function arity(n){if(tokens.length!==n)throw new TypeError('Wrong argument count. Type help.');}
        if(command==='help'){arity(1);output='help | status | advance SECONDS | eval STEP | op ID | verify | registers | clear\nEach virtual second represents exactly 4e30 recurrence multiply/add operations. Final and queried states are calculated by exact modular BigInt affine jumps.';}
        else if(command==='status'){arity(1);data=snapshot();output='Recursive virtual XPU\nx[n+1] = ('+normalized.a+'*x[n] + '+normalized.b+') mod '+normalized.modulus+'\nState: '+data.state+'\nVirtual seconds: '+formatVirtualSeconds(data.virtualNanoseconds)+'\nSteps: '+data.totalSteps+'\nVirtual operations: '+data.totalVirtualOperations+'\nRate: '+RATE.toString()+' virtual operations per virtual second. Actual host arithmetic is modular BigInt.';}
        else if(command==='advance'){arity(2);data=advance(tokens[1]);counts=data.hostIntegerWork;committed=data.receipt.operations;output='Advanced '+formatVirtualSeconds(data.receipt.virtualDurationNanoseconds)+' virtual seconds / '+data.receipt.steps+' steps / '+committed+' virtual operations.\nState '+data.receipt.stateBefore+' -> '+data.receipt.stateAfter;}
        else if(command==='eval'){arity(2);data=query(tokens[1]);counts=data.hostIntegerWork;lastTrace=copy(data);output=data.initialPreview?'Initial state: '+data.state:'Step '+data.step+': '+data.a+' * '+data.previous+' = '+data.product+'; + '+data.b+' = '+data.rawSum+'; mod '+data.modulus+' = '+data.state;}
        else if(command==='op'){arity(2);data=queryOperation(tokens[1]);counts=data.hostIntegerWork;lastTrace=copy(data.trace);output='Operation '+data.operation.id+' ['+data.operation.kind+']: '+data.operation.left+' '+(data.operation.kind==='multiply'?'*':'+')+' '+data.operation.right+' = '+data.operation.result+'\nStep state after modular reduction: '+data.state;}
        else if(command==='verify'){arity(1);data=verify(snapshot(),options);counts=data.hostIntegerWork;output=data.ok?'PASS: recurrence states, durations, counts, affine transforms and hash chain verified.':'FAIL: '+data.errors.join('; ');}
        else if(command==='registers'){arity(1);data={state:state.toString(),lastTrace:copy(lastTrace),commandsExecuted:commandsExecuted.toString()};output=JSON.stringify(data,null,2);}
        else if(command==='clear'){arity(1);clear=true;}
        else throw new TypeError('Unknown contained command. Type help.');
        commandsExecuted++;
        var success={ok:true,command:command,output:output,data:data,hostIntegerWork:counts,hostOperations:(BigInt(counts.multiplications)+BigInt(counts.additions)).toString(),hostFloatingPointOperations:'0',hostArithmeticKind:'BigInt integers',virtualOperationsCommitted:committed,elapsedMs:elapsed(started)};
        if(clear)success.clear=true;return success;
      }catch(error){return {ok:false,command:command,output:'ERROR: '+error.message,data:null,hostIntegerWork:serializedWork(work()),hostOperations:'0',hostFloatingPointOperations:'0',hostArithmeticKind:'BigInt integers',virtualOperationsCommitted:'0',elapsedMs:elapsed(started)};}
    }
    return {advance:advance,advanceVirtualSeconds:advance,snapshot:snapshot,query:query,evaluateStep:query,queryOperation:queryOperation,sample:sample,execute:execute};
  }
  function verify(actual,options){
    var errors=[],counter=work(),expected;
    try{
      options=optionsValue(options);
      if(!actual||typeof actual!=='object'||Array.isArray(actual))throw new TypeError('Snapshot must be an object.');
      var replay=create(actual.params,options);
      if(!Array.isArray(actual.receipts))throw new TypeError('receipts must be an array.');
      for(var i=0;i<actual.receipts.length;i++){
        var record=actual.receipts[i];if(!record||typeof record!=='object')throw new TypeError('Receipt has invalid structure.');
        var result=replay.advance(formatVirtualSeconds(decimal(record.virtualDurationNanoseconds,'receipt duration')));addWork(counter,result.hostIntegerWork);
      }
      expected=replay.snapshot();
    }catch(error){return {ok:false,errors:[error.message],hostIntegerWork:serializedWork(counter)};}
    function compare(value,wanted,path){
      if(errors.length>=50)return;
      if(path.slice(-10)==='.elapsedMs'){
        if(typeof value!=='number'||!Number.isFinite(value)||value<0)errors.push(path+' must contain finite nonnegative telemetry.');return;
      }
      if(wanted===null||typeof wanted!=='object'){if(value!==wanted)errors.push(path+' differs from the regenerated recurrence.');return;}
      if(!value||typeof value!=='object'||Array.isArray(value)!==Array.isArray(wanted)){errors.push(path+' has invalid structure.');return;}
      if(Array.isArray(wanted)&&value.length!==wanted.length)errors.push(path+' has an incorrect length.');
      var keys=Object.keys(wanted),actualKeys=Object.keys(value);
      if(keys.length!==actualKeys.length||actualKeys.some(function(key){return !Object.prototype.hasOwnProperty.call(wanted,key);}))errors.push(path+' has unexpected or missing fields.');
      keys.forEach(function(key){compare(value[key],wanted[key],path+'.'+key);});
    }
    compare(actual,expected,'snapshot');
    return {ok:errors.length===0,errors:errors,hostIntegerWork:serializedWork(counter)};
  }
  return {version:VERSION,schema:SCHEMA,defaultParams:copy(DEFAULTS),rateVirtualOperationsPerSecond:RATE.toString(),stepsPerVirtualNanosecond:STEPS_PER_NS.toString(),maxVirtualNanoseconds:MAX_NS.toString(),formatVirtualSeconds:formatVirtualSeconds,create:create,verify:verify};
}));
