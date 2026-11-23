// Local WebRTC test of the shared capture transport. Encoder policy and codec
// negotiation are a controlled fixture, not a full replica of Discord policy.
(() => {
    initializeWidgetRailNativeCapture(['https://capture-probe.widgetrail.test']);
    let sender,receiver,inbound=[],outbound=[],mediaSources=[],network=null,statsPending=false,generation=0,encodingPolicy=null;
    const video=document.querySelector('video');
    const stop=()=>{++generation;sender?.close();receiver?.close();sender=receiver=null;inbound=[];outbound=[];mediaSources=[];network=null;encodingPolicy=null;video.srcObject=null;};
    const number=value=>Number.isFinite(value)&&value>=0?value:null;
    const text=value=>typeof value==='string'&&value.length<=80&&/^[\w .+/-]+$/.test(value)?value:null;
    function reports(stats,type){
        const result=[];
        for(const report of stats.values()){
            if(report.type!==type||result.length>=4)continue;
            const codec=stats.get(report.codecId);
            const common={kind:report.kind??report.mediaType,timestamp:number(report.timestamp),
                width:number(report.frameWidth),height:number(report.frameHeight),framesPerSecond:number(report.framesPerSecond),
                codec:text(codec?.mimeType),clockRate:number(codec?.clockRate),
                h264:codec?.mimeType?.toLowerCase()==='video/h264'?globalThis.captureProbePolicy.h264Format(codec.sdpFmtpLine):null};
            result.push(type==='media-source'?{kind:common.kind,timestamp:common.timestamp,width:number(report.width),height:number(report.height),
                frames:number(report.frames),framesPerSecond:common.framesPerSecond}
                :type==='outbound-rtp'?{...common,framesEncoded:number(report.framesEncoded),framesSent:number(report.framesSent),
                bytesSent:number(report.bytesSent),targetBitrate:number(report.targetBitrate),totalEncodeTime:number(report.totalEncodeTime),qpSum:number(report.qpSum),
                packetsSent:number(report.packetsSent),retransmittedPacketsSent:number(report.retransmittedPacketsSent),
                totalPacketSendDelay:number(report.totalPacketSendDelay),nackCount:number(report.nackCount),pliCount:number(report.pliCount),
                encoderImplementation:text(report.encoderImplementation),powerEfficientEncoder:typeof report.powerEfficientEncoder==='boolean'?report.powerEfficientEncoder:null,
                qualityLimitationReason:['none','cpu','bandwidth','other'].includes(report.qualityLimitationReason)?report.qualityLimitationReason:null,
                qualityLimitationDurations:Object.fromEntries(['none','cpu','bandwidth','other'].map(key=>[key,number(report.qualityLimitationDurations?.[key])]))}
                :{...common,framesDecoded:number(report.framesDecoded),framesDropped:number(report.framesDropped),
                    bytesReceived:number(report.bytesReceived),totalSamplesReceived:number(report.totalSamplesReceived),
                    packetsReceived:number(report.packetsReceived),packetsLost:number(report.packetsLost),jitter:number(report.jitter),
                    jitterBufferDelay:number(report.jitterBufferDelay),jitterBufferEmittedCount:number(report.jitterBufferEmittedCount),
                    freezeCount:number(report.freezeCount),totalFreezesDuration:number(report.totalFreezesDuration),
                    totalInterFrameDelay:number(report.totalInterFrameDelay),totalSquaredInterFrameDelay:number(report.totalSquaredInterFrameDelay),
                    totalDecodeTime:number(report.totalDecodeTime),decoderImplementation:text(report.decoderImplementation),
                    powerEfficientDecoder:typeof report.powerEfficientDecoder==='boolean'?report.powerEfficientDecoder:null});
        }
        return result;
    }
    globalThis.captureProbe={
        async connect(){
            stop();const id=generation;
            const stream=await navigator.mediaDevices.getDisplayMedia();
            sender=new RTCPeerConnection({iceServers:[]});receiver=new RTCPeerConnection({iceServers:[]});
            const tx=sender,rx=receiver;
            tx.onicecandidate=e=>{if(e.candidate)rx.addIceCandidate(e.candidate).catch(()=>{});};
            rx.onicecandidate=e=>{if(e.candidate)tx.addIceCandidate(e.candidate).catch(()=>{});};
            const received=new MediaStream();
            rx.ontrack=e=>{if(id!==generation)return;received.addTrack(e.track);video.srcObject=received;};
            stream.getTracks().forEach(t=>tx.addTrack(t,stream));
            const policy=globalThis.captureProbePolicy;
            policy.chooseCodec(tx);
            const offer=policy.description(await tx.createOffer());
            await tx.setLocalDescription(offer.description);await rx.setRemoteDescription(tx.localDescription);
            const answer=policy.description(await rx.createAnswer());
            await rx.setLocalDescription(answer.description);await tx.setRemoteDescription(rx.localDescription);
            const actual=await policy.sender(tx,WidgetRailNativeMedia.status().targetFrameRate);
            if(id!==generation)throw Error('Local probe was retired during sender setup');
            encodingPolicy={...policy.reference,...actual,offerCodecs:offer.codecs,answerCodecs:answer.codecs,
                offerH264Formats:offer.h264Formats,answerH264Formats:answer.h264Formats};
            return true;
        },stop,
        status(){
            const tx=sender,rx=receiver,id=generation;
            if(tx&&rx&&!statsPending){
                statsPending=true;
                Promise.all([tx.getStats(),rx.getStats()]).then(([sent,received])=>{
                    if(id!==generation||tx!==sender||rx!==receiver)return;
                    outbound=reports(sent,'outbound-rtp');inbound=reports(received,'inbound-rtp');mediaSources=reports(sent,'media-source');
                    const pair=[...sent.values()].find(value=>value.type==='candidate-pair'&&value.state==='succeeded'&&value.nominated===true);
                    network=pair?{availableOutgoingBitrate:number(pair.availableOutgoingBitrate),currentRoundTripTime:number(pair.currentRoundTripTime)}:null;
                }).catch(()=>{}).finally(()=>{statsPending=false;});
            }
            return {...WidgetRailNativeMedia.status(),nativeSampleTimestamp:performance.now(),connection:sender?.connectionState,inbound,outbound,mediaSources,encodingPolicy,
                currentSenderParameters:tx?globalThis.captureProbePolicy.observe(tx):null,
                stageTimings:globalThis.captureProbeTiming?.(),network,
                width:video.videoWidth,height:video.videoHeight};
        }
    };
    chrome.webview.postMessage({ready:typeof MediaStreamTrackGenerator==='function'&&typeof VideoFrame==='function'});
})();
