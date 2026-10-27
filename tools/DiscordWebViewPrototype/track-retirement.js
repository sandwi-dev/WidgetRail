// Real-browser synthetic sources only: no getUserMedia, native source, Discord, or network.
(() => {
    'use strict';
    const results=[];let complete=false,error=null;
    globalThis.captureProbe={status:()=>({complete,error,results,userAgent:navigator.userAgent}),stop:()=>{}};
    const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
    async function bounded(promise,ms=800){
        let timer;
        try{return await Promise.race([Promise.resolve(promise).then(value=>value===undefined?{state:'fulfilled'}:{state:'fulfilled',value},e=>({state:'rejected',name:e.name})),
            new Promise(resolve=>timer=setTimeout(()=>resolve({state:'pending'}),ms))]);}
        finally{clearTimeout(timer);}
    }
    function frame(kind,index){
        return kind==='video'?new VideoFrame(new Uint8Array(16*16*4),{format:'RGBA',codedWidth:16,codedHeight:16,timestamp:index*33333}):
            new AudioData({format:'s16',sampleRate:48000,numberOfChannels:1,numberOfFrames:480,timestamp:index*10000,data:new Int16Array(480)});
    }
    async function examine(kind,method,queued,readers){
        const generator=new MediaStreamTrackGenerator({kind}),clone=generator.clone(),writer=generator.writable.getWriter();
        const owned=[generator,clone],events=[0,0],inactive=[0,0],streams=owned.map(track=>new MediaStream([track]));
        const evidence={kind,method,queued,readers};
        owned.forEach((track,index)=>track.addEventListener('ended',()=>events[index]++));
        streams.forEach((stream,index)=>stream.addEventListener('inactive',()=>inactive[index]++));
        const pending=[];const readable=[];let written=0,rejected=0;
        try{
            if(readers){for(const track of owned){const reader=new MediaStreamTrackProcessor({track,maxBufferSize:1}).readable.getReader();readable.push(reader);pending.push(bounded(reader.read().then(result=>{result.value?.close();return {done:result.done};})));}}
            const writes=[];
            for(let i=0;i<queued;i++){
                const sample=frame(kind,i);writes.push(writer.write(sample).then(()=>{written++;},()=>{rejected++;}).finally(()=>sample.close()));
            }
            evidence.pendingWritesAtRetire=queued-written-rejected;
            const began=performance.now();
            if(method==='track-stop'){generator.stop();evidence.retire={state:'synchronous'};}
            else if(method==='close')evidence.retire=await bounded(writer.close());
            else if(method==='abort')evidence.retire=await bounded(writer.abort());
            else if(method==='stop-then-abort'){generator.stop();evidence.retire=await bounded(writer.abort());}
            evidence.retireMilliseconds=performance.now()-began;
            evidence.writeCompletion=await bounded(Promise.all(writes));
            await wait(120);
            evidence.states=owned.map(track=>track.readyState);evidence.events=events.slice();evidence.inactive=inactive.slice();
            evidence.streamActive=streams.map(stream=>stream.active);evidence.written=written;evidence.rejected=rejected;
            evidence.readerCompletion=await Promise.all(pending);
            // Repeated source retirement must neither resurrect tracks nor duplicate ended events.
            if(method==='abort'||method==='stop-then-abort')evidence.secondRetire=await bounded(writer.abort());
            else if(method==='close')evidence.secondRetire=await bounded(writer.close());
            else generator.stop();
            await wait(40);evidence.eventsAfterRepeat=events.slice();
            return evidence;
        }finally{
            // Cleanup is after evidence collection; it cannot manufacture test events.
            for(const track of owned)track.stop();
            await bounded(writer.abort(),100);
            for(const reader of readable){await bounded(reader.cancel(),100);try{reader.releaseLock();}catch{}}
            try{writer.releaseLock();}catch{}
        }
    }
    async function runtimeStop(){
        const RealGenerator=globalThis.MediaStreamTrackGenerator,created=[];
        globalThis.MediaStreamTrackGenerator=function(options){const track=new RealGenerator(options);created.push(track);return track;};
        try{
            initializeWidgetRailNativeCapture(['https://capture-probe.widgetrail.test']);
            if(!WidgetRailNativeMedia.start(1,true,'720p30'))throw Error('runtime_start_failed');
            const tracks=[...created,...created.map(track=>track.clone())],events=tracks.map(()=>0);
            tracks.forEach((track,index)=>track.addEventListener('ended',()=>events[index]++));
            const began=performance.now();WidgetRailNativeMedia.stop(1);await wait(350);
            const result={kind:'runtime',method:'stop',queued:0,readers:false,states:tracks.map(t=>t.readyState),events:events.slice(),retireMilliseconds:performance.now()-began};
            WidgetRailNativeMedia.stop(1);await wait(40);result.eventsAfterRepeat=events.slice();
            for(const track of tracks)track.stop();return result;
        }finally{globalThis.WidgetRailNativeMedia?.dispose();globalThis.MediaStreamTrackGenerator=RealGenerator;}
    }
    async function run(){
        try{
            if(typeof MediaStreamTrackGenerator!=='function'||typeof MediaStreamTrackProcessor!=='function')throw Error('generated_media_unavailable');
            for(const kind of ['video','audio'])
                for(const [queued,readers] of [[0,false],[16,false],[16,true]])
                    for(const method of ['track-stop','close','abort','stop-then-abort'])results.push(await examine(kind,method,queued,readers));
            results.push(await runtimeStop());
        }catch(failure){error={name:failure.name,message:String(failure.message).slice(0,120)};}
        complete=true;
    }
    chrome.webview.postMessage({ready:true});void run();
})();
