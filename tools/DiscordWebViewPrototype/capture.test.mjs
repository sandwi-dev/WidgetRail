import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('./capture.js',import.meta.url),'utf8');
const policySource=fs.readFileSync(new URL('./capture-policy.js',import.meta.url),'utf8');
const settle=async()=>{for(let i=0;i<8;i++)await Promise.resolve();};
function setup(){
    const peers=[],video={videoWidth:1920,videoHeight:1080};
    class Peer{
        constructor(){this.connectionState='connected';this.report=new Map();this.reads=0;peers.push(this);}
        close(){} addTrack(){} async createOffer(){return{};} async createAnswer(){return{};}
        async setLocalDescription(value){this.localDescription=value;} async setRemoteDescription(){}
        getStats(){this.reads++;return this.gate??Promise.resolve(this.report);}
    }
    const context={document:{querySelector:()=>video},initializeWidgetRailNativeCapture:()=>{},
        navigator:{mediaDevices:{getDisplayMedia:async()=>({getTracks:()=>[]})}},RTCPeerConnection:Peer,
        MediaStream:class{addTrack(){}},MediaStreamTrackGenerator:class{},VideoFrame:class{},
        WidgetRailNativeMedia:{status:()=>({frames:120,quality:'1080p60',targetFrameRate:60})},performance:{now:()=>9000},
        captureProbePolicy:{description:value=>({description:value,codecs:['VP8']}),chooseCodec:()=>{},sender:async()=>({maxBitrate:8000000,maxFramerate:60}),observe:peer=>peer.parameters??null,reference:{maximumBitrate:8000000}},
        chrome:{webview:{postMessage:()=>{}}}};
    vm.createContext(context);const policyMock=context.captureProbePolicy;
    vm.runInContext(policySource,context);policyMock.h264Format=context.captureProbePolicy.h264Format;context.captureProbePolicy=policyMock;
    vm.runInContext(source,context);return{probe:context.captureProbe,peers};
}
test('probe reports independent native, encoded and decoded observations with codec and adaptation evidence',async()=>{
    const {probe,peers}=setup();await probe.connect();
    peers[0].report=new Map([['codec',{mimeType:'video/VP8',clockRate:90000}],['out',{type:'outbound-rtp',kind:'video',timestamp:1000,codecId:'codec',
        framesEncoded:50,framesSent:49,frameWidth:1904,frameHeight:1072,framesPerSecond:37,totalEncodeTime:1.5,bytesSent:10000,
        encoderImplementation:'libvpx',powerEfficientEncoder:false,qualityLimitationReason:'cpu',qualityLimitationDurations:{none:1,cpu:2}}]]);
    peers[1].report=new Map([['codec',{mimeType:'video/VP8',clockRate:90000}],['in',{type:'inbound-rtp',kind:'video',timestamp:1010,codecId:'codec',
        framesDecoded:48,framesDropped:1,frameWidth:1904,frameHeight:1072,framesPerSecond:36,totalDecodeTime:.2,bytesReceived:9900,
        freezeCount:2,totalFreezesDuration:.6,packetsLost:3,jitter:.01,jitterBufferDelay:.7,jitterBufferEmittedCount:48,
        totalInterFrameDelay:1.2,totalSquaredInterFrameDelay:.04}]]);
    peers[0].report.set('source',{type:'media-source',kind:'video',timestamp:1000,width:1920,height:1080,frames:100,framesPerSecond:60});
    probe.status();await settle();const result=probe.status();
    assert.equal(result.frames,120);assert.equal(result.nativeSampleTimestamp,9000);
    assert.equal(result.outbound[0].framesEncoded,50);assert.equal(result.outbound[0].qualityLimitationReason,'cpu');
    assert.equal(result.outbound[0].codec,'video/VP8');assert.equal(result.outbound[0].totalEncodeTime,1.5);
    assert.equal(result.inbound[0].framesDecoded,48);assert.equal(result.inbound[0].timestamp,1010);
    assert.equal(result.inbound[0].totalDecodeTime,.2);assert.equal(result.width,1920);
    assert.equal(result.inbound[0].freezeCount,2);assert.equal(result.inbound[0].totalFreezesDuration,.6);
    assert.equal(result.inbound[0].packetsLost,3);assert.equal(result.inbound[0].jitter,.01);
    assert.equal(result.inbound[0].jitterBufferDelay,.7);assert.equal(result.inbound[0].jitterBufferEmittedCount,48);
    assert.equal(result.inbound[0].totalInterFrameDelay,1.2);assert.equal(result.inbound[0].totalSquaredInterFrameDelay,.04);
    assert.equal(result.mediaSources[0].frames,100);assert.equal(result.mediaSources[0].framesPerSecond,60);
});
test('probe has at most one pending paired stats read and rejects stale completions',async()=>{
    const {probe,peers}=setup();await probe.connect();let complete;
    peers[0].gate=new Promise(resolve=>complete=resolve);probe.status();probe.status();assert.equal(peers[0].reads,1);
    probe.stop();await probe.connect();
    complete(new Map([['old',{type:'outbound-rtp',kind:'video',framesEncoded:999}]]));await settle();
    const result=probe.status();assert.equal(result.outbound.length,0);assert.equal(result.inbound.length,0);
    await settle();assert.equal(peers[2].reads,1);
});
test('probe keeps missing measurements null and bounds report count without serializing raw stats',async()=>{
    const {probe,peers}=setup();await probe.connect();
    peers[0].report=new Map(Array.from({length:8},(_,i)=>[i,{type:'outbound-rtp',kind:'video',framesEncoded:NaN,frameWidth:-1,
        qualityLimitationReason:'private-value',encoderImplementation:'private\nvalue',id:'secret',remoteId:'secret'}]));
    probe.status();await settle();const result=probe.status();assert.equal(result.outbound.length,4);
    assert.equal(result.outbound[0].width,null);assert.equal(result.outbound[0].framesEncoded,null);
    assert.equal(result.outbound[0].qualityLimitationReason,null);assert.equal(result.outbound[0].encoderImplementation,null);
    assert.equal(result.outbound[0].retransmittedPacketsSent,null);assert.equal(result.outbound[0].totalPacketSendDelay,null);
    assert.equal(JSON.stringify(result).includes('secret'),false);
});

test('probe reports transport retransmission evidence without conflating it with encoded frames',async()=>{
    const {probe,peers}=setup();await probe.connect();
    peers[0].report=new Map([['out',{type:'outbound-rtp',kind:'video',framesEncoded:100,framesSent:99,
        packetsSent:500,retransmittedPacketsSent:8,totalPacketSendDelay:.5,nackCount:4,pliCount:1}]]);
    probe.status();await settle();const result=probe.status().outbound[0];
    assert.equal(result.framesEncoded,100);assert.equal(result.framesSent,99);assert.equal(result.packetsSent,500);
    assert.equal(result.retransmittedPacketsSent,8);assert.equal(result.totalPacketSendDelay,.5);
    assert.equal(result.nackCount,4);assert.equal(result.pliCount,1);
});

test('probe confirms actual sender and receiver H264 formats without publishing SDP or codec identities',async()=>{
    const {probe,peers}=setup();await probe.connect();
    for(const [index,type] of ['outbound-rtp','inbound-rtp'].entries())
        peers[index].report=new Map([['codec',{mimeType:'video/H264',sdpFmtpLine:'profile-level-id=42001f;packetization-mode=1;private=secret'}],
            ['media',{type,kind:'video',codecId:'codec',id:'private-id'}]]);
    probe.status();await settle();const result=probe.status();
    for(const report of [result.outbound[0],result.inbound[0]]){
        assert.equal(report.h264.profileLevelId,'42001f');assert.equal(report.h264.packetizationMode,1);
        assert.equal(JSON.stringify(report).includes('secret'),false);assert.equal(JSON.stringify(report).includes('private-id'),false);
    }
});

test('probe separates initial setup policy from current parameters and clears them on stop',async()=>{
    const {probe,peers}=setup();await probe.connect();
    peers[0].parameters={maxFramerate:30,scaleResolutionDownBy:2};
    const result=probe.status();
    assert.equal(result.encodingPolicy.maxFramerate,60);
    assert.equal(result.currentSenderParameters.maxFramerate,30);
    peers[0].parameters={maxFramerate:60,scaleResolutionDownBy:1};
    assert.equal(probe.status().currentSenderParameters.scaleResolutionDownBy,1);
    probe.stop();assert.equal(probe.status().currentSenderParameters,null);
});
