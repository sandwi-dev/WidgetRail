# Pinned presentation-session authority

This is the managed host authority for authored pinned layouts and the host's
`host.full-widget` projection. It uses the existing controller-input bridge
contract, protocol-63 indexed continuation, and separate version-1 pinned action
and artwork requests. The view schema, runtime-v2 handshake and bridge-v1
handshake are unchanged. A pinned window and its placement remain host
responsibilities; these APIs do not create one.

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
actions, sliders, declared shortcuts and Select options. Context actions and
text commits use `pinned-action-v1` through the bridge and worker. Preserve
captured scope, source, phase, focus and the actual menu opener (Menu/X/Y);
a container menu can be invoked while another control in the same scope has
focus. Text commits use A or a pointer commit without a controller button.
The ordinary frame/authority input APIs reject pinned contexts.

One internal SDK resolver defines the context/text contract for the session,
bridge and worker. The session retains genuine displayed-frame and selection
authority. The bridge compares the origin and current binding under its operation
gate, then supplies the current sequence to the already-running worker. The
worker checks that exact sequence, layout, scope, source and declaration before
admitting the captured action to its existing bounded serial queue. There is no
generic Action fallback, including for a frozen worker that does not recognize
`pinned-action-v1`. Such workers must be rebuilt to support these commits.

These distinct request names carry an explicit version value of 1. Unknown
versions and extra properties are rejected. No widget SDK authoring change is
needed: existing text-entry and context-action declarations remain the source
of authority, with their existing disabled/busy and text-length constraints.

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

## Scoped artwork

`ResolvePinnedArtworkAsync(selection, displayedProjection, handle)` admits only
handles declared in the displayed and current selected root, including focus
backgrounds and focus-presentation fragments. The `resolve-pinned-artwork-v1`
bridge request carries instance/runtime/presentation, layout, scope, original
snapshot sequence and a unique demand ID. The bridge validates this projection
before and after the existing artwork provider resolves. Worker artwork remains
opaque handles and encoded bytes; the host never accepts widget file paths.
Legacy sizing-only layouts inherit the main artwork projection without gaining
ordinary action authority.

Art requests share the existing bounded artwork queue and timeout. An unrelated
main modal or snapshot update does not discard a still-declared pinned image.
Selection removal, scope/handle removal, replacement and Background retire the
local demand immediately, even before its bridge admission reply. Late bytes
cannot satisfy a replacement demand. A sent provider request is not retracted;
it remains bounded by the existing bridge/worker timeout and its result is
ignored after local retirement. Local retirement cancels unsent transport-lane
waits before disposing their timeout, so abandoned demands cannot accumulate.

## Host integration and remaining boundaries

The host-generated compact-media projection belongs to the media service; it is
not an authored or full-widget projection handled by these APIs. Window placement,
media surfaces and the WinUI presenter are separate shell integration work.

Do not build a legacy pinned cursor action engine. Current widget collection
migration uses indexed/discovered contracts; indexed context actions and
`ContinuePinnedDiscoveredCollectionAsync` already preserve projection authority.
Older sizing-only layouts can render inherited content and artwork but do not
acquire ordinary pinned action authority.

Validation includes scripted session tests for provenance, selection lifetimes,
scopes, modal updates, cancellation ordering, indexed authority and delayed
artwork; direct bridge/worker rejection tests; and a session-to-real-worker
round trip for context actions, text commits, full-widget parent input beneath a
modal and pinned-only artwork. These tests do not establish pinned-window
rendering or physical input acceptance; those belong to shell integration.
