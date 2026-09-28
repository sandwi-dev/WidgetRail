using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

internal static class DiscoveredBridgeScenarios
{
    private static ConfiguredWidget Configuration => new()
    {
        Id = "discovered", Name = "Discovered results", InstanceId = "discovered.instance",
        PackageId = "dev.discovered", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
        WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64),
    };
    internal static async Task ServeAsync(string pipe)
    {
        var theme = WrssThemeCompiler.Compile([WrssParser.Parse(".fixture-root { height: 100%; min-height: 0px; gap: 8px; color: #111111; } .fixture-list { flex-grow: 1; min-height: 0px; width: 100%; } .fixture-row { min-height: 72px; padding: 8px; gap: 8px; background: #d9e8f8; color: #111111; flex-shrink: 0; } .fixture-toolbar { flex-shrink: 0; gap: 8px; } image { width: 48px; height: 48px; } text { font-size: 16px; color: #111111; } button { min-height: 32px; padding: 6px; color: #111111; }", "discovered-validation.wrss").Document]);
        if (!theme.IsValid) throw new InvalidOperationException("Discovered validation styles are invalid.");
        await using var server = new WidgetBridgeServer(pipe, new([Configuration with { CompiledTheme = theme.Theme }]));
        await server.RunAsync(TimeSpan.FromSeconds(60), CancellationToken.None);
    }
    internal static async Task EndToEnd()
    {
        var pipe = "discovered-test-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = new WidgetBridgeServer(pipe, new([Configuration]));
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        await session.ListWidgetsAsync(deadline.Token);
        var frame = await session.EstablishPresentationAsync(session.GetTarget("discovered"), WidgetLifecycleState.Interactive, deadline.Token);
        var empty = Descriptor(frame);
        Check(empty.Count == 0 && empty.Discovery is { HasMore: true }, "Initial extent must be an undiscovered prefix.");
        await session.ContinueDiscoveredCollectionAsync(frame.Authority, "items", empty, cancellationToken: deadline.Token);
        frame = await Settle(0, minimumRevision: empty.Discovery!.Revision + 1);
        await session.ContinueDiscoveredCollectionAsync(frame.Authority, "items", Descriptor(frame), cancellationToken: deadline.Token);
        frame = await Settle(16);
        var first = frame;
        await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "items", Descriptor(frame), 8, 1, cancellationToken: deadline.Token);
        await session.SendActionAsync(frame.Authority, new("delay-next", "delay-next", InputScopeId: "root"), deadline.Token);
        frame = await Status("delay-next");
        var appending = session.ContinueDiscoveredCollectionAsync(frame.Authority, "items", Descriptor(frame), cancellationToken: deadline.Token);
        await Settle(16, DiscoveredCollectionStatus.Loading);
        await lease.AdmitInputAsync(first, "item-8", ControllerButton.A, cancellationToken: deadline.Token);
        await Status("0:item-8");
        Check(!appending.IsCompleted, "Existing row actions must complete without waiting for the provider's continuation.");
        await appending;
        frame = await Settle(32);
        Check(lease.IsCurrent && Descriptor(frame).QueryGeneration == empty.QueryGeneration, "Append retired existing row authority.");
        Check(await lease.ResolveArtworkAsync("item-8", "cover", deadline.Token) is not null, "Old-prefix artwork lost exact lease authority.");
        Check(lease.ClaimsInput(first, "item-8", ControllerButton.A), "Displayed prefix failed input compatibility after append.");
        await lease.AdmitInputAsync(first, "item-8", ControllerButton.A, cancellationToken: deadline.Token);
        frame = await Status("0:item-8");
        await session.SendActionAsync(frame.Authority, new("fail-next", "fail-next", InputScopeId: "root"), deadline.Token);
        frame = await Status("fail-next");
        await session.ContinueDiscoveredCollectionAsync(frame.Authority, "items", Descriptor(frame), cancellationToken: deadline.Token);
        frame = await Settle(32, DiscoveredCollectionStatus.Failed);
        Check(lease.IsCurrent, "Loading failure retired existing prefix actions.");
        await session.ContinueDiscoveredCollectionAsync(frame.Authority, "items", Descriptor(frame), retry: true, cancellationToken: deadline.Token);
        frame = await Settle(48);
        await session.SendActionAsync(frame.Authority, new("replace", "replace", InputScopeId: "root"), deadline.Token);
        frame = await Settle(0);
        Check(!lease.IsCurrent && Descriptor(frame).QueryGeneration > empty.QueryGeneration, "Replacement retained old query authority.");
        await session.DisposeAsync(); await serving.WaitAsync(TimeSpan.FromSeconds(5));

        async Task<WidgetPresentationFrame> Settle(int count, DiscoveredCollectionStatus status = DiscoveredCollectionStatus.Ready, long minimumRevision = 0)
        {
            for (var i = 0; i < 200; ++i)
            {
                var current = session.GetState("discovered")!.LastGood!;
                if (Descriptor(current) is { } source && source.Count == count && source.Discovery?.Status == status && source.Discovery.Revision >= minimumRevision) return current;
                await Task.Delay(10, deadline.Token);
            }
            throw new TimeoutException("Discovery publication did not settle: " + count);
        }
        async Task<WidgetPresentationFrame> Status(string expected)
        {
            for (var i = 0; i < 200; ++i)
            {
                var current = session.GetState("discovered")!.LastGood!;
                if (current.Snapshot.Root.Children.First(node => node.Id == "status").Text == expected) return current;
                await Task.Delay(10, deadline.Token);
            }
            throw new TimeoutException("Captured discovery action did not arrive: " + expected);
        }
    }
    private static IndexedCollectionDescriptor Descriptor(WidgetPresentationFrame frame) => frame.Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

internal sealed class DiscoveredBridgeWidget : Widget
{
    private readonly WidgetDiscoveredCollection<int, string> source;
    private int query;
    private bool failNext;
    private bool delayNext;
    private bool duplicateGap;
    private bool emptyPages;
    private string status = "ready";
    private static readonly WidgetEncodedArtwork Artwork = new(WidgetArtworkContentType.Png,
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="));
    public DiscoveredBridgeWidget()
    {
        source = CreateDiscoveredCollection("source", 0, new WidgetDiscoveredCollectionOptions<int, string>
        {
            PageSize = 16, MaximumItems = 128, DuplicatePolicy = WidgetDiscoveredDuplicatePolicy.KeepFirst,
            LoadNext = async (_, token, size, cancellation) =>
            {
                await Task.Delay(60, cancellation);
                if (delayNext) { delayNext = false; await Task.Delay(800, cancellation); }
                if (failNext) { failNext = false; throw new IOException("synthetic provider failure"); }
                if (emptyPages) return new([], "empty" + ((token is null ? 0 : int.Parse(token.AsSpan(5))) + 1));
                if (token is null) { await Task.Delay(200, cancellation); return new([], "page-0"); }
                if (duplicateGap && token == "page-16")
                    return new(Enumerable.Range(0, 16).Select(index => "item-" + index).ToArray(), "next-16");
                var start = token is null ? 0 : int.Parse(token.AsSpan(5));
                return new(Enumerable.Range(start, Math.Min(size, 96 - start)).Select(index => "item-" + index).ToArray(),
                    start + size < 96 ? "page-" + (start + size) : null);
            },
            ItemKey = item => new(item),
            RenderItem = (_, item, context) => UI.ActionSurface("open", context.Id("row"), item,
                ActionSurfaceOrientation.Horizontal, UI.Artwork(new("cover"), context.Id("art"), "Artwork"), UI.Text(item, context.Id("text"))).Classes("fixture-row"),
            OnAction = (captured, item, _, _) => { if (captured == query) { status = captured + ":" + item; Invalidate(); } return ValueTask.CompletedTask; },
            ResolveArtwork = (_, _, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(Artwork),
        });
    }
    public override WidgetView Render() => new(UI.Stack("root", UI.Text(status, "status"),
        UI.Row("commands", UI.Button("Replace query", "replace", "replace"), UI.Button("Fail next load", "fail-next", "fail-next"),
            UI.Button("Delay next load", "delay-next", "delay-next"), UI.Button("Duplicate gap", "duplicate-gap", "duplicate-gap"),
            UI.Button("Empty pages", "empty-pages", "empty-pages")).Classes("fixture-toolbar"),
        UI.CollectionList("items", source, 72, "Discovered results").Classes("fixture-list")).Classes("fixture-root"), "items")
        { FocusGroupEntryRequest = source.Enter("items", query + 1) };
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId is "replace" or "duplicate-gap" or "empty-pages")
        {
            duplicateGap = action.ActionId == "duplicate-gap"; emptyPages = action.ActionId == "empty-pages";
            source.ReplaceQuery(++query); status = "replaced";
        }
        if (action.ActionId == "fail-next") { failNext = true; status = "fail-next"; }
        if (action.ActionId == "delay-next") { delayNext = true; status = "delay-next"; }
        Invalidate(); return ValueTask.CompletedTask;
    }
}
