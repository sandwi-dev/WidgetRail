# Native collection lifecycle

Implementation begins with an internal logical geometry model in
`CollectionLayoutState` and the protocol-v59 declaration below. The declaration is
validated end to end on the task branch; renderer realization is still pending.
Do not release or adopt these helpers in shipping widgets until that path is
connected and the integration gates pass.

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

Remaining delivery includes the descriptor-to-realized-subtree lifecycle,
Taffy item measurement, provider-window
extent integration, navigation/UIA realization, scheduler and resource protection,
and adoption in Playnite grids and variable-height music rows. Existing generic
Scroll behavior remains the production path until that integration passes.

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
