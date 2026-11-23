# Indexed collections

Use an indexed collection when a captured query has an exact item count and a
stable order. The widget owns data and actions; WinUI owns realized controls,
measurement, scrolling and presentation. A large collection does not require a
large parent UI tree. This contract is implemented in the production frontend;
the installed native frontend is not being switched by these instructions.

## Authoring

Use `.WithDirectionalAnchor(FocusAnchorPoint.Center, 50)` to center the focused
poster in a horizontal rail. On a vertical feed,
`.WithDirectionalAnchor(FocusAnchorPoint.Leading, 25)` places the start of
the focused entry one quarter of the way down its viewport. The percentage
accepts integers from 0 through 100. Leading, Center and Trailing select the
point on the item. The host clamps to content boundaries and keeps short rows
visible. The first and last rows can therefore move away from the anchor.

This opt-in requires protocol 143. It applies to directional travel along the
scroll axis. Ordinary entry, modal return, endpoint keys and free scrolling
keep their existing policies. A media gallery with public thumbnails can
configure its horizontal strip with `.WithThumbnailAnchor(FocusAnchorPoint.Center, 50)`.

From protocol 145, ordinary `UI.HorizontalScroll` and `UI.VerticalScroll` containers
also accept `WithDirectionalAnchor`. For example, use
`UI.HorizontalScroll("cards", row).WithDirectionalAnchor(FocusAnchorPoint.Leading, 10)`
for a short row of mixed-width action surfaces. Directional moves between descendants
of that viewport use the anchor. Entry from another section and free scrolling keep
their usual behavior.

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

For an adaptive grid, WinUI paints a static tile in each realized position
while its row data is pending. The tile has no input or accessibility action
and is released when the row arrives or the range fails. Supply the ordinary
item extent and artwork fallback. No second collection or placeholder records
are needed in the widget.

The production Playnite examples are
[Home](../../samples/PlayniteLibraryWidget/PlayniteLibraryIndexedHome.cs) and
[Library](../../samples/PlayniteLibraryWidget/PlayniteLibraryIndexedBrowse.cs).
Their query objects freeze filtering/order before exposing the count.

## Reading groups with attachment actions

`UI.ReadingGroup` lets an indexed message contain explicit child buttons or action
surfaces without turning the whole message into a command. The host owns child
focus and the exact indexed occurrence used for modal return. Protocol 132 is
required. A group permits at most 32 child actions and 128 layout descendants;
rich-text spans retain their separate bounded allowance. Layout is at most four
levels deep within the group and within each admitted child action surface.
Ordinary action surfaces keep their 32-descendant limit. Child actions cannot
contain another interactive control, input scope, menu or preview.

On directional entry, WinUI focuses an available inline action directly. Entering
the row does not invoke it. Text-only messages remain readable as a single stop.
Selection mode keeps focus on the row. Back can return from an inline action to
the message when its text or row commands are useful.

Use a single action for a shared destination. For individually actionable
thumbnails, protocol 139 also permits a plain horizontal scroll viewport as a
direct child of a reading group. Each thumbnail can own a separate action,
accessible name and artwork handle. The viewport cannot paginate or run a
second collection inside the indexed row. A group consisting solely of this
strip does not add a Back stop around the thumbnails.

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

## Preserving local membership changes

For a complete local collection of up to 512 items, optionally provide
`OccurrenceKeys` on `WidgetIndexedCollectionOptions`. Return the immutable
query's complete ordered occurrence keys (including distinct keys for duplicate
songs/items). Return `null` for queries that do not opt in. This metadata does
not fetch or render all rows.

```csharp
OccurrenceKeys = query => query.IsLocalQueue
    ? query.Items.Select(item => new WidgetCollectionItemKey(item.OccurrenceId)).ToArray()
    : null,
```

Continue calling `PublishQuery` for insertion, removal or reordering. The host
rebases focused and visible occurrences onto the new order and preserves their
position in the viewport. If the focused occurrence was removed, it prefers the
next surviving occurrence in the old order, then the preceding one. An update
never moves focus into a collection from another control. User navigation or
scrolling cancels pending restoration; loading responses cannot take it back.

The SDK copies and validates the metadata, rejects duplicates or incomplete
orders, and checks demanded rows against it. `UpdateContent` must leave that
order and participation unchanged. Old rendered content may remain visible
while new ranges arrive, but every input still requires the new query's live
lease; keys are continuity hints, never permission to reuse retired actions.
This opt-in requires protocol 91. Large/remote collections retain normal lazy
query replacement and need no complete key inventory.

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
and use `CreateFocusRequest().EnterCollection(source, collectionId, target)` when
handling Back. Retain the resulting request across renders. The host checks
query generation and occurrence key before realizing/focusing it. Do not store a
native control, assume it remains realized, or issue a new focus request on each
data update. See Playnite's indexed details-return test for a complete example.

For nested navigation, `navigator.PushFromCollectionAction(route, action,
source.Descriptor)` captures the opening occurrence on the bounded route stack.
After Back, render `navigation.CollectionReturnFocus?.Resolve(collectionId,
source.Descriptor)` as the view's focus-group entry request. The navigator allocates
one shared focus request per Back; repeated rendering reuses it. Resolution returns
null after query replacement, collection replacement, or removal of the index,
leaving normal host focus and viewport memory in charge. It does not remap a
removed item or select a semantic destination; those decisions remain widget policy.

The host owns range leases and validates displayed/current command bindings.
Widget authors must still recheck domain authority at execution when an external
item can be removed or changed. Action admission is not execution completion.

## Validation and bounds

### Scroll without moving focus

Use the existing `WidgetView.ScrollRevealRequest` for both ordinary scrolling
containers and indexed collections. For a lazy row, use its exact occurrence:

```csharp
var item = source.FocusTarget("queue", new(currentOccurrenceKey), currentIndex);
// Run once for the navigation event, then retain this request across renders.
var request = CreateFocusRequest().RevealCollection(source, "queue", item);
return view with { ScrollRevealRequest = request };
```

The target type names the occurrence; `Reveal` does not select, activate or focus
it. `Enter` remains the explicit focus-navigation operation. Indexed reveal
requires protocol 90. The SDK validates the source/query/index; the native host
also resolves the row key before scrolling, so a stale position cannot reveal an
unrelated item.

Issue a new request for the navigation event that needs it, such as entering a
queue. Retain the same request through harmless renders. Do not increment it on
every update or track change. Pointer/controller scrolling, keyboard navigation,
query replacement, hiding and dismissal cancel pending work; late row delivery
cannot restart it. A pending focus handoff takes precedence. Ordinary container
requests keep using their declared `TargetId`; do not also supply an indexed
target.

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
artwork remains host-owned. Pinned Up Next is deliberately a finite preview of two
ordinary rows. Their occurrence identity remains encoded in row/action/persistence
IDs; neither cursor anchors nor `CollectionItem` metadata belong on that plain scroll.
The main Queue page requires its complete indexed source and has no eager fallback.

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

## Complete private-model sample

[FullApplicationReferenceWidget](../../samples/FullApplicationWidget/FullApplicationReferenceWidget.cs)
is the minimal sandboxed example for an application-scale private model. Its
immutable 10,000-record model supports exact random-access ranges, so it uses
`CreateIndexedCollection`, not discovered continuation. Parent snapshots contain
no eager rows. The provider reads only the requested bounded range, and item-local
IDs come from `context.Id`. Actions use captured records plus current query/route
authority. Details Back uses the navigator's captured collection return intent and resolves
it against the current source; no widget-managed request counter is needed. Refresh changes content revision while
preserving known membership; the sample explains when to use `PublishQuery` instead.

Its tests use real SDK range/action leases, safe retry, refresh retirement,
background cancellation and deep reverse reads. They do not emulate WinUI layout
or claim physical scrolling acceptance. Rebuild the sample package and the exported
SDK feed before trying it on the migration frontend.


## Complete loaded lists

`CreateLoadedCollection<TContext, TItem>(sourceId, queryIdentity, context, items,
options)` adapts already-loaded immutable values to the existing lazy indexed
source. Provide item keys, row rendering, actions and optional artwork resolvers;
do not provide another range slicer. The adapter freezes the list, while item and
context objects must themselves be immutable. Rendering and actions receive the
context captured with that publication, not a mutable current-page variable.

Call `Publish(queryIdentity, context, items)` at the data-change boundary. It
returns `Unchanged`, `Content`, or `Query`: equal identity/context/items does not
publish; changed content with the same ordered keys increments content revision;
changed identity, membership or order starts a query. `forceReset: true` retires
otherwise identical input. Query identity is private domain data: use the relevant
account, build, filter or opening identity. It is never inferred from item keys.

`PreserveOccurrences` is an explicit opt-in for host focus/viewport continuity.
It supplies occurrence keys only within the existing 512-item continuity bound;
it does not cap the list at 512 items. Identity changes and explicit resets suppress
continuity with the previous query. Larger lists remain lazy indexed sources.
Duplicate domain items must have distinct occurrence keys. Duplicate keys fail
before publication. This validation costs a full key scan when membership changes;
unchanged publications allocate no rows and content-only changes reuse frozen keys.

Use the adapter for complete lists whose publication classification is mechanical.
Keep the original indexed source for provider paging, engine-owned immutable
snapshots that intentionally retire every query, or specialized occurrence policies.
The adapter does not own data fetching, semantic selection, expansion preferences,
permissions, or provider/account validation.

### Live reading windows

For a bounded loaded window within the 512-occurrence continuity limit, opt into
`PreserveOccurrences` and `FollowEnd`. The host keeps an already-at-end reader at
the end when rows arrive or existing content changes height. It does not move
external focus. A focused row or a visible/entered preview is an inspected reader,
including when its viewport is physically at the end. That native ownership
suppresses automatic tail eviction and following until inspection ends. Scrolling
away, hiding the view, and replacing semantic identity keep their normal rules.

For these opted-in finite lists, a strictly appended suffix preserves the existing
query and exact row leases only when the SDK proves the old immutable context,
item values, keys and indices unchanged. This reuses indexed prefix retention;
it does not transfer an old media lease to a different row. Edits, removals,
reordering, context changes and resets retain normal invalidation. Captured old
snapshots remain immutable and bounded by the existing lease budgets. An open
preview retains one still-current semantic lease when a partial page is refilled;
new native row actions use the new page binding. The preview releases its hold on
close, and content/domain retirement still revokes it immediately. A chooser may
share an existing current hold through the same bounded retention API; the last
release cannot be resurrected.
A full-window eviction already admitted before a new inspection can still retire
that old lease. Recovery repairs the reader anchor; stale actions/media are never
kept authorized merely to avoid closing a preview.

An optional pure `LiveOrder` comparison supplies provider chronology for rolling
windows. The SDK verifies ordered forward progress; it never compares opaque IDs
or assumes that a larger count means newer data. Its source-owned epoch and
forward-chain provenance survive coalesced appends. Semantic changes and explicit
resets replace the epoch; reorders and backward movement break the forward chain.
Do not reproduce these counters
in widget state. Normal loaded lists and indexed providers remain unchanged.

Automatic eviction requires a recovery-capable source. Supply
`OnRecoverViewport(context, request, token)` and optionally `OnViewportChanged`.
`ViewportState` conservatively combines active viewports: an older reader prevents
following. This feedback is advisory across process boundaries; a user can move
after an observation was sent. The host therefore retains its existing bounded
paint and exact anchor when a rolling window evicts the newly chosen reader.
It asks the source to recover that key, using the existing cancellable range lane.
No controller action is synthesized and ordinary immutable `Locate` is unchanged.

`ObservedEndItemKey` captures the tail occurrence in the exact revision observed
by all active viewports at their physical end. It is null during recovery, for an
empty collection, while any reader is away from the end, or after an unobserved
publication. Use this captured key when a domain action depends on what was shown;
combining old `IsAtEnd` feedback with the latest collection data could include an
unseen arrival. This does not prove the loaded page is the provider's newest page
or authorize an external effect: the consumer must still validate account, route,
provider pagination and its operation lifetime before acting.

The callback prepares provider data outside locks, then uses
`request.TryCommit(() => ...)` for one bounded synchronous domain commit. Do not
perform IO or await inside the commit. For a cursor resource:

```csharp
using var scope = history.CaptureRecovery();
var page = await ReadAroundAsync(request.AnchorKey, token);
return request.TryCommit(history, scope, page)
    ? WidgetViewportRecoveryResult.Recovered
    : WidgetViewportRecoveryResult.NotFound;
```

`TryRecover` accepts at most `MaximumRetainedItems`, preserves the resource reset
identity, replays bounded live changes received during the read, and requests no
focus. Navigation, reset, lifetime exit, an unavailable anchor with no surviving supplied
neighbor, and replay-journal overflow reject the result. Dispose the recovery scope even when the read fails.
Validate the current provider account/selection as usual. A deleted anchor does
not make a useful around-page unusable: the host supplies bounded old-neighbor
keys, and the shared commit selects the next surviving old occurrence, then the
previous one. The reported resolved key cannot override a newer user anchor.
Return `NotFound` only when neither the anchor nor its supplied neighbors can be
recovered. A provider failure releases held paint rather than leaving a
permanent pending focus operation.

Remote cancellation cannot retroactively undo a commit that beat its receipt.
The host owns the visible guarantee: scroll changes update the held anchor,
useful returned data can satisfy that newer anchor, and focus leaving the list
is not reclaimed. A replacement around-read is issued only when the received
window cannot cover the newer reader. Recovery enters reader mode before waiting,
so ordinary incoming events do not repeatedly refetch old history. Four actual
callback executions and eight viewport observations bound one source; cancelled
callbacks that ignore their token still occupy their execution slot until done.

Consumer applicability: Discord message history opts in. Capture Library,
Playnite Home/Library/achievements, Settings installed widgets, Games & Apps,
Spotify queues, YouTube/YtMusic lists, SDK Gallery and templates keep their
existing collection behavior; these are navigation lists, not forward live tails.
Native qualification covers the opt-in reader and ordinary occurrence continuity;
automatic following in multiple pinned readers has no shipping adopter yet.


## Optional multi-selection

Set `WidgetIndexedCollectionOptions.EnableSelection` for a finite indexed source.
Ordinary activation is unchanged until `BeginSelection()` or the user starts a
held selection gesture. With protocol v128, holding L2/LT selects the focused item
and adds each item reached by directional navigation. Release keeps the selection;
revisiting a selected item does not remove it. This selects visited items, not the
intervening range in a grid. Passive scrolling does not add items. The host owns
trigger arbitration, delayed row admission, release and cancellation on departure;
widgets must not implement another trigger loop. Before selection starts, explicit
widget shortcuts keep precedence. While collection selection is active, L2/LT
owns the selection gesture and replaces any conflicting widget shortcut hint.
Media/browser/keyboard controls still retain ownership when focus is inside them.

In selection mode the
host supplies row checkboxes and selected accessibility status; click, Space and
controller A toggle selection, Shift-click extends the anchor range, and Ctrl+A
selects the complete query only when it fits the 256-item bound. A gesture that
would exceed the bound is rejected in full with an accessible explanation; it
never means only visible or loaded rows.

For an explicit, bounded menu command such as **Select first 256 matches**, call
`SelectFirst(count)` after entering selection mode. It replaces the selection in
the current query order without fetching any rows. The allowed count is 1–256.

`Selection` is an immutable snapshot with `IsActive`, `Indices`, `Count`, and
`LimitReached`. `ClearSelection()` keeps the mode active; `EndSelection()` clears
and leaves it. `PublishQuery` clears selection and its anchor while retaining the
active mode. `UpdateContent` preserves positions in the unchanged query.

`SelectionChanged` fires after explicit selection mutations, outside the source
lock. Read `Selection` in the handler to reconcile domain presentation such as
turning off item previews while selecting. Repeated add-only input for an already
selected item does not publish an unchanged selection. Query publication still
owns its normal invalidation and mode-preserving reset.

Before a domain operation, call `ReadSelectedAsync` to capture at most 256 current
items in bounded provider reads without rendering row templates. A selection or
query/content change rejects the result with `WidgetIndexedSelectionException`
(`Changed`); saturation reports `Busy`. Caller cancellation remains cancellation,
and a provider timeout remains `TimeoutException`. The widget owns the domain
operation, authorization and per-item revision checks. Selection is not permission
to mutate a file or an external account. Larger query-wide bulk operations are not
implied by this API.

Selection was introduced in protocol v112; current SDK selection declarations
also advertise `SupportsHeldSelection`, requiring v128. The new host retains the
existing controls for older declarations and never sends held-selection gestures
without this capability. Lists that do not opt into selection, previews and
discovered sources keep their existing activation and input behavior.

## Authored collection exits

`CollectionList` and `CollectionGrid` support `FocusUp`, `FocusDown`, `FocusLeft`
and `FocusRight`, using the same declared-target contract as buttons. A focused
row's explicit neighbor wins for that direction; otherwise navigation moves
between collection items first and applies the collection's neighbor only at
its boundary. The host still finishes scrolling within that axis before
exiting and rejects hidden or unavailable targets. Declare a shared panel exit
on the collection instead of repeating it in every lazily loaded row.

## Key lookup for large-query continuity

### Preferred entry

`UI.CollectionList(...).PreferredEntry(new WidgetCollectionItemKey(selectedId))`
opts into protocol130 preferred occurrence entry. It also works for indexed grids.
The finite source must supply `FindIndex`. The host uses the key when entering
the collection by default or directional navigation, locates it within the exact
query, and realizes only the needed range. A missing key falls back to ordinary
remembered/default entry. Explicit keyed focus, reveal and navigation-return
requests retain their existing authority and are not overridden.

This is a presentation preference, not a focus request on every snapshot. User
departure, navigation, scrolling, hide, changed preference or query replacement
retires pending lookup work. Existing sources without `PreferredEntry` are
unchanged. `PreferredEntry(null)` clears the preference; choosing `DirectionalEntry`
also clears it, so visible-edge and preferred-key semantics cannot conflict.
Use the preference for master/detail lists such as a selected conversation, not
automatically for every highlighted or currently playing item.

A finite indexed source may provide `FindIndex(query, key, cancellationToken)`.
It returns the occurrence's position in that exact immutable query, or null when
it no longer exists. Use an indexed provider lookup rather than fetching all rows.

With this protocol-v113 declaration, the host can locate the previous viewport and
focused occurrence when membership/order changes, including collections larger
than the 512-key complete-order limit. Lookup uses the existing bounded range lane,
returns metadata only, and creates no row/action/artwork authority. The host still
acquires a current row lease and verifies its key before restoring it. User scroll,
navigation, departure, hide, or another replacement cancels pending continuity.
Focus is restored only when the collection owned it before replacement.

The complete `OccurrenceKeys` path remains available for bounded local collections.
Providers without `FindIndex` retain their existing query replacement behavior.

For an ungrouped bounded occurrence map (at most 512 entries), ordered
insertions/removals now retain the native items source and surviving row slots.
This covers prepending chat history and trimming its opposite edge without
clearing the visible list. Payload leases and pending reads still retire at the
query boundary; retained paint is not permission to invoke an old action.
Retention follows occurrence identity, not the index that a different item may
occupy later. Reorders, unrelated queries, groups and large lazy collections keep
the existing replacement/lookup path. Timeline windows use native WinUI
`KeepLastItemInView` anchoring so prepending at offset zero does not discard the
visible message. Explicit page-edge requests wait for current row layout, as
automatic boundary observations already do.
