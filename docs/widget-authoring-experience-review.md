# Widget Authoring Experience Review

Status: living assessment; runtime-owned operation scopes implemented, later recommendations open  
Date: 2026-08-08  
Reassessed: 2026-08-08 after public `WidgetOperations` and the Spotify 0.2.8 migration  
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

This is not unique to Spotify. Similar patterns appear in Audio Mixer, Network
Controls, Games & Apps, Media Sessions, and YT Music. The framework can express
advanced widgets, but it does not yet make their safe implementation routine.

The recommended direction is evolutionary, not a rewrite: retain `Widget`, the
declarative protocol, GBSS, AppContainer isolation, and typed capabilities.
Add optional high-level SDK primitives that encode the safe patterns already
implemented repeatedly by first-party widgets.

The latest Spotify work supports this direction. Protocol-v11 host-owned
focus-edge pagination and `ScrollElement.Paginate` move a generic controller
interaction out of Spotify and into the platform. The public
`WidgetOperations` coordinator now also owns bounded SingleFlight, Latest, and
Serial execution plus lifecycle cancellation/draining. Spotify has migrated its
page-loading lane to that API. Data/resource state, caches, navigation, and
optimistic commands remain author-owned, so the later recommendations remain
open.

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
- [`ScrollElement.Paginate`](../src/WidgetSdk/Elements.cs) supplies protocol-v11
  host-owned near-start and near-end focus triggers without visible paging
  buttons.
- [`WidgetTesting`](../src/WidgetSdk/WidgetTesting.cs) supplies typed fake
  capabilities and lifecycle control.
- Modern components cover common visual compositions.

These primitives prevent several classes of misuse, but the author still has to
compose them into an application architecture. Spotify is evidence that the
missing layer is coordination, not rendering capability.

## Reassessment after the Spotify 0.2.8 changes

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

The implementation also makes the remaining gap unusually clear. Spotify no
longer owns page-operation tasks, cancellation sources, or generation counters:
one Active `RunLatest` lane provides non-overlap, synchronous stale-result
authority through `IsCurrent`, and lifecycle draining. Spotify still owns two
page dictionaries, cache limits and eviction, loading and error fields, retry
behavior, and focus-ID calculation. It also infers compact versus wide
presentation by parsing the source element ID. Those remaining concerns belong
to the later resource/state/navigation milestones.

The latest changes therefore do not weaken the review's conclusion. They
provide a successful first example of moving one generic behavior into the
SDK/host, and they sharpen the next target: a paged-resource coordinator that
works with `ScrollElement.Paginate`.

No authenticated Player, Queue, Playlists, or Devices screenshots were found in
the current evidence set; the stored Spotify images still cover configuration
and setup. The semantic and interaction tests are stronger, but credential-free
visual scenarios remain necessary to assess the complete playback UI.

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

Admission is explicit (`Started`, `Joined`, `Replaced`, `Enqueued`,
`RejectedInactive`, or `RejectedCapacity`) and completion is explicit
(`Succeeded`, `Canceled`, `Superseded`, `Failed`, or `Rejected`). Latest
contexts become non-current synchronously when replacement is admitted. Bounds
are 32 tracked keys, 64 total active/pending operations, and 16 pending Serial
operations per key. Active spans Visible/Interactive, State belongs to one
exact runtime state, and Widget spans creation through Destroying.

Spotify's automatic destination and 12-row playlist paging now uses one Active
Latest lane, replacing its page task, cancellation source, and generation
counter. Other widget command/auth/polling paths can migrate separately when
their required lifetime and ordering policy are explicit.

### 2. Observable immutable widget state

Provide an optional small state container rather than requiring every widget to
hand-roll locking, snapshot copies, equality checks, and invalidation:

```csharp
private readonly WidgetModel<State> _model = new(State.Initial);

var state = _model.Value;
_model.Update(state => state with { Status = "Refreshing", IsBusy = true });
```

Recommended behavior:

- immutable snapshot reads suitable for `Render`;
- serialized updates;
- invalidate only when the value changes;
- an atomic update that can return an operation input;
- no reflection or ambient global store;
- optional history or transition observation in tests, not production; and
- compatibility with ordinary fields for simple widgets.

This should reduce lock scope and accidental `Invalidate` storms without
forcing a Redux-style architecture.

### 3. Async resource state

Loading a page currently requires data, loading, error, cache timestamp,
generation, cancellation, retry, and invalidation fields. Introduce a reusable
resource model:

```csharp
private readonly WidgetResource<QueueSummary> _queue = new();

await _queue.LoadLatestAsync(
    token => HostServices.Spotify.GetQueueAsync(token),
    cacheFor: TimeSpan.FromSeconds(5),
    cancellationToken);
```

It should represent `NotLoaded`, `Loading`, `Ready`, `Refreshing`, and `Error`,
optionally retain last-good data during refresh, reject stale completions, map
safe public errors, and expose retry. A `WidgetPagedResource<T>` should add a
bounded page window/cache, total/count handling, adjacent-page navigation,
duplicate-request suppression, deterministic eviction, and next-focus
selection.

Protocol-v11 `ScrollElement.Paginate` should remain the host-owned trigger. The
paged resource should complement it by handling the resulting near-start and
near-end actions; it should not reimplement focus-edge detection in the worker.

Pair it with a renderer-neutral composition helper:

```csharp
UI.ResourcePage(
    resource,
    ready: data => RenderQueue(data),
    empty: () => UI.EmptyState(...),
    retryAction: "queue.retry")
```

The resource owns coordination state; the widget still owns copy and visual
composition.

### 4. Command and optimistic-update helper

Media, audio, network, and settings widgets repeatedly implement pending state,
busy controls, optimistic projection, success reconciliation, and rollback.
Provide a bounded command abstraction:

```csharp
await Commands.RunOptimisticAsync(
    key: "playback",
    apply: state => state.TogglePlayback(),
    execute: token => HostServices.Spotify.ControlPlaybackAsync(command, token),
    reconcile: result => result.Playback,
    onError: error => SafePlaybackMessage(error));
```

Required semantics:

- serial or latest-wins policy chosen per key;
- automatic busy state;
- rollback to the exact pre-command revision;
- late-result rejection after lifecycle or route changes;
- absolute-value coalescing for sliders;
- safe error classification; and
- no automatic retry for mutating commands.

The helper should build on the existing controller queue rather than create a
second uncoordinated action path.

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
`ScrollElement.Paginate` authoring API, and public runtime-owned operation
scopes. Spotify page loading is the first migration.

Next work:

1. Observable immutable widget state.
2. Async and paged resource state integrated with `ScrollElement.Paginate`.
3. Optimistic command helper integrated with the controller queue.
4. Migrate one medium widget and the remaining Spotify operation families to
   validate the APIs.

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
- A paged collection requires no author-owned page dictionary, stale-generation
  counter, cache eviction loop, or compact/wide source-ID parsing.
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
lifecycle-aware, testable primitives: operations are now implemented; state,
resources, commands, navigation, and preview tooling remain.

The protocol-v11 pagination change is a strong example to repeat: identify a
generic behavior proven by a demanding widget, move the security- and
input-sensitive portion into the host, expose a small declarative SDK surface,
and leave domain policy with the widget. Applying that same approach to page
resources, operation ownership, commands, and navigation would remove much
more author plumbing without weakening the platform boundary.

The framework will be ready for sophisticated human-authored community widgets
when advanced authors mostly describe domain behavior and UI, while the SDK
owns cancellation, bounded concurrency, stale-result rejection, invalidation,
focus restoration, and deterministic testing.
