# Playnite Library reference

Status: the ordinary full-trust Community sample under
`samples/PlayniteLibraryWidget` implements the installed-only controller-first
library. Its package-owned application uses the generic Community application
runtime, normalized public SDK contracts, lazy artwork, and exact launch
authority without host or package-identity special cases.

The proposed multi-source console-home expansion and launcher-specific theme
system are specified separately in
[Playnite Library product and engineering requirements](../archive/playnite-library-requirements.md).

Playnite Library is the complete-library Community sample backed by the user's
local Playnite Bridge. It is the companion to the curated Games & Apps
tray. Both consume the same normalized installed-game records, opaque SavedIds,
and fresh launch resolution. The widget does not discover stores, retain raw
provider identity, infer games from titles, or cache a complete library.

Each current row is projected from one versioned immutable app-library
presentation: item/source, availability, role-keyed artwork, optional attributed
metadata, closed capabilities, and optional operation state. Playnite Library does
not retain legacy scalar aliases. It uses Tile artwork for rows and Hero artwork
for the hero only when those roles are present, and enables launch only when the
current availability and explicit Launch capability agree. Unknown or malformed
presentation versions fail closed before they can enter widget state.
The SDK validates each focused value independently, then composes only their
relationships. Playnite Library also explicitly requires the freshly resolved
availability state to be `Installed`; unavailable, stale-source, and retained
last-good rows cannot reach launch even if malformed input claims otherwise.

## Collection and controller behavior

- **Search installed games** opens one host-owned text-entry modal. Keyboard or
  controller input remains inside the host; the widget receives only one
  normalized committed value of at most 96 characters. A commits, B cancels
  without changing the query, X/backspace edits, Clear removes the query, and
  focus returns to the exact `playnite-library.search` control after the modal
  closes. The installed generic-worker host route also exposes that stable
  search action through UI Automation; Escape/B cancel emits no commit or
  provider query and keeps the prior committed query.
- Favorites, recent-only, source, and the closed A–Z/Z–A/source sorts are provider queries,
  not filters over the retained widget window. Favorite filtering sends at
  most 128 opaque SavedIds; the broker resolves them against exact current
  authority-scoped identities and an empty match remains an empty result.
- The initial and adjacent requests contain at most 64 records. The shared
  `WidgetCursorResource` appends or prepends complete provider pages, retains at
  most 192 provider rows for this widget, remembers at most 256 opaque traversal
  cursors, and serializes only that retained cursor window. Recent and manually
  added rows are composed as bounded fixed prefixes in the visible rail and
  never enter provider page, cursor, or viewport-anchor accounting.
- A 10,000-item library takes 157 bounded pages. Crossing a boundary requests
  focus on the entering keyed tile and retains the authored viewport anchor.
  While focus is inside a multi-page results scroll, LB requests the available
  previous page and RB requests the available next page. An unavailable or busy
  direction has no shortcut, and focus outside that scroll keeps its existing
  bumper meaning. Single-page tiles retain LB/RB variant grouping and preference.
  Previous, Next, Refresh, automatic near-edge pagination, retry, and every tile
  remain reachable through controller actions.
- Cursors remain query/revision/direction bound in the broker. Committing or
  clearing a query resets the cursor generation and anchor; a late result from
  a replaced query cannot publish. Repeated cursors,
  immediate or multi-hop loops within the finite traversal evidence, stale
  generations, malformed pages, cancellation, and traversal beyond the explicit
  bound fail closed while preserving the last good window.
- The Library and details routes fill the monitor's host-admitted safe area,
  with a 1600 by 1200 DIP preferred fallback and 420 by 340 minimum. The
  built-in `hero-rail` presentation keeps one bounded selected-game hero above
  one horizontal cover rail. Left/Right updates only the selected hero;
  it never dispatches launch or loses the cursor anchor. Add games, Add running
  app, and Hidden retain one bounded responsive vertical grid.
- **Library sources** lists every normalized source relevant to the current
  provider revision as Healthy, Degraded, Unavailable, or Refreshing. A partial
  source failure keeps healthy and last-good usable games visible and leaves
  the existing Refresh action in charge of recovery. The displayed source ID,
  label, revision, and safe status carry no adapter-control or launch authority.

Press **View** on a current Library tile to open its bounded game-details route.
The route shows the full title, normalized source, availability, launch state,
favorite state, and variant-group/preference state for that exact opaque SavedId.
**A** launches through the existing fresh SavedId resolution, **X** toggles the
favorite, and **Y** hides the game. On a single-page collection **LB/RB** retain
their existing two-game selection/prefer meaning; on a paged collection they
remain reserved for Library traversal and are not reassigned inside details.
The details action says **Choose another variant** for the first selection and
returns to the originating Library tile with visible status so a distinct game
can be chosen. On the second game's details it says exactly **Group with selected
game** or **Remove from variant group**, and the result remains visible on the
details page. A stale first selection or selecting the same identity fails closed
with explicit feedback. **B** returns to the
originating tile and retained collection offset. A removed or replaced identity
keeps only its sanitized display projection, becomes visibly unavailable, and
cannot launch or mutate a different same-title row.

The hero reuses the focused row's generation-bound opaque artwork handle and a
semantic Play fallback when artwork is absent. It shows only the normalized
title, exact source, current availability/launch state, favorite state,
preferred-variant state, and bounded group count already present in managed
state. It introduces no network request, raw path, provider metadata, or launch
authority. A and the View/X/Y/LB/RB shortcuts continue to originate from the
focused tile's exact SavedId.

## Artwork, state, and launch authority

Rows carry a generation-bound opaque artwork handle. Listing does not load PNG
bytes or probe Steam artwork directories/files; the native host requests artwork
lazily through the private trusted registry and keeps the semantic Play fallback
when artwork is absent or stale.
Installed Steam rows use the same handle when the trusted provider can bind the
exact current registration to a bounded local Steam library-cache PNG or JPEG.
Only exact demand opens the trusted cache, chooses an allowlisted candidate,
captures object evidence, reads bytes, and normalizes pixels; widgets never
receive the cache path, Steam AppId, file identity, or source bytes. Replacing
or removing the asset invalidates the stale demand and rotates the affected
handle on the next refresh without changing an unaffected neighbor, launch
authority, focus, favorites, groups, or recent order.
The provider retains only the bounded current Steam locator set; terminal
shutdown drains catalog scans, running observation, and all artwork permits
before it disposes any source or clears committed catalog state.
Candidate locator sets remain staged until the matching Steam source generation
commits, so cancellation or a newer refresh cannot retire current row artwork.

Installed Microsoft/Xbox package games enter the same normalized library only
when supported Windows package registration plus bounded
`MicrosoftGame.config` evidence identifies the exact registered application as
a game. Package names, locations, AUMIDs, configuration bytes, and account data
remain provider-private. Exact AppsFolder duplicates collapse by the same stable
launch identity, while distinct registered variants remain separate rows. A
failed package refresh may retain a disabled last-good display row, but launch
requires a fresh matching package generation and AUMID.

Epic installed-game discovery is separately opt-in under **Settings > Game
sources**. It reads only bounded local installed manifests from the fixed
provider-owned ProgramData location and performs no Epic login or network
request. The source reports disabled, unavailable, or degraded status
explicitly. Raw manifest fields, install paths, catalog IDs, and launch URIs
never enter widget state; activation requires a fresh exact manifest and
executable match before the host constructs its constrained Epic launcher URI.

GOG installed-game discovery has its own opt-in on that page. Disabled mode
performs no GOG registration or file I/O. Enabled mode reads only the fixed
machine-wide GOG game registry views, requires the corresponding bounded local
`goggame-<product-id>.info` record, and reports
disabled, unavailable, degraded, or healthy status independently. Product IDs,
registry keys, install paths, and info bytes remain trusted host state. This is
bounded best-effort installed evidence, not an official GOG integration API or
an exhaustive catalog. Because GOG does not document a supported external
launch contract for this evidence, GOG rows expose **Play unavailable** and no
Play request crosses the widget/broker/provider boundary.

Private schema v5 contains at most 128 sanitized SavedId, display-name, and source
rows; display names are capped at 96 characters so the worst valid state remains
below 64 KiB. At most 32 distinct SavedIds may participate in favorites or explicit
variant groups; there are at most 16 groups and four members per group. It
contains no AppId, path, command, AUMID, Steam identity, image bytes, or provider
key. On worker recreation these rows appear immediately as disabled
**Checking…** tiles. They become actionable only after a current provider page
resolves them.

The same schema retains at most 32 opaque SavedIds in newest-accepted order.
Only an exact current **Launcher started**, **Running**, or **Ended** observation
may move an identity to the front. Acknowledgement-only, failed, stale,
replaced, or canceled launches do not change history. **Recent: First**
composes the bounded current recent slice before the provider window and
**Recent: Only** asks the trusted catalog for the exact current SavedIds;
neither path authorizes launch.
Missing identities stay as non-authorizing display rows, replacement SavedIds
remain independent, and **Clear recent** preserves favorites and variant
groups.

**Add games** opens a bounded nested route over the same trusted installed-app
catalog and labels current Game, Application, and Unknown classifications.
Search and source/sort controls remain provider-backed. Adding or removing a
row stores only its opaque SavedId and sanitized display projection, up to 32
manual entries; no path, command, AUMID, store/provider identifier, or artwork
bytes enter private state. B or the visible Back action restores the Library
route and prior focus. On every refresh or worker recreation, manual SavedIds
are resolved again into a separate section of at most 32 rows. Automatic Game
registrations are labeled **Included** and cannot acquire redundant manual
membership. Missing entries remain disabled, replacement SavedIds are
independent, and activation still
requires the same fresh exact-SavedId resolution and short-lived AppId as an
automatic game.

**Add running app** is a separate optional-consent route. It performs one
bounded observation only when opened or refreshed and shows only visible
programs that map one-to-one to a current normalized registration. Duplicate
windows collapse to one row; the host visits at most 256 top-level windows and
returns at most 64 candidates. Automatic games and identities already retained
as recent, hidden, or manual rows are disabled rather than duplicated. Adding
an Application rechecks the short-lived observation revision and exact current
SavedId, then applies the complete app-library item validator before reusing the
same 32-row manual CAS policy. A malformed confirmation cannot change any
neighbor or private state. Process/window/path identity never enters the worker,
private state, or launch path, and a denied or unavailable observation does not
disable the normal complete-library route.

Y opens one scoped game-action sheet for the focused current game's exact opaque
SavedId. Its **Hide game** action adds that identity to **Hidden**, which opens a
bounded nested route containing at most 32 exclusions, each with an explicit
**Restore** action. Current rows may show refreshed display and artwork, while a
missing row keeps only its sanitized display projection; neither kind can launch
from the Hidden route. Restore removes only that SavedId. Refresh, restart,
reclassification, and a same-title replacement do not transfer the exclusion,
and CAS replay preserves unrelated favorites, groups, recent order, and manual
membership. Invalid schema-v4 or older development state resets atomically rather
than partially migrating exclusions.

When **Recent: First** is selected, at most 32 retained recent display rows are
resolved and composed ahead of the manual and provider sections in exact saved
order. This includes a game launched from a later catalog page after a cold
restart. Exact SavedIds are deduplicated across all three sections, while the
ordinary provider page remains independently traversable. **Recent: Only** uses
the same fixed non-authorizing slice. Search, source, favorites, and display sort
filter the fixed sections without expanding provider queries or caching the
complete library.

X toggles the focused current game as a favorite. Y opens the controller-complete
action sheet with the current favorite toggle, hide action, variant step,
preferred-variant state, exact category membership, and source refresh. It does
not duplicate View's full details route or expose a content operation. LB starts an explicit variant
selection and a second LB on another current tile creates the group; repeating
the same pair removes the second tile from that group. RB marks a member of an
existing group as preferred. Recent-first order takes precedence while enabled;
otherwise favorites sort first, and preferred members sort first within the
remaining current window, with visible and accessible labels.
Preference never redirects a different tile's launch or merges titles: every
tile continues to resolve and launch its own exact SavedId. A disabled retained
row preserves organization while its source is missing, and reappearance of the
same SavedId restores the choice. A replacement SavedId is independent.

**Edit title** in the Y action sheet opens a scoped controller TextEntry for the
exact focused SavedId. The normalized custom title is limited to 96 characters
and changes presentation and search only; provider title, source, availability,
artwork, capability, launch identity, and every other variant remain unchanged.
**Reset title** removes only that exact override and immediately restores the
current provider title. Override-only search matches are resolved through the
same bounded exact-SavedId capability before display or launch.
The number of retained overrides is governed by the shared encoded 64-KiB
private-state boundary, not a prototype title-count cap. Validation work remains
finite, while search, resolution, and each current presentation stay within the
existing service and render-window bounds. Invalid title entries reset the title
slice only; favorites, hidden/recent rows, variants, and categories remain intact.

**Categories** provides All Games plus practical local named collections. Names
normalize whitespace and Unicode compatibility form, are limited to 32
characters, and must be unique ignoring case. The shared 64-KiB private-state
budget—not the old four-member/eight-total prototype cap—is the ordinary
admission boundary; focused coverage retains 32 categories and 256 compact
exact Playnite GUID memberships. Create and open remain in one bounded
controller route; deleting a category removes only that category and never hides,
removes, launches, or merges a game. The Y action sheet adds or removes the exact
focused game.

From All Games or a category, LT and RT cycle once through All Games and every
category in saved order. The same exact SavedId keeps focus when it exists in
the destination; otherwise focus moves to the first current member or the
truthful empty-category action. Search entry, category management, details, and
the game action sheet retain their own input scope and never receive this
shortcut.

Category views reuse the bounded app-library query/cursor owner and retain only
sanitized last-good display rows for temporarily missing members. Those rows are
disabled and cannot launch; every Play still resolves the exact current SavedId
through the host capability. Structurally invalid, duplicate, over-bound, or
aggregate-over-64-KiB category fields reset together while unrelated favorites,
variants, recent order, manual membership, hidden games, and experience remain.

Organization mutations use one bounded two-attempt compare-and-swap store. On a
conflict, only the requested favorite/group/preference delta is reapplied to the
newer valid state, preserving unrelated favorite order and groups. A rejected or
failed write leaves the committed state unchanged. The visible **Reset
organization** action clears favorites and groups without deleting external
provider data. Unsupported or invalid pre-release launcher schemas reset
atomically as a whole before current authoritative reconciliation; stale rows
cannot partially survive or authorize launch.

Activation of a current tile resolves its exact SavedId again, verifies that
the keyed row is still in the current retained window, and sends only the newly
issued AppId to the broker. Missing, stale, unavailable, or permission-denied
records cannot launch.

The tile becomes **Pending** while exact revalidation and launch admission run.
The trusted adapter then returns a bounded, sanitized evidence result: **Request
accepted**, **Launcher started**, **Running**, or **Ended**. A failure is shown
as **Failed**. Windows shortcut, packaged-app, and Steam URI launchers currently
prove only **Launcher started**; they never infer Running or Ended from process
names, windows, paths, or store acknowledgement. Adapters without stronger
evidence use **Request accepted**. The overlay closes only after evidence at
least as strong as Launcher started, never for acknowledgement alone.

Lifecycle results are keyed to the exact current SavedId and launch generation.
A late completion after deactivation or replacement cannot change a newer row,
and launching one explicit variant cannot overwrite another variant's state.
No PID, HWND, command, store identity, or path enters widget state, snapshots,
or logs.

## Bounds and failure states

The widget distinguishes non-focusable loading, healthy empty, unavailable,
permission-denied, retained-window partial failure, and retry states. An
adjacent source failure retains prior rows and shows a warning. Lifecycle
deactivation cancels and drains cursor/private-state work; cancellation-ignoring
late pages cannot republish after reset.

Focused managed coverage includes normalized search/source/favorite/sort
criteria, empty favorite matches, query replacement with a cancellation-
ignoring old completion, and 2,000- and 10,000-item forward/reverse
traversal, bounded rows/cursors/snapshot nodes, display-only warm state, fresh
SavedId revalidation, unavailable launch rejection, retained-window errors,
lazy artwork semantics, explicit favorite/group/preference mutations, CAS
conflicts and failures, bounded hide/restore, non-authorizing missing exclusions,
replacement independence, restart, full source disappearance/reappearance,
same-identity refresh, identity replacement, incompatible reset, and
cancellation-ignoring lifecycle completion. It also covers exact same-title
details routing, View/A/B and unchanged X/Y/LB/RB semantics, unavailable
identity refusal, exact Back focus restoration, long labels, and deterministic
bounded detail layout. Exact first/second variant selection, existing-group
removal, same/stale identity refusal, committed hide closure, and failed/busy
organization feedback are also covered. The
installed Community fixture covers the sample manifest/catalog route,
10,000-row broker source, adjacent grid focus, artwork handle, and exact launch.
