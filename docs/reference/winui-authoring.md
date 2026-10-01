# Authoring widgets for the WinUI frontend

This guide describes the implemented migration-branch contract. Widgets publish
C# declarations; the trusted frontend creates and retains WinUI controls. Both
sandboxed and full-trust workers use this boundary. Neither loads XAML objects,
passes a `UIElement` across processes, nor owns a window or compositor.

Use `WidgetRail.WidgetSdk` and `WidgetRail.WidgetProtocol` in worker code. The
frontend owns native layout, realized item containers, drawing, automation peers
and presentation animation. Keep provider work asynchronous in the worker and
publish immutable views with stable IDs. A redraw is not a request to rebuild
the native control tree.

## A small page

```csharp
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

public sealed class ExampleWidget : Widget
{
    public override WidgetView Render() => new(
        UI.Stack("example.root",
            UI.Text("Library", "example.heading"),
            UI.Row("example.actions",
                UI.Button("Refresh", "example.refresh", "example.refresh-button")
                    .Icon(WidgetGlyph.Refresh),
                UI.Button("Settings", "example.settings", "example.settings-button")
                    .Icon(WidgetGlyph.Settings)))
            .InputScope("example.scope"),
        InitialFocusId: "example.refresh-button",
        ActiveInputScopeId: "example.scope",
        Surface: new WidgetSurfaceHints
        {
            PreferredWidth = 720, PreferredHeight = 480,
            MinimumWidth = 320, MinimumHeight = 200,
            HeightMode = WidgetSurfaceAxisMode.Content,
        });
}
```

This declares the page; implement `OnActionAsync` for its two action IDs. Surface
sizes are bounded preferences in logical DIPs. `Content` lets the frontend measure
the page and animate its surface size under the user's motion policy. Do not
calculate monitor pixels in the widget or run a timer to animate window size.

## Explicit WinUI-style Grid layout

Use track definitions for a page whose header sizes to content and whose list
must fill the remaining viewport. The declarations are serializable C# data;
the host assigns native Grid definitions and attached row/column properties.

```csharp
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

public static class GridPage
{
    public static WidgetView Create() => new(
        UI.Grid("grid.page",
            [GridTrack.Auto(), GridTrack.Star()],
            [GridTrack.Pixel(120), GridTrack.Star(2), GridTrack.Star()],
            UI.Text("Library", "grid.heading").InGrid(columnSpan: 3),
            UI.Button("Browse", "browse", "grid.browse").InGrid(row: 1),
            UI.Button("Featured", "featured", "grid.featured").InGrid(row: 1, column: 1),
            UI.Button("Recent", "recent", "grid.recent").InGrid(row: 1, column: 2))
            .Spacing(row: 12, column: 12).InputScope("grid.scope"),
        InitialFocusId: "grid.browse", ActiveInputScopeId: "grid.scope");
}
```

`GridTrack.Auto()` measures content, `Pixel(120)` uses logical DIPs, and
`Star(2)` receives twice the remaining space of `Star()`, subject to each track's
optional `minimum`/`maximum`. Empty row or column lists use WinUI's implicit
single star track. `.InGrid(...)` places an existing element without adding a
layout or focus wrapper. Unspecified cells use row/column zero and spans one.
Overlapping cells are allowed and follow declaration drawing order.

Positions and spans must fit the explicit parent's tracks. Tracks, lengths and
weights are bounded and validated; nonfinite sizes and misplaced cell declarations
fail with property paths. The Grid definitions own track geometry, so CSS-like
`direction`, `gap` and `flex-grow` do not override them. Existing theme paint,
text, padding and controller focus presentation still apply.

A star track needs a finite available extent to divide. Use a bounded widget/page
surface for a growing ScrollViewer or indexed list; an outer scrolling StackPanel
can make its scrolling axis unbounded. WinUI's native measurement rules apply.

`wrail validate` checks stylesheet syntax and reports source line/column warnings
for known unsupported WinUI layout declarations, including imported styles and
state rules. Warnings do not fail validation. Author tests can also run
`WidgetRail.WidgetStyling.WinUiStyleDiagnostics.Analyze(document)` for each parsed
document in a `WrssPackageResult`. It reports legacy `flex-shrink`, `flex-basis` and
`flex-wrap` declarations that have no WinUI effect. Use explicit native tracks and
bounds, or `UI.ResponsiveGrid` for a small wrapping set. The helper is read-only;
it does not change styles or guarantee final geometry. Keep native layout checks.

Explicit Grid requires protocol v64. The SDK calculates this for ordinary,
pinned and focus-presentation declarations. SDK indexed parents advertise the
current SDK protocol because demanded item templates may contain newer controls;
no provider data or row UI is loaded eagerly to determine that version.
The Gallery Controls page includes fixed/star tracks, row/column spans and live
proportion changes using stable IDs. Native layout and controller acceptance are
still separate from snapshot validation.

## Native control mapping

Directional input first navigates between focusable controls in the nearest
matching scroll container, including controls that need to be brought into view.
If no further control exists but content remains, D-pad/left-stick input and
keyboard arrows scroll that content. Internal authored focus links keep their
priority; links leaving the container wait until its content edge. Holding stops
at the edge; a fresh press may leave the container using the usual navigation
rules. Reversing while the remembered control is offscreen scrolls back toward it.
This behavior is host-owned and requires no widget handlers. A pane with no
focusable entry point still needs an authored way to enter it, or right-stick
scrolling. Editors, open menus and sliders being adjusted retain their own input.

| SDK declaration | WinUI implementation and author responsibility |
| --- | --- |
| `UI.Stack`, `UI.Row` | Native Grid tracks implement the supported flow layout and bounded growing children. Use stable children and theme classes. These are not general CSS flex containers. |
| `UI.Grid` | Plain WinUI Grid with explicit Auto, Pixel and weighted Star rows/columns, min/max track bounds, spacing and attached cell spans. WinUI owns measurement and arrangement. |
| `UI.ResponsiveGrid` | Adaptive columns from minimum column width and a column cap. Use this for a small reflowing tile set; use `UI.Grid` when rows and columns have explicit meaning. |
| `UI.VerticalScroll`, `UI.HorizontalScroll` | ScrollViewer for bounded, ordinary content. Use indexed collections for large lists; do not wrap one in an unbounded outer scroll. |
| Indexed/discovered `UI.CollectionList`, `UI.CollectionGrid` | ListView/GridView with native virtualizing panels. The worker supplies captured data, stable occurrence keys and demanded row declarations. |
| `UI.Text`, `UI.Button`, `UI.ActionSurface`, `UI.PosterTile` | TextBlock or Button with trusted content templates. A card's presentation children do not introduce extra action targets. |
| `UI.Slider`, `UI.Scrubber` | Native Slider and shared value reconciliation. `RequireControllerActivation()` enables A-to-adjust interaction. Emit absolute requested values; retain the provider's confirmed state and report pending work accurately. |
| `UI.Select` | A host-owned value button and themed MenuFlyout options, with native automation support and cyclic controller navigation. This is not an arbitrary editable ComboBox. |
| `UI.TextEntry`, `UI.SensitiveTextEntry` | A host-owned entry dialog, native editor and controller keyboard. Sensitive values do not enter published presentation state. |
| Progress/loading declarations | Native ProgressBar/ProgressRing. Publish progress state, not animation frames. |
| Images, icons, controller glyphs | Native image/font/vector presentation behind validated assets and artwork handles. Supply useful accessible names for icon-only actions. |
| Background/focus presentation surfaces | Shared retained native presentation with declaration-scoped source ownership. Background continuity does not grant retained actions or worker authority. |
| `WidgetView.WithModal` | A host-contained, themed modal layer with scoped input and focus return. The helper currently rejects a view declaring an embedded-media session; do not assume arbitrary ContentDialog or media/modal composition is exposed. |

Modals scroll their whole content by default. Set `WidgetModal.ScrollContent = false`
when supplying a bounded layout with fixed content and its own scroll region, for
example an Auto/Star Grid with a game header in the Auto row and a ScrollViewer in
the Star row. Give that content a growing, zero-minimum-height layout within the
modal. `ShowScrollbar` controls only the default whole-content scroll wrapper;
custom scroll regions use their own scrollbar setting. Modal scope, dismissal and
focus return still belong to the same host modal layer.
| Media viewport/window preview | Host-owned WebView2 or native preview integration. Keep using typed session/provider contracts, not raw HWNDs, DOM commands or custom drawing. |
| `WidgetView.PinnedLayout` | An independently declared projection in a host-owned surface. Placement, interaction, opacity and media ownership belong to the host. |

The map describes implementations, not a promise that every WinUI property is
available to a worker. Existing higher-level helpers such as NavigationShell,
SettingRow, Picker and Stepper compile into these primitives and keep their shared
theme classes.

## A virtualized list

```csharp
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

public sealed class ReadingListWidget : Widget
{
    private sealed record Book(string Id, string Title);
    private sealed record Query(IReadOnlyList<Book> Books);
    private readonly WidgetIndexedCollection<Query, Book> books;

    public ReadingListWidget()
    {
        var query = new Query(Array.AsReadOnly(new[]
        {
            new Book("first", "First book"),
            new Book("second", "Second book"),
        }));
        books = CreateIndexedCollection<Query, Book>("reading.source", query, query.Books.Count, new()
        {
            ReadRange = (captured, start, count, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<Book>>(
                    captured.Books.Skip(start).Take(count).ToArray());
            },
            ItemKey = book => new WidgetCollectionItemKey(book.Id),
            RenderItem = (_, book, context) =>
                UI.Button(book.Title, "reading.open", context.Id("book")),
            // Replace this no-op with the captured book's action handler.
            OnAction = (_, book, action, token) => ValueTask.CompletedTask,
        });
    }

    public override WidgetView Render() => new(
        UI.Stack("reading.root",
            UI.Text("Reading list", "reading.heading"),
            UI.CollectionList("reading.list", books, 64, "Books")),
        InitialFocusId: "reading.list",
        Surface: new WidgetSurfaceHints
        {
            PreferredWidth = 640, PreferredHeight = 480,
            MinimumWidth = 320, MinimumHeight = 240,
        });
}
```

`ReadRange` must return the exact requested range from its captured immutable query
and honor cancellation. `context.Id` derives identity from the occurrence key,
not its current index. `PublishQuery` replaces membership/order/count;
`UpdateContent` keeps all keys at their current indices. Neither asks the widget to
render every row. The example holds its small data set in memory; native control
virtualization and provider data retention are separate concerns.

For opaque next-page providers use `CreateDiscoveredCollection`; its count is the
known prefix, never a fabricated remote total. See the complete
[indexed and discovered collection guide](../developers/indexed-collections.md),
including artwork leases, query replacement, errors and focus return.

The older overloads accepting an eager `items` array and `estimatedItemExtent`
emit legacy collection-layout declarations that this frontend rejects. Likewise,
`ScrollElement.Paginate`, cursor-window presentation and virtual-window metadata
are not a WinUI collection path. Provider-side cursor/resource helpers can still
be useful, but publish their data through indexed/discovered sources. Small static
content can use ordinary Scroll or ResponsiveGrid without collection metadata.

## State, focus and presentation

Keep IDs stable across updates and widget visits. Initial focus is a fallback;
the host restores a remembered target when it remains visible and focusable.
`PersistFocusAs` connects responsive equivalents. Input scopes constrain shortcuts
and focus; explicit `FocusUp/Down/Left/Right` edges map to native XYFocus targets.
Use remembered-child groups and indexed logical targets for entry into dynamic
content rather than inventing a new focus ID on every frame.

Busy means an action is pending; disabled means interaction is unavailable. Do
not disable a playback button merely to show in-flight work, since a disabled
control can lose native focus. Reusable SDK operation helpers retain action
sequencing and cancellation across UI updates. A completed native click is not
confirmation that a provider operation succeeded.

Use theme-owned semantic component classes before custom styles. `.Classes(...)`
replaces author classes while preserving SDK-required classes; `.AddClasses(...)`
adds variants. Focus and selection are distinct states. Global animation settings,
reduced motion, text scaling, Bold Text and high contrast remain authoritative.
See [styling](../developers/styling.md), [native layout mapping](../maintainers/winui-layout.md)
and [native style mapping](../maintainers/winui-styles.md) for supported properties.
WRSS validation accepts the shared language; successful syntax validation alone
does not prove a property has a WinUI mapping. General percentage lengths,
flex-basis/shrink and arbitrary wrapping flow are not currently implemented.
Ordinary images and background surfaces honor `object-fit`, the nine
`object-position` keywords, `image-tint` and `scrim-color`. Tint paints over artwork;
the scrim covers its bottom 45 percent. Both remain below foreground content.

## Verify a widget

Use the SDK test host with fake provider responses for settings writes, launch,
power, network and audio actions. `WidgetTestHost.ValidateWinUiPresentation(snapshot)`
runs protocol validation and the same declaration-admission rules as the native
frontend. It checks main and pinned trees and declared focus fragments, reporting
structural paths, safe element IDs and replacement guidance for legacy declarations.
`wrail render snapshot.json` applies this same preflight before printing or saving
a snapshot. It does not execute widget code or request lazy collection rows.
For example, an old collection on a Scroll reports `collectionLayout` and points
to indexed/discovered collections rather than failing later with a generic error.

This check does not fetch lazy rows. After using the indexed test host to acquire a
range, also call `WinUiPresentationContract.ValidateSubtree(item.Root)` on its items.
Continue testing range/action authority and provider effects with those fixtures.
Preflight checks declaration support; it does not certify every WRSS property,
native measurement, controller input or smooth rendering. Then validate actual WinUI geometry and focus;
protocol validity alone does not establish native support or visual parity.
Keep physical controller/provider acceptance separate from fixture passes, and
avoid triggering system-changing actions in automated visual checks.
