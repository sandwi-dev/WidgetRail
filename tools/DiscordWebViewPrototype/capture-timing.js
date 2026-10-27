// Validation page only. Aggregate stage timings without retaining frames,
// mappings or unbounded sample histories. Production never loads this asset.
(() => {
    const aggregate=()=>({count:0,totalMs:0,maxMs:0});
    const construction=aggregate(),writes=aggregate(),acknowledgements=aggregate();
    let pendingWrites=0,maximumPendingWrites=0;
    const received=new Map();
    const record=(target,elapsed)=>{if(Number.isFinite(elapsed)&&elapsed>=0){target.count++;target.totalMs+=elapsed;target.maxMs=Math.max(target.maxMs,elapsed);}};
    const Frame=globalThis.VideoFrame,Generator=globalThis.MediaStreamTrackGenerator;
    globalThis.VideoFrame=new Proxy(Frame,{construct(target,args,newTarget){
        const started=performance.now();try{return Reflect.construct(target,args,newTarget);}finally{record(construction,performance.now()-started);}
    }});
    globalThis.MediaStreamTrackGenerator=new Proxy(Generator,{construct(target,args,newTarget){
        const track=Reflect.construct(target,args,newTarget);
        if(args[0]?.kind==='video'){
            const get=track.writable.getWriter.bind(track.writable);
            track.writable.getWriter=()=>{
                const writer=get(),write=writer.write.bind(writer);
                writer.write=frame=>{
                    const started=performance.now();pendingWrites++;maximumPendingWrites=Math.max(maximumPendingWrites,pendingWrites);
                    const finish=()=>{pendingWrites--;record(writes,performance.now()-started);};
                    try{return Promise.resolve(write(frame)).then(value=>{finish();return value;},error=>{finish();throw error;});}
                    catch(error){finish();throw error;}
                };
                return writer;
            };
        }
        return track;
    }});
    const listen=chrome.webview.addEventListener.bind(chrome.webview),post=chrome.webview.postMessage.bind(chrome.webview);
    chrome.webview.addEventListener=(name,callback)=>listen(name,name==='sharedbufferreceived'?event=>{
        const m=event.additionalData;
        if(m?.kind===1&&Number.isSafeInteger(m.sequence)&&received.size<8)received.set(m.sequence,performance.now());
        return callback(event);
    }:callback);
    chrome.webview.postMessage=message=>{
        if(message?.captureAck===1&&received.has(message.captureSequence)){
            const started=received.get(message.captureSequence);received.delete(message.captureSequence);
            record(acknowledgements,performance.now()-started);
        }
        return post(message);
    };
    const snapshot=value=>({...value,meanMs:value.count?value.totalMs/value.count:null});
    globalThis.captureProbeTiming=()=>({construction:snapshot(construction),writerWrite:snapshot(writes),
        receivedToAckSent:snapshot(acknowledgements),pendingWrites,maximumPendingWrites,pendingAcknowledgements:received.size,
        scope:'Validation-only JS timings; ACK ends at send, excluding host return delivery'});
})();
