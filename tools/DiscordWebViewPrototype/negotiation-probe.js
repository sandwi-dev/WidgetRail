// Tool-only real WebRTC check. The shipping adapter is loaded verbatim by the
// runner; only Discord's stores/source ownership and answer construction are
// modeled here. All pixels are generated; ICE has no STUN or TURN servers.
(() => {
    if (location.origin !== 'https://widgetrail-negotiation-probe.test') return;
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    const check = (condition, checkpoint) => { if (!condition) throw Object.assign(Error(checkpoint), { checkpoint }); };
    async function until(predicate, milliseconds, checkpoint) {
        const end = performance.now() + milliseconds;
        while (!predicate()) { check(performance.now() < end, checkpoint); await sleep(50); }
    }
    // Diagnostic extraction only. No SDP, ICE credentials or addresses leave
    // this page. This is not the adapter's answer-correction implementation.
    function h264(sdp, mid) {
        const section = String(sdp).split(/(?=^m=)/m).find(value => value.split(/\r?\n/).includes('a=mid:' + mid));
        if (!section) return null;
        const map = /^a=rtpmap:(\d+) H264\/90000\r?$/im.exec(section);
        if (!map) return null;
        const line = section.split(/\r?\n/).find(value => value.startsWith('a=fmtp:' + map[1] + ''));
        const profile = /(?: |;)profile-level-id=([0-9a-f]{6})(?:;|$)/i.exec(line ?? '')?.[1]?.toLowerCase();
        const packetization = /(?: |;)packetization-mode=([01])(?:;|$)/i.exec(line ?? '')?.[1];
        return { payload: Number(map[1]), profile: profile ?? null, packetizationMode: packetization === undefined ? null : Number(packetization) };
    }
    function discordAnswer(answer, mid, payload) {
        // Model the cached Discord browser's synthetic answer (725999/eR):
        // H264's profile is Constrained Baseline regardless of its local offer.
        // The production adapter, not this fixture, must perform the correction.
        let inVideo = false, target = false, changed = 0;
        const sections = answer.sdp.split(/(?=^m=)/m);
        const sdp = sections.map(section => {
            inVideo = section.startsWith('m=video ');
            target = section.split(/\r?\n/).includes('a=mid:' + mid);
            if (!inVideo || !target) return section;
            return section.split('\r\n').map(line => {
                if (!line.startsWith('a=fmtp:' + payload + ' ')) return line;
                const next = line.replace(/profile-level-id=42001f(?=;|$)/i, () => { changed++; return 'profile-level-id=42e01f'; });
                return next;
            }).join('\r\n');
        }).join('');
        check(changed === 1, 'model-constrained-answer');
        return { type: answer.type, sdp };
    }

    async function runCase(encoderPreference) {
        const result = { encoderPreference, passed: false, checkpoint: 'setup', source: 'generated canvas', width: 320, height: 180 };
        const canvas = document.createElement('canvas'); canvas.width = 320; canvas.height = 180;
        const video = document.createElement('video'); video.muted = true; video.autoplay = true; video.playsInline = true;
        video.width = 320; video.height = 180; document.body.append(canvas, video);
        const draw = canvas.getContext('2d'); let frame = 0, receivedTrack = null, timer = null, stream = null;
        const senderPc = new RTCPeerConnection({ iceServers: [] }), receiverPc = new RTCPeerConnection({ iceServers: [] });
        const nativeSetRemote = senderPc.setRemoteDescription; let nativeCalls = 0;
        // Count actual native setter calls without substituting its behavior.
        senderPc.setRemoteDescription = function () { nativeCalls++; return nativeSetRemote.apply(this, arguments); };
        const observedSetRemote = senderPc.setRemoteDescription;
        const listeners = new Set(), connections = new Set(); let active = null, metadata = null, rtc = null, sourceTaken = 0;
        let attachment = Promise.resolve();
        const transceiver = senderPc.addTransceiver('video', { direction: 'sendrecv' });
        const sourceId = 'generated-canvas', key = 'guild:10:20:1';
        const engine = {
            connections,
            desktopInputPool: { pool: {}, get(id) { const value = this.pool[id]; delete this.pool[id]; sourceTaken++; return value; } },
            getDesktopSource: async () => sourceId,
            on(event, callback) { if (event === 'connection') listeners.add(callback); },
            removeListener(event, callback) { if (event === 'connection') listeners.delete(callback); }
        };
        const stores = {
            UserStore: { getCurrentUser: () => ({ id: '1' }) },
            SelectedChannelStore: { getVoiceChannelId: () => '20' },
            ChannelStore: { getChannel: id => id === '20' ? { id: '20', guild_id: '10' } : null },
            ApplicationStreamingStore: { getCurrentUserActiveStream: () => active,
                getStreamerActiveStreamMetadataForStream: requested => requested === key ? metadata : null },
            StreamRTCConnectionStore: { getRTCConnection: requested => requested === key ? rtc : null },
            MediaEngineStore: { getMediaEngine: () => engine, getGoLiveSource: () => active ? { desktopSource: { id: sourceId } } : null }
        };
        let connection;
        function start() {
            active = { guildId: '10', channelId: '20', ownerId: '1', state: 'ACTIVE' }; metadata = { sourceId };
            connection = { context: 'stream', userId: '1', streamUserId: '1', destroyed: false, videoSupported: true,
                pc: senderPc, videoTransceiver: transceiver, videoCodec: 'H264', videoPayloadType: null,
                videoStreamParameters: [{ quality: 100, ssrc: 1, maxResolution: { type: 'fixed', width: 320, height: 180 }, maxFrameRate: 15 }],
                getRawStats: () => senderPc.getStats(), getStreamParameters() { return this.videoStreamParameters; },
                setStream(value) { this.streamVideoTrack = value.getVideoTracks()[0]; attachment = transceiver.sender.replaceTrack(this.streamVideoTrack); },
                setDesktopInput(value) { this.input = { desktop: value, stream: value.stream }; this.setStream(value.stream); }
            };
            connection.videoQualityManager = { contextType: 'stream', connection, goliveMaxQuality: {},
                setGoliveQuality(value) { this.goliveMaxQuality = value; } };
            connections.add(connection);
            for (const callback of listeners) callback(connection);
            rtc = { context: 'stream', streamKey: key, _destroyed: false, isOwner: true,
                streamContext: { ownerId: '1', channelId: '20' }, _connection: connection };
            connection.setDesktopInput(engine.desktopInputPool.get(sourceId));
        }
        globalThis.Vencord = { Webpack: {
            findStore: name => stores[name],
            findByProps: (...names) => names.includes('SOURCE') ? { SOURCE: 'source', FIXED: 'fixed' } : undefined,
            findByCode: pattern => pattern === '"STREAM_START",streamType' ? start : pattern === '"STREAM_STOP",streamKey' ? () => { active = metadata = null; } : undefined
        } };
        const invoke = (method, args = {}) => globalThis.WidgetRailDiscordPrototype.invoke({ method, owner: 'a'.repeat(32), args: { account: '1', channelId: '20', ...args } });
        receiverPc.ontrack = event => { receivedTrack = event.track; video.srcObject = new MediaStream([event.track]); video.play().catch(() => {}); };
        try {
            const paint = () => { frame++; draw.fillStyle = frame % 2 ? '#14253d' : '#254b68'; draw.fillRect(0, 0, 320, 180);
                draw.fillStyle = '#f4d35e'; draw.fillRect((frame * 7) % 280, 55, 40, 40); };
            paint(); stream = canvas.captureStream(15); timer = setInterval(paint, 67);
            const track = stream.getVideoTracks()[0];
            result.checkpoint = 'source-constraints';
            await track.applyConstraints({ width: { max: 320 }, height: { max: 180 }, frameRate: { max: 15 } });
            engine.desktopInputPool.pool[sourceId] = { stream, destroy() { stream.getTracks().forEach(item => item.stop()); } };
            const capabilities = RTCRtpSender.getCapabilities('video').codecs;
            const baseline = capabilities.filter(codec => codec.mimeType.toLowerCase() === 'video/h264' &&
                /(?:^|;)profile-level-id=42001f(?:;|$)/i.test(codec.sdpFmtpLine ?? '') && /(?:^|;)packetization-mode=1(?:;|$)/.test(codec.sdpFmtpLine ?? ''));
            check(baseline.length === 1, 'baseline-codec-capability');
            transceiver.setCodecPreferences([...baseline, ...capabilities.filter(codec => codec.mimeType.toLowerCase() === 'video/rtx')]);
            initializeWidgetRailDiscordAdapter(globalThis.negotiationProbeBuild);
            result.checkpoint = 'adapter-share';
            const admitted = await invoke('share', { captureQuality: { resolution: 480, frameRate: 15 }, encoderPreference });
            check(admitted.ok && admitted.value?.requested, 'adapter-share-admission');
            await until(() => connection?.input?.desktop, 3000, 'adapter-source-attachment');
            await attachment;
            result.setterWrapped = senderPc.setRemoteDescription !== observedSetRemote;
            check(result.setterWrapped === (encoderPreference === 'prefer-hardware'), 'adapter-setter-hook');
            result.checkpoint = 'sender-local-offer';
            await senderPc.setLocalDescription(await senderPc.createOffer());
            await until(() => senderPc.iceGatheringState === 'complete', 6000, 'sender-ice-gathering');
            const offered = h264(senderPc.localDescription.sdp, transceiver.mid);
            check(offered?.profile === '42001f' && offered.packetizationMode === 1, 'real-offer-baseline');
            connection.videoPayloadType = offered.payload;
            result.senderOfferProfile = offered.profile;
            result.checkpoint = 'receiver-remote-offer';
            await receiverPc.setRemoteDescription(senderPc.localDescription);
            const answer = discordAnswer(await receiverPc.createAnswer(), transceiver.mid, offered.payload);
            result.checkpoint = 'receiver-local-constrained-answer';
            await receiverPc.setLocalDescription(answer);
            await until(() => receiverPc.iceGatheringState === 'complete', 6000, 'receiver-ice-gathering');
            result.receiverLocalProfile = h264(receiverPc.localDescription.sdp, transceiver.mid)?.profile;
            check(result.receiverLocalProfile === '42e01f', 'receiver-retains-constrained-answer');
            result.checkpoint = 'adapter-real-remote-answer';
            await senderPc.setRemoteDescription(receiverPc.localDescription);
            const expected = encoderPreference === 'prefer-hardware' ? '42001f' : '42e01f';
            result.senderRemoteProfile = h264(senderPc.remoteDescription.sdp, transceiver.mid)?.profile;
            check(result.senderRemoteProfile === expected, 'sender-accepted-expected-profile');
            result.checkpoint = 'decoded-generated-frames';
            const end = performance.now() + 8000;
            while (performance.now() < end) {
                const stats = await receiverPc.getStats();
                const inbound = [...stats.values()].find(item => item.type === 'inbound-rtp' && item.kind === 'video');
                if ((inbound?.framesDecoded ?? 0) >= 3) {
                    result.framesDecoded = inbound.framesDecoded; result.decodedWidth = inbound.frameWidth; result.decodedHeight = inbound.frameHeight;
                    const codec = stats.get(inbound.codecId); result.receiverCodec = codec?.mimeType;
                    result.receiverCodecProfile = /(?:^|;)profile-level-id=([0-9a-f]{6})(?:;|$)/i.exec(codec?.sdpFmtpLine ?? '')?.[1]?.toLowerCase() ?? null;
                    break;
                }
                await sleep(100);
            }
            check(result.framesDecoded >= 3 && result.decodedWidth === 320 && result.decodedHeight === 180, 'decode-at-least-three-generated-frames');
            const outboundStats = await senderPc.getStats(), outbound = [...outboundStats.values()].find(item => item.type === 'outbound-rtp' && item.kind === 'video');
            const codec = outboundStats.get(outbound?.codecId); result.senderCodec = codec?.mimeType;
            result.senderCodecProfile = /(?:^|;)profile-level-id=([0-9a-f]{6})(?:;|$)/i.exec(codec?.sdpFmtpLine ?? '')?.[1]?.toLowerCase() ?? null;
            check(result.senderCodec === 'video/H264' && result.senderCodecProfile === expected &&
                result.receiverCodec === 'video/H264' && result.receiverCodecProfile === '42e01f', 'actual-codec-profile-observation');
            const diagnostic = await invoke('streamDiagnostics');
            result.adapterProfile = diagnostic.ok ? diagnostic.value.attachment.qualityBinding?.appliedProfile ?? null : null;
            result.nativeSetterCalls = nativeCalls; result.sourceTransfers = sourceTaken;
            check(nativeCalls === 1 && sourceTaken === 1, 'single-setter-and-source-transfer');
            check(encoderPreference !== 'prefer-hardware' || result.adapterProfile === '42001f', 'adapter-real-readback');
            result.passed = true; result.checkpoint = 'complete';
        } catch (error) {
            result.checkpoint = error.checkpoint ?? result.checkpoint;
            result.errorType = ['TypeError', 'InvalidAccessError', 'InvalidStateError', 'OperationError', 'NotSupportedError'].includes(error?.name) ? error.name : 'Error';
            result.nativeSetterCalls = nativeCalls;
        } finally {
            clearInterval(timer);
            globalThis.WidgetRailDiscordPrototype?.dispose(); delete globalThis.WidgetRailDiscordPrototype;
            result.setterRestored = senderPc.setRemoteDescription === observedSetRemote;
            senderPc.close(); receiverPc.close(); stream?.getTracks().forEach(track => track.stop()); receivedTrack?.stop();
            video.pause(); video.srcObject = null; canvas.remove(); video.remove();
            result.peersClosed = senderPc.connectionState === 'closed' && receiverPc.connectionState === 'closed';
            result.tracksEnded = !stream || stream.getTracks().every(track => track.readyState === 'ended');
            delete globalThis.Vencord;
        }
        return result;
    }
    globalThis.negotiationProbeResult = { ready: false };
    (async () => {
        const cases = [];
        for (const preference of ['discord', 'prefer-hardware']) cases.push(await runCase(preference));
        globalThis.negotiationProbeResult = { ready: true, adapterBuild: globalThis.negotiationProbeBuild,
            passed: cases.every(value => value.passed && value.peersClosed && value.tracksEnded && value.setterRestored), cases,
            scope: 'Real WebView2 peers and exact adapter; generated320x180 canvas; no account, microphone, native window capture, external ICE server or performance claim.' };
    })().catch(() => { globalThis.negotiationProbeResult = { ready: true, passed: false, checkpoint: 'fixture-initialization' }; });
})();
