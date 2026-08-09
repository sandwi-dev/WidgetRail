# Widget Authoring Experience Review

Status: living assessment; core coordination primitives, bounded navigation, responsive focus persistence, one navigation recipe, and data-only inspection are implemented; a buildable standalone scaffold, isolated semantic preview execution, broader recipes, and onboarding remain open<br>
Date: 2026-08-09<br>
Reassessed: 2026-08-09 against current HEAD after `UI.NavigationShell`, transition-owned responsive focus recovery, SDK Gallery adoption, YT Music operation-lane migration, the first `gbar preview` contract, and standalone-scaffold, package-trust, aggregate-residency, and installed-tree byte-bound audits<br>
Scope: public widget authoring APIs, tooling, examples, and the complexity exposed by advanced widgets such as Spotify

Related: [Engineering Quality Review](engineering-quality-review.md) covers the
cross-cutting architecture, security, verification, and product-readiness
findings that qualify this authoring assessment.

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
- responsive composition outside the new navigation-shell recipe; and
- extensive manual invalidation.

This is not unique to Spotify. Similar patterns remain in Audio Mixer, Network
Controls, Games & Apps, and YT Music; Media Sessions now demonstrates the
shorter model/command path. The framework can express advanced widgets, but
some safe implementation patterns still require broader migration and recipes.

The recommended direction is evolutionary, not a rewrite: retain `Widget`, the
declarative protocol, GBSS, AppContainer isolation, and typed capabilities.
Add optional high-level SDK primitives that encode the safe patterns already
implemented repeatedly by first-party widgets.

The Spotify and SDK Gallery work support this direction. Protocol-v11 host-owned
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
The public `WidgetResource<TValue>` now coordinates non-paged reads, while
`WidgetNavigator<TRoute>` and `WidgetIds` own bounded route history, route
cancellation, exact-scope Back, focus restoration, and validated hierarchical
IDs. `UI.NavigationShell` now renders one destination model as compact tabs or
an expanded rail around one shared content subtree, with optional expanded-only
content. Protocol v13 now separates an explicit bounded focus-persistence
identity from element and action IDs; the shell emits one shared key only for a
destination's compact and rail presentations. SDK Gallery is the production-
style reference for the composition, and YT Music now replaces one more hand-written
cancellation/generation family with an Active Latest operation lane.
Cursor/append resources, additional page recipes, an ID/action analyzer, and
broader migrations remain open.

The first scenario-tooling slice is deliberately manifest-only. `gbar preview`
can validate and list bounded manifest-declared states without resolving or
loading provider assemblies. Selecting a scenario fails closed until execution
can move behind an isolated, forcibly terminable process boundary. Semantic
snapshot execution, interaction, viewport rendering, capture, fake-service
lifecycle support, and template integration remain open.

`gbar render` is now also strictly data-only. It rejects DLL input before
resolving a type or touching an output path, closing the full-trust author-code
escape and making `gbar dev` the only executable CLI integration path. An
initial path-level 4 MiB preflight was racy; the current source now reads one
restrictively shared stream through a ceiling-plus-one detector and rejects
legacy assembly-only options for JSON. A deterministic misreported-length test
covers the consumed-byte bound, and the focused Release CLI suite passes 48/48.
The broader tradeoff is an explicit tooling gap: there is no headless isolated
command that turns widget/scenario code into a snapshot. Authors must currently
add an author-controlled typed-fake test to persist `SnapshotJson` or use the
interactive overlay path.

The scaffold is currently a repository-contributor convenience, not a
standalone community authoring product. Inside this checkout, `gbar new widget`
finds `WidgetSdk.csproj` and emits a working source reference. Outside it, the
same successful command emits an unpublished `0.1.0-preview.1` package
reference and tells the author to build. The generated README has also drifted:
the current worktree replaces its rejected DLL render command with `gbar dev`
and teaches bounded idle unload rather than keep-alive. Until a matching
versioned SDK/template release exists, however, the CLI should fail early or
require an explicit local SDK path rather than generating a knowingly broken
project. Its new snapshot guidance also needs an actual generated typed-fake
test/exporter; the template currently refers to one but creates none.

The presentation layer is further along than an earlier gap list implied.
Pressed-state delivery, bounded subtree translation, responsive branches and
Grid, row wrapping, per-edge borders, semantic code text, activate-to-adjust
Sliders, and the modern controller component set are implemented. They should
be treated as current authoring tools, not roadmap proposals. Higher-level page
recipes beyond the navigation shell and packaged-font support remain distinct
open work.

## Evidence from the repository

Approximate implementation sizes in the current worktree illustrate the gap
between a minimal and an application-like widget:

| Widget | Approximate C# size | What it demonstrates |
| --- | ---: | --- |
| Clock sample | 40 lines | Deterministic render and one action |
| SDK Gallery | About 400 lines | Public components, navigation, responsive shell, and local state |
| Recent Apps | 340 lines | One event-driven provider surface |
| Media Sessions | 735 lines | Selection, commands, progress, and provider lifecycle |
| Games & Apps | 1,150 lines | Navigation, paging, private state, and launch commands |
| YT Music | 1,150 lines | Connection lifecycle and optimistic media state |
| Spotify | About 2,020 lines | OAuth, playback, four destinations, bounded paging, caching, and local playback |
| Network Controls | About 2,000 lines | Multiple providers, discovery, commands, and failure states |
| Audio Mixer | About 2,500 lines | Dense state reconciliation and optimistic controls |

The exact counts are less important than the shape of the code. Spotify owns
multiple `Task`, `CancellationTokenSource`, `SemaphoreSlim`, generation, cache,
pending-operation, loading, error, and focus fields. Other advanced widgets own
similar structures. Its presentation also includes about 500 lines of GBSS and
its dedicated test program is about 1,200 lines. Those tests are valuable, but
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
- [`WidgetResource<TValue>`](../src/WidgetSdk/WidgetResource.cs) supplies a
  non-paged immutable loading snapshot, bounded freshness caching, duplicate
  coalescing, safe errors/retry, last-good retention, subscription publication,
  stale-result rejection, reset, and lifecycle cancellation.
- [`WidgetModel<TState>`](../src/WidgetSdk/WidgetModel.cs) supplies atomic
  immutable snapshots, serialized updates, equality-based invalidation, exact
  revisions, result-bearing mutations, and contained diagnostic observation.
- [`WidgetNavigator<TRoute>`](../src/WidgetSdk/WidgetNavigator.cs) supplies a
  bounded route stack, stable route input scopes, exact nested Back handling,
  return-focus memory, and route-owned cancellation.
- [`WidgetIds`](../src/WidgetSdk/WidgetIds.cs) supplies validated hierarchical
  scopes and deterministic opaque `KeyedId` leaves for durable domain keys.
- [`UI.NavigationShell`](../src/WidgetSdk/NavigationShell.cs) supplies one
  validated two-to-eight destination model, compact tabs, an expanded rail,
  optional expanded-only persistent content, and explicit controller focus
  edges around one shared page subtree.
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

Implementation status reports current-worktree Release results of Widget SDK
84/84, Gbar CLI 48/48, SDK Gallery 6/6, YT Music 48/48, Focus Navigation 41
checks, Declarative Renderer 4,632 checks, and a green full aggregate. Static
inspection confirms that the four managed programs register those case totals,
and focused YT source coverage includes cancellation-ignoring stale ordinary
and authorization failures plus lifecycle exit. This review cycle did not
independently execute those suites or inspect a retained result bundle.
Disabled and busy destinations intentionally remain focusable while activation
is suppressed; renderer and focus-test source encode that contract. The
repository still lacks retained structured results or an immutable CI run, and
available default packages are one source version behind for YT Music and SDK
Gallery, so they are not current packaged evidence. Real controller, companion,
physical-display, performance-trace, and representative visual evidence remain
pending.

These primitives prevent several classes of misuse, but the author still has to
compose them into an application architecture. Spotify is evidence that the
remaining gap is mostly reusable coordination, application composition, and
proof rather than basic rendering capability.

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
commands. The next targets are broader helper migrations, responsive
navigation recipes, and analyzer support; cursor and append/infinite-feed
resource semantics need a separate design
rather than being implied by the offset-paged API.

No authenticated Player, Queue, Playlists, or Devices screenshots were found in
the current evidence set; the stored Spotify images still cover configuration
and setup. The semantic and interaction tests are stronger, but credential-free
visual scenarios remain necessary to assess the complete playback UI.

## Reassessment after navigation-shell and scenario-preview changes

The responsive navigation work closes one of the earlier high-priority
composition gaps. `UI.NavigationShell` accepts one selected destination, one
content entry focus target, one shared content subtree, and two to eight stable
destinations. It generates distinct compact-tab and expanded-rail control IDs,
publishes selected and disabled semantics, supplies wrapping directional focus
edges, and optionally inserts an expanded-only persistent pane. Built-in GBSS
owns the standard dimensions, focus, selected, pressed, and responsive styles.

Disabled and busy destinations intentionally remain in the focus ring. The host
separates navigation from activation so users can focus an unavailable control,
hear or inspect its label/state, and then receive no action dispatch. Native
renderer, controller-navigation, and widget-surface focus tests exercise that
contract. Keeping those destinations in the shell's explicit wrap ring and in
responsive persistence avoids dynamic focus ordering and resize-induced focus
teleportation.

Protocol v13 now carries `focusPersistenceId` only on focusable nodes. When a
resize hides the previously focused compact or expanded presentation, the host
restores focus to the single visible, same-scope control that explicitly shares
that key. Omitted, ambiguous, and cross-scope matches fail closed. Action IDs
remain routing intent and may legitimately be shared across different
destinations. `UI.NavigationShell` derives one bounded persistence key from each
logical destination ID and publishes it only on that destination's compact and
rail controls. This resolves the independent audit's identity concern in the
current code. Reconciliation now runs on resize, snapshot/presentation refresh,
and focus restoration rather than on every paint, so settled frames perform no
focus-persistence traversal. Implementation status reports focused managed and
native passes, but retained results and a direct test of the scheduling edges
are still pending. The native resolver intentionally classifies disabled and
busy candidates as focusable; responsive persistence should preserve that
stable logical destination while activation remains unavailable.

SDK Gallery has adopted the shell around its existing `WidgetNavigator`, so it
now provides a copyable example of one route model and one page subtree across
compact and expanded layouts. Focus and validation tests cover shared content,
distinct responsive IDs, selected-state accessibility, traversal, destination
bounds, and invalid declarations. This is a credible human-authorable path for
the main shell of a Spotify-style widget. Spotify itself has not yet migrated,
and persistent media panels, collection/detail pages, error pages, and action
footers still need comparable recipes.

YT Music independently demonstrates that the coordination helpers continue to
pay off outside Spotify. Its transport reconciliation burst now uses one Active
Latest `WidgetOperations` lane instead of its own lock, task set, linked
cancellation source, generation counter, continuation cleanup, and deactivation
drain. Much of YT Music remains manually coordinated, but this is evidence that
the helper can replace real lifecycle plumbing incrementally.

The migration now applies the Latest currency contract to every attempt-local
outcome. Successful snapshots, retry status, and authorization failure all
perform their final `IsCurrent` check under the state lock shared with
replacement admission. Cancellation-ignoring fakes prove stale ordinary and
authorization failures cannot change status, pending optimism, connection, or
replacement execution; lifecycle-exit 401 is also rejected. Current connect,
poll, and reconciliation 401 behavior still returns the widget to pairing.

The new `gbar preview` command establishes only the data-only discovery portion
of the scenario recommendation. A bounded `gbar.scenarios.json` manifest names
a provider assembly and declared factory metadata. Listing validates strict
JSON, bounded paths, names, descriptions, and scenario count without resolving
or loading the assembly. Selecting any scenario fails with a fixed diagnostic
before assembly resolution; it does not invoke reflection, start a timeout task,
or write an output snapshot.

Semantic execution remains open because an in-process timeout cannot terminate
arbitrary managed code and would leave scenario code with the full filesystem,
network, process, and credential authority of the CLI. The next step is a
dedicated production-equivalent AppContainer/Job/IPC process boundary that can
be forcibly terminated, followed by deterministic state fixtures, the real
widget test host, and native capture without exposing secrets or live services.

Removing DLL execution is the right security decision, but it increases the
priority of that isolated scenario worker. The eventual command should replace
the removed convenience rather than reintroduce it under a trust flag: execute
inside the production-equivalent worker boundary, accept typed fake services,
produce a bounded validated snapshot, and support deterministic interaction and
capture. Canonical documentation snippets should generate fixtures through the
same public test/scenario API instead of embedding one-off serialization code
in every sample.

## Reassessment of standalone repository scaffolding

The generated source itself is readable and appropriately small: one `Widget`
subclass, stable semantic IDs, a three-button focus graph, typed shortcuts, a
bounded integer state, and direct invalidation. A human author can understand
that code without framework internals. The failure is the surrounding product
contract, not the C# example.

The same `gbar new widget` command has two materially different outcomes. Under
this repository it discovers `WidgetSdk.csproj` and generates a working
`ProjectReference`. Anywhere else it silently references unpublished
`GameBarAlternative.WidgetSdk` version `0.1.0-preview.1`, reports success, and
prints a build command that cannot restore through the supported workflow.
There is no explicit SDK path/version option. Tests execute only below this
repository and assert the local-reference branch, so they cannot prove the
distributed CLI experience.

The original generated README was internally incomplete: it asked `gbar render`
to execute a DLL and then replayed the nonexistent output. The worktree now
uses the correct `gbar dev` path and explains that render is data-only. It still
supplies a replay file but no typed-fake snapshot-export test or static
snapshot, leaving the advertised deterministic replay loop incomplete.

The framework should treat CLI, template, SDK package, compatibility contract,
and generated README as one versioned deliverable. Before a public SDK exists,
external scaffolding should fail early or require an explicit validated local
SDK path. The production milestone is a clean-directory test that uses only the
released CLI/feed, builds the generated project, executes every README command,
validates and packages it, and leaves a standalone repository that another
developer can clone and build. The generated source is already a credible human
starter; publishing and testing the complete dependency/tooling loop is what
turns it into a platform authoring experience.

## Reassessment of GitHub sharing and package trust

The current workflow is a credible developer-preview distribution path, not a
finished public ecosystem. An author can produce a deterministic
`.gbarwidget`, attach it to an exact GitHub Release, and give recipients an
exact SHA-256 pin. The CLI maps the shorthand to one named release asset,
applies bounded HTTPS/redirect/size/time rules, validates the locked bytes, and
installs the immutable version disabled. Updates cannot be published while the
widget is enabled, selecting a version remains a separate disabled-only action,
and replacement content receives a new digest-derived runtime authority instead
of inheriting consent or secrets. These are strong foundations.

The workflow still asks too much of both authors and users. Authors have no
supported public SDK package/template feed or signing command. They must arrange
an independent authenticated channel for the digest and explain why a GitHub
release plus a matching hash does not prove authorship. Current HEAD now gives
Settings users an honest **Unsigned · publisher unverified** state,
the full sealed content digest, **Enable unsigned widget** copy, digest prefixes
for version selection, and digest-bound permission language. It still cannot
show the acquisition source, a verified signer, or a version capability delta;
the catalog does not retain a host-owned acquisition receipt.

The exact-byte contract is rechecked at meaningful boundaries: catalog
discovery recomputes the sealed content tree before enablement, and the bridge
does so again before publishing runtime authority. One implementation detail
still needs hardening before authors can rely on that workflow as fully
resource-bounded. Installed manifest and integrity-metadata reads preflight
pathname length before a separate unbounded `ReadAllBytes`, while the tree
hasher totals initial stream lengths but reads to EOF without counting consumed
bytes. Stable tampering fails closed, but concurrent growth/replacement can do
work beyond the documented package ceilings. The catalog should reuse the
maximum-plus-one, single-handle invariant now applied by `gbar render`, with
deterministic growing/misreported-stream tests.

The next author workflow should preserve the safe mechanics while reducing this
trust ceremony. `gbar pack`/future `gbar publish` should emit one canonical
release receipt containing the exact asset name, transferred digest, sealed
content digest, declared identity/capabilities, and—when implemented—a verified
publisher signature. Installation should persist bounded provenance outside
package-controlled content while omitting credentials, URL query data, and full
local paths. Settings should distinguish unsigned development,
verified signer, unknown signer, invalid signature, and revoked signer, and
show capability/authority changes before enabling an update. Until that exists,
documentation must keep calling GitHub sharing unsigned developer preview and
must not imply that AppContainer containment verifies the author.

## Reassessment of residency defaults and ecosystem cost

The framework makes an individual worker's resource request explicit and
enforces it with a one-process Job. The current worktree now also reserves a
default application envelope of eight workers and 512 MiB of summed declared
Job limits before lazy launch. One exact trusted Settings worker is separately
bounded/accounted so a full application budget cannot hide the control plane.
This is a substantial improvement, but Settings still exposes neither
per-widget measured cost nor budget ownership/remediation, and existing
performance evidence covers one selected worker rather than ecosystem-scale
accumulation.

The current worktree also repairs the ordinary authoring default. The
controller-widget template and Clock sample now choose five-minute
`unload-after-idle` and teach reconstruction from durable state. The protocol
retains `keep-alive` as its compatibility default, while authors must choose it
explicitly when continuous Background state is a real requirement. Conversely,
Spotify still needs keep-alive to preserve one temporary authorization
operation, demonstrating that residency remains too coarse a substitute for a
bounded critical-work lease.

The host-owned admission now has exact lifetime ownership: the runtime acquires
a lease immediately before process creation and releases it only when that
exact process exits or its pipe/process/Job session is detached. Direct runtime
tests now cover pre-launch denial plus exact release across crash/relaunch and
cooperative stop; current bridge tests cover normal crash, timeout, and idle
unload. Remaining fault injection includes connection/protocol/companion
failure, live-process pipe disconnect, disable/removal, catalog replacement,
and bridge shutdown. The remaining product path is to make the user contract
complete.

Today a capacity refusal is flattened to generic `request_failed` plus message,
and the native client drops the code. An open widget without a snapshot can
therefore remain on `Starting isolated ...`: its four-second failure text is
used by the tray hint, not the in-widget footer. Settings remains reachable and
shows aggregate application worker and declared-memory totals, but it lists
worker identities only for recorded failures; a pre-launch capacity denial is
correctly not recorded as one. Authors and users consequently cannot tell who
owns the budget or reach the relevant unload/disable action from the refusal.
This needs a typed, bounded admission result, a persistent controller-facing
error state, and Settings attribution of each reservation and its residency and
remediation eligibility.

`keep-alive` should surface its
continuing cost during package review. The runtime should offer a bounded,
lifecycle-visible lease for rare
temporary work that genuinely must survive Background, or move that work into
the responsible broker. A supervisor budget may reclaim only widgets that
explicitly opted into unloading; it should never silently reinterpret
`keep-alive`. When pinned workers exhaust the budget, the author/user needs a
clear refusal and remediation path rather than hidden overcommit.

This keeps a simple widget simple while ensuring that “works in isolation” is
not the only performance standard. Author documentation and conformance tests
should include restart-from-durable-state, idle unload/reopen, denial at the
aggregate boundary, and measured multi-widget cost alongside the current
single-worker manifest checks.

## Reconciliation of earlier framework-gap findings

The following items were rechecked against public SDK declarations, protocol
validation, native behavior, and current author documentation. This prevents
implemented facilities from remaining on the roadmap under an older name.

| Earlier concern | Current verified contract | Remaining boundary |
| --- | --- | --- |
| `:pressed` was parsed but not rendered | GBSS publishes a pressed computed map, and the native host applies it only to the physically held action target. | No framework gap remains; widgets should style the semantic state instead of simulating it. |
| Motion lacked subtree translation | `translate-x` / `translate-y` move the complete presented subtree, including clip, hit-test, focus, accessibility, and Scroll geometry, with bounded retargetable transitions. | Shell-level presentation choreography remains host product work, not a widget API. |
| Settings rows, pickers, action sheets, scrubbers, toasts, and media/app tiles were missing | These are public `UI.*` compositions with stable generated IDs, semantic classes, validation limits, and controller behavior. | Standard page recipes beyond the implemented navigation shell remain open. |
| Layout lacked per-edge borders and responsive wrap/Grid | GBSS supports independent edge colors/widths and Row wrapping; `UI.ResponsiveGrid` provides protocol-v8 row-major reflow. | Virtualized/sectioned collections remain open. |
| Compact and expanded layouts required ad hoc host checks | Protocol-v9 `.VisibleWhen(...)` / `UI.ResponsiveBranch(...)` excludes inactive subtrees from every semantic system, and `UI.NavigationShell` now authors compact tabs and an expanded rail from one destination set around one shared content subtree. Protocol-v13 `focusPersistenceId` maps only explicitly equivalent, same-scope presentations and remains separate from action routing. Disabled and busy destinations deliberately remain focusable while activation is suppressed. | Other adaptive page structures and widgets not yet migrated to the shell still require lower-level composition; the public guide should make the focusable-but-inert contract unmistakable. |
| Sliders always consumed Left/Right during navigation | Protocol-v10 `.RequireControllerActivation()` reserves A/B for a host-owned adjustment mode; outside that mode all directions remain navigation. | A Slider cannot combine activation-first mode with a separate A activation action. |
| Typography lacked semantic monospace | `UI.CodeText` supplies bounded, whitespace-preserving semantic code text. | Packaged fonts and browser-style fallback stacks remain unavailable. |

The two correctness concerns raised in the previous pass are now reconciled:
YT Music guards attempt-local failure commits, and disabled destinations are
correctly focusable-but-inert by platform contract. The highest-priority
remaining authoring work is application composition and proof: cursor/append
resource variants, page
recipes beyond the navigation shell, broader coordination-helper migrations,
an ID/action analyzer, interactive credential-free scenarios, and native visual
capture.
Documentation should keep those
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

### 3. Non-paged resource state — implemented

Use `CreateResource<TValue>` for one current provider value that does not have
page semantics:

```csharp
private readonly WidgetResource<PlayerSnapshot> _player;

public PlayerWidget()
{
    _player = CreateResource("player.snapshot", new()
    {
        Load = token => LoadPlayerSnapshotAsync(token),
        MapError = _ => new WidgetResourceError(
            "player_unavailable", "Playback could not be loaded."),
        CacheDuration = TimeSpan.FromSeconds(30),
    });
}
```

The immutable snapshot exposes `NotLoaded`, `Loading`, `Ready`, `Refreshing`,
and `Error`, plus the current value, safe error, and revision. `EnsureLoaded`
reuses a fresh successful value, `Refresh` and `Retry` force a read,
`Publish` accepts an authoritative subscription value, and `Reset` cancels and
clears the resource. Identical in-flight reads join one operation. Late reads
cannot overwrite a reset or newer subscription publication. The default
Active lifetime, five-minute freshness window, and last-good retention are
explicit options rather than hidden polling or retry policy.

The resource owns state invalidation and bounded coordination, but the widget
still chooses when to read, how to render each state, and how to merge provider
events. It never starts work from `Render`. Cursor pages and append/infinite
feeds remain separate open designs.

### 4. Bounded offset-paged resource state — implemented

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
edge. The widget still owns copy and visual composition. Cursor paging,
append/infinite feeds, and a `UI.ResourcePage` composition are not implemented
and remain later design work.

### 5. Command and optimistic-update helper — implemented

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

### 6. Navigation and focus model — implemented

Advanced widgets need a small controller-native router:

```csharp
private readonly WidgetNavigator<Route> _navigation;

public PlayerWidget() => _navigation = CreateNavigator(
    "player.navigation", Route.Player);

_navigation.Navigate(Route.Playlists, sourceFocusId);
_navigation.Push(new Route.Playlist(id), sourceFocusId);
_navigation.Back(sourceFocusId);
```

The implemented navigator owns:

- a bounded route stack;
- selected root destination;
- source focus ID and return focus restoration;
- nested input-scope identity;
- Back action publication only when a nested route exists; and
- a route-lifetime cancellation token that is canceled before each route
  invalidation; and
- exact pressed-B handling only when the action carries the current route's
  active `InputScopeId`.

`Scope(root)` applies the current generated input scope and publishes B only
for nested routes. `Value.InitialFocusId` restores remembered route focus, and
`RouteCancellationToken` owns work that must end when the route changes. Depth
defaults to eight and is capped at 16; known routes default and cap at 32. A
capacity overflow is an explicit `RejectedCapacity` result. Every open-widget
action now carries the active scope ID, including A activation, focused
shortcuts, scope-root shortcuts, and Slider changes, so stale nested actions
fail closed instead of being inferred from element-name conventions.

SDK Gallery is the production-style public migration. Its tabs use
`Navigate`, Picker and ActionSheet use `Push`, nested B uses `TryHandleBack`,
and leaving a route cancels its token before the new snapshot is published.

The responsive composition is now implemented as `UI.NavigationShell`. It maps
one destination set to an expanded rail and compact tabs, keeps one content
subtree, optionally adds a persistent expanded pane, and publishes explicit
focus entry points. Protocol v13 preserves the logical destination across a
responsive switch through one explicit persistence key even though each
presentation has a distinct stable element ID and actions may be shared.
Low-level Row, Stack, visibility, and input-scope APIs remain
available as escape hatches. SDK Gallery proves the router/shell composition;
Spotify and other multi-page widgets still need migration evidence.

### 7. Stable ID scopes — implemented

Large widgets can use the public hierarchical ID builder instead of assembling
every string by hand:

```csharp
var ids = WidgetIds.Scope("spotify").Scope(mode).Scope("player");
UI.IconButton(..., ids.Id("play-toggle"), ...);
```

Generated composite suffixes should remain documented and stable. A build-time
analyzer or `gbar validate` rule should detect duplicate literal IDs, unstable
index-derived IDs where a durable domain key is available, invalid focus
targets, and action IDs with no known handler in declarative action maps.

`Scope` and `Id` validate each dot-free segment and the final protocol ID.
`KeyedId(name, durableKey)` hashes a bounded durable key into a deterministic
opaque suffix, so unsafe provider text and provider identity do not enter the
snapshot. This helper creates stable identifiers; it does not register action
handlers or prove that an ID is semantically stable. The analyzer remains open.

Spotify's paged-resource migration no longer needs compact/wide source-ID
parsing, and `UI.NavigationShell` gives each responsive presentation distinct
element IDs. Protocol v13 now keeps the three concerns separate: element IDs
identify nodes, action IDs identify intent, and focus-persistence IDs identify
explicit cross-presentation equivalence. A future analyzer should preserve and
validate this separation.

### 8. Layout recipes and semantic styling — partially implemented

The component library is useful, but advanced widgets still carry hundreds of
lines of GBSS. Continue adding reusable, theme-respecting recipes rather than
service-specific controls:

- adaptive navigation shell — implemented as `UI.NavigationShell` with built-in
  semantic GBSS and responsive focus recovery;
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
media/app tiles, Toast, and now one reusable navigation shell. The remaining
recipes should compose those contracts; they should not introduce parallel
controls with different focus or styling semantics.

### 9. Scenario-based preview and visual testing — manifest listing implemented

The first public contract is a deliberately static scenario manifest:

```json
{
  "version": 1,
  "assembly": "bin/Release/net8.0/MyWidget.Scenarios.dll",
  "providerType": "Dev.Example.MyWidgetScenarios",
  "scenarios": [
    { "name": "playing", "factory": "Playing" },
    { "name": "empty", "factory": "Empty" }
  ]
}
```

`gbar preview .` validates and lists declarations without resolving or loading
the assembly. `gbar preview . --scenario playing` currently fails closed with a
fixed diagnostic, and `--output` writes nothing. Tests cover strict parsing,
bounded declarations, traversal rejection, listing without an assembly, and
fail-closed execution/output behavior.

This is useful discovery and validation infrastructure, but it does not yet
preview authenticated-looking, empty, denied, or error surfaces. Executing those
factories in the CLI process would grant ambient authority and a timeout could
not safely terminate them, so semantic execution must wait for isolation.

The remaining target is an interactive and visual layer over the same named
states:

```powershell
gbar preview . --scenario playing --viewport compact
gbar capture . --all-scenarios --viewport compact,standard,wide,accessible
gbar test . --controller-replay replays/smoke.json
```

That layer should instantiate the real widget with typed fake services, drive
lifecycle and actions, render through the native host, show clipping, and
isolate and forcibly terminate misbehaving scenario code. It should never require real
OAuth, hardware, or user secrets. Visual artifacts should be deterministic
enough for review, with pixel comparisons used cautiously and semantic
snapshots retained as the primary contract.

The newly expanded public guide and quickstart make both NavigationShell and
scenario preview much easier to discover, but their copyable examples are not
compiled by `Documentation.Tests`. The invalid quickstart glyph and stale
focus/action wording are corrected, and the guide now lists the additive
protocol-v1–v13 feature matrix. Canonical end-to-end snippets should still be
compile-tested against the same SDK reference an external widget uses so
documentation cannot remain green while recommended code drifts.

### 10. Higher-level test harness

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

### 11. Templates organized by complexity

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
immutable widget models, bounded optimistic commands, non-paged resources, and
bounded offset-paged resources. Spotify playlists are the first paged-resource
migration; Media Sessions is the first medium model and command migration.

Next work:

1. Generalize the cancellation-ignoring stale-success, stale-failure, and
   lifecycle-exit fixtures from YT Music into a reusable operation-migration
   test recipe.
2. Define credential/session generation semantics before any flow allows
   credentials to be replaced while requests are in flight.
3. Design cursor/append resource state separately from current-value and
   offset-page semantics.
4. Migrate the remaining suitable widgets and Spotify operation families while
   preserving their explicit lifetime, ordering, and reconciliation policies.
5. Add focused recipes for provider-event merge, confirmation deadlines, and
   absolute-value command coalescing without making them implicit.

This phase should deliver the largest reduction in semaphores, cancellation
sources, task fields, locks, generation checks, and manual invalidations.

### Phase 2: improve application composition

Completed foundation: `WidgetNavigator<TRoute>`, `WidgetIds`/`KeyedId`, exact
active-scope action propagation, protocol-v13 explicit focus persistence,
`UI.NavigationShell`, host focus recovery between responsive presentations,
and the SDK Gallery production-style migration.

1. Document and test the deliberate focusable-but-inert disabled/busy contract
   at the public `UI.NavigationShell` boundary, including wrap, selected
   destination, activation suppression, and responsive persistence.
2. Add a transition-scheduling seam test and measure the bounded persistence
   resolver so the contract is proven correct and dormant on stable frames.
3. Standard page recipes beyond the navigation shell.
4. Stable-ID/action analyzer rules.
5. Split and migrate advanced samples by responsibility, including adopting the
   shell in a genuinely complex multi-page widget.

### Phase 3: improve feedback and onboarding

Completed foundation: strict bounded scenario manifests, assembly-free listing,
and fail-closed scenario selection with contract and safety tests.

1. Publish a matching versioned SDK/template set and prove `gbar new` through
   build, supported preview, replay, validation, and packaging from a clean
   directory outside this repository.
2. Move scenario assembly execution into a dedicated production-equivalent
   AppContainer/Job/IPC process boundary.
3. Real-widget scenarios with typed fake services, lifecycle, and actions.
4. Viewport-aware native preview, capture, and controller replay.
5. Fluent deterministic test harness.
6. Compile-test the canonical copyable guide and quickstart examples against
   the public SDK; the corrected glyph and protocol-matrix regressions
   demonstrate why prose-only checks are insufficient.
7. Complexity-tiered templates.

### Phase 4: broaden the ecosystem carefully

1. Finish supervisor-owned aggregate residency with a typed controller-facing
   refusal state, per-widget resource visibility/remediation, optional reclaim
   only for unload-eligible workers, and bounded temporary critical-work
   leases.
2. Make installed-tree verification authoritative over consumed bytes, add an
   honest unsigned acquisition receipt, then publisher signing,
   rotation/revocation, signed update metadata, and verified/unverified package
   states before public community distribution.
3. Publish a language-neutral runtime and snapshot wire specification.
4. Add conformance fixtures and golden messages independent of C# types.
5. Evaluate a second worker runtime only after author demand and resource
   measurements justify it.
6. Define a separate reviewed provider-development path; do not grant ordinary
   widgets ambient network, token, or operating-system authority.

## Success metrics

The improvements should be evaluated against measurable author outcomes:

- A C# developer can scaffold and run a basic widget in 15 minutes from an
  unrelated empty directory using only released prerequisites, without a
  platform source checkout or manual project-file repair.
- A one-capability data widget requires no author-created `SemaphoreSlim`,
  `CancellationTokenSource`, or unobserved `Task` field.
- Latest-wins work cannot commit success, retry status, authorization state, or
  rollback after supersession unless an explicit external generation proves
  that result remains globally authoritative.
- An offset-paged collection requires no author-owned page dictionary,
  stale-generation counter, cache eviction loop, or compact/wide source-ID
  parsing. This is implemented and exercised by Spotify 0.2.10.
- An optimistic mutation requires no author-owned admission task, command
  cancellation source, or stale-attempt gate. This is implemented and exercised
  by Media Sessions; domain merge and rollback callbacks remain authored.
- A multipage widget uses one route model and destination set for compact and
  expanded layouts. SDK Gallery now demonstrates this; a larger production
  migration remains the next proof.
- Disabled and busy navigation destinations remain reachable and retain stable
  responsive focus, while pointer/controller activation is consistently
  suppressed and the unavailable state is exposed clearly.
- Named authenticated-looking and unavailable scenarios can be declared and
  listed without loading code, but semantic execution remains unavailable until
  the isolated preview worker exists. Real-widget interaction and native visual
  capture also remain open.
- Every generated README command is exercised by the release gate; the
  scaffold restores/builds through the supported SDK artifact and never emits
  an unavailable placeholder dependency or a rejected execution mode.
- A GitHub-hosted widget can be packaged and acquired by exact immutable bytes;
  before public distribution, Settings clearly distinguishes unsigned from
  verified publishers, shows a bounded source/digest receipt and version
  capability changes, and rejects invalid or revoked signatures.
- A long session has an enforced aggregate resident-process/memory envelope;
  ordinary scaffolded widgets unload after a documented idle bound, genuinely
  retained work uses an explicit bounded lease, and Settings attributes current
  resource ownership before asking the user to resolve capacity pressure.
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
  explicit, lifecycle-bound, and accounted within the host-wide worker budget.
- **A service-specific UI framework.** Spotify should use general media,
  navigation, resource, and command patterns.
- **Magic string replacement without validation.** ID helpers and action maps
  should improve diagnostics, not conceal routing.
- **Premature language expansion.** Another SDK would duplicate current
  authoring pain unless the coordination model is improved first.
- **Weakening isolation for convenience.** Easier external integration must not
  provide ambient network access, secrets, raw device IDs, or desktop authority.
- **Treating in-process preview as a sandbox.** Static scenario assemblies are
  trusted local developer code; a timeout reports a stuck factory but cannot
  safely terminate arbitrary managed work in the CLI process.
- **Optimizing only for line count.** Explicit policy is preferable to concise
  but surprising behavior.

## Final recommendation

Do not replace the C# SDK or declarative widget model. Treat Spotify, Network
Controls, and Audio Mixer as design probes that reveal the same missing
application-level layer. Continue building that layer from small,
lifecycle-aware, testable primitives: operations, non-paged and bounded
offset-paged resources, immutable state, optimistic command coordination,
bounded navigation, and stable-ID scopes are now implemented and exercised;
the first responsive navigation recipe, explicit responsive focus identity,
and bounded scenario-manifest listing are also implemented. Isolated semantic
scenario execution, cursor/append resources, additional page recipes,
an ID/action analyzer, interactive/native preview and capture, publication, and
broader migrations remain.

The protocol-v11 pagination change is a strong example to repeat: identify a
generic behavior proven by a demanding widget, move the security- and
input-sensitive portion into the host, expose a small declarative SDK surface,
and leave domain policy with the widget. Applying that same approach to cursor
resource variants, additional responsive recipes, broader migrations, and the
interactive/native half of scenario tooling would remove much more author
plumbing without weakening the platform boundary.

The framework will be ready for sophisticated human-authored community widgets
when advanced authors mostly describe domain behavior and UI, while the SDK
owns cancellation, bounded concurrency, invalidation, focus restoration, and
deterministic testing, and the recommended patterns make every success and
failure commit prove it is still current.
