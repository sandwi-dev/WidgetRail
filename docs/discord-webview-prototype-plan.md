# Discord WebView2 prototype plan and evidence

Status: WebView streaming experiments stopped at the user's request; retained
implementation and evidence, not accepted as a completed quality improvement.
Updated: 2026-10-27.

The proposed full-feature migration is now documented in
[Discord Desktop integration plan](discord-desktop-integration-plan.md).
That plan uses the existing official desktop client and explicitly includes all
native controls, setup, audio, sharing, pins and the full Discord page. It does
not authorize modifying the installed Discord client. Historical candidate
handoffs below are evidence records, not instructions to resume WebView tuning.

Working-baseline acceptance: the user authorized merging the current integration
to main before beginning the next motion-quality improvement phase. This accepts
the working0.0.14 behavior and its supporting contracts/tools; it does not claim
the remaining audio, sustained60FPS or higher-bitrate acceptance gates are complete.
No release was requested. At that baseline merge, the18Mbps comparison was
test-only; later candidate changes are recorded below.

Merge verification (2026-10-27): the Discord shard, SDK compatibility, protocol
parity, runtime, presentation session, broker and gallery checks passed. The
documentation check caught protocol-summary drift and passed after correcting
104 to 105. Motion tests passed (25); shell tests passed (355) on one bounded
rerun after an initial suggestion-pacing assertion failure. That timing test
and its implementation are unchanged from main.

Native capture quality/audio policy probes passed. The fresh-installation bridge
suite passed 234 of 235 tests. Its Playnite startup assertion rejected the valid
connection-setup dialog (`playnite-start-dialog.layer`, initial focus
`playnite-library.setup.stopped.settings`) while Playnite was not running. Both
the Playnite implementation and this test are unchanged from main; the test's
accepted states omit that dialog. This is a recorded non-green check, not a
claim of a fully passing bridge suite or a separately executed main baseline.
Evidence is in `artifacts/discord-webview/merge-main-verification`,
`merge-main-final-managed`, `merge-main-shell-recheck-final` and
`merge-main-native-verification` under the same artifacts directory.

Current division of work, explicitly set by the user: agent owns the
Vesktop-referenced quality configuration/advertisement correction, reliable
high-quality FPS delivery investigation/correction (item1), and quality option
discovery/persistence (item2). The user owns the remaining live acceptance,
background/recovery, hardware/performance/usability and final readiness work
listed in the preceding discussion. Do not repeatedly ask the user to perform
those checks as prerequisites to independent item1/2 work. Retain evidence and
limitations without claiming the user-owned checks passed. The user subsequently
approved the five proposed widget enhancements: Smoothness/Clarity, participant
voice controls, compact voice-device settings, favorites/recent channels and
accurate connection status. The user specifically requires verifying the shared
source runs at60FPS before testing a60FPS capture path; continue observing that
source during the measurement too.

## Current handoff and remaining gates

### User live feedback and change of direction (2026-10-27)

After launching isolated0.0.18 for the requested live check, the user reported
no meaningful visible improvement and explicitly ended these streaming
experiments. Sender statistics for that attempt were not collected before the
change of direction; do not infer which profile or bitrate actually applied.
The synthetic improvements are not accepted remote-quality evidence. Preserve
the current work without further WebView streaming experiments. The user now
wants to discuss using the official Electron-based Discord desktop client as
the media backend. This is architectural investigation, not authorization to
modify the installed Discord client or install a client adapter.

### Measurement review and cadence decision (2026-10-27)

The matched comparison in `artifacts/discord-webview/cadence-matched-1/`
qualified both generated sources near60FPS before and during capture. Fixed
deadlines changed decoded throughput from56.361 to56.663FPS, while long native
timestamp gaps increased from31/741 to43/638 intervals. This does not establish
a material benefit. The four native experiment files were restored to HEAD;
the existing capture implementation and idle policy remain unchanged. Full
measurements and limitations are recorded in `tools/DiscordWebViewPrototype/QUALITY.md`.

Review found an evidence gap in the capture runner: first/last format checks
could miss a temporary resolution or codec adaptation. The runner now retains
bounded periodic format observations and rejects missing or changed worker,
display/RTP dimensions, codec and H264 profile/mode data. Pure reducer fixtures
cover stale samples, transient changes and missing fields. These are test-tool
changes, not a new host/widget contract or runtime polling mechanism. Earlier
comparisons remain endpoint-only evidence; no additional benchmark was run merely
to exercise this future safeguard. Candidate0.0.18 remains selected and closed.

### Continuation verification / candidate 0.0.18, adapter 23 (2026-10-27)

The preceding goal turn made implementation and verification progress. This
continuation closed an additional gap without reopening the user's Discord
session: a new fresh-profile WebView2 test runs the exact adapter with real peer
connections and generated320x180 video. Default negotiation retains42e01f. The
opt-in adapter's actual instance wrapper changes the sender to42001f while the
receiver's local answer and observed inbound codec remain42e01f. Both peers
decode generated frames. Source ownership and provider stores are fixture data;
this does not establish Discord/SFU/DAVE interoperability or a constrained-only
decoder, hardware implementation or performance.

Independent review reproduced a stale-owner edge case: a provider description
getter could replace the stream metadata after the wrapper's initial check.
Adapter23 snapshots description fields once, then rechecks ownership and the
signaling/codec/MID/payload immediately before forwarding a modified answer.
It otherwise delegates the original description exactly once. The reproduction
now reports one getter read, one setter call, original-description forwarding
and no rewrite after retirement. Cached Discord's ordinary path was not observed
to trigger this getter behavior. It is a fixture-reproduced ownership safeguard.

158 provider tests and181 minified/composed tests plus5 sealed-endpoint checks
pass. The real-WebView test was rerun against adapter23 and now explicitly
asserts actual sender/receiver codec profiles, not merely the description labels;
both cases decoded4 frames, transferred one source, invoked the native setter
once, restored their hook, closed peers/tracks and observed browser exit.
Evidence: `artifacts/discord-webview/adapter-negotiation-real-23/result.json`,
`provenance.json`, `encoder-final-owner-tests-23.log` and
`encoder-final-owner-minified-23.log` under the same artifacts parent.
Probe timeout cleanup now stops only its runner-owned process tree. No
additional performance benchmark, browser flags or real capture was used.

0.0.18 is validated, packaged and selected through the isolated catalog update
path, retaining existing state and matching permission decisions. It remains
closed. Package: `artifacts/discord-webview/widget-0.0.18-h264/discord-0.0.18.wrwidget`;
SHA256 `74692D1A43FEF2779CB74FF73564FB773F408BA8EF9AA7A689BBA3FC133BB4E1`.
The retained comparison widget's17 checks and documentation contracts passed in
`artifacts/discord-webview/adapter23-final-verification/20261027T201447Z-f0c50dd6`.
The real-WebView check is an explicit local developer tool, not a new default CI
gate: installed browser/runtime and local-peer availability still vary by runner.
No commit, merge, push or release was performed for these improvements.

Current completion audit:

| Goal requirement | Authoritative evidence / remaining boundary |
| --- | --- |
| Remembered higher-detail bandwidth, default and eligibility preserved | Account/CAS/stale-action tests; live0.0.15 sender confirmed18Mbps limit. Actual rate was measured separately. Remote motion benefit was not accepted by the user. |
| Investigate encoding, compression, pacing and copies | Qualified exact-profile and9/18Mbps comparisons; current GPU/capability probe and cited upstream source. Cadence experiment reverted when benefit was unproven; no speculative transport rewrite. |
| Verify settings reach the encoder | Live18Mbps sender readback; local real peers confirm changed outbound profile. Corrected profile through real Discord is still pending. |
| Preserve default negotiation and receiver decoding | Real-WebView default/opt-in cases with actual native setters and decoded frames; original answer is retained on unsupported/retired authority. Remote Discord and other receivers remain unverified. |
| Source60FPS qualification and truthful performance | Generated source qualified before/during the recorded1440p60 tests. Reported22/56FPS results are not locked60 or physical-game proof. |
| Source choice, audio, pins, background, cancellation and teardown | No SDK/protocol/host/native transport changes from accepted baseline; relevant widget/provider tests and native peer cleanup pass. Live concurrent streams, audio and sustained background behavior through the new profile still require acceptance. |
| Controller-first settings and persistence |443 widget checks plus expanded/minimum native keyboard/mouse evidence. Physical controller acceptance remains distinct. |
| Privacy/isolation and reviewable candidate | Browser-owned authentication retained; generated local media only for independent tests; no production install/profile changes, no merge/push/release. Real candidate remains closed at user request. |

The goal is not complete: live corrected-profile delivery, motion quality/FPS,
receiver compatibility and application audio cannot be established from these
local fixtures. The remaining live check needs the user's microphone approval,
broadcast-source choice and receiver confirmation. Avoid repeating generic
benchmarks or adding unrelated changes while those acceptance gates are open.

### Review candidate 0.0.17 / adapter 22 (2026-10-27)

Current source includes the opt-in **Prefer hardware H.264** correction and the
stable broadcast-incarnation fix described below. Default negotiation is
unchanged. The correction wraps only the exact outgoing PC's answer setter and
changes six profile digits for the exact outgoing MID/payload, only if the local
offer already admits matching Baseline level/mode. All other SDP bytes are kept;
unsupported/ambiguous answers are passed through unchanged with a warning.
No global peer prototypes, experimental browser flags, forced renegotiation or
retries were introduced. Rejected or stale async completion cannot report success.
Ordinary stream UI record replacement no longer retires content/bandwidth/quality
hooks when stable metadata and exact source/RTC ownership still match. Same-key
restarts, metadata replacement and source/RTC changes retire them. Diagnostic
samples use that same stable incarnation and never compare across a restart.

The preference is account-scoped and explicitly separate from Bandwidth. Both
controls retain focus eligibility while a saved-state write is pending; duplicate
actions are rejected by the existing operation gate. Tests delay real fixture
state transport while preserving typed requests/responses and revision checks;
no production hooks or SDK changes were necessary.

The combined Discord verification passed all13 selected steps in
`artifacts/discord-webview/h264-candidate-verification/20261027T193804Z-4846e487`,
including443 widget checks,157 provider tests,180 composed/minified tests and
5 sealed-endpoint checks. Native keyboard/mouse checks in the synthetic worker
confirmed both dropdowns, keyboard selection, retained activation after save,
and confirmed saved values at1040x680. A420x360 native check confirmed stacked,
reachable controls and a usable encoder popup. It caught duplicated dropdown
text; labels now use the existing SDK's label/selected-value presentation.
Evidence: `artifacts/discord-webview/share-preferences-native/interaction-result.json`.
These are native keyboard/mouse observations, not physical-controller or remote
Discord proof. Both synthetic processes exited; real candidate79800 stayed closed.

0.0.16 was packaged and installed but never launched, then superseded by0.0.17
for the verified label correction. Actual corrected-profile sender/receiver
negotiation, remote compatibility, sustained moving-game FPS and received audio
remain open acceptance gates. A fresh candidate launch does not authorize the
agent to grant microphone access or choose broadcast content.

0.0.17 is validated, packaged and selected in the isolated catalog through the
documented update path; it is **left closed** after the user's close request.
Package: `artifacts/discord-webview/widget-0.0.17-h264/discord-0.0.17.wrwidget`;
SHA256 `29CE5439EF1C12436E9ADD93CC33906A0D35B18F9FA95D58BC90AF4B75A10F79`.
Launcher remains `artifacts/discord-webview/improvements-runtime-1/Launch-Candidate.ps1`.
The final label correction passed443 widget checks and documentation contracts
again in `artifacts/discord-webview/h264-ui-final-verification`. Default/previous
saved preferences were retained. No profile correction has yet been exercised
against the real remote Discord receiver. Next live check: join the approved
Lobby, user-grant microphone access, choose Prefer hardware H.264 + Higher detail,
and user-select safe moving content at1440p60/Clarity. Observe actual negotiated
profile/throughput and receiver picture before considering this verified.

### Motion improvement candidate 0.0.15 / adapter 21 (2026-10-27)

Baseline `23d542cb` is merged into local main. New work remains uncommitted on
`codex/discord-native-prototype`; no push/release is authorized. The user's active
goal now explicitly covers end-to-end motion quality, FPS, measured encoder
behavior and a remembered higher-detail bandwidth option.

Live baseline observation, before restarting the authorized Test server/Lobby
candidate: Silksong capture and outbound both2560x1440, **24 outbound FPS**,
H264, `qualityLimitationReason=cpu`, Clarity,9Mbps maximum and1x scale. Advertised
60FPS did not describe actual delivery. A separate two-second process sample
showed the WebView renderer near99% of one CPU core, capture worker19% and
frontend23%; no GPU encode engine appeared in that sample. NVIDIA reported0%
encode,38% GPU utilization. These are short observations, not a sustained test
or proof of software encoding: an AMD GPU is also present, and encoder-identity
stats may be privacy-gated. The game source was not independently qualified at
60FPS for this live observation.

Implemented:

- Per-account Automatic / Higher detail (up to18Mbps), using the existing SDK
  Select and private-state serial writes. Existing profiles default Automatic;
  changes apply to the next share. No capture-source/permission contract changes.
- The provider's `setDesktopInput` resets `bitrateMax` to3.5/9Mbps. The new
  exact-source attachment override changes only that synchronous policy write,
  restores the manager method immediately, and retains minimum/target bitrate,
  account eligibility, receiver demand and network adaptation. Automatic does
  not change provider bandwidth. Lost readback becomes a visible warning;
  there is no periodic rate-enforcement loop. Policy readback alone is not proof
  of actual sender rate.
- Stream details samples twice over approximately one second, on demand only.
  Provider-owned, bounded counter history is scoped to the exact stream and
  connection; resets, identity/geometry/codec changes and invalid intervals are
  not compared. Details distinguish encoded/sent FPS, actual payload bitrate,
  mean encode cost, QP and transport loss/RTT from advertised quality and limits.
  Missing/unknown hardware identity remains unavailable.
- Activation now loads the existing preference resource directly. Tests found
  cached initial state could otherwise leave the selector disabled until a later
  provider refresh. No retry flags, counters or new host behavior were added.
- The local probe separates startup from steady video-credit drops, and records
  stage pacing, receiver drops/freezes/loss/jitter with bounded storage.

The deadline-preserving native cadence experiment was **deferred and reverted**.
One qualified60FPS-source1440p60/H264/18Mbps run under concurrent real-stream/game
load measured55.07 submitted,56.05 encoded,56.66 decodedFPS, only3 steady credit
drops and zero local receiver drops/freezes/lost packets. It did not establish an
improvement over the historical58.77FPS result. Construction/encode costs were
higher (5.29/10.69ms versus3.46/7.78ms); this is not a controlled same-load A/B.
All four experimental native files are identical to main. Evidence and deferred
patch: `artifacts/discord-webview/cadence-1440p60-clarity-18m-1`.

Ownership/consumer applicability:

| Area | Owner and consumers | Disposition |
| --- | --- | --- |
| Bandwidth policy, measurements | Discord provider; WebView widget and retained native comparison adapter | Optional argument is backward compatible; source and composed/minified adapter checks pass. Other providers do not share Discord encoding semantics. |
| Preference/control lifecycle | Discord widget using existing SDK Select/resource/serial state APIs | No protocol or host additions; account/call/attempt checks retained. |
| Capture diagnostics | Standalone validation tools | No shipping consumer migration; runtime buffers, audio, capture tickets and teardown unchanged. |
| Cadence experiment | Native capture host | Rejected for this candidate pending causal evidence; no runtime transport change shipped. |

Verification:422 widget checks passed in
`artifacts/discord-webview/bandwidth-widget-verification/20261027T190049Z-0819f1f7`.
The remaining12 verification steps, including source/minified provider tests,
backend routing, capture measurement tests and runner self-test, passed in
`artifacts/discord-webview/motion-provider-final-corrected/20261027T190200Z-45ba02a3`.
The new manifest step was added to the runner's explicit aggregate expectation
after its first self-test correctly caught the mismatch.

Candidate0.0.15 was validated, packaged and updated through
`scripts/Install-CommunityPackage.ps1` in the existing isolated catalog; settings
and matching permission decisions were retained. Frontend79800 uses the same
qualified host/native payload. Package:
`artifacts/discord-webview/widget-0.0.15-bandwidth/discord-0.0.15.wrwidget`, SHA256
`DA2344FF970C2A122B05C406F6EADC0C9354D1B7CB5A891587F05FCA064F56C6`.
Live18Mbps readback, actual moving-content throughput, remote picture quality
and native selector/controller behavior were pending. User was asked
to rejoin/approve microphone and select the safe source for the bundled check.

Subsequent live0.0.15 check: user selected Higher detail and shared Silksong again,
but reported little visible improvement. Native UI confirmed the selected18Mbps
setting and the sender readback confirmed18Mbps maximum. A1.1-second on-demand
sample reported2560x1440,52.3 encoded/sentFPS,1.72Mbps actual,6.95ms mean encode
time,QP17.6,26ms transport RTT and0% reported loss, with no reported limitation.
Effective content hint was **Smoothness**, not a confirmed Clarity selection.
The source was not independently qualified for this live sample. This verifies
policy propagation, not sustained60FPS, sufficient motion quality or a fix for
the user's report. The share was left running.

### H.264 profile / hardware investigation (2026-10-27)

Added a separate read-only WebView2 capability probe using a new empty profile,
the pinned SDK and installed runtime156.0.4314.50; its browser exits afterward.
No environment/policy GPU overrides or disable flags were found for the isolated
candidate. The active adapter is RTX5070Ti/D3D11 and video encode is enabled.
The initial GPU page is an asynchronous placeholder; the probe waits for real
adapter identity before interpreting its report.

The GPU table's1080p121/4K30 buckets do **not** establish a1440p60 limitation.
The browser encoding-capability query reports1080p60,1440p30 and1440p60 supported
and smooth. Baseline42001f, Main4d001f and High640032 (packetization mode1) report
power-efficient support; Constrained Baseline42e01f reports false. Capability
predictions are not actual encoder proof. Evidence:
`artifacts/discord-webview/gpu-capabilities-default-4/gpu-capabilities.json` and
`gpu-overrides-20261027.json` in the parent directory.

Discord's cached SDP builder hardcodes42e01f (`quality-source-20261026/725999.js`,
1396–1399). Chromium's `PlatformH264CbpEncoding` is disabled by default on Windows
in [webrtc_util.cc](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/renderer/platform/peerconnection/webrtc_util.cc).
This explains a plausible difference between generic H264 synthetic runs and
Discord's selected profile, but the installed stream's exact negotiated fmtp
still needs confirmation. Diagnostics now extract only bounded H264 profile/mode,
never raw SDP;141 provider tests pass. This addition is not yet installed in0.0.15.

[Legcord's primary implementation](https://github.com/Legcord/Legcord/blob/a5f0a9c3b5d6adab74e203656ed8b51bd831ccb2/src/discord/preload/patches.mts)
rewrites Constrained Baseline to Baseline in local and remote descriptions, with
an opt-out. Its [Windows flags](https://github.com/Legcord/Legcord/blob/a5f0a9c3b5d6adab74e203656ed8b51bd831ccb2/src/common/flags.ts)
also enable constrained-baseline hardware support. Do not copy its global peer
prototype hooks, bitrate floors, blocklist overrides or global flags into this
host. WidgetRail's BrowserOwner environment is shared by other browser consumers.
The next bounded comparison selects exact profiles in the synthetic harness;
the provider review is evaluating an exact outgoing-PC correction that preserves
account/source/RTC ownership, negotiation and DAVE. No profile rewrite or GPU
flag has been installed in the user's candidate.

The exact-profile comparison is now recorded in
`artifacts/discord-webview/h264-profile-comparison-1`: at full2560x1440/60FPS
source,18Mbps/Clarity,42e01f encoded/decoded22.03FPS with43.73ms mean encoding,
4.13Mbps actual andQP28.63;42001f encoded55.94/decoded56.36FPS with14.49ms,
14.40Mbps andQP18.59. Sender and receiver both confirmed the requested profile
and mode1. No receiver drops/freezes/loss were reported. GPU implementation
fields remained unavailable; do not call this proof of the active hardware
encoder. Other user apps were running and candidate shutdown may have begun
near the final second of the second sample. No repeated comparison was run.
Both sources qualified near60FPS before/during; neither pipeline achieved locked60.

The user explicitly requested closing the isolated candidate. Its worker retired
at19:22:56 UTC; frontend79800 and its capture worker are gone. Do not treat the
earlier PID as a running candidate. A new opt-in, provider-scoped encoder
correction is being implemented; default Discord negotiation stays unchanged.

Confirmed lifecycle defect found during this review: ApplicationStreamingStore
replaces the public stream object during ordinary viewer/RTC updates. The prior
`currentShare()===ownedStream` guard could therefore discard quality/content
hooks within one broadcast. Its separate streamer metadata object is created by
STREAM_START and cleared by STREAM_STOP; exact metadata identity/source plus
RTC/PC/selected-track guards can distinguish a same-key restart without depending
on presentation-object identity. Cached source evidence:
`artifacts/discord-webview/provider-source-20261027/application-streaming-store-616356.js`.
This is provider-owned broadcast identity, not SDK presentation plumbing.

### Motion-quality investigation and external references (2026-10-27)

The user subsequently authorized independent investigation/improvements. The
agent extended the generated capture probe with bounded explicit codec and
bitrate-ceiling selection, actual codec confirmation and bitrate/encode-time/QP
measurements. Existing Auto/8Mbps defaults remain; no shipping capture contract
or installed user setting changed.12 focused probe tests pass.

One H264/9Mbps/Clarity run, followed by one controlled18Mbps comparison, both
used a generated source verified at60FPS before and during capture. The9Mbps
run sent full2560x1440 at7.22Mbps with mean quantizer35 and about55 decodedFPS;
18Mbps sent13.96Mbps with quantizer21.09 and58.77 decodedFPS. Encode time was
8.22/7.78ms per frame. Both retained exact geometry and stopped with confirmed
worker exit in59/66ms. The runner's85% throughput tolerance passed; do not call
either a locked60FPS result. This supports testing an explicit higher-detail
bandwidth option through the provider before rewriting transport. It does not
prove remote Discord will accept that ceiling or sustain the rate, and the
encoder implementation/hardware fields were unavailable. Full evidence/limits
are in `tools/DiscordWebViewPrototype/QUALITY.md` and
`artifacts/discord-webview/h264-1440p60-clarity-{1,18m-1}/result.json`.
Only runner-owned generated windows/profiles were used and closed; frontend77324
was left running. No user action is needed for the next source/policy work.

User reports noticeable quality loss during motion after picture/advertisement
became correct. This turn is research, not authorization for a new media-stack
rewrite. No capture setting, installed client, current share or runtime was
changed. The last live observation was H264 with a9Mbps maximum, not measured
actual bitrate during motion. The active encoder's hardware/software identity,
quantization and motion-time limitation reason are still unknown. Compression /
congestion adaptation is a strong hypothesis, not a demonstrated root cause.

Current path is WGC/D3D11 → staging readback/packed BGRA → bounded shared memory
→ VideoFrame/MediaStreamTrackGenerator → Chromium/Discord encode. Confirmed at
`LiveCapture.cpp:157–161`, `CaptureWorker.cpp:58`, `native-media.js:175–176`.
This is not a fully GPU-resident encode path. Extra copies/synchronization can
cost throughput/CPU, but do not themselves prove the reported motion blur.
Local hardware inventory includes RTX5070Ti and AMD integrated graphics; H264
in the diagnostic does not establish which GPU, if any, performs the encode.

Primary-source comparisons, read from current upstream source/docs:

| Reference | Relevant mechanism | Fit / boundary |
| --- | --- | --- |
| [Vesktop picker](https://github.com/Vencord/Vesktop/blob/main/src/renderer/components/ScreenSharePicker.tsx), [capture host](https://github.com/Vencord/Vesktop/blob/main/src/main/screenShare.ts) | Electron desktopCapturer/display-media handler; browser media path; quality patch specifies0.5Mbps minimum,8Mbps maximum,0.6Mbps target; content preference and constraints | Closest Discord reference. Its8Mbps patch is not evidence of better motion quality than our observed9Mbps ceiling. Do not copy timer/global-default/minimum-FPS behavior blindly. |
| [Sunshine D3D11 NVENC input](https://github.com/LizardByte/Sunshine/blob/master/src/nvenc/nvenc_d3d11_native.cpp) | Registers a D3D11 texture directly with NVENC; broader project supports vendor hardware encoders | Strong Windows GPU-pipeline reference. Its encoded output cannot simply replace raw VideoFrame input in the existing Discord browser track. |
| [Selkies design](https://github.com/selkies-project/selkies/blob/main/docs/design.md) | Native pixelflux capture/encoding with hardware-first and zero-copy paths where supported; WebSockets default, WebRTC opt-in | Useful architecture/rate-control reference. Current server is Linux-focused, not a ready Windows capture replacement. Older GStreamer descriptions are no longer its current mainline design. |
| [Discord-video-stream](https://github.com/Discord-RE/Discord-video-stream) | Native/FFmpeg hardware encoder choices, explicit bitrate controls, RTP/RTX and DAVE support | Demonstrates direct encoded Discord streaming is possible. Current usage requires selfbot/user-token authentication; adopting it directly contradicts our browser-owned authentication boundary. Much larger protocol/media integration, not a drop-in transport fix. |

[Discord's engineering overview](https://discord.com/blog/how-it-all-goes-live-an-overview-of-discords-streaming-technology)
explains codec negotiation, bandwidth estimation and receiver constraints. Better
hardware does not remove those network/codec tradeoffs. The generated track
accepts raw [VideoFrame objects](https://developer.chrome.com/docs/capabilities/web-apis/mediastreamtrack-insertable-media-processing),
so importing a native encoder would also require a supported encoded-media
integration or a new sender; decoding and re-encoding its output is not the goal.

Recommended decision sequence: (1) bounded motion observation of actual bitrate,
quantization, encoder implementation, encode time and FPS at both sides, with the
source independently qualified at60FPS; (2) verify provider-supported bitrate,
codec and hardware-encoder choices, retaining congestion control and account
eligibility; (3) consider a GPU-converted NV12 transport to reduce BGRA byte volume
if copy cost is measurable; (4) evaluate a native GPU sender separately only if
the browser route remains the limiting factor. Preserve4FPS idle behavior.
Codec support must be negotiated across sender, provider and receivers. No claim
that changing a label, forcing AV1, adding NVENC, or increasing maxBitrate alone
solves this report. No additional benchmarks were run during this research.

### Installed candidate and live-test handoff

#### Live quality correction verified on0.0.14

The user reported the new share looked correct, then agent refreshed Stream
details in the running native candidate77324/worker21920. It reported capture
and outbound dimensions2576x1384, advertised2680x1440 at60FPS, H264, no reported
quality limitation, effective Clarity,9Mbps maximum and1x scale. Initial details
reported quality applied; a later read showed861 captured frames, live input,
desktop and sender tracks, connected peer, and4FPS on the mostly static source.
No quality warning was present. The receiving native Discord app independently
exposed **1440p60FPS** in its quality button. This verifies the previously wrong
720p label and recovered source-sized outgoing picture, not a sustained60FPS
moving-content performance result or received audio.

The announced2680x1440 is the selected1440-height ceiling at this window's aspect;
actual2576x1384 output is the smaller source size, with no upscaling. The later
diagnostic reported no retained quality hook while the announcement and effective
Clarity remained correct; do not equate absence of that temporary hook with a
new warning or claim its long-term lifetime was validated. Both processes remain
running; no restart, source reselection or further benchmark was performed for
this double-check. User-owned physical/audio acceptance and qualified moving60FPS
validation remain open. Pending quality handoff questions are now satisfied.

Current:0.0.14 / adapter20, frontend77324, same isolated profile and host.
Package `artifacts/discord-webview/widget-0.0.14-event/discord-0.0.14.wrwidget`,
SHA256 `65fb787fa416d199cdcbb25adf30647e6b00d66ff32c2ca05e9f7ebd3dec3636`.
The0.0.13 live checkpoint was **attachment-timeout**, while picture/capture and
sender tracks remained live. Inspecting the actual event constants found the
cause:651139.b.Connection is the lower-case string "connection". The adapter
subscribed to "Connection" and never received an event. The earlier suggestion
of listener-order failure was an unconfirmed hypothesis, not the established
cause; no listener-priority or RTC-authority changes were made.

Adapter20 corrects registration and removal to "connection". The fixture now
uses Node EventEmitter with the actual provider event name rather than manually
invoking stored callbacks. It fails on the original spelling and passes after
the correction.126 provider tests and147 minified provider/backend tests plus5
composed tests pass (`connection-event-fix-tests.log`,
`connection-event-minified-final.log`). The final Release package validates and
packs. Existing precise checkpoint reporting and all authority checks remain.
Source reference:
https://github.com/Wumpus-Central/discrapper-canary/blob/267a7c8e16837c178b83d5147537882116031ec8/chunks/651139.js
(cached alongside972347.js and244600.js for emitter/lifecycle investigation).
Test server/Lobby was verified before closing frontend22556; it and worker24824
exited normally. The reviewed update preserved settings/permissions. A new
user-selected share subsequently passed live metadata verification as recorded
above; performance/audio boundaries still apply.

#### Previous0.0.13 diagnostic handoff

Current:0.0.13 / adapter19, frontend22556, same isolated profile and host.
The user confirmed0.0.12 still warned; live diagnostics showed actual picture
and source-sized H264 video, but unchanged720p30 advertisement and retired quality
binding. The enum correction is valid against source, but did not establish the
remaining guard failure. Do not present it as verified live success.

Adapter19 now latches the precise failed validation checkpoint into the existing
scoped quality warning and exposes only a bounded code in on-demand Stream
details, first in the limited-height notice. Source/account/RTC/connection,
selection, constants, quality manager, primary encoding, preference, apply,
readback and advertisement checks retain their original enforcement. This is
diagnostic refinement, not a bypass or a claimed quality fix. Tests verify that
the code survives hook cleanup without serializing provider exception text.
The12-step Discord verification passes in
`artifacts/discord-webview/quality-checkpoint-verification/20261027T175234Z-c0702423/verification-result.json`.
Final Release package `artifacts/discord-webview/widget-0.0.13-checkpoint-final/discord-0.0.13.wrwidget`
validates/packs; SHA256 `4bcc573b67405bc3e385dedc88b91336936b60680a502720b0630d663f60e91a`.
Backend49,831 bytes. The prior Lobby frontend71912/worker85360 exited normally;
the reviewed update retained settings/permissions. A user-selected share is
pending for the checkpoint read. No more speculative guard changes before that
evidence, and no performance benchmark of unqualified source content.

#### Previous0.0.12 quality-enum correction

Current:0.0.12 / adapter18, frontend71912, same isolated profile/services and
production-candidate8. Package `artifacts/discord-webview/widget-0.0.12-quality/discord-0.0.12.wrwidget`,
SHA256 `fe2e1d366e09d3e703cf5155cbff80c065f62c936c77e2c63e61aa4b305437b1`.
The0.0.11 live check restored outgoing picture (user-confirmed) with live
input/desktop/sender tracks and a connected peer. One observed H264 report sent
2576x1384 at31FPS, Clarity,2.3Mbps maximum and1x scale. This was not a qualified
60FPS-source measurement and is not a60FPS pass/failure. Advertised quality was
still720p30 and the quality hook reported unconfirmed, so metadata correction
remained incomplete despite the recovered picture.

Root cause of that guard failure: Discord731854.ei defines string constants
FIXED="fixed", SOURCE="source"; adapter17 incorrectly required an integer and
the fixture repeated that incorrect model. Source:
https://github.com/Wumpus-Central/discrapper-canary/blob/267a7c8e16837c178b83d5147537882116031ec8/chunks/731854.js#L242
(cached in quality-source-20261026/731854.js). A string-enum regression fixture
fails before the correction (`resolution-enum-reproduction.log`). Adapter18
validates the actual supported enum pair; unknown pairs keep normal attachment
and warn without writing an invented mode.125 provider checks,146 minified
provider/backend checks and5 composed-backend checks pass. The final Release
package validates/packs; sealed backend49,328 bytes. Evidence:
`resolution-enum-final-source-tests.log`, `resolution-enum-minified-final.log`.
Test call/Lobby was verified before restart, and frontend59060/worker56592 exited.
The reviewed update preserved settings and permissions. A fresh user-selected
share is pending to verify first-announcement metadata on0.0.12.

#### Previous0.0.11 handoff and recovered source transfer

Current:0.0.11 / adapter17 is installed in the original isolated catalog and
frontend59060 is running. Package:
`artifacts/discord-webview/widget-0.0.11-pool-fix/discord-0.0.11.wrwidget`, SHA256
`7753b8bfb20fd93b5368617c8eed11d29312182c8698d875db064db2fa478feb`.
The prior frontend75728 was verified in Test server/Lobby before the authorized
restart; frontend75728 and capture worker81432 both exited normally. Settings
and matching permissions were preserved through the reviewed update path.
User microphone consent and reselection of the same safe source are pending.

The user reported a blank outgoing stream on0.0.10. Live Stream details showed
2576x1384 capture dimensions, no outbound RTP video, and unchanged720p30
advertisement. Source inspection found a regression in the new quality hook:
Discord DesktopInputPool.get **removes** the entry (cached725999.js lines3073–3088).
Using it for quality/warning inspection stole the queued source before Discord's
setGoLiveSource could attach it. Map-based tests incorrectly modelled get as
read-only. The corrected fixture reproduces this failure in
`artifacts/discord-webview/destructive-pool-reproduction.log`.

Adapter17 observes only the known browser pool's own data slot and never calls
get for inspection. Discord remains the single source consumer; an absent slot
after handoff is expected, while a replaced pool/source/track still revokes the
binding. Warning observation follows the same rule. The only remaining pool get
is the pre-existing intentional take-and-destroy of an abandoned acquisition.
This is a provider-specific ownership correction, not a host/SDK change.
All quality and Clarity fixtures now use destructive source transfer:124 source
checks and145 minified provider/backend checks plus5 composed checks pass.
Evidence: `destructive-pool-fix-tests-2.log`, `destructive-pool-minified-final.log`.
The12-step pre-pool diagnostic gate also passed407 widget checks in
`blank-stream-diagnostic-verification/20261027T173444Z-6aa21d9a/verification-result.json`.
The final Release package validates/packs and its backend is49,315 bytes.
Live picture/advertisement verification remains pending; do not infer it from
the regression fixture. Additional on-demand Video path diagnostics report
bounded capture counts, track states and attachment state without source IDs.
Independent source review confirms the corrected handoff checks. Follow-up
lifecycle audit: the older abandoned-acquisition cleanup still takes by source ID;
existing delayed-cleanup coverage uses different IDs. Check same-ID reuse before
claiming arbitrary overlapping provider-acquisition cleanup is safe. Keep this
separate from the reproduced observation/get regression; do not weaken the exact
source checks or reintroduce a consuming read to address it.

The preceding0.0.10 / adapter16 package was installed in the original isolated catalog.
Its reviewed update preserved private settings and matching permissions. The
previous frontend75020 was observed idle, closed normally and confirmed exited;
new frontend75728 uses production-candidate8 and improvements-runtime1 services.
Native inspection confirms retained sign-in and loaded server/channel/activity
data. The production overlay and comparison clients were not changed.

Package: `artifacts/discord-webview/widget-0.0.10-final/discord-0.0.10.wrwidget`;
SHA256 `3e506b4a72525c9ecdf3401194f784842d965e55975ddf9bcc7f82b5a50ba00a`.
This supersedes the earlier uninstalled0.0.10-clarity package below. The final
backend is48,073 bytes. The12-step Discord shard passes with406 widget checks,
121 provider checks,142 minified provider/backend checks and5 composed checks:
`artifacts/discord-webview/encoder-details-verification/20261027T172239Z-d75d02fc/verification-result.json`.

Stream details now displays bounded codec, reported adaptation reason, effective
content hint, degradation preference, max bitrate and single-encoding scale when
available. Previously the adapter read sender parameters but the widget discarded
them. The codec is resolved internally from RTC stats and reduced to an allowlisted
name; IDs, SDP, arbitrary provider text and connection metadata are not displayed.
These are on-demand observations, not new background polling or encoder changes.
Missing values remain unavailable. This is Discord domain presentation using the
existing notice/operation contract; no shared framework addition was needed.

The next live check requires the user to join the approved Test server/Lobby,
grant microphone consent and choose a safe share at1440p60/Clarity. A single
handoff question is pending; do not automate consent or select broadcast content.
Inspect live Stream details before deciding on another controlled encoder test.
No live codec, advertised-label or effective-preference pass is claimed yet.

### Packaging and encoder-policy follow-up

The final composed sealed backend is now executed in a VM with synthetic loaded
Discord modules, not just tested as separately minified components. Five checks
cover no-Vencord startup, delayed module discovery, sign-out, owner/cancellation
and command restrictions, native-capture admission and the64KiB asset bound.
`Test-MinifiedBackend.ps1` runs these in the existing CI step after131 component
checks. Composition now rejects a missing/ambiguous comparison-provider binding
instead of silently shipping a Vencord-dependent backend after source drift.
Ownership: package build tooling; no new SDK or host abstraction is needed.
Evidence: `artifacts/discord-webview/composed-backend-verification-2.log`.

The local fixture now distinguishes initial encoding policy from current bounded
sender parameters (max bitrate/FPS, scale, degradation preference and content
hint). Reads do not mutate encoding policy or expose SDP/transport IDs. Ten
capture fixture tests pass in `current-sender-diagnostic-tests.log`. This is
diagnostic coverage only; no additional capture benchmark was run.

The latest local1440p60 result reports12.056 seconds of bandwidth limitation and
zero CPU limitation, with VP8 and720p encoded output despite a1440p source/track.
The8Mbps fixture is a partial policy comparison: cached Discord module725999.js
lines2328–2343 chooses degradation preference based on experiments and budget;
its codec negotiation is separate. Do not infer the live Discord encoder's
resolution/FPS solely from this local run. Next comparison needs observed live
codec/degradation/scaling and RTP dimensions, available through Stream details.

The Clarity override was reproduced in a focused provider fixture using the
cached module725999.js synchronous attachment sequence: setDesktopInput →
input.setDesktop → mergeStreams → handleStream → setStream. mergeStreams retains
the same track objects; the motion experiment overwrites their hint. The initially
failing evidence is `artifacts/discord-webview/clarity-reproduction-1.log`.

Widget0.0.10 / adapter16 corrects this in the Discord adapter. It wraps only the
exact outgoing connection's setStream after verified source/RTC ownership and
restores the host-selected hint after synchronous attachment. No global prototype,
experiment policy or SDK/host API changes. The map is bounded to four connections;
stop/disposal/account/call/source/stream retirement restores only methods still
owned by this binding. Comparison Vesktop sharing without host media is unchanged.
Foreign tracks are never modified. Unsupported methods, async completion, clones,
unwritable hints and optional-status failures show a scoped preference warning
while keeping a working stream available; they do not reject supported quality.
The warning uses the existing domain snapshot and main-widget notice path.

Evidence:120 provider checks (`clarity-provider-tests-3.log`),141 minified
provider/backend checks plus5 composed-asset checks (`clarity-minified-final.log`).
The12-step Discord shard passes including404 widget checks in
`clarity-final-verification/20261027T171812Z-ed7779b0/verification-result.json`;
the final JS-only robustness additions were then rerun in source/minified forms.
Fresh Release package output is `artifacts/discord-webview/widget-0.0.10-clarity`;
validation and packing pass (33 files); SHA256 is
`c4311f7d19c95f23581a4c5e90029765a37bace44e56fbc422701d614fb76cc7`.
The sealed backend is47,739 bytes. This earlier prepared package was superseded
by the final0.0.10 package above before installation. Live provider acceptance
is still open.

### Approved improvements in the working tree

Widget0.0.9 / adapter15 source includes the accepted additional controls:
participant local voice mute/volume, Menu → Voice settings (input/output devices),
actual RTC connection phase, favorites/recent channel prioritization, and a
remembered Smoothness/Clarity choice. Microphone gain is narrowed out because the
current browser media engine's `setInputVolume` is a no-op; the dialog explains
unavailability rather than pretending a saved number changes sound.

Favorites use existing SDK `PrivateState`, revision-CAS, resources, serial
operations and indexed collections. There is no new storage/focus framework.
They are keyed by account and channel IDs, capped at64 favorites and8 recents per
account,8 stored accounts; a new account cannot silently evict another account's
favorites. Invalid data is normalized or rejected without crashing or overwriting
an unknown schema. Only confirmed connected observations enter recents. Search
retains catalog ranking; ordinary lists place favorites and recents first without
duplicates. Native favorite-menu/reorder/return-focus checks pass at minimum and
expanded sizes. The pushed picker now uses the existing vertical-scroll contract
around its finite indexed list, correcting clipped focused rows at minimum size.

Voice dialogs adopt existing SDK modal/navigation/resource contracts. Source and
managed checks found and corrected static BackActionId reuse by binding dialog
actions to the existing navigator revision/lifetime, not an author counter.
The async device dialog initially misused `SizeToContentHeight`, which freezes its
first loading measurement. It now uses the supported fixed-height presentation;
the synchronous participant dialog stays content-sized. Native controller checks
pass at both sizes: participant selection/mute/cold slider entry, adjustment B
versus modal B, exact participant return, Menu → Voice settings, device dropdown
cancel/selection, refresh, favorite add/reorder/cancel, exact channel join, selected
favorite on reopening, and visible focus restoration. Evidence:
`artifacts/discord-webview/voice-controls-final-native-2/{minimum,expanded}/result.json`
(39 and40 checks). These invoke actual WinUI controller routes with a synthetic
provider, not a physical controller or live audio device. Startup was explicitly
activated through Computer Use; fixtures wait for native entry, loaded flyout
options and queued scroll reveal rather than treating a new declaration as a
completed interaction. Reopening the picker preserves its existing remembered
channel focus. No shared host behavior change was needed for these corrections.

The provider configures only the exact outgoing source/connection, before its
first video announcement; no global Discord default-quality patch is installed.
Unsupported/private-API changes surface a bounded quality warning even if a
stream is already active. A temporary instance wrapper/event subscription is
retired on stop, source/account/call replacement or timeout. The provider owns
these Discord-specific semantics; the generic host only owns capture parameters.

The composed readable adapter now exceeds the sealed64KiB script limit. The
package build uses lockfile-pinned Terser5.51.2 with compression/property-mangling
disabled; no runtime loader or wider host resource bound is added. Provider and
backend checks also run against the minified code (131 pass), and that check is
enrolled in the managed Discord CI group. Current packed-script source test:
80,944 bytes becomes46,024 bytes. Final verification through the managed Discord
shard passes all12 steps, including403 widget checks and131 minified provider
checks. Evidence: `artifacts/discord-webview/final-improvements-verification/20261027T170921Z-420890cb/verification-result.json`.
The verification runner self-test also passes (`final-runner-selftest.log`).

Capture throughput correction: the native worker pipe now has an explicitly
inherited data-ready event instead of `Sleep(1)` polling. Existing bounded frame
credits/mappings and teardown remain. Private worker protocol2/argv6 rejects mixed
native payloads; exported C ABI layouts remain unchanged. Production native files
must come together from `event-wakeup-native-production-1`. Native coalesced-event,
credit timeout, queued-terminal/exit, audio recovery, blocked-exit and parent-death
checks pass; production rejects every private validation flag.

The60FPS source preflight and during-run checks pass:60.005 and59.996FPS. With
event wakeup, submitted58.909/encoded58.905/decoded58.815FPS improved from about51/49/49.
The source and browser track are2560x1440, but the local8Mbps WebRTC test adapted
the encoded picture to1280x720. Its strict1440p60 result is still **failed**;
do not describe that as full-resolution delivery or weaken the geometry check.
See `docs/discord-capture-content-preference-note.md` for exact evidence and
the shared-consumer matrix. Idle replay remains unchanged.

The0.0.9 baseline used adapter15 on freshly trimmed
`artifacts/discord-webview/production-candidate-8`, launched with
`improvements-runtime-1/Launch-Candidate.ps1`. Its configuration selects freshly
built services and the original isolated browser profile/catalog. The package was
applied through reviewed `wrail update --file --sha256 --apply --settings-root`,
with disable/enable around the update; settings and matching permissions were kept.
Package SHA256: `206c4202568d7aefb702c895b7daa157ee46ff44d0821004dbfc51034bf3eefc`.
All three native capture files match the qualified event-wakeup production payload.
The publication verifier passes302 local managed references and asynchronous cleanup.
Earlier candidate PID69352 and capture worker75136 exited normally before updating.
Candidate8 PID75020 loaded the signed-in channel catalog without authentication
or profile copying, then was closed for the0.0.10 update recorded above.

The user explicitly authorized this restart and gave standing permission to
restart the isolated candidate while it is in **Test server → Lobby**. Verify
that exact current call before relying on the standing permission; other calls
still require confirmation. Do not touch the installed production overlay or
copy browser account data. Future safe test restarts in Lobby need no repeat ask.

Current delivered UI is Call + activity, the idle server/channel picker, ranked
full-name suggestions/results, X mute, Y start-sharing and L3 picker with indexed
return focus. Existing theme data colors now supply purple/green/red highlights;
focus uses the separate host focus color. Native navigation passes50 minimum-size
and49 expanded checks; native audio/pin passes27. SDK177 and compatibility17 pass.
Those navigation/audio figures are earlier baseline evidence. The current final
Discord shard and native voice controls evidence are recorded above. Candidate8
passes the fresh publication verifier in `candidate8-publication-verification.log`.
No release or merge is authorized.

The better voice-controls pin is installed: top-aligned header, bounded passive
participant avatars/speaking rings and equal-width call controls. The current
voice-pin-era widget suite passed336 checks (current403). Native voice-pin validation passes49 checks at
400x180 and320x180 DIPs, including mute/deafen updates, directional routes and
foreground/focus return. The180-DIP protocol minimum is retained; saved per-layout
dimensions are not silently overwritten.

| Remaining requirement | Required evidence / disposition |
| --- | --- |
| Discord quality configuration / advertisement | Implemented per exact source/connection before announcement; source/minified tests pass. Live remote label still unverified. Baseline720p30 remains available without optional metadata hooks; higher modes require them. |
| New voice-pin layout | Native49-check pass, installed in0.0.8; physical user acceptance remains |
| Expanded quality capture | Qualified60FPS source now yields58.8 decodedFPS after event-wakeup correction; local encoder adapts1440p to720p, so strict1440p60 remains failed. No repeated benchmark without a new hypothesis. |
| Provider quality discovery | Cold loaded-module capability discovery and saved preference across a real restart; source fallback is tested but actual account eligibility is not yet observed |
| Current constrained native capture admission | Latest native content-choice fixture passes46 checks, including constrained-quality admission and Smoothness/Clarity |
| Physical acceptance | User microphone/device behavior, remote picture/audio, high-quality encoding, concurrent streams, hidden page→controls continuity and physical controller/pin interaction |
| Native policy CI | Enrolled and locally passed through the actual native verification runner; uploaded CI execution remains unobserved |
| Setup/docs | Keep current runtime/package paths, documented limitations and consumer matrices consistent; do not treat historical prototypes as the shipping path |

Native checks below are complete for this handoff. More capture benchmarking
needs a concrete hypothesis or implementation change; do not repeat the same
failing measurements without new evidence. Do not mark the goal complete from
the passing subset.

### Restarted candidate and high-quality delivery evidence (2026-10-27)

Candidate6 PID12876 exited through normal WM_CLOSE shutdown, and its capture
worker57472 exited. The exact reviewed0.0.8 package was applied through
`wrail update --file --sha256 --apply` with the existing isolated catalog/profile,
surrounded by CLI disable/enable as required by that update command. Settings and
matching permission decisions were retained. `Launch-Candidate.ps1` now points
to candidate7. Candidate69352 was launched and sign-in/data retention was observed
in its native controls; no microphone consent, real share selection or real call
join was automated. The completed hidden generated-track fixture66480 was also
closed after identifying its exact validation-only command line.

Native evidence:

- `voice-pin-native-check-1/result.json`:49 checks pass,400x180 and320x180,
  native focus/control updates, passive creation and B/foreground return.
- `consent-quality-current-1/result.json`:44 checks pass, actual WinUI/WebView2
  chooser, synthetic microphone consent, constrained quality admission, stale
  document/choice cancellation and uncertain-cleanup behavior; no real device.
- `quality-worker-1080p60-current-1`:invalid test deployment, root supplied an
  older native directory. Buffer setup failed before capture. It is not quality
  performance evidence. Source and live candidate hashes established the current
  native directory as `quality-native-production-1`.
- `quality-worker-1080p60-current-2/result.json`:failed. About54.46 submitted FPS
  and37.27 decoded FPS; received1904x1072. Application audio reached local WebRTC
  (580,320 additional received samples). Clean59ms stop; worker exit confirmed.
- `quality-worker-1440p60-current-1/result.json`:failed. About52.58 submitted FPS
  and21.72 decoded FPS; received2544x1432. Clean78ms stop; worker exit confirmed.
  Paired RTP stats show VP8, matching encoded/decoded frame counts, no reported
  receiver frame drops and `qualityLimitationReason:none`. This narrows the
  observed gap to at/before encoding, but does not identify the cause.

The geometry check assumes requested outer window dimensions equal WGC capture
dimensions; measured frames are16 pixels narrower and8 shorter. Preserve the
strict test until an independent source-owned capturable-bounds measurement
replaces that wrong assumption. Do not derive expectations from receiver output.
This geometry issue does not explain the FPS gap. New test-only capture.js stats
include bounded outbound/inbound codec, dimensions, frame/timing counters and
quality-limitation observations, with single-flight and stale-result handling;
three Node tests pass (`capture-instrumentation-tests-1.log`). No production FPS,
idle cadence, codec, or quality policy was changed to obtain a passing result.

Remote Discord resolution and high-FPS acceptance are still unproven. The update
is available for testing its constraint fix/diagnostics and voice pin, not as a
claim of completed60-FPS delivery. Use **Menu → Stream details** during a new share
to compare actual outbound resolution with advertised metadata.

### Current quality-label investigation (2026-10-27)

The user approved fixing this using Vesktop as reference. Current upstream
`src/renderer/components/ScreenSharePicker.tsx` patches `getDefaultGoliveQuality`
and explicitly assigns the outgoing connection's `maxFrameRate` and
`maxResolution`; its source picker also reapplies track constraints.
`src/main/screenShare.ts` uses Electron `desktopCapturer` and
`setDisplayMediaRequestHandler`, whereas WidgetRail supplies worker-backed
generated tracks to WebView2. WidgetRail's `adapter.js:beginShare` updates saved
provider quality settings but does not synchronize advertised stream parameters.
This is a provider-adapter gap, not a reason to change generic host metadata or
hardcode a receiving label. Retain exact account/call/attempt/connection checks,
supported-mode admission and stale-result rejection when applying the reference
approach. Do not copy broad prototype mutation or delayed callbacks without
those safeguards. The quality policy belongs to the Discord adapter; generic
capture constraints and lifecycle remain host-owned.

The user selected1440p60, but Discord desktop's receiver **quality label** says
720p60. The selected source is a windowed ChatGPT window. It has not been
measured, inspected or manipulated. A smaller source is not upscaled; this alone
does not explain the receiver's advertised720p label or establish encoded size.

Source evidence at Discord commit267a7c8e16837c178b83d5147537882116031ec8:
`725999.js:973` reads the source track's real `getConstraints()` dimensions to
configure capture/encoding and the higher bitrate policy. Missing dimensions
retain the quality manager's1280x720 defaults (`466376.js:174`). Separately,
`904986.js:137` initializes advertised `maxResolution`, but its later
`updateVideoQuality` does not update that field. The receiver label reads that
advertised metadata, not decoded frames (`650338.js:41`, `872363.js:100`). Copies
are under `artifacts/discord-webview/quality-source-20261026`. These are source
findings, not measurements of the user's current stream.

Correction in source: `native-media.js` waits for the first accepted frame and
then observed native source dimensions before calling real `applyConstraints`.
It exposes selected maximum resolution/rate with the source aspect ratio and
leaves subsequent consumer constraints functional. An initial experiment that
waited only for `writer.write` failed with `OverconstrainedError`; Chromium had
not yet published the native source format. The bounded readiness observation
fixes that race. No synthetic replacement of `getConstraints` was introduced.
Initial aspect-derived limits are not reapplied on resize, avoiding overwriting
provider downscaling; a later aspect-ratio change may encounter the original
width ceiling. This limitation remains explicit.

Evidence:

- `artifacts/track-constraints-3/result.json`:10 passing checks in actual WebView2,
  using the production runtime with generated pixels. Both720p30/1440p60 expose
  real constraints, preserve a640x360 source without upscaling, and allow later
  native320x180/15 constraints and observed downscaled delivery.
- `track-fps-diagnostic-1` and `track-fps-startup-1`: focused generated-track
  observations reach approximately60FPS after max60, including when initial
  native settings report30FPS. These are not WGC/Discord delivery tests.
- `native-track-readiness-tests-2.log`:41 passing transport tests.
- `stream-diagnostics-tests-1.log`:108 passing provider/command tests.
- `stream-details-backend-tests-1.log`:18 passing backend tests.
- `share-details-tests-2.log`:336 widget checks, including distinct captured,
  encoded and advertised dimensions, unavailable/malformed results, and late
  results after account/call/session/capture replacement or background transition.
  The first test run was stopped because the test awaited lifecycle draining
  before releasing its deliberately cancellation-ignoring fake read; the corrected
  test starts teardown, releases the read, then awaits completion.

Adapter14 adds a bounded, on-demand numeric-only `streamDiagnostics` read. The
widget's existing menu exposes **Stream details** for an owned outgoing stream;
it uses SDK latest-operation/lifecycle handling and domain capture ownership,
with no extra polling or author-managed generation counter. Failed provider
feature detection returns unavailable rather than guessing dimensions. Neither
the correction nor these diagnostics have been installed in the live candidate.
The0.0.8 voice-pin package previously staged does not include the final diagnostics.
A fresh host publication and package are required for the next handoff.

| Consumer / owner | Applicability |
| --- | --- |
| Host native capture runtime, embedded by `OverlayFrontend.WinUI.csproj` | Corrected generic track readiness/constraints; real WebView test passes |
| Standalone WebView prototype and track-retirement fixture | Share the same runtime; no provider-specific policy added to host |
| Discord shared adapter (Vesktop comparison and WebView widget) | Provider stats/metadata normalization stays in adapter; unsupported engines fail closed |
| Discord WebView widget | Existing SDK operation API adopted for on-demand read; domain ticket/account/call checks retained |
| Other shipping widgets, first-party widgets, templates | No other native-media runtime consumer found; no migration needed |

Remaining: remote actual encoded/decoded dimensions, advertised label after the
correction, and native voice-pin interaction. Do not claim the label is fixed or
1440p60 is delivered to Discord from these local tests. Live PID12876 and its
capture worker57472 remained responsive and untouched at the final check.

### Prepared handoff and CI policy coverage (2026-10-27)

The fresh, trimmed production frontend is
`artifacts/discord-webview/production-candidate-7-qualified`. Its publication
verifier confirms302 local managed references and async cleanup; five native
input hashes match candidate6. It carries the corrected embedded native-media
runtime. Source/provenance is in `production-candidate-7-provenance.json`.
The initial build command incorrectly set `PublishTrimmed` globally, enabling
trim analysis in referenced libraries. That command failed; the successful
publication uses the frontend's existing Release trimming configuration without
warning suppressions or unrelated library changes. Both build logs/binlogs remain.

The final0.0.8 package (adapter14) is built, validated and packed at
`artifacts/discord-webview/widget-0.0.8-quality-final/discord-0.0.8.wrwidget`;
SHA256 `96871b63aad476ee9d1e7ccd9ae4d537e0b24d4da78f42855bae30d48df6a91a`.
It includes voice-pin styling and on-demand Stream details. Neither the package
nor the frontend has been applied or launched. The active user test remains on
candidate6/0.0.7. A question is pending to end that test before foreground native
fixtures, then update via the existing isolated catalog/profile and reviewed
package-update flow. Do not silently replace the package while its worker is live.

Native policy CI is now enrolled before the expensive frontend publication:
`web-capture-policy-build`, `web-capture-quality-policy`, and
`web-capture-audio-policy`. Selecting either probe resolves the single shared
build prerequisite. `Build-Native.ps1 -PolicyOnly` builds only policy probes;
`CaptureQualityProbe --policy-only` checks all twelve presets, geometry bounds
and no-upscaling without a timing benchmark. The audio probe exercises production
queue bounds, age eviction, recovery, exact timestamp preservation and QPC
conversion. No media devices, captured application, WebView, or user profile is
opened. The original local pacing benchmark remains available separately.

The runner self-test and all three actual bounded verification steps pass in
10.42 seconds. Evidence:
`artifacts/discord-webview/capture-policy-ci-evidence/20261027T073619Z-4c4d1a22/verification-result.json`
and `capture-policy-runner-selftest-1.log`. This qualifies local enrollment and
execution, not GitHub CI or end-to-end audio/video delivery. The checks cover the
generic host capture policies shared by Discord and future native web-capture
consumers; recording and unrelated widgets are unchanged.

The complete managed Discord verification group now also passes through
`Verify.ps1 -Lane managed -ManagedShard discord`:10 steps in19.43 seconds, with
repository state stable throughout. This includes the retained Vesktop comparison
widget, current336-check WebView widget suite, shared provider adapter/invocation,
activity discovery, webpack discovery, commands, native-media transport,
session-backend and incoming receiver tests. Result:
`artifacts/discord-webview/discord-final-group/20261027T074018Z-980b99d4/verification-result.json`.
No window, user profile, device or live call was opened by this group. The
foreground native fixtures and updated live candidate remain pending the user's
answer to whether the current stream test has finished; no answer is approval.

## Historical checkpoint: candidate5 and earlier live handoffs

The following chronology preserves earlier evidence and superseded blockers;
its PIDs, package versions and pending statements are not the current handoff.

At this checkpoint: `artifacts/discord-webview/production-candidate-5` (unchanged
qualified trimmed managed frontend from candidate4, validation disabled, plus
the qualified live-audio native fix), now running as PID **84600** with isolated
widget **0.0.5**, adapter build **11**. The latest launch used the reviewed local
update flow with the explicit isolated settings root, preserved matching settings
and permission decisions, and removed the temporary CDP/session-trace flags.
The native Streams view exposes the new audio controls. Physical audio and the
patched hidden-page transition remain with the user. Earlier handoff history: the
reviewed `wrail update --file` path applied it to the existing isolated catalog
with the explicit `artifacts/discord-live-e16ff8bd-profile` settings root; matching
permission/settings continuity was preserved. The old isolated candidate exited
gracefully. Production PID 56728 was left running at that point. After confirming
no other WidgetRail host was running, candidate 67604 exited gracefully and the
same isolated candidate was relaunched as PID 81500 with controller input enabled.
Fresh native platform hash matches the qualified build; publication verification
passes for 302 local managed references and asynchronous cleanup. Native file
hashes and staging provenance are in
`artifacts/discord-webview/production-candidate-5-provenance.json`.

Read-only live observation confirms the updated candidate retained sign-in and
renders actual loaded friend activity, avatars and a contextually eligible Join
action on Home. It starts outside a voice channel. No friend action, microphone
grant, call join or outgoing share was invoked in this check; personal live
screenshots were not saved for publication. This verifies data availability and
rendering, not live action execution. The final widget payload passes 42 native
navigation checks and the native keyboard displays channel completions with guild
descriptions. Synthetic-only Home/Streams images are saved under
`artifacts/discord-webview/ui-final-current/showcase`; evidence and limits are in
`artifacts/discord-webview/ui-final-current/final-ui-evidence.json`.

Candidate 85224 was then verified outside a voice call and exited gracefully
before candidate 67604 was launched with the qualified capture correction.
Installed production PID 56728 is no longer present. Neither this task's update
commands nor the final UI runner stopped it; the reason is not established and
it was not automatically restarted. Do not infer controller ownership from the
earlier PID record; recheck when physical testing resumes.

### Historical live handoff

PID 81500 is the sole observed WidgetRail host. Native channel search was opened,
`Test server` was entered/committed, and the approved `Lobby` row joined the test
call. The existing permission grant satisfied the request without a new prompt;
the assistant did not approve a privacy prompt. The native header shows Lobby,
two participants and **Unmute**, confirming the self-mute state remains on.
The authorized test peer's incoming stream opened in `discord.stream`; actual
video was visible. After hiding the overlay for 20 seconds and reopening it,
the call and selected pin remained present without a receiver error. This does
not establish received audio or physical controller behavior.

The outgoing **Share application** chooser was opened at 720p30, then closed
while the task awaited user input. The live widget displayed the generic capture
unavailable notice; no native capture terminal entry was found for this attempt.
The broker has a 90-second setup deadline, which surfaces `session_timeout`; the
widget previously mislabelled that code as unavailable. Timeout is consistent
with this observation, but the specific live error code was not retained.
No source/audio choice or outgoing broadcast was initiated by the assistant.
The call remains muted and the incoming pin remains selected. The user can reopen
Share game and choose a safe window when ready. Keep this running candidate intact.

Source-only follow-up **0.0.4** now distinguishes setup timeout and cancellation
from access/support failures, without retrying or sending provider commands.
Focused injected timeout/cancellation tests pass within the **170/170** widget
suite (`artifacts/discord-webview/setup-recovery.log`). The package is built,
validated, packed and reviewed at
`artifacts/discord-webview/widget-0.0.4-setup-recovery/discord-0.0.4.wrwidget`;
review SHA-256 is `da9ae616a3e9b364aa1acac2accb09f93f72bed853eb74742dd8570cfb7b609d`.
It is deliberately not applied while the current live call/pin test is pending.
This bounded message correction uses existing SDK error codes; host cancellation
and exact-ticket cleanup remain unchanged. No additional benchmarks were run.

## Historical completion audit before the unified redesign

Blocked audit: the same required user handoff has remained outstanding across
three consecutive goal turns: controller-enabled live setup, the subsequent
timeout-message correction, and a fresh read-only recheck. PID 81500 is still
alive in Lobby, muted, with the incoming pin selected and no outgoing broadcast.
No receiving-account audio or physical controller acceptance has been supplied.
The remaining user-owned source choice cannot be substituted with synthetic
capture, nor can local media tests prove remote audio. Independent code work and
the staged 0.0.4 correction are ready; applying it now would interrupt the live
handoff. Resume when the user chooses test content and reports the live results,
then apply the staged update after that call is finished and reassess completion.

This table records that earlier requirement disposition. The current handoff and
remaining-gates table at the top supersede its versions and pending statements.

| Requirement | Evidence and disposition |
|---|---|
| Native SDK widget, host WebView2, no external client/bridge dependency | Implemented in `samples/DiscordWebViewWidget`; isolated 0.0.3 runs in the normal trimmed shell. The normal WidgetRail runtime bridge remains part of the framework; no Vesktop/CDP bridge is used. Comparison prototypes remain in source. |
| Setup and sign-in | User completed QR sign-in earlier; reviewed 0.0.3 update retained that host profile and actual live Home data. New user sign-in remains user-owned. |
| Voice channels, search, mute/deafen/leave, participants | Implemented; 155 managed checks and final 42-check native fixture pass. Channel completions also displayed in the actual native keyboard. Fresh physical-controller/voice acceptance of 0.0.3 remains open. |
| Friend activity and fast contextual actions | Read-only live Home shows loaded activities/avatars/Join eligibility. Provider/action ownership, Unicode boundaries and bounded prioritization pass synthetic tests. No live friend was joined; real action acceptance remains open. |
| Direct stream/voice pins and replacement behavior | Shared SDK/protocol/host pin contract implemented; prior real receiver/voice-pin success plus host pin and final native navigation fixtures. Per-layout dimensions and host foreground ownership are retained. Fresh real incoming stream and physical pin interaction remain open. |
| Application/game sharing and application audio | Host chooser, opaque targets/tickets, permission and source-loss handling are implemented. Current native consent/lifetime 40/40 and capture 20/20 pass. Earlier integrated picture worked; remote received audio was never confirmed. |
| Supported quality and idle/motion pacing | Four baseline presets implemented; approximate 4-FPS idle and selected local pacing measured. Browser source 720p30 sustained 29.72 FPS with audio. Higher account/server tiers and actual remote encoder negotiation are explicitly limited; do not imply premium support. |
| Concurrent incoming/outgoing and hidden continuity | Earlier integrated picture worked concurrently with incoming pin. Current synthetic background test crosses the 15-second memory-policy transition. Current real call/stream continuity and received audio still need user testing. |
| Cancellation, stale actions and account/session changes | Captured domain intents, SDK attempt generation, receiver leases and host document authority are covered by the 155-check managed suite and native ownership/lifetime fixtures. No mutation replay or silent source substitution. |
| Focus, navigation, themes and dimensions | Final 42-check Discord run and 35-check generic native focus run pass. Default/minimum, light and host high-contrast evidence is recorded. Physical device input is not inferred from these fixtures. |
| Native teardown containment and resource costs | Production worker isolation, bounded stop/kill and exact-ticket retry implemented and tested. Local CPU/GPU/private-memory scopes are recorded; they are not remote Discord benchmarks or leak proofs. Broad benchmarking is finished. |
| Unexpected outgoing stop | The reproduced PCM-overflow failure is fixed and qualified: video/audio recover after 1.5 seconds, while the 4-second watchdog still stops. Candidate 5 carries it. The original incident's cause and remote-audio acceptance remain unproven. |
| Packaging, compatibility and documentation | 0.0.3 validated/packed/applied through reviewed update with explicit isolated settings root. Trimmed production publication and 302-reference/cleanup verification pass; SDK additive API baseline, native protocol parity, CI shard enrollment and consumer matrices are recorded. No merge/push/release. |
| Remaining physical evidence | Microphone/device behavior, received application audio, a selected real game/window, concurrent streams, actual controller navigation and foreground return. User selects real broadcast content and grants privacy permissions. |

Completion is unproven. Keep the full goal active until the open requirements are
verified; the polished UI or green synthetic checks alone are not completion.

## Active goal expansion: polished Discord experience (user authorized 2026-10-26)

Continue the existing full-integration goal while the user is away. Do not wait
for physical checks that can be collected for their return. Preserve the isolated
candidate/profile and all earlier safety, compatibility and no-release boundaries.
The goal tool cannot edit an unfinished goal's objective; this section is the
authoritative additional scope for the active goal, not a completion or pause.

- Diagnose and correct the unexpected outgoing-share termination with evidence.
  Keep source selection/microphone consent user-owned. Use synthetic sources for
  unattended testing; do not restart a real broadcast automatically.
- Replace the plain prototype presentation with a polished native WidgetRail UI.
  Reuse NavigationShell, semantic glyphs, collection/action rows, proper surfaces,
  typography and controller hints; respect themes, scaling and minimum sizes.
  Avoid decorative empty panels, duplicate status text, wall-to-wall buttons and
  focus loss during updates. Preserve the working QR sign-in view.
- Make the common join, leave, mute/deafen, watch/pin and share/stop flows fast.
  Prioritize contextually useful actions, clear current-call state and predictable
  return focus. Keep source/audio/quality consent intact. Do not add numerous
  focus-jump shortcuts or unsolicited remote actions.
- Add an activity-focused Home view, if provider data supports it, similar in
  purpose to Discord's Active Now panel. Show bounded friend activity/voice/stream
  data and only actions that can be verified for the current account and target.
  Friend discovery is read-only. No unsolicited messages, calls, invites or joins.
  Missing/changed private APIs get a clear, quiet fallback; never invent friends,
  infer permissions from labels or bypass provider eligibility.
- Keep Discord-specific presence/relationships/action policy in the adapter/widget;
  framework owns presentation, focus, collections, host permission and lifecycle.
  Use account-scoped identities and reject stale activities/actions after account,
  channel or presence changes. Avoid refetching the full channel/friend catalog on
  each render or turning a cosmetic revision into a broad framework rewrite.
- Validate populated, empty, offline, loading, error and changed-account views with
  fixtures; test native focus, sizing and light/dark/contrast readability, plus
  normal and hidden media lifetimes. Document actual evidence and limits.
- Maintain a short physical-acceptance checklist for the user's return: receiving
  account audio, real microphone/device behavior, physical controller navigation,
  actual game sharing/quality and live activity actions. These checks are pending,
  not implicitly passed by synthetic tests. No merge, push or release yet.

### Ownership decisions during the UI pass

Native indexed-row Watch exposed a pinned-state defect: after receiver negotiation
failed, the main view showed an error but the pin still said Connecting. Its fix
belongs to the widget's exact account/call/stream selection state. The existing
`WidgetOperations` owns cancellation/currentness and busy state; no author counter
is needed. `WidgetResource` is a data-loading abstraction without asynchronous
lease/result disposal, so forcing the relay into it would weaken teardown. Retain
the existing operation/lease design and add a selection-bound failure presentation.
This is a justified narrower change, not a new shared abstraction.

Avatars use existing `UI.Image` HTTPS delivery and host caching; provider code only
produces validated fixed Discord CDN paths at size 64. Missing avatar metadata
uses the semantic person fallback. No widget-owned download/cache/generation loop
was added. Author-selectable load-failure fallback is not exposed by ImageElement;
the existing host error presentation remains in use rather than introducing manual
image-state plumbing in this widget.

## Current integration status

### Current contract and receiver checks (2026-10-26)

Latest widget checks: **155/155 passed**, now including loaded-channel keyboard
suggestions and their account/backend/catalog/editor-lifetime boundaries:
`artifacts/discord-webview/widget-final.log`. The expanded generic native focus
fixture passes **35/35**, including partial-subtree reparenting with retained
siblings: `artifacts/discord-webview/ui-navigation/generic5/result.json`.
It did not reproduce the inferred sibling-fallback case, so production focus
policy was not expanded speculatively. Candidate 10's corrected capture fixture
passes **20/20**, including disposed/missing/inactive runtime retirement and
document reuse: `artifacts/discord-webview/capture-current-2/result.json`.
Candidate 9's consent/chooser/lifetime fixture passes **40/40**:
`artifacts/discord-webview/consent-current-1/result.json`.

The later domain-intent correction passes **138/138 managed checks**:
`artifacts/discord-webview/domain-intent-current.log`. In addition to receiver
health, these cover stale account/call/backend actions, asynchronous receiver
replacement, and Cancel surviving chooser return for the same SDK-assigned
capture attempt while rejecting another attempt. Provider/adapter/invocation
JavaScript tests pass **60/60** with adapter contract build 9. The next isolated
widget package is version 0.0.3; it has not yet replaced the live 0.0.2 package.

The shared presenter correction passes **32/32 generic native checks**, including
two consecutive publications, actual host focus before/after the final publication,
directional cancellation, removed targets, newer group requests, changed scopes,
and replaced workers. Evidence:
`artifacts/discord-webview/ui-navigation/generic4/result.json`. The Discord fixture
separately passes **42/42** controller-shell checks with actual Windows foreground:
`artifacts/discord-webview/ui-navigation/run8/result.json`. That widget payload
predates the later action-ID changes, so its current-package rerun remains due.
The consumer applicability matrix is
`artifacts/discord-webview/ui-navigation/consumer-matrix.json`; no widget needs
its own focus restoration counter or workaround.

The earlier combined selection passed **111 managed widget checks**, including terminal
receiver health, exact-lease cleanup, delayed failure after replacement, and pin
revocation. A disconnected receiver now removes its stale view and offers an
explicit retry without replaying Watch, leaving voice or stopping a broadcast.
The health observer uses the existing SDK session message stream, Completion,
and a single bounded operation lane. The shared receiver's health events are
opt-in so the retained Vesktop comparison does not accumulate unread events.

That 13-step managed selection passed, including SDK compatibility 17/17,
scenario 9/9, ticker 5/5, styling 38/38, both Discord managed suites and seven
provider/runtime JavaScript suites. Evidence:
`artifacts/verification/20261026T222114Z-8787403a/verification-result.json`.
The separate receiver JavaScript suite passes 10/10 and is now enrolled in the
Discord CI shard. Its evidence is
`artifacts/discord-webview/receiver-current-js.log`.

The reviewed SDK API baseline adds only the 19 intended pin, microphone and
native web-capture symbols; no public symbols were removed or changed. The first
compatibility run caught the omitted additive baseline update. The native
generated protocol header was also regenerated and verified at version 104;
final candidate qualification must use a newly built native platform DLL.

These checks establish contracts and synthetic lifecycle behavior. They do not
establish actual friend activity availability, other-account audio reception,
or a fix for the unexpected live outgoing-worker termination. Native controller
qualification is also still in progress: a foreground-qualified fixture exposed
pending focus restoration being overwritten by a second rapid publication.
The correction belongs to the presenter and must preserve newer input and scope
ownership; it is being tested independently of the Discord widget.

### Remaining outgoing-stop investigation

The bounded fault-injection run now establishes a concrete failure mode:
`artifacts/discord-webview/stall-fault-evidence-1/result.json`. A 1,500 ms renderer
stall survives without audio but ends capture with audio (`0x8007006F`, native
buffer overflow). Host dispatcher gaps stayed below 65 ms. A 4,000 ms video-only
stall hits the existing browser heartbeat watchdog; the audio case overflows
earlier. All terminated workers exited and final cleanup was confirmed. This is
controlled failure reproduction, not a repeat of steady-state benchmarking, and
does not establish that the original Discord incident had the same trigger.

The correction is scoped to the live capture worker's downstream PCM queue:
retain the 192,000-byte hard cap, evict old whole packets under pressure, and
expire queued packets beyond an explicit 200 ms live transport latency budget.
Retained packets keep their QPC timestamps and order; an already-published mapping
remains immutable until its exact credit returns. Invalid packets remain fatal.
The native/page watchdogs and shared WASAPI/recording behavior stay unchanged.
No ABI change is needed; dropped PCM counters remain internal to focused tests.
Qualification must show normal delivery, bounds, credit integrity and actual
post-stall audio/video resumption, while the four-second watchdog still stops.
The correction is qualified by `stall-recovery-evidence-1/result.json` and is now
in running candidate 5. The short-stall audio case resumed 30 video frames and
48,480 audio frames; both 4-second cases still stopped through the unchanged
watchdog with confirmed worker exit. This closes the reproduced fault, not the
unexplained original incident or physical remote-audio acceptance.

User steering: further broad capture benchmarking is deferred. Finish the
current Browser-source measurement, then prioritize the UI/candidate and checks
for specific changed behavior. Do not rerun sustained/static performance suites
without a new failure or a change to the measured pipeline that needs evidence.

The completed Browser-source/application-audio run on candidate 9 sustained
29.72 decoded FPS over 356 measured seconds, with zero additional video drops
and 17,083,680 received audio samples; worker shutdown completed. Evidence:
`artifacts/discord-webview/browser-sustained-current-1/result.json`. This is a
generated browser canvas/tone through the production worker and local WebRTC,
not the Discord service or another-account audio acceptance. Its resource scope
and counters are recorded in the artifact; no new performance claim is required
for the UI changes.

No retained log identifies the original live termination. Current diagnostics
record bounded reason codes, HRESULTs, packet counts and confirmed worker exit
without account, window-title or page-content data. Do not infer source closure
from `source_closed` alone: the native validity predicate also rejects protected
or no-longer-queryable windows.

The next controlled experiments are a temporary synthetic renderer stall with
audio off and on, generated audio stop/start, and an actually dead fixture
renderer. The host currently times out a page heartbeat after three seconds;
the bounded audio queue can overflow earlier if acknowledgements stall. The
shared process-loopback reader treats non-initial WASAPI discontinuity and
timestamp-error flags as fatal. These are source-grounded hypotheses, not a
reproduction of the reported Discord failure. Do not relax watchdogs, enlarge
queues without bounds, or change Game Capture's shared audio policy merely to
make a probe pass.

A separate confirmed source gap is under correction: a successful script call
returning false/null from `keep(epoch)` must not renew native capture. Validate
that the exact runtime is still active and stop with a clear reason when it is
gone. This corrects unnecessary capture after runtime retirement; it does not
explain the unexpected early stop. Ordinary webapps and Game Capture do not use
this opted-in WebView capture transport.

### Additional UI/provider review findings

- `DiscordWebViewWidget.Views.cs` / `DiscordWebViewWidget.cs`: ordinary call
  buttons use stable action strings but reconstruct the latest call and inverse
  toggle state when handling them. Source review shows delayed input could affect
  a different account/call or invert the displayed intent. Bind the widget's
  immutable domain target and desired state, keeping node IDs stable. Indexed
  collection actions already have a supported captured-context mechanism; do not
  reimplement that mechanism or add render counters for these domain identities.
- `activity-provider.js` and `adapter.js`: slicing labels at 120 UTF-16 code units
  can split an emoji. A synthetic provider label with 119 ASCII characters plus
  an emoji produced an unmatched surrogate rejected by the managed JSON consumer.
  Correct the provider boundary and test scalar-safe limits.
- `activity-provider.js`: limiting by sorted friend ID before prioritizing useful
  actions omits joinable friends after 50 informational rows. A provider fixture
  reproduced that omission. Rank the bounded candidates before limiting output,
  retain deterministic identity, and keep the protocol's 50-row output limit.

These corrections are in progress. Store signatures were compared with Vencord
commit `443688b5366a4d43e9868519f48a4544cd7520d4`; that comparison supports the API
assumptions but does not replace a read-only check in the actual loaded client.

This section supersedes the historical feasibility-stage status below. The actual
`DiscordWebViewWidget` is now installed in its own catalog/profile, using host-owned
backend sessions, microphone consent, native capture and direct pin requests.
The standalone WebView2 and Vesktop prototypes remain comparison paths.

- Isolated widget 0.0.2 displays Discord's QR sign-in in a 1040 x 740 account view;
  native controls retain their 760 x 660 preferred size. The user completed sign-in
  and session-only microphone approval themselves.
- Native search found Test server / Lobby. Join and mute worked. Voice controls
  opened in a pin, and Watch switched the same widget to its incoming-stream layout.
  Incoming video was visibly rendered. No physical-controller acceptance is claimed.
- The user selected Chrome with application audio in the native source chooser.
  The integrated widget reports sharing at 720p30 while incoming video remains pinned.
  The user confirmed other-account picture reception; audio was not tested.
- Native consent/chooser/document checks passed 40/40 on integrated candidate 3:
  `artifacts/discord-webview/microphone-runs/5c902f492d104e63ab324de9b21bc387/result.json`.
- Actual BrowserSurface capture checks passed 13/13 on integrated candidate 4 with
  production native workers: 29.8576 FPS over 8.0047 s, 435,360 generated application
  audio samples and the expected nonzero tone. Hidden capture, source loss,
  navigation, old-ticket isolation, and browser closure during unconfirmed native
  stop were covered: `artifacts/cap-c56bce41/result.json`.
- A trimmed production publication succeeded after replacing reflection-based
  JSON serialization with the existing source-generated browser context. The later
  transport Dispose refinement also passed a fresh trimmed production publication
  (`artifacts/discord-webview/production-publish-3.log`). Integrated candidate 5 then
  passed 40/40 consent/lifecycle checks and 13/13 actual capture checks at 29.715 FPS
  with generated application audio. Evidence:
  `artifacts/discord-webview/microphone-runs/2381f01829d4477692842f0015c1d5e8/result.json`
  and `artifacts/cap-202025c4/result.json`.
- Capture quality currently supports 480p/720p at 15/30 FPS. This is an implementation
  baseline, not a WebView2 ceiling. Higher account-supported modes require provider
  capability/entitlement checks, coordinated bounded-buffer changes and measured
  performance; they are not yet implemented or validated. At equal aspect ratio,
  1080p60 processes 4.5 times the pixels per second of 720p30.

Remaining acceptance includes receiver audio, live hidden-call/stream continuity,
recovery and controller-shell interaction, sustained CPU/GPU/memory measurements,
final source qualification, and SDK documentation/verification enrollment. The
installed production overlay and its data remain separate from this candidate.

Live observation after picture confirmation: local native capture ended and the
widget reported that the selected application was no longer captured, while
Discord still reported an active stream. The user confirmed this was unexpected.
The old capture worker exited. Its exact termination reason was not in the durable
log; no cause is established. Bounded terminal diagnostics now record only fixed
reason codes, numeric results/counts, preset and worker-exit state, with no source
titles, account data, URLs or frame content. New-candidate qualification is pending.

A generated 100-second motion/application-audio run on integrated candidate 5 did
not reproduce the failure: over the 92.80-second post-warmup interval it decoded
29.76 FPS, with zero additional dropped video frames and 4,463,520 audio samples
delivered to generated browser tracks. The probe process tree used approximately
1.95% machine-normalized CPU; summed private bytes changed from 449 to 471 MiB.
This includes local WebRTC encoding and decoding and excludes the generated source;
it is neither a Discord measurement nor proof of long-term memory stability. Raw
samples: `artifacts/cap-measure-34bf6c93/samples.json`. GPU usage was not measured.
The new `Measure-WebCapture.ps1` runner preserves actual production native binaries
and supports generated static/motion and optional process audio.

The matching generated static-source run also completed cleanly. Its 96.70-second
post-warmup interval decoded 3.96 FPS with zero additional dropped frames, about
0.45% machine-normalized CPU and summed private bytes from 431 to 440 MiB. This
supports the accepted low idle cadence on this machine; it is not an idle memory
plateau claim. Evidence: `artifacts/cap-measure-8d065448/samples.json`.

Review found the existing integrated hidden check stopped before the 15-second
memory-hint transition. It now measures hidden video/audio for 30 seconds with the
normal policy enabled. This is a test-coverage correction, not evidence that the
memory hint caused the live stop. Provider/source-loss reconciliation is also under
review: the widget currently discards the terminal reason and can show Discord's
separate active-stream state after local capture has stopped.

### Continued qualification and UI work

Broader SDK verification initially passed 176/177 checks. The failing return-focus
assertion expected the parent indexed snapshot's version to remain 101, despite
`WidgetView.CreateSnapshot` intentionally advertising CurrentVersion for deferred
templates (the same implementation/assertion mismatch is present at HEAD, whose
CurrentVersion is already 102). The test now separately verifies advertised SDK
support and the feature's unchanged required version 101. Production focus logic
was not changed. The focused test and full SDK executable then passed 1/1 and
177/177. Evidence: `artifacts/discord-webview/focus-version-*.binlog` and
`artifacts/discord-webview/sdk-all-after-version-fix.log`. The remaining SDK-shard
compatibility/scenario/style checks still need their run; the interrupted shard
is not described as wholly green.

UI source now provides compact stationary call controls, Home activity rows,
validated CDN avatars with semantic absent-avatar fallback, truthful empty/search
states, and a 400 x 180 voice pin. Synthetic native checks measured Home's list at
104 DIP and Channels at 86.4 DIP at the exact 420 x 360 widget minimum, retaining
Search/Clear and ordinary bounded collection scrolling. Normal layouts do not add
an outer scroll; an error notice uses a bounded recovery layout. Default, light and
host High contrast presentations were reviewed. Native indexed-row Watch opened
the real host pin; fake receiver failure remains explicitly synthetic. Evidence:
`artifacts/discord-webview/ui-final/native-ui-evidence.json` and the safe generated
pin image `artifacts/discord-webview/ui-final-light/logs/voice-pin.png`.

The latest widget managed suite passed 98 checks, including activity ordering,
destination/offline labels, avatars, pending-command admission, dedicated activity
Join/Watch, and failed receiver pin presentation. The last pin-label correction
still needs its native assertion. Clickable guide bumpers were exercised, but
keyboard navigation was inconclusive and physical-controller evidence is absent.
A fixture-only `--validate-discord-navigation` scenario is authored to exercise
the actual native controller dispatch, focus, Back, empty/populated sections and
indexed pin flow. It is pending a combined frontend publication.

Controller-scenario qualification is in progress. Earlier runs traversed populated
pages, restored Account's exact origin on B, and opened the indexed Watch pin, but
lost focus when the error layout changed. That is not yet an established production
defect: `--shell-no-controller` has no input owner, so input?.IsForeground was an
invalid diagnostic; direct GetForegroundWindow showed the test was not foreground
even before its first action. The fixture now establishes its own native foreground
precondition before rerunning unchanged production focus policy. No widget-side
focus counters or general null-focus recovery workaround have been added.

The retained Vesktop comparison path had a source-confirmed adapter version
mismatch. A single `adapter-contract.json` now supplies the provider, client and
staging build number (currently 8). A separate invocation envelope distinguishes
installation from provider errors, rejects newer providers without replacement,
and never replays a mutation after a lost response. Incoming watches require
valid ownership data; existing user subscriptions stay user-owned, and owned
subscriptions retain their exact close action across same-stream relay replacement.
A failed replacement peer allocation preserves the previous relay. These additions
are covered by 29 provider and 10 invocation JS checks, with dedicated CI entries.

Candidate 8 includes the generator-retirement runtime and initial generated browser
source. The browser-source probe timed out before readiness/capture; diagnostics
showed an empty about:blank document, so it is not a reproduction of the live
Discord stop. Initialization-stage diagnostics and fixture correction are underway
before candidate 9. This generated canvas/tone source uses a fresh profile, blocks
network/device access and selects only the runner-owned PID/creation time. All
unattended capture runners now require that exact identity, rather than a title.

A read-only 32.77-second sample of the long-running isolated live candidate 4
process tree (after the outgoing failure and explicit Stop, with the existing
call/incoming pin) measured 0.67% total-machine CPU and 1,387–1,403 MiB summed
private committed memory. The final breakdown was about 900 MiB across eight
WebView2 processes, 362 MiB frontend, 91 MiB bridge and 48 MiB widget. This is a
development candidate with the full Discord web client and receiver, not a
minimal native chat runtime. It does not prove a leak or a plateau; it makes the
current memory cost explicit. Summed working set was 1,324 MiB and may double-count
shared pages. No media FPS or received audio was measured in this sample.
Evidence: `artifacts/discord-live-resource-2353ee93/result.json`. Further
optimization must preserve the working call/pin lifecycle and be measured.

Real WebView generator retirement exposed a generic host bug: direct track.stop()
does not raise ended on that track and leaves its clones live. Closing/aborting
the generator's writer ends originals and clones with one ended event each. The
synthetic browser probe covers video/audio, readers, 16 pending writes and repeated
retirement; source termination settled within 0.2 ms in those comparisons. The
native-media runtime now revokes admission and aborts the captured old writers,
with direct track-stop only as a one-second bounded failure fallback. No synthetic
events or provider-wide Stop commands are used. Replacement isolation and
rejected/stuck abort are covered by 23 JS checks. Baseline/fixed native evidence:
`artifacts/discord-webview/track-retirement-1/result.json` and
`artifacts/discord-webview/track-retirement-2/result.json`. Embedding the new runtime
in the next frontend and checking Discord's own end reaction remain pending.

The six-minute generated motion/application-audio run completed on integrated
candidate 7 without reproducing the unexpected live stop. Its 356.86-second
post-warmup interval decoded 29.68 FPS, with zero additional dropped video frames
and 17,123,520 audio samples delivered. All 10 process incarnations matched for CPU
accounting: 1.67% total-machine CPU across 16 logical processors. Summed private
committed memory was 458.5 MiB at the start and 474.0 MiB at the end, with a sampled
479.4 MiB peak. The busiest measured GPU engine peaked at 1.03%; raw engine deltas
are used because formatted integer counters hid small loads. DWM/system-attributed
work and the generated source are excluded. This is the local encoder+decoder
fixture, not Task Manager's private working set or a Discord process measurement.
Worker exit was confirmed. Evidence: `artifacts/cap-measure-94e3c2d3/result.json`
and its raw `samples.json`.

Actual shell/owner startup coverage passed 13/13 with candidate 7 and the current
bridge fixture. The four new cases exercise duplicate Open, delayed registration
after Close, disposal before failed registration, and capacity release against
the actual owner handler. Existing worker replacement, messaging and native-view
reattachment still pass. Evidence: `artifacts/hosted-owner-1/result.json`.

Integrated candidate 6 passed 40/40 native consent/chooser/lifecycle checks and the
expanded 13/13 actual-capture suite. The hidden 30-second segment delivered 29.73
FPS and 1,440,480 generated application-audio samples after crossing the memory
policy delay. Evidence: `artifacts/cap-78dd5e3a/result.json` and
`artifacts/discord-webview/microphone-runs/507672becda644659231c292ece49a87/result.json`.
Capture terminal logs now distinguish cancellation, navigation and source close
with exact worker-exit state. These results do not explain the original live stop.

Downstream widget recovery now preserves a capture-interruption record and maps
bounded native reasons to useful recovery text while retaining Discord's reported
broadcast state. Automatic provider Stop is intentionally withheld: current stream
keys can be reused across restarts, so a stale capture ticket must not stop a newer
broadcast. An adapter-issued exact broadcast lease is needed before automatic
cleanup. The 69 widget checks include 16 new interruption/late-cleanup assertions.
During live observation, the explicit Stop sharing action successfully removed the
remaining provider stream; the call and incoming pin stayed available.

The new Discord CI shard passed 7/7 steps (65 managed checks before the later 16
recovery assertions, 63 JavaScript checks); documentation passed 240 Markdown files
and the verification-runner self-test passed. Evidence:
`artifacts/verification/20261026T202313Z-58c8b43e/verification-result.json`.

Startup review found host-owned close-during-profile-registration and capacity
reservation races. A keyed startup reservation plus five delayed/concurrent
regressions passed; native owner integration and exact failed-start cleanup remain
under review. These are independent startup defects, not the diagnosed cause of
the already-running stream ending.

For Home, read-only feature-probed friend/presence stores are preferred over
`NowPlayingViewStore`, whose refresh depends on Discord's own mounted UI. Planned
data is bounded to 50 account-scoped friend rows, excludes blocked/ignored users,
custom statuses, activity secrets and arbitrary URLs, and distinguishes unavailable
from empty. Join revalidates friendship, destination and provider permissions;
Watch uses the existing exact-stream/current-call path. Provider data and polished
native views are being implemented in parallel. No new friend action or physical
controller acceptance has been performed.

### Higher-quality sharing assessment

The current selector controls capture bounds and generated-track settings; it does
not independently verify Discord's encoder resolution or FPS. Increasing native
capture alone is insufficient. This is a source-confirmed integration gap, not a
runtime claim that the current baseline is incorrectly encoded.

Discord's [Go Live guidance](https://support.discord.com/hc/en-us/articles/360040816151-Go-Live-and-Screen-Share)
describes 720p30 for all users and higher subscription tiers; its normal browser UI
does not offer quality adjustment. [Server boosts](https://support.discord.com/hc/en-us/articles/360028038352-Server-Boosting-FAQ)
can also affect eligibility. Provider capability discovery must therefore be scoped
to the current account and destination channel/server, not a hardcoded Nitro flag.
Vesktop/Vencord demonstrate browser feasibility by changing Discord quality-manager
behavior, which is private and not a stable supported API.

Proposed ownership: the Discord adapter discovers supported provider modes and
applies the selected encoding mode; a provider-neutral capture request can narrow
the host's tested mode set. The host owns the chooser, allocation limits, pacing,
source identity and lifecycle. Missing provider APIs retain the baseline rather
than promising an unavailable premium mode. Add 1080p30, 720p60 and 1080p60 only
after measuring actual outbound and decoded video, CPU/GPU, dropped frames,
acknowledgement latency, memory and audio synchronization. Current GPU readback and
native copies make 1080p60 a performance investigation, not a dropdown-only change.
Evaluate the existing `detail` content hint for moving game content as part of this
work. Complexity: medium/high across provider compatibility and media performance;
not implemented in the current baseline.

## Historical standalone feasibility stage

The sections through the background-session foundation describe earlier evidence
and constraints at that stage; later integration sections supersede their pending
items. They do not imply that the integrated SDK/protocol remained unchanged.

## Objective

Replace the Vesktop dependency in the Discord-native experiment with an owned
WebView2 session. Preserve native voice controls, incoming video in a floating
receiver, and outgoing sharing through an application list plus an audio toggle.
Keep the working Vesktop prototype unchanged for comparison.

## Architecture gate

WebView2 ScreenCaptureStarting permits cancellation/deferral but does not accept
an application chosen by our own UI. The experiment therefore uses:

1. Native application catalogue and explicit source selection.
2. Windows Graphics Capture plus optional process-tree loopback audio.
3. Bounded WebView2 shared buffers carrying BGRA/PCM directly, outside widget JSON.
4. Generated MediaStream tracks supplied to Discord's getDisplayMedia request.
5. A provider-specific main-world adapter using feature-checked Discord modules.

No fake browser permissions, Chromium auto-select flags, screenshots-as-video,
recording files, credential export, or remote debugging port are used. The public
SDK/protocol is unchanged. In-process CDP is limited to this validation host.

Source identity checks bind HWND, PID, creation time and class, reject protected
sources and never silently select a replacement. Output is bounded to 2560x720
(to retain vertical detail on an ultrawide), with 30 fps for changing content and
a low refresh cadence for unchanged content. Audio is 48 kHz stereo PCM16 from the
selected process tree; no system-loopback fallback. Shared audio has a bounded
one-second queue and fails explicitly on overflow.

## Verified evidence

- C++ capture bridge builds with /W4 /WX. Multiple validation-enabled host builds
  and publications succeeded; unique binary logs are under ignored artifacts.
- The normal Release host also builds with validation disabled, zero warnings and
  zero errors. The prototype's implementations are excluded from that build.
- 41 JavaScript checks pass across the original adapter, web action authorization,
  generated-media lifecycle and module discovery.
- User signed into the separate WebView2 profile. No authentication was automated.
- Native voice search finds the designated Test server / Lobby. Native Join
  connected after replacing false-positive module matches with concrete APIs.
- The user started outgoing sharing through the native application/audio chooser.
  Discord reported sharing=true and the generated video frame count increased.
- User initially reported poor outgoing quality and spontaneous stopping. After
  first-frame metadata, resolution and lifecycle corrections, the user confirmed
  improved picture quality and that the stream stays running.
- Incoming receiver reported WebRTC connected and decoded hundreds of frames.
- Local WebRTC fixture received generated video and process-audio samples.
- Candidate 6 survived maximize, restore, minimize and restore while streaming
  audio. Over 7,000 video frames and 12 million audio samples were processed.
- Closing the fixture ended native capture with 0x80070578 (invalid window handle)
  and stopped the generated tracks. No alternative window was captured.
- Adaptive capture grew from 640x392 to 2560x720 and decoded at that new size
  without reconnecting. The stationary fixture was corrected to repaint on shrink.
- Microphone requests were recorded as allowed after the user's explicit session
  consent. Incoming and outgoing streams subsequently ran together.
- Reproduced the active timing loss: Sleep(2) scheduling yielded 21.393 fps in a
  standalone 30-fps pacing probe, versus 30.054 fps with a high-resolution timer.
  The local moving-pattern pipeline improved from 21.58 decoded fps to 29.79 fps
  (29.84 submitted), with zero drops during its 20-second measurement window.
- The updated Discord sender reached 30 fps; its 20-second YouTube sample ranged
  20-30 fps, averaging 27. YouTube's own Stats for nerds confirmed this particular
  source was 640x480 at 24 fps. This is not evidence of locked 30-fps source content.

## Corrections driven by implementation/testing

- Discord uses multiple webpack runtimes. Reading only the final runtime missed
  the main application. Discovery now inspects all observed loaded caches.
- Dynamic translation/service proxies manufactured arbitrary methods, producing
  false capability positives and no-op Join commands. Discovery excludes these
  objects and tests cover the failure. No arbitrary module factories are executed.
- Web voice selection uses the provider's concrete selectVoiceChannel action.
  Authorization and selected-channel validation remain provider-owned.
- Busy-control disabling transferred keyboard focus to microphone consent during
  a test. A subsequent Space accidentally enabled that session's microphone;
  no call was joined and the account stayed muted. The session was reset.
  Busy handlers now reject duplicate commands while retaining the focused control.
- The prototype denied microphone requests until explicitly enabled, so Discord
  repeatedly showed its denial dialog. Join is now gated on native session consent
  and the cached permission decision is reset when the user enables it. This
  still needs user verification on the replacement candidate.
- Resize originally ended capture and later kept the initial output dimensions.
  The replacement reallocates bounded output resources and recreates the frame
  pool, preserving the active stream and adapting resolution.
- Generated tracks now wait for an actual first frame, expose its real dimensions
  before handoff, and use the screen-detail content hint.
- Native result codes were lost during cleanup. Result and stop reason now persist.
- Frame acknowledgements alone were an incorrect idle-liveness test. A host-owned
  heartbeat maintains the media lease independently of page refresh/busy commands;
  outstanding buffers retain timeout protection. The idle-failure concern is
  source-derived; the stationary-window fixture continued receiving WGC frames.
- Active capture now uses a per-session high-resolution waitable timer and a frame
  arrival signal. It does not call timeBeginPeriod or change global timer resolution.
- Discord Error 2002 denotes low FPS while watching. The user explicitly accepted
  the idle optimization as long as moving content reaches the selected rate.
  A constant-cadence idle-frame replay experiment was therefore rejected and its
  extra repeat protocol/cache code removed. Idle repeats remain about 4 fps;
  active capture targets 30 fps. Do not suppress Discord's warning UI.

## Framework ownership and consumer matrix

| Area | Owner / current decision | Consumers |
|---|---|---|
| Window enumeration and identity | Reuse host WindowsTaskWindowCatalog and native identity checks | New prototype; shipping Task Switcher unchanged |
| Process loopback | Reuse ProcessLoopbackCapture, including existing bounds and guards | Game Capture unchanged; prototype new consumer |
| Capture-to-WebRTC buffers/lease/teardown | Host-owned WebViewLiveCapture in Validation | Capture fixture and Discord prototype use one implementation |
| Discord stores, commands and account/call checks | Provider adapter | Existing Vesktop adapter reused; web-specific action wrapper separate |
| Native focus, collection publication, pinned lifecycle | Existing SDK/presenter is intended production owner | Standalone feasibility UI only; shell migration deferred until media gate passes |
| Main-world page adapter and hidden-media permission policy | Genuine host/SDK contract gap, not supplied by a normal isolated-world adapter | All shipping WebSession consumers remain unchanged |
| Debug launch hooks | Conditional partial App hook and validation launch election | Shipping builds exclude Validation; no production launch change |

Shipping samples, first-party widgets, templates and SDK declarations do not call
these new validation types. There is no shared-contract migration to apply to them
in this experiment. A production implementation must revisit main-world authority,
hidden-session consent, foreground ownership, controller navigation and all affected
WebSession consumers before widening the contract.

## Historical standalone-prototype gates

- Broaden sustained-motion and hardware/load measurements beyond the local moving
  pattern and the user's 24-fps YouTube source; preserve the measured scope above.
- Exercise the final native Stop/Disconnect controls and controller navigation as
  part of the later shell integration; process teardown and source close were tested.
- End-to-end shared audio on the other account is not yet confirmed by the user.
- Quality selection is not exposed yet. Align supported Discord presets, native
  capture bounds and frame timing when adding the requested compact selector.
- Native controller-shell integration, production pin lifecycle, accessibility,
  hidden/background-session behavior, compatibility updates and performance budgets
  remain separate follow-up work. CPU readback is not a zero-copy production claim.
- Native capture-session Close can block in Windows/driver failure modes. This
  prototype is a separate process, but production containment needs an explicit
  decision before promoting the transport into the normal host.
- Production assets must be sealed separately from browser profiles/results;
  the current ignored development-assets folder also holds the test profile.

Error reference: [Discord audio/video error guide](https://support.discord.com/hc/en-us/articles/30952914470807-Discord-Audio-and-Video-Error-Codes-Troubleshooting-Guide).

Build and run instructions: [README](../tools/DiscordWebViewPrototype/README.md).

## Native widget integration: background-session foundation (2026-10-26)

The standalone prototype remains the working media candidate. This next stage
extends the existing WebSession contract instead of adding a second local server
or exposing CDP to widget code. It is not yet a completed Discord widget migration.

`WebSessionPageAdapter.Mode = SessionBackend` is an explicit, full-trust-only
execution mode. Its sealed package script runs in the exact top-level document's
main JavaScript world and registers `host.onRequest`. Requests and bounded batches
use the existing SDK correlation, cancellation and message transport. The host
owns document readiness, context selection and navigation retirement. Hidden
sessions retain their existing offscreen backing. The mode does not grant any
website permission, capture authority, arbitrary host object, or network response
observer. It rejects unsolicited native page actions.

This mode intentionally shares the website's execution world. It is not a security
boundary against that website or an isolation guarantee for the package script.
The broker admits it only for full-trust workers; sandboxed workers cannot opt in.
The distinct `OpenBackend` operation requires protocol 102 rather than silently
falling back on older hosts. Default isolated adapters omit the new mode field on
the wire, preserving their existing declaration shape.

### Ownership findings

| Location | Finding and evidence | Owner / disposition |
|---|---|---|
| `Browser/WebSessionPageAdapterHost.cs` | Existing isolated-page behavior intentionally cannot read Discord's JavaScript stores. Hidden host requests need a separate lifecycle policy. Source finding; new mode exercised with actual WebView2. | SDK/protocol/host; explicit backend mode, medium complexity. Electron's explicit execution-world/preload distinction is a useful precedent, not an equivalent security model. |
| `Browser/WebSessionPageAdapterHost.Requests.cs` | Waiting for document readiness introduces cancellation-before-admission and stale-document risks. | Host owns bounded readiness admission and cancellation; no widget retry counters. |
| `WidgetSdk/WebSessions.cs` | Cancelling a request before its post acknowledgement previously omitted its cancellation message. Source-derived; deterministic delayed-acknowledgement test now covers it. | SDK sends best-effort cancellation even before acknowledgement. This cannot undo already-performed external effects. |
| `Browser/BrowserSurface.Permissions.cs` | Hidden remote pages cannot request microphone permission through the current visible-page consent flow. | Still open. Add an explicit host-owned user-consent path before migrating native Join; do not automatically grant permission. |
| `Validation/WebViewLiveCapture.cs` | Capture still belongs to the standalone validation process and receives native source identity there. | Still open. Production integration needs permission-bound opaque source references, lifecycle ownership, and a decision on blocking native capture teardown. Do not expose raw HWNDs to the widget. |
| `WidgetSdk/WebSessionPageActionBatchReader.cs` | Existing page-result batching caps the aggregate at 100 items; Discord can enumerate 1,000 voice channels. | Backend-only aggregate of 1,000 now implemented and exercised through real WebView2. Isolated PoB keeps 100. Shared byte/chunk limits stay unchanged. Large status context required a separate domain-shape correction (see full integration milestones). |

### Consumer applicability

| Consumer | Status |
|---|---|
| PoB trade (`PobWidget.Preferences.cs`) | Keeps default isolated actions and response observation. Native synthetic comparison checks its execution-world and hidden-request policy. Live trade workflow has not been retested. |
| PoB passive tree | Packaged session; new backend mode does not apply. Shared SDK cancellation remains compatible. |
| Spotify / YouTube Music players | Packaged sessions; no migration. Their cancellation transport can now receive cancellation while an acknowledgement is pending. |
| Existing Vesktop prototype receiver | Packaged session; unchanged. Existing prototype remains available for comparison. |
| First-party widgets, templates and remaining samples | No additional page-adapter consumer found in repository search. Ordinary WebBrowser consumers retain their current policy. |
| Broker / SDK tests | Cover explicit authority/operation, owner retirement, wire compatibility, background lifetime, RPC batching and cancellation before acknowledgement. |
| Native validation fixture | Uses real BrowserSurface and WebSessionBackgroundHost, fresh synthetic profiles, and intercepted local documents. No account, microphone, or screen-sharing consent is involved. |
| Discord native WebView2 widget | Migration pending the permission and streaming contracts above. Do not mark the standalone media tests as proof of shell integration. |

Validation for this foundation:

- Broker console runner: 5/5 WebSession groups passed, including backend authority,
  wire shape, background lifetime and distinct-operation validation.
- SDK console runner: 1/1 WebSession group passed, including batch assembly,
  explicit backend opening and cancellation before post acknowledgement.
- Actual WinUI/WebView2 fixture: 14/14 checks passed. Includes cancellation before
  readiness (no page mutation), requests while hidden, the root context with a
  child iframe present, navigation retirement, unlisted-origin rejection,
  disposal and isolated-adapter compatibility. Eighteen deliberately uncooperative
  handlers were cancelled without exhausting the 16 active-request slots.
- Native result: `artifacts/discord-webview/backend-runs/06e6acb99b0f4e35a75c6faea1f27e11/result.json`.
- Validation-enabled Release publication and production Release build (validation
  excluded) succeeded with zero warnings/errors. No live Discord call, microphone grant or user
  capture selection was exercised by this fixture.

## WebView-backed SDK widget consumer (2026-10-26)

`samples/DiscordWebViewWidget` is a distinct full-trust package using the actual
SDK and host WebSession backend. The Vesktop package and standalone candidate
remain comparison paths. No installed widget, account, or browser profile was
modified by this work.

Implemented consumer behavior:

- Voice, Channels and Streams sections use native controls, navigator-owned
  scopes and bumper navigation. The account page presents the same host-owned
  persistent session and returns through the SDK navigator.
- Complete channel and participant data use `WidgetLoadedCollection`; channel
  search is local over all loaded channels. Rows are declared on demand, including
  the thousandth channel. Refreshing status does not reconstruct channel rows
  merely because participant speaking state changed.
- Native Join explicitly awaits the host microphone admission API in its user
  action. It checks account and call again after consent. Grant means website
  admission, not proof of OS/device access. Polling never requests permission.
- Mute/deafen/leave use authoritative provider state and SDK single-flight
  operations. Controls remain focusable during a pending mutation; command
  guards prevent duplicate operations without the prototype's earlier disabled
  button focus jump. Uncertain mutations are not retried.
- Watch selects provider state synchronously and awaits the shared pin request
  inside the action scope. Actual pin selection starts the incoming relay.
  Its receiver is owned by an immutable account/call/lease record, separately
  from the next selected stream. Revocation or account/call change closes it;
  a late receiver is disposed instead of being published.
- The packaged incoming receiver and provider adapter are reused from the
  comparison prototype. Build-Package.ps1 composes the existing domain adapter,
  WebView discovery/actions, and the new bounded session endpoint into a sealed
  asset. No separate bridge, CDP socket, credential copying, or browser capture
  fallback is introduced.

### Corrected response-shape assessment

The first design put participants and streams into the repeated batch context.
That was insufficient: 100 participants and 50 streams with long Unicode names
can exceed the frame budget before any channel item is added. The proposal was
narrowed rather than increasing generic byte limits or dropping domain items.

Status now returns small account/call/flags/catalog-version metadata plus
role-tagged participant and stream items through framework batching (at most
150). A separate catalog read returns all channels (at most 1,000) only when its
account-bound version changes. Both paths use `host.batchResult`; no manual
transport fragments are generated. The catalog request must match the status's
account/version, and the C# client publishes one complete validated snapshot
only after both reads agree. Normal unchanged-catalog polling remains one status
request. The host/SDK backend limit is 1,000 items; isolated page actions retain
their existing 100-item limit and all byte/chunk budgets remain unchanged.

Validation: 31 managed checks passed with zero build warnings/errors; 8 JavaScript
checks passed. The JS test executes the current host batching algorithm against
worst-case Unicode names for 100 participants, 50 streams and 1,000 channels,
checking each actual encoded frame and SDK aggregate budget. Managed coverage
includes deep lazy actions, microphone denial and consent races, navigation
return focus declarations, pin demand, late account-change receiver cleanup,
production session-client caching and mixed-account catalog rejection.

Local package preparation succeeded and `wrail validate` passed for
`artifacts/discord-webview-widget/f3b27802007c42efa21601cbf4c9b66c/package`.
The composed backend is 24,477 bytes, below the sealed 64-KiB adapter bound.
This package directory was not installed or launched, and no account/profile
was copied. Its predecessor `220ae038f2984209bfd4dc3c985516ab` predates the
corrected response shape and must not be used for integration testing.

Still required: native rendering/navigation and live microphone/voice/incoming
picture/audio tests for this actual package. Native outgoing sharing is explicitly
unavailable until the host-owned source/audio chooser and streaming ticket contract
are connected. The provider endpoint rejects `share`; it does not expose a hidden
fallback. Future integration must use the host's admitted source/quality options,
not widget-supplied HWNDs or a second manually enumerated source list.

## Full integration milestones (active goal)

The goal remains the complete WebView2-backed SDK widget, not the standalone
prototype or any one shared contract. No merge/release is authorized by this plan.

| Milestone | Current evidence / remaining work |
|---|---|
| Background backend and bounded transport | Committed foundation `4b919285`; expanded backend-only 1,000-item batch passes the actual browser fixture. Latest pre-microphone run: 15/15 checks in `artifacts/discord-webview/backend-runs/4cf9be460ba14818aacb88e1de218bc3/result.json`. |
| Widget-requested pins | Implemented `HostServices.Pinning.RequestOpenAsync(handle or layoutId)`, intrinsic host service, action-success barrier, runtime/owner validation, host replacement UI and existing placement lifecycle. Native nine-check run passed in `artifacts/discord-webview/pinning-runs/7325a61589ee45f89f10fe3258ed107f/result.json`. |
| Native SDK Discord widget | Isolated 0.0.3 is signed in and displays actual Home activity. Current managed suite 155/155 and native navigation 42/42 pass. Search suggestions and synthetic Home/Streams screenshots are verified; physical controller input remains open. |
| Microphone consent | Typed owner/origin/document-bound API and native consent implemented. Current fake-device/chooser/lifetime fixture passes 40/40. The user previously granted the real session prompt; physical microphone/device behavior on the updated candidate remains open. |
| Quality and active/idle pacing | Four baseline presets (480p15/30, 720p15/30); approximate 4-FPS idle retained. Latest Browser-source local 720p30 run sustained 29.72 decoded FPS with audio for 356 measured seconds. This does not prove a locked remote30 or premium-tier support. No further broad benchmarks are planned. |
| Outgoing streaming contract and containment | Native source/audio/quality chooser, opaque tickets and isolated worker with bounded stop/kill implemented. Current capture fixture20/20 includes exact-ticket teardown and runtime retirement. A reproduced live-audio queue overflow on a short renderer stall is under correction. |
| End-to-end acceptance | Earlier integrated incoming/outgoing picture worked together. Updated real microphone/audio reception, concurrent background streams, game sharing, physical controller/pin/foreground workflows and recovery remain open. Current packaging uses reviewed update and a verified trimmed frontend; see the completion audit above. |

### Shared pin action ownership and coverage

Widgets request their own declared layout during the existing runtime-managed user
action scope. They do not send another widget's ID, window handles, focus requests,
placement coordinates, or manual correlation counters. The broker accepts only
interactive, action-bound requests and releases the effect after successful action
completion. Failed/cancelled actions, background work and replayed executions cannot
create pins. The receipt confirms admission, not that a user accepted replacement.

The frontend revalidates the widget/worker and published layout, treats an already
open matching layout as a no-op, and uses the existing pin placement/selection
implementation. Switching the same widget's layouts needs no additional prompt;
replacing another widget or an additional browser page uses host-owned confirmation.
Pins open passively; this action does not transfer foreground from the main widget.

Consumer check: SDK Gallery now demonstrates direct Summary and Level pins; the new
Discord widget adopts direct Shared screen and Voice chat requests. Existing Spotify,
performance monitor and other declared layouts keep their tray path without a forced
UI change. Browser popup pinning and compact media pinning retain their existing host
commands. App-launch close effects reuse the same internal action barrier and remain
covered by the runtime/bridge regression checks.

Evidence: broker pinning/domain checks 4/4, SDK pin group 1/1, real broker/runtime
deferred-effect group 1/1, existing runtime adapter groups 2/2, presentation host-event
tests 14/14, Gallery tests 13/13. The native pin fixture uses the real Gallery worker,
broker, presenter, controller routing and pinned windows in a separate profile. It
checks create/focus, idempotence, own-layout switching, per-layout dimensions,
replacement cancel/confirm, shell hide continuity and unpin. This is automated native
interaction, not a physical-controller acceptance claim.

### Corrected status/catalog assessment

The original backend batching sketch placed all participants/streams in the repeated
batch context. Worst-case long Unicode names can exceed a single-message budget even
with only a few channels. The native widget implementation is narrowing that shape:
small status metadata plus role-tagged participant/stream items, and an independently
versioned channel-catalog read only when its account-bound version changes. Both use
the existing bounded batch API. Publish only a consistent account/catalog combination;
do not silently truncate collections or increase generic byte budgets to hide the issue.

### Host capture integration in progress

`BrowserSurface.WebCapture.cs` connects the admitted capture ticket to the native
worker and the exact backend document. `BrowserOwner.WebSessions.cs` now handles
prepare/stop and publishes capture phase changes separately from provider status.
The host embeds the provider-neutral generated-media runtime; an opted-in backend
cannot fall back to the normal browser screen chooser. The shell chooser owns the
application list, application-audio choice and supported quality selection. Neither
native handles nor media frame payloads are exposed to the widget.

Document navigation, profile changes, disposal, request cancellation and source loss
must retire the exact capture. An uncertain shutdown must keep the ticket and block
another capture rather than acknowledge an unproven stop. Session retirement also
needs an exact-ticket terminal acknowledgement; treating `session_closed` as proof
of native teardown would move a dangerous assumption into every widget. This is a
confirmed source-level integration gap under implementation, not yet live evidence.

Current validation boundary: the isolated native worker has exercised source loss,
bounded shared mappings, parent death and forced shutdown. The integrated surface,
native chooser and final SDK package still require a fresh coordinated publication
and native fixtures. The first publication attempt caught a stale microphone-origin
identifier after the shared document-lease refactor; that call site is corrected.

Consumer applicability for capture: Discord WebView is the only opted-in backend;
ordinary browser/webapp screen sharing retains its existing picker path. Game Capture
recording/screenshots use the existing capture operations. PoB, Spotify and YouTube
Music do not opt into native capture and receive no generated-media interception.
The shared host-choice additions must preserve ordinary choice/notification behavior
and controller navigation. WidgetRequested pin changes remain separately covered.

Latest integrated-contract gates (2026-10-26):

- SDK web-session groups 3/3, broker capture groups 3/3 and broker web-session
  groups 6/6 passed. Widget checks passed: 53 managed and 10 JavaScript.
- Native consent/chooser/document suite passed 36/36 on the immutable
  `artifacts/discord-webview/consent-picker-candidate`. Evidence:
  `artifacts/discord-webview/microphone-runs/7bddb505dafa49af8eee1ac878460447/result.json`.
  Native controller selection originally failed to return focus from the quality
  menu; fixed and rerun. These use synthetic documents/devices, not account consent.
- Actual native worker → WinUI shared buffers → local WebRTC: 27.75 decoded FPS
  at 720p30, normal stop 80 ms; simulated blocked native teardown: 27.99 FPS,
  forced stop 677 ms. Exact child exit confirmed in both runs. Evidence:
  `artifacts/discord-webview/worker-webrtc-3/result.json` and
  `artifacts/discord-webview/worker-webrtc-block-1/result.json`.
  This is not a locked-30-FPS or remote Discord/audio acceptance claim.
- Production helper rejects all nonzero validation flags. Release/verification
  packaging includes the worker and both native libraries; native build/publish
  prerequisite guards passed 9/9. Actual full production publication is still due.
- Current isolated widget workspace:
  `artifacts/discord-webview-widget/workspaces/e16ff8bdb1fa4929b713b06be473d4b5`.
  Built, validated, packed, installed and enabled in its own catalog/profile;
  not yet launched or signed in. No credentials were copied.

The independent lifecycle review found another source-derived issue: a native
shutdown error could prevent WebView closure, leaving a hidden call alive after
the widget was stopped. Page revocation must complete independently of native-exit
confirmation; retain the exact transport/ticket for bounded retries. This correction
and its failure-injection test are in progress. Do not label the goal complete until
this branch and actual BrowserSurface media integration pass.

Live test authorization: the user approved using the private Test server → Lobby
call and second test account again, and explicitly requested keeping the final
test candidate isolated. Sign-in, microphone consent and real content selection
remain user actions; no need to ask again which call or whether to isolate.

Actual BrowserSurface media integration passed 11/11 checks with production native
worker binaries: 29.74 FPS over 8.00 seconds; generated process-loopback audio
reached browser tracks (434,400 samples, nonzero expected tone). Hidden capture,
exact-ticket replacement, navigation retirement, source loss and repeated teardown
passed. Evidence: `artifacts/cap-fb684560/result.json`, using
`artifacts/discord-webview/integrated-candidate-2`. This is generated local content,
not microphone access or remote Discord delivery. The late unconfirmed-stop/browser
teardown correction still needs its own native failure-injection qualification.

The unused staged profile was redirected to the fresh shorter
`artifacts/discord-live-e16ff8bd-profile` before its first launch, to avoid Chromium
named-profile path limits. The staging script now generates shorter workspace paths
and rejects oversized profile paths; no populated profile or account was moved.


### Pending outgoing-capture watchdog experiments (2026-10-26)

The reported unexpected live worker exit remains unexplained. Generated-track
source retirement fixes missing browser `ended` notifications; it does not explain
why that native worker stopped. The six-minute generated motion/audio/GPU run was
healthy and does not exercise transient renderer stalls or audio discontinuities.

A source-confirmed keep-result gap is corrected pending fresh native qualification:
`WebViewCaptureWorker` now requires a bounded JSON acknowledgement with `active:true`
before renewing the child heartbeat. Missing/retired/malformed runtime responses
cancel only that capture with `receiver_heartbeat_invalid`; epoch/document guards
and the existing three-second script/ten-second native deadlines remain unchanged.
The actual BrowserSurface fixture adds runtime-dispose/missing/inactive cases before
navigation and verifies exact-ticket terminal acknowledgement and subsequent reuse.

Keep these separate, narrowly reproducible experiments pending:

- Block only synthetic renderer JavaScript for four seconds with application audio
  disabled; distinguish the three-second script watchdog from process death while
  the host remains responsive. Do not infer this behavior from Node timers.
- Repeat with generated application audio. The 192,000-byte PCM queue represents
  roughly one second and may reach its explicit overflow-stop policy first.
- Exercise synthetic audio stop/start, timestamp/discontinuity flags and scheduling
  stalls. Shared `ProcessLoopbackCapture` currently treats post-initial discontinuity
  as fatal. Do not change that recording/live policy without reproduction and an
  audit of existing Game Capture consumers and A/V timestamp guarantees.
- Crash or suspend only the isolated fixture renderer and verify bounded worker
  teardown, document revocation, and no automatic source/provider restart.

Additional diagnostic follow-ups: distinguish actual window closure from other
`Valid()` failures before labelling `source_closed`; retain a sanitized child exit
code for abrupt worker loss and a reason for callback exceptions currently swallowed
by the native delivery trampoline. These are source findings, not identified causes
of the user's earlier failure. Do not relax consent/liveness safeguards speculatively.

### Independent provider and action-intent review (2026-10-26)

- `activity-provider.js` and `adapter.js` now bound labels without splitting a
  surrogate pair, repair preexisting unmatched surrogates, and remove C0/C1 control
  characters. The previous actual provider projection of 119 ASCII characters plus
  an emoji caused .NET JSON string extraction to throw. This was a synthetic
  provider/consumer reproduction, not a live account observation.
- Activity ranking now occurs before the 50-row transport limit. The bounded
  2,000-friend scan retains only the best 50 rows plus one insertion candidate,
  prioritizing Watch then Join then passive presence with deterministic ID ties.
  A synthetic 50-passive-friends fixture previously hid a later joinable friend.
- Ordinary call actions previously reused `mute`/`deafen`/`leave` IDs and inferred
  account/call/desired state at execution. The host deliberately validates action
  identity rather than parsing labels. The widget now uses deterministic bounded
  domain-qualified IDs for call controls, Watch/Stop watching, and Share/Stop.
  Expected account, call, session, desired toggle state, stream key, and the SDK's
  admitted capture-attempt generation are included where applicable. The attempt
  owns its exact native ticket but remains stable across chooser return, so a
  queued Cancel still applies when capture starts. Element IDs stay stable for focus.
  No action registry, widget-managed counters, or generic host label parsing was added.
- Incoming stream selections retain their backend reference through asynchronous
  receiver setup; session retirement cannot publish or start a replacement watch.
  Join rechecks its captured backend reference after microphone consent. Explicit
  provider Stop also rechecks the backend after asynchronous local capture cleanup.
  Generic permissions/capture ownership remain host responsibilities.

Consumer applicability: both the legacy Vesktop prototype and the WebView2 backend
load the shared adapter/activity provider, so both receive the bounded-label and
activity-selection fixes. The full WebView2 widget owns the new ordinary action
intent rules; its main and pinned call views share one declaration path. Indexed
channel/activity actions already use the SDK's captured item/context bindings.
The legacy Vesktop widget's own static control actions are not migrated: it remains
an experimental alternative, not the target full implementation. No shipping
widgets, templates, or public protocol contracts changed in this bounded fix.

Potential SDK follow-up, deferred: `WidgetIndexedLeases` already captures immutable
item invocation context, while ordinary controls expose opaque action IDs only.
A typed immutable command-context API could reduce repeated domain qualification
across consumers (precedent: command parameters in WPF/WinUI MVVM). Complexity is
medium because queued and pinned input, stale declarations, bounded retention,
and protocol compatibility must remain explicit. This does not justify moving
Discord account/call authorization into the host or rewriting the SDK here.

Known limit: Discord's exposed stream key identifies account/channel/user, not a
broadcast incarnation. A provider broadcast stopped and restarted in the same call
between observations cannot be distinguished reliably. Explicit Stop retains its
documented current-stream semantics; automatic local capture retirement does not
blindly stop the provider broadcast. No fabricated provider generation is used.

Verification: Node provider/adapter/invocation suites pass 60/60, including exact
Unicode-boundary and greater-than-50 mixed-activity regressions. The coordinated
widget run passes 138/138, including the managed stale-intent tests:
`artifacts/discord-webview/domain-intent-current.log`. Native focus/navigation
qualification is tracked separately; this source audit does not establish it.


The opt-in renderer-stall matrix is now authored (not run). After a fresh validation
publication, `tools/DiscordWebViewPrototype/Test-RendererStalls.ps1` selects
`--capture-renderer-stall-matrix` alongside the existing capture fixture route.
The normal capture suite remains the default. Four fresh BrowserSurface lifetimes
stall the next real `keep()` call for 1,500/4,000 ms with audio off/on. Each records
actual JavaScript duration, host-dispatcher tick gaps, exact worker exit, native
HRESULT/reason and source counters, then confirms cleanup. Surviving or terminating
are observations of current policy, not predetermined pass criteria. Only generated
fixture content/audio is selected. No watchdog or shared audio policy was changed.


The candidate-9 full capture fixture reached 11 checks, including visible/hidden
video and audio and the disposed-runtime heartbeat case, then its next read-only
`status` request raced remote navigation. This is a fixture sequencing error:
`docs/developers/hosted-web-sessions.md` documents remote navigation as admission,
not provider readiness, and `NavigateHostedCoreAsync` returns after `core.Navigate`.
The validation harness now waits for a new host document epoch, exact destination
URL, and the existing `CurrentBackendDocumentOrigin` readiness predicate before
sending the next RPC. Initial/replacement stall documents use the same readiness
check. No production navigation semantics changed; no failed command is retried or
replayed. Fresh full-capture and optional stall qualification remains pending.

### Loaded voice-channel completions (2026-10-26)

Channels search now adopts the existing SDK `TextSuggestionCatalog`,
`TextEntry.Suggest`, and `OnSuggestTextAsync` contract, following Playnite's
account/data ownership checks. Suggestions show channel names with guild
descriptions; duplicate names retain distinct channel IDs. Selecting a completion
only fills the keyboard field. Committing it filters the list; neither operation
joins a call, prompts for microphone access, or fetches more Discord data.

One immutable catalog is cached in the widget model for each loaded channel-array,
account, and backend-session identity. Ordinary status polling reuses it. Source
keys retire when that domain owner changes; the callback checks the current
Channels route, interactive lifecycle, source key, and owner before and after
search. The host continues to own editor-session/revision correlation and timing.
No custom request queue, debounce, polling, or focus shortcut was added.

Values retain the complete validated channel name within the 120-unit field limit.
Labels use `WidgetTextChunks.NextLength` to respect the keyboard's smaller label
budget without splitting emoji. Focused managed tests cover Unicode boundaries,
duplicate names/guild context, no side effects, bounded results, unchanged polling,
account/catalog/session retirement, route/background changes, and cancellation.
These tests pass in the coordinated 155-check managed run
(`artifacts/discord-webview/widget-final.log`); native keyboard behavior is not
established by source review alone.


The renderer-stall fault reproduction completed at
`artifacts/discord-webview/stall-fault-evidence-1/result.json`: 1.5 seconds survived
video-only but overflowed the live PCM queue with audio enabled; 4 seconds reached
the existing browser watchdog without audio and overflowed audio first when enabled.
This precisely reproduced policy failure remains separate from the unexplained
original live exit. The worker-only correction now applies a 200 ms queued-audio
latency budget plus the unchanged 192,000-byte hard cap, preserving whole packets,
original QPC/FIFO and the immutable outstanding mapped credit. No recording/WASAPI,
watchdog or ABI changes were made. Focused policy/credit native probes passed from
`worker-audio-1`; the short post-change BrowserSurface matrix with actual resumed
audio/video counters is pending a fresh frontend publication. The running user
candidate was not replaced.


The narrow live-audio correction is now qualified by the real BrowserSurface
post-stall matrix, using `audio-recovery-validation-1/candidate` and production
native binaries from `worker-audio-production-1`. Evidence:
`artifacts/discord-webview/stall-recovery-evidence-1/result.json`. The 1.5-second
video-only case resumed 30 frames (29.69 FPS); with audio it resumed 30 frames
(29.60 FPS) and 48,480 audio frames in the recovery window. Both 4-second cases
still hit the unchanged browser heartbeat watchdog, stopped producing data and
exited cooperatively. Host dispatcher maximum gaps stayed under 67 ms; all exact
fixture workers/source were cleaned up. Candidate 85224 was verified still running
and was not replaced. This closes the reproduced PCM-backpressure failure; it does
not establish the cause of the user's original unexpected exit or remote audio
acceptance. No broad throughput baseline was repeated.

### Incoming pin freezes after closing from native controls (2026-10-26)

User confirms the pin continues when closing with the Discord page selected,
but freezes when closing from native controls. Cause remains unconfirmed.
The provider's WindowVisibilityVideoManager can stop incoming video after 30
seconds hidden; its per-stream GoLiveQualityManager propagates zero video demand.
Separately, `Browser/WebSessionBackgroundHost.cs` parks backend views in a 1x1
host, while `BrowserSurface.Chrome.cs` reserves a 2-DIP progress row. The resulting
viewport/visibility behavior must be measured before choosing provider or host
ownership. Existing passive-pin memory checks do not prove sustained incoming
frame progress after this transition. No production fix has been applied.

At the user's request, restarted isolated candidate 5 (PID 81196) with temporary
loopback CDP port 19347 and `--trace-web-sessions`; physical testing remains with
the user. Injected the opt-in `tools/DiscordWebViewPrototype/discord-incoming-stats.js`
into backend and receiver. It retains at most 300 samples at 2-second intervals:
provider video-enabled state, viewport/visibility, inbound counters, and receiver
playback counters. No account IDs, URLs, tokens, SDP, or media content are collected.
Initial read confirmed the current provider exposes the visibility singleton and
live inbound counters; receiver playback counters are also available. This is
diagnostic readiness, not a reproduced or resolved freeze. Retrieve both rings
after the user's reproduction; remove diagnostic launch flags on the next normal
candidate restart. Do not join calls or run physical scenarios on the user's behalf.

The user's subsequent reproduction confirmed the provider visibility gate.
Entering Discord and returning to controls changed `document.hidden` to true;
roughly 30 seconds later `incomingEnabled` changed to false and the inbound video
track became muted with no inbound video reports. The pin document stayed visible.
Sanitized histories are retained under
`artifacts/discord-webview/incoming-freeze-diagnostics/*-frozen-*.json`.
The earlier successful run retained `document.hidden=false` throughout and did
not exercise this transition. The source-inferred zero-height host hypothesis is
not established by this evidence; no global host visibility/memory change is warranted.

### Stream audio controls (2026-10-26, in progress)

The provider exposes separate `stream` and default `voice` audio contexts. Native
controls must explicitly select `stream` for every getter/setter. Read-only live
inspection confirmed an engine-owned, playing audio output for the watched stream
and an enabled inbound audio track; stream-local mute was true. No second audio
relay is required for that observed state. Audible output remains a user check.

The main Streams view and selected screen pin use the SDK button/slider controls.
Volume uses Discord's perceptual browser scale (0–100), not its stored amplitude.
Finite saved desktop boosts are preserved and expose an explicit Use 100% volume
action; the mute control remains usable without normalizing that saved value.
Commands bind account, call, user, stream, backend and receiver lease. They await
the SDK input lane, using existing slider coalescing rather than a widget queue.
The existing resource Cancel/Refresh API fences polls begun before the mutation.
No automatic mutation retry, deafen change, microphone change or new permission.

| Consumer | Applicability |
| --- | --- |
| DiscordWebViewWidget | New native audio controls; existing SDK pinning and sliders |
| DiscordNativePrototype | Shared adapter and optional audio snapshot; existing UI remains compatible |
| DiscordWidget browser integration | Not a consumer of this adapter/model |
| Other hosted web/music widgets | No shared host, SDK or protocol changes required |

The current diagnostic candidate is deliberately not updated during the user's
ongoing playback test. Source/package version 0.0.5 includes the earlier 0.0.4
setup-message correction. Validation results and package location follow below.

Adapter build 11 implements the confirmed freeze correction by wrapping only the
exact watched stream manager's `onIncomingVideoEnabled` method for the live relay
lease. It delegates to the original quality manager and leaves global document
visibility, prototypes and registered emitter listeners unchanged. The provider's
current policy is restored on release. Same-stream replacement transfers demand;
new connection identities retire the old hook and use the exact replacement
media connection rather than the first connection for that user. Lost ownership,
subscription, call/account or lease retires the relay without resubscribing.
Private method shape and connection identity were verified read-only in the live
client. A live patched call remains untested at the user's request.

Review caught a changing pin input scope when audio availability changed. The
pin now has a stable outer root/scope and video identity; only the audio footer
changes. Percentage and Deafened status are visible text as well as accessible
slider metadata. Package validation caught an unsupported `height:auto` WRSS
declaration; removing the unnecessary height leaves the video pane growing into
the space above the fixed footer. A fresh package validates and packs successfully:
`artifacts/discord-webview/widget-0.0.5-audio-demand-2/discord-0.0.5.wrwidget`, SHA256
`d1f9d7a765f51792b945e1413777fe1d426a74d88531f7912f880a9f9da067a9`.
No installation or running-session mutation was performed. Combined Node checks
pass 115/115; standalone compatibility checks pass 17/17. Final widget check count
is recorded below after the stable-pin regression additions.

Final widget suite: 213/213 checks passed with zero build warnings/errors
(`artifacts/discord-webview/stream-audio-final-tests.log`). This includes exact
main/pin intent, visible deafen/percentage, stable pin root/scope/video ownership,
unavailable/boosted settings, stale account/call/session/user/lease rejection,
SDK slider coalescing with one external write at a time, and fencing a delayed
pre-mutation poll. No native physical navigation/audio or patched live hidden-page
playback claim is made. User retains physical testing; PID 81196 remains running
the prior package with the bounded read-only diagnostic recorder.

### Audio UX correction after user feedback (2026-10-26, in progress)

The user rejected 0.0.5's large permanent footer and implementation-specific saved
volume reset flow. They also reported controller entry into the main slider failing
until pointer use, and slow/unreliable mute feedback. These reports invalidate
the previous assumption that declarative audio tests alone were sufficient.

Version 0.0.6 / adapter build 12 replaces the pin footer with one compact native
speaker/slider/percentage row, with no reset button or saved-volume warning.
Discord's browser caps its output gain; supported saved desktop boosts now project
to the browser slider endpoint without changing the saved value. Muting and
resending unchanged 100% preserve that preference. Only a deliberate change writes
gain. Provider readback verifies a mutation before returning its observed audio
state, rather than returning an empty success. Unsupported/no-op actions fail
without retries. The widget publishes that small acknowledgment without waiting
on broad channel/status polling. Exact remote audibility latency remains unmeasured.

The provider also snaps volume near its default to about 54.2%. With exact
minimum-anchored 5% stepping, repeated isolated Right presses could request 55%
forever. The displayed thumb now rounds to the authored grid while observed state
and percentage remain accurate; near-endpoint observations stay fractional so
actual 0/100 remain reachable. Rendering never writes provider preferences.

Shared addition: `WidgetResource.TryPublishUpdate` atomically merges an observed
partial update into the latest value and fences older reads using the same owner
as `Publish`. This avoids a widget-owned read/modify/publish race and per-command
timestamp overlay. Complexity is small/additive, with no protocol changes. The
existing `WidgetModel.Update` is the local functional-update precedent. Applicability:

| Consumer | Status |
| --- | --- |
| Discord WebView audio | Adopted for checked partial acknowledgments |
| Playnite, Vesktop comparison, read-only data template | Whole-value resources remain unchanged |
| PoB query resources | Separate query authority; not migrated to an unscoped update |
| Spotify, YouTube Music, Settings and other widgets | No matching direct-resource partial publication found; existing model owners retained |
| SDK docs/examples | Additive method documented; no existing declarations require changes |

SDK 177/177, widget 218/218 and Node 122/122 pass. Evidence is in
`stream-audio-resource-sdk-tests.log`, `stream-audio-v6-final-tests.log` and
`stream-audio-v6-node.log` under `artifacts/discord-webview`.
The generic cold native slider test passes all nine 0/50/100% main/pin-width
placements, so no speculative host directional policy change was made.
The production-widget synthetic main controls also pass cold entry/adjustment;
mute acknowledgment measured 32/78ms through that synthetic path. Pin tests are
being completed with actual View entry and minimum-width layout. One fixture
startup required real foreground activation; one assertion was corrected to await
the host's queued pin focus before checking its exact target. Neither is a claimed
production fix. The user's earlier first-use blockage is not yet reproduced.

Controls in this candidate remain always visible; interactive-only pin controls
were recommended in response to the user's question but have not been implemented.
No 0.0.6 update has been applied to the running user session.

### Expanded capture quality and main-only stream controls (2026-10-26)

The latest user request supersedes the pinned audio footer. Native regression
source now keeps audio interactions in the main Streams page and checks a passive,
full-area video-only pin at minimum size. The earlier 47 main/pin checks remain
historical evidence for the superseded footer, not acceptance of the final layout.

Local capture transport now declares twelve presets: 480/720/1080/1440p at
15/30/60 FPS. The default remains 720p30. Legacy preparation offers exactly the
original four presets. The new constrained operation receives provider-approved
qualities, intersects them with local transport support, initializes the chooser
to the caller's preferred option, and rejects any out-of-set chooser result before
native startup. Host chooser cancellation persists no independent preference.

The native/managed/JS upper video envelope is 5120×1440 BGRA: 29,491,200 bytes.
Mappings are allocated for the selected preset, never universally at this maximum.
Output/staging textures and readback pixels use actual source-aspect dimensions;
no upscaling was introduced. The existing capture job limits process count and
lifetime, but has **no process/job memory cap**. The original source-size limit and
two-frame WGC pool remain unchanged. One-video-credit backpressure and exact
epoch/sequence acknowledgements remain unchanged. These bounds are not a promise
of a total process-memory ceiling or guaranteed delivered60 FPS.

Consumer matrix: production Discord uses the new provider-constrained request;
legacy hosted-capture callers and admission/lifetime fixtures retain four choices;
the old standalone Discord validation page explicitly retains its original four
choices because its provider workflow was not migrated; the generic capture probe
can exercise all twelve. YouTube Music, Spotify and PoB do not use native screen
capture. Game Capture recording and shared geometry semantics were not changed.

Verification so far: native-media JavaScript tests pass25/25, including a complete
5120×1440 frame and oversized/odd/mismatched descriptor rejection. PowerShell
runner parsing and scoped diff checks pass. Native quality/pacer tests now cover
all twelve presets and no-upscale landscape/ultrawide geometry. The optional
`CaptureWorkerProbe --high-quality` tests full-size 1080p60/1440p60 mappings,
credits, complete pixels, cadence, exact worker memory sample, and teardown.
`Test-CaptureWorker.ps1` accepts explicit quality/source dimensions for short
real WGC→shared-buffer→WebView→local-WebRTC runs. These native checks await the
coordinated build/run; no live account or user capture has been exercised here.

### Resume checkpoint and approved unified design (2026-10-26)

The user paused before installation of 0.0.6 and subsequently resumed with a UI
redesign. Source and profile were preserved; no merge/release is authorized.
Pre-pause verification: widget 234, SDK 177, compatibility 17; protocol 105,
190 native constants. Synthetic high-quality transport passed 1080p60 and
1440p60 (about 58.9 and 55.6 FPS), not remote Discord delivery. Native production
artifacts are `quality-native-production-1` and `quality-platform-1` beneath
`artifacts/discord-webview`. Real higher-quality browser delivery, remote audio,
and physical acceptance remain outstanding. On resume, the corrected broker
gate passed 93/93 and combined Node suites passed 165/165, with evidence in
`layout-resume-*` logs. No additional capture benchmarks were run.

Approved UI: Call + activity. Voice, participants, incoming screens and outgoing
sharing share one page. Idle shows the server/channel picker. Participant status
uses speaking rings and mute/deafen icons, with descriptive accessibility names;
no visible Speaking/Listening labels or explanatory pin filler. Stream audio
stays in the main widget; video pin stays video-only. Existing shared loaded
collections, navigator, select, focus-target lookup and scoped shortcuts own
generic mechanics. The native host's existing compact breakpoint stacks a
secondary activity projection, while expanded presentation has a narrower
activity column. Preferred width is 1040 DIPs for that existing breakpoint;
minimum 420×360 remains a tested requirement. No widget viewport polling or
custom responsive host contract is introduced for the mockup's arbitrary width.

X toggles microphone; Y starts sharing only when no stream is pending/active;
L3 opens server/channel selection in a nested navigator scope. Y deliberately
does not turn into Stop after admission. B returns to the invoking call control.
Confirmed channel joins return from the nested picker only while its captured
navigation snapshot, backend, account and target are still current. The picker
and full Discord page do not inherit call mutation shortcuts.

Server/channel keyboard suggestions and list results share ranking. Domain IDs
distinguish duplicate names; results span the entire account despite a selected
server. Server selection changes the channel list without joining. Catalogs
beyond the native Select option bound use an indexed server list, not truncation.

Shared addition: `TextSuggestionCatalog.FindMatches` exposes the bounded catalog's
complete ranking; existing `Search` keeps its completion limit and ordering.
This is an SDK-owned ranking mechanism, not duplicated widget search plumbing.
Source-grounded consumer matrix:

| Consumer | Disposition |
| --- | --- |
| Discord server/channel list and keyboard | Adopted; one catalog and rank order |
| Spotify | Existing completions retained; result search is provider-owned |
| YouTube Music | Provider ranking and local fallback retained |
| Playnite achievements | Retained separate result policy for secret/description/filter matches |
| Marketplace | Retained description/setup-term matching, beyond suggestion labels |
| SDK examples/templates | Compatible; no migration needed |

SDK 177 checks pass and fresh API baseline is 3899 symbols. Widget suite initially
passes 265 checks including new unified/picker behavior; the first run caught an
invalid initial focus during backend replacement, now resolved with the existing
SDK target lookup/fallback. Native navigation and audio validation are being
updated and run against synthetic isolated services; no live Discord call or
user capture is used for those checks.

#### Unified layout verification and isolated update

Native verification reproduced a compact-layout defect: allocating one quarter
of the remaining height to activity left its indexed viewport at19.2 DIPs for
102.4-DIP rows. The scroll-first boundary policy then consumed many tiny scroll
steps before leaving the collection. Fixed the widget's layout, not the host's
directional policy: compact primary content now shares an outer scroll surface,
and participant, channel and activity collections have finite viewports. Expanded
activity remains a separate column. The final layout keeps the main call controls
declared once and uses stable distinct IDs for both activity projections.

Review also restored Stop sharing beside the idle picker when call loss leaves
local capture cleanup unconfirmed; retained visible Back chrome during picker
connection failure; and adopted SDK indexed-return navigation for L3 from a
participant/activity row. Removed occurrences or changed queries fall back without
publishing a stale focus request. Confirmed Join observes provider state once,
then returns only from its unchanged captured picker visit.

Full-name search is an explicit opt-in to the additive
`TextSuggestionCatalog(..., TextSuggestionMatchField.Value)` overload. It avoids
losing the searchable suffix when a keyboard display label is abbreviated.
Existing callers retain Label matching and ordering. API baseline:3903 symbols;
SDK177/177 and fresh compatibility17/17 pass. Widget297 checks pass, including
same-name guild identity, catalog retirement, overflow, single-observation joins,
and indexed return/removed-origin cases. Evidence: `unified-widget-tests-4.log`,
`unified-sdk-final-tests-1.log`, `unified-sdk-compat-tests-1.log`.

Native `unified-navigation-3` passes50 minimum-size and49 expanded checks;
`unified-audio-1` passes27 checks. These exercise the actual shell/controller
routes, native dropdown and keyboard, cold slider entry at0/50/100, mute ACKs,
deep indexed L3/Back, passive-pin foreground and video-only minimum pin geometry.
They are synthetic provider tests, not physical hardware or remote audibility.
After the final text/alignment/recovery edits, the same scenarios are rerun with
`unified-fixture-final`, writing `unified-navigation-final` and
`unified-audio-final`; completion is recorded below.

Production frontend `production-candidate-6` is freshly trimmed/published;
all four platform/capture native binary hashes match the qualified source artifacts.
Package0.0.6 at `widget-0.0.6-unified-final/discord-0.0.6.wrwidget` validates and
packs; SHA256 `4655f1886a2aa350a7c3990b0e71c7068b3d3dd17b8b352359ad74d8b7040eb5`.
It was applied only to the existing isolated catalog through reviewed
`wrail update --file --sha256 --apply`, with disable/enable surrounding the update,
preserving its existing profile, preferences and matching permission decisions.
New protocol105 services are in `unified-runtime-1/installation`. Its
`shell-options.json` points to the original isolated browser profile/catalog;
`Launch-Candidate.ps1` launches the qualified production frontend. No credentials
were copied, no production installation changed, and no live call was joined.
The candidate is left closed. Higher-resolution remote delivery and audio
acceptance remain for the user's later sharing test; no release/merge was done.

Final native rerun note: `unified-navigation-final` failed before any checks because
the diagnostic window did not acquire real foreground. This was not counted as a
pass. A fresh run with explicit Computer Use activation, `unified-colors-navigation`,
passes50/49 checks against the final widget fixture. The earlier27 audio checks
remain the audio evidence; `unified-audio-final` was not run after that startup
failure. No host foreground workaround was introduced for the fixture.

The user correctly noted that Mono Ink's neutral `--accent` did not reproduce
the wireframe highlights. Version0.0.7 adopts the same existing theme data-color
tokens used by PoB: purple for primary/share/watch actions, green for connected,
live and speaking indicators, red for Leave. Primary fill uses `alpha()` of the
theme purple; focus still uses `--focus`, and disabled controls remain neutral.
No new palette or theme/host contract was added. Source review confirms the native
high-contrast adapter substitutes system foreground/background colors as before;
actual native visual review was performed in Mono Ink, not a new light/high-contrast
physical acceptance run. The corrected colors and left-aligned activity copy were
visually verified in the native expanded fixture, then that fixture was closed.

Final isolated selection is0.0.7, installed via the reviewed update path from0.0.6.
Package: `widget-0.0.7-colors/discord-0.0.7.wrwidget`; SHA256
`d2aebfd7caa22eb3f8a33175d4d44d192733973bca71784aa3a3012281c12984`.
Production frontend6 and `unified-runtime-1/Launch-Candidate.ps1` remain current.
All test windows are closed. User profile/catalog and matching permissions were
preserved; only synthetic fixtures were launched during this implementation.
