# Discord call/media completion audit

## Current candidate

The isolated catalog selects Discord **0.1.0.47** and Game Help **0.1.9** on the trimmed Release host
`inline-playback-release-47`. The host uses the default user profile. The package
identity, optional capture permission, and reviewed archive digest are recorded
in `send-read-review-47.txt` and `combined-game-help-review-46.txt`.
The running host and enabled package versions are recorded in `inline-playback-candidate-47`.
Its `installed-file-check.json` verifies that all 38 Discord and 13 Game Help
files in the isolated catalog match the reviewed archives byte for byte. The
only additional file in either installation is the catalog's integrity manifest.
`inline-playback-candidate-47/fixture-separation.json` compares the compiled
frontend in the running candidate's directory with the native validation host.
All eight checked fixture methods appeared in the validation binary and zero
appeared in Release. These include the playback timing callback, timing
fixture, and resume-cache inspection method added for .47. The Release
assembly also contained no validation namespace types or embedded session
fixture resources.

| Surface | Verified on the current code path | Live acceptance still open |
| --- | --- | --- |
| Watching a call stream | Watch, fullscreen, same-stream pin handoff, stream audio controls, and focus after the sender stops. A pending connection survives leaving and reopening Watch. The sender name stays visible at 420×360 with 150% text. See `pending-pin-fixed-44.log`, `compact-stream-fixed-45`, and `expanded-stream-fixed-45`. Unpinned hide/resume passed for both the viewer and fullscreen while two unrelated pins continued decoding (`combined-visibility-native-46`). | A second participant's picture and sound across these transitions. |
| Sending and dialog transitions | Both native sizes show the newly confirmed message above the focused composer (`sent-reveal-host-fixed-46`). The two call dialogs leave the call section stationary across 250 sampled native frames (`dialog-motion-46/modal-content`). | Real account delivery timing. |
| Inline video return and fullscreen | `inline-fullscreen-46/placement-native` checks offscreen pause, explicit resume, fullscreen picture, playhead and audio in a native indexed chat. `public-inline-cache-47.json` measured three cold HTTP requests and zero on warm resume. `media-retirement` checks rapid section switching and preserves real format failures. | Startup on the user's specific attachment and network. |
| Unread state | Sending from older history no longer submits the outgoing tail as a new read target. See `send-read-fixed-native-47`. Opening a conversation, reading older history, notification jumps, and hiding during a pending read are covered. Both sizes of `pinned-chat-read-host-44` kept the marker unchanged while only a voice pin was visible. | Agreement between the signed-in user's Discord clients after a real incoming message. |
| Media attachments | `ranged-attachment-44` covers 97 transport tests and 17 native checks including forward/backward seeking and source retirement. `pinned-chat-read-host-44` includes 250 source checks and 186 minified/composed backend checks. | Device/codec variations and longer sessions. Animated WebP and PNG currently render a still frame. |
| Call and pin input | Native call routing and compact/enlarged pin layouts are covered below. `pin-escape-native-44` additionally confirms Escape from the opacity slider restores placement settings and keyboard focus. | Incoming/outgoing real-peer call behavior, microphone/device audio, and physical controller use. |
| Chat and Playnite scrolling | Bounded chat prefetch is qualified in both directions. The real 183-game Playnite replay in `library-viewport-animated-43` showed fewer long gaps and lower allocation after the artwork and indexed-lease fixes. | Physical right-stick acceptance on the updated host. The longest gaps in two runs of the retained implementation were 86 ms and 104 ms. The sporadic chat loading error remains open for a new diagnostic occurrence. |

## Current requirement/evidence map

Artifact paths below are relative to `artifacts/discord-call-media-polish` unless
stated otherwise. Counts were checked against receipts in this continuation.
Implementation evidence and native fixtures do not imply real-peer acceptance.

| Requirement | Current evidence | Remaining scope |
|---|---|---|
| Short content sizing, no false overflow | `short-typography-1`:54 native checks; shared content-sized measurement remains in place | Consumer deferrals in the plan still apply; no claim of a new live PoB/Marketplace sweep |
| Servers naming, contextual L3, direct call actions and notification entry | `dock-state-native-2` and `dock-state-accessibility-1`:45 checks per size each; focused private-call managed suite55 | Real incoming/outgoing peer/audio and physical controller acceptance |
| Participant dock, overflow and reconnect/disconnect | Same180 native checks cover changing counts/order, stable native Mute focus, fixed dock/composer geometry, summary entry and RB return; managed checks cover actual disconnect removal | Real provider timing and physical controller; no claim of frame-by-frame sampling for these new state changes |
| Rich conversation rows and truthful presence/snippets/unread/call state | Widget suite at the conversation-row checkpoint; actual photo/status/sidebar cold-entry smoke in .37; `static-portrait-native-1`:52 | Snippets use available exact-head cache only, with honest fallback; live peer unread agreement remains open |
| Quiet left-aligned messages, explicit child actions and URL deduplication | `link-gallery-native-1`:40 compact/49 expanded; `MessageTextTests` included in current suite; source preserves authored destinations | Exact live text-fragment case was not relocated; synthetic exact-target coverage is explicit |
| Older/newer paging without viewport rubberband or row blanking | `prepared-window-controller-1`:22 per size; `prepared-window-wheel-2`:21 per size; `prepared-window-authority-1`:24; `prepared-window-hide-2`:17 | Bounded delayed up/down/up and eviction evidence, not every Discord network condition; user requested no speculative load-error hunt |
| Sidebar text/photos across entry and chat switching | `static-paint-native-2`:9; `static-portrait-native-1`:52; `static-paint-animation-regression-1`:37; live .37 applicability | Static exact-source pixel continuity only; no transfer of old input/resource authority |
| Gallery item navigation, fullscreen controls, error recovery and return | `gallery-status-native-2`:27 decoded native checks; native media/gallery runs above; live Sanky outside-click dismissal observed on the earlier candidate | Live outside-click returned the same visible row; exact keyboard identity and all transient frames are covered only where the named native fixture asserts them |
| Single inline video; mixed attachments use gallery; per-widget audio memory | `media-design-lifecycle-35-live-followup`:inline26/audio9/passive-video12/remote-image9; prior live decoded playback | Codec/device coverage and long-running stability remain bounded |
| GIF/Klipy/Tenor projection, reduced motion and hidden/offscreen lifecycle | Current widget/provider contracts; native animation37; four `gif-resources-*-9` cases27 each,108 total; `gif-endurance-full-2` and `gif-endurance-large-canvas-1`:30 each, completing finite two-minute timelines | Native GIF/gifv only. Animated WebP/APNG were feasibility questions and remain unsupported. Endurance asserts expected terminal pixels and teardown, not presentation of every frame, all disposal modes, GPU bounds or hours-long stability |
| Public media security/resource limits | `public-media-burst-tests.log`:96 passed; checked transport, native bytes/container metadata and bounded admission | Existing documented byte/pixel/time/resource limits retained; no arbitrary page fetch or cookie forwarding; older legacy consumers remain explicitly deferred |
| Participant audio preview remains open on speaking updates | `participant-speaking-fixed-1`:10 native checks | Real multi-user call acceptance remains separate |
| Voice and passive-participant pins | `pin-label-standard-39` / `pin-label-accessibility-39`:123 each; widget suite at the .39 checkpoint; actual synthetic desktop screenshot review | Real voice/remote-video/audio continuity and physical hardware remain separate; default offscreen PNG material is not reliable visual evidence |
| Visual hierarchy, controller guide and accessibility layouts | Current dock screenshots at compact/wide and150% high contrast inspected; `link-gallery-accessibility-1`:40; earlier guide and theme evidence retained | Compact150% intentionally shows less message context; no reduced text size to conceal that tradeoff |
| Matched isolated Release delivery | Reviewed .47 hash `bf8046cb1306e1ce783dd38f259833409e1c56955bf2844b5ec681337da16353`. The trimmed host passed verification of 317 managed references and two async cleanup locks | No merge, push, installer or release. Current local candidate remains for acceptance |

Independent implementation/fixture work is qualified within the limits above.
Do not mark the overall goal complete until remaining peer/read-state/call/pin
and physical-controller acceptance is resolved or the user explicitly changes
that scope. Long-term stability is an observation period, not something inferred
from passing a short native scenario.

### Remaining live acceptance, with observable outcomes

These checks require the user/second test account; they are not invitations to
send unsolicited messages or change permissions. Continue using only the approved
test peer and Test server → Lobby. TriggerFinger remains read-only.

1. **Unread agreement:** receive a new message from the test peer while outside
   the conversation. Observe the unread indication, open the chat, then confirm
   that the same account's ordinary Discord client also shows it read. Receive a
   second message while reading older history: it should remain new until the
   newest end is presented. This checks real provider/server agreement rather
   than only the tested local `BULK_ACK`/read-store path.
2. **Private calls:** receive a call while the overlay is hidden; the host alert
   should open the exact invitation and focus Answer without answering. Explicit
   Answer/Call uses the user's microphone grant. Verify remote audio and Leave;
   verify Decline separately. Synthetic notification, focus, consent gating and
   lifecycle coverage already exists and need not be re-benchmarked.
3. **Watching and pinning a live stream.** In Test server → Lobby, have the
   second account share a moving picture with audible sound. Choose **Watch**,
   enter fullscreen, then press B to return to the viewer. Confirm the picture
   keeps moving and that stream mute affects the shared audio independently of
   the speaker's voice. Press Back to return to that sender. Choose **Watch &
   pin**, reopen the same stream with **Watch**, then hide the overlay. Confirm
   the picture returns to the pin and audio plays once. Also check the voice
   controls and participant pins during speaking and mute changes. To check
   the unpinned case, leave a participant pin visible, choose **Watch** on an
   unpinned stream, then hide the overlay. Shared audio should stop while the
   call continues. Reopen the viewer and confirm the live picture and sound
   resume.
4. **Physical controller:** check Left to selected conversation, Right to composer,
   contextual L3 focus without activation, held chat scrolling in both directions,
   and LB/RB in an attachment gallery. Native routed-input coverage is recorded;
   this checks the user's hardware, input ownership and perceived transitions.
5. **Everyday stability:** continue normal use and report any new crash, flash,
   jump or genuine load failure. Preserve the candidate version and approximate
   time so logs can be correlated. No fixed short fixture run proves long-term
   stability, and no new speculative message-load investigation is authorized
   without evidence under the user's latest instruction.

## Historical prepared-row .36 checkpoint, November 19

The loading-placeholder gap below is now addressed by bounded host row preparation
using existing indexed range leases. Preparation is limited to incremental paged
windows, with one coalesced successor, exact authority validation and owned cleanup.
Explicit entry/reveal requests retain their established path. The implementation
and consumer matrix are in the final sections of the polish plan.

Evidence under `artifacts/discord-call-media-polish`:

- `prepared-window-controller-1`:22 compact/22 expanded checks, delayed up/down/up
  through actual eviction and return paging; no missing/backward/retired anchors.
- `prepared-window-wheel-2`:21 per size with native wheel notches; preparation
  frames are recorded, so this is not an accidental bypass of the new path.
- `prepared-window-hide-2`:17 checks, including suspension during preparation,
  drained ownership, retained sidebar and successful later paging.
- `prepared-window-authority-1`:24 expanded checks on final validation host7,
  including no revival of obsolete row authority while old paint is retained.
- `prepared-window-shared-1`:continuity16/end-following34/entry65/preview14;
  `prepared-window-return-1`:52. Earlier intermittent reveal failures remain
  recorded; no separate fix is claimed from these successful runs.
- `prepared-window-explicit-entry-1`:44 compact/43 expanded checks for explicit
  Latest/composer entry. `prepared-window-main-screens-1`:40/49, including gallery
  return and chat switching. Main captures were inspected; synthetic media URLs
  intentionally fail, so these qualify layout/fallback rather than real decoding.
- Prepared-source setup checks cover ready insertion, retained identity, exact
  query and occurrence rejection, abandoned preparation and reentrant teardown.
  The strengthened final setup/entry run is `prepared-window-source-final-1`.

The reviewed .36 package is now applied and enabled in the existing isolated
catalog. The matched trimmed, validation-disabled host is
`prepared-release-host-36c`; `prepared-release-host-36c-verification.log` passes317
managed-reference checks,2/2 cleanup locks and exclusion of fixture diagnostics.
Launch receipt: `prepared-window-candidate-36/launch.json`, PID55416. Default
settings and the isolated catalog are preserved. No merge, push, installer or
release was created.

The app's normal profile-election/show-existing path reopened PID53636 without
restarting it. Computer Use confirmed its server picker was idle; Alt+F4 then
closed it normally before the reviewed update. The new candidate is signed in.
Read-only live smoke opened TriggerFinger, loaded older history, returned toward
recent messages, and resumed keyboard navigation after wheel scrolling. No
loading error appeared, and no messages were sent or calls made. Point snapshots
are not frame-by-frame live smoothness evidence; the native fixtures supply that
bounded evidence. Candidate remains open on TriggerFinger.

The full goal is still incomplete. Live peer/read-state and remaining call/pin
acceptance, plus the newly recorded URL-card and transition polish findings,
remain. Do not mark completion from this scrolling checkpoint alone.

## Historical diagnostic follow-up, November 19

Latest continuation checkpoint: the source now also fixes focused-page eviction,
parked focus replay for controller/wheel input, and append anchoring. Passing
receipts include focused controller44 on host6, compact wheel21 on host9,
shared continuity16/entry65/preview14/return52 after wheel ownership, and
end-following34 on diagnostic host12. Preserve the two intermittent earlier
end-following timeouts; a passing comparison/rerun is not a diagnosed fix.

**Qualification remains incomplete.** `wheel-scroll-pipeline-1` reproduces brief
visible loading-placeholder gaps before range data arrives. This is not confined
to fast wheel input: `final-focused-controller-scroll-36b/minimum` also records
one missing-anchor frame on its first prepend, with zero backward/retired-anchor
samples and successful subsequent paging. Do not present the earlier44-check
controller pass as proof this remaining readiness issue is impossible. A bounded
prepare-before-insert proposal, framework references, source ownership and
required lifecycle tests are recorded at the end of the implementation plan.

Prepared **but not deployed or qualified**: `prepared-release-host-36b` is the
fresh trimmed, validation-disabled host matching the current production source.
`prepared-release-host-36b-verification.log` verifies317 managed references,
2/2 cleanup locks and absence of the named fixture diagnostics. It pairs with the
unchanged .36 widget archive below. The original `candidate-release-host-36`
predates the focused-row fixes and is superseded. All owned test windows closed;
the hidden live diagnostic host1/widget .35 remains untouched. No merge, push,
installer, release or account mutation occurred in this continuation.

The .35 checkpoint below is historical. Process54060 was closed and replaced by
the untrimmed Release diagnostic host `live-defects-host-1` (PID53636 verified
on this continuation), using `live-history-diagnostic-1/shell.json`. The installed
isolated widget remains .35. The participant and conversation-order fixes in
source are not yet a deployed, qualified candidate.

The user did not observe the message-load error in the live diagnostic run and
asked to defer investigation unless a cause was found. A subsequent focused-row
synthetic run reproduced a concrete host cause: offscreen keyboard focus was
included among protected page keys, preventing eviction at the configured
retained capacity. The provider succeeded; `WidgetCursorResource.Merge` rejected
the impossible retained window. This is not an injected error. The host now
protects visible rows only; the capacity guard remains intact. Final native
qualification is pending, and equivalence to every earlier live error is not
claimed.
Initial held-scroll runs reported wrong-direction motion in both directions;
the follow-up below separates retired-anchor measurements from the confirmed
upward jump. Failed anchoring/prefetch experiments were removed.

The native comparison isolated stale absolute scroll destinations, retired rows
reported as current anchors, and input cancellation switching WinUI's anchoring
mode before layout finished. These are corrected in source. Pending destinations
also reset on native extent changes. Eight numerical regressions pass.
`held-scroll-qualified-slow-1` now passes20 checks in each size: compact and
expanded up/down/up,500ms responses, no backward/missing/retired anchors, and
native tracking teardown. `history-hide-reopen-settled-1` passes15 checks,
including canceling a delayed page, retaining the sidebar's native source,
reopening without an error, then successfully loading another page. Its new
readiness rule excludes an offscreen native measurement container without a
registered renderer; it still waits for visible and loaded cache-row layout.

`layout-lifetime-final-regressions-1` passes indexed preview14, continuity16,
end-following34 and deferred-preview40. The ordinary preview fixture now checks
its actual offscreen/retirement requirement rather than an exact-zero offset
assumption that also failed on the old .35 host. The earlier74 scroll/focus and61
interactive-preview checks remain recorded with their respective source scope.
`qualified-media-screens-36` passes compact40/expanded49 checks, including gallery
return, bounded inline video and retained conversation source identity. Main
screenshots were inspected; remote artwork is intentionally unavailable in this
fixture, so they qualify layout/fallback rather than real Discord decoding.

`held-scroll-focused-error-1` records the loading exception. The focused variant
also exposed automatic restoration of parked focus during a continuing scroll.
The host correction retains scroll ownership across in-place window rebases and
avoids creating a restoration from offscreen focus. The initial
`held-scroll-focused-owned-1` run did not scroll far enough to exercise eviction
or downward paging, so it is not qualifying evidence. The runner now requires
actual boundary crossings with a bounded input budget. Earlier composer-focused
passes remain valid for their narrower coverage.

`held-scroll-focused-edges-1` passes22 checks per size on
`native-chat-focus-paging-host-6`: focused-row up/down/up,500ms replies, actual
page eviction and reverse paging, no cursor exception, no missing/retired anchor
or backward sample, and visible focus settlement without a viewport move. The
follow-up distinguishes parked reading ownership from passive end-following and
uses `KeepItemsInView` for appends; applying bottom-up tracking at the old bottom
had skipped to the newly appended tail. Two intermediate failures remain in the
plan. Later wheel and final controller results above expose the separate remaining
row-readiness gap; the full qualification is still pending.

Prepared but NOT installed: Discord .36 archive under `candidate-package-36`
(SHA256 `0501501d6f9e3c4653b2f89dcbf1bbd0b8659e698fbb31e0b9c8dbbb12c7dcce`)
and trimmed, validation-disabled `candidate-release-host-36` (predates the latest
focused-row host changes; rebuild before deployment). ReleaseVerifier
passes317 managed-reference checks and matching2/2 cleanup locks. Provider/session
tests pass324 with minification enabled where supported; the actual packaged
backend also passes syntax validation. Local package review keeps identity,
publisher, full-trust model and required/optional permissions unchanged.
No package update or restart has occurred. The desktop helper cannot currently
see the old hidden candidate, so its current call state is unverified. Independent
qualification continues while the user is away. Native validation host7 is
`native-chat-layout-lifetime-host-7`; earlier comparison host6 is a different,
rejected prefetch experiment and must not be deployed.

## Historical checkpoint: .35 applied and live-smoked, November 19

The latest user refinements are implemented in source and a reviewed isolated
package: capped480x270 inline video, slimmer inline chrome, up to two larger
attachment tiles with a `+N` overflow badge and no visible filename/title card,
revised presence badge geometry, and a gallery without duplicate header/filename
chrome. Fitted-image controls sit outside the picture. Expanded layout is not
reduced to satisfy compact constraints.

Current evidence under `artifacts/discord-call-media-polish/`:

- `media-design-35-tests-2.log`:993 managed widget checks.
- `media-design-35-modal-tests.log`:the focused modal contract scenario passes,
  including header omission, accessible name, scope, initial focus and dismissal.
- `media-design-35-sdk-compat.log`:17 SDK compatibility checks pass.
- `media-gallery-design-35b-run/result.json`:24 native decoded gallery checks,
  including the fitted-image footer, selection, fullscreen and return.
- `media-design-expanded-35b` and `media-design-minimum-35b`:49/40 native checks.
  `media-design-light-150-35` and `media-design-high-contrast-35`:40/49 each.
  `media-design-high-contrast-final-35b`:49 after adding thumbnail outlines.
  Native screenshots were inspected; these Discord fixture sources deliberately
  fail remote media, so they qualify layout/fallback and transitions, not real
  Discord image decoding. The separate decoded gallery fixture provides that
  bounded native evidence.
- `media-design-release-verification-35.log`:317 managed references and2/2
  async cleanup lock acquisitions/releases in the trimmed, validation-disabled
  host. `candidate-package-35-final-backend-all.log`:256 packaged/minified checks.
- `candidate-package-35-final-review.txt`:required permissions unchanged (none),
  optional capture permission unchanged. Final archive SHA256:
  `dfbcc53a7178590251a43b651535a961366d6b49772b796644d3ecd341ce1a04`.

The user explicitly authorized resuming desktop checks and updating the isolated
candidate. The reviewed .35 archive is now applied and enabled; the matched
trimmed Release host is running as PID54060. Receipts are
`candidate-package-35-{disable,apply,enable}.txt` and
`media-candidate-35/launch.json`. The default profile and isolated catalog remain
in use; the previous .34 candidate was closed through its owned shutdown path.

Live smoke inspected Servers, the conversation list, and the authorized Sanky
chat. Both real attachment thumbnails render (including the video poster).
The capped inline video decoded and reached its end with muted controls. The
image/video gallery switches in both directions, image controls sit below the
picture, and fullscreen retains arrows without a filename. Escape returns from
fullscreen and then dismisses to the same visible attachment message, with its
row outline restored. UIA reports the outer pane, so this is visual return
evidence, not proof of exact keyboard-focus identity. No call joined or message
sent. Point-in-time live captures do not establish absence of transient flashes;
the frame-sampled fixture evidence above remains the transition qualification.
After the regression fixtures, process54060 is still running at the verified
candidate path, but the overlay is hidden and no targetable window is exposed.
An additional live outside-click dismissal was not performed in this pass.

Current-host shared regressions in `media-design-lifecycle-35-live-followup`
pass inline26, per-widget audio9, passive video12, and remote image9 checks
(56 total). The image check covers decoded HTTPS pixels, zoom/fit, fullscreen/B
return, delayed replacement and rejecting unexpected local authority.

The goal remains active: peer/read-state, remaining pin cases, physical
controller acceptance and long-duration qualification are still open. GIF
stress/lifetime results and their limits are detailed in the implementation plan;
no animated WebP/APNG support is claimed.

## Previous checkpoint: .34

This continuation inspected current source, named result files and the running
0.1.0.34 trimmed Release candidate (PID47172), and exercised the live Sanky chat.
This checkpoint supersedes historical claims below that inline video, poster
metadata or history continuity are still absent. Other open gates remain open.

| Requirement | Current evidence | Remaining limits |
|---|---|---|
| Single-video inline playback | `MessageInlineVideo` uses protocol134 `Inline`; `MediaPlayerView.Inline.cs` owns explicit Play, exact indexed-body authority, suspension and audio preferences. Mixed attachments remain galleries. | No animated WebP/APNG implementation; those were feasibility questions. |
| Lazy acquisition, audio memory, departure and retirement | `inline-lifecycle-7/inline-video.json`:26 checks including12 hide/reopen cycles, late acquisition, offscreen/hidden suspension and idle return. Sibling audio9 and animation12 regressions pass. | Bounded lifecycle evidence, not long-running process stability or all codecs. |
| Indexed-row controller integration | `inline-indexed-visible-6b/result.json`:12 checks through real worker/bridge, A/Play, X, R3, B, scope and held-seek guards. | Live physical controller acceptance remains separate. |
| Compact/expanded layout and chat transitions | `inline-discord-native-4`:38 compact/46 expanded checks, including frame-sampled chat switches. `inline-discord-focus-5/minimum`:38 checks after insetting the clipped Play outline. | Latest enlarged-text/light/high-contrast/pin review remains open. |
| Older-history continuity | `prepend-native-focused-9`:21 checks per size, three delayed prepend/trim loads,203/188 sampled frames, zero blanks or surviving-anchor/source replacements, drift below0.4 DIP. Generic continuity16, end-following34 and entry65 pass. | Broad older compact suite still fails its separate116-DIP height assertion at94.4 DIP; it is not a full-suite pass. Bounded ordered windows qualify, not arbitrary reorder/lazy sources. |
| Pre-open video identification | Validated attachment `PosterUrl`, static gallery thumbnails and inline poster use checked public artwork. .32 live inspection established actual MP4 poster pixels. | Missing/rejected metadata intentionally falls back; no webpage scraping or extra video decoder. |
| Matched isolated Release candidate | `inline-release-verification.log`:317 managed references, async cleanup lock acquisitions/releases2/2. `inline-candidate-34/launch.json`:trimmed host, validation disabled, default profile/isolated catalog. Update review/apply/enable receipts under `candidate-package-34*`. Widget993 and packaged backend256 checks pass. | Candidate is for acceptance; no merge, push, installer or release. |

Package SHA256:
`fab4da9af8153fe143254de6243c939c2ce94d87867ba86d878e9f3a46430124`.

Live smoke observed actual game video pixels inline and expanded, default mute,
Escape returning to the same visible chat slice, and Servers -> Messages returning
to idle Play controls without autoplay. The five-second clip had ended before
the section transition, so this live sequence does not independently prove
interruption of ongoing audio; the lifecycle fixture does. UIA reported the outer
pane after mouse expansion/Escape, so exact keyboard focus return is not claimed
from that observation. No call joined or message sent. Candidate remains running.

Remaining acceptance: affected theme/text/pin/gallery navigation cases,
long-running stability, worst-case animated-image resources, and live peer/read
state cases below. The goal remains active. Evidence paths are relative to
`artifacts/discord-call-media-polish/`.

## Historical audit and outstanding gates

Subsequent same-host layout matrix: `inline-light-150-8` (Mineral Light150%) and
`inline-high-contrast-8` each pass38 compact/46 expanded native assertions,168
total. Main-screen and inline/gallery screenshots were inspected. Focus outlines
and composer/call controls remain usable; compact150% text with a call leaves
little media/message context. These synthetic loading/poster checks close the
latest inline layout/theme gap only. Decoded fullscreen, pins, long-run resources
and peer/read-state acceptance are still separate. The original .34 Release
candidate was restored after testing; see its relaunch receipt.

Subsequent user decisions supersede the fullscreen auto-hide proposal below:
keep attachment arrows visible, omit the filename/title in fullscreen, and add
small static thumbnails inside the single attachment action. A sole video
attachment should play inline; video plus any other attachment uses the gallery.
Inline playback pauses when leaving chat. Native video now uses the ordinary
WinUI tree at the user's explicit direction; requalify against that architecture.
Per-widget mute/volume memory is also newly authorized. See the implementation
plan's latest entries for the crash investigation and current test receipts.

November 19 continuation: isolated .32 adds up to three bounded static attachment
posters, removes the extra card below GIF-only inline content and enlarges themed
presence icons. Real MP4 poster pixels and the GIF card removal were inspected
live. The updated native fixtures pass 31 checks at each size; managed widget
checks pass 993, with 70 raw/70 minified messaging-provider tests. See the plan's
thumbnail checkpoint for the reading-group validation correction and consumer
matrix. Inline single-video support and final trimmed qualification remain open.

Audit snapshot: November 19, 2026; current dirty worktree. Read-only source and
artifact review, plus inspection of four existing PNGs; no build, desktop
interaction, account mutation or goal-status change. The parent is implementing
fullscreen gallery auto-hide concurrently; that work is not qualified here.

Scope: the confirmed decisions and six implementation areas in
[the plan](discord-call-media-polish-plan.md), plus preservation of relevant
conversation/read/call behavior from the preceding messaging work. This is not
a request to redo unrelated Capture Library/Playnite features or migrate every
legacy network-image consumer.

Evidence below is relative to `artifacts/discord-call-media-polish/` unless an
`artifacts/` prefix is given. Counts were read from results/logs, not copied only
from the plan. **Native** means the named fixture, not every shipping widget or
physical controller. **Live reported** means the parent recorded the observation;
this audit did not repeat it or independently inspect its desktop pixels.

## Requirements and remaining gates

| Actual requirement | Strongest current evidence | Remaining gate / concrete next step |
|---|---|---|
| Short content must not acquire false overflow; genuine overflow remains usable, including text scaling | `short-typography-1/result.json`: 54 native checks; `gallery-sizing-regression`: 14; `anchored-sizing-regression`: 136. Shared initial measurement is implemented. | Qualified for these fixtures. Preserve the fix despite removal of chat popovers. Dedicated PoB/Marketplace/Capture visual checks remain deferred in the consumer matrix; do not imply they ran. |
| Stable **Servers** tab/LB naming; truthful separate call status; contextual L3 focuses Call/Join/Answer without activation | `artifacts/u5/{minimum,expanded}/result.json`: 18 checks each, including L3, no ringing/microphone admission on focus, disconnected dock omission and retained Servers label. Current source uses existing focus requests. | Synthetic behavior qualified. Final candidate should preserve busy/modal/selection precedence and Servers-page L3 picker. Physical controller and real peer/audio behavior are not established by these runs. |
| Substantial participant-aware dock; bounded avatars/overflow; grouped Mute/Deafen/Share/Leave; compact usable chat | Latest `opacity-clean-discord-2` captures and two 35-check runs show participants, summary, controls and preserved composer/history. Earlier UI pass has synthetic calls and directional entry. | Verify changing participant count, overflow action, reconnect/disconnect and focus retention on the final candidate. Existing screenshots alone do not prove all dynamic combinations or pin behavior. |
| Rich conversation rows: exact available snippet/time/presence/unread/call; no invented counts, eager history or reordering | `provider-polish-1/{raw,minified}.log`: 248 tests each; `discord-managed-31-tests.log`: 989 checks. Provider uses exact available history-head metadata within existing account/watch/cache bounds. Parent reports live .31 removes stale Direct message after history load. | Resident-cache absence remains an honest fallback. Record final live presence/unread/call-state observation, including account/watch retirement. Do not describe the bounded visit cache as persistent snippets for every previously opened conversation. |
| Left-aligned messages, quiet grouping, fixed composer, no automatic focus/hover message popover | Source in `Messages.cs`, reading-group host integration; latest compact/expanded native runs explicitly verify focusing/entering actions does not open a preview. | Implemented. Final screenshot critique must include chat, not only the modal. Preserve distinct row focus and child-action focus. |
| Clickable URLs/cards, one action per destination; article decoration must not become an attachment | Provider semantic projection and 248 raw/minified tests preserve image/GIF/video embeds while suppressing article/link decoration. Parent reports live .31 has one article URL card and no extra attachment action. | Verify final managed/package/live combination; arbitrary `rich`/unknown embed images intentionally retain existing semantics. No webpage scraping or label-based type inference. |
| Explicit bounded gallery contains supported media, count and stable occurrence identity; spoilers make no request before reveal | `MessageMediaItems` and `HasSpoiler` checks; managed .31 checks; protocol/public-policy and bridge tests. Current messages use a single explicit gallery action. | Synthetic two-item gallery is qualified; retain tests for multiple/duplicate-source occurrences, hidden spoilers, unsupported files, expired/rejected media. Do not infer all formats decode from declared support. |
| B/Escape/outside dismissal returns exact message/reading position; keyboard/controller/mouse admission remains valid after publications | `return-suite-4`: 52 checks; `reading-parent-regression-2`: 19; latest `opacity-clean-discord-2`: 35 per size, repeated returns and navigation. Minimum trace records actual pointer pressed=1/clicked=1 with correct hit-test/foreground root. | Strong native return/admission coverage. Latest Discord fixture uses B; final clean-candidate smoke should explicitly include Escape and intentional outside dismissal, then return from fullscreen to gallery before dismissing. |
| Gallery chrome independent of media ownership; compact arrows/count, sensible labels and traversal; LB/RB maintains focus through replacement | `public-media-legacy-gallery-final`: 20 native checks, including switching/lifecycle; Discord fixtures verify RB and return. `MediaPlayerView.Gallery.cs` retains shared ownership. | Final changed toolbar needs focused Left/Right/Down/Up, LB/RB endpoints, loading/failure and rapid reversal. Earlier passing tests are precedents, not proof of the concurrent auto-hide edit. |
| Fullscreen chrome is transient, no persistent filename ribbon over video, plane geometry respects chrome; intentional focus is preserved | Parent reports actual image→video→fullscreen on trimmed .31; `opacity-clean-layers/layers.json`: 24 native checks; opacity fixture: 8. | **Open: parent actively fixing auto-hide.** Validate idle pointer entry, controller-focused toolbar, explicit focus transfer, reveal/re-entry, narrow/large-text overlap, image→video→image and exit. Never hide a still-focused button. Capture actual native video plus chrome; XAML-only screenshots cannot prove plane placement. |
| Actual video thumbnails where supported, with Play/type/duration and honest fallback | Provider metadata inspected live: video proxy exists, no explicit poster field. Source still drops attachment proxy metadata and has no attachment `PosterUrl`; gallery item has no per-item poster. Parent reports a paused decoded video frame after selecting it in the gallery. | **Not established as completed; see scope assessment below.** A paused gallery frame proves playback initialization, not an attachment thumbnail before opening. Verify a bounded metadata-derived proxy conversion if that surface remains required, or locate an explicit user decision removing it. |
| Reported Klipy GIF, uploaded GIF, Tenor `gifv`, image-animation flags and delayed enrichment use real media rather than provider-page links | Exact external Discord proxy normalization, raw/minified tests, `.31` managed checks. `reading-animation-after` and `passive-animation-after`: 20 native checks each. Parent reports real Wednesday GIF pixels inline/gallery in trimmed `release-frontend-4`. | Live reported target is fixed; preserve the final receipt/screenshots and explicit reopen/return. Unsupported provider page alone still needs an honest link fallback. Direct Klipy origins remain unadmitted. |
| Reduced motion, hidden/offscreen suspension, teardown and bounded decoders; truthful format support | Two 20-check native animation runs; `native-image-formats-1.json`: 5 actual-pixel checks. GIF animates/stops; APNG/WebP show a static first frame on this Windows decoder. | Do not claim APNG/WebP animation support. A new codec is broader shared work, not an implicit requirement to replace working GIF/video paths. Native WebM/other advertised codec qualification is not demonstrated by the MP4 runs. |
| Native compact/wide, enlarged text, dark/light/high-contrast and real screenshot critique after refinements | `artifacts/v3`, `v4`, `v5`: 31 checks each at minimum and expanded (186 total); dark, Mineral Light 150%, high contrast. Latest host repeats both ordinary-size layouts with 35 checks each. | These are real solid-material captures, not complete final live acrylic/native-video/theme qualification. Recheck surfaces affected by the latest toolbar/opacity/provider changes; do not mechanically rerun unrelated suites. Pins and final enlarged-text fullscreen remain unverified. |
| Preserve bounded conversations, selected-row entry, reading position, first unread/read acknowledgments and no false new-message badge | Current `MessageReadState.cs` uses visit/account/watch/viewport authority and separate attempted watermark. Managed .31 log covers initial unread, observed tail, cancellation and late results; previous plan records 38 large-conversation checks and broad native read/navigation runs. | Latest media-only fixtures do not exercise all read-state paths. Final candidate needs targeted native/live opening→read, genuine new arrival while reading older content, Latest, section return and no eager history; perform authorized test messages only after exact recipient identity verification. |
| Calls/alerts behave without accidental side effects; preserve permission and recipient boundaries | Synthetic native calls: 18 per size; managed/provider call tests; prior call module wrapper and concrete `call/ring/stopRinging` fixes. No real peer/audio acceptance is claimed by these fixtures. | Live Test-server and verified Sanky tests are authorized in the plan, but corresponding completed peer/audio/read-state evidence is absent here. Do not treat authorization as a test result; authentication/microphone/privacy grants remain user actions. |
| Keep generic mechanics in SDK/protocol/host, domain policy in widget, preserve consumers | Reading groups, exact-return interception, current-parent admission, public transport and opacity products reside in shared layers; provider summaries/embed semantics remain Discord-owned. Consumer matrices exist. | Keep deferred consumers explicit. Capture Library/Game Help/SDK Gallery and portal/modal consumers inherit shared changes; limited layer fixtures do not establish each app's runtime correctness. No framework rewrite needed merely to mark every matrix cell migrated. |
| Deliver matched clean trimmed candidate through isolated update; preserve settings/permissions; no merge/push/installer/release | `.31` package/backend tests; `release-frontend-4.log` shows trimmed publish. Plan records reviewed isolated update, 317-reference verifier/cleanup pass and live startup with existing sign-in. | Auto-hide changes postdate this candidate. Build/verify the final changed host, retain exact dependency/cleanup/startup receipt, repeat bounded live smoke and leave that candidate running. Audit did not locate a separate `release-frontend-4-verification.log`; link the existing verifier output or retain a final receipt rather than relying only on prose. |

## Security and resource acceptance

The public-media path is implemented, not merely a provider URL allowlist.
`public-media-burst-tests.log` reports 96 passing tests; the older
`remote-media-tests-final.log` reports 75, so those counts must not be conflated.
`public-media-visible-4.json` passes 17 native checks. Parent-recorded desktop
checkpoints showed distinct MP4 scenes after seek; older black/wrong-window
captures remain invalid evidence.

| Boundary inspected in current source | Established bounds / remaining limit |
|---|---|
| `PublicMediaPolicy`, connector, client and ranges | HTTPS/default port/no credentials or fragment; exact source origin across at most 4 redirects; validated public socket destinations; no browser cookies, proxy or default credentials. Four active HTTP requests, 64 queued, 5-second connect and 30-second request budget. Fixed failure text; tests include signed-URL non-disclosure. |
| `PublicMediaBlockSource` | 64-KiB blocks, 4-MiB cache, 8-MiB whole-response fallback, 512-MiB source and 1-GiB transferred-byte budget, 16 queued reads; version-pinned ranges or a bounded snapshot. Native clone/read/seek/cancellation tests pass. |
| `NativeArtworkDecoder`, `NativeGifBounds` | Actual image signatures, 8-MiB encoded bytes, 4096 dimension/16,777,216 pixels, four concurrent public decode admissions, 30-second public-image decode budget; GIF canvas and every delta descriptor checked. **No explicit frame-count/cumulative frame-pixel cap**: tests intentionally accept 10,000 small frames. This is a deliberate source policy, not proof of acceptable worst-case native CPU/memory during ongoing animation. Record acceptance of that bound or add proportional adversarial native evidence before claiming the plan's frame/decode-work gate fully met. |
| `NativePublicMediaSource`, `MediaPlayerView` | Actual container inspection, native track metadata before playback, 15-second metadata timeout, video dimensions/pixels and ≤120 FPS, audio ≤8 channels/192 kHz; checked stream reaches the decoder rather than a remote native URI. Four interactive/two passive-video slots; current authority and expiry checked during admission/playback; late acquisition is disposed. Native MP4 is qualified, not every codec/container. |
| Scope and failure behavior | Discord public sources opt into protocol 133; policy downgrade is rejected and spoilers gate declarations. Local/package/capture custody remains separate. Other legacy image/media callers are explicitly deferred, not silently secured by this change. Current fallback is unavailable media, not automatic external opening. |

## Poster scope assessment

The confirmed user decision removes automatic floating focus/hover previews and
requires explicit gallery activation. It does **not** explicitly prohibit a
static attachment thumbnail. The same confirmed-decisions section subsequently
retains “Supply actual video thumbnails where supported”; section 5 asks for a
Play overlay, verified metadata/proxy representation and a deliberate fallback.
Therefore the later plan assertion that the thumbnail surface is obsolete is an
interpretation, not established user authorization to drop that requirement.

Retaining the existing decoded first frame is justified for the selected gallery
canvas and avoids an unnecessary second decoder there. It does not independently
fulfill pre-open identification or slow-load poster behavior. Keep the thumbnail
gate open unless a concrete user instruction removes that surface. The bounded
next step is one approved provider-supplied proxy conversion probe through the
checked transport, logging only format/bytes/dimensions, then optional domain
poster metadata and existing public artwork. Do not invent signed URLs, scrape
provider pages, bypass Capture Library custody, or introduce a general decoder
solely to satisfy an obsolete implementation proposal.

## Screenshot review and next completion steps

This audit inspected latest minimum chat-return and expanded Servers PNGs, plus
`artifacts/v4/minimum/conversations.png` and `artifacts/v5/expanded/chat.png`.
The compact attachment action stays visible; the composer and participant dock
fit. Expanded Servers fits both stream rows. At 150% light text, only one full
conversation and the start of the next fit; preserving text size and scrolling
is appropriate. High-contrast labels/focus remain clear, while the URL card's
one-sided accent remains a recorded design consideration. These images contain
synthetic media and do not prove final live video/pin/fullscreen appearance.

Finish in this order: qualify the in-progress fullscreen focus/auto-hide change;
resolve the thumbnail requirement without silently narrowing it; close or
explicitly report security-format/resource and live call/read-state gates;
capture/inspect affected final compact/wide/theme/text/pin surfaces; then publish
and verify the matched trimmed isolated candidate and retain the final smoke
receipt. No goal completion, merge, push, installer or release is asserted here.

## Subsequent chat-switch crash

PID54988 crashed in WinUI text accessibility, distinct from the earlier media
teardown crash. Stock-text lifetime fixtures reproduce it without Discord. The
current host adds text-provider/range ownership; native regressions pass 73 checks
each for rich/plain text and 62 for message navigation. This scopes the mitigation
to the host's text projection; long-running stability and other native-control
peers remain unproven. See the plan for dumps, upstream source references and
candidate details. Inline single-video support and the overall goal remain open.

## Later qualification checkpoint: candidate0.1.0.37

The dated implementation plan is the authoritative continuation record; the
earlier open-item snapshots above are historical. The current qualified isolated
candidate includes the bounded prepared-window paging work, text-fragment article
deduplication with exact original destinations, non-overlapping controller-guide
fade-through and centered media loading/failure text. See the plan's consumer
matrix and `link-gallery-*` evidence for scope, pass counts and remaining work.
Cold Inbox-to-chat sidebar preparation and live peer/read/call acceptance remain
open. No overall completion or release is claimed.
