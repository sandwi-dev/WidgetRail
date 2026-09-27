# WidgetUi.State

Pure managed product state for the WinUI frontend. This project does not reference
WinUI, calculate layout, request artwork, move focus, scroll collections, dispatch
actions or own provider requests.

`PresentationSourceCoordinator<TContent>` selects logical background and focus
fragment contributions together. `TContent` is a frontend-owned immutable value
(for example a discriminated record of artwork and fragment declarations), never
a live XAML control. A missing artwork source can be represented by an explicit
empty-artwork value; payloads themselves cannot be null.

For each admitted update:

1. Build a `PresentationRevision<TContent>` from current logical membership.
   Include unrealized items; omit hidden/filtered/evicted sources. Determine each
   contribution's nearest owning slot before creating the revision.
2. Pass the revision and actual logical focus identity to `Resolve`. Modal/tray
   focus may have no eligible contribution. The coordinator decides retention
   independently for each surface.
3. Apply the complete returned selection set to frontend view models on the UI
   dispatcher before presenting the corresponding update.

Reuse a revision for subsequent focus changes. Construct a new revision when
membership, contribution content or defaults change. The coordinator stores only
identities, so retained content always comes from the revision supplied to that
call. Removed source/slot identities do not resurrect when later readmitted.

`PresentationAuthority` isolates runtime, widget instance and presentation (main,
pinned, etc.). Use one coordinator per independently active presentation. Changing
authority permanently retires the previous memory; call `Clear` on retirement.
Calls are serialized by the owner; this is not a thread-safe event dispatcher.
Snapshot ordering, protocol validation and action admission remain upstream.

Collections are copied on revision construction and outputs are read-only.
Payload immutability is a contract of `TContent`; arbitrary mutable payloads are
not deep-cloned. Source/slot strings use ordinal record equality. Changing a
source's role, scope or item-key ancestry changes identity without inventing a new
identity every time its title/artwork changes.

The focused tests establish semantic policy only. Host-level proof still needs
actual WinUI focus, image completion, nested slots, widget-local dialogs and
visible-frame verification; see `docs/maintainers/winui-feature-contracts.md`.
