import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const read=name=>fs.readFileSync(new URL(name,import.meta.url),'utf8');
const build=JSON.parse(read('./adapter-contract.json')).build;
const gate=read('./adapter-invocation.js');
const installSource=read('./activity-provider.js')+'\n'+read('./adapter.js')+`\ninitializeWidgetRailDiscordAdapter(${build});`;
function fixture(current){
 const context={WidgetRailDiscordPrototype:current};vm.createContext(context);
 const invoke=vm.runInContext(`(${gate})`,context);
 return {context,invoke,install:()=>vm.runInContext(installSource,context)};
}
const request={method:'snapshot',owner:'a'.repeat(32),args:{}};
test('shared version contract installs the real provider and subsequent calls accept it',async()=>{
 const f=fixture();let installs=0;assert.equal((await f.invoke(build,request,null)).needsInstall,true);
 const first=await f.invoke(build,request,()=>{installs++;f.install();});assert.equal(first.result.ok,true);assert.equal(f.context.WidgetRailDiscordPrototype.build,build);
 const second=await f.invoke(build,request,()=>{installs++;f.install();});assert.equal(second.needsInstall,false);assert.equal(second.result.ok,true);assert.equal(installs,1);
});
test('missing and older adapters request installation without invoking the old provider',async()=>{
 for(const initial of [undefined,{build:build-1,invoke(){throw Error('old provider ran');}}]){
  const f=fixture(initial);assert.equal((await f.invoke(build,request,null)).needsInstall,true);
  const result=await f.invoke(build,request,f.install);assert.equal(result.result.ok,true);assert.equal(f.context.WidgetRailDiscordPrototype.build,build);
 }
});
test('newer provider is neither replaced nor invoked even during installation retry',async()=>{
 let calls=0,installs=0;const newer={build:build+1,invoke(){calls++;}};const f=fixture(newer);
 for(const installer of [null,()=>{installs++;}]){const result=await f.invoke(build,request,installer);assert.equal(result.needsInstall,false);assert.equal(result.result.code,'adapter_newer');}
 assert.equal(f.context.WidgetRailDiscordPrototype,newer);assert.equal(calls,0);assert.equal(installs,0);
});
test('newer provider arriving between discovery and installation is preserved',async()=>{
 const f=fixture();assert.equal((await f.invoke(build,request,null)).needsInstall,true);let installs=0;
 f.context.WidgetRailDiscordPrototype={build:build+1,invoke(){throw Error('newer invoked');}};
 assert.equal((await f.invoke(build,request,()=>installs++)).result.code,'adapter_newer');assert.equal(installs,0);
});
test('matching provider arriving before retry is invoked once without installation',async()=>{
 const f=fixture();await f.invoke(build,request,null);let calls=0,installs=0;
 f.context.WidgetRailDiscordPrototype={build,invoke:async()=>{calls++;return {ok:true,value:{}};}};
 assert.equal((await f.invoke(build,request,()=>installs++)).result.ok,true);assert.equal(calls,1);assert.equal(installs,0);
});
test('a provider error named adapter_missing is not an installation signal',async()=>{
 let calls=0;const f=fixture({build,invoke:()=>{calls++;return {ok:false,code:'adapter_missing'};}});
 const result=await f.invoke(build,request,null);assert.equal(result.needsInstall,false);assert.equal(result.result.code,'adapter_missing');assert.equal(calls,1);
});
test('provider mutation followed by exception is not replayed or reinstalled',async()=>{
 let calls=0,installs=0;const f=fixture({build,invoke:()=>{calls++;throw Error('response lost');}});
 await assert.rejects(f.invoke(build,request,()=>installs++),/response lost/);assert.equal(calls,1);assert.equal(installs,0);
});
test('unversioned or malformed installed providers fail without being replaced',async()=>{
 for(const version of [undefined,null,'6',-1,0,NaN]){const f=fixture({build:version,invoke(){throw Error('invoked');}});let installs=0;const result=await f.invoke(build,request,()=>installs++);assert.equal(result.result.code,'adapter_incompatible');assert.equal(installs,0);}
});
test('incorrect installation never invokes an incompatible adapter',async()=>{
 const f=fixture();let calls=0;const result=await f.invoke(build,request,()=>{f.context.WidgetRailDiscordPrototype={build:build-1,invoke:()=>calls++};});assert.equal(result.result.code,'adapter_incompatible');assert.equal(calls,0);
});
test('direct domain installer does not downgrade a newer provider',()=>{
 const newer={build:build+1,dispose(){throw Error('disposed');}};const f=fixture(newer);assert.throws(()=>f.install(),/adapter_newer/);assert.equal(f.context.WidgetRailDiscordPrototype,newer);
});
