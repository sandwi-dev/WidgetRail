# Game Launcher reference

Status: bundled package 0.6.0 implements the installed-only controller-first
library and uses the
generic AppContainer worker, normalized app-library broker, shared trusted
provider cache, lazy artwork registry, and exact launch authority.

Game Launcher is the complete-library companion to the curated Games & Apps
tray. Both consume the same normalized installed-game records, opaque SavedIds,
and fresh launch resolution. The widget does not discover stores, retain raw
provider identity, infer games from titles, or cache a complete library.

## Collection and controller behavior

- **Search installed games** opens one host-owned text-entry modal. Keyboard or
  controller input remains inside the host; the widget receives only one
  normalized committed value of at most 96 characters. A commits, B cancels
  without changing the query, X/backspace edits, Clear removes the query, and
  focus returns to the search control after the modal closes.
- Favorites, recent-only, source, and the closed A–Z/Z–A/source sorts are provider queries,
  not filters over the retained widget window. Favorite filtering sends at
  most 128 opaque SavedIds; the broker resolves them against exact current
  authority-scoped identities and an empty match remains an empty result.
- The initial and adjacent requests contain at most 64 records. The shared
  `WidgetCursorResource` appends or prepends complete provider pages, retains at
  most 192 provider rows for this widget, remembers at most 256 opaque traversal
  cursors, and serializes only that retained cursor window. Recent and manually
  added rows are composed in separate fixed sections and never enter provider
  page, cursor, or viewport-anchor accounting.
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
- The responsive grid authors five maximum columns with a 980 by 700 preferred
  surface and 420 by 340 minimum. Native layout chooses the actual compact,
  standard, or wide column count at the active client size and scale.
- **Library sources** lists every normalized source relevant to the current
  provider revision as Healthy, Degraded, Unavailable, or Refreshing. A partial
  source failure keeps healthy and last-good usable games visible and leaves
  the existing Refresh action in charge of recovery. The displayed source ID,
  label, revision, and safe status carry no adapter-control or launch authority.

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

Y hides the focused current game by its exact opaque SavedId. **Hidden** opens a
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

X toggles the focused current game as a favorite and Y hides it. LB starts an explicit variant
selection and a second LB on another current tile creates the group; repeating
the same pair removes the second tile from that group. RB marks a member of an
existing group as preferred. Recent-first order takes precedence while enabled;
otherwise favorites sort first, and preferred members sort first within the
remaining current window, with visible and accessible labels.
Preference never redirects a different tile's launch or merges titles: every
tile continues to resolve and launch its own exact SavedId. A disabled retained
row preserves organization while its source is missing, and reappearance of the
same SavedId restores the choice. A replacement SavedId is independent.

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
cancellation-ignoring lifecycle completion. The
installed generic-worker fixture covers the bundled manifest/catalog route,
10,000-row broker source, adjacent grid focus, artwork handle, and exact launch.
