using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class BridgeIndexedEndToEndScenarios
{
    internal static async Task SessionToRealWorker()
    {
        var configured = new ConfiguredWidget
        {
            Id = "indexed-owned", Name = "Indexed ownership", InstanceId = "indexed-owned.instance",
            PackageId = "dev.indexed", PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64),
        };
        var pipe = "indexed-owned-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server = new WidgetBridgeServer(pipe, new([configured]));
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        _ = await session.ListWidgetsAsync(deadline.Token);
        var frame = await session.EstablishPresentationAsync(session.GetTarget(configured.Id), WidgetLifecycleState.Interactive, deadline.Token);
        var descriptor = frame.Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!;
        await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "items", descriptor, 7, 2, cancellationToken: deadline.Token);
        Check(lease.Range.Items.Select(item => item.Key).SequenceEqual(["item.7", "item.8"]), "Real worker range lost item identities.");
        Check(await lease.ResolveArtworkAsync("item.7", "cover", deadline.Token) is { ContentType: WidgetArtworkContentType.Png },
            "Row artwork did not cross both process protocols.");

        await Invoke(ControllerButton.A, "row:0:7:open");
        await Invoke(ControllerButton.X, "parent");
        Check(lease.IsCurrent, "Unrelated page updates retired row semantics.");
        await Invoke(ControllerButton.Y, "row:0:7:replace");
        Check(!lease.IsCurrent, "Query replacement failed to retire the captured row.");
        await lease.DisposeAsync();
        var next = frame.Snapshot.Root.Children.Single(node => node.Id == "items").IndexedCollection!;
        Check(next.QueryGeneration > descriptor.QueryGeneration, "Worker query did not change.");
        await using var replacement = await session.AcquireIndexedRangeAsync(frame.Authority, "items", next, 7, 2, cancellationToken: deadline.Token);
        Check(replacement.LeaseId != lease.LeaseId && replacement.IsCurrent, "Replacement range did not receive fresh ownership.");
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!replacement.IsCurrent, "Session shutdown retained remote row ownership.");

        async Task Invoke(ControllerButton button, string expected)
        {
            var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<WidgetPresentationInvalidatedEventArgs> handler = (_, _) => invalidated.TrySetResult();
            session.Invalidated += handler;
            try
            {
                Check(await lease.AdmitInputAsync(frame.Authority, "item.7", button, cancellationToken: deadline.Token) == WidgetOperationAdmission.Enqueued,
                    "Indexed input did not enter the existing worker queue.");
                await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(3));
                frame = await session.RefreshAsync(frame.Authority, deadline.Token);
                Check(frame.Snapshot.Root.Children.Single(node => node.Id == "status").Text == expected,
                    $"Indexed action {button} expected {expected}; observed {frame.Snapshot.Root.Children.Single(node => node.Id == "status").Text}.");
            }
            finally { session.Invalidated -= handler; }
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}

internal sealed class IndexedOwnedBridgeProbeWidget : Widget
{
    private readonly WidgetIndexedCollection<int, int> source;
    private string status = "initial";
    internal IndexedOwnedBridgeProbeWidget()
    {
        source = CreateIndexedCollection<int, int>("owned", 0, 100, new()
        {
            ReadRange = (_, start, count, _) => ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start, count).ToArray()),
            ItemKey = item => new("item." + item),
            RenderItem = (_, item, context) => UI.ActionSurface("open", context.Id("row"), $"Item {item}",
                ActionSurfaceOrientation.Horizontal, UI.Artwork(new("cover"), context.Id("cover"), "Cover"),
                UI.Text($"Item {item}", context.Id("title"))).Shortcut(ControllerButton.Y, actionId: "replace"),
            OnAction = (query, item, action, _) =>
            {
                Volatile.Write(ref status, $"row:{query}:{item}:{action.ActionId}");
                if (action.ActionId == "replace") source!.PublishQuery(query + 1, 100);
                Invalidate();
                return ValueTask.CompletedTask;
            },
            ResolveArtwork = (_, _, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png,
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="))),
        });
    }
    public override WidgetView Render() => new(UI.Stack("root", UI.Text(Volatile.Read(ref status), "status"),
        UI.Button("Parent", "parent", "parent"), UI.CollectionList("items", source, 64, "Items").Shortcut(ControllerButton.X, "parent")), "parent");
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    { Volatile.Write(ref status, action.ActionId); Invalidate(); return ValueTask.CompletedTask; }
}
