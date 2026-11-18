import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
import { webcrypto } from 'node:crypto';
import { maximumBackendBytes } from './Build-Backend.mjs';

// Execute the exact composed asset written by Build-Backend, not separately
// minified components with a replacement domain adapter supplied by the test.
const packagedPath = process.env.WRAIL_PACKAGED_BACKEND;
if (!packagedPath) throw new Error('Run through Test-MinifiedBackend.ps1 with a freshly composed backend.');
const source = readFileSync(packagedPath, 'utf8');
const contract = JSON.parse(readFileSync(new URL('../../samples/DiscordWebViewWidget/web/provider/adapter-contract.json', import.meta.url), 'utf8'));
const owner = 'a'.repeat(32);

function fixture() {
    let handler, registrations = 0;
    const runtime = { c: {} };
    const chunks = [];
    chunks.push = function (chunk) { chunk[2](runtime); return Array.prototype.push.call(this, chunk); };
    const context = { crypto: webcrypto, location: { origin: 'https://discord.com' },
        webpackChunkdiscord_app: chunks,
        host: { onRequest(callback) { registrations++; handler = callback; },
            batchResult(context, items) { return { context, items }; } } };
    context.window = context;
    context.top = context;
    vm.runInNewContext(source, context, { timeout: 2000 });
    return { context, runtime, registrations,
        invoke(method, request = { owner }, signal = new AbortController().signal) {
            return handler(method, request, { signal });
        } };
}

test('sealed backend installs its contracted adapter and one host endpoint without Vencord', async () => {
    const f = fixture();
    assert.equal(f.registrations, 1);
    assert.equal(f.context.WidgetRailDiscordPrototype.build, contract.build);
    assert.equal(f.context.Vencord, undefined);
    const snapshot = await f.invoke('snapshot');
    assert.equal(snapshot.context.ready, false);
    assert.equal(snapshot.context.account, '');
    assert.equal(snapshot.context.channelCount, 0);
    assert.equal(snapshot.context.sharing, false);
    assert.equal(snapshot.context.sharePending, false);
});

test('sealed discovery recovers when current-user modules load after startup', async () => {
    const f = fixture();
    assert.equal((await f.invoke('snapshot')).context.ready, false);
    let user = { id: '123' };
    f.runtime.c.user = { exports: { getName: () => 'UserStore', getCurrentUser: () => user } };
    const signedIn = await f.invoke('snapshot');
    assert.equal(signedIn.context.ready, true);
    assert.equal(signedIn.context.account, '123');
    user = null;
    const signedOut = await f.invoke('snapshot');
    assert.equal(signedOut.context.ready, false);
    assert.equal(signedOut.context.account, '');
    assert.notEqual(signedOut.context.channelVersion, signedIn.context.channelVersion);
});

test('sealed endpoint rejects invalid owners, cancelled reads and arbitrary commands', async () => {
    const f = fixture();
    await assert.rejects(f.invoke('snapshot', { owner: 'invalid' }), /Invalid owner/);
    const cancelled = new AbortController();
    cancelled.abort();
    await assert.rejects(f.invoke('snapshot', { owner }, cancelled.signal), /Cancelled/);
    for (const method of ['evaluate', 'fetch', 'show'])
        await assert.rejects(f.invoke('command', { owner, method, args: {} }), /Command unavailable/);
});

test('sealed share cannot invoke a browser chooser without admitted native capture', async () => {
    const f = fixture();
    const response = await f.invoke('command', { owner, method: 'share',
        args: { captureQuality: { resolution: 720, frameRate: 30 } } });
    assert.equal(response.ok, false);
    assert.equal(response.code, 'native_capture_unavailable');
    assert.equal((await f.invoke('snapshot')).context.sharePending, false);
});

test('sealed backend remains inside the host script byte limit', () => {
    assert.ok(Buffer.byteLength(source, 'utf8') <= maximumBackendBytes);
});

test('sealed messaging chooses authenticated bound verbs over earlier raw and tracking request families',async()=>{
    const f=fixture();let rawCalls=0,trackingCalls=0,apiCalls=0;
    const channel={id:'10',type:1,recipients:['456']};
    f.runtime.c.user={exports:{getName:()=> 'UserStore',getCurrentUser:()=>({id:'123'}),getUser:id=>({id,username:'Fixture'})}};
    f.runtime.c.channels={exports:{getName:()=> 'ChannelStore',getChannel:id=>id==='10'?channel:null,getSortedPrivateChannels:()=>[channel]}};
    f.runtime.c.relations={exports:{getName:()=> 'RelationshipStore',isBlockedOrIgnored:()=>false}};
    const raw=Object.assign(function rawRequest(){},{get(url,data,callback){rawCalls++;throw new Error('Wrong positional request contract');},
        post(url,data,callback){rawCalls++;},del(){},put(){},patch(){},Request(){},Response(){},agent(){}});
    const tracking={get(options){trackingCalls++;throw new Error('Missing trackedActionData');},post(options){trackingCalls++;throw new Error('Missing trackedActionData');},
        put(){},patch(){},delete(){}};
    f.runtime.c.raw={exports:raw};f.runtime.c.tracking={exports:tracking};
    const request=async function request(method,options,context){
        apiCalls++;assert.equal(options.url,'/channels/10/messages');assert.equal(options.retries,0);
        const message={id:method==='GET'?'100':'101',channel_id:'10',author:{id:method==='GET'?'456':'123',username:'Fixture'},
            content:method==='GET'?'Fixture message':options.body.content,nonce:options.body?.nonce,attachments:[],timestamp:'2026-11-16T12:00:00Z'};
        return {body:method==='GET'?[message]:message};
    };
    const direct={get:request.bind(null,'GET'),post:request.bind(null,'POST'),del(){},put(){},patch(){}};
    assert.equal(direct.get.length,2);f.runtime.c.api={exports:direct};
    const page=await f.invoke('command',{owner,method:'messageHistory',args:{account:'123',channelId:'10',limit:20}});
    assert.equal(page.context.ok,true);assert.equal(page.items.length,1);assert.equal(apiCalls,1);assert.equal(rawCalls,0);assert.equal(trackingCalls,0);
    const sent=await f.invoke('command',{owner,method:'messageSend',args:{account:'123',channelId:'10',nonce:'1234',content:'Fixture explicit send'}});
    assert.equal(sent.ok,true);assert.equal(sent.value.status,'sent');assert.equal(apiCalls,2);assert.equal(rawCalls,0);assert.equal(trackingCalls,0);
});

test('sealed messaging fails closed with only raw and tracking helpers loaded',async()=>{
    const f=fixture();let rawCalls=0,trackingCalls=0;
    const channel={id:'10',type:1,recipients:['456']};
    f.runtime.c.user={exports:{getName:()=> 'UserStore',getCurrentUser:()=>({id:'123'})}};
    f.runtime.c.channels={exports:{getName:()=> 'ChannelStore',getChannel:()=>channel,getSortedPrivateChannels:()=>[channel]}};
    f.runtime.c.raw={exports:Object.assign(function rawRequest(){},{get(url,data,callback){rawCalls++;},post(url,data,callback){rawCalls++;},del(){},put(){},patch(){}})};
    f.runtime.c.tracking={exports:{get(options){trackingCalls++;},post(options){trackingCalls++;},put(){},patch(){},delete(){}}};
    const response=await f.invoke('command',{owner,method:'messageHistory',args:{account:'123',channelId:'10',limit:20}});
    assert.equal(response.ok,false);assert.equal(response.code,'messaging_unavailable');assert.equal(rawCalls,0);assert.equal(trackingCalls,0);
});
