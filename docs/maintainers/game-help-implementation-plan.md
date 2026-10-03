# Game Help: prerequisites, decisions, and implementation progress

Status: Implementation accepted by the user on 2026-10-03; final automated closeout passed.
Release: application 0.1.0-preview.24, SDK/CLI 0.4.1-dev. Local main integration and
Inno installer generation are authorized; build provenance records their exact source commit.
Updated: 2026-10-03. Branch: `codex/game-help-foundation`.
Baseline: local main `8ec3e93d`. Merge `81f292a1` also carries the delivered
preview.23 control-consistency fixes (`783182d6`, `55bc7d4d`) into this feature
branch so the final delivery retains them. Older worktrees are retained by user
request; the unrelated local edit in main/docs/implementation-agent-goal.md is preserved.

This is the working plan and progress record. Update it with each implementation
slice: code delivered, evidence, limitations, and the next concrete step. Checked
items mean implemented and verified in their stated scope, not physical acceptance.

## Accepted product decisions

- Build shared prerequisites first; implement AI Game Help last.
- Capture the foreground application after the overlay closes and the preparation
  countdown finishes. No application picker, target activation, or selected-window
  capture mode. Preserve the captured application's name/title as attachment context.
- Chat has in-thread question suggestions, a scrolling transcript, and a fixed
  bottom composer. Selecting a suggestion appends a normal user message.
- Infer useful context from visible game/location text even when the user cannot
  identify the encounter. Offer up to five plausible follow-ups, without inventing
  certainty or padding the list. Preserve selected suggestions in chat history.
- Support screenshots and five-second recordings; no existing-image file picker.
- Gemini is the first provider. Users supply their own API keys; use protected
  credentials, never snapshots, chat history, or diagnostics for secrets.
- Use structured responses and real grounded sources. JSON schema conformity
  does not establish factual correctness. Do not invent source URLs/timestamps.
- Browser is a lazy-created pinnable widget with a host-owned WebView2 surface.
  Default-browser fallback applies when no enabled handler is available.
- When an intent destination is already pinned, prefer updating that surface
  without leaving Game Help, provided the receiver supports passive delivery.
  This is generic presentation policy, not a Browser/Game Help special case.
  Passive-delivery support is declared per intent-handler mapping, never as a
  widget-wide switch. Authors retain explicit ways to request interactive presentation.
- No microphone or system audio in recordings. No automatic overlay reopening.
- Preview capture inside the requesting widget, using a shared modal with
  Send/Retry/Discard; do not launch another widget to preview an attachment.
- Native local media is a reusable inline SDK element, not modal-only. The preview
  modal composes that element; widget authors can also place it directly in ordinary
  or pinned layouts with the same host-owned playback and attachment lifetime.

## Recording flow and copy

Title: **Record game context?**

> WidgetRail will hide. Bring the application you want to capture to the foreground.
>
> Recording starts after a 5-second countdown and lasts 5 seconds. No audio will
> be recorded.
>
> Reopen WidgetRail when finished to preview and send the recording.

Actions: Proceed / Cancel. Use the existing themed controller dialog conventions.

After the overlay is hidden, a nonactivating, click-through countdown shows
"Recording starts in 5...". Resolve the foreground target when the countdown ends and check it is producing
frames; otherwise fail with a retryable explanation.
Record five seconds, show a small recording indicator, and release capture.
Never record a different foreground app. Countdown and indicators must not enter
the captured footage or steal focus. Exclusive-fullscreen countdown visibility
requires physical qualification; do not promise universal visibility.

Captures use a session independent of overlay visibility. Reopening the overlay
returns to the pending conversation and preview. Media is temporary until sent;
retry/discard/cancellation and session teardown clean up local resources. Remote
upload cleanup belongs to the Gemini service. Capture and upload authorization
are separate; initiating controller press/release cannot leak into the game.

## Architecture and responsibility

| Shared platform | Feature-specific behavior |
| --- | --- |
| Foreground capture and captured-application metadata | Inferring game/location and helpful questions |
| Authorized bounded capture, countdown and attachment references | Requesting context and attaching it to a conversation |
| Inline native image/video elements and a preview modal composition | Pending question and Send/Retry/Discard workflow |
| Intent contracts, handler selection, delivery, normal widget activation | Browser toolbar and YouTube playback handling |
| Native hosted web surface, controller input and pinning | Browser address/history |
| Existing declarative UI plus reusable message/choice primitives if needed | Chat history, spoiler policy, follow-up selection |

Do not expose raw HWNDs, arbitrary file paths, DOM/host scripting bridges, or
privileged execution through intents. New privileged capabilities still require
explicit host implementation. Keep long uploads/model calls off the Bridge's
ordinary dispatch path.

### Extensible intents

- Namespaced string ID, positive contract version, bounded JSON payload.
- Packages declare requested/handled contracts with locally packaged schemas.
  Initial implementation may embed the bounded schema in the manifest: it is
  then covered by the same package integrity checks without another file loader.
- One documented JSON-schema subset, rejecting unsupported keywords and remote
  references. Schema identity is deterministic; conflicting definitions for the
  same ID/version must never be silently treated as compatible.
- Generic Bridge authorization, resolution, correlation, delivery and terminal
  results. No worker-to-worker channels and no enum entry for every new intent.
- SDK standard contracts initially: `widgetrail.web.open` and
  `widgetrail.video.open`. Future contracts can ship without host changes unless
  they need new host authority.
- Only enabled, admitted handlers are eligible. User defaults or a chooser resolve
  ambiguity; do not silently choose a provider by catalog order.
- Built-in external-browser fallback handles the standard web contract when no
  enabled compatible widget exists. A handler failure must not also auto-launch
  another app; offer explicit fallback. Cancellation/withdrawal invalidates work.
- Intent manifests are declarations, not permission grants. Delivery must be bound
  to authenticated caller, current user action, handler generation and lifetime.
- Playnite game links use the shared web intent with bounded HTTP(S) validation
  and exact displayed-action authority. The former companion `Process.Start`
  route is removed. YouTube consumes the standard video contract including
  timestamps up to the embedded player's 86,400-second limit.

### Normal navigation and B (revised user decision)

An intent opens its destination as an ordinary widget. Do not create a return-to-
sender stack or remap B. Existing local handlers retain first refusal (dialogs,
editing, page interaction); unhandled B follows the existing tray behavior.
Users may choose another widget or pin the destination for use while gaming.
The sender retains ordinary widget state when users return manually. Existing
pinned/fullscreen exit behavior retains priority. Consume the entire gesture
across transitions. Browser page history has its own Back control; it must not
trap B indefinitely. This supersedes the original return-destination proposal.

### Existing pinned destination (additional user direction)

Separate routing/data from presentation. A request's default presentation hint
should prefer an existing surface; a sender can explicitly request opening the
full widget. Each handler mapping (contract ID + version within a widget) opts
into accepting its intent while passively visible; the default for each mapping
is interactive-only. There is no widget-wide passive-intents flag. A widget can,
for example, accept `web.open` in its passive pin while a separate configuration
intent requires interaction. Sender preferences and receiver escalation are per
individual request, subject to the selected mapping's capability.

If a compatible handler is already presented in the pin and accepts passive
delivery, revalidate that exact live pinned projection/worker, deliver there,
and preserve the sender's focus and page. Do not create a new pin or rely only
on persisted pin settings. With no usable pin, use normal widget activation.
An explicit sender request to open the widget takes precedence over reuse.

Allow a receiver to request interaction for a particular accepted request (for
example sign-in). Escalating presentation must not invoke the intent twice:
retain the already accepted data and change presentation. Window/focus ownership
remains host-side, and a handler cannot gain foreground permission through a
background event. This policy is implemented for live full-widget and authored
pinned projections and compact embedded-media destinations, using their distinct
media-document authority.

### Browser and local media preview

Browser: Back/Forward, Reload/Stop, title/domain, Open externally; controller scroll
and virtual cursor for arbitrary sites, normal geometric navigation for toolbar.
Host owns browser lifecycle, origins, permission handling, input and cross-root
pinning. No permanent browser startup/prewarm. Browser overhead is not zero.

Local media: a reusable inline SDK element backed by native
`MediaPlayerElement`/`MediaPlayer`, with host-themed play/pause/seek/replay controls.
Authors can place it in ordinary and pinned layouts; the capture preview modal
composes the same element with Send/Retry/Discard. Image preview uses the equivalent
image presentation. B exits inline interaction according to its input scope; in
the modal it dismisses and restores focus. Only host-issued bounded attachment
references are accepted initially. A later packaged/local asset source can extend
the source contract without adding a second player implementation or exposing
arbitrary paths. This is separate from the restricted WebView media contract.

### Gemini and Game Help

Prove selected model's image/video, structured output, search grounding, limits,
and free-tier availability before implementing the full chat. If grounding and
structured output need separate calls, keep that behind the service. Requests
need cancellation, bounded conversation/attachments, rate-limit errors and upload
cleanup. Never log credentials or captured content. Validate structured output
and preserve grounding references separately.

Widget response model: answer blocks, observations/uncertainty, follow-up choices,
source cards. Suggested actions execute only after user selection. Incremental
presentation must preserve controller focus and avoid auto-scrolling when the
user reads earlier messages. Retain game context within a conversation when
following sources. No game-memory access or autonomous game input.

## Ordered milestones

### 1. Intent foundation and activation
- [x] Bounded contracts and schema validation, including malformed/conflicting definitions.
- [x] Manifest declarations and backward-compatible omission; uses existing manifest validation at package admission.
- [x] Deterministic pure handler resolution policy and standard contracts (not live dispatch).
- [x] Authenticated Bridge transport, admission, cancellation and correlated delivery for ordinary source controls.
- [x] Declarative SDK intent actions on buttons/action surfaces and versioned snapshot/update transport.
- [x] Worker receiver API and exact-process delivery with cancellation, duplicate rejection and lifetime checks.
- [x] Wire ordinary SDK actions and receiver transport into Bridge admission and dispatch.
- [x] Ordinary frontend activation with existing B/tray behavior and gesture handling.
- [x] Pinned/indexed source admission with the same lease/projection authority as ordinary actions.
- [x] Full-widget/authored pinned destination reuse, sender hints and per-mapping passive-delivery opt-in/escalation.
- [x] Extend destination reuse to compact embedded-media pins with exact document authority.
- [x] Fake sender/handler end-to-end tests, including isolated native shell checks.
- [x] Include the completed intent feature in the final Game Help physical-check candidate.

### 2. Browser and link consumers

- [x] Protocol-66 reusable browser document/element, atomic updates and lazy Browser intent receiver.
- [x] Lazy host-owned web surface with controller cursor/scroll and toolbar.
- [x] Pinnable browser widget; synthetic normal/pinned focus qualification.
- [x] User physical acceptance of the Browser candidate (accepted with all changes on 2026-10-03).
- [x] Default-browser fallback through the standard intent (synthetic launcher qualified; user acceptance recorded).
- [x] Playnite link migration; YouTube handler with optional start time (Playnite 0.2.115 and YouTube Video 0.3.38 installed).

### 3. Application context and capture
- [x] Foreground-only capture; selected-window capture and correction dropdown removed by user decision.
- [x] Bounded screenshot capture and attachment ownership.
- [x] Five-second preparation countdown, five-second video capture, confirmation.
- [x] Hidden-overlay lifetime, cancellation, target loss and resource cleanup tests.

### 4. Native media element and attachment preview
- [x] Reusable inline native media SDK element and controller controls. Ordinary/modal native checks pass; user acceptance recorded on 2026-10-03.
- [x] Preview modal composing the same element with Send/Retry/Discard.
- [x] Attachment expiry/revocation, worker restart and cleanup checks. User acceptance recorded on 2026-10-03.

### 5. Gemini foundation
- [x] Documented API compatibility review and synthetic image/video/schema/grounding probes; free-tier constraints reflected in settings. Evidence: `gemini-docs-01/compatibility-review.json`. No live-key request made.
- [x] User verified Gemini model access and optional paid grounding during candidate testing; accepted 2026-10-03.
- [x] Protected per-user key setup and dedicated request service.
- [x] Cancellation, rate limits, uploads/cleanup and response validation.

### 6. Game Help
- [x] Native chat, in-message suggestions and composer.
- [x] Context inference, up-to-five clarification choices, spoiler preference.
- [x] Preview/send workflow, grounded source cards and intent actions, including supplied YouTube timestamps.
- [x] User accepted the completed Game Help workflow and all changes on 2026-10-03.

## Evidence and progress log

- 2026-10-01: Recorded accepted plan and established isolated worktree from main.
  Verified existing Task Switcher identity/order source, Playnite's companion-based
  shell launch, and frontend B routing. Implementation begins with milestone 1.
- 2026-10-01: User clarified that native media must also support inline widget
  placement. Updated milestone 4: reusable element first, modal composition second.
- 2026-10-01: User removed automatic return-to-sender navigation. Intent destinations
  open as normal widgets; preserve existing B/tray and pinning behavior. No return
  stack is needed. Updated milestone 1 before implementing frontend routing.
- 2026-10-01: Implemented contract compilation, closed bounded schema subset,
  canonical contract identity, manifest declarations, standard web/video schemas,
  and pure handler resolution. Added [contract reference](../reference/widget-intents.md).
  Focused checks: 17/17 passed (`artifacts/game-help/intents-tests03.log`);
  existing SDK checks: 148/148 passed (`artifacts/game-help/sdk-tests01.log`).
  Build logs: `intents-build03.binlog`, `sdk-build01.binlog` in the same evidence
  directory. Initial two test failures were an invalid test publisher fixture
  (not a reverse-DNS ID); fixed the fixture. Added an exact numeric regression
  against decimal rounding weakening integer/minimum validation.
  Existing package/catalog checks also pass: 37/37
  (`artifacts/game-help/catalog-tests01.log`, `catalog-build01.binlog`).
- Next: connect user-initiated SDK requests and handler delivery through the
  authenticated worker/Bridge transport, then normal frontend activation. No
  production transport or UI is enabled by the pure policy alone.
- 2026-10-01: Added protocol-v65 intent action declarations, `.OpenIntent(...)` on
  SDK buttons/action surfaces, atomic payload updates, and `Widget.OnIntentAsync`.
  Source control declarations carry schema identity; displayed/current action
  revalidation rejects changed payloads, disabled/busy owners, scope changes and
  reused collection occurrence identities. Intents are user-activated declarations,
  not unsolicited worker-to-worker messages.
  SDK: 151/151 (`sdk-intents-tests02.log`); contract/authority: 19/19
  (`intents-tests04.log`). Updated the SDK public API and native protocol artifact;
  managed/native parity verifies 148 constants. Corrected one old indexed-template
  fixture to expect CurrentVersion, as deferred templates advertise the current
  protocol without evaluating item content.
- 2026-10-01: Implemented host-to-worker receiver transport for both runtime
  assemblies, using the existing bounded async worker lane. User cancellation
  completes a separate cancel/drain handshake without truncating pipe frames or
  abandoning response correlation. Delivery cannot launch/recover a worker or
  target its replacement. Duplicate/older delivery IDs are consumed before author
  code, including on failure. Intent runtime checks: 3/3 including a real fake-worker
  IPC cancellation that keeps the worker usable (`runtime-intents-tests03.log`).
  Shared indexed-lane IPC regressions: 7/7 (`runtime-indexed-regression01.log`).
  WinUI review checklist applied to ownership/input/security; no new UI authored.
- Current boundary: ordinary, pinned and indexed intent sources are connected
  through the Bridge, real workers and native shell, including passive full-widget
  and authored-layout pinned destinations. Compact embedded-media destinations
  remain pending. Browser, capture recording, native
  media element/preview modal, Gemini integration and Game Help are not enabled.
  No updated user candidate has been staged yet.
- 2026-10-01: Bridge prepare/commit/cancel messages and presentation-session APIs
  now connect the previously separate pieces. Verified package intent declarations
  enter the host catalog and its fingerprints. Preparation admits only matching
  current source actions and manifests, without starting a target. Bounded one-shot
  tickets survive the intentional source hide, but reject expired/replaced workers.
  Handler choice is explicit; only standard HTTP(S) web requests with no handler
  return an external-browser launch grant. Cancellation targets one source-owned
  active delivery. Synthetic Bridge checks: 5/5 (`bridge-intents-tests01.log`),
  session build clean (`session-intents-build01.binlog`). Frontend wiring and real
  pipe end-to-end checks are next, so this is not yet a user-visible completion.
- 2026-10-01: Connected frontend activation, a themed controller handler chooser,
  explicit browser fallback offers after handler rejection, and external launch
  through the existing release-aware close/handoff logic. B continues to open the
  tray; no return stack was added. Extracted shared handoff preparation/cleanup
  while retaining Task Switcher's ordering. Late browser-launch completion cannot
  hide a newer overlay opening.
  End-to-end Bridge checks: 7/7 (`bridge-intents-tests07.log`), including real
  worker pipes. Native checks: 18/18
  (`native-intents-01/result-focus-restore.json`, frontend build05 binlog).
  All URLs/providers were synthetic; the native web launcher was replaced with
  a fixture delegate, so no websites or system settings were opened.
- Confirmed during validation: per-intent cancellation initially escaped as an
  unhandled request cancellation and closed the Bridge. It now returns a terminal
  rejected result and uses reserved cancellation transport capacity; a subsequent
  intent succeeds on the same connection. Native dialog teardown also exposed an
  unreliable raw-Control focus restore; the host dialog now saves/restores logical
  widget presentation memory and requests native focus after teardown. The test
  explicitly navigates to a noninitial control and verifies return to it.
- Latest user direction: passive delivery into an already pinned destination is
  the next intent task. Preserve sender focus by default when supported, without
  restricting authors who require interaction. Policy is documented above and
  is not claimed implemented by the ordinary-activation tests.
- User clarification: passive-delivery capability is per intent-handler mapping
  (contract ID/version), because a widget can expose several mappings with
  different interaction requirements. Sender preference remains per invocation.
- 2026-10-01: Implemented `WidgetIntentHandler.SupportsPassiveDelivery` on each
  handler mapping, defaulting to false. Request contracts remain free of receiver
  policy. Bridge resolution/preparation carries only the matched mapping's flag;
  this does not activate a handler or relax the current interactive admission.
  Regression checks cover mixed policies within a widget, version independence,
  manifest and Bridge round trips, unchanged schema identity, and rejection of
  widget-wide/request-side flags. Contract checks: 20/20
  (`handler-mapping-contract-tests01.log`); Bridge checks: 8/8
  (`handler-mapping-bridge-tests01.log`), under `artifacts/game-help`.
  Live pin authority, passive dispatch and interaction escalation remain pending.
- 2026-10-01: Connected live pinned-destination delivery for full-widget/authored
  projections. Sender presentation preference is part of displayed-action
  authority. The session validates its live selected projection; Bridge rechecks
  visibility, worker, schema-bound catalog and per-mapping opt-in. Unpinning cancels
  pending delivery. Receivers return Rejected/Accepted/InteractionRequired; the last
  accepts data once and opens stored state without calling the handler again.
  Contracts: 20/20 (`passive-intents-contract-tests01.log`),
  SDK: 151/151 (`passive-intents-sdk-tests01.log`), runtime: 3/3
  (`passive-intents-runtime-tests01.log`), Bridge: 9/9
  (`passive-intents-bridge-tests02.log`). Native shell: 22/22
  (`native-intents-01/result-passive03.json`), including exact noninitial sender
  focus, passive pin visibility, one-time escalation, explicit opening preference,
  and prior A/B/chooser/external-launch regressions. Updated the unshipped SDK API.
  Test fixture initially expected initial focus rather than correctly restored
  focus; corrected that assertion. Also replaced stale-authority refreshes in the
  pipe test with current-target presentation reads. Review also kept late external
  completion from clearing a newer intent's pinned-target ownership. Synthetic
  shells PID24816 and PID9300 were closed normally. No website, real playback or
  system-setting action was used.
- 2026-10-01: Connected pinned controls and virtualized rows to intent preparation
  using existing live projection/selection and item-lease authority. Both session
  and Bridge revalidate query, scope, item, worker and primary action; disabled or
  busy owners, retired leases and stale selections cannot issue intents. Intent
  rows do not also invoke their widget action handler. Pinned sources use existing
  main-window activation for chooser/destination handoff; a self-directed passive
  update can retain pinned interaction. No B return stack was added.
  Contract checks: 21/21 (`intent-sources-contract-tests01.log`); intent pipe
  checks: 10/10 (`intent-sources-bridge-tests01.log`); indexed regressions: 19/19;
  pinned regressions: 6/6 (matching `intent-sources-*-regression01.log`). Native:
  26/26 (`native-intents-01/result-sources02.json`), including authored pin buttons
  and actual virtualized rows in main and pinned views. The synthetic browser
  handoff stage now explicitly reestablishes main-window foreground after native
  pin disposal and confirms the fake launcher was actually called on failure.
  PID26836 closed normally. No website, real playback or system setting changed.
- Next: host web surface and Browser widget, then Playnite/YouTube consumers and
  compact embedded-media destination integration.
- 2026-10-01: Connected the first real consumers before the new Browser widget,
  using the already implemented default-browser fallback when no handler exists.
  Playnite Library 0.2.115 declares `widgetrail.web.open` for its details links;
  only bounded HTTP(S) URLs without credentials or control characters are emitted.
  The selected link payload is part of snapshot authority, replacing the old
  companion requery-and-shell-launch path. Removed that unused companion API and
  its widget operation/busy state. Provider parsing remains bounded and validated.
  YouTube Video 0.3.38 handles `widgetrail.video.open`, checks provider/video ID/
  schema/position, and reuses its latest-wins load command. The per-mapping passive
  opt-in is declared; compact pin reuse still needs its host authority integration.
  Start time reaches the provider's cue call, while visible position remains zero
  until a real observation supplies duration/position. A late search-key probe
  no longer replaces an intent-opened player or blocks its initial rendering.
  YouTube: 59/59 (`youtube-intent-tests06.log`); updated fake-player adapter check:
  1/1 (`youtube-intent-adapter-tests07.log`, tests 0/42.5/86400-second cue arguments).
  Playnite details/installation/selection regressions: 26/26
  (`playnite-intent-tests04.log`). Both full-trust applications build cleanly.
  The native MTP `dotnet test` driver reported zero tests even without a filter;
  used the built MSTest executables, verified discovery and successful nonzero
  results instead. Focused executable filtering uses `Name~...` here.
  No installed packages updated, browser opened, or real playback started.
- Browser implementation remains next. Inspected WinUI Gallery WebView2 and the
  existing media owner: arbitrary web navigation must be a separate host-owned
  surface, preserving the media adapter's sealed-document/origin restrictions.
- 2026-10-01: Added the reusable `UI.WebBrowser(WebBrowserDocument, id)` SDK
  declaration and protocol 66. Bounded HTTP(S) URL, document ID, navigation revision
  and accessible name participate in snapshot validation and atomic resource/
  authority updates. The Browser widget starts empty, handles web intents and
  declares full-widget pinning. Rendering preserves its navigation revision;
  only new accepted requests advance it, including reopening the same address.
  SDK: 152/152 (`browser-contract-tests01.log`); Browser widget: 2/2
  (`browser-widget-tests02.log`). SDK API regenerated; native protocol artifact
  regenerated and parity verifies 149 constants. No native browser is enabled or
  claimed complete: the WebView owner, rendering/input adapter, durable placement,
  permission policy and native tests remain next. See
  `docs/reference/widget-web-browser.md` for this boundary and authoring contract.
  Browser's manual address input currently uses the existing 96-character SDK
  entry limit; longer URLs work through intents. Expand the bounded text-entry
  contract deliberately for browser addresses and Game Help questions before
  releasing the physical-check candidate.
- 2026-10-01: Carried preview.23 Settings/Spotify source fixes and release baseline
  into this branch with merge `81f292a1`; no merge conflicts. Existing worktrees
  retained. Isolated native fixtures were closed through normal owned WM_CLOSE;
  no user candidate is currently staged with these unfinished prerequisites.

## Validation and delivery policy

### Browser controller/layout revision — 2026-10-01

- Unpin incident: preserved `artifacts/game-help/browser-unpin-incident01` from
  candidate PID 41556. WebView2 `0x8007139F` first arose during native placement,
  then escaped through selection, hide/show and F1. Presenter disposal scheduled
  browser transfer without awaiting it before disposing the pinned XamlRoot.
- Presenter disposal now joins native browser placement before clearing its tree
  or returning to window teardown. Failed detachment retires the affected native
  controller before its root disappears. Native presentation faults stay local
  to the browser, with a Reload widget message; retired controls are not resumed.
- `native-browser-01/unpin-recovery02.json`: 39/39 checks. A deliberate transfer
  barrier proves unpin-from-tray cannot finish early; page/controller identity is
  preserved. Forced controller closure verifies Back, hide/show, another widget
  and worker retirement remain functional. Tests use synthetic pages only.
- Corrected trimmed Release candidate is `artifacts/game-help/browser-candidate-04`
  (`browser-unpin-production01` frontend); cleanup/catalog/inventory checks passed.
  Relaunched on the existing profile with Browser selected, controller enabled,
  PID 43500 recorded in `browser-candidate04-launch.json`. Browser's address
  control is on-screen and enabled. Awaiting the user's unpin regression check.

- Follow-up: Browser placement now prefers the visible pin independently of input
  readiness. Main-widget entry shows a notice and View hint instead of moving the
  live page; unpinning returns it to main. Native regression
  `native-browser-01/pin-stability01.json`: 35/35, including zero placement moves
  across three actual tray-to-widget cycles, pin interaction, intents, and unpin.
- Built, installed, selected and enabled Playnite Library 0.2.115 through the
  candidate CLI. Package: `artifacts/game-help/playnite-candidate-01/`
  `widgetrail.samples.playnite-library-0.2.115.wrwidget`; SHA-256
  `5e9d2ea8d37b5606907178d5f21de4cd2da8f34d9431f67a3d437c8f3ae46d7d`.
  Existing configuration is retained. Game-details links now provide the real
  consumer for Browser intent testing; no game or system action was invoked.
- Relaunched the updated trimmed host at `artifacts/game-help/browser-candidate-03`
  with Playnite selected and the existing profile (`browser-candidate03-launch.json`,
  PID 41556 at launch). The 0.2.115 Playnite application process is running from its
  installed package. Source payload: `browser-pin-production01`; package/catalog
  verification passed. Ready for the user's real link and pinned-browser check.

- User's first physical check confirmed basic browsing but rejected the large
  toolbar, redundant address row and intermediate interaction state.
- Replaced that layout with one themed compact header, a thin loading indicator
  and page-first content. Address row remains only in the empty state. Mouse and
  keyboard retain header controls; controller users enter the page with one A.
- Direct mappings: sticks for pointer/scroll; A click; bumpers history; X reload/
  stop; Y address; triggers zoom with held repeat; L3 precision; R3 page text;
  Menu external browser. View remains owned by the shell.
- Added per-document `ExitInteractionOnBack` (default true for embedded elements).
  Standalone Browser sets false, leaving B unhandled for normal main/pin host
  routing. Dialogs consume B; the host retains release ownership when opening tray.
- Page-text editing is host-owned, bounded and exact-field scoped. It rejects
  replaced fields and cancels on navigation/input loss. Passwords, frame editors
  and rich contenteditable controls remain external-browser workflows.
- Browser 0.1.1 sample checks 2/2; SDK 152/152. Native browser revision checks:
  `artifacts/game-help/native-browser-01/redesign02.json` (23/23). Existing native
  intent regressions: `native-intents-01/redesign01.json` (26/26). Visual inspection:
  `artifacts/game-help/browser-redesign-preview.png` using intercepted synthetic
  content, never an external site. Additional pending-edit cancellation coverage
  and final Release qualification recorded below when completed.
- User approved closing/relaunching the candidate; stop at the next physical
  Browser check rather than advancing capture/Gemini/chat.
- Follow-up direction: triggers zoom rather than page, and zoom should follow
  the controller pointer if supported. Touch-style synthetic pinch had no effect;
  native touchpad-style pinch works and preserves an off-center anchor. Bounded
  100–250% scale is read back from the actual visual viewport. A now uses real
  mouse down/move/up phases; dialog/input changes cancel held pointer input.
- Final native revision checks: `native-browser-01/redesign09.json` (31/31),
  including consecutive off-center anchored zoom steps, narrow click targeting
  while zoomed, actual HTML drag-and-drop, cancelled drag release, and text-field
  preparation cancellation. Native touchpad pinch takes coordinates relative to
  the visual viewport; adding its scroll offset double-counted the location and
  was corrected. Browser-only drag data uses bounded native interception for A
  gestures, with file payloads rejected and ordinary mouse gestures unchanged.
- Final Release qualification passed in
  `artifacts/game-help/browser-redesign-trim-final-result05.txt`: native click,
  zoom in/out, HTML drag-and-drop, exact-field text editing and embedded Back,
  alongside the existing native control projection checks. The small fixture
  now uses the production focus slot, waits for rendered page frames, and checks
  completed drop instead of blocking release on an intermediate drag-over title.
  Diagnostic traces remain confined to validation builds.
- Updated candidate: `artifacts/game-help/browser-candidate-02`, with Browser
  0.1.1 and the final trimmed production frontend from
  `artifacts/game-help/browser-redesign-production01`. Services and sealed widgets
  come from `browser-release-03`; full provenance is in
  `browser-candidate02-provenance.json`. Candidate inventory, cleanup verification
  and all 13 bundled package admissions pass. Physical acceptance remains pending.
- Relaunched candidate 02 with the existing user profile and controller input
  enabled (launch PID 5008; `browser-candidate02-launch.json`). Its Browser address
  control is enabled/on-screen and its own Bridge is running. Left open for the
  requested physical check; no installer, merge or later Game Help milestone.

### Browser candidate checkpoint — 2026-10-01

- Native Browser: 17/17 checks in `artifacts/game-help/native-browser-01/result04.json`.
  Includes a 1,500-character address through ordinary worker input, one controller
  across pin/main roots, stale navigation rejection without lifetime loss, exact
  source-focus preservation for passive intent delivery, pointer clicks, native
  history, B routing and worker retirement.
- Existing native intents: 26/26 in
  `artifacts/game-help/native-intents-01/result-browser01.json`.
- SDK: 152/152 in `artifacts/game-help/browser-sdk-tests09.log`; Browser sample:
  2/2; managed/native protocol parity: 151 constants. Extended text is opt-in up to
  2,048 characters; the default remains 96, with per-control commit validation.
- Release publication: `artifacts/game-help/browser-release-02/payload.json`.
  The first Release build caught reflection-based pointer serialization; fixed
  using generated JSON metadata and rebuilt cleanly. The sealed Browser package
  is included in the developer edition. Production validation hooks stay excluded.
- Candidate staging: `artifacts/game-help/browser-candidate-01`; source and payload
  provenance: `artifacts/game-help/browser-candidate-provenance.json`. This is an
  unpackaged Release candidate, not an installer or a main-branch integration.
- Trimmed native smoke passed: `artifacts/game-help/browser-trim-result02.txt`.
  The first smoke attempt clicked after the title changed but before navigation
  completed; the fixture now waits for completion. Generated pointer serialization,
  actual synthetic-link navigation and B-to-toolbar work in trimmed Release.
  Existing slider, value-button and artwork projection checks pass alongside it.
  Candidate inventory and the real catalog verifier admit all 13 bundled widgets.
- Launched the real candidate with `--widget=browser`, controller input enabled
  and the existing `%LOCALAPPDATA%/WidgetRail` profile. Launch identity is in
  `artifacts/game-help/browser-candidate-launch.json` (PID 32092 at launch).
  Address control is enabled/on-screen; candidate-owned Bridge is running.
  Inspected `browser-candidate01.png`; left the candidate open for physical testing.
- Stop after launching Browser for the user's physical check. Do not start capture,
  preview media, Gemini or chat until the user finishes this gate. The overall
  Game Help objective remains incomplete. Playnite/YouTube consumer package
  staging and compact-media destination reuse remain separate outstanding work.

Use focused synthetic checks for contracts, malformed payloads, stale lifetimes,
duplicate delivery, cancellation, fallback and existing focus behavior. Physical game input,
exclusive fullscreen and capture behavior are user acceptance checks. Do not alter
system settings or start real playback as part of automated checks. Preserve the
existing candidate while it is under test; coordinate restart for visible changes.
Launch a candidate when a runnable user-facing slice is ready; no installer unless
requested. Keep worktrees until explicitly asked to remove them.


### 2026-10-01: Playnite startup crash and session-only pins

- User crashed before Browser physical testing; Browser acceptance remains pending.
- Preserved candidate 04 PID 43500 dump and logs in
  `artifacts/game-help/playnite-crash-incident01`. Native/managed stacks show
  `ContainerContentChanging -> UpdateContainer -> RefreshContextIndicator ->
  IsIndexedLeaseCurrent -> Monitor.Enter -> CoWaitForMultipleHandles ->
  CXcpDispatcher::OnReentrancyProtectedWindowMessage` and XAML fail-fast
  `0xc000027b` / stowed `0x8000ffff`. No WebView transfer is present in that path.
- Replaced the session's acquisition scopes with a reentrant Monitor gate that
  uses a non-alertable kernel wait on STA only while acquiring the lock. The
  caller's synchronization context is restored before accessing session state.
  Authority checks, mutual exclusion, state publication and worker IPC are unchanged.
  Neither plain .NET Lock nor ReaderWriterLockSlim suppressed pumping in the
  forced-contention experiment; those alternatives were discarded.
- Per user request, removed startup pin restoration for ordinary, Browser and
  compact-media pins. Pins survive overlay hide/show within the running session,
  but never restart with the application. Persist geometry/opacity only; old
  `WidgetId` fields are ignored and disappear on the next preference save.
- Automated validation: session suite 317/317, pin placement 12/12, Debug build
  clean. Native Browser 41/41 includes real WinUI collection-layout contention,
  old-format active-pin startup, existing unpin drain and local WebView failure
  recovery (`native-browser-01/layout-contention01.json`). Release candidate
  candidate 05 is staged and relaunched (PID 11332) with the normal profile.
  Playnite reached Interactive; the user subsequently switched to Browser, so
  no further UI navigation was injected. Physical acceptance remains pending.
- Published cleanup guard now recognizes scoped acquisition/disposal and also
  verifies the underlying `Monitor.Exit` survives trimming. Production passes;
  an isolated damaged DLL with that release call removed is rejected. Release
  inventory and the 13-widget catalog pass. Strengthened contention regression
  additionally verifies recursive reads and restoration of the caller context.


### Continued implementation while user is away

The user explicitly authorized continuing through Game Help and closing the
overlay as needed. This supersedes the earlier pause at Browser physical testing.
Keep physical acceptance unchecked; use isolated local fixtures for capture and
synthetic responses for Gemini until user credentials and real-game testing are
available. Finish the required Browser interaction-mode parameter before the
remaining ordered-window/capture, native-media, Gemini, and chat milestones.


### Browser interaction modes (2026-10-01)

- Required `UI.WebBrowser(document, BrowserInteractionMode, id)` argument now
  selects `ActivateToInteract` or `InteractOnFocus`. Removed the independent
  `ExitInteractionOnBack` flag. Missing/unknown wire modes fail validation.
- Browser 0.1.2 opts into focus interaction. Host checks actual slot focus,
  placement and input admission; passive pins cannot acquire interaction.
  Focus acquired before controller initialization enters once ready. Focus
  loss cancels held input. The shell consumes the entry A gesture.
- Explicit mode retains A entry and B exit-to-selection; the whole B gesture
  stays consumed, and the next B uses normal widget routing. Dialog cancellation
  works in either mode. Guide presents the mode-specific actions.
- SDK 152/152, Browser 2/2. Native `interaction-mode06.json` passes all 54 checks,
  including both modes, sibling focus, passive pins, entry-press ownership,
  dialogs, unpin drain, closed-controller recovery and session-layout contention.
  Early fixture runs lacked actual foreground acquisition; enabled the existing
  `--validation-platform-activation` adapter. Later fixture fixes wait for deferred
  native tray/focus restoration and focus an actual sibling rather than a list
  container. No production navigation policy was weakened for those fixture fixes.
- Fresh full release payload: `artifacts/game-help/browser-release-04/payload.json`.
  Preparing candidate 06; user physical acceptance remains pending while other
  Game Help prerequisites continue under the user's away-time authorization.


### Capture, inline media, and initial Game Help (in progress)

- Added a separate `system.apps.windows.capture.v1` capability. Preview permission
  still grants no pixels. New requests require Interactive; request status,
  cancellation and owner-only attachment reads can continue in Background.
- Captures latch the existing Task Switcher opaque window identity, process birth
  time and class. Host confirmation and a release-aware animated hide precede a
  five-second countdown. Native capture rechecks the exact foreground target,
  visibility and protection throughout; no audio path exists.
- Added PNG capture (up to 1920x1080) and five-second H.264 MP4 capture (up to
  1280x720 at 15fps), plus an excluded nonactivating countdown peer.
  Native proof: `context-capture-validation09/result.json` passes known red/green
  pixels, 75-frame/five-second MP4 metadata, hidden-target rejection, cancellation
  and reuse. The first WinUI fixture itself rendered black; a validation-only
  owned GDI window provides independent known pixels. Separate-process fixture
  attempts were refused for lack of actual foreground; production checks were
  not relaxed. Validation exports are excluded from ordinary native builds.
- Added bounded owner-scoped attachment references, expiry, cancellation,
  permission revocation and cleanup, with chunks for authorized reads. Broker
  policy tests initially had only the expected capability-count fixture mismatch
  (40 -> 41); new ownership/expiry/read/discard checks passed. Final suite rerun pending.
- Protocol 67 adds reusable `UI.CapturedMedia` image/native-video presentation for
  ordinary and modal layouts. The native preview uses play/pause, replay and
  controller seeking; runtime/pinning/trim qualification is pending.
- Initial Game Help sample now has a transcript, suggestions which become user
  messages, fixed composer, existing ordered-window correction dropdown, spoiler
  settings, capture preview/send/retry/discard and lifetime-owned async requests.
  Gemini client uses the current Interactions API, bounded structured output,
  the existing protected secret store. Optional grounding remains disabled until its attribution UI is complete. No user key or capture was
  sent to Google. Integration tests, grounding attribution, native-media runtime
  validation and delivery remain outstanding; no Game Help acceptance is claimed.
- Official docs captured in `artifacts/game-help/gemini-docs-01`: current
  `gemini-3.8-flash` supports free model requests, but Google Search grounding is
  not available on its free tier. Plain model answers offer explicitly labeled
  manual search links. Source cards must come only from returned URL citations.


### Sandboxed Game Help and synthetic end-to-end qualification (2026-10-01)

- Corrected the initial full-trust sample approach: existing policy prohibits
  full-trust packages from requesting sandbox host capabilities. Game Help now
  uses ordinary `dotnet-worker` activation with a required parameterless entry.
  No trust-policy exception was added. Gemini HTTP is a dedicated trusted
  provider reached through `HostServices.GameHelp`; secrets use the existing
  package-scoped `gemini.api-key` slot. Workers submit references, not pixels.
- Capture and Gemini permissions are distinct. Admitted Gemini work survives
  overlay hiding, while revoking Gemini, private-secret or attached-capture
  authority cancels it. Duplicate uploads fail busy; no automatic HTTP retry.
- Added stale-frame/worker checks around media resolution, periodic revocation
  checks for loaded previews, 8 MiB attachment bounds, and expired dead-session
  file cleanup. Changing application clears prior image context; expired media
  cannot silently disappear from a send request. Canceled answers cannot append
  to a new conversation or clear its newer busy state.
- `context-capture-validation10/result.json`: 17 native checks pass, covering
  known PNG pixels, exact five-second silent MP4, target rejection, cancel/reuse,
  native play/pause/seek/replay, inactivity, delayed-load retirement, revocation,
  and player/image disposal.
- `gamehelp-flow01/result02.json`: 12 checks pass across real worker/capability
  pipes and native host confirmation, animated hide, countdown, screenshot,
  video, manual reopen, preview, discard and explicit Send to a fake provider.
  The first full-flow run exposed a missing WinUI node-admission mapping. Fixed
  it and added WinUI admission to the preview regression; no decoder issue.
- Game Help tests: 9/9. SDK: 152/152. Session baseline: 317/317, plus the new
  captured-media compatible/replaced/restarted-frame test. Broker: 66/66.
  Intent Bridge: 11/11, including compact media identity and replacement checks.
- Public author documentation: `docs/reference/widget-capture-and-game-help.md`.
  Game Help added to the developer catalog. Fresh Release publication and
  trimmed native media validation are next; no installation or merge requested.
- Grounding is deliberately disabled before provider dispatch until Google's
  prescribed attribution UI exists. The first candidate offers manual searches;
  it does not claim verified live sources. Official capability docs were checked,
  but no real key or user capture has been sent to Google. Physical game accuracy,
  exclusive-fullscreen countdown, Browser acceptance, and final media pinning
  checks remain open.


### Delivered candidate (2026-10-01)

- Fresh production frontend/services: `artifacts/game-help/gamehelp-release-02`.
  Native DLL export inventory contains capture but no validation-only exports.
  Small trimmed-release probe: `trimmed-gamehelp02/result.txt` passes existing
  slider/artwork/Browser interactions and native capture image/video playback.
- Candidate: `artifacts/game-help/gamehelp-candidate-02`, launched as PID 41276
  on Game Help with the normal `%LOCALAPPDATA%/WidgetRail` profile. It contains
  14 validated widgets. Catalog/seal/inventory verification passed; 135 deployed
  frontend DLLs match the qualified publication (`gamehelp-candidate02-dlls.json`).
- The final Game Help assembly was separately published and resealed into the
  candidate after a retry fix: failed uploads retain the capture explicitly sent
  by the user, including subsequent retries. Its exact source publication is
  `gamehelp-final-package/payload/GameHelpWidget.dll`; provenance records this
  override. Release widget tests remain 9/9 and include the failed-upload retry.
- Package validation caught unsupported `border-radius`; replaced with WRSS
  `corner-radius` and added real stylesheet parsing to the widget regression.
  Failed intermediate candidate/publication evidence remains intact.
- YouTube Video 0.3.38 built, validated, installed, selected and enabled through
  the CLI. Playnite 0.2.115 remains installed. Compact media intent Bridge checks
  pass, but real YouTube playback/pin behavior still needs user acceptance.
- Read-only screenshot inspection confirms the actual sandboxed release widget
  reaches setup. Game Help's permissions remain at their normal default; the user
  must allow them in Settings and add their own Gemini key. No key was supplied,
  no request was sent to Google, and no user game/media was captured in testing.
- No merge, commit, installer, or worktree cleanup performed. Automated runs used
  synthetic owned windows, generated media, and fake provider responses only.


### Completion audit continuation: grounded links (in progress)

The active objective is readiness for the complete planned physical check. The
first candidate's free-key path is usable, but grounded source links remain a
real implementation gap; the goal is not complete merely because that subset
passes. Physical Browser and real-game acceptance remain the user's tests.

- Rechecked current official Google Search documentation and terms, saved as
  `gemini-docs-01/search-suggestions.html` and `terms.html`. Search suggestions
  must accompany grounded answers; Google Search API grounding uses paid quota.
  Keep grounding explicitly opt-in/off by default; never run a paid/live call
  automatically. Preserve provider attribution and source links rather than
  discarding the HTML notice or displaying only a collected link list.
- Started bounded host-only attribution contracts. `GameHelpAnswer` can carry an
  opaque provider-document reference; raw search markup is `[JsonIgnore]` and
  cannot enter worker JSON. The parser retains up to five original snippets,
  bounded to 64 KiB, and rejects cited answers that omit associated suggestions.
- Added owner-scoped `ProviderDocumentRegistry` with bounded entries, exact
  identity resolution and retirement checks. Not yet connected to the live
  broker: `WebGrounding=true` still fails before HTTP until presentation exists.
- Next: connect registry lifetime/permissions and discard; add a typed host-owned
  provider-document mode to the existing Browser element. Render original
  attribution with script/permissions/download/network access blocked, using
  host-authorized source bytes. Preserve controller entry/B modes; external
  links must retain their real destinations. Avoid eager WebViews for offscreen
  transcript content and maintain the existing small native-controller budget.
- Then re-enable the opt-in setting, qualify grounded fake responses and native
  presentation, republish, and replace the running candidate. Candidate 02 stays
  running unchanged meanwhile. No real key, upload, system-setting action,
  commit, merge, installer, or worktree cleanup occurred in this continuation.


### Grounded presentation and native lifetime qualification

- Protocol 68 adds typed host-issued provider documents through the existing
  Browser element and explicit interaction modes. Original Google suggestions
  remain host-only; widgets receive opaque references and ordinary citation data.
  Grounding is now an explicit paid-quota opt-in, off by default. No real Google
  request or user media upload was made.
- Restricted native mode blocks scripts, permissions, downloads and background
  requests. It has no address editor; controller pointer/scroll/zoom and B work.
  User-selected search links preserve the exact destination through the existing
  release-aware external-browser path. Ordinary citation cards use intents.
- `grounding-flow01/result03.json`: 21 full-flow native checks pass, including
  capture, preview, fake grounded response, original attribution, script and
  external-resource rejection, exact controller links, B, viewport laziness,
  four-controller budget, scrolling and new-chat disposal.
- The longer transcript test exposed a genuine layout cycle in viewport-driven
  native reparenting (`layout-cycle-incident02.txt`). Provider elements now keep
  a finite default inline height, do not force star rows through an autosized
  transcript, and defer placement work outside EffectiveViewportChanged. The
  regression then passed without relying on a package stylesheet.
- Existing native Browser regression: 54/54 (`native-browser-01/provider-regression01.json`).
  SDK 152/152; Game Help 10/10; broker 68/68. Added session coverage proving that
  changing interaction mode preserves content authority while worker replacement
  rejects old authority. Provider store tests cover discard, revoke, re-grant,
  replacement epochs, disposal and bounded retention.
- Attempted native screenshots after the fixture had relinquished foreground did
  not capture the chat. Treat them as unusable, not visual proof. Automatic approval
  review rejected removal of `grounding-flow01/chat.png` and `chat-screen.png`;
  they remain in ignored test artifacts and must not be included in distribution.
- Trimmed provider presentation and a fresh release candidate are still pending.
  The previous user candidate was closed under the user's standing authorization
  for native validation. No commit, merge, installer or worktree cleanup occurred.

### Final physical-check candidate (2026-10-01)

This checkpoint supersedes the earlier candidate and pending implementation
notes above; their evidence remains as a chronological record.

- Completed optional grounding and original Google Search suggestions. It is
  off by default and labeled as paid-quota opt-in, with the provider's retention
  disclosure in settings. Official API/schema/model review is recorded in
  `gemini-docs-01/compatibility-review.json`; this is not a live-key success claim.
- Provider responses require completed status. Cited YouTube links preserve
  supplied timestamps; unsupported timestamps retain the original web link.
  Release Game Help tests: 14/14. SDK: 152/152. Broker: 68/68. Session authority
  checks cover capture and provider content across worker replacement.
- `trimmed-grounding04/result.txt` passes native media and restricted provider
  rendering alongside existing slider/artwork/browser input regressions.
  Existing Browser suite: 54/54.
- Capture confirmation now uses native Proceed/Cancel buttons without a redundant
  single-option list. Native retries initially exposed an automation-ID lifetime
  issue, not a stuck dialog: the popup and primary button existed but its ID was
  missing on the current template. Assigning the ID in OnApplyTemplate fixed the
  fixture. `grounding-flow01/result06.json` passes all 21 complete-flow checks;
  `native-intents-01/dialog-regression01.json` passes all 26 shared-dialog/intent
  checks, including multi-choice selection and focus restoration.
- Fresh publication: `artifacts/game-help/gamehelp-release-04`. Staged candidate:
  `artifacts/game-help/gamehelp-candidate-04`. All 14 widget catalog/seal admissions
  and release inventory checks passed. All 135 frontend DLL hashes match the
  publication. No separately rebuilt assembly overrides are present.
- Candidate launched as PID 27808 with `--widget=game-help`, normal user profile,
  and normal controller routing. Read-only UIA confirms Game Help is visible and
  enabled. Process responsive; no frontend error entries for this process at
  handoff. Launch and hash evidence: `gamehelp-candidate04-*.json`.
- No real API key, paid request or user-media upload was used. No game was launched,
  system-setting control activated, or commit/merge/installer/worktree cleanup done.

Physical check order:

1. Allow Game Help's requested permissions in Settings and add your Gemini API key.
   Leave web grounding off initially; ask a text question and try a suggested reply.
2. With a game open, confirm the selected application (or correct the dropdown).
   Try a screenshot, then a five-second silent recording. Check confirmation,
   countdown and capture; manually reopen to preview, discard/retry, or Send.
3. Verify controller preview controls, cancellation, and a follow-up using context.
   Check source links open Browser/YouTube and reuse a compatible existing pin.
4. If your project has eligible paid quota, optionally enable grounding and check
   cited sources and Google Search suggestions. Real-game accuracy, project access,
   exclusive-fullscreen countdown visibility and final controller/pin feel remain
   user acceptance; automated fixtures do not establish those results.


### Full-trust services and controller chat revision (2026-10-01)

User direction supersedes the sandboxed Game Help decision above: full-trust
applications must be able to use shared framework services, and Gemini-specific
logic must belong to the widget. The user then requested a complete chat UX review
and explicitly stopped testing while using the PC. Work after that point is
source editing/review only; no tests, builds, installs or launches were run.

Framework changes prepared:

- Removed the manifest rule forbidding host permissions on full-trust packages.
  Application bootstrap uses the shared argument parsing, authenticated capability
  connection and SDK client. Factory injection and the protected HostServices
  property both work; ordinary Windows access remains unchanged.
- Bridge creates the same permission/lifecycle-managed companion for full-trust
  applications. Both trust modes bind the broker pipe to the actual launched PID,
  in addition to nonce and package/instance authentication. No automatic capability
  grants were added for full trust.
- Removed Gemini service/capability/backend/contracts from the host, SDK, protocol
  and Windows provider. Client, schema, model, grounding and parsing are owned by
  Game Help's application package. Its API key uses application-owned DPAPI storage;
  a key in the older sandbox prototype's secret slot must be entered again.
- Replaced special attribution registration with generic restricted HTML documents:
  HostServices.Documents.CreateAsync/DiscardAsync, presentation.documents.v1, and
  UI.ProviderContent. The host treats all supplied HTML as untrusted and retains
  ownership, revocation, limits, lazy presentation and restricted native browsing.
- Sealed bundled catalogs and release publication now handle full-trust executable
  entrypoints and preserve their SDK/runtime dependencies. Installed full-trust
  trust approval remains unchanged. Protocol source advances to 69.

Checks completed BEFORE the user's stop request:

- Broker: 67/67 after the initial domain replacement.
- Bridge build succeeded. A real full-trust child process passed the focused
  service regression for consent, granted window/document calls, undeclared access,
  denial and restart (fulltrust-services01.txt). Its assertions were subsequently
  strengthened to reject stale status; that revision is not yet executed.
- Game Help test build exposed a moved-model namespace reference. Corrected that
  source and replaced the obsolete sandbox-client test, without rerunning after
  the stop request. No final all-green or physical acceptance claim is made.

UI changes prepared:

- Separate, bounded setup card; no unrelated game picker, New chat or Options
  controls before a key exists. Window permission failures no longer appear as
  detached setup errors before the window service is needed.
- Native Grid spacing is explicit, with a bounded conversation column and a
  fixed composer. Two-column starter cards have short titles and supporting copy.
  User/assistant messages, citations, follow-ups and request status have a clear
  hierarchy using theme colors. Errors appear next to the relevant workflow.
- X opens Add context, Y opens Chat options, R3 requests composer focus through
  the existing focus-group contract. RT sends only from the composer subtree;
  it cannot override preview or browser trigger controls. B retains normal tray
  and modal routing. Manual composition sends directly; starter prompts can offer
  capture choices inside the conversation.
- Application dropdown/refresh live together in Add context. Settings use a
  consistent vertical sequence: Answers, Live search, Gemini account, Conversation.
  New chat and key removal have cancellable confirmation. Preview has a bounded
  native media view, question/consent summary, Y Send and X Retry; A/LT/RT remain
  media controls. API-key and chat operations cannot submit while busy.
- Regression sources cover setup isolation, focus-group requests, active modal
  scopes and snapshot validity. Native flow source follows the redesigned routes.
  These sources have not been executed yet.

Next validation boundary, only after the user permits testing:

1. Build the current application, SDK, broker and bridge tests. Regenerate/check
   the SDK public API baseline and protocol native header with the repository
   generators (header still represents 68 until regeneration; no manual edit).
2. Run the focused full-trust consent/authentication/restart regression, broker
   and Game Help tests, SDK snapshot/style validation, and protocol parity.
3. Run the synthetic full-trust Game Help capture/preview/document flow and shared
   dialog/browser checks. Review setup, chat, options, capture and preview at the
   user's scaling and a narrow surface; check shortcut focus and error placement.
4. Publish a fresh coherent candidate, verify executable package admission,
   inventory and hashes, then relaunch for physical acceptance. Do not reuse the
   old sandboxed Game Help payload or claim candidate 04 includes these edits.

No merge, commit, installer or worktree cleanup is authorized by this revision.


### Candidate 06 launch (2026-10-01)

- User requested launch. Built the current Game Help application and regenerated
  the SDK public API baseline and protocol-69 native header using their generators.
  No interactive regression suite was run.
- Publication: gamehelp-release-05. Package admission caught WRSS errors in the
  redesigned stylesheet: logical text alignment requires `start`, and `--border`
  needs a fallback. Corrected both in source, validated with the published CLI,
  copied the corrected style into the publication and resealed the Game Help
  package. Exact correction provenance is in `style-correction.json`; binaries
  were unchanged. Candidate 05 is preserved as failed staging evidence.
- Candidate 06 passed release inventory, native cleanup and all 14 widget package
  admissions. All 135 frontend DLL hashes match the publication. Launched as
  PID 44168 on Game Help using the normal user profile. Read-only UIA found the
  enabled visible widget root; process responsive with no new frontend errors.
- The Game Help application is now full trust and owns Gemini/credentials. Shared
  window/capture/document services retain consent and ownership checks. This launch
  does not establish live provider access, game accuracy, or controller acceptance.


### Sensitive paste and Browser desktop input (source ready, 2026-10-01)

- Found and removed a PasswordBox.Paste handler that explicitly swallowed every
  paste. Sensitive entry retains the native hidden PasswordBox, MaxLength,
  protected automation semantics, and clearing on cancel/commit. Ctrl+V or
  Shift+Insert from the on-screen keyboard moves to the native editor and invokes
  its own PasteFromClipboard; the host never reads/logs clipboard contents.
- Interactive Browser WebView now participates in native keyboard focus. Passive
  pins and inactive views remain noninteractive. Physical arrows in the focused
  WebView bypass the host's controller/spatial navigation; native page editing
  keeps Chromium's keyboard behavior.
- Confirmed idle left-stick polling emitted a synthetic mouse move even at (0,0).
  Neutral movement now performs no dispatch or focus change. Actual controller
  input returns ownership to the existing browser slot. Physical mouse input hides
  the controller cursor, cancels its held gesture and queued movement, and retains
  normal native delivery. Default browser accelerator/context-menu restrictions
  were not broadened to unrelated downloads or privileged actions.
- Added a synthetic native regression source for real WebView keyboard focus,
  neutral polling and actual controller movement. Not executed: the user's current
  no-testing request still applies. No build, clipboard access, input injection or
  restart was performed for this patch. Candidate 06 remains the old running build.
- Pending physical checks: paste a disposable value into ordinary/sensitive editors
  (focused field and Ctrl+V from the controller keyboard), maximum length/cancel;
  browser mouse click/wheel/text selection, native typing/arrows/Tab, then controller
  pointer and B behavior in both interaction modes and passive pins.


### Automatic Browser controller keyboard (source ready, 2026-10-01)

- User requested automatic use of the existing controller keyboard after selecting
  a web text field with A. The implementation starts only after the completed
  controller mouse-up, outside the pointer semaphore. Press/hold, canceled input,
  HTML drag and pointer movement beyond the click tolerance do not open it.
- A fixed host hit-test captures the exact supported focused field under that
  click, including open shadow roots and associated labels. Ordinary clicks are
  silent; an older focused field elsewhere cannot cause the keyboard to open.
  The existing remote-object read/commit path checks field identity, focus,
  connectivity, input type and length. No new SDK or page-to-widget bridge.
- Navigation/edit/pointer generations and source focus prevent late opening after
  newer input. Temporary DOM references are released. R3 continues to reopen an
  editor; native mouse/Tab input does not invoke automatic editing. Passwords,
  rich editors and cross-origin frames retain their previous limitations.
- Added native regression source for waiting until A release, automatic commit,
  unrelated-click rejection and drag/text-selection suppression alongside the
  existing R3/cancellation/replaced-field tests. No builds, tests, clipboard reads,
  input injection or candidate restart performed under the current no-testing
  instruction. Candidate 06 remains unchanged until the next authorized update.


### Pending validation completed (2026-10-02)

User authorized native testing while away. All operations used fake providers,
owned fixture windows and local intercepted pages. No real Gemini key/request,
user-media upload, game launch or system-setting action was used.

- Game Help: 15/15 (`pending-gamehelp02.txt`), including redesigned setup/modal
  scopes and compiled WRSS. Corrected one stale test expecting the application
  dropdown on the home page instead of Add context.
- SDK: 152/152 (`pending-sdk01.txt`). SDK API baseline check: 1/1. Protocol parity:
  153 constants agree with the generated native header.
- Full-trust child process: grant, undeclared access, revocation and restart passed
  (`pending-fulltrust01.txt`). Session capture/document replacement checks: 2/2.
- Broker: 67/67 (`pending-broker02.txt`). End-to-end startup exposed a real upgrade
  defect: the removed Gemini permission in a saved consent file caused strict
  loading to reject the entire profile. Added that exact ID to the existing retired
  capability tombstones; regression preserves unrelated grant/deny decisions and
  still rejects arbitrary unknown or duplicated entries. No Gemini runtime logic
  returned to the host.
- Native text entry: 79 checks (`pending-text-entry01.json`), including ordinary and
  sensitive paste from the controller keyboard and native editor, MaxLength and
  clearing without commit. The test preserved all prior clipboard formats in memory,
  suppressed history/roaming for its synthetic content, and restored the clipboard.
- Native Browser: 61 checks (`native-browser-01/pending-input02.json`), including
  automatic A-release editing, drag suppression, stale-field rejection, native focus,
  neutral polling, pins, both A/B modes and previous browser functionality. Corrected
  a fixture-only invalid cast: WebView2 exposes Focus directly rather than inheriting
  Control.
- Direct desktop delivery: 3 checks (`native-browser-01/desktop02.json`) verified OS
  SendInput typing/Left-arrow editing, actual mouse click and page scrolling. The
  first mouse attempt was rejected by winapp's process guard on Chromium's child
  HWND. The fixture now targets its known WinUI browser slot with a centered synthetic
  button; no product input workaround was added.
- Complete styled full-trust Game Help flow: 21 checks
  (`grounding-flow01/pending-fulltrust02.json`) through real worker/capability pipes,
  confirmation, animated hide, countdown, PNG/MP4, reopen, preview, discard/send,
  original restricted HTML, links, native-view budget and New chat disposal.
- Native intents/dialogs: 26 checks (`native-intents-01/pending-final01.json`).
- Trimmed Release: PASS (`trimmed-input02/result.txt`) for existing controls, browser
  click/zoom/drag, automatic A-click and R3 text editors, native playback and restricted
  document links. The first run passed automatic editing but failed the final document
  click; the fixture had not retired the previous native browser and omitted the new
  slot's IsTabStop. Corrected fixture teardown/focus and re-ran successfully. No
  production fallback, timing sleep or input retry was added.
- Screenshot attempts `pending-chat-style01.png` and `pending-chat-style-screen01.png`
  do not show the overlay (blank window / underlying desktop); they are not visual
  qualification evidence and remain ignored artifacts. No screenshot-based appearance
  claim is made. Actual visual feel and real gameplay/provider behavior remain physical
  acceptance checks.

Publication target: gamehelp-release-06; candidate target: gamehelp-candidate-07.
No commit, merge, installer or worktree cleanup.


### Candidate 07 handoff (2026-10-02)

Fresh production publication `artifacts/game-help/gamehelp-release-06` and candidate
`artifacts/game-help/gamehelp-candidate-07` passed inventory, cleanup and all 14
widget package admissions. All 135 deployed frontend DLL hashes match publication;
there are no source/asset overrides. Launched as PID 46936 with Game Help and the
normal user profile. Read-only UIA confirms the enabled visible root; process
responsive, no new frontend error entries. Candidate contains paste, desktop input,
automatic controller keyboard, full-trust services, redesigned chat and the retired
consent migration. No commit, merge, installer or worktree cleanup performed.

### Gemini HTTP failure investigation (2026-10-02)

User reported "Gemini is unavailable" in candidate 07. The old client maps every
unclassified non-success response (including 404) to that message and discards
its status. Existing logs do not establish the actual HTTP status or cause.
Current public Interactions guide still specifies /v1beta/interactions. A no-key,
empty-body endpoint probe received 403 at v1beta and 404 at v1beta2; this only
checks routing/authentication and is not evidence of model/project compatibility.
No user key was read and no user question or capture was sent. Endpoint and model
remain unchanged pending evidence.

Added distinct status-aware errors for unavailable model/endpoint, authorization,
request rejection, oversized context, quota, timeout, server and unexpected HTTP
responses. UI includes numeric HTTP status; the application writes only UTC,
internal error code and numeric status to a single overwritten last-provider-error.json
beside its encrypted key. Provider response bodies, headers, keys, prompts and media
are never recorded; diagnostic write failure cannot replace the original error.
No automatic retry, fallback model or extra API request was added.

Validation: GameHelpWidget.Tests built cleanly; 27 passed, 0 failed, 0 skipped,
including 12 HTTP status/privacy/no-retry cases. Evidence: gemini-http-errors-01.binlog
and gemini-http-errors-01.txt under artifacts/game-help. Published only the Game Help
application for candidate 08, retaining candidate 07 host/framework binaries.
Actual incident resolution requires the user's retry with status diagnostics;
this is diagnostic preparation, not a confirmed provider fix.

Candidate 08 staging stopped because it inherited an existing package integrity
seal. The attempted removal of that generated seal was blocked by tool policy;
the incomplete candidate was left untouched. Candidate 09 was assembled into a
fresh directory, creating the Game Help package from publication before sealing.
Its release inventory, frontend cleanup, and all 14 package admissions passed.
Use gamehelp-candidate-09 (candidate 08 is not runnable qualification evidence).

Candidate 09 launched with --widget=game-help as PID 4284 using the normal profile. User retry is pending; no live Gemini request was made by the agent.

### Chat section hierarchy and confirmed provider status (2026-10-02)

User's next request failed with HTTP 503. last-provider-error.json records
2026-10-02T09:09:32.9678405+00:00, provider_unavailable, 503. This confirms a service
unavailable response, not its root cause; no key/model fallback or automatic retry
was added and no live request was made by the agent.

Based on the user's screenshot, grouped the chat into a bounded conversation panel
with a nested context bar, scrolling transcript and fixed composer. Context title,
application and capture status now share a section; starter heading/cards share a
container, card titles/descriptions share left alignment, and the composer has a
visible label. Answers group sources/attribution, optional searches and follow-ups
into nested sections. Existing action IDs, shortcut scopes, native media and focus
requests are preserved; additional containers are not focusable.

Uses semantic surface/raised-surface theme tokens. One style check caught unsupported
align-self; removed it and reran the existing suite: 27 passed, 0 failed, 0 skipped.
Evidence: chat-sections-02.binlog and chat-sections-02.txt. Candidate 10 carries only
the republished Game Help package on candidate 09's verified host/framework.

Candidate 10 passed release inventory, frontend cleanup and 14 package admissions;
launched PID 24336, Game Help worker PID 14832 reached interactive state. No new
frontend error entries. It subsequently became background/hidden, so UIA/screenshot
inspection had no visible window; no visual-verification claim is made. Physical
layout inspection remains with the user. The HTTP 503 diagnosis remains unchanged.

### Requested model change (2026-10-02)

Changed the application-owned Gemini client model from gemini-3.8-flash to
user-requested gemini-3.5-flash-lite. Model is currently a code constant, not a
LocalAppData/widget setting; the key remains encrypted in application storage.
Updated the existing outgoing-request assertion. Build and Game Help's small
suite passed (27/27); this was broader than strictly necessary for one constant.
No host/native UI tests or live authenticated requests were run. Published only
the Game Help application for candidate 11; package inventory/admission verifies
staging rather than adding product regression scope.

### Foreground capture without application selection (2026-10-02)

User superseded the earlier application picker design. Game Help now offers only
screenshot/video capture, with no stored window choice, automatic window listing,
refresh action, or window-list permission. It no longer sends a potentially stale
selected application name to Gemini. Questions and visible capture content supply
context. gemini-3.5-flash-lite remains the requested model.

Added generic SDK Capture.RequestForegroundAsync(kind): the request contains no
window ID; the broker queues it under normal capture permission/owner/lifecycle
rules. After confirmation, normal release-aware overlay hide and the existing
five-second countdown, the host reads the actual foreground root window once.
It does not activate another application. The desktop, shell and the overlay's
own process are rejected. The exact identity is retained through native recording;
replacement, loss of foreground/protection and cancellation guards remain. The
native capture engine itself is unchanged. Explicitly selected-window capture
remains available to other SDK authors and keeps its existing validation.

Host confirmation and Game Help copy explain the foreground-at-countdown-end rule.
The optional excluded countdown, silent five-second clip, reopen-to-preview,
Send/Retry/Discard, attachment ownership/limits/expiry, and no automatic reopen
remain. Added exception diagnostics for unexpected capture-run failures.

Validation:
- Game Help: 27/27, including capture with no window-list service and an assertion
  that its SDK request has no preselected WindowId (foreground-capture-gamehelp-02).
- Broker: focused --capture-only, 2/2: ownership/expiry/cleanup plus foreground
  initiation with capture permission alone, background refusal and consent denial
  (foreground-capture-broker-01).
- SDK public API baseline regenerated (3477 symbols).
- Debug WinUI build passed. Initial build required explicit existing native paths;
  a validation namespace collision was corrected; no production workaround.
- Real foreground capture fixture: foreground-flow01/result02.json, 22 checks.
  Uses a disposable external WinForms peer, synthetic chat/provider replies, and
  an isolated profile; no user capture, real key or API request. Screenshot and
  video traversed real application/bridge/broker/host IPC into native preview.
  Includes self-capture rejection, confirmation cancellation, countdown, no auto
  reopen, discard, explicit send, attribution and new-chat disposal. First run
  ended before its countdown assertion (result.json); it established no successful
  capture. Added capture-run diagnostics and the subsequent complete run passed.
- Release publication target gamehelp-release-07; candidate target gamehelp-candidate-12.
No installer, commit, merge or worktree cleanup.

Publication gamehelp-release-07 and candidate 12 passed inventory, cleanup and all
14 widget admissions. Candidate 12 launched with Game Help as PID 30592 using the
normal profile. YouTube Video 0.3.38 was already installed; CLI refused same-version
replacement (immutable versions). Re-enabled it and verified all 14 archive files
against the installed files by SHA-256, proving the exact latest branch package is
present. Evidence: youtube-installed-verification-01.json. No package version was
invented solely to force a reinstall.

### Structured replies, expandable solutions and transcript reveal (2026-10-02)

User identified that model suggestions were questions addressed to the user, but
clicking sent those same questions as user text. Replaced the ambiguous string
array with replyOptions [{label,message}], bounded to five choices. Buttons display
labels; selecting one inserts and sends its complete user message. Clarifications
belong in the visible hint, with no invented user facts or free-text placeholders.

User also requested hint/detail separation. Gemini JSON now has hint and
detailedSolution fields. Only hint renders initially; each message has a local
Show/Hide detailed solution control when detail exists. Expansion neither adds a
chat turn nor calls Gemini. Opened detail participates in later conversation
history within existing message/character budgets. Prompt requires plain text,
spoiler-light hints and separates hidden solution steps. Model remains
user-selected gemini-3.5-flash-lite; search remains off by default.

Added a generic protocol-70 WidgetView.ScrollRevealRequest(requestId,scrollId,targetId).
This gives ordinary scroll containers an explicit reveal without hijacking focus
or replacing the container to reset offsets. Requests are validated against the
same active scope/descendant ownership, deduplicated per presenter owner, wait for
native layout, respect motion settings, and cancel on manual scrolling/navigation,
hide or disposal. Modal and pinned projections clear irrelevant requests. Changed
reveal metadata emits a full atomic checkpoint, like existing focus-group requests.
Game Help requests the latest user turn on send/retry/selection, so its message and
subsequent answer appear in view. It does not jump to the end of a long answer.

Managed checks: 30/30 Game Help tests (reply-structure-03), including local expansion,
label-vs-message dispatch, reveal version/scope rules and modal suppression. SDK
baseline regenerated; native contract parity matches 154 constants. New native
checks cover collapsed/expanded solution and actual ScrollViewer position after
selecting a reply. Fixture corrected to wait for native hide completion, rather
than logical visibility at animation start; foreground activation during that
transition correctly cancels capture. Attribution checks now explicitly scroll the
lazy source view into view, since the new reveal initially shows the user turn.
Release publication target gamehelp-release-08; candidate target gamehelp-candidate-13.
No authenticated API call, user-media upload, installer, commit, merge or cleanup.

Native reply-flow01/result04.json passed all 25 checks, including inline solution
expand/collapse and actual transcript offset/target visibility after selecting a
reply. Test-only source inspection now uses the real controller scroll entry point
to cancel a pending reveal before bringing lazy attribution into view. Earlier
source-readiness timeouts did not qualify the run; result04 is the complete pass.
Fresh trimmed Release publication gamehelp-release-08 and candidate 13 passed
inventory, frontend async-cleanup verification and all 14 widget admissions.
Candidate 13 relaunched using the normal profile. No authenticated model request
was made; physical Gemini output quality remains the user's acceptance check.

### Foreground-only shared capture contract (2026-10-02)

User rejected retaining selected-window capture in the shared SDK. Removed that
mode completely. WidgetCaptureService now has only RequestAsync(kind); there is
no RequestForegroundAsync alias, window ID parameter, application name or optional
target in the request/broker-to-host record. Removed the selected-window resolver,
application-name cache and list-copy overhead it introduced. Legacy request JSON
with windowId is rejected by the broker's strict payload parser. Updated capture
permission copy, docs, SDK baseline and affected fixtures. Capture always reads
the foreground root window once at the end of the existing countdown.

Foreground handling was checked end to end: the close helper waits for release,
plays the exit motion, verifies the overlay still owns foreground, relinquishes
input, then hides its AppWindow. It does not activate or restore an application.
The non-activating countdown does not take focus. Native recording retains the
chosen foreground identity and stops if that window loses foreground, is replaced,
closed/minimized/protected, or the capture is cancelled. No Task Switcher target
activation runs as part of capture.

Checks: two focused broker capture tests passed, including legacy-payload refusal,
permissions, background admission, ownership and cleanup; one focused Game Help
capture/preview/send test passed. Debug WinUI and trimmed production publication
built cleanly; SDK API baseline regenerated. Standalone native red/green capture
attempts did not acquire foreground for the external fixture and produced zero
successful checks, so they are not claimed as capture validation. Its obsolete
selected/hidden-target assertion was removed; foreground physical test remains
with the user. No arbitrary application was captured or sent to Gemini by tests.
Release gamehelp-release-09; candidate gamehelp-candidate-14. No commit/merge/installer.

Candidate 14 passed release inventory, cleanup and all 14 package admissions; relaunched using the normal profile. Native capture physical qualification remains pending.

### Controller chat controls, media focus and Windows intent routing (2026-10-02)

User requested direct New chat access, editable capture questions, improved native
media controls, and an explicit Windows route for sign-in links. Added a visible
New chat header button and LB shortcut while retaining confirmation. Capture review
uses TextEntry for the pending question and R3 to focus it; edits replace the pending
user turn instead of adding another. Empty questions cannot be sent.

Documented current context budgets: latest 20 UI messages; outbound newest 16 entries
and 22,000 characters, with an expanded solution using another assistant entry.
No summarization or persistence was introduced.

Shared CapturedMediaView now uses a rounded themed card, flexible media area,
full-width progress track, mm:ss timestamp, and icon/text transport buttons. Play
and Replay are individually focusable with theme-colored native focus outlines;
left/right moves between them, A performs the focused action, LT/RT seek, LB replays,
and up/down/B retain outer navigation. Removed the fixed 280px child height that
competed with the toolbar for space. Compact widths hide text labels, retaining
icons and accessible names.

Added WidgetIntentRouting.Windows as a per-request override, with an additive SDK
OpenIntent overload; old overloads remain. The Bridge bypasses widget handlers and
maps only the exact standard web contract to a validated HTTP(S) Windows shell URL
launch. Unsupported/custom Windows routes report unavailable; executable schemes
remain invalid. Routing participates in displayed-action identity and protocol 71
requirements. Game Help's AI Studio key link uses Windows routing. Spotify OAuth
and YouTube account setup already use Windows shell URLs; Playnite ordinary links
use WidgetRail's normal widget-first web intent.

User's YouTube question was investigated separately: existing intent opens known
video IDs/timestamps without a search key. Game Help's YouTube search links remain
ordinary results-page URLs; no YouTube search intent or per-handler runtime
availability publication is currently implemented. Suggested search phrases are
not paid grounding; optional Google Search grounding is separate. No dynamic
availability/search feature was added without a follow-up implementation request.

Focused checks so far: Game Help 30/30 (chat-controls-tests02); intent policy 22/22
(windows-routing-tests01); Bridge intent boundary 11/11 including Windows override
with an installed browser handler (windows-routing-bridge01). Protocol parity 155
constants; SDK API baseline regenerated. Native playback/focus fixture uses existing
synthetic PNG/MP4 files, never live capture or provider credentials.

Native player validation passed 14 checks using only existing synthetic PNG/MP4
(chat-controls-native01/result.json): native theme-focus eligibility, actual child
focus and left/right navigation, focused A action, play/pause/seek/replay, flexible
timeline layout, background refusal, stale-load rejection, authority revocation
and disposal. Fixed compiler-detected reference-comparison warnings and marked the
new validation Page partial for WinRT trimming; no production workaround.

The later-capture edit case now creates a new pending user turn when the previous
question already has an answer, preserving prior history. The updated regression
and all 30 Game Help checks pass (chat-controls-tests03). Release 10 predates that
last application fix; final publication is gamehelp-release-11 and candidate 15.
The media styling's disabled label color follows the native button template.
No real OAuth flow, Gemini request, sign-in credential entry or live media capture
was exercised. Windows routing was verified through policy/Bridge boundaries with
synthetic HTTPS URLs, without actually launching account pages.

### Next: YouTube search, per-handler readiness and sender feedback (2026-10-02)

Authorized next implementation. Candidate 15 is launched as PID 46792 with the
previous UI/player/Windows-route improvements; preserve it while implementing.

Design:
- Add standard widgetrail.video.search v1 {provider,query}; YouTube handles search
  separately from known-video playback. Search requires configured API access;
  direct playback remains available without a search key. Search opens Discover
  with the incoming query and reuses the normal indexed search pipeline.
- Handlers can opt into a bounded runtime availability callback per mapping.
  Query a cold worker in Background, without activating its window; YouTube reads
  its own configuration. Recheck on the exact receiver immediately before delivery.
  Availability checks are read-only and grant no capability or input authority.
- Add optional per-request sender feedback (accepted, unavailable, rejected,
  cancelled, failed) through a bounded runtime lane, correlated to the original
  request/worker. Source can be in Background after host navigation.
- A sender may declare one fallback intent with its displayed primary request.
  The sender feedback callback can choose that exact fallback for unavailable or
  rejected outcomes only. No recursive fallback and no automatic retry/fallback
  for uncertain delivery failures or cancellation. Manifest/schema/current-worker
  authority is revalidated before a follow-up is admitted.
- Game Help declares search plus its existing YouTube results-page Web intent as
  fallback, and opts in only on definitive unavailable/rejected outcomes. Normal
  widget-first web routing (including passive browser pins) remains the fallback.
- Verify missing/configured key paths with fake application services, cold-worker
  readiness, key-state changes between preparation and delivery, single feedback,
  one fallback, cancellation/replacement and no double delivery. Do not use real
  keys or execute a live YouTube search during automated tests.

Implementation update (2026-10-02):
- Implemented VideoSearch, per-mapping HasDynamicAvailability, bounded worker
  readiness/feedback callbacks, exact-source result delivery, and one declared
  sender-selected fallback. Protocol 72; native constants/API baseline updated.
- Source gates are released before probing destinations; callbacks and fallback
  resolution never hold two worker operation gates. Cold readiness runs in
  Background and exact receiver readiness is checked again before delivery.
- Game Help now declares YouTube search with a normal Web results-page fallback.
  YouTube Video 0.3.39 accepts configured search into Discover; direct video
  playback retains its existing key-independent path.
- Focused checks passed: 16/16 Bridge intent scenarios (including both-pipe cold
  readiness and source-background feedback), 23/23 intent policy/authority checks,
  6/6 YouTube intent tests, 1/1 Game Help search/fallback test. No authenticated API
  calls or user media were used. Candidate publication and physical acceptance pending.
- Published `artifacts/game-help/gamehelp-release-12`; native/managed release
  build, inventory, frontend cleanup, and all 14 catalog package admissions passed.
  Staged candidate 16 and launched it with `--widget=game-help` (PID 1440).
- Installed, selected and enabled YouTube Video 0.3.39 through the published CLI;
  all 15 staged package files match the installed payload. Evidence:
  `artifacts/game-help/youtube-search-installed-verification.json`.
- The candidate, Bridge and Game Help application are running. User testing of
  configured search and results-page fallback remains pending. No commit/merge,
  installer, profile reset or authenticated provider call was performed.

### Browser toolbar and local library (2026-10-02)

- Removed hold-LS precision movement. LS switches page/toolbar focus; toolbar
  left/right selects commands, and Down/LS resumes page interaction. Existing B
  routing remains intact. Mouse/keyboard access uses the same native controls.
- Added host-owned bookmark toggle, Bookmarks and History lists to the existing
  native title/address bar. Native themed dialogs support controller selection,
  return focus, and confirmation before clearing history. Saved URLs/titles never
  cross widget IPC. Provider-attribution content is excluded.
- Added local atomic, serialized persistence, 100 bookmark/200 recent-page bounds,
  duplicate visit promotion and coalescing for rapid page URL changes. Browser
  begins on a blank page (protocol 73), so saved pages are accessible immediately.
- Passed 3 storage tests, 2 Browser widget tests and 66 native browser checks.
  Evidence: `artifacts/game-help/browser-library-native01/result01.json`.
  Final coalescing revision, visual review and candidate 17 staging in progress.
- Follow-up native run exposed an exact toolbar return-focus race: a delayed
  GotFocus event could remember the neighboring bookmark-toggle button. Dialogs
  now capture their invoking control, toolbar navigation updates memory directly,
  and stale GotFocus events are ignored. Focused rerun pending.
- Final targeted native run passed all 13 toolbar/address/bookmark/history checks;
  native clear-history cancel/confirm checks also passed, preserving bookmarks.
  Evidence: `browser-library-native01/result05.json`, `history-actions05.json` and
  visually reviewed `history05.png`. The short list now sizes to its contents.
- Candidate 17 is staged from services publication 14 and the final02 trimmed
  frontend publication. Inventory, frontend cleanup and 14 package admissions
  passed. Launched with `--widget=browser` using the normal profile. Browser sample
  is 0.1.3; installed YouTube remains 0.3.39. No installer or commit created.

### Captured application name (2026-10-02)

- Confirmed Game Help always sent "Unknown application" despite existing provider
  request fields. Added optional bounded SourceApplication metadata to captures.
- Native capture keeps its exact target identity in the managed result (native
  layout unchanged). Bridge resolves only that identity through the same provider
  used for Task Switcher subtitles, with a bounded non-fatal lookup. Metadata stays
  attached across preview, retries and follow-ups; no foreground/window selection
  or activation behavior changed. Preview displays the captured application name.
- Game Help and fake-HTTP request checks pass. Exact-identity, stale/ambiguous
  target, attachment ownership and protocol checks are being completed. No real
  Gemini request or user-media capture is used for validation.
- Validation completed: 3 capture broker tests, 2 Game Help/fake-HTTP tests and
  1 host-pipe metadata test passed. The pipe test covers provider-label resolution
  and successful media with absent metadata for a replaced target. Protocol parity
  passes with 158 constants; Release/native builds passed. No real provider request.
- Candidate 18 staged from `gamehelp-release-15`; inventory, frontend cleanup and
  all 14 catalog package admissions passed. New captures carry metadata; existing
  metadata-free attachments are not assigned a name from a later foreground app.
- Candidate 18 launched in Game Help using the normal profile; physical capture/send verification remains with the user.

### General native media element (2026-10-02)

- Added `UI.MediaPlayer`, typed capture/package/HTTP(S)/full-trust-local sources and
  bounded initial playback options (protocol 75). Source resolution stays in the
  host with exact worker/declaration checks and sealed package validation.
- Game Help now uses this general element. Legacy captured-media declarations map
  to the same renamed MediaPlayerView; no separate capture playback renderer.
- Shared timeline, play/pause/replay, loop, rate, mute/volume and expanded view
  retain native focus and reuse the player during expansion. X/Y remain widget
  actions. Native source initialization resets rate, so it is applied on MediaOpened.
- Source/authority SDK and Bridge checks pass, including real worker/host pipes
  and pinned projection resolution. Native synthetic playback passed 25 checks;
  a final pending-options/active-expansion regression and publication are in progress.
- Arbitrary local paths are full-trust-only; sandboxed widgets can use all other
  source kinds. A future explicit picker/file grant can support sandboxed local
  files without relaxing filesystem isolation. No arbitrary file grant was added.

- Final native run passed 31 checks, including active playback through expansion,
  option changes during pending loads, inactive decoder release and position
  restore, four-player residency/recovery, revocation and disposal. Reviewed
  inline/expanded synthetic playback screenshots. The native view now has no
  capture-specific playback path; its capture adapter lives only in validation.
- SDK contract/update tests, three Bridge source/authority/pipe tests, two Session
  authority tests and Game Help capture-preview regression passed. Public API
  baseline updated to 3493 symbols; native contract parity has 159 constants.
- Release publication 16 and candidate 19 staging completed; inventory, frontend
  cleanup and all 14 package admissions passed. Candidate 19 launched in Game Help
  using the normal profile (frontend PID 48292, Bridge PID 45708), with no matching
  frontend errors in the recent log. Physical media-player acceptance is pending.
  Launch evidence: `artifacts/game-help/gamehelp-candidate19-launch.json`.

### Grounded-request diagnostics and saved preferences (2026-10-02)

- User reported generic request failures after enabling Google Search, followed
  by widget restarts. Candidate 19 logs confirm worker exits; the last HTTP error
  was an older 429. No current evidence establishes a timeout or HTML-renderer fault.
- Game Help now records request ID, stage, elapsed time, grounding flag, safe error
  code/status and exception/method identities. No keys, prompts, captures, URLs,
  provider bodies or exception messages are logged. Last-request state distinguishes
  a newer successful request from an older failure; stages are written before work
  so interrupted requests remain diagnosable. Logging failure cannot hide the cause.
- Unknown answer-processing failures receive a code/reference instead of only the
  generic heading. Expected cancellations stay cancellations; no automatic retries
  or paid Gemini calls were added or performed.
- Persist Google Search and spoiler preferences atomically in application settings.
  A recreated widget and new chat retain preferences. Malformed settings use defaults.
- All 35 Game Help tests pass, including synthetic timeout/parse failures, diagnostic
  privacy, write-failure handling and preference restoration. Published only the
  changed Game Help application, staged fresh candidate 21 from candidate 19, sealed
  its package and passed release inventory plus 14 package admissions. Candidate 19
  closed through normal cleanup and candidate 21 launched. Live reproduction needed
  to diagnose the still-unconfirmed request failure and worker restart.

### Multiline attribution transport correction (2026-10-02)

- User reproduction fa4f2622 identifies a local BrokerPipeClient timeout during
  search-attribution creation after Gemini responded (total request elapsed 9.156s).
- A real named-pipe regression with CR/LF/tab in synthetic HTML reproduced the same
  timeout: raw document strings failed the broker frame's control-character guard.
  The retired capability channel also explains subsequent worker lifecycle failure.
- Restricted-document serialization now sends bounded base64 UTF-8 content; the
  document service decodes the exact original HTML. No broker-wide control-character
  guard, timeout, permission, ownership, or HTML execution policy was relaxed.
  Earlier single-line raw documents remain readable. Decoded bytes are capped at
  64 KiB, ensuring encoded requests fit the existing broker envelope.
- Tests cover exact Unicode/whitespace round-trip, interactive/background requests,
  invalid encoding/UTF-8/control characters, maximum payload, legacy input, and
  unchanged metadata rejection. Three broker document tests, one real full-trust
  worker consent/restart test, and all 35 Game Help tests pass. No real Gemini call.
- Publishing corrected framework and Game Help together; live acceptance pending.
- Release publication `gamehelp-release-17-attribution` completed. Candidate 22
  passed inventory, published frontend cleanup and all 14 package admissions.
  Candidate 21 shut down normally; candidate 22 launched in Game Help (PID 46368)
  with Bridge and application worker running and no startup errors for that PID.
  Ready for the user's paid-key grounded-request reproduction; no API request was
  made during validation.

### Compact chat and reusable documents (2026-10-02)

- User chose to retain live search after review of Google's requirements. Search
  chips stay visible and retain supplied markup; source buttons use a compact
  responsive grid. Title/context/New chat/Options share a header, duplicate context
  instructions and composer label are removed, and shortcuts remain in the guide
  without a bottom hint row. Existing preferred-pinned intent routing is explicit
  and tested for direct YouTube URLs, web URLs and preserved opaque redirects.
- Shared provider documents measure native DOM box geometry to size an auto-height
  viewport after navigation/width changes. This does not enable page scripts or a
  widget JS bridge. Authored fixed heights and maximum bounds remain authoritative.
- Browser and provider-document containers now have theme-owned focus outlines.
  Per the user's correction, these appear only before entering interaction, hide
  during interaction and return on leaving interaction while retaining focus.
- Playnite description HTML normalization was widget-owned (PlayniteDescriptionText),
  not SDK/host sanitation. Detail endpoint reads now retain bounded HTML for a host
  document; catalog rows remain lightweight and text conversion remains a fallback
  and for notes. Late/closed detail results are discarded; refresh/close releases
  document references. The host retains script/frame/network/permission restrictions.
- Playnite 0.2.116 was packed, installed and selected through the CLI. Its new
  presentation.documents.v1 grant was added through ConsentStore only when absent;
  existing permission choices were preserved. Remote description images remain
  blocked by the restricted document contract.
- Verified 36 Game Help tests, 22 focused Playnite description/details tests, one
  parser test and nine native document sizing/focus checks. No paid Gemini calls
  or real captures. Final native evidence: provider-doc-native/result03.json.
  Browser and documents share one environment with per-widget private profiles;
  embedded media uses its other shared environment. Renderer process allocation
  remains owned by WebView2, not one process tree per document.
- Candidate 23 staged from release 18 with the final focus-policy frontend, passed
  publication cleanup, inventory and 14 package admissions, and launched in Game
  Help. Playnite 0.2.116 is installed/selected/enabled. Physical acceptance pending.

### Provider startup and Playnite source inspection (2026-10-02)

- Live read-only Playnite Bridge checks returned actual markup, not stripped text:
  three installed detail responses took 2-4ms. 007 First Light returned 3,140
  characters with inline CSS, 20 paragraphs and eight images. Auth token stayed
  in memory and was never emitted. Evidence: playnite-live-description-summary.json.
- Playnite's document wrapper explicitly hides remote images and the shared host
  blocks their requests. This accounts for much of the missing visual content;
  no network/image authority was expanded in this correction.
- Confirmed provider toolbar existed until WebView initialization completed.
  It is now collapsed at construction and kept collapsed on chrome updates, while
  the independent progress animation remains. Native checks pass before and after
  initialization, plus the existing sizing/focus checks (11 checks total).
- Synthetic local-document timing: environment 14.1ms, controller creation
  380.9ms, navigation 50.9ms. This isolates a significant native startup cost but
  is not proof of the complete reported Playnite delay. Added bounded safe
  provider-resolve/provider-startup records with stage timing and widget ID only.
  No controller retention/cache policy was changed; performance work remains
  pending the actual-page timings. Evidence: provider-doc-native/startup01.json.
- Actual candidate 24 Playnite timings were materially slower than the synthetic
  fresh-profile run: document resolution 2-24ms, controller initialization 5.24-5.41s,
  HTML navigation 60-66ms. The source response and broker resolution are not the
  dominant delay in these reproductions.
- Controlled same-profile comparison: a new native document while its browser
  process was already running took 128ms for controller creation; after closing
  all browser consumers and confirming process exit, the same profile took 5.296s.
  Evidence: provider-doc-native/profile-live01.json and profile-cold01.json.
  Profile startup/controller recreation is the remaining performance issue;
  bounded controller reuse has not been implemented and no browser profile was
  cleared or security/network policy relaxed. Candidate 24 relaunched in Playnite
  after both owned diagnostic windows shut down normally.

### Playnite document experiment reverted (2026-10-02)

- User rejected the provider-document presentation for Playnite due to equivalent
  appearance and worse performance. Restored the eight exact Playnite source/test
  files to their pre-experiment versions and removed the added HTML document helper.
- Selected and re-enabled installed Playnite 0.2.115 via CLI. Removed only the
  document-display consent grant added for this experiment, preserving all other
  permissions and installed widget versions. The 0.2.116 payload is inactive.
- Shared provider document sizing, focus, toolbar correction and Game Help changes
  remain. The live WebView startup evidence is retained; no retention-policy change
  was introduced as part of this rollback.
- Rollback verification: all 20 existing Playnite description/details tests passed; candidate 24 relaunched with the previous Playnite version. No remaining Playnite experiment source/test diff.

### Provider document sizing through the presenter (2026-10-02)

- User screenshot exposed a missed production path: the presenter still assigned
  an implicit 180px height to provider documents, disabling the content-height
  measurement. Removed that fallback; ordinary intrinsic sizing and explicit
  authored heights remain authoritative.
- Reworked the native document fixture to render a validated snapshot through
  WidgetViewPresenter with computed styles. It failed on the old implicit height
  and passes all 13 checks after the correction, including width wrapping,
  fixed-height removal, disabled page scripts, startup chrome, and focus outlines.
  Evidence: provider-doc-native/presenter-red.json and presenter-green.json.
- Automated screenshot verification was unavailable because Windows declined
  foreground activation of the fixture. Native geometry/focus assertions passed;
  no real Gemini request or game capture was performed.
- Source-count review: the responsive grid uses at most three columns, not three
  links. Current parser retains eight distinct source links after inspecting up
  to 20 annotations per text part; provider search-suggestion HTML is separate.
  Those application limits are not a terms-based allowance to omit citations.
  Reviewed https://ai.google.dev/gemini-api/terms and grounding documentation;
  keep associated search suggestions visible. Source-limit policy remains open
  and unchanged by this layout correction.
- Candidate 25 published and relaunched in Game Help with the normal profile. Release verification passed async cleanup and all 14 package admissions; Playnite remains on 0.2.115.

### Citation associations and collapsible sources (2026-10-02, in progress)

- User requested an expandable numbered source list and matching links beside
  supported statements. Google Search suggestions stay outside the collapsed list.
- The Interactions API reference describes citation indices as byte offsets into
  response text. Mapping must handle UTF-8, JSON escapes, and displayed fields;
  do not infer associations from source titles or fabricate missing ranges.
- User requested a real response inspection before completing this work. Added
  temporary, explicitly armed tracing for exactly one grounded request, expiring
  after at most 24 hours. Captures request body with media data omitted, exact
  bounded response bytes before parsing, and HTTP status. Credentials and HTTP
  headers are excluded. Ordinary diagnostics remain content-free.
- All 38 Game Help tests passed, including one-shot/expiry, exact response capture,
  media/key exclusion, malformed response, and bounded error-body checks. No real
  provider request was made by the agent. Candidate 26 is being prepared for the
  user's question; citation UI implementation awaits that response evidence.
- Candidate 26 is sealed and relaunched with the one-request trace armed for 12 hours. All 14 package admissions passed. Awaiting the user-generated grounded response before finishing citation mapping and collapsible Sources.

### Citation rendering completed (2026-10-02)

- Inspected the user's opted-in response: HTTP 200, complete structured JSON,
  eight URL annotations across seven detailed-solution passages and two sources.
  The hint has no citation association. Byte ranges match the original JSON;
  the first passage is supported by both sources. No further API call was made.
- Saved readable local copies under artifacts/game-help/grounding-response-inspection-20261002:
  gemini-response.json includes metadata; decoded-answer.json shows answer fields.
  Original trace remains in the user's local profile. Removed temporary tracing
  code and its hooks after inspection; routine diagnostics remain content-free.
- Preserved decoded field-specific citation ranges through UTF-8 and JSON escape
  mapping, including trimming offsets, repeated text and array index identity.
  Kept source numbers stable by exact URL. Removed the eight-source/twenty-annotation
  silent truncation; bounded excessive metadata now rejects the response instead.
  Missing/invalid ranges retain aggregate sources without inventing inline support.
- Sources now defaults collapsed behind Show sources (N), independently of detailed
  solution. Google Search suggestions remain outside the collapsed section.
- Per the user's final choice, citations are focusable native numbered buttons beside
  their supported passages. They reuse existing source intents and prefer an existing
  pinned surface. Matching numbers label the aggregate list. No new host input path
  or WebView was introduced. No wording is replaced when citation controls are added.
- Verification: 43 Game Help tests passed. Offline replay of the actual saved response
  mapped all eight associations and exported real widget snapshots/production styles.
  Native presenter validation passed 24 checks at widths 560 and 1040, including
  controller activation, no horizontal overflow, stable collapse focus, and independent
  inline links. DPI-correct screenshot inspected. Evidence: citation-native-fixtures/
  native-result.json, native-final.png, and mapped-citations.json.
- Candidate 27 is sealed and relaunched in Game Help. All 14 package admission checks passed. No installer, commit, merge, or worktree cleanup was performed.

### Reusable native rich text and true inline citations (2026-10-02)

- Added protocol-76 RichText paragraphs plus SDK UI.RichText and UI.InlineLink.
  Paragraphs contain plain text spans and individually identified intent links.
  The shared renderer uses native RichTextBlock runs and InlineUIContainer-hosted
  HyperlinkButtons, without HTML, a WebView, or bypassing intent authorization.
- Game Help now places clickable superscript numbers in the paragraph's normal
  wrapping flow. Multiple references have spacing so sources 1 and 2 are not
  mistaken for source 12. Aggregate sources remain independently collapsible.
- Native layout validation found and corrected ordinary button minimum-height
  projection and generic Grid gap tracks being applied to inline text. Inline
  boxes now use native text sizing, including after responsive width changes.
- Retained native runs skip unchanged text/property writes and redundant clears;
  this avoids paragraph rebuilds that previously disturbed focus. Inline links
  report native FocusState changes directly to host focus memory/controller hints,
  and use the existing keyboard/controller handlers and action authority checks.
- Text span style projections inherit the paragraph's text-scale scope and track
  changes to text/typography/brushes; subscriptions and parent aliases are released
  when spans are detached. Native text parents are handled by text-scale traversal.
- Verification: 43 Game Help checks, 3 SDK contract checks plus the public API baseline,
  and 49 native checks passed. Native checks replay the saved response offline and
  cover 560/1040 widths, inline sizing, text scaling, controller Right/A, retained
  control identity/focus, remembered focus, collapsed sources, and retired commands.
  Visual evidence: richtext-native-fixtures-final/inline.png. Final results:
  richtext-native-fixtures-final2/native-result.json. No additional paid API request.
- SDK authoring contract: docs/reference/widget-rich-text.md. Updated PublicApi.txt.
  Release publication is in progress; no installer or integration commit requested.
- Candidate 28 published from gamehelp-release-19-richtext and relaunched in Game Help. Full release build succeeded; async cleanup and all 14 package admissions passed. Final native check count is 49, including inline focus memory. No installer, commit, merge, or worktree cleanup performed.

### Controller guide readability (2026-10-02)

- Added a shared rounded guide backdrop, using the active shell surface color at
  216/255 opacity with a subtle themed outline. High contrast and reduced
  transparency use an opaque fill. Empty/hidden guides hide the backdrop.
- Backdrop is passive and accessibility-raw. It adds no padding or focus target,
  preserving guide fitting, geometry, pointer actions, and controller ownership.
  One background remains steady while the two hint banks crossfade.
- Existing native shell chrome suite passed all 69 checks, including themes,
  guide fitting, stable height, pointer activation, and crossfade behavior.
  Evidence: artifacts/game-help/guide-backdrop-native-result.json.
- Candidate 29 is sealed and relaunched. Published async cleanup and all 14 package admission checks passed; only the frontend changed from candidate 28.

### Replace an existing pin from another widget (2026-10-02)

- Pinnable widgets now put **Replace pin** first in the shared tray/radial context
  menu when a different widget is pinned. One available layout replaces directly;
  multiple layouts use the existing themed, controller-accessible host chooser.
- Current pin controls remain available. The same pinned widget and widgets without
  a pin representation do not offer replacement. Unpinned menu ordering is unchanged.
- Cancellation leaves the current pin live. Choices are rechecked after the dialog;
  changed selection, visibility session, widget owner, or pin intent retires the request.
  Both media and ordinary pin transitions guard the expected pin under their existing
  transition lock. Ordinary layouts resolve before removing the previous pin.
- Host-choice dismissal now respects tray input ownership instead of enabling the
  underlying widget after a tray-originated choice.
- Native validation passed 37 checks, covering direct/authored replacement, cancel,
  stale requests, menu eligibility/order, tray-mode restoration, and existing intent
  handoffs. Evidence: artifacts/game-help/replace-pin-fixture/result-final.json.
  Embedded media uses the shared menu and existing compact-media cleanup path;
  actual video replacement remains a physical acceptance check.
- Media eligibility also accepts an explicitly owned compact pin being replaced, so an existing video pin does not hide another ready media widget replacement option. Native placement still requires old compact ownership to be released first.
- Candidate 31 published and relaunched with the normal profile. Release build, published async cleanup, and all 14 package admissions passed. Candidate 30 was staged but never launched; 31 includes the final media eligibility correction.

### Tray menu pin ordering and shortcut hints (2026-10-02)

- Pin commands now appear as Replace pin (when available), Unpin, Move / resize,
  Change pinned opacity, then Interact. Existing pin replacement authority and
  choice handling are unchanged.
- Menu commands show the existing controller glyph for pin interaction (View),
  reorder (Y), restart (Y with Hold), and available widget quick actions. Hints use
  active Xbox/PlayStation prompts and accessible tray-shortcut descriptions.
  Reserved or shadowed widget shortcuts do not get misleading hints.
- Uses native MenuFlyout icon/accelerator presentation and existing menu input;
  these describe shortcuts from the tray. No new shortcut mappings were introduced.
- Debug build and 41 native checks passed, including opening the menu, pin command
  order, controller glyphs, hold hint, existing replacement cancellation/authority,
  and intent handoffs. Evidence: artifacts/game-help/replace-pin-fixture/tray-hints-result.json.
- Candidate 32 sealed and relaunched with the normal profile; Release build, published async cleanup, and all 14 package admissions passed.

### Browser address/search entry and guide labels (2026-10-02)

- The shared browser element accepts HTTP(S) URLs, bare domains/IP/localhost
  addresses (HTTPS by default), and plain text as an escaped Google search query.
  Empty submissions do nothing; unsupported explicit protocols and oversized URLs
  are rejected. Widget-authored navigation still uses the existing URL contract.
- A new page opens the address editor with an empty value instead of about:blank.
  The toolbar shows a search/address placeholder; welcome and editor copy explain
  both input modes. Editing an existing page retains its current URL.
- RB guide/toolbar label is Forward; LB remains Previous, with B behavior unchanged.
- All 22 focused BrowserAddressInput tests passed. This frontend-only change does
  not require republishing the Browser widget or making a provider API request.
- Candidate 33 sealed and relaunched into Browser with the normal profile. Release build, published async cleanup, and all 14 package admissions passed.

### SDK Gallery refresh and guide fill correction (2026-10-02)

- SDK Gallery 0.1.26 adds Text and Web & media pages, rich text/inline intents,
  ordinary/sensitive entry, expandable content, scroll reveal and passive text
  scrolling, SettingsField, direct/activation sliders, busy/disabled examples,
  browser interaction modes, inline/modal native media playback, provider documents,
  capture previews, intent results/fallbacks/Windows routing, and authored/full pins.
- Bundled player sample is offline and paused by default. Web/document/capture demos
  are explicit selections; document/capture permissions are optional. Mocked service
  tests make no real capture or external provider requests. README maps examples to
  current APIs and describes the service, privacy, and source boundaries.
- Gallery Release build and all 13 test groups pass. Community package validation
  and packing passed: artifacts/game-help/sdk-gallery-0.1.26/widgetrail.samples.sdk-gallery-0.1.26.wrwidget.
- Guide correction supersedes the earlier full-row backdrop: removed the shared
  Border and apply 216/255 themed fill to each button instead. Space between and
  around buttons stays transparent. High contrast/reduced transparency stay opaque.
- Candidate 34 sealed and relaunched into SDK Gallery. Published async cleanup and all 14 package admissions passed. Existing candidate payloads remain intact.

### Scroll reveal for newly inserted native text (2026-10-02)

- Reproduced the gallery append failure in a native presenter using exported SDK
  Gallery snapshots. The new TextBlock had measured size, a current scope, and the
  correct visual ScrollViewer ancestor, but IsLoaded stayed false. The request
  remained pending indefinitely, leaving the viewport offset unchanged.
- Scroll reveal now requires the target to share the live viewport's XamlRoot,
  with valid measured size and the existing nearest-scroll, binding, and scope
  checks. It no longer uses the unreliable TextBlock IsLoaded flag as readiness.
- Native regression passed six checks: two appends reveal the new text, preserve
  append-button focus, and do not replay a consumed request on later snapshots.
  Evidence: artifacts/game-help/gallery-scroll-fixtures/native-result.json (before)
  and gallery-scroll-fixtures-fixed/native-result.json (after).
- Added a repeatable fixture exporter and native test entry point. Diagnostics are
  validation-only; no production logging, widget-specific workaround, or SDK change.
- Candidate 35 sealed and relaunched with the normal profile. Release build, async cleanup, and all 14 package admission checks passed. Gallery remains 0.1.26 because the correction is in the shared host.


### Accepted closeout and release (2026-10-03)

- User accepted all accumulated changes and authorized local main integration and
  both standard Inno installers. No push, remote release, installation, or worktree
  deletion is part of this closeout.
- App release advances to preview.24 (Windows file version 0.1.0.23); SDK/CLI
  advances to additive 0.4.1-dev with no API removals relative to the main baseline.
- Final Release checks passed: SDK 153, SDK compatibility 17, YouTube 61, runtime
  116, presentation session 320, broker 70, Game Help 43, Browser 2, intents 23,
  SDK Gallery 13, first-party conformance 6, WinUI shell 187, documentation (202
  Markdown files), and managed/native contract parity (protocol 76).
- Native policy checks and trimmed WinUI publication passed. Bridge integration:
  200/201 passed initially; the sole failure was a stale test path expecting the
  Settings worker's retired windows-specific TFM. Correcting it to the actual
  net8.0 target and rerunning that exact packaged-Spotify case passed 1/1 against
  the same sealed installation. No production runtime change was needed.
- Closeout repaired stale release/verification self-test expectations, connected
  the new suites to the standard runner, added SDK documentation mappings, and
  regenerated the native protocol header from the version-76 managed contract.
- Evidence: artifacts/game-help/final-closeout/ (per-step logs, binlogs, JUnit and
  run manifests), final-native-closeout.log, closeout-spotify-runtime.log.
  Installer and release inventories/checksums are written by the standard build
  scripts from the clean committed source. Installation itself remains user-owned.
