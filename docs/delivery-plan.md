# WidgetRail transition — Delivery Plan

Status: active implementation authority

The complete delivery record through the DLV-277 assignment is preserved in the\n[2026-08-20 15:40 snapshot](history/delivery-plan/2026-08-20T15-40-55-07-00.md).\nEarlier snapshots remain under `docs/history/delivery-plan/`. Snapshots are\nevidence only; this file is the sole authority for current work.

## Current baseline and active task map

- Accepted production/test integration baseline on main is `c21ad02`. It
  contains accepted DLV-265 package production through `4aca229` and focused
  evidence `bb8234f`, plus accepted DLV-277 host production `144ede8` and
  focused evidence `c21ad02`.
- Exact DLV-277 production candidate PID 34764 is visibly running from clean,
  physically accepted commit `830364d` over main `ce13a3c`. `OverlayHost.exe`
  SHA-256 is
  `5EAEB6240C89A6DF8BE4A203340A683118394811955C9368D0C031DAB16D00FA`;
  `OverlayPlatformInterop.dll` is
  `089029C61D87F9837BA4E4E15908C411FCFF8E9B2A28707601FABD1928A91DEE`.
  The provider-free Full Application sample remains installed and enabled.
- DLV-265 production and focused evidence are accepted and integrated while the
  immutable installed Spotify 0.3.3 and existing Client ID/account state remain
  unchanged. DLV-270 is now the active widgets deliverable.

| Lane | Task/worktree | State |
| --- | --- | --- |
| Platform | `Implementation agent — platform lane`; `C:\Users\dwive\.codex\worktrees\6196\GameBarAlternative` | DLV-277 is accepted and integrated through main `c21ad02`; PID 34764 remains the accepted production artifact. No platform deliverable is assigned. DLV-273 remains retired and DLV-248 remains deferred. Never push. |
| Widgets | `Implementation agent — widgets lane`; `C:\Users\dwive\.codex\worktrees\563c\GameBarAlternative` | DLV-270 is active from accepted integrated main `c21ad02`. Reconcile cleanly, then follow its physical-first package-local paging and Spotify virtualization-adoption assignment. Preserve installed Spotify 0.3.3, current Client ID/account/configuration, PID 34764, and shared contracts. Never push. |

## Execution, review, and architecture rules

- Local `main` is the reviewer-owned integration branch. Review and integrate
  only accepted DLV commits. Implementation tasks never edit reviewer-owned
  documents and never push; the planner never authors implementation code.
- Follow physical-first ordering where an assignment says so: coherent
  production/build, user verdict, then focused tests. A production-only
  candidate is not integrated before the verdict and its post-verdict coverage.
- Launch a coherent native Release only when accepted integrated production or
  runtime artifact inputs change. Retain the accepted running candidate when
  the only delta is tests or reviewer documents and it already contains the
  integrated production commit.
- Keep verification proportional. Use the affected Tier 1 suites once after a
  coherent change and only the smallest linked Tier 2 group when a changed
  boundary requires it. Do not substitute Tier 3 unless explicitly required.
- The native overlay is the sole production presentation path. Avalonia/AVP is
  closed failed-experiment history: do not resume, message, launch, integrate,
  delete, or otherwise touch it without a new explicit user decision.
- The native host owns HWND, graphics, semantic admission, layout, clipping,
  focus, input, accessibility, and scroll offset. Widgets own private data and
  item materialization. Do not add package-specific behavior to core host/SDK
  layers or create duplicate focus, scroll, render, cache, or lifecycle owners.
- Input, focus, free-scroll, and re-entry bind to one fully admitted interaction
  generation. The prior committed generation remains authoritative until its
  replacement commits atomically; ordinary refresh must not create a transient
  interaction gap or reacquire focus.
- Sandboxed and full-trust Community widgets use the same bounded host-admission
  path. Full trust grants no overlay HWND, render, input, or focus authority.
- WidgetRail is pre-release and single-user. Do not add migration or old-version
  compatibility code without a later explicit user decision. Preserve external
  provider data, credentials, accounts, applications, and user files.

## Integrated prerequisites and retired history

- DLV-269 viewport-driven paging is accepted and integrated through main
  `683af77`; its full review chain is in the current historical snapshot.
- DLV-273 presenter extraction and corrections `b54e1e2`, `e98566e`, and
  `dcd58e0` are retired rejected history. Do not resume, test, or integrate them.
- DLV-274/275 cache retention, keep-alive lifetime, worker-failure isolation,
  and non-destructive stale-request cancellation are accepted and integrated by
  `9189cad` plus focused tests `6ce32b7`. The decoded/GPU aggregate budgets
  remain 96 MiB with 32 MiB per-image admission ceilings.
- DLV-271 bounded virtual collection windows are accepted through `d100f49`
  and integrated through main `56bc604`. Exact accepted PID 56752 already ran
  that production tree; no rejected DLV-273 presenter code entered main.
- DLV-248 remains deliberately deferred until explicit user promotion.

Detailed candidate hashes, PIDs, rejection evidence, focused counts, and prior
serialized transitions are preserved in the linked timestamped snapshot.

## Accepted and integrated — DLV-276: themed controller-first text-entry surface

Owner/baseline: platform lane from accepted main `56bc604`. This is a generic
correction to the existing
host-owned `TextEntryModal`, GameInput/modal interaction scope, native text-entry
presentation, theming, UI Automation, and directly affected public text-entry
guidance. Spotify Setup is one physical consumer, never a source of
package-specific host behavior. Do not interrupt in-progress DLV-271.

Replace the provisional stock-control keyboard shown in the current build. The
user observed that the widget retains visible controller focus behind the modal;
the modal uses untinted system window/edit/button styling instead of the active
WidgetRail theme; the hint `Enter public Spotify Client ID` appears as though it
were the text-entry value; the edit display does not visibly track characters as
they are entered; and a bottom row exposes Backspace, Clear, Cancel, and Commit
as ordinary focusable buttons. These are defects in the shared host component,
not Spotify onboarding behavior.

Opening text entry must establish one exclusive modal interaction scope before
the modal becomes visible. Suppress the underlying widget/tray focus treatment
and make its semantics inert while the modal is active. Route every controller
input category to the modal or consume it there; D-pad/left stick navigate the
keyboard, and right stick, shoulders, triggers, shortcuts, repeat, pointer, UIA,
and physical-keyboard input must never leak into widget scrolling, focus, or
actions. Retain the exact prior current focus identity only as restoration data.
On commit, cancel, close, failure, runtime replacement, or overlay shutdown,
retire modal authority once and restore that exact focus when still valid,
otherwise resolve one valid current-surface target. Do not introduce a second
GameInput, host-focus, semantic-tree, or modal-lifetime owner.

Use a compact controller keyboard instead of a form made from stock buttons.
The focused key is visually unmistakable and spatial navigation is stable.
Default character entry supports lowercase letters and digits; an explicit
Shift/Caps or symbol layer makes uppercase and the bounded generic punctuation
set reachable without duplicating the keyboard tree. A activates the focused
key, X performs Backspace, B cancels, and right trigger performs Enter/commit.
LB moves the live insertion cursor one position left and RB moves it one
position right, clamped at the buffer boundaries and with bounded controller
repeat. Shoulder movement changes only the caret: it must not move the focused
keyboard key, select text implicitly, or escape to a widget shortcut. Character
insertion and Backspace operate at that visible caret position rather than
always appending/removing at the end. Physical Left/Right, Enter/Escape/
Backspace, and ordinary pointer activation remain equivalent. Remove the
focusable Backspace/Clear/Cancel/Commit action row; show the controller mappings
as themed, accessible shortcut guidance rather than extra navigation targets.
Do not overload an existing reserved host gesture or send raw keys to the
widget.

Render the existing modal HWND through the active effective WidgetRail
appearance, text/interface scale, focus, spacing, and high-contrast policies;
do not create another overlay HWND, renderer authority, theme store, or
package-specific palette. Define prompt/hint, committed initial value, live edit
buffer, and password presentation as distinct states. A hint is displayed only
as non-value guidance when the live buffer is empty and is never selected,
counted, returned, or logged as text. Each character, Backspace, and layer
change updates the visible live buffer immediately; password mode masks it.
Commit returns only the final bounded buffer, while cancel/close preserves the
authored committed value. Preserve the existing 96 UTF-16-unit ceiling,
control-character rejection, secure clearing, stale runtime/snapshot/action
revalidation, and rule that only final committed text crosses to a widget.

Follow physical-first ordering. Produce a production-only tests-skipped Release
after direct source review of modal authority, focus capture/restoration,
GameInput routing, owner HWND activation, DPI/work-area bounds, theme ownership,
UIA, live-buffer rendering, password handling, and failure cleanup. The user
opens Spotify Setup from configured Ready state, types mixed lowercase/digits,
observes immediate live-buffer and Backspace updates, changes keyboard layer,
uses LB/RB to move the visible caret and insert/delete in the middle, and
cancels with B without changing the existing Client ID/account. Verify that the
underlying Spotify control never retains visible focus or reacts to any modal
input, the modal matches the active theme, and compact/wide plus 150% text/
interface scale remain fully reachable. No account change or real commit is
required for this physical verdict.

Production candidates `3d3ed0e` and `c4619f3` were physically rejected. Exact
correction `c96cdc0` admits only `BN_CLICKED`, restores bounded clipboard paste,
and preserves the neutral gate, B containment, semantic revalidation, result
feedback, and Enter wording. Border-only correction `d20b2c5` removes the stock
`WS_EX_DLGMODALFRAME` without changing modal behavior. The user physically
accepted the complete production behavior and styling on PID 22944. The
post-verdict focused tests and directly affected public guidance are assigned;
do not change or relaunch the accepted production candidate for that
non-production-only delta.

Post-verdict `TextEntryModalTests`, `AccessibilityTreeTests`, and the corrected
installed Community-host route pass. The original Cancel failure was a stale
fixture target: it sent Escape to the modal root instead of the edit control.
The original Enter backend-query oracle also exceeded what that UIA fixture can
prove because bridge admission is intentionally not worker completion and the
fixture has no packaged dequeue acknowledgement. It did not prove a production
failure. Bounded runtime and bridge probes instead prove that non-secret
committed text reaches worker dequeue, `OnActionAsync`, and publication; the
direct Game Launcher handler also passes. Completion-evidence correction
`ca967e6` retains only durable boundary assertions and logs no committed text.
DLV-276 is integrated through main `ca967e6`; PID 34208 is the coherent main
Release.

After acceptance, add focused modal layout/theme/live-buffer/hint/password,
caret insertion/deletion and LB/RB boundary/repeat behavior, exclusive
controller-routing, no-input-leak, UIA, commit/cancel/close/failure,
stale-authority, exact-focus-restoration, DPI/scale, and maximum-length coverage.
Use the existing credential-free production-host TextEntry route for committed
value proof. Update controller-input and widget-authoring guidance so the public
contract and controller legend match the accepted behavior.

Stop for a required Spotify/package branch, raw-input exposure to widgets, a
second native focus/input/theme owner, an accessibility model that cannot
represent the edit buffer and keys accurately, a public protocol change rather
than correction of the existing value/placeholder contract, credential or
configuration mutation, destructive state action, substantial conflict, or
missing accepted DLV-271 baseline. Never push.

## Assigned platform deliverable — DLV-277: one committed responsive focus mode

Owner/baseline: platform lane after accepted DLV-276 integration. This is a
generic native host correction in the existing renderer, committed surface-
geometry, responsive focus-persistence, and interaction-session owners. It may
run independently from package-only DLV-265 after their shared DLV-276 baseline.

Correct the reproduced focus loop when returning from the provider-free Full
Application reference widget to playing Spotify. Current PID 34644 logs prove
that playback publications arrive about every 200 ms; sequence 762 briefly
accepts `spotify.nav.wide.queue`, then 12 ms later remaps to the hidden
`spotify.nav.compact.player`, and sequence 763 restores Player. The loop repeats
at sequence 778. Each cycle also forces full raster work near 170-180 ms.

The renderer selects responsive visibility from the admitted authored maximum
extent (Spotify `980x560`), while `ReconcileResponsiveFocusPersistence` derives
mode again from the post-chrome content panel (`980x505`). Since compact mode is
height below 540, the two owners disagree about which responsive branch is
visible. This is a host geometry-authority mismatch, not Spotify refresh intent,
Reference Widget lifetime, provider behavior, or image-cache pressure.

Use one exact committed responsive surface/mode decision for rendering,
navigation, focus persistence, UI Automation, hit testing, and refresh
reconciliation. A same-geometry snapshot or playback/progress publication must
never remap current focus. Responsive aliases may transfer focus exactly once
only after a real committed compact/wide transition, and must resolve to the
branch actually rendered. Preserve current input scope, focus identity, scroll,
pressed/slider state, retained checkpoints, and last-valid presentation.

Follow physical-first ordering. Produce a production-only tests-skipped Release
after source review. The user enters playing Spotify from the reference widget,
moves focus from Player to Queue/Playlists/Devices without activating them, and
observes it remain there across several playback updates. Repeat after a genuine
compact/wide resize. Logs must show one branch per committed geometry, no
wide/compact focus oscillation, no reset to Player, and no focus-induced full-
raster loop. After acceptance add focused renderer/focus persistence, committed-
geometry, same-sequence refresh, real resize, UIA, and interaction tests.

Do not change Spotify trees, responsive thresholds, provider publication rate,
public SDK/protocol, Reference Widget residency, or create another geometry,
focus, renderer, accessibility, or input owner. Stop for a required package
branch, inability to expose the existing committed responsive decision at the
focus seam, protocol work, substantial conflict, missing accepted DLV-276
baseline, destructive action, or physical behavior that contradicts the logged
mode mismatch. Never push.

Production-only commit `830364d` removes the four host-side pre-paint mode
recalculations. A successful `DeclarativeRenderer` result now records the exact
responsive viewport and compact/expanded mode used for that pass; responsive
focus persistence consumes only that committed result. Navigation, hit testing,
UIA, and focus reconciliation therefore share the rendered geometry. The
tests-skipped Release build passes with the six existing `C4244` warnings and is
running as PID 34764. The physical verdict is accepted. Focused renderer route
evidence passes 5,009 checks; focused interaction groups pass 94 interaction-
session, 122 controller-navigation, 49 focus-navigation, 24 widget-surface,
2,096 slider, 29 pressed-state, 168 accessibility-provider, and text-entry
checks. Production and evidence are integrated through main `c21ad02`; the
running accepted artifact remains unchanged.

## Assigned widgets deliverable — DLV-265: controller-first Spotify onboarding

Preserve clean production-only correction tip `13bd971`, reconciliation
`5fd1a06`, installed Spotify 0.3.3, and the user's existing Client ID/account
state. DLV-276 is accepted and integrated after DLV-271; reconcile the saved
correction onto current main `ca967e6` without rewriting it and continue
physical-first. Do not reinstall, replace configuration, or reset state.

The Spotify Community package must expose a controller-reachable Setup/Change
Client ID flow on configured Ready and unconfigured surfaces, reuse the bounded
host text-entry modal, open the developer dashboard, copy the exact non-secret
redirect URI, validate and atomically store the public Client ID, and refresh
configuration. Changing it retains fail-closed removal of the prior OAuth
refresh credential. Never request/store a Client Secret; PKCE refresh credentials
stay in package-owned Windows Credential Manager. Ordinary setup must require no
terminal, repository checkout, or source directory, and must not add a shared
protocol or service-specific core behavior.

Candidate 0.3.3 at `13bd971` uses the package-owned 96-character input bound.
After DLV-276, physically verify Setup uses the accepted host keyboard and
opens/cancels in configured Ready wide and compact layouts while Player, Queue,
Playlists, Devices, account state, and shortcuts remain intact. Do not replace
the existing Client ID unless the user chooses that account action. After the
verdict add only focused presentation/action, configuration-write,
old-credential invalidation, failure, and cancellation tests. Stop for shared
protocol/capability work, Client Secret, Credential Manager migration,
third-party authentication needed for automation, provider-dashboard
automation, substantial conflict, or out-of-package work. Never push.

Reconciliation `edf2583` preserves `13bd971` and `5fd1a06` over main `ce13a3c`.
One fresh isolated tests-skipped Release and Spotify 0.3.3 package build pass;
the package is 1,158,141 bytes with SHA-256
`1AB8A56D400292004C8B6D924E917E03B3E1E4E7FEA30DEDDDC0524A86AD5B53`.
Nothing was installed or selected and no configuration, authentication, or
process state changed. The already installed immutable 0.3.3 plus PID 34764 are
the physically accepted candidate. Focused Spotify widget presentation/action
evidence passes 54/54 and package application evidence passes 6/6, including
replacement Client-ID persistence and exact one-time prior refresh-credential
deletion without network access. Production and evidence are integrated through
main `bb8234f`; installed package and configuration state remain unchanged.

## Assigned widgets deliverable — DLV-270: Spotify collection paging efficiency

Owner/baseline: widgets lane after accepted DLV-265 and DLV-277 integration on
main `c21ad02`. Reconcile the standing worktree cleanly before production work.
Keep provider calls,
page/retention policy, queue parsing, diagnostics, and immutable package version
inside Spotify. Reuse accepted generic cursor, viewport-prefetch, and virtualized
window contracts; do not add Spotify behavior to core layers. Explicitly opt
Spotify playlist and playlist-track viewports into DLV-271 virtual windows with
stable item keys, bounded estimated row extents, and authoritative logical
first-index/total-count metadata. Preserve Queue as the explicit bounded
unpaginated collection unless Spotify exposes a real adjacent-page contract;
do not invent queue cursors merely to claim virtualization adoption.

Load/validate selected-playlist metadata once per exact selection/configuration
generation, then fetch adjacent tracks only through paged `/items`. Preserve
cancellation, latest-generation authority, stale rejection, duplicate identity,
provider correction, retry, and bounded errors. Measure the current 12-item
page/24-item window against bounded candidates no larger than Spotify's
documented limit and select the smallest settings that avoid visible stalls
without materially increasing snapshot size, layout/render work, decoded
artwork, memory, or calls.

Unify Queue's 50-item cursor declaration and 100-item parser ceiling into one
explicit bounded unpaginated contract governing parsing, admission,
presentation, truncation/error behavior, tests, and diagnostics. Do not invent
queue cursors, poll on playback progress, silently truncate, or refresh
unchanged collections into avoidable churn.

Use physical-first ordering with one immutable package candidate and bounded
request-count/timing evidence: one metadata call per selection, one items call
per page, no duplicate edge load, measured window/snapshot effect, and consistent
Queue admission. The user traverses long collections forward/backward with
right stick and D-pad in compact/wide layouts and verifies timely rows, stable
focus/anchors, artwork, playback, refresh, and recovery. Then add focused call,
cursor, cancellation/stale-generation, bound, truncation, snapshot-size, and
presentation tests. Stop for account authentication needed for automation,
provider ambiguity changing contract, shared-boundary changes, destructive
credential/configuration action, unbounded retention, substantial conflict, or
missing accepted DLV-265/DLV-271/DLV-276 baseline. Never push.

## Serialized order

1. DLV-269, DLV-274/275, DLV-271, DLV-276, DLV-265, and DLV-277 are accepted
   and integrated through main `c21ad02`; DLV-273 remains retired rejected
   history.
2. Run DLV-270 physical-first in the widgets lane from main `c21ad02`.
3. DLV-248 remains deferred until explicit user promotion.

## Manual, external, and blocked evidence

| Item | Blocker / required evidence |
| --- | --- |
| DLV-257 identity | Complete: all exact mapping decisions are approved and frozen. |
| Microsoft Store name | Partner Center availability/reservation through the user's account. |
| Domain | Live registrar/RDAP availability and optional registration through the user's account. |
| Trademark | Similar-mark clearance; qualified counsel recommended before public release. |
| GitHub identity | User-selected owner plus repository/organization availability and optional rename/creation. |
| DLV-276 | Complete: behavior/styling accepted, focused boundary evidence passes, and production/tests are integrated through main `ca967e6`. |
| DLV-277 | Complete: physical verdict accepted; focused native evidence passes and production/tests are integrated through main `c21ad02`. |
| DLV-265 | Complete: physical verdict accepted; focused Spotify evidence passes and production/tests are integrated through main `bb8234f`. |
| DLV-248 | Deliberately deferred until explicit user promotion. |

## Recent accepted milestones

| DLV | Accepted evidence / integration |
| --- | --- |
| DLV-268 | Right-stick free-scroll/focus re-entry accepted and integrated through `1cc9be8`. |
| DLV-272 | Interaction-session extraction accepted and integrated through `8100bd7`. |
| DLV-265 | Controller-first Spotify onboarding accepted and integrated through `bb8234f`. |
| DLV-277 | One committed responsive focus mode accepted and integrated through `c21ad02`. |
| DLV-269 | Viewport-driven paging accepted and integrated through `683af77`. |
| DLV-274/275 | Cache retention and lifetime corrections accepted as `949b586`; integrated by `9189cad` plus tests `6ce32b7`. |
| DLV-271 | Virtual collection windows accepted through `d100f49` and integrated through `56bc604`. |
| DLV-276 | Controller keyboard behavior/styling accepted and integrated through main `ca967e6`; corrected host, runtime, bridge, handler, UIA, and modal evidence is green. |
