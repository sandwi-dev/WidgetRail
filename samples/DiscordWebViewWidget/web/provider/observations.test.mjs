import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL('./observations.js',import.meta.url),'utf8');
function fixture(){
    const stores={},events=[];
    const context=vm.createContext({});vm.runInContext(source,context);
    const observer=context.createWidgetRailDiscordObserver(()=>({findStore:name=>stores[name]}),(...args)=>events.push(args));
    return {stores,events,observer};
}
function store(){
    const listeners=new Set();return {listeners,addChangeListener:cb=>listeners.add(cb),removeChangeListener:cb=>listeners.delete(cb),emit(){for(const cb of listeners)cb();}};
}
test('provider changes coalesce without identity or state payloads',async()=>{
    const f=fixture(),voice=f.stores.VoiceStateStore=store(),speaking=f.stores.SpeakingStore=store();
    f.observer.refresh();f.observer.refresh();assert.equal(voice.listeners.size,1);
    for(let i=0;i<100;i++){voice.emit();speaking.emit();}
    assert.equal(f.events.length,0);await Promise.resolve();assert.deepEqual(f.events,[[]]);
    voice.emit();await Promise.resolve();assert.equal(f.events.length,2);f.observer.dispose();
});
test('late store discovery works and replaced store callbacks are retired',async()=>{
    const f=fixture();f.observer.refresh();const first=f.stores.SpeakingStore=store();f.observer.refresh();
    const old=[...first.listeners][0],second=f.stores.SpeakingStore=store();f.observer.refresh();
    assert.equal(first.listeners.size,0);old();await Promise.resolve();assert.equal(f.events.length,0);
    second.emit();await Promise.resolve();assert.equal(f.events.length,1);f.observer.dispose();
});
test('teardown suppresses queued notifications and removes every subscription',async()=>{
    const f=fixture(),voice=f.stores.VoiceStateStore=store();f.observer.refresh();voice.emit();
    f.observer.dispose();await Promise.resolve();assert.equal(f.events.length,0);assert.equal(voice.listeners.size,0);
    f.observer.refresh();assert.equal(voice.listeners.size,0);
});
test('unknown store interfaces fail independently while known stores still notify',async()=>{
    const f=fixture();f.stores.MediaEngineStore={addChangeListener(){throw Error('changed API');},removeChangeListener(){}};
    f.stores.RTCConnectionStore={addChangeListener(){throw Error('must not subscribe without removal');}};
    const voice=f.stores.VoiceStateStore=store();assert.doesNotThrow(()=>f.observer.refresh());
    voice.emit();await Promise.resolve();assert.equal(f.events.length,1);f.observer.dispose();
});
