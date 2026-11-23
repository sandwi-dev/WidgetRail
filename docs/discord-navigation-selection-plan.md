# Discord navigation, read state, and controller selection

Started: November 17, 2026.
Status: implemented and independently qualified; matched shipping candidate launched for user acceptance on November 18. Live acceptance items are listed in the final completion audit.
Baseline: accepted local main `c26a6ea8`. Earlier plans and unfinished chat steps were completed and merged; they are historical evidence, not a backlog to resume.

## Scope and estimated complexity

| Work | Complexity | Primary owner | Main uncertainty |
|---|---|---|---|
| Hold L2/LT and move to select items | Moderate, 3/5 | Host input + SDK selection + protocol; small consumer adoption | Starting selection atomically and preserving visited items through asynchronous navigation |
| Integrated Discord Call / Messages interface | Moderate, 3/5 | Discord widget using existing navigation/layout controls | Responsive two-pane navigation and preserving current call/draft/focus behavior |
| Mark opened conversations read | Moderate to high, 4/5 | Discord provider + widget; shared visibility support only if proven missing | Discovering and verifying the current client's acknowledgment action and its completion semantics |
| Connecting briefly becomes Sign in | Small to moderate, 2/5 | Discord provider state and widget presentation | Reliable distinction between startup, signed out, and unavailable client APIs |
| Faint full-width outline behind focused message | Likely small, 2/5; shared regression checks required | Theme/widget style or host visual targeting, according to reproduction | Separate container shadow/shading from WinUI default item paint |
| Transient status messages as toasts | Small to moderate, 2/5 | Discord adoption of existing SDK notification service | Separating action outcomes from persistent recovery/progress state |

These are relative engineering estimates, including focused validation, not elapsed-time promises. Findings below are source-confirmed or user-reported; no new runtime reproduction or performance measurement was performed in this planning pass.

## 1. L2/LT held selection

User confirmed: hold L2 while moving through items to select them; release keeps the selection.

Proposed precise behavior:

- On an eligible, selectable collection, pressing L2/LT begins selection if needed and selects the focused item. No long-press delay; the trigger acts as a modifier while held.
- D-pad/left-stick navigation adds each item actually reached by focus. Previously selected items remain selected. Revisiting an item does not deselect it.
- This is visited-item selection, not a contiguous index range: moving vertically in a grid must not select every intervening item in row-major order. Passive right-stick scrolling does not select offscreen items.
- Release ends the gesture but leaves selection mode and selected items intact. A continues to toggle individual items; existing Done/Clear/batch controls remain available. Preserve the consumer's existing Back behavior.
- A second hold adds more items. Keep the existing 256-item bound and its visible limit feedback; reaching the limit must not discard the prior selection.
- Guide text uses the current controller's L2/LT glyph and explains hold-to-select only in eligible collections. Mouse/Ctrl/Shift/Space behavior stays intact.
- Releasing during pending navigation prevents later completion from adding an item. Losing foreground, changing query/account/page/widget, opening a modal/preview, entering a pin, or disconnecting the controller ends the gesture. Require a fresh press after ownership changes.
- Existing child control ownership wins: media seek/zoom, browser zoom, keyboard Shift and sliders must not also execute selection. Before selection starts, explicit widget shortcuts retain precedence. User refinement: while selection is active and focus is in that collection, L2 takes precedence over authored row/ancestor shortcuts and the guide shows only the selection hint. Claim the gesture once for the exact collection; do not arm it merely because a trigger was already held when focus entered.

Current mechanisms and genuine gaps:

- `src/WidgetProtocol/IndexedSelection.cs`: bounded selection and Toggle/Replace/Extend/ExtendAdd/SelectAll/Clear already exist.
- `src/WidgetSdk/WidgetIndexedCollection.Selection.cs`: query/epoch validation, bounded resolution and selection lifetime already exist. `ApplySelectionAsync` currently requires selection to be active. There is no idempotent add-only gesture or host-driven start-and-select operation.
- `src/OverlayFrontend.WinUI/Presentation/WidgetIndexedCollectionView.Selection.cs`: mouse/keyboard selection and delayed-focus completion already exist. ExtendAdd selects a range, so it is not equivalent to the requested gesture.
- `src/OverlayFrontend.WinUI/Presentation/WidgetViewPresenter.HeldActions.cs` and `Shell/OverlayShellPage.HeldActions.cs`: trigger repeats are bound to an action/row identity. Repeatedly firing Toggle would undo selection; relaxing row identity globally would weaken other held actions. Integrate a collection-scoped modifier into existing input ownership instead.
- `src/FirstPartyWidgets/CaptureLibraryWidget/CaptureLibraryWidget.Gallery.cs:588`: entering selection currently also updates `galleryQuery.Selecting`, which controls previews and actions. A shared input addition cannot bypass this synchronization. Prefer deriving presentation policy from the existing selection state or a supported SDK change notification; avoid a second manual gesture state in the widget.

Implementation design to validate: extend the existing selection command with atomic activation/add semantics and an add-only operation. Preserve query identity, selection epochs, worker authority and stale-action rejection. Coalesce/bound pending selections without losing legitimately reached items. Older installed workers must never receive an unsupported gesture: negotiate/version the addition and retain their current selection controls. No custom per-widget trigger handler.

Precedents: WinUI extended selection separates navigation from selection state; modifier-assisted selection is familiar in desktop file managers. The requested additive visited-item behavior differs from Shift range selection and must be tested as its own policy.

## 2. Make Discord messaging a primary destination

Current evidence: `DiscordWebViewWidget.Views.cs:37` adds Messages as a secondary header button. The common header is omitted on message routes at line101; `DiscordWebViewWidget.Messages.cs:435` replaces the body with either recipient list or conversation. The current route structure makes messages feel like a separate nested feature.

Recommended direction:

- Persistent top-level **Call / Messages** destinations, using the existing navigation-shell/section controls and theme tokens. Messages has an unread indicator derived from Discord's read state. Do not invent an exact unread-message count from a boolean.
- Call preserves the existing behavior: server/voice-channel picker while disconnected; call participants, streams and friend activity while connected.
- Messages uses a conversation list beside the selected chat when width permits. Search and unread conversations remain easy to reach. Compact layouts show the list or conversation with a conventional upper-left Back control.
- Retain compact active-call controls while viewing messages: current channel, mute, deafen and return-to-call. Share/watch remain available from Call; changing destination never rejoins a call, restarts a stream or rebuilds the provider session.
- Refresh, Open Discord, voice settings and setup commands use consistent secondary placement. Avoid recreating the complete call panel or two competing header rows inside chats.
- Keep per-account/per-conversation drafts, upload state, the bounded timeline, previews, initial unread reveal, latest-following and visible-edge focus entry. Reuse existing resources rather than adding a parallel conversation store.
- Every new incoming file share still begins at recipient selection. It must not automatically choose the currently open conversation. Existing drafts remain independent, and Send stays explicit.
- Notifications open their exact account/conversation/message through the existing route. Switching Call/Messages is section navigation; opening a conversation is detail navigation. Avoid accumulating repeated top-level tabs in Back history.
- Proposed controller mapping: LB/RB switches top-level destinations only when the current child interaction does not own those buttons. B closes the current child/detail before leaving the widget; X composes in Messages and mutes in Call; Y retains context-sensitive latest/share behavior; L3 reaches list/header controls. Audit existing dashboard, pin, player, preview and keyboard bindings before finalizing.
- First prepare a wide/compact visualization with active call, unread list, conversation and disconnected states. It should establish proportions and navigation before production layout changes.

Owner: widget data/layout and adoption of existing `src/WidgetSdk/NavigationShell.cs`, navigator, responsive layout and collection APIs. A new general navigation system is not justified. Inspect whether conversation collection demand is currently route-bound before making it permanently visible; both panes must share one account-scoped data source.

## 3. Read acknowledgments

Meaning: update the user's own Discord read/unread position. This does not add recipient-facing delivery/read receipts.

Existing support:

- `web/provider/messaging-provider.js:75` observes `ReadStateStore`; line214 projects unread state and the authoritative oldest unread ID.
- `Domain/DiscordMessages.cs:48` already represents `Unread` and `OldestUnreadMessageId`.
- `DiscordWebViewWidget.Messages.cs:280` uses that boundary when opening a conversation.
- `web/provider/adapter.js:16` exposes messaging commands, but no mark-read operation. Current observation, history reads and notification mapping deliberately do not acknowledge messages.
- `DiscordWebViewWidget.MessageViewport.cs:20` and `src/WidgetSdk/WidgetLoadedCollection.Viewport.cs:6` supply active viewport, recovery and end-of-list feedback. Being at the loaded end is not proof of being at the newest Discord message; `history.HasAfter` and current provider identity matter.

Proposed behavior:

- An explicit normal conversation opening marks it read after the correct account/conversation has successfully loaded and its history is visibly presented. Capture the current last-message watermark for that opening; do not acknowledge messages that arrive later merely because loading is pending.
- Preserve the opening unread divider/reading position for that visit, even when acknowledgment updates the inbox. Do not jump to the bottom just to mark read.
- While the chat remains visible at the actual newest end, coalesce acknowledgments for newly displayed messages. While reading older history, incoming messages retain their unread state and the existing Latest affordance.
- A notification/reply jump into historical content must not silently mark newer, unseen messages read. Treat that entry separately from ordinary opening, and advance only with appropriate presentation evidence or explicit Latest navigation.
- No acknowledgment from inbox loading, hover previews, call/stream pins, cached hidden pages, background polling, failed history loads, or an overlay that has already closed. On reactivation, reconcile the current presentation before any acknowledgment.

Provider work:

1. Inspect the current Discord web client's read acknowledgment action and feature-probe its actual arguments, batching and store updates. Prefer that action over an invented endpoint or synthetic store mutation. The exact action has not been established in this audit.
2. Add a typed adapter command constrained to account, accessible private conversation and target message ID. Validate the target belongs to that conversation; compare snowflakes without floating-point conversion. Keep scope to the existing DM/group messaging feature.
3. Coalesce to the newest eligible watermark, bound outstanding work, respect provider rate limits and never regress read state. Observe authoritative read-store changes; invocation alone is not confirmation.
4. Revalidate account/session/conversation before dispatch and before publishing outcomes. A cancellation after dispatch cannot undo an external acknowledgment. On ambiguous completion, reconcile before retrying; do not fake-clear the badge.
5. If the private API is unavailable, keep chat working and expose a restrained, truthful failure/retry path. No endless retry loop or notification spam.

Framework boundary: use existing lifecycle/viewport signals first. Verify that they distinguish an actually shown conversation from a retained page or a widget active only through a pin. If they cannot, add the smallest reusable presentation-activity contract; do not infer visibility from timers, focus labels or Discord DOM manipulation. The host must never know Discord read semantics. Initial-open acknowledgment is domain policy, not a generic auto-read collection feature.

Open-source comparison (November 17): [Vencord ReadAllNotificationsButton, e75ac9ac](https://github.com/Vendicated/Vencord/blob/e75ac9ace41f6212cfaa2d5f3670830be392da4e/src/plugins/readAllNotificationsButton/index.tsx) reads `ReadStateStore.lastMessageId` and dispatches `BULK_ACK` with `context: APP` and channel/message/read-state identities. [Abaddon, 7b3a4ff9](https://github.com/uowuo/abaddon/blob/7b3a4ff97ae6490a15adaa0a792ea9225c4c9e51/src/discord/discord.cpp) implements its own authenticated `/channels/{channel}/messages/{message}/ack` request. Vencord is the closer precedent for our resident web client. Vesktop embeds Vencord; a separate Vesktop-specific read-state implementation was not established by the search. No external source code is copied into WidgetRail.

The previously downloaded Discord public bundle confirms the read-state dispatcher owns both `BULK_ACK` and `CHANNEL_ACK`. Bulk processing updates local read state before its network operation completes, so a cleared local unread flag is not proof of server confirmation. Validate the current client's contract before live use and report local/requested versus confirmed outcomes accurately; do not add a second raw HTTP path merely to conceal that distinction.

## 4. Correct Discord startup presentation

Source-confirmed ambiguity: `web/provider/adapter.js:157` computes `ready` from current user plus webpack availability. `DiscordWebViewWidget.Views.cs:54` renders Sign in for every non-null, non-ready snapshot. A missing current user during client hydration is therefore displayed as signed out. The user reports the exact Connecting -> Sign in -> loaded sequence; timings have not been measured here.

- Introduce explicit provider connection/authentication states, such as initializing, signed-out, ready, reconnecting and unavailable, with validated domain values.
- Show Connecting through session creation, adapter/module discovery and account-store hydration. Show Sign in only with affirmative evidence of Discord's signed-out/login state; absent data or a timeout is not that evidence.
- Discover reliable client auth/connection stores or events during implementation. URL/login route can be supporting evidence, but no scanning visible labels, token extraction or new credential store.
- Distinguish logged-in but empty server list from incomplete hydration. Load individual sections independently where possible; missing optional messaging APIs must not make a valid voice session look signed out.
- A bounded startup failure offers retry/Open Discord with an accurate unavailable/loading explanation. Do not hide the flash with an arbitrary delay or cached sign-in flag. Account changes retire old personalized state before publishing the new account.
- Preserve native permission prompts and the current QR/browser sign-in flow.

## 5. Full-width decoration around a focused message

User screenshot: `C:/Users/dwive/AppData/Local/Temp/codex-clipboard-97459144-bf48-47a2-a599-53fcc040d59d.png`. It shows a faint rounded full-width row behind a green outline restricted to the message bubble. The visual is observed; its runtime paint owner is not yet proven.

Leading source hypothesis:

- `DiscordWebViewWidget.Messages.cs:593` declares a ReadingSurface with `FocusVisualTarget` pointing at the bubble.
- `styles/default.wrss:119` makes the row transparent with zero border width, but does not reset shadow or surface shading.
- `src/WidgetSdk/TileComponents.cs:144` assigns the shared action-surface role, also used by reading surfaces.
- `src/PlatformSettings/Themes/builtin-default.wrss:1125` gives focused action surfaces shading and a shadow.
- `src/OverlayFrontend.WinUI/Presentation/WidgetViewPresenter.Styles.cs:236` redirects focus decoration; its depth handling around line516 separately attaches shadow/edge effects to the original control. Transparent fill does not imply zero shadow.

Plan: reproduce with no pointer hover, then hover/focus/pressed separately; inspect computed normal/focused styles and native container paint. Temporarily isolate shadow, shading and native item border in a fixture to identify the exact source. Prefer the existing flat style vocabulary or a specific row style if this is authored depth. If target redirection itself violates the shared visual contract, fix that contract with tests. Do not move every background/shadow to the focus target globally: some controls intentionally focus one part while retaining a larger styled surface.

Required outcome: empty row space and date/unread separators have no unwanted outline, shadow or bevel; the message bubble retains a clear theme-correct focus cue. Preserve full-row hit testing where declared for previews, accessibility semantics, preview transit, virtualization, and recycled-row cleanup. Check own/incoming messages, attachments and plain text across dark/light/high-contrast and fractional DPI.

## 6. Transient action feedback through existing toasts

User supplied `codex-clipboard-34d6474d-f278-4ff2-a8ea-1fdd3ab62f5f.png`: “Sharing cancelled. Nothing was shared.” remains above the Messages header. Convert such one-off outcomes to the existing host toast service, without a new popup implementation or render-triggered publication. Review all Discord notice assignments, not just this string. Keep upload/sending progress, uncertain delivery with Check delivery, field validation, and persistent connection/recovery issues adjacent to their controls. Toast once per actual outcome, scoped to the original account/action; no repeated toast from polling or stale results. Preserve drafts and cancellation semantics. Add focused assertions for cancellation, sent/failed actions, duplicate refresh, teardown, and host-unavailable behavior.

## Consumer applicability and validation

| Consumer | Applicability / planned action |
|---|---|
| Capture Library | Only shipping `EnableSelection=true` consumer found in samples/FirstPartyWidgets. Adopt shared held selection and remove duplicate mode synchronization where safely supported. Verify toolbar/batch/preview behavior. |
| SDK selection fixtures/examples | Extend relevant command/epoch tests and native selection fixture; document the default gesture. No production trigger loops in templates. |
| Playnite, PoB, Spotify, YouTube collections | Current collections are not opted into this selection API. Preserve navigation and their existing triggers; no forced multi-select migration. Native representative regressions required for shared input/paint changes. |
| Discord messages | Adopt primary navigation and read-state command; fix transparent reading-row appearance. Current timeline viewport APIs are available for reuse, with presentation authority to be verified. |
| Game Help / shared reading, action and preview surfaces | Check focus visuals, preview hit testing and media child triggers if shared rendering changes. Do not change their domain semantics. |
| Browser/media pins, fullscreen, keyboard, modals | Preserve LT zoom/seek/Shift and ownership priority; cancel selection on transitions. |
| Spotify queue / other viewport consumers | Regression targets only if viewport/lifecycle contracts need extension. Preserve bounded memory and existing end-following semantics. |

Validation gates:

- Selection: fresh and existing selection; revisits; diagonal/grid moves; fast stick repeats; delayed rows; release before realization; query reorder; 256 bound; modal/foreground loss; controller reconnect; old installed worker compatibility. Check native mouse/keyboard/controller routing because the shared input path changes.
- Navigation: wide/compact, text scale/themes, B and bumper precedence, draft retention, notification target opening, share-recipient selection, active call and three stream pins through route changes. No live messages or uploads sent automatically.
- Read state: initial open, historical notification, initial unread marker retention, incoming at newest versus scrolled-up, hide during load/ack, pin-only activity, account/channel switches, deleted target, rate-limit/timeout and already-read acknowledgments. Native presentation evidence plus a user-controlled second-account message is needed for real Discord acceptance.
- Startup: warm/cold signed-in, slow hydration, genuinely signed-out, expired login, offline/reconnect, missing optional module and account switch. Record transitions to prove no false sign-in state; no arbitrary extra startup delay.
- Paint: isolate the supplied artifact in a native fixture, then review actual message rows and representative shared consumers. Screenshot evidence alone does not establish the cause.
- Run focused managed/adapter tests, then proportional native checks. Broaden only for new failures or changed shared contracts. Close automation capture helpers afterward; avoid retained UIA probes during performance checks.

## Additional scope: Windows Share (investigation only)

Feasible, estimated moderate complexity (3/5). This is outbound sharing to the Windows system share sheet, not registration of WidgetRail as a Windows Share target. Microsoft documents `IDataTransferManagerInterop.GetForWindow` / `ShowShareUIForWindow` for packaged **and unpackaged** desktop applications, so the current installer can remain unchanged. Reference: [desktop Share integration](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/integrate-sharesheet-send).

Proposed flow: retain the current controller-friendly widget destination picker and add an explicit **Windows Share…** destination. The host validates the existing opaque file offer, owns the Windows HWND/share sheet and returns to the source on cancellation. Windows chooses its own compatible receiving apps; a particular desktop app is not guaranteed to register as a share target. No Discord-specific implementation or paths exposed to widget authors.

Concrete boundaries found in source:

- `src/WidgetBridge/BridgeClientRegistry.Intents.cs`, `CommitIntentCoreAsync`: current file delivery requires a selected widget worker and calls `FileReferenceRegistry.AuthorizeShare`. An OS destination needs a distinct trusted host-delivery branch, with the same source/run/presentation validation. Do not invent a fake widget recipient or let workers resolve arbitrary filesystem paths.
- `src/PlatformBroker/FileReferenceRegistry.cs`, `RegisterSelected`, `OfferFiles`, `AuthorizeShare`: custody is an open read-only handle, not a durable pathname. Sources may unlink a temporary capture; replacing the original path cannot redirect the existing grant. Offers currently expire after 60 seconds. A share-sheet transfer therefore needs explicit bounded host custody independent of the picker timeout, plus an OS-compatible `StorageFile` representation of those exact bytes. Simply reopening `FileStream.Name` would weaken the existing guarantee. Evaluate streamed storage files versus bounded disk staging with real receiving apps; never buffer a large recording in memory.
- `src/OverlayFrontend.WinUI/Shell/OverlayShellPage.Intents.cs`: the current chooser owns input suspension, cancellation, source-preview retention and foreground checks. Integrate system-window ownership here rather than giving each widget its own native dialog/focus restoration.

Validation: reject expired/foreign offers; source withdrawal before handoff; cancellation during preparation; exact bytes after original deletion/replacement; multiple files and duplicate names; large recording/disk limits; receiving-app failure; host shutdown and temporary-file cleanup; correct focus return and no activation of the underlying game. Target selection is not delivery confirmation, and reported completion must not claim a message was sent by the receiving app.

Controller support is unverified. Microsoft does not document a share-sheet-specific D-pad/A/B guarantee; generic WinUI controller support is not evidence that this system window or every receiving app supports it. Test the installed Windows build first. Controller-to-keyboard mapping, if needed, is a separate scoped host feature and must not inject into unrelated foreground apps. Keep the native WidgetRail picker usable without relying on the system sheet.

Consumer matrix: Capture Library and any SDK file-share sender reuse the existing offer contract; Discord remains an existing widget receiver; other widgets are unaffected unless they opt into file sharing. Implementation is not authorized by this scoping request and has not started.

## Execution sequence

1. Confirm startup and read-state provider contracts; capture the row decoration reproduction. Prepare the navigation visualization.
2. Implement startup state and the bounded row-paint correction independently of navigation.
3. Implement shared held selection with Capture Library adoption and compatibility tests.
4. Implement integrated Discord navigation, then read acknowledgments against its final presentation lifecycle.
5. Run shared/native checks and prepare a matched candidate for user acceptance if implementation is authorized.

Implementation is authorized under the new goal. Approved wireframes: `C:/Users/dwive/.codex/visualizations/2026/09/10/01a08c6c-1b59-7460-b297-9e9cc7236510/discord-integrated-wireframes.html`. Branch: `codex/discord-navigation-selection`. Prepare a qualified matched candidate; preserve live user sessions when deployment would interrupt them. No merge, push, installer or release. Reassess proposed abstractions if evidence makes them more brittle than the existing design.

## Implementation checkpoint (not completion)

- Discord source now has persistent Call/Messages destinations, a responsive conversation sidebar, retained draft/conversation navigation, and call controls on message pages. Source-only layout review; native visual/focus acceptance is still outstanding.
- Connection states distinguish initializing, affirmative unauthenticated login, ready, reconnecting and bounded unavailability. Auth checks do not read credentials. Unknown auth APIs never imply signed out.
- Sharing cancellation, completed sends and pre-send upload cancellation use the existing banner-only notification publisher. Ongoing progress and uncertain delivery retain their actionable controls. The remaining notice assignments still need the planned full categorization pass.
- Message read acknowledgment uses the resident client's `BULK_ACK` path for one validated private conversation and watch epoch. Local applied/requested outcomes are distinguished from server confirmation. Read-head provenance is bounded; newer arrivals cannot silently change the opening watermark. No real-account acknowledgment has been tested.
- A concrete SDK gap was found: `IsAtEnd` plus current widget data could include an unobserved later publication. Added `ObservedEndItemKey`, captured only when every active viewport observed the exact current source revision at its physical end. This reuses existing wire feedback; no new host read-state or Discord-specific presentation logic. The widget schedules read actions only from viewport callbacks, not polling. The source still needs native timing qualification and historical-notification/late-completion coverage.
- The row-paint correction explicitly suppresses inherited action-surface shadow/shading on transparent message rows while retaining bubble focus. Native isolation/visual proof remains pending.
- Held selection now has protocol128 capability gating and atomic BeginAdd/idempotent Add semantics. The SDK exposes a selection-change event; Capture Library reconciles mode-dependent preview/action policy through it rather than only through its old toolbar handler. The host implementation queues bounded visited identities, uses the existing range retention/lease admission, and stops admitting new visits on trigger release. Already-focused queued choices can finish while ownership remains valid; navigation completed after release cannot add an unseen item. Query/scope/foreground/pointer departures cancel the gesture. Native qualification has not run yet.

Evidence in `artifacts/discord-navigation-selection`:

- `widget-checkpoint-2.log`: 845 Discord checks passed before the held-selection protocol bump; includes navigation, startup, toast outcomes and initial read-state coverage.
- `backend-checkpoint.log`: 230 adapter/backend/observer tests passed; `read-state-provider-2.log`: 49 focused messaging-provider tests passed, including issued watermark versus a new arrival.
- `sdk-observed-tail-tests.log`: focused viewport suite passed, including stale publication and multiple viewports. `sdk-held-selection-tests.log`: focused selection suite passed, including no range filling, idempotence and version admission.
- SDK API baseline was regenerated after observed-tail addition; regenerate again for the new selection event. Protocol native header and matching interop binary still need regeneration/build for128.
- A locale-dependent edit briefly corrupted ellipsis text in Messages.cs; the focused upload test exposed it. Repaired the UTF-8 conversion without discarding changes; upload tests then passed. Earlier obsolete no-tabs assertions were updated to the approved Call/Messages contract. These failed intermediate attempts are not native product passes.
- Native validation publish initially failed NETSDK1150 because the validation frontend references the Bridge executable. The retry explicitly uses `SelfContained=true`. Its build is still pending at this checkpoint; several later source edits require a final incremental build before testing. The original candidate remains running with replay recording (last observed frontend52868/replay33872); no candidate restart or live Discord operation was performed.

Next: finish native compilation, regenerate protocol/API artifacts, qualify held selection in list/grid/grouped fixtures and old-worker compatibility, extend read-state/native navigation tests, complete toast categorization and native visual review, then package through the reviewed update path and coordinate any interruption to the active recording. No unfinished implementation is considered accepted or ready to deploy.

### Follow-up checkpoint

- Native Release validation publication succeeded with a freshly built protocol128 interop DLL; the generated header parity check passed (217 constants). SDK API baseline now contains both observed-tail state and the selection event (4141 symbols).
- `widget-checkpoint-7.log`: 853 managed Discord checks passed. `read-state-expanded-4.log`: 14 focused checks include historical middle-page entry, observed Latest, navigation departure, ambiguous late completion, and deterministic cancellation of coalesced unsent watermarks. A real bug found during this work was that cancellation could reserve a superseded unsent watermark. Pending and actually dispatched watermarks are now distinct; queued requests that never run also release their reservation.
- `backend-checkpoint-2.log` and `backend-minified-2.log`: 226 selected JavaScript checks passed in each form. The composed adapter40 asset is 91,507 bytes, inside the 128KiB limit. This is not live Discord acceptance.
- SDK selection and viewport focused suites pass. Authoring guidance in `docs/developers/indexed-collections.md` describes held selection, capability negotiation, selection callbacks and exact observed tails.
- Definite send rejection/upload failure, pin-command failures and confirmed notification setup use banner feedback. Inline notices remain for field/recipient/attachment validation, ongoing transfer or diagnostic progress, uncertain mutation/delivery with reconciliation, and persistent permission/device/receiver recovery instructions. Stream details are requested diagnostic content, not a transient action result. New notification-success/no-repeat coverage is awaiting its next managed run.
- Native list testing has exercised initial L2 selection, visited-only movement, revisits, release retention, a second hold and focus departure. The first fixture failure was an existing explicit LT navigation shortcut correctly winning arbitration, not a dropped worker request; the selection fixture now isolates that conflict explicitly. The user refined the rule: **active selection wins LT over widget shortcuts while focused in the collection**; inactive selection preserves those shortcuts. New native checks also cover guide replacement and delivery of a later widget-owned LT release. Their latest binary is building; no full native pass is claimed yet.
- The existing physical pointer fixture did not complete its first mouse-selection assertion. A separate computer-use row click selected the expected row, so mouse functionality works in that observed case; the scripted input discrepancy is still being isolated with press/release/tap evidence. Do not treat controller-dispatch calls as physical-controller hardware evidence or skip the old mouse/keyboard regression gate.
- The user authorized closing the running overlay. The old candidate was brought forward through its existing single-instance activation and closed gracefully; its frontend, bridge and replay processes exited. Only isolated fixtures have been launched since. No default-profile widget installation, merge or release has occurred.

### Native selection qualification and latest checks

- `selection-native-6.json`: **175 native checks passed** on the final selection implementation. List, adaptive grid and grouped grid cover automatic start, visited-only adds, revisits, release retention, second hold, departure, active-selection priority over a conflicting LT shortcut, the matching single guide hint, and inactive shortcut precedence. An unrelated later release is no longer claimed by a completed selection gesture. Existing physical mouse/keyboard selection, checkbox hit areas, light/dark rendering, bounds and large-query identity/focus checks also passed. This validates normalized controller dispatch in real native controls, not controller hardware capture.
- Earlier native fixture attempts are retained as failures: an unintended LT shortcut, missing foreground ownership, one non-reproducing injected mouse tap, and a test-only unsupported Released shortcut declaration. None was counted as a pass or used to weaken the production protocol. The final fixture uses supported Pressed shortcuts and checks release ownership without inventing a new authored event phase.
- `widget-checkpoint-10.log`: **857 managed Discord checks passed**. `read-state-expanded-6.log`: **16 focused read checks passed**. `backend-all-3.log`: **258 JavaScript tests passed**, including observer/activity/session suites; the earlier selected minified suite remains 226/226.
- The watch-timing hypothesis was narrowed after source review: cold history already awaits the existing observation operation. The affected case is a **retained chat on reactivation**. A controlled delayed-watch test now proves that its one visible observation waits for the existing watch completion and does not require a second layout event. Account/route/visibility and provider epoch are still revalidated before dispatch. No generic SDK abstraction was added for this Discord-specific read policy.
- Pin-feedback outcomes are also scoped to their originating account and web session. Notification-success feedback is covered by the full managed run and does not reappear on refresh.
- The successful native fixture was closed after verification. Remaining work is still required: delayed-navigation/query/foreground and legacy-capability selection edges, Capture Library behavior through the shared event, native Discord wide/compact navigation and row-paint qualification, synthetic/native read timing, matched package/host staging and candidate launch through reviewed updates. Current implementation is uncommitted and the goal remains active.

### November 18 qualification checkpoint (supersedes earlier pending checks)

- `selection-native-8.json` passed 195 native checks: list/grid/grouped selection, active/inactive LT shortcut precedence, guide arbitration, delayed navigation after release, disconnect/re-prime, missed release, host input reset, query/layout replacement, mouse/keyboard, bounded realization and deep identity continuity. Controller evidence is normalized dispatch through native controls, not physical hardware testing.
- `capture-selection-tests-2.log` passed 2 Capture Library checks, including the real SDK start-and-add path, selection-driven preview/action policy and unchanged query identity. `sdk-compat-tests.log` covers legacy v127 declarations and omitted capability JSON. The protocol header has 217 constants; SDK API baseline has 4141 symbols.
- `widget-checkpoint-12.log`: 860 managed Discord checks passed. `backend-all-4.log`: 259 JavaScript checks passed. `backend-minified-3.log`: 177 minified provider/session checks plus 7 composed-payload checks passed; adapter40 sealed payload is 91,998 bytes, below 128KiB. This replaces the earlier selected-suite counts; it does not claim live Discord acceptance.
- Native compact/expanded navigation and message paint were checked in `messages-minimum-6` (66), `messages-expanded-2` (70), `messages-contrast-1` (69) and `messages-light-1` (70, text scale1.25). Minimum surface remains 420x360; the native history viewport was approximately122.4 DIPs. The transparent row has no native shadow, shading or border; the bubble retains focus decoration. Own-message material now uses opaque theme surface tokens with an accent border.
- The synthetic provider now implements scoped read acknowledgment and records `readAcks`. `messages-read-minimum-2/result.json` passed72 native checks and `messages-read-expanded-1/result.json` passed73. These prove visible-history feedback reaches the provider for the correct channel/message and clears the unread indicator without sending messages/uploads. Real Discord server receipt and second-account behavior remain user acceptance checks.
- Shared host finding: `WidgetViewPresenter.Focus.cs` previously called broad `CancelGroupEntry()` when a previously declared focus request disappeared. That could cancel a newer native entry unrelated to the withdrawn request. `messages-read-minimum-1` reproduced the focus stall during history publication. Collection entry now carries the existing request ID; withdrawal cancels only matching entry/return work. No new public API or widget counter is introduced. The failing Discord case passes after this change; direct delayed-request regression qualification is still in progress, so this fix is not yet declared fully qualified.
- Focus-withdrawal applicability: Discord messages, Playnite library/achievements, Catalogue, Settings installed list, Capture Library, Spotify, YouTube Music, YouTube, Games & Apps, Game Help and PoB publish focus/return intents through the shared presenter. They benefit automatically; no consumer migration is needed. SDK Gallery and FullApplication are source/example consumers. The shared entry/return fixture is the representative native regression gate; app-specific live behavior outside the Discord fixture is not claimed verified. Consumers without focus requests are unaffected. Browser/media/keyboard/modal ownership stays ahead of held selection.
- A shipping-only build failure exposed an existing unguarded diagnostic assignment in `WidgetViewPresenter.Previews.cs`: `FocusTrace` is excluded without validation. Guarded that assignment with the existing validation compilation symbol; production preview behavior is unchanged. A clean shipping build remains required.
- Candidate package versions are prepared as Discord0.1.0.21 and Capture Library0.2.1, newer than the inspected installed0.1.0.20/0.2.0. No package update or candidate deployment has happened yet. No merge/push/installer/release.

### Shared regression and shipping qualification follow-up

- `selection-native-12.json`:201 native checks passed using the freshly built x64 worker, including completed-request withdrawal preserving a newer native entry and cancellation of the genuinely pending authored request. Runs9–11 launched the stale `bin/Release` worker instead of the new `bin/x64/Release` output; their failed setup and partial assertions are not qualification evidence. The matching executable path is recorded in `selection-processes-12.json`.
- `collection-return-1` and3 reproduced a second host ownership edge: a delayed return had parked native focus on loaded, enabled host chrome, then direct focus to another authored control was not treated as departure. `WidgetViewPresenter.CollectionReturns.cs` now recognizes that transition as ownership transfer while retaining the guard for unloaded/disabled structural recovery. `collection-return-4.json`:51 checks passed, including programmatic departures, immediate capture, delayed data, disabled/query-replaced targets, retained hide/show, recreation, horizontal layout, resizing and native wheel cancellation. `entry-replacement-1.json`:65 checks passed, including normal authored entry and refresh/replacement continuity. `inline-preview-1.json`:13 native rich-preview checks passed.
- The trimmed shipping build exposed IL2026 in the existing `WidgetSdk/InlinePreviewActionContract.cs` ancestry fingerprint. Replaced reflective anonymous-object serialization with an internal typed record and generated JSON metadata. Its identity fields and equality behavior remain unchanged. This shared private contract is consumed by the SDK queued execution, presentation session, bridge and intent authority; no widget migration is needed. Existing inline previews (Capture Library, Discord and other authored interactive previews) receive the same authority checks. `sdk-inline-trim-tests-3.log` passed the focused suite, expanded to cover changed occurrence keys, ancestor kind and preview entry button in addition to removal/reparenting/value updates. No public API change or manual widget bookkeeping was introduced.
- The latest native build is16. The final shipping retry is in progress in `shipping-3`; previous shipping outputs are failed diagnostics, not runnable candidate evidence. The source-generated serializer does not waive trimming or suppress the analyzer.
- The older working candidate catalog uses development versions Discord0.1.0.40/Capture Library0.2.0.31. The installed default catalog contains accepted0.1.0.20/0.2.0. Candidate staging must therefore clone the default catalog and use the documented update transaction to0.1.0.21/0.2.1; it must not downgrade the older development catalog or manipulate its version state. Keep the production catalog intact.

## Candidate delivery and completion audit

Implementation and independent qualification are complete; the candidate is running for user acceptance. No merge, push, installer or release was performed.

| Requirement | Evidence / outcome |
|---|---|
| Shared held L2/LT visited-item selection, retention and active-mode precedence | `selection-native-12.json`:201 native checks. SDK selection/legacy compatibility and Capture Library event-policy tests pass. No widget-managed trigger loop. |
| Integrated wide/compact Call/Messages navigation and persistent call controls | Native420x360 and1040x680 cases, draft/section/Back behavior and real shipping inbox smoke check. Theme/text-scale cases passed. |
| Scoped read acknowledgments | Managed read lifecycle/late-result tests and provider tests; native synthetic72/73-check cases acknowledge only the displayed conversation and clear unread state. Existing Discord client `BULK_ACK`, without new direct HTTP path. Real server synchronization remains live acceptance. |
| Truthful authentication/startup | Managed and JS hydration/signed-out/offline cases; saved live session loaded normally in the shipping candidate. A single smoke startup does not independently prove every transient timing case. |
| Remove full-row decoration | Native normal/focused paint assertions and compact/expanded/light/high-contrast captures; bubble focus retained. No global theme-depth rewrite. |
| Toast outcomes | Managed success/cancellation/failure/no-repeat/account-scope checks. Persistent validation, progress and ambiguous delivery remain actionable inline. |
| Shared regression preservation | Native return51, entry65 and inline-preview13 checks; shared consumer matrix above. Production trimming checks and generated preview-authority metadata covered. |
| Matched candidate | `shipping-3/payload.json`; shipping dependency verifier304 references and Settings discovery9 packages; normal published startup4 checks; candidate payload check144 DLLs and22 native exports. |
| Reviewed widget updates | Separate candidate catalog cloned from installed active packages. Update logs show Discord0.1.0.20→0.1.0.21 and Capture Library0.2.0→0.2.1 using `Install-CommunityPackage.ps1`/CLI update. Production catalog-state SHA-256 remained unchanged. |
| Documentation, ownership and scope | Indexed-collection authoring guide, Discord user/source docs, fixture README, current platform status and this plan updated. Windows Share remains scoped only. |

Candidate: `artifacts/discord-navigation-selection/candidate/shell.json`, with default settings at `%LOCALAPPDATA%/WidgetRail`, separate catalog under `candidate/catalog`, and shipping executable under `shipping-3/frontend-published`. Process7156 was observed live with bridge52304 and Discord0.1.0.21 worker52216. These process IDs are observations, not permanent identifiers. `candidate/launched.json`, `candidate/payload-verification.json` and `candidate/live-smoke.json` record delivery evidence. All standalone validation fixture processes were closed.

Remaining user acceptance (not claimed as completed): physical controller L2/LT behavior; second-account live read synchronization and new unread arrivals; active call plus incoming/outgoing stream pins while changing sections. Native tests used synthetic data and normalized controller dispatch. No live messages, uploads, microphone grants, joins or broadcasts were performed. These checks do not block delivering the qualified candidate for the acceptance step requested by the goal.

## Acceptance follow-up: section scroll and panel focus (November18)

User reported chat scrolling upward after each Call→Messages return. User clarified cross-panel navigation: left goes to the selected conversation; right goes to the composer. Also requested removal of More because Refresh/Open Discord already exist in the top bar. The wide standalone inbox and purpose of compact presentation were discussed; no minimum-size change has been requested.

Implementation in progress:

- Discord declares its changing body with the existing `TransitionContent` contract. Account/conversation identity is part of the stable destination key. The host, not the widget, stores reading position.
- `WidgetViewPresenter.PageMemory.cs` extends the existing bounded page-memory store to indexed viewports and content-section returns. Section restoration does not steal focus from the persistent tabs; Page Back keeps its previous focus policy. Existing source/query/layout/key validation, native row admission, user-input cancellation and owner isolation remain authoritative. It does not apply a stale exact-query memento to an unrelated query.
- `IndexedCollectionElement` now exposes the existing protocol focus-neighbor vocabulary, matching buttons/action surfaces. The host falls back per direction from row neighbors to collection neighbors. This avoids rewriting every message row or allocating event counters. SDK baseline4146; focused SDK native declaration/transport test passes.
- Discord sidebar row exits target the composer; composer/back/history left exits target the current selected conversation, with search fallback if that conversation is absent from the filtered list. Compact hidden controls remain ineligible native focus targets. More and its redundant L3 header shortcut are removed; X Write/Y Latest and existing Menu commands remain.
- Shared applicability: Discord adopts collection exits; other indexed-list consumers keep unchanged defaults and their authored row targets. Content-section memory also applies to existing transitions in Spotify, YouTube Music, Playnite, PoB, Network Controls, Display Controls and Power. No consumer-specific scroll bookkeeping is added. Standard navigation-shell content transitions use the same mechanism. Page transitions retain Back-only focus restoration; section returns restore geometry only. Native Discord and shared regression qualification are pending.
- Managed Discord suite862 passed, including selected-row/composer exits, source-owned transition declaration and removal of redundant controls. Native expanded test now checks three consecutive section round trips against the exact visible key/clipped fraction and cross-panel targets. First attempt stopped at the obsolete five-controls assertion after More was removed; four remaining controls were visually in bounds. The fixture assertion now matches the requested four controls; no product layout assertion was weakened.

Further acceptance refinements: the conversation list now says Search and has no separate reload button. The top-level Refresh performs the existing version-resetting conversation-list refresh as well as the Discord state refresh, scoped to the active account and Messages route. Managed suite863 checks passed.

Native investigation found a real secondary trigger: after recreation at offset0, the list requested an older provider page before viewport restoration. Page-memory restoration is now reserved synchronously before yielding to layout. The existing collection-scroll-pending callback includes those pending mementos, suppressing premature boundary/end observations. Settled/canceled restoration republishes normal viewport state through the existing observation path. This is shared host behavior, with no widget timer or boundary-suppression flag.

The new section-position test is isolated from the older exhaustive paging test: that scenario intentionally evicts/reloads the target1111 and later recovers around1001, so using1111 after it is not a valid fixed test precondition. The dedicated section test starts with the known initial20-message window, then performs three round trips and exact-anchor comparisons. The full paging/draft/upload regression remains separately required. The native secondary conversation is11 (the managed fixture uses20); the focus regression now uses its actual declared ID. Final native qualification is pending.

### Acceptance fixes delivered in Discord0.1.0.22

- `section-expanded-4/result.json`:31 native checks passed. Three consecutive Call/Messages round trips retained the visible key and clipped fraction, with exactly one original history fetch and no extra paging. Right from a nonselected sidebar row reached the composer; Left returned to the selected row; history-left did the same.
- `section-compact-1/result.json`:26 native checks passed for the same three-round-trip reading-position contract. `section-general-expanded-1/result.json`:74 full native interaction checks passed (paging, previews, keyboard, draft retention, synthetic cancellation and Back). `collection-return-5.json`:51 shared return-ownership checks passed.
- `section-return-managed-final.log`:864 managed checks passed, including account-qualified section-memory keys and consolidation of refresh. Indexed-collection exits passed SDK declaration/transport validation; public baseline4146 symbols.
- Reassessment: new generic indexed viewport memory is deliberately limited to Content section transitions. Page Back continues to use its existing explicit collection-return contract, avoiding competing anchor policies. Both continue to use the shared bounded native viewport-restoration mechanism.
- `shipping-4` is the successful trimmed shipping build. `published-startup-2/result.json` passed ordinary startup. `candidate/payload-verification-22.json` verified144 deployed DLLs and22 capture/preview exports. Candidate14212 was launched using `candidate/shell-22.json`, the same default settings and separate candidate catalog. Discord0.1.0.21→0.1.0.22 used the documented reviewed update transaction; no production-catalog update or release.
- More and the unused L3 header action are removed; the separate conversation reload is removed; Search is the visible label. Top-level Refresh retains state refresh and also reloads the active conversation list with its existing catalog-version reset. The separate inbox/compact layout was discussed but has not been removed or given a wider minimum size.
- Remaining live acceptance: user controller feel and real conversation/account behavior. No live message, upload, permission grant, or voice/channel join was automated.
