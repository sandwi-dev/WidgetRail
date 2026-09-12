# WIDGE-242 Spatial sound selection

Built on audio-device selection 0a5e93b6 and feasibility probe e9dc2216. Those
changes remain pending acceptance/integration along with this candidate.

Audio Mixer places Spatial sound between Output device and Microphone. It shows
Off and supported known formats (Sonic, Dolby and DTS variants available to the
installed Windows SDK/OS). Unknown selected formats remain Other spatial format
and cannot be selected as an invented format. Windows-selected default and active
format are separate; an active override is displayed as secondary information.

The public contracts are additive. Spatial settings have separate optional read
and control capabilities. Existing device/output/session payload shapes do not
change, preserving installed application-worker compatibility. Settings exposes
Read spatial sound settings and Change spatial sound. No grants are auto-added.

WindowsAudioProvider now targets net8.0-windows10.0.19041.0 to consume documented
WinRT spatial APIs; its two consumers are the Windows bridge and provider tests.
The provider-owned WindowsSpatialAudioAdapter tracks only the current endpoint,
observes ConfigurationChanged, and releases the old subscription when the output
changes or the adapter is disposed. Callback work is coalesced onto the existing
Core Audio owner thread and rebinds audio sessions/endpoint controls as needed.

Set requests carry the captured opaque output ID and a bounded format token.
The provider refreshes and checks current output identity, native direction and
available format before dispatch. The mutation is bounded to three seconds and
never retried. Success requires both a successful Windows status and format
readback on the same endpoint. After a timeout an asynchronous Windows operation
might still finish; subsequent notifications reflect actual state without replay.

Failure isolation is deliberate at each layer:

- Native spatial getters/setters and notification teardown catch ordinary COM,
  WinRT, missing-API, timeout and unexpected managed failures locally.
- Provider spatial read errors publish unavailable only for the spatial section.
  Setter exceptions do not call the global audio-degraded transition.
- Broker validates DTO bounds and returns typed errors for malformed data.
- Widget optional subscription/read failures stay in an independently retryable
  section. Incoming data is validated before it can reach Select rendering.
- Widget control failures always clear busy state, retain/re-query confirmed
  selection once, and show sanitized license/access/device/error guidance.
  Optional failures do not enter the required-provider Error state.
- Widget/runtime lifetime generations reject completions after deactivation or
  replacement; starting a fresh audio run clears old spatial state.

A supported format is not necessarily licensed. Atmos licensing failure leaves
other controls available and explains the required audio app/license. Spatial
errors never expose exception text, native IDs or raw HRESULTs to users.

## Verification

Provider tests include real read-only spatial configuration through the Core Audio
endpoint ID and injected read/set failures, access/license/timeouts, stale device
and unsupported format rejection, recovery, and ordinary volume after failures.
Widget tests inject control/read exceptions and malformed format data and check
that rendering, volume control and retry remain usable. The broker checks separate
consent, lifecycle, bounded payloads and bad result data. Native scroll traversal
includes the additional spatial control without bypassing offscreen focus reveal.
Final verification: WindowsAudioProvider 19/19, AudioMixerWidget 47/47,
PlatformBroker 57/57, WidgetSdk 115/115, SDK compatibility 14/14,
SettingsWidget 67/67, WidgetBridge 129/129, and AudioMixerScrollHostTests passed.
The complete native/managed Release build passed. Published Audio Mixer and
WindowsAudioProvider payload hashes match the tested current build outputs.

Physical testing remains required before integration; no push. Enable Read
spatial sound settings and Change spatial sound in Audio Mixer permissions.
Test Off/Sonic selection, Atmos license failure, volume/mute after a failure,
and switching outputs while checking the spatial format refresh.
