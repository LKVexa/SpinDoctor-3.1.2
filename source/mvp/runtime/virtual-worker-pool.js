/* Addressed independent chains executed by a bounded real worker pool.
 * Only validated completed-chunk acknowledgments advance aggregate counters.
 */
(function(root,factory){
  'use strict';var loadCore=typeof module==='object'&&module.exports?function(){return require('./individual-xpu.js');}:null,api=factory(root,loadCore);
  if(typeof module==='object'&&module.exports)module.exports=api;else root.VirtualWorkerPool=api;
}(typeof globalThis!=='undefined'?globalThis:this,function(root,loadCore){
  'use strict';
  var Core=root.IndividualXPU||(loadCore&&loadCore()),SCHEMA='VIRTUAL-WORKER-POOL/1';
  function copy(value){return JSON.parse(JSON.stringify(value));}
  function integer(value,name){var result;if(typeof value==='bigint'&&value>=0n)result=value;else if(typeof value==='number'&&Number.isSafeInteger(value)&&value>=0)result=BigInt(value);else if(typeof value==='string'&&value.length<=78&&/^(0|[1-9][0-9]*)$/.test(value))result=BigInt(value);else throw new TypeError(name+' must be an exact nonnegative integer.');if(result>=(1n<<256n))throw new RangeError(name+' exceeds 256 bits.');return result;}
  function ceilCubeRoot(value){var n=integer(value,'value');if(n===0n)return 0n;var low=0n,high=1n;while(high*high*high<n)high<<=1n;while(low+1n<high){var mid=(low+high)>>1n;if(mid*mid*mid>=n)high=mid;else low=mid;}return high;}
  function quotaForWorker(targetSteps,workers,id){var t=integer(targetSteps,'targetSteps'),w=integer(workers,'virtualWorkers'),i=integer(id,'workerId');if(w<1n||i>=w)throw new RangeError('Worker ID is outside its address space.');return (i+1n)*t/w-i*t/w;}
  function seedForWorker(seed,id,modulus){return (integer(seed,'seed')+integer(id,'workerId'))%integer(modulus,'modulus');}
  function configure(params,options){
    options=options||{};if(!Core)throw new Error('IndividualXPU must load before the worker pool.');if(typeof options.hashText!=='function')throw new TypeError('hashText callback is required.');
    var target=integer(options.targetOperations===undefined?Core.defaultTargetOperations:options.targetOperations,'targetOperations');if(target<2n||target%2n)throw new RangeError('Target must be positive and even.');
    var normalized=Core.create(params||{},{hashText:options.hashText,targetOperations:target,historySize:1060,now:function(){return 0;}}).snapshot().params;
    var virtual=options.virtualWorkers===undefined?ceilCubeRoot(target):integer(options.virtualWorkers,'virtualWorkers');if(virtual<1n)throw new RangeError('virtualWorkers must be positive.');
    var hardware=root.navigator&&root.navigator.hardwareConcurrency||4,physical=options.physicalWorkers===undefined?Math.max(1,Math.min(4,hardware)):options.physicalWorkers;
    var maximum=options.allowEightWorkers===true?8:4;if(!Number.isSafeInteger(physical)||physical<1||physical>maximum)throw new RangeError('physicalWorkers must be between 1 and '+maximum+'.');
    var chunk=options.chunkSteps===undefined?8192:options.chunkSteps;if(!Number.isSafeInteger(chunk)||chunk<1||chunk>65536)throw new RangeError('chunkSteps must be from 1 through 65536.');
    if(typeof options.workerSource!=='string'||!options.workerSource.trim())throw new TypeError('Self-contained workerSource text is required.');
    if(options.workerFactory!==undefined&&typeof options.workerFactory!=='function')throw new TypeError('workerFactory must be a function.');
    if(options.now!==undefined&&typeof options.now!=='function')throw new TypeError('now must be a function.');
    return {params:normalized,options:options,target:target,steps:target/2n,virtual:virtual,physical:physical,chunk:chunk,virtualOverride:options.virtualWorkers!==undefined};
  }
  function create(params,options){
    var config=configure(params,options),generation=1,slots=[],contexts=new Map(),nextId=0n,completed=0n,reserved=0n,running=false,failed=false,disposed=false,requestSequence=0,ackSequence=0,recentEntries=[],errors=[],pending=new Map(),pauseWaiters=[],goal=null,goalPromise=null,mainQueryCount=0n;
    var clock=config.options.now||function(){return root.performance&&root.performance.now?root.performance.now():Date.now();};
    function snapshot(){
      var chains=[];contexts.forEach(function(c){chains.push({workerId:c.id.toString(),quotaSteps:c.quota.toString(),quotaOperations:(c.quota*2n).toString(),completedSteps:c.completed.toString(),actualOperations:(c.completed*2n).toString(),state:c.snapshot.state,complete:c.completed===c.quota,physicalWorkerIndex:c.slotIndex,snapshot:c.snapshot});});
      return copy({schema:SCHEMA,version:'1.0.0',params:config.params,targetOperations:config.target.toString(),targetSteps:config.steps.toString(),virtualWorkerCount:config.virtual.toString(),virtualWorkersExplicit:config.virtualOverride,physicalWorkers:config.physical,runningWorkerCount:slots.length,activeWorkers:slots.filter(function(s){return s.context&&s.context.completed<s.context.quota;}).length,inFlight:slots.filter(function(s){return !!s.flight;}).length,paused:!running,completedSteps:completed.toString(),actualOperations:(completed*2n).toString(),actualReductions:completed.toString(),remainingOperations:(config.target-completed*2n).toString(),complete:completed===config.steps,generation:generation,chains:chains,materializedWorkers:contexts.size,inactiveWorkerAddresses:(config.virtual-BigInt(contexts.size)).toString(),mainQueryHostIntegerWork:{multiplications:mainQueryCount.toString(),additions:mainQueryCount.toString(),reductions:mainQueryCount.toString()},errors:errors,backendThreadIds:slots.map(function(s){return s.threadId;}).filter(function(v){return v!==null;}),accounting:'Independent addressed chains. Only acknowledged actual sequential steps are credited; inactive addresses receive no work credit.'});
    }
    function update(){if(typeof config.options.onUpdate==='function'){try{config.options.onUpdate(snapshot());}catch(error){if(typeof config.options.onError==='function')config.options.onError(error);}}}
    function idle(){return !slots.some(function(s){return !!s.flight;});}
    function settle(){
      if(idle()){
        pauseWaiters.splice(0).forEach(function(resolve){resolve(snapshot());});
        if(goalPromise&&(!running||completed>=goal)){var done=goalPromise;goalPromise=null;goal=null;done.resolve(snapshot());}
      }
    }
    function makeWorker(index){
      if(config.options.workerFactory)return config.options.workerFactory(config.options.workerSource,index);
      if(typeof root.Worker!=='function'||typeof root.Blob!=='function')throw new Error('Web Workers are unavailable.');
      var url=root.URL.createObjectURL(new root.Blob([config.options.workerSource],{type:'text/javascript'}));try{return new root.Worker(url);}finally{root.URL.revokeObjectURL(url);}
    }
    function stopOnError(slot,message){
      running=false;failed=true;errors.push(message);if(slot&&slot.flight){reserved-=slot.flight.steps;pending.delete(slot.flight.requestId);slot.flight=null;}
      if(goalPromise){goalPromise.reject(new Error(message));goalPromise=null;goal=null;}
      if(typeof config.options.onError==='function'){try{config.options.onError(new Error(message));}catch(ignore){}}
      update();settle();
    }
    function allocate(slot){
      if(nextId>=config.virtual)return null;
      var id=nextId,q=quotaForWorker(config.steps,config.virtual,id);
      if(q===0n){var covered=id*config.steps/config.virtual;id=((covered+1n)*config.virtual+config.steps-1n)/config.steps-1n;if(id>=config.virtual)return null;q=quotaForWorker(config.steps,config.virtual,id);}
      nextId=id+1n;
      var derived=Object.assign({},config.params,{seed:seedForWorker(config.params.seed,id,config.params.modulus).toString()}),initial=Core.create(derived,{hashText:config.options.hashText,targetOperations:q*2n,historySize:1060,now:function(){return 0;}}).snapshot();
      var context={id:id,quota:q,completed:0n,params:derived,snapshot:initial,lastHash:initial.initialHash,window:[],firstPrevious:null,slotIndex:slot.index};contexts.set(id.toString(),context);slot.context=context;return context;
    }
    function dispatch(slot){
      if(!running||slot.flight||!slot.ready||failed||disposed)return;
      var capacity=config.steps-completed-reserved;if(goal!==null){var requested=goal-completed-reserved;if(requested<capacity)capacity=requested;}
      if(capacity<=0n)return;
      var context=slot.context;if(!context||context.completed===context.quota)context=allocate(slot);if(!context)return;
      var left=context.quota-context.completed,size=BigInt(config.chunk);if(size>left)size=left;if(size>capacity)size=capacity;if(size<=0n)return;
      var id=++requestSequence,init=slot.residentId!==context.id.toString()?{params:context.params,targetOperations:(context.quota*2n).toString()}:undefined;
      slot.residentId=context.id.toString();slot.flight={requestId:id,steps:size,context:context};reserved+=size;pending.set(id,{kind:'run',slot:slot});
      try{slot.worker.postMessage({type:'run',generation:generation,requestId:id,workerId:context.id.toString(),steps:size.toString(),init:init});}catch(error){stopOnError(slot,error.message);}
      update();
    }
    function pump(){slots.forEach(dispatch);if(completed===config.steps||(goal!==null&&completed>=goal)){running=false;update();}settle();}
    function message(slot,value,ownGeneration){
      if(disposed||ownGeneration!==generation)return;
      if(value.type==='ready'){slot.ready=true;slot.threadId=value.threadId===undefined?null:value.threadId;update();dispatch(slot);return;}
      if(value.generation!==generation)return;
      var request=pending.get(value.requestId);if(!request||request.slot!==slot)return;
      if(value.type==='error'){pending.delete(value.requestId);if(request.kind==='query'){request.reject(new Error(value.error));return;}stopOnError(slot,value.error);return;}
      if(request.kind==='query'&&value.type==='query-result'){
        pending.delete(value.requestId);request.context.snapshot=value.snapshot;request.resolve(Object.assign({workerId:request.context.id.toString()},value.data));update();return;
      }
      if(request.kind!=='run'||value.type!=='ack')return;
      var flight=slot.flight,context=flight.context,count=flight.steps;
      try{
        if(value.workerId!==context.id.toString()||BigInt(value.snapshot.completedSteps)!==context.completed+count||BigInt(value.receipt.stepsExecuted)!==count||BigInt(value.snapshot.actualOperations)!==(context.completed+count)*2n||value.receipt.previousHash!==context.lastHash)throw new Error('Worker acknowledgment counters or routing are inconsistent.');
        if(JSON.stringify(value.snapshot.params)!==JSON.stringify(context.params)||value.snapshot.targetOperations!==(context.quota*2n).toString()||value.snapshot.historySize!==1060)throw new Error('Worker acknowledgment configuration or quota is inconsistent.');
        var verified=Core.verifyRecords(value.snapshot,{hashText:config.options.hashText});if(!verified.ok)throw new Error('Worker acknowledgment record integrity failed: '+verified.errors.join('; '));
        if(value.snapshot.receipts.length!==context.snapshot.receipts.length+1||JSON.stringify(value.snapshot.receipts[value.snapshot.receipts.length-1])!==JSON.stringify(value.receipt))throw new Error('Worker acknowledgment receipt does not match its snapshot.');
        var body={sequence:value.receipt.sequence,stepsExecutedBefore:value.receipt.stepsExecutedBefore,stepsExecutedAfter:value.receipt.stepsExecutedAfter,stepsExecuted:value.receipt.stepsExecuted,stateBefore:value.receipt.stateBefore,stateAfter:value.receipt.stateAfter,actualMultiplications:value.receipt.actualMultiplications,actualAdditions:value.receipt.actualAdditions,actualReductions:value.receipt.actualReductions,actualOperations:value.receipt.actualOperations,previousHash:value.receipt.previousHash};
        if(config.options.hashText(JSON.stringify(body)).toLowerCase()!==value.receipt.hash)throw new Error('Worker receipt hash is invalid.');
        if(value.receipt.stepsExecutedBefore!==context.completed.toString()||value.receipt.stateBefore!==context.snapshot.state||value.receipt.stateAfter!==value.snapshot.state)throw new Error('Worker receipt continuity is invalid.');
        if(!value.recent||!Array.isArray(value.recent.entries)||value.recent.entries.length!==value.snapshot.retainedSteps||integer(value.firstPrevious,'firstPrevious')>=BigInt(context.params.modulus))throw new Error('Worker retained output is invalid.');
        var nextAckSequence=ackSequence+1,newEntries=[];
        value.recent.entries.forEach(function(entry,index){var recordedState=integer(entry.state,'retained state');if(integer(entry.step,'retained step')!==BigInt(value.snapshot.retainedStartStep)+BigInt(index)||recordedState>=BigInt(context.params.modulus)||!Number.isSafeInteger(entry.digit)||entry.digit!==Number(recordedState%10n)||entry.initialPreview!==false)throw new Error('Worker retained output addresses or values are invalid.');if(BigInt(entry.step)>BigInt(value.receipt.stepsExecutedBefore))newEntries.push(Object.assign({workerId:context.id.toString(),ackSequence:nextAckSequence},entry));});
        if(value.recent.entries[value.recent.entries.length-1].state!==value.snapshot.state)throw new Error('Worker retained output does not end at the acknowledged state.');
        pending.delete(value.requestId);slot.flight=null;reserved-=count;completed+=count;context.completed+=count;context.snapshot=value.snapshot;context.lastHash=value.receipt.hash;context.window=value.recent.entries;context.firstPrevious=value.firstPrevious;
        ackSequence=nextAckSequence;recentEntries=recentEntries.concat(newEntries);if(recentEntries.length>1060)recentEntries=recentEntries.slice(-1060);
        update();pump();
      }catch(error){stopOnError(slot,error.message);}
    }
    function ensureWorkers(){
      if(slots.length)return;
      for(var i=0;i<config.physical;i++){
        var slot={index:i,worker:makeWorker(i),ready:false,flight:null,context:null,residentId:null,threadId:null},own=generation;slots.push(slot);
        (function(s,g){s.worker.onmessage=function(event){message(s,event.data,g);};s.worker.onerror=function(event){if(g===generation&&!disposed)stopOnError(s,event.message||'Worker failed.');};}(slot,own));
      }
    }
    function start(){if(disposed)throw new Error('Pool is disposed.');if(failed)throw new Error('Reset the failed pool before restarting.');if(completed===config.steps)return snapshot();running=true;try{ensureWorkers();}catch(error){running=false;failed=true;generation++;terminate('Worker construction failed: '+error.message);reserved=0n;errors.push('Worker construction failed: '+error.message);if(typeof config.options.onError==='function'){try{config.options.onError(error);}catch(ignore){}}update();throw error;}update();pump();return snapshot();}
    function pause(){running=false;update();return new Promise(function(resolve){pauseWaiters.push(resolve);settle();});}
    function runSteps(value){var count=integer(value,'stepCount');if(count<1n)throw new RangeError('stepCount must be positive.');if(goalPromise)throw new Error('A finite run is already active.');if(!idle()||running)throw new Error('Pause and settle the pool before a finite run.');var remaining=config.steps-completed;if(count>remaining)count=remaining;if(count===0n)return Promise.resolve(snapshot());goal=completed+count;var result=new Promise(function(resolve,reject){goalPromise={resolve:resolve,reject:reject};});try{start();}catch(error){if(goalPromise){goalPromise.reject(error);goalPromise=null;}goal=null;}return result;}
    function terminate(reason){slots.forEach(function(slot){try{slot.worker.terminate();}catch(ignore){}});slots=[];pending.forEach(function(request){if(request.kind==='query')request.reject(new Error(reason));});pending.clear();if(goalPromise){goalPromise.reject(new Error(reason));goalPromise=null;}pauseWaiters.splice(0).forEach(function(resolve){resolve(snapshot());});}
    function dispose(){running=false;disposed=true;generation++;terminate('Pool disposed.');reserved=0n;return snapshot();}
    function reset(nextParams,nextOptions){var replacement=configure(nextParams===undefined?config.params:nextParams,Object.assign({},config.options,nextOptions||{}));running=false;generation++;terminate('Pool reset.');config=replacement;clock=config.options.now||clock;contexts=new Map();nextId=0n;completed=0n;reserved=0n;failed=false;disposed=false;recentEntries=[];errors=[];goal=null;mainQueryCount=0n;update();return snapshot();}
    function recent(count){if(!Number.isSafeInteger(count)||count<1||count>1060)throw new RangeError('count must be from 1 through 1060.');var entries=recentEntries.slice(-count);if(!entries.length)for(var i=0;i<count;i++)entries.push({workerId:null,step:'0',state:config.params.seed,digit:Number(BigInt(config.params.seed)%10n),initialPreview:true});return copy({entries:entries,accounting:{completedSteps:completed.toString(),actualOperations:(completed*2n).toString(),materializedWorkers:contexts.size,virtualWorkerCount:config.virtual.toString(),initialPreview:completed===0n}});}
    function query(workerId,stepValue,operationId){
      var id=integer(workerId,'workerId'),context=contexts.get(id.toString());if(!context)return Promise.reject(new Error('Logical worker has not been materialized.'));
      var step=operationId===undefined?integer(stepValue,'STEP'):integer(operationId,'operationId')/2n+1n;
      if(!context.window.length||step<BigInt(context.window[0].step)||step>BigInt(context.window[context.window.length-1].step))return Promise.reject(new Error('Requested executed step is not retained.'));
      var slot=slots[context.slotIndex];if(slot&&slot.residentId===id.toString())return new Promise(function(resolve,reject){var requestId=++requestSequence;pending.set(requestId,{kind:'query',slot:slot,context:context,resolve:resolve,reject:reject});slot.worker.postMessage({type:operationId===undefined?'query':'op',generation:generation,requestId:requestId,workerId:id.toString(),step:step.toString(),operationId:operationId===undefined?undefined:integer(operationId,'operationId').toString()});});
      var index=Number(step-BigInt(context.window[0].step)),before=BigInt(index?context.window[index-1].state:context.firstPrevious),a=BigInt(context.params.a),b=BigInt(context.params.b),m=BigInt(context.params.modulus),product=a*before,sum=product+b,state=sum%m;mainQueryCount++;
      var trace={workerId:id.toString(),step:step.toString(),previous:before.toString(),a:a.toString(),b:b.toString(),modulus:m.toString(),product:product.toString(),rawSum:sum.toString(),state:state.toString(),result:state.toString(),digit:Number(state%10n),operations:[{id:((step-1n)*2n).toString(),kind:'multiply',left:a.toString(),right:before.toString(),result:product.toString()},{id:((step-1n)*2n+1n).toString(),kind:'add',left:product.toString(),right:b.toString(),result:sum.toString()}],hostIntegerWork:{multiplications:'1',additions:'1',reductions:'1'}};
      update();return Promise.resolve(operationId===undefined?trace:{workerId:id.toString(),operation:trace.operations[Number(integer(operationId,'operationId')%2n)],trace:trace,step:trace.step,state:trace.state,hostIntegerWork:trace.hostIntegerWork});
    }
    return {start:start,pause:pause,runSteps:runSteps,dispose:dispose,reset:reset,snapshot:snapshot,recent:recent,query:function(id,step){return query(id,step);},queryOperation:function(id,op){return query(id,null,op);}};
  }
  function verifyRecords(actual,options){
    var errors=[];
    try{
      if(!actual||actual.schema!==SCHEMA)throw new Error('Pool schema is invalid.');var target=integer(actual.targetOperations,'targetOperations'),workers=integer(actual.virtualWorkerCount,'virtualWorkerCount');if(target<2n||target%2n||workers<1n)throw new Error('Pool domain counts are invalid.');if(!actual.virtualWorkersExplicit&&workers!==ceilCubeRoot(target))throw new Error('Default worker address count is not the exact ceiling cube root.');
      var total=0n,seen=new Set();actual.chains.forEach(function(chain){var id=integer(chain.workerId,'workerId');if(id>=workers||seen.has(id.toString()))throw new Error('Logical IDs must be unique and in range.');seen.add(id.toString());var quota=quotaForWorker(target/2n,workers,id);if(quota===0n||chain.quotaSteps!==quota.toString()||chain.quotaOperations!==(quota*2n).toString())throw new Error('Worker quota is invalid.');var derived=Object.assign({},actual.params,{seed:seedForWorker(actual.params.seed,id,actual.params.modulus).toString()});if(JSON.stringify(chain.snapshot.params)!==JSON.stringify(derived))throw new Error('Worker seed binding is invalid.');var check=Core.verifyRecords(chain.snapshot,options);if(!check.ok)throw new Error(check.errors.join('; '));var count=BigInt(chain.snapshot.completedSteps);if(count>quota||chain.completedSteps!==count.toString()||chain.actualOperations!==(count*2n).toString()||chain.snapshot.targetOperations!==(quota*2n).toString()||chain.state!==chain.snapshot.state||chain.complete!==(count===quota))throw new Error('Worker acknowledgment summary is invalid.');total+=count;});
      if(actual.completedSteps!==total.toString()||actual.actualOperations!==(total*2n).toString()||actual.actualReductions!==total.toString()||actual.remainingOperations!==(target-total*2n).toString()||actual.complete!==(total*2n===target)||actual.targetSteps!==(target/2n).toString()||actual.materializedWorkers!==seen.size||actual.inactiveWorkerAddresses!==(workers-BigInt(seen.size)).toString())throw new Error('Aggregate acknowledgment accounting is invalid.');
    }catch(error){errors.push(error.message);}
    return {ok:errors.length===0,errors:errors,kind:'worker-record-integrity',replayedSteps:'0',scope:'Address quotas, seed binding, worker receipt integrity and acknowledged aggregate counts checked. Uninstantiated addresses receive no credit. Arithmetic replay was not performed.'};
  }
  return {version:'1.0.0',schema:SCHEMA,ceilCubeRoot:ceilCubeRoot,quotaForWorker:quotaForWorker,seedForWorker:seedForWorker,create:create,verifyRecords:verifyRecords,verify:verifyRecords};
}));
