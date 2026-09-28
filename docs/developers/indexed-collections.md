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
- For a complete indexed source, count zero means empty. It is not an unknown total. Do not invent a remote total
  or expose a continuation token as a random-access range source. A finite returned
  snapshot can be indexed independently of a provider's larger remote library.
- Persisted display metadata does not confer action authority. If showing saved
  placeholders during startup, render them disabled and replace them with a live
  query before enabling actions. Playnite's indexed Home test covers this handoff.

## Forward-discovered providers

Use `CreateDiscoveredCollection(sourceId, initialQuery, options)` when the provider
has an opaque next token and no complete immutable result. Its shell uses the same
`UI.CollectionList`/`UI.CollectionGrid` overloads. A discovered descriptor's `Count`
is the addressable prefix already admitted, never a guessed remote total. Its
`Discovery.HasMore` is independent of that count; initial zero with `HasMore=true`
means discovery has not finished. Complete indexed descriptors have no Discovery.

```csharp
results = CreateDiscoveredCollection<Query, Result>("search", initialQuery, new()
{
    PageSize = 24,
    MaximumItems = 1024,
    LoadNext = async (query, continuation, count, token) =>
    {
        var page = await provider.SearchAsync(query.Text, continuation, count, token);
        return new(page.Items, page.NextToken);
    },
    ItemKey = item => new(item.OccurrenceId),
    RenderItem = (_, item, context) => UI.Button(item.Title, "open", context.Id("row")),
    OnAction = (query, item, action, token) => OpenCapturedAsync(query, item, token),
});
```

Keep query/item values immutable and keys stable. `LoadNext` returns at most the
requested number of items; null continuation means exhausted. Tokens stay private
to the worker and are bounded to 1024 characters. Empty filtered pages can advance
the token. Repeated tokens are rejected to prevent loops. Page and token validation
complete before any prefix is published. Source failure leaves existing data intact.

The default duplicate policy is `Reject`: repeated domain entities need distinct
occurrence keys. `KeepFirst` is an explicit unique-result policy, useful for YouTube
videos repeated across search pages. It retains the first admitted value/key and
does not silently update earlier rows. Duplicates still consume provider page size.

Native incremental demand starts one load for the current query. Failure requires
explicit Retry; changing the query uses `ReplaceQuery`, which retires previous
ranges, actions and pending reads. A successful append retains old-prefix leases,
artwork and action identity. Native source identity and the viewport remain owned
by WinUI; authors never adjust offsets or realize placeholder trees. Discovered
sources do not support static group partitions or arbitrary insertion/reordering.
The WinUI adapter pauses after four consecutive successful pages that admit no
new rows, exposing explicit **Load more**. This preserves truthful `HasMore` while
preventing an underfilled native viewport from consuming arbitrary provider quota
across empty or duplicate-only pages. New rows reset that burst count. This is a
host demand policy; it does not alter provider tokens or the discovered history.

`MaximumItems` is an explicit 1–4096 metadata-history limit (default 1024).
Reaching it with more provider data publishes `LimitReached`, retaining `HasMore`
and every admitted row. The host displays that limit; refine or replace the query
to continue. There is no implicit leading-page eviction or invented backward-token
support. Separate range/artwork budgets still control materialized payloads.
Token history is also bounded to 4096 steps, including empty/duplicate-only pages.

`PageSize` is 1–64 and no greater than `MaximumItems`. `ReadTimeout` is 100 ms–30 s.
Cancelled providers retain their real capacity until they finish; at most four
provider tasks can be active across current and retiring queries. Timeouts/errors
publish bounded user-facing failures, and a still-draining load can report busy on
Retry. Providers must still honor cancellation and avoid unbounded synchronous work.

Tests can call `WidgetIndexedCollectionTestHost.ContinueAsync(collectionId)` and
then `PublishSnapshot`, followed by ordinary range/action/artwork assertions.
Use `retry: true` only to model deliberate Retry. Test old-prefix actions across
append, replacement retirement, empty pages, duplicate policy, loops, cancellation,
failure/retry, limits and deep reverse navigation. Native focus and viewport checks
remain separate from worker tests. See YouTube Discover for a production adoption.

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

## Games & Apps: finite broker snapshots

[GamesAppsWidget.Indexed.cs](../../src/FirstPartyWidgets/GamesAppsWidget/GamesAppsWidget.Indexed.cs)
shows the same contract applied to a finite system-provider result. Library and
Running each register a stable source. Their immutable query captures the exact
ordered app array, row presentation state, route generation, and (for Running)
the broker observation revision. Ranges slice that array; no remote item count or
random-access provider API is invented. Catalog keeps its explicit bounded
Previous/Next page controls.

The occurrence key is derived from the durable opaque SavedId, not AppId or a
position. Existing hashed row element IDs remain stable; new widgets should use
`context.Id` when they do not already have a collision-free item ID scheme.
Membership/order changes publish a query; metadata, availability and busy-state
changes update content without changing keys or query generation. Repeated
renders with unchanged row state do not advance the descriptor.

Row actions receive the captured app. They recheck route/generation and exact
SavedId/AppId inside the command's mutation lock. Running additions also require
the captured observation revision, then use broker confirmation/registration as
before. Launches still resolve the SavedId immediately before requesting launch,
so current opaque AppId rotation remains supported. Existing CAS persistence,
curation, cleanup and launch-result handling are shared with the bounded Catalog
workflow. No action uses the current item at an old row index.

The ordinary page initially focuses its collection ID. Deliberate selection
changes use a keyed `FocusTarget`/`Enter` request; ordinary data refreshes do not
continually re-enter the list. When a still-present key moves to a new query, the
existing one-shot intent is kept valid with the same request ID, so a host that
already consumed it does not replay the focus move. The native host owns deep
focus and scroll memory.

Games & Apps tests retain the actual empty-child indexed parent. Semantic
inspection acquires bounded ranges through `WidgetIndexedCollectionTestHost` and
keeps those fragments in a test-only side table, rather than fabricating inline
children in the production snapshot. Styling checks use the installed widget WRSS
and platform theme for both the parent and an actual deep range. Launch and
running-add regressions route acquired leases against mocked host services; no
real applications or user curation state are changed by those tests.

## Spotify: complete queue and discovered provider results

[SpotifyIndexedQueue.cs](../../samples/SpotifyWidget/SpotifyIndexedQueue.cs)
indexes the complete bounded queue observation returned by the provider. Its
loader explicitly has no adjacent cursors. Repeated songs keep the existing
occurrence keys, while range rendering uses the logical index to distinguish
"Next track" from "Play from here". The first row of a deep range is not the
head of the queue. Rows declare no links to unrealized neighboring element IDs;
the page enters the collection ID and WinUI owns logical navigation/memory.

The immutable query captures the entire returned queue and active generation.
An action checks the exact current observation and route after command admission,
then executes the captured occurrence and suffix. This preserves Spotify's
existing behavior: first occurrence uses Next without replacing context; later
occurrences start the bounded captured suffix, including repeated URIs. HTTPS
artwork remains host-owned. Pinned Up Next keeps its existing two inline rows.

Playlist library, playlist details and Search use
[SpotifyDiscoveredCollections.cs](../../samples/SpotifyWidget/SpotifyDiscoveredCollections.cs).
Each keeps an append-only discovered prefix bounded to 1,024 logical items by
default. Provider offsets stay in worker-private continuation tokens; remote totals
only determine whether another page may exist. They never become indexed `Count`
or justify random access. The host sees admitted count, continuation status and
bounded range reads, and renders the common loading/retry/Load-more/limit footer.

Library deduplicates by playlist ID using `KeepFirst`. Detail and Search preserve
repeated occurrences with keys incorporating provider-page offset and admitted
slot plus playlist/query context. Filtered response slots are not remote item
indexes. Actions capture that occurrence and query, then revalidate route and
availability after command admission. Background/reopen and destination changes
retain accepted history; explicit refresh retires the query. Returning from a
playlist or Settings uses the admitted keyed `FocusedCollectionItem`, with a
one-shot request ID from the common navigation revision.

The source prepares lazy grid/list declarations, with no inline item children or
links to unrealized neighbors. Realization never controls metadata retention:
reversing into previously admitted results reads the retained prefix without
another provider request. The shared four-empty-page pause and explicit retry
policies also apply. See the [Spotify development notes](../../samples/SpotifyWidget/DEVELOPMENT.md)
and `SpotifyDiscoveredTests` for provider, action-authority and keyed-return examples.
