import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';

const [adapterPath, profile, encodedRequest] = process.argv.slice(2);
const request = JSON.parse(Buffer.from(encodedRequest, 'base64').toString('utf8'));
const html = fs.readFileSync(adapterPath, 'utf8');
const externalScripts = [...html.matchAll(/<script\s+[^>]*src=["']([^"']+)["'][^>]*><\/script>/gi)]
  .map(match => match[1]);
const scripts = [...html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/gi)]
  .map(match => match[1]).filter(Boolean);
if (externalScripts.length !== 1 || externalScripts[0] !== 'adapter-runtime.js')
  fail('adapter-runtime-source', 'expected one adjacent adapter-runtime.js script');
if (scripts.length !== 1)
  fail('adapter-script-count', `expected one inline script; got ${scripts.length}`);
const runtimePath = path.join(path.dirname(adapterPath), externalScripts[0]);
if (!fs.existsSync(runtimePath))
  fail('adapter-runtime-missing', runtimePath);
const runtimeScript = fs.readFileSync(runtimePath, 'utf8');

const publicCommands = new Map([
  ['navigatePrevious', 'previous'],
  ['navigateNext', 'next'],
  ['activate', 'arm-activate'],
  ['back', 'back'],
  ['togglePlayback', 'toggle'],
  ['seekBackward', 'seek-back'],
  ['seekForward', 'seek-forward'],
]);
const playbackCommands = new Map([
  ['Load', 'load'],
  ['Cue', 'cue'],
  ['Play', 'arm-activate'],
  ['Pause', 'pause'],
  ['Seek', 'seek'],
  ['SetVolume', 'volume'],
  ['SetPlaybackRate', 'playback-rate'],
  ['SetMuted', 'muted'],
  ['SetLoop', 'loop'],
]);
const requirements = [];
for (const command of request.declaredCommands) {
  const adapterCommand = publicCommands.get(command);
  if (!adapterCommand) fail('unknown-public-command', command);
  requirements.push({source:`declared:${command}`, command:adapterCommand});
}
for (const command of request.playbackCommands) {
  const adapterCommand = playbackCommands.get(command);
  if (!adapterCommand) fail('unknown-playback-command', command);
  requirements.push({source:`playback:${command}`, command:adapterCommand});
}

class FakeMedia {
  constructor() {
    this.currentTime = 0;
    this.duration = 60;
    this.volume = .8;
    this.playbackRate = 1;
    this.muted = false;
    this.loop = true;
    this.paused = true;
    this.readyState = 1;
    this.seeking = false;
    this.error = null;
    this.src = '';
    this.currentSrc = '';
    this.listeners = new Map();
  }
  addEventListener(name, callback) {
    const callbacks = this.listeners.get(name) ?? [];
    callbacks.push(callback);
    this.listeners.set(name, callbacks);
  }
  removeEventListener(name, callback) {
    this.listeners.set(name, (this.listeners.get(name) ?? [])
      .filter(value => value !== callback));
  }
  dispatchEvent(event) {
    for (const callback of [...(this.listeners.get(event.type) ?? [])]) callback(event);
  }
  load() {
    this.currentSrc = this.src;
    this.dispatchEvent({type:'loadedmetadata'});
  }
  async play() {
    this.paused = false;
  }
  pause() {
    this.paused = true;
  }
}

const pageEvents = [];
let messageListener = null;
let stateCallback = null;
let playbackRateCallback = null;
let playerErrorCallback = null;
let playbackRateTimeout = null;
let suppressPlaybackRateEvent = false;
let providerErrorCode = null;
let mutedValueAfterReads = 0;
let pendingMutedValue = null;
let holdMutedPolls = false;
let heldMutedPolls = [];
function emitProviderError() {
  if (providerErrorCode === null) return false;
  const code = providerErrorCode;
  providerErrorCode = null;
  playerErrorCallback?.({data:code});
  return true;
}
const media = new FakeMedia();
const player = {
  state: 5,
  currentTime: 0,
  duration: 120,
  volume: 80,
  playbackRate: 1,
  muted: false,
  loop: false,
  videoId: '',
  availablePlaybackRates: [.5, .75, 1, 1.25, 1.5, 2],
  cueVideoById(options) {
    this.videoId = options.videoId;
    this.state = 5;
    stateCallback?.({data:5});
  },
  playVideo() { this.state = 1; stateCallback?.({data:1}); },
  pauseVideo() { this.state = 2; stateCallback?.({data:2}); },
  seekTo(value) { this.currentTime = value; },
  setVolume(value) { this.volume = value; },
  setPlaybackRate(value) {
    this.playbackRate = value;
    if (emitProviderError()) return;
    if (!suppressPlaybackRateEvent)
      queueMicrotask(() => playbackRateCallback?.({data:value}));
  },
  getPlaybackRate() { return this.playbackRate; },
  getAvailablePlaybackRates() { return [...this.availablePlaybackRates]; },
  mute() { requestMuted(true); },
  unMute() { requestMuted(false); },
  isMuted() {
    if (pendingMutedValue !== null && Number.isFinite(mutedValueAfterReads) &&
        --mutedValueAfterReads <= 0) {
      this.muted = pendingMutedValue;
      pendingMutedValue = null;
    }
    return this.muted;
  },
  setLoop(value) { this.loop = value; emitProviderError(); },
  getVideoData() { return {video_id:this.videoId}; },
  getPlayerState() { return this.state; },
  getCurrentTime() { return this.currentTime; },
  getDuration() { return this.duration; },
  getVolume() { return this.volume; },
};
const document = {
  documentElement: {clientWidth:640, clientHeight:360},
  head: {appendChild() {
    queueMicrotask(() => context.window.onYouTubeIframeAPIReady?.());
  }},
  createElement() { return {}; },
  querySelector(selector) { return selector === '#media' ? media : null; },
};
const context = vm.createContext({
  console,
  document,
  location:{origin:'https://installed-widget.invalid'},
  DOMException,
  Event: class Event { constructor(type) { this.type = type; } },
  Map,
  Number,
  Promise,
  AbortController,
  String,
  Object,
  Math,
  setInterval() { return 1; },
  clearInterval() {},
  setTimeout(callback, delay) {
    if (delay === 25 && holdMutedPolls) {
      heldMutedPolls.push(callback);
    } else if (delay < 1_000) queueMicrotask(callback);
    else if (delay === 1_500) playbackRateTimeout = callback;
    return 1;
  },
  clearTimeout() { playbackRateTimeout = null; },
  chrome:{webview:{
    addEventListener(name, callback) {
      if (name === 'message') messageListener = callback;
    },
    postMessage(value) { pageEvents.push(structuredClone(value)); },
  }},
});
context.window = context;
context.YT = {Player: function Player(_id, options) {
  stateCallback = options.events.onStateChange;
  playbackRateCallback = options.events.onPlaybackRateChange;
  playerErrorCallback = options.events.onError;
  queueMicrotask(() => options.events.onReady());
  return player;
}};

vm.runInContext(runtimeScript, context, {filename:runtimePath, timeout:2_000});
vm.runInContext(scripts[0], context, {filename:adapterPath, timeout:2_000});
await flush();
if (!messageListener)
  fail('message-listener-missing', 'adapter did not register the command boundary');
let commandId = 40;
let commandSequence = 70;
let currentMediaKey = profile === 'html-media' ? 'aurora-video-0' : 'M7lc1UVf-VE';
let authority = {
  environmentGeneration:1,
  surfaceGeneration:2,
  sessionGeneration:3,
  controllerGeneration:4,
  documentGeneration:5,
};
await send({command:'initialize', ...authority, commandId:++commandId});
const ready = pageEvents.at(-1);
if (ready?.type !== 'ready' || ready.commandId !== commandId)
  fail('initialize-correlation', 'initial ready envelope was absent or uncorrelated');
const playbackKeys = [
  'mediaKey', 'playbackState', 'positionSeconds', 'durationSeconds', 'volume'];
const readyPlaybackKeys = playbackKeys.filter(key => Object.hasOwn(ready, key));
if (readyPlaybackKeys.length !== 0 && readyPlaybackKeys.length !== playbackKeys.length)
  fail('initialize-playback-block',
    `initial ready envelope had partial playback keys: ${readyPlaybackKeys.join(',')}`);
if (readyPlaybackKeys.length !== 0 &&
    (typeof ready.mediaKey !== 'string' || ready.mediaKey.length === 0))
  fail('initialize-media-key', 'initial ready playback block had an empty media key');
if (profile === 'state-callback' && readyPlaybackKeys.length !== 0)
  fail('initialize-empty-media-ready',
    'state-callback initial ready must omit playback fields before media is loaded');

await exercise({source:'bootstrap:load', command:'load'});
for (const requirement of requirements) await exercise(requirement);
if (request.playbackCommands.includes('SetPlaybackRate'))
  await exercisePlaybackRateContract();
if (request.playbackCommands.includes('SetMuted'))
  await exerciseMutedContract();
if (request.playbackCommands.some(command =>
  ['SetPlaybackRate', 'SetMuted', 'SetLoop'].includes(command)))
  await exercisePreferenceProviderErrors();
await exercise(
  {source:'expected-error:media-key-mismatch', command:'volume'},
  {mediaKey:`${currentMediaKey}-mismatch`, expectedErrorCode:'media-key-mismatch'});

console.log(JSON.stringify({requirements:requirements.length, profile}));

async function exercise(requirement, options = {}) {
  const expectedErrorCode = options.expectedErrorCode ?? null;
  const before = pageEvents.length;
  const id = ++commandId;
  const sequence = ++commandSequence;
  const message = {
    command:requirement.command,
    ...authority,
    commandId:id,
    commandSequence:sequence,
    mediaKey:options.mediaKey ?? currentMediaKey,
    positionSeconds:12,
    volume:.65,
    playbackRate:options.playbackRate ?? 1.25,
    muted:options.muted ?? true,
    loop:false,
  };
  providerErrorCode = options.providerErrorCode ?? null;
  mutedValueAfterReads = options.mutedValueAfterReads ?? 0;
  holdMutedPolls = options.triggerMutedTimeout === true;
  if (holdMutedPolls) heldMutedPolls = [];
  await send(message);
  providerErrorCode = null;
  if (options.emitSuppressedRate) {
    playbackRateCallback?.({data:message.playbackRate});
    await flush();
  }
  if (options.triggerRateTimeout) {
    if (!playbackRateTimeout)
      fail('rate-timeout-missing', `${requirement.source} did not arm its timeout`);
    playbackRateTimeout();
    await flush();
  }
  if (options.triggerMutedTimeout) {
    for (let index = 0; index < 60 && heldMutedPolls.length > 0; index++) {
      heldMutedPolls.shift()();
      await flush();
    }
    holdMutedPolls = false;
    heldMutedPolls = [];
  }
  const armed = pageEvents.slice(before).find(event =>
    event.type === 'armed' && event.commandId === id && event.commandSequence === sequence);
  if (requirement.command === 'arm-activate' && armed) {
    if (profile === 'html-media') media.onclick?.();
    else player.playVideo();
    await flush();
  }
  const correlated = pageEvents.slice(before).filter(event =>
    event.commandId === id && event.commandSequence === sequence && event.type !== 'armed');
  if (correlated.length !== 1)
    fail('terminal-count',
      `${requirement.source} (${requirement.command}) produced ${correlated.length} correlated terminals`);
  const terminal = correlated[0];
  if (!['media','back'].includes(terminal.type))
    fail('terminal-type', `${requirement.source} (${requirement.command}) produced ${terminal.type}`);
  if (expectedErrorCode !== null && terminal.errorCode !== expectedErrorCode)
    fail('terminal-error-mismatch',
      `${requirement.source} expected ${expectedErrorCode}; got ${terminal.errorCode ?? 'success'}`);
  if (expectedErrorCode === null && terminal.errorCode === 'command-unsupported')
    fail('command-unsupported',
      `${requirement.source} requires adapter message '${requirement.command}'`);
  if (expectedErrorCode === null && terminal.errorCode)
    fail('unexpected-terminal-error', `${requirement.source} produced ${terminal.errorCode}`);
  if (options.expectedPlaybackState &&
      terminal.playbackState !== options.expectedPlaybackState)
    fail('terminal-playback-state',
      `${requirement.source} expected ${options.expectedPlaybackState}; got ${terminal.playbackState}`);
  if (!terminal.errorCode && terminal.mediaKey) currentMediaKey = terminal.mediaKey;

  const observationsBefore = pageEvents.length;
  if (profile === 'html-media') media.dispatchEvent({type:'timeupdate'});
  else stateCallback?.({data:player.state});
  await flush();
  const falseAcknowledgements = pageEvents.slice(observationsBefore).filter(event =>
    event.commandId !== 0 || event.commandSequence !== 0);
  if (falseAcknowledgements.length !== 0)
    fail('unsolicited-correlation', `${requirement.source} observation acknowledged a command`);

  const probeId = ++commandId;
  const probeSequence = ++commandSequence;
  const probeStart = pageEvents.length;
  await send({command:'volume', ...authority, commandId:probeId,
    commandSequence:probeSequence, mediaKey:currentMediaKey, volume:.7});
  const probe = pageEvents.slice(probeStart).filter(event =>
    event.commandId === probeId && event.commandSequence === probeSequence &&
    event.type !== 'armed');
  if (probe.length !== 1)
    fail('in-flight-not-released', `${requirement.source} left the adapter operation in flight`);
  if (probe[0].type !== 'media' || probe[0].errorCode)
    fail('in-flight-probe-failed',
      `${requirement.source} release probe produced ${probe[0].errorCode ?? probe[0].type}`);
  return terminal;
}

async function exercisePlaybackRateContract() {
  player.state = 2;
  const paused = await exercise(
    {source:'rate:paused-late-success', command:'playback-rate'},
    {playbackRate:1.5, expectedPlaybackState:'paused'});
  if (paused.playbackRate !== 1.5)
    fail('rate-paused-value', `expected 1.5; got ${paused.playbackRate}`);

  player.state = 5;
  const cued = await exercise(
    {source:'rate:cued-late-success', command:'playback-rate'},
    {playbackRate:.75, expectedPlaybackState:'ready'});
  if (cued.playbackRate !== .75)
    fail('rate-cued-value', `expected .75; got ${cued.playbackRate}`);

  player.availablePlaybackRates = [.5, 1, 1.5, 2];
  await exercise(
    {source:'rate:unavailable', command:'playback-rate'},
    {playbackRate:1.25, expectedErrorCode:'command-unsupported'});
  player.availablePlaybackRates = [.5, .75, 1, 1.25, 1.5, 2];

  suppressPlaybackRateEvent = true;
  await exercise(
    {source:'rate:timeout', command:'playback-rate'},
    {playbackRate:1.25, expectedErrorCode:'player-operation-timeout',
      triggerRateTimeout:true});

  player.videoId = `${currentMediaKey}-stale`;
  await exercise(
    {source:'rate:stale-video', command:'playback-rate'},
    {playbackRate:1.5, expectedErrorCode:'player-operation-timeout',
      emitSuppressedRate:true, triggerRateTimeout:true});
  player.videoId = currentMediaKey;
  suppressPlaybackRateEvent = false;
}

async function exercisePreferenceProviderErrors() {
  player.state = 2;
  for (const [source, command, playbackRate] of [
    ['rate', 'playback-rate', 1.5],
    ['mute', 'muted', 1],
    ['loop', 'loop', 1],
  ]) {
    const terminal = await exercise(
      {source:`preference-provider-error:${source}`, command},
      {playbackRate, providerErrorCode:101,
        expectedErrorCode:'embedding-disabled', expectedPlaybackState:'error'});
    if (terminal.mediaKey !== currentMediaKey)
      fail('preference-provider-error-media-key',
        `${source} terminal lost current media authority`);
  }
}

async function exerciseMutedContract() {
  player.state = 2;
  player.muted = false;
  pendingMutedValue = null;
  const muted = await exercise(
    {source:'muted:delayed-success', command:'muted'},
    {muted:true, mutedValueAfterReads:3, expectedPlaybackState:'paused'});
  if (muted.muted !== true)
    fail('muted-delayed-value', 'delayed mute acknowledged before the provider applied it');

  const unmuted = await exercise(
    {source:'muted:delayed-unmute', command:'muted'},
    {muted:false, mutedValueAfterReads:3, expectedPlaybackState:'paused'});
  if (unmuted.muted !== false)
    fail('unmuted-delayed-value', 'delayed unmute acknowledged before the provider applied it');

  player.muted = false;
  pendingMutedValue = null;
  await exercise(
    {source:'muted:timeout', command:'muted'},
    {muted:true, mutedValueAfterReads:Number.POSITIVE_INFINITY,
      expectedErrorCode:'player-operation-timeout', triggerMutedTimeout:true});

  player.muted = false;
  pendingMutedValue = null;
  holdMutedPolls = true;
  heldMutedPolls = [];
  const staleId = ++commandId;
  const staleSequence = ++commandSequence;
  const staleStart = pageEvents.length;
  await send({command:'muted', ...authority, commandId:staleId,
    commandSequence:staleSequence, mediaKey:currentMediaKey, muted:true});
  const stalePoll = heldMutedPolls.at(-1);
  if (!stalePoll)
    fail('muted-stale-poll-missing', 'mute did not retain its provider confirmation poll');
  if (pageEvents.slice(staleStart).some(event =>
      event.commandId === staleId && event.commandSequence === staleSequence))
    fail('muted-stale-early-terminal', 'unconfirmed mute published a terminal');

  const successorAuthority = {...authority, documentGeneration:authority.documentGeneration + 1};
  await send({command:'initialize', ...successorAuthority, commandId:++commandId});
  authority = successorAuthority;
  stalePoll();
  await flush();
  if (pageEvents.slice(staleStart).some(event =>
      event.commandId === staleId && event.commandSequence === staleSequence))
    fail('muted-stale-terminal', 'retired mute acknowledged the successor authority');

  holdMutedPolls = false;
  heldMutedPolls = [];
  pendingMutedValue = null;
  const successor = await exercise(
    {source:'muted:successor-authority', command:'muted'},
    {muted:true, mutedValueAfterReads:2, expectedPlaybackState:'paused'});
  if (successor.muted !== true)
    fail('muted-successor-value', 'successor mute did not publish confirmed provider state');
}

function requestMuted(value) {
  if (emitProviderError()) return;
  if (mutedValueAfterReads <= 0) {
    player.muted = value;
    pendingMutedValue = null;
    return;
  }
  pendingMutedValue = value;
}

async function send(data) {
  await messageListener({data});
  await flush();
}
async function flush() {
  for (let index = 0; index < 5; index++) await Promise.resolve();
}
function fail(code, message) {
  console.error(`${code}: ${message}`);
  process.exit(1);
}
