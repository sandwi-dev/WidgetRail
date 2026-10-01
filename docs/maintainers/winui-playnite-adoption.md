# Playnite indexed adoption boundary

2026-09-28 implementation checkpoint. Browse now uses the indexed SDK source in
the production widget. Home still uses its existing cursor rail. This does not yet
claim native UI acceptance, complete WinUI styling, or measured performance parity.

## Implemented service checkpoint

`CaptureQueryAsync` now returns `PlayniteLibraryCapturedQuery` with immutable
ordered games, nested metadata, authority, source observations, stale status,
catalog revision and retrieval time. `ReadRange` projects 1–64 exact positions
without registering thousands of artwork handles. Count is exact for the service
query; the widget must still compute its final title/fixed-row/filter membership.

Captured artwork validates the row/query/game/role/revision and uses the existing
byte-bounded cache independently of the old cursor registry. Four provider calls
may run concurrently without holding the catalog gate. There is no historical
Bridge image endpoint: an uncached request retrieves the captured game's current
image, never another game's pixels, and cannot promise historic artwork content.

The service cancels/drains catalog and artwork work during disposal, including
late cancellation-ignoring providers and client-disposal failure. Legacy
mutation/details operations retain caller lifetimes; late projection/registration
rejects the retired service rather than claiming those operations were drained.

All 28 PackageRuntimeTests pass, including nine capture/lifetime tests. Evidence:
`artifacts/winui-playnite-capture-retirement-tests.log` and the matching binlogs in
`artifacts/winui-playnite/`. The first test run's cancellation assertion was too
specific about exception subtype and was corrected to test OperationCanceledException.
The production Browse route consumes this capture; Home retains its cursor behavior.

## Implemented Browse projection and presentation checkpoint

`PlayniteLibraryBrowseQuery` computes exact final Browse membership from one
captured service query and the same authoritative organization used for display.
It preserves provider order after title overrides, exclusions, favorites, source
and saved-ID filtering, and freezes category/variant presentation. It does not
inject Home's manual/unavailable/title-match fixed rows. Membership construction
keeps lightweight identities; `ReadRange` projects only 1–64 demanded rows. Sparse
display membership maps back to exact provider positions for both actions and art.

`PlayniteLibraryBrowseContent` captures action enablement and launch presentation
alongside the query. `PlayniteLibraryIndexedBrowse.Options` provides the SDK source
callbacks, using the existing poster renderer and shared game-option definitions.
Its callback receives the captured Browse item directly. The owner must handle that
exact game, rather than finding its generated control ID in a cursor window.

`PlayniteLibraryPresentation.Render(..., indexedBrowse: source)` now renders the
actual Browse page around a native indexed grid: existing navigation, filters,
count, hints, background surface and error/empty states remain in place. The grid
owns its viewport; there is no outer scroll region, cursor anchor or eager first
page on this path. Entry uses the source's typed logical focus request. Empty
indexed membership never falls back to stale cursor items supplied in status state.

The production `PlayniteLibraryWidget` now calls this indexed overload. Its Browse
cursor, retained-item declaration cache, cursor paging dispatch and cursor reload
retirement operation have been removed. The SDK source is the only item owner;
`IndexedBrowseStatus` is a status-only adapter for the shared Home/Browse page,
with no rows, cursors or second cache. Render-facing Browse publication belongs to
the existing `WidgetModel<PlayniteLibraryRenderState>`.

`Operations.RunLatest` owns capture/refresh cancellation. The widget gate fences
source generation, exact membership, authority and page state against rendering.
A superseded or cancelled capture cannot publish a source. Unchanged membership
and query semantics use a content revision, preserving logical item identity;
search, sort, category or membership changes publish a new query. Failed refresh
keeps usable last-good data for the same semantic query. A failed new filter retires
the prior source, so old games cannot be presented as matching the failed query. A cancelled semantic change retries after reactivation,
rather than mistaking retained old results for the newly selected query.

Captured rows supply exact game targets to the common production handlers. Modal
availability follows the logical query owner, not native realization or retained
range leases. Close publishes the original `FocusedCollectionItem` through
`source.Enter`; it is valid only for the current query and that close request ID.
A later RS search request cannot replay the consumed modal return. Modal refresh
preserves the same parent owner. Row artwork uses captured source callbacks,
independently of the old Home/Details artwork pin registry.

Warm display metadata and source observations are persisted using lightweight
captured game records, without projecting artwork or visual trees. Organization
updates reproject the captured query; unchanged projection is a no-op. Membership
changes replace the logical owner, while favorite/category presentation changes
with stable membership retain it. Launch still revalidates the exact saved game
against the application service and rejects a retired owner before launch/results.

Validation: 149 widget tests pass and all 28 application `PackageRuntimeTests` pass.
Tests cover exact membership, production Browse declarations without inline
posters, public SDK range/action/artwork paths, deep item 80 modal entry and exact
return, parent scope rejection, later RS search, favorite content revision,
superseded uncooperative capture, cancellation/reopen, retained refresh failure,
and queued old-game actions after query replacement. Existing cursor assertions
were migrated to explicit logical-query or SDK range assertions; Home cursor
coverage remains intact. One queued-action test initially raced with the expected
busy-content binding retirement; it now admits the burst before busy publication.

Commands use `dotnet test --project` with unique binary logs under
`artifacts/winui-playnite/`. No native window, installed package or controller was
launched by this workstream. Remaining delivery gates are integration with the
WinUI shell/modal controls, native rendering/input checks, real Bridge data and
performance measurements. Home's separate horizontal indexed conversion remains
outstanding.

## Reuse existing service behavior

`Application/PlayniteLibraryApplicationService.cs` already fetches the entire
bounded catalog (64 games/request, 10,000 maximum), validates total/offsets and
unique IDs, then publishes a last-good catalog. Bridge failures retain prior data.
`QueryWithAuthorityAsync` creates an ordered traversal using `ApplyQuery`; cursor
tokens are a traversal ID plus local offset. There is no need to issue new raw
Bridge requests for each WinUI-realized range. There is also no remote snapshot
token, so the validated local catalog is not a transactional server snapshot.

Preserve installed/hidden/category/source/search/favorite filters, deterministic
sort tie-breakers, Home activity/favorite order and independent Home/Library queries.
The widget's final `LoadPageAsync` projection additionally applies saved title
overrides, exclusions, favorite filtering and fixed Home rows. Compute exact indexed
membership after those rules; `MatchingGameCount` alone can be incorrect.

## Freeze the query, not just its order

The existing service traversal freezes positions but overlays latest `_lastGood`
game values when projecting results. A new indexed query must capture both ordered
identity and immutable lightweight content. Range projection slices that captured
query. Publish a new query when membership/order changes; use content revisions only
when every key and position remains the same. Preserve current policy that Home
ordering remains stable after favorite mutation until refresh.

## Actions and details

The production action core now accepts `PlayniteLibraryGameTarget`: an immutable
selected item plus a logical-owner validity callback and optional cursor anchor
operation. Existing cursor entry points capture this target once; delayed launch
work no longer resolves the source control ID again. Indexed callbacks can supply
the exact captured row with query validity instead of retaining its visual/cache.
Shared favorite, hide, category and completion handlers use that selected identity.
Launch still resolves that exact saved ID through the application service and checks
current installation/capability and owner validity before launch or publication.

Details opened from a captured target keep that target as their availability and
mutation owner, so an off-window poster does not disable its modal. Refresh keeps
the same owner; closing or switching the parent page releases it. Query retirement
must make its validity callback false. Existing cursor Home behavior remains intact.
Production Browse now supplies these targets and typed exact return requests.
Tests include out-of-window action identity, captured-modal completion/retirement,
and retirement during launch revalidation.

Reuse shared `PlayniteLibraryGameOptions.Create`, launch revalidation, capability
checks, cancellation, operation generations and existing details behavior. Home entry points still validate their cursor selection once; indexed actions
use the captured exact-game path. A queued admitted
action must never select a game from whatever page happens to be current later.

Capture `WidgetActionEvent.FocusedCollectionItem` when opening Details and retain it
with the parent route/query. Close uses the source's one-shot Enter request if that
logical query is still current. Modal availability must depend on logical query/live
authority, not whether its poster happens to remain in a native container or cache.
Fresh modal scope, Play/Install initial focus, completion changes, description and
tab lifetimes remain existing functionality to preserve.

## Artwork ownership

Do not eagerly project all 10,000 games into the current handle registry: its limits
derive from a 192-item retained window and projection evicts older handles. Capture
game/role/revision authority for indexed artwork callbacks instead, sharing the
bounded byte cache without relying on the old cursor's pin set. Preserve same-game
background-to-cover fallback and reject unknown or malformed handles.

Captured artwork work is separate from catalog publication, with bounded provider
concurrency and cancellation. Browse does not register all captured images in the
legacy cursor artwork registry.

## Native presentation

Browse maps to the indexed GridView contract with the existing 150-DIP minimum
column width, 225 estimated extent and seven-column limit. Keep navigation/filter
controls, count badge, shortcuts, background surface and cover-fit Tile appearance.
Home is one exact horizontal sequence, assembled through HeroRailPolicy.Project
from saved/manual/provider/unavailable rows with saved-ID deduplication. It does not
need grouped vertical virtualization.

The new frontend still needs modal/context-menu/background/focus-presentation and
full theme integration. Indexed poster rows alone do not establish feature parity.

## Validation anchors

- PackageRuntimeTests: catalog/order/count bounds, filters, frozen Home ordering,
  partial-refresh rejection and artwork lifetimes.
- PlayniteLibraryTests: independent Home/Browse state, modal/back, favorite-owner
  refresh, semantic resets, stale query/authority barriers and launch revalidation.
- PlayniteLibraryDetailsTests: resume, opening scope, stale results and shared options.
- Public indexed author helper: demanded ranges, captured row/parent actions,
  release/query replacement, delayed artwork and explicit stale input contexts.

Replace inline-tree-only assertions with meaningful logical-range checks; do not
remove behavior coverage merely because WinUI now realizes item controls lazily.
