using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.PlatformSettings;

internal static class BridgeIntentEndToEnd
{
    internal static async Task ThroughBothPipes()
    {
        var pipe = "intent-e2e-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = new WidgetBridgeServer(pipe, Catalog());
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        _ = await session.ListWidgetsAsync(deadline.Token);
        var source = await session.EstablishPresentationAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Interactive, deadline.Token);
        var action = new WidgetActionEvent("open", "open", ControllerButton.A, InputScopeId: "root");
        Check(session.HasIntentAction(source, action));
        var prepared = await session.PrepareIntentAsync(source, action, deadline.Token);
        Check(prepared.Kind == WidgetIntentLaunchKind.Widget && prepared.Destinations.Single().WidgetId == "intent-target");
        await session.SetLifecycleAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Background, deadline.Token);
        var target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        Check((await session.CommitIntentAsync(prepared, target, deadline.Token)).Accepted);
        target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        Check(target.Snapshot.Root.Children[0].Text == "https://example.com/guide");
        Check(!(await session.CommitIntentAsync(prepared, target, deadline.Token)).Accepted);

        // A new user activation can be cancelled while the receiver is working.
        source = await session.EstablishPresentationAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Interactive, deadline.Token);
        var waitAction = new WidgetActionEvent("wait", "wait", ControllerButton.A, InputScopeId: "root");
        prepared = await session.PrepareIntentAsync(source, waitAction, deadline.Token);
        await session.SetLifecycleAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Background, deadline.Token);
        target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var pending = session.CommitIntentAsync(prepared, target, cancel.Token);
        await Task.Delay(60, deadline.Token);
        cancel.Cancel();
        try { await pending; throw new Exception("Pending intent did not cancel."); } catch (OperationCanceledException) { }
        // Prove both transport readers remain usable after the cancellation reply.
        source = await session.EstablishPresentationAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Interactive, deadline.Token);
        prepared = await session.PrepareIntentAsync(source, action, deadline.Token);
        target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        Check((await session.CommitIntentAsync(prepared, target, deadline.Token)).Accepted);
        // A live selection can receive while visible, without making its worker interactive.
        source = await session.EstablishPresentationAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Interactive, deadline.Token);
        target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Visible, deadline.Token);
        var projection = session.ResolvePinnedProjection(target, WidgetPinnedProjection.FullWidgetLayoutId);
        var selection = await session.SelectPinnedLayoutAsync(projection, cancellationToken: deadline.Token);
        prepared = await session.PrepareIntentAsync(source, action, deadline.Token);
        Check((await session.CommitPinnedIntentAsync(prepared, selection, projection, deadline.Token)).Accepted);
        target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Visible, deadline.Token);
        projection = session.ResolvePinnedProjection(target, selection.LayoutId);
        prepared = await session.PrepareIntentAsync(source, new("interaction", "interaction", ControllerButton.A, InputScopeId: "root"), deadline.Token);
        var interaction = await session.CommitPinnedIntentAsync(prepared, selection, projection, deadline.Token);
        Check(interaction.Accepted && interaction.RequiresInteraction);
        var afterIntent = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Visible, deadline.Token);
        var calls = afterIntent.Snapshot.Root.Children[1].Text;
        target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        Check(target.Snapshot.Root.Children[1].Text == calls); // Showing state is not another delivery.
        prepared = await session.PrepareIntentAsync(source, action, deadline.Token);
        await session.ClearPinnedSelectionAsync(selection, target, cancellationToken: deadline.Token);
        try
        {
            await session.CommitPinnedIntentAsync(prepared, selection, projection, deadline.Token);
            throw new Exception("Retired pin accepted an intent.");
        }
        catch (WidgetPresentationSessionException error) when (error.Code == "pinned_input_stale") { }
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task SearchFeedbackThroughBothPipes()
    {
        var pipe = "intent-feedback-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = new WidgetBridgeServer(pipe, Catalog());
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        _ = await session.ListWidgetsAsync(deadline.Token);
        var source = await session.EstablishPresentationAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Interactive, deadline.Token);
        var prepared = await session.PrepareIntentAsync(source, new("search", "search", ControllerButton.A, InputScopeId: "root"), deadline.Token);
        Check(prepared.Kind == WidgetIntentLaunchKind.Widget && prepared.Destinations.Single().WidgetId == "intent-target");
        source = await session.EstablishPresentationAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Interactive, deadline.Token);
        Check(source.Snapshot.Root.Children.Single(node => node.Id == "feedback").Text == "Unavailable");
        await session.SetLifecycleAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Background, deadline.Token);
        var target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        Check(target.Snapshot.Root.Children.Single(node => node.Id == "readiness").Text == "Background");
        Check((await session.CommitIntentAsync(prepared, target, deadline.Token)).Accepted);
        target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        Check(target.Snapshot.Root.Children[0].Text == "https://example.com/search-fallback");
        source = await session.EstablishPresentationAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Interactive, deadline.Token);
        Check(source.Snapshot.Root.Children.Single(node => node.Id == "feedback").Text == "Accepted");
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task ServeAsync(string pipe)
    {
        await using var server = new WidgetBridgeServer(pipe, Catalog());
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }

    internal static async Task SourceProjections()
    {
        var pipe = "intent-sources-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = new WidgetBridgeServer(pipe, Catalog());
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        _ = await session.ListWidgetsAsync(deadline.Token);
        var source = await session.EstablishPresentationAsync(session.GetTarget("intent-source"), WidgetLifecycleState.Interactive, deadline.Token);
        var projection = session.ResolvePinnedProjection(source, "links-pin");
        var selection = await session.SelectPinnedLayoutAsync(projection, cancellationToken: deadline.Token);
        var action = new WidgetActionEvent("pin-open", "pin-open", ControllerButton.A, InputScopeId: "pin-scope");
        Check(!session.HasIntentAction(source, action));
        Check(session.HasPinnedIntentAction(selection, projection, action));
        var prepared = await session.PreparePinnedIntentAsync(selection, projection, action, deadline.Token);
        var target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        Check((await session.CommitIntentAsync(prepared, target, deadline.Token)).Accepted);
        target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
        Check(target.Snapshot.Root.Children[0].Text == "https://example.com/pinned");

        foreach (var inPin in new[] { false, true })
        {
            var parent = inPin ? projection.Snapshot : source.Snapshot;
            var collection = parent.Root.Children.Single(node => node.Id == (inPin ? "pin-links" : "links"));
            await using var lease = await session.AcquireIndexedRangeAsync(source.Authority, collection.Id, collection.IndexedCollection!,
                0, 2, inPin ? selection.LayoutId : null, deadline.Token);
            Check(session.ResolveIndexedIntentAction(lease, source, "link.1") is not null);
            prepared = await session.PrepareIndexedIntentAsync(lease, source, "link.1", deadline.Token);
            Check((await session.CommitIntentAsync(prepared, target, deadline.Token)).Accepted);
            target = await session.EstablishPresentationAsync(session.GetTarget("intent-target"), WidgetLifecycleState.Interactive, deadline.Token);
            Check(target.Snapshot.Root.Children[0].Text == "https://example.com/indexed/1");
            await lease.DisposeAsync();
            try
            {
                await session.PrepareIndexedIntentAsync(lease, source, "link.1", deadline.Token);
                throw new Exception("Retired item lease accepted an intent.");
            }
            catch (WidgetPresentationSessionException error) when (error.Code == "indexed_retired") { }
        }
        await session.ClearPinnedSelectionAsync(selection, source, cancellationToken: deadline.Token);
        try
        {
            await session.PreparePinnedIntentAsync(selection, projection, action, deadline.Token);
            throw new Exception("Retired pin accepted an intent source.");
        }
        catch (WidgetPresentationSessionException error) when (error.Code == "pinned_input_stale") { }
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task ServeOwnedFixtureAsync(string pipe, string settingsRoot)
    {
        var paths = new PlatformSettingsPaths(Path.GetFullPath(settingsRoot));
        var store = new PlatformSettingsStore(paths);
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        await using var server = new WidgetBridgeServer(pipe, Catalog(), appearance: appearance);
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }

    private static BridgeCatalog Catalog() => new([Entry("intent-source", false), Entry("intent-target", true)]);
    private static ConfiguredWidget Entry(string id, bool handles) => new()
    {
        Id = id, PackageId = "example." + id, PublisherId = "example.publisher", Name = handles ? "Intent destination" : "Intent source",
        InstanceId = id + ".instance", WorkerExecutable = Environment.ProcessPath!,
        WorkerFingerprint = new string(handles ? 'b' : 'a', 64), CatalogFingerprint = new string(handles ? 'b' : 'a', 64),
        Intents = handles ? new() { Handles = [new(WidgetIntentContracts.Web) { SupportsPassiveDelivery = true }, new(WidgetIntentContracts.VideoSearch) { HasDynamicAvailability = true }] } : new() { Requests = [WidgetIntentContracts.Web, WidgetIntentContracts.VideoSearch] },
        PinningSupported = true, FullWidgetPinningSupported = true,
    };
    private static void Check(bool value) { if (!value) throw new Exception("Intent end-to-end assertion failed."); }
}

internal sealed class IntentBridgeProbeWidget : Widget
{
    private readonly bool target;
    private readonly WidgetIndexedCollection<int, int> links;
    internal IntentBridgeProbeWidget(bool target)
    {
        this.target = target;
        links = CreateIndexedCollection<int, int>("intent-links", 0, 3, new()
        {
            ReadRange = (_, start, count, _) => ValueTask.FromResult<IReadOnlyList<int>>(Enumerable.Range(start, count).ToArray()),
            ItemKey = item => new("link." + item),
            RenderItem = (_, item, context) => Link(context.Id("link"), "Indexed guide " + item, "indexed/" + item),
            OnAction = (_, _, _, _) => ValueTask.FromException(new InvalidOperationException("Intent leaked into the widget action handler.")),
        });
    }
    private string current = "No intent received";
    private int calls;
    private string feedback = "None";
    private string readiness = "None";
    public override ValueTask<bool> IsIntentAvailableAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        readiness = LifecycleState.ToString();
        return ValueTask.FromResult(false);
    }
    public override ValueTask<bool> OnIntentCompletedAsync(WidgetIntentFeedback result, CancellationToken cancellationToken = default)
    {
        feedback = result.Status.ToString();
        Invalidate();
        return ValueTask.FromResult(true);
    }
    public override WidgetView Render() => target
        ? new(UI.Stack("root", UI.Text(current, "result"), UI.Text("Deliveries: " + calls, "calls"), UI.Button("Destination action", "noop", "noop"), UI.Text(readiness, "readiness")), "noop")
        : new(UI.Stack("root", Link("open", "Open guide", "guide"), Link("wait", "Cancellable request", "wait"),
            Link("unsupported", "Unsupported guide", "unsupported"), Link("interaction", "Needs interaction", "interaction"),
            Link("open-full", "Open full widget", "guide") with { Intent = WidgetIntentRequest.Create(WidgetIntentContracts.Web,
                JsonSerializer.SerializeToElement(new { url = "https://example.com/guide" }), WidgetIntentPresentation.OpenWidget) },
            UI.CollectionList("links", links, 52, "Guide links"),
            UI.Text(feedback, "feedback"), UI.Button("Search", "search", "search").OpenIntent(WidgetIntentContracts.VideoSearch,
                JsonSerializer.SerializeToElement(new { provider = "youtube", query = "boss guide" }))
                .WithIntentFeedback(WidgetIntentRequest.Create(WidgetIntentContracts.Web,
                    JsonSerializer.SerializeToElement(new { url = "https://example.com/search-fallback" })) with { ReportsResult = true })), "open")
        { PinnedLayouts = [WidgetView.PinnedLayout("links-pin", "Pinned links", new(),
            UI.Stack("pin-root", Link("pin-open", "Pinned guide", "pinned"), UI.CollectionList("pin-links", links, 52, "Pinned links")).InputScope("pin-scope"),
            initialFocusId: "pin-open", activeInputScopeId: "pin-scope")] };
    private static ButtonElement Link(string id, string label, string path) => UI.Button(label, id, id)
        .OpenIntent(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url = "https://example.com/" + path }));
    public override async ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        if (!target) return WidgetIntentResult.Rejected;
        calls++;
        var url = request.Payload.GetProperty("url").GetString()!;
        if (url.EndsWith("/unsupported", StringComparison.Ordinal)) return WidgetIntentResult.Rejected;
        if (url.EndsWith("/wait", StringComparison.Ordinal)) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        current = url;
        return url.EndsWith("/interaction", StringComparison.Ordinal) ? WidgetIntentResult.InteractionRequired : WidgetIntentResult.Accepted;
    }
}
