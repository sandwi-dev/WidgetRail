// Offline fixture for the shipped playback page; no browser or service access.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const handlers = new Map(), audio = [], messages = [];
class Audio {
  constructor() { this.paused = true; this.readyState = 4; this.duration = 200; this.currentTime = 0; this.events = {}; audio.push(this); }
  addEventListener(name, callback) { this.events[name] = callback; }
  play() { this.paused = false; return Promise.resolve(); }
  pause() { this.paused = true; this.events.pause?.(); }
  removeAttribute(name) { delete this[name]; }
  load() { }
}
const html = fs.readFileSync(path.resolve(__dirname, '../../samples/YtMusicWidget/web/player.html'), 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
vm.runInNewContext(script, { Audio, navigator: {}, chrome: { webview: { postMessage: value => messages.push(value) } },
  WidgetRailWebSession: { handle: (method, handler) => handlers.set(method, handler) } });
const call = (method, parameters) => handlers.get(method)(parameters);
const load = generation => call('load', { generation, trackId: 'song' + generation, volume: .5, url: 'http://127.0.0.1/fixture' });
load(1);
call('stop', { generation: 2 });
assert.equal(audio[0].paused, true);
load(1);
assert.equal(audio.length, 1, 'An old resolve must not restart a removed track');
load(3);
call('stop', { generation: 2 });
assert.equal(audio[1].paused, false, 'A delayed stop must not silence the newer selection');
audio[0].events.ended();
assert.equal(messages.filter(message => message.event === 'ended').length, 0, 'Retired audio must not advance the queue');
call('stop', { generation: 4 });
assert.equal(audio[1].paused, true);
assert.equal(messages.at(-1).state.playing, false);
console.log('PASS player removal generations, late load/stop and retired events');
