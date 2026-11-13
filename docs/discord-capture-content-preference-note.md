# Capture content preference and source-qualified FPS evidence

Historical implementation note from 2026-10-27. The current Discord widget fixes
its preference to clarity and offers up to 1080p60. Native interaction
and high-FPS delivery are not yet qualified by the changes in this note.

## Ownership and compatibility

The generic host capture contract now has
`WebCaptureStreamOptions.ContentPreference`, with
`WebCaptureContentPreference.Detail` and `Motion`. Omission retains Detail and
does not add a JSON field, preserving existing callers and wire shape. Discord
can opt into Motion as its game-sharing default; its remembered user preference
belongs to widget private state. The host displays **Prefer smoothness** and
**Prefer clarity** in the existing source chooser and returns the actual choice.

This is a confirmed host-contract gap: `native-media.js` previously forced every
generated video track to `contentHint='detail'`. Widget code cannot configure the
host-owned track without a supported contract. The host now carries the validated
preference into the production worker-backed track and the retained in-process
comparison path. This follows the browser `MediaStreamTrack.contentHint` contract
and Vesktop's smoothness/clarity picker precedent. It does not promise a measured
FPS, force a codec, upscale a window, or change the approximately4-FPS idle policy.

Provider `AllowedQualities` remain resolution/FPS restrictions. Their admission
compares these two fields using `IsSameQuality`; content preference cannot create
duplicate provider modes or bypass resolution/FPS restrictions. After preparation,
the broker retains exact record equality for the selected quality/preference, so
a later status cannot silently change either. Unknown enum values fail validation.

| Consumer | Disposition |
| --- | --- |
| Discord WebView widget | Root integrates Motion default and remembers returned selection in existing widget private state |
| Generic SDK/broker capture preparation | Resolution/FPS admission excludes content preference, actual returned choice preserved, later changes rejected |
| Production BrowserSurface/WebViewCaptureWorker | Validated selected preference becomes native browser track content hint |
| Standalone WebViewLiveCapture and worker probe | Same explicit content hint; default Detail remains for comparison |
| Existing SDK, broker and validation callers | Omitted preference retains Detail; original constructors/operations remain valid |
| Other shipping widgets, first-party widgets, templates | Search found no additional `Capture.WebStreams` or opted-in native capture consumers; unrelated recording/audio policies unchanged |

## FPS source qualification

`BrowserCaptureSourcePage` independently reads its own DWM extended frame bounds
and makes a bounded outer-window sizing correction so requested dimensions refer
to the capturable region. The runner records those measured bounds. It no longer
expects the receiver to equal a requested outer window size or derives expected
geometry from receiver output. DWM query failures, invalid bounds, or failure to
reach the requested dimensions fail the fixture rather than relax the assertion.

Before a60-FPS pipeline run, `Assert-SyntheticSource60Fps` observes at least five
seconds of source renderer canvas draw counters and monotonic `performance.now`
timestamps. It accepts58.5–61.5FPS, allowing59.94Hz displays and small scheduling
jitter. Failure leaves evidence and prevents pipeline startup. The source's exact
process incarnation, visible-document state and independently measured dimensions
are checked with each observation. The same timestamped counters are retained
during pipeline measurement and a cadence drop fails source qualification.

This measures requestAnimationFrame/canvas draw cadence, **not an independent
compositor presentation rate**. Do not present it as a GPU-present trace. The
pipeline records independent browser submitted-frame, RTP encoded and RTP decoded
measurements using each clock's own elapsed interval. Test-only sender
`media-source` counters help distinguish a pre-encoder drop from encoding.

The native worker test additionally accepts `-ContentPreference motion|detail`,
checks the observed preference, and reads explicit self-test startup failures
instead of waiting for a generic timeout. The existing throughput threshold was
not weakened. The runner-owned source/probe are always retired in finally.

## Verification so far

- 43 Node native-media tests pass, including legacy Detail default, explicit
  Motion and invalid preference rejection before replacing the current source.
- 3 test-only WebRTC instrumentation tests pass, including bounded observations
  and stale completion rejection.
- SDK177/177 and targeted broker4/4 pass; they include wire default/round-trip,
  mode-restriction independence and immutable admitted content preference.
- Validation frontend built at
  `artifacts/discord-webview/content-source-native-2`, using current production
  native files from `quality-native-production-1`. An initial compile failed on
  two WinRT collection expressions; explicit array types corrected them, and the
  fresh publication succeeded without warning suppression.
- PowerShell parser checks and deterministic monotonic cadence/repeated-timestamp
  checks pass.

Logs are `content-preference-node-tests-1.log`,
`capture-source-instrumentation-tests-2.log`,
`content-preference-sdk-tests-1.log`, `content-preference-broker-tests-1.log` and
`content-source-native-2-build.log` under `artifacts/discord-webview`.

## Coordinated native results

Root closed the authorized Test server/Lobby candidate and granted the foreground
slot. `content-choice-native-1/result.json` passes46 native consent/chooser checks,
including controller selection of content preference without confirming a source.

`motion-1440p60-qualified-1/result.json` **fails** the full-quality delivery gate:

- Source preflight:300 canvas draws /5.0008 seconds =59.9904FPS.
- Source throughout the measured pipeline:720 draws /11.9997 seconds =60.0015FPS.
- Independent DWM source bounds:2560x1440; native browser track:2560x1440.
- Submitted50.38FPS, encoded50.52FPS, decoded50.43FPS.
- The WebRTC encoder reduced output to640x360 and reported bandwidth limitation
  for12.098 seconds. This is not1440p60 success. Strict dimensions and FPS gates
  were not relaxed.
- Native worker exit was confirmed, with51ms stop. Generated fixtures exited.

Two separate source-grounded issues remain. First, the worker reported149 video
drops: `CaptureWorker.cpp` increments that counter only when its single video
credit is occupied. The browser keeps that credit through `VideoFrame` pixel
construction and awaited `writer.write`, then releases/acknowledges it. This
locates substantial lost delivery after native capture, but does not yet prove
which browser stage is slow. Bounded test-only construction/write/credit timing
is the proposed next discriminator before changing buffer policy.

Second, the local WebRTC fixture has no explicit sender bitrate configuration.
Vesktop's provider quality patch uses500kbps minimum,8Mbps maximum and600kbps
target, while the fixture delegates everything to Chromium defaults. Motion's
reported bandwidth adaptation makes these paths materially different. A controlled
bitrate-policy comparison must retain adaptation and strict output dimensions.
Do not call the previous low-FPS results a proven hardware/CPU limitation or fix
them by accepting the lower resolution.

The live candidate/profile/call was not touched by this lane; root owns that
handoff. No further capture run is authorized in this lane until coordinated.

## Timing discriminator and event-driven correction

The next root-coordinated diagnostic (`motion-1440p60-diagnostic-2`) kept its
source at59.999FPS before testing and60.007FPS during measurement. It still failed:
50.94 submitted,49.55 encoded and49.46 decoded FPS at640x360, with a reported
7.452Mbps target bitrate. Standard sender limits read back8Mbps/60FPS; video SDP
used Discord's `x-google-max-bitrate=8000`. Adaptation remained enabled. Vesktop's
500kbps minimum and600kbps target are recorded as provider-reference values, not
claimed to be applied through nonexistent sender properties.

Test-only timing disproved the proposed early-ACK/queued-write optimization:
`writer.write` took0.0235ms mean /0.2ms max. VideoFrame pixel construction took
3.37ms mean /5.9ms max; receipt to ACK send took3.60ms mean /6.4ms max. Do not
increase frame buffering or alter production ownership on this evidence.

Source inspection then found the native pipe reader polling `PeekNamedPipe` with
`Sleep(1)`, whose Windows scheduling granularity can delay delivery by roughly a
frame. The source fix replaces polling with one owned, explicitly inherited
auto-reset data-ready event. The worker signals only after a successful complete
packet write; the host peeks/drains before waiting on event/process exit, retaining
the existing bounded timeout. Coalesced signals cannot discard queued packets,
and a terminal packet is drained before reporting simultaneous process exit.
No global timer resolution, extra pixel buffer, relaxed credit rule, or source
selection change was introduced.

The private worker launch now requires its data-ready handle and protocol
version2. Configuration/packet sizes and exported host APIs remain unchanged.
Native payloads must be staged together; older mixed worker launches reject the
different private contract instead of running with mismatched assumptions.

Native validation `event-wakeup-native-validation-2` passes the dedicated wakeup
cases, ordinary15/30FPS mapped delivery, held audio credit and fresh recovery,
forced blocked teardown (157ms) and parent-death cleanup. An initial timing test
overlapped root's managed publication and failed its30FPS threshold; after adding
failure-only numeric diagnostics, one coordinated quiet rerun passed14.949/29.806
FPS. Logs preserve both runs. Production-native flag rejection and the subsequent
qualified-source WebRTC comparison are still pending at this checkpoint.

### Latest event-wakeup qualification

Production payload: `artifacts/discord-webview/event-wakeup-native-production-1`.
Its private validation-flag rejection probe passes in the separate
`event-wakeup-native-flags-1` staging directory; no test executable was added to
the production payload directory. Configuration/Packet/StopResult layouts remain
568/56/16 bytes. Stage **all three** new native files together with the frontend.

`event-wakeup-1440p60-motion-1/result.json` is the one subsequent coordinated,
CPU-quiet comparison. Renderer source preflight60.005FPS and measured-run59.996FPS
pass independently; source/DWM and browser track dimensions are2560x1440.
Submitted58.909FPS, encoded58.905FPS and decoded58.815FPS improve from the prior
50.942/49.546/49.455. Native credit drops total40 rather than144; no extra buffer
or changed idle cadence was introduced. Stop completed in57ms with confirmed
worker exit. The delivered video was still1280x720 with reported bandwidth
limitation and approximately7.10Mbps target bitrate. **Overall strict1440p60
test remains failed**. This evidence qualifies the capture throughput correction,
not full-resolution encoder delivery. Further capture runs are stopped pending a
new provider/encoder hypothesis.

Final ownership/wakeup review: the parent owns a non-inheritable event handle;
only its explicitly duplicated handle enters the worker's allowlisted inherited
handle set. The worker signals after a complete packet write. Host read always
checks/drains the pipe before waiting and rechecks after either data or process
exit signals, so signals may coalesce without packet loss. A stale signaled event
causes at most one empty recheck before the next wait. Timeouts remain bounded by
the caller's existing deadline; cancellation/stop, exact ACK sequence/epoch,
one-video/one-audio credits, kill-on-close ownership and target identity are
unchanged. Dedicated probe covers coalesced signals, withheld-credit timeout and
simultaneous terminal packet/process exit; regressions cover stale ACKs,
teardown containment and parent death.
