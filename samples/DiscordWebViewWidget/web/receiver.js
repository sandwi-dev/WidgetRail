(() => {
    'use strict';
    let pc = null;
    let lease = null;
    const video = document.getElementById('video');
    const status = document.getElementById('status');
    function close() { const old=pc;pc=null;lease=null;old?.close();video.srcObject=null;status.textContent='Stream stopped.'; }
    WidgetRailWebSession.handle('stop', () => { close(); return {}; });
    WidgetRailWebSession.handle('status', () => ({ lease, state: pc?.connectionState ?? 'closed', width: video.videoWidth, height: video.videoHeight }));
    WidgetRailWebSession.handle('diagnostics', data => {
        if(!pc||typeof data?.lease!=='string'||data.lease!==lease)throw new Error('Receiver lease changed');
        const count=value=>Number.isSafeInteger(value)&&value>=0&&value<=1000000000000?value:null;
        const dimension=value=>Number.isInteger(value)&&value>=0&&value<=16384?value:null;
        const quality=typeof video.getVideoPlaybackQuality==='function'?video.getVideoPlaybackQuality():null;
        return {available:true,lease,state:pc.connectionState,
            width:dimension(video.videoWidth),height:dimension(video.videoHeight),
            paused:typeof video.paused==='boolean'?video.paused:null,
            readyState:Number.isInteger(video.readyState)&&video.readyState>=0&&video.readyState<=4?video.readyState:null,
            totalFrames:count(quality?.totalVideoFrames),droppedFrames:count(quality?.droppedVideoFrames)};
    });
    WidgetRailWebSession.handle('offer', async (data, { signal }) => {
        if (typeof data?.lease!=='string'||!/^[a-f0-9]{32}$/.test(data.lease) || data.offer?.type !== 'offer' || typeof data.offer.sdp !== 'string' || data.offer.sdp.length > 60000) throw new Error('Invalid offer');
        close();
        const current = pc = new RTCPeerConnection({ iceServers: [] });
        lease = data.lease;
        const selectedLease=data.lease;let terminalReported=false,selectedTrack=null;
        const report=phase=>{
            if(pc!==current||lease!==selectedLease||terminalReported||data.healthEvents!==true)return;
            terminalReported=true;
            // Opt-in, one terminal event per lease: legacy clients only drain ready.
            WidgetRailWebSession.post({receiverHealth:1,lease:selectedLease,phase});
        };
        const abort = () => { if (pc === current) close(); else current.close(); };
        signal.addEventListener('abort', abort, { once: true });
        try {
            current.ontrack = event => {
                if (pc !== current || event.track.kind !== 'video') return;
                const track=selectedTrack=event.track;
                video.srcObject = new MediaStream([track]);
                track.addEventListener('ended',()=>{if(selectedTrack===track)report('track_ended');},{once:true});
                video.play().catch(() => {
                    if(pc!==current||lease!==selectedLease||selectedTrack!==track)return;
                    status.textContent='Playback could not start.';report('playback_failed');
                });
            };
            current.onconnectionstatechange = () => {
                if(pc!==current)return;
                status.textContent=current.connectionState==='connected'?'':`Stream: ${current.connectionState}`;
                // disconnected may recover; only definitive states retire the native relay.
                if(current.connectionState==='failed')report('rtc_failed');
                else if(current.connectionState==='closed')report('rtc_closed');
            };
            await current.setRemoteDescription(data.offer);
            await current.setLocalDescription(await current.createAnswer());
            const deadline = Date.now() + 4000;
            while (current.iceGatheringState !== 'complete') {
                if (signal.aborted || pc !== current || Date.now() > deadline) throw new Error('ICE incomplete');
                await new Promise(resolve => setTimeout(resolve, 40));
            }
            if (signal.aborted || pc !== current) throw new Error('Canceled');
            return { lease:selectedLease, answer: current.localDescription.toJSON() };
        } catch (error) { abort(); throw error; }
        finally { signal.removeEventListener('abort', abort); }
    });
    window.addEventListener('pagehide', close);
    WidgetRailWebSession.post({ ready: true });
})();
