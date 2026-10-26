// Local WebRTC encode/decode test of the exact transport used by Discord.
(() => {
    let sender,receiver,inbound=[],generation=0;
    const video=document.querySelector('video');
    const stop=()=>{++generation;sender?.close();receiver?.close();sender=receiver=null;video.srcObject=null;};
    globalThis.captureProbe={
        async connect(){
            stop();inbound=[];const id=generation;
            const stream=await navigator.mediaDevices.getDisplayMedia();
            sender=new RTCPeerConnection({iceServers:[]});receiver=new RTCPeerConnection({iceServers:[]});
            const tx=sender,rx=receiver;
            tx.onicecandidate=e=>{if(e.candidate)rx.addIceCandidate(e.candidate).catch(()=>{});};
            rx.onicecandidate=e=>{if(e.candidate)tx.addIceCandidate(e.candidate).catch(()=>{});};
            const received=new MediaStream();
            rx.ontrack=e=>{if(id!==generation)return;received.addTrack(e.track);video.srcObject=received;};
            stream.getTracks().forEach(t=>tx.addTrack(t,stream));
            await tx.setLocalDescription(await tx.createOffer());await rx.setRemoteDescription(tx.localDescription);
            await rx.setLocalDescription(await rx.createAnswer());await tx.setRemoteDescription(rx.localDescription);
            return true;
        },stop,
        status(){
            const current=receiver;
            current?.getStats().then(stats=>{
                if(current!==receiver)return;inbound=[];
                for(const r of stats.values())if(r.type==='inbound-rtp')inbound.push({kind:r.kind,framesDecoded:r.framesDecoded,bytesReceived:r.bytesReceived,totalSamplesReceived:r.totalSamplesReceived});
            }).catch(()=>{});
            return {...WidgetRailNativeMedia.keep(),connection:sender?.connectionState,inbound,width:video.videoWidth,height:video.videoHeight};
        }
    };
    chrome.webview.postMessage({ready:typeof MediaStreamTrackGenerator==='function'&&typeof VideoFrame==='function'});
})();
