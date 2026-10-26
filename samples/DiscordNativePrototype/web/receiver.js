(() => {
    'use strict';
    let pc = null;
    let lease = null;
    const video = document.getElementById('video');
    const status = document.getElementById('status');
    function close() { pc?.close(); pc = null; lease = null; video.srcObject = null; status.textContent = 'Stream stopped.'; }
    WidgetRailWebSession.handle('stop', () => { close(); return {}; });
    WidgetRailWebSession.handle('status', () => ({ lease, state: pc?.connectionState ?? 'closed', width: video.videoWidth, height: video.videoHeight }));
    WidgetRailWebSession.handle('offer', async (data, { signal }) => {
        close();
        if (!/^[a-f0-9]{32}$/.test(data.lease) || data.offer?.type !== 'offer' || typeof data.offer.sdp !== 'string' || data.offer.sdp.length > 60000) throw new Error('Invalid offer');
        const current = pc = new RTCPeerConnection({ iceServers: [] });
        lease = data.lease;
        const abort = () => { if (pc === current) close(); else current.close(); };
        signal.addEventListener('abort', abort, { once: true });
        try {
            current.ontrack = event => {
                if (pc !== current || event.track.kind !== 'video') return;
                video.srcObject = new MediaStream([event.track]);
                video.play().catch(() => { status.textContent = 'Playback could not start.'; });
            };
            current.onconnectionstatechange = () => {
                if (pc === current) status.textContent = current.connectionState === 'connected' ? '' : `Stream: ${current.connectionState}`;
            };
            await current.setRemoteDescription(data.offer);
            await current.setLocalDescription(await current.createAnswer());
            const deadline = Date.now() + 4000;
            while (current.iceGatheringState !== 'complete') {
                if (signal.aborted || pc !== current || Date.now() > deadline) throw new Error('ICE incomplete');
                await new Promise(resolve => setTimeout(resolve, 40));
            }
            if (signal.aborted || pc !== current) throw new Error('Canceled');
            return { lease, answer: current.localDescription.toJSON() };
        } catch (error) { abort(); throw error; }
        finally { signal.removeEventListener('abort', abort); }
    });
    window.addEventListener('pagehide', close);
    WidgetRailWebSession.post({ ready: true });
})();
