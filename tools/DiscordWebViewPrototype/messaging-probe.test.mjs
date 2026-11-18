import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
import {webcrypto} from 'node:crypto';

const host=readFileSync(new URL('../../src/OverlayFrontend.WinUI/Validation/BrowserOwner.DiscordMessagingProbe.cs',import.meta.url),'utf8');
const script=/private const string DiscordMessagingProbeScript="""\r?\n([\s\S]*?)\r?\n\s*""";/.exec(host)[1];
function fixture(get,options={}){
    const calls=[],timers=[];
    const api={get};
    let searches=0;
    const wp={findByProps:(...names)=>{
        if(typeof names.at(-1)==='function'){searches++;return (options.candidates??[]).find(names.at(-1));}
        return names[0]==='getAPIBaseURL'?options.authenticated??api:names[0]==='USER'?{MESSAGES:id=>'/channels/'+id+'/messages'}:
            names[0]==='editMessage'?options.actions??api:api;
    }};
    const context=vm.createContext({URL,crypto:webcrypto,webpackChunkdiscord_app:options.chunks,location:{href:'https://discord.com/app',origin:'https://discord.com'},
        performance:{getEntriesByType:()=>options.resources??[]},
        WidgetRailWebWebpack:wp,
        WidgetRailDiscordPrototype:{invoke:request=>{calls.push(request.method);return {ok:true,value:{method:'messageHistory',type:'Error',detail:'private-message-text'}};}},
        setTimeout:callback=>timers.push(callback)});
    return {api,wp,calls,timers,searches:()=>searches,read:()=>vm.runInContext(script,context)};
}
test('opt-in messaging probe reports shapes without exposing identities text or URLs or issuing history reads',async()=>{
    let calls=0;
    const response={status:200,body:[{id:'987654321',channel_id:'123456789',author:{id:'999999999',username:'private-person-name'},
        content:'private-message-text',timestamp:'2026-11-16T12:00:00Z',attachments:[{id:'111111111',filename:'private-file-name',size:50,url:'https://private.example/secret'}]}]};
    const f=fixture(function(){assert.equal(this,f.api);calls++;return Promise.resolve(response);});
    assert.equal((await f.read()).probeAttached,true);assert.equal(calls,0);assert.deepEqual(f.calls,['diagnostics']);
    assert.equal(await f.api.get({url:'/channels/123456789/messages',query:{limit:20}}),response);
    const output=await f.read(),json=JSON.stringify(output);
    assert.equal(output.shape.count,1);assert.equal(output.shape.channelMatches,1);assert.equal(output.code,'unclassified');
    for(const secret of ['987654321','123456789','999999999','private-person-name','private-message-text','private-file-name','private.example'])assert.equal(json.includes(secret),false);
    assert.equal(calls,1);assert.equal(f.calls.every(method=>method==='diagnostics'),true);
});
test('probe preserves provider rejection and removes its wrapper on bounded expiry',async()=>{
    const failure=Object.assign(new Error('private transport detail'),{status:404}),original=()=>Promise.reject(failure);
    const f=fixture(original);await f.read();assert.notEqual(f.api.get,original);
    await assert.rejects(f.api.get({url:'/channels/123456789/messages'}),error=>error===failure);
    const output=await f.read();assert.equal(output.shape.attemptSeen,true);assert.equal(output.shape.rejected,true);assert.equal(output.shape.httpStatus,404);
    assert.equal(JSON.stringify(output).includes('private transport detail'),false);assert.equal(JSON.stringify(output).includes('123456789'),false);
    assert.equal(f.timers.length,2);f.timers.forEach(expire=>expire());assert.equal(f.api.get,original);
    assert.equal((await f.read()).probeAttached,false);assert.equal(f.timers.length,2);
});
test('API contract probe distinguishes generic HTTP from authenticated API routing without exposing routes or issuing requests',async()=>{
    let requests=0;
    const authenticated={getAPIBaseURL:()=>'/api/v9',get:()=>{requests++;},post(){},delete(){}};
    const f=fixture(()=>{requests++;},{authenticated,resources:[{name:'https://discord.com/channels/987654321/messages?private=query'}]});
    const output=await f.read();
    assert.equal(output.apiFacts.sameAsGeneric,false);assert.equal(output.apiFacts.authenticatedBaseSameOrigin,true);
    assert.equal(output.apiFacts.authenticatedBaseVersioned,true);assert.equal(output.apiFacts.endpointRelative,true);
    assert.equal(output.apiFacts.recentHistorySitePath,true);assert.equal(output.apiFacts.recentHistoryApiPath,false);
    assert.equal(requests,0);assert.equal(f.timers.length,3);
    const serialized=JSON.stringify(output);assert.equal(serialized.includes('987654321'),false);assert.equal(serialized.includes('private=query'),false);
    assert.equal(serialized.includes('https://'),false);assert.equal(serialized.includes('/api/v9'),false);
});
test('candidate enumeration is bounded and cached and records the actual production finder result without reading credential fields',async()=>{
    let credentialReads=0;
    const candidates=Array.from({length:10},(_,index)=>({get(options){return options;},post(options){return options;},delete(){},marker:index}));
    Object.defineProperty(candidates[0],'token',{enumerable:true,get(){credentialReads++;throw new Error('Must not read credentials');}});
    const f=fixture(function(url,data,callback){},{candidates});
    const first=await f.read();assert.equal(first.candidates.length,8);const searches=f.searches();
    assert.equal(first.candidates.some(candidate=>candidate.selected),false);
    f.wp.findByProps('get','post',candidate=>candidate===candidates[3]);
    const selected=await f.read();assert.equal(f.searches(),searches+1);assert.equal(selected.candidates[3].selected,true);
    assert.equal(selected.candidates.filter(candidate=>candidate.selected).length,1);assert.equal(credentialReads,0);
    assert.equal(selected.candidates[0].fields.includes('token'),true);
    assert.equal(Object.hasOwn(selected.candidates[0],'target'),false);
    f.wp.findByProps('get','post',candidate=>candidate===candidates[9]);
    const late=await f.read();assert.equal(late.candidates.length,8);assert.equal(late.candidates[7].selected,true);
    assert.equal(f.searches(),searches+2);
    f.timers.forEach(expire=>expire());assert.equal(credentialReads,0);
});
test('already-loaded module factory is inspected without execution and separate template literals do not hide retry logic',async()=>{
    let executions=0,credentialReads=0;
    const candidate={get:function(e){return a('GET',e,'fixture');},post:function(e){return a('POST',e,'fixture');},delete(){}};
    const factory=function(module,exports,require){
        executions++;
        function a(method,options,label){
            const before=`private-before-${options.url}`;
            const retries=options.retries??1;
            const after=`private-after-${options.content}`;
            for(let attempt=0;attempt<retries;attempt++)deliver(method,options);
            return {before,after};
        }
    };
    const unrelated={};Object.defineProperty(unrelated,'key',{get(){credentialReads++;throw Error('credential read');}});
    const runtime={c:{one:{exports:unrelated},two:{exports:{A:candidate}}},m:{two:factory}};
    const chunks=[];chunks.push=function(item){Array.prototype.push.call(this,item);item[2](runtime);return this.length;};
    const actions={editMessage(){},sendMessage(){},fetchMessages(options){
        const first=`private-first-${options.channelId}`;
        const result=client.get({url:endpoint,query:{limit:20},retries:1});
        const last=`private-last-${options.channelId}`;return result;
    }};
    const f=fixture(function(url,data,callback){},{candidates:[candidate],chunks,actions});
    await f.read();f.wp.findByProps('get','post',value=>value===candidate);
    const output=await f.read();assert.equal(output.factory.found,true);
    assert.match(output.factory.helperSource,/attempt<retries/);assert.match(output.factory.retrySource,/options.retries/);
    assert.equal(output.apiFacts.fetchUsesObjectGet,true);assert.match(output.sources.messageFetchRequest,/retries:1/);
    assert.equal(executions,0);assert.equal(credentialReads,0);assert.equal(chunks.length,0);
    for(const secret of ['private-before','private-after','private-first','private-last'])assert.equal(JSON.stringify(output).includes(secret),false);
});
