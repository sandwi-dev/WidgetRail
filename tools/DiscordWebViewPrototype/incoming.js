(() => {
    let pc=null;
    globalThis.incoming={
        async connect(offer){
            pc?.close();const next=pc=new RTCPeerConnection({iceServers:[]});
            next.ontrack=e=>document.querySelector('video').srcObject=new MediaStream([e.track]);
            await next.setRemoteDescription(offer);await next.setLocalDescription(await next.createAnswer());
            if(next.iceGatheringState!=='complete')await new Promise((resolve,reject)=>{
                const timer=setTimeout(()=>{next.removeEventListener('icegatheringstatechange',changed);reject(Error('ice_timeout'));},5000);
                function changed(){if(next.iceGatheringState==='complete'){clearTimeout(timer);next.removeEventListener('icegatheringstatechange',changed);resolve();}}
                next.addEventListener('icegatheringstatechange',changed);changed();
            });
            return next.localDescription.toJSON();
        },
        stop(){pc?.close();pc=null;document.querySelector('video').srcObject=null;},
        async status(){const records=[];if(pc)for(const r of (await pc.getStats()).values())if(r.type==='inbound-rtp'&&r.kind==='video')records.push({framesDecoded:r.framesDecoded,bytesReceived:r.bytesReceived});return {connection:pc?.connectionState,records};}
    };
})();
