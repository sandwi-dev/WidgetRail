# Native collection lifecycle

Implementation begins with an internal logical geometry model in
`CollectionLayoutState` and the protocol-v59 declaration below. The declaration is
validated end to end on the task branch. Renderer realization and logical
directional navigation and UIA realization are connected; scheduling and widget
adoption remain. Do not release these helpers until the full integration gates pass.

## Declarative contract

`UI.CollectionList(id, estimatedItemExtent, axis, items)` declares a one-dimensional
list. `UI.CollectionGrid(id, minimumColumnWidth, estimatedItemExtent, maximumColumns,
items)` declares a vertical adaptive grid. Both produce Scroll elements carrying
a typed `CollectionLayout` policy, so existing cursor `Capture().Present()` and
scope/shortcut APIs remain usable. Their direct items must already carry unique
`CollectionItem` keys and be Buttons or ActionSurfaces. The helper supplies a first
anchor for a static collection; the cursor presentation replaces it as appropriate.

Each item is one controller target. Nested focusable content or per-item responsive
visibility requires an ordinary Scroll layout; filtering replaces logical items
explicitly. The contract admits content-sized rows and poster content, rather than
requiring a fixed item height. The host owns overscan and realization budgets.
Collection placement starts at its content padding; collection-level flex
justification does not redistribute rows. Style item contents to align their copy.

Version 59 is required whenever the policy is present. Managed and native validators
reject unsupported kinds, shapes, estimates and column limits. Policy changes use
the existing atomic `PresentationProperty.CollectionLayout` update with layout,
paint, interaction and accessibility impact; they do not add a separate transport.
The existing bounded wire tree and cursor limits still apply.

## Ownership and identities

The provider owns data retrieval and its retained cursor window. The widget owns
bounded declarative item content. The host owns realization, measurement,
placement, focus, accessibility, painting and scheduling. Unrealizing a visual
item must not remove its logical descriptor or evict provider data.

Collection identity includes widget/input-scope/query authority and a reset
generation. Each item has a stable key and a host measurement revision covering
content, resolved style and intrinsic measurement inputs. The measurement context
covers actual item constraints, inherited style, text/display scale and other
shared measurement dependencies. A key alone never authorizes measurement reuse.
Position-dependent style changes must change the affected item's measurement
revision. Either item measurement revision zero or measurement context zero
deliberately opts out of reuse across descriptor replacement.

The internal model accepts a bounded logical window, currently at most 100,000
descriptors. This is an internal safety bound, not a new SDK/provider limit or
permission to increase serialized UI-tree limits. Unknown provider prefixes and
suffixes are not invented as exact item geometry. Their integration remains part
of the host cursor contract.

Keys are bounded to 128 characters and composite host scope identities to 512.
Item extents must be finite and between 1/64 and 1,000,000 DIPs. Collapsed or absent
items must be omitted from the navigable logical window rather than represented
by zero/subnormal extents. This keeps prefix lookup well-defined; it does not
round valid measurements to an integer pixel grid.

## Geometry and updates

Lists use one column; adaptive grids supply the column count resolved from their
actual container constraint. Main-axis item extents exclude the collection gap.
The collection places grid lines using each line's maximum item extent. Signed
window indices preserve partial rows through non-aligned provider eviction or
prepend, including opaque cursors whose relative indices become negative.

Replacing descriptors validates the entire input and builds new state before
publication. Stable matching items retain valid measurements across reordering.
Each replacement changes a generation token. Measurement batches carry that
token, validate fully before mutation, and reject stale, invalid or duplicate
results without partial admission. A separate geometry revision changes after
accepted measurements, so a prepared demand plan can be rejected if its geometry
has become obsolete. This is not a substitute for host action/scope admission.

A prefix-sum index supports logarithmic position lookup and measured line updates.
Snapshot metadata reconciliation and initial index construction are linear in
logical descriptors. The expensive UI preparation will be limited to realized
items when the renderer is connected to this model.

## Demand, navigation and anchors

Viewport demand returns visible and buffered item ranges plus distinct protected
items. A distant focused/modal-parent/accessibility target does not expand the
range to include all intervening items. Demand reports budget exhaustion; it never
silently drops visible items. The scheduler must shrink optional prefetch first
and explicitly handle a visible/protected set exceeding its configured budget.

Logical navigation returns an item or an explicit before/after data requirement,
terminal boundary or missing origin. It does not move focus. The host must realize
and measure a target, validate pending navigation authority, and commit focus
against the admitted geometry. At an incomplete provider edge, same-column grid
navigation requests data rather than changing columns prematurely. A terminal
partial row chooses its nearest available column.

An anchor records authority, stable item key and the item's offset relative to
the viewport. Restoration follows that key through prepend, reorder and column
changes, clamped to the available extent. A reset or missing key yields no
restoration: the host must select its documented surviving-neighbor or explicit
position fallback. It must not silently carry an unrelated item's anchor forward.

## Publication and remaining integration

The model is host-thread confined and owns no actions, visual nodes, images or COM
objects. Prepare a staged copy when necessary, and publish it together with the
matching geometry, focus and paint state. It does not itself implement atomic
scene publication or a background measurement scheduler.

The renderer now uses one estimated extent boundary in the surrounding layout,
and independently measures only realized item subtrees with Taffy. It retains
valid item measurements, fills newly exposed lines after estimate correction,
projects item boxes through the collection's actual clip, and publishes logical
navigation authority separately from rendered rectangles. Unknown offscreen
geometry is never invented for directional search.

UIA exposes lightweight logical providers and VirtualizedItem/ScrollItem patterns.
Realization is an authority-checked reveal, not an invocation or focus transfer.
Providers survive visual eviction but reject recycled item identities and scopes.
The host protects the requested item and preserves controller focus until physical
navigation resumes. A production pinned-host fixture verifies this behavior.

`WidgetCollectionItems<TItem>` reuses bounded immutable item declarations by key
and input equality. Its pure factory must receive every render input, including
selection and availability. Failed captures do not replace the previous cache;
provider eviction drops cached declarations independently of host realization.

Playnite Browse grids and regular YouTube Music rows now use these declarations;
music rows keep a cache per retained section with explicit immutable render inputs.
Grouped Home shelves and empty-state content keep their generic layout.

`DeclarativeRenderer::PrepareCollections` now provides host-thread work slices
bounded by new item measurements and elapsed time. Protected/visible demand precedes
optional buffered items inside each collection. An indivisible item can overrun the
time budget; at least one new item is allowed to make progress. Pending is distinct
from failure. Only measurement caches survive a slice: committed scroll, geometry,
focus presentation and pixels remain unchanged. Source/context proofs revalidate
reuse on changed requests; explicit cancellation and widget retirement release it.
The host calls this before beginning paint and retains the prior admitted scene
until ready. Session completions can opt into a preparation hold: validated
candidates stay outside Snapshot/Presentation, retaining the existing transport
admission lock and old input/UIA authority. Approval re-enters normal admission
validation with exact request/generation. Retry, lifecycle change and retirement
discard obsolete candidates; staged candidates count toward the bounded queue.
Main and pinned snapshot replacements use timer slices and shared render constraints.
Hidden/non-presented snapshots need no visual preparation. Genuine preparation
failure uses the existing synchronous diagnostic path; Pending never becomes a
worker error. Production pinned-owner tests preserve old snapshot/focus/offset
through multiple slices before admitting a replacement. The session tests cover
old authority, coalesced refresh, stale approval, retry and lifecycle cancellation.

Directional offscreen targets use shared `FocusRealizationIntent`: one target,
exact request/scope/sequence/item-key authority, and an unchanged origin until
successful prepared visible geometry. Main and pinned timers perform the work;
new input, scrolling, lifecycle changes and recycled IDs cancel obsolete intents.
Already measured targets keep their ordinary path. Preparation caches retain
bounded intermediate measurements through old-scene repaints and multi-pass reveal.

`PlanPreparedFreeScroll` tentatively applies one sample, prepares newly demanded
items, and restores offset/paint-plan state on Pending/Failed. Later cadence samples
retry from the committed viewport instead of accumulating movement debt. Already
measured buffered demand bypasses preparation. Provider boundary loading remains
distinct from preparation. Renderer tests compare sliced movement with synchronous
measurement/anchor correction and exercise old-scene repaints between slices.

Ready slices can return prepared focus geometry without painting. The host resolves
an incoming one-shot group request against that geometry and prepares its remembered
target before snapshot admission; read-only prediction does not consume request
authority or replay requests already below the runtime high-water mark. Ordinary
render settlement retains the final exact-request/geometry validation.

Main and pinned UIA requests also prepare on timers before applying reveal options
to paint. They preserve original controller focus and reject stale request authority;
pending work exposes neither partial geometry nor speculative accessibility bounds.

Final paint/device transactions still need audit/integration. A newly ready snapshot
or focus target is not proof of successful D2D EndDraw/presentation. Existing focus
settlement remains synchronous after the relevant collection measurements are ready.

Remaining delivery includes host frame-budgeted scheduling and
resource protection, broader provider-window/scale/scroll validation, and adoption
in other suitable widgets. The initial realization path
still rebuilds the small outer layout on collection updates/scroll; localized
preparation and scheduling must follow before declaring the delivery complete.
Existing generic Scroll behavior remains available.

## Verification

`CollectionLayoutStateTests` is registered in CMake and the canonical native
`build.ps1` path (`-CollectionLayoutTestsOnly`). Tests compare index geometry and
viewport demand against a simple eager reference across randomized variable-height
lists/grids and incremental measurements. Targeted cases cover stale batches,
invalid atomic updates, scope/reset/revision invalidation, non-aligned signed
windows, anchors, partial rows, protected targets, bounds and empty viewports.

These model tests do not establish renderer pixel equivalence, controller behavior,
accessibility integration or frame-time improvements. Those remain integration
gates before the collection task is complete.

The connected renderer now also has eager-reference pixel/geometry fixtures for
buttons, wrapped content rows and poster subtrees in vertical/horizontal lists
and adaptive grids at 100%/125% scale. They cover deep focus, prepend, reset,
multiple collections, hot resize/DPI changes, known provider prefixes and failed
frame rollback. Logical navigation tests exercise unmeasured targets and stale
scope/query/order authority. Collection scroll state is staged with the frame and
committed only after successful rendering. These fixtures are not a substitute
for broader widget lifecycle validation.

Production Playnite and YouTube Music fixture exports now feed
`ScrollWorkloadProbe --compare`: eager and realized visible geometry/content pixels
match at 620/980/1400 DIP widths and 100%/125% scale. The comparison retains themed
painting and excludes only scrollbar tracks, whose thumbs intentionally reflect
estimated unmeasured extent. Hidden decoration is not required to retain geometry.
Artwork readiness is awaited before comparison. `--eager` supplies a same-build
timing reference; these synthetic offscreen measurements are not live overlay FPS.
The renderer measures items under a real containing block, shares immutable
measurement dependencies between staged frames, and projects raw coordinates
before pixel snapping. Final snapping normalizes float noise at 1/1024 physical
pixel precision, without changing layout constraints or scroll accumulation.
