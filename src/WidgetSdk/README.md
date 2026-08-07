# Widget SDK developer testing

Widgets access platform providers through the protected `HostServices` property.
Production services are attached exactly once by `WidgetWorkerBootstrap` before
`OnCreatedAsync`; a widget cannot replace them after creation.

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
