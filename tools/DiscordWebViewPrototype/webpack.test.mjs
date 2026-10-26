import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
const source=fs.readFileSync(new URL('./webpack.js',import.meta.url),'utf8');
function load(runtimes){const chunks=[];chunks.push=function(item){Array.prototype.push.call(this,item);for(const r of runtimes)item[2](r);};const c={location:{origin:'https://discord.com'},crypto:{randomUUID:()=> 'fixture'},webpackChunkdiscord_app:chunks};c.window=c;c.top=c;vm.createContext(c);vm.runInContext(source,c);return c.WidgetRailWebWebpack;}
test('discovery covers every runtime instead of the last one',()=>{const user={getName:()=> 'UserStore',getCurrentUser:()=>({})};const d=load([{c:{one:{exports:{default:user}}}},{c:{two:{exports:{unrelated:true}}}}]);assert.equal(d.findStore('UserStore'),user);});
test('manufactured members cannot satisfy a capability',()=>{const fake=new Proxy({}, {get:(target,key)=>target[key]??(target[key]=()=>{})});const real={selectVoiceChannel(){}};const d=load([{c:{fake:{exports:fake},real:{exports:real}}}]);assert.equal(d.findByProps('selectVoiceChannel'),real);});
test('real inherited dispatcher methods remain discoverable',()=>{class Dispatcher{dispatch(){}subscribe(){}wait(){}}const value=new Dispatcher();const d=load([{c:{one:{exports:value}}}]);assert.equal(d.Common.FluxDispatcher,value);});
test('misses can recover when a module loads later',()=>{const runtime={c:{}};const d=load([runtime]);assert.equal(d.findByProps('toggleSelfMute'),undefined);const actions={toggleSelfMute(){}};runtime.c.late={exports:actions};assert.equal(d.findByProps('toggleSelfMute'),actions);});
test('throwing exports do not prevent finding a valid module',()=>{const bad={};Object.defineProperty(bad,'oops',{enumerable:true,get(){throw Error('fixture');}});const good={hello(){}};const d=load([{c:{bad:{exports:bad},good:{exports:good}}}]);assert.equal(d.findByProps('hello'),good);});
