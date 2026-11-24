// Discord Web synthesizes a remote answer from a server-supplied transport
// description. Its answer builder hardcodes a codec hint of 2500 kb/s even
// when its own Go Live quality manager selects a higher video budget.
function matchesCaptureFormat(quality, format) {
    return quality?.width === format.width && quality.height === format.height && quality.framerate === format.fps;
}

function bindGeneratedVideoBandwidth(session, isCurrent) {
    const {connection, pc, sender} = session;
    const transceiver = connection.videoTransceiver;
    const original = connection.generateSDPAnswer;
    const descriptor = Object.getOwnPropertyDescriptor(connection, 'generateSDPAnswer');
    if (typeof original !== 'function' || typeof connection.handleNegotiationNeeded !== 'function' ||
        !/\bvideoBitRate\s*:\s*2500\b/.test(Function.prototype.toString.call(original))) return null;

    function applicableMaximum() {
        if (!isCurrent() || connection.pc !== pc || connection.videoTransceiver !== transceiver ||
            transceiver?.sender !== sender || transceiver.mid == null) return null;
        const base = connection.sdp;
        if (typeof base !== 'string' || base.length > 131072 || !/^c=/m.test(base) ||
            /^b=|x-google-max-bitrate|max-br\s*=/im.test(base)) return null;
        const quality = connection.videoQualityManager?.goliveMaxQuality;
        const encodings = sender.getParameters().encodings;
        if (!matchesCaptureFormat(quality?.capture, session.format) ||
            encodings?.length !== 1 || encodings[0].active === false) return null;
        const maximum = quality.bitrateMax;
        return Number.isSafeInteger(maximum) && maximum > 2500000 && maximum <= 2147483647 ? maximum : null;
    }
    if (!applicableMaximum()) return null;
    function wrapper(...args) {
        const result = original.apply(this, args);
        if (this !== connection) return result;
        try {
            const maximum = applicableMaximum();
            const answer = result?.answer;
            if (!maximum || answer?.type !== 'answer' || typeof answer.sdp !== 'string' ||
                answer.sdp.length > 131072 || /^b=/m.test(answer.sdp)) return result;
            const payload = connection.videoPayloadType;
            if (!Number.isInteger(payload) || payload < 0 || payload > 127) return result;
            const parts = answer.sdp.split(/(?=^m=)/m);
            const matches = parts.filter(part => {
                const lines = part.split(/\r?\n/);
                return part.startsWith('m=video ') && lines.includes('a=mid:' + transceiver.mid) &&
                    lines.some(line => line.toLowerCase() === 'a=rtpmap:' + payload + ' h264/90000') &&
                    lines.some(line => line.startsWith('a=fmtp:' + payload + ' ') &&
                        /[ ;]x-google-max-bitrate=2500(?:;|$)/i.test(line));
            });
            if (matches.length !== 1) return result;
            const video = matches[0];
            const address = video.match(/^c=[^\r\n]+/gm);
            if (address?.length !== 1) return result;
            const newline = video.includes('\r\n') ? '\r\n' : '\n';
            const codecLines = video.split(newline).filter(line => line.startsWith('a=fmtp:' + payload + ' '));
            if (codecLines.length !== 1) return result;
            const codecLine = codecLines[0];
            const hints = codecLine.match(/[ ;]x-google-max-bitrate=[^;\s]+/gi);
            if (hints?.length !== 1) return result;
            const selectedCodec = codecLine.replace(/\bx-google-max-bitrate=2500(?=;|$)/i,
                'x-google-max-bitrate=' + Math.floor(maximum / 1000));
            parts[parts.indexOf(video)] = video.replace(codecLine, () => selectedCodec)
                .replace(address[0], () => address[0] + newline + 'b=TIAS:' + maximum);
            return {...result, answer:new RTCSessionDescription({type:'answer', sdp:parts.join('')})};
        } catch { return result; }
    }
    if (descriptor && !descriptor.configurable) return null;
    Object.defineProperty(connection, 'generateSDPAnswer', {value:wrapper, writable:true, configurable:true});
    return () => {
        if (connection.generateSDPAnswer !== wrapper) return;
        if (descriptor) Object.defineProperty(connection, 'generateSDPAnswer', descriptor);
        else delete connection.generateSDPAnswer;
    };
}

// An encoded source cannot follow an explicit spatial or frame-rate cap.
function usesFullCaptureSize(encoding, format) {
    const size = encoding?.scaleResolutionDownTo;
    return !!encoding && (encoding.scaleResolutionDownBy ?? 1) === 1 &&
        (!size || (size.width >= format.width && size.height >= format.height)) &&
        (encoding.maxFramerate == null || encoding.maxFramerate >= format.fps);
}

// This is a local Go Live preset calculation. Leave transport feedback, the
// actual pixel request and any later provider policy or negotiation intact.
async function bindPreviewBitratePreset(session, current, hasViewer, videoRateLimits) {
    const {connection, pc, sender} = session;
    const manager = connection.videoQualityManager;
    const method = 'getGoliveQuality';
    const original = manager?.[method];
    if (typeof original !== 'function' || manager.connection !== connection || manager.contextType !== 'stream' ||
        typeof connection.updateVideoQuality !== 'function') return null;
    // Exact method bodies inspected in the provider builds. A change in the
    // calculation must be reviewed before the candidate can opt in again.
    const bytes = new TextEncoder().encode(Function.prototype.toString.call(original));
    const digest = await crypto.subtle.digest('SHA-256', bytes);
    const hash = Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('');
    if (hash !== '0584534300c0e6470dbdeb77e5e3fe9ce2fc3a7727cdaef92f69b16a0c734dc4' &&
        hash !== '7657209cc4e211a54715ed33526eb8c3f639d0e137efff957590f397002e1eec') return null;
    if (!current() || connection.videoQualityManager !== manager || manager[method] !== original) return null;
    const descriptor = Object.getOwnPropertyDescriptor(manager, method);
    if (descriptor && (!descriptor.configurable || !('value' in descriptor))) return null;
    let applied = false, lastViewer = false;

    function primaryDemand() {
        const streams = connection.videoStreamParameters;
        if (!Array.isArray(streams) || streams.length !== 1 || streams[0].quality !== 100 ||
            streams[0].active === false || !Number.isInteger(streams[0].ssrc) || streams[0].ssrc <= 0) return null;
        return connection.getRemoteVideoSinkPixelCount?.(streams[0].ssrc);
    }

    function eligible() {
        if (!current() || connection.videoQualityManager !== manager || connection.pc !== pc ||
            connection.videoTransceiver?.sender !== sender || manager.qualityOverwrite != null ||
            manager.fakeGoLiveEncodePixelCount != null || !hasViewer()) return false;
        const encodings = sender.getParameters().encodings;
        const preset = manager.goliveMaxQuality;
        const base = connection.sdp;
        const negotiated = videoRateLimits(pc.remoteDescription, connection.videoTransceiver.mid, connection.videoPayloadType);
        return encodings?.length === 1 && encodings[0].active !== false && encodings[0].maxBitrate !== 0 &&
            usesFullCaptureSize(encodings[0], session.format) && matchesCaptureFormat(preset?.capture, session.format) &&
            matchesCaptureFormat(preset?.encode, session.format) && preset.encode.pixelCount === session.format.width * session.format.height &&
            Number.isSafeInteger(preset.bitrateMax) && preset.bitrateMax > 0 &&
            !negotiated.otherBandwidth && (negotiated.media == null || negotiated.media >= preset.bitrateMax) &&
            negotiated.hint >= preset.bitrateMax &&
            typeof base === 'string' && base.length <= 131072 && /^c=/m.test(base) &&
            !/^b=|x-google-max-bitrate|max-br\s*=/im.test(base);
    }
    function wrapped(localWant, pixels) {
        const result = original.apply(this, arguments);
        if (this !== manager) return result;
        try {
            const preset = manager.goliveMaxQuality;
            // Calls with unknown size also happen for unrelated preset queries.
            if (!(localWant > 0 && Number.isSafeInteger(pixels) && pixels > 0)) return result;
            applied = false;
            if (!eligible() || pixels !== primaryDemand() || pixels >= preset.encode.pixelCount || result === preset ||
                !Number.isFinite(result?.bitrateMax) || result.bitrateMax <= 0 || result.bitrateMax >= preset.bitrateMax) return result;
            const sizeMaximum = Math.ceil(pixels * preset.bitrateMax / preset.encode.pixelCount);
            const motionFloor = Math.min(0.02 * preset.encode.pixelCount * preset.encode.framerate, preset.bitrateMax);
            if (result.bitrateMax !== Math.max(sizeMaximum, motionFloor)) return result;
            const previousMaximum = sender.getParameters().encodings[0].maxBitrate;
            if (previousMaximum != null && previousMaximum < preset.bitrateMax && previousMaximum !== result.bitrateMax) return result;
            // The recognized small-view branch returns a fresh quality object.
            // Preserve its type, target, minimum, frame rate and geometry.
            result.bitrateMax = preset.bitrateMax;
            applied = result.bitrateMax === preset.bitrateMax;
        } catch { /* Preserve the provider result when optional state is unavailable. */ }
        return result;
    }
    Object.defineProperty(manager, method, {value:wrapped, configurable:true, writable:true, enumerable:descriptor?.enumerable ?? false});
    return {
        get applied() { return applied && manager[method] === wrapped; },
        sync() {
            if (manager[method] !== wrapped) return;
            const viewer = hasViewer();
            if (viewer === lastViewer && (viewer || !applied)) return;
            lastViewer = viewer;
            // The roster and first pixel request can arrive in either order.
            // Recalculate on that edge, never continually rewrite sender settings.
            if (viewer && eligible()) {
                const pixels = primaryDemand(), preset = manager.goliveMaxQuality;
                if (!(pixels > 0 && pixels < preset.encode.pixelCount)) return;
                const prior = original.call(manager, 100, pixels);
                if (sender.getParameters().encodings[0].maxBitrate !== prior.bitrateMax) return;
                connection.updateVideoQuality();
            } else if (applied) {
                applied = false;
                if (sender.getParameters().encodings?.[0]?.maxBitrate === manager.goliveMaxQuality?.bitrateMax)
                    connection.updateVideoQuality();
            }
        },
        release(refresh) {
            if (manager[method] !== wrapped) return;
            if (descriptor) Object.defineProperty(manager, method, descriptor);
            else delete manager[method];
            if (applied && refresh && connection.videoQualityManager === manager &&
                sender.getParameters().encodings?.[0]?.maxBitrate === manager.goliveMaxQuality?.bitrateMax) connection.updateVideoQuality();
            applied = false;
        }
    };
}

const h264Levels = [
    // level_idc, MaxFS, MaxMBPS, baseline MaxBR (kb/s)
    [31, 3600, 108000, 14000], [32, 5120, 216000, 20000], [40, 8192, 245760, 20000],
    [41, 8192, 245760, 50000], [42, 8704, 522240, 50000], [50, 22080, 589824, 135000],
    [51, 36864, 983040, 240000], [52, 36864, 2073600, 240000]
];

function externalH264Format(codecs, payloadType, format, remoteLevel) {
    const codec = codecs?.find(item => item.payloadType === payloadType && item.mimeType?.toLowerCase() === 'video/h264');
    const profile = codec?.sdpFmtpLine?.match(/(?:^|;)\s*profile-level-id=(42e0[0-9a-f]{2})(?=;|$)/i)?.[1];
    if (!profile || !Number.isInteger(payloadType) || payloadType < 0 || payloadType > 127) return null;
    const level = Math.min(parseInt(profile.slice(4), 16), remoteLevel);
    const columns = Math.ceil(format.width / 16), rows = Math.ceil(format.height / 16);
    const picture = columns * rows;
    const fits = ([, maxPicture, maxRate]) => picture <= maxPicture && picture * format.fps <= maxRate &&
        columns ** 2 <= 8 * maxPicture && rows ** 2 <= 8 * maxPicture;
    const native = h264Levels.find(fits);
    const negotiated = h264Levels.find(([id]) => id === level);
    if (!native || !negotiated || !fits(negotiated)) return null;
    return { payloadType, profile: '42e0' + level.toString(16), minimumLevel: native[0], maximumLevel: level,
        nativeBitrateLimit: native[3] * 1000, codecBitrateLimit: negotiated[3] * 1000 };
}

// Opt-in candidate only. Discord owns the peer connection, encryption, and sender limits.
function createExperimentalCapture({ store, me, channelId, currentShare, streamKey, currentEpoch, peekDesktopSource, videoRateLimits }) {
    let cancel, active, observed;
    const native = () => globalThis.WidgetRailNativeMedia?.status?.();
    const ceiling = encoding => encoding?.active === false ? 0 : (encoding?.maxBitrate ?? null);

    function prepare(args, quality, engine, sourceId, epoch) {
        cancel?.();
        const capture = native();
        if (!quality || capture?.quality !== quality.resolution + 'p' + quality.frameRate ||
            !['detail', 'motion'].includes(capture.contentPreference)) return;
        // Smoothness keeps the adaptive raw-track encoder. Only Clarity may
        // replace the raw track with the fixed-size external encoder.
        const useExternalEncoder = capture.contentPreference === 'detail';
        const selected = peekDesktopSource(engine, sourceId), track = selected?.stream?.getVideoTracks?.()[0];
        if (!track) return;
        const constraints = track.getConstraints?.();
        const bound = value => typeof value === 'number' ? value : value?.ideal ?? value?.max;
        const format = { width: bound(constraints?.width), height: bound(constraints?.height), fps: quality.frameRate };
        if (!Number.isInteger(format.width) || !Number.isInteger(format.height)) return;
        const pool = engine.desktopInputPool, guild = store('ChannelStore')?.getChannel?.(args.channelId)?.guild_id;
        const key = guild ? 'guild:' + guild + ':' + args.channelId + ':' + args.account : 'call:' + args.channelId + ':' + args.account;
        let policySession;
        let closed = false, timer, missingSince;
        function captureOwned() {
            return !closed && currentEpoch() === epoch && me()?.id === args.account && channelId() === args.channelId &&
                track.readyState === 'live' && engine.desktopInputPool === pool && selected.stream?.getVideoTracks?.()[0] === track &&
                store('MediaEngineStore')?.getMediaEngine?.() === engine && native()?.active === true;
        }
        function owner() {
            if (!captureOwned()) return null;
            const own = currentShare(), rtc = store('StreamRTCConnectionStore')?.getRTCConnection?.(key), connection = rtc?._connection;
            if (!own || own.state === 'ENDED' || streamKey(own) !== key || (own.ownerId || own.userId) !== args.account ||
                own.channelId !== args.channelId || rtc?.isOwner !== true || rtc._destroyed !== false || rtc.context !== 'stream' ||
                rtc.streamKey !== key || rtc.streamContext?.channelId !== args.channelId || rtc.streamContext?.ownerId !== args.account ||
                connection?.destroyed !== false || connection.context !== 'stream' || connection.userId !== args.account ||
                connection.streamUserId !== args.account || !engine.connections?.has(connection) || connection.input?.desktop !== selected) return null;
            const pc = connection.pc, transceiver = connection.videoTransceiver;
            return pc && transceiver?.sender ? { rtc, connection, pc, sender: transceiver.sender, format } : null;
        }
        function sameOwner(session, now = owner()) {
            return now?.rtc === session.rtc && now.connection === session.connection && now.pc === session.pc && now.sender === session.sender;
        }
        async function retire(session, recover) {
            if (session.stopped) return;
            session.stopped = true;
            session.nativeReader?.close();
            session.worker?.terminate();
            if (session.url) URL.revokeObjectURL(session.url);
            try {
                if (recover && session.detached && session.sender.track === null && sameOwner(session)) await session.sender.replaceTrack(track);
            } finally {
                if (active === session) active = null;
            }
        }
        function failed(session) {
            if (policySession && sameOwner(session, policySession)) {
                policySession.rejectedFormat = session.videoFormat;
                policySession.fallback = 'encoder_failed';
            }
            retire(session, true).catch(() => {});
        }
        function releasePolicy(refresh) {
            const session = policySession;
            if (!session) return;
            policySession = null;
            if (observed === session) observed = null;
            try { session.presetPolicy?.release(refresh); }
            finally { session.restoreBandwidth?.(); }
        }
        function dispose() {
            try { releasePolicy(!!policySession && sameOwner(policySession)); } catch { /* The provider is closing. */ }
            if (active) retire(active, false).catch(() => {});
            closed = true;
            clearTimeout(timer);
            track.removeEventListener('ended', dispose);
            if (cancel === dispose) cancel = null;
        }
        async function configurePolicy(session) {
            const current = () => policySession === session && sameOwner(session);
            try {
                session.restoreBandwidth = bindGeneratedVideoBandwidth(session, current);
                if (session.restoreBandwidth) {
                    let deadline;
                    try {
                        await Promise.race([session.connection.handleNegotiationNeeded(),
                            new Promise(resolve => deadline = setTimeout(resolve, 5000))]);
                    } catch {
                        session.restoreBandwidth();
                        session.restoreBandwidth = null;
                    } finally { clearTimeout(deadline); }
                }
                if (!current()) return;
                const policy = await bindPreviewBitratePreset(session, current, () => {
                    const viewers = store('ApplicationStreamingStore')?.getViewerIds?.(key);
                    return Array.isArray(viewers) && viewers.length <= 1000 &&
                        viewers.some(id => typeof id === 'string' && /^[0-9]{1,32}$/.test(id) && id !== args.account) &&
                        currentShare()?.state !== 'PAUSED';
                }, videoRateLimits);
                if (current()) session.presetPolicy = policy;
                else policy?.release(false);
            } catch { /* Unknown provider implementations keep their own quality calculation. */ }
            finally { session.ready = true; }
        }
        async function attach(session, codec, providerCeiling) {
            active = session;
            session.lastReport = performance.now();
            const current = () => active === session && !session.stopped && sameOwner(session);
            try {
                const stats = await session.sender.getStats();
                if (!current() || session.sender.track !== track) return retire(session, false);
                const outbound = Array.from(stats.values()).find(r => r.type === 'outbound-rtp' && r.kind === 'video');
                const initialTarget = outbound?.targetBitrate > 0 ? outbound.targetBitrate : null;
                const script = '(' + runEncodedSourceWorker.toString() + ')()';
                session.url = URL.createObjectURL(new Blob([script], { type: 'text/javascript' }));
                session.worker = new Worker(session.url);
                session.worker.onerror = () => failed(session);
                session.worker.onmessage = ({ data }) => {
                    if (session.stopped) return;
                    if (data.kind === 'nativeRate') { session.nativeReader?.update(data.bitrate, data.keyframe); return; }
                    if (data.kind === 'nativeClosed') { session.nativeReader?.close(); session.nativeReader = null; return; }
                    if (data.kind === 'failed') { failed(session); return; }
                    if (data.kind === 'sample') {
                        session.lastReport = performance.now();
                        session.sample = data;
                    }
                };
                const readable = new MediaStreamTrackProcessor({ track }).readable;
                await session.sender.replaceTrack(null);
                session.detached = true;
                // A failed worker can retire while replaceTrack is still pending.
                if (session.stopped && !active && sameOwner(session) && session.sender.track === null) {
                    await session.sender.replaceTrack(track);
                    return;
                }
                if (!current()) return retire(session, true);
                await session.sender.createEncodedSource(session.worker);
                if (!current()) return retire(session, true);
                session.ceiling = providerCeiling;
                session.nativeReader = globalThis.WidgetRailNativeMedia?.openH264?.(unit => {
                    if (session.stopped) return;
                    session.worker.postMessage({kind:'nativeUnit',unit},unit?[unit.data]:[]);
                });
                session.worker.postMessage({ ...codec, ...session.videoFormat, initialTarget, ceiling: providerCeiling,
                    nativeEncoder:!!session.nativeReader, readable }, [readable]);
                session.attached = true;
            } catch { failed(session); }
        }
        function sameFormat(left, right) {
            return left?.width === right.width && left?.height === right.height && left?.fps === right.fps;
        }

        function negotiatedCodec(session, params, format) {
            const payload = session.connection.videoPayloadType;
            const limit = videoRateLimits(session.pc.remoteDescription, session.connection.videoTransceiver.mid, payload);
            return externalH264Format(params.codecs, payload, format, limit.h264Level);
        }

        function check() {
            try {
                if (!captureOwned()) return dispose();
                const now = owner();
                if (now) missingSince = null;
                else missingSince ??= performance.now();
                if (policySession && ((now && !sameOwner(policySession, now)) ||
                    (!now && performance.now() - missingSince > 10000))) releasePolicy(false);
                if (!policySession && now && now.sender.track === track && !now.connection.negotiating &&
                    now.pc.signalingState === 'stable' &&
                    matchesCaptureFormat(now.connection.videoQualityManager?.goliveMaxQuality?.capture, format)) {
                    observed = policySession = { ...now, useExternalEncoder };
                    configurePolicy(policySession);
                }
                if (policySession?.ready && now) policySession.presetPolicy?.sync();
                if (active) {
                    const session = active;
                    if ((!now && performance.now() - missingSince > 10000) ||
                        (now && (!sameOwner(session, now) || (session.detached && session.sender.track === track)))) {
                        retire(session, false).catch(() => {});
                    } else if (performance.now() - session.lastReport > 10000) {
                        failed(session);
                    } else if (now && session.attached && !session.stopped) {
                        const params = session.sender.getParameters(), currentCapture = native();
                        const videoFormat = {width: currentCapture.width, height: currentCapture.height, fps: format.fps};
                        const codec = negotiatedCodec(now, params, session.videoFormat);
                        if (!sameFormat(session.videoFormat, videoFormat) || codec?.profile !== session.codec.profile ||
                            codec.payloadType !== session.codec.payloadType ||
                            params.encodings?.length !== 1 || !usesFullCaptureSize(params.encodings[0], session.videoFormat)) {
                            retire(session, true).catch(() => failed(session));
                        } else {
                            const limit = ceiling(params.encodings[0]);
                            if (limit !== session.ceiling) {
                                session.ceiling = limit;
                                session.worker.postMessage({ kind: 'ceiling', ceiling: limit });
                            }
                        }
                    }
                }
                if (useExternalEncoder && !active && now && policySession?.ready && now.sender.track === track &&
                    !now.connection.negotiating && now.pc.signalingState === 'stable') {
                    const params = now.sender.getParameters(), currentCapture = native();
                    const videoFormat = {width: currentCapture.width, height: currentCapture.height, fps: format.fps};
                    const validSize = Number.isInteger(videoFormat.width) && Number.isInteger(videoFormat.height) &&
                        videoFormat.width >= 2 && videoFormat.height >= 2 && videoFormat.width <= format.width &&
                        videoFormat.height <= format.height && videoFormat.width % 2 === 0 && videoFormat.height % 2 === 0;
                    const codec = validSize && negotiatedCodec(now, params, videoFormat);
                    if (!codec) policySession.fallback = 'negotiated_format';
                    else if (typeof now.sender.createEncodedSource !== 'function') policySession.fallback = 'runtime';
                    else if (sameFormat(policySession.rejectedFormat, videoFormat)) policySession.fallback = 'encoder_failed';
                    else if (params.encodings?.length !== 1 || !usesFullCaptureSize(params.encodings[0], videoFormat))
                        policySession.fallback = 'provider_scaling';
                    else {
                        policySession.fallback = null;
                        attach({ ...now, videoFormat, codec }, codec, ceiling(params.encodings[0]));
                    }
                }
            } catch {
                if (active) failed(active);
            }
            if (!closed) timer = setTimeout(check, 1000);
        }
        cancel = dispose;
        track.addEventListener('ended', dispose, { once: true });
        timer = setTimeout(check, 300);
    }
    return {
        prepare, dispose: () => cancel?.(),
        sample(connection) {
            const policy = observed?.connection === connection ? observed : null;
            const encoder = active?.connection === connection && !active.stopped ? active : null;
            if (!policy && !encoder) return null;
            const preparing = policy?.useExternalEncoder && !policy.fallback && !encoder?.detached;
            return {
                presetBitrateActive: policy?.presetPolicy?.applied === true,
                ...(encoder?.sample ? {externalEncoder: encoder.sample} : {}),
                encoderPath: encoder?.sample?.encoderPath ?? (encoder?.detached ? 'starting' : 'webrtc'),
                encoderFallback: policy?.fallback ?? (preparing ? 'external_pending' : null)
            };
        }
    };
}
