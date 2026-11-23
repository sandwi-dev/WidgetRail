// Package-owned endpoint over the shared Discord domain adapter. No transport IDs or manual fragments.
(() => {
    'use strict';
    let fingerprint = null;
    let version = null;
    const validQuality=q=>q&&typeof q==='object'&&!Array.isArray(q)&&[480,720,1080,1440].includes(q.resolution)&&[15,30,60].includes(q.frameRate);
    const commands = new Set(['joinActivity', 'watchActivity', 'join', 'leave', 'mute', 'deafen', 'streamMute', 'streamVolume', 'streamDiagnostics', 'voiceMute', 'voiceVolume', 'voiceSettings', 'voiceInputDevice', 'voiceOutputDevice', 'voiceInputVolume', 'watch', 'answer', 'keep', 'unwatch', 'share', 'cancelPendingShare', 'stopShare']);
    async function snapshot(owner) {
        const response = await WidgetRailDiscordPrototype.invoke({ method: 'snapshot', owner });
        if (!response.ok) throw new Error('Discord unavailable');
        const state = response.value;
        if (!Array.isArray(state.channels) || state.channels.length > 1000 ||
            !Array.isArray(state.participants) || state.participants.length > 100 ||
            !Array.isArray(state.streams) || state.streams.length > 50) throw new Error('Invalid Discord catalog');
        if(state.activities!==undefined&&(!Array.isArray(state.activities)||state.activities.length>50))throw new Error('Invalid Discord activity');
        if(state.activityAvailability!==undefined&&!['ready','unavailable','disconnected'].includes(state.activityAvailability))throw new Error('Invalid activity availability');
        if(state.shareQualities!==undefined&&(!Array.isArray(state.shareQualities)||state.shareQualities.length>12||
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
        if (request.method === 'share') {
            const bandwidth = request.args?.bandwidthPreference;
            if (bandwidth !== undefined && !['automatic', 'high-detail'].includes(bandwidth)) throw new Error('Stream bandwidth unavailable');
            const encoder = request.args?.encoderPreference;
            if (encoder !== undefined && !['discord', 'prefer-hardware'].includes(encoder)) throw new Error('Stream encoder preference unavailable');
            const quality = request.args?.captureQuality;
            if (!quality || Object.keys(quality).some(key => !['resolution', 'frameRate'].includes(key)) ||
                ![480,720,1080,1440].includes(quality.resolution) || ![15,30,60].includes(quality.frameRate)) throw new Error('Capture quality unavailable');
            // Never fall through to Discord's ordinary browser capture chooser.
            // The host must have installed its native, ticket-bound transport.
            if (typeof globalThis.WidgetRailNativeMedia?.start !== 'function') return { ok:false, code:'native_capture_unavailable' };
        }
        // Account, call, channel membership, desired-state and stream lease checks remain in the domain adapter.
        const response=await WidgetRailDiscordPrototype.invoke({ method: request.method, args: request.args, owner: request.owner });
        if(['streamDiagnostics','voiceSettings'].includes(request.method)&&signal.aborted)throw new Error('Cancelled');
        return response;
    });
})();
