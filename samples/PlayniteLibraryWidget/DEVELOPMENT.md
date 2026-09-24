# Playnite Library development notes

[User guide and setup](README.md)

Playnite Library is a controller-first, full-trust Community sample backed by a
user-installed Playnite Bridge instance. The package owns its bounded localhost
client, protected credential prompt, presentation, and organization state. It
does not copy a product app-library provider into the package.

## Navigation and library behavior

Home and Library are focusable header destinations. Library opens Browse; Home
returns to the installed-game rail without resetting Browse's query. A on either
destination and B from Browse request entry into the destination's results-only
focus group. The host restores the remembered game when still eligible, otherwise
the first available game. Header and filter buttons are outside these groups.
Requests wait for loading to finish, remain stable across renders, and retire on
other actions or when the widget stops being interactive. Other nested pages keep
ordinary Back behavior. Y refreshes, Menu opens Categories, Hidden
games, and Playnite connection, and X opens the focused game's options. These
controller hints remain non-focusable. Category rows are single action surfaces.

Package WRSS owns layout and uses shared theme tokens for colors, fonts, borders,
and focus. Home retains focus-driven background artwork and a selected-game
summary. Expanded summaries separate metadata and status badges; compact surfaces
use a shorter summary and smaller posters. Missing-cover posters retain readable
labels. Connection content scrolls below its fixed header when space is limited.
Theme regression tests compile platform defaults, each selected built-in theme,
then widget styles in production cascade order.

Browse includes installed and uninstalled games by default. The Installed toggle
beside Favorites limits Browse to installed games
and combines with Favorites; Clear turns it off. Uninstalled games show "Not installed"
on a visible poster badge and keep their game options, but cannot be launched.
Pressing A shows a toast asking the user to install the game through Playnite and
then refresh. Home lists installed games only.

Home orders the full provider collection by favorites, most recently played,
name, and stable game ID before splitting it into pages. The rail preserves
that order as pages arrive. Saved games outside a partial cursor window are
not presented as unavailable merely because their page has not loaded.
Browse shows the total matching its current query, independently of retained
page size. Adjacent loading preserves focus and uses the shared edge indicator;
providers without a known total display an explicitly labeled loaded count.
Each cursor traversal keeps its initial membership and ordering until refresh;
current game details and favorite authority remain live. A favorite change takes
effect in ordering on refresh, so later pages cannot overlap an earlier ordering.
Home and Browse own separate traversals. Replacing a query retires its old cursors.
Saving a favorite disables activation temporarily while preserving rail focus.
Home and Browse load more games automatically while scrolling or navigating;
there are no LB/RB page-jump shortcuts. Home fetches 16 games per page; Browse fetches 30. Home refreshes
once whenever it becomes active, including reopening the overlay and returning from another
route. A successful refresh starts the rail at the beginning.
Visible-to-interactive transitions do not trigger a second refresh.
Browse reuses its loaded pages, stable scroll container, and position on reopening
while the widget remains loaded. After five minutes hidden, WidgetRail unloads
the widget application to release memory. Reopening then starts a fresh instance;
in-memory pages and navigation reset, while saved settings remain. This does not
close Playnite itself or its Bridge extension.
Refresh and query changes request fresh data explicitly. The retention target is 48 items for Home and 60 for Browse, with a hard maximum
of 192; visible pages remain protected by the shared cursor policy.

## Setup and safety

Install and enable Playnite Bridge in Playnite. Without a saved token, the widget
shows **Set up Playnite Bridge** with a **Set up connection** button. Select it,
paste the token from Playnite Bridge settings, and save. You can also open
**Menu → Playnite connection** at any time. A rejected token shows **Update token**.
The token is stored under the package-scoped Windows Credential Manager target
and is never rendered, logged, or copied into package or private state. The UI
offers explicit replace and remove actions. Missing credentials produce an
actionable setup screen; no environment variable is part of the product flow.

The transport is structurally limited to `localhost:19821`, disables proxy,
redirect, cookie, and decompression behavior, and exposes only the fixed routes
needed for bounded library reads, artwork, launch request admission, favorites,
hidden state, categories, and completion status. It does not expose arbitrary
methods or paths, install/uninstall, metadata refresh, deletion, evaluation, or
account operations.

## Data and authority

Playnite game GUIDs are the exact item, action, and mutation authority. Library
queries traverse deterministic 64-item Bridge pages up to 10,000 games, then
apply the current search, source, collection, sort, and 32-item presentation
window locally. Installed and owned-but-not-installed games remain distinct.
Manual and emulated games are ordinary Playnite records; optional source,
category, completion, metadata, and artwork fields fail closed when absent or
malformed.

Favorites, hidden state, category membership, completion status, and launch
requests are re-resolved against the current exact GUID before mutation. A
successful launch response means only that Playnite Bridge accepted the request;
it is not presented as proof that a process started. Launch admission and observed process startup are separate states.

The application may retain a bounded last-good catalog for presentation when
the Bridge becomes unavailable. Retained entries are marked stale, expose no
launch capability, and cannot authorize mutations. A fresh current observation
is required before any action.

Home and Browse use independent cursor resources, query generations, retained
windows, selection anchors, and pagination. Paging or filtering Browse does not
replace Home's current window or fixed rows, while Home refreshes on returning to it. Provider identity and mutation authority remain
shared and current across both presentation routes.

## Artwork retention

Resolved artwork payloads use a package-owned, least-recently-used content cache
bounded to 128 MiB. That byte budget is an application cache policy, not a
process-memory limit. Cache hits promote recency, oversized payloads may serve
the current request without being retained, and byte eviction never revokes a
still-published artwork handle. Handle registration, lifecycle pinning, and
late-result rejection remain separate authority owners.

## Build and package

From the repository root:

```powershell
pwsh -NoProfile -File samples/PlayniteLibraryWidget/Build-CommunityPackage.ps1 -Configuration Release
```

The script publishes the full-trust application, validates the staged manifest
and payload, rejects product-provider/debug files, and emits the immutable
archive under `artifacts/community-addons/playnite-library/`. It does not install
or launch unless its separate explicit install switch is supplied.

Focused deterministic coverage uses fake Playnite Bridge and credential seams;
it neither reads a real credential nor connects to a live Playnite instance.

## State ownership

The widget owns render-facing local state through one constructor-created
`WidgetModel<PlayniteLibraryRenderState>`. Each render reads one atomic model
snapshot, and equal updates publish neither a model revision nor a widget
invalidation. Related transitions commit together, so query, route-local
selection, modal selection, hero, status, and connection presentation cannot be
observed as a partially updated field cluster.

Provider and persistence authority deliberately remain outside that model:

| Owner | State and responsibility |
| --- | --- |
| `WidgetModel<PlayniteLibraryRenderState>` | Immutable query/collection projection, fixed rows and source observations, details/action-sheet/title-editor selections, route-local category/running/hero/focus state, local status/busy/launching presentation, and Playnite connection presentation. |
| Home and Browse `WidgetCursorResource` instances | Independent remote page lifecycles, queries, cursors, retained item windows, anchors, stale-generation rejection, cancellation, and provider errors. |
| `WidgetNavigator` | Route stack, route input scopes, and route-return focus. |
| `WidgetOperations` | Named asynchronous operation admission, cancellation, and drain. |
| Playnite application/Bridge authority | Current catalog identities, favorites, hidden/category/completion state, and exact mutation/launch authorization. |
| Private-state and launch-persistence owners | CAS revision, bounded organization persistence, launch-state retention, and their independent generations. |

Remote collections, mutable dictionaries, tasks, cancellation tokens, provider
clients, and resource/navigator state never move into the render model. The model
contains only immutable local projections needed to produce a coherent view.
