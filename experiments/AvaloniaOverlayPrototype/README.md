# Avalonia Overlay Prototype — AVP-003

This remains an isolated .NET 10/Avalonia 12.1.1 feasibility process, not a
production migration. AVP-003 shapes one representative vertical slice around
CommunityToolkit.Mvvm 8.4.2, compiled AXAML bindings, ordinary Avalonia
virtualization/UIA, and an experiment-owned semantic remote boundary. It does
not import the production Widget SDK, renderer, GBSS, catalog, or package
runtime.

## Production-shaped responsibility split

`PrototypeComposition` is a small manual composition root. Immutable shell and
page state plus commands live in CommunityToolkit view models. `MainWindow`,
`PrototypeShellView`, `SemanticInputRouter`, `FocusNavigator`, and
`PageTransitionPresenter` retain window lifecycle, controller input, focus,
navigation policy, and native transition ownership. The complete before/after
map and measured DI decision are in
[`architecture-decision.md`](architecture-decision.md).

AXAML bindings are compiled project-wide with typed view/data-template scopes.
The deliberately separate
`tests/fixtures/InvalidCompiledBinding` project binds a missing property and
must fail with `AVLN2000`; it is excluded from the real solution so the
production-shaped slice remains buildable.

The visual base remains Avalonia's built-in Fluent theme with project-owned
styles. AVP-003 adds no ReactiveUI, DynamicData, generic host, Serilog, EF,
FluentAvalonia, SukiUI, or other navigation/theme framework. Page changes use
Avalonia's native `TransitioningContentControl` with `CrossFade`; retained
start/midpoint/completion samples remain an Avalonia-surface diagnostic rather
than a physical-compositor claim.

## Virtualized launcher and remote projection

Game Launcher is an ordinary selectable `ListBox` backed by a
`VirtualizingStackPanel` and 10,000 typed items. No manual container cache or
custom accessibility tree exists. Prepared standard `ListBoxItem` containers
receive stable semantic AutomationIds; the presentation page scrolls/realizes
only the requested item and the shared keyboard/controller semantic router
moves or invokes that focused container. Last focused semantic identity and
scroll position survive page replacement and container recycling.

`RemoteWidgetContract.cs`, `FakeRemoteWidgetEndpoint`, and
`RemoteWidgetProjection` contain no Avalonia, XAML, control, view-model, or UI
thread types. The projection owns one active generation, latest-wins request
cancellation, last-good retention on failure, exact typed actions, and
deactivation cancellation. `GameLauncherViewModel` adapts that state into typed
immutable presentation records.

The accepted AVP-002 input behavior remains: XInput is still a deliberately
narrow Windows prototype adapter because it gave maintained deployment and
deterministic reconnect/repeat semantics without creating another focus
system. Its legacy limitations remain explicit: four compatible slots, no
durable device identity, and incomplete coverage of non-XInput controllers. It
is not the production GameInput decision.

## Focused verification and evidence

From the repository root, run the bounded final Release build, invalid-binding
proof, and 20-test MSTest.Sdk 4.3.2 unit/headless/UIA suite once:

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Verify-Avp003.ps1 -TimeoutSeconds 180
```

The suite covers immutable commands, UI-neutral remote types, delayed/latest-
wins/failure/action/deactivation behavior, bounded 10,000-item realization,
semantic identity and focus/scroll return, shared controller movement, accepted
tray/slider/lifecycle behavior, real render-scale fixtures, native transition
samples, and standard Windows UIA List/ListItem/Scroll/Selection semantics.

After the clean AVP-003 commit, run exactly one ordinary Windows measurement:

```powershell
powershell -NoProfile -File .\experiments\AvaloniaOverlayPrototype\scripts\Measure-Avp003.ps1 -TimeoutSeconds 90
```

The ignored exact-commit runtime and measurement are retained under
`experiments/AvaloniaOverlayPrototype/artifacts/avp003`. The measurement records
source commit, executable ProductVersion/SHA-256, package versions, startup,
visible/hidden private memory and CPU, page-switch latency, native transition
samples, total/realized items, semantic focus before/after recycling,
focus/scroll return, and exact fake action identity.

## Visible planner/user launch

```powershell
& .\experiments\AvaloniaOverlayPrototype\artifacts\avp003\runtime-win-x64\AvaloniaOverlayPrototype.exe
```

Physical controller compatibility/feel, focus rings, transparency,
mixed-monitor behavior, and the Windows compositor verdict remain planner/user
checks. GPU timing remains unavailable without an authorized ETW/PresentMon
lane. The 250 MiB value remains an initial comparison target rather than an
acceptance gate; evidence also states whether memory remains below the user's
roughly 500 MiB unacceptable region. AVP-004 is not included.
