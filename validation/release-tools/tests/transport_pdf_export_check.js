'use strict';
const fs = require('fs');
const vm = require('vm');
const assert = require('assert/strict');
const source = fs.readFileSync(process.argv[2], 'utf8');
const functionLine = source.split(/\r?\n/).find(line => line.startsWith('async function downloadEquationSource(){'));
assert.ok(functionLine, 'exact downloadEquationSource function must exist');
assert.match(source, /\$\('inputExportButton'\)\.addEventListener\('click',downloadEquationSource\)/, 'download button uses binary-safe implementation');
assert.match(source, /downloadInputSource:downloadEquationSource/, 'public UI export uses the same implementation');
async function test(filename, binary) {
  const bytes = Buffer.from([0x25,0x50,0x44,0x46,0x2d,0x31,0x2e,0x34,0x0a,0x00,0xff,0xfe,0x80,0x0d,0x0a]);
  const blob = new Blob([bytes], {type:'application/pdf'});
  const events = [];
  const anchor = {click(){ events.push(['click',this.href,this.download]); }};
  const context = {
    connected:true, TOKEN:'audit-token', inputMetadata:()=>({id:'opaque & id',filename}),
    fetch:async(path,options)=>{
      assert.equal(path,'/api/attachments/opaque%20%26%20id/export');
      assert.equal(options.headers.Authorization,'Bearer audit-token');
      assert.equal(options.cache,'no-store');
      return {ok:true, blob:async()=>{assert.ok(binary);events.push(['blob']);return blob;},text:async()=>{assert.ok(!binary,'binary PDF must never pass through UTF-8 text decoding');return 'plain source\n';}};
    },
    URL:{createObjectURL:returned=>{assert.equal(returned,blob);events.push(['create']);return 'blob:audit';},revokeObjectURL:url=>events.push(['revoke',url])},
    element:tag=>{assert.equal(tag,'a');return anchor;},
    setTimeout:(fn,milliseconds)=>{assert.equal(milliseconds,1000);fn();},
    downloadText:(text,name)=>{assert.ok(!binary);assert.equal(text,'plain source\n');assert.equal(name,filename);events.push(['text']);},
    text:(id,value)=>events.push(['status',id,value]),
    log:(kind,error)=>{throw new Error(`${kind}: ${error}`);}
  };
  const fn = new vm.Script(functionLine+'\ndownloadEquationSource').runInNewContext(context);
  await fn();
  if(binary){assert.deepEqual(Buffer.from(await blob.arrayBuffer()),bytes);assert.deepEqual(events.map(x=>x[0]),['blob','create','click','revoke','status']);assert.equal(anchor.download,filename);}
  else assert.deepEqual(events.map(x=>x[0]),['text','status']);
  return {filename,binary,passed:true};
}
(async()=>{
  const results=[await test('original.pdf',true),await test('ORIGINAL.PDF',true),await test('original.json',false)];
  console.log(JSON.stringify({exactExportFunction:true,buttonWiring:true,publicExportWiring:true,cases:results},null,2));
})().catch(error=>{console.error(error);process.exitCode=1;});
