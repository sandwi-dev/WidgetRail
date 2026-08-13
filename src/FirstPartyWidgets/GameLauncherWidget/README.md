# Game Launcher

Game Launcher is the controller-first reference view over the trusted installed
application library. Its maintained managed source can be exported as an ordinary
Community repository that consumes only the packaged public SDK; the currently
bundled entry remains unchanged until the generic host cutover. It can search,
filter, organize, and launch only identities
resolved through the app-library capability; saved display rows never grant launch
authority.

Create a self-contained Community candidate from a built `gbar` distribution:

```powershell
pwsh -NoProfile -File .\src\FirstPartyWidgets\GameLauncherWidget\Export-CommunityReference.ps1 `
  -Gbar .\artifacts\gbar\gbar.exe `
  -Output C:\temp\GameLauncherCommunity
```

The generated project contains no checkout `ProjectReference` or friend access.
It restores the exact locally packaged `GameBarAlternative.WidgetSdk`, declares
only app-library read plus optional running/launch capabilities, and packages as
`org.gbar.community.reference.game-launcher` without changing the installed built-in entry.

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

The Library also exposes one controller-reachable collection strip. **All
installed** is always present; non-empty **Continue**, **Favorites**, **Manual**,
and source collections with proven matching rows appear with bounded counts.
An observation-only healthy source with no current matching game remains in the
source-health summary but is not advertised as an empty collection. Selection is a
single mutually exclusive query over exact SavedIds or sanitized source
attribution and never creates launch authority. Proven source choices remain
stable while paging or selecting another collection; only an authoritative
unfiltered refresh can retire one. Hidden games remain on the separate Hidden
route so Restore stays explicit.

Library availability stays explicit: offline and permission-denied library
failures are not rendered as an empty library; degraded and unavailable sources
retain safe last-good games; and each unavailable, stale, disabled, or busy tile
names its reason visibly and through accessibility. Such tiles remain reachable
in controller navigation but Play stays disabled until a fresh current provider
item grants exact launch capability. Retry replaces only the current failed read.
Last-good browsing does not itself grant Play: activation always resolves the exact
SavedId again. A local current Installed+Launch result may therefore launch while
an unrelated catalog refresh is offline, while stale or missing exact evidence
fails closed. Recovery preserves the selected collection, tile focus, and
deduplicated Recent order.

An existing provider may also identify an owned game that is not installed.
Those rows remain controller-navigable and say **Owned** rather than **Offline**.
The details projection names **Install from source** only when the current typed
capability set includes `Install`; signed-out, offline, unsupported, and stale
rows instead retain their exact non-launching reason. The current SDK exposes no
content-install command, so this is truthful availability guidance rather than a
synthetic installer: Play and broker launch admission remain disabled.

On a current Library tile, View opens a bounded details route projected by
`GameLauncherDetailsPresentation`. It displays the full normalized title,
source, availability, primary action, favorite, grouping, and category state,
plus version, last played, playtime, and active operation only when current
normalized metadata supplies a safely renderable value; an SDK-valid timestamp
outside the platform calendar range is omitted. It retains no second provider snapshot or
launch token. A reuses the existing exact-SavedId launch path; X and Y reuse the
existing organization policy. Collection LB/RB shortcuts do not leak into this
nested route. B restores the exact originating collection, tile, cursor page,
anchor, and focus. If that identity disappears, the route remains display-only
and every authority-bearing action fails closed.

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

The Library search uses the host-owned bounded TextEntry. Only committed text
changes the query; cancel retains the prior committed value. Clearing search
retains the selected All installed, Continue, Favorites, Manual, or source
collection and restores that collection's unsearched intersection. Details and
the game action sheet suppress collection shortcuts and Back returns to the
exact originating result; the host retains focus on the search entry when its
modal is canceled.

Source collections are admitted only after a current game row proves the source.
That bounded, non-authorizing display catalog is persisted with organization
state, so sources discovered on later pages remain selectable after a worker
restart. An authoritative unfiltered refresh retires absent sources, and
health-only observations never create an empty collection.

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

Play reports only **Pending**, **Request accepted**, **Launcher started**,
**Running**, **Ended**, or **Failed** for the exact focused SavedId. A newer Play
request supersedes cancellation-ignoring older evidence before it can update the
tile or Recent history. Failed launch returns focus to the same tile for retry;
overlay close is requested only after launcher-started-or-stronger evidence.

`GameLauncherLaunchPersistenceCoordinator` owns the one launch generation and
accepted-observation Recent transition. If an accepted predecessor finishes a
cancellation-ignoring private-state write after a newer Play is admitted, it
uses the existing CAS state owner to restore the exact current Recent projection
before the queued launch runs. Rejected or inactive admissions never reserve a
generation; the widget remains the sole lifecycle, private-state, and view owner.

At the documented 420×340-DIP minimum, the compact Library semantic branch keeps
the truthful status, source summary, the exact host-owned Search TextEntry,
collection tabs, focused game with its Play action, and controller hints
reachable. Expanded-only hero duplication, filters, and footer buttons return at
standard and wide sizes; the same actions remain available through the focused
tile and controller shortcuts. Physical clipping remains a live-renderer check,
not a semantic-fixture claim.
