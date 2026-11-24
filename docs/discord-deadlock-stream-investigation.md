# Discord fast-motion stream investigation

The test uses the user-authorized Discord test lobby, a Deadlock sandbox, and a separate Chrome viewer. Compare the same capture target at 720p30. Record sender cadence and receiver cadence under quiet and sustained camera movement.

| Step | Status | Evidence |
| --- | --- | --- |
| Recover Clarity after a thumbnail-to-large transition | Verified on this PC with Discord Controls 0.1.0.75 and its final native worker | Desktop received 2.854 Mb/s full screen and 2.450 Mb/s in the restored large window during separate 007 motion windows. The previous build delivered 1.662 and 1.705 Mb/s. The final low-to-high transition had zero decoder drops or counted freezes. See “Native encoder recovery and viewer demand.” |
| Locate and raise the 720p30 sender ceiling | Verified on this PC, opt-in candidate 0.1.0.74 | Two consecutive Chrome receiver windows averaged 3.171 and 3.095 Mb/s at 720p30 with zero freezes and decoder drops. See “Where the 720p30 allocation comes from.” |
| Verify the opt-in Windows encoder against Discord desktop | Complete for this PC at 720p30 Prefer clarity | Two final 007 runs with Discord Controls 0.1.0.70 delivered 29.94 and 29.87 decoded FPS at 1280×720 with zero freezes or decoder drops. The two 0.1.0.68 controls froze seven times each. Desktop delivered 30.04 and 30.03 FPS with zero freezes. Desktop's higher bitrate and other capture presets remain outside this verification. |
| Establish the desktop and WidgetRail baselines | Captured | The earlier WidgetRail Chrome dump delivered 8.5 FPS and 2.62 Mb/s in its worst five seconds, with 37 keyframes among 45 decoded frames. The desktop Chrome dump delivered 30.1 FPS and 3.39 Mb/s over 37 seconds, with one keyframe among 1,123 decoded frames. Both used constrained-baseline H.264 (`42e01f`) and the same viewer SDP bitrate hint. |
| Raise the transport encoding ceiling | Rejected | Sending parameters read back 6 Mb/s, and the user reported the same stutter. The experiment is removed from the adapter. |
| Raise the Chromium H.264 SDP hint | Rejected | The sender read back 6 Mb/s for the codec hint while its measured encoder target stayed at 2.5 Mb/s. The user reported the same stutter. The SDP rewrite is removed from the adapter. |
| Trace the exact outgoing share during the drop | Confirmed after capture | A live Clarity sample on `0.1.0.55` averaged 29.9 submitted and 17.2 encoded/sent FPS for 12 s, with a 6 FPS minimum one-second sample, 32 keyframes and no PLI or FIR. A separate sample recorded 30 submitted and WebRTC source frames versus 16 encoded frames in the affected second, with no increase in PLI, FIR, NACK or retransmitted bytes. WebView2 reported `powerEfficientEncoder: false`. |
| Compare Smoothness against Clarity | Improved cadence, resolution tradeoff | `0.1.0.56` averaged 30.1 submitted and 28.1 encoded/sent FPS over 12 s, with a 21.3 FPS one-second minimum, one keyframe, and no PLI or FIR. Chrome dump (4) recorded one freeze near the start, then about 30 FPS at 960×540. Both WidgetRail sweeps ran 186 six-degree camera steps in about nine seconds. |
| Confirm the desktop encoder with controlled motion | Verified on sender | Desktop logged `H264 (nvidia: direct3d)` and NVENC. The 05:21:57.231–05:22:07.263 local interval contained 234 six-degree steps (13:21:57.256–13:22:06.399 UTC). There were 301 new encoded frames (30.0 FPS), 4,401,839 new sent bytes (3.51 Mb/s), zero new keyframes or freezes, and 1280×720 at both samples. WidgetRail was the viewer because desktop and Chrome had the same account active. Compare this sender result with the earlier independent desktop-to-Chrome receiver dump. |
| Test hardware encoding through Media Foundation | Rejected | WebView2 `157.0.4341.60` reported accelerated capability for Baseline (`42001f`) and software for Constrained Baseline (`42e01f`). With only `PlatformH264CbpEncoding` enabled, the advertised hardware capability changed, but WebView2 failed to configure the Media Foundation encoder and fell back to software. The live test delivered 21.3 encoded FPS, a 14 FPS one-second minimum and 11 keyframes. Chrome dump (5) remained at 720p and decoded 20.4 FPS over the sweep. |
| Test motion encoding with resolution-first adaptation | Rejected and removed | `0.1.0.57` kept the motion hint and requested `maintain-resolution`. Sender cadence averaged 19.8 FPS with sustained 15 FPS during motion despite continued 30 FPS input. It produced no new keyframes or PLI/FIR. Chrome dump (6) had no active inbound video counters, so use the sender result only. |
| Default to Smoothness with normal WebView2 settings | Implemented and deployed | `0.1.0.58` starts the picker in Smoothness and keeps Clarity selectable. With the full-screen Chrome viewer, settled 720p sweeps decoded 29.0 and 26.6 FPS. The second sweep included two freezes. The 480p comparison delivered 28.7 and 28.9 FPS and one freeze in each sweep. Keep 720p as the normal quality unless the user selects something else. |
| Test D3D12 encoding for Constrained Baseline | Confirmed with candidate-only runtime options | Explicitly enabling both `PlatformH264CbpEncoding` and `D3D12VideoEncodeAccelerator` made the isolated probe initialize the RTX 5070 Ti D3D12 encoder for negotiated `42e01f`. No GPU preference override or Media Foundation disable switch was required. Live sender samples reported `powerEfficientEncoder: true`. The flags are applied only to a test process. |
| Verify live accelerated Clarity | Rejected as a general fix | Two instrumented samples averaged 29.8 and 29.5 encoded FPS at 720p with zero new keyframes, PLI, FIR, or NACK. Two settled receiver windows averaged 30.05 decoded FPS at 720p with zero counted freezes, decoder drops or new keyframes. Fresh video-only and application-audio sessions still had intermittent receiver freezes. The normal launch omits the experimental flags. |
| Correlate packet timing with receiver freezes | Reproduced inside the WebRTC sender | `0.1.0.59` adds UTC and bounded RTP packet counters to the on-demand sample. The final accelerated sweep held 30 submitted and WebRTC source FPS but fell to 15, 11 and 19 encoded/sent FPS during three affected seconds. Mean local packet send delay was 296, 262 and 355 ms in those seconds, with zero new keyframes, PLI, FIR, NACK or retransmitted bytes. Chrome dump (15) showed 25.1 decoded FPS, seven freezes and zero decoder drops over the sweep at 1280×720. The preceding run held 30.1 sender FPS and had only one receiver freeze. |
| Set the desktop-reference bitrate through Discord's quality manager | Rejected and removed | Temporary build `0.1.0.60` confirmed 3.5 Mb/s in the exact outgoing provider manager at 720p30. With normal WebView2 settings, the encoder still topped out at 2.5 Mb/s even when WebRTC estimated over 4 Mb/s available. Chrome dump (16) measured 29.25 FPS at 720p with one 189 ms freeze, then 28.22 FPS on the repeat with a 20.79 FPS one-second minimum and a downshift to 540p. The sender's minimum sampled second fell to 21 FPS with 52 ms mean packet send delay. Evidence is under `provider-rate-policy`. |
| Check Discord’s normal alternate codec negotiation | External protocol constraint confirmed | An isolated WebView2 probe verified that the process-local test options removed H.264 from sender capabilities while VP8, VP9 and AV1 remained available. The live peer connected and capture delivered 1,608 frames by the first check, but Chrome displayed the loading indicator after subscribing twice. Every observed remote video answer in dump (17) listed only H.264 and RTX. Chrome reported no decoded video. Restoring the original options and selected `0.1.0.59` made the same two accounts decode Deadlock video again. No codec flags or overrides are kept in the product. |
| Verify the restored build against the same motion | Playback restored, stutter reproduced | Chrome dump (18) recorded 23.76 decoded FPS, a 13.86 FPS one-second minimum and a 189 ms freeze. At onset, the sender kept receiving 30 FPS while sending 15 FPS and averaging 112 ms of local packet send delay. WebRTC estimated 0.66–1.13 Mb/s available for this control, substantially lower than in the quality-manager trial. The runs establish that stutter persists in both configurations, with insufficient control of the network estimate to attribute their average FPS difference to the trial. |
| Classify frame drops in the normal WebView2 encoder | Encoder-internal drops confirmed | With 720p30 Smoothness and Discord desktop watching, two live ten-second continuous turns at 15:22:49.532 and 15:24:43.280 UTC each called OpenH264 303 times and produced 284 and 280 frames. The worst complete seconds produced 20 and 21 frames. Both turns triggered quality scaling from 1280×720 to 960×540. The quiet controls encoded 298 and 300 frames from 300 calls each. Across the entire share, WebRTC reported 330 encoder-internal drops, four encoder-queue drops, four rate-limiter drops, and zero congestion-window, timestamp, or capture drops. |
| Classify loss in accelerated Smoothness | Hardware encoding improved cadence | D3D12 produced 301/301 and 302/302 encoded frames from submitted encoder calls during two sustained turns. Both had a worst complete second of 29 encoded frames. One turn held 720p and the other shifted to 540p after one rate-limiter drop and one encoder-queue drop. Discord desktop decoded live video. Receiver freeze totals were unavailable for this run. |
| Reproduce pacing stall in accelerated Clarity | Rate limiter and send queue confirmed | At 15:42:40.325 UTC, the ten-second turn produced 232 encoder outputs and 41 logged pre-encode rate-limiter drops. The 12-second widget sample averaged 29.8 submitted and 24.1 sent FPS, with a one-second minimum of two sent frames at 720p. The five archived samples captured 511 ms mean local send delay in that second and no keyframes, PLI, FIR, or NACK. WebRTC logged large-queue catch-up at a nominal pace of roughly 1.07 Mb/s. |
| Test faster WebRTC pacing with hardware encoding | Rejected for rollout | The process-local trial trace confirms `D3D12VideoEncodeAccelerator` with hardware acceleration enabled. In the sampled second ending at 16:04:28.038 UTC, input held at 30.1 FPS while the encoder and sender reported 20.1 FPS and the mean local send delay was 283 ms per packet. No new keyframes, PLI, FIR, NACK, or retransmitted bytes appeared in that second. The first share needed a retry. The browser arguments were confined to the test launch. |
| Switch viewers on an existing accelerated pacing-trial share | Playback stalled | Chrome rejoined while the desktop viewer was displaced. WebRTC reconfigured H.264 at 16:12:00 UTC, and encoder output stopped. Three later ten-second turns kept submitting 298–301 frames each with zero encoded frames in the verbose trace. Chrome dump (19) recorded zero assembled and decoded video frames during all three turns. This is specific to the running trial so far. |
| Verify hardware-only Smoothness with Chrome receiving | Matched desktop cadence at reduced resolution | A fresh 720p30 Smoothness share with D3D12 and the normal pacer decoded 30.00, 29.96 and 30.09 FPS in three ten-second Deadlock turns from 16:22:04 UTC. The lowest complete receiver intervals were 29.49, 29.60 and 29.54 FPS, with zero freezes and zero decoder drops. Sender encoded 301, 300 and 300 frames and held at least 29 in every complete second. Chrome dump (20) shows 960×540 throughout the measured turns, at 1.51–1.84 Mb/s. Startup briefly selected 480×270, where WebView2 explicitly fell back to OpenH264 below 360p. D3D12 resumed at 640×360 before the measured turns. |
| Repeat hardware-only Smoothness after resolution recovery | 720p cadence verified in one complete turn | Three more turns starting at 16:24:50 UTC decoded 29.91, 30.03 and 29.94 FPS with zero freezes or decoder drops in Chrome dump (21). The first downshifted from 720p to 540p, the second stayed at 540p, and the third held 1280×720 at 2.55 Mb/s. The last turn's one-second receiver minimum was 29.52 FPS. The browser launch had the two D3D12 feature flags and no pacing trial. |
| Repeat at steady 720p with hardware-only Smoothness | Encoder held cadence, receiver freeze reproduced | Chrome dump (22) measured 29.94, 30.04 and 30.03 FPS at 1280×720 at 2.02–2.48 Mb/s. Sender encoded all 301, 300 and 300 submitted frames, with at least 30 in every complete second. The first turn had one 596 ms freeze and a 16.76 FPS minimum receive interval, while the other two had zero freezes and 29.55 and 29.61 FPS minima. The affected receiver samples assembled 17 frames in one second and 43 in the next, with zero decoder drops and one net packet loss. This confirms a delivery or frame assembly gap after encoding. |
| Pair hardware-only Smoothness sender drops with the Chrome receiver | WebRTC frame dropper confirmed | In the sample ending at 16:59:32.184 UTC, the video source delivered 30 frames. WebRTC logged 28 pre-encode rate-control drops and called D3D12 twice. Both calls produced a frame. Mean local send delay was 269 ms per packet, and the candidate-pair bandwidth estimate was 4.54 Mb/s at the end of the sample. The matching Chrome interval decoded five frames and counted two freezes. The 17:05:36.315 UTC repeat logged 29 pre-encode drops, one successful encode, and 721 ms mean local send delay. Chrome had stopped decoding before the repeat, so use sender evidence only for that run. |
| Isolate the frame dropper with the same hardware encoder | Verified in loopback | A deterministic 720p30 H.264 `42e01f` probe lowered the sender ceiling from 2.5 to 1.25 Mb/s during sustained motion. The normal D3D12 run decoded 5.57 FPS in the seven-second comparison window, with a 4.96 FPS minimum and 215 pre-encode drops across the run. Disabling only the WebRTC frame dropper in the test process yielded 29.96 decoded FPS, a 29.25 FPS minimum and no logged pre-encode drops. Both kept 720p with zero new keyframes and negligible local send delay. Media throughput was 1.26 and 1.34 Mb/s, respectively. On a separate resolution-adapting run, falling below 360p still triggered a software fallback and frame skipping even with the WebRTC dropper disabled. |
| Verify the frame dropper trial over Discord | Three 720p turns passed with test-only runtime options | Chrome decoded 30.05, 29.99 and 30.20 FPS during three ten-second Deadlock turns beginning at 17:27:33 UTC. One-second minima were 29.60, 29.54 and 29.58 FPS. All held 1280×720 at 2.30–2.51 Mb/s with zero counted freezes, decoder drops, keyframes or net packet loss in the measured windows. The sender used D3D12 with normal pacing and only the frame-dropper field trial disabled. |
| Stress the experimental sender during sustained motion | Sender cadence held through a downshift | Two 25-second turns beginning at 17:29:31 UTC and 17:29:58 UTC produced 751 and 754 frames from the same number of encode calls. The minimum complete seconds produced 28 and 29 frames. The first turn switched to 960×540. The trace logged no pre-encode rate-control drops or large-queue catch-up. Chrome rendered the stream after both turns. Its export for these two turns was unavailable because the file picker blocked the second dump. |
| Qualify per-sender H.264 temporal layers | Accepted locally and in the test share | WebView2 reported `scalabilityMode=L1T2` on a loopback sender negotiating the same `42e01f` profile at 720p30 with and without hardware flags. Its capability result was power-efficient only with the flags. The live Clarity trial under the normal runtime reconfigured the exact outgoing track to 1280×720 L1T2, and Chrome continued decoding. |
| Measure two temporal layers during live fast motion | Rejected and rolled back | In three ten-second turns from 16:40:35 UTC, OpenH264 encoded 165, 148 and 176 frames out of 300, 299 and 300 encoder calls. The separate pre-encode dropper accounted for only one frame across the turns. Chrome dump (23) recorded 16.00, 12.52 and 17.93 FPS at 720p, with 15–26 new keyframes per turn and no PLI. The preceding single-layer turn was worse at 4.17 FPS and six freezes, but the receive bitrate was also substantially lower (0.88 versus 2.24–2.57 Mb/s). The test package was built outside maintained source and reverted to 0.1.0.59. |
| Try to scope the frame-dropper behavior to a layered sender | Rejected | WebView2 accepted H.264 `L1T2` in screen-share mode and logged two encoder layers. At fixed 720p with the 2.5 to 1.25 Mb/s loopback cut, D3D12 still logged 204 pre-encode drops and decoded 5.57 FPS after the cut, the same as hardware `L1T1`. OpenH264 decoded 4.57 FPS before the cut and 2.0 after it. Upstream reads a separate H.264 temporal-layer field for the dropper exception. Explicit `scalabilityMode` and that field are populated differently, which is consistent with the trace. |
| Downshift the outgoing sender without disabling the browser frame dropper | Verified in loopback with hardware | Applying `scaleResolutionDownBy` in the same update as the 2.5 to 1.25 Mb/s cut kept D3D12 decoding at 30.15 FPS at 540p and 30.0 FPS at 360p in the settled seven-second windows. The first second after the cut decoded 27.1 and 28.2 FPS with zero freezes, compared with 7.0 FPS and two freezes when held at 720p. OpenH264 still fell below 7 FPS at either resolution under the same synthetic load. This test controls when the bitrate cuts. A live sender has to detect when to downshift, so live parity is unverified. |
| Drive the downshift and recovery from sender stats | Repeated in loopback with hardware | The adaptive probe observed the encoder target through `RTCRtpSender.getStats()` and changed only that sender’s resolution using `setParameters`. It selected 540p about 140 ms after the synthetic ceiling cut while motion continued. The transition second decoded 27.1 FPS, then returned to about 30 FPS with zero counted freezes. In the extended repeat, restoring the ceiling returned the sender to 720p after about four seconds, with 26.8 FPS in the transition second and zero counted freezes. Both probes logged just two pre-encode drops across the run. The WebRTC browser-wide dropper stayed enabled. |
| Check browser runtime scope without using signed-in profiles | Verified with synthetic profiles | Ordinary WebViews initialized together from one user-data folder with matching options. A second WebView with different encoder options and the same folder failed before page readiness. With a different folder it initialized while both ordinary WebViews were running. There is currently no separate capture runtime in the host. |
| Check the newer WebView2 release with its normal encoder options | Software fallback persists | Microsoft's signed fixed runtime `158.0.4370.33` ran the adaptive loopback probe from a fresh test folder. Its encoder remained OpenH264 for `42e01f`. It decoded 5.29 FPS at 720p before the cut and 3.71 FPS at 540p after it, against approximately 30 FPS at the source in both intervals. The existing Evergreen installation and the running candidate stayed on their original configuration. |
| Check the firewall prompt after the fixed-runtime probe | Attributed to the isolated executable | Windows Firewall event 2097 at 09:47:27 local named the fixed runtime under `webview2-158-fixed/runtime` and showed inbound blocks for TCP and UDP. The probe completed with those blocks. The installed Evergreen runtime had a different executable path. No firewall permission was granted or changed during the investigation. |
| Verify regression and teardown | Verified | Managed widget harness passed 1,075 checks and JavaScript passed 382 checks. Capture start/stop, re-sharing, application-audio track setup and call transitions worked live. WidgetRail received and released the desktop Discord stream pin during the comparison. The first share immediately after the final widget update timed out waiting for provider quality confirmation. Stop and a new share recovered it. Test accounts disconnected and the temporary game binding was restored. |
| Match desktop Discord using the normal launch configuration | Open | The test-only D3D12 and frame-dropper combination matched the desktop reference in three measured 720p turns. Normal WebView2 still used OpenH264 for this profile in the control. The active 0.1.0.59 candidate was restored and relaunched without experimental browser arguments. Both test accounts left voice, and the temporary Deadlock binding was removed. |

The latest diagnostic result matches the desktop's 720p cadence in short
live sweeps. The evidence is in `frame-dropper-ab/fixed-resolution` and
`frame-dropper-ab/live`, including the receiver dump and browser trace.
The browser trial disables a process-wide WebRTC safeguard. Normal WebView2
with the negotiated profile still selects OpenH264 on this machine. The
remaining goal is a production configuration with hardware selection and
rate control scoped to the sending path, verified under sustained motion,
bandwidth changes, and a fresh share with application audio.

## Shared native-capture scope

Discord is the shipping consumer of the existing hosted-session
`NativeCapture` capability. Any runtime or rate-control change belongs to that
capability so future streaming widgets use the same path. Provider negotiation
and quality limits remain with the adapter. Ordinary browser sessions, hosted
audio players and media previews use their existing environments. A browser
view attached to a capture session, such as a stream pin, must use the parent
session's environment and profile.

| Consumer | Applicability |
| --- | --- |
| Discord Controls hosted call and outgoing game capture | Affected. Verify signed-in startup, microphone and application audio, stop, re-share, and outgoing cadence. |
| Discord incoming video and stream pins | Shares the owning session. Verify attachment, pin reparenting, and teardown. |
| Native capture fixtures | Affected. Exercise capture admission, repeated and interrupted start/stop, simultaneous ordinary browsers, and profile cleanup. |
| Browser, hosted music and video widgets, gallery previews, SDK examples and templates | No shipping native-capture use found. Verify ordinary browser and media playback if host runtime selection changes. |

WebView2 profile names isolate storage inside a user-data folder. They do not
scope browser arguments. A dedicated folder can isolate a test, but placing
existing persistent sessions elsewhere changes where account data lives and
requires explicit profile lifecycle handling. Keep login and profile cleanup
in the host and avoid copying selected auth data between stores. The isolated
runtime probe used fresh synthetic folders. Microsoft documents option matching
for browser processes and labels the flags used in this investigation as
diagnostic. The native-capture capability alone cannot select a hardware encoder
through the standard WebRTC API. [WebView2 environment options](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/webview2-idl?view=webview2-1.0.4258.31) [WebView2 profiles](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/multi-profile-support) [Browser flags](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/webview-features-flags)

The sender-scale, adaptive recovery and temporal-layer controls use the same generated motion,
H.264 profile `42e01f`, 1280×720 input at 30 FPS, and bitrate step as the
frame-dropper trial. Their snapshots and logs are under
`frame-dropper-ab/supported-scale-540`, `supported-scale-360`,
`adaptive-540-recovery`, and `scoped-temporal`. The normal-runtime repeat is
under `webview2-158-fixed/default-options`. WebRTC's H.264 codec initializer sets the simulcast layer
count from the explicit scalability mode and fills the H.264-specific field
from the legacy temporal-layer setting. The dropper reads the latter. The
field mismatch is a source inference supported by the observed drop counts.
[WebRTC codec initializer](https://webrtc.googlesource.com/src/+/refs/heads/main/modules/video_coding/video_codec_initializer.cc) [WebRTC dropper condition](https://webrtc.googlesource.com/src/+/refs/heads/main/video/video_stream_encoder.cc)

## 1080p60 startup and variable game frame rate

The user prefers Clarity after testing both modes and requested local-only
reproduction of the intermittent receiver startup failure. The running user
test used diagnostic widget 0.1.0.62 and the installed runtime with D3D12
enabled. The trace contained no adaptive-policy samples. Its trial policy was
limited to a 1280×720 Smoothness source, so the Clarity result does not verify
adaptive sender behavior.

| Question | Status | Evidence |
| --- | --- | --- |
| Does native 1080p60 Clarity reach a local receiver? | Verified with application audio | The existing worker harness sent 1920×1080 H.264 through the installed WebView2 runtime and D3D12. The measured source submitted 60.06 FPS, the encoder produced 59.66 FPS and the receiver decoded 60.48 FPS. The receiver counted 581,760 audio samples. The worker exited normally. |
| Can a delayed viewer reproduce a share that never starts? | Unreproduced locally | Three fresh 1920×1080 Clarity loopbacks used constrained-baseline `42e01f`, application audio and a conservative 2 Mb/s ceiling. Two held the video sender inactive for 1.5 seconds, then enabled it at 300 kb/s for 1.1 seconds. First rendered frames arrived 138, 279 and 266 ms after video enable. Both delayed runs counted one freeze but recovered. No Discord calls were started for this test. The older live sender trace showed both starts that produced only a few frames per second and starts that reached 60 FPS. It did not include matching receiver data identifying which starts failed. Leave Discord startup behavior unchanged. |
| Does changing the game frame rate change the worker output rate? | Checked on generated Windows Graphics Capture content | A source with 10–52 ms between draws yielded about 30 or 60 worker packets per second at the selected setting. At 60, only about 24–26 packets per second carried a different picture with default acquisition. The worker repeats the latest pixels when Windows has no newer frame. Source cadence and delivered packet cadence therefore need separate measurements. |
| Can the compositor supply fresher pictures? | Blanket change rejected | Halving the Windows Graphics Capture minimum acquisition interval improved the variable case from 24–26 distinct pictures/sec to 32–34 at 60 send FPS. It reduced steady-source distinct-picture rate in some runs, including 56.72 to 45.92 in the fixed 60 FPS control. Removed the unconditional experiment from maintained source. |
| Adapt the compositor interval to observed capture cadence | Implemented and validated with a synthetic transition comparison | The shared native worker checks fresh acquisitions every 750 ms for a 60 FPS share. It shortens the system interval only within the measured low-refresh range and restores the original interval when the source settles. Across two A/B runs alternating steady and variable content, variable phases averaged 24.94 to 33.53 distinct pictures/sec. The 95th-percentile gap between fresh pictures improved from about 68 to 52 ms. Steady phases averaged 54.73 to 57.50 distinct pictures/sec. Packet cadence stayed near 60 FPS. |
| Repeat with the built native candidate | Passed locally | The production build repeated the variable phases at 34.68 and 33.53 fresh pictures/sec, held 57.73 and 57.37 fresh pictures/sec in the steady phases, and sent about 60 packets/sec throughout. Local Clarity pipeline tests with application audio passed at 1080p60 (60.43 decoded FPS) and 720p30 (29.95–30.02 decoded FPS) with normal worker exit. The first scaled-source 720p30 run exposed an integer overload in the fixture's expected-scale calculation. The fixture now uses a floating-point scale and the same larger source passed on repeat. A live game comparison with the built candidate remains open. |

Evidence is under `user-1080p60-local`, `1080p60-startup-matrix-3`, and
`variable-source` in the local diagnostics directory. The variable-source
fixture records frame callbacks and paint changes. It does not measure game
present cadence or packets after network transport. Replay uses a distinct
texture delivery path. Any acquisition optimization should remain shared by
the native web-capture path and be checked at a steady 60 FPS as well as through
transitions between steady and variable sources. Discord Controls uses this
shared worker. Future native web-capture widgets get the same behavior through
the existing capability. Replay texture capture, 30 FPS capture and Windows
versions without the optional acquisition interface keep their existing path.
[Windows acquisition interval](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.minupdateinterval)

## 007 First Light on the staged native worker

The maintained widget (0.1.0.59) ran with the staged worker and standard
WebView2 encoder settings. Each Clarity share included application audio.
Alternating horizontal camera movements in the same training corridor
reproduced the slowdown. Chrome's inbound RTP timestamps delimit the
reported intervals. The desktop reference uses its native sender log because
the desktop client and Chrome use the same test account. WidgetRail watched
the desktop stream in a pin.

| Run | Result |
| --- | --- |
| Existing 1080p60 session before switching workers | Excluded from the cadence comparison. Chrome's counters reset after previously decoding at about 60 FPS. During both sweeps packets arrived but Chrome assembled zero video frames. A viewer-only reconnect displayed Discord error 2012. The alternating sweep produced 288 encoder outputs over 12.0 seconds. This was an incidental observation on the existing share. The cause of error 2012 is unresolved. |
| Candidate 1080p60, two 12-second sweeps | At 1920×1080 Chrome decoded 12.07 and 11.65 FPS over complete samples. The worst intervals were 5.92 and 7.00 FPS. Chrome recorded three freezes and one freeze respectively, zero decoder drops and zero net packet loss. Keyframes accounted for 118 of 134 and 109 of 129 decoded frames. Neither sweep added a PLI or FIR. |
| Candidate 1080p60, additional sender sample | During four full seconds of alternating motion, capture and WebRTC source each advanced 240 frames. The encoder and sender advanced five, with no new PLI or FIR. WebRTC reported a non-power-efficient encoder. Its target was 0.16–0.37 Mb/s at those sample endpoints. Chrome's stats collection had a gap during this sweep, so it supplies no paired receiver rate. |
| Desktop and candidate 720p30, same game and movement | At 1280×720, Discord desktop's NVIDIA H.264 sender encoded 602 frames in 20.068 seconds (30.00 FPS) at 3.57 Mb/s with no new keyframes. The candidate's Chrome viewer decoded 213 frames in 24.20 seconds (8.80 FPS) at 2.56 Mb/s. It counted 176 keyframes, zero decoder drops, zero PLI/FIR and one net packet loss. The desktop account offered 720p30 as its standard preset. There is no matched desktop 1080p60 reference. |
| Standard per-sender codec selection | In local WebView2 loopbacks, setting the encoding's codec to Baseline H.264 succeeded when both profiles were negotiated. When only Constrained Baseline was negotiated it raised `InvalidModificationError`. At fixed 720p under synthetic motion, Baseline delivered 29.93 FPS against Constrained Baseline's 6.14 at 2.5 Mb/s. After a cut to 1.25 Mb/s both stalled (5.00 and 2.43 FPS respectively). |
| Qualify that switch on Discord | The exact outgoing sender offered five H.264 profiles, including Baseline (`42001f`). Its remote answer and negotiated codec list contained only Constrained Baseline (`42e01f`), before and after Chrome subscribed. No profile switch was applied. The Chrome **receiving** leg independently reported `42001f`. Its format cannot establish what the sender may select. |

**Production parity remains open.** The native interval change improves local
variable-rate frame acquisition. The live bottleneck persists downstream of
capture. On this machine the production WebView2 runtime selects software for
the only H.264 profile negotiated on the outgoing share. The standard
per-sender API cannot select an unnegotiated profile or select the underlying
hardware encoder. Neither a fixed server-wide bitrate cap nor the cause of the
receiver startup error was established.

Local evidence is under `bond-007-live` and `shared-adaptive-native`. Both
test accounts left voice, the reference pin was released, and the capture
worker exited. The catalog was restored to 0.1.0.59. The staged frontend was
relaunched with the two local hardware-encoder trial flags used during the user's
Clarity test. The normal WebRTC frame dropper remains enabled. No browser flags
or codec-selection experiment were added to shipping code.

### Can the existing peer connection accept an external encoder?

| Check | Result |
| --- | --- |
| Capability on standard WebView2 | On secure local pages in installed 157.0.4341.60 and fixed 158.0.4370.33, WebCodecs accepted an H.264 configuration requesting hardware acceleration and produced 12 chunks each. Neither runtime exposed `RTCRtpSender.createEncodedSource`. The ordinary encoded transform operates after the browser encoder and cannot recover its skipped input frames. The WebCodecs preference alone does not identify the hardware implementation or prove bitstream compatibility with the negotiated profile. |
| Isolated experimental sender on 157 | Enabling `RTCEncodedSource` exposed the method, but constructing a new encoded frame from bytes raised `TypeError`. Enabling `RTCEncodedVideoFrameConstructor` as well allowed a WebCodecs worker to supply H.264 to a local video sender. All 212 frames passed through an existing sender-side transform and decoded at 640×360 over about seven seconds. The transform counted and forwarded frames. Discord encryption, actual native capture, game-motion quality, network feedback and live parity were not tested in this probe. |
| Production decision | Defer the external-encoder integration until a supported WebView2 runtime exposes both facilities. The existing host supplies raw video tracks. A future shared native-capture integration would need negotiated H.264 compatibility, keyframe and bitrate feedback, and verified coexistence with the provider's transform. No experimental Blink flags were set on the user's candidate. |

**Blocked on the production sender.** A check of the public native header and
managed API reference for WidgetRail's pinned WebView2 SDK 1.0.4078.44
identified no host API for choosing a WebRTC encoder implementation or supplying
encoded video. Revisit when a standard runtime accelerates the negotiated profile
on the affected GPU, the outgoing connection negotiates a hardware-capable
alternative, or a supported encoded-source API ships. Each route still needs a
matched live camera sweep with receiver measurements.

The isolated results and probe sources are under
`external-encoder-20261123-194547`. Chromium currently lists this API as
proposed. I found no external video encoder input in Discord's public Social
SDK reference. [Chromium feature status](https://chromestatus.com/feature/5177374353260544)
[WebRTC Encoded Transform](https://www.w3.org/TR/webrtc-encoded-transform/)
[Discord Social SDK](https://discord.com/developers/docs/social-sdk/index.html)

### Opt-in encrypted 007 candidate

The user authorized testing the encoded-source flags on the main WebView2
environment. Discord Controls 0.1.0.67 automatically selects the experiment
for a 1280×720 source shared at 720p30 Clarity. The candidate uses
`RTCEncodedSource` and `RTCEncodedVideoFrameConstructor`. It leaves the
WebRTC frame dropper enabled. The earlier D3D12 trial flags were omitted
because that WebCodecs path emitted a Baseline SPS without the Constrained
Baseline constraint bit. With the two Blink flags, the external encoder
requested hardware acceleration, used constant bitrate, and emitted
`42c01f`, compatible with the negotiated profile and level. Hardware identity
was not independently measured on the live encoder.

The candidate detaches only the owned video sender's raw track and feeds its
captured frames through WebCodecs to that sender. The worker handles bitrate
allocations and keyframe requests. Version 0.1.0.67 also emits a recovery
keyframe about every four seconds and follows the current RTC connection and
capture track through viewer changes. The private test call reported DAVE
version 1. Chrome received 1,986,240 application-audio samples during the
41.38-second video measurement.

| Test | Result |
| --- | --- |
| Local late handoff | Attaching an encoder while the original track was attached raised an operation error. After detaching it, Chrome decoded 720p through a counting sender transform, including after the sender ceiling fell from 2.5 to 1.25 Mb/s. The original track could be reattached. |
| First live prototype | The independent Chrome viewer decoded 29.99 and 30.90 FPS in two 20-second camera sweeps at full 720p, with zero freezes or decoder drops. A later viewer transition left it receiving packets without decoded frames. This prompted the ownership and recovery changes. |
| Final candidate, 42-second sustained turn | Chrome decoded 1,121 frames in 41.38 seconds (27.09 FPS), with a 12.84 FPS worst interval. It held 1280×720 with zero counted freezes and zero decoder drops. Over 41 overlapping seconds the external encoder processed and submitted 1,230 frames, with no queue drops. Its reported allocation ranged from 0.26 to 2.50 Mb/s. |
| Display delay during sustained motion | Compared timestamped samples of the actual game window and the displayed Chrome video on the same PC. Of 1,434 receiver observations, 437 had distinctive matches. Their median delay was 190 ms, the 90th percentile was 824 ms, and the later five-second windows settled near 158 ms. These are approximate visible-picture delays, with approximately 33 ms sampling intervals. |
| Final candidate, repeat and rejoin | Chrome decoded 724 frames in 24.16 seconds (29.97 FPS) with a 28.86 FPS worst interval, zero freezes and zero decoder drops at full 720p. Stopping and restarting viewing in Chrome displayed the game again on the same share. Stopping the share kept both call participants connected and released the native worker. |

The earlier matched 007 reference remains 8.80 decoded FPS on standard
WidgetRail and 30.00 encoded FPS on Discord desktop. The attempted fresh
ordinary-sender control produced no decoded frames and was excluded. Image
sharpness at low bitrate, incoming stream pins on this build, other GPUs and
1080p60 remain unverified. The local candidate remains selected, with 0.1.0.59
installed as a rollback. Both test accounts disconnected. The trial package,
build inputs and measurements are under
`external-007-candidate-1795482706589` in the local diagnostics folder.
No release default was changed.

### Candidate 0.1.0.68 follows the WebRTC allocation

The opt-in 720p30 Clarity worker removed WidgetRail's 2.5 Mb/s maximum,
30 kb/s floor, 8% reduction and 20% target-change threshold. It passes the
full positive WebRTC allocation to WebCodecs, subject to the provider's
current explicit sender maximum. It updates that maximum when the sender
changes or clears it. Feedback updates are coalesced over 250 ms. Zero
allocation after startup pauses frame submission until the allocation returns.
The regular build excludes the experimental worker. Build the trial backend
with the Discord package script's `-ExperimentalEncodedSource` switch.

In a WebView2 loopback using the encoder and binding extracted from the
packaged backend, the test peer varied the sender maximum from 6 to 1.2,
4.5 and 4.9 Mb/s. During four settled windows Chrome received 6.05, 1.34,
4.56 and 4.94 Mb/s respectively, at 30.2–30.3 FPS and 1280×720. Those
windows recorded zero freezes; the initial handoff recorded one. RTP
throughput can differ from configured elementary-video bitrate. This proves
the candidate can exceed the former local ceiling and follow recovery.
Discord's live allocation and any provider-side limit remain unverified for
0.1.0.68.

Stream details shows the latest allocation, configured and encoded bitrates,
and an explicit sender maximum when present. Measure stream cadence reports
average RTP payload throughput for the measurement interval. Evidence is under
`uncapped-068-1795485766626` in the local diagnostics directory. The installed
payload matched the tested build. Candidate 0.1.0.67 is available for rollback.

#### Live viewer-size comparison on 0.1.0.68

The user's existing 007 share was 720p30 Clarity, with Discord desktop
receiving. Before any receiver changes, the viewer's last recorded pixel
request was 101,468. The sender's explicit `maxBitrate` was 552,960 b/s.
A 12-second sample averaged 0.47 Mb/s sent. The separate remote codec
maximum reported in the negotiated session was 2.5 Mb/s.

Switching the desktop viewer to its large stream view raised the explicit
sender maximum to 3.5 Mb/s. Selecting the test server's text channel while
watching reduced the pixel request to around 90,000–101,468 and the maximum
to 0.55 Mb/s. Returning to the lobby's large, windowed video requested
1,922,700 pixels and restored the 3.5 Mb/s sender maximum. The remote codec
maximum remained 2.5 Mb/s. The sender and share remained running throughout.

During repeated camera sweeps in the same scene, over complete receiver
log intervals of about 20 seconds each, the compact viewer received
1.23 Mb/s at 29.58 FPS and the large viewer received 2.50 Mb/s at
29.98 FPS. Both decoded 1280×720 with zero additional decoder drops
or counted freezes during those intervals. Sender maxima are settings
sampled around the sweeps; received rates are measured byte deltas during
motion and can differ. This comparison did not measure visual fidelity
or a matched 1080p60 share. Local evidence is under
`live-bitrate-068-1795486761680`. The viewer was returned to the large,
windowed presentation with the user's share active.

#### Stock senders with the same Chrome receiver at 720p30

Both accounts used the test lobby. The sender shared the 007 First Light
game window. The viewer stayed in regular Chrome. Each motion interval
used repeated camera sweeps for about 35 seconds. All decoded at 1280×720.
Rates below are receiver byte and frame deltas over complete sampling intervals.

| Sender | Viewer | Mb/s | Decoded FPS | Worst interval FPS | Freezes / decoder drops |
| --- | --- | ---: | ---: | ---: | ---: |
| Desktop | Large | 3.533 | 30.03 | 28.92 | 0 / 0 |
| Desktop | Compact | 3.521 | 30.07 | 29.60 | 0 / 0 |
| Desktop | Large again | 3.524 | 30.03 | 28.71 | 0 / 0 |
| Stock Discord Web | Compact | 2.531 | 7.88 | 5.99 | 1 / 0 |
| Stock Discord Web | Large | 2.542 | 7.96 | 6.01 | 2 / 0 |

An initial stock-web large sample showed the pause menu and is excluded.
The compact and subsequent large gameplay runs used the same uninterrupted
share. Sender media-source cadence averaged about 29 FPS in each. OpenH264
reported software encoding and encoded 267 and 268 frames respectively.
Of these, 232 and 233 were keyframes. Each run recorded zero new sender PLI,
FIR, NACK or retransmitted bytes and zero net receiver loss. The cadence
loss was already present at encoder output. The traces do not identify the
component that requested or generated the extra keyframes.

The web sender's remote answer included `x-google-max-bitrate=2500`.
Its mean target was 2.48 and 2.47 Mb/s, actual payload 2.49 and 2.50 Mb/s,
and mean available outgoing estimate 4.20 and 4.11 Mb/s. Desktop instead
logged NVIDIA Direct3D H.264, CBR, low latency, one temporal layer and a
3.5 Mb/s configured maximum. No new desktop receiver keyframes appeared
during the sweeps. Desktop's Chrome receiver also saw a 2500 codec hint
while receiving 3.52–3.53 Mb/s. The direction of negotiation matters.
These observations establish a difference between sender implementations
on this setup. They do not establish a universal service-side limit.

In separate loopbacks with the packaged 0.1.0.68 worker and the same
3.5 Mb/s sender maximum, changing the test peer's answer hint from 2500
to 3500 raised the mean configured bitrate over the final ten samples from
2.20 to 3.13 Mb/s and receiver throughput from 2.18 to 3.10 Mb/s.
These are single local trials. The remote service did not participate.
The earlier live WidgetRail 29.98 FPS result used a desktop viewer.
Comparison of visual fidelity still needs a matched WidgetRail-to-Chrome
run with the same scene, viewer and motion. For that run, correlate actual
encoded bytes and keyframe cadence with the provider's allocation before
changing encoder policy. Evidence is under
`desktop-chrome-720-1795488372798` in the local diagnostics directory.

#### WidgetRail 0.1.0.68 with a Chrome receiver

Ran three 35-second camera sweeps in the same 007 scene using a single
720p30 Prefer clarity game-window share. WidgetRail sent from Sanky's
account. Sanky1 watched in Chrome on the same machine used for the stock
tests. The accounts' sender and receiver roles were reversed from those
tests. The encoder diagnostics confirmed the external worker was active.

| Chrome viewer | Received Mb/s | Decoded FPS | Lowest complete interval FPS | Keyframes | Freezes (duration) |
| --- | ---: | ---: | ---: | ---: | --- |
| Large | 2.529 | 30.33 | 28.95 | 9 | 0 |
| Compact | 2.531 | 29.87 | 24.98 | 9 | 1 (400 ms) |
| Large again | 2.535 | 29.99 | 28.87 | 8 | 1 (252 ms) |

All three decoded at 1280×720 with zero decoder drops. Both freezes were
counted within the first 1.2 seconds of camera movement after returning to
the game. Chrome sent 183, 125 and 150 NACKs versus 24, 3 and 4 in the
desktop reference. Neither series recorded any new PLI. The counters show
retransmission requests; they do not attribute the early freezes or identify
whether packets were delayed, reordered or lost before recovery.

The widget's sender trace overlapped 6, 5 and 4 seconds of motion,
respectively. The single-encoding maximum stayed at 3.5 Mb/s while mean
external allocations were 2.45, 2.49 and 2.49 Mb/s. Mean configured worker
targets were 2.45, 2.49 and 2.49 Mb/s. Mean outgoing bandwidth estimates
were 4.56, 4.18 and 4.92 Mb/s. The worker's encoded-rate samples varied
between 0.37 and 3.19 Mb/s across the overlaps, including its transition
from the pause menu. RTP byte deltas over the overlaps averaged 2.54, 2.32
and 2.25 Mb/s. The full receiver windows are the better measure of sustained
delivery. At about 2.53 Mb/s, the external encoder used the available video
budget during motion. Desktop's 3.52–3.53 Mb/s delivered roughly 40% more
data per frame at the same cadence. The camera was moved reproducibly, but
these were different frames and no perceptual quality score was measured.

Compact Chrome viewing did not reproduce the earlier bandwidth reduction
seen with a compact Discord desktop viewer. WidgetRail also emitted about
one keyframe every four seconds, consistent with its recovery policy;
desktop decoded zero new keyframes during these reference windows. The
remaining questions are how to obtain a higher provider-negotiated video
budget and whether fewer periodic keyframes improve fidelity within that
budget while preserving late join and decoder recovery. Evidence is in
the same directory as the stock runs, under `widget-*`.

#### Encoder quality at an equal frame sequence and bitrate

Captured 480 consecutive 1280×720 frames from the 007 window during fast
camera movement. Every encoder received the same 30 FPS I420 sequence.
The reference is losslessly archived with a verified SHA-256 digest.
Measured unscaled 720p VMAF, luma PSNR and SSIM after the first 30 frames.
The WebCodecs cases used the installed candidate's two Blink flags and
emitted Constrained Baseline SPS `42c01f`. The duplicate control produced
identical bytes. An earlier D3D12-flag run emitted `42001f` and is excluded.

| Local encoder and recovery interval | Actual elementary stream Mb/s | Mean VMAF | 10th percentile VMAF |
| --- | ---: | ---: | ---: |
| Candidate settings, four seconds, 2.5 Mb/s target | 2.518 | 79.01 | 70.24 |
| Same settings, initial keyframe only | 2.501 | 78.77 | 70.29 |
| Candidate settings, four seconds, 3.5 Mb/s target | 3.509 | 88.47 | 79.28 |
| Same settings, initial keyframe only | 3.504 | 88.39 | 79.36 |
| Direct NVENC P4 low-latency CBR at matched output | 2.518 | 80.34 | 71.69 |
| Direct NVENC P7 full-resolution multipass | 2.495 | 80.35 | 72.44 |

Retain the four-second recovery interval. Its four keyframes used only
1.48% of encoded bytes at the lower target, and removing the three periodic
keyframes produced no fidelity gain here. Paced WebCodecs quality and realtime
modes emitted identical bytes. Variable bitrate improved mean VMAF by 0.50
but used 2.64 Mb/s on average, peaking at 4.35 Mb/s over a sliding second
versus 3.13 Mb/s for CBR. Retain CBR and realtime for this candidate.
Direct NVENC at equal output recovered about 1.3 points. This single clip
does not justify a platform-specific encoder integration. The direct NVENC
benchmarks measured picture quality, not end-to-end interactive latency.

The W3C sender maximum is an upper bound subject to other constraints. Discord
documents desktop resolution and frame rate controls and says browser streaming
quality cannot be adjusted. No public browser control for requesting the
desktop encoder allocation was identified. In a separate local experiment,
the packaged external sender accepted low → high → low RTP priority on the
same connection. Settled allocations averaged 2.17, 2.21 and 2.22 Mb/s. Its
estimated available bandwidth was only 2.33 Mb/s during the high-priority
window. This did not demonstrate higher allocation and cannot establish a
service limit. None of these experiments changed the installed package.
[Discord Go Live](https://support.discord.com/hc/en-us/articles/360040816151-Go-Live-and-Screen-Share)
and [WebRTC sender parameters](https://www.w3.org/TR/webrtc/#dom-rtcrtpencodingparameters-maxbitrate).

There are two relevant paths in upstream libwebrtc. An explicit per-encoding
maximum can take precedence over the codec hint when creating the encoder
configuration. Separately, the send channel calls `GetBitrateConfigForCodec`
and passes the result to the transport controller's `SetSdpBitrateParameters`.
A session bandwidth field can change that second limit. The stock Discord
Web sender's three recorded remote answers all contained the 2500 codec
hint and none contained an AS or TIAS bandwidth line. This gives a concrete
browser-side explanation for a 3.5 Mb/s encoding maximum coexisting with a
lower allocation. The deployed Edge binary's internal limit was not traced,
and this observation does not establish that Discord's relay polices the rate.
[Upstream send-channel implementation](https://webrtc.googlesource.com/src/+/5c5b140751e0f4907e092c7a7b59981787d642e8/media/engine/webrtc_video_engine.cc).

Quality evidence is under `007-keyframe-quality-1795491748842` in the local
diagnostics directory. Files with the `ship-` prefix use the candidate's
runtime flags. The 3.5 Mb/s quality measurements used local encoders with
an explicit target. They do not represent a 3.5 Mb/s live WidgetRail share.

#### Quiet-to-motion frame bursts and native encoder candidate

With 007 focused, repeated seven seconds of still camera and twenty seconds
of fast camera turns three times on a single share per test. At 720p30
Clarity, candidate 0.1.0.68 had seven freezes in each of two 80-second
runs (1.79 and 1.87 seconds total). Desktop had zero in both controls.
All four runs held nearly 30 decoded FPS on average. Chrome used the D3D11
decoder and UDP over Ethernet in both arrangements. The sender and receiver
accounts swapped roles between desktop and WidgetRail.

Chrome's receiver packet log reconstructed 2,436 complete WidgetRail frames.
Median video payload was 9,294 bytes. Eight frames exceeded 100 KB, peaking
at 179,217 bytes. Their original packets took 171–308 ms from first to last.
The absolute-send-time extension spanned 165–298 ms for those frames. The
receiver did not need a retransmission to complete any of the eight. This
locates the delay upstream of decoding. A relay can rewrite send times, so
the receiver trace alone cannot distinguish send pacing from relay pacing.
Desktop's median was 14,377 bytes and its largest was 82,426 bytes.

Replayed a still frame followed by the original camera movement three times.
WebCodecs produced a **197,490-byte delta frame** when movement resumed,
with and without periodic keyframes. It contained coded picture data and no
filler NAL. The direct NVIDIA encoder also overshot with its default buffer.
At the same 2.5 Mb/s target, a 100 ms buffer reduced its largest frame from
243,509 to 46,215 bytes and its 99th percentile from 128,422 to 22,306 bytes.
A tighter buffer did not consistently produce a smaller worst frame.

The Windows Media Foundation hardware encoder honored the documented H.264
buffer size when passed **before input type negotiation**. Setting it afterward
could report success but read back the previous size. A 31,250-byte buffer at
2.5 Mb/s produced Constrained Baseline `42c01f` and reduced its 99th-percentile
frame from 148,806 to 20,096 bytes on the same staged input. A live target
change to 1 Mb/s and back yielded 2.51, 1.07 and 2.46 Mb/s in the three
settled motion windows. Both explicit keyframe requests produced IDRs on
the requested frame. A paced sample-grabber probe returned 479 of 480 frames
before finalization. These probes used this machine's NVIDIA adapter.
[Windows H.264 buffer units](https://learn.microsoft.com/en-us/windows/win32/directshow/avenccommonbuffersize-property)
and [dynamic encoder configuration](https://learn.microsoft.com/windows/win32/api/mfreadwrite/nn-mfreadwrite-imfsinkwriterencoderconfig).

**Implementation.** `WIDGETRAIL_EXPERIMENTAL_NATIVE_H264=1` enables the
host-side candidate for 720p30 detail capture. A widget also needs the opt-in
encoded-source backend and a compatible negotiated H.264 sender. The worker
feeds the existing scaled GPU texture to Media Foundation and retains the
raw track for preview and fallback. A bounded access unit accompanies an
authorized raw frame in the same read-only mapping and shares its epoch and
credit. Raw pixels, audio, the account's original sender, and its encryption
remain on their established paths. The browser's real allocation sets the
encoder target and buffer size. Zero allocation suspends encoding. Stale
reader generations cannot update a replacement. Unsupported hardware,
an incompatible bitstream or a stalled native reader falls back to WebCodecs.

| Consumer | Effect and verification |
| --- | --- |
| Discord experimental outgoing share | Native H.264 only after capture consent, source ownership checks and successful encoded-source attachment. Live tests use the test lobby, selected game window and independent Chrome receiver. |
| Ordinary Discord build and other WebView captures | Raw track stays available. The ordinary backend does not request an H.264 reader. Covered by shared JS and capture-worker credit, epoch and teardown tests. |
| Capture Library / replay | Shares the GPU encoder implementation with distinct settings. Its recording and replay constructors keep their existing defaults. Tested with the native replay worker save, cancellation and cleanup fixture. |
| Game Help, SDK Gallery, templates | No native H.264 reader request in these consumers. Screenshot, normal capture and template paths do not opt into encoded-source delivery. |

**Live result.** Sanky sent the 007 application window from WidgetRail on the
test server and Sanky1 watched in Chrome. Each measured run included three
quiet-to-motion cycles. The final desktop control used the same machine
and game with the account roles reversed. All rows held 1280×720 throughout.

| Sender | Duration | Decoded FPS | Mean receiver Mb/s | Freezes / drops |
| --- | ---: | ---: | ---: | ---: |
| 0.1.0.68 WebCodecs | 79.69 s | 29.90 | 2.452 | 7 / 0 |
| 0.1.0.68 WebCodecs, repeated | 79.82 s | 29.96 | 2.427 | 7 / 0 |
| Discord desktop reference | 80.82 s | 30.03 | 3.514 | 0 / 0 |
| Native feasibility build, verified gameplay | 80.63 s | 29.99 | 2.493 | 0 / 0 |
| 0.1.0.70 Windows encoder | 80.75 s | 29.94 | 2.303 | 0 / 0 |
| 0.1.0.70 after minimizing and restoring the game | 80.56 s | 29.87 | 1.957 | 0 / 0 |

A preliminary 0.82 Mb/s native run lacked positive gameplay confirmation
and was excluded. During the 0.1.0.70 packet recording, the largest complete
frame was 39,690 bytes, and worst first-to-last-packet time was 107 ms
(179,217 bytes and 308 ms on the WebCodecs control). The native path follows
the transport's allocation. The post-restore run began after the allocation
fell while the picture was black, and finished with a 2.5 Mb/s allocation.
No larger bandwidth grant or universal sharpness parity was demonstrated.

**Lifecycle and compatibility.** A Chrome viewer left and rejoined the
same native share and decoded 784 frames in the measured 26.14 seconds.
The pre-minimize-hardening build transferred a running sender to WebCodecs
on native source loss; Chrome decoded 1,847 subsequent frames on that peer.
The final worker treats minimize as temporary, submits a black GPU texture,
then resumes the game on the native encoder. Confirmed the black picture
on the receiver and native encoding after restore. The worker delay-loads
Media Foundation so the unused optional path is not a process-start dependency.
Actual Media Feature Pack removal, other GPU models, 1080p60 and simultaneous
application audio on the final live native path were not tested. The incoming
desktop stream displayed the selected game's picture in a real WidgetRail
pin; the test pin was removed. Stop sharing released the worker while the
call remained available. Candidate packages are local and unsigned.

The final host is in `frontend-final` under the evidence directory, with
the checked `native-release` capture worker and texture library. The host
uses `WIDGETRAIL_EXPERIMENTAL_NATIVE_H264=1` and WebView2 Blink features
`RTCEncodedSource,RTCEncodedVideoFrameConstructor`. Build the consumer
with `Build-Package.ps1 -ExperimentalEncodedSource`. The package staged as
0.1.0.70 is selected on the default profile. **Stream details** identifies
the active path as **Windows hardware H.264** or **WebCodecs H.264 (fallback)**.

The final verification passed 204 JavaScript tests, 1,076 managed Discord
checks, and the native capture and replay worker regression suites. Confirmed
the optional Media Foundation libraries appear only in the capture worker's
delay-load imports. The original desktop share was restored; the original
Chrome viewer decoded 2,310 frames after reconnecting. Diagnostic packet
recording was switched off and desktop automation was stopped.

### Where the 720p30 allocation comes from

On this PC, Discord Web's Go Live quality manager selected 3.5 Mb/s for
the active outgoing 720p30 share. In 13 inspected public client bundles,
Discord's local answer builder supplied a fixed `videoBitRate:2500` when
constructing the video section from the incoming transport description.
This produced the H.264 codec hint seen in the sender's remote description.
The observed transport description contained no explicit video bandwidth
limit. Receiver-side SDP describes a separate connection to the relay.

The early 6 Mb/s experiments correctly changed what the sender reported
for encoding parameters or the codec hint. In the control trace, WebRTC
initially registered a 3.5 Mb/s request, configured the encoder at a
2.5 Mb/s maximum, and registered that lower maximum with the allocator.
In upstream libwebrtc the single-stream factory also defaults to 2500 kb/s
at 720p when a maximum is unspecified. The trace identifies the applied
encoder ceiling. It does not expose every branch in the deployed binary.

Supplying the selected preset as the outgoing video section's media
bandwidth changed the native send channel's `bandwidth_bps` from -1 to
3500000. The encoder and allocator then both registered 3500 kb/s. The
experimental adapter checks the exact owning stream, original provider
builder and selected preset on each negotiation. An explicit incoming
bandwidth limit, an unfamiliar builder or a different preset preserves
the provider's original answer. Discord continues to own signaling,
encryption and dynamic sender limits. The capture worker follows its
live WebRTC allocation.

| Candidate | Change | Chrome receiver during 007 motion |
| --- | --- | --- |
| 0.1.0.71 | Align only the local H.264 hint with the selected preset | 1.33 and 1.35 Mb/s in 45- and 33-second windows. Encoder target still topped out around 2.5 Mb/s. |
| 0.1.0.72 | Align the local hint and supply the selected media bandwidth | 2.928 Mb/s over 44.32 seconds, including 0.053 Mb/s of retransmitted payload. 29.96 decoded FPS at 720p with zero freezes or decoder drops. Mean sampled sender target was 3.023 Mb/s. |
| 0.1.0.73 | Supply media bandwidth alone, preserving the original codec hint | Encoder and allocator ceilings reached 3.5 Mb/s. Chrome received 1.336 and 1.328 Mb/s over 44.32 and 34.27 seconds. Two further 44-second runs with horizontal and vertical turns received 1.483 and 1.459 Mb/s at 29.98 and 29.96 FPS with zero freezes or decoder drops. |
| 0.1.0.74 | Final packaged version with both fields aligned to the provider preset, fresh share, verbose logging off | Over two consecutive 44.33- and 44.34-second windows, Chrome received 3.171 and 3.095 Mb/s. Excluding retransmissions, the rates were 3.095 and 3.001 Mb/s. Both held 1280×720, at 29.89 and 29.86 decoded FPS, with zero freezes or decoder drops. |

The first final run used horizontal turns, the second horizontal and vertical
turns. Excluding retransmissions, 87 of 88 complete receiver intervals exceeded
2.5 Mb/s. After the viewer stopped watching and rejoined, a further 34.28 s
of 007 motion delivered 3.137 Mb/s (0.088 Mb/s retransmissions), 29.96 FPS
and zero freezes or decoder drops. The earlier desktop reference averaged 3.514 Mb/s on its separate
quiet-to-motion workload. Picture-quality parity with desktop and performance
on other GPUs, networks and resolutions remain unverified.

This change is isolated to the experimental Discord provider backend. An
ordinary backend build excludes the negotiation hook. Stream details reports
the remote codec hint, media bandwidth, live allocation, actual encoded rate
and sender maximum separately. The four relevant JavaScript suites passed
208 tests, and the managed Discord harness passed 1,076 checks.
The default test profile has 0.1.0.74 selected with verbose logging off.
Stopping the widget share released its capture worker. After restoring
the original desktop sender, regular Chrome decoded the 007 share at
29.97 FPS over 22.12 seconds. Desktop automation was stopped.

The actual encoded rate can stay below both the ceiling and current
allocation. For example, in the last two 0.1.0.73 windows sampled
sender targets averaged 2.51 and 2.49 Mb/s. The comparison measures
different live congestion histories and camera sequences. It cannot
isolate the codec hint's independent contribution to delivered bitrate.
Compact evidence lives under `allocated-rate-1795501126565`, including
`provider-origin.json`, sender limit transitions and paired receiver totals.

#### Follow-up with the user's Discord desktop viewer

Reproduced visible blocking on 0.1.0.74 during 007 camera turns and movement.
The widget executable, managed assembly and provider backend matched the
verified package. Windows hardware H.264 was active. Independently recomputing
the earlier Chrome runs from raw inbound payload bytes, subtracting retransmitted
bytes, yielded 3.0954, 3.0014 and 3.0487 Mb/s. Those were Chrome-viewer results.

On this share the desktop viewer initially requested 1,922,700 pixels, then
101,468 pixels half a second later. Its large window was visibly showing the
game. Stream details showed a 3.5 Mb/s media bandwidth and codec hint, but a
0.55 Mb/s current sender maximum. The receiver averaged 0.961 Mb/s over 20.23 s
of movement at 29.81 decoded FPS. The sender maximum is a point-in-time sample.

Switching the viewer to full screen raised its request to 2,111,875 pixels and
the sender maximum to 3.5 Mb/s without restarting the share. The renderer
received 1.662 Mb/s over 30.34 s of movement with one freeze. Toggling back
to the large window requested 1,922,700 pixels. A subsequent receiver
reconnection again requested 101,468 pixels. After another layout toggle the
large window received 1.705 Mb/s over 20.23 s at 30.05 FPS with zero freezes.
All of these receiver samples decoded 1280×720.

The final matched movement trace shows where the remaining bitrate difference
was visible. Across six consecutive one-second sender samples the external
source reported targets of 2.90–3.20 Mb/s, a 3.50 Mb/s sender maximum, and
encoded output of 1.54–1.71 Mb/s. Desktop received 1.684 Mb/s in a complete
10.12-second movement interval. Receiver demand explains the initial low
ceiling. Raising it alone did not reproduce the earlier Chrome throughput.

Replayed the same 480 losslessly recorded 007 frames through the production
live H.264 encoder class and a paced Media Foundation sample-grabber sink on
this NVIDIA GPU. At frame 90 both encoders received the same 3.1 Mb/s target.
With a 552,960 b/s initial target, the two later five-second windows encoded
at 1.963 and 1.980 Mb/s. Starting at 3.1 Mb/s yielded 3.087 and 3.053 Mb/s
over the identical frames. Reversing test order reproduced all four figures.
The initial rate also determines the initial H.264 buffer size, so these
runs isolate initialization history, not an individual driver property.
The live share's exact initialization rate was not traced.

A separate probe tried changing the sink writer's target media type at
frame 90. This configuration rejected it with `MF_E_INVALIDMEDIATYPE`.
Results and probe sources are in `startup-rate-probe` in the allocation
evidence directory. No experimental retargeting change was installed.

#### Native encoder recovery and viewer demand

Discord's viewer supplies a pixel **area** for a stream. In the inspected
Discord Web client, `getGoliveQuality` retains the selected encode and capture
dimensions and lowers the bitrate range when the requested area falls. The
public browser sender can also specify an explicit resolution scale. The
fixed-size experimental encoder now returns the original track to WebRTC
when the outgoing encoding explicitly selects a smaller size, and is eligible
again when full size returns. The observed 007 shares remained 1280×720.

Stream details now displays the aggregate viewer pixel request for the unique
primary outgoing stream. The interface suggests toggling full screen on the
viewing device when its request is small relative to the capture. Discord
desktop logged a subscription ending and a new request for 101,468 pixels
while its large window was still showing the stream. WidgetRail independently
reported the same demand and a 0.55 Mb/s sender maximum. Toggling full screen
produced desktop requests for 2,111,875 and then 1,922,700 pixels. The active
sender maximum returned to 3.5 Mb/s. WidgetRail cannot determine the correct
size of another client's window or prevent that client from subscribing again.

On this GPU, a dynamic mean-rate update succeeded but the Media Foundation
encoder retained its startup H.264 buffer size. Reapplying both codec properties
directly still read back the original 6,912-byte buffer. The opt-in native
worker continues applying allocations immediately. If the target stays above
1.5× or below two thirds of the buffer's original rate for two seconds, it
checks the driver's actual buffer. A material mismatch starts at most one
replacement writer on the same multithread-protected D3D device. The current
writer keeps encoding until the replacement is ready. The worker drains old
frames before the new keyframe and discards preparations invalidated by a
generation change, suspension or demand reversal. If the second encoder
cannot open, the existing one continues. Driver-specific recovery on other
GPUs and operating systems has not been verified.

The synchronous trial took about 200 ms to initialize a replacement. Desktop
counted a 259 ms freeze around one resize. The final asynchronous probe delivered
all 480 source frames across the upshift and downshift. Neither replacement
blocked a capture submission for over 5 ms in the timed run. A separate run
suspended and replaced the source during preparation, checked generation,
keyframes and timestamps, and completed without stale output.

| Discord desktop receiver, 007 in motion | Seconds | Mb/s | Decoded FPS | Freezes / drops |
| --- | ---: | ---: | ---: | ---: |
| Previous 0.1.0.74, full screen after thumbnail | 30.34 | 1.662 | 29.60 | 1 / 0 |
| Previous 0.1.0.74, large window after refreshing demand | 20.23 | 1.705 | 30.05 | 0 / 0 |
| Final worker, thumbnail demand after reconnect | 30.37 | 0.760 | 29.67 | 0 / 0 |
| Final worker, full screen after thumbnail | 40.48 | 2.854 | 29.96 | 0 / 0 |
| Final worker, windowed viewer resubscribed at thumbnail demand | 30.35 | 0.741 | 29.06 | 0 / 0 |
| Final worker, large window after refreshing demand again | 40.43 | 2.450 | 29.88 | 0 / 0 |

The measured 50.59 s spanning the final thumbnail-to-full-screen layout
change included a paused menu and had zero freezes or decoder drops. Desktop
receiver Mb/s uses changes in video bytes received. Its log lacks a separate
retransmitted-byte counter, so these are total receiver rates. The agreed
maximum is a budget and the produced rate varies with motion and allocation.
Desktop-sender visual parity was not established.

For the matched quality comparison, both runs encoded the same 480 recorded
007 frames with the production capture encoder class. The target changed
from 552,960 to 3,100,000 b/s at frame 90 and dropped again at frame 375.
Over frames 180–373, baseline and final output averaged 1.980 and 3.086 Mb/s.
Mean VMAF against the lossless reference was 71.10 and 85.08. Both 194-frame
sequences use frame-index timestamps at 30 FPS. Preliminary timestamp-based
scores used different input time bases and are superseded.

| Consumer | Effect |
| --- | --- |
| Experimental Discord 720p30 Clarity | Uses the verified allocation recovery. Viewer demand is read only on requested diagnostics. Actual explicit sender downscaling hands back to WebRTC. The latter lifecycle was exercised in a controlled adapter test. |
| Ordinary Discord, other capture qualities and other web widgets | The new writer requires the existing native H.264 opt-in. No new public SDK or protocol fields. |
| Capture recording and replay | Share the encoder implementation; the buffer readback and recovery paths execute only for live H.264. |

Local evidence lives in `rate-recovery-1795505993185` under the allocation
investigation directory. It includes `desktop-windows.json`, aligned VMAF
reports, paced frame logs, native workers and the rollback copy. The selected
widget package is 0.1.0.75. The separate final native build is `native76`.
Verified the running worker against that build by SHA-256. All 211 selected
JavaScript tests, 1,076 managed Discord checks and the capture and replay
worker regression suites passed. The test share remained connected with
007 as its source and Discord desktop in the large windowed layout.

#### Initial windowed joins after the native candidate

In four subsequent desktop viewer subscriptions at 00:23:42, 00:24:57,
00:25:30 and 00:27:42 local time on November 24, the first nonzero request
was 1,922,700 pixels. Between 0.67 and 3.57 seconds later, the same
receiver requested 101,468 or 102,600 pixels for the same stream. This
rules out a missing first size announcement in these examples. The later
request is indistinguishable from a legitimate small viewer at the sender.

The first of these streams decoded at 1920×1080. During one 10.125 s
interval before enlargement it decoded only 47 frames (4.64 FPS), with
26 new keyframes and no increase in decoder drops. The subsequent stream
decoded 1280×720 and reached approximately 60 FPS in settled intervals.
Neither profile uses the 720p30 Clarity native candidate. These logs do
not identify which receiver UI surface issued the smaller request.

#### Fixed-budget picture-quality check at 720p30 Clarity

Replayed the same 480 recorded 007 frames through the candidate's native
capture encoder, first at a constant 552,960 b/s target and then at
3,500,000 b/s. Both runs emitted all 480 frames in order. Over the same
194 motion frames (indices 180–373), encoded video averaged 1.087 and
3.489 Mb/s respectively. Frame-aligned mean VMAF against the lossless
reference was 40.59 and 88.25. The low-rate run overshot its target.
These are encoder measurements without a Discord connection.

This test isolates the benefit of a larger **allocation**. Merely keeping
a larger WidgetRail ceiling cannot produce that allocation: the worker
already uses WebRTC's allocated rate and then applies the sender maximum.
The stream preset exposes the session maximum. The active encoding reports
the final per-sender maximum without a breakdown of contributing limits.
A positive viewer count establishes that someone is watching; it cannot
distinguish a stale small request from a genuine thumbnail. No live override
was installed or tested in this comparison.

#### Windowed-view Clarity candidate 0.1.0.76

The experimental Discord provider binds the local Go Live preset calculation
for the exact 1280×720, 30 FPS Clarity share. For a confirmed viewer and an
identified size-derived maximum, it retains the negotiated preset maximum and
preserves the requested pixels, minimum, target and encoding dimensions.
Unknown method bodies and separate lower limits fall back to provider behavior.
The observer reconciles a viewer joining after the initial size request and
restores the original method on retirement. This required no native worker change.

The packaged backend passed controlled checks against both recognized selector
implementations across 13 cached builds. The JavaScript suites passed 329 tests
and the managed Discord harness passed 1,078 checks. The user confirmed
that this candidate worked in their live test. A 2560×720 ultrawide share
showed only the first three lines of Stream details.

#### Clarity presets and ultrawide candidate 0.1.0.77

| Consumer | Status | Validation |
| --- | --- | --- |
| Discord Controls experimental Clarity | Implemented for 480p, 720p and 1080p at 15, 30 and 60 FPS. The size-based ceiling policy can apply with the regular WebRTC sender. External H.264 requires a compatible negotiated level, and a smaller requested layer returns to WebRTC. | JavaScript fixtures cover all nine presets in both 16:9 and ultrawide geometry, encoder selection, explicit downscaling, viewer changes, and provider bandwidth limits. Windows hardware encoding produced valid SPS levels and keyframes for 15 synthetic modes, including 2560×720 at 15, 30 and 60 FPS. Live Discord transport at the new presets awaits user testing. |
| Normal Discord Controls, Smoothness, and host web app capture | The normal package leaves the experimental provider out. Smoothness uses regular WebRTC. Discord Controls is the only shipped widget that calls the capture stream capability directly. Web app capture continues to use the host transport and its own sender. | Ordinary and experimental backend composition passed. The native capture worker, mapped video and audio credit, pacing, and teardown probes passed. |
| Replay and standard recording | These consumers also use the native video encoder. The explicit H.264 level is supplied only by the opt-in live encoder. | The replay worker probe passed its save, pause, cancellation, and teardown cases. |
| SDK and templates | No public SDK or protocol changes. No templates, SDK examples or first-party widgets call the opt-in encoder. | Consumer search covered shipping samples, first-party widgets, templates and SDK examples. |

Stream details now wraps within the call page and shows the measured capture,
sent and advertised dimensions, the encoder path, and the active Clarity rule.
A 2560×720 capture needs H.264 Level 4.0 at 30 FPS. When the relay negotiates
Level 3.1, the ordinary WebRTC sender carries the ultrawide capture.
The 1440p host transport was exercised with synthetic GPU input. The Discord
widget retains the account-eligible resolutions it already offers.

In the user's 1080p30 test, Stream details reported 1920×1080 sent at 29 FPS
through ordinary WebRTC and a 9 Mb/s negotiated bandwidth limit. The
diagnostics did not include the negotiated H.264 level.

#### Smoothness preview bitrate candidate 0.1.0.78

The same per-stream preview bitrate policy applies to both capture
preferences. Smoothness keeps the original WebRTC video track so the encoder
can adapt its resolution to maintain frame rate. An explicit provider
downscale, a paused stream, or a lower negotiated limit keeps the
provider's bitrate decision. Stream details identifies when the preview
bitrate policy is active. The 720p30 Clarity startup initially reported
WebRTC in the user's snapshot. The active capture worker subsequently
loaded NVIDIA's H.264 transform and used its GPU Video Encode engine.
Stream details now labels the external encoder preparation period.
The user confirmed 0.1.0.78 worked in their live test.

#### Discord capture choices in candidate 0.1.0.79

Discord Controls now offers 1080p at 15 or 30 FPS. The user requested removal
of 1080p60 after testing. Saved 1080p60 and 1440p60 settings select 1080p30 when
the account and call support it. The widget retains 60 FPS at 480p and 720p.

## Earlier Deadlock packet timing

The original collapse happened between WebRTC's input and encoder output. With
accelerated Clarity, the remaining freezes can occur even when interval totals
show the expected number of encoded and decoded frames. A matched receiver
freeze count and one-second minimum are necessary alongside average FPS.

The final matched timing evidence is in `d3d12-packet-live-059-{a,b}-samples.json`
under the diagnostics directory. The Chrome dump (15) covers both sweeps.
The worse sweep started at 14:32:50.064 UTC and ended at 14:33:00.079 UTC.

The documented Discord Social SDK Call surface exposes audio and voice-call
controls. Its public member list has no native video sender for the signed-in
user's existing Go Live session. On the current route, Discord chooses the
single negotiated video format and WebView2 owns its encoder and packet pacing.
The live codec trial and provider bitrate readback above establish those limits
for this test lobby. See the [Discord Social SDK Call reference](https://discord.com/developers/docs/social-sdk/classdiscordpp_1_1Call-members.html)
and [WebRTC codec negotiation](https://www.w3.org/TR/webrtc/#dom-rtcrtptransceiver-setcodecpreferences).

The software control used WebView2 157.0.4341.60 and process-local verbose
logging. The encoder configuration initially requested 3.5 Mb/s and resolved
to a 2.5 Mb/s stream limit. A later sender reconfiguration set the encoding
limit to 1.679 Mb/s before both turns. The sender's codec match records
constrained baseline `42e01f` with `x-google-max-bitrate=2500`. The receiver
offered other H.264 profiles and codecs in six recorded sessions and its remote
description consistently selected only `42e01f` plus RTX. This rules out
choosing the hardware-supported `42001f` profile on the existing negotiation.
The traces identify OpenH264's internal frame skipping as the dominant loss
in this session. The separate WebRTC
rate limiter emitted no drops during either ten-second turn. Upstream WebRTC
enables frame skipping for its single-layer OpenH264 encoder and counts those
drops separately from its queue and rate limiter. [OpenH264 integration](https://webrtc.googlesource.com/src/+/refs/heads/main/modules/video_coding/codecs/h264/h264_encoder_impl.cc)

The raw sender evidence is `webrtc-drop-reasons.log` in the local diagnostics
directory. Discord desktop received the stream using the second test account
because Windows Computer Use stopped at Chrome's screen-share chooser. The
desktop viewer and WidgetRail sender disconnected, the game binding was
restored, and WidgetRail was relaunched with its normal browser arguments.
The accelerated trace is `webrtc-d3d12-drop-reasons.log` and the matching partial
counter recording is `d3d12-clarity-verbose-live.nettrace`. In the severe Clarity
run the video bandwidth estimate fell from about 2.18 Mb/s to 1.06 Mb/s.
The encoder kept returning output when called, while WebRTC dropped frames
under its send budget and drew down the pacing queue. The logs establish
the sender response to that bandwidth estimate. They do not establish what
triggered the estimate change or whether changing the pacer would improve
receiver cadence under comparable network conditions. Global WebRTC
frame-dropper changes would also leave the software skip mechanism enabled.

The hardware-only comparison used the same WebView2 version and the
`webrtc-d3d12-smoothness-fresh.log` sender trace. Smoothness enters WebRTC's
real-time video mode, where resolution can adapt to preserve cadence. Hardware
encoding selection is still a runtime choice. WebRTC sender parameters have no
hardware preference, and Microsoft describes WebView2 browser flags as
development diagnostics. A shipping change needs a supported hardware path or
equivalent verified behavior under normal runtime configuration.
[WebRTC sender parameters](https://www.w3.org/TR/webrtc/#rtcrtpsender-interface)
[WebView2 browser flags](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/webview-features-flags)

The 16:26:53–16:26:55 UTC receiver intervals in dump (22) isolate the
remaining 720p freeze from encoder throughput. Chrome counted 240 packets,
including 20 retransmissions, and assembled 17 frames in the first interval.
The next interval assembled 43. Mean time to assemble a multi-packet frame
increased from 3.7 ms before the gap to 22.6 ms and 20.5 ms in the affected
intervals. The receiver kept getting packets. Measure sender packet delay
against the same intervals in another accelerated Smoothness sweep to determine
where the late packets accumulated.

The later paired sweep provides a second, directly classified reproduction.
The first rate-control drop in its worst interval used a 2.5 Mb/s target
with 30 FPS source input. The prior sampling interval sent 3.31 Mb/s and
averaged 64 ms of local packet delay. The trace identifies the D3D12 encoder
as hardware accelerated with `has_trusted_rate_controller = 0`. Upstream
WebRTC keeps its frame dropper active in that case. The Chrome interval had
zero new decoder drops or net packet loss. This establishes the pre-encode
drop path for these two sweeps. It does not classify the separate dump (22)
freeze where encoder cadence held. The 4.54 Mb/s value is an end-of-interval
connection estimate, while the dropper uses an encoder target and recent
encoded frame sizes.
[WebRTC encoder drop path](https://webrtc.googlesource.com/src/+/refs/heads/main/video/video_stream_encoder.cc)
[WebRTC frame dropper](https://webrtc.googlesource.com/src/+/refs/heads/main/modules/video_coding/utility/frame_dropper.cc)
[WebRTC statistics definitions](https://www.w3.org/TR/webrtc-stats/)

The paired evidence is in `paired-packet-delay/sender.log`, `stream.nettrace`,
`repeat.nettrace`, and Chrome dump (25). The active candidate was checked
without the experimental encoder or frame-dropper flags.

## Independent reports and fixes

- Cisco's OpenH264 tracker and Mozilla's WebRTC tracker both have first-hand
  Discord Web reports of about 5 FPS with software H.264 while capture or VP8
  remains smooth. Our Firefox-independent WebView2 result has the same symptom.
  The VP8 workaround requires a mutually negotiated codec, which the live
  WidgetRail trial did not obtain in this lobby.
  [Cisco report](https://github.com/cisco/openh264/issues/3970)
  [Mozilla report](https://bugzilla.mozilla.org/show_bug.cgi?id=2091226)
- A Sharkord reproduction on Windows with Chrome 157 isolated the same
  `42e01f` software fallback while other H.264 profiles used hardware. It
  changed the negotiated profile on its own service. Chromium's D3D12 rollout
  change separately documents that constrained-baseline acceleration is gated.
  [Sharkord reproduction](https://github.com/Sharkord/sharkord/issues/942)
  [Chromium change](https://chromium.googlesource.com/chromium/src/+/4c2d53207162e5f93c725e3929f18ee619e4db23)
- The Yapfast maintainer reproduced OpenH264 losing most frames during game
  motion and addressed it by owning the encoder, using GPU encoding with a
  different software fallback. Discord's own Go Live team documented another
  feedback loop where rate adjustments increased bits per remaining frame,
  causing WebRTC to drop even more frames. They fixed it by leaving headroom in
  their native encoder. These fixes rely on selecting and configuring the encoder
  directly. The existing WidgetRail capture encoder also needs a supported route
  into the Discord sender before it can replace WebView2's encoder.
  [Yapfast 0.3.0](https://yapfast.app/blog/yapfast-0-3-0/)
  [Discord engineering](https://discord.com/blog/from-blocky-to-brilliant-improving-video-quality-on-discord-go-live-on-amd-gpus)
- Chromium fixed a near-zero-FPS case by deferring to the encoder's rate
  controller for a specific Intel HEVC path. Microsoft also added H.264 rate
  controller-owned frame dropping to Chromium's Media Foundation path. These
  are precedents for an encoder-specific fix. The measured stalls use the
  D3D12 path on NVIDIA, and Media Foundation failed to initialize the
  negotiated profile in our test.
  [Chromium Intel HEVC change](https://chromium.googlesource.com/chromium/src/+/b0def6201667f43558fd6a690cb71865c06d3efd)
  [Chromium Media Foundation H.264 change](https://chromium.googlesource.com/chromium/src/+/489a20e381d2497f46c70a0e5439ba680d98d741)
