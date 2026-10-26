import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
const source=fs.readFileSync(new URL('./discord-commands.js',import.meta.url),'utf8');
const owner='a'.repeat(32);
function setup(){
    const calls=[],state={ready:true,account:'account',channelId:null,channels:[{id:'lobby'}]};
    const context={WidgetRailDiscordPrototype:{build:4,invoke:async request=>request.method==='snapshot'?{ok:true,value:state}:{ok:true,value:{forwarded:request.method}}},
        WidgetRailWebWebpack:{findByProps:()=>({selectVoiceChannel:id=>{calls.push(id);state.channelId=id;}})}};
    vm.createContext(context);vm.runInContext(source,context);return {state,calls,context,invoke:context.WidgetRailDiscordPrototype.invoke};
}
test('web join uses the concrete action once',async()=>{const s=setup();const request={method:'join',owner,args:{account:'account',target:'lobby'}};assert.equal((await s.invoke(request)).ok,true);assert.equal((await s.invoke(request)).ok,true);assert.deepEqual(s.calls,['lobby']);});
test('join rejects missing and unadvertised targets',async()=>{const s=setup();for(const target of [undefined,null,'missing'])assert.equal((await s.invoke({method:'join',owner,args:{account:'account',target}})).ok,false);assert.equal(s.calls.length,0);});
test('account changes cannot trigger a web voice action',async()=>{const s=setup();assert.equal((await s.invoke({method:'join',owner,args:{account:'old',target:'lobby'}})).ok,false);assert.equal(s.calls.length,0);});
test('invalid owner cannot invoke voice action',async()=>{const s=setup();assert.equal((await s.invoke({method:'join',owner:'invalid',args:{account:'account',target:'lobby'}})).ok,false);assert.equal(s.calls.length,0);});
test('leave validates current channel and preserves another call',async()=>{const s=setup();s.state.channelId='lobby';assert.equal((await s.invoke({method:'leave',owner,args:{account:'account',channelId:'old'}})).ok,false);assert.equal(s.calls.length,0);assert.equal((await s.invoke({method:'leave',owner,args:{account:'account',channelId:'lobby'}})).ok,true);assert.deepEqual(s.calls,[null]);});
test('other provider methods retain the original adapter',async()=>{const s=setup();const result=await s.invoke({method:'stopShare',owner,args:{account:'account'}});assert.equal(result.value.forwarded,'stopShare');});
