# Isolated capture worker

The production transport lives in `Browser/Capture`. `--capture-probe-worker`
selects the same transport in the local capture probe; the original in-process
prototype remains a validation-only comparison. The transport itself grants no
permission: host admission owns document, source and audio authorization.

## Boundary and ownership

- `WebRtcCaptureWorker.exe` runs the existing WGC and selected-process-loopback
  implementation. The source HWND, PID, creation time and class remain private to
  the host/helper. The existing exact-source validation still runs during capture.
- `CaptureWorkerHost.dll` creates the helper with an atomic `JOB_LIST` process
  attribute, a non-inherited kill-on-close job, and an explicit inherited-handle
  list. The job permits one process. A child cannot survive loss of its parent/job
  owner or escape into an unowned startup interval.
- The job has an active-process limit, not a process/job memory cap. Video memory
  is bounded by the selected preset: at most 5120×1440 BGRA (29,491,200 bytes),
  with one shared mapping and one outstanding video credit. Baseline 720p still
  allocates only its 2560×720 envelope. Native output/staging textures and readback
  pixels scale to the actual aspect-preserving output, not always the maximum.
  Windows' two-frame capture pool additionally scales to source size, whose
  existing 32-million-pixel limit remains unchanged. GPU allocations and browser
  encoding are not a promise of a whole-process memory ceiling.
- Local transport presets cover 480/720/1080/1440p at 15/30/60 FPS, without upscaling.
  Legacy capture requests still offer only 480/720p at 15/30 FPS. Provider-restricted
  requests intersect their approved choices with these transport presets; the
  host does not infer service subscriptions. 720p30 remains the default.
  Discord's widget obtains the allowed list from its provider adapter and passes
  the saved custom resolution/FPS from Discord's own settings store when allowed.
  The native chooser keeps no second quality-preference store. Missing loaded
  provider helpers retain baseline choices and reject higher settings; see
  [the quality contract](QUALITY.md) for discovery and preference limits.
- Two native shared mappings carry video and optional PCM. An anonymous pipe
  carries fixed 56-byte metadata records only. Each media kind has one outstanding
  credit; exact epoch and sequence acknowledgement is required before that mapping
  may change. Live queued PCM remains hard-bounded at 192,000 bytes, with an
  explicit 200 ms latency budget. Old queued packets are evicted whole at enqueue
  and before publication; retained packets keep their original QPC and FIFO order.
  The already-published mapping is outside that freshness guarantee and cannot be
  overwritten until its exact acknowledgement. Invalid PCM packets still fail
  closed. The helper never sends pixels as JSON or base64.
- The frontend's native helper uses the documented WinRT
  `ICoreWebView2Interop2::GetComICoreWebView2` bridge, native environment's
  `CreateSharedBuffer`, `FileMappingHandle`, and native `PostSharedBufferToScript`.
  The inspected WinRT projection exposes `Buffer` and `Size`, not the .NET Core
  wrapper's `FileMappingHandle`. Builds resolve the pinned package from
  `src/OverlayPlatformInterop/NativeDependencies.csproj`; native interop also
  compiled successfully with its current 1.0.4078.44 headers.
- Buffer COM operations stay on the frontend's UI thread. Mapping duplicates are
  inherited only by the capture child. The frontend performs no `Marshal.Copy`
  or managed byte-array/stream frame copy. The existing native readback buffer is
  still copied into the shared mapping; this is not GPU zero-copy capture.
- `native-media.js` is provider-neutral. Its initializer accepts the host's exact
  origin allowlist; production injection must wrap it in a document-created
  closure. Discord sender telemetry lives separately in `discord-sender-stats.js`.

## Stop semantics

The frontend cancels queued frame presentation and asks the child to stop. Native
stop waits at most the requested cooperative interval (600 ms in the production
transport), terminates the owning job if necessary, and checks exit for at most
250 ms more. The result distinguishes cooperative/forced cleanup and confirmed
exit. It does not claim that a Windows driver has recovered merely because a
termination request succeeded.

The managed transport also bounds its parent-side retirement wait.
A retiring pump retains the mappings, so late startup cannot use recycled handles;
another session cannot start through that transport until retirement completes.
An unconfirmed exit retains its exact process/job handle, fails StopAsync, and
blocks replacement. A subsequent StopAsync retries native exit verification and
releases ownership only once exit is confirmed. JS start/keep/stop use global
host session epochs; old callbacks cannot retire or renew a replacement. A
read-only status query does not renew capture authority.

Buffer finalizer fallback schedules COM release on the creating dispatcher; if
that dispatcher is already shut down, process teardown remains the final owner.

## Historical baseline native evidence

`CaptureWorkerProbe.exe` uses private mappings and generated pixels/PCM. The run
under `artifacts/discord-webview/worker-native-6/` passed:

- Complete mapped pictures, exact credit/epoch rejection and bounded outstanding
  video at measured 15.000 / 29.777 FPS for the 15 / 30 FPS targets.
- PCM mapping integrity and the former audio-overflow termination policy. This
  historical policy was later replaced by the live queue behavior below.
- A deterministic simulated blocked cleanup terminated in **156 ms** with the
  150 ms cooperative budget. This simulates an unresponsive child; it does not
  reproduce a specific Windows driver failure.
- Parent death killed the exact worker incarnation without a cooperative stop.

`CaptureWorkerProbe.exe --window` created only its generated red/green moving
window, verified those pixels after real WGC in the child and direct shared-map
delivery, measured **29.708 FPS**, then closed that exact source. The worker ended
with a source-loss failure and exited without forced termination. No other
application or user data was captured.

The actual WinUI/WebView/local-WebRTC mapping integration also passed against
the immutable `consent-picker-candidate` with validation native6:

| Evidence directory | Decoded FPS | Stop | Confirmed worker exit |
| --- | ---: | ---: | --- |
| `worker-webrtc-3` | 27.75 | 80 ms cooperative | Yes |
| `worker-webrtc-block-1` | 27.99 | 677 ms forced | Yes |

Both live runs used generated moving content at 1106 x 720 and observed bounded
frame drops (18 / 17). They establish that the documented COM interop, direct
shared-buffer delivery, local WebRTC and teardown work together. They do not
establish locked 30 FPS, remote Discord reception, audio quality, prolonged
resource stability, or recovery from an actual Windows driver hang.

The runner now tolerates an empty in-progress legacy stats file, and the
validation page publishes those snapshots atomically. The original
`worker-webrtc-2` failure was this evidence-file race, not an in-process transport
selection. All launched test processes were cleaned up.

## Expanded-quality native evidence

The current twelve-preset geometry/pacing probe passed in
`artifacts/discord-webview/quality-native-policy.log`. Targets of 15, 30 and 60 FPS
measured 14.930, 29.757 and 58.867 FPS through the production pacer. The
`CaptureWorkerProbe.exe --high-quality` generated-mapping run in
`artifacts/discord-webview/quality-native-high-probe.log` passed complete frame,
credit and stop checks:

| Mapping / target | Measured FPS | Private bytes | Peak working-set bytes |
| --- | ---: | ---: | ---: |
| 3840 × 1080 / 60 FPS | 58.888 | 18,145,280 | 41,365,504 |
| 5120 × 1440 / 60 FPS | 55.606 | 31,068,160 | 66,396,160 |

These are synthetic mapped-transport measurements. They do not include a real
high-resolution WGC source, WebView encoding/decoding, or a remote Discord peer.
No passing 1080p60/1440p60 worker→WebView→local-WebRTC result is recorded yet.
The command and remaining live acceptance gates are listed in [QUALITY.md](QUALITY.md).

## Sustained measurements

For longer, repeatable resource measurements, use `Measure-WebCapture.ps1` with a
qualified `-CandidateDirectory`, `-NativeDirectory`, and `-Seconds` (20–600).
`-ApplicationAudio` adds generated process audio; `-Static` measures an unchanged
source; `-IncludeGpu` records raw Windows GPU-engine busy-time deltas for the probe
process tree. The runner copies the candidate's actual production worker binaries,
uses a fresh profile and never selects an existing user window or opens Discord.
`-SourceKind Browser` replaces the default native GDI source with a separate
isolated WebView2 animated canvas and optional generated tone. Capture selection
requires the exact runner-owned PID and creation time for either source kind.
Browser-source rendering/visibility/audio-clock samples are retained separately;
they do not claim to reproduce Chrome-specific extensions or page behavior.

`samples.json` retains per-process CPU/private/working-set and media samples;
`result.json` summarizes the post-warmup interval. Local WebRTC encoding and decoding
are included, while the generated source and DWM/system-attributed GPU work are
excluded. CPU totals compare matching process incarnations and report exclusions.
These samples are evidence for the measured interval, not a memory-leak verdict or
proof of remote Discord quality. Confirmed worker exit is required at the end.

## Private ABI

`CaptureWorkerProtocol.h` defines version 1 configuration (568 bytes), packet
(56 bytes) and stop result (16 bytes). The parent-facing start takes a sealed
helper path, validated source/quality configuration, and borrowed mapping handles.
The factory duplicates those handles before the child runs. The session owner
provides `Read`, `Acknowledge`, `Heartbeat`, bounded `Stop`, and `Release`.
`CaptureWorkerNative.cs` preserves these sizes with explicit interop declarations.

The two test flags (generated pattern and simulated blocked exit) belong only to
this developer helper/probe. Public SDK/broker admission must never accept them.
Default `Build-Native.ps1` builds only the production payload, rejects every
nonzero flag in host and worker admission, and compiles the generated/blocked
branches out. `-Validation` explicitly builds those branches and developer probes.
The `--production-flags` probe passed against the production DLLs in
`artifacts/discord-webview/production-flags-1/`, rejecting all three nonzero flag
combinations before process creation. The twelve local presets are implemented;
high-quality end-to-end delivery, prolonged GPU/load testing, real remote audio
acceptance and capture-driver failure recovery remain distinct gates.

## Live audio backpressure correction

The targeted BrowserSurface experiment in
`artifacts/discord-webview/stall-fault-evidence-1/result.json` reproduced a precise
failure: a 1.5-second renderer stall survived with video only but ended audio/video
capture with `0x8007006F` (buffer overflow) when audio was enabled. Host dispatcher
ticks remained responsive. This does not identify the user's earlier live failure.

`CaptureAudioQueue.h` now owns only the worker's downstream live-audio queue. Its
200 ms maximum queued age is an explicit latency budget; the independent 192,000
byte payload cap remains. Expiry uses the same QPC timebase as WASAPI's original
packet timestamps, with overflow-resistant integer conversion. Whole-packet drops
create a real audio gap rather than replaying stale audio or inventing timestamps.
No shared WASAPI/recording policy, watchdog, ABI size or packet flags changed.
Dropped PCM-frame counters remain internal to queue tests; `Packet.flags` continues
to count video drops only. Native delivery exceptions now retain a bounded memory
or unhandled-exception error code rather than looking like successful source end.

Focused native evidence at `artifacts/discord-webview/worker-audio-1/`:

- 500 normal packets delivered losslessly with exact bytes/QPC.
- 10,000 enqueues retained only the newest bounded FIFO suffix without growth.
- Age eviction, expiry without new input, malformed-packet rejection and recovery.
- QPC conversion boundaries, including intermediate-overflow cases.
- A real worker held one audio mapping/credit for 1.7 seconds while video continued;
  mapping bytes stayed unchanged, stale/wrong/duplicate acknowledgements were
  rejected, fresh ordered audio resumed, and the worker exited cooperatively.

The opt-in BrowserSurface stall fixture now requires actual post-stall video and
optional audio progress before reporting a surviving capture. It passed against
`artifacts/discord-webview/audio-recovery-validation-1/candidate`, using production
native files from `worker-audio-production-1`. Evidence is
`artifacts/discord-webview/stall-recovery-evidence-1/result.json`:

- 1.5-second stall, video only: survived and resumed 30 frames at 29.69 FPS.
- 1.5-second stall, application audio: survived and resumed 30 frames at 29.60 FPS
  plus 48,480 audio frames during the approximately one-second recovery window.
- Both 4-second cases still stopped through `browser_heartbeat_failed`, with no
  subsequent audio/video progress and confirmed cooperative worker exit.
- Maximum host dispatcher gaps remained below 67 ms. Every case confirmed exact
  child cleanup; the generated source and validation host were removed afterward.

No production watchdog was relaxed, no long baseline was repeated, and the user's
running candidate remained untouched. Remote Discord audio acceptance and the
cause of the original live exit are still separate questions.
