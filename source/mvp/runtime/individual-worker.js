/* Sequential modular recurrence executor. Every credited step executes its
 * own BigInt multiply, add and reduction. Readback work is counted separately.
 */
(function(root,factory){
  'use strict';var api=factory(root);
  if(typeof module==='object'&&module.exports)module.exports=api;else root.IndividualXPU=api;
}(typeof globalThis!=='undefined'?globalThis:this,function(root){
  'use strict';
  var VERSION='1.0.0',SCHEMA='INDIVIDUAL-XPU/1',MAX_STEPS=65536;
  var DEFAULT_TARGET='4000000000000000000000000000000';
  var DEFAULTS={a:'48271',b:'1',modulus:'2305843009213693951',seed:'1'};
  function copy(value){return JSON.parse(JSON.stringify(value));}
  function decimal(value,name){
    var result;
    if(typeof value==='bigint'&&value>=0n)result=value;
    else if(typeof value==='number'&&Number.isSafeInteger(value)&&value>=0)result=BigInt(value);
    else if(typeof value==='string'&&value.length<=78&&/^(0|[1-9][0-9]*)$/.test(value))result=BigInt(value);
    else throw new TypeError(name+' must be an exact nonnegative decimal integer.');
    if(result>=(1n<<256n))throw new RangeError(name+' exceeds the 256-bit input limit.');return result;
  }
  function bounded(value,name,maximum){if(!Number.isSafeInteger(value)||value<1||value>maximum)throw new RangeError(name+' must be an integer from 1 through '+maximum+'.');return value;}
  function hash(value,hashText){var result=hashText(JSON.stringify(value));if(typeof result!=='string'||!/^[0-9a-f]{64}$/i.test(result))throw new TypeError('hashText must synchronously return a SHA-256 hexadecimal string.');return result.toLowerCase();}
  function finiteRate(operations,ms){if(ms<=0)return null;var rate=Number(operations)*1000/ms;return Number.isFinite(rate)?rate:null;}
  function counters(n){return {multiplications:n.toString(),additions:n.toString(),reductions:n.toString()};}
  function bodyOf(record){return {sequence:record.sequence,stepsExecutedBefore:record.stepsExecutedBefore,stepsExecutedAfter:record.stepsExecutedAfter,stepsExecuted:record.stepsExecuted,stateBefore:record.stateBefore,stateAfter:record.stateAfter,actualMultiplications:record.actualMultiplications,actualAdditions:record.actualAdditions,actualReductions:record.actualReductions,actualOperations:record.actualOperations,previousHash:record.previousHash};}
  function create(params,options){
    params=params||{};options=options||{};
    if(typeof params!=='object'||Array.isArray(params))throw new TypeError('Parameters must be an object.');
    Object.keys(params).forEach(function(key){if(['a','b','modulus','seed'].indexOf(key)<0)throw new TypeError('Unknown parameter: '+key);});
    if(typeof options.hashText!=='function')throw new TypeError('A synchronous hashText callback is required.');
    if(options.now!==undefined&&typeof options.now!=='function')throw new TypeError('now must be a function.');
    var m=decimal(params.modulus===undefined?DEFAULTS.modulus:params.modulus,'modulus');
    if(m<2n||m>((1n<<64n)-1n))throw new RangeError('modulus must be between 2 and 18446744073709551615.');
    var a=decimal(params.a===undefined?DEFAULTS.a:params.a,'a'),b=decimal(params.b===undefined?DEFAULTS.b:params.b,'b'),seed=decimal(params.seed===undefined?DEFAULTS.seed:params.seed,'seed');
    if(a>=m||b>=m||seed>=m)throw new RangeError('a, b and seed must be in [0, modulus).');
    var target=decimal(options.targetOperations===undefined?DEFAULT_TARGET:options.targetOperations,'targetOperations');
    if(target<2n||target%2n!==0n)throw new RangeError('targetOperations must be positive and even.');
    var targetSteps=target/2n,historySize=bounded(options.historySize===undefined?1060:options.historySize,'historySize',MAX_STEPS);
    var normalized={a:a.toString(),b:b.toString(),modulus:m.toString(),seed:seed.toString()};
    var definition={schema:SCHEMA,version:VERSION,params:normalized,targetOperations:target.toString(),targetSteps:targetSteps.toString(),historySize:historySize,recurrence:'x[n+1] = (a*x[n] + b) mod modulus',accounting:'Each credited step individually executes one BigInt multiplication, one addition and one modular reduction. ActualOperations counts multiply plus add. Target progress excludes readback, bookkeeping and hashing.'};
    var hashText=options.hashText,initialHash=hash(definition,hashText),clock=options.now||function(){return root.performance&&typeof root.performance.now==='function'?root.performance.now():Date.now();};
    var state=seed,completed=0n,queryCount=0n,kernelMs=0,receipts=[],beforeRing=new BigUint64Array(historySize),afterRing=new BigUint64Array(historySize),nextSlot=0,retained=0,lastTrace=null;
    function readClock(){var value=Number(clock());return Number.isFinite(value)?value:0;}
    function snapshot(){return copy({schema:SCHEMA,version:VERSION,definition:definition,params:normalized,initialState:seed.toString(),state:state.toString(),targetOperations:target.toString(),targetSteps:targetSteps.toString(),completedSteps:completed.toString(),actualOperations:(completed*2n).toString(),actualMultiplications:completed.toString(),actualAdditions:completed.toString(),actualReductions:completed.toString(),remainingSteps:(targetSteps-completed).toString(),remainingOperations:(target-completed*2n).toString(),complete:completed===targetSteps,kernelElapsedMs:kernelMs,kernelOperationsPerSecond:finiteRate(completed*2n,kernelMs),kernelStepsPerSecond:finiteRate(completed,kernelMs),historySize:historySize,retainedSteps:retained,retainedStartStep:retained?(completed-BigInt(retained)+1n).toString():'0',retainedEndStep:completed.toString(),queryCalls:queryCount.toString(),queryHostIntegerWork:counters(queryCount),hostFloatingPointOperations:'0',initialHash:initialHash,receipts:receipts});}
    function run(maxSteps,budget){
      bounded(maxSteps,'maxSteps',MAX_STEPS);
      if(budget!==null&&(typeof budget!=='number'||!Number.isFinite(budget)||budget<=0||budget>16))throw new RangeError('timeBudgetMs must be positive and at most 16.');
      if(completed===targetSteps)return {receipt:null,snapshot:snapshot(),stepsExecuted:'0',actualOperations:'0',elapsedMs:0};
      var remaining=targetSteps-completed,limit=remaining<BigInt(maxSteps)?Number(remaining):maxSteps;
      var startTime=readClock(),stateBefore=state,completedBefore=completed,executed=0;
      while(executed<limit){
        if(budget!==null&&executed>0&&readClock()-startTime>=budget)break;
        var before=state;
        var product=a*before;
        var rawSum=product+b;
        var after=rawSum%m;
        beforeRing[nextSlot]=before;afterRing[nextSlot]=after;
        nextSlot++;if(nextSlot===historySize)nextSlot=0;
        state=after;executed++;
      }
      var measured=Math.max(0,readClock()-startTime),count=BigInt(executed);
      completed+=count;retained=Math.min(historySize,retained+executed);kernelMs+=measured;
      var body={sequence:(BigInt(receipts.length)+1n).toString(),stepsExecutedBefore:completedBefore.toString(),stepsExecutedAfter:completed.toString(),stepsExecuted:count.toString(),stateBefore:stateBefore.toString(),stateAfter:state.toString(),actualMultiplications:count.toString(),actualAdditions:count.toString(),actualReductions:count.toString(),actualOperations:(count*2n).toString(),previousHash:receipts.length?receipts[receipts.length-1].hash:initialHash};
      var receipt=Object.assign({},body,{hash:hash(body,hashText),elapsedMs:measured});receipts.push(receipt);
      return {receipt:copy(receipt),snapshot:snapshot(),stepsExecuted:count.toString(),actualOperations:(count*2n).toString(),elapsedMs:measured};
    }
    function executeSteps(value){var count=decimal(value,'stepCount');if(count<1n||count>BigInt(MAX_STEPS))throw new RangeError('stepCount must be from 1 through 65536.');return run(Number(count),null);}
    function runChunk(config){config=config||{};if(typeof config!=='object'||Array.isArray(config))throw new TypeError('Chunk settings must be an object.');Object.keys(config).forEach(function(key){if(['maxSteps','timeBudgetMs'].indexOf(key)<0)throw new TypeError('Unknown chunk setting: '+key);});return run(config.maxSteps===undefined?8192:config.maxSteps,config.timeBudgetMs===undefined?8:config.timeBudgetMs);}
    function recent(count){
      bounded(count,'count',1060);var entries=[];
      if(completed===0n){for(var p=0;p<count;p++)entries.push({step:'0',state:seed.toString(),digit:Number(seed%10n),initialPreview:true});}
      else{var length=Math.min(count,retained),slot=(nextSlot-length+historySize)%historySize,start=completed-BigInt(length)+1n;for(var i=0;i<length;i++){var value=afterRing[slot];entries.push({step:(start+BigInt(i)).toString(),state:value.toString(),digit:Number(value%10n),initialPreview:false});slot++;if(slot===historySize)slot=0;}}
      return {entries:entries,accounting:{retainedStartStep:retained?(completed-BigInt(retained)+1n).toString():'0',retainedEndStep:completed.toString(),retainedSteps:retained,sampledStates:completed===0n?'0':entries.length.toString(),completedSteps:completed.toString(),actualOperations:(completed*2n).toString(),initialPreview:completed===0n}};
    }
    function query(value){
      var step=decimal(value,'STEP');
      if(completed===0n&&step===0n)return {step:'0',state:seed.toString(),result:seed.toString(),digit:Number(seed%10n),operations:[],initialPreview:true,hostIntegerWork:counters(0n)};
      var lower=completed-BigInt(retained)+1n;
      if(step<lower||step>completed)throw new RangeError('STEP is not retained. Available executed steps: '+lower.toString()+' through '+completed.toString()+'.');
      var slot=Number((step-1n)%BigInt(historySize)),before=beforeRing[slot],stored=afterRing[slot];
      var product=a*before,rawSum=product+b,result=rawSum%m;queryCount++;
      if(result!==stored)throw new Error('Retained recurrence data is inconsistent.');
      return {step:step.toString(),previous:before.toString(),a:a.toString(),b:b.toString(),modulus:m.toString(),product:product.toString(),rawSum:rawSum.toString(),state:stored.toString(),result:stored.toString(),digit:Number(stored%10n),operations:[{id:((step-1n)*2n).toString(),kind:'multiply',left:a.toString(),right:before.toString(),result:product.toString()},{id:((step-1n)*2n+1n).toString(),kind:'add',left:product.toString(),right:b.toString(),result:rawSum.toString()}],reduction:{modulus:m.toString(),result:stored.toString()},initialPreview:false,hostIntegerWork:counters(1n),accounting:'Readback reconstructs one multiply/add/reduction, counted separately and never credited toward the execution target.'};
    }
    function queryOperation(value){var id=decimal(value,'operationId');if(id>=completed*2n)throw new RangeError('operationId has not been executed.');var trace=query(id/2n+1n);return {operation:trace.operations[Number(id%2n)],trace:trace,step:trace.step,state:trace.state,hostIntegerWork:trace.hostIntegerWork};}
    function execute(line){
      var started=readClock(),command='',data=null,output='',clear=false;
      try{
        if(typeof line!=='string'||line.length>2048||!line.trim())throw new TypeError('Enter a contained command of at most 2048 characters.');
        var tokens=line.trim().split(/\s+/);if(tokens.length>3)throw new TypeError('Too many command tokens.');command=tokens[0].toLowerCase();
        function arity(n){if(tokens.length!==n)throw new TypeError('Wrong argument count. Type help.');}
        if(command==='help'){arity(1);output='help | status | run STEPS | eval STEP | op ID | clear\nrun individually executes at most 65536 steps. Readback is restricted to retained executed steps. Target work is credited only after real sequential iterations.';}
        else if(command==='status'){arity(1);data=snapshot();output='Individually executed steps: '+data.completedSteps+'\nActual multiply/add operations: '+data.actualOperations+'\nActual modular reductions: '+data.actualReductions+'\nTarget operations: '+data.targetOperations+'\nRemaining operations: '+data.remainingOperations+'\nState: '+data.state+'\nComplete: '+data.complete;}
        else if(command==='run'){arity(2);data=executeSteps(tokens[1]);output='Individually executed '+data.stepsExecuted+' steps / '+data.actualOperations+' multiply/add operations. State: '+data.snapshot.state;}
        else if(command==='eval'){arity(2);data=query(tokens[1]);lastTrace=copy(data);output=data.initialPreview?'Initial seed preview, zero executed steps: '+data.state:'Executed step '+data.step+': '+data.a+' * '+data.previous+' = '+data.product+'; + '+data.b+' = '+data.rawSum+'; mod '+data.modulus+' = '+data.state+'\nReadback arithmetic is separate from target progress.';}
        else if(command==='op'){arity(2);data=queryOperation(tokens[1]);lastTrace=copy(data.trace);output='Executed operation '+data.operation.id+' ['+data.operation.kind+']: '+data.operation.left+' '+(data.operation.kind==='multiply'?'*':'+')+' '+data.operation.right+' = '+data.operation.result+'\nReadback arithmetic is separate from target progress.';}
        else if(command==='clear'){arity(1);clear=true;}
        else throw new TypeError('Unknown contained command. Type help.');
        var response={ok:true,command:command,output:output,data:data,elapsedMs:Math.max(0,readClock()-started)};if(clear)response.clear=true;return response;
      }catch(error){return {ok:false,command:command,output:'ERROR: '+error.message,data:null,elapsedMs:Math.max(0,readClock()-started)};}
    }
    return {executeSteps:executeSteps,runChunk:runChunk,snapshot:snapshot,recent:recent,query:query,evaluateStep:query,queryOperation:queryOperation,execute:execute};
  }
  function verifyRecords(actual,options){
    var errors=[];
    try{
      if(!actual||typeof actual!=='object'||Array.isArray(actual))throw new TypeError('Snapshot must be an object.');
      if(!options||typeof options.hashText!=='function')throw new TypeError('hashText is required.');
      var baseline=create(actual.params,{hashText:options.hashText,targetOperations:actual.targetOperations,historySize:actual.historySize,now:function(){return 0;}}).snapshot();
      var m=BigInt(baseline.params.modulus),target=BigInt(baseline.targetOperations),cursor=0n,state=BigInt(baseline.initialState),previous=baseline.initialHash,totalMs=0;
      function match(a,b,path){if(JSON.stringify(a)!==JSON.stringify(b))errors.push(path+' differs from the recorded execution structure.');}
      ['schema','version','definition','params','initialState','targetOperations','targetSteps','historySize','initialHash','hostFloatingPointOperations'].forEach(function(key){match(actual[key],baseline[key],key);});
      if(!Array.isArray(actual.receipts))throw new TypeError('receipts must be an array.');
      for(var i=0;i<actual.receipts.length;i++){
        var record=actual.receipts[i],count=decimal(record.stepsExecuted,'stepsExecuted'),after=decimal(record.stateAfter,'stateAfter');
        if(count<1n||count>BigInt(MAX_STEPS))throw new RangeError('A receipt chunk must contain 1 through 65536 steps.');
        if(after>=m)throw new RangeError('Receipt state is outside the configured modulus.');
        var expected={sequence:(BigInt(i)+1n).toString(),stepsExecutedBefore:cursor.toString(),stepsExecutedAfter:(cursor+count).toString(),stepsExecuted:count.toString(),stateBefore:state.toString(),stateAfter:after.toString(),actualMultiplications:count.toString(),actualAdditions:count.toString(),actualReductions:count.toString(),actualOperations:(count*2n).toString(),previousHash:previous};
        match(bodyOf(record),expected,'receipt '+i);match(record.hash,hash(expected,options.hashText),'receipt hash '+i);
        if(Object.keys(record).length!==13)errors.push('receipt '+i+' has unexpected or missing fields.');
        if(typeof record.elapsedMs!=='number'||!Number.isFinite(record.elapsedMs)||record.elapsedMs<0)throw new TypeError('Receipt elapsedMs must be finite nonnegative telemetry.');
        cursor+=count;state=after;previous=record.hash;totalMs+=record.elapsedMs;
      }
      if(cursor*2n>target)throw new RangeError('Recorded execution exceeds its target.');
      var retained=cursor<BigInt(actual.historySize)?Number(cursor):actual.historySize;
      var expectedTotals={state:state.toString(),completedSteps:cursor.toString(),actualOperations:(cursor*2n).toString(),actualMultiplications:cursor.toString(),actualAdditions:cursor.toString(),actualReductions:cursor.toString(),remainingSteps:(target/2n-cursor).toString(),remainingOperations:(target-cursor*2n).toString(),complete:cursor*2n===target,kernelElapsedMs:totalMs,kernelOperationsPerSecond:finiteRate(cursor*2n,totalMs),kernelStepsPerSecond:finiteRate(cursor,totalMs),retainedSteps:retained,retainedStartStep:retained?(cursor-BigInt(retained)+1n).toString():'0',retainedEndStep:cursor.toString()};
      Object.keys(expectedTotals).forEach(function(key){match(actual[key],expectedTotals[key],key);});
      var calls=decimal(actual.queryCalls,'queryCalls');match(actual.queryHostIntegerWork,counters(calls),'queryHostIntegerWork');
      if(Object.keys(actual).length!==Object.keys(baseline).length)errors.push('snapshot has unexpected or missing fields.');
    }catch(error){errors.push(error.message);}
    return {ok:errors.length===0,errors:errors,kind:'record-integrity',replayedSteps:'0',scope:'Hashes, binding, recorded counter continuity and receipt structure checked. Individual recurrence arithmetic was not replayed; timing telemetry is not execution proof.'};
  }
  return {version:VERSION,schema:SCHEMA,defaultParams:copy(DEFAULTS),defaultTargetOperations:DEFAULT_TARGET,maxChunkSteps:MAX_STEPS,create:create,verifyRecords:verifyRecords,verify:verifyRecords};
}));

/* Worker entry appended to the unchanged IndividualXPU engine. */
(function(root){
  'use strict';
  var nodePort=null,threadId=null;
  if(typeof module==='object'&&typeof require==='function'){var threads=require('node:worker_threads');nodePort=threads.parentPort;threadId=threads.threadId;}
  var Engine=typeof module==='object'&&module.exports?module.exports:root.IndividualXPU;
  var encoder=new TextEncoder();
  function rol(x,n){return ((x<<n)|(x>>>(32-n)))>>>0;}
  function hashText(text){
    var msg=encoder.encode(text),K=[0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];
    var H=[0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19],n=msg.length,total=Math.ceil((n+9)/64)*64,p=new Uint8Array(total);p.set(msg);p[n]=128;
    var bits=n*8,hi=Math.floor(bits/4294967296),lo=bits>>>0;for(var j=0;j<4;j++){p[total-8+j]=(hi>>>(24-j*8))&255;p[total-4+j]=(lo>>>(24-j*8))&255;}
    var w=new Uint32Array(64);
    for(var o=0;o<total;o+=64){
      for(var i=0;i<16;i++){var k=o+i*4;w[i]=((p[k]<<24)|(p[k+1]<<16)|(p[k+2]<<8)|p[k+3])>>>0;}
      for(i=16;i<64;i++){var x=w[i-15],y=w[i-2];w[i]=(w[i-16]+(rol(x,25)^rol(x,14)^(x>>>3))+w[i-7]+(rol(y,15)^rol(y,13)^(y>>>10)))>>>0;}
      var a=H[0],b=H[1],c=H[2],d=H[3],e=H[4],f=H[5],g=H[6],h=H[7];
      for(i=0;i<64;i++){var t1=(h+(rol(e,26)^rol(e,21)^rol(e,7))+((e&f)^((~e)&g))+K[i]+w[i])>>>0,t2=((rol(a,30)^rol(a,19)^rol(a,10))+((a&b)^(a&c)^(b&c)))>>>0;h=g;g=f;f=e;e=(d+t1)>>>0;d=c;c=b;b=a;a=(t1+t2)>>>0;}
      H[0]=(H[0]+a)>>>0;H[1]=(H[1]+b)>>>0;H[2]=(H[2]+c)>>>0;H[3]=(H[3]+d)>>>0;H[4]=(H[4]+e)>>>0;H[5]=(H[5]+f)>>>0;H[6]=(H[6]+g)>>>0;H[7]=(H[7]+h)>>>0;
    }
    return H.map(function(value){return value.toString(16).padStart(8,'0');}).join('');
  }
  function send(value){if(nodePort)nodePort.postMessage(value);else root.postMessage(value);}
  var job=null,logicalId=null;
  function handle(message){
    var tag={generation:message.generation,requestId:message.requestId,workerId:message.workerId,threadId:threadId};
    try{
      if(message.type==='run'){
        if(!job||logicalId!==message.workerId){if(!message.init)throw new Error('Worker initialization is required.');job=Engine.create(message.init.params,{hashText:hashText,targetOperations:message.init.targetOperations,historySize:1060});logicalId=message.workerId;}
        var result=job.executeSteps(message.steps),recent=job.recent(1060),firstPrevious=null;
        if(recent.entries.length&&!recent.accounting.initialPreview)firstPrevious=job.query(recent.entries[0].step).previous;
        send(Object.assign(tag,{type:'ack',receipt:result.receipt,snapshot:job.snapshot(),recent:recent,firstPrevious:firstPrevious}));
      }else if(message.type==='query'||message.type==='op'){
        if(!job||logicalId!==message.workerId)throw new Error('Logical worker is not resident.');
        var data=message.type==='query'?job.query(message.step):job.queryOperation(message.operationId);
        send(Object.assign(tag,{type:'query-result',data:data,snapshot:job.snapshot()}));
      }else if(message.type==='hash-test')send(Object.assign(tag,{type:'hash-result',hash:hashText(message.text)}));
      else throw new Error('Unknown worker protocol command.');
    }catch(error){send(Object.assign(tag,{type:'error',error:error.message}));}
  }
  if(nodePort)nodePort.on('message',handle);else root.onmessage=function(event){handle(event.data);};
  send({type:'ready',threadId:threadId});
}(typeof globalThis!=='undefined'?globalThis:this));
