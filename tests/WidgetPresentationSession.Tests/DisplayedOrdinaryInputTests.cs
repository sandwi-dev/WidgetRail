using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class DisplayedOrdinaryInputTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private static BridgeWidgetDescriptor Descriptor => new() { Id = "ordinary", Name = "Ordinary", InstanceId = "ordinary.instance",
        RuntimeGeneration = new('a', 32), PresentationGeneration = new('b', 32), PackageContentDigest = new('c', 64), Icon = WidgetGlyph.Music };
    private static ViewNode Button => new() { Id = "open", Kind = ViewNodeKind.Button, Text = "Open", ActionId = "open-details" };
    private static ViewSnapshot Snapshot(long sequence, ViewNode? control = null, string scope = "root") => new()
    {
        Sequence = sequence, WidgetInstanceId = Descriptor.InstanceId, ActiveInputScopeId = scope,
        Root = new() { Id = scope, Kind = ViewNodeKind.Stack, Children = [control ?? Button,
            new() { Id = "status", Kind = ViewNodeKind.Text, Text = "Publication " + sequence }] },
    };
    private static WidgetActionEvent Action => new("open-details", "open", Sequence: 7, MonotonicTimestampMicroseconds: 1234, InputScopeId: "root") { FocusedElementId = "open" };
    private static ControllerInputEvent Input => new(ControllerButton.A, ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget,
        "open", 7, 1234, "root", 1);

    [TestMethod]
    public async Task AuthorityOnlyInputRejectsUnchangedControlAfterSnapshotDuringLifecycleAcknowledgment()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var lifecycle = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.SetWidgetLifecycle, lifecycle.Type);
            var refresh = await Read(channel);
            await ReplySnapshot(channel, refresh.RequestId, Snapshot(2));
            await ready.Task.WaitAsync(Limit);
            await Reply(channel, lifecycle.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, displayed) =>
        {
            var acknowledgment = session.SetLifecycleAsync(session.GetTarget("ordinary"), WidgetLifecycleState.Interactive);
            await session.RefreshAsync(displayed.Authority); ready.SetResult(); await acknowledgment;
            var actionError = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendActionAsync(displayed.Authority, Action));
            var inputError = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(displayed.Authority, Input));
            Assert.AreEqual("presentation_stale", actionError.Code);
            Assert.AreEqual("presentation_stale", inputError.Code);
        });
    }


    [TestMethod]
    public async Task DisplayedInputsSurviveLifecyclePublicationWithoutRewritingCapturedPayload()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var lifecycle = await Read(channel);
            var refresh = await Read(channel); await ReplySnapshot(channel, refresh.RequestId, Snapshot(2));
            await ready.Task.WaitAsync(Limit);
            await Reply(channel, lifecycle.RequestId, BridgeMessageTypes.Acknowledged, new { });
            var action = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.Action, action.Type);
            Assert.AreEqual(Action, action.Payload.GetProperty("action").Deserialize<WidgetActionEvent>(BridgeJson.Options));
            await Reply(channel, action.RequestId, BridgeMessageTypes.Acknowledged, new { admission = WidgetOperationAdmission.Enqueued });
            var input = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.ControllerInput, input.Type);
            Assert.AreEqual(Input, input.Payload.GetProperty("input").Deserialize<ControllerInputEvent>(BridgeJson.Options));
            Assert.AreEqual(Descriptor.RuntimeGeneration, input.Payload.GetProperty("runtimeGeneration").GetString());
            await Reply(channel, input.RequestId, BridgeMessageTypes.ControllerInputResult, new { handled = true });
        }, async (session, displayed) =>
        {
            var acknowledgment = session.SetLifecycleAsync(session.GetTarget("ordinary"), WidgetLifecycleState.Interactive);
            await session.RefreshAsync(displayed.Authority); ready.SetResult(); await acknowledgment;
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await session.SendActionAsync(displayed, Action));
            Assert.IsTrue(await session.SendControllerInputAsync(displayed, Input));
        });
    }

    [TestMethod]
    [DataRow("action")][DataRow("disabled")][DataRow("busy")][DataRow("kind")][DataRow("missing")][DataRow("scope")][DataRow("occurrence")]
    public async Task ChangedOriginTargetCannotAcquireNewAction(string change)
    {
        var next = change switch
        {
            "action" => Button with { ActionId = "install-instead" },
            "disabled" => Button with { IsDisabled = true }, "busy" => Button with { IsBusy = true },
            "kind" => Button with { Kind = ViewNodeKind.Text, ActionId = null },
            "missing" => Button with { Id = "different" }, "occurrence" => Button with { CollectionItemKey = "game-2" },
            _ => Button,
        };
        await Run(async channel =>
        {
            var request = await Read(channel); await ReplySnapshot(channel, request.RequestId, Snapshot(2, next, change == "scope" ? "other" : "root"));
        }, async (session, displayed) =>
        {
            await session.RefreshAsync(displayed.Authority);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendActionAsync(displayed, Action));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(displayed, Input));
        });
    }

    [TestMethod]
    public async Task FabricatedFrameAndMismatchedSequenceOrContextAreRejectedBeforeIpc()
    {
        await Run(_ => Task.CompletedTask, async (session, displayed) =>
        {
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendActionAsync(displayed with { }, Action));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(displayed with { }, Input));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(displayed, Input with { SnapshotSequence = 2 }));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(displayed, Input with { Context = ControllerInputContext.DashboardQuickAction }));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendActionAsync(displayed, Action with { ActionId = "undeclared" }));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => session.SendActionAsync(displayed, Action, cancellation.Token));
        });
    }

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task RetiredWorkerCannotUseOldDisplayedFrame(bool remove)
    {
        await Run(async channel =>
        {
            var request = await Read(channel);
            if (remove) await Reply(channel, request.RequestId, BridgeMessageTypes.Widgets, new { revision = 2, isComplete = true, widgets = Array.Empty<BridgeWidgetDescriptor>() });
            else await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { widgetId = "ordinary", state = WidgetLifecycleState.Interactive });
        }, async (session, displayed) =>
        {
            if (remove) await session.ListWidgetsAsync(); else await session.RestartAsync(session.GetTarget("ordinary"));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendActionAsync(displayed, Action));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(displayed, Input));
        });
    }

    [TestMethod]
    [DataRow("changed")][DataRow("added")][DataRow("disabled")]
    public async Task ShortcutAndRawOwnershipChangesAreRejected(string change)
    {
        var initial = Snapshot(1);
        if (change != "added") initial = Snapshot(1, Button with { Shortcuts = [new(ControllerButton.Y, "refresh")] });
        var next = Snapshot(2, Button with { Shortcuts = [new(ControllerButton.Y, change == "changed" ? "other-action" : "refresh")], IsDisabled = change == "disabled" });
        await Run(async channel => { var request = await Read(channel); await ReplySnapshot(channel, request.RequestId, next); }, async (session, displayed) =>
        {
            await session.RefreshAsync(displayed.Authority);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendControllerInputAsync(displayed, Input with { Button = ControllerButton.Y }));
        }, initial);
    }

    [TestMethod]
    public async Task UnchangedAncestorShortcutAndRawFallbackKeepOriginalFocusAndSequence()
    {
        var initial = Snapshot(1) with { Root = Snapshot(1).Root with { Shortcuts = [new(ControllerButton.Y, "refresh")] } };
        await Run(async channel =>
        {
            var request = await Read(channel); await ReplySnapshot(channel, request.RequestId, initial with { Sequence = 2 });
            for (int i = 0; i < 2; ++i)
            {
                request = await Read(channel);
                var input = request.Payload.GetProperty("input").Deserialize<ControllerInputEvent>(BridgeJson.Options)!;
                Assert.AreEqual(1L, input.SnapshotSequence); Assert.AreEqual("open", input.FocusedElementId);
                await Reply(channel, request.RequestId, BridgeMessageTypes.ControllerInputResult, new { handled = true });
            }
        }, async (session, displayed) =>
        {
            await session.RefreshAsync(displayed.Authority);
            Assert.IsTrue(await session.SendControllerInputAsync(displayed, Input with { Button = ControllerButton.Y }));
            Assert.IsTrue(await session.SendControllerInputAsync(displayed, Input with { Button = ControllerButton.Menu, Sequence = 8 }));
        }, initial);
    }

    [TestMethod]
    [DataRow("slider")][DataRow("text")][DataRow("select")][DataRow("context")]
    public async Task CommittedValuesAndPopupActionsRequireUnchangedSemanticContract(string kind)
    {
        var (node, action) = ComplexControl(kind);
        var changed = kind switch
        {
            "slider" => node with { Step = 2 },
            "text" => node with { TextEntryMaximumLength = 5 },
            "select" => node with { SelectOptions = [new("option", "Option", "choose", IsSelected: true, IsDisabled: true)] },
            _ => node with { ContextMenuButton = ControllerButton.Menu },
        };
        await Run(async channel => { var request = await Read(channel); await ReplySnapshot(channel, request.RequestId, Snapshot(2, changed)); }, async (session, displayed) =>
        {
            await session.RefreshAsync(displayed.Authority);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.SendActionAsync(displayed, action));
        }, Snapshot(1, node));
    }

    [TestMethod]
    [DataRow("slider")][DataRow("text")][DataRow("select")][DataRow("context")]
    public async Task UnchangedComplexControlSendsOriginalCommit(string kind)
    {
        var (node, action) = ComplexControl(kind);
        await Run(async channel =>
        {
            var request = await Read(channel); await ReplySnapshot(channel, request.RequestId, Snapshot(2, node));
            request = await Read(channel);
            Assert.AreEqual(action, request.Payload.GetProperty("action").Deserialize<WidgetActionEvent>(BridgeJson.Options));
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { admission = WidgetOperationAdmission.Enqueued });
        }, async (session, displayed) =>
        {
            await session.RefreshAsync(displayed.Authority);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await session.SendActionAsync(displayed, action));
        }, Snapshot(1, node));
    }

    private static (ViewNode, WidgetActionEvent) ComplexControl(string kind) => kind switch
    {
        "slider" => (new() { Id = "open", Kind = ViewNodeKind.Slider, Minimum = 0, Maximum = 10, Step = 1, Value = 2, ValueChangedActionId = "adjust", AccessibilityLabel = "Volume", AccessibilityValue = "2" }, Action with { ActionId = "adjust", RequestedValue = 3 }),
        "text" => (new() { Id = "open", Kind = ViewNodeKind.TextEntry, Text = "Value", TextEntryValue = "", TextEntryPlaceholder = "", ActionId = "commit", TextEntryMaximumLength = 20 }, Action with { ActionId = "commit", CommittedText = "captured" }),
        "select" => (new() { Id = "open", Kind = ViewNodeKind.Select, Text = "Options", AccessibilityValue = "Option", SelectOptions = [new("option", "Option", "choose", IsSelected: true)] }, Action with { ActionId = "choose", ControllerButton = ControllerButton.A }),
        _ => (new() { Id = "open", Kind = ViewNodeKind.Stack, ContextMenuButton = ControllerButton.X, ContextActions = [new("options", "Options")] }, Action with { ActionId = "options", ControllerButton = ControllerButton.X, FocusedElementId = null }),
    };

    private static async Task Run(Func<BridgeFrameChannel, Task> script,
        Func<WidgetPresentationSession, WidgetPresentationFrame, Task> exercise, ViewSnapshot? initial = null)
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var catalog = await Read(channel);
            await Reply(channel, catalog.RequestId, BridgeMessageTypes.Widgets, new { revision = 1, isComplete = true, widgets = new[] { Descriptor } });
            var establish = await Read(channel); await ReplySnapshot(channel, establish.RequestId, initial ?? Snapshot(1));
            await script(channel);
            var stop = await Read(channel); Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await Reply(channel, stop.RequestId, BridgeMessageTypes.Acknowledged, new { });
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            await session.ListWidgetsAsync();
            var establish = session.EstablishPresentationAsync(session.GetTarget("ordinary"), WidgetLifecycleState.Visible);
            if (await Task.WhenAny(establish, serving) == serving) await serving;
            var displayed = await establish.WaitAsync(Limit);
            var exercising = exercise(session, displayed);
            if (await Task.WhenAny(exercising, serving) == serving) await serving;
            await exercising.WaitAsync(Limit);
        }
        await serving.WaitAsync(Limit);
    }
    private static async Task ReplySnapshot(BridgeFrameChannel channel, long id, ViewSnapshot snapshot)
    {
        var errors = ViewSnapshotValidator.Validate(snapshot);
        Assert.AreEqual(0, errors.Count, string.Join("; ", errors.Select(error => error.Message)));
        using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await Reply(channel, id, BridgeMessageTypes.Snapshot, new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint",
            baseSequence = 0, recoveryOriginSequence = 0, snapshot = json.RootElement, renderStyles = new Dictionary<string, BridgeNodeRenderStyles>() });
    }
    private static Task<BridgeEnvelope> Read(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Limit);
    private static async Task Reply(BridgeFrameChannel channel, long id, string type, object value) =>
        await channel.WriteAsync(new() { Type = type, RequestId = id, Payload = BridgeJson.ToElement(value) }, CancellationToken.None);
}
