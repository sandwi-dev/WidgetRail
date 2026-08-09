# Widget Authoring Experience Review

Status: living assessment; operation scopes, immutable models, optimistic commands, and bounded offset-paged resources implemented, later recommendations open<br>
Date: 2026-08-08<br>
Reassessed: 2026-08-08 after public paged resources, the Spotify resource migration, and the Media Sessions model/optimistic-command production migration<br>
Scope: public widget authoring APIs, tooling, examples, and the complexity exposed by advanced widgets such as Spotify

## Executive conclusion

The framework has a sound foundation: a native host owns rendering, focus,
accessibility, permissions, and lifecycle, while isolated C# workers contribute
renderer-neutral UI and react to typed actions. Simple widgets are already easy
to express.

The framework becomes difficult when a widget has multiple pages, remote state,
commands, caching, optimistic updates, and lifecycle-sensitive work. Authors
currently implement too much coordination infrastructure themselves:

- locks and state snapshots;
- task and cancellation-token ownership;
- single-flight, serial, and replace-running operation policies;
- loading, stale-result, retry, and cached-data states;
- optimistic updates and rollback;
- route stacks, Back handling, and focus restoration;
- duplicated compact and expanded composition; and
- extensive manual invalidation.

This is not unique to Spotify. Similar patterns remain in Audio Mixer, Network
Controls, Games & Apps, and YT Music; Media Sessions now demonstrates the
shorter model/command path. The framework can express advanced widgets, but
some safe implementation patterns still require broader migration and recipes.

The recommended direction is evolutionary, not a rewrite: retain `Widget`, the
declarative protocol, GBSS, AppContainer isolation, and typed capabilities.
Add optional high-level SDK primitives that encode the safe patterns already
implemented repeatedly by first-party widgets.

The latest Spotify work supports this direction. Protocol-v11 host-owned
focus-edge pagination and `ScrollElement.Paginate` move a generic controller
interaction out of Spotify and into the platform. The public
`WidgetOperations` coordinator now also owns bounded SingleFlight, Latest, and
Serial execution plus lifecycle cancellation/draining. The public
`WidgetPagedResource<TItem>` builds on both contracts with offset-page state,
validation, invalidation, a deterministic bounded LRU, retry, and entering-edge
focus. Spotify has migrated its playlist collections to that API. The public
`WidgetModel<TState>` now serializes immutable state transitions, suppresses
equal-state invalidations, and can derive an operation input from the exact
committed revision. Media Sessions now keeps all render-facing state in one
model and uses the public `WidgetOptimisticCommand` coordinator for transport.
Cursor/append resources and navigation remain open; other widgets still need
deliberate migration to the new command contract.

The presentation layer is further along than an earlier gap list implied.
Pressed-state delivery, bounded subtree translation, responsive branches and
Grid, row wrapping, per-edge borders, semantic code text, activate-to-adjust
Sliders, and the modern controller component set are implemented. They should
be treated as current authoring tools, not roadmap proposals. Higher-level
responsive navigation/page recipes and packaged-font support remain distinct
open work.

## Evidence from the repository

Approximate implementation sizes in the current worktree illustrate the gap
between a minimal and an application-like widget:

| Widget | Approximate C# size | What it demonstrates |
| --- | ---: | --- |
| Clock sample | 40 lines | Deterministic render and one action |
| SDK Gallery | 360 lines | Public components and local state |
| Recent Apps | 340 lines | One event-driven provider surface |
| Media Sessions | 735 lines | Selection, commands, progress, and provider lifecycle |
| Games & Apps | 1,150 lines | Navigation, paging, private state, and launch commands |
| YT Music | 1,150 lines | Connection lifecycle and optimistic media state |
| Spotify | About 2,170 lines | OAuth, playback, four destinations, bounded paging, caching, and local playback |
| Network Controls | About 2,000 lines | Multiple providers, discovery, commands, and failure states |
| Audio Mixer | About 2,500 lines | Dense state reconciliation and optimistic controls |

The exact counts are less important than the shape of the code. Spotify owns
multiple `Task`, `CancellationTokenSource`, `SemaphoreSlim`, generation, cache,
pending-operation, loading, error, and focus fields. Other advanced widgets own
similar structures. Its presentation also includes about 500 lines of GBSS and
its dedicated test program is about 1,100 lines. Those tests are valuable, but
their size reinforces that this is an application-scale reference.

The SDK already provides important low-level safety mechanisms:

- [`Widget`](../src/WidgetSdk/Widget.cs) supplies runtime-owned widget, state,
  and active lifetimes.
- [`WidgetTicker`](../src/WidgetSdk/WidgetTicker.cs) supplies bounded,
  non-overlapping active updates.
- [`WidgetControllerQueue`](../src/WidgetSdk/WidgetControllerQueue.cs) supplies a
  bounded serial action queue with slider coalescing.
- [`WidgetOperations`](../src/WidgetSdk/WidgetOperations.cs) supplies bounded
  SingleFlight, Latest, and Serial lanes tied to Active, State, or Widget
  lifetimes, with explicit admission and non-faulting completion results.
  Games & Apps uses an Active Latest lane for initial/retry library reads,
  replacing author-owned cancellation-source lifecycle cleanup.
- [`WidgetPagedResource<TItem>`](../src/WidgetSdk/WidgetPagedResource.cs)
  supplies an offset-based immutable page snapshot, Latest coordination,
  bounded LRU, safe error/retry state, invalidation, and responsive Scroll-to-
  focus mappings.
- [`WidgetModel<TState>`](../src/WidgetSdk/WidgetModel.cs) supplies atomic
  immutable snapshots, serialized updates, equality-based invalidation, exact
  revisions, result-bearing mutations, and contained diagnostic observation.
- [`ScrollElement.Paginate`](../src/WidgetSdk/Elements.cs) supplies protocol-v11
  host-owned near-start and near-end focus triggers without visible paging
  buttons.
- [`WidgetTesting`](../src/WidgetSdk/WidgetTesting.cs) supplies typed fake
  capabilities and lifecycle control.
- [`ModernComponents`](../src/WidgetSdk/ModernComponents.cs),
  [`TileComponents`](../src/WidgetSdk/TileComponents.cs), and
  [`Toast`](../src/WidgetSdk/Toast.cs) cover settings rows, bounded pickers and
  action sheets, scrubbers, status/empty states, media/app tiles, and
  non-focus-stealing feedback.
- Protocol-v8/v9 responsive Grid and visibility branches, plus GBSS Row wrap,
  per-edge borders, `:pressed`, and bounded presentation translation, cover the
  low-level responsive/presentation contracts already required by advanced
  widgets.

These primitives prevent several classes of misuse, but the author still has to
compose them into an application architecture. Spotify is evidence that the
missing layer is coordination, not rendering capability.

## Reassessment after the Spotify 0.2.10 changes

The latest Spotify changes improve both the widget and the framework:

- playlists and playlist items use bounded 12-item page windows rather than
  continually appending every loaded item to one snapshot;
- the worker keeps at most six cached pages for each collection;
- controller focus approaching a Scroll boundary emits a host-owned near-start
  or near-end action through protocol v11;
- previous and next pages restore focus to an appropriate item in the new
  window;
- tests cover pagination metadata, bounded snapshot size, cached reverse
  navigation, and focus placement; and
- playback-host bootstrap reports SDK download failure explicitly and serves
  its trusted page from a stable internal HTTPS origin.

The protocol addition is a meaningful authoring improvement. A widget can now
request adjacent data naturally as the user navigates, without adding a visible
`Load more` button or detecting focus geometry itself.

Spotify no longer owns page-operation tasks, cancellation sources, generation
counters, page dictionaries, eviction loops, loading/error state, retry intent,
or compact/wide focus-mode parsing. Two `WidgetPagedResource<TItem>` instances
provide those generic behaviors with 12-row windows and a six-page/72-item LRU;
Spotify supplies provider loading, safe error copy, viewport IDs, item rendering,
and selected-playlist domain state. Queue remains a separate non-paged path.

The latest changes are a successful example of moving proven generic behavior
into the SDK/host without changing the protocol boundary. Immutable state now
also has a medium production migration, including optimistic transport
commands. The next targets are navigation and broader helper migrations;
cursor and append/infinite-feed resource semantics need a separate design
rather than being implied by the offset-paged API.

No authenticated Player, Queue, Playlists, or Devices screenshots were found in
the current evidence set; the stored Spotify images still cover configuration
and setup. The semantic and interaction tests are stronger, but credential-free
visual scenarios remain necessary to assess the complete playback UI.

## Reconciliation of earlier framework-gap findings

The following items were rechecked against public SDK declarations, protocol
validation, native behavior, and current author documentation. This prevents
implemented facilities from remaining on the roadmap under an older name.

| Earlier concern | Current verified contract | Remaining boundary |
| --- | --- | --- |
| `:pressed` was parsed but not rendered | GBSS publishes a pressed computed map, and the native host applies it only to the physically held action target. | No framework gap remains; widgets should style the semantic state instead of simulating it. |
| Motion lacked subtree translation | `translate-x` / `translate-y` move the complete presented subtree, including clip, hit-test, focus, accessibility, and Scroll geometry, with bounded retargetable transitions. | Shell-level presentation choreography remains host product work, not a widget API. |
| Settings rows, pickers, action sheets, scrubbers, toasts, and media/app tiles were missing | These are public `UI.*` compositions with stable generated IDs, semantic classes, validation limits, and controller behavior. | Adaptive navigation shells and full page recipes remain open. |
| Layout lacked per-edge borders and responsive wrap/Grid | GBSS supports independent edge colors/widths and Row wrapping; `UI.ResponsiveGrid` provides protocol-v8 row-major reflow. | Virtualized/sectioned collections remain open. |
| Compact and expanded layouts required ad hoc host checks | Protocol-v9 `.VisibleWhen(...)` / `UI.ResponsiveBranch(...)` lets the host exclude inactive subtrees from every semantic system. | Authors still duplicate the changed branches; a navigation-shell recipe could reduce that duplication. |
| Sliders always consumed Left/Right during navigation | Protocol-v10 `.RequireControllerActivation()` reserves A/B for a host-owned adjustment mode; outside that mode all directions remain navigation. | A Slider cannot combine activation-first mode with a separate A activation action. |
| Typography lacked semantic monospace | `UI.CodeText` supplies bounded, whitespace-preserving semantic code text. | Packaged fonts and browser-style fallback stacks remain unavailable. |

The highest-priority remaining authoring gaps are therefore application
coordination, not basic controls: a bounded navigator with focus restoration,
non-offset resource variants, broader coordination-helper migrations, and
credential-free scenario/visual testing. Documentation should keep those
separate from already shipped primitives so authors can use the safe short path
today.

## Two different extension problems

The platform should distinguish these personas explicitly.

### Widget author

A widget author consumes existing public capabilities such as Spotify, audio,
network, media, or app-library services. The framework should make UI, state,
navigation, and safe asynchronous coordination straightforward.

### Capability provider author

A provider author adds new trusted access to an operating-system or external
service boundary. This involves broker contracts, consent, secret ownership,
native or trusted implementations, and security review. It is necessarily more
difficult and should not be disguised as ordinary widget work.

A human can build a Spotify-style widget only because `HostServices.Spotify`
already exists. A community widget cannot currently invent an equivalent
privileged service from inside its capability-free worker. Documentation and
templates should make this boundary clear.

## Design principles for improvements

1. **Preserve the simple path.** A Clock-style widget should still need only
   `Render`, `OnActionAsync`, and `Invalidate`.
2. **Use progressive disclosure.** Advanced helpers should be optional
   compositions, not a mandatory new base-class hierarchy.
3. **Make the safe behavior the short behavior.** Runtime-owned cancellation,
   bounded concurrency, stale-result rejection, and task observation should
   require less code than hand-written alternatives.
4. **Keep policy explicit.** Helpers must expose whether an operation is serial,
   single-flight, replace-running, latest-wins, active-scoped, or widget-scoped.
5. **Avoid hidden work.** No helper should silently poll, retain a worker, or
   broaden capability authority.
6. **Remain renderer-neutral.** Convenience APIs should emit the existing
   protocol rather than introduce Spotify-specific native elements.
7. **Prefer testable state transitions.** Domain state should be separable from
   rendering and transport.

## Recommended framework additions

### 1. Runtime-owned operation scopes — implemented

The public coordinator is now available from the protected `Operations`
property:

```csharp
protected WidgetOperations Operations { get; }

Operations.RunSingleFlight("refresh",
    async context => await RefreshAsync(context.CancellationToken));
Operations.RunLatest("page",
    async context => await LoadPageAsync(route, context),
    WidgetOperationLifetime.Active);
Operations.RunSerial("command",
    async context => await ExecuteCommandAsync(context.CancellationToken),
    WidgetOperationLifetime.Widget);
```

The implemented coordinator:

- binds each lane to `ActiveCancellationToken`, `StateLifetimeToken`, or
  `WidgetLifetimeToken`;
- returns non-faulting completion results and raises each failure once;
- cancels and awaits ending work before lifecycle transition callbacks;
- implements bounded Serial, SingleFlight, and non-overlapping Latest policies;
- rejects new active-scoped work while inactive;
- exposes busy state by stable operation key; and
- supports deterministic idle waits and focused tests without sleeps.

Admission is explicit (`Completed`, `Started`, `Joined`, `Replaced`,
`Enqueued`, `RejectedInactive`, or `RejectedCapacity`) and completion is explicit
(`Succeeded`, `Canceled`, `Superseded`, `Failed`, or `Rejected`). Latest
contexts become non-current synchronously when replacement is admitted. Bounds
are 32 tracked keys, 64 total active/pending operations, and 16 pending Serial
operations per key. Active spans Visible/Interactive, State belongs to one
exact runtime state, and Widget spans creation through Destroying. `Completed`
means the request was satisfied synchronously without scheduling work.
Busy-edge changes auto-invalidate the widget and also publish `BusyChanged`.

Spotify's non-paged destination loading uses one Active Latest lane. Its two
12-row playlist collections now use separate Active paged resources built on
the same coordinator. Other widget command/auth/polling paths can migrate
separately when their required lifetime and ordering policy are explicit.

### 2. Observable immutable widget state — implemented

Create the optional state container once in the widget constructor rather than
hand-rolling locking, snapshot copies, equality checks, and invalidation:

```csharp
private readonly WidgetModel<State> _model;

public PlayerWidget()
{
    _model = CreateModel(State.Initial);
}

var state = _model.Value;
_model.Update(state => state with { Status = "Refreshing", IsBusy = true });
```

The implemented contract provides:

- immutable snapshot reads suitable for `Render`;
- serialized updates;
- invalidate only when the value changes;
- an atomic update that can return an operation input;
- no reflection or ambient global store;
- contained `Changed` observation for diagnostics and tests, without retaining
  production history; and
- compatibility with ordinary fields for simple widgets.

`Value` is intentionally not cloned: authors must use immutable records or
otherwise treat published values as immutable. Update delegates run under the
model lock and must remain quick and side-effect free. A committed transition
increments the model revision and invalidates exactly once after releasing the
lock; an equal replacement does neither. The result-bearing overload derives
its result from the same prior state revision, so controller commands do not
need to reread unrelated mutable fields. Destroyed widgets may finish cleanup
state changes but no longer invalidate. This reduces lock scope and accidental
`Invalidate` storms without forcing a Redux-style architecture.

Media Sessions is the first medium production proof. Its session collection,
selection, pending command, view/status state, snapshot revision, live-update
availability, and reload admission now share one `WidgetModel<State>`. A
focused regression verifies that selecting a different session publishes one
invalidation and that repeating the selected action publishes none. Lifecycle
generations, stale-result rejection, and command admission remain intact.

Media Sessions also validates the command coordinator described below. The
model still contains domain state, while the coordinator owns admission,
lifecycle-bound execution, current-attempt completion, and the exact callback
sequence. Domain-specific projection, provider-event merge, safe messages, and
rollback remain explicit callbacks rather than hidden framework policy.

### 3. Bounded offset-paged resource state — implemented

The public resource is constructed once through `CreatePagedResource<TItem>`:

```csharp
private readonly WidgetPagedResource<Item> _items;

public LibraryWidget()
{
    _items = CreatePagedResource<Item>("library.items", new()
    {
        PageSize = 12,
        MaximumCachedPages = 6,
        MaximumCachedItems = 72,
        LoadPage = LoadPageAsync,
        MapError = MapSafeError,
        Viewports =
        [
            new("library.items.scroll",
                (_, absoluteIndex) => $"library.item.{absoluteIndex}"),
        ],
    });
}
```

Its immutable snapshot represents `NotLoaded`, `Loading`, `Ready`,
`Refreshing`, `LoadingAdjacent`, and `Error`; last-good retention is optional
and enabled by default. It validates offset pages, maps safe bounded errors,
retries the exact failed intent, rejects stale/late completions, coalesces an
identical request, and exposes explicit `Completed` admission for cache hits and
no-op boundaries. Limits are 100 items per page, eight cached pages, 512 cached
items, and a protocol-v11 threshold of 1–8. Cache eviction is deterministic
LRU, and its lifetime defaults to Active.

Protocol-v11 `ScrollElement.Paginate` remains the host-owned trigger. The
resource's `Paginate(scroll)` publishes only currently valid boundary actions;
`TryHandlePagination` accepts only an exact action and configured Scroll ID.
Viewport delegates receive absolute collection indexes and produce stable
entering-edge focus IDs for compact/wide surfaces. No worker focus geometry or
visible Load-more row is required.

The resource owns its state-change invalidation and runtime-owned Latest lane,
including synchronous cache-hit/reset changes that have no operation busy
edge. The widget still owns copy and visual composition. A generic
`WidgetResource<T>`, cursor paging, append/infinite feeds, and a
`UI.ResourcePage` composition are not implemented and remain later design work.

### 4. Command and optimistic-update helper — implemented

Media, audio, network, and settings widgets repeatedly implement pending state,
busy controls, optimistic projection, success reconciliation, and rollback. The
public helper is created once over a `WidgetModel<TState>`:

```csharp
_playback = CreateOptimisticCommand(
    "playback",
    _model,
    new WidgetOptimisticCommandOptions<State, PlaybackRequest, ProviderCommand, Playback>
    {
        Policy = WidgetCommandPolicy.Latest,
        Lifetime = WidgetOperationLifetime.Active,
        Apply = (state, request) => new(
            state.Project(request),
            ProviderCommand.From(state, request)),
        Execute = (command, token) => Provider.ControlAsync(command, token),
        Reconcile = (current, command, result) =>
            current.MergeProviderResult(command, result),
        Rollback = (current, baseline, command) =>
            current.RemoveProjection(baseline, command),
        MapError = MapSafeCommandError,
        Fail = (current, baseline, command, error) =>
            current.RemoveProjection(baseline, command).WithError(error),
    });

var handle = _playback.Run(request);
```

The implemented semantics are:

- SingleFlight joins an in-flight key without projecting a duplicate; Latest
  replaces stale work and retains the first baseline through a replacement
  chain; Serial projects each admitted request only when its bounded FIFO turn
  begins;
- `Apply` derives both optimistic state and exact provider input from one
  serialized model transition;
- `Reconcile`, `Rollback`, and optional `Fail` receive the current model, so
  their domain merge can preserve unrelated provider events received in flight;
- Active, State, or Widget lifetime ownership, cancellation, and draining reuse
  `WidgetOperations`, including current-attempt rejection of late results;
- inactive/capacity rejection and a joined SingleFlight request do not call
  `Apply` and cannot mutate the model;
- provider exceptions are mapped to a validated `WidgetCommandError` with a
  stable code and at most 256 visible characters; mapper failure falls back to
  the bounded generic error; and
- mutating provider calls are executed once. The SDK never retries them.

`ShouldExecute: false` lets an admitted projection publish domain-specific
unavailable state without calling the provider. The helper does not infer
provider-event identity, confirmation deadlines, slider coalescing semantics,
or a correct merge/rollback for the widget. Authors must implement those
callbacks over immutable state. It builds on `WidgetOperations`; it does not
create a second controller-input queue.

### 5. Navigation and focus model

Advanced widgets need a small controller-native router:

```csharp
private readonly WidgetNavigator<Route> _navigation = new(Route.Player);

_navigation.Navigate(Route.Playlists, sourceFocusId);
_navigation.Push(new Route.Playlist(id), sourceFocusId);
_navigation.Back();
```

It should own:

- a bounded route stack;
- selected root destination;
- source focus ID and return focus restoration;
- nested input-scope identity;
- Back action publication only when a nested route exists; and
- route-change cancellation through an associated operation scope.

Add a responsive composition such as `UI.NavigationShell` that maps the same
destinations to an expanded rail and compact tabs. It should allow a persistent
expanded pane without requiring authors to duplicate the entire semantic tree.
The current low-level Row, Stack, visibility, and input-scope APIs should remain
available as escape hatches.

### 6. Stable ID scopes

Stable IDs are necessary, but large widgets currently assemble many strings by
hand. Add a lightweight hierarchical ID builder:

```csharp
var ids = WidgetIds.Scope("spotify").Scope(mode).Scope("player");
UI.IconButton(..., ids.Id("play-toggle"), ...);
```

Generated composite suffixes should remain documented and stable. A build-time
analyzer or `gbar validate` rule should detect duplicate literal IDs, unstable
index-derived IDs where a durable domain key is available, invalid focus
targets, and action IDs with no known handler in declarative action maps.

Spotify's current pagination code also derives compact or wide mode from the
source element ID. IDs should identify elements, not become an undocumented
action-data channel. A navigation-shell helper or bounded typed action context
should provide the current route/presentation variant without requiring string
parsing.

### 7. Layout recipes and semantic styling

The component library is useful, but advanced widgets still carry hundreds of
lines of GBSS. Add reusable, theme-respecting recipes rather than
service-specific controls:

- adaptive navigation shell;
- persistent media panel;
- scrollable collection page;
- detail page with nested Back behavior;
- status/loading/error page;
- compact transport row; and
- responsive action footer.

Recipes should be SDK compositions that emit current protocol nodes and
semantic classes. They must remain themeable and must not add background work.
Widget-local GBSS should primarily express identity and small variations, not
rebuild standard focus, spacing, disabled, and responsive behavior.

This recommendation is above the current primitive/composite layer. Authors
already have `ResponsiveGrid`, responsive visibility branches, Row wrapping,
settings rows, Picker, ActionSheet, Scrubber, status/empty compositions,
media/app tiles, and Toast. The missing recipes should compose those contracts;
they should not introduce parallel controls with different focus or styling
semantics.

### 8. Scenario-based preview and visual testing

The current Spotify evidence can render setup without credentials, but not its
authenticated playback surfaces. Add author-defined scenarios:

```csharp
public static IEnumerable<WidgetScenario> Scenarios =>
[
    Scenario.Named("playing").WithServices(Fakes.Playing),
    Scenario.Named("empty").WithServices(Fakes.NoPlayback),
    Scenario.Named("permission-denied").WithServices(Fakes.Denied),
];
```

Suggested tooling:

```powershell
gbar preview . --scenario playing --viewport compact
gbar capture . --all-scenarios --viewport compact,standard,wide,accessible
gbar test . --controller-replay replays/smoke.json
```

The preview should show focus, input scope, shortcut ownership, element IDs,
clipping, and accessibility labels. It should never require real OAuth,
hardware, or user secrets. Visual artifacts should be deterministic enough for
review, with pixel comparisons used cautiously and semantic snapshots retained
as the primary contract.

### 9. Higher-level test harness

Keep `WidgetTestHostServicesBuilder`, but add fluent scenario and interaction
helpers over it:

```csharp
await WidgetScenario.For(new SpotifyWidget(), services)
    .ActivateAsync()
    .PressAsync("spotify.play-toggle")
    .ExpectBusyAsync("spotify.play-toggle")
    .CompleteCapabilityAsync(...)
    .ExpectTextAsync("Updated in Spotify")
    .DeactivateAndAssertNoWorkAsync();
```

The harness should provide deterministic operation completion, virtual time,
route navigation, focus assertions, lifecycle leak detection, and standard
capability states such as unavailable, denied, revoked, stale, and delayed.

### 10. Templates organized by complexity

One starter cannot teach every level. Provide repository templates such as:

- `basic`: local state and actions;
- `data`: one typed capability, loading/error/empty states, and subscription;
- `media`: playback projection, scrubber, optimistic commands, and progress;
- `multipage`: responsive navigation, cached pages, nested detail, and Back; and
- `companion`: exact-port companion JSON with explicit security constraints.

Templates should use the same production helpers as first-party widgets and
remain small enough to read end to end.

## Recommended Spotify structure after the helpers exist

Spotify should remain an advanced reference, but it should be separated by
responsibility. A reasonable target structure is:

```text
SpotifyWidget/
  SpotifyWidget.cs             lifecycle wiring and action dispatch
  SpotifyState.cs              immutable domain/view state
  SpotifyController.cs         capability calls and commands
  SpotifyRoutes.cs             navigation and route data
  Views/
    SpotifyRootView.cs
    PlayerView.cs
    QueueView.cs
    PlaylistsView.cs
    DevicesView.cs
    SetupView.cs
  styles/default.gbss
```

Splitting files alone does not remove complexity, but it makes the remaining
domain complexity reviewable. Framework helpers should remove coordination
code first; file splitting should then expose the actual Spotify behavior.

An ambitious but useful target is for the widget-specific C# layer to fall
below roughly 600–900 readable lines, excluding capability DTOs and generic SDK
helpers. The goal is not minimum line count. The goal is that most remaining
code describes Spotify behavior or presentation rather than task plumbing.

## Prioritized roadmap

### Phase 1: remove unsafe repetition

Completed foundation: protocol-v11 focus-edge Scroll pagination, the public
`ScrollElement.Paginate` authoring API, runtime-owned operation scopes,
immutable widget models, bounded optimistic commands, and bounded offset-paged
resources. Spotify playlists are the first resource migration; Media Sessions
is the first medium model and command migration.

Next work:

1. Generic non-paged and separately designed cursor/append resource state.
2. Migrate the remaining suitable widgets and Spotify operation families while
   preserving their explicit lifetime, ordering, and reconciliation policies.
3. Add focused recipes for provider-event merge, confirmation deadlines, and
   absolute-value command coalescing without making them implicit.

This phase should deliver the largest reduction in semaphores, cancellation
sources, task fields, locks, generation checks, and manual invalidations.

### Phase 2: improve application composition

1. Navigator and route stack.
2. Responsive navigation shell and standard page recipes.
3. Stable ID scopes and analyzer rules.
4. Split advanced samples by responsibility.

### Phase 3: improve feedback and onboarding

1. Scenario-based preview and capture.
2. Fluent deterministic test harness.
3. Complexity-tiered templates.
4. Publish the SDK and templates so authors do not require a repository-local
   project reference.

### Phase 4: broaden the ecosystem carefully

1. Publish a language-neutral runtime and snapshot wire specification.
2. Add conformance fixtures and golden messages independent of C# types.
3. Evaluate a second worker runtime only after author demand and resource
   measurements justify it.
4. Define a separate reviewed provider-development path; do not grant ordinary
   widgets ambient network, token, or operating-system authority.

## Success metrics

The improvements should be evaluated against measurable author outcomes:

- A C# developer can scaffold and run a basic widget in 15 minutes.
- A one-capability data widget requires no author-created `SemaphoreSlim`,
  `CancellationTokenSource`, or unobserved `Task` field.
- An offset-paged collection requires no author-owned page dictionary,
  stale-generation counter, cache eviction loop, or compact/wide source-ID
  parsing. This is implemented and exercised by Spotify 0.2.10.
- An optimistic mutation requires no author-owned admission task, command
  cancellation source, or stale-attempt gate. This is implemented and exercised
  by Media Sessions; domain merge and rollback callbacks remain authored.
- A multipage widget uses one route model for compact and expanded layouts.
- Authenticated and unavailable states can be previewed without real secrets.
- Advanced samples contain substantially more domain/rendering code than
  lifecycle and concurrency plumbing.
- Deactivation, stale responses, command rollback, focus restoration, and task
  cleanup receive standard reusable tests.
- The SDK preserves current message, update-rate, memory, and lifecycle bounds.
- The simple `Widget` API remains source-compatible and no more complicated.

Suggested baseline metrics for each migrated widget:

- widget-specific lines by category: domain, render, coordination, tests;
- number of author-owned tasks, cancellation sources, semaphores, and locks;
- number of explicit invalidation calls;
- number of duplicated compact/expanded nodes;
- time for a new developer to add one page and one capability-backed action;
- defects found in cancellation, stale-result, focus, and rollback testing; and
- preview coverage across compact, standard, wide, and accessible viewports.

## Risks to avoid

- **A giant framework base class.** Prefer small composable helpers.
- **Implicit polling or residency.** Every source of continuing work must remain
  explicit and lifecycle-bound.
- **A service-specific UI framework.** Spotify should use general media,
  navigation, resource, and command patterns.
- **Magic string replacement without validation.** ID helpers and action maps
  should improve diagnostics, not conceal routing.
- **Premature language expansion.** Another SDK would duplicate current
  authoring pain unless the coordination model is improved first.
- **Weakening isolation for convenience.** Easier external integration must not
  provide ambient network access, secrets, raw device IDs, or desktop authority.
- **Optimizing only for line count.** Explicit policy is preferable to concise
  but surprising behavior.

## Final recommendation

Do not replace the C# SDK or declarative widget model. Treat Spotify, Network
Controls, and Audio Mixer as design probes that reveal the same missing
application-level layer. Continue building that layer from small,
lifecycle-aware, testable primitives: operations and bounded offset-paged
resources plus general immutable state and optimistic command coordination are
now implemented and production-exercised; non-paged/cursor resources,
navigation, preview tooling, and broader migrations remain.

The protocol-v11 pagination change is a strong example to repeat: identify a
generic behavior proven by a demanding widget, move the security- and
input-sensitive portion into the host, expose a small declarative SDK surface,
and leave domain policy with the widget. Applying that same approach to other
resource variants, commands, navigation, and scenario tooling would remove
much more author plumbing without weakening the platform boundary.

The framework will be ready for sophisticated human-authored community widgets
when advanced authors mostly describe domain behavior and UI, while the SDK
owns cancellation, bounded concurrency, stale-result rejection, invalidation,
focus restoration, and deterministic testing.
