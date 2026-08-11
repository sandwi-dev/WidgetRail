# Game Launcher reference

Status: the installed-only controller-first library is bundled and uses the
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

Private state contains at most 96 sanitized SavedId, display-name, and source
rows. It contains no AppId, path, command, AUMID, Steam identity, image bytes, or
provider key. On worker recreation these rows appear immediately as disabled
**Checking…** tiles. They become actionable only after a current provider page
resolves them.

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
lazy artwork semantics, and cancellation-ignoring lifecycle completion. The
installed generic-worker fixture covers the bundled manifest/catalog route,
10,000-row broker source, adjacent grid focus, artwork handle, and exact launch.
