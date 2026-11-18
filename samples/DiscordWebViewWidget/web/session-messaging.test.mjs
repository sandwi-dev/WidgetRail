import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL('./session-backend.js',import.meta.url),'utf8');
const owner='a'.repeat(32);
const plain=value=>JSON.parse(JSON.stringify(value));
function fixture(invoke){
    let endpoint;const batches=[],calls=[];
    vm.runInNewContext(source,{WidgetRailDiscordPrototype:{invoke:async request=>{calls.push(request);return invoke(request);}},
        host:{onRequest:value=>endpoint=value,batchResult:(context,items)=>{batches.push({context,items});return {context,items};}}});
    return {batches,calls,request:(method,args={},signal=new AbortController().signal)=>endpoint('command',{owner,method,args},{signal})};
}
test('history uses existing batch envelope while retaining account and both provider cursors',async()=>{
    const context={account:'1',channelId:'10',before:'100',after:null,nextBefore:'50',nextAfter:'99',hasMore:true};
    const items=Array.from({length:50},(_,i)=>({id:String(50+i),content:'x'.repeat(4000)}));
    const f=fixture(()=>({ok:true,value:{...context,items}}));
    const page=await f.request('messageHistory',{account:'1',channelId:'10',before:'100',limit:50});
    assert.equal(f.batches.length,1);assert.equal(page.items.length,50);
    assert.deepEqual(plain(page.context),{ok:true,value:context});
    assert.equal(Object.hasOwn(page.context.value,'items'),false);assert.equal(page.items[0].content.length,4000);
});
test('empty conversation page retains the same typed context without requesting an empty batch',async()=>{
    const context={account:'1',version:'catalog-1',totalCount:0,offset:0,hasMore:false,truncated:false};
    const f=fixture(()=>({ok:true,value:{...context,items:[]}}));
    const page=await f.request('messageConversations',{account:'1'});
    assert.deepEqual(plain(page),{context:{ok:true,value:context},items:[]});assert.equal(f.batches.length,0);
});
test('oversized messaging pages fail before batch publication',async()=>{
    const f=fixture(()=>({ok:true,value:{account:'1',items:Array.from({length:51},(_,i)=>({id:String(i)}))}}));
    await assert.rejects(f.request('messageHistory'),/Invalid message page/);assert.equal(f.batches.length,0);
});
test('late canceled history is discarded before any batch publication',async()=>{
    let complete;const pending=new Promise(resolve=>complete=resolve),cancel=new AbortController();
    const f=fixture(()=>pending),request=f.request('messageHistory',{},cancel.signal);
    cancel.abort();complete({ok:true,value:{account:'1',items:[{id:'50'}]}});
    await assert.rejects(request,/Cancelled/);assert.equal(f.batches.length,0);
});
test('send and reconciliation preserve uncertain outcomes without batching or replay',async()=>{
    const outcome={ok:true,value:{account:'1',channelId:'10',nonce:'123',status:'unknown',message:null,errorCode:'message_unconfirmed'}};
    const f=fixture(()=>outcome);
    assert.deepEqual(plain(await f.request('messageSend',{nonce:'123'})),outcome);
    assert.deepEqual(plain(await f.request('messageReconcile',{nonce:'123'})),outcome);
    assert.equal(f.calls.length,2);assert.equal(f.batches.length,0);
    assert.deepEqual(f.calls.map(value=>value.method),['messageSend','messageReconcile']);
});
test('provider preflight failures remain structured errors for native definite-failure handling',async()=>{
    const f=fixture(()=>({ok:false,code:'recipient_unavailable'}));
    assert.deepEqual(plain(await f.request('messageSend')),{ok:false,code:'recipient_unavailable'});
    assert.equal(f.batches.length,0);
});
