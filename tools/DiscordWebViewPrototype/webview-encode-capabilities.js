// Local-only capability queries; this does not create a peer connection,
// capture source, encoder session or microphone stream.
(() => {
    if (location.origin !== 'https://widgetrail-gpu-probe.test') return false;
    globalThis.widgetrailEncodeCapabilities = { ready: false };
    (async () => {
        const result = { ready: true, secureContext: isSecureContext, queries: [] };
        if (typeof RTCRtpSender?.getCapabilities !== 'function' || typeof navigator.mediaCapabilities?.encodingInfo !== 'function') {
            result.unavailable = 'capability-api-unavailable';
            return result;
        }
        const codecs = RTCRtpSender.getCapabilities('video')?.codecs ?? [];
        const seen = new Set();
        const h264 = codecs.filter(codec => /^video\/h264$/i.test(codec.mimeType ?? '')).slice(0, 8)
            .map(codec => ({ mimeType: codec.mimeType, fmtp: codec.sdpFmtpLine ?? '' }))
            .filter(codec => { const key = codec.mimeType + ';' + codec.fmtp; if (seen.has(key)) return false; seen.add(key); return true; });
        result.h264Capabilities = h264;
        const modes = [{ width: 1920, height: 1080, framerate: 60 }, { width: 2560, height: 1440, framerate: 30 }, { width: 2560, height: 1440, framerate: 60 }];
        result.queries = await Promise.all(h264.flatMap(codec => modes.map(async mode => {
            const contentType = codec.mimeType + (codec.fmtp ? ';' + codec.fmtp : '');
            const query = { contentType, ...mode, bitrate: 18000000 };
            let timeout;
            try {
                const value = await Promise.race([
                    navigator.mediaCapabilities.encodingInfo({ type: 'webrtc', video: query }),
                    new Promise((_, reject) => timeout = setTimeout(() => reject(new DOMException('timeout', 'TimeoutError')), 3000))
                ]);
                return { ...query, supported: value.supported, smooth: value.smooth, powerEfficient: value.powerEfficient };
            } catch (error) {
                return { ...query, errorType: ['TypeError', 'NotSupportedError', 'TimeoutError', 'SecurityError'].includes(error?.name) ? error.name : 'Error' };
            } finally { clearTimeout(timeout); }
        })));
        return result;
    })().then(result => { globalThis.widgetrailEncodeCapabilities = result; }, () => {
        globalThis.widgetrailEncodeCapabilities = { ready: true, unavailable: 'capability-query-failed' };
    });
    return true;
})();
