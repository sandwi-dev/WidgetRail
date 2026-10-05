# WinUI feature contracts and preservation inventory

> Implementation record: checkpoint dates, temporary candidates, former source
> paths and pending-work statements below describe the stage recorded, not current
> release status. See [current platform status](current-platform-status.md) and
> [WinUI authoring](../reference/winui-authoring.md) for the implemented contract.

This is a source-backed inventory for the WinUI frontend, not a claim that the
features already work there. Preserve user-visible behavior and authoring
capability; replace native renderer mechanisms rather than porting them as a
second layout/focus engine. Cursor and lazy collection declarations may be
redesigned. Other semantic changes require an explicit decision and evidence.

## Ownership and identity

WinUI owns measurement, layout, realized controls, ordinary focus traversal,
scrolling primitives, visual state and composition. WidgetRail owns validated
worker declarations, action authority, route/input policy, logical item identity,
presentation selection and provider demand. Both full-trust and sandboxed workers
use the same declarative frontend; neither supplies executable XAML or controls.

Keep these identities separate:

- Runtime/presentation authority and widget instance: replacement retires all old
  actions, pending requests and remembered presentation.
- Page/input scope: selects the active interaction domain, and namespaces focus
  and scroll state. An inactive retained page is not a deleted page.
- Collection/reset generation and stable item key: window movement is not reset;
  a recycled control is not a new item; reuse of an element ID with another item
  key is not continuity.
- Element and presentation-slot identity: identifies the declaration and owning
  slot, not a XAML object reference.
- Navigation selection versus focus: selecting Library is different from focusing
  its tab, and restoring focus must not emit its activation action.

Sources: `src/WidgetSdk/WidgetIds.cs`, `src/WidgetSdk/Elements.cs`,
`src/WidgetSdk/Widget.cs`, `src/WidgetProtocol/ViewModels.cs`,
`docs/reference/identity-and-input.md`.

## Presentation slots: background and focused fragment

| Existing contract | WinUI implementation responsibility | Semantic trap / required proof |
| --- | --- | --- |
| Background paints one optional bounded image behind one content subtree; only content owns layout/input semantics | A shared surface component owns background image and foreground content layers; layout remains WinUI-owned | Background must not change focusability or create an independent action/accessibility surface. Test transparent/theme backgrounds and missing artwork. |
| `UseFocusedDescendantArtwork` consumes only the exact focused source within the nearest owning background surface | Logical source coordinator chooses artwork; XAML image loading/rendering consumes the decision | Do not search arbitrary descendants of a focused container or let a nested background source leak to its ancestor. |
| Background retention defaults **true**; focus-fragment retention defaults **false** | Retention policy is explicit per slot with these defaults during migration | A single global retention flag silently changes behavior. Playnite explicitly opts its summary into retention. |
| Focus fragment has a default and one selected source, placed before ordinary content; fragment is non-interactive | A slot displays the selected declaration through the trusted frontend, with actions and focus unavailable | `IsHitTestVisible=false` alone does not prove keyboard/UIA inertness. Validate the fragment contract and automation behavior too. |
| Every selection resolves current source data | Coordinator stores source identity, not old node pointers, copied text or stale image declarations | Updating selected game's title/artwork must update the retained presentation without refocusing. |
| Nested surfaces are ownership boundaries for their respective kind | Slot ownership is part of the logical model, independent of XAML ancestry | Removing/reparenting source or slot must revalidate ownership. One background boundary must not accidentally suppress a separate summary slot. |
| Lost membership/declaration/visible responsive branch invalidates selection; no resurrection | Clear remembered source when it no longer belongs to the logical model | Recycling/unrealizing a XAML container is **not** membership loss. Eviction/filtering is. Returning an evicted item cannot restore selection until it is focused again. |
| Tray/modal/unrelated focus retains valid selection when enabled | Actual focus event updates policy once; modal activation does not synthesize parent focus changes | Open game B after game A: never paint A first. Dismiss/reopen/modal data updates must not reset the parent. |
| Scale/theme/render-target changes do not change semantic selection | Re-render the existing selection with new WinUI resources | No reselecting first item during DPI or theme change. Runtime replacement must clear selection even if all IDs match. |

Primary source: `src/WidgetSdk/BackgroundSurface.cs`,
`src/WidgetSdk/FocusPresentation.cs`, focus modifiers in
`src/WidgetSdk/Elements.cs`, `src/OverlayHost/FocusSurfaceSelection.h`,
`docs/developers/presentation-retention.md`.

The existing native resolver keys remembered entries by authority, instance,
scope, surface kind and surface ID. It validates source identity using scope,
node kind and collection-item key ancestry. Those distinctions explain behavior;
the WinUI design need not preserve the native string concatenation or tree walk.

## Focus, scopes, actions and modal pages

| Contract to preserve | Product policy above WinUI | Required end-to-end proof |
| --- | --- | --- |
| `InitialFocusId` is initial/fallback focus, not a command on every update | Resolve once at entry or when current target becomes invalid | Loading/title/artwork updates do not pull focus back to header. |
| `RememberChildFocus` is explicit entry-group memory; nested ancestors remember independently | Store logical child identities; request realization before assigning actual focus | Leave/return to nested group; recycled or disabled target falls back correctly; no extra activation. |
| `FocusGroupEntryRequest` is one-shot per runtime/instance and positive RequestId | Deduplicate requests; latest accepted view and active scope validate the target | Duplicate snapshot does not jump again; runtime restart permits its own request sequence. |
| Active input scope gates main-widget input | Input coordinator controls routed shortcuts/actions independently of XAML text `InputScope` | Parent shortcuts cannot execute through modal; stale events from former scope are rejected. |
| Explicit focus neighbors and stable focus persistence are authorable | Translate supported relationships to WinUI focus APIs after validating current logical target | No dependence on recycled element pointers; offscreen target realization works. |
| D-pad/left stick move focus, right stick moves viewport and settles focus | Controller routing selects the WinUI navigation/scroll operation; one owner per gesture | Held navigation, reversal, nested scrollers and loading edges do not jump into unrelated header controls. |
| Context/Select popup and keyboard have higher input precedence than widget modal | Maintain topmost interaction context and restore its owner on dismissal | B dismisses popup before modal; Menu/X/Y retain authored mapping; captured stale anchor cannot dispatch action. |
| Modal belongs to parent page and is bounded inside widget, including corner-aligned widgets | Widget-local dialog layer; parent size/scroll remain independent; actual dialog focus enters its scope | Open details in Home and deep Library at multiple scales; parent cursor/background never jumps. |
| Modal initial focus follows its opening identity; ordinary open-modal updates preserve current focus | Use stable logical modal lifetime, separate from data refresh; do not infer every snapshot is a new opening | Fresh game/opening focuses Play/Install; changing status or loading description keeps current control. |
| Dismissal restores valid parent focus/scroll; exit pixels have no action authority | Detach modal input immediately, optionally animate its presentation | Rapid close/reopen, action during exit and removed parent game do not invoke stale actions. |
| Pinned layouts omit modal and have independent presentation/input demands | Project parent view for pinned surface rather than sharing interactive modal tree | Modal open on main surface neither crashes nor blocks pinned projection. |

Sources: `src/WidgetSdk/Widget.cs` (`WithModal`, controller dispatch),
`src/WidgetSdk/WidgetModal.cs`, `src/WidgetSdk/Elements.cs`,
`src/WidgetProtocol/ViewFocusTargetLookup.cs`, `src/WidgetProtocol/ViewModels.cs`,
`docs/developers/navigation.md`, `src/WidgetSdk/WidgetNavigator.cs`.

Current `WithModal` allows one modal and rejects embedded-media views and nested
modals. Preserve all currently supported content; do not silently promise media
inside dialogs just because XAML can declare a media element. Its layering and
input need separate proof. Closing overlay versus closing modal is route policy,
not an automatic WinUI window lifecycle side effect.

## Collections and paging: intentional redesign boundary

The legacy native collection contract submits a bounded retained item tree.
WinUI should instead consume keyed logical data and trusted templates, realize
controls on demand, and issue asynchronous provider requests through workers.
Avoid treating a `StackPanel` with every item allocated as virtualization.

| Behavior | Required new contract / proof |
| --- | --- |
| Offset pages and continuous cursor windows are distinct resources | Support both; optional total count and opaque before/after cursors. Do not convert unknown totals into fabricated indexes. |
| Adjacent loading preserves traversal; refresh/reset is a different operation | Explicit query/reset generation and window revision. Reject stale pages after query/session replacement. |
| Stable keys and provider ordering | Overlap deduplication and ordered membership updates; no local page-only sorting or index-based identity. |
| Prepend/eviction preserves visible content | Keyed visible anchor plus relative offset. Protect currently visible/focused items where necessary; bound retained data independently of control realization. |
| Existing rows remain interactive during loading | Loading edge is state, not a focusable placeholder; direction reversal operates immediately on existing items. |
| Boundary hold resumes without replaying input backlog | Coalesce gesture demand; page arrival extends range without dispatching queued repeated moves. |
| Regular grid width can change column count | Maintain logical target and sensible viewport across resize/DPI/overlay-scale changes; no fixed geometry cache from old column count. |
| Source metadata survives unrealization | Presentation contributions and action validity are available from logical item data; never require a realized poster to retrieve its background. |
| Accessibility includes logical offscreen items | Use framework virtualization automation support or a verified peer bridge; test realization invoked by automation. |
| Artwork demand follows displayed surfaces | Include retained background, summaries, pinned surfaces and transitioning content; cancellation isn't a missing-image result. |

Sources: `src/WidgetSdk/Collections.cs`, `src/WidgetSdk/WidgetCursorResource.cs`,
`src/WidgetSdk/WidgetPagedResource.cs`, `src/WidgetSdk/WidgetCollectionItems.cs`,
`src/WidgetProtocol/CollectionLayout.cs`, `src/WidgetProtocol/ViewModels.cs`,
`docs/reference/collections.md`.

Choose the WinUI collection control/layout based on production interaction
requirements. A collection needs a constrained viewport; wrapping an already
scrolling ListView/GridView in an outer ScrollViewer can defeat virtualization.
Test heterogeneous/nested content separately rather than force every scroll into
a regular item-grid model.

## Motion, themes and general controls

- Section `GroupId` coordinates content, layout and selection. Only section-key
  changes start motion; normal snapshots and scrolling do not. Order supplies
  direction. Incoming content alone owns actions; an outgoing visual never does.
- Global section/dialog/focus presets, speed and reduced-motion policy remain
  authoritative. Preserve synchronized headers and stationary navigation labels.
- Focus/pressed scale is presentation transform, not repeated layout sizing.
  Clip/scroll reveal must account for scaled appearance; the Menu affordance
  participates in the same transform. Interrupt from displayed state.
- Theme-aware depth, text, controller glyphs, options, selected/disabled/busy
  states and high contrast need explicit template/resource mapping. An identity
  update must not reconstruct controls and replay all state animations.
- Select and context menus share visual language but retain distinct selection
  and invocation semantics; they need anchored placement, edge clamping,
  sections/icons, scrolling, and correct active-scope dismissal.
- TextEntry needs value/commit semantics, sensitive-input handling, accessibility,
  keyboard overlay behavior and no accidental second action on focus loss.
  Slider values use shared numeric rules and declared interaction modes.

Sources: `src/WidgetSdk/WidgetTransitions.cs`,
`src/WidgetProtocol/WidgetTransition.cs`,
`src/OverlayHost/WidgetAnimationPolicy.h`, `src/WidgetSdk/Elements.cs`,
`src/WidgetSdk/ControllerGlyph.cs`, `src/WidgetSdk/SliderMath.cs`,
`docs/developers/navigation.md`.

These are behavior requirements, not a mandate to reproduce private native
animation curves or renderer caches. Real WinUI visuals must prove interruption,
clipping, media coexistence and frame pacing; timer-driven screenshot equality
is insufficient.

## Remaining visible feature families that cannot be omitted

`ViewNodeKind` in `src/WidgetProtocol/ViewModels.cs` currently includes Stack,
Row, Scroll, Text, Button, Progress, Slider, Spacer, Image, Icon,
LoadingIndicator, ActionSurface, Grid, TextEntry, MediaViewport,
BackgroundSurface, FocusPresentationSurface, Select, WindowPreview,
ControllerGlyph and ModalLayer. Each needs implemented behavior or an explicit
migration decision, not an unknown-node placeholder in the shipped frontend.

Additional cross-cutting contracts:

- Responsive visibility and compact/expanded NavigationShell must share one
  logical content identity. See `src/WidgetSdk/NavigationShell.cs` and `Grid.cs`.
- Host-managed embedded media retains session/document authority, geometry,
  input, accessibility and teardown. Omitting session closes; retaining session
  without viewport parks it. See `ViewSnapshot.EmbeddedMediaSession`.
- Window previews expose opaque IDs and fallback state, not raw HWNDs/pixels to
  widgets; unavailable/protected/minimized sources are valid states. See
  `src/WidgetSdk/WindowPreview.cs`.
- Independent pinned projections and sizing/selection lifecycle must work while
  the main overlay is hidden. See `PinnedPresentationLayout`,
  `src/WidgetSdk/PinnedLayoutHandle.cs` and `Widget.cs`.
- Worker action admission is not action completion. Preserve serialized
  execution, cancellation, optimistic state and failures without letting XAML
  command execution introduce a second incompatible queue. See
  `WidgetControllerQueue.cs`, `WidgetOptimisticCommand.cs`, `WidgetOperations.cs`.

## First pure managed implementation

Implement a **logical presentation-source coordinator** before tying selection
to WinUI controls. It can be exercised without an HWND and consumed by both
background and focus-fragment components.

Input is an immutable logical presentation revision containing authority,
instance, responsive mode, declared slots and eligible contributions with stable
source identity, owning slot, logical membership and current content. Focus is a
logical target identity or no target. WinUI realization is deliberately absent.
Output is one selection per slot: current source or authored default, referencing
current revision content. State remembers identities only.

Required unit cases: independent retention defaults; nested ownership; source
refresh; same ID/different key; filter/eviction and return without resurrection;
unrealization with unchanged membership; responsive removal; authority/session
replacement; simultaneous background/summary selection; modal/tray focus; removed
slot; theme/scale unchanged semantic selection. Validate duplicate IDs and invalid
ownership before publishing a revision, not halfway through selection updates.

Then the frontend integration proof must open actual Playnite Home and Library,
move focus, open details, update details, close it and switch games while tracing
selected logical identities and displayed frames. Pure coordinator tests prove
policy only; they cannot prove XAML z-order, focus, image timing or smoothness.

## Immediate design decisions still requiring resolution

1. Define source contribution metadata in the redesigned collection protocol so
   it is available without realizing or serializing every item's full visual tree.
2. Define modal/page opening identity and route lifetime explicitly; do not make
   identity depend on incidental snapshot sequence or XAML instance creation.
3. Decide which actual WinUI control realizes bidirectional variable-size cursor
   windows, with demonstrable anchor behavior during prepend/eviction.
4. Establish widget-local modal/media/native-preview composition feasibility
   before adopting a global dialog primitive that changes placement or layering.
5. Define one retained view-model update boundary that changes membership,
   actions, presentation contributions and current scope coherently. Do not port
   the old deferred renderer-publication pipeline into that boundary.

Completion requires actual host input-to-present evidence, production widget
content and delayed worker/artwork responses. Passing a renderer fixture or
source-policy unit test is supporting evidence, not complete feature acceptance.

### Current implementation evidence

`src/WidgetUi.State/PresentationSources.cs` now supplies this pure managed
coordinator and immutable logical revision contract. Focused tests in
`tests/WidgetUi.State.Tests/PresentationSourceCoordinatorTests.cs` cover the policy
cases above (15 tests passed on 2026-09-27). This is not yet frontend integration
or proof of WinUI presentation correctness. Snapshot sequencing and admission
remain the frontend/session owner's responsibility; payload values must be
immutable. Reuse admitted revisions for focus-only updates.
