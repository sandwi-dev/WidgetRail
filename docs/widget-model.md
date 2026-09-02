# Immutable widget models

`WidgetModel<TState>` is the SDK owner for a group of widget-local values that
must change and render as one immutable state. It serializes updates, suppresses
equal replacements, and requests one widget invalidation after each changed
commit. It is optional: a small widget with one independent field can still use
that field and call `Invalidate()` explicitly.

Create a model once in the widget constructor. Do not create one per render,
route, provider result, or operation.

## Choose the owner, not just a container

| Need | SDK owner | What it owns |
| --- | --- | --- |
| Several local presentation values must change atomically | `WidgetModel<TState>` | One immutable widget-local render state and its semantic revision |
| One current provider value with loading, refresh, error, last-good, and cancellation behavior | `WidgetResource<TValue>` | The provider read lifecycle and immutable resource snapshot |
| A bounded offset/limit collection window | `WidgetPagedResource<TItem>` | Page loading, replacement window, cache, retry, and stale-result rejection |
| A continuous opaque-cursor collection | `WidgetCursorResource<TItem>` | Cursor traversal, keyed retained window, anchor, eviction, and stale-result rejection |
| Nested pages, B handling, input scopes, and return focus | `WidgetNavigator<TRoute>` | The bounded route stack and route lifetime |
| Asynchronous work with explicit lifetime, cancellation, admission, and drain | `WidgetOperations` | Task coordination; not presentation state |
| A remote mutation that projects immediate UI and later reconciles or rolls back | `WidgetOptimisticCommand<TState,TRequest,TExecution,TResult>` | Command policy over a `WidgetModel`, using `WidgetOperations` for execution |

These owners compose. A large widget commonly renders one `WidgetModel`
snapshot alongside one cursor-resource snapshot and one navigator value. Do not
copy the resource or navigator snapshot into the model merely to make one large
object. Their lifecycle, cancellation, revision, and authority remain with the
SDK owner that already implements them.

Each owner also publishes its own semantic changes. One logical action can
therefore request adjacent invalidations from more than one owner—for example,
a model can commit an active search query immediately before a cursor resource
enters Loading. The runtime coalesces adjacent requests before rendering; this
is not an atomic cross-owner transaction or a guarantee that intermediate owner
states are unobservable. Do not add a widget-level `Invalidate()` to batch or
compensate for those publications. Use a single model update only when the
values genuinely share one widget-owned atomic invariant.

The embedded-media callback is a deliberate compatibility example. After
`OnEmbeddedMediaPlaybackEventAsync` returns, the SDK requests an invalidation so
a widget that stores playback in an ordinary field still republishes. A model,
resource, or command facility updated inside that callback publishes its own
change as usual. The SDK cannot conditionally suppress the callback publication
without losing renders for valid field-backed widgets.

Remote authority, provider clients, tasks, cancellation tokens, mutable caches,
and mutable dictionaries or lists do not belong in a model. The model may hold
an immutable, presentation-safe projection of provider data when the widget—not
the provider resource—owns that projection.

## Minimal model

```csharp
private sealed record CounterState(int Count, string Status)
{
    internal static CounterState Initial { get; } = new(0, "Ready");
}

private readonly WidgetModel<CounterState> _model;

public CounterWidget()
{
    _model = CreateModel(CounterState.Initial);
}

public override WidgetView Render()
{
    var state = _model.Value;
    return new WidgetView(UI.Stack("counter.root",
        UI.Text(state.Count.ToString(), "counter.value"),
        UI.Text(state.Status, "counter.status")));
}

private void Increment()
{
    _model.Update(state => state with
    {
        Count = state.Count + 1,
        Status = "Updated",
    });
}
```

`CreateModel` requires a non-null initial state. The initial revision is zero.
Constructing a model does not itself invalidate the widget.

## Equality is the publication contract

`WidgetModel<TState>` uses the comparer passed to `CreateModel`, or
`EqualityComparer<TState>.Default` when none is supplied. `Set` and both
`Update` overloads compare the proposed state with the current state:

- unequal state commits, advances the revision by one, and invalidates once;
- equal state keeps the same object, revision, and invalidation count;
- the SDK does not clone, freeze, or inspect the state graph.

Records provide value equality only for members whose own equality is
semantic. Arrays and most values typed as `IReadOnlyList<T>` compare by
reference under their default equality. A new array containing the same items
can therefore look changed, while mutating a previously published array in
place can look unchanged and also corrupt older snapshots.

Prefer scalar values, nested immutable records, enums, stable typed IDs, and
collection values with an explicitly reviewed equality policy. For a state
that contains sequence data, either reuse the same immutable sequence instance
when its contents did not change or provide an `IEqualityComparer<TState>` that
performs bounded sequence equality and a matching hash calculation:

```csharp
private sealed class QueueStateComparer : IEqualityComparer<QueueState>
{
    public bool Equals(QueueState? left, QueueState? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        left.Status == right.Status &&
        left.ItemIds.SequenceEqual(right.ItemIds, StringComparer.Ordinal);

    public int GetHashCode(QueueState state)
    {
        var hash = new HashCode();
        hash.Add(state.Status, StringComparer.Ordinal);
        foreach (var itemId in state.ItemIds)
            hash.Add(itemId, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

_model = CreateModel(QueueState.Initial, new QueueStateComparer());
```

Keep comparer work bounded. Equality runs while the model lock is held.

## Read one committed state

`Value` returns the current state from one atomic read. Use it when rendering or
when only the state value matters:

```csharp
public override WidgetView Render()
{
    var state = _model.Value; // capture once for this render
    return BuildView(state);
}
```

Do not reread `Value` for each property; another thread may commit between
reads. `Snapshot` atomically returns both the value and its revision when code
needs to correlate later work with the exact observed model commit:

```csharp
var snapshot = _model.Snapshot;
LogModel(snapshot.Revision, snapshot.Value.Status);
```

The revision is local model chronology, not provider authority, a protocol
sequence, a persistence revision, or an operation generation.

## Set and Update

`Set(value)` proposes a complete replacement. `Update(state => next)` computes
a replacement from the latest committed state while holding the model lock.
Both return `Previous`, `Current`, `Revision`, and `Changed`.

Use the result-bearing overload when one transition must also produce the exact
input for subsequent work:

```csharp
var update = _model.Update(state =>
{
    var next = state with
    {
        Filter = state.Filter == Filter.All ? Filter.Favorites : Filter.All,
        Status = "Refreshing…",
    };
    return (next, new RefreshRequest(next.Filter));
});

if (update.Changed)
    _ = Operations.RunLatest("library.refresh",
        context => RefreshAsync(update.Result, context),
        WidgetOperationLifetime.Active);
```

The request is derived inside the same serialized transition that commits its
filter. Do not commit and then reread another field to reconstruct operation
input. The update result's revision identifies that commit if later code needs
to compare it with a fresh model snapshot.

Updater delegates must be quick and side-effect free. Do not perform I/O,
await, block, acquire an unrelated lock, start a task, call a provider, or
mutate an external object from an updater. If an updater throws or returns a
null state, the attempted update does not commit, advance the revision,
invalidate, or raise `Changed`; the exception propagates to the caller.

## Publication and observation

A changed commit stores the new state and advances its revision while holding
the model lock. After releasing that lock, the model requests one widget
invalidation and then invokes `Changed` observers. `Changed` is a diagnostic
and test seam, not another state owner. Every observer sees the committed
previous/current snapshots; an exception from one observer is contained and
does not prevent later observers from running.

Do not use `Changed` to mirror the model into fields, trigger provider work, or
build a second mutable graph. Route actions and operation completions should
call `Set` or `Update` directly. Rendering remains a deterministic projection
of current SDK owners.

After the widget enters `Destroying`, a later model commit can still change the
model and notify diagnostic `Changed` observers, but the model no longer asks
the widget to invalidate. Operations should already be canceled and drained by
their declared lifecycle; Destroying suppression is not permission to continue
background work.

## Migrate a field cluster without creating two owners

For a widget with query, selected item, modal state, and status fields:

1. Inventory every mutable field and identify its real owner.
2. Keep provider pages in `WidgetResource`/`WidgetCursorResource`, nested or
   stacked routes in `WidgetNavigator`, tasks in `WidgetOperations`, and
   persistence/provider authority in their existing owners. A flat lateral
   route machine remains widget-owned when adopting generated scopes or stack
   semantics would change its established public contract.
3. Define one immutable record for only widget-owned render state.
4. Construct one model in the widget constructor.
5. Change each related field cluster in one `Update`; return the current state
   for a semantic no-op.
6. Capture one model value at the start of `Render` and delete manual
   invalidations now owned by model publication.
7. Remove the migrated fields and their lock. Do not dual-write during a
   compatibility period unless a separate migration contract explicitly
   requires it.
8. Test initial revision zero, changed and equal transitions, correlated
   result-bearing updates, stale operation rejection, lifecycle drain, and the
   absence of a second render-state owner.

The [Media Sessions widget](../src/FirstPartyWidgets/MediaSessionsWidget/MediaSessionsWidget.cs)
is a focused example combining one model with an optimistic transport command.
The [Playnite Library model](../samples/PlayniteLibraryWidget/PlayniteLibraryModels.cs)
and [widget](../samples/PlayniteLibraryWidget/PlayniteLibraryWidget.cs) show a
larger migration: local query, modal, hero, status, and connection presentation
share one atomic model, while cursor, navigation, operation, provider, private-
state, and launch-persistence authority stay in their existing owners. These are
examples of the generic contract, not package-specific API behavior.

## Anti-patterns

- Creating a model and retaining the same mutable fields as another writable
  source of truth.
- Putting a mutable list or dictionary in a record and mutating it after
  publication.
- Rebuilding an equal array on every provider event and expecting record
  equality to suppress publication.
- Copying resource, cursor, navigator, task, cancellation, provider, or remote
  authority state into the model.
- Calling providers, starting operations, acquiring unrelated locks, or doing
  expensive sequence work inside an updater.
- Reading `Value` several times to assemble one render or command input.
- Treating the model revision as a provider, protocol, persistence, or security
  authority.
- Using `Changed` to run domain behavior or maintain a mirrored state store.
- Calling `Invalidate()` after a changed model update, which requests a second
  invalidation for the same local transition.
- Treating adjacent invalidations from independent SDK owners as a defect and
  copying their state into one model merely to force one publication.

See the [Widget authoring guide](widget-authoring-guide.md) for the broader
lifecycle, operation, resource, navigation, and packaging workflow, and the
[Widget SDK README](../src/WidgetSdk/README.md) for the compact API map.
