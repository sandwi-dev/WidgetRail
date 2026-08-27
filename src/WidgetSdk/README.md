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

Pinned widgets reuse the same host ownership. `WidgetView.PinnedLayouts`
accepts legacy size-only profiles and protocol-v21 declarative projections
created with `WidgetView.PinnedLayout(...)`. A projection supplies only its
bounded root, surface, input scope, and initial focus. The host injects Full
widget, validates each projection plus the aggregate catalog, and atomically
selects one on the single pinned surface. Override
`OnPinnedLayoutSelectionChangedAsync` only to observe package data demand;
null revokes it and never grants host authority.

Widgets that need one embedded media rectangle may set the optional
`WidgetView.EmbeddedMedia` protocol-v22 property. The declaration contains a
stable surface ID and accessible name, bounded surface/aspect hints, a closed
typed command set, and a finite inventory of package-local assets. Every asset
path must be normalized and declared in the signed package inventory; the host
revalidates its length and digest before creating the native surface. The host
owns the WebView2 controller, origin, bounds/DPI, focus and input routing,
accessibility boundary, visibility, fault handling, and teardown. Widgets own
only their provider adapter assets and typed state. The contract intentionally
does not expose navigation, DOM access, script execution, arbitrary URLs, or a
second HWND/input owner. Widgets that omit `EmbeddedMedia` retain their existing
snapshot and rendering behavior.

Protocol v23 adds `UI.MediaViewport(surface, id)`, a provider-neutral native
layout leaf that binds the one current `WidgetView.EmbeddedMedia` declaration
into the ordinary declarative tree. Taffy layout, WRSS/GBSS styling, clipping,
accessibility, and responsive geometry remain host-owned; the embedded browser
supplies only the pixels inside the committed viewport. Exactly one viewport
must reference the current surface identity. Missing, duplicate, or mismatched
bindings fail closed, and semantic previews display a deterministic native
placeholder rather than creating WebView2.

Visible media chrome remains ordinary declarative UI. Host-native buttons may
bind their action IDs to `host.embeddedMedia.togglePlayback`,
`host.embeddedMedia.seekBackward`, `host.embeddedMedia.seekForward`,
`host.embeddedMedia.previous`, `host.embeddedMedia.next`, or
`host.embeddedMedia.back`; the host admits those actions only against the exact
current widget, surface, viewport, input scope, focus, sequence, and declared
closed command set. The browser never receives the widget focus graph or raw
controller keys. Overlay-root B remains host Back, while ordinary pinned B
retains the selected-projection widget route; the compact-pinned exception is
described below.

Media widgets choose their own quick-seek increment for authored overlay
controls or use the existing Slider absolute-value action for direct seeking.
Protocol v25 additionally lets a widget opt into the host-owned compact pinned
presentation with `CompactPinnedPresentation`. The pin keeps the same
`MediaViewport` and media session, removes the authored title/transport rows,
and overlays one native themed seek bar that hides during passive playback.
`CompactPinnedSeekStepSeconds` defaults to 10 and accepts finite values from 1
through 60 seconds. X toggles playback; LT/RT rewind or forward from the current
position by that interval; A does not enter a hidden seek mode; D-pad and left
stick navigation do not seek; B exits compact controller interaction to
click-through; LB/RB select Previous/Next only when those commands are declared;
View retains the host tray route. Authors cannot remap these physical controls.

For selection-driven data, create an optional per-widget
`PinnedLayoutHandle` with `CreatePinnedLayoutHandle(id, name, surface,
initialFocusId, activeInputScopeId)`. Those stable values are registered once;
each render supplies only its current root through `handle.Present(root)`. The
handle exposes `IsSelected` and a selection-scoped cancellation token. The SDK
updates the handle before the compatible low-level callback, cancels the token
on deselection/replacement/teardown, and invalidates once when effective demand
changes. The public `WidgetPinnedLayoutTestHost` can deterministically select,
restore, revoke, replace, and route actions against these projections without a
native window. Existing `WidgetView.PinnedLayout(...)` and callback-only widgets
remain supported.

Keep returning complete immutable `WidgetView` values on every render. When a
protocol-v18 host explicitly negotiates atomic presentation updates, the SDK
automatically compares stable element IDs and chooses a bounded typed update or
a complete checkpoint; widget code does not construct patches, track base
sequences, or branch by sandbox/full-trust runtime. Identical views become a
validated no-op batch, while missing/stale bases, unstable identity, structural
ambiguity, and size limits fall back to a checkpoint. Hosts that do not opt in
continue receiving the same full snapshots.

Use `UI.TextEntry(value, placeholder, action, id, maximumLength)` for bounded
controller-first text. The native host owns keyboard/controller editing, clear,
backspace, commit/cancel, focus restoration, high-contrast colors, and the UIA
edit surface. The widget receives only the final value through
`WidgetActionEvent.CommittedText`; raw keys, HWNDs, and intermediate edits never
cross the worker boundary. Values are capped at 96 characters and negotiate
protocol v15 only when authored. `.Disabled()` preserves a stable focus stop
while suppressing activation.

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

Focusable buttons, sliders, scrubbers, and action surfaces can opt into
protocol-v13 responsive focus continuity with `.PersistFocusAs(id)`. Share that
ID only across mutually exclusive presentations of one logical destination;
action IDs remain independent routing intent. `UI.NavigationShell` generates
the keys automatically for its compact and expanded controls.

Use `UI.CodeText(text, id, accessibilityLabel?)` for bounded diagnostics or
commands. It emits one nonfocusable Text node with `.wrail-code-text`, preserves
whitespace, and caps content/accessibility text at 4,096 characters. It does
not create selection, a copy command, scope, shortcut, or background work;
provide a separate explicit Button when copying matters. The built-in theme
uses single-family `Consolas` with up to eight wrapped lines. Native WRSS does
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
`id.subtitle`, `id.metadata`, and `id.state` suffixes and `wrail-action-surface`/
`wrail-tile` classes when adding widget-specific classes.

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

For one non-paged provider value, create a `WidgetResource<TValue>` with
`CreateResource`. Supply an async `Load`, a presentation-safe `MapError`, and
optionally an explicit cache duration, lifecycle, and last-good policy. Its
immutable snapshot reports `NotLoaded`, `Loading`, `Ready`, `Refreshing`, or
`Error`. `EnsureLoaded` reuses a fresh success and joins a duplicate in-flight
read; `Refresh` and `Retry` force a new read. `Publish` commits an authoritative
subscription value and prevents an older read from overwriting it. `Reset`
cancels and clears the resource. State changes invalidate automatically and
`WhenIdleAsync` supports deterministic tests. The helper starts no implicit
polling, subscription, or automatic retry.

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
duration to five minutes, and last-good retention to enabled. This API remains
offset-based with replacement-window compatibility.

For continuous feeds, use `WidgetCursorResource<TItem>` with typed
`WidgetCollectionCursor` and `WidgetCollectionItemKey` values. Its
`PresentItem`/`Present` helpers author protocol-v14 item keys and one viewport
anchor; adjacent pages append/prepend, whole segments evict from the opposite
edge, and refresh follows the segment containing the anchor. Page size is
1–100, retention is at least two pages and at most 256 items, and cursor
history is capped at 256. Duplicate keys, cursor loops, stale completions, and
malformed pages fail without partial publication.

For a large logical collection, opt a cursor viewport into protocol v19 by
setting `WidgetCursorViewport<TItem>.EstimatedItemExtent` and returning the
page's zero-based `FirstItemIndex` plus `TotalItemCount`. The SDK continues to
retain and serialize at most 256 real keyed items; it adds one typed virtual
window descriptor so the existing vertical or horizontal host Scroll can
reserve the bounded off-window extent. Admitted rows keep normal measured
layout, focus, hit testing, paint, and UIA semantics. Off-window private items
are never serialized or materialized by the host. The estimate is 1–512 DIPs,
the known total is at most 1,000,000, and their product is capped at 1,000,000
DIPs. Missing metadata preserves protocol-v14 behavior, while malformed or
stale windows retain the last valid complete checkpoint. Unknown-position and
provider-mutation windows publish `replace`. Indexed append/prepend windows are
admitted only when they move contiguously in the declared direction, keep the
same known-total authority, and preserve every overlapping logical-position key.
The monotonic request generation is bounded to JSON's exact integer range.

`WidgetAppLibraryItem` has one authoritative normalized `Presentation` value;
there are no duplicate scalar title/kind/source/artwork accessors. It contains
the sanitized display name and closed kind, one opaque source reference, one
closed availability/launchability status, role-keyed artwork (`Tile`, `Cover`,
`Hero`, or `Logo`), optional revisioned metadata with explicit attribution, a
closed capability set, and an optional current operation. `AppId` remains a
short-lived exact launch token and `SavedId` remains the only durable identity.
Unknown presentation versions, enum values, duplicate artwork roles or
capabilities, inconsistent launchability, unsafe attribution, and malformed
operation state fail with `malformed_response`; callers must rebuild against
this pre-release contract rather than retaining the removed scalar model.
Each immutable value owns its local bounds and vocabulary validation. A narrow
relationship check then requires `Installed` plus explicit launchability and
the `Launch` capability to agree, and requires an active operation's matching
capability; a paused operation also requires `Resume`. `Unavailable` and
`StaleSource` values are always non-launchable.

`WidgetAppLibraryPage.Sources` carries at most 16 immutable value-only source
observations for the same page revision. Each row contains an opaque
observation-only `SourceId`, sanitized display label, closed
`Healthy`/`Degraded`/`Unavailable`/`Refreshing` health, monotonic source
revision, bounded account state and last-successful-refresh observation, and a
bounded safe status code. It grants no adapter selection,
refresh, launch, filesystem, registry, store, or account authority. Keep usable
items visible when another source is degraded, and use the existing page
refresh operation rather than creating a per-source refresh pipeline.

`WidgetAppLibraryService.ObserveRunningAsync` belongs to the separate optional
`system.apps.running.read.v1` grant. It returns at most 64 sanitized candidates
that the trusted host mapped exactly to current installed SavedIds plus one
short-lived revision. Call `ConfirmRunningAsync(savedId, revision)` before a
durable add. A non-null confirmation is accepted only when its SavedId exactly
matches the request and its complete normalized presentation passes the same
closed validation as paged/resolved app-library items; otherwise
the SDK throws `WidgetCapabilityException` with `malformed_response`. Neither
method exposes process/window/path identity or grants launch authority, and
ordinary catalog access remains independently consented.

For launchers that need truthful progress rather than a simple acknowledgement,
`LaunchObservedAsync` returns `RequestAccepted`, `LauncherStarted`, `Running`,
or `Ended` plus explicit support flags. It is a sanitized public SDK result: it
contains no process, executable, window, provider, or OS identity and is not
reusable authority. Resolve the durable `SavedId` immediately before launch and
use the resulting current opaque `AppId` exactly once.

Protocol v14 also supplies `WidgetArtworkHandle`, `UI.Artwork`, and
`ButtonElement.LeadingArtwork`. Handles are bounded opaque identities—not URLs
or paths—and grant no fetch, file, network, decode, or action authority. Use
the separate `WidgetResource<TValue>` for one current non-paged value.
Sparse pages are supported for providers that filter unavailable server
entries; next-page offsets use the server page limit rather than rendered item
count. An empty non-terminal page still needs a focusable widget placeholder so
the host can emit the next focus-edge action.

For controller-native pages and nested surfaces, create a bounded
`WidgetNavigator<TRoute>` with `CreateNavigator`. `Navigate` changes the root
destination, `Push` opens a nested route, `Back` pops it, and
`TryHandleBack` accepts only the navigator's exact pressed-B action in the
current input scope. Render the current route through `Scope(root)` and publish
`Value.InitialFocusId` plus `Value.InputScopeId`. Route changes cancel
`Value.RouteCancellationToken` before invalidating, and focus is remembered per
route and restored to the parent source on Back. The default maximum depth is
eight (16 hard maximum) and the route-identity table is bounded at 32.

All open-widget actions produced by the standard router carry the current
`WidgetActionEvent.InputScopeId`, including A activation, focused/root
shortcuts, and Slider changes. Validate it for nested manual routing; never
derive the active page from an element-ID substring.

Packages that need bounded operational diagnostics may override
`OnActionDiagnostic`. The SDK calls it with the same `WidgetActionEvent` at
serial-queue admission, dequeue, and terminal completion. Observers must remain
best-effort, bounded, non-secret, and must not influence action handling; SDK
queue behavior is unchanged if an observer throws.

Physical View is reserved for host pinned-surface navigation. Do not declare it
as a dashboard quick action or open-widget shortcut; snapshot validation reports
`host_reserved_view`. The `ControllerButton.View` enum value remains public only
for the host-owned `PinnedLayoutSelection` notification delivered by the pinned
layout handle/test-host contract.

Protocol v24 extends the single host-owned `EmbeddedMediaSurface` with one
optional monotonic `PendingCommand` and validated playback observations through
`OnEmbeddedMediaPlaybackEventAsync`. Commands are a closed load/cue/play/pause/
seek/volume vocabulary over bounded opaque media keys; observations contain
only typed playback state, time, volume, and sanitized error codes. The host
revalidates widget, instance, runtime, presentation, snapshot, surface, and
command authority before either direction crosses the worker boundary. Optional
`AllowedFrameOrigins` entries use canonical lowercase `https://host` form with
no trailing slash, userinfo, wildcard, or explicit port, and are exact origins
for subframes only; the
top-level document remains the sealed package entry asset. This contract does
not expose URLs, DOM, script, browsing, or provider identity.

Use `WidgetIds.Scope(root)` to construct validated hierarchical IDs.
`scope.Scope(segment)` creates a child prefix, `scope.Id(name)` creates a leaf,
and `scope.KeyedId(name, durableKey)` creates a deterministic opaque leaf by
hashing a bounded durable domain key. Segments reject dots and unsafe
characters. The helper prevents malformed/provider-revealing IDs, but it does
not make an index or display label semantically stable. Duplicate-ID/action
handler analyzer rules remain future work.

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

For a credential-free CLI preview, expose one public static parameterless
factory returning `WidgetScenarioDefinition` and declare it in
`widgetrail.scenarios.json`. `wrail preview <directory> --scenario <name>` invokes it
only in the capability-free AppContainer/Job worker. The returned
`WidgetHostServices` supplies typed fakes; the output is a validated,
deterministic `WidgetScenarioResult`, not native pixels.

For discoverable tests, the optional `WidgetScenario` runner sequences named
activation, action, exact async barriers, semantic expectations, deactivation,
and idle checks over the same widget and fake services. Its structured result
names the exact failing step and mismatch. `WidgetScenarioOperationBarrier`
provides manually completed typed requests and cancellation observation without
sleeps or polling; the widget still owns its lifecycle, operations, and state.

When a widget combines a current snapshot with future events, open the typed
acknowledged subscription first (`OpenSessionsSubscriptionAsync` or
`OpenStatusSubscriptionAsync`), fetch current state second, then consume
`ReadAllAsync`. A successful open means host registration is complete, so the
coalesced full-snapshot event buffer closes the fetch/subscription race.
`WatchSessionsAsync` and `WatchStatusAsync` remain compatibility helpers for
event-only consumers.
