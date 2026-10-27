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
