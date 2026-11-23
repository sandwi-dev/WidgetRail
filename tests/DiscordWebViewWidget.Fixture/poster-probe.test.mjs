import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {runInNewContext} from 'node:vm';
import test from 'node:test';

// Execute the actual opt-in validation script. No WebView, account or network.
const native=readFileSync(new URL('../../src/OverlayFrontend.WinUI/Validation/BrowserOwner.DiscordPosterProbe.cs',import.meta.url),'utf8');
const script=native.match(/private const string DiscordPosterProbeScript = """\r?\n([\s\S]*?)\r?\n\s*""";/)?.[1];
assert.ok(script,'Native fixture script is present');
const plain=value=>JSON.parse(JSON.stringify(value));
const video=(extra={})=>({id:'200',filename:'clip.mp4',content_type:'video/mp4',size:1000,
    url:'https://cdn.discordapp.com/attachments/10/200/clip.mp4?ex=111&hm=abc',
    proxy_url:'https://media.discordapp.net/attachments/10/200/clip.mp4?ex=111&is=222&hm=a%2Bb%3D',...extra});
const row=(files=[video()])=>({id:'100',channel_id:'10',attachments:files});
const request={method:'messageHistory',owner:'00000000000000000000000000000000',args:{account:'1',channelId:'10',limit:40}};
function fixture(rows=[row()]){
    const f={account:'1',gets:0,rows,timers:[],status:200};
    f.api={async get(options){assert.equal(this,f.api);++f.gets;f.lastRequest=options;return f.read?f.read(options):{status:f.status,body:f.rows};},post(){},put(){},patch(){},del(){}};
    f.adapter={async invoke(call){
        assert.equal(this,f.adapter);
        if(call.method!=='messageHistory')return {ok:true,value:{}};
        const response=await f.api.get({url:'/channels/'+call.args.channelId+'/messages',query:{limit:call.args.limit??40}});
        if(f.reply)return f.reply(response,call);
        return {ok:true,value:{account:call.args.account,channelId:call.args.channelId,items:response.body.map(item=>({id:item.id,
            attachments:item.attachments.map(file=>({id:file.id,url:file.url,byteLength:file.size,contentType:file.content_type,isSpoiler:!!(file.flags&8)}))}))}};
    }};
    f.originalGet=f.api.get;f.originalInvoke=f.adapter.invoke;
    f.context={URL,Promise,setTimeout:(fn,delay)=>{assert.equal(delay,900000);f.timers.push(fn);return f.timers.length;},
        WidgetRailDiscordPrototype:f.adapter,WidgetRailWebWebpack:{
            findByProps:(...args)=>typeof args.at(-1)==='function'&&args.at(-1)(f.api)?f.api:null,
            findStore:name=>name==='UserStore'?{getCurrentUser:()=>({id:f.account})}:null}};
    f.readProbe=()=>plain(runInNewContext(script,f.context));
    f.call=()=>f.context.WidgetRailDiscordPrototype.invoke(request);
    return f;
}

test('install and polling never issue an authenticated or media request',async()=>{
    const f=fixture();assert.deepEqual(f.readProbe(),{category:'waiting'});assert.equal(f.gets,0);
    await f.adapter.invoke({method:'snapshot'});assert.equal(f.gets,0);
    await f.api.get({url:'/channels/10/messages',query:{limit:40}});
    assert.deepEqual(f.readProbe(),{category:'waiting'},'An unrelated resident/background request is ignored');
    assert.equal(f.gets,1);assert.equal(f.timers.length,1);
});

test('one already-requested history supplies one bounded JPEG candidate with signing fields unchanged',async()=>{
    const signing='ex=111&is=222&hm=a%2Bb%3D&custom=%2Fkeep%2f';
    const f=fixture([row([video({proxy_url:'https://media.discordapp.net/attachments/10/200/clip.mp4?'+signing+'&format=webp&width=4096&height=2048&%77idth=999&quality=100&animated=true'})])]);
    f.readProbe();const response=await f.call();assert.equal(response.ok,true);
    const candidate=f.readProbe();assert.equal(candidate.category,'candidate');
    assert.equal(candidate.posterUrl,'https://media.discordapp.net/attachments/10/200/clip.mp4?'+signing+'&format=jpeg&width=512&height=512');
    assert.equal(f.gets,1);assert.deepEqual(plain(f.lastRequest),{url:'/channels/10/messages',query:{limit:40}});
    assert.equal(f.api.get,f.originalGet);assert.equal(f.adapter.invoke,f.originalInvoke);
    assert.deepEqual(f.readProbe(),{category:'retired'});assert.equal(f.gets,1);
});

test('host path credentials port fragments whitespace and traversal cannot produce a candidate',async()=>{
    for(const proxy_url of [
        'http://media.discordapp.net/attachments/10/200/clip.mp4',
        'https://cdn.discordapp.com/attachments/10/200/clip.mp4',
        'https://media.discordapp.net.evil.example/attachments/10/200/clip.mp4',
        'https://localhost/attachments/10/200/clip.mp4',
        'https://127.0.0.1/attachments/10/200/clip.mp4',
        'https://media.discordapp.net:443/attachments/10/200/clip.mp4',
        'https://media.discordapp.net:444/attachments/10/200/clip.mp4',
        'https://u:p@media.discordapp.net/attachments/10/200/clip.mp4',
        'https://media.discordapp.net/attachments/10/200/clip.mp4#x',
        'https://media.discordapp.net/attachments/10/200/clip.mp4\n',
        'https://media.discordapp.net/attachments/10/200/a\\b.mp4',
        'https://media.discordapp.net/attachments/10/200/../clip.mp4',
        'https://media.discordapp.net/attachments/10/200/%2e%2e/clip.mp4',
        'https://media.discordapp.net/attachments/10/200/clip.mp4?%not-valid=1',
        'https://media.discordapp.net/external/10/200/clip.mp4'
    ]){
        const f=fixture([row([video({proxy_url})])]);f.readProbe();await f.call();
        assert.deepEqual(f.readProbe(),{category:'no_candidate'});assert.equal(f.gets,1);
    }
});

test('probe skips spoiler sources and does not infer a proxy from the original URL',async()=>{
    for(const extra of [{flags:8},{filename:'SPOILER_clip.mp4'},{proxy_url:null},{content_type:'image/jpeg'},{flags:'8'}]){
        const f=fixture([row([video(extra)])]);f.readProbe();await f.call();assert.deepEqual(f.readProbe(),{category:'no_candidate'});
    }
    const f=fixture([row([video({flags:8}),video({id:'201',url:'https://cdn.discordapp.com/attachments/10/201/safe.mp4',
        proxy_url:'https://media.discordapp.net/attachments/10/201/safe.mp4'})])]);
    f.readProbe();await f.call();assert.match(f.readProbe().posterUrl,/\/201\/safe\.mp4\?/);
});

test('raw candidate must survive the ordinary provider history validation and current normalized attachment',async()=>{
    for(const changed of [item=>{item.id='different';},item=>{item.attachments=[];},item=>{item.attachments[0].url=null;},
        item=>{item.attachments[0].byteLength++;},item=>{item.attachments[0].isSpoiler=true;}]){
        const f=fixture();f.reply=response=>{
            const item={id:'100',attachments:[{id:'200',url:response.body[0].attachments[0].url,byteLength:1000,contentType:'video/mp4'}]};
            changed(item);return {ok:true,value:{account:'1',channelId:'10',items:[item]}};
        };
        f.readProbe();await f.call();assert.deepEqual(f.readProbe(),{category:'no_candidate'});
    }
    const f=fixture();f.reply=()=>({ok:false,code:'invalid_message'});f.readProbe();await f.call();
    assert.deepEqual(f.readProbe(),{category:'history_failed'});
});

test('probe inspection is bounded and never substitutes another channel',async()=>{
    for(const rows of [Array.from({length:51},()=>row()),[row(Array.from({length:11},()=>video()))],
        [{...row(),channel_id:'20'}]]){
        const f=fixture(rows);f.readProbe();await f.call();assert.deepEqual(f.readProbe(),{category:'no_candidate'});
    }
});

test('account changes and timeout retire the probe without exposing a candidate',async()=>{
    const f=fixture();let resolve;f.read=()=>new Promise(done=>resolve=done);
    f.readProbe();const pending=f.call();f.account='9';resolve({status:200,body:f.rows});await pending;
    assert.deepEqual(f.readProbe(),{category:'retired'});assert.equal(f.api.get,f.originalGet);
    const waiting=fixture();waiting.readProbe();waiting.timers[0]();assert.deepEqual(waiting.readProbe(),{category:'expired'});
    assert.equal(waiting.api.get,waiting.originalGet);assert.equal(waiting.adapter.invoke,waiting.originalInvoke);assert.equal(waiting.gets,0);
});

test('native current check remains true only for the same adapter account and bounded probe lifetime',async()=>{
    const f=fixture();f.readProbe();await f.call();assert.equal(f.readProbe().category,'candidate');
    assert.equal(f.context.__widgetRailDiscordPosterProbe.current(),true);
    f.account='9';assert.equal(f.context.__widgetRailDiscordPosterProbe.current(),false);
    f.account='1';f.context.WidgetRailDiscordPrototype={...f.adapter};assert.equal(f.context.__widgetRailDiscordPosterProbe.current(),false);
    f.context.WidgetRailDiscordPrototype=f.adapter;f.timers[0]();assert.equal(f.context.__widgetRailDiscordPosterProbe.current(),false);
});

test('one unsuccessful observed history is terminal and leaves subsequent calls unchanged',async()=>{
    const f=fixture([row([])]);f.readProbe();await f.call();assert.deepEqual(f.readProbe(),{category:'no_candidate'});
    f.rows=[row()];await f.call();assert.deepEqual(f.readProbe(),{category:'no_candidate'});assert.equal(f.gets,2);
});

test('failed installation restores the ordinary API without creating a persistent probe',()=>{
    const f=fixture();Object.freeze(f.adapter);
    assert.deepEqual(f.readProbe(),{category:'probe_unavailable'});assert.equal(f.api.get,f.originalGet);
    assert.equal(f.context.__widgetRailDiscordPosterProbe,undefined);assert.equal(f.gets,0);
});

test('ordinary failures keep their original behavior and do not expose raw error text',async()=>{
    const f=fixture(),failure=new Error('private transport detail');f.read=()=>Promise.reject(failure);
    f.readProbe();await assert.rejects(f.call(),error=>error===failure);
    assert.deepEqual(f.readProbe(),{category:'history_failed'});assert.equal(f.api.get,f.originalGet);
    const sync=fixture();sync.adapter.invoke=function(){throw failure;};sync.originalInvoke=sync.adapter.invoke;
    sync.readProbe();assert.throws(sync.call,error=>error===failure);
    assert.deepEqual(sync.readProbe(),{category:'history_failed'});assert.equal(sync.adapter.invoke,sync.originalInvoke);
});
