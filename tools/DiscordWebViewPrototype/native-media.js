// Main world, Discord origin only. No native window handles or credentials.
(() => {
    if (!['https://discord.com','https://capture-probe.widgetrail.test'].includes(location.origin) || window !== top) return;
    let epoch=null, video=null, audio=null, vw=null, aw=null, stream=null, armed=false, deadline=0;
    let frames=0,samples=0,lease=0;
    let width=0,height=0,firstFrame,resolveFirst;
    let readSettings=()=>({}),outbound=[],measuring=false,lastMeasure=0;
    async function measureSending(){
        if(measuring||Date.now()-lastMeasure<1500)return;
        measuring=true;lastMeasure=Date.now();const measuredEpoch=epoch;
        try{
            const entries=[],seen=new Set();
            const connections=globalThis.WidgetRailWebWebpack?.findStore('MediaEngineStore')?.getMediaEngine?.()?.connections??[];
            for(const connection of connections){
                if(connection.context!=='stream')continue;
                const pc=Object.values(connection).find(v=>v&&typeof v.getSenders==='function'&&typeof v.getStats==='function');
                if(!pc||seen.has(pc))continue;seen.add(pc);
                for(const r of (await pc.getStats()).values())if(r.type==='outbound-rtp'&&r.kind==='video')entries.push({width:r.frameWidth,height:r.frameHeight,fps:r.framesPerSecond,frames:r.framesEncoded,bytes:r.bytesSent,limitation:r.qualityLimitationReason});
            }
            if(measuredEpoch===epoch)outbound=entries.slice(0,8);
        }catch{if(measuredEpoch===epoch)outbound=[];}finally{measuring=false;}
    }
    const stop=()=>{
        epoch=null;armed=false;resolveFirst?.(false);resolveFirst=null;stream?.getTracks().forEach(t=>t.stop());
        vw?.abort().catch(()=>{});aw?.abort().catch(()=>{});
        video=audio=vw=aw=stream=null;readSettings=()=>({});outbound=[];
    };
    const original=navigator.mediaDevices.getDisplayMedia.bind(navigator.mediaDevices);
    // This explicitly-scoped development profile never falls through to a second
    // chooser. A native Start action is the only source of an authorized stream.
    navigator.mediaDevices.getDisplayMedia=async()=>{
        if(!armed||!stream||Date.now()>deadline)throw new DOMException('Choose an application in WidgetRail first.','NotAllowedError');
        const selectedEpoch=epoch,selected=stream;
        armed=false;chrome.webview.postMessage({captureConsume:true,captureEpoch:epoch});
        let timeout;
        let available;
        try{available=await Promise.race([firstFrame,new Promise(resolve=>timeout=setTimeout(()=>resolve(false),5000))]);}
        finally{clearTimeout(timeout);}
        if(!available||epoch!==selectedEpoch)throw new DOMException('The selected window did not supply a frame.','AbortError');
        return selected;
    };
    globalThis.WidgetRailNativeMedia={
        start(id,withAudio){
            stop();epoch=id;frames=samples=width=height=0;lease=Date.now()+10000;
            firstFrame=new Promise(resolve=>resolveFirst=resolve);
            video=new MediaStreamTrackGenerator({kind:'video'});vw=video.writable.getWriter();
            video.contentHint='detail';
            const originalSettings=video.getSettings?.bind(video);
            readSettings=()=>originalSettings?.()??{};
            video.getSettings=()=>{const actual=readSettings();return {...actual,width:actual.width||width,height:actual.height||height,frameRate:actual.frameRate||30,displaySurface:'window',logicalSurface:true,cursor:'never'};};
            if(withAudio){audio=new MediaStreamTrackGenerator({kind:'audio'});aw=audio.writable.getWriter();}
            stream=new MediaStream([video,...(audio?[audio]:[])]);armed=true;deadline=Date.now()+15000;
            return true;
        },stop,
        keep(){lease=Date.now()+10000;void measureSending();const actual=readSettings();return {frames,samples,width,height,active:!!stream,armed,outbound,browserWidth:actual.width,browserHeight:actual.height};},
        dispose(){stop();navigator.mediaDevices.getDisplayMedia=original;}
    };
    setInterval(()=>{if(stream&&Date.now()>lease)stop();},1000);
    chrome.webview.addEventListener('sharedbufferreceived',async event=>{
        const buffer=event.getBuffer(),m=event.additionalData;let frame;
        try{
            if(m.epoch!==epoch)return;
            if(m.kind===1){frame=new VideoFrame(buffer,{format:'BGRA',codedWidth:m.width,codedHeight:m.height,timestamp:m.time});width=m.width;height=m.height;resolveFirst?.(true);resolveFirst=null;await vw.write(frame);if(m.epoch===epoch)frames++;}
            else if(m.kind===2&&aw){frame=new AudioData({format:'s16',sampleRate:48000,numberOfChannels:2,numberOfFrames:m.bytes/4,timestamp:m.time,data:new Uint8Array(buffer,0,m.bytes)});await aw.write(frame);if(m.epoch===epoch)samples+=m.bytes/4;}
        }catch(e){if(m.epoch===epoch){chrome.webview.postMessage({captureError:e.name,captureEpoch:m.epoch});stop();}}
        finally{frame?.close();chrome.webview.releaseBuffer(buffer);chrome.webview.postMessage({captureAck:m.kind,captureEpoch:m.epoch});}
    });
})();
