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
Use `UI.Picker` for bounded single selection rather than disguising choices as
commands: it exposes one stable option ID per row, explicit selected state,
Up/Down neighbors, focus-safe unavailable state, and the same scope-owned B.
Use `UI.Scrubber` for media seeking instead of pairing a private Slider with
duplicate progress text. Its only focus stop is `id.slider`; Left/Right uses
the native Slider contract and the action receives an absolute requested
position in milliseconds. Elapsed and duration labels are formatted by the SDK
unless localized labels are supplied.

Call `.RequireControllerActivation()` on a Slider or Scrubber when ordinary
Left/Right navigation must not change its value. This opts into protocol v10:
A enters host-owned adjustment mode, Left/Right adjusts only while that mode is
active, and A or B exits it. Outside the mode, all directions navigate and the
control may declare horizontal focus neighbors. The activation-first contract
cannot also publish an A activation action through `.Activate(...)` or
`activationAction`; Disabled/Busy controls remain focusable but cannot enter
adjustment mode.

Use `UI.ResponsiveGrid(id, minimumColumnWidth, maximumColumns?, children)` for
a bounded set of peer cards/categories that should reflow across compact and
wide logical viewports. It is protocol v8: the host derives row-major columns
from final DIP width, authored gap, a 44–1600 DIP minimum column width, and an
optional 1–32 column cap. The Grid is not focusable; children keep their stable
IDs and normal focus graph. Put unbounded collections in a host-owned Scroll.
Settings uses this same public helper for its root category surface.

Use `element.VisibleWhen(ResponsiveVisibility.CompactOnly)` and
`ExpandedOnly` when the same semantic view needs substantially different
composition below the host's compact breakpoint (less than 960 DIPs wide or
540 DIPs high). `UI.ResponsiveBranch(...)` is the equivalent non-fluent form.
This protocol-v9 modifier adds no layout container and preserves the wrapped
element's stable ID. The inactive element and its complete subtree are absent
from native layout, paint, pointer hit testing, controller focus, shortcuts,
and accessibility. Keep the root unconditional and give mutually exclusive
branches distinct stable IDs; use ordinary responsive Grid/Row wrapping when
the content hierarchy itself does not need to change.

Use `UI.CodeText(text, id, accessibilityLabel?)` for bounded diagnostics or
commands. It emits one nonfocusable Text node with `.gbar-code-text`, preserves
whitespace, and caps content/accessibility text at 4,096 characters. It does
not create selection, a copy command, scope, shortcut, or background work;
provide a separate explicit Button when copying matters. The built-in theme
uses single-family `Consolas` with up to eight wrapped lines. Native GBSS does
not yet implement CSS font fallback stacks or packaged font loading.

For indeterminate work that lasts long enough to be visible, use
`UI.LoadingIndicator(id, accessibilityLabel, size)`. It is a protocol-v5,
host-rendered primitive with bounded Compact, Standard, and Large sizes. It
never enters controller focus, accepts no action or interaction state, and
does not require widget polling. The native host animates its lightweight arc
only while it is visible; reduced-motion mode keeps the same accessible status
as a static indeterminate arc. Keep fast cached transitions visually quiet
instead of flashing a spinner for a single frame.

For rich media/application rows, use `UI.MediaTile` or `UI.AppTile`. Both are
protocol-v7 `ActionSurface` compositions: the complete tile is the sole focus,
pointer, pressed, and A-action target, while generated artwork/copy children are
presentation only. `TileArtwork` accepts one semantic glyph, credential-free
HTTPS image, or bounded inline PNG. Custom rich actions may use
`UI.ActionSurface`, but its subtree is limited to 8 direct children, 32 total
descendants, and four relative levels and cannot contain actions, focus,
scopes, shortcuts, scrolling, interaction state, Buttons, Sliders, or another
ActionSurface. Preserve generated `id.artwork`, `id.content`, `id.title`,
`id.subtitle`, `id.metadata`, and `id.state` suffixes and `gbar-action-surface`/
`gbar-tile` classes when adding widget-specific classes.

Use `UI.Toast` for brief feedback that must not steal focus. Tone is paired
with visible text, duration is bounded to 2–30 seconds (five by default), and
the widget—not the host or component—owns removal through normal lifecycle-
aware state. Do not create a timer or hidden worker solely for Toast animation;
themes must suppress or shorten motion when reduced motion is active.

Use the protected `Operations` coordinator for bounded asynchronous work:

```csharp
Operations.RunSingleFlight("refresh",
    async context => await RefreshAsync(context.CancellationToken));
Operations.RunLatest("page",
    async context => await LoadPageAsync(context),
    WidgetOperationLifetime.Active);
Operations.RunSerial("save",
    async context => await SaveAsync(context.CancellationToken),
    WidgetOperationLifetime.Widget);
```

`RunSingleFlight` joins duplicate work, `RunLatest` cancels/supersedes stale
work while keeping delegates non-overlapping, and `RunSerial` preserves FIFO.
`Active` spans Visible/Interactive, `State` ends on every state transition, and
`Widget` ends at Destroying. Ending lifetimes are canceled and drained before
their lifecycle callbacks run. Check `WidgetOperationContext.IsCurrent` before
committing latest-wins results and always honor its cancellation token.

The returned handle separates admission (`Completed`, `Started`, `Joined`,
`Replaced`, `Enqueued`, `RejectedInactive`, `RejectedCapacity`) from completion
(`Succeeded`, `Canceled`, `Superseded`, `Failed`, `Rejected`). Completion tasks
never fault; `Completed` means no work had to be scheduled, while failed
results carry the exception and also raise
`OperationFailed` once. Limits are 32 busy keys, 64 total active/pending
operations, and 16 pending Serial operations per key. Do not retry capacity
rejection in a tight loop or mix a key's policy/lifetime while it is busy.
`IsBusy`, `BusyChanged`, `Cancel`, `WhenIdleAsync`, and `WhenAllIdleAsync`
support UI and deterministic tests. Busy-edge changes auto-invalidate the
widget; event subscribers can observe the same edge without owning that
invalidation.

Use `CreateModel<TState>(initialState)` in the widget constructor when several
fields form one immutable render state:

```csharp
private readonly WidgetModel<PlayerState> _model;

public PlayerWidget()
{
    _model = CreateModel(PlayerState.Initial);
}

public override WidgetView Render() => BuildView(_model.Value);
```

`Set` and `Update` serialize changes and invalidate exactly once only when the
configured equality comparer reports a different value. `Snapshot` reads the
value and revision atomically. The result-bearing `Update` overload can derive
an operation input from the exact state transition it commits. Update delegates
run under the model lock and must be quick and side-effect free; use immutable
records and never mutate a published reference in place. `Changed` is a
contained diagnostic/test observer, not a second state store. After Destroying,
model changes no longer invalidate the widget.

For a remote mutation with immediate UI feedback, create one
`WidgetOptimisticCommand<TState,TRequest,TExecution,TResult>` over that model:

```csharp
_playback = CreateOptimisticCommand(
    "player.playback",
    _model,
    new WidgetOptimisticCommandOptions<State, Request, ProviderCommand, Result>
    {
        Policy = WidgetCommandPolicy.Latest,
        Lifetime = WidgetOperationLifetime.Active,
        Apply = (state, request) => new(
            state.Project(request),
            ProviderCommand.From(state, request)),
        Execute = (command, token) => Provider.ControlAsync(command, token),
        Reconcile = (current, command, result) =>
            current.MergeResult(command, result),
        Rollback = (current, baseline, command) =>
            current.RemoveProjection(baseline, command),
        MapError = _ => new WidgetCommandError(
            "playback_failed", "Playback could not be changed."),
    });

var handle = _playback.Run(request);
```

`Apply` runs inside the model's serialized update and returns both projected UI
state and the exact provider input derived from that same revision. Keep it
quick and side-effect free. SingleFlight joins an existing command without a
duplicate projection, Latest cancels stale work and retains the first baseline
through replacements, and Serial applies each projection only when its bounded
FIFO turn begins. Inactive/capacity-rejected requests and joined SingleFlight
requests do not call `Apply` or mutate the model. A projection may set
`ShouldExecute: false` to publish an admitted domain state without invoking the
provider.

The helper reuses `WidgetOperations`, including Active/State/Widget lifetime
ownership, cancellation, draining, admission/completion handles, busy state,
and current-attempt checks. `Reconcile`, `Rollback`, and optional `Fail` receive
the current model value plus command data, so callbacks can remove only their
own projection while preserving provider events that arrived in flight. The
SDK cannot infer that merge and does not automatically retry mutations.

Map provider exceptions to `WidgetCommandError`: its code is a stable
identifier and its message is limited to 256 visible, non-control characters.
A null mapper result or throwing mapper becomes `WidgetCommandError.Unexpected`.
Without a custom `Fail`, failure uses `Rollback`; a custom callback can apply
the bounded error to presentation state. Media Sessions is the production
reference: it uses SingleFlight transport coordination, immediately projects
Play/Pause, and restores only the affected playback projection on failure or
cancellation.

For a bounded offset/limit collection, create one
`WidgetPagedResource<TItem>` with `CreatePagedResource` instead of maintaining
page tasks, generations, caches, and focus calculations independently. Supply
`WidgetPagedResourceOptions<TItem>` with `PageSize`, `MaximumCachedPages`,
`MaximumCachedItems`, `LoadPage`, `MapError`, and one or more
`WidgetPagedViewport<TItem>` mappings. A viewport maps its stable Scroll ID and
absolute item indexes to stable focus IDs.

Call `EnsureLoaded`, `Refresh`, `Move`, or `Retry`; inspect the immutable
`Snapshot`; render the current page through `resource.Paginate(scroll)`; and
route `resource.TryHandlePagination(action, out operation)` before unrelated
actions. The resource owns Latest coordination, state-change invalidation,
stale-result rejection, safe errors, entering-edge `RequestedFocusId`, and a
deterministic bounded LRU. A cache hit or no-op boundary returns
`WidgetOperationAdmission.Completed`. `ClearRequestedFocus` acknowledges a
consumed entering-edge focus request. `Reset` cancels and clears the resource;
its optional non-invalidating form is only for one immediately composed widget
state update. `WhenIdleAsync` supports deterministic tests.

Statuses are `NotLoaded`, `Loading`, `Ready`, `Refreshing`,
`LoadingAdjacent`, and `Error`. Bounds are page size 1–100, cached pages 1–8,
cached items 1–512 (and at least one page), pagination threshold 1–8, and error
messages up to 256 visible characters. The lifetime defaults to `Active`, cache
duration to five minutes, and last-good retention to enabled. This API is
offset-based: cursor paging, append/infinite feeds, and a generic
`WidgetResource<T>` are not implemented.
Sparse pages are supported for providers that filter unavailable server
entries; next-page offsets use the server page limit rather than rendered item
count. An empty non-terminal page still needs a focusable widget placeholder so
the host can emit the next focus-edge action.

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
