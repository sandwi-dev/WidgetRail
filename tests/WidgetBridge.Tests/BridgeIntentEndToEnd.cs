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

    internal static async Task ServeAsync(string pipe)
    {
        await using var server = new WidgetBridgeServer(pipe, Catalog());
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
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
        Intents = handles ? new() { Handles = [new(WidgetIntentContracts.Web) { SupportsPassiveDelivery = true }] } : new() { Requests = [WidgetIntentContracts.Web] },
        PinningSupported = true, FullWidgetPinningSupported = true,
    };
    private static void Check(bool value) { if (!value) throw new Exception("Intent end-to-end assertion failed."); }
}

internal sealed class IntentBridgeProbeWidget(bool target) : Widget
{
    private string current = "No intent received";
    private int calls;
    public override WidgetView Render() => target
        ? new(UI.Stack("root", UI.Text(current, "result"), UI.Text("Deliveries: " + calls, "calls"), UI.Button("Destination action", "noop", "noop")), "noop")
        : new(UI.Stack("root", Link("open", "Open guide", "guide"), Link("wait", "Cancellable request", "wait"),
            Link("unsupported", "Unsupported guide", "unsupported"), Link("interaction", "Needs interaction", "interaction"),
            Link("open-full", "Open full widget", "guide") with { Intent = WidgetIntentRequest.Create(WidgetIntentContracts.Web,
                JsonSerializer.SerializeToElement(new { url = "https://example.com/guide" }), WidgetIntentPresentation.OpenWidget) }), "open");
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
