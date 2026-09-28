# WinUI migration implementation status

2026-09-27; branch `codex/winui3-frontend`. Main and the installed native candidate
are untouched. Migration is active and incomplete.

## Current implementation

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
