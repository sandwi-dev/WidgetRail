# Widget Authoring Experience Review

Status: active independent assessment<br>
Last reassessed: 2026-08-12 against integrated `main` `04fbdc0`<br>
Scope: public Widget SDK APIs, tooling, examples, packages, diagnostics, and the
experience of building basic through full application-scale widgets

Detailed prior analysis is preserved in
[`history/widget-authoring-experience-review/2026-08-12T02-47-00-07-00.md`](history/widget-authoring-experience-review/2026-08-12T02-47-00-07-00.md).
That snapshot is evidence only. Implementation order comes solely from
[`delivery-plan.md`](delivery-plan.md).

Related: [`engineering-quality-review.md`](engineering-quality-review.md)
covers cross-cutting architecture, correctness, security, performance,
verification, and product-readiness findings.

## Executive conclusion

A new developer can build a useful simple widget without reimplementing the
host, renderer, focus engine, lifecycle, packaging, or isolation machinery.
Advanced media, system-control, multipage, and large-collection widgets also
have real reusable foundations: scoped operation ownership, immutable model
publication, commands, paged resources, stable focus/identity, navigation,
responsive declarative UI, typed capabilities, scenario hosts, scaffolding,
validation, packaging, installation, version selection, rollback, and removal.

The framework is not yet a finished public platform. Exact cache-isolated SDK
consumption, self-contained external-repository setup, a full-application
reference, and the shortest edit/build/validate/preview/scenario/pack loop are
now proven. The largest remaining author costs are public version/update
governance, separately hosted third-party proof, broader interactive/native
preview, composed failure/accessibility proof, and publisher/update trust.

C#/.NET remains a reasonable widget-authoring choice. It gives ordinary
developers strong tooling, async primitives, records, package management,
testing, and Windows API access while the native C++ host owns the latency-
sensitive trusted renderer/input/window boundary. The important design choice
is the process and capability boundary, not using one language everywhere.

## Supported author scale

Installed widgets may be full applications. A widget may privately own large
domain models, databases, indexes, caches, navigation stacks, computation, and
helper processes. The SDK must not force that private application into a small
memory, one-process, or tiny-state quota.

Only shared-host submissions are bounded. Authors project the current route or
virtualized collection window across IPC. The native host validates message,
tree, string, recursion, update-rate, queue, cache, native/GPU resource, and
capability budgets before allocation. Rejection is explicit and retains the
last valid presentation where appropriate; content is never silently
truncated. Paging, virtualization, handles, coalescing, and backpressure are
the scalable path.

## Current author journey

| Step | Current support | Remaining friction |
| --- | --- | --- |
| Choose a starter | Basic, data, media, and multipage templates generate atomically. | The catalog is local; public discovery/version policy is not final. |
| Obtain the SDK | DLV-194/199 prove a copied distribution restores the exact content-versioned SDK from a fixture-local feed into a fresh isolated NuGet cache for build and pack; its nupkg exposes exactly the two public runtime DLLs. | No externally published/versioned package or update policy exists yet. |
| Build UI | Declarative components, responsive layout, styling, semantic roles, focus IDs, collections, modal/navigation shells, common recipes, and an accepted application-scale reference exist. Loading/reset snapshots now validate with null focus while Error and Ready retain exact focus targets. | More advanced high-level recipes should be driven by a real widget need rather than another abstract framework layer. |
| Own state/work | `WidgetModel<T>`, scoped operations, commands, tickers, nonpaged and paged resources, latest/single-flight/coalesced policies, cancellation, and invalidation exist. | The migration recipe across these helpers is spread across examples and flagship implementations. |
| Use privileged services | Typed, narrow capabilities cover media, audio, network, Bluetooth, app library, settings, diagnostics, artwork, and other host-owned authority. | Provider-authoring is a separate trusted extension problem and is not yet a public workflow. |
| Navigate and act | Stable identities, responsive focus persistence, nested Back, contextual shortcuts, gesture ownership, action sheets, picker/list/grid/slider/text-entry semantics exist. | Some live focus-edge, physical-controller, and assistive-technology evidence remains. |
| Test | Deterministic fake services, semantic scenario hosts, focused fixtures, preview contracts, and MSTest.Sdk 4.3.2 for new projects coexist with existing runners. | Isolated interactive preview and broader real-host accessibility/failure scenarios remain. |
| Package and install | Deterministic validation/pack/install/list/select/rollback/remove and versioned immutable local packages exist. | Verified publisher identity, acquisition provenance, automatic update discovery, and public gallery policy remain open. |
| Share through GitHub | Repository-independent package graphs, checkout-path-free artifacts, fresh-cache SDK consumption, and one documented self-contained export/onboarding command are proven locally. | A truly separate hosted repository and approved publication/update channel remain; the local fixture is no longer described as that external hosting proof. |

## Framework capability assessment

### Implemented and reusable

- Explicit lifecycle states and cancellation-bound work.
- Latest, single-flight, serialized, and coalesced operation ownership.
- Immutable render-facing state and deterministic snapshots.
- Nonpaged resource state plus bounded occurrence-aware cursor/offset paging.
- Optimistic command state with authoritative reconciliation and failure policy.
- Stable element/collection identities and focus restoration.
- Navigation shells, nested scopes, modal routes, Back, shortcuts, sliders,
  text entry, pickers, action sheets, lists, grids, and responsive layout.
- Typed capability requests with narrow consent/gesture authority.
- Trusted opaque resource handles for artwork and host-owned assets.
- Declarative styling with accessibility policy as the final authority.
- Deterministic scenario/fake-service testing and generated starter tests.
- Strict package validation, immutable version installs, enable/disable,
  selection, rollback, and removal.
- Isolation that is a real process/capability boundary, not an in-process
  timeout presented as a sandbox.

### Partially complete

- Continuous lists: the generic machinery and Spotify correction exist, but
  composed live 12/12/5 and reverse-edge verification remain.
- Action feedback: managed admission and native composed failure presentation
  exist; physical controller and assistive-technology proof remain.
- Accessibility: deterministic semantics and real HWND/UIA fixtures are broad;
  Narrator/MSAA and packaged traversal remain manual.
- Preview: DLV-135 adds deterministic Launcher Experience semantic/offscreen
  scenarios; a safe isolated interactive
  author loop and reliable production-window targeting remain unfinished.
- Layout recipes: reusable primitives, Launcher Experience native presets, and
  DLV-135 scaffold/validate/preview/pack/inspect/install/list/remove tooling
  exist. DLV-134/145/146 now prove all four presets through the ordinary
  production worker/bridge/host path with separate provider fallback. Installed
  custom-pack selection, last-good reload, and safe-start recovery remain the
  next author-to-product gap.
- Documentation examples: canonical starters compile, but many prose snippets
  are still not extracted into API-checked projects.

### Open platform work

#### Flagship proof currently has the wrong trust tier

Game Launcher was intended to prove that an independent author can build a
full application-scale widget. The current implementation instead lives under
`src/FirstPartyWidgets`, uses `org.gbar.firstparty.game-launcher`, is copied into
the built-in runtime catalog, references the SDK project directly, retains an
SDK `InternalsVisibleTo("GameLauncherWidget")` exception, and reaches a native
Launcher Experience path designed around its known first-party projection.
That is useful product code, but it is not evidence that the public framework
supports the same result.

Games & Apps should remain bundled. Convert Game Launcher into a Community
reference in three explicit gates: close managed public-SDK portability gaps in
a clean external consumer; replace every identity/shape/private presentation
dependency with one generic documented contract; then install and run the
actual reference through ordinary Community package, consent, selection,
update, and removal flows. Acceptance must include a differently named fixture
using the same advanced contract so a renamed Game Launcher is not the only
consumer that passes.

- Externally published and versioned SDK/template artifacts.
- Public compatibility, deprecation, migration, and update policy.
- Verified publisher/acquisition provenance and update discovery.
- Maintainability/performance evidence as real authors extend the accepted
  advanced application reference beyond the deterministic example.
- Clear separation between ordinary widget authors and trusted capability-
  provider authors.
- A real third-party GitHub repository onboarding proof.

## Current contract review: responsive branch fit

Accepted DLV-144 gives Spotify's compact branch the same controller rail, one
focus-revealing player viewport, and shared focus identities across responsive
branches. Its 49/49 suite executes named width, height, and scale envelopes;
the refreshed accepted Release reports every first-page UIA rectangle inside
the host. This closes the immediate widget defect. A public preview/assertion
path should still report selected branch, selected-node bounds, overflow, and
focus reveal so authors do not need screenshots or native-host source reading.

## Current contract review: normalized app library

DLV-130 correctly removes the former scalar/canonical dual model and introduces
one versioned presentation containing opaque item identity, source reference,
availability, role-keyed artwork, optional attributed metadata, closed
capabilities, and optional active operation. Games & Apps and Game Launcher
consume the normalized value, and private provider identifiers, paths,
commands, package identities, URLs, and bytes remain hidden.

Accepted DLV-142 now gives source, availability, artwork, metadata provenance,
capabilities, and operations focused validators while a small composer owns
only their relationships. Diagnostics can identify the invalid subvalue and
new fields no longer require understanding one monolithic validator. The
author-facing invariant is explicit: presentation is not permission. Only a
current launchable availability paired with `Launch` capability can reach fresh
host resolution; stale, unavailable, retained-last-good, or merely displayed
entries never authorize launch.

## Recommended public architecture

Keep the framework composable rather than imposing one mandatory application
framework:

1. The widget root owns lifecycle and one committed render-facing model.
2. Domain state remains private and may be application-sized.
3. Reusable SDK owners handle operation concurrency, paging, navigation,
   commands, invalidation, and host publication.
4. Pure policies handle route transitions, action vocabulary, provider-state
   normalization, and presentation.
5. Capabilities expose only typed intent and opaque authority.
6. The current route/window crosses into the host; databases and full catalogs
   do not.
7. Tests bind every continuing task to a lifecycle/request/generation and cover
   stale, cancellation-ignoring, failure, empty, long, and boundary cases.

Do not solve complexity with a universal base view model, service locator,
generic mediator, reflection router, global task registry, or one giant
presentation/state bag. Extract a seam only when it reduces shared mutable
knowledge, isolates a policy behind focused tests, or makes a normal author
change possible without reading the complete widget.

## Starter tiers

### Basic widget

- One route and responsive snapshot.
- No privileged capability required.
- Demonstrates lifecycle, invalidation, stable IDs, accessibility, package,
  install, and removal.

### Data widget

- Typed capability or fake data source.
- Loading/empty/error/retained-last-good states.
- Refresh and bounded cancellation.

### Media widget

- Immutable now-playing state, progress ticker, optimistic command,
  reconciliation, artwork handle, contextual dashboard shortcuts, and failure
  feedback.

### Multipage widget

- Navigation shell, nested routes/modal, stable Back, focus restoration,
  responsive compact/wide presentation, and accessibility order.

### Large-collection/full-application widget

- Private storage/search/indexes and optional helper processes.
- Cursor/occurrence-aware virtualized host projection.
- Stable selection across refresh/filter/page/profile changes.
- Bounded host resources and coalesced publication.
- Provider health, retained-last-good display, exact fresh activation, and
  deterministic recovery.

The advanced tier is a reference architecture, not a different trust tier and
not a requirement that every simple widget adopt all helpers.

## Success metrics

The authoring platform is ready for external preview when a new developer can:

1. Create a repository outside this checkout.
2. Reference a versioned supported SDK/template artifact.
3. Generate a starter and understand its ownership without reading host code.
4. Run deterministic semantic and interactive fake-service scenarios.
5. Build responsive, accessible keyboard/controller navigation.
6. Use one typed capability without raw platform identifiers or commands.
7. Project a large collection without submitting its entire private model.
8. Receive precise package, protocol, focus, capability, and resource errors.
9. Pack, inspect, install, enable, select, roll back, disable, and remove it.
10. Share the repository and reproduce the same artifact graph on another
    machine without checkout-relative references.

## Prioritized review queue

1. Review DLV-148 as the first production motion/effect-budget use of the
   accepted Launcher Experience projection; authors must not gain arbitrary
   animation or compositor authority.
2. Keep DLV-135's semantic/offscreen preview claim distinct from live custom-
   pack selection. Review the serialized DLV-149/150 pair for exact installed
   version selection, metadata disclosure, removal denial, last-good reload,
   and one-activation safe start before calling the local author loop complete.
3. Keep the UIA-guided first-page smoke after each accepted
   Release; do not require a taskbar-visible production window merely for tool
   discovery. Use exact logs for real author diagnostics.
4. Schedule external-repository SDK consumption and advanced application sample
   before calling the platform public-ready.

Avoid reopening installed-widget security absent a reproducible P0 or threat-
model violation. Do not let capture tooling, wholesale test migration, or
internal refactors displace visible product and developer outcomes.
