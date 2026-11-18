# Replay capture qualification

These probes exercise the production replay recorder and its private worker boundary. They do not install WidgetRail, replace the running candidate, select arbitrary user windows, or send captures to a provider.

The production implementation is split by authority:

- `src/WinUiWindowPreviewNative/ReplayCaptureRecorder.h` feeds the shared hardware encoder and process-audio timeline. Legacy requests retain H.264; explicit codec/color/rate profiles are admitted only after real hardware verification. Encoded video/AAC packets enter `EncodedReplayBuffer.h`; `ReplaySampleOutput.h` muxes a frozen packet snapshot without re-encoding.
- `tools/DiscordWebViewPrototype/LiveCapture.cpp` supplies a GPU-texture capture path beside the existing raw WebRTC transport. Both use the existing window identity checks, WGC acquisition, pacing and process loopback implementation. They do not share a single live WGC session.
- `ReplayWorker.cpp`, `ReplayWorkerHost.cpp` and `ReplayWorkerProtocol.h` isolate capture and bounded save I/O in a kill-on-close process. `CaptureWorkerLifetime.h` is the same process/job/handle-inheritance helper used by the existing streaming worker.
- `src/OverlayFrontend.WinUI/Capture/NativeReplayCaptureBackend.cs` owns the safe native handle. The broker/coordinator owns approval, settings, source selection, application identity, authority renewal and library commit; none of that policy belongs in the codec.

## Explicit encoding and source geometry

`CaptureEncodingProfile.h` separates requested encoded color sampling/bit depth from private GPU surface layout. `CaptureVideoEncoder.h` verifies the actual hardware MFT, input/output subtype, encoded profile, frame size and rate-control readback. An enable-hardware flag or registered transform is insufficient; a fallback to software is rejected before recording frames. The unsuccessful direct-ARGB32 HEVC4:4:4 experiment was removed after hardware/profile verification rejected it on the tested system. No CPU color conversion or vendor-specific second encoder was added.

`CaptureEncodingEnvironment.h` provides a cheap bounded adapter/driver inventory and exact adapter selection. Its LUID remains host-private; public adapter IDs are opaque. Capture and encoding use the selected D3D device. This identifies the capture/encoding adapter, not an inferred GPU used internally by another application.

The additive native encoding probe uses four generated GPU frames, the production compressed replay sink, an in-memory MP4 and GPU decode. It checks the visible aperture as well as allocation bounds: a decoder can expose a larger backing surface than the visible image. This is an availability check, not a gaming-performance benchmark. Thumbnail, editor, player and sharing compatibility remain separate admission criteria. Local evidence currently keeps AV1 unavailable in usable settings because the standard library thumbnail path fails, despite successful hardware encode/GPU decode; HEVC has a deliberate trimming limitation. Do not infer4:4:4 or quality/VBR support from GPU specifications or MFT registration.

Replay requests the supported Windows borderless-capture property using the same native session setup as raw streaming. Windows permissions or another bordered capture can retain the indicator. Ordinary screenshot/manual-recording border policy is unchanged.

Replay's source-size choice means **source size at start**. Odd dimensions are padded to an even canvas when they fit; native-sized and subsequently smaller sources use an integer-position GPU copy without upscaling. Larger resized sources fit within the retained canvas. The encoder, audio timeline and replay buffer stay alive across resizing. A frame-pool/session rebind on the same validated target requests a fresh initial picture after a static resize; it does not switch target or reset the transport epoch. Repeat frames retain their last rendered source dimensions until fresh pixels arrive. Advanced admission rejects a changed first-frame source geometry rather than silently reducing a requested native source.

The replay private ABI is version2 (configuration624 bytes, state168 bytes), with versioned managed Start/Read entry points. Manual capture adds `WrailCaptureWindowConfiguredV4` while retaining the original configured export and legacy defaults. Build coherent native/managed inputs; older worker binaries must not be paired with the new ABI.

## Bounds and lifecycle

The production request permits 15–120 seconds and 32–256 MiB of compressed packet custody. The live ring has a separate 32,768-packet limit, plus at most one frozen snapshot of that bounded window; encoder surfaces, WGC frame-pool depth and PCM queues are also bounded. The compressed-byte setting is **not a promise that total process/GPU memory equals that number**. At the maximum setting, the ring reserves a conservative container allowance so saved MP4 files remain within the 256 MiB library envelope; output length is independently checked.

Both rolling eviction and Save use a keyframe at or after the requested cutoff. The result may therefore be shorter than the requested duration, which is reported explicitly. The recorder requests a one-second GOP and a force-next-keyframe where supported; it still rejects observed reordering or excessive keyframe age. Accepting a codec property does not prove the actual output complies. Ordinary recording does not use these replay-only controls.

Application audio and video use one active-time timeline. `CaptureFrameTiming.h` maps incoming timestamps to monotonic nominal video slots with at most one frame of phase error. It avoids duplicate/gap pairs caused by independently rounding regular half-slot jitter, while still correcting sustained input-rate differences and real stalls without cumulative clock drift. Minimized/unavailable targets pause replay and retain the last valid buffer. Paused PCM is discarded, and resume starts a fresh audio-alignment segment in the same encoders. The existing Discord transport's black keepalive behavior is unchanged. Complete AAC frames are kept inside the saved video interval, so edge gaps can be up to one AAC frame; audio cannot extend the hard duration cap.

Only one frozen save snapshot is admitted. Its compressed payload shares the same byte accounting as the running ring. The mux releases written snapshot references packet by packet. Before evicting live GOPs for byte pressure, the ring verifies that their payloads are actually reclaimable; references still held by a stalled save cannot justify wiping the live window. If no space can be reclaimed, incoming packets are dropped under the hard cap, the last valid history remains, and video resumes only at a fresh admitted keyframe. Out-of-window delayed audio cannot displace useful video. Save admission is synchronous at the managed/native boundary: calling `SaveAsync` and then `StopAsync` snapshots first, stops acquisition, and drains that admitted save. Revoke/dispose terminates the owned process instead. Authority expires after ten seconds without host renewal; a separate capture-thread progress watchdog also catches a blocked encoder. A responsive graceful-save drain continues publishing progress, with a 30-second write deadline and a 35-second worker drain ceiling.

The native output path is an exact broker-reserved staging path. It is never accepted from widget data. MP4 container selection is explicit, so a `.pending` suffix is supported. The broker independently inspects and atomically commits completed files. A successful native save alone is not evidence that authorization or library publication succeeded.

## Probes

| Probe | What it establishes |
| --- | --- |
| `CaptureFrameTimingTests` | Half-slot jitter, sustained faster/slower inputs, long stalls, pause/resume offset and bounded clock error at 15/30/60 FPS |
| `ReplayBufferTests` | Synthetic packet bounds, GOP eviction, one-snapshot custody, shared held-payload budget, AV watermark and explicit order/keyframe failures |
| `EncodedOutputProbe` | Actual hardware encoder outputs, optional generated AAC, concurrent snapshot mux and strict duration |
| `InspectReplay` | Decode, timestamps, allocation/aperture diagnostics and optional generated-tone/pixel checks; `--gpu` exercises the hardware source-reader path |
| `EncoderInventoryProbe` / `EncoderCapabilityProbe` | Per-adapter registered formats versus actual four-frame encode/remux/GPU-decode admission; exact adapter failures never fall back |
| `EncodingQualityProbe` | Matched native-size generated text/edges/gradients and optional translated texture; source/decoded PNGs and regional PSNR, not gameplay impact |
| `CapturePixelProbe` | Actual WGC odd-size padding, smaller-source exact interior copy, larger-source bounded fit, and a simultaneous bordered-session positive control |
| `WgcEncodingAdmissionProbe` | Stale first source rejected; exact advanced configuration and intentional later resize retained; nonstandard 3620×1018 replay/manual canvases from a 5120×1440 source are not aspect-fitted twice |
| `LibraryRecordingProbe` | Additive library-only2GiB budget admission using the ordinary file recorder; legacy128MiB caps, exact-source fence, actual small-budget overflow, Stop/cancel cleanup, and8MiB caller reuse. Declared large budget is not a large allocation or large-file stress claim. |
| `EncodingContentionProbe` | Owned GPU workload off/on/off with actual WGC/application-audio replay; GPU fences/timestamps, bounded save and memory/drop diagnostics; explicitly not real-game FPS |
| `WgcReplayProbe` | In-process production recorder with its own generated window/tone, pause/resume and resize |
| `ReplayWorkerProbe` | Private production worker ABI, static scene, paused save, actual partial-file cancellation, one-save admission, slow Save→Stop drain, authority expiry and emergency disposal |
| `WgcReplayWorkerProbe` | Actual WGC/process audio through the isolated worker; only its own generated window is minimized, resized and closed. The `--pulse` mode reuses the existing ordinary-capture pulse-window/tone fixture for decoded A/V edge alignment. Optional `--measure` records 76 seconds of worker CPU, private/ring memory and frame gaps, then saves the last bounded 60 seconds. |
| `tests/ReplayCaptureBackend.Tests` | Production managed ABI/layout, safe-handle adapter, cancellation, Save→Stop ordering, awaited disposal and production rejection of synthetic flags |

`tools/DiscordWebViewPrototype/Build-Native.ps1 -Validation` builds the worker probes with synthetic-source controls. The ordinary build compiles those controls out. `Build.ps1` here builds the buffer/codec/decoder probes; `-AcquisitionDirectory` also builds the in-process WGC probe against an existing native capture directory. All output paths should be fresh artifact directories.

The WGC worker probe also accepts `--coexist`: it starts the existing production raw streaming worker on the same owned source, drains its bounded video/audio credits, saves replay while streaming continues, and checks both independent stop orders. This is a local acquisition/worker integration check without a Discord connection, upload, or real-game performance claim. Decode its two saved `.pending` MP4 outputs with the existing `InspectReplay --quiet-audio` owned-fixture mode (including expected image pixels and generated440Hz tone).

Advanced recording resolves and probes an exact even canvas before admission. Replay texture V4 and manual configured V4 preserve those dimensions; legacy texture V1–V3 and ordinary legacy capture continue treating dimensions as bounds. Source pixels are copied or aspect-fitted inside the admitted canvas, which remains fixed after source resize. The source-size fence applies to admission, not later intentional resize.

Native desktop probes must run sequentially with other native UI qualification. Headless hardware encoding can still distort GPU/CPU timing measurements. The managed probe's optional `--measure` mode holds a 60-second static 1280×720/30 ring for 76 seconds and records memory/CPU observations; it deliberately excludes WGC, game rendering and application audio. Report that limited scope with any numbers. The WGC worker measurement includes its owned animated window and generated application tone, but still cannot establish real-game FPS impact. Warmup and steady-state observations must be distinguished, and competing builds/UI tests invalidate an uncontended timing claim.

## Shared consumer check

| Consumer | Applicability and preservation check |
| --- | --- |
| Capture Library / Game Help ordinary screenshots and clips | Existing `WindowContextCapture` still uses the ordinary MP4 encoder and one-argument audio flush. Encoding/decode profiles and audio tests remain the regression lane. Legacy requests need no migration. Advanced requests use the same optional profile and native V4 implementation; codec-specific editor admission stays explicit. |
| Discord streaming | Existing raw worker ABI, mapped credits, joined PCM transport, optional borderless request and black keepalive are preserved. Worker lifecycle/backpressure tests, real raw-WGC source loss, static shrink/grow and local coexistence qualify this branch. |
| Replay | Adopts GPU-texture acquisition, encoded sample sinks, bounded ring and independent save. Its broker approval/revocation/atomic-commit tests are separate gates. |
| Validation WebView capture | Keeps `WrailLiveCaptureRunV2`; the new private texture API does not change its callback layout. |
| Other shipping widgets, SDK examples and templates | Do not call these native codec/worker interfaces. They use broker capture/media APIs and require no native plumbing. |

The relevant codec contracts are Microsoft's [sample-grabber sink](https://learn.microsoft.com/en-us/windows/win32/medfound/using-the-sample-grabber-sink), [AAC encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/aac-encoder), and [force-next-keyframe property](https://learn.microsoft.com/en-us/windows/win32/medfound/codecapi-avencvideoforcekeyframe). Device behavior still needs measured qualification.

The library-only native entry point `WrailCaptureWindowLibraryV1` shares the
ordinary recording implementation and the existing V3 options/result layouts.
It accepts recording output budgets up to2GiB; legacy configured exports retain
128MiB, and existing context callers retain their smaller limits. A null encoding
selection uses legacy bounds and requires zero expected-source dimensions; an
explicit selection preserves the existing exact-source/exact-canvas admission.
The Windows one-off capture-border policy is unchanged.

Output remains streamed to the broker-reserved file. Runtime size reads fail
closed and finalization rejects oversized output; this is a final-file budget,
not a write-level quota (an in-progress file can temporarily overshoot). The
caller still owns failed-partial deletion and the broker owns inspection and
atomic library publication. The probe uses small owned files even when testing
the2GiB admission boundary.
