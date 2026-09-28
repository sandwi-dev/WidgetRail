using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class PinnedPresentationSessionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private static BridgeWidgetDescriptor Descriptor => new() { Id = "pinned", Name = "Pinned", InstanceId = "pinned.instance",
        RuntimeGeneration = new('a', 32), PresentationGeneration = new('b', 32), PackageContentDigest = new('c', 64),
        Icon = WidgetGlyph.Music, PinningSupported = true, FullWidgetPinningSupported = true };
    private static ViewNode Button(string action) => new() { Id = "shared", Kind = ViewNodeKind.Button, Text = action, ActionId = action };
    private static PinnedPresentationLayout Layout(string id, ViewNode? control = null, string? scope = null) => new()
    {
        Id = id, Name = id, Surface = new(), ActiveInputScopeId = scope ?? id + ".scope",
        Root = new() { Id = id + ".root", InputScopeId = scope ?? id + ".scope", Kind = ViewNodeKind.Stack,
            Children = [control ?? Button(id + ".play")] },
    };
    private static ViewSnapshot Snapshot(long sequence, ViewNode? pinned = null, bool modal = false) => new()
    {
        Sequence = sequence, WidgetInstanceId = Descriptor.InstanceId, ProtocolVersion = ProtocolConstants.CurrentVersion,
        ActiveInputScopeId = modal ? "dialog" : "main",
        Root = modal ? new() { Id = "modal", Kind = ViewNodeKind.ModalLayer,
            Children = [new() { Id = "main", InputScopeId = "main", Kind = ViewNodeKind.Stack, Children = [Button("main.play")] },
                new() { Id = "dialog", InputScopeId = "dialog", Kind = ViewNodeKind.Stack, Children = [new() { Id = "close", Kind = ViewNodeKind.Button, Text = "Close", ActionId = "close" }] }] }
            : new() { Id = "main", Kind = ViewNodeKind.Stack, Children = [Button("main.play")] },
        PinnedLayouts = [Layout("compact", pinned), Layout("other")],
    };
    private static ControllerInputEvent Input(WidgetPinnedProjection projection, ControllerButton button = ControllerButton.A) =>
        new(button, ControllerEventPhase.Pressed, ControllerInputContext.PinnedSurface, "shared", 7, 100,
            projection.Snapshot.ActiveInputScopeId, projection.Frame.Authority.SnapshotSequence) { PinnedLayoutId = projection.LayoutId };

    [TestMethod]
    public async Task ProjectionUsesIndependentScopeAndNamespacedStylesAndRejectsCopiedFrames()
    {
        await Run(_ => Task.CompletedTask, (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact");
            Assert.AreEqual("compact.scope", projection.Snapshot.ActiveInputScopeId);
            Assert.AreEqual("compact.play", projection.Snapshot.Root.Children[0].ActionId);
            Assert.AreEqual("#00ff00", projection.RenderStyles["shared"].Base["color"].Text);
            Assert.IsFalse(projection.RenderStyles.ContainsKey("compact/shared"));
            Assert.AreEqual("#ff0000", session.ResolvePinnedProjection(frame, WidgetPinnedProjection.FullWidgetLayoutId).RenderStyles["shared"].Base["color"].Text);
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => session.ResolvePinnedProjection(frame with { }, "compact"));
            Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, BridgeNodeRenderStyles>)projection.RenderStyles).Clear());
            return Task.CompletedTask;
        }, styles: true);
    }

    [TestMethod]
    [DataRow(false, false, "compact")][DataRow(true, false, "host.full-widget")][DataRow(true, true, "unknown")]
    public async Task ProjectionRequiresManifestAndDeclaredLayout(bool pin, bool full, string id)
    {
        await Run(_ => Task.CompletedTask, (session, frame) =>
        {
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => session.ResolvePinnedProjection(frame, id));
            return Task.CompletedTask;
        }, descriptor: Descriptor with { PinningSupported = pin, FullWidgetPinningSupported = full });
    }

    [TestMethod]
    public async Task PinnedInputKeepsOriginAndOwnScopeAcrossMainModalPublication()
    {
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            var refresh = await Read(channel); await ReplySnapshot(channel, refresh.RequestId, Snapshot(2, modal: true));
            var request = await Read(channel);
            var wire = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
            Assert.AreEqual(Descriptor.RuntimeGeneration, wire.RuntimeGeneration);
            Assert.AreEqual("compact.play", wire.ExpectedActionId);
            Assert.AreEqual("compact.scope", wire.Input.ActiveInputScopeId);
            Assert.AreEqual(1L, wire.Input.SnapshotSequence);
            Assert.AreEqual("compact", wire.Input.PinnedLayoutId);
            await Handled(channel, request);
        }, async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact");
            var selection = await session.SelectPinnedLayoutAsync(projection);
            await session.RefreshAsync(frame.Authority);
            Assert.IsTrue(selection.IsCurrent);
            Assert.IsTrue(await session.SendPinnedControllerInputAsync(selection, projection, Input(projection)));
        });
    }

    [TestMethod]
    public async Task FullWidgetSelectionRevokesAuthoredHandleWithoutInventingAFullWidgetHandle()
    {
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            await Selection(channel, null, false, handled: false);
            var request = await Read(channel);
            var wire = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
            Assert.AreEqual("host.full-widget", wire.Input.PinnedLayoutId);
            Assert.AreEqual("main", wire.Input.ActiveInputScopeId);
            Assert.AreEqual("main.play", wire.ExpectedActionId);
            await Handled(channel, request);
        }, async (session, frame) =>
        {
            var compact = await session.SelectPinnedLayoutAsync(session.ResolvePinnedProjection(frame, "compact"));
            var full = session.ResolvePinnedProjection(frame, "host.full-widget");
            var selected = await session.SelectPinnedLayoutAsync(full);
            Assert.IsFalse(compact.IsCurrent); Assert.IsTrue(selected.IsCurrent); Assert.IsFalse(selected.DemandAcknowledged);
            Assert.AreEqual(ViewNodeKind.Stack, full.Snapshot.Root.Kind);
            Assert.IsTrue(await session.SendPinnedControllerInputAsync(selected, full, Input(full)));
        }, initial: Snapshot(1, modal: true));
    }

    [TestMethod]
    [DataRow("action")][DataRow("disabled")][DataRow("busy")][DataRow("kind")][DataRow("scope")]
    public async Task ChangedPinnedBindingRejectsCapturedInput(string change)
    {
        var control = change switch { "disabled" => Button("compact.play") with { IsDisabled = true },
            "busy" => Button("compact.play") with { IsBusy = true }, "kind" => new ViewNode { Id = "shared", Kind = ViewNodeKind.Text, Text = "Unavailable" },
            "action" => Button("compact.install"), _ => Button("compact.play") };
        var next = Snapshot(2, control);
        if (change == "scope") next = next with { PinnedLayouts = [Layout("compact", scope: "replacement"), Layout("other")] };
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            var refresh = await Read(channel); await ReplySnapshot(channel, refresh.RequestId, next);
        }, async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact");
            var selected = await session.SelectPinnedLayoutAsync(projection);
            await session.RefreshAsync(frame.Authority);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(selected, projection, Input(projection)));
        });
    }

    [TestMethod]
    public async Task RemovedAndReappearingLayoutNeverRevivesOldAuthority()
    {
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            var request = await Read(channel); await ReplySnapshot(channel, request.RequestId, Snapshot(2) with { PinnedLayouts = [] });
            request = await Read(channel); await ReplySnapshot(channel, request.RequestId, Snapshot(3));
        }, async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact");
            var selected = await session.SelectPinnedLayoutAsync(projection);
            var removed = await session.RefreshAsync(frame.Authority);
            var restored = await session.RefreshAsync(removed.Authority);
            Assert.IsFalse(selected.IsCurrent);
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => session.ResolvePinnedProjection(frame, "compact"));
            Assert.IsNotNull(session.ResolvePinnedProjection(restored, "compact"));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(selected, projection, Input(projection)));
        });
    }

    [TestMethod]
    public async Task NavigatingInsidePinnedLayoutKeepsSelectionButRequiresTheNewScope()
    {
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            var refresh = await Read(channel);
            await ReplySnapshot(channel, refresh.RequestId, Snapshot(2) with { PinnedLayouts = [Layout("compact", scope: "new.scope")] });
            var request = await Read(channel); var wire = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
            Assert.AreEqual("new.scope", wire.Input.ActiveInputScopeId); Assert.AreEqual(2L, wire.Input.SnapshotSequence);
            await Handled(channel, request);
        }, async (session, frame) =>
        {
            var prior = session.ResolvePinnedProjection(frame, "compact"); var selected = await session.SelectPinnedLayoutAsync(prior);
            var current = await session.RefreshAsync(frame.Authority);
            Assert.IsTrue(selected.IsCurrent);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(selected, prior, Input(prior)));
            var next = session.ResolvePinnedProjection(current, "compact");
            Assert.IsTrue(await session.SendPinnedControllerInputAsync(selected, next, Input(next)));
        });
    }

    [TestMethod]
    public async Task SelectionSwitchAndSameLayoutReselectionRetireCapturedSelectionAndCleanup()
    {
        await Run(async channel =>
        {
            await Selection(channel, "compact", true); await Selection(channel, "other", true);
            await Selection(channel, null, false); await Selection(channel, "compact", true);
        }, async (session, frame) =>
        {
            var compact = session.ResolvePinnedProjection(frame, "compact"); var other = session.ResolvePinnedProjection(frame, "other");
            var first = await session.SelectPinnedLayoutAsync(compact); var second = await session.SelectPinnedLayoutAsync(other);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(first, compact, Input(compact)));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(second, compact, Input(compact)));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ClearPinnedSelectionAsync(first, frame));
            Assert.IsTrue(await session.ClearPinnedSelectionAsync(second, frame));
            var again = await session.SelectPinnedLayoutAsync(compact);
            Assert.IsTrue(again.IsCurrent); Assert.IsFalse(first.IsCurrent); Assert.IsFalse(second.IsCurrent);
        });
    }

    [TestMethod]
    public async Task BackgroundRevokesInputAndVisibleLifecycleDoesNotResurrectSelection()
    {
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            for (int index = 0; index < 2; index++)
            { var lifecycle = await Read(channel); Assert.AreEqual(BridgeMessageTypes.SetWidgetLifecycle, lifecycle.Type); await Reply(channel, lifecycle.RequestId, BridgeMessageTypes.Acknowledged, new { }); }
        }, async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact");
            var selected = await session.SelectPinnedLayoutAsync(projection);
            await session.SetLifecycleAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Background);
            Assert.IsFalse(selected.IsCurrent);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SelectPinnedLayoutAsync(projection));
            await session.SetLifecycleAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Visible);
            Assert.IsFalse(selected.IsCurrent);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(selected, projection, Input(projection)));
        });
    }

    [TestMethod]
    public async Task BackgroundThenVisibleCannotActivateAnOutstandingSelection()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var selection = await Read(channel); entered.SetResult();
            for (int index = 0; index < 2; index++)
            {
                var lifecycle = await Read(channel);
                Assert.AreEqual(BridgeMessageTypes.SetWidgetLifecycle, lifecycle.Type);
                await Reply(channel, lifecycle.RequestId, BridgeMessageTypes.Acknowledged, new { });
            }
            await Handled(channel, selection);
        }, async (session, frame) =>
        {
            var pending = session.SelectPinnedLayoutAsync(session.ResolvePinnedProjection(frame, "compact"));
            await entered.Task.WaitAsync(Limit);
            var attempt = session.GetPinnedSelection(frame)!;
            await session.SetLifecycleAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Background);
            await session.SetLifecycleAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Visible);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => pending);
            Assert.IsFalse(attempt.IsCurrent);
            Assert.IsNull(session.GetPinnedSelection(frame));
        });
    }

    [TestMethod]
    public async Task CatalogRuntimeReplacementRetiresPinnedProjectionAndSelection()
    {
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            var list = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.ListWidgets, list.Type);
            await Reply(channel, list.RequestId, BridgeMessageTypes.Widgets,
                new { revision = 2, isComplete = true, widgets = new[] { Descriptor with { RuntimeGeneration = new string('d', 32) } } });
        }, async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact");
            var selection = await session.SelectPinnedLayoutAsync(projection);
            await session.ListWidgetsAsync();
            Assert.IsFalse(selection.IsCurrent);
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => session.ResolvePinnedProjection(frame, "compact"));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(selection, projection, Input(projection)));
        });
    }

    [TestMethod]
    [DataRow("layout")][DataRow("scope")][DataRow("snapshot")][DataRow("context")][DataRow("focus")][DataRow("selected")][DataRow("clock")]
    public async Task InvalidPinnedOriginAndOrdinaryRouteCannotBypassSelection(string change)
    {
        await Run(channel => Selection(channel, "compact", true), async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact");
            var selected = await session.SelectPinnedLayoutAsync(projection);
            var input = Input(projection);
            var invalid = change switch { "layout" => input with { PinnedLayoutId = "other" }, "scope" => input with { ActiveInputScopeId = "main" },
                "snapshot" => input with { SnapshotSequence = 2 }, "context" => input with { Context = ControllerInputContext.OpenWidget },
                "focus" => input with { FocusedElementId = "missing" }, "selected" => input with { IsPinnedLayoutSelected = true }, _ => input with { Sequence = -1 } };
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(selected, projection, invalid));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(frame.Authority, input));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(frame, input));
            Assert.IsFalse(await session.SendPinnedControllerInputAsync(selected, projection, Input(projection, ControllerButton.B)));
        });
    }

    [TestMethod]
    [DataRow("primary")][DataRow("slider")][DataRow("select")][DataRow("shortcut")]
    public async Task SupportedActionsUseExactPinnedControllerBinding(string kind)
    {
        var control = kind switch
        {
            "slider" => new ViewNode { Id = "shared", Kind = ViewNodeKind.Slider, Minimum = 0, Maximum = 10, Step = 1, Value = 2, ValueChangedActionId = "adjust", AccessibilityLabel = "Volume", AccessibilityValue = "2" },
            "select" => new() { Id = "shared", Kind = ViewNodeKind.Select, Text = "Choose", AccessibilityValue = "Option", SelectOptions = [new("option", "Option", "choose", IsSelected: true)] },
            "shortcut" => Button("compact.play") with { Shortcuts = [new(ControllerButton.X, "extra")] },
            _ => Button("compact.play"),
        };
        var actionId = kind switch { "slider" => "adjust", "select" => "choose", "shortcut" => "extra", _ => "compact.play" };
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            var request = await Read(channel); Assert.AreEqual(BridgeMessageTypes.ControllerInput, request.Type);
            var wire = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
            Assert.AreEqual(actionId, kind == "select" ? wire.ExpectedSelectOptionActionId : wire.ExpectedActionId);
            Assert.AreEqual("compact", wire.Input.PinnedLayoutId); Assert.AreEqual("compact.scope", wire.Input.ActiveInputScopeId);
            Assert.AreEqual(ControllerInputOrigin.AccessibilityAutomation, wire.Input.Origin);
            await Handled(channel, request);
        }, async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact"); var selected = await session.SelectPinnedLayoutAsync(projection);
            var action = new WidgetActionEvent(actionId, "shared", kind == "shortcut" ? ControllerButton.X : null,
                RequestedValue: kind == "slider" ? 3 : null, InputScopeId: "compact.scope") { FocusedElementId = "shared" };
            Assert.IsTrue(await session.SendPinnedActionAsync(selected, projection, action));
        }, initial: Snapshot(1, control));
    }

    [TestMethod]
    [DataRow("text")][DataRow("context")]
    public async Task UnsupportedOrdinaryCommitsNeverUseTheGenericActionEndpoint(string kind)
    {
        var node = kind == "text"
            ? new ViewNode { Id = "shared", Kind = ViewNodeKind.TextEntry, Text = "Name", TextEntryValue = "", TextEntryPlaceholder = "", TextEntryMaximumLength = 96, ActionId = "commit" }
            : new() { Id = "shared", Kind = ViewNodeKind.Stack, ContextMenuButton = ControllerButton.X, ContextActions = [new("menu", "Option")] };
        await Run(channel => Selection(channel, "compact", true), async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact"); var selected = await session.SelectPinnedLayoutAsync(projection);
            var action = new WidgetActionEvent(kind == "text" ? "commit" : "menu", "shared", InputScopeId: "compact.scope")
                { CommittedText = kind == "text" ? "Value" : null, FocusedElementId = "shared" };
            var error = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedActionAsync(selected, projection, action));
            Assert.AreEqual("pinned_action_unsupported", error.Code);
        }, initial: Snapshot(1, node));
    }

    [TestMethod]
    public async Task CancelledSelectionCannotPublishLateInputAuthorityAndCanBeExplicitlyCleared()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var request = await Read(channel); entered.SetResult();
            // Teardown is queued after cancellation. A catalog round trip can
            // pass, but the next pinned command cannot overtake this reply.
            var list = await Read(channel); Assert.AreEqual(BridgeMessageTypes.ListWidgets, list.Type);
            await Reply(channel, list.RequestId, BridgeMessageTypes.Widgets,
                new { revision = 1, isComplete = true, widgets = new[] { Descriptor } });
            await Handled(channel, request);
            await Selection(channel, null, false);
        }, async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact");
            using var cancel = new CancellationTokenSource();
            var selecting = session.SelectPinnedLayoutAsync(projection, cancellationToken: cancel.Token);
            await entered.Task.WaitAsync(Limit);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SelectPinnedLayoutAsync(projection));
            var attempt = session.GetPinnedSelection(frame)!; Assert.IsFalse(attempt.IsCurrent);
            cancel.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => selecting);
            Assert.IsFalse(attempt.IsCurrent);
            var clearing = session.ClearPinnedSelectionAsync(attempt, frame);
            await session.ListWidgetsAsync();
            Assert.IsTrue(await clearing);
            Assert.IsNull(session.GetPinnedSelection(frame));
        });
    }

    [TestMethod]
    public async Task PinnedContinuationCarriesLayoutAndScopeForACollectionAbsentFromTheMainRoot()
    {
        var source = new IndexedCollectionDescriptor("feed.source", 1, 0, 0)
            { Discovery = new(1, true, DiscoveredCollectionStatus.Ready, 100) };
        var node = new ViewNode { Id = "feed", Kind = ViewNodeKind.IndexedCollection, IndexedCollection = source,
            ScrollAxis = ScrollAxis.Vertical, CollectionLayout = new() { Kind = CollectionLayoutKind.List, EstimatedItemExtent = 60 }, AccessibilityLabel = "Feed" };
        await Run(async channel =>
        {
            await Selection(channel, "compact", true);
            var request = await Read(channel); Assert.AreEqual(BridgeMessageTypes.ReadIndexedRange, request.Type);
            var wire = BridgeJson.FromElement<BridgeIndexedRangeRequest>(request.Payload);
            Assert.AreEqual("compact", wire.Range.PinnedLayoutId); Assert.AreEqual(IndexedCollectionRequestKind.Continue, wire.Range.Kind);
            var range = new IndexedCollectionRange(Descriptor.InstanceId, "feed", source, "compact.scope", 0, wire.Range.DemandId, [], "compact");
            await Reply(channel, request.RequestId, BridgeMessageTypes.IndexedRange,
                new BridgeIndexedRangeResponse(wire.WidgetId, wire.InstanceId, wire.RuntimeGeneration, wire.PresentationGeneration, range));
        }, async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "compact"); var selected = await session.SelectPinnedLayoutAsync(projection);
            await session.ContinuePinnedDiscoveredCollectionAsync(selected, projection, "feed", source);
        }, initial: Snapshot(1, node));
    }

    [TestMethod]
    public async Task LegacySizingLayoutCanRenderButCannotAcquireAControllerRoute()
    {
        await Run(channel => Selection(channel, "legacy", true), async (session, frame) =>
        {
            var projection = session.ResolvePinnedProjection(frame, "legacy"); var selected = await session.SelectPinnedLayoutAsync(projection);
            Assert.AreEqual("main", projection.Snapshot.ActiveInputScopeId); Assert.IsFalse(projection.SupportsOrdinaryInput);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendPinnedControllerInputAsync(selected, projection, Input(projection)));
        }, initial: Snapshot(1) with { PinnedLayouts = [new() { Id = "legacy", Name = "Legacy", Surface = new() }] });
    }

    private static async Task Run(Func<BridgeFrameChannel, Task> script, Func<WidgetPresentationSession, WidgetPresentationFrame, Task> exercise,
        ViewSnapshot? initial = null, BridgeWidgetDescriptor? descriptor = null, bool styles = false)
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await Read(channel); await Reply(channel, list.RequestId, BridgeMessageTypes.Widgets,
                new { revision = 1, isComplete = true, widgets = new[] { descriptor ?? Descriptor } });
            var establish = await Read(channel); await ReplySnapshot(channel, establish.RequestId, initial ?? Snapshot(1), styles);
            await script(channel);
            var stop = await Read(channel); Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await Reply(channel, stop.RequestId, BridgeMessageTypes.Acknowledged, new { });
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            await session.ListWidgetsAsync();
            var start = session.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Visible);
            if (await Task.WhenAny(start, serving) == serving) await serving;
            var frame = await start.WaitAsync(Limit);
            var running = exercise(session, frame);
            if (await Task.WhenAny(running, serving) == serving) await serving;
            await running.WaitAsync(Limit);
        }
        await serving.WaitAsync(Limit);
    }
    private static async Task ReplySnapshot(BridgeFrameChannel channel, long requestId, ViewSnapshot snapshot, bool styles = false)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        Assert.AreEqual(0, errors.Count, string.Join("; ", errors.Select(error => error.Message)));
        var values = new Dictionary<string, BridgeNodeRenderStyles>();
        if (styles)
            foreach (var (id, color) in new[] { ("shared", "#ff0000"), ("compact/shared", "#00ff00"), ("other/shared", "#0000ff") })
                values[id] = new() { Base = new Dictionary<string, BridgeComputedStyleValue> { ["color"] = new() { Kind = WrssValueKind.Color, Text = color } }, Focused = new Dictionary<string, BridgeComputedStyleValue>(), Pressed = new Dictionary<string, BridgeComputedStyleValue>() };
        using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await Reply(channel, requestId, BridgeMessageTypes.Snapshot, new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint",
            baseSequence = 0, recoveryOriginSequence = 0, snapshot = json.RootElement, renderStyles = values });
    }
    private static async Task Selection(BridgeFrameChannel channel, string? id, bool selected, bool handled = true)
    {
        var request = await Read(channel); Assert.AreEqual(BridgeMessageTypes.ControllerInput, request.Type);
        var wire = BridgeJson.FromElement<BridgeControllerInputRequest>(request.Payload);
        Assert.AreEqual(ControllerInputContext.PinnedLayoutSelection, wire.Input.Context);
        Assert.AreEqual(id, wire.Input.PinnedLayoutId); Assert.AreEqual(selected, wire.Input.IsPinnedLayoutSelected);
        Assert.AreEqual(Descriptor.RuntimeGeneration, wire.RuntimeGeneration);
        await Handled(channel, request, handled);
    }
    private static Task Handled(BridgeFrameChannel channel, BridgeEnvelope request, bool handled = true) =>
        Reply(channel, request.RequestId, BridgeMessageTypes.ControllerInputResult, new { handled });
    private static Task<BridgeEnvelope> Read(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Limit);
    private static async Task Reply(BridgeFrameChannel channel, long id, string type, object value) =>
        await channel.WriteAsync(new() { Type = type, RequestId = id, Payload = BridgeJson.ToElement(value) }, CancellationToken.None);
}
