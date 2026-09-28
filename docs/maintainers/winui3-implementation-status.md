# WinUI migration implementation status

Updated 2026-09-28; branch `codex/winui3-frontend`. Main and the installed native candidate
are untouched. Migration is active and incomplete.

## Current summary

- Real catalog/lifecycle shell is integrated. It reuses OwnedBridgeProcess and the
  accepted native controller/activation adapter. Automated shell runs use
  `--shell-no-controller`; production controller acceptance is still outstanding.
- Both Playnite Home and Browse use captured indexed queries. The isolated real
  package/provider probe loads 36 Home and 178 Library items, retrieves cover bytes,
  and opens/dismisses details. A startup invalidation-before-first-snapshot race
  was reproduced, tested and fixed; no diagnostic polling is required.
- Native modal, Select, TextEntry/controller keyboard, context menus and sliders
  are integrated. Scoped input and exact indexed action ownership remain enforced.
- Responsive branches, static grids, basic poster layering and ordinary styles
  are mapped. Native scroll routing and global-policy dialog entrances are wired.
  Focused/pressed scale uses native presentation animation; effective TextScale
  reaches typography. The modal veil now paints above its retained parent.
  These pass 52 native style checks and 25 modal checks in their lane. Section
  transitions, focus decoration, depth and complete styling remain incomplete.
- Authored surface sizing and per-display interface zoom are integrated, reusing
  existing display identity/settings and platform placement. Sixteen shell tests,
  13 native sizing checks and real Playnite at Windows 125% DPI/interface 75% pass.
  Physical mixed-monitor and full controller acceptance remain outstanding.
- Real Home and details render. A production long-description regression corrected
  inactive scroll axes: details now keeps the poster and all navigation tabs in
  view. Home focus bounds now fit poster content rather than the full rail height.
- The real Library layout-cycle crash is corrected by using native viewport width
  and honoring explicit full-width alignment. Home, Library and both details pages
  now render real artwork in the integrated shell. Adaptive cells are allocated in
  physical pixels so native rounding at 125% DPI no longer wraps six columns into
  five. A real Library Down move now goes from item 0 to item 6. The native style
  fixture passes 42 checks, including the production fractional-width case.
- Saved Home placeholders now use bounded indexed publications instead of expanding
  into the parent tree and exceeding its style budget. They remain display-only;
  live replacement retires their bindings. The fresh real package starts without
  Retry, and both its empty-state and saved-state transport probes pass.
- Latest integrated managed gates: 157 Playnite tests, 88 session tests, 16 shell
  tests, 35 style tests. Package-icon transport is admitted by exact inventory/hash
  and bounded independently. OriginalColor Icon/Button/Select rendering is integrated
  with 15 native checks and a 7-check/45-symbol glyph regression. ThemeTint currently
  retains the semantic fallback; native SVG alpha-mask capability work remains.
- Repeatable real-package smoke: Home startup/row/details/focus/shutdown passes all
  five checks. Library startup/navigation/row passes, but one unfocused UIA poster
  invocation did not open details; a separate immediate run succeeded. A deterministic
  regression reproduced rejection of a displayed frame after a newer unrelated
  publication. The correction retains exact frame provenance and checks unchanged
  binding/scope/query through the existing Bridge, with no frontend input rebasing.
  Real Library repeatability still needs rechecking after this integration.
- Pending ordinary artwork now renews on snapshot replacement while decoded pixels
  remain retained. A native regression fails against the old loader at replacement
  demand, and all 13 surface checks pass with the correction. Stale artwork admission
  no longer becomes an overlay error. Real-package integrated confirmation remains.
  Native fixture gates and real screenshots are recorded below and in
  [Playnite validation](winui-playnite-validation.md); fixture results are not
  production-widget performance acceptance.
- Remaining product work includes special media/capture/pinned surfaces, remaining
  widget adoption, complete tray/chrome behavior, actual controller,
  performance/memory/accessibility validation and packaging.

## Checkpoint history

The dated entries below preserve investigation evidence. Older failures and counts
describe their checkpoint, not current status; the summary above takes precedence.

### Initial implementation

- Microsoft WinApp CLI 0.7.0 installed with user authorization. Developer Mode
  enabled with separate explicit UAC approval. Existing .NET SDK reused.
- Official WinUI MVVM template created `src/OverlayFrontend.WinUI`; resolved stable
  dependencies pinned (Windows App SDK 2.5.1). Counter sample removed; this is a
  frontend foundation, not a widget port. Development package registration is
  separate from the existing installed product.
- XAML shell builds with Microsoft WinUI analyzer enabled: zero warnings/errors.
- OS backdrop compositor requires a Windows dispatcher queue. The initial startup
  crash is diagnosed in `artifacts/winui-shell/debug-launch.log`. Fixed using the
  framework's `DispatcherQueue.EnsureSystemDispatcherQueue`, avoiding custom P/Invoke
  or a competing message loop. Frontend then stayed live and exposed Shell.Status
  and Shell.Close through UIA; invoking Close exited normally.
- `WidgetUi.State` selects background/presentation sources from immutable logical
  membership, independent of realization. Its 15 tests pass. This policy component
  does not implement layout, spatial navigation, painting, or animation.
- Feature inventory: [contracts](winui-feature-contracts.md).
- Replaced custom backdrop code with pinned WinUIEx 2.9.3. Basic transparency
  now passes a controlled red/green desktop-background test at three margin
  coordinates. WinUIEx owns the native DWM/message integration rather than a
  copied implementation in our frontend. License retained and packaged.
- The shared script `scripts/Test-WinUiTransparency.ps1` records pixel results and
  screenshots. A real pointer click reached Close after the test. Test PID 22148
  exited; no validation frontend is intentionally left running.
- [Native binding inventory](winui-platform-binding.md) identifies existing ABI
  reuse and the separate View+Menu observer that must also be exported/preserved.

## Failed / unproven gates

The original top-level Window plus transparent SystemBackdrop brush did NOT show the
desktop through the empty margins. Both window and desktop-composited screenshots
show opaque margins. Extending content into the titlebar did not correct this.
An external diagnostic DwmExtendFrameIntoClientArea(-1) call returned success but
made the window white; it was not added to production. A subsequent external style
probe did not establish transparency either. Test window PID 11060 was closed
through Shell.Close afterward. This attempt was replaced, not hidden behind a
passing build. WinUIEx then passed the basic alpha proof described above.

The first winapp launch with --output-appx-directory failed registration (missing
appxmanifest.xml); default project-mode deployment succeeded. Revisit a supported
short staging path if package size/path limits require it. No manifest was deleted,
no unpackaged fallback was introduced to suppress the failure.

Raw debug-process sample was roughly 98 MB private / 142 MB working set; this is an
empty-shell observation, NOT a full-product memory comparison or acceptance result.

No controller routing, real widget transport, lazy collection port, native media,
pinned projection, transparent-area input policy, or frame-latency gate has passed.
Do not claim migration readiness based on shell build or source-selection tests.

## Next actions

1. Extend basic desktop-alpha proof to input regions, native media, pinning,
   monitor/DPI changes and recovery. The shell still has a visible DWM outline;
   finish intended chrome policy through supported window APIs. No undocumented
   root surgery or unstable CompositionEngine opt-in as a workaround.
2. Connect existing platform ABI input/activation rather than rewrite it. Prefer
   framework APIs whenever they remove custom code.
3. Connect immutable source selection to real page/item metadata and shared WinUI
   presentation slots; retain cursor membership independently from realized controls.
4. Deliver the real sandboxed Playnite/collection slice, followed by remaining
   migration features and the pre-WIDGE-293 comparison from the plan.

Evidence: ignored `artifacts/winui-shell/` (build binlogs, UIA/launch output, screenshots,
source-reference excerpt) and `artifacts/winui-state/` (focused tests/binlog).

## Controller and external-content foundation follow-up

- `scripts/Build-OverlayPlatform.ps1` builds only the existing ABI v4 DLL. Release
  build and 24-export inspection passed. It neither initializes hardware nor
  installs the copied GameInput redistributable. Third-party ViGEm source emits
  existing encoding warnings.
- `OverlayPlatformClient` now provides exact ABI structs, initialized outputs,
  stdcall imports/callbacks, SafeHandle lifetime and serialized access. Its 15
  focused fake-native tests pass; they cover callback/disposal and failure paths,
  not physical delivery.
- The WinUI input pump and opt-in controller validation page build with zero
  warnings/errors. User authorized stopping native candidate PID 30152; its logs
  were preserved under `artifacts/winui-controller/`. It had no controller-isolation
  command-line flag. WinUI PID 26144 receives connected GameInputVisibleLease frames.
  Physical navigation/single-A/Guide acceptance is pending. Do not run the old
  native candidate concurrently with this controller owner.
- External-surface validation uses WinUI WebView2 with trusted literal animated
  content. Actual screenshots show a XAML button and ContentDialog above the
  external surface. Four scripted checks pass: ready, pointer-open, Escape focus
  restoration, and reopen. The widget-local modal component is NOT implemented by
  this fixture; neither actual playback nor pinning/rounded clipping is proven.
- Packaged desktop launch arguments are consumed from both activation data and the
  process command line. Project-mode winapp launches now explicitly pass
  `-p Platform=x64` after adding a managed project reference.
- Removed the unrelated template systemAIModels capability. The shell exposes only
  x64 until the native boundary gains supported additional architectures.

### Physical controller feedback and activation correction

Follow-up source audit found a separate confirmed input-starvation defect: the
WinUI pump publishes window state each poll, while the native setter reset its
edge tracker on every unfocused publication. Each background-visible read then
only primed, producing no A/navigation edge despite a connected visible lease.
The setter now applies a shared tracker transition operation: actual hide or
focus loss retires history; unchanged visible/unfocused state preserves it.
The pump can continue publishing current ownership without a duplicate managed
state cache. Isolation close/selection behavior is unchanged.

The focused production-DLL executable passes 119 checks, including an actual
exported-setter regression and deterministic A/D-pad/stick press/repeat/release
checks. The identical executable against the previous DLL fails specifically at
`repeated background state must not re-prime the production input tracker`.
Evidence: `artifacts/winui-controller/idempotent-state-*.log`. The requested full
`-PlatformInteropTestsOnly` selector stopped first at the preexisting
`GameInputQueryRuntimeTests` device-enumeration/runtime-identity check; that gate
is reported separately, not waived or repaired. The focused executable was then
compiled/run directly against the newly built production DLL.

The replay fake does not model native window-state resets, so its earlier pass
could not catch this defect. Foreground activation/restore policy and duplicate
WinUI/native physical gamepad delivery remain separate unaccepted gates. No
additional physical check was requested while the user was away. The corrected
DLL is in `src/OverlayHost/out/Release`; rebuild the WinUI platform artifact
before the next candidate launch.

User reported: Guide hide/show works, but native navigation/A required clicking a
button first. This is a failed controller-native startup gate, not acceptance.
No further physical checks are requested while the user is away.

The validation page previously called Focus during Loaded and ignored failure.
It now queues focus entry after loading/activation/show, verifies the framework
result, and preserves an already focused descendant. Cold-launch automated check
(PID 23444) observed First focused without any click, Right moved to Second,
Enter changed the shared command count to Second: 1. Native status reported
connected GameInputVisibleLease and entry retained. Keyboard automation does not
prove physical controller delivery; keep the original failure open until an
end-to-end native-frame/activation test and later physical acceptance establish it.
The validation process was closed via Shell.Close after checks.

## Controller replay and virtualized collection follow-up

`--replay-controller` now supplies deterministic native-shaped frames through the
same PlatformInputPump/OverlayPlatformSession, actual WinUI focus traversal,
command path and Guide hide/show handler as the live fixture. It does not load the
native DLL or read hardware. Cold focus, Right, one A, hide/show, retained Second
focus and a second A passed with no injected mouse or keyboard. This narrows the
startup-focus check but does not supersede the user's physical delivery failure.

A `KeyedObservableCollection<T>` preserves per-key binding wrappers within a data
generation and emits granular standard notifications. Twelve new state tests pass
(27 combined). Current code does not claim atomic notifications or constant-time
arbitrary reorders. See its README for limits.

`--validate-collection` connects this model to WinUI's higher-level ItemsView with
UniformGridLayout and ItemContainer. No custom virtualizing layout/focus engine.
With 1,000 items, direct visual-tree inspection counted 13 realized containers at
the top and 14 at the bottom. Scrolling reached Item 999, appending reached Item
1029, and payload update changed the existing container's title. These are
functional observations, not frame-time or full-product acceptance.

**Failing regression:** focus Item 0, then send fixture F7 to prepend 30 records
without transferring focus to a toolbar button. UIA retained the same focused
item/container identity but reported it offscreen with zero bounds afterward.
The focused item must remain visible under our cursor contract. Evidence is in
`artifacts/winui-collection/prepend-before.json`, `prepend-after.json`, and
`prepend-search.json`. Investigate documented ScrollView anchoring and keyed
logical index mapping before implementing the admission policy. Do not mark the
collection complete or mask this by invoking ScrollIntoView from a test.

Transport correction: current Playnite is a full-trust application widget, not a
sandboxed worker. Use its real application path for UI parity and a real dotnet
worker (e.g. Clock) for the separate AppContainer proof. Existing managed bridge
facade is reusable but needs typed host effects/media/artwork correlation extensions;
see `winui-widget-transport.md`. Prior plan references to sandboxed Playnite are
not accurate descriptions of its manifest.
A bounded follow-up set ScrollView.VerticalAnchorRatio to 0.5 (documented item
anchoring instead of the extent-edge special case). The same prepend still left
Item 0 offscreen. That experimental setting was removed; it is not a fix.
`anchor-before.json` / `anchor-after.json` retain the result. Next inspect logical
key/index mapping and native ItemsView bring/anchor lifecycle with a repeatable
regression, rather than accumulating unproven scroll settings.

## Real bridge and installed Clock round trip

`OwnedBridgeProcess` owns a random-pipe bridge child with explicit installation,
settings and installed-catalog roots. It drains bounded diagnostics, observes
early exit, and disposes the session before awaiting/terminating only its child.
Startup cleanup preserves the original connection/cancellation error. Transport
shutdown now interrupts blocked writers, write-gate and capacity waiters and
drains active requests before disposing synchronization; concurrent disposal
shares one task. Eighteen session tests pass, including the stalled-peer cases.

The first real catalog request found an outdated client contract: the producer
returns `isComplete` alongside revision/widgets. The facade now preserves that
field and tests pending-to-complete discovery. A real Power initial snapshot
(read-only, no action) succeeded and the owned bridge exited after disposal.

Shared framing, catalog descriptors and style DTOs now live in
`WidgetBridge.Contracts`. The client dependency graph includes only Contracts,
Protocol, SDK and Styling; it no longer pulls the executable, runtime, broker,
catalog or providers into WinUI. Wire shapes/namespaces are unchanged. Managed
consumers need rebuilding because types moved assemblies. Nine focused bridge
checks passed, including sandbox/full-trust managed session paths. The full-trust
check initially lacked its required Release fixture; building it resolved setup.

The Clock source was packaged and installed through wrail into the isolated
`artifacts/winui-clock/catalog`, with a separate settings profile. A new opt-in
WinUI fixture uses the real bridge/catalog/worker and validated declarations;
it never loads widget code in the frontend. Ten UIA checks passed: first snapshot,
cold focus without injected input, real generic worker process, three Refresh
actions each advancing exactly one snapshot, stable focused control through all
updates, and frontend/bridge/worker exit on close. The worker exit log records
cooperative stop and exit code zero. Final screenshot was visually inspected.
Frontend analyzer build: zero warnings/errors.

This is deliberately a **Clock-only integration fixture**, supporting its four
node kinds and basic typography, not the production renderer or theme migration.
Unsupported structure fails visibly. The shipping adapter, controller shortcuts,
media/artwork, scopes, modals and collection semantics remain incomplete. No
physical controller acceptance was inferred from UIA or deterministic replay.

Pixel-check caveat: while idle, both Clock and the existing shell/replay showed
UIA state without rendered pixels. A harmless synthetic Escape restored shell
pixels; the normal transparent Clock subsequently rendered correctly. Temporary
opaque-window experiments were removed. This suggests idle desktop presentation
suspension, but its cause is not proven; do not classify blank captures as a
widget rendering defect without checking actual presentation state. The scripted
capture sends Escape only after its no-input initial-focus assertion.

Further collection investigation tried public `StartBringItemIntoView` and
`IKeyIndexMapping` (recognized by `ItemsSourceView.HasKeyIndexMapping`). Neither
provided correct prepend focus/viewport retention in the tested combinations.
All unsuccessful adapters/scroll corrections were removed. Source experiments
remain only in `artifacts/winui-collection/anchor-experiment`; no claimed fix.

Evidence: `artifacts/winui-bridge/` contains build/test binlogs and real bridge
smoke output; `artifacts/winui-clock/results.json`, `clock-verified.png` and
`profile/overlay.log` contain the real worker/UI round-trip evidence. All changes
remain on the isolated migration branch; no main merge, push or release.

## Native control retention and typed host events

`Presentation/WidgetViewPresenter` is the shared beginning of the declaration
adapter. It creates standard WinUI controls, retains them by scope/element/kind/
item ancestry within a worker owner, and reconciles changed child membership.
WinUI still owns measurement, layout, painting, directional focus and control
behavior; this is not a custom layout/virtualization/frame scheduler. The real
Clock fixture now uses it, removing its separate four-kind renderer. The native
controller fixture/replay also uses the same presenter for entry, directional
focus and activation instead of creating its own test buttons.

Currently supported: Stack, Row, ordinary Scroll, Text, Button, standard
ActionSurface, Progress, LoadingIndicator and Spacer, with basic resolved font
size. Control identity survives ordinary content updates and reparenting. Worker
replacement creates fresh controls and retires saved command tokens. Inactive
scope controls cannot dispatch; returning to a scope can restore its still-valid
focus. Entry work is coalesced and ordinary updates do not take focus from outside
the widget. This remains an incomplete adapter: full styling/themes, explicit
focus groups/neighbors/shortcuts, images/icons, modals, collections, media,
responsive layout and the remaining controls still need implementation. Unsupported
node/layout families fail before membership mutation; they are not substitutes
for migrated product features. WinUI object creation/property failure is not an
atomic transaction guarantee; the surface owner must show its failed state.

Sixteen scripted UI checks pass through the shared presenter: authored initial
focus on the second button, single activation, insertion/reparenting with stable
control and focus, scope switch/return, inactive-scope rejection, external focus
retention, disabled-target fallback, new-owner initial focus/new identity and
rejection of a retained retired command, and two deliberate presses while the
first admission remains pending. WinUI command execution does not introduce a
second single-flight queue over the worker's action policy. Real Clock round trip still passes all
ten checks. Native-shaped controller replay passes through the shared presenter,
including Guide hide/show and retained focus. These remain automated checks, not
physical controller acceptance. Final frontend analyzer build has zero warnings
and errors; evidence is under `artifacts/winui-controls/`.

The managed session now publishes typed catalog/appearance revision notifications
and host effects. It accepts the actual producer's initiation timestamp and
activation target fields, rejects stale/replayed effects, preserves host-private
native identity and supplies authority revalidation after UI dispatch. Unknown
optional effects are explicitly Unsupported. Retirement/restart boundaries prevent
old effects from reviving. The host must still enforce its visible-session policy
and revalidate HWND/process/class identity; receiving a typed event does not
execute it. Twenty-seven session tests pass, including exact producer shapes,
malformed target rejection, stale/replayed effects, retirement and monotonic
revision notifications. No service/provider dependencies were reintroduced.

### GridView comparison: retention remains unresolved

The opt-in `--validate-gridview` fixture tests standard GridView/ItemsWrapGrid over
the same keyed ItemsSource, without custom offsets/anchor compensation. It also
fails prepend and leading-eviction retention. Final isolated run: seven checks
pass, four fail. Item0 after prepend keeps its logical focus but moves y=4 to
y=1180 at offset zero. Removing thirty leading items while Item600 is focused
leaves its container offscreen and the viewport advances to Item628. Append and
same-key updates pass independently; visual-tree container counts remain bounded.

This rules out a control swap as the collection fix. The fixture and
`scripts/Test-WinUiGridView.ps1` preserve the regression, not an accepted production
collection. Evidence and six inspected screenshots are in
`artifacts/winui-gridview/isolated-cases/`. Resolve the collection admission/data
virtualization and anchoring contract before wiring it to Playnite. No compensating
scroll code or unsuccessful adapter was added to the shared presenter.

## Sparse indexed data and artwork demand identity

`Collections/IndexedItemsSource<T>` now implements WinUI IList/IItemsRangeInfo for
one exact-count query and one consumer. It keeps sparse stable binding slots,
requests visible/tracked pages asynchronously with bounded concurrency, validates
query/request/index/key identity, and releases payload without structural item
removal. A new query replaces the source. The range reader remains trusted internal
code that must honor cancellation and bound its work; SDK/worker range transport,
retry UI and the production widget path are not connected yet.

The new eight-check `--validate-indexed` integration run passes. Native ListView
range callbacks were observed directly. One million logical rows used at most
96 resident slots and 21 realized containers without enumerating the full source.
Down/up navigation remained usable while an adjacent buffer request was held.
Releasing it increased completed loads while preserving focus 600000, y=20 and
scroll offset 43200172 exactly. Return traversal evicted distant payload and a
later deep visit performed another load while Count stayed unchanged. Screenshot
inspection and analyzer build passed. This demonstrates native data virtualization
and admission behavior, not production-widget frame pacing or controller acceptance.
Evidence: `artifacts/winui-indexed/authority/`.

An additional native anchoring experiment did not provide a reliable cursor fix.
An explicit AnchorRequested preference and interior ratio initially preserved a
top item, but broader cases still displaced it after prepend. All experimental
code was removed; `artifacts/winui-collection/anchor-selection/` retains source and
observations. No guessed offset correction or layout retry loop was introduced.

The SDK/service design direction and real provider capability analysis are in
`winui-indexed-collections.md`. YouTube's finite browse snapshot is an indexed
candidate. Playnite needs final display filtering normalized before advertising
its indexed count. Spotify's unqualified live search remains discovered/cursor
data. Arbitrary query membership changes and opaque before-origin insertion remain
distinct unresolved cases; the indexed cache path does not claim to solve them.

Artwork correlation now uses unique demand IDs with runtime/presentation authority,
so cancellation, reordering and late same-handle replies cannot satisfy replacement
demands. Local admission/completion has a configurable timeout and exact authority
is rechecked around decoding. The old 512-character diagnostic-text limit no longer
truncates/rejects normal Base64 artwork; the encoded-artwork limit applies instead.
All 34 session tests pass, including seven new demand/race/timeout cases. Server-side
decoding still lacks per-demand cancellation; already-sent transport requests remain
bounded/correlated until reply or shutdown. This distinction is intentional and
documented, not a claim that local cancellation stops provider work.


## Worker-side indexed declaration foundation

The migration SDK now registers immutable exact-count queries and renders only
requested bounded ranges. Protocol 60 declarations carry no inline items. Shared
range validation resolves the actual main/pinned parent and validates its source,
scope, occurrence keys, response correlation, presentation ownership and bounds.
Normal parent snapshots still reject inline indexed payloads. Item declaration
lists are frozen with the same helper used by retained collection declarations;
source/collection/key-scoped IDs distinguish independent placements.

The SDK limits actual provider tasks to four per source, including cancelled or
timed-out tasks until they terminate. Query/content changes retire old readers;
late content and cancelled rendering cannot publish. The public API baseline is
updated. The 128-check SDK console suite passes, including six new indexed groups.
This is not an end-to-end production collection: bridge/session demand, row
leases and actions, native templates, and widget adoption remain outstanding.


### Concurrent range transport and controller correction

Worker/process-client range reads now have a separate four-request lane, explicit
exact-demand cancellation and bounded teardown. Slow range reads leave the serial
input/render/lifecycle path available. Original response correlation survives
cancellation until its terminal reply drains; cancellation never targets a
replacement worker. Both worker trust tiers share this implementation. Six new
runtime scenarios and nine existing focused regressions pass. SDK API compatibility
passes all fourteen tests; generated protocol parity passes 140 constants. Bridge
and WinUI analyzer builds pass without warnings/errors. No indexed row action or
artwork authority is enabled by this transport alone.

The visible-but-unfocused controller defect has an isolated regression: the WinUI
pump publishes window state each poll, but the native setter previously reset input
tracking on every unfocused publication. The setter now retires tracking only on a
real hide or focus-loss transition. The production DLL passes 119 focused ABI/policy
checks; the same executable against the prior DLL fails the repeated-background-
state regression. The broader native selector stopped at the existing GameInput
query-lifetime/device-enumeration check before this executable; that result remains
separate and uncorrected. Evidence is in `artifacts/winui-controller/`. The platform
artifact was rebuilt and copied by the subsequent WinUI build. Physical Guide
reopen/navigation/A acceptance remains pending; no validation window was launched.


## Bridge/session indexed demand and native focus policy

The bridge now routes indexed reads and exact cancellation independently from its
ordinary per-widget FIFO. It bounds reads to eight total/four per widget, captures
an already-running worker ordinal without startup/recovery, releases the operation
gate during provider work and holds a publication lease through actual completion.
Cancellation waits for read admission, eliminating cancel-before-registration.
Five new bridge scenarios pass, including real bridge-pipe-to-worker traffic;
eleven existing focused dispatcher/retirement/session regressions also pass.

The presentation-session API owns demand identity, timeout, capacity and exact
frame/projection cancellation. Its forty-seven tests pass. New coverage includes
out-of-order replies, replacement authority, hidden surfaces, disposal, preserved
data across unrelated snapshots and modal opening, and stale-frame cancellation
that cannot affect a replacement frame. Callers still own surface lifetime tokens.
The result remains data only: no row action/artwork lease or native item template
has been enabled yet. This connection is not production-widget acceptance.

The shared WinUI presenter now maps authored neighbors (including group targets)
to native XYFocus properties. Native directional entry honors remembered/default
group children, and searches stay within the active scope. One-shot group requests
can wait for data, are not replayed on ordinary updates, and retire on newer user
navigation, activation, pointer input or explicit withdrawal. Worker replacement
clears old memories. Fifteen real WinUI focus-policy checks and all sixteen prior
presenter checks pass; analyzer builds pass. Test windows were closed. These tests
exercise native UI and synthetic navigation, not physical controller acceptance or
realized lazy-row focus, which remain outstanding.


## Captured item semantics in the SDK

The indexed source options now require a typed captured-item `OnAction` callback
and optionally supply a captured artwork resolver. Internal semantic leases retain
bounded frozen rows and resolve the current parent input-owner path at admission. Shared logical binding
resolution preserves row versus ancestor shortcuts, menu option availability,
nested scopes, pinned ownership and modal input suppression. Typed item execution
uses the existing serial action queue with query-aware repeat identity; no second
queue was added. Already-admitted actions retain their original values when visual
or data leases retire. Artwork lookup remains separately bounded and rejects late
retired results without discarding valid parent artwork merely because a modal is
active.

Validation: 134/134 SDK checks, 14/14 API compatibility checks, 6/6 indexed-runtime
and 5/5 indexed-bridge regressions. The first runtime invocation used `dotnet` with
the DLL, which made its self-spawning fixture attempt to launch dotnet as a worker;
rerunning the built test executable passed. This was an invocation correction,
not a production-code fix. No candidate was installed/launched in this pass.

The semantic lease API is currently internal to the worker SDK. Runtime, bridge
and session delivery/release/interaction routes plus native item templates and
first-party widget conversion remain incomplete. Existing range data still grants
no remote row action/artwork authority. The migration remains active and unmerged.


## Worker/runtime semantic lease transport

The actual worker pipe now supports acquisition/release, typed item input and
independent artwork requests. An internal disposable client lease owns the exact
captured process session, prevents use after disposal and never starts/reaches a
replacement worker. Cancellation after delivery and failed reply writes reclaim
worker retention. Range/artwork lanes share bounded cancellation/drain mechanics
while retaining independent four-request budgets; input remains on the existing
serial queue. Release and cancellation acknowledgements are strictly typed.

A code review corrected the distinction between retained data and current input:
leases retain immutable row data, not a frozen parent shortcut path. Input carries
a current worker snapshot sequence and scope. Page-level bindings are resolved
from that snapshot; stale input cannot execute a changed page command. The bridge
must still compare the user's origin frame with its current declarations before
forwarding that sequence. An admitted action may itself replace its query without
invalidating its admission acknowledgement.

Twelve indexed-runtime scenarios pass (six new lease scenarios), including real
worker acquisition/invocation/artwork, cancellation/reload, release during artwork,
worker replacement, late cancellation and failed delivery. Ten existing focused
action/teardown scenarios, five indexed-bridge tests, forty-seven presentation
session tests and the 134 SDK checks also pass. The replacement-worker test first
attempted unload while Interactive; its setup now performs the required Background
transition before unload. No production workaround was added for that test error.

Bridge/session semantic-lease tables and native interactive item templates are
still unimplemented. Existing bridge range reads remain data-only. No candidate
was installed/launched and nothing was merged to main.

Final transport review found that a rejected duplicate acquisition cancelled its
original successful demand and released the existing lease. Terminal worker
rejections now bypass withdrawal; cancelled or unadmitted successful replies still
withdraw their exact demand. A real-worker regression verifies the original row
action and artwork remain valid. All thirteen indexed runtime checks pass, and the
WinUI analyzer checkpoint build passes without warnings or errors.

## Interactive indexed data path and native content refresh

Bridge and presentation-session ownership now connect the runtime's semantic
leases end to end. The registry validates origin/current row and parent bindings,
keeps unchanged data across modals, and retires exact owners on query/projection/
worker changes. Active input/artwork operations keep registrations alive until
they drain. The session exposes disposable immutable row ranges with typed input
admission and artwork APIs. Independent provider/cancellation/control admission
and bounded release batches prevent bulk eviction from saturating the bridge;
separate release admission/reply deadlines cover a stalled ordinary control lane.

Validation: 64 session tests, 13 indexed bridge scenarios (including both pipes
and a real worker), four existing dispatcher and eighteen registry scenarios pass.
The full WinUI analyzer build has zero warnings/errors. New fixture errors were
corrected without changing production behavior: opaque test artwork lacked an
accessibility label, and an end-to-end test used the shortcut label overload
instead of the explicit `actionId` argument.

The native indexed source now owns/retires asynchronous page lifetimes and
refreshes content without replacing logical slots. Thirteen native UI checks
pass, including seven dispatcher lifetime scenarios. At index 600,000, refreshed
content preserved focus and exact scroll offset; the million-row source retained
96 slots and 21 native containers with no enumeration. This is a source fixture,
not production-widget performance evidence.

Evidence: `artifacts/winui-indexed/lease-final-*.log`,
`artifacts/winui-indexed/lease-complete-build.log`,
`artifacts/winui-indexed/native-ownership-ui/`, and
`artifacts/winui-session-leases/admission-tests.log` with corresponding binlogs.
Validation windows were closed. No controller physical acceptance was requested.

Next: connect these leases to native item templates, constrain collection layout,
validate navigation to unrealized items, and adopt real YouTube/Playnite sources.
The remaining styles, modals, media, pinned surfaces, animations and shell work
are still required for the full migration. Nothing was merged to main.

## Native interactive collection templates

Owned row ranges are now connected to the shared WinUI presenter through native
ListView/GridView controls and compiled item templates. Rich row contents reuse
the ordinary declaration renderer; native containers own interaction and focus.
Rows receive the bridge's computed styles, and opaque artwork shares bounded
provider admission with range loading. Basic native Grid auto/star layout keeps
fill collections constrained. Content replacement retains old image pixels until
new artwork is decoded, and realized row resource work drains during disposal.

An actual semantic-controller probe established that pure spatial focus search
could lose moves at realization boundaries (60 Down inputs reached item 49).
The collection adapter now resolves the logical target, coalesces frame bursts,
and asks WinUI to realize/scroll/focus its native container. Current probes reach
item 60 after 60 moves, item 50 after reversing 20 moves from 70, and preserve the
grid column. Window close now enters the same asynchronous cleanup path from
both the close button and system close; test bridge processes terminate cleanly.

Validation uses `--indexed-validation-pipe` with the test assembly's
`--serve-indexed-validation` mode, never a physical controller owner or installed
community widget. The full session suite passes 71 tests; indexed bridge checks
pass 15 tests. Existing native controls/focus/source checks pass 16/15/13 tests.
The native widget script covers eleven checks, including actual invocation count,
styled artwork, deep refresh, forward/reverse/burst navigation, cancellation of
superseded focus and GridView.
Evidence is under `artifacts/winui-indexed/widget-ui-checkpoint/`,
`templates-checkpoint-build.log`, `worker-checkpoint-tests.log`, and
`artifacts/winui-session-leases/styles-final-tests.log`.

The presenter remains incomplete: full styles/pseudo-state presentation, live
theme changes for retained leases, grouped collections, logical collection focus
requests, modals, popups, media, pinned surfaces and production-shell integration
are not proven. Production YouTube/Playnite sources still need conversion and
physical/performance validation. The collection document records concrete YouTube
playback and SDK contract requirements discovered in this pass.

## Logical collection entry and public author testing

Protocol 61 extends the existing one-shot group entry with an exact indexed query
occurrence. The host waits for native realization and verifies the loaded item key;
stale generations and wrong keys cannot focus another row. Initial collection focus,
explicit native neighbors and remembered entry focus native item containers. A
bounded identity-only history survives removed page controls. Row and parent actions
receive worker-generated logical focus metadata. Public indexed author test helpers
exercise real SDK acquisition, queued actions, cancellation and artwork paths.

Validation: 14 indexed SDK tests, 15 indexed bridge tests, 13 native widget checks
(including a ten-case logical-focus sequence with deliberately delayed row data),
and 15 existing native focus-policy checks pass. The WinUI analyzer build is clean.
Protocol header and SDK API baseline are regenerated; 14 SDK compatibility tests
pass. Native evidence is under `artifacts/winui-indexed/focus-disabled-final-ui/`
and `focus-policy-regression/`. One fixture initially used reserved View for a
test command; changing that fixture to RightStick restored valid worker startup.

Physical review confirmed navigation after Guide reopen but found a Guide problem
after Alt+Tab. The window remained alive and visible behind another application.
The validation shell now hides only while it owns foreground; otherwise Guide
requests show/activation. Live logs proved Guide delivery while both Activate and
a direct foreground request failed to reacquire the visible window. The native
host's existing bounded foreground acquisition is now shared through platform
ABI 5, with same-process/thread HWND validation and actual ownership confirmation.
Thirteen native ownership checks and nineteen managed tests pass. The WinUI caller
uses that adapter. A live physical Guide trace now confirms reactivation from a
visible background window to the WinUI process, then successful hiding on the next
press. The user confirmed activation/navigation/A/hiding, then reported that the
focus cue disappeared after Alt+Tab. Foreground acquisition now leaves control
focus to each frontend: the legacy host focuses its HWND, while WinUI explicitly
reasserts Keyboard focus on its existing leaf. Controller replay passes with
HasKeyboardFocus true on the retained Second button. The user then physically
confirmed that the focus highlight and input both work after Alt+Tab/Guide.
This closes the validation-shell activation/focus defect, not production controller
integration for all widget surfaces. Earlier controller
replay passed cold entry, navigation, one action per press, hide/show and retained
focus. Bounded asynchronous diagnostics preserve native Guide delivery and actual
foreground/visibility; activation is never inferred from a show request alone.
Evidence: `artifacts/winui-controller/shared-activation-physical.log`. The legacy
host now calls the same adapter but was not rebuilt during this checkpoint; the
installed native product remains unchanged.

Full migration remains incomplete. Grouped collections, production widget adoption,
complete styling/themes, modal/popup integration, media/pinned surfaces, global
animations and the production shell still require implementation and validation.

## Native grouped range demand

Executed grouped/flat/adapted probes confirm that CollectionViewSource preserves
indexed inner access but hides IItemsRangeInfo from ListViewBase. The small
NativeGroupedRangeView adapter restores direct range admission while delegating
the actual ICollectionView behavior to WinUI. One flat indexed query supplies
all group slices, avoiding per-group retention and provider budgets.

The three modes pass 7/9/10 UI checks, with five additional adapter contract
scenarios. At 30,000 logical items, deep focus/refresh/reversal required no full
enumeration, with 17 observed native containers and a peak of 96 data slots.
This is correctness evidence; real widget artwork and controller performance
remain unproven. See `winui-grouped-collections-assessment.md` and
`artifacts/winui-grouped/checkpoint/`. Public grouped declarations, production
row/header templates and first-party adoption remain next work.

## Grouped declarations connected to worker rows

Protocol 62 and the SDK's `.Grouped(...)` now describe bounded contiguous groups
over one flat indexed query. The production collection presenter uses compiled
header templates, native observable group slices and the proven range adapter.
List and grid mode share the same leases; partial/empty groups preserve meaningful
controller targets. Header text changes retain view/focus/offset. SDK structural
updates now transport both group metadata and indexed source descriptor changes
atomically; the prior differ omitted descriptor changes.

Validation: 15 indexed SDK checks, 15 indexed bridge checks, 14 SDK compatibility
checks and 17 real-worker native UI checks pass. Native tests include seven grouped
focus/header cases and sixty list moves across group headers. Analyzer build is
clean; API baseline and protocol header are regenerated. Evidence:
`artifacts/winui-grouped/worker-checkpoint-ui/`, `sdk-groups-tests.log`,
`bridge-tests.log`, `compatibility-tests.log`. An earlier run observed an unexpected
viewport enlargement before grouped tests and failed its size assertion; a targeted
window/layout inspection and fresh full run did not reproduce it. That failed run
is retained under `worker-ui/`; it is not classified as a proven grouping defect.

The public group metadata and production row/header integration are complete for
this contract, but widget conversion and full theme/animation/overlay parity remain
outstanding. The new Playnite captured-service query path is undergoing its own
tests and has not replaced the installed widget's cursor UI.

## Playnite captured application queries

The application service now captures immutable ordered query values and exposes
bounded random-range projection with captured artwork authorization. Projection
does not populate the old retained-cursor artwork registry. Catalog admission and
four-slot artwork work are independent, sharing the existing bounded byte cache.
Disposal cancels/drains these lanes and rejects late legacy projection. The existing
Bridge integration, query rules and cursor UI are preserved while conversion proceeds.

All 28 PackageRuntimeTests pass, including nine new tests for random access,
immutable values, authority, eviction, independent refresh, concurrency,
cancellation and retirement. Evidence: `artifacts/winui-playnite-capture-retirement-tests.log`.
This is service correctness evidence, not WinUI Playnite appearance/performance.
Next: final widget projection, captured actions and logical modal return targets,
then complete presenter support and actual overlay integration.

## Pending indexed focus across presentation changes

Review found that rebuilding a collection for layout/group partition changes
cancelled an already-consumed exact entry while its row was loading. The collection
now retains that logical intent when its source/query owner remains unchanged and
restores it after rebuilding native presentation. Query replacement, disabled
scope, request withdrawal and superseding input retain their cancellation behavior.

The real-worker sequence now includes delayed exact entry during list/grid changes
and regrouping; all 17 UI checks pass with twelve logical-entry cases. The 15
indexed bridge tests and clean WinUI analyzer build also pass. Evidence:
`artifacts/winui-grouped/entry-checkpoint-ui/` and `entry-final-bridge-tests.log`.
A test fixture initially used a reserved D-pad shortcut; changing its test-only
command to X restored valid worker startup. A separate interrupted artifact write
was an inspection/file-sharing conflict, not a UI result; the clean full run is
the accepted evidence.

## Retained native background and focus-presentation checkpoint

Native presentation surfaces now consume the shared logical source policy. The
background uses an ImageBrush so artwork intrinsic size cannot enlarge the collection
viewport. Focus fragments are presentation-only subtrees. Indexed contributions retain
bounded data through the existing range source, independently of visual realization;
content refresh updates the same slots and removal releases demand.

Artwork identity includes both the range lease and exact row key. A real-worker
regression verifies adjacent rows sharing one lease and handle decode different images,
and that a delayed former row cannot replace the newly selected artwork. Missing
artwork clears obsolete pixels. Session artwork authorization now includes authored
focus/default fragments.

Validation: analyzer build clean; 73 session tests, 15 indexed bridge tests, 18 native
worker UI checks (including eight surface cases), ten standalone surface-policy checks,
16 explicit retention checks and seven existing ownership checks passed. Screenshot
review confirms the fixture's constrained list and presentation placement; it is not
a production-theme or performance acceptance. Evidence: artifacts/winui-surfaces/.

This checkpoint implements basic selection, retention and layout. Full WRSS styling,
depth, animation and production-widget adoption remain incomplete. Test windows closed.
Parallel work continues in separate Playnite, shared-controls and styling/motion
worktrees; changes integrate only into codex/winui3-frontend, never main.

## Native glyph adapter checkpoint

ControllerGlyph and semantic Icon declarations now use native FontIcon controls.
Controller prompts reuse the existing Kenney Xbox/PlayStation fonts and PromptFont
Guide fallback, with their licenses packaged; no system font registration or new
controller reader is introduced. Host family changes update existing presenters,
including realized fragments, without changing worker snapshots. Unknown input
family retains the last observed family. Semantic icons use Segoe Fluent Icons;
package SVG transport/presentation and final themed glyph sizing remain outstanding.

Seven native glyph checks pass across all 45 declared controller/semantic symbols,
including family changes, accessible labels, fallback font selection and noninteractive
behavior. Packaged font rendering was inspected in artifacts/winui-surfaces/glyphs-fixed.png.
The unstyled validation gallery is not production-theme acceptance.

## Parallel integration pass, 2026-09-28

- Production Playnite Browse now uses a complete captured indexed query instead of
  its retained cursor window. Query/authority/source publication is coordinated;
  exact game targets and logical modal return survive content updates. The integrated
  widget suite passes 149 tests; the application lane also passes 28 runtime tests.
  Home still uses its previous rail and awaits conversion. This sample requires the
  indexed frontend; it has not been installed over the native product.
- Native Select passes 17 lifecycle/input checks. A shared normalized-button entry
  gives popups precedence, sends indexed actions through owned row leases, and uses
  the existing worker input pipeline for ordinary shortcuts. The real-worker input
  probe passes its three checks.
- Widget-local modals pass 21 standalone native checks and screenshot review after
  correcting fixture geometry/default panel fill. The real-worker indexed-parent
  probe is still under investigation: one run did not admit its first activation;
  standalone success does not close the indexed-parent gate.
- Motion policy preserves global settings and uses native composition batches.
  Nine policy tests and 18 actual compositor/focus checks pass. Production section,
  modal and focus wiring remains incomplete; no smoothness acceptance is claimed.
- The real Clock worker still passes ten transport/action/focus/shutdown checks.
  Its presenter now binds artwork/session resources and retires before the bridge.

All changes remain on codex/winui3-frontend. Main, installed native packages and
controller ownership are unchanged. Shared styling and indexed-modal investigation
continue in their isolated lanes. No production WinUI candidate is ready yet.

## Integration validation follow-up and open row lifetime defect

Native styles pass 24 checks after desktop ThemeSettings and resource ownership
corrections; the supported subset and omissions are recorded in winui-styles.md.
The combined indexed test passed 20 checks before strengthening the final rendering
assertion. Screenshot inspection showed the final replacement-query row could have
focus but no text/artwork. The new 25th modal assertion reproduces that defect.

A diagnostic run found the row loaded, its semantic lease current, its payload key
correct, but its Content null. The failure is not missing game data. Delaying Unloaded
retirement did not resolve all repeated-modal cases. A stable parent-layer experiment
also failed the full lifecycle sequence and was removed from production changes;
its patch is retained under artifacts/winui-surfaces/stable-parent-experiment.patch.
The integration source retains the strengthened failing regression and bounded row
metadata diagnostics. Do not call the combined modal gate green or launch a physical
candidate based only on the earlier 24 modal checks.

Evidence: styled-combined-ui/17-indexed-modal.json, modal-row-diagnostic.json,
row-lifetime-ui/17-indexed-modal.json and modal-entry-ui/17-indexed-modal.json under
artifacts/winui-surfaces. The earlier intermittent first-A observation is also still
unresolved. Continue by inspecting the native item-template/rendered-row lifetime,
including why a live row can lose its presenter while its data owner remains valid.
Main and the installed native product remain untouched.

## Indexed modal lifetime and keyboard follow-up, 2026-09-28

The missing row content was caused by queued native Unloaded notifications arriving
after the same row was loaded again. Row retirement now checks its current IsLoaded
state; explicit disposal and genuine unload still retire it. Temporary per-row/image
tracing was removed after confirming the event ordering.

The subsequent repeated-modal timeout was a different defect, not missing artwork:
the live Image contained its decoded source, but the next A arrived during a content
refresh while the displayed row's lease was retired. The collection now retains one
bounded activation intent for the same focused item/action, then uses the replacement
lease. Navigation, another button, hiding, changed scope/query/action/availability,
or a two-second expiry cancel it. SDK/session stale-lease checks remain unchanged.

Fixture row images now use yellow/dark checkerboards distinct from the blue parent
background. Modal assertions verify decoded 8x8 cover images with nonzero displayed
size, and timeout diagnostics identify the actual pending condition. Visual inspection
of the final replacement-query row confirms both text and checker artwork.

Integration validation: analyzer and worker builds have zero warnings/errors; 16
indexed bridge tests, all 20 combined native worker checks (including all 25 modal
assertions), and 11 dedicated deferred-activation checks pass. One combined attempt
was interrupted by another worktree's package deployment and was inconclusive; its
replacement run passed. Evidence: artifacts/winui-surfaces/combined-row-fixed/,
integrated-activation/, and integrated-indexed-tests.log. Scripts wait for initial
worker readiness and preserve the original failure when cleanup finds an exited app.

Native TextEntry/controller keyboard is integrated with guarded commit/cancel,
native password semantics, local edit state, authority revocation and live prompt
fonts. Its 26 native checks also pass on the integration branch. Details and remaining
physical/production/theme validation are in winui-text-entry.md. These are correctness
fixtures, not production-widget performance or full migration acceptance. Main and
the installed native product remain untouched.

## Playnite Home indexed adoption, 2026-09-28

Home now uses captured indexed membership and demanded row rendering, with no Home
cursor/load-page path. It preserves the manual/title-match prefix, provider ordering,
unavailable saved entries, captured action/artwork ownership, and exact deep-item modal
return. Same-membership updates retain logical query identity; refresh explicitly
retires obsolete launch evidence. Author notes and migration-specific tests are updated.

All 155 Playnite widget tests pass on the integration branch (zero failures/skips).
Evidence: artifacts/winui-surfaces/home-integrated-tests.log and its unique build log.
This does not validate the actual Home rail's WinUI rendering or performance; production
presentation support and shell integration remain outstanding. No sample was installed
over the native frontend, and no changes were merged into main.
