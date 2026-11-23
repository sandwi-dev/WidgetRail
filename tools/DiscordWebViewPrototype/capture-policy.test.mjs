import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('./capture-policy.js',import.meta.url),'utf8');
function policy(configuration,extra={}){const context={captureProbeConfiguration:configuration,...extra};vm.createContext(context);vm.runInContext(source,context);return context.captureProbePolicy;}

test('explicit codec policy admits only the selected video codec and repair formats before negotiation',()=>{
    let chosen;
    const h264={mimeType:'video/H264',sdpFmtpLine:'profile-level-id=42e01f'},vp8={mimeType:'video/VP8'},rtx={mimeType:'video/rtx'};
    const p=policy({codec:'H264',maximumBitrate:9000000},{RTCRtpSender:{getCapabilities:()=>({codecs:[vp8,h264,rtx]})}});
    p.chooseCodec({getTransceivers:()=>[{sender:{track:{kind:'video'}},setCodecPreferences:value=>chosen=value}]});
    assert.equal(chosen.length,2);assert.equal(chosen[0],h264);assert.equal(chosen[1],rtx);
    assert.equal(p.reference.requestedCodec,'H264');assert.equal(p.reference.maximumBitrate,9000000);
    assert.match(p.description({type:'offer',sdp:'m=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\n'}).description.sdp,/x-google-max-bitrate=9000/);
});

test('explicit H264 profiles require exact supported profile and packetization mode one',()=>{
    const variants=[
        {mimeType:'video/H264',sdpFmtpLine:'profile-level-id=42e01f;packetization-mode=1'},
        {mimeType:'video/H264',sdpFmtpLine:'profile-level-id=42001f;packetization-mode=0'},
        {mimeType:'video/H264',sdpFmtpLine:'profile-level-id=42001f;packetization-mode=1'},
        {mimeType:'video/rtx'}
    ];
    for(const profile of ['42001f','42e01f']){
        let selected;
        const p=policy({codec:'H264',h264Profile:profile},{RTCRtpSender:{getCapabilities:()=>({codecs:variants})}});
        p.chooseCodec({getTransceivers:()=>[{sender:{track:{kind:'video'}},setCodecPreferences:value=>selected=value}]});
        assert.equal(selected.length,2);assert.match(selected[0].sdpFmtpLine,new RegExp('profile-level-id='+profile));
        assert.match(selected[0].sdpFmtpLine,/packetization-mode=1/);assert.equal(selected[1],variants[3]);
        assert.equal(p.reference.requestedH264Profile,profile);
        const offer=p.description({type:'offer',sdp:`m=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\na=fmtp:96 packetization-mode=1;profile-level-id=${profile};level-asymmetry-allowed=1\r\n`});
        assert.equal(offer.h264Formats[0].profileLevelId,profile);assert.equal(offer.h264Formats[0].packetizationMode,1);
        assert.match(offer.description.sdp,/level-asymmetry-allowed=1/);
        for(const fmtp of ['profile-level-id=64001f;packetization-mode=1',`profile-level-id=${profile};packetization-mode=0`,''])
            assert.throws(()=>p.description({type:'answer',sdp:`m=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\na=fmtp:96 ${fmtp}\r\n`}));
    }
    assert.throws(()=>policy({codec:'H264',h264Profile:'42001f'},{RTCRtpSender:{getCapabilities:()=>({codecs:[variants[0],variants[1]]})}}).chooseCodec({}));
});

test('H264 format diagnostics expose only bounded normalized profile and packetization data',()=>{
    const p=policy();
    const normalized=p.h264Format('level-asymmetry-allowed=1; PROFILE-LEVEL-ID = 42E01F ; packetization-mode=1;private=secret');
    assert.equal(normalized.profileLevelId,'42e01f');assert.equal(normalized.packetizationMode,1);
    assert.equal(JSON.stringify(normalized).includes('secret'),false);
    for(const line of [null,'x'.repeat(2049),'profile-level-id=42e01f;profile-level-id=42001f','packetization-mode=1;packetization-mode=0']){
        const result=p.h264Format(line);assert.equal(result.profileLevelId,null);assert.equal(result.packetizationMode,null);
    }
    assert.equal(p.h264Format('profile-level-id=42e01f').packetizationMode,null);
    for(const settings of [{codec:'H264',h264Profile:'64001f'},{codec:'VP8',h264Profile:'42001f'},{h264Profile:'42e01f'}])assert.throws(()=>policy(settings));
});

test('unknown or unsupported codec policies fail rather than silently testing a different encoder',()=>{
    for(const settings of [{codec:'unknown'},{maximumBitrate:Infinity},{maximumBitrate:50000001}])assert.throws(()=>policy(settings));
    assert.throws(()=>policy({codec:'AV1'},{RTCRtpSender:{getCapabilities:()=>({codecs:[{mimeType:'video/H264'}]})}}).chooseCodec({}));
    assert.throws(()=>policy({codec:'H264'},{RTCRtpSender:{getCapabilities:()=>({codecs:[{mimeType:'video/H264'}]})}}).chooseCodec({getTransceivers:()=>[]}));
    policy().chooseCodec({getTransceivers:()=>assert.fail('Auto retains the existing browser negotiation')});
});
test('local policy applies only observed SDP video max and preserves codec parameters',()=>{
    const p=policy(),result=p.description({type:'offer',sdp:'v=0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=fmtp:111 minptime=10\r\nm=video 9 UDP/TLS/RTP/SAVPF 96 97 98\r\na=rtpmap:96 VP8/90000\r\na=rtpmap:97 rtx/90000\r\na=fmtp:97 apt=96\r\na=rtpmap:98 H264/90000\r\na=fmtp:98 profile-level-id=42e01f;x-google-max-bitrate=2500\r\n'});
    assert.deepEqual(Array.from(result.codecs),['VP8','H264']);
    assert.equal((result.description.sdp.match(/x-google-max-bitrate=8000/g)||[]).length,2);
    assert.match(result.description.sdp,/a=fmtp:98 profile-level-id=42e01f;x-google-max-bitrate=8000/);
    assert.match(result.description.sdp,/a=fmtp:97 apt=96/);assert.match(result.description.sdp,/a=fmtp:111 minptime=10/);
    assert.equal(result.description.sdp.includes('x-google-min-bitrate'),false);assert.equal(result.description.sdp.includes('x-google-start-bitrate'),false);
});
test('local sender policy uses supported max fields without disabling adaptation',async()=>{
    const p=policy();let parameters={encodings:[{active:true,scaleResolutionDownBy:1}],degradationPreference:'balanced'};
    const sender={track:{kind:'video'},getParameters:()=>structuredClone(parameters),setParameters:async value=>{parameters=value;}};
    const result=await p.sender({getSenders:()=>[sender]},60);
    assert.equal(parameters.encodings[0].maxBitrate,8000000);assert.equal(parameters.encodings[0].maxFramerate,60);
    assert.equal(parameters.degradationPreference,'balanced');assert.equal(parameters.encodings[0].scaleResolutionDownBy,1);
    assert.equal(Object.hasOwn(parameters.encodings[0],'minBitrate'),false);assert.equal(Object.hasOwn(parameters.encodings[0],'targetBitrate'),false);
    assert.equal(result.maxBitrate,8000000);assert.equal(p.reference.providerMinimumAndTargetApplied,false);
});
test('local policy rejects unconfirmed or unsupported sender configuration',async()=>{
    const p=policy();await assert.rejects(p.sender({getSenders:()=>[]},60));
    await assert.rejects(p.sender({},120));assert.throws(()=>p.description({type:'offer',sdp:'v=0\r\nm=audio 9 x\r\n'}));
    const sender={track:{kind:'video'},getParameters:()=>({encodings:[{}]}),setParameters:async()=>{}};
    await assert.rejects(p.sender({getSenders:()=>[sender]},60));
});

test('diagnostic read observes current sender policy without changing it or exposing transport identity',()=>{
    const p=policy();let parameters={encodings:[{maxBitrate:8000000,maxFramerate:60,scaleResolutionDownBy:1,ssrc:'private'}],degradationPreference:'balanced',transactionId:'private'};
    const track={kind:'video',contentHint:'detail'};
    const connection={getSenders:()=>[{track,getParameters:()=>parameters,setParameters:()=>assert.fail('Observation must not mutate policy')}]};
    assert.equal(p.observe(connection).scaleResolutionDownBy,1);
    parameters={encodings:[{maxBitrate:4000000,maxFramerate:30,scaleResolutionDownBy:2}],degradationPreference:'maintain-framerate'};
    track.contentHint='motion';
    const latest=p.observe(connection);
    assert.equal(latest.maxBitrate,4000000);assert.equal(latest.maxFramerate,30);
    assert.equal(latest.scaleResolutionDownBy,2);assert.equal(latest.contentHint,'motion');
    assert.equal(latest.degradationPreference,'maintain-framerate');
    assert.equal(JSON.stringify(latest).includes('private'),false);
    parameters={encodings:[{maxBitrate:NaN,maxFramerate:-1}],degradationPreference:'unknown'};
    assert.equal(p.observe(connection).maxBitrate,null);assert.equal(p.observe(connection).degradationPreference,null);
    assert.equal(p.observe({getSenders:()=>[]}),null);
    assert.equal(p.observe({getSenders(){throw Error('retired');}}),null);
});
