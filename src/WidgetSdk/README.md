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
See [Declarative UI](../../docs/reference/declarative-ui.md) and
[Display and resolution](../../docs/reference/display-and-resolution.md).

SDK composites own their required `wrail-*` root, state, and generated-part
classes intrinsically. `.Classes(...)` replaces only author-owned classes;
`.AddClasses(...)` appends author variants. Both preserve the SDK classes and
their deterministic order. Required classes serialize first, followed by each
authored class's first ordinal occurrence. Primitive elements with no SDK
semantics retain ordinary complete author-class replacement, and valid authored
`wrail-*` names remain available.

Pinned widgets reuse the same host ownership. `WidgetView.PinnedLayouts`
accepts legacy size-only profiles and protocol-v21 declarative projections
created with `WidgetView.PinnedLayout(...)`. A projection supplies only its
bounded root, surface, input scope, and initial focus. The host injects Full
widget, validates each projection plus the aggregate catalog, and atomically
selects one on the single pinned surface. Override
`OnPinnedLayoutSelectionChangedAsync` only to observe package data demand;
null revokes it and never grants host authority.

Widgets that need one embedded media document may set the optional
`WidgetView.EmbeddedMediaSession` property. The declaration contains a stable
session ID and accessible name, bounded presentation/aspect hints, a closed
typed command set, and a finite inventory of package-local assets. Every asset
path must be normalized and declared in the signed package inventory; the host
revalidates its length and digest before creating the native surface. The host
owns the WebView2 controller, origin, bounds/DPI, focus and input routing,
accessibility boundary, visibility, fault handling, and teardown. Widgets own
only their provider adapter assets and typed state. The contract intentionally
does not expose navigation, DOM access, script execution, arbitrary URLs, or a
second HWND/input owner. Widgets that omit `EmbeddedMediaSession` retain their existing
snapshot and rendering behavior.

Use the SDK's readable `EmbeddedMediaAdapterRuntime.js` as the canonical adapter
command boundary. The SDK package carries it under
`contentFiles/any/any/WidgetRail`, but this is opt-in content: WidgetRail does
not inject it, and declaring `EmbeddedMediaSession` does not add it automatically.
Copy it beside the entry document (optionally as the package-local alias
`adapter-runtime.js`), declare that exact staged path in
`EmbeddedMediaSession.Resources` with `ContentType = "application/javascript"`,
and load the same relative path before the provider driver.

```xml
<PackageReference Include="WidgetRail.WidgetSdk" Version="0.3.0-dev"
                  GeneratePathProperty="true" />
<None Include="$(PkgWidgetRail_WidgetSdk)\contentFiles\any\any\WidgetRail\EmbeddedMediaAdapterRuntime.js"
      Link="media\adapter-runtime.js"
      CopyToOutputDirectory="PreserveNewest"
      CopyToPublishDirectory="PreserveNewest" />
```

```csharp
Resources =
[
    new EmbeddedMediaResource
    {
        Path = "payload/media/adapter-runtime.js",
        ContentType = "application/javascript",
    },
];
```

```html
<script src="adapter-runtime.js"></script>
<script>
const adapter = WidgetRailEmbeddedMediaAdapter.create({
  focus: "media-plane",
  bounds: () => ({ x: 0, y: 0, width: innerWidth, height: innerHeight }),
  snapshot: () => playerSnapshot,
  driver: { load, cue, activate, pause, toggle, seek, setVolume },
});
</script>
```

Repository samples explicitly link/copy the source-tree runtime in MSBuild, and
their package scripts explicitly `Copy-Item` it into sealed payloads. External
NuGet consumers use the `contentFiles` path above; neither mechanism is host
injection.

`WidgetRailEmbeddedMediaAdapter.create` accepts a focus ID, bounds and snapshot
readers, an optional bounded error mapper, and explicit player-driver hooks.
The runtime—not the driver—owns initialization authority, generation checks,
one-in-flight admission, command/event correlation, armed activation, terminal
publication, unsolicited observations, and error-token bounding. See the local
embedded-media sample for an HTML-media driver and the YouTube package for a
callback-based external-player driver.
Each hook receives the current operation's `AbortSignal`; use it to detach
provider callbacks and reject pending driver work when the runtime admits a new
initialization authority.

The host retains at most four exact per-widget embedded-media sessions in one
shared WebView2 environment/profile. Normal widget cycling, overlay hide/show,
and overlay-to-pin transfer only change visibility and input focus; they do not
recreate a resident controller or stop its audio. Playing and pinned sessions
are never evicted. A fifth session fails explicitly until a slot is released by
widget removal/restart, exact authority retirement, controller failure, or host
shutdown. Those terminal boundaries start fresh and do not restore playback.

Protocol v23 adds `UI.MediaViewport(session, id)`, a provider-neutral native
layout leaf that binds the one current `WidgetView.EmbeddedMediaSession` declaration
into the ordinary declarative tree. Taffy layout, WRSS/GBSS styling, clipping,
accessibility, and responsive geometry remain host-owned; the embedded browser
supplies only the pixels inside the committed viewport. Exactly one viewport
must reference the current session identity. Missing, duplicate, or mismatched
bindings fail closed, and semantic previews display a deterministic native
placeholder rather than creating WebView2.

Visible media chrome remains ordinary declarative UI. Host-native buttons may
bind their action IDs to `host.embeddedMediaSession.togglePlayback`,
`host.embeddedMediaSession.seekBackward`, `host.embeddedMediaSession.seekForward`,
`host.embeddedMediaSession.previous`, `host.embeddedMediaSession.next`, or
`host.embeddedMediaSession.back`; the host admits those actions only against the exact
current widget, session, viewport, input scope, focus, sequence, and declared
closed command set. The browser never receives the widget focus graph or raw
controller keys. Overlay-root B remains host Back, while ordinary pinned B
retains the selected-projection widget route; the compact-pinned exception is
described below.

Media widgets choose their own quick-seek increment for authored overlay
controls or use the existing Slider absolute-value action for direct seeking.
The session opts into host-owned alternative presentations through the closed
`SupportedPresentations` collection. `MediaPresentationKind.CompactPinned` keeps the same
`MediaViewport` and media session, removes the authored title/transport rows,
and overlays one native themed seek bar that hides during passive playback.
`MediaSeekStepSeconds` defaults to 10 and accepts finite values from 1 through
60 seconds; it is the step for both this presentation and overlay fullscreen, so
declare it once and either mode honours it. X toggles playback; LT/RT rewind or forward from the current
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

Use `UI.SensitiveTextEntry(placeholder, action, id, maximumLength)` when the
committed value is a credential or other secret. It negotiates protocol v28,
always publishes an empty snapshot value, uses a masked native edit with
protected UI Automation semantics, and sends the bounded commit exactly once
through `WidgetActionEvent.CommittedText`. Transfer that value immediately to
the widget's credential owner and render only configured/unconfigured status.
Cancel sends nothing. Sensitive values are removed from framework action
diagnostics and failure observations; never copy them into presentation state,
logs, exceptions, or support artifacts.

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

For rich content rows, use `UI.Tile`. It is a protocol-v7 `ActionSurface`
composition: the complete tile is the sole focus,
pointer, pressed, and A-action target, while generated artwork/copy children are
presentation only. `TileArtwork` accepts one semantic glyph, credential-free
HTTPS image, or bounded inline PNG. Custom rich actions may use
`UI.ActionSurface`, but its subtree is limited to 8 direct children, 32 total
descendants, and four relative levels and cannot contain actions, focus,
scopes, shortcuts, scrolling, interaction state, Buttons, Sliders, or another
ActionSurface. Preserve generated `id.artwork`, `id.content`, `id.title`,
`id.subtitle`, `id.metadata`, and `id.state` suffixes and `wrail-action-surface`/
`wrail-tile` classes when adding widget-specific classes.

Use `UI.PosterTile` for a portrait, fixed-aspect card whose optional image
fills the complete surface with bounded `Cover` placement. Protocol v37 keeps
the same `ActionSurface` focus, activation, state, shortcut, contextual-action,
and accessibility owner while the native renderer layers the generated
`id.artwork` behind `id.scrim`/`id.content`. The default theme reserves fixed
rows for the optional subtitle, two-line ellipsized title, metadata, and state;
visible copy therefore never changes the outer card geometry, while the
complete untruncated strings remain in the poster's accessible name. Customize
the stable `wrail-poster-tile__*` classes rather than adding nested controls.

Use `UI.Toast` for brief feedback that must not steal focus. Tone is paired
with visible text, duration is bounded to 2–30 seconds (five by default), and
the widget—not the host or component—owns removal through normal lifecycle-
aware state. Schedule removal through a `WidgetTimedMutation` slot so replacement,
lifecycle retirement, and test time remain under the SDK operation owner. The
host does not create a timer or hidden worker for Toast animation; themes must
suppress or shorten motion when reduced motion is active.

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
private readonly WidgetTimedMutation _filterConfirmationExpiry;

public FilterWidget()
{
    _filterConfirmationExpiry = CreateTimedMutation(
        WidgetOperationLifetime.Active, TimeProvider.System);
}

_filterConfirmationExpiry.ScheduleLatest(TimeSpan.FromSeconds(4), () =>
{
    _filterConfirmation = null;
    Invalidate();
}, route.RouteCancellationToken);
```

Each `WidgetTimedMutation` is one independent replaceable state slot. Its delay
must be positive and no longer than `WidgetTimedMutation.MaximumDelay` (one
day). It publishes no Busy edge and performs no automatic invalidation; the
quick synchronous callback updates the widget's authoritative state and makes
the ordinary semantic invalidation outside SDK locks. When replacement can race
a due callback, capture a unique immutable notice identity and clear only if that
exact notice still owns the widget state. `Cancel`, lifecycle/owner-token retirement,
and disposal prevent stale callbacks, while the returned completion,
`WhenIdleAsync`, and `Failed` support deterministic observation.

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

The complete public contract—including owner selection, collection equality,
atomic reads, revisions, result-bearing updates, failure/publication behavior,
Destroying semantics, and migration guidance—is in
[Immutable widget models](../../docs/developers/widget-model.md).

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

SDK unit tests that need state, revision, and observer behavior without a
runtime widget may use `WidgetModel<TState>.CreateForTesting`. It intentionally
has no widget invalidation owner; production widgets must use protected
`CreateModel`.

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
        Execute = (command, context) =>
            Provider.ControlAsync(command, context.CancellationToken),
        Reconcile = (current, command, result) =>
            current.MergeResult(command, result),
        Rollback = (current, baseline, command) =>
            current.RemoveProjection(baseline, command),
        MapError = _ => new WidgetCommandError(
            "playback_failed", "Playback could not be changed."),
    });

var handle = _playback.Run(request);
```

`Apply`, `Reconcile`, `Rollback`, and `Fail` run under the model lock and return
immutable state. Keep them quick and side-effect free: never call the model or
command recursively, invoke providers, acquire unrelated locks, or block.
`Execute` runs without the model or command lock, may await, and receives the
actual `WidgetOperationContext`; honor its cancellation token and use
`IsCurrent` before committing adjacent provider-side work. `MapError` also runs
without those locks and must remain bounded, side-effect free, and non-throwing.
SingleFlight joins an existing command without a
duplicate projection, Latest cancels stale work and retains the first baseline
through replacements, and Serial applies each projection only when its bounded
FIFO turn begins. Inactive/capacity-rejected requests and joined SingleFlight
requests do not call `Apply` or mutate the model. A projection may set
`ShouldExecute: false` to publish an admitted domain state without invoking the
provider.

The helper reuses `WidgetOperations`, including Active/State/Widget lifetime
ownership, cancellation, draining, admission/completion handles, busy state,
and current-attempt checks. Model publication occurs after command ownership is
installed or retired, so a synchronous `Changed`/invalidation observer may
start a newer command without the older publication clearing that new owner.
`Reconcile`, `Rollback`, and optional `Fail` receive
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

When confirmation does not come from the mutation call itself, use one
`WidgetOutOfBandCommand<TState,TRequest,TObservation,TAuthority>`. It owns the
command sequence, observation watermark, one pending projection, and the
authority needed to correlate an inbound event. `Run` is single-flight;
`RunLatest` replaces an older intent such as a superseded media load.
`ShouldStart: false` rejects command authority but deliberately still publishes
the projection's `State`, allowing one atomic transition to expose validation.

```csharp
_playback = CreateOutOfBandCommand(_model,
    new WidgetOutOfBandCommandOptions<State, Request, PlaybackEvent, string>
    {
        Apply = (state, request, sequence) => new(
            state.Project(request, sequence),
            request.MediaKey),
        ObservationSequence = report => report.Sequence,
        CorrelationSequence = report => report.CommandSequence,
        MatchesAuthority = (state, report) =>
            state.MediaKey == report.MediaKey,
        MatchesProjectionAuthority = (mediaKey, report) =>
            mediaKey == report.MediaKey,
        Reconcile = (state, report, confirms) =>
            state.Merge(report, confirms),
    });

var admission = _playback.Run(request);
// Later, from the independent event callback:
_playback.Observe(report);
```

An independent poll or subscription read calls `BeginObservation` before it
starts and completes through `Observe(ticket, value)`. This preserves the true
start boundary when reads finish out of order: an observation begun before the
projection may update unrelated authoritative fields without erasing the owned
projection, while a matching or successor observation can confirm it through
`ConfirmsProjection`. A ticket is consumed by its first owned `Observe` attempt,
including an authority or correlation rejection; retry with a fresh ticket.
Rejected payloads do not advance the accepted-observation watermark, so they
cannot make a different valid read that was already in flight appear stale.
While a projection is pending, every correlated or
uncorrelated observation must also pass `MatchesProjectionAuthority`; a matching
command number can never substitute for the projected entity authority.

A projection may supply `ExpiresAfter`; expiry uses the complete widget lifetime
by default so Visible/Interactive transitions cannot strand reconciliation.
Successful expiry, lifecycle cancellation, scheduler rejection, and timer
failure all retire only that exact projection's correlation authority. A stale
timer terminal cannot clear a `RunLatest` successor. The projected model value
is not rolled back by expiry; the next admitted observation is ordinary
authoritative state, and `Reconcile` receives `true` for that no-projection case
so it can replace any projection-shaped fields. The optional expiry data is absent from event-only
projections rather than represented by an unused callback or mode.

Keep `Apply`, authority matching, confirmation, and reconciliation quick and
side-effect free. The facility stores no task completion source or other mutable
signal in model state. Correlated reports for an unknown command, foreign
authorities, and observations at or behind the admitted sequence are rejected.
Model value and facility ownership commit before synchronous invalidation or
`WidgetModel.Changed` observers run, so a render or reentrant callback cannot
observe projected state paired with an older pending sequence or watermark.
Presentation-only delayed feedback remains a separate `WidgetTimedMutation`
when it has a different lifecycle—for example, a Busy threshold may retire on
deactivation while the actual media command remains pending for its adapter.

For an offset/limit provider with a known total and a UI that displays one page
at a time, create `WidgetPagedResource<TItem>` with `CreatePagedResource` instead of maintaining
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

Next/Previous replace the displayed page; there is no built-in arbitrary page
jump. `Refresh()` reloads the current offset and bypasses its cached page.
`EnsureLoaded()` reuses a ready page only while its cache entry is fresh;
otherwise it loads offset zero. This differs from CursorResource, whose refresh
starts at the beginning and whose ready data has no automatic expiry.

Statuses are `NotLoaded`, `Loading`, `Ready`, `Refreshing`,
`LoadingAdjacent`, and `Error`. Bounds are page size 1–100, cached pages 1–8,
cached items 1–512 (and at least one page), pagination threshold 1–8, and error
messages up to 256 visible characters. The lifetime defaults to `Active`, cache
duration to five minutes, and last-good retention to enabled.

For continuous feeds, use `WidgetCursorResource<TItem>` with opaque cursors and
stable item keys. Offset APIs can also be adapted to this model, as Spotify does.
Both resource types are supported; choose by provider contract and browsing
behavior. Already-loaded local lists generally need only scrolling or local
pagination. See [Collections](../../docs/reference/collections.md) for the comparison.

Capture one immutable cursor presentation before constructing rows:

```csharp
var collection = resource.Capture();
var rows = collection.Snapshot.Items.Select(item => collection.PresentItem(item,
    UI.Button(item.Title, "open", item.FocusId))).ToArray();
var scroll = collection.Present(UI.VerticalScroll("items", rows));
```

The capture owns one data revision; it never reselects a window or rereads the
resource during presentation. `Resource.Present(scroll)` was removed because
building children before its separate snapshot read could mix revisions.

`TryHandlePagination` and `Prefetch` load adjacent data without moving focus.
`Move(direction, scrollId, action)` represents explicit page navigation and
includes its originating focus. Its versioned request is consumed once by the
host; a late request cannot replace a different current focus. D-pad boundary
navigation is retained by the host and resumes inside the owning scroll after
admission. Ordinary offscreen navigation and responsive column measurement
remain host-owned.

Whole provider segments evict from the opposite end. `MaximumRetainedItems` is
still a hard bound (at most 256); optional `RetainedItemTarget` is a lower eviction
target. Host pagination actions include visible collection keys, so filling a
large viewport may retain more than the target. A configured hard bound that
cannot contain the protected window fails without partial publication or evicting
those visible rows. Choose a hard bound sufficient for supported widget layouts;
never use a tiny hard limit merely to force an eviction demonstration.

Protocol v47 collection positions keep responsive-grid columns aligned after
opposite-edge eviction. The SDK supplies a stable relative index even when the
provider cursor has no absolute position; authors do not specify column counts
or duplicate layout measurements. The host preserves a visible keyed item's
viewport position when the retained content changes, separately from focus-follow.

Optional v19 `EstimatedItemExtent` with provider `FirstItemIndex`/`TotalItemCount`
still describes logical extent. Sequential right-stick scrolling is limited to
the loaded window while adjacent data is requested; spacers alone do not grant
random-access seeking into unloaded content. Existing per-page, item-count,
cursor-history, identifier, and logical-extent bounds remain enforced.

Cursor presentations expose shared protocol-v48 loading feedback. `Capture().Present`
sets `CollectionLoading` automatically; custom presentations use the captured
snapshot's `LoadingState`. The host draws a non-focusable loading badge at the
requested edge without moving content or disabling navigation. Initial loading
and refresh use the trailing edge; idle and failure remove the badge.
Positioned collections prefetch within two viewport lengths of either loaded
edge. This is a latency budget, not a guarantee: slow providers can still require
waiting at the loaded boundary. Size page batches and retention for the viewport.

Pagination actions are published only for a Ready cursor snapshot. Custom
presentations must follow this rule too, so a pending refresh or fetch cannot
be mistaken for another available page. A stale page demand during refresh
joins the refresh without replacing it; callers can request again after it
settles. Cancellation restores the last settled state rather than a superseded
operation's transient loading state.

Cursor presentations also forward protocol-v49 `CollectionGeneration` from
the captured snapshot's `WindowGeneration`. Every successful commit advances
this value, including an identical refresh and nonvirtual cursor collections.
Custom presentations must forward it too: item counts and edge keys alone
cannot distinguish a refreshed window when intermediate views are coalesced.

`Refresh()` requests the first page and keeps the current data during loading.
A successful fresh load chooses the first item as its anchor and publishes
protocol-v50 `CollectionResetGeneration` from snapshot `ResetGeneration`.
Adjacent pages retain this value even when their intermediate presentations
are coalesced. The host resets collection scroll and remembered item focus once;
focus outside the collection is preserved. Failure or cancellation retains the
previous reset identity and position. `Reset()` clears data without fetching;
the next successful load creates a new reset identity. `EnsureLoaded()` reuses
ready data. Widgets do not need to change scroll IDs to reset collections.
Custom cursor presentations must forward the reset metadata, including through
retained loading views. The default `Capture().Present()` does this automatically.

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
`WidgetNavigator<TRoute>` with `CreateNavigator`. Existing widgets retain the
same generated per-route scope behavior: `Navigate` changes the root,
`Push` opens a nested route, `Back` pops it, and `TryHandleBack` accepts only the
navigator's exact pressed-B action in the current input scope. Render the current
route through `Scope(root)` and publish `Value.InitialFocusId` plus
`Value.InputScopeId`. Route changes cancel `Value.RouteCancellationToken` before
invalidating, and focus is remembered per route and restored to the parent source
on Back. `PushFromAction` captures `WidgetActionEvent.FocusedElementId`, which is
the actual focused control at dispatch; `SourceElementId` remains the action or
shortcut declaration owner.

Flat sibling roots may opt into one stable root scope with
`CreateNavigatorWithOptions`. List only the root routes that share that scope;
nested routes still receive independent scopes and exact B ownership. Use
`NavigateRoot(route)` when A on a persistent header should keep that logical
header. Use `NavigateRoot(route, groupId)` for LB/RB-style sibling navigation:
the navigator issues one increasing protocol-v44 group-entry request only when
the root actually changes. Publish `Value.FocusGroupEntryRequest` on the
`WidgetView`, and author the destination container with
`RememberChildFocus(defaultChildId)`. The host consumes the request once,
restoring its valid remembered child or the group's authored fallback; routine
renders carrying the same request cannot steal focus again.

`Value.RootRouteCancellationToken` owns root-page work and remains active while
a nested route is open. `Value.RouteCancellationToken` owns only the current
route. Changing roots cancels both departed owners; pushing or popping a nested
route cancels route work without cancelling the root page. `Scope` accepts every
`ContainerElement` root, preserving its concrete Stack, Row, Scroll, Grid, or
future container type; it rejects a leaf because only a container can own a
scope and Back shortcut. The default maximum depth is eight (16 hard maximum)
and the route-identity table is bounded at 32. Omitting navigator options keeps
the existing generated-scope and focus behavior exactly.

`UI.NavigationShell` also has an additive compact-adornment overload for small,
input-inert hints placed directly before and after its compact destination items.
The original overload and expanded rail/body are unchanged. Adornments may use
presentational stacks, rows, text, icons, images, progress, or loading elements;
actions, focus, scopes, shortcuts, scrolling, and interactive descendants are
rejected.

```csharp
var shell = UI.NavigationShell(
    "library.shell",
    selectedDestinationId,
    contentEntryFocusId,
    content,
    destinations,
    expandedPane: null,
    expandedPaneEntryFocusId: null,
    compactLeadingAdornment: UI.ControllerHint(
        ControllerButton.LeftBumper, "Previous section", "library.section.previous"),
    compactTrailingAdornment: UI.ControllerHint(
        ControllerButton.RightBumper, "Next section", "library.section.next"));
```

The adornments appear only in the compact presentation. Keep root LB/RB
shortcuts on the navigator-scoped owner so expanded layouts retain the same
discoverable input contract without duplicating actions on the adornments.

When compact navigation belongs inside a larger author-owned header, use
`UI.NavigationShellParts` instead of indexing the shell's private children. It
returns the same compact navigation and body that `UI.NavigationShell` arranges
by default. Keep `Body` below the header: it owns the expanded rail, optional
persistent pane, and the one shared content subtree.

```csharp
var parts = UI.NavigationShellParts(
    "library.shell",
    selectedDestinationId,
    contentEntryFocusId,
    content,
    destinations,
    compactLeadingAdornment: previousHint,
    compactTrailingAdornment: nextHint);

var header = UI.Row(
        "library.header",
        parts.CompactNavigation.AddClasses("library-header-navigation"),
        statusHint.AddClasses("library-header-status"))
    .Classes("library-header");
var root = UI.Stack("library.root", header, parts.Body);
```

```css
.library-header { width: 100%; min-width: 0; align: center; gap: 12px; }
.library-header-navigation { min-width: 0; flex-grow: 1; flex-shrink: 1; }
.library-header-status { flex-shrink: 0; }
```

`CompactNavigation` remains compact-only. Authors may place noninteractive
status or controller help beside it without placing that content beside the
complete page body. The original `UI.NavigationShell` overloads remain the
preferred default and serialize the same tree as before. Keep both named parts
under the same input scope because the generated navigation focus links target
descendants in `Body`. Let the navigation group shrink beside natural-width
header content; do not use percentage or fixed header heights.

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

Protocol v24 extends the single host-owned `EmbeddedMediaSession` with one
optional monotonic `PendingCommand` and validated playback observations through
`OnEmbeddedMediaPlaybackEventAsync`. Commands are a closed load/cue/play/pause/
seek/volume vocabulary over bounded opaque media keys; observations contain
only typed playback state, time, volume, and sanitized error codes. The host
revalidates widget, instance, runtime, presentation, snapshot, session, and
command authority before either direction crosses the worker boundary. Optional
`AllowedFrameOrigins` entries use canonical lowercase `https://host` form with
no trailing slash, userinfo, wildcard, or explicit port, and are exact origins
for subframes only; the
top-level document remains the sealed package entry asset. This contract does
not expose URLs, DOM, script, browsing, or provider identity.

The SDK requests a compatibility invalidation after each playback callback so
widgets backed by ordinary fields still publish the observation. If the callback
updates a `WidgetModel`, resource, or command facility, that owner also publishes
its own semantic change; adjacent requests are coalesced by the runtime. Do not
add a manual `Invalidate()` to batch or compensate for either owner.

Protocol v27 adds the closed `SetPlaybackRate`, `SetMuted`, and `SetLoop`
playback commands. Playback rate is finite and bounded from 0.5 through 2.0;
mute and loop carry exact Boolean values. Every correlated terminal observation
reports the applied playback rate, muted state, and loop state. Adapters must
return a bounded correlated error when a requested rate is unavailable rather
than assuming success. These preferences belong to the current adapter
document: `Load` and `Cue` retain them while changing media within that
document, while controller/document replacement resets them to adapter
defaults. Muting never changes the authored volume.

Protocol v42 unifies embedded-media lifetime and presentation. A declared
session with zero `MediaViewport` nodes is valid: an exact resident controller
parks, while a cold declaration remains dormant and creates no background
controller. `SupportedPresentations` explicitly opts into `OverlayFullscreen`
and/or `CompactPinned`; these are capabilities, not state, and the host owns
which presentation currently holds the session.

Entering and leaving fullscreen preserve the sealed adapter, controller,
document, and playback session.
Entering and leaving preserve the sealed adapter, controller, document, and
playback session. The host owns safe-work-area placement, aspect fit, DPI,
native guide, focus, accessibility, and return to the authored layout.

Offer entry with the reserved `host.embeddedMediaSession.enterFullscreen` action; the
host leaves the mode on B without routing it to the widget, and View remains
host-owned. Webpages can neither request nor exit fullscreen. A widget holds no
fullscreen state and needs no exit shortcut, so it cannot leave the user inside
a presentation that draws no tray, guide, or accessibility tree. The host binds
its activation to the exact admitted declaration and retires it whenever that
declaration stops applying, which is why nothing has to be cleared on route
changes, deactivation, or restart.

While fullscreen is active the host services X and the triggers directly against
the media plane, so optimistic pending/busy state in the widget does not apply;
playback events still arrive through `OnEmbeddedMediaPlaybackEventAsync`.

Use the declared `MediaSeekStepSeconds` for package-side seek command math so it
matches the exact value consumed by host-owned compact and fullscreen controls.

Protocol v26 adds `AllowedFrameDomainFamilies` for the bounded case where an
external frame legitimately spans one registrable DNS family. Entries are
lowercase ASCII registrable domains, not URLs or public suffixes, and match
only the exact root plus dot-boundary subdomains over default-port HTTPS. The
host validates them against its integrity-checked Public Suffix List snapshot,
checks every intercepted resource and redirected-resource destination, and
fails closed when the suffix authority is unavailable or corrupt. Exact
origins remain the sole frame-document navigation authority; families do not
admit frame navigation.

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

### Context menus from controller hints

A visible non-focusable container can declare a scoped native dropdown with
`ContextMenu(ControllerButton.Menu, actions)`. For example, apply it to a
`UI.ControllerHint` row. The hint stays outside focus traversal; the host uses
its visible bounds to anchor the existing dropdown. Use one container menu per
button in an active input scope. Ambiguous or unrendered owners do not dispatch.

Action surfaces retain Menu as their default context trigger. Use
`ContextMenuShortcut(ControllerButton.X)` to choose X instead. Explicit triggers
support Menu, X and Y and require protocol v51. A focused action surface takes
precedence over a container menu for the same button. Disabled/busy owners and
other input scopes cannot capture the trigger. These declarations use the
existing full-widget context menu and action-validation path.

The host guide automatically advertises **Options** with the actual Menu, X or Y
binding when the resolved menu has an available action. These hints take priority
over ordinary shortcuts when space is limited; host Back/Close hints remain.
This applies to both focused action surfaces and visible container menus.
Only the focused action surface gets a small themed ellipsis indicator. It is
decorative, does not resize the tile or add a focus stop, and disappears when
focus moves or all menu actions become unavailable. No extra authored hint is
required. Changing context actions requests a paint update so the cue stays current.

### Live window content

Use `UI.WindowPreview` by itself or pass it to the content overload of
`UI.PosterTile`. Window previews are view-only, use opaque IDs from
`HostServices.TaskSwitcher.GetWindowsAsync`, and require the separate
`system.apps.windows.preview.v1` permission. Existing artwork posters are
unchanged. See [live window previews](../../docs/reference/window-previews.md) for the
complete authoring example, theme hooks, backend behavior and limits.

### PC power controls

Use `HostServices.Power` to check availability and request PC shutdown, restart or sleep.
The separate read/control permissions, confirmation guidance, and Windows behavior
are documented in [Power widget](../../docs/reference/power-widget.md).
