import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import vm from 'node:vm';

const script = await readFile(new URL('./encoded-source-worker.js', import.meta.url), 'utf8');

test('native errors fall back to a keyframe on the same RTP clock',async()=>{
    let now=100, read;
    const messages=[],written=[],options=[];
    const source={allocatedBitrate:2500000,writable:{getWriter:()=>({write:async f=>written.push(f)})}};
    const prefix=new Uint8Array([0,0,1,0x67,0x42,0xc0,0x1f,0]);
    class Chunk{
        constructor({type,timestamp,data}){this.type=type;this.timestamp=timestamp;this.data=new Uint8Array(data);this.byteLength=this.data.length;}
        copyTo(out){out.set(this.data);}
    }
    class Encoder{
        state='unconfigured';encodeQueueSize=0;
        static async isConfigSupported(){return {supported:true};}
        constructor({output}){this.output=output;}
        configure(){this.state='configured';}
        encode(f,o){options.push(o);this.output(new Chunk({type:o.keyFrame?'key':'delta',timestamp:f.timestamp,data:prefix}));}
    }
    const self={},settle=()=>new Promise(resolve=>setImmediate(resolve));
    vm.runInNewContext(script+'\nrunEncodedSourceWorker();',{self,performance:{now:()=>now},postMessage:m=>messages.push(m),
        VideoEncoder:Encoder,EncodedVideoChunk:Chunk,RTCEncodedVideoFrame:class{constructor(f){Object.assign(this,f);}}});
    self.onrtcsenderencodedsource({encodedSource:source});
    self.onmessage({data:{nativeEncoder:true,width:1280,height:720,fps:30,profile:'42e01f',minimumLevel:31,maximumLevel:31,readable:{getReader:()=>({read:()=>new Promise(r=>{read=r;})})}}});
    await settle();
    const raw=async stamp=>{read({value:{timestamp:stamp,displayWidth:1280,displayHeight:720,close(){}}});await settle();};
    await raw(0);
    assert.equal(messages.find(m=>m.kind==='nativeRate').bitrate,2500000);
    self.onmessage({data:{kind:'nativeUnit',unit:{type:'key',width:1280,height:720,time:1000000000,data:prefix}}});
    await settle();now+=34;self.onmessage({data:{kind:'nativeUnit',unit:null}});
    await raw(34000);
    assert.equal(written.length,2);assert.equal(options[0].keyFrame,true);
    assert.equal(written[1].rtpTimestampWithoutOffset-written[0].rtpTimestampWithoutOffset,3060);
    assert.ok(messages.some(m=>m.kind==='nativeClosed'));
    self.onmessage({data:{kind:'nativeUnit',unit:{type:'key',width:1280,height:720,time:1000068000,data:prefix}}});
    assert.equal(written.length,2);read({done:true});await settle();
});

test('the external encoder follows allocation, pause, and changing or removed sender ceilings', async () => {
    const configured = [], encoded = [], samples = [];
    let time = 0, readFrame;
    const reader = { read: () => new Promise(resolve => { readFrame = resolve; }) };
    const source = { allocatedBitrate: 0, writable: { getWriter: () => ({}) } };
    class Encoder {
        state = 'unconfigured';
        encodeQueueSize = 0;
        static async isConfigSupported() { return { supported: true }; }
        configure(config) {
            assert.equal(config.codec, 'avc1.42e033');
            assert.equal(config.framerate, 60);
            assert.equal(config.width, 3840);
            configured.push(config.bitrate);
            this.state = 'configured';
        }
        encode(frame) { encoded.push(frame.timestamp); }
    }
    const self = {};
    vm.runInNewContext(script + '\nrunEncodedSourceWorker();', {
        self, performance: { now: () => time }, postMessage: sample => samples.push(sample), VideoEncoder: Encoder
    });
    self.onrtcsenderencodedsource({ encodedSource: source });
    self.onmessage({ data: { readable: { getReader: () => reader }, width: 3840, height: 1080, fps: 60, profile: '42e033' } });
    const settle = () => new Promise(resolve => setImmediate(resolve));
    await settle();
    async function frame(allocation) {
        time += 1000;
        source.allocatedBitrate = allocation;
        readFrame({ value: { displayWidth: 3840, displayHeight: 1080, timestamp: time * 1000, close() {} } });
        await settle();
    }
    await frame(0); // No feedback yet. WebCodecs chooses its initial target.
    await frame(6_000_000);
    await frame(19_000);
    await frame(0); // WebRTC suspended an already allocated sender.
    assert.equal(encoded.length, 3);
    await frame(4_500_000);
    await frame(4_900_000);
    self.onmessage({ data: { kind: 'ceiling', ceiling: 2_000_000 } });
    await frame(4_900_000);
    self.onmessage({ data: { kind: 'ceiling', ceiling: 0 } });
    await frame(4_900_000);
    assert.equal(encoded.length, 6);
    self.onmessage({ data: { kind: 'ceiling', ceiling: null } });
    await frame(4_900_000);
    assert.deepEqual(configured, [undefined, 6_000_000, 19_000, 4_500_000, 4_900_000, 2_000_000, 4_900_000]);
    assert.equal(samples.at(-1).configured, 4_900_000);
    assert.equal(samples.at(-1).ceiling, null);
    readFrame({ done: true });
    await settle();
});
