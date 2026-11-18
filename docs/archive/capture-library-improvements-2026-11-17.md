# Archived Capture Library plan and implementation evidence

Archived November 17, 2026. Historical record only; follow the [active implementation plan](../capture-library-improvements-plan.md). Scope, status, authorization and future-work statements below may be superseded. Original evidence and decisions are preserved.

Status: previous implementation checkpoint qualified with the limitations below; additional work is planned in the November 17 follow-up. Follow-up work is not qualified or staged unless explicitly recorded. No installer, release or merge is authorized.
Base: 2ae52d5e. Branch: codex/capture-library-upgrade.

## November 17 follow-up: current plan and superseding decisions

This section supersedes conflicting earlier scope decisions, especially the Windows shortcut-only approach. Older checkpoints below are historical evidence, not proof that the new work is complete. The user clarified that this code has no existing users: remove superseded contracts and implementation where appropriate rather than preserving compatibility wrappers, legacy outcomes, or migration machinery solely for this unreleased work. Preserve actual local captures, folder links and unrelated functionality.

Current authorization (latest November 17 instruction): user requested a new goal and implementation of this plan, including the folder filter. Overlay interaction, closing/restarting and candidate validation are now authorized; the earlier testing restriction is lifted. Preserve unrelated processes and user data. No real Discord test sends/uploads, installer, release or merge. Launch the qualified normal candidate for user verification. Parallelize independent bounded work where useful, as previously requested, without overlapping file ownership.

### A. Native continuous recording and replay saves

Decision: replace the Game Bar historical-capture command with WidgetRail-owned continuous recording. Keep external Windows capture-folder browsing as an independent supported source. This is a distinct milestone with a measured feasibility checkpoint, not a promise that continuous recording has negligible cost.

Ownership and reuse:

- The shared host capture service owns recording sessions, target authority, status, encoder/buffer lifetime, save operations and capture-library publication. Capture Library supplies user intent and settings UI through SDK APIs. No Playnite- or Discord-specific policy belongs in the service.
- Reuse the continuous native streaming worker's pacing, bounded queues, process isolation, target validation and resize handling; reuse existing process-loopback audio and recording encoder capabilities. Inspect shipping entry points rather than assuming code under tools is unused: WebViewCaptureWorker consumes the native streaming implementation.
- Current Discord path: native WGC/raw BGRA and PCM -> bounded WebView transport -> generated media tracks -> Discord/WebRTC encoding. Current recording path: WGC/audio -> native Media Foundation sink writer -> MP4. These share components, not a single encoded output. A replay buffer cannot simply retain the existing raw transport or assume access to Discord's encoded packets.
- Generalize the native capture source/worker only where semantics match. Streaming and recording the same authorized target may share acquisition, with independent bounded output queues and failure handling. A slow disk or save must not stall streaming. Do not force differing target/audio/quality/permission policies into one session.
- Separate encoding is expected initially for Discord/WebRTC and local recording. Sharing acquisition does not eliminate encoder cost. Native recording must work with no Discord connection, browser or widget active.
- Extend encoding to expose compressed samples or suitable continuous segments. Prototype before choosing an architecture; restarting ordinary MP4 recordings in a loop is not an acceptable substitute if it causes gaps. Saving should assemble a playable keyframe-aligned interval without re-encoding where feasible and without interrupting capture. Report boundary precision honestly.

Buffer/performance policy:

- Retain compressed video/audio with hard duration and byte limits, bounded GPU frames and bounded pending saves. Never retain minutes of raw frames. Define behavior when the byte cap shortens the requested replay duration; expose actual buffered duration.
- Start the prototype with one explicitly selected game/window, hardware-encoded SDR video, game audio and a 60-second target. Treat resolution/frame-rate/bitrate as measured choices; no silent expensive software fallback for continuous recording.
- Compare RAM and bounded disk-segment buffering before selecting defaults. Video payload alone at 10 Mbps is approximately 75 MB/minute; at 20 Mbps approximately 150 MB/minute, excluding audio and pipeline allocations. These are arithmetic estimates, not measured application memory.
- Measure CPU, GPU encode/copy load, private/managed/GPU memory, disk writes, power where available, frame pacing and game frame-time impact. Include concurrent Discord streaming and manual recording; do not promise one encoder or zero copies without evidence.
- Validate timestamp continuity, keyframes, audio/video synchronization, overlapping save requests, slow/full disk, game resize/minimize/exit, device loss, sleep/resume and audio-device changes. Begin a fresh segment/session where necessary; never silently switch to another app or desktop.

Lifecycle:

- Capture Library remains subject to its ordinary idle lifecycle. Closing/idle/unloading the widget or hiding the overlay does not stop an explicitly host-owned recording session.
- Global Save replay dispatches to the capture service without initializing the widget. Reopening the widget reads authoritative session status and buffered duration; it does not reconstruct recording state locally.
- Selected game exit ends that session; WidgetRail exit stops workers and releases buffers. Handle shutdown/save finalization explicitly and clean abandoned temporary segments on next startup if disk buffering is chosen.
- Persistent automatic-start preference is distinct from an active recording session. No encoder should run just because the preference is enabled and no eligible target exists. Start with explicit target/start; add automatic selected-game policies through existing target/app lifecycle mechanisms when qualified.
- Preserve browser/document-scoped lifetimes for Discord sharing and request-scoped screenshots; host ownership is explicit, not a relaxation of all capture authority. A widget request alone cannot silently create an indefinite background session.
- Keep recording status and Stop reachable from host UI even if Capture Library is unavailable. Define removal/disable behavior in the shared package/service path, including CLI removal; do not rely only on an interactive uninstall prompt. Default to stopping sessions when their controlling feature is removed unless a separately supported host control remains. This policy requires implementation-time review, not a claimed existing behavior.

Settings and UX:

- Capture Library settings is the main configuration surface; shared host settings persist preferences independently of widget process lifetime. The service owns live status, not persisted UI flags.
- Sections: Replay recording (enable, duration, buffer budget, automatic-start policy); Video (existing resolution/frame-rate/quality capabilities); Audio (game audio, optional microphone and levels when supported); Storage (default save folder and any chosen disk-buffer options).
- Main page offers concise Off/Waiting/Recording/Error state, actual buffered duration, Start/Stop and Save replay. Save returns the created capture identity or a concrete failure, not merely a shortcut-requested acknowledgment.
- Configure the global save shortcut in existing host shortcut settings. Controller/mouse/keyboard entry points reuse existing routing. Playnite may supply generic game identity/policy through supported APIs; no provider-specific service branches.
- Finalized clips reuse library metadata, notifications/revision updates, related references and sharing. Native replay functionality must not depend on a linked Windows folder.

Game Bar cleanup (no unreleased compatibility burden):

- Replace the Windows-specific recent-gameplay request/outcome contract with an appropriate native recording/session/save contract. Remove obsolete SDK/protocol/bridge/broker/frontend handlers and request-only wording rather than preserving misleading forwarding APIs.
- Remove Win+Alt+G injection and capture-specific foreground-hide/restore orchestration. Repository-wide search first: retain shared guarded-input or target helpers used elsewhere, with their relevant tests.
- Remove Game Bar enabled-state detection work, settings-toggle proposals, prerequisite guidance and shortcut-only fixtures/docs. Replace tests with native replay behavior tests; retain historical test evidence as historical.
- Keep authorized Windows Captures folder discovery/linking, original-file playback, source indexing and duplicate prevention. Preserve files, folder links, titles, favorites and associations. Do not alter Windows recording preferences or uninstall Game Bar. Do not automatically enable our recorder.
- Remove dead code/declarations/resources and update SDK examples, manifests, protocol generation and documentation after checking every consumer. No migration system or compatibility aliases solely for this unpublished path. Replace/remove the old command once the new path is qualified so intermediate work does not masquerade as working native replay.

### B. Capture previews, actions and shared media presentation

- Add a Folder filter alongside application/type/date/search, default All folders. Offer WidgetRail captures and registered linked folders using friendly names, with a parent-path qualifier for collisions. Filter the whole indexed library by stable source/folder identity through the existing broker query, never only realized cards or display-path string matching. Folder and application are independent dimensions. Inspect existing source authority, query facets and duplicate-source behavior before extending contracts; preserve scoped access. Cover combined filters, paging/counts, renamed/disconnected sources, same-name folders, empty results and live updates. Do not grant access or re-register folders through a filter operation.
- November 17 grid review supersedes the initial suggestion to enlarge the widget first: target four columns at the demonstrated window width using a useful minimum card width and the existing density preference. Increase card size before increasing surface dimensions; keep monitor-aware constraints and room for tray/guide. A three-item library stays left-aligned at normal density, not stretched to fill the window.
- Use wider approximately 16:9 landscape thumbnail frames, with deliberate Fit/Fill handling for portrait/small sources and full image in preview. Quieter caption backgrounds/padding, useful title/date and a compact clip-duration badge; tighten search-to-month spacing. Preserve existing custom-title/game-name metadata precedence: the earlier repeated timestamp filenames were mostly missing-metadata fallback, not evidence that normal titles are broken. Do not infer metadata or diagnose source-image blur as a host font-rendering defect.
- User-supplied recording `C:/Users/dwive/AppData/Local/Packages/Microsoft.ScreenSketch_8wekyb3d8bbwe/TempState/Recordings/20261117-0911-19.3561274.mp4` supplies new visual regression evidence. Offline review of the first second at all 30 recorded frames shows the preview appearing with an empty media rectangle before video arrives. Sampled later frames show video shrinking substantially when interactive transport controls appear, then enlarging on exit. This is observed visual behavior; exact source cause and rendering timing are not yet established. Prior clip qualification does not establish continuity for this recording. Contact sheets: `artifacts/capture-sources/preview-motion-opening.jpg` and `preview-motion-review.jpg`.
- Preview polish is a continuity requirement, not merely adding animation. Keep a correctly fitted poster visible from opening through first decoded-frame presentation; reserve final geometry before reveal, and ensure no empty/black flash or mismatched native-video plane. Do not delay interaction while waiting indefinitely for decoding. Preserve the current image on transient buffering; show bounded loading/error feedback as appropriate.
- Passive-to-interactive preview should keep the same video rectangle, playback position and decoder. Overlay transport chrome instead of allocating another layout row that shrinks video. Entry, exit, controls, media and frame must move/fade together; a focus-mode change must not reopen/restart playback. Preserve existing mute policy and only permit audio according to explicit interaction policy.
- Preview fullscreen regression: user reports that the video preview's fullscreen action does not work. This is user-reported, not independently reproduced; investigate shared host fullscreen admission, portal placement, input routing and preview lifetime before assigning a cause. Keep useful preview controls instead of disabling all video controls as a workaround. Passive previews remain muted with minimal chrome; entered/interactive previews expose play/pause, seeking, audio and fullscreen where supported.
- Fullscreen must expand the same player through existing host fullscreen ownership, preserving source, position, playback state and valid media authority. The fullscreen handoff must not be mistaken for pointer departure that retires the player. Keep necessary preview resources valid during expansion without bypassing source/query/session invalidation or adding a separate player/lifetime registry. On exit, restore the same valid preview and appropriate mouse/keyboard/controller focus; if its source or presentation is gone, return safely to the gallery/current valid owner without resurrecting stale content. Hide or disable expansion when the host genuinely cannot support it; never leave a nonworking enabled control.
- Fullscreen qualification: reproduce the reported preview failure, then check entry/exit from mouse, keyboard and controller; Escape/B routing; pointer departure during expansion; rapid reversal/dismissal; source removal, recycling and navigation while expanded; playback/position continuity; correct video layer/bounds; focus restoration and decoder/lease cleanup. Cover ordinary and dialog-hosted previews where applicable, plus existing detail video/image fullscreen consumers. Preserve the existing interrupted-image restoration checks; this new report is not covered merely by earlier passing fullscreen fixtures.
- Use existing host motion and preview portal mechanisms for a restrained anchored reveal/dismiss, suggesting roughly 160–220 ms opening and 100–160 ms closing as initial tuning values, not fixed promises. Prefer opacity and small translation over aggressive scaling that blurs text/video. Establish stable final bounds and layer order first. If native media cannot track a transform correctly, use coordinated opacity rather than introduce another overlay/player or fragile compositor workaround.
- Motion must be interruptible: quick hover changes, leave/re-entry, controller navigation, scrolling/recycling and opening an intent chooser must cancel/retarget from the current visual state with existing authority checks. Never reopen an obsolete preview after decode, show another item's frame, steal focus, or add duplicate hover/click routing. Keep one preview decoder and bounded demand; avoid preloading a player per card. Retain only presentation resources necessary for a short closing transition, with deterministic release and immediate authority revocation.
- Preserve spatial connection to the source card without moving the grid. Reuse existing viewport-clamped placement and pointer travel behavior; do not force connected zoom where edge clamping produces awkward travel. Provide settled high-quality text at non-default scale, reduced-motion transitions and stable hit targets during animation. Compact image-first chrome and icon actions should make the preview feel like part of the gallery rather than a nested dialog.
- Qualification for this polish includes offline before/after frame comparison and native behavior evidence: cold/warm opens, first frame, repeated open/close, passive/interactive reversal, rapid source changes, image/video switching, edges of viewport, delayed decode/errors, Share chooser, mouse/controller/keyboard, 90%/100% UI scale and representative monitor DPI. Check frame pacing and resource bounds as well as screenshots; a recording alone cannot identify GPU performance causes. No automatic quality claim equivalent to another product without measured evidence.
- Complete screenshot gallery previews using existing rich-preview dwell, placement, interaction, dismissal and focus-return rules; selection mode and resource limits still apply. Clips remain muted in passive previews; no grid relayout on hover.
- Existing preparatory source edits for retained-original PNG access and inactive preview bitmap/poster cleanup are NOT built, qualified or in the candidate. Validate and finish them, rather than count them as new completed features.
- Add Share to previews through the normal generic intent router. Existing nested-preview dispatch and intent revalidation do not consistently resolve preview declarations. Reuse indexed/inline preview action proof; avoid a parallel tree walker or Capture-only popup.
- Keep the validated semantic action lease alive for the host chooser operation, without retaining native preview controls or bypassing source/account/query/worker invalidation. Release in the existing operation lifetime on success, cancel and failure. Review preparatory resolver overloads before expanding them.
- Audit Capture Library actions across home, details, selection, editing and previews. Prefer standard icons for Back, Previous/Next, Share, Favorite, Edit, Delete, Refresh and More. Keep descriptive labels for ambiguous actions, folder linking and confirmations. Reuse package SVG/icon-button semantics, accessibility labels, tooltips and controller guide; do not repurpose unrelated glyphs.
- Improve shared MediaPlayerView rather than build another player: larger aspect-preserving stage, quieter borders/padding, transport over a readable bottom gradient, timeline above compact primary controls, speed/loop in a small menu, contextual replay at end.
- Reuse MediaControlsVisibility and existing fullscreen ownership. Controls reveal on appropriate input and hide without resizing video. Preserve focus while interacting; never hide controls during scrubbing/menu use or strand controller focus. Respect reduced motion, high contrast and small preview dimensions.
- Add scrub target time, generous hit targets, predictable accelerating controller seeks and non-layout-changing seek/volume feedback. Preserve poster until ready and clear loading/buffering/paused/ended/error states. Images get matching zoom/Fit/fullscreen controls, no video-only chrome.
- Keep original-file leases, bounded decoders, resource cleanup and interrupted fullscreen/image restoration. Thumbnail scrub frames are deferred pending a separate bounded generation/cache design; visual polish does not require them.

### C. Discord chat and reusable conversation facilities

- Direct-message presentation: incoming left, own messages right; text within both left-aligned. Compact avatar/name/time, consecutive-author grouping and date boundaries, readable variable-height rows, stable composer and standard action icons. Preserve virtualization and message identity; no normal-message rich preview just to read text.
- Inspect/adopt Game Help's existing rich text/citations, disclosures, composer and result-notification intent patterns. Improve shared text/presentation/scroll behavior for both where appropriate; retain separate domain state for AI answers and Discord messages. Do not impose a new conversation storage model on Game Help.
- Extend existing SDK cursor resources with bounded in-place mutation batches where needed instead of maintaining a second widget timeline. Provider owns message ordering and opaque paging cursors; shared resource owns publication/stale-load integration. Review Spotify queue as the other real cursor consumer.
- Preserve create/edit/delete payloads from the existing Discord event subscription. Apply locally without per-event history GET; merge partial edits, retain revision/tombstone protection against stale history, deduplicate sends by ID/nonce and fence account/session/watch epochs. Coalesce recovery fetches only for actual gaps/overflow/reconnect/missing information. No new history polling loop.
- Host viewport-aware follow-end: follow appended messages only when already at the end; preserve reading anchor otherwise and expose jump-to-latest. No widget scroll counters or incoming-message focus theft. Notification-target reveal and explicit user jumps remain distinct from passive updates.
- Reuse native rich text/link/image/disclosure mechanisms. Add missing generic inline-image and style support only where needed. Discord parses markdown, mention identities, custom emoji, spoilers, quotes/code, replies, embeds and reaction summaries. Unicode emoji uses native fallback; test grapheme/ZWJ/variation-selector handling. Do not add arbitrary HTML rendering.
- Inline image/video posters and on-demand media/link previews. Explicit playback and bounded image/media demand; do not autoplay every attachment. Provide readable fallback for stickers/polls/system messages or content beyond supported bounds, not empty rows or silent truncation. Complete Discord feature parity is not claimed.
- One notification pipeline: reuse center/banner/preferences/dedup and existing OpenIntent routing. Investigate generic activation enrichment with reliable account/conversation/message identifiers from provider events. Never correlate by displayed text/timing or publish duplicate notifications. Reveal exact message via bounded load if needed; preserve drafts/access/account checks and original browser fallback when mapping is unavailable.
- Investigate supported client read acknowledgments; only acknowledge actually viewed messages with coalescing, never background fetch or notification arrival. Do not invent endpoints or bypass provider/account restrictions.

### D. Keyboard and dashboard guide corrections

- Physical Enter in the shared onscreen text editor commits editing regardless of selected virtual key; controller A/mouse still activates a virtual key. Escape cancels; Shift+Enter inserts newline in multiline fields. Preserve IME composition confirmation, suggestion behavior and exactly-once commit. Finishing editing does not send a chat message unless the editor explicitly declares submission semantics.
- Source finding: WidgetTextEntryDialog.HandleKeyboardKey explicitly restricts Enter commit to the focused native single-line editor. Update that policy and tests; avoid an outer router workaround that double-activates virtual keys.
- Investigate missing tray dashboard hints first. OverlayShellPage.Guide gates dashboard actions through IsGuidePresentationReady, which also depends on interactive content readiness. This is a source suspicion, not runtime proof. Use existing dashboard frame authority and guide publication rather than Discord-specific flags. Preserve current owner, focus/transition semantics, routing eligibility and width fitting.

### Consumer matrix and qualification gates

| Shared area | Consumers / current disposition |
| --- | --- |
| Capture sessions and native worker | Capture Library: planned native replay controls; Discord: preserve streaming lifetime/audio/quality, assess shared acquisition; Game Help: preserve request-scoped captures; Playnite: generic metadata/optional policy only, no forced recorder migration |
| External capture sources | Windows folder links and Playnite achievement sources: retain existing behavior; unrelated linked folders: no migration |
| Preview intent routing/lifetime | Capture: planned Share adoption; PoB/Discord/other shipping rich previews: applicability and dismissal/focus regression checks; ordinary indexed/pinned intents: preserve authority and chooser behavior |
| Shared media player | Capture detail/edit/preview and Game Help screenshot: affected; SDK samples/general media: inspect; embedded browser media uses separate controls, not assumed automatically changed |
| Cursor mutation/follow-end | Discord: planned adoption; Spotify queue: existing behavior regression checks; Game Help transcript: adopt viewport policy only if semantics match; inspect other shipping/manual patterns before finalizing contract |
| Rich text/conversation presentation | Discord and Game Help: affected; all rich-text consumers/templates/examples: style/link/image compatibility check; provider-specific parsing remains local |
| Text editor | Search/settings fields, Game Help/Discord composition and other shipping editor users: physical Enter, multiline, suggestions, IME and controller checks |
| Dashboard guide | Discord, Spotify, Audio Mixer and a widget with no quick actions: tray/interactive/switching checks; source suspicion requires native confirmation |

Before implementation of each shared change, refine this matrix with actual repository entry points and source locations; mark migrated/already compliant/not applicable/deferred with reasons. Preserve existing guards; reject a proposed abstraction if real consumers require brittle special cases. New source findings are not runtime performance evidence.

Sequence: (1) bounded guide/keyboard fixes and finish preview/resource/intent work; (2) shared media presentation and Discord live/text/chat/notification work in independent bounded slices; (3) native replay prototype, measure and settle buffer/encoder/session design; (4) recording controls/contracts and full Game Bar command cleanup after qualification. Independent source work can proceed in parallel where ownership is clear, without repeated unchanged broad test runs.

Qualification: targeted behavior tests per changed mechanism, including delayed events, teardown, identity/account switches, stale authority, notification targeting and no duplicate network calls. Native focus/scroll/preview/keyboard/media evidence after user testing restriction is lifted; compare actual app flows in addition to controlled fixtures. Replay requires actual recordings, playable output/audio sync and measured resource/game-frame-time impact before success is claimed. Retain prior failures as evidence. Launch the final normal candidate for user verification only when permitted; no installer/release.

## Current qualification checkpoint

This table supersedes older chronological checkpoint statuses below; detailed failures and evidence remain recorded.

Final launch: normal candidate PID39432, Capture Library visible/interactive with396 captures, preferred1180×700 surface at90% interface zoom. Command line contains only `--show` and the normal candidate profile; no validation, input-disable or diagnostic flags. Candidate versions: Capture0.2.0.12, Discord0.1.0.34, Playnite0.4.6.7. Existing widget availability/settings are preserved. Bridge runtime matches the verified file-share discovery publication. Native cold sharing9, actual clip/bounds21, image reversal101, general hover61, scope55, entry47, return51, cache43, Messages38/39 and the relevant managed suites provide the completion evidence. Real external Discord sends/uploads, physical-controller hardware, and actual Game Bar clip production are not claimed; those limits do not change the implemented request/delivery contracts. Future context-menu intent expansion, metadata/session grouping polish and precise native-allocation attribution remain explicitly outside this completed implementation rather than implied shipped features.

| Workstream | Implemented / verified | Qualification limits / follow-up |
| --- | --- | --- |
| Navigation retention and focus | Host cache bounds 2/widget, 3/host; native entry47/cache43/return51/scope55; real Capture retained pixels/deep scroll; direct live Playnite Home/Library cache hits retain source and34 Home row instances, with visible focus | Normal candidate running; no extra row-ownership workaround justified |
| Memory investigation | Paired cache eviction and forced-GC measurements, including settled462.00→440.94MiB private commit and55.72→36.45MiB managed bytes | Precise native allocation ownership and a fixed byte cap are not established; do not label whole-process memory as cache cost or a proven leak |
| Windows recent gameplay | Guarded request and known-folder linking; native89 assertions and picker7 | Real Windows clip production remains unverified; request result intentionally does not claim saved output |
| Gallery previews | Generic saved-media reference, single muted player, scoped leases; native lifetime12, general interactive61, ordinary/dialog media22 each; actual saved-clip gallery21 verifies gameplay pixels, complete action bounds and decoder/lease cleanup; Capture0.2.0.12 staged | Normal candidate running |
| Image fullscreen restoration | Original intermittent failure reproduced with exact geometry/ChangeView evidence; readiness-owned restore fix passes deterministic unloaded/reload and cancellation baseline101 plus preview-return16 | Normal candidate running; original failure preserved as before evidence |
| Discord quick actions | Existing dashboard API adopted; busy/stale-action checks; native tray publication and Expanded guide display verified | Existing in-widget X mute check deliberately excluded; no real voice mutation performed |
| Native Discord messages | Bounded inbox/history, drafts/send reconciliation; live two-conversation reads/paging; native Minimum38/Expanded39 including50% upload/Cancel/draft preservation;707 managed checks; clean candidate0.1.0.34 staged | Actual external send/upload intentionally not performed; native successful Windows file selection unverified (synthetic file services used for composer tests) |
| Capture sharing / attachments | Scoped file offers, streaming upload fixtures;106 Capture checks; actual cold two-file Capture→Discord recipient-picker/Cancel smoke9 passes with clean Discord34 and updated Bridge; Capture0.2.0.12 staged | Actual external send/upload intentionally not performed |

No application installer, release or merge is authorized. Development widget packages update only the candidate catalog. Leave a normal development candidate running for user verification after qualification.

Approved file-sharing discovery policy (latest user clarification): the host lists compatible enabled file-share receivers from their manifests without starting/probing every worker. Existing `hasDynamicAvailability` remains available for chosen-target validation at delivery; other contracts retain their current preflight semantics. `BridgeClientRegistry.Intents.cs:ResolveIntentAsync` owns this standard-contract policy. No new SDK flag, timeout, availability cache or provider-specific host branch is needed. Native cold Discord evidence additionally showed its preflight Background account read was correctly denied with `lifecycle_denied`; preserve that lifecycle rule and activate only the chosen receiver. Controlled tests cover unused dynamic receivers never starting, chosen receiver admission/rejection, and ordinary dynamic search behavior. Runtime/candidate qualification follows the updated Bridge publication.

Discovery qualification: all **26 intent Bridge scenarios** pass (`artifacts/file-share-discovery-tests.log`), including the new two-dynamic-receiver case, selected rejection before file authority, real broker handoff/cancellation, search preflight/recheck, and existing pinned/indexed intent paths. Documentation checks252 pass. Updated Bridge is published and copied only to the development candidate runtime, verified SHA-256 `C60ABDAF6F5161F53AB8477D572EE9452AF99C2EB3AB8AB5E427CD7CB56E5D55` (`artifacts/file-share-discovery/staged-runtime.json`). No SDK/protocol changes or installer are needed for this routing policy.

| File-share discovery consumer | Applicability |
| --- | --- |
| Capture Library detail/selection Share | Sender adopts unchanged contract; chooser avoids unrelated worker startup. |
| Discord Files receiver | Uses static declaration plus chosen-delivery/account/draft safeguards and cancellable initial loading. |
| Future/fixture Files receivers with dynamic mappings | Listed from catalog; optional callback still runs only after selection, before recipient read authority. |
| YouTube search and Playnite related-item handlers | Different contracts; retain dynamic preflight and recheck behavior. |
| Other shipping widgets, templates and SDK examples | No additional Files receivers found in repository applicability search; no migration required. |

Completion-audit follow-up: Capture Library now exposes the same generic Share intent from its existing selection toolbar, bounded to ten files (the currently qualified receiving workflow). It adopts `ReadSelectedAsync` without realizing rows, derives preparation identity from the SDK query/selection snapshot, retains independently opened originals, checks content revision, and releases every sender grant on terminal/late/rejected preparation. Selection and original files remain intact. Full Capture suite **106/106** passes (`artifacts/capture-batch-share-suite.log`), including mixed image/video offers, oversized selection, selection changes during a noncooperative file return, partial preparation/content changes, and duplicate feedback. No shared API or other widget migration is required. Final candidate staging is0.2.0.11; native multi-file handoff remains to be checked.

Per-card context-menu Share is deferred: existing `WidgetContextAction` carries typed widget actions, whereas trusted prepared intents are supported on buttons. Adding a Capture-only popup or programmatic dispatch would duplicate host routing/authority. Detail Share and selection Share cover single/multiple files through existing supported APIs. A future generic context-intent contract should reuse button preparation/delivery and indexed lease admission; it is a genuine SDK/protocol/host extension (moderate complexity), not a missing widget call. Existing context Open/Favorite behavior is preserved.

Direct Playnite native qualification (`artifacts/playnite-publication-probe/direct-return/continuity.json`): production widget0.4.6.6 on the actual library,90% interface zoom/125% monitor DPI, Home→Library with eight early normalized Down inputs, then direct Previous and Next. Home returns with `hit:home`, native Home/favorites source identities1170455/21353801 retained; all34 original Home row instances have **zero retirement** through return. Fresh provider captures advance content revisions in place while query generations remain2/3. Library returns with `hit:library`, original source60574779 and visible focused item0/offset0. The returned Home screenshot displays artwork and focus normally. Earlier three-tab cycling through Achievements did not exercise this cache path because that page has no matching Content transition; its new source is not proof of a cache failure. Preserve fresh captured-provider authority; reject both hash-based publication suppression and speculative unloaded-row retention. Temporary domain probes are removed from final Playnite0.4.6.7; its domain fixes are unchanged.

## Scope and ownership

### Planned addition: Windows recent-gameplay captures (agreed November 16)

- Add a controller/keyboard/mouse-accessible **Save recent gameplay** action that requests Windows' existing historical capture through its capture shortcut. Windows owns the buffer and configured duration; do not add a recorder or duplicate buffer.
- Resolve the intended game/application through the existing host target and foreground-handoff rules before invoking the Windows shortcut. Verify targeting with WidgetRail open, game switching, held input, unsupported targets, and fullscreen/borderless games. Do not assume a sent shortcut means recording succeeded.
- Link the actual configured Windows Captures folder through the existing authorized linked-source/indexing service. Detect redirected locations or offer folder selection; never assume the default Videos/Captures path is authoritative. Discover completed screenshots/clips without copying originals or adding duplicate entries when the same folder/file is already linked.
- Detect background-recording availability if reliable. If known off, offer **Open Windows capture settings**; if unknown, allow the request. Reliable detection is not a prerequisite for the feature. No in-widget Windows recording/settings toggle, start/stop recording controls, or Game Bar companion component is in scope.
- Confirm a completed capture where reliable correlation is available; otherwise distinguish request sent from confirmed saved. A coincidentally discovered unrelated file must not become false confirmation. Handle no output, an inaccessible folder, incomplete/locked files, and captures created outside WidgetRail.
- Reuse generic source metadata (origin, media type, timestamps, duration/dimensions) and existing playback/editing. Do not infer game/achievement identity from unreliable filename parsing. Saved-video playback must retain the existing original-file lease rather than copying an entire recording to temporary storage.
- Ownership: Capture Library chooses the workflow and renders controls; a narrow Windows platform adapter owns shortcut dispatch/target validation behind broker authority; the existing shared capture service owns linked-folder observation and indexing. Audit existing target-handoff and source APIs before adding contracts. Game Help benefits from the resulting indexed media without acquiring recording controls; Playnite linked capture sources remain unchanged.
- Validation: capture with overlay open/closed, mouse/keyboard/controller, disabled/unknown recording state, relocated folder, duplicate registration, pending file writes, external deletion, and long-video playback. No implementation resumed by this plan update.

### Gallery experience improvements (agreed November 16)

- Favor larger media, quieter card chrome, compact title/context, and small duration/favorite indicators. Use theme secondary colors for hierarchy and explicit state, preserving readable captions and high-contrast behavior.
- Add a focused/hovered card that expands into an anchored preview after a short dwell (start evaluation around 500 ms, tune through native interaction). Keep the grid's layout, spatial-navigation targets, and scroll position fixed. Maintain the poster until a decoded video frame is ready; never flash an empty player during handoff. Clamp placement to the usable viewport and preserve pointer travel into the preview.
- Preview one clip at a time, muted, only while the gallery and target remain active. Cancel pending opens and release playback/file leases on departure, navigation, minimization, or recycling. Disable automatic previews in selection mode; provide an autoplay preference and honor reduced-motion settings separately from playback preference. Rapid scrolling must not start a decoder per passing card.
- Reuse existing rich-preview pointer transit, outside-click routing, focus ownership and native media/file-lease mechanisms where compatible. A media preview's shared lifecycle belongs in the host; capture-specific preview policy belongs in the widget. Inspect actual preview content restrictions before claiming the existing contract supports embedded playback. Do not add widget timers or a player to every virtualized row.
- Add context-sensitive quick actions on the active preview (open, favorite, more), responsive favorite feedback, and restrained focus lift/outline. Preserve existing A/Enter/open and context-menu bindings; directions continue navigating the grid until the user explicitly enters interactive preview controls. Do not silently repurpose controller buttons for scrubbing. Mouse hover and keyboard/controller focus share one preview owner, rather than competing players.
- Follow with source-card-to-detail visual continuity and optional game/session grouping using reliable metadata. Keep existing date grouping and browsing available where richer metadata is missing. First pass prioritizes card composition, anchored previews, and favorite feedback.
- First resolve the reproduced blank-grid, post-return artwork loss and loading-time focus issues. Require frame-level before/after evidence and measured decoder/memory costs before accepting new motion or automatic media behavior. Planning approval does not resume implementation.

Reuse requirements for this work:

| Area | Existing mechanism / intended owner | Validation and applicability |
| --- | --- | --- |
| Grid and cards | Existing indexed collection, occurrence identity, row-artwork, WRSS/theme and native focus styling; widget supplies capture data and composition | Preserve virtualization, density choices, readable captions, selection, and stable layout; check Playnite and other indexed consumers if shared styling changes |
| Preview placement and input | Existing host rich-preview portal, dwell, pointer transit, focus entry/exit, and outside-click routing | Extend the supported shared contract only for demonstrated gaps; exercise Capture filters, Discord participant controls, and PoB item previews; do not create a Capture-only popup or click router |
| Video playback | Existing MediaPlayer declaration and native player, with capture original-file leases; host owns bounded active-preview lifecycle | MediaPlayerOptions already exposes AutoPlay/Muted/Loop in `src/WidgetProtocol/MediaPlayerDefinition.cs`; verify preview admission through `FocusPreviewContentContract` and its validator before using it. Keep full viewer/editor and Game Help behavior intact; no parallel media engine or per-card player pool |
| Motion and navigation | Existing motion/appearance policy, navigator, presentation cache and focus/scroll memory | Improve or extend these owners if needed; no widget-maintained return-focus counters, duplicated navigation history, or animation timers. Preserve exact return position, reduced motion, and rapid interruption behavior |
| Favorites and grouping | Existing capture metadata commands/revisions and grouped indexed queries | Widget owns presentation policy; no second metadata store or grouping engine. Reject stale actions and preserve live-update ordering |

For each shared extension, update the existing consumer matrix with actual affected entry points, run proportional native checks, and document any unverified consumers. Reuse must preserve behavior rather than force an unsuitable abstraction; record rejected approaches and retain working safeguards. Validate rapid focus changes, pointer entry/exit, preview actions, selection mode, file replacement/deletion, page switching, hidden/cached views, and teardown. No preview playback may remain active in a hidden navigation-cache entry.

### Previously authorized scope

### Discord tray actions, native messages and capture sharing (agreed November 16)

The user cleared the previous goal and requested a replacement encompassing this addition and unfinished earlier work. Preserve the existing in-widget X mute/unmute shortcut: the user clarified they had forgotten it existed and explicitly said no verification work is needed for that shortcut.

- Add dashboard/tray quick actions for self mute/unmute and deafen/undeafen, with additional useful call actions exposed through the existing action/menu affordances where appropriate. Use `WidgetView.QuickActions`, dashboard input dispatch and existing dynamic state/capability publication, following Audio Mixer and Spotify consumers. Do not invent another tray UI or global shortcut system. Preserve the existing account/call/session/state-bound action IDs and guards in `samples/DiscordWebViewWidget/DiscordWebViewWidget.cs`; stale tray actions must not affect a different call or invert a changed state.
- Implement native Discord direct-message views: a bounded conversation/recipient list, paginated message history, live incoming/outgoing reconciliation, unread state and a composer with explicit Send, attachment review/removal, progress, cancellation and recoverable errors. Use native shared controls and the established authenticated Discord session/provider adapter; do not revive the cancelled desktop-client migration or introduce an independent login/token store. Resolve provider capabilities and API behavior before committing to specific adapters.
- Preserve account, recipient and message identities throughout asynchronous reads and sends. Retain conversation draft/scroll context within the proper owner; changing conversations/accounts must not leak drafts, retarget pending uploads, or publish late results into the new conversation. Reconcile a completed external send even if the UI request was canceled. Do not automatically retry an ambiguous send in a way that duplicates messages.
- Use existing indexed collections, text-entry/editor, rich-text/link rendering, navigation, focus/scroll memory, context menus, host notifications and media/file-access paths. Widget owns Discord data/ordering/recipient policy; shared mechanics remain SDK/host owned. Extend existing APIs only where an actual messaging/attachment requirement demonstrates a reusable gap. Native messaging does not require new server/channel administration, voice or streaming engines.
- Add **Share to Discord** from Capture Library detail and appropriate existing context/multi-selection actions within proven batch/provider limits. Reuse the intent registry, handler readiness, chooser, routing and destination navigation. Receiving an intent opens a native Discord composer with selected files and recipient selection/review; it never silently sends to a guessed recipient or automatically posts a message.
- Intent payloads should describe authorized attachment references plus display metadata, not grant unrestricted disk access by supplying an arbitrary path. Inspect existing capture/media read leases and file-picker grants first; if cross-widget delivery lacks scoped read authority, extend the shared intent/attachment mechanism with bounded recipient-scoped, revocable/expiring references. Preserve content revision, external-file replacement/deletion checks, and grant cleanup on cancel/uninstall/teardown. The same transfer mechanism should support future receiving widgets without Discord-specific host logic.
- Native Discord file attachment selection and captures received through intents must converge on one upload/composer path. Stream from admitted original-file reads where supported; do not copy entire long clips into RAM or temporary storage just to share them. Discover/handle the provider's actual upload/type/size limits and upload completion state; avoid hard-coded tier assumptions. Respect unavailable integration, signed-out session, blocked/unavailable recipient, offline, rate-limit, partial-upload and ambiguous-send outcomes.
- Test messaging and upload mutations using controlled provider fixtures/test infrastructure; this feature request does not authorize sending test messages or files to real contacts. Read-only real-session checks may establish capability and rendering. Record any end-to-end external delivery that remains unverified rather than claiming a fixture proves provider compatibility.

| Addition | Existing owner / API to adopt | Shared-consumer checks |
| --- | --- | --- |
| Discord dashboard actions | SDK QuickActions + host dashboard dispatch; Discord existing call commands | Audio Mixer/Spotify quick actions, stale snapshot/call changes, signed-out/no-call states; do not reopen verification of the existing in-widget X shortcut |
| Native conversations | Existing indexed collections, navigator, editor and text/link controls; Discord provider owns message semantics | Capture Library and Playnite navigation/scroll; PoB/Discord existing previews and editor consumers if shared controls change |
| Attachments and upload | Existing capture/file read leases + one Discord attachment composer | Native media viewer/editor, Game Help attachment flow, linked Windows/Playnite capture originals |
| Capture sharing intent | Existing dynamic availability and intent delivery; generic scoped file-transfer extension only if missing | Existing Playnite-to-Capture-Library and related-item intents, receiver lifecycle/permission changes, future generic handler fixture |

### Earlier authorized scope

- Host tooltip cleanup: remove redundant tips on labeled controls; retain useful icon,
  truncated-label/status tips and accessibility names. Adjust pin Save/Cancel/Opacity
  currently share a blanket tooltip assignment (PinnedAdjustmentToolbar.cs:81).
- Shared graph contract: bounded optional empty text; domain widget maps actual FPS
  state. WidgetLineGraph.cs:18 currently assumes every empty graph is collecting.
- Shared media collapse mouse bug: reported opening Notifications, even though a
  widget tray icon was underneath. Do not assume click-through. Reproduce native
  press/release and trace focus restoration; utility icons open panels on GotFocus.
- Capture confirmation: per-widget/per-kind skip, saved only on Proceed, resettable
  in Settings. Retain permission checks, countdown, target validation and stop controls.
- Capture Library: bounded indexed search/filter/sort, favorites/titles, revisioned live
  updates, virtualized gallery with density controls, viewer previous/next and return
  position. Stage two: reusable multi-selection and bounded safe batch file/metadata
  operations with recoverable deletion, export and reveal-file behavior.
- Keep existing cards/media backgrounds and the accepted 1180 x 700 preferred extent.
- Capture service owns files/index/authority; widget owns gallery policy and workflow;
  SDK/host owns reusable collection input, rendering, selection and lifetimes.
- EXCLUDED from the original phase: capture pipeline consolidation, border behavior changes, trimming,
  annotations, cloud sync, visual-content search. Do not modify real captures in tests.
  The subsequent authorized editing phase explicitly adds crop/rotate and clip trimming; that later scope supersedes the original trimming exclusion.

## Reuse approach — agreed refinement

The framework shares interaction mechanics, not all widget data or product policy.
Reuse existing indexed collections, row artwork, media playback/zoom, navigation and
subscriptions. Add opt-in collection multi-selection as the concrete shared gap.
Do not introduce a universal gallery, provider query engine, favorites database or
batch-operation framework. Capture queries/metadata/revisions remain capture-service
contracts, usable by Capture Library and Game Help. Widgets choose filters and
available operations; the broker executes authorized domain commands. Shared internal
Windows helpers may implement recycle/reveal/export, but widgets submit authorized
capture IDs rather than unrestricted paths. No other widget is forced into selection.

Game Help's picker benefits from richer discovery but does not become a management
screen. Existing List/Open consumers remain compatible. Extend navigator/collection
continuity only if real large-query requirements reveal a concrete missing contract;
retain proven behavior if a proposed abstraction fails representative consumers.

## Consumer matrix

| Change | Affected/migrated | Already compliant or not applicable |
|---|---|---|
| Graph empty states | Performance Monitor | Discord ping conditionally shows graph; SDK Gallery verify compatibility |
| Capture query API | Capture Library; Game Help adopts query search, filters, favorites and live updates | Legacy List/Save response shapes remain unchanged for older packages |
| Capture confirmation | Capture Library, Game Help, other Capture SDK consumers | Discord live sharing uses separate chooser |
| Media collapse | Captured image/video, Game Help, embedded player and browser expansion audit | Pins/banner shared focus boundary regression checks |
| Indexed selection | Capture Library representative migration; documented opt-in example | Playnite, PoB, Discord, music widgets, Games & Apps, Settings and full-app reference remain opt-out; YouTube has no indexed collection |
| Tooltips | Adjust pin and host labeled controls | Keep meaningful icon/status/truncation tips |
| Linked folder sources | Capture Library settings and read-only removal; Playnite setup and metadata companions | Game Help consumes the resulting query/open path; SDK Gallery/templates keep owned-capture flows |
| Related-item intents | Capture Library and Playnite | Existing dynamic availability filters handlers before chooser and rechecks delivery; no new routing engine |
| Saved-video read leases | Capture Library/native player; Game Help keeps its small attachment limit and disabled oversized rows | Temporary recording budgets and explicit copy/export behavior remain bounded separately |

## Validation requirements

- Behavior tests: queries across full library; stable ordering/revisions; stale results;
  bounded memory; titles/favorites persistence; external edits/deletes; permission
  revocation; cancellation and partial batch outcomes; Game Help compatibility.
- Native mouse/keyboard/controller: media collapse, rapid transitions, no unintended
  utility panel, exact return focus, multi-select range semantics, collection viewport
  restoration, dialogs/cancel/reset and tooltip overlap. Real press/release evidence.
- Performance: thousands of synthetic entries, bounded row/thumbnail demand, first
  load vs warm query costs; profile thumbnail lock before any concurrency rewrite.
- Theme/scaling: light/dark/high contrast, acrylic toggle, narrow and wide viewport,
  no clipping, unchanged preview/media appearance. No source audit treated as runtime proof.
- Record rejected/narrowed abstraction proposals with evidence rather than force
  migrations. Existing protocol/API compatibility and integrity boundaries remain.

## Work log

- Started parallel graph, confirmation and library-service lanes. Parent owns
  media collapse/tooltip diagnosis, widget UI and shared selection integration.
- Protocol allocation: graph empty text v111; indexed selection reserved v112.

- Resumed after user scope review. Graph tests 15/15 and SDK transport 1/1 passed;
  confirmation PlatformSettings 35/35 passed; core capture service query tests 2/2
  passed before batch additions. These are partial evidence, not final qualification.
- Native baseline media-collapse clicks: six cycles did not reproduce the reported
  notification panel. Guard edits remain experimental until native fixture evidence.
- Gallery partial authoring is unfinished and not yet compiled; do not deploy this tree.

- Source-based review found and corrected: unsafe permanent-delete fallback for a
  previously unavailable capture (new RemoveUnavailable rejects restored originals);
  late canceled batch replies overwriting newer confirmation/selection; simultaneous
  initial and post-subscription query publications (now one SDK RunLatest lane);
  metadata-only edits upgrading a changed preview's destructive content revision.
  Deterministic regression tests are being integrated; no guard is justified by
  cancellation alone.
- Shared selection is bounded to 256 explicit query items; oversized gestures reject
  atomically. Select all is only offered for matching sets within the bound, never
  as an alias for loaded rows. Long-running unlimited query-wide jobs are not added.
- Added optional provider key-to-index lookup (v113) on the existing indexed request
  lane for large-query focus/viewport rebasing. Legacy consumers opt out unchanged.
- Capture browse uses existing grouped indexed grid/list for complete monthly
  partitions, application facets, and adjustable thumbnail density. No new gallery
  renderer or universal metadata/batch engine.
- Native media transition checks passed 48: WinUI pointer-focus fallback onto the
  notifications icon is confirmed in traces. Shared fullscreen entry/exit owns focus
  through detach/restore; no utility opens after the fix in those native cycles.
- Native graph checks passed 13; confirmation native checks passed 23 (automation
  peers, not physical clicks). Service tests include 3,001 indexed captures and
  SQLite deletion/corruption. Native temporary-file recycle/restore preserves bytes.

## Added scope: existing achievement captures and Playnite integration

User authorized integration on 2026-11-15 and then explicitly chose reusing
Playnite Achievements' existing screenshots/clips over a second automatic recorder.
The earlier automatic-screenshot proposal is superseded, not an implementation
requirement. Capture-pipeline consolidation remains excluded.

Provider evidence: installed Playnite Achievements 4.1.1; upstream Captures wiki
and README describe clean/notification/framed screenshots, buffered recording,
provider-owned folders and achievement/game galleries. Reusing that recorder avoids
competing capture sessions; no quality/performance superiority is claimed.

- Keep generic capture context: authenticated origin, bounded subject/event references,
  readable labels, optional provider event time, origin-scoped idempotency. Recorded
  time is distinct from actual capture time; unknown actual time stays unknown.
  Preserve strict legacy List/Save shapes. No Playnite semantics in host or SDK.
- Add generic linked-source registration only after checking existing authority APIs:
  user-authorized folder/source, opaque source ID, contained relative references,
  supported media validation, no traversal/reparse escape. Do not copy provider
  originals into our library. Thumbnails and bounded temporary playback leases may
  use caches. Persist source ownership explicitly; removing a linked library entry
  must not delete a provider original. Provider retention/deletion remains visible.
- Widget setup must expose supported provider capture configuration through the
  Playnite integration plugin, with version/capability checks, validation, persistence
  and readback. Inspect the actual public provider API before choosing controls.
  Do not silently edit settings JSON behind a running addon or claim unsupported
  settings can be configured safely. Missing addon remains a normal unavailable state.
- Preserve the widget's unload-after-idle behavior where possible. The recorder runs
  inside Playnite independently; source discovery/reconciliation must not rely on an
  always-interactive widget. Audit durable generic sources versus provider-plugin
  inventory and existing application lifetimes before selecting the smallest mechanism.
  Current source manifest defaults to 300 seconds; verify installed/user overrides
  rather than assuming the reported two minutes is the source default.
- Bidirectional UI: context captions and authorized Open related item; game/achievement
  details show linked captures and View in Capture Library. Uninstalled providers keep
  readable metadata but disable unavailable actions. Reuse generic intents/queries.
- Tests: no persistent media duplication; source-root authority/isolation; traversal,
  replacement, missing/moved roots and files; partial writes/recordings; rescans after
  widget unload/restart; dedup/replay; metadata/index recovery; provider missing/version
  mismatch; settings round-trip/cancellation; no deletion without original-file authority;
  old widget wire compatibility and Game Help browse/open of supported linked media.

Research sources:
- https://github.com/justin-delano/PlayniteAchievements/wiki/Captures
- https://github.com/justin-delano/PlayniteAchievements#overlay

## Added scope: large saved recordings without temporary media duplication

User explicitly requested fixing whole-recording temporary playback copies after
reviewing Windows' two-hour recording option. This is shared media-library work,
not a Windows Game Bar-specific adapter and not capture-pipeline consolidation.

- Existing library Open obtains a verified original lease, but registry Import then
  copies it into temporary capture storage. Replace saved-video playback import
  with an explicit adopted read lease; release/revoke/expiry/disposal must close
  authority and never delete the original. Retain bounded image normalization and
  explicit export/copy operations where requested.
- Separate saved-library playback limits from WidgetRail recording-creation budgets.
  Use bounded streaming/inspection/thumbnail work for long or large media. Avoid a
  whole-file hash/copy on every playback open; preserve file identity, revision and
  containment safeguards using verified handles rather than trusting filenames,
  length and modification time alone.
- Audit parent-directory/reparse replacement between validation and media open;
  preserve the same verified resource through playback. Keep linked originals
  read-only. Reuse media authority renewal while a player remains active.
- Consumer checks: Capture Library, Game Help's bounded attachment/export path,
  native media player, expanded/pinned media, permission revocation and old widget
  wire compatibility. Measure large-file open/seek/memory/temporary-disk behavior;
  source review alone is not performance evidence.
- Generic folder linking also supports compatible external capture exports. Dedicated
  Windows settings controls, SteelSeries library/export/settings adapters, timelines
  and editing are not added to this implementation merely because discussed as examples.

## Additional qualification

- Full Capture Library suite passed 54/54 before the latest folder-management UI,
  file-modification timestamp and direct-video-playback changes. New context cases
  cover owner-scoped associations, replay/recovery, modal close versus late results,
  and related-item intents. These newer changes require another qualification pass.
- Native shared selection/lookup passed 84/84 twice. Checkbox hit target moved during
  parent-row press animation; suppressing that motion only for its own checkbox
  stabilized repeated list/grid/grouped clicks. Opt-out consumers retain normal press
  motion. Deferred focus dispatch removed the observed crash; its original exact
  cause is not conclusively established.
- Native embedded media passed 49 checks; source-folder picker passed 4 checks.
- Production gallery fixture at 1180 x 700 logical / 125% scaling shows ten complete
  medium cards with spacing and truncation. Selection uses overlay checkboxes; all
  1,200 synthetic entries stay virtualized. Other sizes/themes remain to qualify.
- Playnite capture settings adapter uses public provider ISettings transaction with
  active-edit/revision guards and persisted readback; 56 isolated API/framework checks
  passed. No live user settings were changed by those tests.

## Final integration evidence before installer update

- Capture Library 64/64, SDK 181/181, compatibility 17/17, catalog 52/52,
  documentation 250 files and native parity 200 constants passed. Modern SaveAsync
  uses an additive current-response operation; legacy wire shapes remain closed.
- Native long-media playback passed 8 checks with a real two-hour / 134 MiB MP4,
  including seek into the second hour, renewal and source disconnect. Actual widget
  confirm-delete passed normal and deliberately delayed scene updates; another
  widget's lease correctly blocks recycling and the failed preview recovers.
- Adjust pin native checks passed 104; text entry passed 207. Redundant labeled
  tooltips are absent; accessible names and useful icon/truncation/status tips remain.
- Gallery and selection inspected in light, high contrast and maximum text/interface
  scaling. Source management enlarged to 760 x 520 logical so two folder actions fit;
  real mouse open/close returns to Settings. Batch buttons use the existing responsive
  grid and selected count shares the existing count line.
- Source access survives idle unload, reviewed package updates and old-version
  cleanup. Actual catalog uninstall removes exact access; the final owner revokes
  discovery. Reinstall can register a fresh source. No catalog-absence heuristic.
- Playnite widget 0.4.6 and plugin 1.2.0: 246 widget, 119 community, 14 core,
  72 framework/API checks; native settings 29, details geometry 22, navigation 47.
  Six live community opt-ins were skipped. Actual provider API verification follows
  installation; no achievement unlock has been triggered on the user's account.
- Metadata companions remain optional; ordinary folder discovery needs none.
  API-based SDK integrations can submit context directly. New Playnite companions
  are hidden, preserve existing visibility and never change original attributes.
  Display-only references are plain text; related actions require explicit opt-in.
- The already-started registered host installer completed successfully at
  0.1.0-preview.41.local.1. Playnite 0.4.6, Game Help 0.1.8 and plugin 1.2.0
  are installed. Seven explicitly requested built-in widgets were restored with
  their settings; other enabled choices, credentials and consent were preserved.
  Live provider GET and same-value PATCH verified all 19 capture settings with
  unchanged persisted values. No actual achievement unlock was triggered.
- User subsequently prohibited creating installers/releases until requested.
  All following UI refinements are development-only: no packaging, deployment,
  release or version bump. No merge to main is performed for this feature branch.


## UI refinement after user review (development only)

- Capture Library uses Capture and More command entry points, search, a collapsed
  Filters & view disclosure, and Select. Batch actions appear only after selection.
  Screenshot/record controller shortcuts and all existing library operations remain.
  Existing SDK anchored modal/disclosure contracts own navigation and focus; this
  does not introduce a new host toolbar or author-managed focus bookkeeping.
- Primary Save actions and Capture use the current theme accent with contrasting
  canvas text. Close/Cancel remain neutral. Destructive confirmation keeps its
  danger styling. Disabled primary actions return to subdued styling.
- Playnite achievement capture settings group versions, picture/timing, audio and
  location; existing semantic theme colors distinguish screenshots, clips and folders.
  The widget owns these domain groupings; no provider settings contract changed.
- Shared checkbox finding: WidgetIndexedRowView.RefreshSelection assigned the
  entire card background to the CheckBox outer control, producing a square behind
  its indicator. Host owns the fix: clear that outer fill; grid unchecked indicator
  uses WinUI's on-image resource, preserving native checked/high-contrast resources.
  Existing list/grouped rendering and exact-once selection routing stay intact.
- Consumer matrix: Capture Library migrates presentation; Playnite settings migrate
  domain grouping/styles; SDK indexed selection list/grid/grouped fixtures exercise
  shared checkbox behavior. Other shipping widgets/templates retain opt-out behavior;
  no selection contract, protocol, collection identity, or lifecycle changes.
- Playnite refinement validation: 16 focused checks and 29 native checks at normal
  and narrow dimensions passed, including Close/Escape/controller behavior.
  Capture Library and final shared checkbox qualification recorded below when complete.

- Final Capture Library refinement: 65/65 managed checks, followed by focused theme
  compilation and mixed-removal checks after final paint changes. Native toolbar
  passed 19 checks per run in normal, light and large-text/interface configurations
  (57 checks total): real mouse menus/disclosure/selection/settings return and Escape.
  Screenshots verified primary accent/contrast, wrapped filters and two full gallery
  rows after correcting the count label's obsolete flex-grow. Initial light screenshot
  preceded asynchronous row realization; subsequent light screenshot confirms rows.
  Artifacts: `artifacts/capture-ui/` (isolated synthetic gallery only).
- Applicability search: Capture Library is the only shipping consumer opting into
  indexed selection (`EnableSelection = true`). Other shipping indexed collections
  and controller templates are opt-out/not applicable to checkbox paint; list/grid/
  grouped native fixtures cover the shared mechanism.

- Final shared selection run passed 118 native checks with physical input
  (`artifacts/selection-checkbox/native/result3.json`): list/grid/grouped selection,
  exactly-once clicks, keyboard/controller semantics, bounded selection, lazy deep
  lookup and stale-focus rejection. Light/dark checked/unchecked rendered bitmaps
  confirm transparent outer hit areas and visible native on-image indicators.
  Windows OS high-contrast switching was not exercised in this refinement; its
  resource declarations were reviewed and preserve system-color semantics.
- Latest UI work is complete in source and isolated development builds, deliberately
  not installed or packaged. User review/release authorization remains separate.


## Candidate verification handoff correction

User clarified that no installer/release does not prohibit launching the development
candidate. Finish future implementation passes by launching the qualified candidate
for user verification. Do not stop at closed isolated test fixtures.

The latest frontend and staged Capture Library/Playnite development payloads are
running using `artifacts/capture-ui/user-candidate/default-profile.json`, the normal
settings/capture profile and installed catalog, with two candidate-local bundled
overrides. Installed package files, release installation and startup shortcut remain
unchanged. Real Capture Library rows and updated Playnite capture-settings groups
were observed; 315 frontend DLLs and both widget assemblies matched the checked
builds. The candidate was left open for the user.


## Follow-up gallery, detail and status refinements (in progress)

- Restore direct Screenshot and Record clip header actions. More becomes a native
  command dropdown rather than an anchored modal. Existing Select is a value editor,
  so a genuine reusable command-menu declaration was needed; do not fake a selected
  command or retain widget-owned modal state. Protocol115 opts into this behavior.
- Shared ownership: SDK supplies UI.CommandMenu and bounded command data; protocol
  validates zero selected options/no accessible value; host reuses native popup
  placement, dismissal, controller traversal, and exact captured action authority.
  Ordinary Select behavior is unchanged; mode transitions retire stale popup actions.
- Consumer matrix: Capture Library adopts command menu; existing Select consumers
  in samples/FirstPartyWidgets/templates stay value selectors. Playnite filters,
  Discord device/connection forms and PoB search remain structured panels, not command
  lists. Native Select lifecycle checks cover both modes and unchanged value behavior.
- Make the incoming related-item scope explicit outside filters with a removal action
  that preserves type/date/search choices. The existing generic browse intent carries
  opaque identity, not a display name, so use honest generic text rather than parsing
  Playnite IDs or guessing a game from file/application metadata. No broker change.
- Filters & view becomes one interactive focus/hover preview; explicit primary entry
  (mouse/source invocation and controller A) is host-owned. Existing X/Y/stick previews
  retain their primary A behavior. Reuse the same live fragment/control instances,
  not a duplicate modal or widget-managed focus state. Verify Discord/PoB mechanisms.
- Gallery count joins the title row. Cards use larger cropped thumbnails with thinner
  framing and quieter captions; original media stays intact in the viewer. These are
  widget styles/layout, not shared collection changes.
- Detail uses an approximately half-width viewer and a metadata/action column, stacking
  at narrow widths, with desktop acrylic behind the page. Keep MediaPlayer unchanged
  and verify scrolling/focus, unsaved/save/delete recovery and actual expanded viewer.
- Performance Monitor permission requests and operational errors belong near the top.
  Reuse its existing permission flow with an explicit Enable monitoring action.
- Candidate error root cause: staging omitted runtime/Performance. Restore the whole
  already-verified collector payload; do not suppress or loosen integrity checks.
- Still no installer/release. Relaunch the qualified normal-profile development
  candidate after testing, preserving installed package/release files and shortcuts.


### Follow-up evidence and corrected candidate staging

- CommandMenu: SDK182/182, compatibility 17/17, public API addition reviewed, native
  protocol parity202 constants. Native value-select/command-menu lifecycle47/47.
- Capture Library65/65 and Performance Monitor18/18 passed; detail confirmation,
  wide/narrow geometry and large-text action reveal44/44 native checks passed.
- Primary rich-preview entry validation preserves existing remembered controls;
  tests must not assume Apply is always first after exercising a slider. The native
  reading-surface subclass also requires the base ContentControl style/template;
  verify actual pointer activation and UIA semantics, not only controller entry.
- Explicit A entry adds no widget focus flags. It supports inline/deferred content,
  keeps one current-source pending entry for cold reads, cancels on departure/B/
  Escape/provider retirement, and yields A to indexed selection mode. Existing
  Discord and PoB X-entry and passive previews remain unchanged.
- Candidate now uses a cloned installed catalog with three reviewed development-only
  updates (Capture0.2.0.1, Performance 0.1.5.1, Playnite 0.4.6.1), same normal settings
  profile, and no trusted widget overrides. The supported update path carries over
  matching private state/allow-and-deny decisions/source access; credentials are not
  transplanted. Original installed catalog hash and enabled choices remain unchanged.
  Candidate development versions live only in artifacts, not source/release metadata.
- Capture permissions and Performance read/FPS decisions matched the original installed
  authorities after transfer. Playnite had no installed-authority capture grants;
  none were invented from the earlier trusted override. Normal permission requests
  remain available. Candidate-specific settings for new authorities do not silently
  sync back to old installed package authorities.


### Live inline-preview authority correction

The normal-profile candidate exposed a real failure missed by standalone UI fixtures:
choosing Screenshots in Filters & view closed the menu but retained All captures.
`WidgetPresentationSession.OrdinaryInput.cs` traversed ordinary children only, while
inline preview controls were dispatched through that route. This is a shared session/
worker authority gap, not Capture Library filter state. The fix admits only interactive
inline fragments under a current preview owner and binds the source ancestry. Protocol
115 workers recheck that binding at queued execution, including remove/restore retirement.
Existing indexed/deferred lease routes remain intact. Session380/380 and SDK183/183 passed;
a real-worker native fixture is being run before the candidate is considered fixed.

| Consumer | Inline action applicability |
| --- | --- |
| Capture Library Filters & view | Affected; shared SDK/session fix, no widget focus plumbing. |
| Discord participant controls | Indexed preview lease route; already compliant. |
| PoB item/gem previews | Deferred/indexed lease route; already compliant. |
| Playnite summaries / SDK Gallery artwork | Passive inline previews; no action migration needed. |
| Pinned interactive fragments | Existing declared pinned-action route retained; no ordinary-action bypass. |

Native testing also found an older indexed-row edge-click issue: press scaling can move
an edge hit outside a wide row. This is deferred with its own reproduction; it is not
used to weaken the new primary-entry tests or change all collection styling in this pass.

### Basic capture editing (active)

The user authorized independent implementation and controller-first testing. Scope and
qualification are maintained in `capture-library-editing-plan.md`. The host owns native
crop/trim interactions and modal lifetime; the broker retains exact source access and
publishes a new owned copy. Widget authors supply the selected source and user intent.
No installer/release is authorized. Candidate launch remains the final verification step.


## Final follow-up status

All gallery/detail/status refinements and basic editing are implemented and qualified;
see capture-library-editing-plan.md for complete evidence and candidate handoff.

A second reproduced framework defect was fixed at
Presentation/WidgetViewPresenter.Previews.cs:162: native MenuFlyout focus is outside
fragment bindings, so the old GotFocus handler exited an entered preview as a selector
opened. WidgetViewPresenter.Controls.cs now owns that exact valid flyout for its open
lifetime, revokes command authority immediately on dismissal, and reconciles the exact
opener after Closed without stealing a genuine outside focus choice. Controller option
activation reads the popup's own XamlRoot. No widget focus flags were added.

Corrected consumer matrix: Capture Library Filters & view adopts inline selector entry;
PoB samples/PobCompanion/PobWidget.CandidateInspection.cs:82 has an Equip in selector
inside a deferred candidate preview and benefits automatically, with its domain/provider
lease unchanged. Its real generic provider path passed 40/40; live PoB equip remains
unverified because no ready exported fixture was found and no user build was touched.
Discord participant previews are buttons/sliders, with device selectors in modals;
Playnite passive summaries and ordinary main-page selectors keep existing behavior.
Native ordinary selector/command-menu 47/47 and interactive preview 42/42 passed.

Broader inferred follow-up: nested ContextMenu inside an entered preview may need analogous
native focus ownership in WidgetViewPresenter.ContextMenus.cs. No shipping nested-context
consumer or runtime reproduction was found, so it is deferred rather than generalized
speculatively. The previously reproduced indexed-row edge press-scaling issue also remains
separate. Neither issue is hidden by weakened tests or widget-specific focus plumbing.

Final default-profile candidate is running with Capture 0.2.0.3 on host build 31. Keyboard
filter selection updates the real query and retains Type focus; actual image and video
edited-copy saves, playback, cancellation and cleanup succeeded with originals unchanged.
No installer/release/merge was created. Optional diagnostic-artifact deletion was blocked
by automatic approval review; files remain local and ignored by git.


## November 16 follow-up: navigation, editing input, default copies

- Reproduced from the user's video/source: `CaptureLibraryWidget.Gallery.cs:NavigateCapture` published a null preview between neighboring items, removing metadata and the retained player. Widget ownership: open first, publish the new item/preview together, then release the exact old lease. Failed opens retain the previous capture; cancellation releases late leases. No new focus counters or navigation abstraction.
- Native editor controller movement was fixed-rate. `CaptureCropView.Move` and `CaptureEditorContent.Move` now share a bounded acceleration policy, using the shell's actual Pressed/Repeated navigation phase instead of guessing from event timing. Short crop taps and 0.1-second trim taps stay precise; holds accelerate, with trim speed scaled to duration. Reversal, new presses and handle changes reset speed. Existing keyboard/pointer behavior remains covered by native checks.
- `CaptureLibraryStore.Duplicate.cs` and SDK `SaveCopyAsync` own default-folder saving, automatic names, original-byte streaming, revision checks and metadata/context publication. Capture Library supplies the source and displays the result. Explicit folder export remains supported through `CopyAsync`. Copy and edit reuse pinned-source authority and the journal/recovery mechanism; byte copies do not pass through an encoder or inherit editing output limits.
- Packaging finding: the manually staged Playnite candidate omitted `payload/integration`; discovery itself succeeded, but setup raised DirectoryNotFoundException. Sample packaging owns the fix. The archive validator checks nested files/hashes; missing bundle errors now identify the widget package. Installed Playnite profile and plugin are preserved.

Consumer matrix:

| Shared change | Consumers | Disposition |
| --- | --- | --- |
| Native dialog repeat phase | Capture editor | Migrated; crop and trim native input checks |
| Native dialog repeat phase | Permission/confirmation/source dialogs, package installation | No interactive editing content; default navigation remains unchanged |
| SaveCopyAsync | Capture Library single and batch saves | Migrated to default folder; original CopyAsync export kept compatible |
| SaveCopyAsync | Game Help, Playnite and other shipping widgets; SDK examples/templates | No existing Save a copy caller to migrate; generic API available |
| Copy/edit journal recovery | Capture editing and new copies | Shared publication/recovery, independent encoding policy |

Validation evidence is recorded under `artifacts/capture-navigation` and `artifacts/capture-ui`; source checks alone do not establish native correctness. Candidate-only updates; no installer, release or main merge.


Follow-up verification: 89 Capture Library checks, 184 SDK checks, 382 shell checks, and 30 Playnite setup cases passed. Native crop/trim testing additionally exercises actual keyboard/pointer input and controller dispatch; shared image replacement is checked for delayed resolution, owner changes, failures, and option changes during decoding. The in-place image viewer now decodes a replacement bitmap before showing it; the old frame can remain noninteractive for at most 200 ms within the same owner. Game Help's capture preview uses the same player and receives this fix; SDK Gallery's current media-player consumers are video and retain their existing path. Long-copy IPC gets the existing bounded copy deadline without a picker allowance. The normal installed catalog hash is unchanged; development candidate only.


Final native evidence: `artifacts/capture-navigation/native-image-final2/result.json` passed all 35 image/editor checks. The final 30-fps mouse/keyboard Next/Previous recording (`navigation-verified.mp4`) contains 240 frames and no blank image frames; the earlier recording reproduced eight blank frames across four swaps. `navigation-frame-check.json` records the pixel check; this is bounded evidence for the exercised screenshot transitions, not a claim about every decoder/display. Live Save a copy created an automatically titled, directly opened library item without a picker; only that verification copy was subsequently recycled through the library UI. The final candidate is Capture Library 0.2.0.5 / Playnite 0.4.6.2, with rebuilt host/worker/broker. Playnite's Start-and-connect flow reached its populated home page. Broker tests: 93 passed. Game Help shares the tested native image player; its full provider workflow was not separately exercised in this follow-up.


## November 16 preview and typography follow-up

The capture browse message now has a v2 contract with a bounded `referenceLabel`. Matching remains the same integration package/kind/opaque reference. The Playnite widget supplies the game or achievement name it already owns; Capture Library displays it separately from the query, preserves it across ordinary filters, and removes it when the related filter is cleared. v1 messages remain accepted with a generic label. No Playnite plugin, SQLite schema, or broker query change is involved.

Reproduced hover failure: native hit testing over `capture-library.filters.label` resolved the Text binding and rejected it as non-navigable, whereas padding resolved the ReadingSurface. `WidgetViewPresenter.Previews.cs:PreviewSourceFromTarget` now follows declared parent identity to the nearest focusable source, stopping even if that source is disabled. Indexed sources retain their existing exact-row path. No widget hover timers, input counters or label parsing.

Outside presses: the visible preview observes its XamlRoot's existing routed PointerPressed event, including handled presses. It excludes its opener and content, dismisses without returning focus, and never marks the event handled or replays it. Native menus retain their own routing. Pin activation and cross-window input revocation continue through the existing shell; no duplicate pin/outside-click policy was introduced. Native button and handled-background clicks are checked for one delivery and correct focus. A physical cross-window pin click is not separately exercised in this follow-up.

Reproduced typography mismatch: the running page/native collection used Segoe UI, but generated GridViewItem containers reverted to Segoe UI Variable; their captions inherited it. Transform inspection found no additional row scale beyond the configured interface scale. Indexed row style adapters now inherit the collection font as a fallback, preserving explicit authored fonts. A native font-property subscription updates realized containers on font changes and is detached with the native view.

| Shared change | Consumers | Applicability |
| --- | --- | --- |
| Capture browse v2 | Capture Library / Playnite | Receiver supports v1 + v2; sender adopts v2; old contract preserved |
| Capture browse v2 | Other shipping widgets, templates, SDK examples | No other production browse sender found |
| Composed preview source resolution / outside press | Capture Library, Marketplace, SDK Gallery; Playnite passive summaries | Shared host behavior, no widget plumbing |
| Composed preview source resolution / outside press | Discord / PoB indexed and deferred previews | Existing row/provider authority retained; shared popup dismissal applies |
| Indexed font inheritance | Capture Library, Games & Apps, Settings, Playnite, Discord, PoB, Spotify, YouTube Music, FullApplication sample | Shared native list/grid containers; explicit row fonts remain authoritative |
| Other widgets / non-indexed content | No matching generated-container path | Not applicable |

Tests so far: Capture Library 90/90, Playnite 246/246, SDK 184/184. Native evidence and final candidate checks are under `artifacts/preview-followup`; source applicability alone does not establish native correctness. Two initial outside-click fixture attempts targeted clipped/off-window test coordinates; those fixture positions were corrected without changing production click policy.


Qualified result: all 53 native interactive-preview/typography checks passed (`artifacts/preview-followup/native-preview-trace/result.json`). This includes normal and already-handled outside presses, no lost/duplicated external button action, real hover leave-and-return, dropdown/slider retention, and inherited/explicit fonts for native grid and list items. The running candidate's fonts matched Segoe UI for both page and all realized capture titles/captions; there was no extra row scale. Native navigation from Playnite verified the game label `ACE COMBAT 8: WINGS OF THEVE` and the achievement label `Yes, Sir!`, including empty result sets. The read-only typography instrumentation was initially too expensive when repeatedly inspecting Playnite's full tree, so it now takes one typography snapshot; the final candidate runs without diagnostics enabled and remained responsive during the same Playnite flows. The normal installed catalog hash remained unchanged. Candidate: Capture Library 0.2.0.6, Playnite 0.4.6.3, rebuilt shared host/worker/bridge. No installer/release/merge.


Raster-quality qualification: correcting font inheritance alone did not remove the remaining card softness at 90% interface zoom on the 125% display. The user also confirmed 100% interface zoom removes it. Text-only and row-root pixel snapping did not change the card pixels. Direct composition access on the native SelectorItem conflicted with WinUI's scale-animation ownership and crashed the isolated probe; that approach was rejected and removed. Setting the supported XAML UIElement.RasterizationScale on the item produced visibly sharper text without resizing layout. Text-only rasterization had no effect because the item is cached as a whole.

The shared native item style adapter now uses IndexedRasterizationPolicy: no boost at 100% interface zoom, at most 1.5 per dimension at other zoom levels, and a 512 x 512 estimated raster-pixel budget per boosted item. Large/high-DPI items are capped further or retain their original quality. Explicit existing native quality settings are preserved. Size/DPI/appearance changes recompute the setting; disposal restores it and detaches subscriptions. This applies to the same indexed list/grid consumers listed above, with no widget-specific workaround or loss of virtualization. Experimental file-controlled raster mutations were removed from the opt-in diagnostics.

Performance limitation: 1.5 per dimension means up to 2.25 times the pixels for an affected item surface, not 2.25 times whole-overlay frame cost. For a 240 x 220 DIP card at 125% display scale, the incremental RGBA-equivalent storage is approximately 0.4 MiB per surface. This is an estimate, not measured GPU allocation; native caches, buffers, and realization affect actual cost. No representative GPU-memory/frame-time benchmark has been completed, so this change is not qualified as performance-neutral. Screenshots compare the same live grid at several raster factors under artifacts/preview-followup; 1.5 was chosen over 2 to bound the tradeoff. Automated shell tests pass 384/384, including policy bounds across zoom, DPI, and item sizes.


Final follow-up qualification: rebuilt host with zero warnings/errors; 384/384 shell checks and 59/59 native preview/typography/raster-quality checks passed. The first native run timed out in the real hover-return sequence; an unchanged rerun passed, so the initial failure is retained as an intermittent automation/runtime observation rather than erased or attributed to a proven cause. The user was also interacting with the candidate during subsequent live inspection. Final native result: artifacts/preview-followup/native-quality-confirm/result.json. The user then explicitly verified that the cards look good at 90% interface scaling on their monitor. The clean candidate remains running (PID 28332 at verification), without diagnostic flags. No installer, release, merge, or normal-catalog write was performed. GPU/frame-time impact remains unmeasured; correctness tests and visual acceptance do not establish performance neutrality.


## November 16 button flash on return to Capture Library

Runtime reproduction: recording `artifacts/preview-followup/back-flash-before.mp4` shows the new gallery's native buttons, including Search, briefly filled light gray before their themed dark fill settles. Search does not declare Busy or Disabled here, ruling out the initial hypothesis that the widget's global busy flag alone explains the flash. Retain action guards and operation sequencing; no widget busy-state rewrite is justified by this finding.

Owner: host native button paint. `Presentation/WidgetCollectionTemplates.xaml` declares an 83 ms ContentPresenter.BackgroundTransition; `Presentation/WidgetNativeButtonStates.cs` previously suppressed native state brush storyboards for WRSS-owned channels but left this implicit background transition active. The source hypothesis is a native-to-authored initial brush fade on recreated page controls. Extend the existing ownership mechanism to suspend that transition only while an authored background owns the channel, restoring the exact original when ownership is removed or retired. Apply ownership before assigning the new background. This is a bounded host correction, with no SDK/protocol contract or widget timer.

| Consumers | Applicability |
| --- | --- |
| Capture Library toolbar/search/detail buttons | Affected; live detail-to-gallery recording is the reproduction |
| First-party Audio Mixer, Clock, Display Profiles, Games & Apps, Marketplace, Media Sessions, Network Controls, Performance Monitor, Power, Settings | Shared Button / Select / TextEntry native path adopts automatically |
| Shipping Discord, Playnite, PoB, Game Help, Spotify, YouTube, YouTube Music; SDK Gallery / FullApplication / EmbeddedMedia examples | Shared native controls adopt automatically; embedded web content unaffected |
| ControllerWidget templates and SDK examples emitting native commands | Existing API already compliant; no author-side changes required |
| Native controls without an authored background, native OS dialogs, GridView/ListView item caches | Original transition restored or different path; no behavior change intended |

Validation targets ordinary buttons, value buttons (Search/Select), and disclosure buttons, with initial realization, enabled/disabled changes, background ownership removal, disposal, and existing busy-focus/input admission behavior. Full provider workflows of every consumer are not separately exercised. Native evidence and final result are recorded below.


Qualified fix: build passed with zero warnings/errors; 26 native button paint and busy-focus checks passed (`artifacts/preview-followup/native-button-paint/result.json`). Actual mouse Back-to-library navigation was recorded at 60 fps after the fix. In the identical Search fill sample, the pre-fix recording rises to luminance 85.94 before settling to 28; after the fix it reaches 28 directly with no brighter flash. Evidence: `back-flash-before.mp4`, `back-flash-after.mp4`, and `back-flash-pixels.json`. This confirms the native background-transition diagnosis for the reported return path; it does not claim every consumer's provider workflow was tested. The candidate is running without diagnostic flags (PID 20496 at verification). No widget behavior, package version, installer, release, or installed-catalog change was needed for this host-only fix.


## November 16 host-owned page navigation cache (implementation in progress)

User authorization: host-only page caching, preserve existing focus/scroll memory, verify other widgets, launch the candidate. No widget, SDK or protocol migration is planned. Existing top-level Page transition group/key provides identity; section/content transitions retain their separate semantics.

Bounded first implementation: one prior eligible native page per presenter, at most three across the UI host, at most 512 declaration bindings per cached page. Virtualized collections keep only their existing resident window. The cache uses the existing collection suspension path: cancel demand/release leases, retain logical slots and decoded paint, reacquire current authority on resume. Hidden pages remain mounted under an input-disabled, automation-empty transparent parking layer so native row Unloaded does not destroy their paint. Same-scope/topology checks admit reuse; latest properties and collection query descriptors reconcile before input resumes. Native live media/browser/window-preview and focus/background-surface pages are excluded initially because their independently owned presentation lifetimes require separate qualification.

Existing page memory remains authoritative for logical focus. A cache hit preserves the native ScrollViewer position; eviction/reconstruction still runs the existing memory restore. Existing indexed return-intent and viewport machinery remains in place; runtime checks must establish that it does not unnecessarily move a retained grid. Scope/owner changes must not inherit old input authority. Owner retirement clears cache and pending work.

| Consumer | Applicability / validation |
| --- | --- |
| Capture Library home -> detail -> Back | Eligible home retained; media detail not cached; primary runtime acceptance |
| Settings native pages | Eligible ordinary top-level Page declarations; focus/scroll and eviction regression checks |
| Playnite / PoB sections | Content transitions, not Page navigation; unchanged by cache, regression-check native section transitions and representative live UI |
| SDK TransitionPage consumers / templates | Existing declaration automatically eligible under same bounded rules |
| Media/browser/focus-surface pages, pinned layouts and row/preview fragments | Excluded with existing memory and rendering behavior preserved |

Cache-miss preparation-before-reveal is a separate proposed enhancement; do not weaken input or retention safeguards to force it into this cache implementation. Record qualification and any narrowed assessment below before declaring complete.


Scope correction following review: section transitions also cover real destinations (Playnite Home/Library). The initial page-only proposal was too restrictive for the user's observed reload problem. The shared cache now considers keyed Page and Content regions, retaining only the changed subtree while shared headers remain active. Passive and interactive presentation surfaces use existing fragment suspension; open previews are dismissed before retention. Browser/media/window-preview subtrees and modal content remain ineligible because independent native lifetime owners need separate qualification. Compatibility checks include input scope, structural identity, declared parent/child topology, surface-owner ancestry, and render mode; changed properties/data descriptors reconcile on reuse. This does not cache an unchanged key's ordinary data updates.

User-selected limits: TWO previous destinations per widget, THREE across the host, excluding active destinations. Evict the least recently used cached destination when either bound is exceeded. Lightweight focus/scroll history survives. Count bounds are not byte bounds: measure process memory under repeated navigation and qualify GPU allocation separately; do not claim a negligible or fixed overhead from counts alone.


Integration findings: (1) native focus can move an ordinary retained ScrollViewer; page history now records semantic scroll anchors and uses the existing memory-restoration lane after focus settles. Its settled ViewChanged event clears the prior requested offset so a superseding BringIntoView cannot strand restoration. (2) cache parking must not run during presenter disposal. (3) motion-disabled content changes with shared IDs must not reuse a binding that is concurrently owned by the cache; active reconciliation now excludes cached bindings. Regression coverage checks native focusability in that reduced-motion case. All three are host-owned lifecycle/geometry issues, not widget workarounds.

User requested faster focus and selected immediate retained focus plus a ready-target fast path. Cache return adopts the existing RestoreRetainedFocusPresentation path for loaded visible targets, without an input grant. Indexed Enter reuses existing CompleteNavigation admission/container checks when the target is current and fully visible; stale rows keep the existing deferred path. The native indexed-entry suite adds explicit synchronous-focus/no-scroll checks.

Qualification in progress: 384 shell unit checks, 44 native indexed entry/refresh checks, 10 reveal checks and 31 stale-activation checks passed. The first collection-return invocation used the generic server fixture, which omitted its required tall-row WRSS; its height assertion correctly failed. The corrected dedicated --serve-return-validation fixture passed all 51 assertions unchanged. Keep both evidence sets; this was a harness configuration error, not evidence of a fast-path regression. A separate ordinary-page fixture was corrected to establish a settled, valid focused viewport rather than issue conflicting setup scroll requests. Native page/cache/section/memory suite is rerun after each relevant correction.

Live evidence: Capture Library cache hits retain populated thumbnails on return (cache-return-live.mp4). Playnite Home/Library, Settings category pages and PoB Calcs/Items produced cache hits. Six Playnite round trips fluctuated around 617–655 MiB frontend private commit, ending near the starting level; these are Debug whole-process samples including artwork, not isolated cache cost or GPU memory. Files: cache-memory-cycles.json and navigation-cache-live2.jsonl. Final no-diagnostics candidate and final test totals remain to be recorded.


Final qualification (November 16): host build succeeded with zero warnings/errors. Navigation cache/page/section/ordinary-memory/reduced-motion/teardown and native pointer-drag suite passed 43/43 (`native-navigation-cache-all-final/result.json`). Indexed entry suite passed 47/47, including immediate ready-target focus with no extra scroll (`native-indexed-entry-qualified.json`); reveal 10/10; stale-activation 31/31; dedicated collection-return suite 51/51 (`native-collection-return-final.json`); shell unit suite 384/384. The ready-target fixture now sizes and restores its own viewport, and proves full visibility before claiming a synchronous fast-path check. The legacy section-transition probe now selects visible outgoing layers, excluding the intentionally invisible cache parking layer; its modal clipping assertion remains unchanged.

Cached navigation releases native pointer capture with the existing PointerCaptureLifetime mechanism and retires unsent slider intentions while preserving the control. Native held-drag/page-away/page-return checks verify capture is gone, control identity is retained and unpressed mouse motion cannot continue the old gesture. Immediate retained focus respects a newer explicit group/item/target intent rather than briefly highlighting an unrelated remembered item. Visibility checks reuse arranged layout bounds so focus-scale paint is not mistaken for offscreen geometry.

Final consumer matrix: Capture Library, Playnite Home/Library, Settings pages and PoB Calcs/Items have live cache-hit evidence. Display Profiles, Power, Network Controls, Spotify and YouTube Music use the same Page/Content contract and adopt automatically where their region is eligible; their full provider workflows were not separately exercised. SDK examples/templates remain API-compatible. Browser/media/window-preview and modal regions retain their existing owners and are not cached. Cache misses (including eviction/incompatible structure) retain the existing loading/reconstruction flow and focus/scroll memory; no new wait-for-all-thumbnails policy was introduced. This follows the user's selected immediate-focus approach rather than delaying the entire page.

The normal development candidate was launched without diagnostic flags (PID 32488 at verification), using the existing cloned catalog with Capture Library 0.2.0.6 and Playnite 0.4.6.3. This was a host-only change: no widget migration, installer, release, merge or normal installed-catalog update. Process memory samples remain whole-process smoke evidence, not an isolated cache-overhead or GPU-memory benchmark.


## November 16 isolated navigation-cache memory probe

Added explicit opt-in validation (`--navigation-cache-memory-output`) to sample the same active UI with hidden cache entries retained and then evicted through their existing retirement path. Both samples run the same two GC/finalizer settling passes; no working-set trimming. Normal launches do not enable the probe or force collections. Evidence: `artifacts/cache-memory/paired-summary.json`, paired raw JSON files and `probe.jsonl`.

Observed private-commit reductions after eviction (MiB): Capture Library gallery 7.05, 7.93, 7.76 across three trials; Playnite Library 19.65 and 11.40; Playnite Home 9.64 and 18.98. A mixed three-entry cache (Playnite Library, Capture gallery, Settings root) released 7.27 MiB private commit and 16.17 MiB working set, with 9.12 MiB fewer managed live bytes. Its later empty-cache control dropped another approximately 7 MiB of private commit without cache eviction, demonstrating background/allocator noise. These are measured released-memory deltas, not exact ownership accounting or a fixed per-view cost. Do not sum per-view observations into a claimed host maximum.

Dedicated GPU counter reductions were 0–30 MiB for Capture gallery and 30–49 MiB for Playnite; adapter totals tracked the same reductions in these samples. Shared GPU allocations and driver resource pooling prevent exact attribution. Zero immediate release does not imply zero cache GPU cost. Neither the GPU results nor the mixed-cache trial establishes a hard budget. The implemented limit remains two previous views per widget and three across the host, not a byte cap. Large-artwork, DPI/zoom, and resident-row differences can change costs. Earlier 617–655 MiB readings were total frontend memory, not navigation-cache overhead.

This task adds validation instrumentation only; no SDK/protocol/widget behavior changes. Existing native cache/focus/scroll qualification remains applicable. The probe host built successfully with zero warnings/errors and exercised real Capture Library, Playnite and Settings navigation. Restore the ordinary candidate without the probe after measurement; no installer, release, merge or installed-catalog change.


## November 16 resumed navigation investigation

Active goal now includes the approved Windows recent-gameplay action and gallery experience work; both follow correctness qualification. Frame-by-frame video evidence and initial memory breakdown are in `artifacts/navigation-review/review.md`.

Reproduced in real Playnite: an opt-in replay routes RightBumper then eight Down events at 40ms intervals through the production presenter. Before the fix, the library finishes loading with `focused=null`, `needsEntry=false`, and no collection entry pending (`live4.jsonl`); later scrolling can recover it. Owner: host `WidgetViewPresenter.MoveFocusCore` / `InputRoots`, which canceled the initial handoff before there was a current navigation origin. The existing parking guard covered only explicit native collection parking, not absent/outgoing focus during page replacement. Extended the same guard to those pending-entry states; explicit pointer/native departures keep existing cancellation rules. Keyboard arrows and controller directions use the same predicate, with no queued movement backlog.

Retained-focus paint now selects a current explicit group/item intent before remembered header paint. Existing identity/scope/visibility checks remain in force. Indexed native navigation uses its existing readiness notifications at Normal dispatcher priority, while coalescing the latest target. A broader experiment raising all page-entry work to Normal failed the native startup-focus prerequisite; rejected and reverted that experiment rather than weakening the fixture. After reverting it, the native cache/page/section/memory suite passes 43 checks (`entry-guard-cache/result.json`). Earlier indexed-entry checks passed47; remaining return/live verification continues.

The capture-grid issue is not established as fixed: cache hits retain collection/source identity, but WinUI reassigns native row presenters on return, including swapping the second and third occurrences in a three-item gallery. Their differing child IDs correctly force artwork reconstruction. This explains why page/control identity assertions alone missed visible blank thumbnails. Do not add a second row cache or relax occurrence/authority checks to conceal this; investigate stable mounting/realization and qualify actual pixels. Indexed row artwork also now adopts the existing ArtworkAuthorityCurrent mechanism already used by background/preview fragments, preventing a retired resolver from treating unavailable authority as authoritative absence; this is an inferred lifecycle gap, not the claimed root cause of the observed container reassignment.

Consumer applicability for pending-entry guard: Playnite reproduced; all native indexed consumers (Capture Library, PoB, Discord lists, Spotify/YouTube Music and relevant first-party lists) share the host path and require no widget counters. Existing native entry and return fixtures cover delayed data, explicit departure, teardown and query replacement; full provider flows are not yet requalified. Ordinary pages use the preserved Low-priority entry path. Browser/native external dialogs are not changed.


Live early-direction verification after the guard: three production Playnite trials (one cold Library entry, two cache hits) all settled on visible native item21 with no pending entry (`early-directions-fixed.json`). Native cache43 and collection-return51 passed. The initial return-harness invocation omitted its required settings-root argument and ran the general bridge suite rather than the dedicated server; its timeout/failures are retained as harness evidence, not attributed to navigation. Corrected invocation passed51 unchanged. Do not describe the unfiltered bridge-suite failures as qualified results.

A bounded top-level cache experiment now retains roots under the existing WidgetMotionStage rather than moving them into/out of the parking canvas. It maintains prior layout constraints, excludes hidden roots from input/automation, and uses the same motion and eviction owners. Nested sections still use the existing clipped placement path; no second row/pixel cache is introduced. Native and real-grid qualification pending; reject if behavior regresses or retained pixels still disappear.


Stable-root cache qualification: rebuilt with zero warnings/errors; native cache/page/section/memory43 and indexed-entry47 checks pass (`stable-root-cache/result.json`, `stable-root-entry/result.json`). The actual three-capture gallery completed two detail/return cycles without any row-presenter reassignment; all six refreshed artwork publications retained existing pixels. Recorded frames show all thumbnails and the selected card present immediately on return (`stable-root-live.mp4`, `stable-root-return.jpg`). A deep return in the394-item real library preserved focus on item82 and the exact3252.000244px viewport offset, with zero row reassignments (`stable-root-deep-return-qualified.json`). An earlier attempted item35 probe targeted a non-realized item after query continuity moved the viewport; its output is not a successful navigation test. Nested section mounting, additional consumer coverage and final clean-candidate qualification remain outstanding. No claim is made that this top-level fix alone eliminates all Playnite section repaint costs.

Current development process at this checkpoint: PID33524, host-cache-stable-root-probe build, diagnostic candidate; no installer/release/merge. The user expanded scope to Discord messaging/sharing and recreated the active goal; existing in-widget X mute verification is excluded by their explicit correction.


## November 16 Discord dashboard implementation

Discord now publishes the existing shared QuickActions contract: Left Bumper deafen/undeafen, X mute/unmute, Right Bumper leave call. It reuses the immutable call/account/session/state action IDs and command lane already used by voice controls; busy, signed-out, no-call and unsupported controls are withheld appropriately. No existing in-widget X shortcut code or dedicated shortcut check was changed. Owner: widget adoption of an existing SDK/host API, not a framework gap. Audio Mixer and Spotify were inspected as representative consumers; no shared quick-action behavior changed.

`tests/DiscordWebViewWidget.Tests/DashboardTests.cs` covers actual SDK dashboard input dispatch at the host's Visible lifecycle, held-toggle suppression, busy publication, external mute-state changes and stale account/call actions. The first fixture incorrectly attempted action admission at Background; source inspection confirmed the production dashboard promotes its target to Visible. Corrected fixture preserves the SDK admission rule. Build has zero warnings/errors and the Discord suite passes524 checks (`artifacts/discord-dashboard-tests-build2.log`, `discord-dashboard-tests-run2.log`). Candidate package staging and native tray presentation verification are pending; tests do not prove the currently running package contains these edits.

Parallel ownership: discord_messaging_provider owns new provider messaging module + JS fixture tests only; windows_capture_plan_probe and shared_attachment_audit are read-only. Root owns C# UI/session integration, all SDK/protocol/broker/host changes and plan updates. Existing browser file-picker CDP staging and capture original-file/chunk reads are confirmed reuse candidates; attachment authority design is still under audit, not yet implemented.


Parallel follow-up checkpoint: Discord message DTOs now validate account/channel/nonce identity, bounded full incoming text, attachment metadata and CDN origins, pagination progress, and ambiguous-send status (`Domain/DiscordMessages.cs`, `MessageContractTests.cs`). Combined Discord checks now pass535 (`discord-message-contracts-build.log`, `discord-message-contracts-run.log`); these are boundary/fixture evidence, not native messaging UI or live Discord delivery qualification. Incoming messaging provider module is being implemented separately; root still owns integration and native UI.

Windows host primitive is implemented by the isolated input subtask: `MainWindow.SaveRecentGameplayAsync(page, authorityCurrent, token)` returns Requested/KeyboardBusy/TargetUnavailable/InputRejected/Cancelled using exact remembered target, existing handoff/release and a shared guarded chord helper. Task View adopted the helper;387 shell unit checks passed in `artifacts/recent-gameplay-probe`. No actual capture dispatched. Broker/SDK request plumbing, known Windows Captures link UI and native verification remain pending. Read-only local probe confirmed `KnownFolders.AppCaptures.Path` resolves the real folder and HistoricalCaptureEnabled=1; stored preference is not proof of an active buffer.

Attachment authority decision: a new generic file-reference service will adopt original-file/chunk-read and native-picker mechanisms. Intents carry an opaque offer ID; authenticated intent commit authorizes the exact recipient worker to redeem it. Accepted per-file grants are independent of the sender's broader linked-folder permission and survive ordinary sender idle/unload/uninstall, while pending offers remain source-bound. Receiver teardown/release/expiry and exact file identity checks govern accepted references. This is explicit selected-file transfer, not blanket Capture Library permission. Root owns existing intent delivery/picker integration; the shared_attachment_audit subtask owns contracts/registry/SDK transport and focused tests.

## November 16 bounded backend script capacity

The composed Discord messaging backend is 78,192 UTF-8 bytes after syntax-only minification, exceeding the former shared 64 KiB frontend script allowance. Ownership is protocol/bridge/frontend: `WebSessionContract` now names separate 64 KiB isolated-page-action and 128 KiB full-trust-session-backend script limits. `EmbeddedMediaAssetResolver.ResolveWebSessionAdapter` applies the selected limit before allocating/reading the sealed asset; `WebSessionPageAdapter.DecodeScript`, used by `BrowserSurface.WebSession`, applies the same byte limit and validates base64, SHA-256 and strict UTF-8 before execution. Exact origin, package inventory/digest, ownership, full-trust admission and execution-world checks remain in their existing owners. No credential, message, response, or page-action authority is expanded.

Transport audit: adapter assets flow through `WidgetBridgeServer.HandleWebSessionsAsync` on the existing direct bridge, not the platform-broker JSON response channel. `WebSessionRegistry.TakeAsync` carries one session work item per batch. A maximum backend asset is about 171 KiB in base64, below 180 KiB including its resource envelope and well below the existing 12 MiB bridge frame. Packaged web applications retain 256 KiB per-resource / 2 MiB aggregate limits; isolated scripts and ordinary web-session messages retain their 64 KiB bounds. No new transport, chunking, or raised pipe limits were needed. Earlier hosts can reject the larger backend; this is not an automatic compatibility fallback.

| Consumer | Applicability and verification |
| --- | --- |
| Discord native backend (`DiscordSession`) | Affected; builder and exact composed-asset checks adopt 128 KiB. Generated `artifacts/discord-backend-budget.js`: 135,090 source bytes to 78,192 output bytes, adapter33. |
| PoB trade page actions (`PobWidget.Preferences`) | Existing isolated mode retained at 64 KiB; shared boundary tests confirm backend-only expansion. No widget migration. |
| Spotify, YouTube Music, Game Help, Browser, other shipping first-party/sample widgets | Package or unadapted browser paths; no page-adapter declarations found outside Discord/PoB. Package and message budgets unchanged. |
| Templates and SDK examples | No additional production adapter declaration found; hosted-web-session documentation example uses the same backend contract and now documents both budgets. |
| Native backend/microphone/capture validation fixtures | Existing modes unchanged; protocol ownership/microphone admission tests pass. Native execution remains for root's candidate qualification. |

Checks completed: PlatformBroker focused web-session suite7/7, WidgetBridge sealed-resource budget scenario1/1, builder budget tests2/2, exact generated backend checks5/5. Both .NET builds completed with zero warnings/errors and binary logs under `artifacts/backend-budget-*.binlog`. The first generated-backend check correctly failed its old hardcoded64KiB assertion; it now uses the builder's explicit backend budget and passes. Tests cover exact UTF-8 boundaries, one-byte overflow, invalid mode/base64/UTF-8/digest, tampered sealed assets, retained isolated-mode rejection, and frame fit. No frontend build, app launch, installer or release was performed by this subtask; runtime/provider compatibility is not claimed from these checks.

### Combined Discord/capture integration checkpoint — 2026-11-16

Native messaging now uses the existing query resource for bounded conversation pages and the existing cursor resource only for data history. An attempted direct cursor `Present` projection failed production WinUI validation (`winui_legacy_collection_anchor/generation/reset`), so that presentation was rejected, not exempted from validation. Like Spotify's indexed queue, Discord publishes the bounded history window through `WidgetLoadedCollection` and `UI.CollectionList`; the SDK owns row occurrence continuity and realization. Explicit Older/Newer actions use cursor `Prefetch`, without legacy focus metadata. Full message text and attachment links use existing reading surfaces and anchored rich previews. This is source/fixture qualified; live provider/native message interaction remains pending.

Discord native fixtures currently pass 595 checks including dashboard actions, native message declarations, draft edits during send, ambiguous-send reconciliation, account isolation, attachment receipt validation, bounded 16-MiB stream tests, and incoming file handoff without automatic upload/send. Capture sender tests cover accepted/rejected/cancelled/failed routing cleanup, stale feedback, and late preparation after navigation/cancellation. The source retains an independently opened library attachment until the intent outcome. The host transfers only an opaque offer through the standard intent pipeline; accepted recipient file custody survives source teardown. Private Discord upload negotiation remains provider-dependent and has not been live-delivery qualified.

The combined frontend build (`host-discord-files-windows-capture`) passed with zero warnings/errors. Native cache43 and entry47 passed on that build. The native return suite failed check35, smaller viewport geometry, in `files-windows-combined-returns/result.json`; retain this failure and investigate before calling the candidate qualified. No installer/release or installed-system update occurred. Candidate staging stays under artifacts.

Consumer matrix additions: Discord adopts Files choice/receipt streams and receives standard Files intents; Capture Library adopts prepared outgoing Files intents; browser and widget file choice share shell/native dialog lifetime handling; Game Help and Playnite retain existing capture/folder paths because their domain operations differ. Existing capture folder and permission host-pipe tests remain passing. Windows recent-gameplay uses the existing capture permission and foreground handoff, with a fake-launcher native validation planned rather than generating recordings during tests.

### Native indexed row layout qualification — 2026-11-16

The combined return failure exposed a host readiness gap: an admitted data lease and loaded container could still represent the 48px loading estimate when viewport restoration completed, before the real 84px tall row was arranged. `WidgetIndexedRowView.Layout.cs` now reports the exact row's completed native layout through the existing container owner; `WidgetIndexedCollectionView.Memory.cs` waits for that layout before completing geometry restoration. Immediate focus remains independent and unchanged retained rows require no new layout wait. No timer, new cache, widget counter or stable-root mounting change was added. Applicability: all native indexed collections use the host fix; Capture Library/Settings page caching and Playnite/PoB section navigation retain their existing ownership. Non-indexed views are unaffected.

Preserved evidence: `files-windows-combined-returns/result.json` failed at check35. The subsequent `row-layout-ready-returns/result.json` passed the resized geometry (row y28.80005 + height84, viewport112.80000) and49 checks, then timed out waiting for an injected native wheel event. That artifact does not establish why the event was missing. Diagnostic-only instrumentation now records planned/actual cursor, foreground/point HWNDs, DPI, current viewport and root-versus-scroll routed events; no production wheel handling was changed to work around the timeout.

After the clean `attachment-wheel-diagnostics` build (zero warnings/errors), sequential native fixtures passed: `wheel-diagnostics-returns/result.json` **51/51**, `wheel-diagnostics-repeat-returns/result.json` **51/51**, `row-layout-qualified-cache/result.json` **43/43**, and `row-layout-qualified-entry/result.json` **47/47**, all under `artifacts/navigation-review/`. The instrumented wheel pass confirmed the correct foreground and native child, planned/actual cursor equality,120DPI matching1.25 XAML scale, the same live ScrollViewer and actual routed wheel delivery. The wheel canceled pending return and late rows preserved first visible item70 at y=-15. The earlier missing-wheel cause remains undetermined; these successful runs do not retroactively turn that failure into a pass. Test processes were closed by their own fixture cleanup; no user candidate, installer or release was launched by this subtask.


### Windows recent-gameplay and native picker qualification — 2026-11-16

The combined `host-row-layout-and-native-files` frontend built cleanly. Root ran the native fixtures: `artifacts/discord-capture-candidate/picker-first/result.json` passed **7/7**, including cancellation before admission, concurrent-picker rejection, real Windows dialog cancellation/join, and restoration of the invoking control. `artifacts/discord-capture-candidate/recent-first/result.json` passed **89/89** across12 cases: full/reduced handoff, held Shift, revoked request before/during/after hide, cancellation, reopening, stale identity, rejected input and replay. The recent-gameplay fixture used a controlled peer window and intercepted the chord; **no Win+Alt+G was sent and no Windows recording was created**. Controller A and broker revocation in this fixture are controlled inputs; actual broker ownership/revocation is separately covered by the passing host-pipe/non-UI tests. This establishes native handoff and shared picker lifetime behavior, not Game Bar recording availability, successful clip creation, hardware-controller behavior, or a live Discord upload.

Supporting checks: Capture Library **94/94**, shell unit checks **387/387**, and focused real host-pipe scenarios for files, recent gameplay, permission prompts, capture folders and closed request classification passed. Browser and widget file selection share the same shell input/focus lifetime and native cancellation helper; the browser provider's own upload workflow was not rerun by these fixtures. No installer, release, installed-profile update or real capture command was performed for this qualification.


### Live messaging qualification follow-up

Live inbox reads succeeded (40 conversation controls), but history initially failed. Sanitized native probes identified the selected `get/post/del/put` export as a raw positional HTTP utility (GET/POST arity3), not Discord's object-request facade. Adapter34 rejects that observed mismatch through the existing webpack finder with a bounded predicate; no API prefixes or credentials were invented. Relevant Node tests and exact composed backend tests pass. The typed candidate still times out in the live history path: managed opt-in evidence reports request-stage `WidgetCapabilityException` code `page_request_failed` roughly29s after the history action. Provider diagnostics had no completed failure. This is not a qualified history/send runtime success; the next probe inspects the actually selected module and bounded static candidate signatures.

The first live Capture Library→Discord handoff exposed a widget navigation error: pushing Messages while its authored scope already existed under Conversation returned RejectedConflict, which the widget ignored. Incoming share now uses NavigateRoot(Home)+Push(Messages) before adopting files, and Messages derives its initial-focus target from the same captured body/model. The fixture reproducing an incoming share from an existing conversation passes. Live handoff must be repeated with the corrected staged version. Source remains provider-neutral and only sends the generic file offer.

The new preview API briefly replaced two existing CLR signatures with extra optional parameters. Original `FocusPreviewContentContract.Validate(ViewNode,bool)` and the existing presentation-session media-resolve signature are restored as forwarding overloads. This was a real binary-compatibility issue; it is not enough that old C# call sites still compile. A diagnostic widget startup failure coincided with mixed staging; coherent rebuild/restaging restored startup, but no raw startup exception was captured, so exact causal attribution is not claimed.

Remaining messaging UX qualification includes initially showing the latest history and reflecting confirmed own sends without forcing a reader away from older messages. The SDK already has WidgetFocusRequest.RevealCollection (YtMusic queue precedent); adopt its explicit intent and scope rules rather than author-managed reveal counters. Test long text/attachment previews, actual controller entry, older/newer reversal and account changes. No real message or upload has been sent during qualification.

### Capture gallery media previews — source and non-UI qualification

Ownership audit: `CaptureLibraryWidget.Gallery.cs` now supplies card content, context-action intent and the saved capture ID/content revision. The existing `FocusPresentationSurface`, indexed row lease resolver and native `MediaPlayerView` own deferred realization, input entry, fullscreen, playback and teardown. The missing shared contract was a declarative saved-library media reference and an explicit one-player preview opt-in (protocol 116); ordinary interactive fragments already supported input, so no presentation-only safeguard was relaxed. No provider-specific resolver, SQLite schema, second player, arbitrary tree walk or hover timer was added.

`HostMediaPlaybackLeases` uses the library's existing gated revision validation and retained-original capture registry. It rejects changed owner/query/item/content proof, joins late acquisitions on retirement, and stores no native resources in cached declarations. Passive playback forces mute on every reopen and disables the native player's CommandManager/SMTC, preserving the user's other media session. Normal media players keep their existing policy. Original public CLR method signatures remain forwarding overloads. The new nullable bridge fields omit unused values for existing consumers.

Cards retain grid dimensions and accessible names, use theme data colors, and expose Favorite through existing per-item context menus. Metadata edits use exact metadata revisions. The Play clip previews preference persists; selection suppresses media. Decoder continuity across metadata/query refresh is **deferred**: the existing query proof can retire even when content revision is unchanged. Keeping an old authority or inferring query membership from unchanged counts was rejected. This first version safely withdraws/reopens instead of weakening stale-result checks.

| Consumer | Applicability and qualification |
|---|---|
| Capture Library indexed gallery | Adopts saved clip declarations, one muted preview, existing context actions and persisted preference. Native visual/input checks pending. |
| Capture Library detail and Game Help capture review | Existing attachment/media sources preserved; sealed/root/pinned source tests still pass. No widget migration needed. |
| SDK Gallery native media examples | Existing package/web/local media behavior and protocol floor preserved; does not opt into preview playback. |
| PoB, Discord, Marketplace, Playnite rich previews | Existing preview types remain opt-out and unchanged. Shared Back/outside-click handling only delegates to a native media owner when one exists. Native cross-consumer regressions pending. |
| Templates and SDK examples | No native media hover implementation to migrate; existing constructors and original validation signatures retained. |

Current evidence: full Capture Library **101/101**, focused SDK media contract checks **2/2** and existing focus-preview contract **1/1**, real indexed admission/playback pool/worker-and-host-pipe checks **4/4**, existing sealed root/pinned media checks **3/3**, and indexed lifecycle/authority regression checks **19/19** passed. SDK compatibility passed **17/17** without changing the reviewed baseline; documentation checks passed across **252** Markdown files. Pipe coverage includes read permission revocation, original-file handle release, stale revision/proof rejection, rapid A-to-B replacement and a deliberately noncooperative late open. These are not native playback, performance or controller evidence. Native fixtures are prepared for actual decoder/mute/SMTC lifecycle and preview volume/fullscreen/outside-click routing; root owns their coordinated build and desktop run.

Native checkpoint: clean `host-preview-native-geometry` build (zero warnings/errors) passed native playback lifetime **12/12** (`artifacts/capture-preview-native/lifetime-final/result.json`), preview input **11/11** (`input-geometry/result.json`) and existing native media regression **82/82** (`baseline-final/result.json`). The native tests use synthetic media and real Windows decoding, mouse presses and Escape; controller buttons are normalized in-process calls, not hardware-controller evidence. They verify one decoder/lease, forced passive mute including old unmuted memory, SMTC suppression, rapid replacement, teardown, volume Back/Escape, fullscreen controls/return and outside-click activation exactly once.

Preserved failures and limits: the first lifetime fixture incorrectly expected an unmuted saved scalar after withdrawal, although withdrawal correctly forces mute first; its corrected test separately seeds an old unmuted scalar to qualify restoration. Initial input runs did not own foreground. Explicit Computer Use activation of the exact test HWND resolved admission; no Settings changes were made. A later test pointer exposed the fixture's fixed layout exceeding the default client at125% DPI. The media fixture now sizes its client in DIPs; the shared validation-only pointer helper refuses coordinates outside its XAML client before injecting a press. No production foreground or layout policy was changed for these setup failures.

The existing general interactive-preview regression remains **unqualified**: `interactive-qualified/result.json` and `interactive-repeat/result.json` each completed42 assertions, then timed out in the repeated hover leave/reopen loop. One had no current pointer source despite a hit trace; the repeat retained `preview.neighbor` when the fixture expected no preview. Both contain no media declaration, and their root cause is not established. Preserve and investigate this native hover evidence before claiming all shared preview consumers pass. Full live Capture Library gallery visuals and physical controller operation are still pending.

Hover follow-up source audit: the old trace's `hit=True` meant only `FrameworkElement.IsHitTestVisible`, not that native input reached that element. `HoverAsync` uses UIA screen coordinates once and restores the old pointer between calls; the general fixture also used the same oversized fixed root inside the default physical client. Validation-only preparation now sizes that client in DIPs and records actual native window ownership, pre/post-layout centers and bounded routed pointer ancestry in the failing loop. Assertions are unchanged. No production hover fix is justified until the next native run identifies the actual routing; root retains desktop/build ownership meanwhile.

Poster audit found a real omission: the gallery declared saved media without loading artwork and `MediaPlayerView` displayed opening text. The bounded implementation now adopts `MediaPlayerElement.WithPoster(WidgetArtworkHandle)` (two additive SDK symbols;4008 total), existing `ViewNode.ArtworkHandle`/Contain fit, the exact indexed/preview artwork resolver and `NativeArtworkDemand`. It assigns the existing Windows player's `PosterSource`; there is no new decoder, pixel cache, provider resolver or query. Normal media consumers remain opt-out. The shared artwork lifecycle clears changed poster identity, rejects late results and can reapply its existing decoded image on media resume. Existing SDK CLR signatures remain intact.

This narrows the original first-frame wording: [WinUI's documented poster lifetime](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.mediaplayerelement.postersource?view=windows-app-sdk-1.8) includes loading before `MediaOpened`, which is not a separate first-presented-frame guarantee. Native pixel/recording inspection remains required before claiming the handoff never exposes an empty frame. Native fixture additions hold media resolution until the scoped poster appears, then check one artwork resolution and one decoder. Non-UI poster contracts, gallery declaration and indexed late-result/revocation checks pass; native poster additions are not yet compiled/run.

Live gallery qualification/staging checklist: stage a coherent protocol116 frontend/bridge/session/worker and updated Capture Library package into the existing candidate only (no installer/release or installed-profile update). Keep its linked source grants and other available widgets. Use existing clips plus a screenshot; if additional media is needed, use synthetic files in an isolated linked test folder. Verify delayed muted hover/focus, fixed grid/scroll geometry, rapid clip-to-clip and clip-to-screenshot moves, R3/F6 entry, fullscreen return, real pointer controls, outside dismissal, and Selection/Play clip previews off suppressing media. Confirm decoder count returns to baseline on departure/hide, favorite metadata refresh safely restarts rather than retaining old query proof, the preference survives restart, and late/revoked sources show no wrong poster. Inspect cold/warm loading at100% and90% interface scale with actual monitor DPI and full/reduced motion; capture frame-level handoff evidence. Hardware-controller operation and true first-frame pixel behavior remain distinct from normalized-input/native-property fixture results.

Poster native checkpoint on coherent `host-scope-focus-diagnostics`: `artifacts/capture-preview-native/poster-input-first/result.json` passed **13/13**, `poster-lifetime/result.json` passed **12/12**, and `poster-baseline/result.json` passed **82/82**. The delayed media source displayed artwork through the ordinary scoped demand before opening; playback had one artwork resolution and one native decoder. This verifies native property/loading behavior, not frame-by-frame pixel continuity. The corrected general hover fixture still timed out after42 checks (`hover-geometry/result.json`): target and actual pointer agreed, native hit belonged to the fixture and it owned foreground, but the fixture-root routed-pointer trace was empty. Further validation-only diagnostics compare UIA coordinates against XAML-root+ClientToScreen and trace the entire XAML root. No production hover policy changed and the failure remains open.

Live gallery startup finding: candidate39164 stayed at Loading captures without rows for approximately two minutes; explicit More > Refresh library loaded396 items within five seconds. Current colored card composition was visible at90% interface scale. Live autoplay was not qualified because pointer/keyboard tooling did not enter the filter control; native More worked. Evidence is `artifacts/capture-preview-native/live-poster/result.json`.

Startup cause is now reproduced in a controlled production-widget/SDK-operations test. `WatchLibrary` can acknowledge late and supersede the initial `RefreshGalleryAsync` read. Its superseded waiter legitimately returns while the newer query runs; the former `RefreshAsync` ordering then loaded preferences, whose clip-preview `UpdateContent` replaced the gallery query object. The newer read's exact-query guard consequently rejected its result and left Page null, with no next event guaranteed. This is widget initialization ordering, not an index-readiness or permission diagnosis. `RefreshAsync` now loads preferences before scheduling its explicit query; subscription acknowledgement reconciliation and stale-query rejection are unchanged. The original test failed0/1 (`startup-before-fix.log`) and passed1/1 after the two-line ordering change (`startup-after-fix.log`). A second check passes both cancellation and temporary preference-read failure followed by exactly one successful reactivation retry, without an automatic retry loop. Full Capture Library regression is running; live restart qualification requires the next staged package.

Follow-up reproduced the same overly broad identity guard after startup: selection `UpdateContent` during a delayed library refresh caused its current data to be dropped (`presentation-before-fix.log`,0/1). The widget now compares the exact domain Filter object and requested library Revision, checks cancellation inside its publication gate, and merges data into the current query's presentation flags. New filters/order revisions still reject stale responses; this does not relax indexed lease or mutation authority. The delayed refresh test now passes both selection-mode and preview-preference changes, and all11 gallery checks pass including stale search and changed-original metadata cases. No style-triggered requery or SDK generation counter was added. The first full run passed100/101: the permission-denial fixture lacked the intrinsic private-state service now read first. Adding that real host prerequisite preserves its denied-query/scoped-settings/no-prompt assertions, which now pass. Final full-suite qualification is running.

Final startup/update non-UI qualification: Capture Library **102/102** passed (`artifacts/capture-preview-media/startup-final-suite.log`), and the fresh isolated Bridge test build passed both startup ordering and cancellation/failure-reactivation checks **2/2**. Documentation checks passed252 files. This qualifies the widget/domain fix in controlled scheduling; actual candidate restart remains pending staging0.2.0.9.

Read-side effect review found two additional widget-owned races predating the filter guard. Next uses the work lane and does not cancel a watch query; Back creates its return request before asynchronous lease release starts the replacement query. Controlled tests reproduced an old capture's changed-file warning on the new Next preview, and an old anchor remapping a newer Back request (**0/2**, `effects-before-fix.log`). `ReadGallerySnapshotAsync` now captures the SDK navigator snapshot together with selected capture identity. Metadata/notice effects require the same selected item and navigation revision; return remapping additionally requires the exact captured SDK return object. A valid library page can still publish independently. Both tests pass (**2/2**, `effects-after-fix.log`), including count4 publication while the fresh Back metadata read remains blocked, no old-item focus override, and the correct next-item return when that read completes. No widget-authored focus counter or framework change was added. Full regression is running before final staging.

Final effect-guard qualification: Capture Library **104/104** (`artifacts/capture-preview-media/effects-final-suite.log`), startup ordering/reactivation **2/2**, and documentation252 passed. Candidate restart/Back native verification remains separate from these controlled scheduling checks.

General hover failure diagnosed from `completed-keyboard-final-interactive/result.json`: in-process UIA supplied `(688,295)` but XAML-root transformation plus `ClientToScreen` gave `(784,391)`, exactly the missing96px native window origin. Routed events reached shell chrome at presenter-relative Y=-42.4, outside the fixture and any preview portal. The validation helper now uses current XAML-root ancestry and its actual AppWindow HWND for ordinary content, retaining the UIA fallback for a genuinely separate popup visual root. It rejects hidden/out-of-client targets, verifies the native cursor reaches the owned fixture, and the unchanged hover loop additionally asserts routed movement reaches the intended reading source. Production pointer/preview behavior is unchanged. Source-only correction awaits coordinated build/native rerun.

Live media qualification found a real host rendering omission despite the earlier13 property/control checks: the actual clip preview stayed blank, but entering it showed an advancing0:04/0:05 timeline and Pause control. `MediaPresentationStage.SetSlot` used the existing layer resolver, which recognized dialog ancestry but placed a portaled preview's resident video at ContentMedia100, below its opaque ContentPreview150 frame (and likewise below dialog preview350). The native player and poster were moved together into that covered plane. Passive activation is explicitly enabled and is not the leading cause. Earlier native control/decoder checks must not be cited as visible-pixel qualification.

The bounded host fix extends the existing semantic layer resolver: preview-owned media uses175 below ordinary dialogs, and dialog-preview media uses375 below fullscreen. `PresentationPortal.Layer` exposes its declared role; content100, dialog300, fullscreen500 and shell700 policies remain unchanged. Native and embedded media continue sharing the same stage; Game Help/Capture detail ordinary and dialog media retain prior bands. Existing preview consumers without media are unaffected. No new rendering surface, decoder or widget-specific branch was added. Validation now includes actual window-pixel captures (synthetic checker poster and known blue clip), a negative control moving the same active player back to the covered100 band, actual inline Expand pointer press/release, fullscreen return and optional dialog-preview repetition. Source changes are not yet built/run. The observed live Expand dismissal remains unassigned until the corrected-coordinate pointer test runs; production outside-click routing is unchanged.


### Live native Discord history qualification — adapter35

The staged development widget0.1.0.27 returned native history for two existing conversations through the direct authenticated HTTP facade; no message/send/upload action was invoked. Older paging in the second conversation enabled Newer as expected. Initial activation failed because the overlay hid before acquiring foreground; the user's configured Shift+F1 shortcut opened the exact candidate successfully. This was a test activation issue, not a provider failure. Initial subscription also exposed a false New messages indicator. Adapter36 corrects this: only watched message create/update/delete events advance the message revision; observation captures a watermark so an unconsumed event from another channel cannot become a new-message notice on entry. Channel metadata, read acknowledgments and history loading do not fabricate mutations. The optional native DTO watermark and comparison have targeted tests; live adapter36 qualification remains pending. Delivery/upload remain fixture-qualified only.

Cold message entry now adopts the existing `WidgetFocusRequest.RevealCollection` API. The explicit Open event allocates one request before loading and captures the SDK navigation revision, route lifetime and active lifetime. The same render that publishes the first nonempty loaded indexed window derives the exact latest occurrence target; rendering does not allocate another intent. The host owns row realization, stale-query rejection and scroll cancellation, and composer focus is unchanged. Back, account/session replacement or active-lifetime retirement suppresses the departed request. A ready same-conversation return preserves its bounded cursor window and indexed query; it does not reset/reveal again. This is widget policy using an existing supported framework API, with YouTube Music queue reveal as the precedent; no shared presentation behavior or other widget was changed.

Automatic follow after a confirmed send or incoming updates is deliberately deferred. The current SDK cannot causally cancel a reveal that is first published after a user has already scrolled during an asynchronous operation. Do not add widget-owned raw input counters or force an end jump over older history. The explicit Latest action and update indicator remain the path to fresh history. A cached return retains its current loaded window. Any later auto-follow proposal needs appropriate viewport/input authority, rather than treating the old cursor presentation metadata as supported WinUI behavior.

Managed checks after compact-layout and initial-reveal changes: Discord652 checks passed with zero build warnings/errors (`artifacts/discord-initial-reveal-run.log`). Tests verify exact item/source/query target, composer focus declaration, one request across renders, cached-return identity, Back/late completion, active retirement, revision watermark and false-positive suppression. Relevant provider/adapter/webpack/envelope checks217 passed; the exact adapter36 bundle is78,754 bytes and passes7 composed checks. Native initial-latest geometry and controller/mouse behavior remain with the coordinated fixture run; these managed checks do not establish native pixel behavior.


Live Capture-to-Discord handoff now succeeds from an already-open conversation: Capture Library Share activates Discord's recipient picker with Cancel sharing, selecting a recipient presents one attachment and an explicit Send button, and Back clears the unsent sharing draft. No real send/upload was attempted. The display name currently comes from the stored capture filename (opaque for this imported screenshot); naming polish remains separate from authority/custody correctness.

### Playnite nested-section qualification and current memory sample

The live 90%-scale ultrawide check reproduced a blank Home rail during return even though the host reported `hit:home`. In the same return window, native row diagnostics show34 existing row presenters retiring with `IsLoaded=false`; the nested Home section is reparented by the transition/cache path. The settled view repopulates correctly. Evidence: `artifacts/navigation-review/final-playnite/home-return-analysis.json`, `rows2.jsonl` and `interaction.jsonl`. This is a reproduced host lifecycle problem; the top-level Capture Library stable-root fix does not cover it. Do not claim Playnite retained pixels are qualified. Investigate current row teardown ownership before adding a second row/pixel cache or changing widget declarations.

Current settled Home with one cached Library destination measured462.00MiB frontend private commit,55.72MiB managed live bytes and130.21MiB GC committed before forced collection. Equivalent two-pass GC/finalizer settling produced440.94MiB private,36.45MiB managed and122.09MiB GC committed; no navigation cache was evicted. This run freed21.06MiB private commit, but roughly318.85MiB remains outside GC commit. These are whole-process measurements, not an allocation-owner breakdown or proof of a leak. Earlier cold Library samples changed while artwork was still loading and are not a valid paired GC comparison. Evidence: `memory2.jsonl` in that directory.

Correction to the Playnite attribution above: matching the settled descriptors before departure and on return shows Home QueryGeneration2->4 and favorites3->5, with new native source identities despite equal counts. Therefore existing-row teardown is not by itself proof of an erroneous Unloaded policy: a genuinely new query requires retirement. The untested validation-only Unloaded-retention experiment was removed before qualification. Investigate why Home's domain refresh publishes a new generation; do not relax query/lease identity or introduce a second row cache to hide this. Native page cache hit still does not establish retained collection pixels.

### Playnite display-cache ordering correction

A behavior fixture reproduces an independent widget-domain bug: Browse's alphabetical `ProjectDisplay` update can reorder Home's unavailable referenced tail through `PlayniteLibraryHomeQuery`'s use of the shared display-cache order. With unchanged provider membership and referenced IDs, `SameMembership` then returns false. The corrected fixture fails against the original implementation and passes after the fix. Live Home now uses the existing `ReferencedSavedIds` domain order for that unavailable tail; provider/manual/title-match order and warm snapshot order remain unchanged. Owner: widget domain ordering, not host navigation authority. Full Playnite suite248/248 passes (`artifacts/playnite-fallback-suite.binlog`). A fresh development package and native query-generation/pixel check remain pending.

The proposed inactivity-only Unloaded retention is rejected for production and its experimental hook has been removed. Existing container bookkeeping registers renderers only after layout and does not own every detached renderer through recycle/disposal; suppressing disposal would risk retaining static controller-prompt subscriptions. A broader row-ownership refactor is deferred unless stable-query native evidence establishes a remaining need. No second row/pixel cache or weakened generation check is introduced.

### Native Messages and scope-entry focus qualification

The synthetic fixture uses the production Discord widget with a bounded fake provider and an isolated catalog/profile; no real Discord messages, files or credentials are used. `artifacts/discord-messages-native/run8/{minimum,expanded}/result.json` passes30 and31 checks respectively on the final host. Coverage includes420x360 and1040x680 client bounds,164/421.6 DIP history viewport, lazy20-message history, bounded40-message Older/Newer windows, cold latest-item reveal without composer focus loss, full long-message A/B and F6/Escape previews, pointer transit/click, native keyboard draft editing, and Back restoring the invoking inbox row. All three dashboard actions are published in both sizes and displayed in Expanded; Minimum continues using the existing measured-width guide fitting policy. No existing in-widget mute shortcut or real voice effect was invoked. Provider mutation attempts remain zero. Incoming file handoff is covered by the separate production candidate Share/Cancel check and sender/receiver tests, not fabricated in this native fixture.

This uncovered a shared host race: an already completed floating keyboard can issue native Programmatic/Keyboard/None recovery after its owner has changed scopes. Accepting the first control as a fresh focus choice overwrites remembered scope focus. `WidgetViewPresenter.EntryOwnership.cs` now retains native entry provenance independently of an optional target, yields to actual outside focus choices, and handles only the same completed dialog belonging to that exact presenter/widget/session/worker. The dialog keeps a weak origin stamp through native close cleanup. Native `TrySetNewFocusedElement` decides readiness; prechecking `IsLoaded` was disproved because both intended and fallback controls can still report false during native entry. If native redirect refuses, this exact recovery is cancelled and the existing Loaded/LayoutUpdated entry wait is used. No sleep, new polling, widget return flag or duplicate focus cache was added.

Before/after evidence is preserved: `scope-focus-before` reproduces outside-focus theft; `scope-recovery-proof` fails49 checks when an unloaded intended target is skipped; `scope-recovery-ready` passes55 and explicitly records the recovery redirect before memory changes. Tests also cover fully targetless loading followed by an outside choice, loading/ready rebasing, directional cancellation, retired targets and worker replacement. Earlier Messages failures remain under run3/run5; run8 is the qualified final result. Native regressions on the same host pass indexed entry47, returns51 and page cache43 (`artifacts/navigation-review/completed-keyboard-final-*`), plus media preview input13 (`artifacts/capture-preview-native/completed-keyboard-final-input`). This establishes bounded native behavior, not a claim that every application navigation or memory workload is fully qualified.

| Consumer | Applicability and validation |
|---|---|
| Discord native Messages | Affected shared floating-keyboard/scope return; production widget adopted unchanged; native Minimum/Expanded and exact recovery proof passed. |
| Capture Library and Settings page navigation | Shared scope/retained-entry behavior; native page cache43 and return51 pass, including pointer/wheel departures and resized return geometry. |
| Playnite and PoB indexed groups | Existing group-entry mechanism retained; native indexed entry47 and returns51 pass. Live Playnite retained pixels remain separately qualified by its domain-ordering work. |
| Game Help, Marketplace, Network Controls, Display Profiles, YouTube, Spotify, standalone YT Music, SDK Gallery | Repository text-entry/navigation entry-point search confirms these use the existing native presenter path; no SDK/protocol API or widget migration is required. Generic native scope tests cover delayed/empty content and owner changes; individual application flows were not all replayed. |
| Other first-party widgets and templates | Existing input-scope/group navigation continues through the same presenter. No matching alternate native keyboard lifecycle was found in author code and no duplicate return mechanism is introduced. Templates require no new declaration. |
| Browser text entry and user web-app editor | Different keyboard origin; intentionally excluded from widget-presenter recovery by exact weak owner stamp. Existing browser handling remains unchanged. |

Playnite Home publication qualification correction (2026-11-16): the first fallback-order fix covers a real independent display-cache ordering bug, but did not explain the live blank return. The opt-in Debug publication probe proved Browse `SaveStateAsync` changed Home from 33 provider rows/no fallback to 33 provider rows plus one unavailable fallback; the Home refresh removed that fallback again. Favorites changed 1→2→1 and each transient membership change correctly renewed the SDK query. Authority reprojection itself was unchanged. Evidence: `artifacts/playnite-publication-probe/live/publications.jsonl`, process40916 records5–10. Do not weaken host/query identity or retain retired rows to hide this domain error.

The corrected Home projection excludes catalog identities omitted by its provider query from the unavailable-reference tail. `PlayniteLibraryAuthorityProjection.ContainsGame` uses the existing complete-catalog identity map, including null completion-status values, without an additional catalog copy. Genuine missing/local references and warm display snapshots remain supported. Favorite semantics are unchanged: since their introduction in dce9d3ce, Home favorites are up to three favorite occurrences from Home membership; Home selects installed provider games, while View all opens the separate Library favorites query. The transient extra favorite was disabled (`Current=null`), not a valid favorite action. This correction neither clears provider favorites nor turns Home into an all-catalog favorite query.

Validation: a focused fixture failed before the coverage fix when Browse cached a known uninstalled favorite, then passed with stable membership across Browse/Home display-cache coverage changes. It also checks genuine provider additions, favorite authority preservation, and null completion status. Full Playnite widget suite:249 passed/0 failed; captured-query/application authority subset:12 passed/0 failed. Existing tests cover genuine unavailable references and warm snapshot ordering. Debug publish prepared at `artifacts/playnite-publication-probe/fixed-application-publish`; native generation/paint qualification is still pending. Temporary bounded publication diagnostics remain opt-in Debug only until that evidence is collected, then must be removed.

### Discord upload progress and cancellation completion

Completion audit found that the promised upload progress/cancellation UI was missing despite the underlying cancellable stream. The widget now adopts the existing SDK single-flight operation cancellation/currentness rather than adding another lifetime or cancellation registry. Its current status row reports coalesced integer-percent byte progress; the same composer row replaces Attach files with Cancel upload, then Stop waiting after message submission. The existing credential-free HTTP body writes bounded64KiB chunks and reports completed writes, without copying the full file to RAM or temporary storage.

Before provider message submission, cancellation stops the upload and preserves draft/files for an explicit retry. A late uploader that ignores cancellation is fenced before POST. After submission, Stop waiting cannot promise message recall: the nonce remains available for read-only Check delivery; a late confirmed acknowledgment is reconciled and cannot erase newer draft text. Account replacement cancels obsolete work, and stale Cancel actions cannot target a new upload nonce. No host/SDK/protocol contract changed. Other capture/media/browser uploads retain their existing owners.

The full Discord suite passes670 checks on a clean isolated build (`artifacts/discord-upload-cancel-build2.binlog`, `artifacts/discord-upload-cancel-run2.log`), including bounded transport writes/progress, cooperative and uncooperative upload cancellation, stale actions, account replacement, submitted ambiguity and late confirmation. The first new test expected50% for a3-byte fixture whose first read transfers1 byte; its timeout is retained in run.log and the expected ratio was corrected to33%. No production behavior was weakened. Native busy-row bounds/controller cancellation will use the synthetic provider and a real broker-selected isolated file; no network upload or message POST is authorized.

Candidate-only Discord0.1.0.29 is built and validated/packed at `artifacts/discord-capture-candidate/discord-final29/discord-0.1.0.29.wrwidget`, with unchanged composed adapter36 (78,754 bytes). Catalog update and final native launch remain root-owned; no application installer/release was created.

### Preview media visible-pixel and input qualification

The final `host-preview-layer-final-fixture` build passes **22 checks each** for ordinary and dialog-hosted clip previews (`artifacts/capture-preview-native/layer-content-qualified` and `layer-dialog-qualified`). These capture actual owned-window pixels, including the native swapchain: the pending checker poster is visible, the playing blue clip occupies the viewport, the deliberately covered negative control is absent, and native pointer press/release on Expand opens fullscreen. Fullscreen video and the returned inline video are visible; their owner bands are175/375. This is stronger evidence than the earlier decoder/control-only13 checks. The old-band negative evidence is preserved in `layer-pixels-content`: moving the same playing plane below the preview produced0 matching pixels out of2914 sampled.

The first Expand diagnostic proved an actual native Click and fullscreen pixels but exposed a collapse readiness gap: `SetSlot` can run before the restored content visual ancestry settles. The existing media stage's placement pass now reconciles its semantic layer after valid loaded geometry. It does not create another rendering surface, timer, decoder or click router. No production outside-press policy change was needed for the qualified native Expand path. Regressions on this host passed decoder/lease lifetime12 and navigation return51. Ordinary image/video controls passed82 on an unchanged repeat; the first baseline attempt timed out at the existing mid-animation image zoom restoration check20, preserved in `layer-final-baseline`, so this intermittent animation case remains a qualification limitation rather than an erased failure. No physical-controller or guaranteed first-decoded-frame continuity claim is made.

The corrected hover test now proves actual routed motion through the reading source and opens its preview. It still fails after42 checks when moving onto a sibling toolbar outside the presenter (`hover-corrected-qualified/result.json`, line274). Whole-XAML-root diagnostics show the real sibling Button ancestry, not a preview portal; the preview retains its old pointer source. Therefore the old coordinate error was only one fixture problem, not a complete explanation of hover closing. The remaining source/root event-boundary behavior is under investigation; assertions and production hover policy have not been weakened to make the suite pass.

Bounded corrective adoption: `WidgetViewPresenter.PreviewPointer.cs` now observes outside routed movement through the same temporary XAML-root lease as outside presses. Presenter/chrome/local-binding paths continue using their existing handlers; outside passive movement supplies its actual point to the existing corridor and200ms exit timer. Touch, suppressed hover, entered previews and expanded/moving media remain excluded. Both observers detach when the preview hides. This is host-owned input lifecycle, with no widget change, duplicated click routing, new timer or protocol API. The native loop retains all prior assertions and adds a settled outside-close check; qualification is pending the combined build.

| Hover consumer | Applicability |
|---|---|
| Capture Library clip and filter previews | Affected by outside sibling/shell movement; adopts shared fix automatically. Clip pixel/input fixture covers media ownership separately. |
| Discord participant and message previews | Same presenter input mechanism; participant sliders and message previews retain explicit entry and outside-click behavior. Generic interactive fixture exercises slider capture, nested selector, keyboard/controller entry and sibling movement; synthetic Messages regression is coordinated separately. |
| PoB inspection, gem/skill/history previews | Same shared presenter for inline/deferred preview declarations; no author input plumbing added. Generic transit and lease tests apply; individual PoB screens are not all replayed. |
| Playnite Library, Marketplace, SDK Gallery | Existing reading/focus presentation consumers use shared passive movement; no declarations or domain behavior changed. |
| Other first-party widgets and templates | Repository search found no other focus-preview entry points requiring migration; ordinary controls/media and non-preview consumers retain existing behavior. |

Final native qualification of the root-movement adoption: clean `host-preview-root-movement` build (zero warnings/errors), interactive preview **61/61** (`root-movement-qualified/result.json`), media pixel/input **22/22 ordinary and22/22 dialog** (`root-movement-media-content`, `root-movement-media-dialog`), and decoder/lease lifetime **12/12** (`root-movement-lifetime`). The interactive check includes four real hover leave/return cycles, settled sibling dismissal, diagonal pointer travel, interrupted slider capture, keyboard/controller/UIA entry and unchanged outside-click action delivery. Four presenter exit events were recorded, so the diagnosis is missing current outside-movement updates across sibling ownership, not an assertion that native exit events never occur. Prior failed evidence remains intact. All fixture processes closed normally. The current host also includes the corrected validation helper's separate-popup fallback and the root agent's previous/next diagnostics. No real captures, upload, messages or settings changes were used for this qualification.

Actual installed-candidate Capture Library0.2.0.11 qualification now passes **18/18** on clean `host-live-gallery-select-ready` (`artifacts/capture-preview-native/actual-gallery-select-ready/result.json`). The opt-in validation-only shell harness uses the real widget, existing396-capture index and normal broker permissions; native Filters/Type selects Clips, the real indexed row opens its saved-capture preview, and no fake resolver or authority bypass is installed. The actual five-second clip is visibly playing, muted, with SMTC disabled and exactly one decoder. Owned-window pixel samples show115 quantized colors; covering its existing stage changes3277/4080 central samples to a uniform frame, and restoring it changes3326/4080 samples with113 colors. The preview uses band175. Moving to search closes it, retires the decoder, clears the local playback lease and awaits the existing release task. The active filter chip restores All captures. Screenshots were visually inspected; no recording, media/metadata edit, upload or message send was performed.

First live harness run (`actual-gallery-first`) stopped before filtering because it invoked the native selector before its loaded/nonzero layout readiness; the existing inline-select fixture already waits for that readiness. Adopting that same wait fixed the harness without a production change. Both failed and passing results are retained. The first combined build also preserved validation-only compile failures (Window/MainWindow static type and a missing input namespace); the corrected build has zero warnings/errors. Earlier intermittent image zoom reversal remains unresolved pending its separate diagnostic run; an unchanged82-check repeat is not evidence that the original timeout was merely test timing.

Live screenshot review additionally confirmed the clip preview's action-button bottoms were clipped by its fixed420-DIP budget. Capture12 adopts the already-supported content-sized preview measurement with a480-DIP maximum; no host layout behavior changed. `actual-gallery-sized/result.json` passes **21/21** on the actual candidate, including both authored buttons entirely inside the native fragment viewport and independently above sibling host footer chrome. At the user's90% interface scale, button bottoms825.840 and content bottom825.840 precede footer top833.040 by7.2 transformed DIPs. The playing screenshot confirms complete button borders. Existing real clip pixel, mute, one-decoder, retirement and All-filter restoration checks also pass. All smoke processes closed normally; the separate image-reversal investigation retains its outstanding evidence and ownership.

Playnite coverage fix follow-up: Debug candidate0.4.6.6 live qualification kept Home query generation2 and favorites generation3 across navigation, proving the transient membership correction. A fresh capture still legitimately increments ContentRevision; the host's existing indexed source accepts that change and retains slots/pixels, so no freshness/authority suppression was added. The three-tab Home→Library→Achievements→Home sequence is not a valid cache-hit qualification because Achievements does not declare the matching destination transition; direct Home↔Library native cache/paint evidence remains separate work.

Temporary Playnite publication probe source and all its hooks were removed after collecting that evidence. Release-configuration candidate package0.4.6.7 (not a product release/installer) was compiled, validated and archived at `artifacts/playnite-publication-probe/playnite-final-0.4.6.7.wrwidget`; SHA256 `3279EBAD7111C2956B4CDA8ED9D590089310B6D8F457701749498EFC87FBD3AD`. Existing integration bundle is unchanged and all six payload hashes pass. Debug0.4.6.6 archive remains available for comparison. No candidate-catalog mutation or desktop action was performed by this packaging step.

Native busy-upload qualification was separated from OS dialog automation after run9 timed out at26checks and run10 was stopped when the Computer Use tool could not target the owned file dialog's filename field. These are preserved harness/automation failures, not evidence of an upload failure. The fixture now uses the same pre-creation `WidgetWorkerServer` + `WidgetTestHostServicesBuilder` seam as CaptureLibraryWidgetFixture, with its own friend-enabled test runtime. Production one-time host-service attachment remains unchanged. File selection/read/release and the provider upload are deterministic; real widget controls, operation cancellation, input routing and WinUI bounds remain under test. Successful OS chooser selection remains unverified; the separate native picker7 covers cancellation/lifetime only. No candidate runtime update or production host hook was added for this fixture.

Final native busy-composer qualification: `artifacts/discord-messages-native/run11/minimum/result.json` passes38/38 and `expanded/result.json` passes39/39 on the clean combined `host-discord-upload-service-final` build (zero warnings/errors). These runs use the real production widget and native host with deterministic SDK file services; no OS chooser or Computer Use interaction is involved. Both verify50% streamed progress, actual Cancel/draft/Send bounds, controller cancellation, retained unsent text/file and subsequent inbox focus return. Each provider audit records one synthetic upload preparation, one half-stream upload, one cancellation and zero provider message mutations; no network upload occurred. The same run requalifies Messages paging/previews/composition and dashboard publication at both sizes. All fixture windows closed; root owns the final normal candidate launch.

Direct Playnite Home↔Library native qualification corrected the earlier host suspicion: root observed `hit:home`, identical Home/favorites source identities1170455/21353801 across return, and zero retirements among the34 Home row instances captured before departure (102 applications reused those existing renderers). Legitimate ContentRevision refresh continued. Therefore no detached-row ownership refactor, Unloaded suppression, or additional row cache is justified; the existing host behavior is retained. The earlier three-tab route through Achievements was not a cache-hit test.

Interrupted-image reversal qualification remains a specific unresolved intermittent observation. The original `layer-final-baseline/result.json` timed out after20 completed checks at the assertion restoring absolute image scale after a60ms expansion reversal; the assertion waited10seconds after the close task completed. The unchanged immediate repeat passed82 checks. Do not call this proven fixture flakiness: a late native viewport change or closing-time checkpoint overwrite can be a product race, and the original artifact has insufficient geometry evidence to decide.

Added validation-only bounded viewport/ChangeView diagnostics without changing rendering behavior or assertions. Three unchanged native baseline attempts (`artifacts/capture-preview-native/reversal-diagnostics-1` through `-3`) each passed82 checks; DLL SHA256 `F6E0DE9041C0E71EB992285D30AE95BB16650F8489F445CCE7CEEB26FD44636F`. This is nonreproduction, not a fix or proof of correctness for the intermittent case. All fixture processes were closed; no actual media, messages, or provider settings were changed. Further work should use the retained diagnostics on a recurrence rather than weaken the assertion or add speculative restoration retries.

Live Capture batch-share smoke: the first run selected two real available captures and invoked the declared file intent, but the fixture waited on dialog construction before `Opened` and could skip handler-choice inspection; its timeout is retained in `artifacts/capture-preview-native/batch-share-first/result.json`. The corrected readiness check records an honest unavailable-receiver result in `batch-share-chooser-ready/result.json`: Capture selection count2, active host intent, opened dialog with zero handler choices, no Discord recipient picker. Both runs returned to Capture Library and ended only the smoke's temporary selection with no cleanup error; neither selected a recipient or invoked Send. This does not qualify successful two-file delivery. Cold receiver startup is a possible explanation, not established by these facts; no production availability guard or existing draft was bypassed.

Interrupted-image reversal is now reproduced and fixed with native evidence. The bounded phase sweep preserved the original assertion and reproduced the failure in `artifacts/capture-preview-native/reversal-phase-sweep-2/result.json`, second cycle at60ms. Before reversal the inline scale was1.764000/zoom1.2. The correct original checkpoint survived closing; return requested zoom1.2 and native ChangeView returnedtrue while the ScrollViewer was unloaded. Ten seconds later it was loaded but still at fullscreen zoom1.1217806, yielding inline scale1.6490175. This identifies a lost native ChangeView during reparenting, not a bad fixture baseline or overwritten checkpoint.

The shared image viewer now retains one source-lifetime/placement-owned checkpoint until its ScrollViewer is Loaded with valid layout. It resumes through existing Loaded/LayoutUpdated events, with no timer or polling; fresh pointer/key input, zoom/pan/Fit, source replacement, presentation retirement, error and disposal revoke pending restoration. A new placement carries the intended checkpoint rather than sampling an uncommitted native zoom. Existing fullscreen ownership guards remain intact.

Consumer matrix: Capture Library screenshot detail and Game Help screenshot preview are affected through the same MediaPlayerView; no widget/API changes. SDK Gallery/general image sources use the same fix when images are shown. Video paths share fullscreen control but have no image checkpoint; ordinary video/playback/fullscreen assertions and the media-preview return lane passed. Embedded browser/media surfaces use other controls and are not applicable. This change did not run a separate Game Help product workflow; the native shared-control fixture covers the changed image behavior.

Validation: clean frontend builds, two99-check native phase-sweep/cancellation baselines, ordinary media-preview input/fullscreen-return16checks, then final101-check baseline including a deterministic positive unloaded→Loaded restoration case. Real detach/reload cases also prove fresh Zoom, Fit and retirement prevent late checkpoint replay. Final artifacts: `reversal-readiness-fixed-1`, `-2`, `reversal-readiness-media-input`, and `reversal-readiness-deterministic-final` under `artifacts/capture-preview-native`. The earlier failure remains preserved. No assertions were weakened and no speculative compositor or row-cache changes were introduced.

### Cold Discord file-share admission: corrected assessment and final proof

The real cold batch smoke exposed two separate readiness assumptions. First, dynamic discovery started Discord in Background and immediately queried availability; its initial backend read failed the intended broker lifecycle guard (`lifecycle_denied`), leaving Error/no account/session. The bounded Debug31 probe records this at `artifacts/discord-capture-candidate/discord-cold31-probe2/discord-intent-probe.jsonl`. Awaiting remote startup inside the2second intent RPC or weakening the broker was rejected. Discord's Files handler adopts the existing static manifest declaration; the separately approved host Files-discovery policy now lists catalog handlers without starting potential receivers and preserves chosen-receiver validation. Other intent semantics remain unchanged.

Second, accepting a pending local share and awaiting only the first read was insufficient: Debug33 proves the first successful snapshot was Ready=false/Messaging.disconnected, followed2.13seconds later by Ready=true/CanSend=true in the same backend session. The provider defines Ready as current user plus webpack readiness, so initial hydration and a fresh signed-out page cannot be distinguished. The final implementation treats this ambiguity consistently both before and after receipt until an account has been verified. An accepted share waits on the existing SDK resource-publication/Invalidated signal, using the navigator route and SDK Active lifetime; it adds no polling timer. The existing Open Discord action, Cancel sharing and Back remain available. First observed session and verified account are pinned before recipient adoption. Later account loss/session replacement, known Ready=true/CanSend=false, draft replacement and errors remain guarded. Fresh signed-out can stay locally pending until sign-in or cancellation; that limitation is explicit, not a fabricated authentication claim.

Full Discord suite707 checks pass (`artifacts/discord-hydration-complete2.binlog`, `discord-hydration-complete2.log`). Controlled cases cover an initial read delayed beyond the intent RPC deadline, callback-token cancellation after accepted custody, first disconnected before/after receipt, readiness completing during Accept, Cancel/Back/background, error/denial, initially absent then replaced session, verified-account replacement/loss, existing drafts and worker destruction. The first new cancellation test also caught duplicate release ownership during cancellation; atomic receipt retirement now selects exactly one cleanup owner. Earlier failed runs remain preserved. Generic accepted-custody/source-revocation semantics remain covered by the existing broker file-transfer tests; no source-folder access is recreated in the widget.

Clean development Discord0.1.0.34 (adapter36, no probe source/marker) is selected and enabled only in the candidate catalog. Archive `artifacts/discord-capture-candidate/discord-cold34/discord-0.1.0.34.wrwidget`, SHA256 `af7b9c7dfff7b9a3ec8fc7700618affd9d1389c283a5565bfa9b3b9544e5412f`. On the updated candidate Bridge, the actual cold batch smoke passes9/9 at `artifacts/capture-preview-native/batch-share-cold34-final/result.json`: two real available captures selected through current indexed leases, actual generic Share intent delivered into Discord's two-file recipient picker, Cancel sharing without selecting a contact or invoking Send, then return to Capture Library with temporary selection ended. Cleanup reports no error. No recording, upload, message, installer or release was created. All smoke processes closed; final normal candidate launch remains root-owned.
