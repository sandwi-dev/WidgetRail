// Web-client actions can perform prerequisite work absent from a raw Flux event.
// Keep authorization and current-channel checks at this provider boundary.
if (!globalThis.WidgetRailDiscordPrototype.observed) {
    const original=globalThis.WidgetRailDiscordPrototype;
    globalThis.WidgetRailDiscordPrototype={...original,observed:true,async invoke(request){
        let response;
        if(['join','leave'].includes(request.method)){
            try{
                if(typeof request.owner!=='string'||!/^[a-f0-9]{32}$/.test(request.owner))throw Error('invalid_owner');
                const snapshot=await original.invoke({method:'snapshot',owner:request.owner});
                const s=snapshot.value,a=request.args;
                if(!snapshot.ok||!s.ready||a?.account!==s.account)throw Error('account_changed');
                const target=request.method==='join'?a.target:null;
                if(request.method==='join'&&(typeof target!=='string'||!s.channels.some(c=>c.id===target)))throw Error('channel_unavailable');
                if(request.method==='leave'&&(!a.channelId||a.channelId!==s.channelId))throw Error('call_changed');
                const actions=WidgetRailWebWebpack.findByProps('selectVoiceChannel');
                if(typeof actions?.selectVoiceChannel!=='function')throw Error('voice_action_unavailable');
                if(s.channelId!==target)await actions.selectVoiceChannel(target);
                response={ok:true,value:{}};
            }catch(e){response={ok:false,code:e.message==='voice_action_unavailable'?e.message:'voice_action_failed'};}
        }else response=await original.invoke(request);
        if(!['snapshot','diagnostics','keep'].includes(request.method))globalThis.WidgetRailLastCommand={method:request.method,ok:response.ok,code:response.code};
        return response;
    }};
}
