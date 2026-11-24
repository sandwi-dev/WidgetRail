// WidgetRail hosted Discord adapter, v1. No Discord credentials cross this boundary.
// Internal Discord APIs are feature-probed and may disappear after Discord updates.
function initializeWidgetRailDiscordAdapter(build,videoHost=null,experimentalCaptureFactory=null) {
if(!Number.isSafeInteger(build)||build<1)throw new Error('invalid_adapter_build');
if(globalThis.WidgetRailDiscordPrototype?.build>build)throw new Error('adapter_newer');
if (globalThis.WidgetRailDiscordPrototype?.build !== build) {
    globalThis.WidgetRailDiscordPrototype?.dispose?.();
    globalThis.WidgetRailDiscordPrototype = (() => {
        'use strict';
        const revision = 1;
        const wp = () => globalThis.WidgetRailWebWebpack;
        const store = name => wp()?.findStore(name);
        const props = (...names) => wp()?.findByProps(...names);
        const code = pattern => wp()?.findByCode(pattern);
        const dispatcher = () => wp()?.Common?.FluxDispatcher;
        const messageCommands = new Map([['messagingState','state'],['messageObserve','observe'],['messageConversations','conversations'],['messageLocateConversation','locateConversation'],
            ['messageHistory','history'],['messageMarkRead','markRead'],['messageChanges','readChanges'],['messageRecover','recoverMessages'],['messageOpenDm','openDm'],['messagePrepareAttachments','prepareAttachments'],
            ['messageSend','sendMessage'],['messageReconcile','reconcileSend'],['messageNotificationTarget','notificationTarget']]);
        const fail = code => { throw Object.assign(new Error(code), { adapterCode: code }); };
        const text = value => {
            let result = '';
            for (const scalar of String(value ?? '').replace(/[\u0000-\u001f\u007f-\u009f]/g, ' ')) {
                if (result.length + scalar.length > 120) break;
                result += scalar.length === 1 && /[\ud800-\udfff]/.test(scalar) ? '\ufffd' : scalar;
            }
            return result;
        };
        const me = () => store('UserStore')?.getCurrentUser?.();
        const messaging = globalThis.createWidgetRailDiscordMessaging?.({store,props,me,dispatcher,invalidate:()=>videoHost?.invalidate?.()});
        const channelId = () => store('SelectedChannelStore')?.getVoiceChannelId?.() ?? null;
        const calls = globalThis.createWidgetRailDiscordCalls?.({store,props,me,currentChannel:channelId,invalidate:()=>videoHost?.invalidate?.()});
        const currentShare = () => store('ApplicationStreamingStore')?.getCurrentUserActiveStream?.();
        const streamKey = s => s.guildId ? `guild:${s.guildId}:${s.channelId}:${s.ownerId || s.userId}` : `call:${s.channelId}:${s.ownerId || s.userId}`;
        const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
        const activities = globalThis.createWidgetRailDiscordActivities?.({store,me,currentChannel:channelId,
            voiceActionAvailable:()=>typeof props('selectVoiceChannel')?.selectVoiceChannel==='function',
            watchAvailable:key=>!!watchDemandApi()&&watchOwnership(key)!==null});
        const relays = new Map(); // One independently renewable lease per display slot.
        const retiringWatches = new Map(); // At most one unsettled close per slot; slot reuse waits below.
        const watchFailures = new Map(); // At most the latest failed watch in each of the three slots.
        const byLease = lease => Array.from(relays.values()).find(entry => entry.lease === lease);
        let shareEpoch = 0;
        const experimentalCapture=experimentalCaptureFactory?.({store,me,channelId,currentShare,streamKey,currentEpoch:()=>shareEpoch,peekDesktopSource,videoRateLimits});
        let sharePending = false;
        let shareError = null;
        let shareQualityWarning = null;
        let shareErrorOwner = null;
        let shareDeadline = 0;
        let shareOwner = null;
        let shareOwnerDeadline = 0;
        let lastFailure = null;
        let diagnosticsPending = null;
        let incomingDiagnosticsPending = null;
        let shareQualityBinding = null;
        let connectionStarted = Date.now(), connectionWasReady = false;

        function connectionState(user) {
            // Missing UserStore data is normal during hydration. Only the
            // client's explicit unauthenticated state on its login route is a
            // sign-in requirement; never read tokens or inspect visible text.
            if (user && wp()) { connectionWasReady = true; connectionStarted = Date.now(); return 'ready'; }
            try {
                const auth = store('AuthenticationStore');
                const path = globalThis.location?.pathname;
                if (typeof auth?.isAuthenticated === 'function' && auth.isAuthenticated() === false &&
                    typeof path === 'string' && /^\/(login|register)(?:\/|$)/.test(path)) {
                    connectionWasReady = false; connectionStarted = Date.now(); return 'signed-out';
                }
            } catch { /* A changed optional auth probe is not proof of logout. */ }
            if (Date.now() - connectionStarted >= 30000) return 'unavailable';
            return connectionWasReady ? 'reconnecting' : 'initializing';
        }

        function capabilities() {
            const actions = props('toggleSelfMute', 'toggleSelfDeaf');
            return { voice: activities?.guildVoiceAvailable()===true && !!store('VoiceStateStore'),
                mute: typeof actions?.toggleSelfMute === 'function',
                deafen: typeof actions?.toggleSelfDeaf === 'function',
                watch: !!channelId() && !!watchDemandApi() && streams(channelId()).some(stream=>watchOwnership(stream.key)!==null),
                share: typeof code('"STREAM_START",streamType') === 'function' &&
                    typeof store('MediaEngineStore')?.getMediaEngine?.()?.getDesktopSource === 'function',
                stopShare: typeof code('"STREAM_STOP",streamKey') === 'function' };
        }

        function channels() {
            const result = new Map();
            const guilds = Object.values(store('GuildStore')?.getGuilds?.() ?? {}).slice(0, 200);
            for (const guild of guilds) {
                const groups = store('GuildChannelStore')?.getChannels?.(guild.id) ?? {};
                for (const entries of Object.values(groups)) {
                    if (!Array.isArray(entries)) continue;
                    for (const entry of entries) {
                        const c = entry.channel;
                        if (!c || c.type !== 2 || result.size >= 1000 || !activities?.guildVoiceChannel(c.id)) continue;
                        result.set(c.id, { id: c.id, guildId: guild.id, name: text(c.name), guild: text(guild.name) });
                    }
                }
            }
            return [...result.values()];
        }

        function streams(id) {
            const all = new Map();
            const streaming = store('ApplicationStreamingStore');
            for (const s of [...(streaming?.getAllActiveStreamsForChannel?.(id) ?? []),
                ...(streaming?.getAllApplicationStreamsForChannel?.(id) ?? [])]) {
                if (s.state !== 'ENDED' && s.channelId === id) all.set(streamKey(s), s);
            }
            const channel = store('ChannelStore')?.getChannel?.(id);
            for (const v of Object.values(store('VoiceStateStore')?.getVoiceStatesForChannel?.(id) ?? {})) {
                if (v.selfStream) {
                    const s = { ownerId: v.userId, channelId: id, guildId: channel?.guild_id,
                        streamType: channel?.guild_id ? 'guild' : 'call' };
                    if (!all.has(streamKey(s))) all.set(streamKey(s), s);
                }
            }
            return [...all].filter(([, s]) => (s.ownerId || s.userId) !== me()?.id).slice(0, 50)
                .map(([key, s]) => ({ key, userId: s.ownerId || s.userId, descriptor: s,
                    name: text(store('UserStore')?.getUser?.(s.ownerId || s.userId)?.globalName ||
                        store('UserStore')?.getUser?.(s.ownerId || s.userId)?.username || 'Participant') }));
        }

        function desktopNotifications() {
            try {
                const type=store('NotificationSettingsStore')?.getDesktopType?.();
                return type==='NEVER'?'disabled':['ALL','ONLY_MENTIONS'].includes(type)?'enabled':'unavailable';
            }catch{return 'unavailable';}
        }

        async function enableDesktopNotifications(args) {
            authorize(args);
            if(globalThis.Notification?.permission!=='granted')fail('notification_permission_required');
            const settings=store('NotificationSettingsStore');
            const before=desktopNotifications();
            authorize(args);
            if(before!=='disabled')return {state:before};
            const actions=props('setDesktopType','setTTSType');
            if(typeof actions?.setDesktopType!=='function')return {state:'unavailable'};
            authorize(args);
            if(store('NotificationSettingsStore')!==settings||desktopNotifications()!=='disabled')return {state:desktopNotifications()};
            // This is the explicit Enable notifications action, never a snapshot
            // side effect. Preserve existing ALL / ONLY_MENTIONS preferences.
            await actions.setDesktopType('ALL');
            authorize(args);
            const state=store('NotificationSettingsStore')===settings?desktopNotifications():'unavailable';
            authorize(args);
            return {state};
        }

        function outgoingViewers(own,user,id,participants) {
            try {
                if(!own||!user||!id||own.state==='ENDED'||own.channelId!==id||(own.ownerId||own.userId)!==user.id)return null;
                const key=streamKey(own),viewers=store('ApplicationStreamingStore')?.getViewerIds?.(key);
                if(!Array.isArray(viewers)||viewers.length>1000||viewers.some(value=>typeof value!=='string'||!/^[0-9]{1,32}$/.test(value)))return null;
                const present=new Set(participants.map(person=>person.id));
                const userIds=[...new Set(viewers)].filter(value=>value!==user.id&&present.has(value));
                if(me()?.id!==user.id||channelId()!==id||currentShare()!==own||own.state==='ENDED')return null;
                return {key,userIds};
            }catch{return null;}
        }

        function snapshot() {
            const relay=relays.values().next().value;
            const user = me();
            const id = channelId();
            if(shareErrorOwner&&(shareErrorOwner.account!==user?.id||shareErrorOwner.channel!==id)){shareError=null;shareErrorOwner=null;}
            const media = store('MediaEngineStore');
            const own = currentShare();
            const participants = Object.values(store('VoiceStateStore')?.getVoiceStatesForChannel?.(id) ?? {})
                .slice(0, 100).map(v => {
                    const participant=store('UserStore')?.getUser?.(v.userId);
                    return {id:v.userId,name:text(participant?.globalName||participant?.username||'Participant'),
                        muted:!!(v.mute||v.selfMute),deafened:!!(v.deaf||v.selfDeaf),
                        speaking:!!store('SpeakingStore')?.isSpeaking?.(v.userId),
                        audio:v.userId!==user?.id?voiceAudio(v.userId)?.state??{available:false,muted:false,volume:null}:{available:false,muted:false,volume:null},
                        avatarUrl:activities?.avatarUrl(v.userId,participant?.avatar,participant?.discriminator)??null};
                });
            const privateCalls=calls?.snapshot()??null;
            return { revision, ready: !!user && !!wp(), connectionState:connectionState(user), account: user?.id ?? '', capabilities: capabilities(), messaging:messaging?.state()??null,
                calls:privateCalls,
                channels: user ? channels() : [], channelId: id, channelName: text(store('ChannelStore')?.getChannel?.(id)?.name||privateCalls?.current?.name),
                muted: !!media?.isSelfMute?.(), deafened: !!media?.isSelfDeaf?.(), participants,
                streams: id ? streams(id).map(({ descriptor, ...value }) => ({...value,watching:watchOwnership(value.key)?.alreadyWatching===true,audio:streamAudio(value.userId)?.state??
                    {available:false,muted:false,volume:null}})) : [], sharing: !!own && own.state !== 'ENDED',
                outgoingViewers:outgoingViewers(own,user,id,participants),
                desktopNotifications:desktopNotifications(),
                notificationPermission:globalThis.Notification?.permission ?? "unavailable",
                ...shareQualitySnapshot(user?.id,id),voiceConnection:voiceConnection(),
                sharePending,shareError:shareErrorOwner?.account===user?.id&&shareErrorOwner?.channel===id?shareError:null,
                shareQualityWarning:qualityWarningForSnapshot(), relayState: relay?.viewId ? 'connected' : relay?.pc.connectionState ?? 'closed',
                ...(activities?.snapshot()??{activities:[],activityAvailability:'unavailable',activitiesTruncated:false}) };
        }

        function authorize(args) {
            if (!me() || args?.account !== me().id) fail('account_changed');
        }
        function sameCall(args) {
            authorize(args);
            if (!args.channelId || channelId() !== args.channelId) fail('call_changed');
        }

        function voiceConnection() {
            try {
                const account=me()?.id,selected=channelId(),rtc=store('RTCConnectionStore');
                if(typeof account!=='string'||typeof rtc?.getState!=='function'||typeof rtc?.getChannelId!=='function')return 'unavailable';
                const phase=rtc.getState(),actual=rtc.getChannelId();
                if(me()?.id!==account||channelId()!==selected)return 'unavailable';
                if(phase==='DISCONNECTED')return 'disconnected';
                if(!selected||actual!==selected)return 'unavailable';
                if(phase==='RTC_CONNECTED')return 'connected';
                if(!['AWAITING_ENDPOINT','AUTHENTICATING','CONNECTING','RTC_DISCONNECTED','RTC_CONNECTING','NO_ROUTE','ICE_CHECKING','DTLS_CONNECTING'].includes(phase))return 'unavailable';
                return rtc.getWasEverRtcConnected?.()===true?'reconnecting':'connecting';
            }catch{return 'unavailable';}
        }

        function connectionDiagnostics(args) {
            sameCall(args);
            const rtc=store('RTCConnectionStore'),selected=channelId();
            if(typeof rtc?.getChannelId!=='function'||rtc.getChannelId()!==selected)
                return {phase:'unavailable',pingMilliseconds:null,averagePingMilliseconds:null,quality:null};
            const connection=rtc.getRTCConnection?.();
            const phase=voiceConnection();
            const ping=method=>{
                if(phase!=='connected'||typeof rtc[method]!=='function')return null;
                try{const value=rtc[method]();return Number.isFinite(value)&&value>=0&&value<=60000?value:null;}catch{return null;}
            };
            const pingMilliseconds=ping('getLastPing'),averagePingMilliseconds=ping('getAveragePing');
            let observed=null;
            try{if(phase==='connected')observed=rtc.getQuality?.();}catch{}
            sameCall(args);
            if(store('RTCConnectionStore')!==rtc||rtc.getChannelId()!==selected||rtc.getRTCConnection?.()!==connection)
                fail('connection_diagnostics_stale');
            return {phase,pingMilliseconds,averagePingMilliseconds,
                quality:['bad','average','fine'].includes(observed)?observed:null};
        }

        async function incomingDiagnostics(args,owner) {
            sameCall(args);
            const entry=byLease(args.lease),rtc=entry?.demand?.connection,connection=rtc?._connection;
            const connections=store('StreamRTCConnectionStore'),engine=store('MediaEngineStore')?.getMediaEngine?.();
            const exact=()=>entry&&relayCurrent(entry)&&entry.owner===owner&&entry.lease===args.lease&&entry.key===args.key&&
                entry.account===args.account&&entry.channelId===args.channelId&&entry.demand?.connection===rtc&&
                streams(args.channelId).some(stream=>stream.key===entry.key)&&watchOwnership(entry.key)?.alreadyWatching===true&&
                connections?.getRTCConnection?.(entry.key)===rtc&&store('StreamRTCConnectionStore')===connections&&
                rtc?._connection===connection&&rtc._destroyed===false&&rtc.isOwner===false&&rtc.context==='stream'&&rtc.streamKey===entry.key&&
                rtc.streamContext?.ownerId===entry.userId&&rtc.streamContext?.channelId===entry.channelId&&
                connection?.context==='stream'&&connection.streamUserId===entry.userId&&
                store('MediaEngineStore')?.getMediaEngine?.()===engine&&
                (typeof engine?.connections?.has==='function'?engine.connections.has(connection):
                    Array.isArray(engine?.connections)&&engine.connections.length<=100&&engine.connections.includes(connection));
            if(!exact())fail('incoming_diagnostics_stale');
            if(typeof connection.getRawStats!=='function')return {available:false};
            if(incomingDiagnosticsPending)fail('incoming_diagnostics_busy');
            const read=connection.getRawStats,pending={};incomingDiagnosticsPending=pending;
            const observed=Promise.resolve().then(()=>read.call(connection));
            const release=()=>{if(incomingDiagnosticsPending===pending)incomingDiagnosticsPending=null;};
            observed.then(release,release);
            const timedOut={};let timer,raw;
            try{raw=await Promise.race([observed,new Promise(resolve=>timer=setTimeout(()=>resolve(timedOut),1500))]);}
            finally{clearTimeout(timer);}
            if(raw===timedOut)fail('incoming_diagnostics_timeout');
            sameCall(args);
            if(!exact()||connection.getRawStats!==read)fail('incoming_diagnostics_stale');
            if(!raw||!Number.isSafeInteger(raw.size)||raw.size<0||raw.size>256||typeof raw.values!=='function')return {available:false};
            const bounded=(value,max)=>Number.isFinite(value)&&value>=0&&value<=max?value:null;
            const counter=value=>Number.isSafeInteger(value)?bounded(value,1000000000000):null;
            let selected=null,inspected=0,count=0;
            for(const report of raw.values()) {
                if(++inspected>256)fail('incoming_diagnostics_unavailable');
                if(report?.type!=='inbound-rtp'||(report.kind??report.mediaType)!=='video'||report.isRemote===true)continue;
                if(++count>4)fail('incoming_diagnostics_unavailable');
                const value={available:true,width:counter(report.frameWidth),height:counter(report.frameHeight),
                    framesPerSecond:bounded(report.framesPerSecond,240),framesReceived:counter(report.framesReceived),
                    framesDecoded:counter(report.framesDecoded),bytesReceived:counter(report.bytesReceived)};
                if(value.width>16384||value.height>16384)fail('incoming_diagnostics_unavailable');
                if(!selected||(value.width??0)*(value.height??0)>(selected.width??0)*(selected.height??0))selected=value;
            }
            if(!exact())fail('incoming_diagnostics_stale');
            return selected??{available:false};
        }

        function voiceAudio(userId) {
            try {
                const media=store('MediaEngineStore'),actions=props('setLocalVolume','toggleLocalMute');
                if(typeof media?.isLocalMute!=='function'||typeof media?.getLocalVolume!=='function'||
                    typeof actions?.toggleLocalMute!=='function'||typeof actions?.setLocalVolume!=='function'||
                    typeof media.isSupported==='function'&&media.isSupported()===false)return null;
                const muted=media.isLocalMute(userId,'default'),gain=media.getLocalVolume(userId,'default');
                if(typeof muted!=='boolean'||!Number.isFinite(gain)||gain<0||gain>200)return null;
                // The ordinary browser volume range is 0..100 perceptual percent.
                // Preserve saved desktop boosts until the user actually changes it.
                return {actions,state:{available:true,muted,volume:100*Math.pow(Math.min(gain,100)/100,1/2.8)}};
            }catch{return null;}
        }
        function voiceParticipant(args) {
            sameCall(args);
            if(typeof args.userId!=='string'||!/^[0-9]{1,32}$/.test(args.userId)||args.userId===args.account||
                !Object.values(store('VoiceStateStore')?.getVoiceStatesForChannel?.(args.channelId)??{}).slice(0,100).some(p=>p?.userId===args.userId))
                fail('voice_participant_unavailable');
        }
        function updateVoiceAudio(method,args) {
            voiceParticipant(args);
            if(method==='voiceMute'?typeof args.value!=='boolean':!Number.isFinite(args.value)||args.value<0||args.value>100)fail('voice_audio_value_invalid');
            const audio=voiceAudio(args.userId);if(!audio)fail('voice_audio_unavailable');
            voiceParticipant(args);
            if(method==='voiceMute'){
                if(audio.state.muted!==args.value)audio.actions.toggleLocalMute(args.userId,'default');
            }else if(Math.abs(audio.state.volume-args.value)>1e-8)audio.actions.setLocalVolume(args.userId,100*Math.pow(args.value/100,2.8),'default');
            voiceParticipant(args);
            const observed=voiceAudio(args.userId)?.state;voiceParticipant(args);
            if(!observed||(method==='voiceMute'?observed.muted!==args.value:
                Math.abs(observed.volume-args.value)>1e-4&&!(args.value>99&&Math.abs(observed.volume-100)<1e-4)))fail('voice_audio_unconfirmed');
            return {audio:observed};
        }
        function voiceSettings(args) {
            authorize(args);
            const media=store('MediaEngineStore'),actions=props('setInputDevice','setOutputDevice');
            const deviceId=value=>typeof value==='string'&&value.length>0&&value.length<=256&&!/[\u0000-\u001f\u007f-\u009f]/.test(value);
            function devices(kind) {
                const empty={available:false,devices:[],selected:null};
                try {
                    const list=kind==='Input'?'getInputDevices':'getOutputDevices',getter='get'+kind+'DeviceId',setter='set'+kind+'Device';
                    if(typeof media?.[list]!=='function'||typeof media?.[getter]!=='function'||typeof actions?.[setter]!=='function'||
                        typeof media.supports!=='function'||media.supports(kind==='Input'?'AUDIO_INPUT_DEVICE':'AUDIO_OUTPUT_DEVICE')!==true)return empty;
                    const raw=media[list](),selected=media[getter]();
                    if(!raw||typeof raw!=='object'||Array.isArray(raw)||Object.keys(raw).length>64)return empty;
                    const entries=Object.entries(raw);
                    if(entries.some(([key,d])=>!deviceId(key)||!d||d.id!==key||typeof d.name!=='string'||typeof d.disabled!=='boolean'))return empty;
                    const values=entries.filter(([,d])=>!d.disabled).map(([id,d])=>({id,name:text(d.name)}));
                    if(!deviceId(selected)||!values.some(d=>d.id===selected))return empty;
                    return {available:true,devices:values,selected};
                }catch{return empty;}
            }
            const input=devices('Input'),output=devices('Output');authorize(args);
            // Browser MediaEngine.setInputVolume is a no-op (725999.js). Do not
            // show a slider that only changes a saved number without changing audio.
            return {inputAvailable:input.available,outputAvailable:output.available,inputVolumeAvailable:false,
                inputDevices:input.devices,outputDevices:output.devices,inputDeviceId:input.selected,outputDeviceId:output.selected,inputVolume:null};
        }
        function updateVoiceSettings(method,args) {
            authorize(args);
            if(method==='voiceInputVolume')fail('voice_setting_unavailable');
            const input=method==='voiceInputDevice',before=voiceSettings(args);
            const devices=input?before.inputDevices:before.outputDevices,available=input?before.inputAvailable:before.outputAvailable;
            if(!available)fail('voice_setting_unavailable');
            if(typeof args.value!=='string'||!devices.some(d=>d.id===args.value))fail('voice_device_unavailable');
            const actions=props('setInputDevice','setOutputDevice'),setter=input?'setInputDevice':'setOutputDevice';
            if(typeof actions?.[setter]!=='function')fail('voice_setting_unavailable');
            authorize(args);
            if((input?before.inputDeviceId:before.outputDeviceId)!==args.value)actions[setter](args.value);
            const observed=voiceSettings(args);
            if((input?observed.inputDeviceId:observed.outputDeviceId)!==args.value)fail('voice_setting_unconfirmed');
            return observed;
        }

        const captureResolutions=[480,720,1080],captureFrameRates=[15,30,60];
        const isCaptureQuality=quality=>quality&&captureResolutions.includes(quality.resolution)&&captureFrameRates.includes(quality.frameRate)&&
            (quality.resolution!==1080||quality.frameRate!==60);
        function shareQualityPolicy(expectedAccount=me()?.id,expectedChannel=channelId()) {
            const baseline=[{resolution:480,frameRate:15},{resolution:480,frameRate:30},
                {resolution:720,frameRate:15},{resolution:720,frameRate:30}];
            let preferred=null;
            const fallback=()=>{
                let sameOwner=false;try{sameOwner=me()?.id===expectedAccount&&channelId()===expectedChannel;}catch{}
                return {qualities:baseline,availability:'unavailable',preferred:sameOwner?preferred:null};
            };
            try {
                const user=me(),selected=channelId(),channel=store('ChannelStore')?.getChannel?.(selected);
                const canStream=code('canStreamWithSettings'),presets=props('PRESET_CUSTOM','PRESET_AUTO');
                const update=code('"STREAM_UPDATE_SETTINGS"'),settings=store('ApplicationStreamingSettingsStore');
                if(user?.id!==expectedAccount||selected!==expectedChannel)return fallback();
                if(Number.isSafeInteger(presets?.PRESET_CUSTOM)&&typeof settings?.getState==='function') {
                    const saved=settings.getState();
                    const resolution=saved?.resolution===1440?1080:saved?.resolution;
                    const frameRate=resolution===1080&&saved?.fps===60?30:saved?.fps;
                    const candidate={resolution,frameRate};
                    if(saved?.preset===presets.PRESET_CUSTOM&&isCaptureQuality(candidate))preferred=candidate;
                }
                if(!user?.id||!selected||channel?.id!==selected||typeof canStream!=='function'||typeof update!=='function'||
                    typeof settings?.getState!=='function'||!Number.isSafeInteger(presets?.PRESET_CUSTOM))return fallback();
                const tier=channel.guild_id?store('GuildStore')?.getGuild?.(channel.guild_id)?.premiumTier:undefined;
                const qualities=[];
                // Use Discord's custom-quality policy for every pair, including
                // baseline. It owns account/server entitlement; no tier arithmetic
                // or premium bypass is duplicated here. SOURCE/4K is not offered.
                for(const resolution of captureResolutions)for(const frameRate of captureFrameRates) {
                    if(!isCaptureQuality({resolution,frameRate}))continue;
                    const allowed=canStream(presets.PRESET_CUSTOM,resolution,frameRate,user,tier,channel);
                    if(typeof allowed!=='boolean')return fallback();
                    if(allowed)qualities.push({resolution,frameRate});
                }
                if(me()?.id!==user.id||channelId()!==selected){preferred=null;return fallback();}
                return {qualities,availability:'ready',preferred,update,settings,preset:presets.PRESET_CUSTOM};
            }catch{return fallback();}
        }

        function shareQualitySnapshot(account,channel) {
            const policy=shareQualityPolicy(account,channel);
            return {shareQualities:policy.qualities,shareQualityAvailability:policy.availability,preferredShareQuality:policy.preferred};
        }

        function requireShareQuality(quality,args) {
            const policy=shareQualityPolicy(args.account,args.channelId);
            if(!policy.qualities.some(q=>q.resolution===quality.resolution&&q.frameRate===quality.frameRate))
                fail('share_quality_unsupported');
            return policy;
        }

        function peekDesktopSource(engine,source) {
            // DesktopInputPool.get is a destructive ownership transfer, not a
            // Map lookup. Observe the current browser pool's data slot only;
            // Discord must remain the sole consumer during source attachment.
            const pool=engine.desktopInputPool?.pool;
            if(!pool||typeof pool!=='object')return undefined;
            return Object.getOwnPropertyDescriptor(pool,source)?.value;
        }
        function setQualityWarning(code,args,engine,source,epoch,capturedSource=null,check=null) {
            let sourceObject=null,stream=null;
            try{sourceObject=capturedSource??peekDesktopSource(engine,source)??null;stream=currentShare()??null;}catch{}
            shareQualityWarning={code,account:args.account,channel:args.channelId,engine,source,epoch,created:Date.now(),
                pool:engine.desktopInputPool,sourceObject,stream,sourceObserved:false,check};
        }
        function qualityWarningForSnapshot() {
            const warning=shareQualityWarning;if(!warning)return null;
            const clear=()=>{if(shareQualityWarning===warning)shareQualityWarning=null;return null;};
            try {
                const media=store('MediaEngineStore'),own=currentShare(),queued=peekDesktopSource(warning.engine,warning.source);
                if(warning.epoch!==shareEpoch||me()?.id!==warning.account||channelId()!==warning.channel||media?.getMediaEngine?.()!==warning.engine||
                    warning.engine.desktopInputPool!==warning.pool||warning.sourceObject&&queued!==undefined&&queued!==warning.sourceObject)return clear();
                const track=warning.sourceObject?.stream?.getVideoTracks?.()[0];
                if(track&&track.readyState!=='live')return clear();
                const selected=media?.getGoLiveSource?.()?.desktopSource?.id;
                if(selected!=null){if(selected!==warning.source)return clear();warning.sourceObserved=true;}
                else if(warning.sourceObserved)return clear();
                if(warning.stream!==null){if(own!==warning.stream||own.state==='ENDED')return clear();}
                else if(own){
                    if(own.state==='ENDED'||own.channelId!==warning.channel||(own.ownerId||own.userId)!==warning.account)return clear();
                    warning.stream=own;
                }else if(Date.now()-warning.created>15000)return clear();
                return warning.code;
            }catch{return clear();}
        }

        function bindOutgoingQuality(args,quality,engine,source,epoch) {
            const sourcePool=engine.desktopInputPool,selected=peekDesktopSource(engine,source),track=selected?.stream?.getVideoTracks?.()[0];
            if(!selected||track?.kind!=='video'||typeof track.getConstraints!=='function'||
                typeof engine.on!=='function'||typeof engine.removeListener!=='function'||typeof engine.connections?.has!=='function')fail('share_quality_unavailable');
            const selectedConstraints=track.getConstraints();
            const bound=value=>typeof value==='number'?value:value?.ideal??value?.max;
            const width=bound(selectedConstraints?.width),height=bound(selectedConstraints?.height);
            const maximumWidth=({480:1706,720:2560,1080:3840})[quality.resolution];
            if(!Number.isInteger(width)||!Number.isInteger(height)||width<2||height<2||width>maximumWidth||height>quality.resolution)
                fail('share_quality_unavailable');
            const wrappers=new Map();let closed=false,applied=false,timer,ownedStream=null;
            const streamWrappers=new Map();
            let contentHint=null,contentPreferenceUnavailable=false;
            try{
                const native=globalThis.WidgetRailNativeMedia?.status?.();
                if(native?.active===true){
                    if(['detail','motion'].includes(native.contentPreference)&&track.contentHint===native.contentPreference)
                        contentHint=native.contentPreference;
                    else contentPreferenceUnavailable=true;
                }
            }catch{contentPreferenceUnavailable=true;}
            const key=(store('ChannelStore')?.getChannel?.(args.channelId)?.guild_id)
                ?`guild:${store('ChannelStore').getChannel(args.channelId).guild_id}:${args.channelId}:${args.account}`:`call:${args.channelId}:${args.account}`;
            const current=()=>{try{const queued=peekDesktopSource(engine,source);return !closed&&epoch===shareEpoch&&me()?.id===args.account&&channelId()===args.channelId&&
                store('MediaEngineStore')?.getMediaEngine?.()===engine&&engine.desktopInputPool===sourcePool&&
                (queued===undefined||queued===selected)&&selected.stream?.getVideoTracks?.()[0]===track&&track.readyState==='live'&&
                (ownedStream===null||currentShare()===ownedStream&&ownedStream.state!=='ENDED');}catch{return false;}};
            const restore=connection=>{
                const held=wrappers.get(connection);if(!held)return;
                wrappers.delete(connection);
                if(connection.setDesktopInput!==held.wrapper)return;
                try{if(held.descriptor)Object.defineProperty(connection,'setDesktopInput',held.descriptor);else delete connection.setDesktopInput;}catch{}
            };
            const dispose=()=>{
                if(closed)return;closed=true;clearTimeout(timer);
                try{engine.removeListener('connection',created);}catch{}
                for(const connection of [...wrappers.keys()])restore(connection);
                for(const [connection,held] of streamWrappers){
                    if(connection.setStream!==held.wrapper)continue;
                    try{if(held.descriptor)Object.defineProperty(connection,'setStream',held.descriptor);else delete connection.setStream;}catch{}
                }
                streamWrappers.clear();
                if(shareQualityBinding===binding)shareQualityBinding=null;
            };
            const markUnavailable=(check='attachment-timeout')=>{if(current())setQualityWarning('share_quality_unconfirmed',args,engine,source,epoch,selected,check);dispose();};
            const contentUnavailable=()=>{if(current())setQualityWarning('share_content_preference_unconfirmed',args,engine,source,epoch,selected);};
            function bindContentHint(connection,rtc) {
                if(contentPreferenceUnavailable){contentUnavailable();return;}
                if(contentHint===null||streamWrappers.has(connection))return;
                if(streamWrappers.size>=4){contentUnavailable();return;}
                const original=connection.setStream,descriptor=Object.getOwnPropertyDescriptor(connection,'setStream');
                if(typeof original!=='function'){contentUnavailable();return;}
                const exact=()=>current()&&connection.destroyed===false&&engine.connections.has(connection)&&
                    store('StreamRTCConnectionStore')?.getRTCConnection?.(key)===rtc&&rtc._connection===connection&&rtc._destroyed===false&&
                    rtc.isOwner===true&&rtc.streamContext?.ownerId===args.account&&rtc.streamContext?.channelId===args.channelId&&
                    connection.input?.desktop===selected&&selected.stream?.getVideoTracks?.()[0]===track;
                const wrapper=function(stream){
                    // Discord's browser setStream synchronously overwrites the
                    // hint under its motion experiment. Preserve only this
                    // host-selected track, after the provider has attached it.
                    const result=original.apply(this,arguments);
                    if(this!==connection)return result;
                    try{
                        if(!current()){dispose();return result;}
                        const attached=stream?.getVideoTracks?.()[0];
                        if(attached!==track){
                            if(attached&&exact()&&connection.streamVideoTrack===attached)contentUnavailable();
                            return result;
                        }
                        if(!exact()||(result&&typeof result.then==='function')||connection.streamVideoTrack!==track){contentUnavailable();return result;}
                        track.contentHint=contentHint;
                        if(track.contentHint!==contentHint)contentUnavailable();
                    }catch{contentUnavailable();}
                    return result;
                };
                try{Object.defineProperty(connection,'setStream',{value:wrapper,writable:true,configurable:true});streamWrappers.set(connection,{wrapper,descriptor});}
                catch{contentUnavailable();}
            }
            function created(connection) {
                if(!current()){dispose();return;}
                if(connection?.context!=='stream'||connection.userId!==args.account||connection.streamUserId!==args.account||
                    !engine.connections.has(connection))return;
                if(wrappers.has(connection))return;
                if(wrappers.size>=4||typeof connection.setDesktopInput!=='function'){markUnavailable();return;}
                const original=connection.setDesktopInput,descriptor=Object.getOwnPropertyDescriptor(connection,'setDesktopInput');
                const wrapper=function(input){
                    if(this!==connection||input!==selected)return original.apply(this,arguments);
                    restore(connection);
                    let check='provider-exception';
                    const requireCheck=(condition,name)=>{if(!condition){check=name;fail('share_quality_unconfirmed');}};
                    try {
                        // These stores may load only while the first stream is
                        // being constructed. Inspect loaded exports at attachment;
                        // never execute unknown module factories to force discovery.
                        const media=store('MediaEngineStore'),connections=store('StreamRTCConnectionStore'),constants=props('SOURCE','FIXED');
                        const own=currentShare(),rtc=connections?.getRTCConnection?.(key),manager=connection.videoQualityManager;
                        requireCheck(current(),'source-owner');
                        requireCheck(own&&own.state!=='ENDED'&&streamKey(own)===key,'stream-owner');
                        requireCheck(rtc?.context==='stream'&&rtc.streamKey===key,'rtc-identity');
                        requireCheck(rtc._destroyed===false&&rtc.isOwner===true,'rtc-owner');
                        requireCheck(rtc._connection===connection,'rtc-connection');
                        requireCheck(rtc.streamContext?.ownerId===args.account&&rtc.streamContext?.channelId===args.channelId,'rtc-context');
                        requireCheck(connection.destroyed===false,'connection-live');
                        requireCheck(media?.getGoLiveSource?.()?.desktopSource?.id===source,'selected-source');
                        requireCheck(constants?.FIXED==='fixed'&&constants?.SOURCE==='source','resolution-mode');
                        requireCheck(manager?.connection===connection&&manager.contextType==='stream'&&typeof manager.setGoliveQuality==='function','quality-manager');
                        const parameters=connection.videoStreamParameters;
                        requireCheck(Array.isArray(parameters)&&parameters.length>=1&&parameters.length<=4&&
                            parameters.filter(p=>p?.quality===100).length===1&&!parameters.some(p=>!p||typeof p!=='object'),'primary-encoding');
                        check='quality-eligibility';
                        const policy=requireShareQuality(quality,args);
                        requireCheck(!policy.update||(policy.preferred?.resolution===quality.resolution&&policy.preferred?.frameRate===quality.frameRate),'quality-preference');
                        requireCheck(current(),'source-owner');
                        // Configure this connection before setDesktopInput emits its
                        // first Video event. No global/default quality patch and no
                        // timer later rewrites an unrelated stream connection.
                        check='quality-apply';
                        manager.setGoliveQuality({capture:{width,height,framerate:quality.frameRate},encode:{width,height,framerate:quality.frameRate}});
                        requireCheck(current()&&manager.goliveMaxQuality?.capture?.width===width&&manager.goliveMaxQuality?.capture?.height===height&&
                            manager.goliveMaxQuality?.capture?.framerate===quality.frameRate&&manager.goliveMaxQuality?.encode?.framerate===quality.frameRate,'quality-readback');
                        check='quality-advertisement';
                        const primary=parameters.find(p=>p.quality===100);
                        primary.maxFrameRate=quality.frameRate;
                        primary.maxResolution={type:constants.FIXED,width,height};
                        ownedStream=own;
                        applied=true;shareQualityWarning=null;clearTimeout(timer);
                        bindContentHint(connection,rtc);
                    }catch{markUnavailable(check);}
                    return original.apply(this,arguments);
                };
                try{Object.defineProperty(connection,'setDesktopInput',{value:wrapper,writable:true,configurable:true});wrappers.set(connection,{wrapper,descriptor});}
                catch{markUnavailable();}
            }
            const binding={dispose,current,observe:()=>({active:current(),applied,pending:wrappers.size,contentHooks:streamWrappers.size})};
            shareQualityBinding?.dispose();shareQualityBinding=binding;
            try{engine.on('connection',created);timer=setTimeout(()=>{if(!applied)markUnavailable();},15000);}
            catch{dispose();fail('share_quality_unavailable');}
            return binding;
        }

        function videoRateLimits(description,mid,payload) {
            const sdp=description?.sdp;
            if(typeof sdp!=='string'||sdp.length>131072||!Number.isInteger(payload)||payload<0||payload>127)return {};
            const video=sdp.split(/\r?\nm=/).filter(section=>{
                const lines=section.split(/\r?\n/);
                const header=lines[0].split(' ');
                return header[0]==='video'&&header.slice(3).includes(String(payload))&&
                    (mid==null||lines.includes('a=mid:'+mid));
            });
            if(video.length!==1)return {};
            const prefix='a=fmtp:'+payload+' ';
            const videoLines=video[0].split(/\r?\n/);
            const codecLines=videoLines.filter(line=>line.startsWith(prefix));
            const fmtp=codecLines.length===1?codecLines[0]:null;
            const match=fmtp?.slice(prefix.length).match(/(?:^|;)\s*x-google-max-bitrate\s*=\s*(\d{1,6})\s*(?:;|$)/i);
            const bitsPerSecond=Number(match?.[1])*1000;
            const profile=fmtp?.slice(prefix.length).match(/(?:^|;)\s*profile-level-id\s*=\s*42e0([0-9a-f]{2})(?=;|$)/i);
            const h264Level=profile?parseInt(profile[1],16):null;
            const mediaLines=video[0].match(/^b=TIAS:(\d+)\r?$/gm);
            const media=mediaLines?.length===1?Number(mediaLines[0].slice(7)):null;
            const otherBandwidth=/^b=/m.test(sdp.split(/\r?\nm=/,1)[0])||
                videoLines.some(line=>line.startsWith('b=')&&!/^b=TIAS:\d+$/.test(line))||
                (mediaLines?.length??0)>1||(media!==null&&!(media>0&&media<=100_000_000))||
                /(?:^|;)\s*max-br\s*=/i.test(fmtp?.slice(prefix.length)??'');
            return {hint:bitsPerSecond>0&&bitsPerSecond<=100_000_000?bitsPerSecond:null,
                media:media>0&&media<=100_000_000?media:null,h264Level,otherBandwidth};
        }

        async function streamDiagnostics(args) {
            try {
                sameCall(args);
                if(diagnosticsPending)fail('stream_diagnostics_busy');
                const own=currentShare(),generation=shareEpoch;
                if(!own||own.state==='ENDED'||own.channelId!==args.channelId||(own.ownerId||own.userId)!==args.account)
                    fail('stream_diagnostics_unavailable');
                const key=streamKey(own),connections=store('StreamRTCConnectionStore');
                const rtc=connections?.getRTCConnection?.(key),connection=rtc?._connection;
                const engine=store('MediaEngineStore')?.getMediaEngine?.();
                const exact=()=>shareEpoch===generation&&me()?.id===args.account&&channelId()===args.channelId&&
                    currentShare()===own&&own.state!=='ENDED'&&streamKey(own)===key&&own.channelId===args.channelId&&
                    (own.ownerId||own.userId)===args.account&&connections?.getRTCConnection?.(key)===rtc&&
                    rtc?.context==='stream'&&rtc.streamKey===key&&rtc._destroyed===false&&rtc.isOwner===true&&
                    rtc.streamContext?.ownerId===args.account&&rtc.streamContext?.channelId===args.channelId&&
                    rtc._connection===connection&&connection?.context==='stream'&&connection.userId===args.account&&
                    connection.streamUserId===args.account&&store('MediaEngineStore')?.getMediaEngine?.()===engine&&
                    typeof engine?.connections?.has==='function'&&engine.connections.has(connection);
                if(!exact()||typeof connection.getRawStats!=='function'||typeof connection.getStreamParameters!=='function')
                    fail('stream_diagnostics_unavailable');
                const readStats=connection.getRawStats,readParameters=connection.getStreamParameters;
                // getStats has no cancellation API. Keep one outstanding read
                // even after our response deadline, rather than accumulate a
                // new unresolved provider promise for each button press.
                const pending={};diagnosticsPending=pending;
                const observed=Promise.resolve().then(()=>readStats.call(connection));
                const release=()=>{if(diagnosticsPending===pending)diagnosticsPending=null;};
                observed.then(release,release);
                const timedOut={};let timer,raw;
                try{raw=await Promise.race([observed,new Promise(resolve=>timer=setTimeout(()=>resolve(timedOut),1500))]);}
                finally{clearTimeout(timer);}
                if(raw===timedOut)fail('stream_diagnostics_timeout');
                sameCall(args);
                if(!exact()||connection.getRawStats!==readStats||connection.getStreamParameters!==readParameters)
                    fail('stream_diagnostics_stale');
                if(!raw||!Number.isSafeInteger(raw.size)||raw.size<0||raw.size>256||typeof raw.values!=='function')
                    fail('stream_diagnostics_unavailable');
                const dimension=value=>Number.isInteger(value)&&value>=2&&value<=8192?value:null;
                const rate=value=>Number.isFinite(value)&&value>=0&&value<=240?value:null;
                const bitrate=value=>Number.isFinite(value)&&value>0&&value<=100_000_000?value:null;
                const count=value=>Number.isSafeInteger(value)&&value>=0?value:null;
                const pc=connection.pc;
                const outbound=[];let inspected=0;
                for(const report of raw.values()) {
                    if(++inspected>256)fail('stream_diagnostics_unavailable');
                    if(report?.type!=='outbound-rtp'||(report.kind??report.mediaType)!=='video'||report.isRemote===true)continue;
                    if(outbound.length===4)fail('stream_diagnostics_unavailable');
                    const codecReport=typeof raw.get==='function'?raw.get(report.codecId):null;
                    const codec=({'video/vp8':'VP8','video/vp9':'VP9','video/h264':'H264','video/av1':'AV1','video/h265':'H265'})[
                        typeof codecReport?.mimeType==='string'?codecReport.mimeType.toLowerCase():''];
                    const transport=raw.get?.(report.transportId);
                    const pair=raw.get?.(transport?.selectedCandidatePairId);
                    const target=bitrate(report.targetBitrate),available=bitrate(pair?.availableOutgoingBitrate);
                    const mid=report.mid??connection.videoTransceiver?.mid;
                    const local=videoRateLimits(pc?.localDescription,mid,codecReport?.payloadType);
                    const remote=videoRateLimits(pc?.remoteDescription,mid,codecReport?.payloadType);
                    const sourceReport=raw.get?.(report.mediaSourceId);
                    const sourceFps=rate(sourceReport?.framesPerSecond);
                    const cadence={};
                    for(const [name,value] of Object.entries({sourceFrames:sourceReport?.frames,framesEncoded:report.framesEncoded,
                        framesSent:report.framesSent,keyFramesEncoded:report.keyFramesEncoded,pliCount:report.pliCount,firCount:report.firCount,
                        nackCount:report.nackCount,bytesSent:report.bytesSent,packetsSent:report.packetsSent,
                        retransmittedBytesSent:report.retransmittedBytesSent})){
                        const observed=count(value);
                        if(observed!==null)cadence[name]=observed;
                    }
                    if(Number.isFinite(report.totalEncodeTime)&&report.totalEncodeTime>=0)cadence.totalEncodeTime=report.totalEncodeTime;
                    if(Number.isFinite(report.totalPacketSendDelay)&&report.totalPacketSendDelay>=0)
                        cadence.totalPacketSendDelay=report.totalPacketSendDelay;
                    if(typeof report.powerEfficientEncoder==='boolean')cadence.powerEfficientEncoder=report.powerEfficientEncoder;
                    outbound.push({width:dimension(report.frameWidth),height:dimension(report.frameHeight),framesPerSecond:rate(report.framesPerSecond),
                        qualityLimitationReason:['none','cpu','bandwidth','other'].includes(report.qualityLimitationReason)?report.qualityLimitationReason:null,
                        ...(target!==null?{targetBitrate:target}:{}),...(available!==null?{availableOutgoingBitrate:available}:{}),
                        ...(local.hint?{localCodecMax:local.hint}:{}),...(remote.hint?{remoteCodecMax:remote.hint}:{}),
                        ...(remote.media?{remoteMediaMax:remote.media}:{}),
                        ...(sourceFps!==null?{sourceFps}:{}),
                        ...(Object.keys(cadence).length?{cadence}:{}),
                        ...(codec?{codec}:{})});
                }
                const parameters=readParameters.call(connection);
                if(!Array.isArray(parameters)||parameters.length>4||parameters.some(item=>!item||typeof item!=='object'))
                    fail('stream_diagnostics_unavailable');
                const advertised=parameters.map(item=>({width:dimension(item.maxResolution?.width),height:dimension(item.maxResolution?.height),frameRate:rate(item.maxFrameRate)}));
                let viewerPixels=null;
                const primary=parameters.filter(item=>item.quality===100&&Number.isInteger(item.ssrc)&&item.ssrc>0);
                if(primary.length===1&&typeof connection.getRemoteVideoSinkPixelCount==='function'){
                    try{
                        const pixels=connection.getRemoteVideoSinkPixelCount(primary[0].ssrc);
                        if(Number.isSafeInteger(pixels)&&pixels>0&&pixels<=268435456)viewerPixels=pixels;
                    }catch{ /* Optional receiver demand is unavailable. */ }
                }
                const observedNative=typeof globalThis.WidgetRailNativeMedia?.status==='function'?globalThis.WidgetRailNativeMedia.status():null;
                const native=observedNative?.active===true?observedNative:null;
                const source={width:dimension(native?.width),height:dimension(native?.height),browserWidth:dimension(native?.browserWidth),
                    browserHeight:dimension(native?.browserHeight),targetFrameRate:rate(native?.targetFrameRate)};
                const desktopTrack=connection.input?.desktop?.stream?.getVideoTracks?.()[0];
                let senderInfo;
                const sender=connection.videoTransceiver?.sender;
                if(typeof sender?.getParameters==='function'){
                    const parameters=sender.getParameters();
                    if(parameters&&Array.isArray(parameters.encodings)&&parameters.encodings.length<=4){
                        const positive=(value,max)=>Number.isFinite(value)&&value>0&&value<=max?value:null;
                        senderInfo={degradationPreference:['maintain-framerate','maintain-resolution','balanced'].includes(parameters.degradationPreference)?parameters.degradationPreference:null,
                            ...(['detail','motion','text'].includes((sender.track??desktopTrack)?.contentHint)?{contentHint:(sender.track??desktopTrack).contentHint}:{}),
                            encodings:parameters.encodings.map(e=>({active:typeof e?.active==='boolean'?e.active:null,
                                maxBitrate:positive(e?.maxBitrate,100000000),maxFramerate:positive(e?.maxFramerate,240),
                                scaleResolutionDownBy:positive(e?.scaleResolutionDownBy,64)}))};
                    }
                    if(connection.videoTransceiver?.sender!==sender)fail('stream_diagnostics_stale');
                }
                if(!exact())fail('stream_diagnostics_stale');
                const track=connection.input?.stream?.getVideoTracks?.()[0];
                const trackState=value=>['live','ended'].includes(value?.readyState)?value.readyState:null;
                const attachment={nativeFrames:Number.isSafeInteger(native?.frames)&&native.frames>=0?native.frames:null,
                    videoSupported:typeof connection.videoSupported==='boolean'?connection.videoSupported:null,
                    inputTrack:trackState(track),desktopTrack:trackState(desktopTrack),senderTrack:trackState(sender?.track),
                    peerState:['new','connecting','connected','disconnected','failed','closed'].includes(pc?.connectionState)?pc.connectionState:null,
                    signalingState:['stable','have-local-offer','have-remote-offer','have-local-pranswer','have-remote-pranswer','closed'].includes(pc?.signalingState)?pc.signalingState:null,
                    qualityBinding:shareQualityBinding?.observe?.()??null,
                    qualityCheck:qualityWarningForSnapshot()?shareQualityWarning?.check??null:null};
                if(!exact())fail('stream_diagnostics_stale');
                const candidate=experimentalCapture?.sample(connection);
                const measured=candidate?.externalEncoder;
                const externalEncoder=measured?{allocated:measured.allocated,configured:measured.configured,
                    encodedBitrate:measured.encodedBitrate,ceiling:measured.ceiling,encoderPath:measured.encoderPath,
                    nativeFallback:measured.nativeFallback}:null;
                return {source,outbound,advertised,...(viewerPixels?{viewerPixels}:{}),...(senderInfo?{sender:senderInfo}:{}),
                    ...(externalEncoder?{externalEncoder}:{}),...(candidate?{encoderPath:candidate.encoderPath,
                        encoderFallback:candidate.encoderFallback,presetBitrateActive:candidate.presetBitrateActive}:{}),attachment};
            }catch(error) {
                if(['account_changed','call_changed','stream_diagnostics_unavailable','stream_diagnostics_busy','stream_diagnostics_timeout','stream_diagnostics_stale'].includes(error?.adapterCode))throw error;
                fail('stream_diagnostics_unavailable');
            }
        }

        function streamAudio(userId) {
            try {
                const media=store('MediaEngineStore'),actions=props('setLocalVolume','toggleLocalMute');
                if(typeof media?.isLocalMute!=='function'||typeof media?.getLocalVolume!=='function'||
                    typeof actions?.toggleLocalMute!=='function'||typeof actions?.setLocalVolume!=='function')return null;
                if(typeof media.isSupported==='function'&&media.isSupported()===false)return null;
                // Discord stores amplitude, not its displayed slider percentage.
                // Match its browser's 0..100 perceptual slider. A saved desktop
                // boost displays at that slider's ceiling without rewriting the
                // provider preference or changing the DEFAULT voice context.
                const muted=media.isLocalMute(userId,'stream'),gain=media.getLocalVolume(userId,'stream');
                if(typeof muted!=='boolean'||!Number.isFinite(gain)||gain<0||gain>200)return null;
                const volume=100*Math.pow(Math.min(gain,100)/100,1/2.8);
                return {actions,state:{available:true,muted,volume}};
            }catch{return null;}
        }

        function streamAudioTarget(args,owner) {
            sameCall(args);
            const current=byLease(args.lease);
            if(!current||current.owner!==owner||current.account!==args.account||current.channelId!==args.channelId||
                current.lease!==args.lease||current.key!==args.key||current.userId!==args.userId||
                Date.now()>current.expires||['closed','failed'].includes(current.pc?.connectionState))fail('watch_cancelled');
            if(!streams(args.channelId).some(s=>s.key===args.key&&s.userId===args.userId))fail('stream_ended');
            if(watchOwnership(args.key)?.alreadyWatching!==true)fail('watch_cancelled');
            sameCall(args);
            if(!relayCurrent(current))fail('watch_cancelled');
            return current;
        }

        function watchOwnership(key) {
            try {
                const account=me()?.id;
                const streaming=store('ApplicationStreamingStore');
                if(typeof account!=='string'||typeof streaming?.getActiveStreamForStreamKey!=='function')return null;
                // Discord retains rtcStreams.viewerIds after STREAM_CLOSE. Only
                // activeStreams describes whether this client is watching.
                const active=streaming.getActiveStreamForStreamKey(key);
                if(active!==null&&(typeof active!=='object'||!active||streamKey(active)!==key||
                    !['CONNECTING','ACTIVE','RECONNECTING','PAUSED','ENDED','FAILED'].includes(active.state)))return null;
                const alreadyWatching=active!==null&&!['ENDED','FAILED'].includes(active.state);
                const startWatch=code('"STREAM_WATCH",streamKey'),closeWatch=code('"STREAM_STOP",streamKey');
                if(!alreadyWatching&&(typeof startWatch!=='function'||typeof closeWatch!=='function'))return null;
                return {alreadyWatching,startWatch,closeWatch};
            }catch{return null;}
        }

        function watchDemandApi() {
            try {
                const connections=store('StreamRTCConnectionStore');
                const visibility=props('isIncomingVideoEnabled','lastIncomingVideoEnabledChangeTime');
                if(typeof connections?.getRTCConnection!=='function'||typeof visibility?.isIncomingVideoEnabled!=='function'||
                    typeof visibility.lastIncomingVideoEnabledChangeTime!=='function'||
                    typeof visibility.isIncomingVideoEnabled()!=='boolean')return null;
                return {connections,visibility};
            }catch{return null;}
        }

        function relayCurrent(entry) {
            return relays.get(entry.slot)===entry&&me()?.id===entry.account&&channelId()===entry.channelId&&Date.now()<=entry.expires&&
                !['closed','failed'].includes(entry.pc?.connectionState);
        }

        function refreshWatchDemand(entry) {
            const api=watchDemandApi();
            if(!api)fail('watch_demand_unavailable');
            const connection=api.connections.getRTCConnection(entry.key);
            const manager=connection?._goLiveQualityManager;
            const previous=entry.demand;
            if(previous&&(previous.connection!==connection||previous.manager!==manager||connection?._destroyed===true)) {
                entry.demand=null;previous.release();
            }
            // STREAM_WATCH establishes the connection asynchronously. Reuse the
            // existing bounded track wait; never instantiate provider internals.
            if(!connection||!manager||!connection._connection||connection._destroyed===true)return false;
            if(connection.context!=='stream'||connection.streamKey!==entry.key||connection._destroyed!==false||
                connection.isOwner!==false||connection.streamContext?.ownerId!==entry.userId||
                connection.streamContext?.channelId!==entry.channelId||
                connection._connection.context!=='stream'||connection._connection.streamUserId!==entry.userId||
                ![...(store('MediaEngineStore')?.getMediaEngine?.()?.connections??[])].includes(connection._connection))
                fail('watch_demand_unavailable');
            if(typeof manager.getUserID!=='function'||typeof manager.onIncomingVideoEnabled!=='function'||
                typeof manager.incomingVideoEnabled!=='boolean')fail('watch_demand_unavailable');
            const providerUser=manager.getUserID();
            if(providerUser==null)return false;
            if(providerUser!==entry.userId)fail('watch_demand_unavailable');
            if(entry.demand) {
                if(manager.onIncomingVideoEnabled!==entry.demand.wrapper)fail('watch_demand_replaced');
                return true;
            }
            const descriptor=Object.getOwnPropertyDescriptor(manager,'onIncomingVideoEnabled');
            if(descriptor?(!('value'in descriptor)||(!descriptor.configurable&&!descriptor.writable)):!Object.isExtensible(manager))
                fail('watch_demand_unavailable');
            const original=manager.onIncomingVideoEnabled;
            let active=true,lastPolicy=api.visibility.isIncomingVideoEnabled();
            if(typeof lastPolicy!=='boolean')fail('watch_demand_unavailable');
            const binding={connection,manager,entry,wrapper:null,release:null};
            const exact=()=>api.connections.getRTCConnection(binding.entry.key)===connection&&
                connection._goLiveQualityManager===manager&&connection._destroyed===false&&connection.isOwner===false&&
                connection.context==='stream'&&connection.streamKey===binding.entry.key&&
                connection.streamContext?.ownerId===binding.entry.userId&&connection.streamContext?.channelId===binding.entry.channelId&&
                manager.getUserID()===binding.entry.userId;
            // Only this Go Live manager gains an external viewer. Discord's
            // global visibility and emitter listener remain completely unchanged.
            binding.wrapper=function(enabled,...rest) {
                if(this!==manager||typeof enabled!=='boolean')return original.call(this,enabled,...rest);
                lastPolicy=enabled;
                let demanded=false;
                try { demanded=active&&relayCurrent(binding.entry)&&exact(); }catch{}
                return original.call(this,enabled||demanded,...rest);
            };
            binding.release=()=>{
                if(!active)return;active=false;
                // Another extension may have replaced our property. Its wrapper
                // must remain intact; a retained call through ours is now inert.
                if(Object.getOwnPropertyDescriptor(manager,'onIncomingVideoEnabled')?.value!==binding.wrapper)return;
                try {
                    if(descriptor)Object.defineProperty(manager,'onIncomingVideoEnabled',descriptor);
                    else Reflect.deleteProperty(manager,'onIncomingVideoEnabled');
                }catch{} // A provider-frozen property can retain only an inert wrapper.
                try {
                    if(!exact()||me()?.id!==binding.entry.account||channelId()!==binding.entry.channelId)return;
                    let policy=lastPolicy;
                    try { const current=api.visibility.isIncomingVideoEnabled();if(typeof current==='boolean')policy=current; }catch{}
                    original.call(manager,policy);
                }catch{} // A destroyed provider connection must not block relay retirement.
            };
            Object.defineProperty(manager,'onIncomingVideoEnabled',descriptor?{...descriptor,value:binding.wrapper}:
                {value:binding.wrapper,writable:true,configurable:true,enumerable:false});
            entry.demand=binding;
            try { binding.wrapper.call(manager,lastPolicy); }
            catch(error) { entry.demand=null;binding.release();throw error; }
            return true;
        }

        function retireRelay(previous,preserveSubscription=false) {
            if(!previous)return;
            clearInterval(previous.timer);
            try { previous.demand?.release(); }catch{}
            if(previous.viewId && previous.viewId!==relays.get(previous.slot)?.viewId)try{videoHost.setVideoTrack(previous.viewId,null);}catch{}
            try { previous.pc?.close(); }catch{}
            // A subscription that predates this adapter belongs to the user.
            if (!preserveSubscription && previous.ownsWatch && me()?.id === previous.account) {
                retiringWatches.set(previous.slot,{account:previous.account,channelId:previous.channelId,key:previous.key});
                try { previous.closeWatch(previous.key,false);
                    if(watchOwnership(previous.key)?.alreadyWatching===false)retiringWatches.delete(previous.slot); } catch { /* No blind retries. */ }
            }
        }

        async function awaitWatchRetirement(entry) {
            const deadline=Date.now()+8000;
            for(let i=0;i<80;i++) {
                if(!relayCurrent(entry))fail('watch_cancelled');
                let pending=false;
                for(const [slot,closed] of retiringWatches) {
                    if(closed.account!==entry.account||closed.channelId!==entry.channelId) {
                        retiringWatches.delete(slot);continue;
                    }
                    if(slot!==entry.slot&&closed.key!==entry.key)continue;
                    const ownership=watchOwnership(closed.key);
                    // Local watch removal acknowledges closure. Viewer IDs and
                    // RTC objects can both remain cached after stopping.
                    if(ownership?.alreadyWatching===false)
                        retiringWatches.delete(slot);
                    else pending=true;
                }
                if(!pending)return;
                if(Date.now()>=deadline)break;
                await sleep(100);
            }
            fail('watch_close_pending');
        }

        function stopRelay(entry,reason) {
            if(!entry || relays.get(entry.slot)!==entry)return;
            if(reason)watchFailures.set(entry.slot,{owner:entry.owner,account:entry.account,lease:entry.lease,reason});
            relays.delete(entry.slot);
            retireRelay(entry);
        }

        function stopAllRelays() {
            for(const entry of relays.values())stopRelay(entry);
        }

        function incomingTrack(entry) {
            // The user can restart a stream with the same key. Do not pick the
            // first same-user media connection: it can still be the obsolete one.
            const connection=entry.demand?.connection?._connection;
            if(!connection)return null;
            const pc=Object.values(connection).find(value=>value&&typeof value.getReceivers==='function');
            return pc?.getReceivers().map(r=>r.track).find(t=>t?.kind==='video'&&t.readyState==='live'&&!t.muted)??null;
        }

        async function gather(pc) {
            if (pc.iceGatheringState === 'complete') return;
            await new Promise((resolve, reject) => {
                const done = () => { clearTimeout(timer); pc.removeEventListener('icegatheringstatechange', changed); };
                const changed = () => { if (pc.iceGatheringState === 'complete') { done(); resolve(); } };
                const timer = setTimeout(() => { done(); reject(Object.assign(new Error(), { adapterCode: 'ice_timeout' })); }, 4000);
                pc.addEventListener('icegatheringstatechange', changed);
                changed();
            });
        }

        async function watch(args,owner) {
            sameCall(args);
            const stream = streams(args.channelId).find(s => s.key === args.key);
            if (!stream || typeof args.lease !== 'string' || !/^[a-f0-9]{32}$/.test(args.lease)) fail('stream_ended');
            const ownership=watchOwnership(stream.key);
            if(!ownership)fail('watch_ownership_unavailable');
            if(!watchDemandApi())fail('watch_demand_unavailable');
            const {alreadyWatching}=ownership;
            // A same-stream receiver replacement transfers our own subscription;
            // an existing user subscription is never reclassified as ours.
            const slot=args.slot??1;
            if(slot!==1 && slot!==2 && slot!==3)fail('invalid_watch_slot');
            const relay=relays.get(slot);
            if(Array.from(relays.values()).some(entry=>entry!==relay && (entry.key===stream.key || entry.lease===args.lease ||
                args.videoView && entry.viewId===args.videoView)))fail('watch_conflict');
            const sameStream=alreadyWatching&&relay?.key===stream.key&&relay.account===args.account&&
                relay.channelId===args.channelId&&relay.userId===stream.userId;
            const transfer=sameStream&&relay.ownsWatch===true;
            const closeWatch=transfer?relay.closeWatch:ownership.closeWatch;
            // Allocate before relinquishing the old relay/subscription. A local
            // peer-construction failure must leave its existing owner intact.
            const viewId=args.videoView;
            if(viewId!==undefined && (typeof viewId!=='string'||!/^[a-f0-9]{32}$/.test(viewId)||
                typeof videoHost?.setVideoTrack!=='function'||!videoHost.videoPlayback(viewId)))fail('video_view_unavailable');
            // The standalone research prototype still negotiates an SDP offer.
            // The shipping widget supplies an already-admitted native video view.
            const pc = viewId ? null : new RTCPeerConnection({ iceServers: [] });
            const previous=relay,demand=sameStream?previous.demand:null;
            if(demand)previous.demand=null;
            const entry = { slot, pc, viewId, key: stream.key, userId:stream.userId, owner, lease: args.lease, account: args.account,
                channelId: args.channelId, ownsWatch: transfer, closeWatch, expires: Date.now() + 25000, timer: null,demand };
            relays.set(slot,entry);
            if(demand)demand.entry=entry;
            retireRelay(previous,transfer);
            const current = () => relayCurrent(entry);
            try {
                // A provider can acknowledge its local close asynchronously.
                // A new explicit watch must not borrow that dying subscription.
                await awaitWatchRetirement(entry);
                if(!current())fail('watch_cancelled');
                const confirmed=watchOwnership(stream.key);
                if(!confirmed)fail('watch_ownership_unavailable');
                if(!streams(args.channelId).some(value=>value.key===stream.key))fail('stream_ended');
                // Discord's ordinary watch action can close existing subscriptions.
                // Each pin adds its own watch, including alongside Discord's page.
                if (!confirmed.alreadyWatching) {
                    entry.closeWatch=confirmed.closeWatch;entry.ownsWatch=true;
                    confirmed.startWatch(stream.descriptor, { forceMultiple: true, noFocus: true });
                }
                let track;
                const trackDeadline=Date.now()+8000;
                for (let i = 0; i < 80 && current(); ++i) {
                    track = refreshWatchDemand(entry)?incomingTrack(entry):null;
                    if (track) break;
                    if(Date.now()>=trackDeadline)break;
                    await sleep(100);
                }
                if (!current()) fail('watch_cancelled');
                if (!track) fail('video_unavailable');
                let sender=null;
                if(viewId){
                    if(!videoHost.setVideoTrack(viewId,track))fail('video_view_unavailable');
                }else{
                    sender=entry.sender=pc.addTrack(track);
                    await pc.setLocalDescription(await pc.createOffer());
                    await gather(pc);
                }
                if (!current()) fail('watch_cancelled');
                // The widget must publish the receiver before a real remote
                // decoder may produce frames. Observe after attachment, never
                // block watch's reply on an as-yet-unpresented WebView.
                let changing = false, unavailableSince=null, firstPicturePendingSince=Date.now(), firstPictureBaseline=0, retriedPlayback=false;
                entry.restartPlayback=()=>{
                    if(!viewId || !current() || !refreshWatchDemand(entry))return false;
                    const currentTrack=incomingTrack(entry);
                    if(!currentTrack || !videoHost.setVideoTrack(viewId,currentTrack,true))return false;
                    track=currentTrack;
                    firstPicturePendingSince=Date.now();
                    firstPictureBaseline=videoHost.videoPlayback(viewId)?.totalFrames??0;
                    retriedPlayback=false;
                    return true;
                };
                entry.timer = setInterval(async () => {
                  try {
                    if (!current() || !streams(args.channelId).some(s => s.key === stream.key)||
                        watchOwnership(stream.key)?.alreadyWatching!==true) {
                        stopRelay(entry,'watch_ended');
                        return;
                    }
                    const replacement = refreshWatchDemand(entry)?incomingTrack(entry):null;
                    if(!replacement){
                        unavailableSince??=Date.now();
                        if(Date.now()-unavailableSince>=8000){stopRelay(entry,'track_unavailable');return;}
                    }else unavailableSince=null;
                    if(viewId){
                        const playback=videoHost.videoPlayback(viewId);
                        if(!playback||track.readyState==='ended'&&!replacement){stopRelay(entry,'video_view_unavailable');return;}
                        if(playback.totalFrames<firstPictureBaseline)firstPictureBaseline=0;
                        if(playback.width>0&&playback.height>0&&playback.totalFrames>firstPictureBaseline)firstPicturePendingSince=null;
                        else if(firstPicturePendingSince!==null&&Date.now()-firstPicturePendingSince>=12000){
                            lastFailure={method:'watch',type:'Error',detail:'video_no_frames'};stopRelay(entry,'video_no_frames');return;
                        }
                        if(!retriedPlayback && firstPicturePendingSince!==null && Date.now()-firstPicturePendingSince>=6000 && replacement===track){
                            // The receiver can be bound while its view is still parked offscreen.
                            // Rearm local playback once without dropping the Discord subscription.
                            retriedPlayback=true;
                            if(!videoHost.setVideoTrack(viewId,track,true)){stopRelay(entry,'video_view_unavailable');return;}
                        }
                        if(replacement && replacement!==track){
                            if(!videoHost.setVideoTrack(viewId,replacement)){stopRelay(entry,'video_view_unavailable');return;}
                            track=replacement;
                        }
                    }else if (!changing && replacement && sender.track !== replacement) {
                        changing = true;
                        try { await sender.replaceTrack(replacement); } catch { stopRelay(entry); }
                        finally { changing = false; }
                    }
                  }catch(error) {
                    lastFailure={method:'watch',type:'Error',detail:error.adapterCode??'watch_demand_failed'};
                    stopRelay(entry,'watch_demand_failed');
                  }
                }, 1000);
                return viewId ? {lease:args.lease,videoView:viewId} : { lease: args.lease, offer: pc.localDescription.toJSON() };
            } catch (error) { stopRelay(entry); throw error; }
        }

        function beginShare(args, owner) {
            sameCall(args);
            if (sharePending || currentShare()) fail('share_already_active');
            const quality=args.captureQuality;
            if(quality!==undefined&&(!quality||typeof quality!=='object'||Array.isArray(quality)||
                Object.keys(quality).some(key=>!['resolution','frameRate'].includes(key))||
                !isCaptureQuality(quality)))fail('share_quality_unsupported');
            if(quality!==undefined)requireShareQuality(quality,args);
            const sourceOptions=quality===undefined?{width:1280,height:720}:
                {width:({480:854,720:1280,1080:1920})[quality.resolution],height:quality.resolution,frameRate:quality.frameRate};
            const engine = store('MediaEngineStore')?.getMediaEngine?.();
            const start = code('"STREAM_START",streamType');
            if (typeof start !== 'function' || typeof engine?.getDesktopSource !== 'function') fail('share_unavailable');
            const epoch = ++shareEpoch;
            sharePending = true;
            shareError = null;
            shareErrorOwner={account:args.account,channel:args.channelId};
            shareQualityWarning = null;
            shareDeadline = Date.now() + 60000;
            shareOwner = owner;
            shareOwnerDeadline = Date.now() + 12000;
            const current = () => epoch === shareEpoch && Date.now() < shareDeadline && Date.now() < shareOwnerDeadline &&
                me()?.id === args.account && channelId() === args.channelId;
            const timer = setTimeout(() => {
                if (epoch === shareEpoch) { ++shareEpoch; sharePending = false; shareError = 'share_picker_timeout'; }
            }, 60000);
            // Obtain the source through the host-admitted native capture transport.
            // Permission and source selection remain owned by the host.
            (async () => {
                let source;
                let handedOff = false;
                let qualityBinding = null;
                try {
                    source = await engine.getDesktopSource(sourceOptions, true);
                    if (!current()) return;
                    if(currentShare())fail('share_already_active');
                    if(quality!==undefined) {
                        // Recheck after the asynchronous chooser. Capturing a
                        // larger source alone does not update Discord's encoder.
                        const policy=requireShareQuality(quality,args);
                        if(!current())return;
                        if(policy.update) {
                            policy.update({preset:policy.preset,resolution:quality.resolution,frameRate:quality.frameRate});
                            if(!current())return;
                            const observed=policy.settings.getState();
                            if(observed?.preset!==policy.preset||observed?.resolution!==quality.resolution||observed?.fps!==quality.frameRate)
                                fail('share_quality_unconfirmed');
                        }
                        requireShareQuality(quality,args);
                    }
                    const guild = store('ChannelStore')?.getChannel?.(args.channelId)?.guild_id ?? null;
                    if(!current())return;
                    if(currentShare())fail('share_already_active');
                    if(quality!==undefined){
                        try{qualityBinding=bindOutgoingQuality(args,quality,engine,source,epoch);}
                        catch{
                            // Preserve the previously working baseline when optional
                            // metadata correction is unavailable. Never silently
                            // admit a higher mode without a scoped binding mechanism.
                            if(quality.resolution>720||quality.frameRate>30)fail('share_quality_unavailable');
                            setQualityWarning('share_quality_unavailable',args,engine,source,epoch);
                        }
                    }
                    if(!current())return;
                    experimentalCapture?.prepare(args,quality,engine,source,epoch);
                    start(guild, args.channelId, { pid: null, sourceId: source, sourceName: null });
                    if(shareQualityWarning?.epoch===epoch&&shareQualityWarning.stream===null)shareQualityWarning.stream=currentShare()??null;
                    handedOff = true;
                } catch(error) {
                    if(epoch===shareEpoch)shareError=['share_quality_unsupported','share_quality_unconfirmed','share_quality_unavailable'].includes(error.adapterCode)
                        ?error.adapterCode:'share_cancelled_or_failed';
                }
                finally {
                    clearTimeout(timer);
                    if (source != null && !handedOff) { if(epoch===shareEpoch)experimentalCapture?.dispose();qualityBinding?.dispose();try { engine.desktopInputPool?.get?.(source)?.destroy?.(); } catch {} }
                    if (epoch === shareEpoch) sharePending = false;
                }
            })();
            return { requested: true };
        }

        async function invoke({ method, args, owner }) {
            try {
                if (typeof owner !== 'string' || !/^[a-f0-9]{32}$/.test(owner)) fail('invalid_owner');
                if (method === 'snapshot') {
                    if(shareQualityBinding&&!shareQualityBinding.current())shareQualityBinding.dispose();
                    if (sharePending && shareOwner === owner) shareOwnerDeadline = Date.now() + 12000;
                    return { ok: true, value: snapshot() };
                }
                if (method === 'diagnostics') return { ok: true, value: lastFailure ?? {} };
                authorize(args);
                if(['callStart','callAnswer','callDecline'].includes(method)) {
                    if(!calls)fail('call_action_unavailable');
                    return {ok:true,value:await calls.invoke(method,{...args,owner})};
                }
                if(messageCommands.has(method)) {
                    if(!messaging)fail('messaging_unavailable');
                    return {ok:true,value:await messaging[messageCommands.get(method)](args)};
                }
                const relay=byLease(args.lease);
                switch (method) {
                    case 'enableNotifications':return {ok:true,value:await enableDesktopNotifications(args)};
                    case 'voiceMute':case 'voiceVolume':return {ok:true,value:updateVoiceAudio(method,args)};
                    case 'voiceSettings':return {ok:true,value:voiceSettings(args)};
                    case 'voiceInputDevice':case 'voiceOutputDevice':case 'voiceInputVolume':return {ok:true,value:updateVoiceSettings(method,args)};
                    case 'streamDiagnostics':return {ok:true,value:await streamDiagnostics(args)};
                    case 'connectionDiagnostics':return {ok:true,value:connectionDiagnostics(args)};
                    case 'incomingDiagnostics':return {ok:true,value:await incomingDiagnostics(args,owner)};
                    case 'joinActivity': {
                        if(!activities)fail('activity_unavailable');
                        const target=activities.validateJoin(args);
                        const action=props('selectVoiceChannel');
                        if(typeof action?.selectVoiceChannel!=='function')fail('voice_action_unavailable');
                        if(channelId()!==target.id)await action.selectVoiceChannel(target.id);
                        break;
                    }
                    case 'watchActivity':
                        if(!activities)fail('activity_unavailable');
                        activities.validateWatch(args);
                        return {ok:true,value:await watch(args,owner)};
                    case 'join': {
                        if(!activities)fail('voice_action_unavailable');
                        const target=activities.validateGuildJoin(args);
                        const action=props('selectVoiceChannel');
                        if(typeof action?.selectVoiceChannel!=='function')fail('voice_action_unavailable');
                        if(channelId()!==target.id)await action.selectVoiceChannel(target.id);
                        break;
                    }
                    case 'leave': sameCall(args); ++shareEpoch; sharePending = false; stopAllRelays();
                        dispatcher().dispatch({ type: 'VOICE_CHANNEL_SELECT', channelId: null, guildId: null }); break;
                    case 'mute': case 'deafen': {
                        sameCall(args);
                        if (typeof args.value !== 'boolean') fail('invalid_request');
                        const media = store('MediaEngineStore');
                        const action = props('toggleSelfMute', 'toggleSelfDeaf');
                        const getter = method === 'mute' ? 'isSelfMute' : 'isSelfDeaf';
                        const setter = method === 'mute' ? 'toggleSelfMute' : 'toggleSelfDeaf';
                        if (typeof media?.[getter] !== 'function' || typeof action?.[setter] !== 'function') fail('voice_control_unavailable');
                        if (!!media[getter]() !== args.value) action[setter]();
                        break;
                    }
                    case 'streamMute': case 'streamVolume': {
                        if(method==='streamMute'?typeof args.value!=='boolean':
                            !Number.isFinite(args.value)||args.value<0||args.value>100)fail('invalid_request');
                        const target=streamAudioTarget(args,owner),audio=streamAudio(args.userId);
                        if(!audio)fail('stream_audio_unavailable');
                        // Provider getters/actions can run synchronous store callbacks.
                        // Revalidate immediately before applying a side effect.
                        if(streamAudioTarget(args,owner)!==target)fail('watch_cancelled');
                        if(method==='streamMute') {
                            if(audio.state.muted!==args.value)audio.actions.toggleLocalMute(args.userId,'stream');
                        }else if(Math.abs(audio.state.volume-args.value)>1e-8) {
                            // Only a changed slider value writes the preference.
                            // Re-sending the displayed ceiling preserves a saved boost.
                            audio.actions.setLocalVolume(args.userId,100*Math.pow(args.value/100,2.8),'stream');
                        }
                        // Discord's action discards its dispatch promise, but its
                        // normal Flux queue drains synchronously. Confirm the
                        // observed state rather than acknowledging mere invocation.
                        // Never retry a toggle if the provider did not apply it.
                        if(streamAudioTarget(args,owner)!==target)fail('watch_cancelled');
                        const observed=streamAudio(args.userId)?.state;
                        if(streamAudioTarget(args,owner)!==target)fail('watch_cancelled');
                        if(!observed)fail('stream_audio_unconfirmed');
                        if(method==='streamMute') {
                            if(observed.muted!==args.value)fail('stream_audio_unconfirmed');
                        }else {
                            const epsilon=1e-4,defaultVolume=100*Math.pow(0.18,1/2.8);
                            // AudioActionCreators snaps requests within one slider
                            // point of the STREAM default gain (18) back to 18.
                            const defaultSnap=Math.abs(args.value-defaultVolume)<1&&Math.abs(observed.volume-defaultVolume)<=epsilon;
                            if(Math.abs(observed.volume-args.value)>epsilon&&!defaultSnap)fail('stream_audio_unconfirmed');
                        }
                        return {ok:true,value:{audio:observed}};
                    }
                    case 'watch': return { ok: true, value: await watch(args,owner) };
                    case 'videoPlayback': {
                        sameCall(args);
                        if(!relay?.viewId || relay.owner!==owner || relay.lease!==args.lease || !relayCurrent(relay))fail('watch_cancelled');
                        const stats=videoHost.videoPlayback(relay.viewId);
                        return {ok:true,value:stats?{...stats,available:true,lease:args.lease,state:'connected'}:{available:false}};
                    }
                    case 'answer': {
                        sameCall(args);
                        const entry = relay;
                        if (!entry?.pc || entry.lease !== args.lease || entry.owner !== owner || !relayCurrent(entry)) fail('watch_cancelled');
                        await entry.pc.setRemoteDescription(args.answer);
                        if (!relayCurrent(entry)) fail('watch_cancelled');
                        // Only the local pin encoder. Preserve the resolution of the received
                        // track; this ceiling allows detailed motion without reserving bandwidth.
                        try {
                            const parameters = entry.sender.getParameters();
                            if (parameters.encodings?.length === 1) {
                                parameters.encodings[0].maxBitrate = 20_000_000;
                                parameters.degradationPreference = 'maintain-resolution';
                                await entry.sender.setParameters(parameters);
                            }
                        } catch { /* Keep negotiated playback when the runtime rejects optional tuning. */ }
                        if (!relayCurrent(entry)) fail('watch_cancelled');
                        break;
                    }
                    case 'reconnectVideo': {
                        if(!relay || relay.owner!==owner || !relayCurrent(relay) || !relay.viewId || relay.viewId!==args.videoView ||
                            relay.account!==args.account || relay.channelId!==args.channelId || relay.key!==args.key)fail('watch_cancelled');
                        return {ok:true,value:{reconnected:relay.restartPlayback?.()===true}};
                    }
                    case 'keep':
                        if (relay?.lease === args.lease && relay.owner===owner && relayCurrent(relay))
                            relay.expires = Date.now() + 12000;
                        break;
                    case 'unwatch':
                        if (relay?.lease === args.lease && relay.owner===owner) stopRelay(relay);
                        for(const [slot,failure] of watchFailures){
                            if(failure.owner===owner && failure.account===args.account && failure.lease===args.lease){
                                watchFailures.delete(slot);
                                return {ok:true,value:{reason:failure.reason}};
                            }
                        }
                        break;
                    case 'cancelPendingShare':
                        if (shareOwner === owner) { ++shareEpoch; sharePending = false;shareQualityBinding?.dispose(); }
                        break;
                    case 'share': return { ok: true, value: beginShare(args, owner) };
                    case 'stopShare': {
                        sameCall(args); ++shareEpoch; sharePending = false;shareQualityBinding?.dispose();shareQualityWarning=null;
                        const own = currentShare();
                        if (own) {
                            const stop = code('"STREAM_STOP",streamKey');
                            if (typeof stop !== 'function') fail('stop_share_unavailable');
                            stop(streamKey(own));
                        }
                        break;
                    }
                    default: fail('unknown_command');
                }
                return { ok: true, value: {} };
            } catch (error) {
                lastFailure = { method, type: error.name, detail: String(error.message).slice(0, 200) };
                return { ok: false, code: error.adapterCode ?? 'adapter_incompatible' };
            }
        }
        return Object.freeze({ revision, build, invoke, dispose() { messaging?.dispose(); calls?.dispose(); ++shareEpoch; sharePending = false;experimentalCapture?.dispose();shareQualityBinding?.dispose();shareQualityWarning=null;shareError=null;shareErrorOwner=null; stopAllRelays(); } });
    })();
}

}
