# Games & Apps reference

Status: automatic trusted-Game reconciliation, durable authority-scoped user
curation, and trusted close-on-confirmed-launch are implemented. A bounded
Steam launcher adapter supplies the current evidence-backed Game classification;
packaged hands-on verification and additional sources remain.

Games & Apps replaces Recent Apps in the bundled overlay catalog. It is a
manifest-backed first-party package that uses the public SDK, generic
`WidgetWorkerHost`, package-specific capability-free AppContainer, authenticated
broker, normal lifecycle, and declarative renderer. It is not a privileged
native shell panel.

Games & Apps intentionally remains the small curated dashboard tray. The
separate [Playnite Library](playnite-library.md) traverses the complete installed-game
catalog through the same trusted provider cache and launch authority without
copying provider/source policy into either widget.

The old Recent Apps project remains useful historical coverage for the
read-only foreground-activity API, but it is no longer one of the packaged
dashboard widgets. Games & Apps lists installed launch registrations rather
than applications the user happened to foreground. Steam manifests are an
explicit game-library source, not a guess based on process or executable names.

## Current user experience

- Entering Visible or Interactive reads package-private state and reconciles a
  bounded current catalog. Entries classified `Game` by the trusted provider
  are appended automatically; `Application` and `Unknown` remain opt-in.
  Reactivation shows the last-good Library immediately while a lifecycle-owned
  reconciliation refreshes it in the background. Y requests the same bounded
  refresh explicitly.
- **Add applications** opens a nested Catalog backed by a vertical controller
  Scroll; A toggles the focused entry in/out of the Library and B returns.
  Next/Previous page controls retain at most 32 application rows in any one
  semantic snapshot while the trusted catalog is traversed through bounded
  opaque cursor pages.
  Library X removes the focused entry. Removing a Game records its exact
  authority-scoped SavedId as an exclusion, so the same identity stays absent
  after restart, disappearance, and reappearance. Adding it explicitly clears
  that exclusion. Catalog and Library removal first build one immutable state
  delta, then publish it only after the bounded private-state compare-and-swap
  succeeds. A write failure leaves the complete prior Library visible; a CAS
  conflict reapplies only the requested delta to the newer durable state, so
  unrelated membership, order, display rows, and exclusions do not disappear.
  The bounded display projection is canonical before validation, including
  trimming whitespace exposed by the 20-rune truncation boundary; one long
  label therefore cannot invalidate and reset an otherwise valid Library.
- Both surfaces use stable hashed UI IDs; widget snapshots contain only display
  names, conservative kinds, broker-issued short-lived AppIds, and
  authority-scoped SavedIds. Resolved Library entries may also contain a
  bounded 48 by 48 PNG icon rasterized by the trusted provider; Catalog
  discovery remains text-only. Provider launch tokens stay inside the host and
  last only for the current provider snapshot.
- Library Up/Down selects one full-width application row. The icon and complete
  two-line name are rendered inside the same focus target, so its outline never
  lands on an inner text fragment. A resolves that row's stable SavedId again
  and launches only the resulting current AppId while the widget is Interactive.
  After confirmed provider success, that item moves to
  the front and the new order is persisted; failure preserves order and
  actionable focus. Removing a focused item selects the next surviving row, or
  the previous row when the removed item was last.
- Curation, automatic-membership provenance, exclusions, recent-first order,
  selected SavedId, and a display-only last-good projection are stored in a
  schema-v3 document through
  `HostServices.PrivateState`. During single-user development, v1, v2,
  unsupported, or invalid documents reset atomically to an empty v3 state before
  the current trusted catalog reconciles. No legacy membership, exclusion,
  selection, or display row is partially retained, and no legacy value can
  authorize launch. A bounded compare-and-swap merge reapplies the widget's
  exact delta to newer valid v3 state, so a concurrent exclusion is not
  overwritten.
- The last-good projection contains only SavedId, a sanitized 20-scalar display
  prefix, and the closed Game/Application/Unknown kind. It never stores AppId,
  icon pixels, path, AUMID, Steam ID, command, or other provider identity. A
  fresh worker renders those rows immediately as disabled **Checking…** tiles.
  Fresh provider resolution atomically replaces each tile with a short-lived
  AppId without changing SavedId-derived focus or order; unresolved rows cannot
  launch. Cached AppIds are discarded on each active-lifetime transition and
  after a failed authority refresh. Launch performs one final SavedId resolution,
  so a provider revision that retires the displayed AppId cannot authorize stale
  launch authority.
- Missing SavedIds remain visible as bounded disabled order tombstones.
  Reappearance with the same SavedId restores launch using a fresh short-lived
  AppId. A different SavedId is an independent new Game even when the display
  title is identical; no title matching occurs.
  Auto-added entries that cease to be classified Game are hidden, while an
  explicitly added Application or Unknown entry remains user-owned.
- A successful launch requests `CloseOnConfirmedSuccess`. The broker emits the
  host effect only after the exact trusted provider call succeeds, and the
  native host accepts it only for the current widget and runtime generation.
  Enqueueing, timeout, stale generation, denial, or failure leaves the overlay
  visible with focus and an actionable status.
- Catalog `Next page` requests another bounded page and moves selection to its
  first item; `Previous page` follows the provider's exact reverse cursor. The
  widget renders only the current 32-row page, so a large provider catalog
  cannot overflow the 2,048-node snapshot budget. The
  curated order remains capped at the public 64-SavedId resolver limit, with
  existing user order taking precedence over newly discovered Games.
- Newly committed automatic additions produce one count-only, non-focusable,
  lifecycle-bound toast. An unchanged refresh produces no extra write or
  repeated notice. Exclusions retain at most 128 IDs so the worst-case schema-v3
  document remains below the 64 KiB private-state limit; at that bound a new
  removal is refused without changing visible or durable membership.
- Permission denied, lifecycle denied, provider unavailable, healthy empty,
  and generic failure states use the shared Card, EmptyState, and Alert
  hierarchy. Recovery remains controller reachable through one explicit retry
  action, while loading is non-focusable and cannot strand controller focus.
- A failed fresh authority refresh retains the display-only Library with an
  explicit unavailable status; it never promotes stale rows to launchable.
  Initial activation, reactivation, and explicit Y refresh keep that last-good
  Library tree in place while reconciliation runs, including exactly one
  reachable **Add applications** action. Background work may change status or
  disable a row while its authority is checked, but it does not replace a Ready
  Library with a transient loading tree.
  Moving to Background cancels the active load lifetime and rejects even a
  cancellation-ignoring late provider result. The manifest uses
  `unload-after-idle` with a 120-second idle interval; the bridge can recreate
  the worker from its last validated snapshot when it is needed again.

## Managed responsibility map

The DLV-027 boundary is behavioral, not a file-size convention. Before this
split, the widget class itself composed every visual tree, maintained Catalog
page history, interpreted schema-v3 membership/projection/exclusion rules,
implemented the private-state compare-and-swap loop, called providers, admitted
actions, and owned lifecycle cancellation. It also mirrored the committed
schema in three mutable membership/provenance/exclusion lists.

The current implementation has four deliberately narrow internal seams:

| Responsibility | Owner | May not own |
| --- | --- | --- |
| Active lifetime, action admission, provider calls, current AppId launch admission, one state lock, one command semaphore, publication/invalidation | `GamesAppsWidget` | A second lifecycle/state coordinator |
| Library, Catalog, loading, empty, and failure tree composition from one immutable input value; stable hashed element IDs | `GamesAppsPresentation` | Host services, locks, persistence, or provider calls |
| Current Catalog page plus opaque forward/reverse cursors as bounded immutable transitions | `GamesAppsCatalogPolicy` | Library membership, private state, or cursor parsing |
| Schema-v3 normalization, add/remove/order/exclusion policy, display projection, trusted-Game reconciliation, conflict merge, and the two-attempt CAS transaction | `GamesAppsLibraryPolicy` and `GamesAppsLibraryStore` | Rendering, lifecycle, launch, or ambient host-service ownership |

`GamesAppsLibraryState` is now the single committed membership/provenance/
exclusion/order value; widget-local mirror lists were removed. Rendering captures
one immutable `GamesAppsPresentationState` under the widget's existing lock and
the pure presenter consumes only that revision. The store receives explicit
read/write functions for one bounded transaction, so removal, reconciliation,
and CAS conflict behavior can be tested without constructing or rendering the
widget. These types remain package-internal and do not introduce a public SDK
framework or a new persistence schema.

## Responsive and accessibility envelope

The surface is a single bounded vertical controller hierarchy. The header and
section hierarchy stay fixed while the Library or Catalog Scroll owns the
remaining height; focused rows are revealed by the host's shared Scroll
geometry. The package has no monitor-resolution branch or widget-local focus
offset.

Deterministic coverage uses compact 560×420, standard 880×520, and wide
1120×620 logical surfaces. The compact accessibility profile combines 150%
text scaling with a 144-DPI pixel-scale render, while standard/default coverage
starts at 100%. Root/content/Scroll minimum heights are zero, so short surfaces
yield space to the focus-follow Scroll instead of clipping the header or an
essential action. Names are sanitized to 120 characters and remain a two-line,
ellipsis-bounded part of the one focusable Tile. High-contrast captures and
semantic labels provide non-color state evidence; physical display,
controller, and assistive-technology sign-off remains manual release evidence.
The Library and Catalog now request a safe 600-DIP preferred height, up from the
430-DIP Library baseline. That adds more than two normal 78-DIP row pitches when
the host safe area allows it; the existing minimums and Scroll behavior still
bound compact and 150% layouts.

## Public SDK contract

Authors declare the read grant when the library is core functionality and the
launch grant only when opening a selected app is an optional feature:

```json
{
  "permissions": ["system.apps.library.read.v1"],
  "optionalPermissions": [
    "system.apps.library.launch.v1",
    "system.apps.running.read.v1"
  ]
}
```

The corresponding public service is `HostServices.AppLibrary`:

```csharp
var page = await HostServices.AppLibrary.QueryAsync(
    new WidgetAppLibraryQuery(InstalledOnly: true),
    limit: 32,
    refresh: true,
    cancellationToken);

// Save SavedId—not AppId—in package-private state.
var savedIds = page.Items.Select(item => item.SavedId).Take(64).ToArray();

// On the next worker/host run, reconcile durable IDs to current launch tokens.
var currentItems = await HostServices.AppLibrary.ResolveSavedAsync(
    savedIds,
    cancellationToken);

await HostServices.AppLibrary.LaunchAsync(
    currentItems[0].AppId,
    WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
    cancellationToken);
```

`system.apps.running.read.v1` is a separate optional read grant. **Add running
app** performs one on-demand observation and returns only visible programs that
the trusted host maps exactly to one current installed registration. The widget
receives a sanitized name, kind/source label, opaque SavedId, and short-lived
revision—never a PID, HWND, path, command, AUMID, package identity, or retained
process handle. The host visits at most 256 top-level windows before all
eligibility filters and returns at most 64 deduplicated candidates. Adding
rechecks both the observation revision and current registration, and validates
the complete confirmed item with the ordinary app-library rules before the
existing bounded SavedId CAS mutation; malformed confirmation leaves every
existing row and the durable store unchanged. Denial leaves the ordinary
Catalog usable.

`QueryAsync` accepts a bounded installed/kind/source/sort query, an optional
opaque cursor with its direction, and a page size of 1–64. It returns
`WidgetAppLibraryPage`, containing sanitized `WidgetAppLibraryItem` records,
opaque Before/After cursors, and a provider revision. Each item has a short-lived
`AppId` for launch, a durable `SavedId` for private state, and one authoritative
versioned `Presentation`. That immutable value carries the sanitized title and
closed kind, opaque source reference, closed availability/launchability,
role-keyed artwork, optional revisioned metadata with attribution, a closed
capability set, and an optional current operation. No legacy scalar aliases are
retained. Cursors are traversal-only: never parse them or use them as launch or
durable identity.
`ResolveSavedAsync` accepts at most 64 unique SavedIds, refreshes the provider,
preserves request order, and omits apps that are no longer available. A
SavedId is scoped by a persisted host key plus the authenticated publisher and
package IDs; it remains stable across worker/host restarts and package updates,
but it cannot be correlated or reused by another widget package. The widget
instance ID is intentionally not part of this durable authority.

Current items may include a Tile artwork role in `Presentation.Artwork`; its
handle is an opaque generation-bound registration that is neither a path nor a
URL. Pass it to `UI.Artwork`,
`Button.LeadingArtwork`, or `TileArtwork.FromHandle` and retain a semantic glyph
fallback when it is absent. The host asks the trusted provider for pixels only
when the artwork is rendered. Demand is admitted quickly and provider I/O runs
on the bridge's bounded request lane, so a stalled icon source does not hold
input, lifecycle, catalog, or snapshot traffic. Completion is accepted only for
the exact current worker and artwork generations. The provider derives a
host-only artwork revision from its exact revalidation record; changing that
record rotates the opaque handle and the native per-row decoded/bitmap cache
key even when the SavedId and launch identity remain stable. The host decodes
at most 12 KiB / 64 by 64 PNG sources into a 32-entry / 32 MiB in-memory LRU
cache. Handles and pixels are never persisted in widget private state, and no
disk artwork cache is created.

For an installed Steam registration, the trusted provider may associate the
exact numeric app identity with an allowlisted `_icon.png`, `_icon.jpg`, or
`_icon.jpeg` file in that Steam root's local `appcache/librarycache`. Catalog
enumeration and refresh retain only the current bounded host-internal lazy
locator set (at most 4,096 entries) and do not open the cache directory or any
candidate image. Removing a catalog row retires its locator generation rather
than allowing later churn to revive it, but only when that source refresh wins
the provider's current-generation commit; canceled or superseded refreshes do
not mutate current artwork handles. Exact artwork demand opens
only regular non-reparse objects beneath the trusted root, chooses an allowlisted
candidate, captures object evidence, caps the source at 1 MiB / 4,096 per
dimension / 16,777,216 decoded pixels, normalizes it to the same 64-pixel /
12-KiB PNG contract, and retains at most 64 decoded entries. File identity,
length, file-change, and last-write evidence are part of the host-only observed
revision. Replacement or removal rejects the stale demand and rotates only the
affected opaque handle after refresh. Cache paths, Steam AppIds, file identities,
and source bytes never enter widget snapshots or private state.

`LaunchAsync` accepts only the short-lived opaque `AppId` returned by a current
page or resolution. Widgets must never persist AppId. Widgets never
receive a path, `.lnk` filename, target executable, command line, AUMID,
package identity, launcher identity, PID, or window handle.

Read requires Visible or Interactive. Launch is a control operation and
requires Interactive; it cannot use dashboard-gesture authority. Declaration,
user consent, authenticated package/publisher/instance identity, current
lifecycle, payload validation, and provider validation are independent gates.
The SDK validates source, availability, artwork, metadata, capabilities, and
operation values through separate focused owners before one narrow relationship
composer runs. Games & Apps additionally requires a freshly resolved
`Installed` row with matching explicit launchability and `Launch` capability;
unavailable, stale-source, and retained last-good display rows never authorize
launch.

## Trusted Windows provider boundary

The current provider merges bounded trusted Windows sources: current-user/
all-user Start Menu Programs shortcuts, the current user's Shell `AppsFolder`
namespace, installed Microsoft/Xbox package registrations, and registered Steam
libraries, plus automatically discovered local Epic and GOG installed
registrations when their supported local data exists. It:

- treats Windows-installed and Steam libraries as ordinary implementations of
  one private source contract, with source-owned discovery, exact resolution,
  launch, artwork, health, and version behavior rather than a central
  source-specific switch;
- enumerates at most 4,096 `.lnk` candidates, to a maximum directory depth of
  16, without following reparse points;
- accepts bounded shortcuts whose resolved target is an `.exe` or `.com`;
- enumerates at most 2,048 AppsFolder items on one bounded process-wide Shell
  STA lane, retains only a canonical AppUserModelID (AUMID) plus sanitized
  display text, and treats a malformed/disappearing item as an isolated skip;
- enumerates at most 4,096 current-user packages through the supported Windows
  [`PackageManager.FindPackagesForUser`](https://learn.microsoft.com/windows/uwp/api/windows.management.deployment.packagemanager.findpackagesforuser)
  API and classifies at most 1,024 applications as games only when the package's
  bounded [`MicrosoftGame.config`](https://learn.microsoft.com/gaming/gdk/docs/features/common/game-config/microsoftgameconfig-overview)
  names that exact registered application ID through its documented
  [`Executable Id`](https://learn.microsoft.com/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-executable);
  an icon, title, package name, or install path is never treated as game evidence;
- discovers at most 32 Steam library roots and 4,096 bounded
  `appmanifest_<id>.acf` files, accepts only a matching positive numeric AppId
  plus sanitized name, classifies those registrations as games, and prefers an
  exact duplicate registration with current trusted local artwork before the
  deterministic manifest-path tie-break;
- reads at most 4,096 bounded `.item` files only from Epic's fixed ProgramData
  installed-manifest directory when that directory exists;
  unknown format versions, duplicate identities, unsafe/reparse paths, partial
  writes, unsupported application records, and missing exact executables fail
  closed without exposing manifest fields;
- reads at most 4,096 machine-wide registrations from GOG's fixed 32-bit and
  64-bit registry roots when those registrations exist and requires
  the matching bounded `goggame-<product-id>.info` file under an existing,
  non-reparse install root; a malformed or duplicate product ID, unsafe path,
  partial file read, or mismatched product/name is isolated as unavailable or
  degraded without publishing launch authority; this is best-effort installed
  evidence rather than an official GOG API or exhaustive catalog;
- sanitizes display names, deduplicates the same trusted target identity, and
  serves the normalized catalog through revision-bound pages of at most 64;
- assigns random opaque IDs that stay stable only while that registration
  remains in the current provider snapshot; and
- rasterizes a shortcut or AppsFolder Shell icon on demand to a bounded 48 by
  48 RGBA PNG, caches it by the exact registration revalidation key, and
  returns only pixels; Steam local PNG/JPEG artwork is separately decoded on a
  bounded non-STA artwork lane so catalog and lifecycle traffic remain live;
  and
- exposes a separate stable private provider fingerprint only to the host
  broker, which derives non-reversible authority-scoped SavedIds with HMAC; and
- keeps shortcut paths, raw provider identities, arguments, host key, and file
  fingerprints out of widget IPC; Steam AppIds, manifests, and library paths
  remain private by the same rule.

Launch does not trust a stale opaque-ID lookup by itself. Immediately before
launch, the provider re-enumerates the exact source on the Shell STA lane.
A shortcut must still have one matching scope, target identity, full path, and
content fingerprint; AppsFolder must still expose exactly one matching
canonical AUMID and revalidation key. A Microsoft/Xbox game must still expose
the same package generation, exact AUMID, and matching game-configuration
evidence. Shortcuts use only Shell `open` on the
exact fully qualified `.lnk`, without supplied arguments, working directory,
elevation verb, or owner window. AppsFolder activation uses
`IApplicationActivationManager.ActivateApplication` with the exact revalidated
AUMID and null arguments; the returned PID is discarded. Missing, moved,
changed, duplicated, or unknown registrations fail as `app_not_found`;
platform and Shell failures are sanitized before returning to widget code.
Steam launch also re-reads the exact manifest, requires the same identity,
location, and content hash, then opens only
`steam://rungameid/<numeric-id>` without widget-supplied arguments.
Epic launch likewise re-reads the exact current manifest and executable
evidence, then constructs the fixed Epic launcher URI from validated
provider-private catalog components. Widget code never supplies a URI, path,
argument, or Epic identifier.
GOG rows deliberately expose no Launch capability. GOG does not document a
supported external launch contract for this fixed registry/info evidence, so
both first-party widgets render the provider-agnostic **Play unavailable** state
and no Play request crosses the widget/broker/provider boundary. Registry keys,
product IDs, paths, and info-file bytes never enter widget IPC or private state.

## Honest limitations

- Discovery covers bounded Start Menu `.lnk`, current-user AppsFolder/AUMID,
  installed Microsoft/Xbox package registrations with explicit game evidence,
  registered Steam manifests, and installed-only Epic manifests and GOG
  registrations discovered automatically from their supported local data. Other
  launcher catalogs are not integrated; package
  registration is not a promise that every alias,
  launcher-owned game, account-owned title, or machine policy will be visible.
- Curation is durable for the package, but deduplication across launchers,
  source-aware grouping, additional evidence-backed game sources, and broader
  source reconciliation remain tracked as [GBA-033](known-issues.md).
- Shell icons are available for resolved curated Start Menu or AppsFolder
  entries, and installed Steam games may use the bounded trusted local cache
  association described above. Missing, malformed, stale, unsupported, or
  over-budget artwork uses the host semantic Play glyph; broad Catalog
  discovery intentionally does not rasterize hundreds of icons.
- The public kind enum supports Unknown, Application, and Game. Start Menu and
  AppsFolder entries remain conservative Applications; reviewed Steam manifests
  and exact Microsoft game-configuration evidence are classified as Games.
  Filename/path/title/icon guessing is not used.
- There is no search, grouping, install/uninstall,
  game history, foreground switching, running-program capture, file picker, or
  arbitrary executable/path launch.
- Reconciliation occurs on activation and explicit refresh; event-driven source
  change observation and packaged performance evidence remain open.
- Responsive intrinsic-height and Catalog-only loading regressions are green;
  the refreshed packaged visual/controller pass remains tracked as
  [GBA-038](known-issues.md).

## Executable evidence

Focused tests cover first and later trusted-Game discovery, idempotence, atomic
v1/v2/unsupported/invalid-state reset, current-v3 restart continuity,
automatic-versus-explicit provenance, bounded exclusions, concurrent
compare-and-swap exclusion, exact one-row Catalog removal across Back,
invalidation, delayed and failed fresh-worker reconciliation, write failure,
and a forced CAS conflict, disappearance/reappearance, same-title identity
replacement, stable order/focus across AppId rotation, stale action rejection,
healthy empty and sanitized failure states, Catalog add/remove,
confirmed recent-first ordering, nearest-row removal focus, opaque selected
launch, bidirectional 32-row Catalog cursor paging,
64-entry long-name Library snapshots, shared component classes, and responsive
WRSS contracts. Provider tests cover
lazy refresh, sanitization and bounds, opaque-ID lifetime, payload privacy,
on-demand icon caching and bounds, exact shortcut/AUMID revalidation,
constrained Shell/packaged/Steam activation, STA queue cancellation,
direct normalized adapter contracts, duplicate-name separation, independently
retained last-good source state, stale-generation rejection, terminal drain,
source-failure isolation, sanitized errors, and non-mutating real Start Menu,
AppsFolder, package-registration, and Steam scans. Broker, SDK, bridge, Settings,
and first-party conformance suites
cover separate read/launch consent, lifecycle denial, invalid payload/backend
data, transport mapping, permission copy, packaged AppContainer startup, render,
and a simulated launch.

The DLV-027 focused Release run is retained at
`artifacts/verification/20260810T102345Z-7c1b0cdd/verification-result.json`.
It passes Games & Apps 55/55 (including direct policy, Catalog, presentation,
and source-boundary cases), generic worker lifecycle 9/9, Windows private state
10/10, the unchanged fresh installed-worker/AppContainer conformance sequence
6/6, and documentation validation across 52 Markdown files. No canonical
aggregate was run for this behavior-preserving managed refactor.

The retained auth-free capture path renders automatic Library, Catalog, and
mixed Library snapshots from the real installed package and generic worker
through production shared styles and native renderer sources at compact,
standard, accessible, and wide/high-contrast profiles. This is local automated
evidence, not a public-release claim. A packaged controller/visual playtest,
broader Windows catalog matrix, and
authoritative game sources remain required.
