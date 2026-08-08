# Widget SDK developer testing

Widgets access platform providers through the protected `HostServices` property.
Production services are attached exactly once by `WidgetWorkerBootstrap` before
`OnCreatedAsync`; a widget cannot replace them after creation.

For dense controller surfaces, use `UI.VerticalScroll` or
`UI.HorizontalScroll`; the host owns clipping, focus-follow, and restored
offsets. A view may also provide bounded `WidgetSurfaceHints` so compact and
wide widgets communicate a useful shape without assuming a monitor or fixed
window. Both are optional snapshot protocol-v2 features. Widgets that use
neither continue emitting the package-API-1-compatible protocol-v1 snapshot.
See [Declarative UI](../../docs/declarative-ui.md) and
[Display and resolution](../../docs/display-and-resolution.md).

For settings and contextual commands, prefer the public controller composites
over custom focus routing. `UI.SettingsRow` keeps long supporting copy separate
from its one stable `id.action` target. `UI.ActionSheet` accepts 1–32 stable
items, owns a vertical focus-follow Scroll, and binds B on its nested scope;
publish that scope as `WidgetView.ActiveInputScopeId` while it is open. Disabled
and Busy actions stay focusable and are suppressed by the standard router.

For indeterminate work that lasts long enough to be visible, use
`UI.LoadingIndicator(id, accessibilityLabel, size)`. It is a protocol-v5,
host-rendered primitive with bounded Compact, Standard, and Large sizes. It
never enters controller focus, accepts no action or interaction state, and
does not require widget polling. The native host animates its lightweight arc
only while it is visible; reduced-motion mode keeps the same accessible status
as a static indeterminate arc. Keep fast cached transitions visually quiet
instead of flashing a spinner for a single frame.

Unit tests can use the supported transport-free fake instead of reflection,
internal APIs, named pipes, or hand-written JSON:

```csharp
var services = new WidgetTestHostServicesBuilder()
    .WithResponse(
        WidgetAudioCapabilities.GetSessions,
        (IReadOnlyList<WidgetAudioSession>)[
            new("game", "Game", 0.75, false, true)])
    .WithEvents(
        WidgetAudioCapabilities.SessionsChanged,
        [new WidgetAudioSessionsChanged([])])
    .Build();

var widget = WidgetTestHost.Attach(new MyWidget(), services);
await WidgetTestHost.InitializeAsync(widget);
await WidgetTestHost.SetLifecycleStateAsync(
    widget, WidgetLifecycleState.Interactive);
// ...assert UI and behavior...
await WidgetTestHost.DestroyAsync(widget);
```

Pass `IsAvailable: false` in `WidgetAudioSessionsChanged` to simulate a live
provider outage. This is not equivalent to an available event with an empty
session list; widgets should render distinct recovery and empty states.

Use `WithHandler` for request-dependent or asynchronous responses and
`WithEventStream` for a live deterministic stream. The builder takes an
immutable snapshot at `Build`. Missing operations/events fail with
`WidgetCapabilityException` and `test_handler_missing`; mismatched generic
contracts fail with `test_contract_mismatch`. Duplicate handlers are rejected.
`WidgetTestHost.Attach` uses the same one-time pre-creation gate as production,
so double attachment and attachment after `OnCreatedAsync` remain invalid.
Its lifecycle helpers call the same host-validated transitions as production;
`Created` and `Destroying` cannot be requested through `SetLifecycleStateAsync`.

When a widget combines a current snapshot with future events, open the typed
acknowledged subscription first (`OpenSessionsSubscriptionAsync` or
`OpenStatusSubscriptionAsync`), fetch current state second, then consume
`ReadAllAsync`. A successful open means host registration is complete, so the
coalesced full-snapshot event buffer closes the fetch/subscription race.
`WatchSessionsAsync` and `WatchStatusAsync` remain compatibility helpers for
event-only consumers.
