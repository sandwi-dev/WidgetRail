# Indexed collections on the WinUI migration branch

Use an indexed collection when a captured query has an exact item count and a
stable order. The widget owns data and actions; WinUI owns realized controls,
measurement, scrolling and presentation. A large collection does not require a
large parent UI tree. This contract is implemented on the migration branch;
the installed native frontend is not being switched by these instructions.

## Authoring

Register one `WidgetIndexedCollection<TQuery,TItem>` with
`CreateIndexedCollection(sourceId, initialQuery, count, options)` during widget
construction. Render its shell with the indexed overload of `UI.CollectionList`
or `UI.CollectionGrid`. Keep the source ID and collection element ID stable for
that logical location; multiple placements need different collection element IDs.

Supply these callbacks:

| Callback | Contract |
| --- | --- |
| `ReadRange` | Read from the captured immutable query. Return exactly the requested range, observe cancellation, and support concurrent calls. |
| `ItemKey` | Return a stable occurrence key. Two appearances of the same song need distinct keys; a position alone is not an identity. |
| `RenderItem` | Build one Button or ActionSurface for the captured item. It must always exist; apply filtering before computing count and order. Use `context.Id(name)` for stable item-local child IDs. |
| `OnAction` | Handle the captured query/item/action. Do not look up `SourceElementId` in a mutable viewport buffer or use the current item at the old index. |
| `ResolveArtwork` | Required for widget-owned opaque artwork. Resolve only handles declared by that captured item. Broker-provided app-library icons are resolved by the host and need no callback. Return null when absent; do not block query publication waiting for images. |

`estimatedItemExtent` estimates loading geometry; it does not replace WinUI's
measurement of realized content. The grid's minimum width and optional maximum
columns describe adaptive layout. Authors do not compute monitor pixels, visible
rows, scroll offsets or cache margins.

The production Playnite examples are
[Home](../../samples/PlayniteLibraryWidget/PlayniteLibraryIndexedHome.cs) and
[Library](../../samples/PlayniteLibraryWidget/PlayniteLibraryIndexedBrowse.cs).
Their query objects freeze filtering/order before exposing the count.

## Publishing data

- Use `PublishQuery` when membership, ordering or count changes. Old range/action
  bindings retire with that query. Use stable occurrence keys in the replacement.
- Use `UpdateContent` only when every key stays at the same index and count stays
  unchanged, for example a favorite indicator or playback state. Freeze all
  values used by range rendering; do not mutate a shared list after publication.
- Neither operation renders all rows. Rebuild the ordinary page declaration so
  it captures the new descriptor. Source publication invalidates the widget.
- Count zero means empty. It is not an unknown total. Do not invent a remote total
  or expose a continuation token as a random-access range source. A finite returned
  snapshot can be indexed independently of a provider's larger remote library.
- Persisted display metadata does not confer action authority. If showing saved
  placeholders during startup, render them disabled and replace them with a live
  query before enabling actions. Playnite's indexed Home test covers this handoff.

## Focus, details and actions

Keep `InitialFocusId`, input scopes, focus groups, background surfaces and focus
presentation declarations on the ordinary page. Item declarations can contribute
their authored artwork and presentation; recycled containers do not erase logical
membership. A modal still belongs to its parent page and uses its own input scope.

For an exact return target, retain the admitted action's `FocusedCollectionItem`
and use the source's `Enter` method with a new positive request ID. The host checks
query generation and occurrence key before realizing/focusing it. Do not store a
native control, assume it remains realized, or issue a new focus request on each
data update. See Playnite's indexed details-return test for a complete example.

The host owns range leases and validates displayed/current command bindings.
Widget authors must still recheck domain authority at execution when an external
item can be removed or changed. Action admission is not execution completion.

## Validation and bounds

Use `WidgetTestHost.CreateIndexedCollectionHost`, acquire ranges, and route actions
through those leases. Test deep items, duplicate occurrences, empty results,
query replacement, cancellation, content changes, stale actions, modal return,
and saved-to-live handoff. Native checks remain necessary for viewport geometry,
DPI, focus visibility, theming and animations.

Each range is bounded to 64 items and 2048 nodes. A source admits four provider
tasks; cancellation does not free a task that ignores its token until it finishes.
Keep range reads bounded and avoid remote work in `RenderItem`. These safeguards
apply equally to full-trust and sandboxed widget declarations.

App-library artwork handles must be retained unchanged from the broker response.
Their reserved syntax does not grant access: the host checks the current widget
identity, live broker registration and exact row lease before returning pixels.
Do not manufacture handles or replace app icons with widget callbacks. Mixed rows
still need `ResolveArtwork` for any additional widget-owned images.
