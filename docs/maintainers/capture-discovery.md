# Capture capability discovery

Recording admission must not depend on discovery of the entire Settings catalog.
The host still verifies support before confirmation and initializes the exact
configuration when recording starts. It never silently substitutes saved quality.

## Ownership and caching

- `capture.capabilities` returns the complete verified catalog for Settings.
- `capture.recording-capabilities` takes a `CaptureRecordingQuery`. An explicit
  recording asks for a supporting profile and only its requested audio mode. A
  null recording asks for the recommended silent profile, preferring 1080p/30
  hardware and falling back through other verified hardware profiles to software.
- `capture.validate` and `capture.start` use targeted discovery automatically.
  Capture Library requests a recommendation only when it has no saved profile;
  it preserves saved dimensions, frame rate, bitrate, encoder, duration and audio.
- `WindowCaptureRegistry` deduplicates discovery requests, limits pending queries
  to 16 and cached replies to 32, and rejects replies for retired request IDs.
  Cancelling one waiter does not cancel discovery needed by another waiter.
- `CaptureEncoderDiscovery` caches at most ten profile/audio combinations and
  one audio-device probe per frontend process. Concurrent requests share a lazy
  task. Native encoder probes are serialized to avoid transient GPU contention.
  Each successful envelope still qualifies minimum and maximum bitrate.
- The native capture-support check is cached too. A silent recording does not
  initialize application loopback or AAC. Opening Settings reuses prior probes.
- A full catalog's audio flag covers all profiles. A targeted profile may support
  audio even if another profile does not; a negative catalog-wide audio flag must
  not override a successful targeted check.
- The shell publishes discovery asynchronously without holding up the capture
  polling loop. This does not change confirmation, hide, countdown or foreground
  selection. Qualification does not record the screen or another application's audio.

These are initialization checks, not sustained encoding benchmarks. Windows
encoder metadata alone is not proof that a particular driver/configuration can
initialize; actual recording can still fail after a successful probe. Results
are process-local and not persisted to disk.

## Validation

`PlatformBroker.Tests --capture-only` covers scoped discovery, pipe deadlines,
deduplication, cancelled/stale replies, bounds, retirement, capture ownership and
output reservations. `CaptureLibrary.Tests` covers preserving defaults and saved
settings without requesting the full catalog. The WidgetBridge test prefix
`Capture capabilities` verifies independent replies across the frontend pipe.

The Debug frontend accepts `--validate-capture-discovery=<result.json>`, optionally
with `--full-discovery`, for real Windows encoder initialization without screen
recording. Run each mode in a separate process for cold comparison. Results
report cold/warm elapsed time, encoder/audio probe counts and supported profiles.

On the development PC on 2026-10-12, full discovery took 3866 ms (20 encoder probes
and one audio probe); targeted recommendation took 517 ms (two encoder probes,
no audio), with a cached repeat of 0.154 ms. These are local discovery timings,
not end-to-end button-to-confirmation latency or a cross-device guarantee.
Evidence is under `artifacts/capture-startup`. Slow individual native probes
(500 ms or more) also enter the bounded frontend diagnostic log with profile and
elapsed time, without window names, media content or credentials.
