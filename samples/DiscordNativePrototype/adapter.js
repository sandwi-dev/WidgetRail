// WidgetRail development adapter, v1. No Discord credentials cross this boundary.
// Internal Discord APIs are feature-probed and may disappear after Discord updates.
if (globalThis.WidgetRailDiscordPrototype?.build !== 4) {
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
        const text = value => String(value ?? '').replace(/[\u0000-\u001f]/g, ' ').slice(0, 120);
        const me = () => store('UserStore')?.getCurrentUser?.();
        const channelId = () => store('SelectedChannelStore')?.getVoiceChannelId?.() ?? null;
        const currentShare = () => store('ApplicationStreamingStore')?.getCurrentUserActiveStream?.();
        const streamKey = s => s.guildId ? `guild:${s.guildId}:${s.channelId}:${s.ownerId || s.userId}` : `call:${s.channelId}:${s.ownerId || s.userId}`;
        const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
        let relay = null;
        let relayEpoch = 0;
        let shareEpoch = 0;
        let sharePending = false;
        let shareError = null;
        let shareDeadline = 0;
        let shareOwner = null;
        let shareOwnerDeadline = 0;
        let lastFailure = null;

        function capabilities() {
            const actions = props('toggleSelfMute', 'toggleSelfDeaf');
            return { voice: !!dispatcher()?.dispatch && !!store('VoiceStateStore'),
                mute: typeof actions?.toggleSelfMute === 'function',
                deafen: typeof actions?.toggleSelfDeaf === 'function',
                watch: typeof code('"STREAM_WATCH",streamKey') === 'function',
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
                        if (!c || c.type !== 2 || result.size >= 1000) continue;
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
            const media = store('MediaEngineStore');
            const own = currentShare();
            const participants = Object.values(store('VoiceStateStore')?.getVoiceStatesForChannel?.(id) ?? {})
                .slice(0, 100).map(v => ({ id: v.userId,
                    name: text(store('UserStore')?.getUser?.(v.userId)?.globalName || store('UserStore')?.getUser?.(v.userId)?.username || 'Participant'),
                    muted: !!(v.mute || v.selfMute), deafened: !!(v.deaf || v.selfDeaf),
                    speaking: !!store('SpeakingStore')?.isSpeaking?.(v.userId) }));
            return { revision, ready: !!user && !!wp(), account: user?.id ?? '', capabilities: capabilities(),
                channels: user ? channels() : [], channelId: id, channelName: text(store('ChannelStore')?.getChannel?.(id)?.name),
                muted: !!media?.isSelfMute?.(), deafened: !!media?.isSelfDeaf?.(), participants,
                streams: id ? streams(id).map(({ descriptor, ...value }) => value) : [], sharing: !!own && own.state !== 'ENDED',
                sharePending, shareError, relayState: relay?.pc.connectionState ?? 'closed' };
        }

        function authorize(args) {
            if (!me() || args?.account !== me().id) fail('account_changed');
        }
        function sameCall(args) {
            authorize(args);
            if (!args.channelId || channelId() !== args.channelId) fail('call_changed');
        }

        function stopRelay() {
            ++relayEpoch;
            const previous = relay;
            relay = null;
            if (!previous) return;
            clearInterval(previous.timer);
            previous.pc?.close();
            // A subscription that predates this adapter belongs to the user.
            if (previous.ownsWatch && me()?.id === previous.account) {
                try { code('"STREAM_CLOSE",streamKey')?.(previous.key); } catch { /* No blind retries. */ }
            }
        }

        function incomingTrack(userId) {
            const connections = store('MediaEngineStore')?.getMediaEngine?.()?.connections;
            for (const connection of connections ?? []) {
                if (connection.context !== 'stream' || connection.streamUserId !== userId) continue;
                const pc = Object.values(connection).find(value => value && typeof value.getReceivers === 'function');
                const track = pc?.getReceivers().map(r => r.track).find(t => t?.kind === 'video' && t.readyState === 'live' && !t.muted);
                if (track) return track;
            }
            return null;
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

        async function watch(args) {
            sameCall(args);
            const stream = streams(args.channelId).find(s => s.key === args.key);
            if (!stream || typeof args.lease !== 'string' || !/^[a-f0-9]{32}$/.test(args.lease)) fail('stream_ended');
            const action = code('"STREAM_WATCH",streamKey');
            if (typeof action !== 'function') fail('watch_unavailable');
            stopRelay();
            const epoch = relayEpoch;
            const alreadyWatching = (store('ApplicationStreamingStore')?.getViewerIds?.(stream.key) ?? []).includes(args.account);
            const pc = new RTCPeerConnection({ iceServers: [] });
            const entry = relay = { pc, key: stream.key, lease: args.lease, account: args.account,
                channelId: args.channelId, ownsWatch: !alreadyWatching, expires: Date.now() + 25000, timer: null };
            const current = () => relay === entry && relayEpoch === epoch && me()?.id === args.account && channelId() === args.channelId;
            try {
                if (!alreadyWatching) action(stream.descriptor, { noFocus: true });
                let track;
                for (let i = 0; i < 80 && current(); ++i) {
                    track = incomingTrack(stream.userId);
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
                    if (!current() || Date.now() > entry.expires || !streams(args.channelId).some(s => s.key === stream.key)) {
                        if (relay === entry) stopRelay();
                        return;
                    }
                    const replacement = incomingTrack(stream.userId);
                    if (!changing && replacement && sender.track !== replacement) {
                        changing = true;
                        try { await sender.replaceTrack(replacement); } catch { if (relay === entry) stopRelay(); }
                        finally { changing = false; }
                    }
                }, 1000);
                return { lease: args.lease, offer: pc.localDescription.toJSON() };
            } catch (error) { if (relay === entry) stopRelay(); throw error; }
        }

        function beginShare(args, owner) {
            sameCall(args);
            if (sharePending || currentShare()) fail('share_already_active');
            const engine = store('MediaEngineStore')?.getMediaEngine?.();
            const start = code('"STREAM_START",streamType');
            if (typeof start !== 'function' || typeof engine?.getDesktopSource !== 'function') fail('share_unavailable');
            const epoch = ++shareEpoch;
            sharePending = true;
            shareError = null;
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
                try {
                    await globalThis.VesktopNative?.win?.focus?.();
                    source = await engine.getDesktopSource({ width: 1280, height: 720 }, true);
                    if (!current()) return;
                    const guild = store('ChannelStore')?.getChannel?.(args.channelId)?.guild_id ?? null;
                    start(guild, args.channelId, { pid: null, sourceId: source, sourceName: null });
                    handedOff = true;
                } catch { if (epoch === shareEpoch) shareError = 'share_cancelled_or_failed'; }
                finally {
                    clearTimeout(timer);
                    if (source != null && !handedOff) { try { engine.desktopInputPool?.get?.(source)?.destroy?.(); } catch {} }
                    if (epoch === shareEpoch) sharePending = false;
                }
            })();
            return { requested: true };
        }

        async function invoke({ method, args, owner }) {
            try {
                if (typeof owner !== 'string' || !/^[a-f0-9]{32}$/.test(owner)) fail('invalid_owner');
                if (method === 'snapshot') {
                    if (sharePending && shareOwner === owner) shareOwnerDeadline = Date.now() + 12000;
                    return { ok: true, value: snapshot() };
                }
                if (method === 'diagnostics') return { ok: true, value: lastFailure ?? {} };
                if (method === 'show') { await globalThis.VesktopNative?.win?.focus?.(); return { ok: true, value: {} }; }
                authorize(args);
                switch (method) {
                    case 'join': {
                        const target = channels().find(c => c.id === args.target);
                        if (!target) fail('channel_unavailable');
                        if (channelId() !== target.id) dispatcher().dispatch({ type: 'VOICE_CHANNEL_SELECT', channelId: target.id, guildId: target.guildId });
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
                    case 'watch': return { ok: true, value: await watch(args) };
                    case 'answer':
                        sameCall(args);
                        if (!relay || relay.lease !== args.lease) fail('watch_cancelled');
                        await relay.pc.setRemoteDescription(args.answer); break;
                    case 'keep':
                        if (relay?.lease === args.lease) relay.expires = Date.now() + 12000;
                        break;
                    case 'unwatch': if (relay?.lease === args.lease) stopRelay(); break;
                    case 'cancelPendingShare':
                        if (shareOwner === owner) { ++shareEpoch; sharePending = false; }
                        break;
                    case 'share': return { ok: true, value: beginShare(args, owner) };
                    case 'stopShare': {
                        sameCall(args); ++shareEpoch; sharePending = false;
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
        return Object.freeze({ revision, build: 4, invoke, dispose() { ++shareEpoch; sharePending = false; stopRelay(); } });
    })();
}
