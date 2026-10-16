const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const sent=[],handlers=[];
const window={};
vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../../src/WidgetSdk/WebSessionRuntime.js'),'utf8'),{
  window,TextEncoder,AbortController,setTimeout,clearTimeout,chrome:{webview:{postMessage:value=>sent.push(value),addEventListener:(_,handler)=>handlers.push(handler)}}
});
const api=window.WidgetRailWebSession;
api.post({text:'x'.repeat(40000)+'🎮'});
assert.equal(sent[0].text.length,40002);
api.post('x'.repeat(65534));
assert.throws(()=>api.post('x'.repeat(65535)),/bound/);
assert.throws(()=>api.post('界'.repeat(22000)),/bound/,'Limits count UTF8 bytes');
api.handle('echo',value=>value);
(async()=>{
  await handlers[0]({data:{widgetRail:1,kind:'request',id:'a'.repeat(32),method:'echo',payload:{text:'x'.repeat(40000)}}});
  assert.equal(sent.at(-1).payload.text.length,40000);
  await handlers[0]({data:{widgetRail:1,kind:'request',id:'b'.repeat(32),method:'echo',payload:{text:'x'.repeat(65536)}}});
  assert.equal(sent.at(-1).error,'page_request_failed','Oversized handler results produce a bounded failure');
  console.log('PASS shared page runtime: 40 KiB round trip, exact byte limit, Unicode and oversized responses');
})().catch(error=>{console.error(error);process.exitCode=1;});
