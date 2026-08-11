# Game Launcher reference

Status: bundled package 0.2.0 implements the installed-only controller-first
library and uses the
generic AppContainer worker, normalized app-library broker, shared trusted
provider cache, lazy artwork registry, and exact launch authority.

Game Launcher is the complete-library companion to the curated Games & Apps
tray. Both consume the same normalized installed-game records, opaque SavedIds,
and fresh launch resolution. The widget does not discover stores, retain raw
provider identity, infer games from titles, or cache a complete library.

## Collection and controller behavior

- The initial and adjacent requests contain at most 64 records. The shared
  `WidgetCursorResource` appends or prepends complete pages, retains at most 192
  rows for this widget, remembers at most 256 opaque traversal cursors, and
  serializes only the retained responsive-grid window.
- A 10,000-item library takes 157 bounded pages. Crossing a boundary requests
  focus on the entering keyed tile and retains the authored viewport anchor.
  Previous, Next, Refresh, automatic near-edge pagination, retry, and every tile
  remain reachable through controller actions.
- Cursors remain query/revision/direction bound in the broker. Repeated cursors,
  immediate or multi-hop loops within the finite traversal evidence, stale
  generations, malformed pages, cancellation, and traversal beyond the explicit
  bound fail closed while preserving the last good window.
- The responsive grid authors five maximum columns with a 980 by 700 preferred
  surface and 420 by 340 minimum. Native layout chooses the actual compact,
  standard, or wide column count at the active client size and scale.

## Artwork, state, and launch authority

Rows carry a generation-bound opaque artwork handle. Listing does not load PNG
bytes; the native host requests artwork lazily through the private trusted
registry and keeps the semantic Play fallback when artwork is absent or stale.

Private schema v2 contains at most 96 sanitized SavedId, display-name, and source
rows. At most 32 distinct SavedIds may participate in favorites or explicit
variant groups; there are at most 16 groups and four members per group. It
contains no AppId, path, command, AUMID, Steam identity, image bytes, or provider
key. On worker recreation these rows appear immediately as disabled
**Checking…** tiles. They become actionable only after a current provider page
resolves them.

X toggles the focused current game as a favorite. LB starts an explicit variant
selection and a second LB on another current tile creates the group; repeating
the same pair removes the second tile from that group. RB marks a member of an
existing group as preferred. Favorites sort first and preferred members sort
first within the remaining current window, with visible and accessible labels.
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
records cannot launch. Confirmed success requests the existing
`CloseOnConfirmedSuccess` overlay behavior.

## Bounds and failure states

The widget distinguishes non-focusable loading, healthy empty, unavailable,
permission-denied, retained-window partial failure, and retry states. An
adjacent source failure retains prior rows and shows a warning. Lifecycle
deactivation cancels and drains cursor/private-state work; cancellation-ignoring
late pages cannot republish after reset.

Focused managed coverage includes 2,000- and 10,000-item forward/reverse
traversal, bounded rows/cursors/snapshot nodes, display-only warm state, fresh
SavedId revalidation, unavailable launch rejection, retained-window errors,
lazy artwork semantics, explicit favorite/group/preference mutations, CAS
conflicts and failures, restart, full source disappearance/reappearance,
same-identity refresh, identity replacement, incompatible reset, and
cancellation-ignoring lifecycle completion. The
installed generic-worker fixture covers the bundled manifest/catalog route,
10,000-row broker source, adjacent grid focus, artwork handle, and exact launch.
