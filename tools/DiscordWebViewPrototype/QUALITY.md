# Capture quality contract

## Real adapter negotiation check (2026-10-27)

`Test-AdapterNegotiation.ps1` compiles a small non-activating WinForms/WebView2
runner with the already installed .NET Framework compiler and pinned WebView2
SDK. It copies the shipping adapter verbatim, records its build and SHA256, and
uses a fresh profile with default browser options. The only media is a generated
320 × 180 canvas; two real `RTCPeerConnection`s use no STUN/TURN servers. There
is no account, microphone, window capture, browser flag or global prototype patch.
Provider stores and source ownership are fixture data. Discord's Constrained
Baseline answer construction is modeled, while the actual shipping adapter must
perform any answer correction and the browser executes both description setters.

Run `tools/DiscordWebViewPrototype/Test-AdapterNegotiation.ps1` with an optional
fresh `-OutputDirectory`. The window uses both `ShowWithoutActivation` and
`WS_EX_NOACTIVATE`. The runner records only bounded checkpoints, codec/profile
observations, decoded-frame counts and cleanup; it never exports SDP, ICE
credentials or addresses. Browser permission requests are denied.

The first run passed on WebView2 `156.0.4314.50`, adapter **22**, SHA256
`C8B27BE9CC49F9BA4064966FCBDAC0CB5DB8E7400B729E57814C8815FBF1F18D`:

| Observation | Discord default | Prefer hardware |
| --- | --- | --- |
| Sender's actual local offer | `42001f`, mode 1 | `42001f`, mode 1 |
| Receiver's actual local answer / inbound codec | `42e01f` | `42e01f` |
| Sender's actual remote answer / outbound codec | `42e01f` | `42001f` |
| Shipping adapter's applied profile readback | none | `42001f` |
| Decoded generated frames | 3 at 320 × 180 | 3 at 320 × 180 |
| Native remote setter calls / source transfers | 1 / 1 | 1 / 1 |

Both cases restored the setter, closed both peers, ended their tracks and observed
browser-process exit. Evidence: `artifacts/discord-webview/adapter-negotiation-real-1/`
(`result.json`, `provenance.json`, exact staged source and compiler log).

The adapter23 rerun in `adapter-negotiation-real-23/` also passed after the final
post-getter ownership check was added. It asserts actual outbound/inbound codec
profiles explicitly; both cases decoded4 frames with one setter/source transfer
and confirmed cleanup. This is a bounded functional test, not a benchmark.

This closes the mocked-setter gap for this WebView2 runtime and verifies that its
receiver decodes generated frames while declaring Constrained Baseline. It does
**not** prove a Constrained-Baseline-only decoder, native Discord/DAVE/SFU
interoperability, sustained frame rate, hardware encoding, application audio or
background continuity. The tiny frame count is a functional assertion, not a
performance benchmark. The user's isolated Discord candidate remained closed.

## Supported capture modes

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
widget uses the constrained overload. No 4K or source-resolution option is offered.

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

### Exact H264 profile comparison (2026-10-27)

The validation-only codec policy now accepts `Auto`, baseline `42001f`, or
constrained baseline `42e01f`. Explicit profiles require H264 and packetization
mode 1 in the browser's actual capabilities; unsupported combinations fail
instead of testing a fallback codec. Codec preferences are applied before the
offer. Both SDP descriptions and actual sender/receiver codec statistics must
retain the exact requested profile and mode. Only normalized profile/mode values
are exported, never raw SDP, transport IDs or other format parameters. Existing
Auto codec/profile and 8 Mbps defaults are unchanged. Seventeen JavaScript
fixture tests and invalid cross-codec staging admission passed.

One back-to-back pair tested the exact profile hypothesis with the unchanged
production native worker, generated 2560x1440 source, 1440p60, H264, Clarity and
18 Mbps ceiling. The worker SHA256 matched in both staged candidates and the
production-native directory. Both actual sender and receiver confirmed their
requested profile with packetization mode 1 and full 2560x1440 encoded dimensions.

| Measurement | Constrained baseline `42e01f` | Baseline `42001f` |
| --- | --- | --- |
| Source FPS before / during | 59.998 / 59.997 | 59.853 / 60.078 |
| Delivered video FPS | 57.274 | 55.903 |
| Encoded / decoded FPS | 22.031 / 22.031 | 55.945 / 56.357 |
| Mean encode time | 43.734 ms | 14.492 ms |
| Actual bitrate | 4.128 Mbps | 14.401 Mbps |
| Mean reported H264 quantizer | 28.628 | 18.594 |
| Steady-state native credit drops | 1 | 11 |
| Receiver drops / freezes / lost packets / retransmissions | 0 / 0 / 0 / 0 | 0 / 0 / 0 / 0 |
| Stop / confirmed worker exit | 89 ms / yes | 79 ms / yes |

Constrained baseline failed the fixture's existing 85%-of-target throughput
gate; baseline passed. Both reported limitation `none`, demonstrating why
measured encode time and delivered/encoded/decoded rates are needed alongside
that categorical field. Encoder implementation and power-efficiency fields
remained unavailable. GPU sampling produced no attributable encoder-engine
observations, so it cannot establish zero GPU utilization or identify hardware
encoding. The result supports profile selection as a concrete next lever; it
does not establish a particular encoder implementation or prove remote Discord
will negotiate the same improved path.

Evidence: `artifacts/discord-webview/h264-profile-comparison-1/{42e01f,42001f}/result.json`.
The samples span 19:21:54–19:22:47 UTC. The real candidate/game was also active;
the user's requested candidate shutdown completed at 19:22:56 UTC and may have
started near the final second of the second sample. Competing load was therefore
not controlled exactly. No repeat pair was run. Generated sources were closed;
the comparison did not change the live candidate, installed widget, production
policy, native transport or user-selected share.

### Cadence experiment rejected for this iteration (2026-10-27)

The later matched comparison in `artifacts/discord-webview/cadence-matched-1/`
rebuilt both baseline and fixed-deadline native variants from the same current
source using identical validation flags/toolchain, then tested both with the
same current runner, generated 2560x1440 source, exact H264 `42001f`/mode 1,
18 Mbps and Clarity. The real candidate was closed and no external capture
workers were present. Source qualification passed before and during both runs.
Baseline and candidate deterministic policy checks passed; the candidate's
jitter, stall, overrun and idle-resume tests did not require timing benchmarks.

| Matched measurement | Existing pacing | Fixed deadlines |
| --- | --- | --- |
| Source FPS before / during | 60.029 / 59.991 | 60.016 / 60.006 |
| Delivered / encoded / decoded FPS | 56.467 / 56.437 / 56.361 | 57.129 / 56.752 / 56.663 |
| Mean native timestamp interval | 17.697 ms | 17.499 ms |
| Gaps above 1.5 frame periods | 31 / 741 intervals | 43 / 638 intervals |
| Steady-state native credit drops | 0 | 0 |
| Mean encode time / quantizer | 8.030 ms / 19.924 | 7.872 ms / 21.103 |
| Actual bitrate | 13.909 Mbps | 13.257 Mbps |
| Worker mean CPU, percent of one core | 20.47% | 20.09% |
| Worker peak private memory | 102.18 MiB | 102.85 MiB |
| Stop / confirmed worker exit | 319 ms / yes | 67 ms / yes |

Both receivers reported zero drops, freezes, packet loss and retransmissions.
The first and last sender/receiver samples retained 2560x1440 and the exact
requested codec/profile/mode. Intermediate encoded dimensions were not retained,
so this pair does not prove uninterrupted full-resolution encoding. Resource
samples cover each process's observed lifetime, including startup, and are
supporting observations rather than isolated steady-state CPU benchmarks.

After this comparison, the runner gained bounded `observedFormat` measurements
across periodic ready samples. Missing or changed worker identity, display/RTP
dimensions, codec or H264 profile/mode now prevents a stable-format pass; repeated
identical RTP timestamp pairs do not count as additional observations. Final
sender and receiver dimensions must also match rendered dimensions. Reducer
fixtures cover transient changes that recover before the final sample and
missing values. This is periodic observation, not frame-by-frame proof; it does
not retroactively strengthen the recorded comparisons. No new capture benchmark
was run solely for this test-tool change.

The approximately 0.3 decoded-FPS difference does not establish a material
benefit, while long-gap incidence increased. The fixed-deadline change was
therefore rejected for this iteration and its exact four native files restored
to HEAD; baseline bytes, hashes, source patch, resource samples and both results
remain in the evidence directory. No repeat pair, sweep or real Discord test
was run. The accepted 250 ms idle-repeat policy and transport ownership remain
unchanged; actual idle/audio/source-loss testing of this rejected variant is
unnecessary and no claim of that acceptance is made.

Source inspection found that the native pacer schedules the next frame from
the actual wake time, allowing timer lateness to accumulate. A bounded experiment
preserved scheduled deadlines for ordinary jitter and reset after stalls without
catch-up bursts. Deterministic 15/30/60 FPS jitter, stall, overrun and idle-resume
policy checks passed, but the single generated 1440p60/H264/18Mbps/Clarity run
did not demonstrate a performance improvement. The runtime experiment and its
probe changes were reverted; the accepted native transport remains unchanged.

The source measured 59.995 FPS before capture and 59.999 FPS during it. Submission
was 55.07 FPS, encoding 56.05 FPS and local decoding 56.66 FPS, compared with the
historical 58.74/58.77/58.77 FPS run below. This run overlapped the real stream/game
load: JavaScript frame construction rose from 3.46 to 5.29 ms and mean encoding
from 7.78 to 10.69 ms. It is not a controlled causal A/B comparison and does not
prove the proposed deadline policy caused the difference. No further benchmark
was run and no cadence improvement is claimed.

The retained diagnostic improvements now separate startup from steady-state
loss. In the measured window there were three native credit drops (59 before,
62 after), zero receiver frame drops/freezes/lost packets/retransmissions, and
50 native-timestamp gaps versus 48 browser-delivery gaps above 1.5 frame periods.
Mean delivered native cadence was 18.16 ms. Independent asynchronous stage
counters cannot be subtracted to infer exact frame loss. The worker stopped in
83 ms and its exit was confirmed. Evidence and the retired experiment patch are
in `artifacts/discord-webview/cadence-1440p60-clarity-18m-1/`.

`CaptureMeasurements.ps1` retains unknown/reset counters as unknown, distinguishes
native packet counts (including audio) from video frames, and rejects comparisons
across worker replacement. The fixture now records receiver freeze, jitter-buffer,
packet-loss and retransmission evidence plus native-timestamp/browser-delivery
cadence. These diagnose the local test peer; they do not measure remote Discord.
Eight focused JavaScript checks and the PowerShell measurement checks passed.

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
