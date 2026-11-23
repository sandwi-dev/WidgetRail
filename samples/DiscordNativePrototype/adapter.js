// WidgetRail development adapter, v1. No Discord credentials cross this boundary.
// Internal Discord APIs are feature-probed and may disappear after Discord updates.
function initializeWidgetRailDiscordAdapter(build) {
if(!Number.isSafeInteger(build)||build<1)throw new Error('invalid_adapter_build');
if(globalThis.WidgetRailDiscordPrototype?.build>build)throw new Error('adapter_newer');
if (globalThis.WidgetRailDiscordPrototype?.build !== build) {
    globalThis.WidgetRailDiscordPrototype?.dispose?.();
    globalThis.WidgetRailDiscordPrototype = (() => {
        'use strict';
        const revision = 1;
        const wp = () => globalThis.Vencord?.Webpack;
        const store = name => wp()?.findStore(name);
        const props = (...names) => wp()?.findByProps(...names);
        const code = pattern => wp()?.findByCode(pattern);
        const dispatcher = () => wp()?.Common?.FluxDispatcher;
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
        const channelId = () => store('SelectedChannelStore')?.getVoiceChannelId?.() ?? null;
        const currentShare = () => store('ApplicationStreamingStore')?.getCurrentUserActiveStream?.();
        const streamKey = s => s.guildId ? `guild:${s.guildId}:${s.channelId}:${s.ownerId || s.userId}` : `call:${s.channelId}:${s.ownerId || s.userId}`;
        const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
        const activities = globalThis.createWidgetRailDiscordActivities?.({store,me,currentChannel:channelId,
            voiceActionAvailable:()=>typeof props('selectVoiceChannel')?.selectVoiceChannel==='function',
            watchAvailable:key=>!!watchDemandApi()&&watchOwnership(key)!==null});
        let relay = null;
        let relayEpoch = 0;
        let shareEpoch = 0;
        let sharePending = false;
        let shareError = null;
        let shareQualityWarning = null;
        let shareErrorOwner = null;
        let shareDeadline = 0;
        let shareOwner = null;
        let shareOwnerDeadline = 0;
        let lastFailure = null;
        let diagnosticsPending = null;
        const diagnosticsHistory = new WeakMap();
        let shareQualityBinding = null;

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

        function snapshot() {
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
                        avatarUrl:activities?.avatarUrl(v.userId,participant?.avatar)??null};
                });
            return { revision, ready: !!user && !!wp(), account: user?.id ?? '', capabilities: capabilities(),
                channels: user ? channels() : [], channelId: id, channelName: text(store('ChannelStore')?.getChannel?.(id)?.name),
                muted: !!media?.isSelfMute?.(), deafened: !!media?.isSelfDeaf?.(), participants,
                streams: id ? streams(id).map(({ descriptor, ...value }) => ({...value,audio:streamAudio(value.userId)?.state??
                    {available:false,muted:false,volume:null}})) : [], sharing: !!own && own.state !== 'ENDED',
                ...shareQualitySnapshot(user?.id,id),voiceConnection:voiceConnection(),
                sharePending,shareError:shareErrorOwner?.account===user?.id&&shareErrorOwner?.channel===id?shareError:null,
                shareQualityWarning:qualityWarningForSnapshot(), relayState: relay?.pc.connectionState ?? 'closed',
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

        const captureResolutions=[480,720,1080,1440],captureFrameRates=[15,30,60];
        const highDetailBitrate=18000000;
        const isCaptureQuality=quality=>quality&&captureResolutions.includes(quality.resolution)&&captureFrameRates.includes(quality.frameRate);
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
                    const saved=settings.getState(),candidate={resolution:saved?.resolution,frameRate:saved?.fps};
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
        function streamIncarnation(key,source) {
            try {
                const owner=store('ApplicationStreamingStore'),read=owner?.getStreamerActiveStreamMetadataForStream;
                if(typeof read!=='function'||typeof source!=='string'||source.startsWith('prepicked:'))return null;
                const value=read.call(owner,key);
                return value&&typeof value==='object'&&value.sourceId===source?{owner,read,value,key,source}:null;
            }catch{return null;}
        }
        function sameStreamIncarnation(held) {
            try{return store('ApplicationStreamingStore')===held.owner&&held.owner.getStreamerActiveStreamMetadataForStream===held.read&&
                held.read.call(held.owner,held.key)===held.value&&held.value.sourceId===held.source;}catch{return false;}
        }
        function setQualityWarning(code,args,engine,source,epoch,capturedSource=null,check=null) {
            let sourceObject=null,stream=null;
            try{sourceObject=capturedSource??peekDesktopSource(engine,source)??null;stream=currentShare()??null;}catch{}
            shareQualityWarning={code,account:args.account,channel:args.channelId,engine,source,epoch,created:Date.now(),
                pool:engine.desktopInputPool,sourceObject,stream,sourceObserved:false,check,
                incarnation:stream?streamIncarnation(streamKey(stream),source):null};
        }
        function qualityWarningForSnapshot() {
            shareQualityBinding?.checkBandwidth?.();
            shareQualityBinding?.checkEncoder?.();
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
                if(warning.stream!==null){if(!own||own.state==='ENDED'||(warning.incarnation?
                    !sameStreamIncarnation(warning.incarnation)||streamKey(own)!==warning.incarnation.key:own!==warning.stream))return clear();}
                else if(own){
                    if(own.state==='ENDED'||own.channelId!==warning.channel||(own.ownerId||own.userId)!==warning.account)return clear();
                    warning.stream=own;
                }else if(Date.now()-warning.created>15000)return clear();
                return warning.code;
            }catch{return clear();}
        }

        function baselineAnswer(local,answer,mid,payload) {
            // This parser deliberately accepts only the known browser answer
            // boundary. It never rewrites a local offer or changes payload IDs,
            // audio, RTX, transport, encryption, bandwidth or other video MIDs.
            if(local?.type!=='offer'||answer?.type!=='answer'||typeof mid!=='string'||!/^[\w-]{1,32}$/.test(mid)||
                !Number.isInteger(payload)||payload<0||payload>127)return null;
            function selected(sdp,localSide) {
                if(typeof sdp!=='string'||sdp.length>65536||!sdp.startsWith('v=0\r\n')&&!sdp.startsWith('v=0\n'))return null;
                const rows=sdp.split('\n');if(rows.length>1024)return null;
                const sections=[];let part=null,offset=0;
                for(const raw of rows){
                    const line=raw.endsWith('\r')?raw.slice(0,-1):raw;
                    if(/[\r\u0000]/.test(line))return null;
                    if(line.startsWith('m=')){part=[];sections.push(part);if(sections.length>16)return null;}
                    if(part)part.push({line,offset});else if(line.startsWith('a=mid:'))return null;
                    offset+=raw.length+1;
                }
                const mids=new Set();let target=null;
                for(const section of sections){
                    const found=section.filter(row=>row.line.startsWith('a=mid:'));
                    if(found.length!==1)return null;
                    const id=found[0].line.slice(6);
                    if(!/^[\w-]{1,32}$/.test(id)||mids.has(id))return null;mids.add(id);
                    if(id===mid)target=section;
                }
                if(!target)return null;
                const media=/^m=video ([1-9]\d*) UDP\/TLS\/RTP\/SAVPF ([\d ]+)$/.exec(target[0].line);
                if(!media)return null;
                const payloads=media[2].split(' ');
                if(payloads.some(value=>!/^\d{1,3}$/.test(value)||Number(value)>127)||new Set(payloads).size!==payloads.length||!payloads.includes(String(payload)))return null;
                const directions=target.filter(row=>/^a=(sendrecv|sendonly|recvonly|inactive)$/.test(row.line));
                if(directions.length!==1||!(localSide?/^a=(sendrecv|sendonly)$/:/^a=(sendrecv|recvonly)$/).test(directions[0].line))return null;
                const rtp=target.filter(row=>new RegExp('^a=rtpmap:'+payload+'(?:\\s|$)').test(row.line));
                if(rtp.length!==1||!new RegExp('^a=rtpmap:'+payload+' H264/90000$','i').test(rtp[0].line))return null;
                const fmtp=target.filter(row=>new RegExp('^a=fmtp:'+payload+'(?:\\s|$)').test(row.line));
                if(fmtp.length!==1||!fmtp[0].line.startsWith('a=fmtp:'+payload+' '))return null;
                const prefix='a=fmtp:'+payload+' ',parameters=new Map();let position=fmtp[0].offset+prefix.length,profileOffset=null;
                for(const raw of fmtp[0].line.slice(prefix.length).split(';')){
                    const match=/^(\s*([a-z0-9-]+)\s*=\s*)([^;\s]+)(\s*)$/i.exec(raw);
                    if(!match||parameters.has(match[2].toLowerCase()))return null;
                    parameters.set(match[2].toLowerCase(),match[3]);
                    if(match[2].toLowerCase()==='profile-level-id')profileOffset=position+match[1].length;
                    position+=raw.length+1;
                }
                const profile=parameters.get('profile-level-id')?.toLowerCase();
                if(!/^(4200|42e0)(0a|0b|0c|0d|14|15|16|1e|1f|20|28|29|2a|32|33|34|3c|3d|3e)$/.test(profile??'')||
                    parameters.get('packetization-mode')!=='1'||parameters.get('level-asymmetry-allowed')!=='1'||profileOffset===null)return null;
                return {profile,profileOffset};
            }
            const offered=selected(local.sdp,true),remote=selected(answer.sdp,false);
            if(!offered?.profile.startsWith('4200')||!remote||offered.profile.slice(4)!==remote.profile.slice(4))return null;
            const profile=offered.profile;
            return {profile,sdp:answer.sdp.slice(0,remote.profileOffset)+profile+answer.sdp.slice(remote.profileOffset+6)};
        }

        function bindOutgoingQuality(args,quality,engine,source,epoch) {
            const requestedBitrate=args.bandwidthPreference==='high-detail'?highDetailBitrate:null;
            const preferHardware=args.encoderPreference==='prefer-hardware';
            const sourcePool=engine.desktopInputPool,selected=peekDesktopSource(engine,source),track=selected?.stream?.getVideoTracks?.()[0];
            if(!selected||track?.kind!=='video'||typeof track.getConstraints!=='function'||
                typeof engine.on!=='function'||typeof engine.removeListener!=='function'||typeof engine.connections?.has!=='function')fail('share_quality_unavailable');
            const selectedConstraints=track.getConstraints();
            const bound=value=>typeof value==='number'?value:value?.ideal??value?.max;
            const width=bound(selectedConstraints?.width),height=bound(selectedConstraints?.height);
            const maximumWidth=({480:1706,720:2560,1080:3840,1440:5120})[quality.resolution];
            if(!Number.isInteger(width)||!Number.isInteger(height)||width<2||height<2||width>maximumWidth||height>quality.resolution)
                fail('share_quality_unavailable');
            const wrappers=new Map();let closed=false,applied=false,timer,ownedStream=null,ownedIncarnation=null;
            const streamWrappers=new Map();
            const encoderWrappers=new Map();let requestedProfile=null,appliedProfile=null,observedEncoderPc=null;
            let contentHint=null,contentPreferenceUnavailable=false,appliedBitrate=null,observeBandwidth=null;
            try{
                const native=globalThis.WidgetRailNativeMedia?.status?.();
                if(native?.active===true){
                    if(['detail','motion'].includes(native.contentPreference)&&track.contentHint===native.contentPreference)contentHint=native.contentPreference;
                    else contentPreferenceUnavailable=true;
                }
            }catch{contentPreferenceUnavailable=true;}
            const key=(store('ChannelStore')?.getChannel?.(args.channelId)?.guild_id)
                ?`guild:${store('ChannelStore').getChannel(args.channelId).guild_id}:${args.channelId}:${args.account}`:`call:${args.channelId}:${args.account}`;
            const current=()=>{try{const queued=peekDesktopSource(engine,source),own=currentShare();return !closed&&epoch===shareEpoch&&me()?.id===args.account&&channelId()===args.channelId&&
                store('MediaEngineStore')?.getMediaEngine?.()===engine&&engine.desktopInputPool===sourcePool&&
                (queued===undefined||queued===selected)&&selected.stream?.getVideoTracks?.()[0]===track&&track.readyState==='live'&&
                (ownedStream===null||own&&own.state!=='ENDED'&&(ownedIncarnation?
                    sameStreamIncarnation(ownedIncarnation)&&streamKey(own)===key:own===ownedStream));}catch{return false;}};
            const restoreEncoder=pc=>{
                const held=encoderWrappers.get(pc);if(!held)return;
                encoderWrappers.delete(pc);++held.sequence;
                if(pc.setRemoteDescription!==held.wrapper)return;
                try{if(held.descriptor)Object.defineProperty(pc,'setRemoteDescription',held.descriptor);else delete pc.setRemoteDescription;}catch{}
            };
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
                for(const pc of [...encoderWrappers.keys()])restoreEncoder(pc);
                requestedProfile=appliedProfile=observedEncoderPc=null;
                if(shareQualityBinding===binding)shareQualityBinding=null;
            };
            const markUnavailable=(check='attachment-timeout')=>{if(current())setQualityWarning('share_quality_unconfirmed',args,engine,source,epoch,selected,check);dispose();};
            const contentUnavailable=()=>{if(current())setQualityWarning('share_content_preference_unconfirmed',args,engine,source,epoch,selected);};
            const encoderUnavailable=pc=>{
                if(pc&&observedEncoderPc!==null&&observedEncoderPc!==pc)return;
                appliedProfile=null;
                if(current()&&!shareQualityWarning)setQualityWarning('share_encoder_unconfirmed',args,engine,source,epoch,selected);
            };
            const checkEncoder=()=>{
                if(observedEncoderPc===null)return;
                try{if(encoderWrappers.get(observedEncoderPc)?.attached())return;}catch{}
                encoderUnavailable(observedEncoderPc);requestedProfile=null;
            };
            function bindEncoderPreference(connection,rtc) {
                if(!preferHardware)return;
                for(const [pc,held] of encoderWrappers)if(!held.current())restoreEncoder(pc);
                const pc=connection.pc,transceiver=connection.videoTransceiver,sender=transceiver?.sender;
                if(!ownedIncarnation||!pc||typeof pc.setRemoteDescription!=='function'||!transceiver||!sender||encoderWrappers.size>=4){encoderUnavailable();return;}
                if(encoderWrappers.has(pc))return;
                const original=pc.setRemoteDescription,descriptor=Object.getOwnPropertyDescriptor(pc,'setRemoteDescription');
                const ownsConnection=()=>current()&&sameStreamIncarnation(ownedIncarnation)&&connection.destroyed===false&&engine.connections.has(connection)&&
                    store('StreamRTCConnectionStore')?.getRTCConnection?.(key)===rtc&&rtc._connection===connection&&rtc._destroyed===false&&
                    rtc.context==='stream'&&rtc.streamKey===key&&rtc.isOwner===true&&rtc.streamContext?.ownerId===args.account&&
                    rtc.streamContext?.channelId===args.channelId&&connection.pc===pc&&connection.videoTransceiver===transceiver&&
                    transceiver.sender===sender&&
                    store('MediaEngineStore')?.getGoLiveSource?.()?.desktopSource?.id===source&&pc.signalingState!=='closed';
                const exact=()=>ownsConnection()&&sender.track===track&&connection.streamVideoTrack===track&&connection.input?.desktop===selected;
                const held={descriptor,sequence:0,current:ownsConnection,attached:exact,wrapper:null};
                held.wrapper=function(description){
                    if(this!==pc)return original.apply(this,arguments);
                    if(!ownsConnection()){restoreEncoder(pc);encoderUnavailable(pc);return original.apply(this,arguments);}
                    if(!exact()){encoderUnavailable(pc);return original.apply(this,arguments);}
                    const sequence=++held.sequence;let changed=null,offer,mid,payload;
                    try{
                        const answer={type:description?.type,sdp:description?.sdp};
                        const local=pc.localDescription;
                        offer={type:local?.type,sdp:local?.sdp};
                        mid=transceiver.mid;payload=connection.videoPayloadType;
                        if(pc.signalingState==='have-local-offer'&&connection.videoCodec==='H264')
                            changed=baselineAnswer(offer,answer,mid,payload);
                    }catch{}
                    // Provider description getters can run synchronous callbacks.
                    // Snapshot them once, then recheck the exact negotiation and
                    // owner immediately before changing this answer.
                    if(!changed||pc.signalingState!=='have-local-offer'||connection.videoCodec!=='H264'||
                        transceiver.mid!==mid||connection.videoPayloadType!==payload||!exact()){
                        if(!ownsConnection())restoreEncoder(pc);
                        encoderUnavailable(pc);return original.apply(this,arguments);
                    }
                    const profile=changed.profile;observedEncoderPc=pc;requestedProfile=profile;appliedProfile=null;
                    const forwarded=Array.from(arguments);forwarded[0]={type:'answer',sdp:changed.sdp};
                    let result;
                    try{result=original.apply(this,forwarded);}
                    catch(error){encoderUnavailable(pc);throw error;}
                    Promise.resolve(result).then(()=>{
                        if(held.sequence!==sequence||encoderWrappers.get(pc)!==held||!exact())return;
                        const observed=baselineAnswer(pc.localDescription?.type==='offer'?pc.localDescription:
                            {type:'offer',sdp:pc.currentLocalDescription?.sdp},pc.remoteDescription,transceiver.mid,connection.videoPayloadType);
                        if(observed?.profile===profile&&pc.remoteDescription?.sdp===observed.sdp){
                            appliedProfile=profile;
                            if(shareQualityWarning?.code==='share_encoder_unconfirmed')shareQualityWarning=null;
                        }else encoderUnavailable(pc);
                    },()=>{if(held.sequence===sequence&&encoderWrappers.get(pc)===held&&exact())encoderUnavailable(pc);}).catch(()=>{});
                    return result;
                };
                try{Object.defineProperty(pc,'setRemoteDescription',{value:held.wrapper,writable:true,configurable:true});encoderWrappers.set(pc,held);}
                catch{encoderUnavailable();}
            }
            const bandwidthUnavailable=()=>{
                appliedBitrate=null;
                if(current()&&!shareQualityWarning)setQualityWarning('share_bandwidth_unconfirmed',args,engine,source,epoch,selected);
            };
            function attachWithBandwidth(connection,rtc,manager,original,input) {
                if(requestedBitrate===null)return original.call(connection,input);
                const exact=()=>current()&&connection.destroyed===false&&engine.connections.has(connection)&&
                    connection.videoQualityManager===manager&&manager?.connection===connection&&manager.contextType==='stream'&&
                    store('StreamRTCConnectionStore')?.getRTCConnection?.(key)===rtc&&rtc._connection===connection&&rtc._destroyed===false&&
                    rtc.isOwner===true&&rtc.streamContext?.ownerId===args.account&&rtc.streamContext?.channelId===args.channelId&&
                    store('MediaEngineStore')?.getGoLiveSource?.()?.desktopSource?.id===source;
                const originalQuality=manager?.setGoliveQuality,descriptor=manager&&Object.getOwnPropertyDescriptor(manager,'setGoliveQuality');
                if(!exact()||typeof originalQuality!=='function'){bandwidthUnavailable();return original.call(connection,input);}
                let calls=0,installed=false,result;
                const wrapper=function(value){
                    // The browser's setDesktopInput resets bitrateMax itself.
                    // Replace that one synchronous source-policy write, then let
                    // its normal quality manager retain remote demand and network
                    // adaptation. Never patch a prototype, floor, target or sender.
                    const constraints=track.getConstraints(),w=bound(constraints?.width),h=bound(constraints?.height);
                    if(this!==manager||!exact()||value?.capture?.width!==w||value?.capture?.height!==h||
                        value?.encode?.width!==w||value?.encode?.height!==h||
                        !Number.isFinite(value?.bitrateMax)||value.bitrateMax<=0)
                        return originalQuality.apply(this,arguments);
                    calls++;
                    return originalQuality.call(this,{...value,bitrateMax:requestedBitrate},...Array.prototype.slice.call(arguments,1));
                };
                try{Object.defineProperty(manager,'setGoliveQuality',{value:wrapper,writable:true,configurable:true});installed=true;}
                catch{bandwidthUnavailable();}
                try{result=original.call(connection,input);}
                catch(error){bandwidthUnavailable();throw error;}
                finally{
                    // Never leave the manager patched across an await or replace
                    // a method installed by the provider while attachment ran.
                    if(installed&&manager.setGoliveQuality===wrapper){
                        try{if(descriptor)Object.defineProperty(manager,'setGoliveQuality',descriptor);else delete manager.setGoliveQuality;}
                        catch{bandwidthUnavailable();}
                    }
                }
                if(installed&&calls===1&&!(result&&typeof result.then==='function')&&exact()&&
                    connection.input?.desktop===selected&&connection.streamVideoTrack===track&&manager.goliveMaxQuality?.bitrateMax===requestedBitrate){
                    appliedBitrate=requestedBitrate;
                    observeBandwidth=()=>exact()&&connection.input?.desktop===selected&&connection.streamVideoTrack===track&&
                        manager.goliveMaxQuality?.bitrateMax===requestedBitrate;
                }
                else bandwidthUnavailable();
                return result;
            }
            const checkBandwidth=()=>{
                if(appliedBitrate===null||!observeBandwidth)return;
                try{if(observeBandwidth())return;}catch{}
                bandwidthUnavailable();
            };
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
                let boundManager=null,boundRtc=null;
                const wrapper=function(input){
                    if(this!==connection)return original.apply(this,arguments);
                    if(input!==selected){if(requestedBitrate!==null)dispose();return original.apply(this,arguments);}
                    if(!current()){dispose();return original.apply(this,arguments);}
                    if(boundManager!==null&&(connection.videoQualityManager!==boundManager||
                        store('StreamRTCConnectionStore')?.getRTCConnection?.(key)!==boundRtc)){
                        bandwidthUnavailable();dispose();return original.apply(this,arguments);
                    }
                    if(requestedBitrate===null)restore(connection);
                    let bandwidthOwner;
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
                        ownedIncarnation??=streamIncarnation(key,source);
                        applied=true;shareQualityWarning=null;clearTimeout(timer);
                        bindContentHint(connection,rtc);
                        bindEncoderPreference(connection,rtc);
                        bandwidthOwner={rtc,manager};
                        if(requestedBitrate!==null){boundManager=manager;boundRtc=rtc;}
                    }catch{markUnavailable(check);}
                    return bandwidthOwner?attachWithBandwidth(connection,bandwidthOwner.rtc,bandwidthOwner.manager,original,input):original.apply(this,arguments);
                };
                try{Object.defineProperty(connection,'setDesktopInput',{value:wrapper,writable:true,configurable:true});wrappers.set(connection,{wrapper,descriptor});}
                catch{markUnavailable();}
            }
            const binding={dispose,current,checkBandwidth,checkEncoder,observe:()=>{if(!current()){dispose();return null;}checkBandwidth();checkEncoder();return {active:current(),applied,pending:wrappers.size,contentHooks:streamWrappers.size,
                requestedBitrate,appliedBitrate,encoderPreference:preferHardware?'prefer-hardware':'discord',requestedProfile,appliedProfile};}};
            shareQualityBinding?.dispose();shareQualityBinding=binding;
            try{engine.on('connection',created);timer=setTimeout(()=>{if(!applied)markUnavailable();},15000);}
            catch{dispose();fail('share_quality_unavailable');}
            return binding;
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
                const incarnation=streamIncarnation(key,store('MediaEngineStore')?.getGoLiveSource?.()?.desktopSource?.id);
                const ownsStream=()=>{
                    const active=currentShare();
                    return active&&active.state!=='ENDED'&&streamKey(active)===key&&active.channelId===args.channelId&&
                        (active.ownerId||active.userId)===args.account&&(incarnation?sameStreamIncarnation(incarnation):active===own);
                };
                const exact=()=>shareEpoch===generation&&me()?.id===args.account&&channelId()===args.channelId&&
                    ownsStream()&&connections?.getRTCConnection?.(key)===rtc&&
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
                const previous=diagnosticsHistory.get(connection);
                const diagnosticOwner=incarnation?.value??own;
                const prior=previous?.owner===diagnosticOwner&&previous.generation===generation?previous.samples:null;
                const samples=new Map();
                const number=(value,max=Number.MAX_SAFE_INTEGER)=>Number.isFinite(value)&&value>=0&&value<=max?value:null;
                const outbound=[];let inspected=0;
                for(const report of raw.values()) {
                    if(++inspected>256)fail('stream_diagnostics_unavailable');
                    if(report?.type!=='outbound-rtp'||(report.kind??report.mediaType)!=='video'||report.isRemote===true)continue;
                    if(outbound.length===4)fail('stream_diagnostics_unavailable');
                    const codecReport=typeof raw.get==='function'?raw.get(report.codecId):null;
                    const codec=({'video/vp8':'VP8','video/vp9':'VP9','video/h264':'H264','video/av1':'AV1','video/h265':'H265'})[
                        typeof codecReport?.mimeType==='string'?codecReport.mimeType.toLowerCase():''];
                    const item={width:dimension(report.frameWidth),height:dimension(report.frameHeight),framesPerSecond:rate(report.framesPerSecond),
                        qualityLimitationReason:['none','cpu','bandwidth','other'].includes(report.qualityLimitationReason)?report.qualityLimitationReason:null,
                        ...(codec?{codec}:{})};
                    if(codec==='H264'&&typeof codecReport?.sdpFmtpLine==='string'&&codecReport.sdpFmtpLine.length<=2048){
                        const fields=codecReport.sdpFmtpLine.split(';').map(value=>value.trim());
                        const profiles=fields.filter(value=>/^profile-level-id=[0-9a-f]{6}$/i.test(value));
                        const modes=fields.filter(value=>/^packetization-mode=[01]$/.test(value));
                        if(profiles.length===1)item.h264Profile=profiles[0].split('=')[1].toLowerCase();
                        if(modes.length===1)item.packetizationMode=Number(modes[0].split('=')[1]);
                    }
                    // Retain only bounded numeric counters for this exact stream.
                    // Requested limits and advertised FPS are not measurements.
                    const sample={timestamp:number(report.timestamp),frames:number(report.framesEncoded),sent:number(report.framesSent),
                        bytes:number(report.bytesSent),encode:number(report.totalEncodeTime),qp:number(report.qpSum),
                        codec:codec+':'+(item.h264Profile??'')+':'+(item.packetizationMode??''),
                        width:item.width,height:item.height};
                    const id=typeof report.id==='string'&&report.id.length<=256?report.id:null;
                    const old=id===null?null:prior?.get(id);
                    if(id!==null&&sample.timestamp!==null)samples.set(id,sample);
                    if(old&&sample.timestamp!==null&&old.timestamp!==null&&sample.codec===old.codec&&sample.width===old.width&&sample.height===old.height){
                        const ms=sample.timestamp-old.timestamp;
                        const delta=key=>sample[key]!==null&&old[key]!==null&&sample[key]>=old[key]?sample[key]-old[key]:null;
                        const frames=delta('frames'),bytes=delta('bytes'),encode=delta('encode'),qp=delta('qp'),sent=delta('sent');
                        if(ms>=250&&ms<=30000){
                            item.measurement={milliseconds:ms,encodedFps:frames===null?null:number(frames*1000/ms,240),
                                sentFps:sent===null?null:number(sent*1000/ms,240),bitrateMbps:bytes===null?null:number(bytes*8/ms/1000,1000),
                                meanEncodeMs:frames>0&&encode!==null?number(encode*1000/frames,10000):null,
                                meanQp:frames>0&&qp!==null?number(qp/frames,255):null};
                        }
                    }
                    if(typeof report.powerEfficientEncoder==='boolean')item.powerEfficientEncoder=report.powerEfficientEncoder;
                    const implementation=['OpenH264','libvpx','ExternalEncoder','MediaFoundation','NVENC'].find(name=>
                        typeof report.encoderImplementation==='string'&&report.encoderImplementation.includes(name));
                    if(implementation)item.encoderImplementation=implementation;
                    const remote=typeof raw.get==='function'?raw.get(report.remoteId):null;
                    if(remote?.type==='remote-inbound-rtp'&&(remote.kind??remote.mediaType)==='video')
                        item.remote={roundTripMs:number(remote.roundTripTime*1000,120000),fractionLost:number(remote.fractionLost,1)};
                    outbound.push(item);
                }
                const parameters=readParameters.call(connection);
                if(!Array.isArray(parameters)||parameters.length>4||parameters.some(item=>!item||typeof item!=='object'))
                    fail('stream_diagnostics_unavailable');
                const advertised=parameters.map(item=>({width:dimension(item.maxResolution?.width),height:dimension(item.maxResolution?.height),frameRate:rate(item.maxFrameRate)}));
                const observedNative=typeof globalThis.WidgetRailNativeMedia?.status==='function'?globalThis.WidgetRailNativeMedia.status():null;
                const native=observedNative?.active===true?observedNative:null;
                const source={width:dimension(native?.width),height:dimension(native?.height),browserWidth:dimension(native?.browserWidth),
                    browserHeight:dimension(native?.browserHeight),targetFrameRate:rate(native?.targetFrameRate)};
                let senderInfo;
                const sender=connection.videoTransceiver?.sender;
                if(sender?.track?.kind==='video'&&typeof sender.getParameters==='function'){
                    const parameters=sender.getParameters();
                    if(parameters&&Array.isArray(parameters.encodings)&&parameters.encodings.length<=4){
                        const positive=(value,max)=>Number.isFinite(value)&&value>0&&value<=max?value:null;
                        senderInfo={degradationPreference:['maintain-framerate','maintain-resolution','balanced'].includes(parameters.degradationPreference)?parameters.degradationPreference:null,
                            ...(['detail','motion','text'].includes(sender.track.contentHint)?{contentHint:sender.track.contentHint}:{}),
                            encodings:parameters.encodings.map(e=>({active:typeof e?.active==='boolean'?e.active:null,
                                maxBitrate:positive(e?.maxBitrate,100000000),maxFramerate:positive(e?.maxFramerate,240),
                                scaleResolutionDownBy:positive(e?.scaleResolutionDownBy,64)}))};
                    }
                    if(connection.videoTransceiver?.sender!==sender)fail('stream_diagnostics_stale');
                }
                if(!exact())fail('stream_diagnostics_stale');
                const track=connection.input?.stream?.getVideoTracks?.()[0];
                const desktopTrack=connection.input?.desktop?.stream?.getVideoTracks?.()[0];
                const pc=connection.pc;
                const trackState=value=>['live','ended'].includes(value?.readyState)?value.readyState:null;
                const attachment={nativeFrames:Number.isSafeInteger(native?.frames)&&native.frames>=0?native.frames:null,
                    videoSupported:typeof connection.videoSupported==='boolean'?connection.videoSupported:null,
                    inputTrack:trackState(track),desktopTrack:trackState(desktopTrack),senderTrack:trackState(sender?.track),
                    peerState:['new','connecting','connected','disconnected','failed','closed'].includes(pc?.connectionState)?pc.connectionState:null,
                    signalingState:['stable','have-local-offer','have-remote-offer','have-local-pranswer','have-remote-pranswer','closed'].includes(pc?.signalingState)?pc.signalingState:null,
                    qualityBinding:shareQualityBinding?.observe?.()??null,
                    qualityCheck:qualityWarningForSnapshot()?shareQualityWarning?.check??null:null};
                if(!exact())fail('stream_diagnostics_stale');
                diagnosticsHistory.set(connection,{owner:diagnosticOwner,generation,samples});
                return {source,outbound,advertised,...(senderInfo?{sender:senderInfo}:{}),attachment};
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
            const current=relay;
            if(!current||current.owner!==owner||current.account!==args.account||current.channelId!==args.channelId||
                current.lease!==args.lease||current.key!==args.key||current.userId!==args.userId||
                Date.now()>current.expires||['closed','failed'].includes(current.pc.connectionState))fail('watch_cancelled');
            if(!streams(args.channelId).some(s=>s.key===args.key&&s.userId===args.userId))fail('stream_ended');
            if(watchOwnership(args.key)?.alreadyWatching!==true)fail('watch_cancelled');
            sameCall(args);
            if(relay!==current||!relayCurrent(current))fail('watch_cancelled');
            return current;
        }

        function watchOwnership(key) {
            try {
                const account=me()?.id;
                const streaming=store('ApplicationStreamingStore');
                if(typeof account!=='string'||typeof streaming?.getViewerIds!=='function')return null;
                const viewers=streaming.getViewerIds(key);
                if(!Array.isArray(viewers)||viewers.length>1000||viewers.some(id=>typeof id!=='string'||!/^[0-9]{1,32}$/.test(id)))return null;
                const alreadyWatching=viewers.includes(account);
                const startWatch=code('"STREAM_WATCH",streamKey'),closeWatch=code('"STREAM_CLOSE",streamKey');
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
            return relay===entry&&me()?.id===entry.account&&channelId()===entry.channelId&&Date.now()<=entry.expires&&
                !['closed','failed'].includes(entry.pc.connectionState);
        }

        function refreshWatchDemand(entry) {
            const api=watchDemandApi();
            if(!api)fail('watch_demand_unavailable');
            const connection=api.connections.getRTCConnection(entry.key);
            const manager=connection?._goLiveQualityManager;
            const previous=entry.demand;
            if(previous&&(previous.connection!==connection||previous.manager!==manager)) {
                entry.demand=null;previous.release();
            }
            // STREAM_WATCH establishes the connection asynchronously. Reuse the
            // existing bounded track wait; never instantiate provider internals.
            if(!connection||!manager||!connection._connection)return false;
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
            try { previous.pc?.close(); }catch{}
            // A subscription that predates this adapter belongs to the user.
            if (!preserveSubscription && previous.ownsWatch && me()?.id === previous.account) {
                try { previous.closeWatch(previous.key); } catch { /* No blind retries. */ }
            }
        }

        function stopRelay(preserveSubscription=false) {
            ++relayEpoch;
            const previous = relay;
            relay = null;
            retireRelay(previous,preserveSubscription);
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
            const {alreadyWatching,startWatch}=ownership;
            // A same-stream receiver replacement transfers our own subscription;
            // an existing user subscription is never reclassified as ours.
            const sameStream=alreadyWatching&&relay?.key===stream.key&&relay.account===args.account&&
                relay.channelId===args.channelId&&relay.userId===stream.userId;
            const transfer=sameStream&&relay.ownsWatch===true;
            const closeWatch=transfer?relay.closeWatch:ownership.closeWatch;
            // Allocate before relinquishing the old relay/subscription. A local
            // peer-construction failure must leave its existing owner intact.
            const pc = new RTCPeerConnection({ iceServers: [] });
            const previous=relay,demand=sameStream?previous.demand:null;
            if(demand)previous.demand=null;
            const epoch = ++relayEpoch;
            const entry = relay = { pc, key: stream.key, userId:stream.userId, owner, lease: args.lease, account: args.account,
                channelId: args.channelId, ownsWatch: transfer||!alreadyWatching, closeWatch, expires: Date.now() + 25000, timer: null,demand };
            if(demand)demand.entry=entry;
            retireRelay(previous,transfer);
            const current = () => relayEpoch === epoch && relayCurrent(entry);
            try {
                if (!alreadyWatching) startWatch(stream.descriptor, { noFocus: true });
                let track;
                for (let i = 0; i < 80 && current(); ++i) {
                    track = refreshWatchDemand(entry)?incomingTrack(entry):null;
                    if (track) break;
                    await sleep(100);
                }
                if (!current()) fail('watch_cancelled');
                if (!track) fail('video_unavailable');
                const sender = pc.addTrack(track);
                await pc.setLocalDescription(await pc.createOffer());
                await gather(pc);
                if (!current()) fail('watch_cancelled');
                let changing = false;
                entry.timer = setInterval(async () => {
                  try {
                    if (!current() || !streams(args.channelId).some(s => s.key === stream.key)||
                        watchOwnership(stream.key)?.alreadyWatching!==true) {
                        if (relay === entry) stopRelay();
                        return;
                    }
                    const replacement = refreshWatchDemand(entry)?incomingTrack(entry):null;
                    if (!changing && replacement && sender.track !== replacement) {
                        changing = true;
                        try { await sender.replaceTrack(replacement); } catch { if (relay === entry) stopRelay(); }
                        finally { changing = false; }
                    }
                  }catch(error) {
                    lastFailure={method:'watch',type:'Error',detail:error.adapterCode??'watch_demand_failed'};
                    if(relay===entry)stopRelay();
                  }
                }, 1000);
                return { lease: args.lease, offer: pc.localDescription.toJSON() };
            } catch (error) { if (relay === entry) stopRelay(); throw error; }
        }

        function beginShare(args, owner) {
            sameCall(args);
            if (sharePending || currentShare()) fail('share_already_active');
            const quality=args.captureQuality;
            if(args.encoderPreference!==undefined&&!['discord','prefer-hardware'].includes(args.encoderPreference))fail('share_encoder_unsupported');
            if(args.encoderPreference==='prefer-hardware'&&quality===undefined)fail('share_encoder_unsupported');
            if(args.bandwidthPreference!==undefined&&!['automatic','high-detail'].includes(args.bandwidthPreference))fail('share_bandwidth_unsupported');
            if(args.bandwidthPreference==='high-detail'&&quality===undefined)fail('share_bandwidth_unsupported');
            if(quality!==undefined&&(!quality||typeof quality!=='object'||Array.isArray(quality)||
                Object.keys(quality).some(key=>!['resolution','frameRate'].includes(key))||
                !isCaptureQuality(quality)))fail('share_quality_unsupported');
            if(quality!==undefined)requireShareQuality(quality,args);
            const sourceOptions=quality===undefined?{width:1280,height:720}:
                {width:({480:854,720:1280,1080:1920,1440:2560})[quality.resolution],height:quality.resolution,frameRate:quality.frameRate};
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
            // Explicitly show Vesktop's source/audio chooser. Never auto-select a
            // source or approve a permission dialog. Source ID stays inside Vesktop.
            (async () => {
                let source;
                let handedOff = false;
                let qualityBinding = null;
                try {
                    await globalThis.VesktopNative?.win?.focus?.();
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
                    start(guild, args.channelId, { pid: null, sourceId: source, sourceName: null });
                    if(shareQualityWarning?.epoch===epoch&&shareQualityWarning.stream===null)shareQualityWarning.stream=currentShare()??null;
                    handedOff = true;
                } catch(error) {
                    if(epoch===shareEpoch)shareError=['share_quality_unsupported','share_quality_unconfirmed','share_quality_unavailable'].includes(error.adapterCode)
                        ?error.adapterCode:'share_cancelled_or_failed';
                }
                finally {
                    clearTimeout(timer);
                    if (source != null && !handedOff) { qualityBinding?.dispose();try { engine.desktopInputPool?.get?.(source)?.destroy?.(); } catch {} }
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
                if (method === 'show') { await globalThis.VesktopNative?.win?.focus?.(); return { ok: true, value: {} }; }
                authorize(args);
                switch (method) {
                    case 'voiceMute':case 'voiceVolume':return {ok:true,value:updateVoiceAudio(method,args)};
                    case 'voiceSettings':return {ok:true,value:voiceSettings(args)};
                    case 'voiceInputDevice':case 'voiceOutputDevice':case 'voiceInputVolume':return {ok:true,value:updateVoiceSettings(method,args)};
                    case 'streamDiagnostics':return {ok:true,value:await streamDiagnostics(args)};
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
                    case 'leave': sameCall(args); ++shareEpoch; sharePending = false; stopRelay();
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
                    case 'answer':
                        sameCall(args);
                        if (!relay || relay.lease !== args.lease || relay.owner!==owner || !relayCurrent(relay)) fail('watch_cancelled');
                        await relay.pc.setRemoteDescription(args.answer); break;
                    case 'keep':
                        if (relay?.lease === args.lease && relay.owner===owner && relayCurrent(relay))
                            relay.expires = Date.now() + 12000;
                        break;
                    case 'unwatch': if (relay?.lease === args.lease && relay.owner===owner) stopRelay(); break;
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
        return Object.freeze({ revision, build, invoke, dispose() { ++shareEpoch; sharePending = false;shareQualityBinding?.dispose();shareQualityWarning=null;shareError=null;shareErrorOwner=null; stopRelay(); } });
    })();
}

}
