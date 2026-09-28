# Widget presentation session

`WidgetPresentationSession` is the managed presentation-side facade for the
existing `WidgetBridge` backend. It connects to the bridge's random,
current-user-only named pipe, performs the existing correlated hello handshake,
and uses the shared bridge framing and JSON contract. It does not launch a
bridge, create a server, connect to a widget process, or define an alternate
wire format.

Call `ListWidgetsAsync`, retain the returned `WidgetPresentationTarget`, and use
`EstablishPresentationAsync` to obtain the exact runtime, presentation,
snapshot, and input-scope authority. Open-widget actions and controller input
must carry that authority. Successful invalidations are coalesced into a fresh
snapshot; a failed refresh retains the last accepted frame and publishes a
bounded typed failure.

The facade keeps bounded pending request, artwork, and diagnostic collections.
Disposal asks the existing bridge session to stop, closes the client endpoint,
and completes outstanding work with a terminal transport failure.

## Owned indexed ranges

`AcquireIndexedRangeAsync` returns a `WidgetPresentationIndexedLease`. Its `Range`
contains immutable declarative rows; retain the lease while the frontend retains
those rows, their open popup, or their presentation/artwork contribution. XAML
container recycling does not by itself dispose a semantic lease. The ordinary
`ReadIndexedRangeAsync` remains a data-only read with no action/artwork authority.

Use `lease.AdmitInputAsync(originFrame.Authority, itemKey, button, ...)` for row
activation, shortcuts and context selection. The origin must be the exact frame
admitted by this presentation session; no method substitutes the latest sequence
for a stale caller. The bridge additionally compares retained origin and current
binding before the worker's serial action queue admits the command. Admission is
not execution completion, and cancellation does not retract a sent command.

`lease.ResolveArtworkAsync(itemKey, handle)` authorizes only artwork declared by
that row, including focused background and presentation fragments. Requests have
independent bounded demand IDs and cancellation correlation; slow artwork never
occupies the row's action queue. A missing image returns null.

Leases survive unrelated snapshot revisions and parent modals. Main-view row
input is rejected while its parent scope is inactive; pinned projection scope is
resolved independently. Query/content revision changes, projection removal,
widget/session replacement and widget retirement invalidate the data lease and
release its exact remote owner. Disposal is idempotent and never targets a new
worker incarnation. Capacity includes in-flight acquisitions and releases, not
only successfully returned lease objects. Dispose the session to release all
remaining ownership before bridge shutdown.

The transport admits at most 15 correlated wire requests against the bridge's
16-request limit, leaving headroom for response finalization. Separate bounded
lanes reserve four provider and four provider-cancellation slots at the default
configuration; ordinary controls share the remainder. Lower configured capacity
reduces these reservations. Lane ownership lasts until a correlated response or
terminal transport failure, even if a caller stops waiting. Bulk lease release
uses at most four control slots, and its acknowledgement deadline begins after
write admission. Range and indexed-artwork admission share the same effective
provider bound, so an unsent extra artwork request cannot occupy cancellation
capacity. Session disposal bounds aggregate drain before reaching transport stop.

Before letting an unhandled indexed button return to host navigation, call
`lease.ClaimsInput` with the exact displayed authority and key. This shares the
worker's shortcut resolver and treats unavailable authored shortcuts as owned.
A stale frame/scope/lease throws and must be consumed. Once a declaration claims
input, a null `AdmitInputAsync` result must also be consumed: null can indicate a
worker publication racing IPC, not just an absent binding. Never infer host Back
from a null admission result or retry it against a newer frame.

## Package icons

`ResolvePackageIconAsync(target, assetId)` resolves manifest-inventory SVG through
its existing bridge endpoint. The session sends descriptor generation, package
content digest, source hash and normalized hash, verifies echoed identity, byte
count and SHA-256, then returns a private copy. Paths, URIs and widget SVG are
never accepted. Catalog replacement invalidates both in-flight results and cache
lookups. Cache retention is capped at 128 entries/2 MiB; pending demand at 16 and
active package exchanges at four. Cancellation/timeout never publishes late bytes.
