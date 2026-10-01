# Playnite Library development notes

[User guide and setup](README.md)

Playnite Library is a controller-first, full-trust Community sample backed by a
user-installed Playnite Bridge instance. The package owns its bounded localhost
client, protected credential prompt, presentation, and organization state. It
does not copy a product app-library provider into the package.

## Navigation and library behavior

Home and Library are peer navigation destinations. Library selects Browse; Home
returns to the installed-game rail without resetting Browse's query. A on either
destination requests entry into the destination's results-only
focus group. The host restores the remembered game when still eligible, otherwise
the first available game. Header and filter buttons are outside these groups.
Requests wait for loading to finish, remain stable across renders, and retire on
other actions or when the widget stops being interactive. Other nested pages keep
ordinary Back behavior; B at Home or Library returns to the host tray. Y refreshes.
X opens the focused game's options. Menu opens page-level Categories, Hidden games, and
Playnite connection, including when a game is focused. These
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
Pressing A opens details with an Install action for uninstalled games. Installation
and uninstallation are delegated to Playnite; refresh after the launcher finishes.
Home lists installed games only.

Home and Browse now publish separate frozen indexed queries. Home preserves the
provider's favorite/recent/name/ID order and inserts its bounded manual and title
match rows before the provider rows. Referenced unavailable saved entries appear
last, only after complete query membership is known. Browse displays its exact
logical query count. Neither page builds a full poster tree: the native frontend
requests bounded random-access ranges and owns realization, scrolling and focus.

Actions and artwork capture the exact item and query owner. Same-membership content
updates preserve logical focus identity; a semantic query or membership/order change
publishes a new query generation. Home refreshes once on activation and return from
another route. Unchanged membership preserves its native position. Visible-to-
interactive transitions do not refresh again. Browse retains its query on reopening.
After five minutes hidden, the widget application unloads and its in-memory queries
and navigation reset; saved settings and the external Playnite process remain.

The SDK leases bound rendered-row lifetimes independently of the application catalog.
A range request does not issue another Bridge page request or accumulate cursor
history. Provider capture, fixed-row resolution, persistence and authority publication
are coordinated before the replacement query is visible. Failed same-query refreshes
retain the last complete query; retired/superseded captures cannot publish.

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
hidden state, categories, completion status, installation/uninstallation, achievements,
and activity. It does not expose arbitrary methods or paths, metadata refresh, deletion, evaluation, or
account operations.

## Data and authority

Playnite game GUIDs are the exact item, action, and mutation authority. Library
queries traverse deterministic 64-item Bridge pages up to 10,000 games, then
apply the current search, source, collection and sort locally. The indexed
frontend requests bounded presentation ranges from that frozen membership. Installed and owned-but-not-installed games remain distinct.
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

Home and Browse own independent query publications and native collections. Browse
filtering cannot replace Home's membership or fixed rows. Provider identity and
mutation authority remain shared and current across both presentation routes.

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
| Home and Browse `WidgetIndexedCollection` instances | Frozen logical membership, bounded demanded rows, exact item action/artwork captures, query generations and content revisions. The native frontend owns realization, scroll position and focus. |
| `WidgetNavigator` | Route stack, route input scopes, and route-return focus. |
| `WidgetOperations` | Named asynchronous operation admission, cancellation, and drain. |
| Playnite application/Bridge authority | Current catalog identities, favorites, hidden/category/completion state, and exact mutation/launch authorization. |
| Private-state and launch-persistence owners | CAS revision, bounded organization persistence, launch-state retention, and their independent generations. |

Remote collections, mutable dictionaries, tasks, cancellation tokens, provider
clients, and resource/navigator state never move into the render model. The model
contains only immutable local projections needed to produce a coherent view.


## Navigation and game details

Home (`Library` internally) and Library (`Browse` internally) are root destinations.
They retain separate indexed collections and native remembered item focus. RS click requests
one-shot entry into the single-field search group; analog scrolling remains host-owned.
A poster opens a `WidgetModal` instead of launching. The modal retains the game's
opaque identity and displays current provider metadata, rechecking current membership
before enabling Play. Launch still uses the existing fresh provider revalidation.
B dismisses the modal; background page geometry and scroll are unchanged. Deactivation
retains the selected game, completed detail data, tab, and opening identity. It cancels
active reads, fences late results with a new generation, clears busy/loading flags,
and cancels uninstall confirmation. Activation retries interrupted reads, never
installation or browser operations. Unloading still discards this in-memory state.
A widget-instance prefix plus monotonically increasing opening identity supplies
the modal ID (and therefore its input scope and scroll-container ID). Loading, refresh,
tab changes, and hide/reopen retain that ID; another explicit opening gets a new one. This uses the existing
SDK modal contract without adding focus-reset APIs. The host/Bridge/SDK must support protocol 55 to display details.
Opening details resolves the full game record separately from compact catalog pages.
HTML descriptions and notes become bounded plain text with paragraph/line breaks and
list markers preserved; they are never rendered as HTML. Description text uses
max-lines: 128 and up to 64 real paragraph nodes instead of 240-character chunks.
The original 4096-character normalized-text limit remains.
Home and Library use the same navigation header and frame; Library filters/results occupy
a separate panel below it. Both fill available height with 24 logical pixels of vertical
padding. The themed count badge follows Clear, and RS Search is shown in its footer.
The modal uses tile artwork beside source and vertically stacked controls, with a
full-width navigation rail beneath. `WidgetModal.HeaderActions` replaces the default
Close button with X/Y/B controller hints; the ordinary B dismissal shortcut remains.
The X hint container owns the context menu, so its anchor stays in the header.
Source and actual completion status share a row; confirmed mutations update the
header without reloading details. Full-game status takes precedence over catalog
state, and a confirmed local mutation wins over a read already in flight until refresh. Its X menu shares the poster's
action definition and current organization authority; categories are available there
instead of a separate section.

Achievements (`GET /api/games/{id}/achievements`) and activity
(`GET /api/games/{id}/activity`) load lazily and are retained only while that modal stays
open. Refresh invalidates those results. Requests are bound to the exact game and modal
generation; explicit dismissal or switching games retires them. Deactivation retains
completed results and activation retries interrupted reads. An HTTP 200 with
`installed:false` means no saved plugin data for this game, not an empty collection or
proof that the companion extension is absent. These reads do not trigger plugin sync.

Optional responses are limited to 2 MiB, with at most 4,000 achievements or 10,000 sessions.
Details render 20 optional entries at a time to bound the view tree. Locked secret
achievement names and descriptions are omitted from visible and accessibility text until
revealed. Session dates/durations, achievement unlock dates, rarity, and points display
when supplied. No achievement images are exposed by this Bridge API.

Install/uninstall commands use fixed routes and freshly resolve the exact game first.
Uninstall requires in-modal confirmation, with B canceling it. An acknowledgment means
the request was passed to Playnite, not that installation completed. Metadata links allow
only HTTP(S), are revalidated against freshly fetched game data, and open in the user's
browser. User guide links describe the optional companion extensions.

The completion control uses the SDK's native Select dropdown. Available statuses
load independently of game metadata and are cached for the open details instance.
Each option action contains the details generation and a hash of its exact status
name; dispatch resolves against the current list, so stale options cannot target a
new game. Mutations set the selected name directly. Empty/failed lists disable the
control with feedback; interrupted reads retry on resume. The list reserves one of
the protocol's 128 slots for an unset or unlisted current value; larger provider
lists show an explicit error rather than silently hiding statuses.

Home opts its FocusPresentationSurface into `RetainLastPresentation` (protocol 56),
matching the native focused-background policy. Both resolve retained source IDs
against the current view and item keys; cursor overlap keeps the source, while
window eviction or filtering it out uses the authored defaults. No worker-side
focus cache, cursor refresh, or background input admission is added.

## Section motion

Home places the horizontal game rail at the bottom of its available content area,
with the retained focused-game summary immediately above it, aligned left. The
focus-presentation surface declares this through `justify: end`, `align: start`, and
a 14-DIP gap; the rail content keeps its full width and does not grow a spacer.
Home and Library destination buttons share a 128-DIP preferred width with a
104-DIP minimum for narrower overlays. These layout styles preserve the existing
presentation ownership, logical focus IDs, and section transition group.

Home and Library opt into the shared navigation transition group
`playnite-library.destinations`. The header stays in place while its selected
destination and lower content animate together. Cursor changes keep the section
key; details use the host's ordinary modal entrance/exit motion.
Overview, Achievements, and Activity also share a transition group inside the
details dialog. Only their lower content moves; the poster and action controls
remain stationary. Data refreshes preserve the selected tab's transition key.


## Native presentation contract

Home and Library require their indexed collection source even when empty or
loading. The page publishes the total count, navigation and background owner;
SDK-acquired ranges publish exact game posters, focus summaries and artwork.
There is no eager cursor-window renderer or separate warm-saved-game renderer.
Warm Home uses the same indexed query with noninteractive saved-only rows.
Provider cursor resources and captured action authority remain independent.

Use explicit responsive grids for bounded wrapping groups (badges,
connection actions and details source/completion). Native track sizing replaces
ignored flex-basis, flex-shrink and flex-wrap style declarations.
Controller footer hints instead use left-aligned content-sized rows with24DIP
gaps, so their positions are not redistributed into equal-width columns.

The Playnite test suite can export synthetic layout evidence when
`WRAIL_PLAYNITE_LAYOUT_OUTPUT` is set. Indexed pages emit `.indexed.json` files with
separate parent snapshots, bounded acquired ranges and their resolved styles.
`Test-WinUiPlaynitePresentation.ps1 -FixturePath <path> -OutputDirectory <fresh>`
checks native row and focused-summary geometry. The existing standalone Details
geometry fixture remains `.renderer.json`. These fixtures do not use Playnite,
credentials, launch actions or system settings, and are not performance benchmarks.

Game details use a bounded Auto/Star Grid: artwork and controls remain fixed above
the lower section scroll. The tabs are Description (initial), Information,
Achievements, Activity and Links. Description also contains saved notes. The
section tabs share the scroll owner with their content so directional fallback
can read sections with no action controls. The SDK modal's default whole-content
scroll is disabled explicitly. Opening identities and scroll IDs still survive
background/resume and refresh, and optional providers remain on-demand.
