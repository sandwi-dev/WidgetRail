# Playnite indexed adoption boundary

2026-09-27 source review. The installed native widget is unchanged; this is the
implementation direction for its WinUI adoption, not a completed conversion.

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
The existing widget still uses its cursor UI; production indexed adoption is next.

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

Reuse shared `PlayniteLibraryGameOptions.Create`, launch revalidation, capability
checks, cancellation, operation generations and existing details behavior. Current
handlers resolve SourceElementId through retained cursor windows; extract an
exact-game action path for captured indexed row actions instead. A queued admitted
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

Current `ResolveArtworkAsync` holds the catalog gate over network I/O. Separate
immutable catalog publication from bounded artwork work so range/query admission
does not wait for image downloads. Preserve cancellation and cache synchronization.

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
