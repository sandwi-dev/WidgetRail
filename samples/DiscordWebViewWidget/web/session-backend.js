// Package-owned endpoint over the shared Discord domain adapter. No transport IDs or manual fragments.
(() => {
    'use strict';
    host.observeMainView?.();
    const observation = typeof host.invalidate === 'function' && typeof createWidgetRailDiscordObserver === 'function'
        ? createWidgetRailDiscordObserver(() => globalThis.WidgetRailWebWebpack, () => host.invalidate()) : null;
    globalThis.addEventListener?.('pagehide', () => observation?.dispose(), {once:true});
    let fingerprint = null;
    let version = null;
    const validQuality=q=>q&&typeof q==='object'&&!Array.isArray(q)&&[480,720,1080].includes(q.resolution)&&
        [15,30,60].includes(q.frameRate)&&(q.resolution!==1080||q.frameRate!==60);
    const commands = new Set(['enableNotifications', 'joinActivity', 'watchActivity', 'join', 'leave', 'mute', 'deafen', 'streamMute', 'streamVolume', 'streamDiagnostics', 'connectionDiagnostics', 'incomingDiagnostics', 'videoPlayback', 'voiceMute', 'voiceVolume', 'voiceSettings', 'voiceInputDevice', 'voiceOutputDevice', 'voiceInputVolume', 'watch', 'reconnectVideo', 'keep', 'unwatch', 'share', 'cancelPendingShare', 'stopShare']);
    for(const command of ['messagingState','messageObserve','messageConversations','messageLocateConversation','messageHistory','messageMarkRead','messageChanges','messageRecover','messageOpenDm','messagePrepareAttachments','messageSend','messageReconcile','messageNotificationTarget'])commands.add(command);
    for(const command of ['callStart','callAnswer','callDecline'])commands.add(command);
    async function snapshot(owner) {
        observation?.refresh();
        const response = await WidgetRailDiscordPrototype.invoke({ method: 'snapshot', owner });
        if (!response.ok) throw new Error('Discord unavailable');
        const state = response.value;
        if(state.calls!=null && (typeof state.calls!=='object'||!Array.isArray(state.calls.incoming)||state.calls.incoming.length>8||
            !Array.isArray(state.calls.activeChannels)||state.calls.activeChannels.length>100))throw new Error('Invalid private call state');
        if(state.connectionState!==undefined && (!['initializing','signed-out','ready','reconnecting','unavailable'].includes(state.connectionState) ||
            state.ready!==(state.connectionState==='ready')))throw new Error('Invalid connection state');
        if (!Array.isArray(state.channels) || state.channels.length > 1000 ||
            !Array.isArray(state.participants) || state.participants.length > 100 ||
            !Array.isArray(state.streams) || state.streams.length > 50) throw new Error('Invalid Discord catalog');
        if(state.activities!==undefined&&(!Array.isArray(state.activities)||state.activities.length>50))throw new Error('Invalid Discord activity');
        if(state.activityAvailability!==undefined&&!['ready','unavailable','disconnected'].includes(state.activityAvailability))throw new Error('Invalid activity availability');
        if(state.shareQualities!==undefined&&(!Array.isArray(state.shareQualities)||state.shareQualities.length>8||
            state.shareQualities.some(q=>!validQuality(q))||new Set(state.shareQualities.map(q=>q.resolution+':'+q.frameRate)).size!==state.shareQualities.length))
            throw new Error('Invalid share qualities');
        if(state.shareQualityAvailability!==undefined&&!['ready','unavailable'].includes(state.shareQualityAvailability))throw new Error('Invalid quality availability');
        if((state.shareQualityAvailability??'unavailable')==='unavailable'&&state.shareQualities?.some(q=>q.resolution>720||q.frameRate>30))
            throw new Error('Invalid share qualities');
        if(state.preferredShareQuality!=null&&!validQuality(state.preferredShareQuality))throw new Error('Invalid preferred quality');
        const next = JSON.stringify([state.account, state.channels]);
        if (next !== fingerprint) { fingerprint = next; version = crypto.randomUUID().replaceAll('-', ''); }
        return state;
    }
    const result = (context, items) => items.length ? host.batchResult(context, items) : { context, items: [] };
    host.onRequest(async (method, request, { signal }) => {
        if (signal.aborted) throw new Error('Cancelled');
        if (!request || typeof request.owner !== 'string' || !/^[a-f0-9]{32}$/.test(request.owner)) throw new Error('Invalid owner');
        if (method === 'snapshot') {
            const { channels, participants, streams, activities = [], ...state } = await snapshot(request.owner);
            const context = { ...state, channelVersion: version, channelCount: channels.length };
            if (signal.aborted) throw new Error('Cancelled');
            return result(context, [...participants.map(value => ({ kind: 'participant', value })),
                ...streams.map(value => ({ kind: 'stream', value })),...activities.map(value=>({kind:'activity',value}))]);
        }
        if (method === 'channels') {
            const state = await snapshot(request.owner);
            if (signal.aborted) throw new Error('Cancelled');
            if (request.account !== state.account || request.channelVersion !== version) throw new Error('Channel catalog changed');
            return result({ account: state.account, channelVersion: version }, state.channels);
        }
        if (method !== 'command' || !commands.has(request.method)) throw new Error('Command unavailable');
        if (request.method === 'watch' || request.method === 'watchActivity') {
            if(typeof request.args?.videoView!=='string'||!/^[a-f0-9]{32}$/.test(request.args.videoView))
                throw new Error('A live host video view is required');
        }
        if (request.method === 'share') {
            const quality = request.args?.captureQuality;
            if (!validQuality(quality) || Object.keys(quality).some(key => !['resolution', 'frameRate'].includes(key)))
                throw new Error('Capture quality unavailable');
            // Never fall through to Discord's ordinary browser capture chooser.
            // The host must have installed its native, ticket-bound transport.
            if (typeof globalThis.WidgetRailNativeMedia?.start !== 'function') return { ok:false, code:'native_capture_unavailable' };
        }
        // Account, call, channel membership, desired-state and stream lease checks remain in the domain adapter.
        const response=await WidgetRailDiscordPrototype.invoke({ method: request.method, args: request.args, owner: request.owner });
        if(response.ok && ['messageConversations','messageHistory','messageChanges','messageRecover'].includes(request.method)) {
            if(signal.aborted)throw new Error('Cancelled');
            const {items,...context}=response.value;
            if(!Array.isArray(items)||items.length>50)throw new Error('Invalid message page');
            return result({ok:true,value:context},items);
        }
        if(['streamDiagnostics','connectionDiagnostics','incomingDiagnostics','voiceSettings'].includes(request.method)&&signal.aborted)throw new Error('Cancelled');
        return response;
    });
})();
