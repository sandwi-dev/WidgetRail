using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class GridLayoutTests
{
    private const string Generation = "abababababababababababababababab";
    public static Task Declarations()
    {
        var grid = UI.Grid("grid", [GridTrack.Auto(10, 100), GridTrack.Star(2)],
            [GridTrack.Pixel(120), GridTrack.Star()],
            UI.Button("First", "first", "first").FocusDown("second").InGrid(columnSpan: 2),
            UI.Button("Second", "second", "second").InGrid(1, 1))
            .InputScope("scope").RememberChildFocus("first").Shortcut(ControllerButton.Y, "refresh", "Refresh")
            .Spacing(8, 12);
        var snapshot = new WidgetView(grid, "first", ActiveInputScopeId: "scope").CreateSnapshot("grid.widget", 1);
        Equal(64, snapshot.ProtocolVersion);
        var read = SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot));
        Equal(GridTrackSizing.Auto, read.Root.GridLayout!.Rows[0].Sizing);
        Equal(2d, read.Root.GridLayout.Rows[1].Value);
        Equal(120d, read.Root.GridLayout.Columns[0].Value);
        Equal(8d, read.Root.GridLayout.RowSpacing);
        Equal(12d, read.Root.GridLayout.ColumnSpacing);
        Equal(2, read.Root.Children[0].GridCell!.ColumnSpan);
        Equal(1, read.Root.Children[1].GridCell!.Row);
        Equal("second", read.Root.Children[0].Focus!.Down);
        Equal("scope", read.Root.InputScopeId);
        Equal("refresh", read.Root.Shortcuts[0].ActionId);
        True(ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = 63 }).Count > 0, "Grid must be version-gated.");
        Equal(ProtocolConstants.ResponsiveGridVersion, new WidgetView(UI.ResponsiveGrid("adaptive", 100)).CreateSnapshot("old", 1).ProtocolVersion);
        var implicitGrid = new WidgetView(UI.Grid("implicit", [], [], UI.Text("One", "one").InGrid(), UI.Text("Overlap", "two").InGrid())).CreateSnapshot("implicit", 1);
        Equal(0, ViewSnapshotValidator.Validate(implicitGrid).Count);
        var omitted = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes("""
            {"protocolVersion":64,"sequence":1,"widgetInstanceId":"omitted","activeInputScopeId":"grid",
             "root":{"id":"grid","kind":"Grid","gridLayout":{},"children":[
               {"id":"text","kind":"Text","text":"Default cell","gridCell":{}}]}}
            """));
        Equal(0, omitted.Root.GridLayout!.Rows.Count);
        Equal(1, omitted.Root.Children[0].GridCell!.RowSpan);
        var defaultTrack = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes("""
            {"protocolVersion":64,"sequence":1,"widgetInstanceId":"omitted","activeInputScopeId":"grid",
             "quickActions":[],"pinnedLayouts":[],"root":{"id":"grid","kind":"Grid",
             "contextActions":[],"selectOptions":[],"styleClasses":[],"shortcuts":[],"children":[],
             "gridLayout":{"rows":[{"sizing":"Pixel"}],"columns":[{}]}}}
            """));
        Equal(1d, defaultTrack.Root.GridLayout!.Rows[0].Value);
        Equal(GridTrackSizing.Star, defaultTrack.Root.GridLayout.Columns[0].Sizing);
        return Task.CompletedTask;
    }

    public static Task InvalidDeclarations()
    {
        var valid = new WidgetView(UI.Grid("grid", [GridTrack.Star()], [GridTrack.Star()], UI.Text("Text", "text").InGrid())).CreateSnapshot("grid.widget", 1);
        void Reject(ViewNode root, string code) => True(ViewSnapshotValidator.Validate(valid with { Root = root }).Any(error => error.Code == code), code);
        void Track(GridTrackDefinition track, string code) => Reject(valid.Root with { GridLayout = new() { Rows = [track] } }, code);
        Reject(valid.Root with { GridLayout = new() { Rows = null! } }, "required");
        Reject(valid.Root with { GridLayout = new() { Rows = [null!] } }, "required");
        Reject(valid.Root with { GridLayout = new() { Columns = Enumerable.Repeat(GridTrack.Auto(), 65).ToArray() } }, "too_many_grid_tracks");
        Track(new() { Sizing = (GridTrackSizing)99 }, "invalid_grid_track_sizing");
        Track(new() { Sizing = GridTrackSizing.Auto, Value = 2 }, "invalid_grid_track_value");
        Track(new() { Sizing = GridTrackSizing.Pixel, Value = -1 }, "invalid_grid_track_value");
        Track(new() { Sizing = GridTrackSizing.Star, Value = 0 }, "invalid_grid_track_value");
        Track(new() { Value = double.NaN }, "invalid_grid_track_value");
        Track(new() { Minimum = double.PositiveInfinity }, "invalid_grid_length");
        Track(new() { Minimum = 20, Maximum = 10 }, "invalid_grid_track_bounds");
        Track(new() { Maximum = 16385 }, "invalid_grid_length");
        Reject(valid.Root with { GridLayout = new() { RowSpacing = -1 } }, "invalid_grid_length");
        Reject(valid.Root with { GridMinimumColumnWidth = 100 }, "mixed_grid_layout");
        Reject(valid.Root with { Kind = ViewNodeKind.Stack }, "grid_layout_not_allowed");
        Reject(valid.Root with { GridCell = new() }, "grid_cell_not_allowed");
        Reject(valid.Root with { Children = [valid.Root.Children[0] with { GridCell = new() { Row = 1 } }] }, "invalid_grid_cell");
        Reject(valid.Root with { Children = [valid.Root.Children[0] with { GridCell = new() { ColumnSpan = int.MaxValue } }] }, "invalid_grid_cell");
        Reject(valid.Root with { Children = [valid.Root.Children[0] with { GridCell = new() { RowSpan = 0 } }] }, "invalid_grid_cell");
        Reject(valid.Root with { GridLayout = null, GridMinimumColumnWidth = 100 }, "grid_cell_not_allowed");
        Reject(valid.Root with { Children = [new() { Kind = ViewNodeKind.Stack, Id = "nested", Children = valid.Root.Children }] }, "grid_cell_not_allowed");
        Throws<ArgumentOutOfRangeException>(() => GridTrack.Star(0));
        Throws<ArgumentOutOfRangeException>(() => GridTrack.Pixel(double.PositiveInfinity));
        Throws<ArgumentOutOfRangeException>(() => GridTrack.Auto(10, 5));
        Throws<ArgumentOutOfRangeException>(() => UI.Text("X", "x").InGrid(column: 63, columnSpan: 2));
        Throws<ArgumentOutOfRangeException>(() => UI.Grid("g", [], []).Spacing(double.NaN, 0));
        return Task.CompletedTask;
    }

    public static Task ImmutableAndComposed()
    {
        var rows = new[] { GridTrack.Pixel(40) };
        var columns = new List<GridTrackDefinition> { GridTrack.Star() };
        var declaration = new GridLayoutDefinition { Rows = rows, Columns = columns };
        rows[0] = GridTrack.Pixel(99); columns.Clear();
        Equal(40d, declaration.Rows[0].Value); Equal(1, declaration.Columns.Count);
        Throws<NotSupportedException>(() => ((IList<GridTrackDefinition>)declaration.Rows)[0] = GridTrack.Auto());
        var copied = declaration with { Rows = rows }; rows[0] = GridTrack.Pixel(88);
        Equal(99d, copied.Rows[0].Value);

        var button = UI.Button("Choice", "choose", "choice").PersistFocusAs("choice-memory")
            .Classes("author-choice").Busy().FocusBackground(new WidgetArtworkHandle("cover"))
            .PresentOnFocus(UI.Text("Detail", "detail")).TransitionLayout("choices", "one", 1)
            .VisibleWhen(ResponsiveVisibility.ExpandedOnly).InGrid().AddClasses("positioned");
        var page = new WidgetView(UI.FocusPresentationSurface(UI.Grid("grid", [], [], button),
            UI.Text("Default", "fallback"), "surface")).CreateSnapshot("grid.widget", 1);
        var node = page.Root.Children[0].Children[0];
        Equal("choose", node.ActionId); Equal("choice-memory", node.FocusPersistenceId);
        Equal(true, node.IsBusy); Equal("cover", node.FocusBackgroundArtworkHandle);
        Equal("detail", node.FocusPresentation!.Id); Equal("choices", node.Transition!.GroupId);
        Equal(ResponsiveVisibility.ExpandedOnly, node.VisibleWhen);
        True(node.StyleClasses.Contains("author-choice") && node.StyleClasses.Contains("positioned"), "Placement must preserve classes.");
        var reverse = new WidgetView(UI.Grid("grid", [GridTrack.Auto(), GridTrack.Star()], [],
            UI.Button("Choice", "choose", "choice").InGrid(1).VisibleWhen(ResponsiveVisibility.CompactOnly)
                .TransitionLayout("choices", "two", 2).InGrid(0))).CreateSnapshot("grid.widget", 1);
        Equal(0, reverse.Root.Children[0].GridCell!.Row);
        Equal(ResponsiveVisibility.CompactOnly, reverse.Root.Children[0].VisibleWhen);

        var pin = new WidgetView(UI.Text("Main", "main")) { PinnedLayouts = [WidgetView.PinnedLayout("pin", "Pin", new(), UI.Grid("pin-grid", [], []))] };
        Equal(64, pin.CreateSnapshot("pin.widget", 1).ProtocolVersion);
        var fragment = new WidgetView(UI.FocusPresentationSurface(UI.Stack("content"), UI.Grid("default-grid", [], [], UI.Text("Default", "default")), "surface"));
        Equal(64, fragment.CreateSnapshot("fragment.widget", 1).ProtocolVersion);
        return Task.CompletedTask;
    }

    public static Task IncrementalUpdates()
    {
        var before = new WidgetView(UI.Grid("grid", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star(), GridTrack.Star()],
            UI.Text("Text", "text").InGrid())).CreateSnapshot("grid.widget", 1);
        var after = before with { Sequence = 2, Root = before.Root with
        {
            GridLayout = before.Root.GridLayout! with { Columns = [GridTrack.Pixel(100), GridTrack.Star(3)], ColumnSpacing = 12 },
            Children = [before.Root.Children[0] with { GridCell = new() { Row = 1, Column = 1 } }],
        } };
        var update = WidgetPresentationDiff.Create(before, after, Generation, 1, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        True(update is not null, "Grid changes must use the atomic property path.");
        var applied = PresentationUpdateMaterializer.Apply(before, update!, Generation);
        Equal(SnapshotJson.Serialize(after), SnapshotJson.Serialize(applied));
        var reset = after with { Sequence = 3, Root = after.Root with { GridLayout = new(), Children = [after.Root.Children[0] with { GridCell = null }] } };
        update = WidgetPresentationDiff.Create(after, reset, Generation, 2, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Equal(SnapshotJson.Serialize(reset), SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(after, update!, Generation)));
        var adaptive = reset with { Sequence = 4, Root = reset.Root with { GridLayout = null, GridMinimumColumnWidth = 100 } };
        update = WidgetPresentationDiff.Create(reset, adaptive, Generation, 3, PresentationUpdateCapabilities.Current,
            WidgetPresentationTransactionKind.IncrementalUpdate).Update;
        Equal(SnapshotJson.Serialize(adaptive), SnapshotJson.Serialize(PresentationUpdateMaterializer.Apply(reset, update!, Generation)));
        var bad = update! with { Operations = [new() { Kind = PresentationUpdateOperationKind.SetProperties, TargetId = "text",
            Properties = [new(PresentationProperty.GridCell, JsonSerializer.SerializeToElement(new GridCellPlacement { Row = 2 }))] }] };
        Throws<ProtocolValidationException>(() => PresentationUpdateMaterializer.Apply(reset, bad, Generation));
        True(PresentationPropertyMetadata.Impact(PresentationProperty.GridLayout).HasFlag(PresentationPropertyImpact.MeasureLayout), "Track changes require layout.");
        return Task.CompletedTask;
    }

    public static async Task DeferredTemplates()
    {
        var widget = new IndexedFixture();
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        try
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "grid.indexed");
            Equal(64, host.CurrentSnapshot.ProtocolVersion);
            Equal(0, widget.RenderCount);
            using var lease = await host.AcquireAsync("items", 0, 1);
            Equal(1, widget.RenderCount);
            Equal(GridTrackSizing.Star, lease.Range.Items[0].Root.Children[0].GridLayout!.Columns[0].Sizing);
        }
        finally { await WidgetTestHost.DestroyAsync(widget); }
    }

    private sealed class IndexedFixture : Widget
    {
        private readonly WidgetIndexedCollection<string, string> source;
        public int RenderCount { get; private set; }
        public IndexedFixture() => source = CreateIndexedCollection<string, string>("source", "query", 1, new()
        {
            ReadRange = (_, _, _, _) => ValueTask.FromResult<IReadOnlyList<string>>(["one"]),
            ItemKey = value => new(value),
            RenderItem = (_, value, context) =>
            {
                ++RenderCount;
                return UI.ActionSurface("open", context.Id("row"), "Row", ActionSurfaceOrientation.Vertical,
                    UI.Grid(context.Id("grid"), [], [GridTrack.Star()], UI.Text(value, context.Id("text")).InGrid()));
            },
            OnAction = (_, _, _, _) => ValueTask.CompletedTask,
        });
        public override WidgetView Render() => new(UI.Stack("root", UI.CollectionList("items", source, 80, "Items")), "items");
    }

    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    {
        if (expected is byte[] left && actual is byte[] right && left.SequenceEqual(right)) return;
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
