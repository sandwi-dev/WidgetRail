using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

internal sealed class PinnedPresenterBridgeWidget : Widget
{
    private readonly WidgetIndexedCollection<int, int> rows;
    private string status = "ready";
    private int calls;
    private bool modal = true;
    private bool removed;
    internal PinnedPresenterBridgeWidget()
    {
        rows = CreateIndexedCollection<int, int>("pin-source", 0, 80, new()
        {
            ReadRange = async (_, start, count, token) => { await Task.Delay(25, token); return Enumerable.Range(start, count).ToArray(); },
            ItemKey = item => new("key." + item),
            RenderItem = (_, item, context) => UI.ActionSurface("row-open", context.Id("row"), "Pinned row " + item,
                ActionSurfaceOrientation.Horizontal, UI.Artwork(new("row-cover"), context.Id("cover"), "Checker artwork"),
                UI.Text("Pinned row " + item, context.Id("label"))).ContextMenuShortcut(ControllerButton.X).ContextAction("row-menu", "Row option"),
            OnAction = (_, item, action, _) => { status = action.ActionId + ":" + item; ++calls; Invalidate(); return ValueTask.CompletedTask; },
            ResolveArtwork = (_, _, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(Pixels),
        });
    }
    internal static WidgetEncodedArtwork Pixels => new(WidgetArtworkContentType.Png, Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAIUlEQVR4nGP4f83hPwiriJmAMTqfgaACXBIwPmEFg8ANADROkCEUK5WhAAAAAElFTkSuQmCC"));
    public override WidgetView Render()
    {
        var main = new WidgetView(UI.Stack("main", UI.Text(status, "status"), UI.Text(calls.ToString(), "calls"),
            UI.Button("Main action", "main-action", "open").Classes("main-action")), "open", ActiveInputScopeId: "main");
        if (modal) main = main.WithModal(new WidgetModal("dialog", "Main dialog", UI.Button("Close", "toggle", "close"), "close", "toggle"));
        return main with { PinnedLayouts = removed ? [] : [Layout("compact", false), Layout("alternate", true)] };
    }
    private PinnedPresentationLayout Layout(string id, bool alternate)
    {
        var root = UI.Stack("pin", UI.Row("commands",
                UI.Button(alternate ? "Alternative action" : "Pinned action", "open", "open").Classes(alternate ? "alternate-action" : "pin-action"),
                UI.Select("Choice", [new("first", "First", "select-first", IsSelected: true), new("second", "Second", "select-second")], "choice"),
                UI.TextEntry("", "Type a value", "commit", "entry", 32)),
            UI.Row("secondary", UI.Artwork(new("pin-cover"), "cover", "Checker artwork"),
                UI.Slider(2, 0, 10, 1, "volume", "volume", "Volume")),
            UI.Text(status, "pin-status"),
            UI.CollectionList("pin-items", rows, 58, "Pinned items"))
            .InputScope("pin.scope").Shortcut(ControllerButton.Y, "refresh", label: "Refresh")
            .ContextMenu(ControllerButton.Menu, new WidgetContextAction("menu", "Pinned option"));
        return WidgetView.PinnedLayout(id, id, new() { PreferredWidth = 640, PreferredHeight = 480 }, root, "open");
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId == "toggle") modal = !modal;
        else if (action.ActionId == "remove") removed = true;
        else if (action.ActionId == "restore") removed = false;
        else { ++calls; status = action.ActionId + ":" + (action.CommittedText ?? action.RequestedValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""); }
        Invalidate(); return ValueTask.CompletedTask;
    }
    public override ValueTask<WidgetEncodedArtwork?> OnResolveArtworkAsync(WidgetArtworkHandle handle, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<WidgetEncodedArtwork?>(Pixels);
    internal static async Task Serve(string pipe)
    {
        var theme = WrssThemeCompiler.Compile([WrssParser.Parse(
            "image { width: 40px; height: 40px; } .main-action { font-size: 12px; } .pin-action { font-size: 23px; } .alternate-action { font-size: 19px; }", "pinned-validation.wrss").Document]);
        if (!theme.IsValid) throw new InvalidOperationException("Pinned fixture theme invalid.");
        var configured = new ConfiguredWidget { Id = "pinned-presenter", Name = "Pinned presenter", InstanceId = "pinned-presenter.instance",
            PackageId = "dev.pinned", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new('a', 64),
            CatalogFingerprint = new('b', 64), PinningSupported = true, FullWidgetPinningSupported = true, CompiledTheme = theme.Theme };
        await using var server = new WidgetBridgeServer(pipe, new([configured]));
        await server.RunAsync(TimeSpan.FromSeconds(45), CancellationToken.None);
    }
}
