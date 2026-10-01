# Game Help: prerequisites, decisions, and implementation progress

Status: implementation started; no new user-facing feature is enabled yet.
Updated: 2026-10-01. Branch: `codex/game-help-foundation`.
Baseline: local main `8ec3e93d`. The separately running preview.23 and its
unmerged control-consistency changes are not modified by this work.

This is the working plan and progress record. Update it with each implementation
slice: code delivered, evidence, limitations, and the next concrete step. Checked
items mean implemented and verified in their stated scope, not physical acceptance.

## Accepted product decisions

- Build shared prerequisites first; implement AI Game Help last.
- Use Task Switcher's existing ordered windows, excluding WidgetRail; default to
  the first item. Offer the same windows in a correction dropdown. Do not add a
  second foreground-history mechanism. Latch and revalidate the chosen target
  for each capture so reordering cannot redirect it.
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
- No microphone or system audio in recordings. No automatic overlay reopening.
- Preview capture inside the requesting widget, using a shared modal with
  Send/Retry/Discard; do not launch another widget to preview an attachment.
- Native local media is a reusable inline SDK element, not modal-only. The preview
  modal composes that element; widget authors can also place it directly in ordinary
  or pinned layouts with the same host-owned playback and attachment lifetime.

## Recording flow and copy

Title: **Record game context?**

> WidgetRail will hide so you can return to **{application name}**.
>
> Recording starts after a 5-second countdown and lasts 5 seconds. No audio will
> be recorded.
>
> Reopen WidgetRail when finished to preview and send the recording.

Actions: Proceed / Cancel. Use the existing themed controller dialog conventions.

After the overlay is hidden, a nonactivating, click-through countdown shows
"Recording starts in 5...". Check the intended target is foreground and producing
frames when the countdown ends; otherwise fail with a retryable explanation.
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
| Validated application context and ordered-window selection | Inferring game/location and helpful questions |
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
- Playnite currently uses its full-trust companion's `Process.Start` for verified
  game links. Migrate that consumer to the shared web intent; preserve its link
  validation. YouTube consumes the standard video contract including timestamps.

### Normal navigation and B (revised user decision)

An intent opens its destination as an ordinary widget. Do not create a return-to-
sender stack or remap B. Existing local handlers retain first refusal (dialogs,
editing, page interaction); unhandled B follows the existing tray behavior.
Users may choose another widget or pin the destination for use while gaming.
The sender retains ordinary widget state when users return manually. Existing
pinned/fullscreen exit behavior retains priority. Consume the entire gesture
across transitions. Browser page history has its own Back control; it must not
trap B indefinitely. This supersedes the original return-destination proposal.

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
- [ ] Authenticated Bridge transport, admission, cancellation and correlated delivery.
- [x] Declarative SDK intent actions on buttons/action surfaces and versioned snapshot/update transport.
- [x] Worker receiver API and exact-process delivery with cancellation, duplicate rejection and lifetime checks.
- [ ] Wire SDK actions and receiver transport into Bridge admission and dispatch.
- [ ] Frontend activation with ordinary B/tray/pinning behavior and gesture handling.
- [ ] Fake sender/handler end-to-end tests and candidate for physical acceptance.

### 2. Browser and link consumers
- [ ] Lazy host-owned web surface with controller cursor/scroll and toolbar.
- [ ] Pinnable browser widget; normal/pinned focus qualification.
- [ ] Default-browser fallback through the standard intent.
- [ ] Playnite link migration; YouTube handler with optional start time.

### 3. Application context and capture
- [ ] Reuse ordered Task Switcher windows and add correction dropdown.
- [ ] Bounded screenshot capture and attachment ownership.
- [ ] Five-second preparation countdown, five-second video capture, confirmation.
- [ ] Hidden-overlay lifetime, cancellation, target loss and resource cleanup tests.

### 4. Native media element and attachment preview
- [ ] Reusable inline native media SDK element, ordinary/pinned placement and controller controls.
- [ ] Preview modal composing the same element with Send/Retry/Discard.
- [ ] Attachment expiry, focus restoration and cleanup validation.

### 5. Gemini foundation
- [ ] Compatibility probe (image/video/schema/grounding/free-tier constraints).
- [ ] Protected per-user key setup and dedicated request service.
- [ ] Cancellation, rate limits, uploads/cleanup and response validation.

### 6. Game Help
- [ ] Native chat, in-message suggestions and composer.
- [ ] Context inference, up-to-five clarification choices, spoiler preference.
- [ ] Preview/send workflow, grounded source cards and intent actions.
- [ ] Complete real-game workflow and failure/retry acceptance.

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
- Current boundary: contracts, authoring and receiver transport exist; Bridge
  request admission/dispatch and frontend intent activation are still pending.
  Browser, capture recording, native media element/preview modal, Gemini integration
  and Game Help widget are not enabled. Existing candidate untouched.
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

## Validation and delivery policy

Use focused synthetic checks for contracts, malformed payloads, stale lifetimes,
duplicate delivery, cancellation, fallback and existing focus behavior. Physical game input,
exclusive fullscreen and capture behavior are user acceptance checks. Do not alter
system settings or start real playback as part of automated checks. Preserve the
existing candidate while it is under test; coordinate restart for visible changes.
Launch a candidate when a runnable user-facing slice is ready; no installer unless
requested. Keep worktrees until explicitly asked to remove them.
