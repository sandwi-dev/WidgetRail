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

## Observable keyed collections

`Collections.KeyedObservableCollection<TPayload>` is a WinUI-compatible
`ItemsSource` model, not a paging provider or a layout engine. Bind its `Items`
(`ReadOnlyObservableCollection<ObservableCollectionEntry<TPayload>>`) and bind
item templates to each entry's `Value` properties. `Key` is stable. Payloads must
be immutable values whose equality includes all content dependencies.

```csharp
var authority = new CollectionAuthority("runtime-1", "playnite", "library");
var collection = new KeyedObservableCollection<PosterItem>(authority);
collection.Apply(new KeyedCollectionRevision<PosterItem>(authority, 0, 1,
    games.Select(game => new KeyedCollectionItem<PosterItem>(game.Id,
        new PosterItem(game.Title)))));
```

Create/mutate this model on its owning UI dispatcher thread. Build immutable
revisions elsewhere if useful, then marshal `Apply` to that thread. Do not use
this as a cross-thread observable collection. `TryGetEntry` also checks thread
ownership; direct `Items` reads remain the caller's thread responsibility.

- Authority is fixed for the model lifetime. Worker/instance replacement creates
  a new model; a foreign update is rejected, never allowed to switch authority
  back to a retired runtime.
- Generation is monotonic and identifies query/traversal lifetime. Revision is
  monotonic within a generation. Equal/older revisions or older generations are
  rejected without notifications. Revision can restart when generation advances.
- Surviving keys retain the same observable entry within a generation. Changed
  payload emits `PropertyChanged("Value")`, not collection replacement. Equal
  values cause no event. A new generation retires wrappers even for equal keys.
- Insert/remove/move events use standard `INotifyCollectionChanged` semantics.
  No operation emits `Reset`; even generation replacement removes then inserts.
  This avoids hiding changes from selection/realization, but is not an atomic
  rendered update or a promise that large update batches are cheap.
- Keys/authority/nonnegative counters/null values are validated and source lists
  copied before mutation. Duplicate keys never partly modify the collection.
- Reentrant updates are rejected. Notification subscribers must not throw. If one
  does, the model faults and rejects further updates rather than silently
  proceeding from a partly observed revision; the frontend must replace it.
- The model prechecks surviving key order using dictionaries and a linear scan.
  Append/prepend/eviction/content updates do not repeatedly search for keys.
  `ObservableCollection` still uses array shifts for insertion/removal; arbitrary
  reorder also uses `IndexOf` and `Move` and can be quadratic. This implementation
  does not claim an optimal reorder algorithm or eliminate WinUI update costs.

Cursor fetch ordering, generation allocation, anchor selection, focus restoration
and input authority remain with their respective frontend/resource policies.
Actual WinUI viewport and focus preservation need integration tests in addition
to these model tests.
