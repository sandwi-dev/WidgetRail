using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScenarioRunner = GameBarAlternative.WidgetSdk.WidgetScenario;

namespace GameBarAlternative.WidgetScenario.Tests;

[TestClass]
public sealed class ScenarioHelperTests
{
    [TestMethod]
    public async Task BasicConsumerSequencesLifecycleActionAndSemanticAssertions()
    {
        var widget = new CounterWidget();
        var scenario = new ScenarioRunner(
                "basic-action",
                new WidgetScenarioDefinition(
                    widget, new WidgetTestHostServicesBuilder().Build()))
            .Activate()
            .ExpectFocus("initial-focus", "increment")
            .ExpectText("initial-value", "value", "Count 0")
            .Action("increment", new WidgetActionEvent("increment", "increment"))
            .ExpectText("updated-value", "value", "Count 1")
            .Deactivate();

        var result = await scenario.RunAsync();

        Assert.IsTrue(result.Passed, Failure(result));
        CollectionAssert.AreEqual(
            new[] { "activate", "initial-focus", "initial-value", "increment", "updated-value", "deactivate" },
            result.Steps.Select(step => step.StepId).ToArray());
        Assert.IsNotNull(result.FinalSnapshot);
    }

    [TestMethod]
    public async Task DataConsumerControlsDelayedDeniedRevokedStaleAndInactiveWork()
    {
        var barrier = new WidgetScenarioOperationBarrier<WidgetCapabilityQuery,
            IReadOnlyList<WidgetMediaSession>>();
        var services = new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetMediaCapabilities.GetSessions, barrier.InvokeAsync)
            .Build();
        var widget = new DataWidget();
        WidgetScenarioOperationInvocation<WidgetCapabilityQuery,
            IReadOnlyList<WidgetMediaSession>>? stale = null;
        WidgetScenarioOperationInvocation<WidgetCapabilityQuery,
            IReadOnlyList<WidgetMediaSession>>? current = null;
        var scenario = new ScenarioRunner(
                "media-data",
                new WidgetScenarioDefinition(widget, services))
            .Activate()
            .Wait("initial-request", async token =>
                stale = await barrier.WaitForInvocationAsync(token))
            .ExpectBusy("initial-busy", "refresh", true)
            .Action("replace-load", new WidgetActionEvent("refresh", "refresh"))
            .Wait("replacement-request", async token =>
            {
                await stale!.CancellationObserved.WaitAsync(token);
                stale.Complete(Sessions("Stale Song"));
                await stale.HandlerCompleted.WaitAsync(token);
                current = await barrier.WaitForInvocationAsync(token);
                current.Fail(new WidgetCapabilityException(
                    "permission_denied", "Media permission was denied."));
                await current.HandlerCompleted.WaitAsync(token);
                await widget.LastLoad.Completion.WaitAsync(token);
            })
            .ExpectText("denied-state", "status", "permission_denied")
            .Action("retry-revoked", new WidgetActionEvent("refresh", "refresh"))
            .Wait("revoked-response", async token =>
            {
                current = await barrier.WaitForInvocationAsync(token);
                current.Fail(new WidgetCapabilityException(
                    "permission_revoked", "Media permission was revoked."));
                await current.HandlerCompleted.WaitAsync(token);
                await widget.LastLoad.Completion.WaitAsync(token);
            })
            .ExpectText("revoked-state", "status", "permission_revoked")
            .Action("retry-success", new WidgetActionEvent("refresh", "refresh"))
            .Wait("successful-response", async token =>
            {
                current = await barrier.WaitForInvocationAsync(token);
                current.Complete(Sessions("Current Song"));
                await current.HandlerCompleted.WaitAsync(token);
                await widget.LastLoad.Completion.WaitAsync(token);
            })
            .ExpectText("current-state", "status", "Current Song")
            .ExpectBusy("settled", "refresh", false)
            .Deactivate()
            .Action("inactive-refresh", new WidgetActionEvent("refresh", "refresh"))
            .ExpectIdle("no-work-after-deactivate", barrier);

        var result = await scenario.RunAsync();

        Assert.IsTrue(result.Passed, Failure(result));
        Assert.IsFalse(result.FinalSnapshot!.Root.Children
            .Any(node => node.Text == "Stale Song"));
        Assert.AreEqual(0, barrier.ActiveInvocations);
        Assert.AreEqual(3, widget.ActionCalls);
    }

    [TestMethod]
    public async Task SemanticFailureNamesExactStepAndMismatch()
    {
        var result = await new ScenarioRunner(
                "failure-name",
                new WidgetScenarioDefinition(
                    new CounterWidget(), new WidgetTestHostServicesBuilder().Build()))
            .Activate()
            .ExpectText("expected-copy", "value", "Count 99")
            .RunAsync();

        Assert.IsFalse(result.Passed);
        Assert.AreEqual("expected-copy", result.Steps[^1].StepId);
        Assert.AreEqual("semantic_mismatch", result.Steps[^1].Code);
        StringAssert.Contains(result.Steps[^1].Message!, "Count 99");
    }

    private static IReadOnlyList<WidgetMediaSession> Sessions(string title) =>
    [
        new("fixture", "Fixture", title, "Artist", WidgetMediaPlaybackStatus.Playing,
            0, 1000, 0, 1, true, true, true, true, true, true),
    ];

    private static string Failure(WidgetScenarioRunResult result) => string.Join(
        "; ", result.Steps.Where(step => !step.Passed)
            .Select(step => $"{step.StepId}:{step.Code}:{step.Message}"));

    private sealed class CounterWidget : Widget
    {
        private int _count;
        public override WidgetView Render() => new(
            UI.Stack("root", UI.Text($"Count {_count}", "value"),
                UI.Button("Increment", "increment", "increment")),
            InitialFocusId: "increment");
        public override ValueTask OnActionAsync(
            WidgetActionEvent action,
            CancellationToken cancellationToken = default)
        {
            if (action.ActionId == "increment") _count++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DataWidget : Widget
    {
        private string _status = "idle";
        internal WidgetOperationHandle LastLoad { get; private set; }
        internal int ActionCalls { get; private set; }

        protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
        {
            StartLoad();
            return ValueTask.CompletedTask;
        }

        public override ValueTask OnActionAsync(
            WidgetActionEvent action,
            CancellationToken cancellationToken = default)
        {
            if (action.ActionId == "refresh")
            {
                ActionCalls++;
                StartLoad();
            }
            return ValueTask.CompletedTask;
        }

        public override WidgetView Render() => new(UI.Stack("root",
            UI.Text(_status, "status"),
            UI.Button("Refresh", "refresh", "refresh").Busy(Operations.IsBusy("load"))));

        private void StartLoad() => LastLoad = Operations.RunLatest(
            "load",
            async context =>
            {
                try
                {
                    var sessions = await HostServices.Media.GetSessionsAsync(
                        context.CancellationToken);
                    if (context.IsCurrent) _status = sessions[0].Title;
                }
                catch (WidgetCapabilityException exception)
                {
                    if (context.IsCurrent) _status = exception.ErrorCode;
                }
            });
    }
}
