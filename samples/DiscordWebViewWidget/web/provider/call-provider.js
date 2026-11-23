// Private-call semantics belong to Discord. No credentials, HTTP client or
// capture source crosses this adapter. Actions mirror the resident client.
globalThis.createWidgetRailDiscordCalls = ({store,props,me,currentChannel,invalidate}) => {
    const validId=value=>typeof value==='string'&&/^[0-9]{1,32}$/.test(value);
    const fail=code=>{throw Object.assign(new Error(code),{adapterCode:code});};
    const label=value=>{
        let result='';
        for(const scalar of String(value??'').replace(/[\u0000-\u001f\u007f-\u009f]/g,' ')) {
            if(result.length+scalar.length>120)break;
            result+=scalar.length===1&&/[\ud800-\udfff]/.test(scalar)?'\ufffd':scalar;
        }
        return result;
    };
    let disposed=false,accountId=null,callStore=null,listener=null,pending=null;
    let invitations=new Map();const commands=new Map();
    function startActions(){
        // Current Discord exposes CallActions.call(channel, video, ring,
        // recipient, onSelected). Require its sibling actions as a concrete
        // module signature; a function's inherited Function.call is not this API.
        const modern=props('call','ring','stopRinging',value=>['call','ring','stopRinging'].every(key=>typeof value[key]==='function'));
        if(modern&&typeof modern==='object'&&Object.hasOwn(modern,'call')&&
            modern.call!==Function.prototype.call&&['call','ring','stopRinging'].every(key=>typeof modern[key]==='function'))return {actions:modern,modern:true};
        const legacy=props('startCall',value=>typeof value.startCall==='function');
        return typeof legacy?.startCall==='function'?{actions:legacy,modern:false}:null;
    }
    function startCall(binding,target){
        if(!binding.modern)return binding.actions.startCall(target.id);
        return new Promise((resolve,reject)=>{
            // The current client returns void while its recipient preflight is
            // pending. Keep single-flight ownership until its completion callback,
            // not merely until that synchronous return. An ambiguous timeout is
            // never retried automatically and remains in the command ledger.
            const timer=setTimeout(()=>reject(Object.assign(new Error('call_action_unconfirmed'),{adapterCode:'call_action_unconfirmed'})),10000);
            const complete=()=>{clearTimeout(timer);resolve();};
            try{
                const result=binding.actions.call(target.id,false,true,target.type===1?target.recipients[0]:undefined,complete);
                if(result&&typeof result.then==='function')result.catch(error=>{clearTimeout(timer);reject(error);});
            }catch(error){clearTimeout(timer);reject(error);}
        });
    }
    function account(expected) {
        if(disposed)fail('calls_retired');
        const actual=me()?.id;
        if(actual!==accountId){accountId=actual;invitations.clear();commands.clear();pending=null;}
        if(!validId(actual)||expected!==undefined&&expected!==actual)fail('account_changed');
        return actual;
    }
    function channel(id) {
        if(!validId(id))return null;
        const value=store('ChannelStore')?.getChannel?.(id);
        if(value?.id!==id||![1,3].includes(value.type)||value.guild_id||!Array.isArray(value.recipients)||
            value.recipients.length>100||value.recipients.some(id=>!validId(id)))return null;
        if(value.type===1 && (value.recipients.length!==1 ||
            store('RelationshipStore')?.isBlockedOrIgnored?.(value.recipients[0])!==false))return null;
        return value;
    }
    function name(value) {
        if(value.name)return label(value.name);
        return label(value.recipients.map(id=>{const user=store('UserStore')?.getUser?.(id);return user?.globalName||user?.username||'Participant';}).join(', '))||'Private call';
    }
    function participants(id) {
        const values=store('VoiceStateStore')?.getVoiceStatesForChannel?.(id);
        if(!values||typeof values!=='object')return [];
        return Object.values(values).slice(0,101).filter(value=>validId(value?.userId)&&value.userId!==accountId&&
            (value.channelId===undefined||value.channelId===id));
    }
    function observe() {
        const next=store('CallStore');
        if(next===callStore)return;
        if(listener)try{callStore?.removeChangeListener?.(listener);}catch{}
        invitations.clear();callStore=next;listener=null;
        if(typeof next?.addChangeListener==='function'&&typeof next.removeChangeListener==='function') {
            const changed=()=>{if(disposed||callStore!==next)return;try{sync();}catch{invitations.clear();}invalidate?.();};
            try{next.addChangeListener(changed);listener=changed;}catch{listener=null;}
        }
    }
    function sync() {
        const user=account();observe();
        const all=callStore?.getCalls?.();
        if(!Array.isArray(all)||typeof callStore?.getCall!=='function'){
            invitations.clear();return {available:false,active:[],diagnostic:!callStore?'store_missing':
                typeof callStore.getCalls!=='function'?'calls_method_missing':!Array.isArray(all)?'calls_shape_'+typeof all:'call_method_missing'};
        }
        const incoming=new Map(),active=[];
        for(const call of all.slice(0,1000)) {
            const target=channel(call?.channelId);
            if(!target||call.unavailable||!Array.isArray(call.ringing)||call.ringing.length>100||call.ringing.some(id=>!validId(id)))continue;
            if(participants(target.id).length>0&&active.length<100&&!active.includes(target.id))active.push(target.id);
            if(!call.ringing.includes(user)||currentChannel()===target.id||incoming.size>=8)continue;
            const signature=validId(call.messageId)?call.messageId:null;
            const prior=invitations.get(target.id);
            incoming.set(target.id,prior?.signature===signature?{...prior,name:name(target)}:
                {id:crypto.randomUUID().replaceAll('-',''),channelId:target.id,name:name(target),signature});
        }
        invitations=incoming;
        return {available:true,active};
    }
    function snapshot() {
        try {
            const observed=sync(),selected=currentChannel(),target=channel(selected);
            const start=startActions()!==null,join=typeof props('selectVoiceChannel')?.selectVoiceChannel==='function';
            const call=target?callStore?.getCall?.(selected):null;
            const ringing=Array.isArray(call?.ringing)?call.ringing:[];
            return {available:observed.available,
                diagnostic:observed.diagnostic??(!start?'start_action_missing':!join?'join_action_missing':'ready'),
                canStart:observed.available&&start&&join,
                canAnswer:observed.available&&listener!==null&&typeof props('selectVoiceChannel')?.selectVoiceChannel==='function',
                canDecline:observed.available&&listener!==null&&typeof props('stopRinging')?.stopRinging==='function',
                incoming:[...invitations.values()].map(({signature,...value})=>value),activeChannels:observed.active,
                current:observed.available&&target?{channelId:selected,name:name(target),phase:call?.unavailable===true?'unavailable':participants(selected).length>0?'active':
                    ringing.some(id=>id!==accountId&&validId(id))?'outgoing':'waiting'}:null};
        } catch {return {available:false,canStart:false,canAnswer:false,canDecline:false,incoming:[],activeChannels:[],current:null,diagnostic:'snapshot_failed'};}
    }
    function invoke(method,args) {
        account(args?.account);
        if(!['callStart','callAnswer','callDecline'].includes(method)||!validId(args.channelId)||
            !/^[a-f0-9]{32}$/.test(args.commandId??'')||!/^[a-f0-9]{32}$/.test(args.owner??'')||
            args.previousChannelId!==null&&!validId(args.previousChannelId))fail('invalid_request');
        const key=args.owner+':'+args.commandId;
        const signature=JSON.stringify([method,args.account,args.channelId,args.previousChannelId,args.invitationId??null]);
        const prior=commands.get(key);
        if(prior){if(prior.signature!==signature)fail('invalid_request');return prior.promise;}
        if(pending)fail('call_action_pending');
        const entry={signature,promise:null};
        commands.set(key,entry);pending=entry;
        // Set the ledger before execution: provider actions may synchronously
        // reenter the adapter through Flux notifications.
        entry.promise=Promise.resolve().then(async()=>{
            function verify() {
                account(args.account);
                if(currentChannel()!==args.previousChannelId)fail('call_changed');
                const observed=sync(),target=channel(args.channelId);
                account(args.account);if(currentChannel()!==args.previousChannelId)fail('call_changed');
                if(!observed.available||!target)fail('call_unavailable');
                if(method!=='callStart'&&!listener)fail('call_action_unavailable');
                if(method!=='callStart' && invitations.get(target.id)?.id!==args.invitationId)fail('call_invitation_expired');
                return observed;
            }
            const observed=verify();
            if(method==='callDecline') {
                const actions=props('stopRinging');if(typeof actions?.stopRinging!=='function')fail('call_action_unavailable');
                verify();await actions.stopRinging(args.channelId,[args.account]);
            } else {
                if(args.previousChannelId===args.channelId)return {requested:false};
                // A live existing call is joined without ringing its peers again.
                const join=method==='callAnswer'||observed.active.includes(args.channelId);
                if(join){
                    const actions=props('selectVoiceChannel');if(typeof actions?.selectVoiceChannel!=='function')fail('call_action_unavailable');
                    verify();await actions.selectVoiceChannel(args.channelId);
                }else{
                    const binding=startActions();if(!binding)fail('call_action_unavailable');
                    verify();await startCall(binding,channel(args.channelId));
                }
            }
            account(args.account);return {requested:true};
        }).finally(()=>{
            if(pending===entry)pending=null;
            while(commands.size>16)commands.delete(commands.keys().next().value);
        });
        return entry.promise;
    }
    return Object.freeze({snapshot,invoke,dispose(){disposed=true;if(listener)try{callStore?.removeChangeListener?.(listener);}catch{}
        invitations.clear();commands.clear();pending=null;callStore=listener=null;}});
};
