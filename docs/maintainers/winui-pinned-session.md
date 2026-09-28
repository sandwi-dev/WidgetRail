# Pinned presentation-session authority

This is the managed host foundation for authored pinned layouts and the host's
`host.full-widget` projection. It uses the existing controller-input bridge
contract and protocol-63 indexed continuation. It introduces no wire version,
SDK declaration, or permission changes. A pinned window and its placement remain
host responsibilities; this API does not create one.

## Projection, selection, and displayed origin

Three identities must remain separate:

- `WidgetPresentationFrame` is the genuine immutable frame published by this
  session. Its authority identifies the widget incarnation and original sequence.
- `WidgetPinnedProjection` selects one layout from that frame. Its `Snapshot`
  and `RenderStyles` are rendering views, not a new ordinary input frame.
- `WidgetPinnedSelection` is one host selection lifetime. Deselecting, switching,
  or reselecting the same layout retires the preceding lifetime.

Resolve a projection from the exact published frame with
`ResolvePinnedProjection(frame, layoutId)`, select it with
`SelectPinnedLayoutAsync(projection)`, then render the projection. Retain that
projection alongside the rendered controls. On a new publication, resolve and
render a fresh projection while retaining the current selection; do not select
again for every refresh. Selection retains minimal authority, not an old UI tree.

A layout can change its own active input scope without losing its selection.
Input from the preceding displayed scope is rejected. Removing a layout and
later reintroducing the same ID creates a new lifetime: neither old input nor an
old selection revives. Widget instance, runtime, presentation, or session
replacement also retires the authority. Fabricated or copied frames are rejected.

Authored layouts use their own root, active scope, initial focus and surface.
Their style keys arrive as `layoutId/nodeId`; the projection exposes `nodeId`.
The full-widget projection excludes a main-page modal. Legacy layouts with no
root inherit the main view for rendering, but `SupportsOrdinaryInput` is false;
the existing ordinary pinned controller route cannot authorize them. Indexed
leases have their own independently validated projection route.

## Host selection and authored demand

The host's effective projection is distinct from package-authored handle demand.
Selecting an authored layout sends its exact layout ID and selection state to
the worker. Selecting `host.full-widget` instead sends a null-ID deselection:
this retires prior authored demand without inventing a package handle.

`DemandAcknowledged` reports the worker's response to that notification. It is
not permission and does not define host selection. In particular, full-widget
selection can be current even when no authored demand existed to acknowledge.
A failed or cancelled exchange never promotes the attempted selection to active.

A host teardown uses `ClearPinnedSelectionAsync(selection, currentFrame)`.
It revokes local input immediately, then sends the existing null-ID revoke using
the genuine current frame. An old selection object cannot clear a newer one.
After a cancelled selection, `GetPinnedSelection(currentFrame)` retrieves the
unconfirmed attempt for a fresh teardown intent. Cancellation cannot retract a
command that already reached the worker; do not infer its remote effect.

Selection changes and pinned input are serialized through bridge admission.
Caller cancellation ends waiting, but the dispatch gate remains held until the
underlying response or terminal failure. This prevents a later selection or
teardown overtaking uncertain earlier input. There is no automatic input retry,
sequence rebasing, or replay after a stale rejection.

## Input routes

`SendPinnedControllerInputAsync(selection, displayedProjection, input)` requires:

- `PinnedSurface` context and the exact layout ID;
- the displayed projection's own scope and original frame sequence;
- the original focused element and a still-equivalent current binding;
- a current selection for this session and widget incarnation.

The bridge independently validates the retained origin, runtime generation,
selected layout and expected action. Unbound input returns false without using a
raw-controller override. A stale or invalid input throws and must be consumed,
not retried as ordinary widget input or host navigation.

`SendPinnedSelectOptionAsync` commits the captured Select option through the
bridge's expected-option route. `SendPinnedActionAsync` maps supported pointer
or accessibility commits to the same exact-layout controller routes: primary
actions, sliders, declared shortcuts and Select options. Set the appropriate
`ControllerInputOrigin`; preserve captured scope, source, phase and focus.
The ordinary frame/authority input APIs reject pinned contexts.

Successful responses describe bridge handling/admission, not asynchronous widget
operation completion. Observe subsequent publications for the result.

## Indexed collections and lifecycle

Acquire indexed leases with `PinnedLayoutId` in their existing range request and
retain the genuine originating frame. Pinned row input requires the matching
current selection, including the authority-only input overload. Data can be
prepared before selection, but cannot grant interaction authority. Indexed
context actions use the existing exact lease route and are supported.

Switching or clearing selection retires that layout's leases and pending range
demands. Dispose retained leases normally; release remains idempotent. Main
modal updates do not change the pinned projection's independent active scope.
`ContinuePinnedDiscoveredCollectionAsync` carries the exact layout ID and
selection into protocol-63 continuation registration, including collections that
are absent from the main root. It does not repurpose legacy cursor actions.

Keep the widget lifecycle Visible or Interactive while a pinned surface is
visible, even if the main overlay hides. The shell owns focus, interaction and
placement policy. Setting the widget Background revokes active pinned selection;
returning it to Visible does not restore selection implicitly. Terminal failure,
widget retirement and disposal revoke authority as well.

## Bounded follow-ups before full pinned UI parity

These are intentionally unavailable rather than routed through ordinary Action:

- Ordinary pinned context-menu actions and text-entry commits need a bridge
  action contract with exact layout, scope, origin and binding authority.
  `SendPinnedActionAsync` rejects these with `pinned_action_unsupported`.
- Legacy pinned cursor pagination needs that exact-layout action contract too.
  The old native host's generic Action route must not be copied into WinUI.
  Indexed context actions and discovered continuation are already supported.
- Ordinary artwork declared only inside an authored pinned root needs scoped
  resolution; the existing generic artwork facade checks the main root.
  Indexed row artwork retains its existing exact lease route.
- The host-generated compact-media projection belongs to the media service;
  it is not an authored or full-widget projection handled by these APIs.

The foundation is covered by presentation-session scripted bridge tests for
projection provenance, independent scopes, selection lifetimes, stale bindings,
modal updates, cancellation ordering, indexed authority and explicit unsupported
routes. These tests do not establish pinned-window rendering or physical input
acceptance; those belong to the WinUI shell integration.
