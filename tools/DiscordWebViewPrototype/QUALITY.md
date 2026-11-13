# Capture quality contract

The local native/C#/JavaScript transport supports twelve presets: 480p, 720p,
1080p and 1440p, each at 15, 30 or 60 FPS. `720p30` remains the default. These are
target rates and nominal vertical resolution ceilings, not promises about the
encoded rate Discord or the receiving network will sustain.

| Preset family | Adaptive output envelope | Target FPS |
| --- | --- | --- |
| 480p | 1706 × 480 | 15, 30 or 60 |
| 720p | 2560 × 720 | 15, 30 or 60 |
| 1080p | 3840 × 1080 | 15, 30 or 60 |
| 1440p | 5120 × 1440 | 15, 30 or 60 |

The chooser does not automatically expose all twelve. The original SDK preparation
overload retains exactly `480p15`, `480p30`, `720p15` and `720p30`. The constrained
overload accepts a copied, bounded provider-approved list; its preferred setting
must belong to that list. The host intersects it with local transport support,
and the SDK/broker reject a returned setting outside the admitted list. Discord's
widget supplies up to nine account-eligible modes through 1080p60 and fixes clarity. No 4K or source-resolution option is offered.

The envelope preserves source aspect ratio, including ultrawide windows. A smaller
source is not upscaled. A portrait source may be narrower; a very wide source may
reach the width limit before the nominal height. Resize remains adaptive. Idle
content keeps the existing approximately four-frame-per-second repeat policy;
active content uses the selected pacer interval.

The C# `LiveCaptureQuality` selection is passed as a bounded ID to
`WidgetRailNativeMedia.start(epoch, withAudio, presetId)` and as matching dimensions
and FPS in the capture-worker configuration. The in-process validation path uses
`WrailLiveCaptureRunV2`; its original native export remains available with the
previous 2560 × 720 / 30 FPS behavior. Frontend builds must be packaged with the
matching rebuilt native capture payload.

Generated track settings report the actual first-frame dimensions and selected
target FPS. Every frame is checked against the selected buffer envelope before
publication. Lower-resolution captures allocate a smaller managed/shared video
buffer. Confirmed stop releases it; unconfirmed worker retirement retains exact
ownership until cleanup succeeds. The largest video mapping is 5120 × 1440 BGRA,
or 29,491,200 bytes. This is a mapping bound, not a process or GPU memory ceiling.

## Discord policy and remembered preference

The provider receives optional
`captureQuality: { resolution: 480 | 720 | 1080 | 1440, frameRate: 15 | 30 | 60 }`.
Omitting it preserves the Vesktop comparison path's source request. Malformed
settings and choices outside the current provider policy are rejected before
source acquisition, and policy/account/call ownership are checked again after
asynchronous source selection and before `STREAM_START`.

The adapter asks Discord's loaded `canStreamWithSettings` helper about every pair
using `PRESET_CUSTOM`, the current user, channel and server tier. It does not infer
Nitro entitlement itself. With the required helpers available, it applies
`STREAM_UPDATE_SETTINGS`, verifies the observed resolution/FPS/preset, and only
then starts the stream. Increasing capture dimensions alone does not change
Discord's encoder policy.

Resolution and FPS use Discord's existing `ApplicationStreamingSettingsStore`
preference rather than a second WidgetRail settings file. The widget initializes
the chooser to that saved custom setting when it is currently allowed; otherwise
it uses 720p30, or the first allowed setting if 720p30 is unavailable. Opening or
cancelling the native chooser does not overwrite a saved preference. A disallowed
saved value is not erased merely because another account/call cannot use it.

Discovery reads loaded modules only; it never executes unknown module factories.
If the policy/settings helpers are unavailable, the snapshot reports quality
availability as `unavailable` and offers only the original four baseline choices.
Higher choices fail closed. Missing helper lookups are retried on later snapshots;
this fallback is not proof that helpers are discoverable on a cold start, or that
preference updates work while those helpers are unavailable.

Discord's [Go Live and Screen Share guidance](https://support.discord.com/hc/en-us/articles/360040816151-Go-Live-and-Screen-Share)
states that all users can stream up to 720p/30 FPS. Higher resolutions and 60 FPS
are exposed only when Discord's own current policy permits them. Local support
and provider permission do not establish sustained or remote delivered quality.

## Checks

`Build-Native.ps1 -Validation` builds `CaptureQualityProbe.exe`. It checks all twelve presets,
adaptive geometry, portrait/ultrawide sources, no upscaling, rejection of invalid
options, and the production pacing helper at 15, 30 and 60 FPS. The run recorded in
`artifacts/discord-webview/quality-native-policy.log` measured 14.930, 29.757 and
58.867 FPS respectively. This is a pacing test, not Discord or end-to-end capture
evidence. An earlier four-preset run measured 14.941/29.749 FPS; those historical
15/30-FPS results do not cover the expanded contract.

`CaptureWorkerProbe.exe --high-quality` passed generated mapped-picture, credit
and stop checks at 3840 × 1080/60 and 5120 × 1440/60. The recorded rates were
58.888 and 55.606 FPS, with private bytes of 18,145,280 and 31,068,160 and peak
working sets of 41,365,504 and 66,396,160 bytes. Evidence is
`artifacts/discord-webview/quality-native-high-probe.log`. These are synthetic
shared-mapping results, not real WGC→WebView→Discord delivery measurements.

The repository's native verification lane automatically builds and runs the
quality and audio-queue policy probes before frontend publication. Its
`web-capture-quality-policy` step uses `--policy-only`: deterministic preset,
geometry and no-upscaling checks without a load-sensitive pacing benchmark.
`web-capture-audio-policy` checks bounded queue/timestamp behavior without opening
an audio device. Selecting either step includes their shared policy-only build.
The no-argument quality probe retains the separate local pacing measurement.

The JavaScript adapter/transport and managed tests cover unchanged legacy defaults,
provider-constrained choices, preference projection, rejected or unconfirmed
provider updates, target metadata, bounded frames, and replacement-session
isolation. Passing these checks does not establish the current live Discord module
shape, account entitlement or receiving-side behavior.

For a focused check through the production worker, a validation-enabled frontend
and matching native payload can run generated BrowserSurface content:

```powershell
./tools/DiscordWebViewPrototype/Test-CaptureWorker.ps1 `
  -CandidateDirectory <candidate-directory> -NativeDirectory <native-directory> `
  -SourceKind Browser -Quality 1080p60 -SourceWidth 1920 -SourceHeight 1080 `
  -OutputDirectory <fresh-short-evidence-directory>
```

Use `1440p60` with a 2560 × 1440 generated source for the corresponding higher
quality check. The runner opens only its generated source, creates fresh local
WebRTC profiles, measures submitted/decoded FPS and dimensions, and requires
confirmed worker exit. It never opens Discord, accesses a signed-in profile or
shares user content. These higher-quality WebView worker runs have not yet been
recorded as passing evidence.

`Test-CaptureQuality.ps1` remains an in-process comparison runner. It accepts all
twelve preset IDs through `-Presets`, but defaults to the original four. Its
historical evidence must not be presented as a production-worker quality result.

### Historical four-preset in-process result

The four-preset native run in `artifacts/discord-webview/quality-native-pipeline-2/results.json`
decoded approximately 15.06, 29.90, 15.07 and 27.41 FPS respectively on the larger
generated source. There were no IPC drops during the measurement intervals.
The 720p/30 target was not locked at 30 FPS in that run; sustained throughput on
larger sources remains a performance gate. The 480p output was 738 × 478 because
the existing geometry helper rounds down to even dimensions; the test permits
that two-pixel rounding while enforcing the selected ceiling. Provider source
options are exercised by adapter tests, but live Discord encoder-policy changes
have not been verified by this test.

## Remaining acceptance gates

### Controlled H264 motion comparison (2026-10-27)

The local probe now accepts explicit `-Codec` and `-BitrateMbps` test options.
Defaults retain browser-selected codec and8Mbps. A requested codec must be
supported, is selected through transceiver codec preferences before negotiation,
and is checked against actual sender/receiver RTP statistics. Missing codecs
fail; they do not silently fall back. Staging writes a test-only configuration;
the installed widget's settings and browser profile are unaffected.12 focused
JavaScript policy/capture checks pass in `encoder-probe-policy-tests.log`.

Two runs used the unchanged production capture worker, generated2560x1440 source,
1440p60/Clarity, H264 and independently checked source renderer cadence:

| Measurement |9Mbps ceiling |18Mbps ceiling |
| --- | --- | --- |
| Source preflight |60.0350FPS |59.9916FPS |
| Source during measurement |59.9920FPS |59.9836FPS |
| Actual outbound bitrate |7.220Mbps |13.960Mbps |
| Submitted FPS |58.768 |58.741 |
| Encoded / decoded FPS |54.105 /55.004 |58.774 /58.774 |
| Encoded dimensions |2560x1440 |2560x1440 |
| Mean reported H264 quantizer |35 |21.092 |
| Mean encode time per frame |8.223ms |7.780ms |
| Reported limitation |none |none |
| Stop / confirmed worker exit |59ms / yes |66ms / yes |

Evidence: `artifacts/discord-webview/h264-1440p60-clarity-1/result.json` and
`h264-1440p60-clarity-18m-1/result.json`. The9Mbps run matches the recently observed
live codec family/ceiling, not every Discord profile/experiment/network policy.
Both pass the runner's existing85%-of-target tolerance with strict geometry;
neither is proof of a locked60FPS. Source qualification is renderer draw cadence,
not GPU-present ETW. Hardware encoder identity/power-efficient fields were
unavailable in these WebView2 stats. No hardware-encoder claim follows from H264.

The higher ceiling substantially lowered the reported quantizer on the same
generated-motion workload, supporting bitrate policy as a useful next lever.
It is not a VMAF/SSIM score or a remote Discord quality measurement. No production
bitrate was changed, no network adaptation was disabled and no further codec
sweep is justified yet. A provider-owned, explicit higher-detail option should
be evaluated before a GPU-transport rewrite; preserve automatic/default policy,
account quality eligibility and congestion adaptation.

The local fixture's8Mbps policy is a partial comparison, not a full reproduction
of Discord's codec negotiation or experiment-dependent degradation policy.
`encodingPolicy` records setup-time limits; `currentSenderParameters` reads the
current bounded values and track hint without changing them. Compare both with
RTP dimensions and codec before attributing resolution adaptation to capture.
The provider's motion experiment overwriting Clarity during attachment is
reproduced in the adapter fixture and corrected in adapter16. The hook restores
only the host-selected track's hint on its exact owned connection after the
current synchronous provider method returns. Unsupported attachment paths warn
without guessing, changing another track or ending a working stream. Live
verification remains open; this correction does not force encoder resolution.

The receiver's quality label is advertised metadata, not a decoded-frame
measurement. For the reported1440p60 selection /720p60 label mismatch, the
native track now exposes real resolution/FPS constraints after native format
readiness. `artifacts/track-constraints-3/result.json` verifies both720p30 and
1440p60 bounds in WebView2 without upscaling, and verifies later consumer
downscaling. This does not establish Discord's remote encoded dimensions or
correct its separately initialized advertised label. The widget's
on-demand **Stream details** reports capture, outbound RTP and advertised
dimensions separately; unavailable fields remain unavailable. Candidate8 /
widget0.0.14 / adapter20 is installed in the isolated test profile. Its
Vesktop-referenced metadata correction configures the exact source and connection
before the first video announcement. Automated source/minified-provider checks
pass; the live receiver label is not yet verified.
The0.0.10 live attempt was blank: the quality hook incorrectly consumed Discord's
destructive desktop-source pool getter during inspection. Adapter17 uses a
non-consuming data-slot read; the fixture now models the real transfer semantics
and reproduces the original failure. The user confirmed picture recovered on0.0.11.
Its quality hook still warned because it incorrectly expected numeric resolution
mode constants. Adapter18 uses the provider's actual "fixed"/"source" strings;
the0.0.12 live attempt still warned and advertised720p30. Adapter19 identified
attachment-timeout. The adapter had subscribed to "Connection", while Discord's
event constant is "connection". Adapter20 corrects registration/removal; the
real-emitter fixture reproduces the missed callback and verifies the fix. Live
0.0.14 verification now reports2680x1440/60 advertisement and the native receiving
Discord app shows1440p60FPS. Actual outbound2576x1384 matches the smaller source;
Clarity is effective and no quality warning is shown. Current4FPS is idle-state
evidence, not a qualified60FPS motion test. Received audio remains unverified.

- Discover the live provider policy/settings helpers after a cold start, before
  visiting Discord's own sharing UI, and verify the allowed list for that call.
- Full-resolution1440p60 remains unresolved. The latest event-wakeup worker test
  independently measured2560x1440 capturable bounds and a60.005FPS source preflight,
  with59.996FPS during capture. Submitted/encoded/decoded rates improved to
  58.909/58.905/58.815FPS, but the encoder adapted to1280x720 under bandwidth
  limitation. The strict test remains failed. Evidence:
  `artifacts/discord-webview/event-wakeup-1440p60-motion-1/result.json`.
  Source qualification measures renderer draws, not GPU-present ETW events.
  Further benchmarking requires a concrete new provider/encoder hypothesis.
- Verify actual remote Discord resolution, moving-content FPS and application
  audio on a second account. A local active-capture state is not remote receipt.
- Verify saved resolution/FPS across a real restart, including an unavailable
  preference, without resetting the signed-in profile.
