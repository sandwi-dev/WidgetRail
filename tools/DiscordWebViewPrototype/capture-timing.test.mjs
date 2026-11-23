import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('./capture-timing.js',import.meta.url),'utf8');
test('diagnostic timing aggregates construction, write and received-to-ACK independently',async()=>{
    let now=0,listener,completed;const sent=[];
    class Frame{constructor(){now+=4;}}
    class Generator{constructor(){this.writable={getWriter:()=>({write:()=>new Promise(resolve=>completed=resolve)})};}}
    const context={VideoFrame:Frame,MediaStreamTrackGenerator:Generator,performance:{now:()=>now},
        chrome:{webview:{addEventListener:(_,fn)=>listener=fn,postMessage:value=>sent.push(value)}}};
    vm.createContext(context);vm.runInContext(source,context);
    const writer=new context.MediaStreamTrackGenerator({kind:'video'}).writable.getWriter();
    context.chrome.webview.addEventListener('sharedbufferreceived',async event=>{
        const frame=new context.VideoFrame();await writer.write(frame);now+=1;
        context.chrome.webview.postMessage({captureAck:1,captureSequence:event.additionalData.sequence});
    });
    const pending=listener({additionalData:{kind:1,sequence:1}});
    assert.equal(context.captureProbeTiming().pendingWrites,1);now+=5;completed();await pending;
    const result=context.captureProbeTiming();assert.equal(result.construction.meanMs,4);assert.equal(result.writerWrite.meanMs,5);
    assert.equal(result.receivedToAckSent.meanMs,10);assert.equal(result.maximumPendingWrites,1);assert.equal(result.pendingAcknowledgements,0);
    assert.deepEqual(sent,[{captureAck:1,captureSequence:1}]);
});
test('diagnostic timing bounds unacknowledged observation storage',()=>{
    let listener;const context={VideoFrame:class{},MediaStreamTrackGenerator:class{},performance:{now:()=>1},
        chrome:{webview:{addEventListener:(_,fn)=>listener=fn,postMessage:()=>{}}}};
    vm.createContext(context);vm.runInContext(source,context);context.chrome.webview.addEventListener('sharedbufferreceived',()=>{});
    for(let sequence=0;sequence<100;sequence++)listener({additionalData:{kind:1,sequence}});
    assert.equal(context.captureProbeTiming().pendingAcknowledgements,8);
});

test('cadence separates native timestamps from delayed browser delivery and ignores epoch discontinuities',()=>{
    let now=0,listener;
    const context={VideoFrame:class{},MediaStreamTrackGenerator:class{},performance:{now:()=>now},
        WidgetRailNativeMedia:{status:()=>({targetFrameRate:60})},
        chrome:{webview:{addEventListener:(_,fn)=>listener=fn,postMessage:()=>{}}}};
    vm.createContext(context);vm.runInContext(source,context);context.chrome.webview.addEventListener('sharedbufferreceived',()=>{});
    const frame=(epoch,sequence,time,at)=>{now=at;listener({additionalData:{kind:1,epoch,sequence,time}});};
    frame(1,1,1000,0);frame(1,2,17667,40);frame(1,3,34334,41);
    let result=context.captureProbeTiming();
    assert.equal(result.nativeTimestampIntervals.count,2);assert.equal(result.nativeTimestampIntervals.lateIntervals,0);
    assert.equal(result.browserDeliveryIntervals.count,2);assert.equal(result.browserDeliveryIntervals.lateIntervals,1);
    assert.equal(result.browserDeliveryIntervals.maxMs,40);assert.equal(result.expectedFrameIntervalMs,1000/60);
    frame(2,1,1,5000);frame(2,2,33335,5033);
    result=context.captureProbeTiming();
    assert.equal(result.nativeTimestampIntervals.count,3);assert.equal(result.nativeTimestampIntervals.lateIntervals,1);
    assert.equal(result.browserDeliveryIntervals.count,3);assert.equal(result.browserDeliveryIntervals.lateIntervals,2);
    assert.equal(result.browserDeliveryIntervals.maxMs,40);
});
