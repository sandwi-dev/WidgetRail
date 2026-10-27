// Provider-neutral generated media transport. The host invokes this inside a
// document-created closure with its already-validated exact HTTPS origins.
function initializeWidgetRailNativeCapture(allowedOrigins) {
    if(!Array.isArray(allowedOrigins)||allowedOrigins.length<1||allowedOrigins.length>8||
        !allowedOrigins.includes(location.origin)||window!==top)return;
    let lastEpoch=0;
    let epoch=null, video=null, audio=null, vw=null, aw=null, stream=null, armed=false, deadline=0;
    let frames=0,samples=0,lease=0;
    let width=0,height=0,firstFrame,resolveFirst,sourceRetired,resolveRetired;
    let sourceDimensions={width:0,height:0};
    const presets=Object.freeze({
        '480p15':Object.freeze({id:'480p15',maximumWidth:1706,maximumHeight:480,frameRate:15}),
        '480p30':Object.freeze({id:'480p30',maximumWidth:1706,maximumHeight:480,frameRate:30}),
        '480p60':Object.freeze({id:'480p60',maximumWidth:1706,maximumHeight:480,frameRate:60}),
        '720p15':Object.freeze({id:'720p15',maximumWidth:2560,maximumHeight:720,frameRate:15}),
        '720p30':Object.freeze({id:'720p30',maximumWidth:2560,maximumHeight:720,frameRate:30}),
        '720p60':Object.freeze({id:'720p60',maximumWidth:2560,maximumHeight:720,frameRate:60}),
        '1080p15':Object.freeze({id:'1080p15',maximumWidth:3840,maximumHeight:1080,frameRate:15}),
        '1080p30':Object.freeze({id:'1080p30',maximumWidth:3840,maximumHeight:1080,frameRate:30}),
        '1080p60':Object.freeze({id:'1080p60',maximumWidth:3840,maximumHeight:1080,frameRate:60}),
        '1440p15':Object.freeze({id:'1440p15',maximumWidth:5120,maximumHeight:1440,frameRate:15}),
        '1440p30':Object.freeze({id:'1440p30',maximumWidth:5120,maximumHeight:1440,frameRate:30}),
        '1440p60':Object.freeze({id:'1440p60',maximumWidth:5120,maximumHeight:1440,frameRate:60})
    });
    let quality=presets['720p30'];
    let readSettings=()=>({});
    let readConstraints=()=>({});
    const positive=value=>Number.isFinite(value)&&value>0;
    const constraintMaximum=(value,ceiling)=>positive(value?.max)&&value.max<=ceiling?value.max:undefined;
    const awaitNativeFormat=(read,current,retired,selectedQuality)=>new Promise((resolve,reject)=>{
        let poll,timeout,finished=false;
        const finish=(error,value)=>{
            if(finished)return;finished=true;clearTimeout(poll);clearTimeout(timeout);
            if(error)reject(error);else resolve(value);
        };
        const observe=()=>{
            if(finished)return;
            if(!current())return finish(new DOMException('The selected capture ended before its native format was ready.','AbortError'));
            let actual;try{actual=read();}catch(error){finish(error);return;}
            if(Number.isInteger(actual.width)&&actual.width>=2&&Number.isInteger(actual.height)&&actual.height>=2){
                if(actual.width>selectedQuality.maximumWidth||actual.height>selectedQuality.maximumHeight)
                    finish(new DOMException('The native format exceeds the selected capture envelope.','OverconstrainedError'));
                else finish(null,{width:actual.width,height:actual.height});
            }else poll=setTimeout(observe,16);
        };
        timeout=setTimeout(()=>finish(new DOMException('The browser did not expose a native capture format in time.','TimeoutError')),5000);
        retired.then(()=>finish(new DOMException('The selected capture ended before its native format was ready.','AbortError')));
        observe();
    });
    const aspectLimits=(actual,selectedQuality)=>{
        const widthLimited=actual.width*selectedQuality.maximumHeight>actual.height*selectedQuality.maximumWidth;
        // Keep the limiting dimension exact; floating-point scale multiplication
        // can otherwise turn a nominal 1440 ceiling into 1438 after even rounding.
        return {width:{max:widthLimited?selectedQuality.maximumWidth:Math.max(2,2*Math.floor(selectedQuality.maximumHeight*actual.width/actual.height/2))},
            height:{max:widthLimited?Math.max(2,2*Math.floor(selectedQuality.maximumWidth*actual.height/actual.width/2)):selectedQuality.maximumHeight},
            frameRate:{max:selectedQuality.frameRate}};
    };
    const retireSource=(track,writer)=>{
        if(!track)return;
        // Ending the generator source naturally notifies every sourced track,
        // including clones. track.stop() intentionally suppresses its own ended
        // event, so it is only a bounded fallback for failed source retirement.
        let timer;
        const finished=()=>{clearTimeout(timer);track.removeEventListener('ended',finished);};
        const fallback=()=>{try{track.stop();}finally{finished();}};
        if(!writer){fallback();return;}
        track.addEventListener('ended',finished,{once:true});
        timer=setTimeout(fallback,1000);
        try{Promise.resolve(writer.abort()).then(()=>{if(track.readyState==='ended')finished();},fallback);}
        catch{fallback();}
    };
    const stop=()=>{
        const oldVideo=video,oldAudio=audio,oldVideoWriter=vw,oldAudioWriter=aw;
        epoch=null;armed=false;resolveFirst?.(false);resolveFirst=null;resolveRetired?.();resolveRetired=null;
        video=audio=vw=aw=stream=null;readSettings=readConstraints=()=>({});
        // Retire captured old objects after revoking admission. A late completion
        // can never stop a replacement or renew its epoch.
        retireSource(oldVideo,oldVideoWriter);retireSource(oldAudio,oldAudioWriter);
    };
    const original=navigator.mediaDevices.getDisplayMedia.bind(navigator.mediaDevices);
    // An opted-in backend never falls through to a second chooser. A native
    // Start action is the only source of an authorized stream.
    navigator.mediaDevices.getDisplayMedia=async()=>{
        if(!armed||!stream||Date.now()>deadline)throw new DOMException('Choose an application in WidgetRail first.','NotAllowedError');
        const selectedEpoch=epoch,selected=stream,selectedVideo=video,selectedQuality=quality;
        const selectedFirstFrame=firstFrame,selectedRetired=sourceRetired,selectedReadSettings=readSettings;
        const owns=()=>epoch===selectedEpoch&&stream===selected&&video===selectedVideo;
        const current=()=>owns()&&selectedVideo?.readyState==='live'&&Date.now()<=lease;
        armed=false;chrome.webview.postMessage({captureConsume:true,captureEpoch:epoch});
        let timeout;
        try{
            let available;
            try{available=await Promise.race([selectedFirstFrame,new Promise(resolve=>timeout=setTimeout(()=>resolve(false),5000))]);}
            finally{clearTimeout(timeout);}
            if(!available||!current())throw new DOMException('The selected window did not supply a current frame.','AbortError');
            if(typeof selectedVideo.applyConstraints!=='function'||typeof selectedVideo.getConstraints!=='function')
                throw new DOMException('The browser cannot establish the selected capture limits.','NotSupportedError');
            // Writable acceptance precedes Chromium's cross-thread source-format
            // publication. Wait for observed native dimensions, never the
            // getSettings fallback that describes our submitted pixels.
            const actual=await awaitNativeFormat(selectedReadSettings,current,selectedRetired,selectedQuality);
            if(!current())throw new DOMException('The selected capture changed before its limits were applied.','AbortError');
            // Consumers use the real track's constraints to configure encoding.
            // getSettings describes actual frames and is not a substitute for
            // these upper bounds. Leave native get/applyConstraints intact so
            // the consumer can subsequently lower its delivery resolution/rate.
            // The allocation envelope admits ultrawide sources; it is not the
            // source's aspect ratio. Express the initial aspect at the selected
            // ceiling without requesting upscaling. Later consumer constraints
            // remain untouched. We deliberately do not reapply these bounds on
            // resize and risk overwriting a consumer's lower-quality request.
            const limits=aspectLimits(actual,selectedQuality);
            try{
                await Promise.race([
                    selectedVideo.applyConstraints(limits),
                    selectedRetired.then(()=>{throw new DOMException('The selected capture ended.','AbortError');}),
                    new Promise((_,reject)=>timeout=setTimeout(()=>reject(new DOMException('The browser did not establish capture limits in time.','TimeoutError')),5000))
                ]);
            }finally{clearTimeout(timeout);}
            if(!current())throw new DOMException('The selected capture changed while applying its limits.','AbortError');
            const applied=selectedVideo.getConstraints();
            if(applied?.width?.max!==limits.width.max||applied?.height?.max!==limits.height.max||
                applied?.frameRate?.max!==limits.frameRate.max)
                throw new DOMException('The browser did not confirm the selected capture limits.','OverconstrainedError');
            return selected;
        }catch(error){
            if(owns()){chrome.webview.postMessage({captureError:error?.name??'Error',captureEpoch:selectedEpoch});stop();}
            throw error;
        }
    };
    globalThis.WidgetRailNativeMedia={
        start(id,withAudio,preset='720p30',contentPreference='detail'){
            // Validate before stopping an existing session. No unrecognized or
            // unbounded setting can be introduced by a page-authored object.
            if(!Number.isSafeInteger(id)||id<=lastEpoch||typeof withAudio!=='boolean'||typeof preset!=='string'||!Object.hasOwn(presets,preset)||!['detail','motion'].includes(contentPreference))return false;
            stop();lastEpoch=id;quality=presets[preset];const selectedQuality=quality;epoch=id;frames=samples=width=height=0;lease=Date.now()+10000;
            firstFrame=new Promise(resolve=>resolveFirst=resolve);sourceRetired=new Promise(resolve=>resolveRetired=resolve);
            sourceDimensions={width:0,height:0};const observedDimensions=sourceDimensions;
            video=new MediaStreamTrackGenerator({kind:'video'});vw=video.writable.getWriter();
            video.contentHint=contentPreference;
            const originalSettings=video.getSettings?.bind(video),originalConstraints=video.getConstraints?.bind(video);
            readSettings=()=>originalSettings?.()??{};
            readConstraints=()=>originalConstraints?.()??{};
            video.getSettings=()=>{
                const actual=originalSettings?.()??{},applied=originalConstraints?.()??{};
                return {...actual,width:positive(actual.width)?actual.width:observedDimensions.width,
                    height:positive(actual.height)?actual.height:observedDimensions.height,
                    frameRate:positive(actual.frameRate)?actual.frameRate:constraintMaximum(applied.frameRate,selectedQuality.frameRate)??selectedQuality.frameRate,
                    displaySurface:'window',logicalSurface:true,cursor:'never'};
            };
            if(withAudio){audio=new MediaStreamTrackGenerator({kind:'audio'});aw=audio.writable.getWriter();}
            stream=new MediaStream([video,...(audio?[audio]:[])]);armed=true;deadline=Date.now()+15000;
            return true;
        },
        stop(expectedEpoch){if(expectedEpoch!==epoch)return false;stop();return true;},
        keep(expectedEpoch){if(expectedEpoch!==epoch)return false;lease=Date.now()+10000;return this.status();},
        status(){
            const actual=readSettings();let applied;try{applied=readConstraints();}catch{}
            return {frames,samples,width,height,active:!!stream,armed,quality:quality.id,targetFrameRate:quality.frameRate,contentPreference:video?.contentHint,
                browserWidth:actual.width,browserHeight:actual.height,browserFrameRate:actual.frameRate,
                constraintWidth:constraintMaximum(applied?.width,quality.maximumWidth),constraintHeight:constraintMaximum(applied?.height,quality.maximumHeight),
                constraintFrameRate:constraintMaximum(applied?.frameRate,quality.frameRate)};
        },
        dispose(){stop();navigator.mediaDevices.getDisplayMedia=original;}
    };
    setInterval(()=>{if(stream&&Date.now()>lease)stop();},1000);
    chrome.webview.addEventListener('sharedbufferreceived',async event=>{
        const buffer=event.getBuffer(),m=event.additionalData;let frame;
        try{
            if(m.epoch!==epoch)return;
            if(m.kind===1){
                if(!Number.isInteger(m.width)||!Number.isInteger(m.height)||m.width<2||m.height<2||
                    m.width>quality.maximumWidth||m.height>quality.maximumHeight||m.width%2||m.height%2||m.bytes!==m.width*m.height*4||buffer.byteLength<m.bytes)
                    throw new RangeError('Frame exceeds selected capture quality');
                frame=new VideoFrame(buffer,{format:'BGRA',codedWidth:m.width,codedHeight:m.height,timestamp:m.time});width=m.width;height=m.height;sourceDimensions.width=m.width;sourceDimensions.height=m.height;
                await vw.write(frame);if(m.epoch===epoch){frames++;resolveFirst?.(true);resolveFirst=null;}
            }
            else if(m.kind===2&&aw){frame=new AudioData({format:'s16',sampleRate:48000,numberOfChannels:2,numberOfFrames:m.bytes/4,timestamp:m.time,data:new Uint8Array(buffer,0,m.bytes)});await aw.write(frame);if(m.epoch===epoch)samples+=m.bytes/4;}
        }catch(e){if(m.epoch===epoch){chrome.webview.postMessage({captureError:e.name,captureEpoch:m.epoch});stop();}}
        finally{frame?.close();chrome.webview.releaseBuffer(buffer);chrome.webview.postMessage({captureAck:m.kind,captureEpoch:m.epoch,captureSequence:m.sequence});}
    });
}
