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

## SAMSUNG device identity correction

The first candidate passed the MMDevice endpoint ID directly to the WinRT
GetForDeviceId API. On SAMSUNG (NVIDIA High Definition Audio), that call returned
a valid object reporting IsSpatialAudioSupported=false and only Off. The exact
same output queried through its Windows device-interface ID reported Sonic,
Dolby and DTS support. This was a host identity-mapping bug, not a widget or
license failure. The original read-only smoke gate checked only for a returned
object and Off, so it did not catch the silent unsupported result.

The adapter now enumerates Windows audio-render interfaces and matches the
OS-provided System.Devices.DeviceInstanceId to the exact SWD\MMDEVAPI devnode
for the admitted MMDevice endpoint. It passes the matched interface ID to WinRT
while retaining the original native endpoint ID for broker authority. Friendly
names and Windows' potentially different default-device roles are never used to
select a match. Missing, disabled or ambiguous matches return spatial unavailable.

Supported formats are refreshed on authoritative reads and before setting so
capabilities that settle after connection cannot remain cached as Off-only.
Failure isolation, readback checks and existing permission contracts are unchanged.

Validation: 21 provider tests passed, including exact/disabled/ambiguous identity
matching and live equality of the provider's spatial support and Sonic/Atmos
choices with the interface-based Windows query. The corrected read-only probe
on SAMSUNG reports Off, Windows Sonic, Dolby Atmos variants and supported DTS
variants. Selected and active formats remained Off throughout this correction.
Native production code is unchanged; its candidate binaries were copied with
SHA-256 verification, followed by complete managed-runtime republication.
The corrected candidate also passes WidgetBridge.Tests 129/129 after coherent
managed publication. Its packaged WindowsAudioProvider hash matches the tested
provider. No widget, protocol, permission or native-renderer changes were needed.

## DTS:X metadata and license distinction

DTS:X for home theater was missing because its SpatialAudioFormatSubtype getter
was introduced in Windows contract v12 / build 20348. The production 19041
managed projection has no such property, so reflection returned null even when
the installed Windows runtime supports it. A separate read-only 26100 SDK probe
on SAMSUNG confirmed the getter returns {10201B4A-3322-4967-BF40-2CAA9BAFCA44}
and IsSpatialAudioFormatSupported is true. Production now resolves that stable
subtype when Windows ApiInformation advertises the newer getter, without raising
the application's minimum Windows version or changing other project SDK targets.
The normal endpoint support check still governs whether it is listed.

IsSpatialAudioFormatSupported reports device-format support, not licensing.
The documented SpatialAudioDeviceConfiguration surface has no read-only license
validation method; SetDefaultSpatialAudioFormatAsync returns license outcomes.
SpatialAudioFormatConfiguration provides companion-app license-change reporting,
not a license query. Listing therefore cannot promise a supported format is
licensed. Licensing failures are reported when a selection is attempted.
There is no automatic switch-through-format preflight and no license inference
from an installed provider app. Failed selections keep the existing safe behavior.

Verification: provider 22/22 and AudioMixerWidget 47/47 passed, including the new
runtime-getter resolution and live DTS:X support equality check. The live read-only
SDK probe observed the user's existing Atmos for home theater selection; no
selection was changed by this correction.

Reference: https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.spatialaudioformatsubtype.dtsxforhometheater
License API reference: https://learn.microsoft.com/en-us/uwp/api/windows.media.audio.spatialaudioformatconfiguration
Full WidgetBridge.Tests also passed 129/129 after coherent runtime publication.
Packaged provider/widget hashes match tested outputs; native production payloads
were reused with hash verification. The corrected provider read confirms DTS:X
for home theater is listed on SAMSUNG, with Atmos for home theater still selected.

## Audio Mixer feedback refinement

The static licensing and application restart notes have been removed. Failed
spatial selections use the shared danger toast with a theme-owned warning icon,
surface and text. The notification sits outside the audio scroll container,
does not accept focus, and expires five seconds after terminal feedback is
published. New errors replace the deadline; provider snapshots do not renew it.
Successful selection, retry and widget deactivation clear old feedback.
The preferred widget height is 580 (previously 520), with the existing width and
minimum dimensions preserved.

AudioMixerWidget.Tests: 48/48 passed, including timed expiry, replacement,
provider-update independence, successful recovery, focus and lifecycle cleanup.
Native AudioMixerScrollHostTests also passed after coherent runtime publication.
