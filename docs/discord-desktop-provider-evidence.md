# Discord Desktop provider evidence

Date: 2026-10-27. Read-only source investigation for the
[desktop integration plan](discord-desktop-integration-plan.md).
No Discord code was executed, native module initialized, client patched, call
changed, profile inspected or source enumerated during this investigation.

## Scope and provenance

Two source classes must remain distinct:

1. Installed official Discord `app-1.0.9265` code under `modules/discord_voice-1/discord_voice/index.js`.
   SHA256: `C06FC0D68FBEB6B139E10AE98810918CB91512325E221922FE0DE9B85A9CB8C1`.
   This confirms what that native wrapper exposes on disk, not which runtime
   engine or modules a currently running renderer has selected.
2. Previously downloaded public Discord web bundle
   `artifacts/discord-webview/provider-source-20261027/public-web.33a8a649a2fda558.js`.
   SHA256: `5B2AFCF1DEA50E7E6E98E460CF8DBA00101FF37C4610363F7002719DE4F7BB1C`.
   It includes native, web and dummy implementations. Selected functions were
   extracted using Acorn syntax parsing and Terser formatting into
   `artifacts/discord-desktop-provider-20261027/`. Neither parser executes the
   provider code. Its native branch is useful evidence, but the installed client
   may load another web build or experiment configuration.

Upstream Vencord source inspected locally at commit
`443688b5366a4d43e9868519f48a4544cd7520d4` in
`artifacts/discord-desktop/vencord-source-review`.

Numeric module IDs and minified export names below identify evidence in that
specific bundle. They are not proposed runtime binding keys. Runtime discovery
must use loaded stores and bounded export-shape/source signatures, fail closed
on ambiguity, and never execute unknown module factories to make an API appear.

## Findings that change the migration approach

- The native engine's `getDesktopSource()` **always rejects `NO_STREAM`** in
  module206959, lines1966-1968. The browser implementation acquires/transfers a
  media stream. The existing WebView `beginShare` flow cannot simply be routed
  through a different IPC connection.
- The native engine already renders decoded video as a muted HTML video element
  using `window.createDiscordStream(streamId)` with reference-counted native
  output sinks. A client-owned native video component is a concrete route to
  investigate before any CPU snapshot or local WebRTC relay.
- A real source-enumeration helper and native source-to-PID mapping exist.
  Hand-inventing IDs from window titles is unnecessary and incorrect.
- `MediaEngineStore.isSelfMute()` includes microphone permission, disabled audio,
  deafen and restrictions. It is not the persisted mute preference. Desired-state
  controls must not read it and blindly toggle in response.
- DEFAULT voice audio and STREAM audio are separate provider contexts. The stream
  raw-volume default is18, while voice defaults to100. Display percentages are
  not interchangeable with raw engine gain.

## 1. Native-engine boundary

Installed voice wrapper lines1-17 loads `discord_voice.node` and determines
renderer context from `window.DiscordNative.isRenderer`. Lines125-190 wrap a
native VoiceConnection with self mute/deafen, local mute/volume, statistics,
native source selection, source-status/ended and soundshare callbacks.
Lines197-209 construct that native connection and provide the own-stream alias.

Cached MediaEngineStore module360906 lines8-27 configures native dependencies
only in the embedded platform branch and verifies a native `VoiceConnection`.
Module823598 supplies the injected native dependency accessors. Calling its
`getVoiceEngine` accessor may initialize/cache a native module; a read-only
capability probe should not invoke it just to inspect availability.

Safe probe observations, reported separately rather than collapsed into a
hardware/native-success claim:

- `typeof window.DiscordNative === 'object'` and its `isRenderer` boolean.
- `typeof window.createDiscordStream === 'function'`.
- Loaded MediaEngineStore and its `getMediaEngine`, `getVideoComponent` getters.
- Existing engine method shapes: `setGoLiveSource`, `getWindowPreviews`,
  `getScreenPreviews`, `getSingleWindowPreview`, `supports` and `Video`.
- `engine.supports('ELECTRON_VIDEO')`, when the verified enum/string contract
  remains available. Module206959 lines1674-1675 delegates this to the native
  feature registry. It proves a reported feature, not a hardware encoder choice.

Existence of `getDesktopSource`, `supported()` or a `Video` function alone does
not distinguish native from web/dummy implementations. Constructor names are
minified and must not become runtime identity. Never instantiate another native
engine: its constructor registers global device/video/watchdog callbacks and could
replace the current client's handlers.

## 2. Application selection and native sharing

### Sources and source identity

Extracted module742459 lines9-23 contains the desktop source-list helper:

```text
listDesktopSources(engine, useWgc, types?, thumbnailSize?)
```

It requires DiscordNative, restricts types to window/screen, prefers the engine's
native window/screen preview APIs when supported, and otherwise uses
`DiscordNative.desktopCapture.getDesktopCaptureSources`.

Module206959 lines1969-1979 and2094-2109 implements native preview wrappers.
These paths can capture thumbnails; invoking them is not equivalent to a pure
metadata query. The requested list-only chooser must verify whether the actual
desktop API supports zero-thumbnail enumeration, or discard bounded thumbnails
without persisting/exporting them. Do not assume zero dimensions work in every
native wrapper.

Module19575 lines661-680 maps `window:<handle>` through native
`getPidFromWindowHandle`, provides `getDesktopSourceFromPid`, and exposes a separate
`getAudioPid`. Screen/prepicked source IDs have distinct semantics. Module830012
compares a window source's handle component with a game window handle.

Proposed flow: obtain provider source IDs from this exact client, validate them
against current native window/process identity in the host, and return only a
short-lived selection token. Revalidate before starting. Avoid trusting a source
label, PID alone, reused HWND, or a stale saved list row.

### Native start and stop

Module827343 `setGoLiveSource` (existing formatted file lines343-353) dispatches
`MEDIA_ENGINE_SET_GO_LIVE_SOURCE` with settings. Current source shape includes:

```text
{
  desktopSettings: { sourceId, sound },
  qualityOptions: { preset, resolution, frameRate },
  context: 'stream'
}
```

MediaEngineStore360906 lines2310-2338 translates those settings into its native
desktop source, soundshare identity and quality. Its `ny` helper (956-986) maps
audio PID, creates soundshare identity and can initiate the native audio/hook
path. This is a side-effectful operation, not a source-validation probe.

Native connection module206959 lines936-981 applies that source using
`setDesktopSourceWithOptions`: source type/handle, native hook/WGC flags and other
provider policy are supplied by Discord. It also sets soundshare and encoding
options. The adapter should call the established high-level provider actions,
not invoke native connection setters directly or replace Discord's capture policy.

Module401843 lines25-35 starts provider streaming with
`startStream(guildId, channelId, options)` -> `STREAM_START`;
lines83-103 distinguish `STREAM_STOP` from `STREAM_CLOSE`.
The native picker’s exact initial ordering of source setup, quality persistence
and STREAM_START remains a live binding/fixture gate. Finding both operations
does not prove that arbitrarily dispatching them in either order is correct.

The stream action module's watch path accepts a stream descriptor, not only its
public key; it can change channel selection/focus. Watch-and-popout is separate
from watch without focus. Preserve these distinctions in typed commands.

### Quality and persisted choice

Module327649 exposes the `canStreamWithSettings` helper. Module753070 defines
presets, resolutions and FPS options including1440p/60; its eligibility table
references account entitlements and server tier. Do not duplicate premium logic.
Use the currently loaded helper for each requested pair.

Module401843 lines140-146 dispatches `STREAM_UPDATE_SETTINGS`. Native quality UI
module405916 lines19-88 checks eligibility and routes an active source's changed
quality back through `setGoLiveSource`. It reads ApplicationStreamingSettingsStore
and preserves source/audio while changing quality.

Module907926 shows an auto-quality controller that can change source quality in
response to native stats under PRESET_AUTO. This is provider policy and should
remain enabled when that preset is selected. Browser `contentHint`, outgoing SDP
profile rewriting and our18Mbps browser patch are not native equivalents.

Application-only audio is not proven merely by passing `sound:true`. The current
source PID may be remapped, and screen sources have different audio semantics.
Gate application-only audio on current native capability and a real receive-side
test; do not silently fall back to whole-system loopback.

## 3. Voice, audio and devices

Existing formatted module360906 locations:

| Getter | Line | Meaning / caution |
| --- | --- | --- |
| `getMediaEngine()` / `getVideoComponent()` |1520/1523 | Existing engine/component only; do not construct a replacement. |
| `isMute()` / `isDeaf()` |1538/1541 | Effective state including server restrictions. |
| `isSelfMute()` / `isSelfDeaf()` |1557/1580 | Includes more than raw preference; self mute includes permission/enable/deafen. |
| `isScreenSharing()` / `isSoundSharing()` |1596/1600 | Provider local-source state, not remote picture/audio proof. |
| `isLocalMute(user, context)` |1604 | Context defaults to voice. |
| `getGoLiveSource()` / `getGoLiveContext()` |1633/1636 | Current source identity; export only explicitly allowed fields. |
| `getLocalVolume(user, context)` |1650 | Raw provider volume, context-dependent default. |
| `getInputVolume()` / `getOutputVolume()` |1656/1659 | Current device gain settings. |
| `getInputDeviceId()` / `getOutputDeviceId()` |1683/1686 | Device IDs can change/hotplug. |
| `getInputDevices()` / `getOutputDevices()` |1692/1695 | Current enumerated device inventory. |
| `getSettings(context)` |1774 | Raw saved mute/deaf and other settings; never serialize the whole object. |

Module827343 has `setSelfMute(context, desiredMute, playSoundEffect)` at86-93.
Use the explicit setter where present. Its deafen API is a toggle; implement a
serialized desired-state operation by comparing the raw context preference and
checking authoritative state after the synchronous dispatch. A stale or uncertain
toggle must not be replayed. Preserve enabled/permission semantics rather than
silently invoking an enable path that may request access.

Participant/stream controls use the same action family with different contexts:
`toggleLocalMute(userId, context)` and `setLocalVolume(userId, rawGain, context)`.
Use `'default'` for participant voice and `'stream'` for stream sound. Module731854
lines59-70 confirms context strings, raw default voice100, raw stream18 and
maximum voice200 constants in this bundle. Vencord's optional VolumeBooster plugin
also modifies these limits; detect/preserve existing settings rather than applying
the WebView widget's100%-endpoint clamp or guessing another mod's policy.

Device actions in827343: `setInputVolume`, `setOutputVolume`, `setInputDevice`,
`setOutputDevice`. Device changes dispatch state, not just native setters, which
keeps the desktop UI synchronized. Recheck the chosen device against the latest
inventory before mutation; do not route by a stale list index.

## 4. Events, identities and safe state publication

Use loaded Flux stores' add/remove change listeners and the existing FluxDispatcher
subscription API. Vencord's `findStore` implementation and existing plugin use of
`addChangeListener` are concrete precedent. Do not wrap provider dispatch globally
or replace native callbacks that Discord already owns.

Relevant state domains include UserStore, SelectedChannelStore, ChannelStore,
GuildStore/GuildChannelStore, PermissionStore, VoiceStateStore, MediaEngineStore,
ApplicationStreamingStore, ApplicationStreamingSettingsStore,
StreamRTCConnectionStore and relationship/presence/activity stores.

Events visible in the cached implementation include `SPEAKING`,
`RTC_CONNECTION_STATE`, `RTC_CONNECTION_VIDEO`, `MEDIA_ENGINE_CONNECTION_STATS`,
`MEDIA_ENGINE_SET_GO_LIVE_SOURCE`, `STREAM_START`, `STREAM_STOP`, `STREAM_CREATE`,
`STREAM_UPDATE`, `STREAM_DELETE`, `CONNECTION_OPEN`, `CONNECTION_CLOSED` and `LOGOUT`.
Subscribe only to needed stable behavior, re-read stores after dispatch, and
coalesce cosmetic changes. Account loss and source retirement should publish
immediately. Event names are versioned evidence, not an unconditional contract.

ApplicationStreamingStore's public stream records are replaced during normal
updates. `getStreamerActiveStreamMetadataForStream(key)` refers to separate source
metadata created by STREAM_START and cleared by STREAM_STOP. Use source metadata,
account/call epoch and native connection identity together; never rely on object
identity of `getCurrentUserActiveStream()` or the reused public key alone.
Native `prepicked:` sources can update metadata after a picker event, so the
WebView guard that rejects those IDs is not a general desktop identity algorithm.

StreamRTCConnectionStore module116956 lines760-786 provides
`getRTCConnection(key)` and `getVideoStats(key)`. Its objects contain connection,
transport and sensitive fields: do not serialize them. Diagnostics must construct
a whitelist of measurements and separate requested/advertised/captured/encoded
and receiver-observed data. Start confirmation is not delivery proof.

## 5. Native incoming video

Installed wrapper lines259-343 tracks direct/non-direct output sinks, provides
reference-related native calls and a one-shot `getNextVideoOutputFrame`. The
one-shot call performs CPU ImageData extraction; it is not the intended pin path.

Extracted module229209 creates a native-backed stream using
`window.createDiscordStream(streamId)`, reference counts consumers, calls
`addDirectVideoOutputSink` once per active native stream and removes it at zero
references. Its returned handle is `{stream, release}`.

Native media component in206959 lines1444-1511 acquires that handle, assigns
`stream` to a muted autoplay video element's `srcObject`, and releases it on
pause/stream change/unmount. `MediaEngineStore.getVideoComponent()` exposes the
existing engine's component. Reusing that component in the selected Discord-owned
surface is preferable to reimplementing its native sink bookkeeping.

Still unproven: resolving exact native stream ID from watched stream identity;
rendering that component in a dedicated popup; visibility/demand while overlay is
hidden; cleanup without affecting a watch already open in desktop Discord; and
foreground/input behavior. The element uses the main `document` to create video
but ownerDocument-aware ResizeObserver, so cross-document portability cannot be
assumed merely from its React component shape.

No claim is made that the native-backed stream is safely transferable to another
process, Electron renderer or WebView2. Present it within the owning desktop
runtime, then qualify the host surface integration separately.

## 6. Bounded foundational provider functions

Implement these in order; their names are proposals, not current exports:

1. `ReadCapabilitySummary`: booleans/method-shape evidence, supported engine
   features, module availability and build markers. No source/device/user names,
   IDs, raw stores, tokens, statistics objects or mutation calls.
2. `ReadAccountCallSnapshot`: validated minimal state and raw/effective mute/deaf
   distinctions, stable identity held internally. Add bounded store subscriptions
   and a single adapter-owned epoch/sequence mechanism.
3. `SetSelfMute` and `SetSelfDeaf`: explicit desired-state intents guarded against
   account/call changes, no uncertain retries, observed acknowledgments. Only live
   operation after approved installation and private-call authorization.
4. `ListShareSources` and `PrepareShare`: feature-probed provider enumeration,
   native identity correlation, eligible modes and explicit audio policy. No
   stream start during enumeration or preparation.
5. `StartPreparedShare` / `StopCurrentShare`: exact preparation/broadcast ownership;
   native provider flow, not direct VoiceConnection mutation or browser capture.
6. `AcquireIncomingPresentation` / `ReleaseIncomingPresentation`: exact watch and
   video-component identity; client-owned surface and bounded lease, no second
   encoder/audio playback. Treat surface feasibility as an independent gate.

Fixtures should model real edge cases discovered here: dummy/browser method
shape overlap, NO_STREAM native getter, raw versus effective mute, stale source
handle/PID reuse, soundshare PID remapping, source preparation retirement,
metadata replacement during native picker updates and another consumer retaining
a direct video sink. Source inspection alone does not establish runtime success.
