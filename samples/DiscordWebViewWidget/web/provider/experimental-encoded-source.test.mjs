import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const script=await readFile(new URL('./experimental-encoded-source.js',import.meta.url),'utf8');
const bind=vm.runInNewContext(script+'\nbindGeneratedVideoBandwidth', {
    RTCSessionDescription:class{constructor(description){Object.assign(this,description);}}
});

function fixture(){
    const audio='m=audio 9 UDP/TLS/RTP/SAVPF 111\r\nc=IN IP4 0.0.0.0\r\na=mid:0\r\n';
    const video='m=video 9 UDP/TLS/RTP/SAVPF 102 103\r\nc=IN IP4 0.0.0.0\r\na=mid:1\r\na=rtpmap:102 H264/90000\r\n';
    const sender={getParameters:()=>({encodings:[{active:true,maxBitrate:700000}]})};
    const result={answer:null,remainingAudioStreams:[1],remainingVideoStreams:[]};
    class Connection{
        generateSDPAnswer(){
            const preset={videoBitRate:2500};
            result.answer={type:'answer',sdp:'v=0\r\n'+audio+video+'a=fmtp:102 x-google-max-bitrate='+preset.videoBitRate+'\r\n'};
            return result;
        }
        handleNegotiationNeeded(){}
    }
    const connection=new Connection(), pc={};
    Object.assign(connection,{pc,videoTransceiver:{mid:'1',sender},videoPayloadType:102,sdp:'c=IN IP4 0.0.0.0\r\n',
        videoQualityManager:{goliveMaxQuality:{capture:{width:1280,height:720,framerate:30},bitrateMax:3500000}}});
    return {connection,pc,sender,result,format:{width:1280,height:720,fps:30}};
}

test('the external encoder defers to WebRTC for a smaller size or rate',()=>{
    const full=vm.runInNewContext(script+'\nusesFullCaptureSize');
    const format = {width:2560,height:720,fps:60};
    assert.equal(full({},format),true);
    assert.equal(full({scaleResolutionDownBy:1},format),true);
    assert.equal(full({scaleResolutionDownBy:2},format),false);
    assert.equal(full({maxFramerate:30},format),false);
    assert.equal(full({scaleResolutionDownTo:{width:1280,height:720}},format),false);
    assert.equal(full({scaleResolutionDownTo:{width:2560,height:720}},format),true);
});

test('explicit downscale returns the original track and full-size demand can attach again', async () => {
    let nextCheck, attachments = 0, terminations = 0, rejectAttach = false;
    const track = Object.assign(new EventTarget(), {readyState: 'live', getConstraints: () => ({width:1280,height:720})});
    const encoding = {active: true, scaleResolutionDownBy: 2};
    class Sender {
        track = track;
        getParameters() {
            return {codecs: [{mimeType: 'video/H264', sdpFmtpLine: 'profile-level-id=42e01f', payloadType: 102}], encodings: [encoding]};
        }
        async getStats() { return new Map(); }
        async replaceTrack(value) { this.track = value; }
        async createEncodedSource() { attachments++; if (rejectAttach) throw Error('Encoder unavailable'); }
    }
    const selected = {stream: {getVideoTracks: () => [track]}};
    const sender = new Sender();
    const connection = {context: 'stream', userId: 'a', streamUserId: 'a', destroyed: false,
        input: {desktop: selected}, pc: {signalingState: 'stable'}, videoTransceiver: {sender}, videoPayloadType:102,
        videoQualityManager: {goliveMaxQuality:{capture:{width:1280,height:720,framerate:30}}}};
    const engine = {connections: new Set([connection]), desktopInputPool: {}};
    const rtc = {context: 'stream', isOwner: true, _destroyed: false, _connection: connection, streamKey: 'call:c:a',
        streamContext: {channelId: 'c', ownerId: 'a'}};
    const stores = {StreamRTCConnectionStore: {getRTCConnection: () => rtc}, MediaEngineStore: {getMediaEngine: () => engine}};
    const create = vm.runInNewContext(script+'\ncreateExperimentalCapture', {
        performance, Blob, URL, RTCRtpSender: Sender, runEncodedSourceWorker() {},
        MediaStreamTrackProcessor: class { readable = {}; },
        Worker: class { postMessage() {} terminate() { terminations++; } },
        setTimeout(callback) { nextCheck = callback; return 1; }, clearTimeout() {},
        WidgetRailNativeMedia: {status: () => ({active: true, quality: '720p30', contentPreference: 'detail', width: 1280, height: 720})}
    });
    const capture = create({store: name => stores[name], me: () => ({id: 'a'}), channelId: () => 'c',
        currentShare: () => ({ownerId: 'a', channelId: 'c'}), streamKey: () => rtc.streamKey,
        currentEpoch: () => 1, peekDesktopSource: () => selected, videoRateLimits: () => ({h264Level:31})});
    async function observe() { nextCheck(); await new Promise(resolve => setImmediate(resolve)); }
    capture.prepare({channelId: 'c', account: 'a'}, {resolution: 720, frameRate: 30}, engine, 'game', 1);
    const firstCheck=observe();
    assert.equal(capture.sample(connection).encoderPath,'webrtc');
    assert.equal(capture.sample(connection).encoderFallback,'external_pending');
    await firstCheck;
    assert.equal(sender.track, track);
    encoding.scaleResolutionDownBy = 1;
    await observe();
    assert.equal(attachments, 1);
    assert.equal(capture.sample(connection).encoderPath,'starting');
    encoding.scaleResolutionDownBy = 2;
    await observe();
    assert.equal(sender.track, track);
    assert.equal(terminations, 1);
    encoding.scaleResolutionDownBy = 1;
    await observe();
    assert.equal(attachments, 2);
    encoding.scaleResolutionDownBy = 2;
    await observe();
    rejectAttach = true;
    encoding.scaleResolutionDownBy = 1;
    await observe();
    assert.equal(sender.track, track, 'a runtime failure after detachment restores the live raw track');
    await observe();
    assert.equal(attachments, 3, 'the failed encoder is not repeatedly reattached');
    assert.equal(capture.sample(connection).encoderFallback, 'encoder_failed');
    capture.dispose();
});

test('only the outgoing video section gets its provider-selected bandwidth',()=>{
    const f=fixture(),original=f.connection.generateSDPAnswer,expected=original.call(f.connection).answer.sdp;
    let owned=true;const restore=bind(f,()=>owned);
    const generated=f.connection.generateSDPAnswer();
    assert.equal(generated.answer.sdp, expected.replace('a=mid:1','b=TIAS:3500000\r\na=mid:1')
        .replace('x-google-max-bitrate=2500','x-google-max-bitrate=3500'));
    assert.equal(generated.remainingAudioStreams,f.result.remainingAudioStreams);
    assert.equal(f.sender.getParameters().encodings[0].maxBitrate,700000);
    owned=false;assert.equal(f.connection.generateSDPAnswer(),f.result);
    owned=true;f.connection.sdp+='b=TIAS:1200000\r\n';assert.equal(f.connection.generateSDPAnswer(),f.result);
    restore();assert.equal(f.connection.generateSDPAnswer,original);
    assert.equal(Object.hasOwn(f.connection,'generateSDPAnswer'),false);
});

test('explicit signaling limits, a lower preset, and unfamiliar provider implementations keep original negotiation',()=>{
    for(const field of ['b=AS:1200','b=TIAS:1200000','a=fmtp:102 max-br=1200','a=fmtp:102 x-google-max-bitrate=1200']){
        const f=fixture();f.connection.sdp+=field+'\r\n';assert.equal(bind(f,()=>true),null);
    }
    const f=fixture();f.connection.videoQualityManager.goliveMaxQuality.bitrateMax=2000000;
    assert.equal(bind(f,()=>true),null);
    f.connection.videoQualityManager.goliveMaxQuality.bitrateMax=3500000;
    f.connection.generateSDPAnswer=()=>f.result;assert.equal(bind(f,()=>true),null);
});

test('a replaced sender or changed preset keeps the original answer during renegotiation', () => {
    const f = fixture();
    const restore = bind(f, () => true);
    f.connection.videoTransceiver.sender = {};
    assert.equal(f.connection.generateSDPAnswer(), f.result);
    f.connection.videoTransceiver.sender = f.sender;
    f.connection.videoQualityManager.goliveMaxQuality.capture.framerate = 60;
    assert.equal(f.connection.generateSDPAnswer(), f.result);
    restore();
});

test('retiring the wrapper preserves a provider method installed after it', () => {
    const f = fixture();
    const restore = bind(f, () => true);
    const replacement = () => f.result;
    f.connection.generateSDPAnswer = replacement;
    restore();
    assert.equal(f.connection.generateSDPAnswer, replacement);
});

test('every listed H.264 format needs a negotiated level large enough for padded dimensions and cadence', () => {
    const select = vm.runInNewContext(script+'\nexternalH264Format');
    const payload = 102;
    for (const [width,height,fps,level] of [
        [854,480,15,31], [854,480,30,31], [854,480,60,31],
        [1280,720,15,31], [1280,720,30,31], [1280,720,60,32],
        [1920,1080,15,40], [1920,1080,30,40], [1920,1080,60,42],
        [2560,720,15,40], [2560,720,30,40], [2560,720,60,42],
        [3840,1080,60,51], [5120,1440,60,52]
    ]) {
        const format = {width,height,fps};
        const codecs = [{payloadType:payload,mimeType:'video/H264',sdpFmtpLine:'profile-level-id=42e0'+level.toString(16)}];
        assert.equal(select(codecs,payload,format,level)?.minimumLevel,level);
        if (level > 31) assert.equal(select(codecs,payload,format,31),null);
        assert.equal(select(codecs,payload,format,null),null);
        assert.equal(select(codecs,103,format,level),null);
    }
});
