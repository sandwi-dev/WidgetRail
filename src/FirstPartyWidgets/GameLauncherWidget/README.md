# Game Launcher

Game Launcher is the controller-first first-party view over the trusted installed
application library. It can search, filter, organize, and launch only identities
resolved through the app-library capability; saved display rows never grant launch
authority.

Every provider row enters the widget as one immutable normalized presentation
containing its sanitized item/source identity, availability, role-keyed artwork,
optional attributed metadata, closed capabilities, and optional operation. The
widget keeps no parallel scalar model: unavailable retained rows are explicit
non-authorizing presentations, and launch requires a freshly resolved row whose
availability and Launch capability agree.

The surface requests 980 by 700 DIPs and supports a 420 by 340 DIP minimum. The
Library route opens as the built-in `hero-rail` experience: a bounded selected-game
hero above one horizontal cover rail. Left/Right focus movement changes only the
hero selection; it does not launch or mutate organization. The header,
library-source status, search entry, and filter strip remain fixed chrome, and the
rail retains the existing stable collection anchor and cursor paging. Add games,
Add running app, and Hidden keep their single bounded vertical viewport.

Below 960 DIPs wide or 540 DIPs high, the same semantic tree uses a compact title
and bounded source-health summary. Larger surfaces show the expanded heading and
per-source detail. Both branches retain the same route, collection, action, and
focus identities; reopening or resizing does not create a second responsive page
tree or reset the collection offset.

The Library's controller-reachable **Experience** action selects Hero Rail,
Cover Wall, Carousel, or Compact Grid. The selected built-in profile is stored
with the widget's bounded private state and an unavailable value recovers only
that setting to Hero Rail; favorites, variants, recent history, manual entries,
and exclusions remain intact. Game Launcher partitions the one current view
into details, game-rail, collection, source, operation, and controller-hint
slots. The partition moves the existing elements without creating action IDs,
SavedIds, focus IDs, provider authority, or another retained library.

These slot markers are a private first-party host seam, not a public widget or
pack authoring API. Until the ordinary OverlayHost render path adopts that seam,
the declarative fallback remains usable and selection persists, but production
does not yet apply the four native slot layouts.

The hero reuses only the selected row's opaque trusted artwork handle and otherwise
shows a semantic fallback. It names the exact source, availability, favorite,
preferred-variant, grouping, and launch state. A, View, X, Y, LB, and RB continue
to route from the focused tile's exact SavedId; the hero is presentation only.

On a current Library tile, View opens a bounded details route projected by
`GameLauncherDetailsPresentation`. It displays the full normalized title and
source plus availability, launch, favorite, grouping, and preferred-variant
state without retaining another provider snapshot or launch token. A reuses the
existing exact-SavedId launch path; X/Y and the context-valid LB/RB actions reuse
the existing organization policy. B restores the originating tile and collection
offset. If that exact identity disappears, the route remains display-only and all
authority-bearing actions fail closed.

Y opens one nested `UI.ActionSheet` for the exact focused game. The sheet shows
the current favorite, hide, variant, preferred-variant, and source-refresh
actions, routes them through the same organization and cursor owners, and uses B
to return to the originating Library or details scope. It contains no launch or
duplicate details action and adds no provider or content authority.

The same sheet projects exact category membership. A separate bounded route owns
create, rename, browse, and delete under the shared 64-KiB organization budget;
focused coverage retains 32 categories and 256 compact memberships. LT/RT cycles
All Games and saved category order while keeping the same exact member focused
when possible. Category policy/projection is immutable and separate from the
widget lifecycle adapter. Missing members retain sanitized display only; Play
always requires fresh exact provider resolution.

The game action sheet also opens one scoped **Edit title** TextEntry. Its
96-character-bounded exact-SavedId override changes presentation/search only,
retains the provider title separately for recovery, and can be removed with
**Reset title**. Override capacity is governed by the shared encoded 64-KiB
private-state boundary rather than a small library-count cap; generous validation
ceilings bound malformed input work. Search resolves at most one service-bounded
batch of override-only matches by exact SavedId; launch authority remains the
current provider item and never consumes the custom title.

Variant organization remains the existing explicit two-game policy. Details
labels the first step **Choose another variant**, returns to the Library with the
first identity named in status, and labels a valid second step **Group with
selected game** or **Remove from variant group**. The details status reports the
committed result or failure; a same or stale identity cannot be paired.

Every top control preserves a protocol-valid collection while its replacement
query is pending. The viewport anchor always names a currently rendered game,
including when the first saved display row is hidden. View, X, Y, LB, and RB are
advertised only while the focused game can accept their exact action. At a
non-terminal collection edge, Down admits one cursor continuation and restores
focus to an entering game row; page and footer controls remain ordinary explicit
navigation targets after the final game row.
