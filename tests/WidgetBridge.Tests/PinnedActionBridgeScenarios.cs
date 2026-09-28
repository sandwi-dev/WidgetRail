using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class PinnedActionBridgeScenarios
{
    private static ConfiguredWidget Configured => new()
    {
        Id = "pinned-actions", InstanceId = "pinned-actions.instance", Name = "Pinned actions", PackageId = "dev.pinned", PublisherId = "dev",
        WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new('a', 64), CatalogFingerprint = new('b', 64),
        PinningSupported = true, FullWidgetPinningSupported = true,
    };

    internal static async Task EndToEnd()
    {
        var configured = Configured;
        var pipe = "pinned-actions-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server = new WidgetBridgeServer(pipe, new([configured]));
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        await session.ListWidgetsAsync(deadline.Token);
        var frame = await session.EstablishPresentationAsync(session.GetTarget(configured.Id), WidgetLifecycleState.Interactive, deadline.Token);
        var projection = session.ResolvePinnedProjection(frame, "compact");
        var selection = await session.SelectPinnedLayoutAsync(projection, cancellationToken: deadline.Token);
        var image = await session.ResolvePinnedArtworkAsync(selection, projection, "pin.cover", deadline.Token);
        Check(image.EncodedBytes.Span.SequenceEqual(PinnedActionBridgeWidget.Pixels.Bytes.Span), "Pinned-only artwork did not cross bridge.");
        await Invoke("commit", "entry", "typed", "commit:typed");
        await Invoke("menu", "menu-owner", null, "menu:");
        // Same declared actions in a full-widget projection, with a main modal
        // open, must still target the parent scope and never the dialog.
        frame = session.GetState(configured.Id)!.LastGood!;
        projection = session.ResolvePinnedProjection(frame, WidgetPinnedProjection.FullWidgetLayoutId);
        selection = await session.SelectPinnedLayoutAsync(projection, cancellationToken: deadline.Token);
        await Invoke("main-menu", "main-owner", null, "main-menu:");
        await session.ClearPinnedSelectionAsync(selection, session.GetState(configured.Id)!.LastGood!, cancellationToken: deadline.Token);
        await session.DisposeAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));

        async Task Invoke(string action, string source, string? text, string expected)
        {
            frame = session.GetState(configured.Id)!.LastGood!;
            projection = session.ResolvePinnedProjection(frame, selection.LayoutId);
            var invalidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<WidgetPresentationInvalidatedEventArgs> handler = (_, _) => invalidation.TrySetResult();
            session.Invalidated += handler;
            try
            {
                Check(await session.SendPinnedActionAsync(selection, projection,
                    new(action, source, text is null ? ControllerButton.X : ControllerButton.A, InputScopeId: projection.Snapshot.ActiveInputScopeId)
                        { FocusedElementId = source == "menu-owner" ? "entry" : source, CommittedText = text }, cancellationToken: deadline.Token), "Pinned action was not admitted.");
                await invalidation.Task.WaitAsync(TimeSpan.FromSeconds(3));
                frame = await session.RefreshAsync(frame.Authority, deadline.Token);
                Check(frame.Snapshot.Root.Children[0].Children[0].Text == expected, "Pinned action was misrouted or lost its text.");
            }
            finally { session.Invalidated -= handler; }
        }
    }

    internal static async Task Authority()
    {
        var configured = Configured;
        var changed = false;
        await using var fixture = new RegistryFixture(new([configured]), configure: (_, client) =>
        {
            client.SnapshotFactory = seq => PinnedActionBridgeWidget.CreateView(changed).CreateSnapshot(configured.InstanceId, seq);
            client.ArtworkResolver = _ => PinnedActionBridgeWidget.Pixels;
        });
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive);
        var origin = (await fixture.GetSnapshotAsync(configured.Id)).Snapshot;
        var descriptor = configured.PublicDescriptor();
        var input = new PinnedActionInput(1, "compact", origin.Sequence, new("menu", "menu-owner", InputScopeId: "pin.scope"));
        var request = new BridgePinnedActionRequest(configured.Id, descriptor.InstanceId, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, input);
        using (var result = await fixture.Registry.AdmitPinnedActionAsync(request, default, default))
            Check(result.Value == WidgetOperationAdmission.Enqueued, "Declared pinned action rejected.");
        var art = new BridgePinnedArtworkRequest(1, configured.Id, descriptor.InstanceId, descriptor.RuntimeGeneration,
            descriptor.PresentationGeneration, "compact", origin.Sequence, "pin.scope", "pin.cover", "pin-demand");
        using (var result = await fixture.Registry.ResolveArtworkAsync(configured.Id, art.ArtworkHandle,
            descriptor.RuntimeGeneration, descriptor.PresentationGeneration, default, default, pinned: art))
            Check(result.Value.Artwork is not null, "Pinned artwork rejected.");
        await Reject(() => fixture.Registry.AdmitPinnedActionAsync(request with { Input = input with { Version = 2 } }, default, default));
        await Reject(() => fixture.Registry.AdmitPinnedActionAsync(request with { InstanceId = "other" }, default, default));
        await Reject(() => fixture.Registry.AdmitPinnedActionAsync(request with { RuntimeGeneration = new('d', 32) }, default, default));
        await Reject(() => fixture.Registry.AdmitPinnedActionAsync(request with { Input = input with { LayoutId = "other" } }, default, default));
        await Reject(() => fixture.Registry.AdmitPinnedActionAsync(request with { Input = input with { Action = input.Action with { InputScopeId = "dialog.scope" } } }, default, default));
        await Reject(() => fixture.Registry.ResolveArtworkAsync(configured.Id, "main.cover", descriptor.RuntimeGeneration,
            descriptor.PresentationGeneration, default, default, pinned: art with { ArtworkHandle = "main.cover" }));
        changed = true;
        _ = await fixture.GetSnapshotAsync(configured.Id);
        await Reject(() => fixture.Registry.AdmitPinnedActionAsync(request, default, default));
        var text = input with { Action = new("commit", "entry", InputScopeId: "pin.scope") { CommittedText = "Value" } };
        await Reject(() => fixture.Registry.AdmitPinnedActionAsync(request with { Input = text }, default, default));
        Check(fixture.Clients.Single().PinnedActions.Count == 1 && fixture.Clients.Single().ActionEvents.Count == 0,
            "Invalid pin action or generic action reached worker.");
        Check(BridgeRequestClassifier.Classify(new() { Type = BridgeMessageTypes.PinnedAction, RequestId = 1,
            Payload = BridgeJson.ToElement(request with { Input = input with { Version = 2 } }) }).Kind == BridgeRequestKind.Malformed,
            "Unknown pinned contract version admitted by classifier.");
    }
    internal static async Task WorkerAuthority()
    {
        var bridgeResult = BridgeJson.ToElement(new PinnedActionResult(null));
        var workerResult = WidgetRail.WidgetRuntime.RuntimeJson.ToElement(new PinnedActionResult(null));
        Check(bridgeResult.GetProperty("admission").ValueKind == System.Text.Json.JsonValueKind.Null &&
            workerResult.GetProperty("admission").ValueKind == System.Text.Json.JsonValueKind.Null,
            "Stale admission was omitted from a strict wire result.");
        var widget = new PinnedActionBridgeWidget();
        await widget.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, default);
        try
        {
            widget.RenderSnapshot("worker", 1);
            var valid = new PinnedActionInput(1, "compact", 1,
                new("commit", "entry", ControllerButton.A, InputScopeId: "pin.scope") { CommittedText = "Value", FocusedElementId = "entry" });
            Check(widget.AdmitPinnedAction(valid with { SnapshotSequence = 99 }) is null, "Worker accepted stale sequence.");
            Check(widget.AdmitPinnedAction(valid with { LayoutId = "missing" }) is null, "Worker accepted removed layout.");
            Check(widget.AdmitPinnedAction(valid with { Action = valid.Action with { CommittedText = new string('a', 33) } }) is null, "Worker accepted oversized declared text.");
            Check(widget.AdmitPinnedAction(valid with { Action = valid.Action with { InputScopeId = "dialog.scope" } }) is null, "Worker accepted foreign scope.");
            Check(widget.AdmitPinnedAction(valid with { Action = valid.Action with { ControllerButton = ControllerButton.X } }) is null, "Worker accepted invalid text trigger.");
            await Reject(() => Task.FromResult(widget.AdmitPinnedAction(valid with { Version = 2 })));
            await Reject(() => Task.FromResult(widget.AdmitPinnedAction(valid with { Action = valid.Action with { CommittedText = "line\nbreak" } })));
            Check(widget.AdmitPinnedAction(valid) == WidgetOperationAdmission.Enqueued, "Worker rejected valid text.");
            var context = new PinnedActionInput(1, "compact", 1,
                new("menu", "menu-owner", ControllerButton.X, InputScopeId: "pin.scope") { FocusedElementId = "entry" });
            Check(widget.AdmitPinnedAction(context) == WidgetOperationAdmission.Enqueued, "Container menu lost its independently focused source.");
            Check(widget.AdmitPinnedAction(context with { Action = context.Action with { ControllerButton = ControllerButton.Y } }) is null, "Worker accepted another menu trigger.");
            widget.RenderSnapshot("worker", 2);
            Check(widget.AdmitPinnedAction(context) is null, "Worker admitted the old snapshot after publication.");
        }
        finally { await widget.DestroyAsync(default); }
    }

    private static async Task Reject(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception error) when (error is BridgeProtocolException or BridgeStalePinnedInputAuthorityException or BridgeStaleArtworkAuthorityException or ArgumentException) { return; }
        throw new InvalidOperationException("Invalid pinned authority was accepted.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

internal sealed class PinnedActionBridgeWidget : Widget
{
    internal static readonly WidgetEncodedArtwork Pixels = new(WidgetArtworkContentType.Png,
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="));
    private string status = "ready";
    public override WidgetView Render() => CreateView(false, status);
    internal static WidgetView CreateView(bool changed, string status = "ready")
    {
        var view = new WidgetView(UI.Stack("main", UI.Text(status, "status"), UI.Artwork(new("main.cover"), "main-image", "Main cover")), ActiveInputScopeId: "main");
        var main = view.Root.ToProtocolNode() with { ContextMenuButton = ControllerButton.X, ContextActions = [new("main-menu", "Main")] };
        // The actual parent owner is a separate declared scope node, so its menu
        // cannot borrow the similarly named pinned action owner.
        view = new WidgetView(new ProtocolElement(main with { Id = "main-owner", InputScopeId = "main" }), ActiveInputScopeId: "main");
        return view.WithModal(new WidgetModal("dialog", "Dialog", UI.Button("Close", "close", "close"), "close", "close")) with
        {
            PinnedLayouts = [new() { Id = "compact", Name = "Compact", ActiveInputScopeId = "pin.scope", Surface = new(),
                Root = new() { Id = "pin", InputScopeId = "pin.scope", Kind = ViewNodeKind.Stack, Children = [
                    UI.TextEntry("", "Name", "commit", "entry", changed ? 16 : 32).ToProtocolNode(),
                    new() { Id = "menu-owner", Kind = ViewNodeKind.Stack, ContextMenuButton = ControllerButton.X,
                        ContextActions = [new(changed ? "changed-menu" : "menu", "Option")] },
                    new() { Id = "cover", Kind = ViewNodeKind.Image, ArtworkHandle = "pin.cover", ImageFit = ImageFit.Cover, AccessibilityLabel = "Cover" }] } }],
        };
    }
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    { status = action.ActionId + ":" + action.CommittedText; Invalidate(); return ValueTask.CompletedTask; }
    public override ValueTask<WidgetEncodedArtwork?> OnResolveArtworkAsync(WidgetArtworkHandle handle, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<WidgetEncodedArtwork?>(Pixels);
    private sealed record ProtocolElement(ViewNode Node) : WidgetElement(Node.Id)
    { internal override ViewNode ToProtocolNode() => Node; }
}
