const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const host=fs.readFileSync('src/OverlayFrontend.WinUI/Browser/WebSessionPageAdapterHost.cs','utf8').split('private const string Prelude="""')[1].split('""";')[0];
const requests=fs.readFileSync('src/OverlayFrontend.WinUI/Browser/WebSessionPageAdapterHost.Requests.cs','utf8').split('private const string RequestsPrelude="""')[1].split('""";')[0];
const messages=[];
const context={TextEncoder,crypto:require('node:crypto').webcrypto,AbortController,setTimeout,clearTimeout,document:{querySelectorAll:()=>[]},deliver:text=>messages.push(JSON.parse(text))};
vm.createContext(context);vm.runInContext(host+requests+'\nglobalThis.testHost=host;',context);
const pause=()=>new Promise(resolve=>setTimeout(resolve,5));
(async()=>{
  context.testHost.onRequest(async(method,payload,operation)=>{
    assert.equal(method,'load');operation.progress({loaded:10});
    return context.testHost.batchResult({version:1},Array.from({length:100},(_,id)=>({id,text:'界'.repeat(500)})));
  });
  context.__widgetRailRequest({kind:'request',id:'a'.repeat(32),method:'load',payload:{}});await pause();
  assert.equal(messages[0].kind,'progress');const chunks=messages.filter(m=>m.kind==='response-batch');
  assert.equal(chunks.flatMap(m=>m.payload.items).length,100);assert.ok(chunks.length>1);assert.ok(messages.every(m=>Buffer.byteLength(JSON.stringify(m))<64*1024));
  messages.length=0;
  context.testHost.onRequest((method,payload,{signal})=>new Promise((resolve,reject)=>signal.addEventListener('abort',()=>reject(new Error('cancelled')),{once:true})));
  context.__widgetRailRequest({kind:'request',id:'b'.repeat(32),method:'load',payload:{}});await pause();
  context.__widgetRailRequest({kind:'cancel',id:'b'.repeat(32)});await pause();
  assert.equal(messages.length,1);assert.equal(messages[0].kind,'response');assert.ok(messages[0].error);
  messages.length=0;
  context.__widgetRailRequest({kind:'request',id:'c'.repeat(32),method:'load',payload:{}});await pause();context.__widgetRailCancelRequests();await pause();
  assert.equal(messages.length,1);assert.ok(messages[0].error);
  console.log('PASS asynchronous adapter progress, bounded Unicode response batches, cancellation and visibility cancellation');
})().catch(e=>{console.error(e);process.exitCode=1;});
