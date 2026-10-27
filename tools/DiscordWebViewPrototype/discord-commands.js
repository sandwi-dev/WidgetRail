// Guild joins use the domain adapter's shared permission/current-call validator
// and concrete voice action. The legacy leave wrapper remains for web prerequisites.
// Keep authorization and current-channel checks at this provider boundary.
if (!globalThis.WidgetRailDiscordPrototype.observed) {
    const original=globalThis.WidgetRailDiscordPrototype;
    globalThis.WidgetRailDiscordPrototype={...original,observed:true,async invoke(request){
        let response;
        if(request.method==='leave'){
            try{
                if(typeof request.owner!=='string'||!/^[a-f0-9]{32}$/.test(request.owner))throw Error('invalid_owner');
                const snapshot=await original.invoke({method:'snapshot',owner:request.owner});
                const s=snapshot.value,a=request.args;
                if(!snapshot.ok||!s.ready||a?.account!==s.account)throw Error('account_changed');
                const target=null;
                if(!a.channelId||a.channelId!==s.channelId)throw Error('call_changed');
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
