// Domain-only discovery over already loaded Discord stores. Never mounts a page,
// fetches hidden presence, reads messages, or contacts another person.
globalThis.createWidgetRailDiscordActivities = dependenciesInput => {
    'use strict';
    const {store,me,currentChannel,voiceActionAvailable,watchAvailable}=dependenciesInput;
    const id=value=>typeof value==='string'&&/^[0-9]{1,32}$/.test(value);
    const label=value=>{
        if(typeof value!=='string')return '';
        let result='';
        for(const scalar of value.replace(/[\u0000-\u001f\u007f-\u009f]/g,' ')){
            if(result.length+scalar.length>120)break;
            result+=scalar.length===1&&/[\ud800-\udfff]/.test(scalar)?'\ufffd':scalar;
        }
        return result;
    };
    function avatarUrl(userId,hash){
        return id(userId)&&typeof hash==='string'&&/^(?:a_)?[a-f0-9]{32}$/.test(hash)
            ?`https://cdn.discordapp.com/avatars/${userId}/${hash}.png?size=64`:null;
    }
    const VIEW_CHANNEL=1024n,CONNECT=1048576n; // Discord's documented permission bits.
    const types=new Map([[0,'playing'],[1,'streaming'],[2,'listening'],[3,'watching'],[5,'competing']]);
    const fail=code=>{throw Object.assign(new Error(code),{adapterCode:code});};
    function dependencies(){
        const relationships=store('RelationshipStore'),presence=store('PresenceStore'),users=store('UserStore');
        if(typeof relationships?.getFriendIDs!=='function'||typeof relationships?.isFriend!=='function'||
            typeof relationships?.isBlockedOrIgnored!=='function'||typeof presence?.getActivities!=='function'||
            typeof presence?.getStatus!=='function'||typeof users?.getUser!=='function')return null;
        return {relationships,presence,users};
    }
    function friend(d,userId){return id(userId)&&userId!==me()?.id&&d.relationships.isFriend(userId)===true&&d.relationships.isBlockedOrIgnored(userId)===false;}
    function guildVoiceAvailable(){
        return typeof store('PermissionStore')?.can==='function'&&
            typeof store('ChannelStore')?.getChannel==='function'&&voiceActionAvailable();
    }
    function guildVoiceChannel(target){
        if(!id(target)||!guildVoiceAvailable())return null;
        const channel=store('ChannelStore').getChannel(target),permission=store('PermissionStore');
        if(!channel||channel.id!==target||channel.type!==2||!id(channel.guild_id)||
            permission.can(VIEW_CHANNEL,channel)!==true||permission.can(CONNECT,channel)!==true)return null;
        return channel;
    }
    function validateGuildJoin(args){
        if(!id(args?.account)||me()?.id!==args.account)fail('account_changed');
        if((args.channelId??null)!==(currentChannel()??null))fail('call_changed');
        if(!guildVoiceAvailable())fail('voice_action_unavailable');
        const channel=guildVoiceChannel(args.target);if(!channel)fail('channel_unavailable');
        if(me()?.id!==args.account||(args.channelId??null)!==(currentChannel()??null))fail('call_changed');
        return channel;
    }
    function voiceFor(userId){
        const voice=store('VoiceStateStore'),permission=store('PermissionStore');
        if(typeof voice?.getDiscoverableVoiceStateForUser!=='function'||typeof permission?.can!=='function')return null;
        const state=voice.getDiscoverableVoiceStateForUser(userId);
        if(!state||state.discoverable===false||state.userId!==userId||!id(state.channelId))return null;
        const channel=store('ChannelStore')?.getChannel?.(state.channelId);
        // Guild voice only: Stage/DM calls have different admission and are not inferred.
        if(!channel||channel.id!==state.channelId||channel.type!==2||!id(channel.guild_id)||permission.can(VIEW_CHANNEL,channel)!==true)return null;
        const guild=store('GuildStore')?.getGuild?.(channel.guild_id);
        if(!guild||guild.id!==channel.guild_id||guild.afkChannelId===channel.id)return null;
        return {channel,guild,joinable:permission.can(CONNECT,channel)===true};
    }
    function streamFor(userId,voice){
        const streaming=store('ApplicationStreamingStore');
        if(!voice||typeof streaming?.getAnyDiscoverableStreamForUser!=='function')return null;
        const stream=streaming.getAnyDiscoverableStreamForUser(userId);
        if(!stream||stream.state==='ENDED'||(stream.ownerId??stream.userId)!==userId||stream.channelId!==voice.channel.id||stream.guildId!==voice.guild.id)return null;
        const key=`guild:${voice.guild.id}:${voice.channel.id}:${userId}`;
        return {key,watchable:voice.joinable&&currentChannel()===voice.channel.id&&watchAvailable(key)&&streaming.isStreamMarkedFull?.(key)!==true};
    }
    function row(d,userId){
        if(!friend(d,userId))return null;
        const user=d.users.getUser(userId);if(!user||user.id!==userId||user.bot)return null;
        const rawStatus=d.presence.getStatus(userId);
        const status=['online','idle','dnd'].includes(rawStatus)?rawStatus:'offline';
        const visible=status==='offline'?[]:d.presence.getActivities(userId);
        if(!Array.isArray(visible))throw Error('Activity data unavailable');
        const activity=visible.slice(0,16).find(a=>a&&types.has(a.type)&&label(a.name));
        const voice=voiceFor(userId),stream=streamFor(userId,voice);
        if(!activity&&!voice)return null;
        const started=activity?.timestamps?.start;
        return {userId,name:label(user.globalName||user.username)||'Friend',status,
            kind:activity?types.get(activity.type):'voice',title:activity?label(activity.name):'In voice chat',details:label(activity?.details),
            channelId:voice?.channel.id??null,guildId:voice?.guild.id??null,channelName:voice?label(voice.channel.name):null,guildName:voice?label(voice.guild.name):null,
            canJoinVoice:!!voice?.joinable&&voiceActionAvailable()&&currentChannel()!==voice.channel.id,
            streamKey:stream?.key??null,canWatch:!!stream?.watchable,
            startedAt:Number.isSafeInteger(started)&&started>=0&&started<=Date.now()+60000?started:null,avatarUrl:avatarUrl(userId,user.avatar)};
    }
    function snapshot(){
        const account=me()?.id;
        if(!id(account))return {activities:[],activityAvailability:'disconnected',activitiesTruncated:false};
        try{
            const d=dependencies();if(!d)return {activities:[],activityAvailability:'unavailable',activitiesTruncated:false};
            const ids=d.relationships.getFriendIDs();if(!Array.isArray(ids))throw Error('Invalid friends');
            const selected=[...new Set(ids.slice(0,2000).filter(id))].sort();
            const activities=[];let truncated=ids.length>2000;
            const priority=value=>value.canWatch?0:value.canJoinVoice?1:2;
            const compare=(left,right)=>priority(left)-priority(right)||(left.userId<right.userId?-1:left.userId>right.userId?1:0);
            for(const userId of selected){
                const value=row(d,userId);if(!value)continue;
                // Inspect the bounded friend set before choosing the visible rows: a
                // later joinable/watchable friend must not lose to passive presence.
                const position=activities.findIndex(existing=>compare(value,existing)<0);
                if(position<0)activities.push(value);else activities.splice(position,0,value);
                if(activities.length>50){truncated=true;activities.pop();}
            }
            if(me()?.id!==account)return {activities:[],activityAvailability:'unavailable',activitiesTruncated:false};
            return {activities,activityAvailability:'ready',activitiesTruncated:truncated};
        }catch{return {activities:[],activityAvailability:'unavailable',activitiesTruncated:false};}
    }
    function validate(args,watch){
        if(!id(args?.account)||me()?.id!==args.account)fail('account_changed');
        if((args.channelId??null)!==(currentChannel()??null))fail('call_changed');
        const d=dependencies();if(!d||!friend(d,args.userId))fail('activity_unavailable');
        const voice=voiceFor(args.userId);
        if(!voice||voice.channel.id!==args.target||!voice.joinable)fail('activity_unavailable');
        if(watch){const stream=streamFor(args.userId,voice);if(!stream?.watchable||args.key!==stream.key)fail('stream_ended');}
        else validateGuildJoin(args);
        // Re-read authority after provider getters; no row or display label authorizes a command.
        if(me()?.id!==args.account||(args.channelId??null)!==(currentChannel()??null)||!friend(d,args.userId))fail('activity_changed');
        return voice.channel;
    }
    return {snapshot,avatarUrl,guildVoiceAvailable,guildVoiceChannel,validateGuildJoin,validateJoin:args=>validate(args,false),validateWatch:args=>validate(args,true)};
};
