# Capture, chat and shared UI: active implementation plan

Updated: November 17, 2026. Branch: `codex/capture-library-upgrade`.
Status: implemented and qualified within the documented scope; normal candidate PID27708 launched November17 at08:49 Pacific. Gallery/screenshot/Share/fullscreen, chat presentation/notification routing, replay media/controls and focus/preview continuity have native evidence. The task bullets below retain the original scope; the current checkpoint and limitations in [evidence](maintainers/capture-chat-followup-evidence.md) describe what was actually verified.

This is the authoritative active plan. The [archived plan and evidence](archive/capture-library-improvements-2026-11-17.md) preserve prior decisions, failures, test artifacts and the previous candidate checkpoint. Archived completion statements do not apply to this follow-up. Keep this file concise: current decisions, tasks, ownership, evidence pointers and unresolved issues only.

## Authorization and working rules

- Implement the agreed changes; parallelize independent work where ownership is clear.
- Overlay interaction, closing/restarting and candidate validation are authorized. The earlier user-testing restriction is lifted. Leave a qualified normal candidate running for user verification.
- No installer, release, merge or real Discord test sends/uploads. Preserve user captures, folder links, settings and unrelated working-tree changes.
- No existing users depend on these unreleased changes: remove superseded APIs/code rather than add unnecessary compatibility aliases or migrations. Preserve unrelated supported behavior.
- Follow repository AGENTS.md: use existing SDK/host contracts, preserve authority/account/order/lifecycle safeguards, check all applicable consumers, and reject abstractions that weaken working behavior.
- Favor familiar interaction patterns and standard semantic icons to reduce learning effort; reuse host mechanisms instead of widget-specific substitutes for common sharing, chat, media and navigation behavior.
- Native focus/navigation/media claims require native evidence. Run targeted tests after relevant changes, not repeated unchanged broad suites. Record unverified behavior explicitly.

## Work and acceptance criteria

### 1. Dashboard guide and onscreen keyboard

- Restore dashboard shortcuts in tray guide. Investigate readiness gating, publication and fitting; reuse exact dashboard frame authority, not Discord-specific flags.
- Physical Enter finishes onscreen editing regardless of selected virtual key. Controller A/mouse activates that key; Escape cancels; Shift+Enter inserts a multiline newline. Preserve IME confirmation, suggestions and exactly-once commit. Finishing editing does not implicitly send a message.
- Native checks: tray/interactive switching, Discord/Spotify/Audio Mixer/no-action widget; search and chat fields, multiline/suggestions/IME, mouse/keyboard/controller.

Evidence: shared guide/editor corrections are implemented. Native keyboard209 and shortcut28 pass; Discord native45 covers composer/dashboard guide. Physical hardware IME remains outside this qualification.

### 2. Folder filter and gallery proportions

- Folder filter: All folders, WidgetRail captures and linked folders, independent of application/type/date/search. Query the entire index by stable source identity; preserve counts, paging, live updates and stale-query handling.
- Capture gallery L3 focuses the top control bar (Search entry), using the existing shortcut/focus-request mechanism as in PoB. Preserve grid scroll/selection and navigation return memory; do not intercept L3 in modal/media interaction or add a custom input router.
- Friendly labels and disambiguation for same-name folders. Do not expose other widgets' management authority or unrestricted paths through the filter API. Existing source-management listing is owner-scoped and cannot serve as the complete library facet.
- Cover mixed filters, unavailable/disconnected sources, same-name folders, empty matches and source changes. Choosing a filter never grants access or registers a folder.
- Default to four larger columns at the demonstrated width using existing density controls. Increase card size before widget dimensions; monitor-constrained layout with room for guide/tray. Small libraries stay left-aligned at normal card size.
- Approximately 16:9 landscape thumbnails with deliberate Fit/Fill for other shapes; lighter captions, useful title/date, compact clip-duration badge and tighter search-to-month spacing.
- Preserve existing custom-title/game-name precedence. Repeated timestamp names in the first screenshot were mostly missing-metadata fallback. Do not invent metadata or confuse source-image blur with text rendering.
- Standard accessible icons throughout Capture Library for familiar actions; retain labels for ambiguous choices, folder linking and destructive confirmations. Reuse icon buttons, tooltips and controller guide.

### 3. Screenshot/video previews, Share and fullscreen

- Add screenshots to existing gallery rich previews; retain grid geometry, dwell/placement/pointer-travel/input rules, selection-mode exclusions and bounded resources.
- Validate preparatory retained-original PNG access and inactive bitmap/poster cleanup. These source edits existed before this goal but were not built, qualified or staged.
- Add preview Share through generic intent routing. Reuse exact indexed/inline preview proof and operation-owned semantic lease retention across the chooser. Do not duplicate tree walkers or retain native controls unnecessarily. Source/query/account/worker invalidation must still revoke authority.
- Sharing regression reported November 17: current candidate showed “Cannot open this item / The destination is no longer available. Please try again.” across files/folders. User subsequently identified an existing unfinished Discord share; Cancel share was not visible and removing its attachment required navigating/scrolling into the chat. Treat this as user-reported causal evidence pending reproduction, not as a file-source failure. Screenshot: `codex-clipboard-c32e4b97-24b1-4182-8618-498bc9defb0a.png`.
- Streamline the complete Capture -> receiver -> recipient -> composer -> send/cancel/return flow. Latest user correction: EVERY new external Share opens recipient selection. Never reuse an old share's recipient or jump straight into its composer. An unfinished share/draft must not block incoming files or trigger a routine Continue/Replace/Cancel prompt. Keep incoming files separate from existing drafts until the user chooses a recipient; then add/deduplicate into that recipient's compatible unsent draft and preserve its text/files. A different recipient receives only the new incoming files, never another conversation's draft. Explicit Send remains required. Reuse host intent/result/presentation and shared draft/operation mechanisms, not a Capture-specific second router.
- Keep pending attachments, upload state and Cancel sharing/Remove attachment immediately reachable in a fixed composer/share area, independent of message-history scroll. Preserve drafts across back/reopen. Cancelling recipient selection releases only the new incoming share and leaves existing drafts intact. Never silently discard text/files, move a draft across accounts/conversations or mutate a send already in flight. Real attachment/size limits require concise actionable feedback identifying the constraint; do not fall back to destination-unavailable. Stage incoming files separately from an active send if supported, with bounded ownership and no duplicate send; otherwise expose a truthful actionable busy state. No generic existing-draft conflict prompt for ordinary repeated sharing.
- Own incoming file grants through resolution, then release only superseded/cancelled grants exactly once; cancel must not erase another conversation's draft. Avoid staging the same file twice or leaking offers after repeated Share. In-flight/uncertain external sends retain existing cancellation/reconciliation safeguards: do not promise that stopping waiting unsends a message. Surface plain-language progress/recovery, not file-offer counts, lease conflicts or multiple-attachment-operation jargon.
- Dedup contract checkpoint: broker issues optional opaque recipient-scoped original/version identity (no path or whole-file hash), with10 passing file-reference/authority checks. Discord adopts it when available and otherwise only exact grant identity, never filename/size guesses. Recipient-first repeat sharing, admission/account races and cancellation are included in813 managed checks.
- Sharing acceptance: begin an unsent share then share different files/folder again; ALWAYS show recipient selection, cancel without changing prior drafts, choose same recipient (append/dedup only after choice), choose different recipient (no draft leakage), limits, remove/cancel, back/reopen, scrolled history, upload cancellation, uncertain send, account changes and stale source. No silent data loss, duplicate sends, automatic recipient reuse, routine draft-conflict prompt, hidden cancel action or generic destination error for an existing draft. Test ordinary detail/batch Share and preview Share; explicit Send remains required, with controlled providers for mutation tests.
- Fix user-reported preview fullscreen. Reuse the same player and existing fullscreen owner, preserving playback/position and valid media lease. Return to the same valid preview/focus; if invalidated, return safely to the current gallery/owner without resurrecting content. Hide/disable genuinely unavailable expansion, not all video controls.
- Passive previews are muted/minimal; interactive previews offer playback, seeking, audio and fullscreen. Entry must not restart playback or shrink the video.
- Maintain fitted poster until first decoded frame. Stable bounds and overlay controls; no empty flash, source mismatch or incorrect video layer.
- Restrained anchored reveal/dismiss through existing motion/portal mechanisms. Initial tuning: roughly 160–220 ms open, 100–160 ms close. Prefer opacity/small translation to text-blurring scale. Coordinate frame, video and controls; use opacity if native video cannot safely track transforms.
- Interruptible hover/focus/reversal/source switching with no stale reopen, focus theft or duplicate click routing. One preview decoder; deterministic retirement; reduced-motion support.
- Native before/after evidence: cold/warm opening, controls entry/exit, fullscreen reversal, source removal/recycling, Share chooser, viewport edges, slow decode/errors, mouse/keyboard/controller, 90%/100% UI scale and representative DPI.

Evidence: user video `20261117-0911-19.3561274.mp4` shows the original empty opening rectangle and shrink. Fullscreen failure was reproduced: valid handoff hid the preview anchor and retired its player. Existing owner/lifecycle correction passes28/28 native ordinary/dialog checks and final actual Capture gallery30; same player/position survives fullscreen return. Offline contact sheets remain in `artifacts/capture-sources`.

### 4. Shared media player

- Improve existing MediaPlayerView: larger aspect-preserving stage, quieter borders/padding, bottom gradient, timeline and compact primary icons. Speed/loop in a small menu; replay prominent at end.
- Reuse existing visibility/fullscreen mechanisms. Controls fade without resizing video; stay available during scrubbing/menu/keyboard/controller interaction. Stable focus and hit targets, high contrast/reduced motion.
- Scrub target time, generous hit areas, accelerating controller seeks and brief non-layout-changing seek/volume feedback. Distinct loading/buffering/paused/ended/error states.
- Matching image zoom/Fit/fullscreen toolbar, no video-only controls. Preserve original-file playback, bounded decoders and interrupted image restoration.
- Verify shared consumers and native pixel/lifetime behavior. Thumbnail scrub generation is deferred; do not add a frame cache merely for visual polish.

### 5. Discord chat and reusable conversation facilities

- Compact variable-height virtualized DM timeline: incoming left, own right, text left-aligned; author/avatar/time grouping and date boundaries; stable composer and familiar icons. Ordinary text is readable inline, not through a rich preview.
- Adopt/improve Game Help rich text, disclosures, composer and notification-intent precedents. Shared presentation improvements may benefit both; keep AI-answer and Discord-message domain state separate.
- Add bounded keyed mutation support to the existing SDK cursor resource. Preserve provider page segments/cursors, reverse navigation, reset scopes and delayed-load safeguards; no second widget-owned timeline. Check Spotify queue.
- Preserve Discord event payloads and apply creates/partial edits/deletes locally. Bounded journal, revisions/tombstones, send ID/nonce dedup and account/session/watch fencing. History GET only for initial/paged loads or coalesced genuine recovery gaps, not each event.
- Host-owned follow-end based on actual viewport: follow new messages when at bottom, preserve reader position otherwise and show jump-to-latest. No widget scroll counters/focus theft. Resolve bounded-window eviction versus protected reading anchor explicitly.
- Rich text/link/image primitives: markdown, readable mentions, custom/Unicode emoji, spoilers, quotes/code, replies, embeds and reaction summaries. Add only missing reusable inline-image/style support. Provider syntax stays outside host; no arbitrary HTML renderer.
- Inline attachment images/video posters and on-demand media/link previews. Bounded demand and explicit playback; readable fallback for unsupported stickers/polls/system content rather than empty cards or silent truncation. Do not claim full Discord parity.
- One existing notification pipeline: investigate reliable structured account/conversation/message activation enrichment, reuse OpenIntent and exact-message reveal with bounded history load. Preserve drafts, account/access checks and browser fallback. No title/timing guesses or duplicate notifications.
- Assess supported client read acknowledgments, coalesced only for messages actually viewed. No guessed endpoints or background-fetch acknowledgment.

### 6. Native replay recording: measured prototype, then integration

- Replace Game Bar shortcut recording with a host-owned replay session. First prototype: one selected game/window, hardware-encoded SDR video, game audio and a 60-second target. No silent continuous software-encoder fallback.
- Reuse native streaming worker pacing/queues/isolation/target handling, process-loopback audio and current recording encoder. Discord currently receives raw BGRA/PCM and encodes via WebRTC; it does not expose an encoded stream we can simply retain. Recording currently writes a Media Foundation MP4 sink.
- Prototype compressed-sample or continuous-segment output and keyframe-aligned assembly without interruption/re-encoding where feasible. Do not rotate ordinary recordings if that introduces gaps.
- Share frame/audio acquisition for compatible targets/settings/authority where practical; independently bounded consumers so saving cannot stall Discord. Separate WebRTC/local encoding is expected initially; do not promise one encoder.
- Hard duration/byte/GPU-queue/save-operation bounds, actual available replay duration. Compare RAM versus disk segments before choosing defaults. At 10/20 Mbps, video payload is approximately 75/150 MB per minute, excluding overhead; estimates are not measurements.
- Measure CPU/GPU encode/copy use, private/managed/GPU memory, disk writes, frame pacing/game frame times and save latency, including concurrent streaming/manual recording. Qualify audio sync, keyframes, overlapping saves, slow/full disk, resize/minimize/exit, sleep, device loss and audio changes.

### 7. Recording lifecycle, settings and Game Bar cleanup

- Host service owns target authority, encoder/buffer, real status, save results and library publication. Capture Library remains an ordinary idle-able control panel. Hidden/closed widget or overlay does not stop explicitly enabled recording; host shortcut can save without starting the widget.
- Game exit stops that session, never silently captures desktop/another app. WidgetRail exit releases resources; finalize saves/clean abandoned temporary segments safely. Automatic-start preference is separate from actual recording; no idle encoder with no eligible game.
- Keep ordinary screenshot request and Discord browser/document lifetimes unchanged. Explicit user-approved host recording ownership is a separate session policy.
- Capture Library settings backed by host preferences: replay enable/duration/budget/auto-start; existing video quality controls; supported game/microphone audio; save/buffer storage. Main page exposes Off/Waiting/Recording/Error, buffered duration, Start/Stop/Save. Global shortcut stays in host shortcut settings.
- Recording status/Stop remain reachable if the widget is unavailable. Define shared disable/uninstall/CLI behavior; do not rely on a GUI uninstall prompt. Default to stopping when the controlling feature is removed unless independently supported host controls remain.
- Playnite can supply generic game/context policy; no Playnite-specific capture-service code. Saved clips reuse metadata, library events and sharing.
- Replace obsolete request-only recent-gameplay contracts/outcomes with native session/save semantics. Remove Win+Alt+G injection and capture-specific foreground handoff, dead handlers, guidance and shortcut-only fixtures after the replacement is qualified. Retain shared helpers still used elsewhere.
- Keep external Windows capture-folder linking/indexing/playback, including redirected folders and duplicate prevention. Preserve captures/metadata/links; never change Windows recording settings, uninstall Game Bar or silently enable our recorder. No compatibility wrappers/migration machinery solely for this unpublished path.

## Shared consumer matrix

Concrete shipping entry points and current disposition. Numbers refer to the evidence note; native coverage is not implied by a source audit.

| Mechanism | Consumers and required disposition |
| --- | --- |
| Capture/session/worker | Migrated: CaptureLibraryWidget.Replay + host coordinator. Native recorder/library13, control UI14 and broker101 pass. Discord raw streaming/replay coexistence passes. GameHelpWidget capture regression passes; ordinary capture native imports remain covered. Playnite provides generic metadata, no host-specific branch. |
| Folder query | Migrated: CaptureLibraryWidget.Gallery/Filters. SQL predicate applies count/ranges/groups/application facets/anchors. Playnite/Game Help/null-source readers already compliant; Windows/other linked sources retain management boundaries. Capture regression114 and native gallery30. |
| Preview intents/lifetimes | Migrated: Capture preview Share/fullscreen. Native gallery30 at both scales; exact indexed lease54 and intent proof checks. Discord Messages native45; protected partial-page continuity26 passes. PoB previews use existing shared lifecycle, no widget migration; no claim of a new full PoB live run. |
| Media controls | Shared renderer change: Capture detail/edit/preview, GameHelpWidget.Presentation, Discord.Messages and SdkGallery.Modern. Native baseline113/lifetime19/content36/dialog36/interactive61 plus actual gallery30. Browser-owned YouTube/Netflix-style players are not changed by this native control work. |
| Cursor/live/follow-end | Migrated: Discord Messages resource. Spotify indexed queue is already compliant and two targeted regressions pass. Protected native reader26 and ordinary focus recovery16 pass. Game Help does not adopt provider-specific Discord event state. |
| Rich text | Migrated Discord parser uses shared native runs/images. Game Help rich-link/notification/capture7 pass; PlayniteAchievementPresentation and PoB RichText/Planning/Trade/Workspace remain compatible consumers. Templates/examples require no new declaration; inline images are optional. Unsupported Discord formats use explicit fallback. |
| Keyboard/guide | Shared editor209 and shortcut28 native checks; Discord native45 includes composer/dashboard guide. Settings35 plus shortcut1 and Session authority2 pass. Spotify/Audio Mixer/no-action use the existing shared dashboard declaration path; no duplicate widget input routing. Hardware IME and OS notification-area menu drawing remain unverified. |

## Execution and checkpoints

1. Bounded guide/keyboard fixes; folder query; finish preview authority/resource/fullscreen work.
2. Gallery/media polish and Discord live/text/chat/notifications in independent slices.
3. Native replay prototype -> measured design decision -> production session/settings -> Game Bar command cleanup.
4. Targeted shared-consumer/native qualification, remove diagnostics, update documentation and launch normal candidate.

Current owners: root = plan/Capture replay UI/Discord presentation/provider/notification consumer; discord_messaging_provider = viewport recovery/native focus plus shared notification-intent assessment; windows_capture_plan_probe = replay broker/bridge authority and atomic library publication (media slice qualified); shared_attachment_audit = replay native worker/IPC/backend (Discord delta/share slice handed back). Root coordinates native builds/candidate restarts. No agent edits this plan concurrently.

Checkpoints: native media baseline113/lifetime19/content36/dialog36/interactive61, Discord chat45, keyboard209 and shortcut28 pass. Chat managed813/provider44, native generic notification routing7, Spotify queue2 and Game Help7 pass. Capture114 earlier regression plus latest replay3/toolbar1/navigation2/permission1 pass; final actual gallery30 checks each at100% and90%. File intent5, file streaming3 and Settings/session shortcut1+2 pass. Replay policy25, coordinator28, broker101, native recorder→library13, real SDK/host pipe scenario and durable catalog/CLI removal pass. Corrected production replay's uncontended76s run saved59.4s/1782frames/0missed, with memory plateau around132MiB and~2.7%one-core CPU in an owned animated-window/audio fixture. Concurrent raw streaming/replay and independent stop pass. Real-game performance is not established. Fresh local candidate packages validate; no installer/release/merge.

Final qualification: ordinary focus/scroll16, protected preview/recovery26, replay UI14 and final actual gallery30 pass. Candidate staging and normal launch are complete in `artifacts/capture-chat-candidate-20261117`; Capture0.2.0.13 and Discord0.1.0.35 preserve settings through reviewed local CLI updates. First launch exposed an omitted Settings integrity seal in candidate assembly; restored through the existing seal tool, without bypassing checks. PID27708 is the successful normal launch. No test fixture remains open.

Narrowed/deferred scope: automatic read acknowledgments require exact viewed-message evidence; reactions/polls are read-only and some animated Discord content has fallback. Replay auto-start uses the explicitly approved application, hardware SDR/application audio, and a strict duration upper bound with keyframe shortening. Acquisition code is reused; independent bounded raw-streaming/replay workers are qualified, not a new cross-authority shared-frame service. Real-game performance across hardware, physical PC sleep, hardware IME and notification-area menu drawing were not exercised; controlled native capture/audio, injected power transitions, editor guards and overlay tray/hotkey paths were. Thumbnail scrub generation remains deferred. Historical preview-lease failure evidence was ambiguous; the corrected actual-preview assertion passes without broad observer effects. These limits are recorded rather than represented as universal runtime/performance proof.
