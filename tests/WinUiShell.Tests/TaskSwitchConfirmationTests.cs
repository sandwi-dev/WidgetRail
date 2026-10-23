using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.WindowsWindowActivation;

namespace WinUiShell.Tests;

[TestClass]
public sealed class TaskSwitchConfirmationTests
{
    private sealed class Scenario
    {
        internal long Now;
        internal int Launches, Reads;
        internal uint? InitialInput = 42;
        internal TaskSwitchObservation State = new(true, true, false, 42);
        internal WindowsTaskViewResult LaunchResult = WindowsTaskViewResult.Requested;
        internal Action<Scenario>? BeforeRead, AfterDelay;
        internal CancellationTokenSource Cancellation = new();
        internal Task<HostTaskActivationOutcome> Run(TaskWindowActivationResult result = TaskWindowActivationResult.Requested) =>
            TaskSwitchConfirmation.ConfirmAsync(result, InitialInput, () =>
            { ++Reads; BeforeRead?.Invoke(this); return State; }, () => { ++Launches; return LaunchResult; },
                Cancellation.Token, () => Now, (ms, _) => { Now += ms; AfterDelay?.Invoke(this); return Task.CompletedTask; });
    }

    [TestMethod]
    public async Task AcceptedButUnfocusedAndExplicitDenialBothWaitThenRequestTaskViewOnce()
    {
        foreach (var result in new[] { TaskWindowActivationResult.Requested, TaskWindowActivationResult.Denied })
        {
            var s = new Scenario();
            var outcome = await s.Run(result);
            Assert.AreEqual(250, s.Now);
            Assert.AreEqual(1, s.Launches);
            Assert.AreEqual(TaskWindowActivationResult.Denied, outcome.Activation);
            Assert.AreEqual(WindowsTaskViewResult.Requested, outcome.Fallback);
        }
    }

    [TestMethod]
    public async Task EarlyOrDeadlineActivationNeedsNoFallback()
    {
        foreach (var arrivesAt in new[] { 0, 100, 250 })
        {
            var s = new Scenario { BeforeRead = x => { if (x.Now >= arrivesAt) x.State = x.State with { TargetForeground = true }; } };
            var outcome = await s.Run();
            Assert.AreEqual(TaskWindowActivationResult.Requested, outcome.Activation);
            Assert.IsNull(outcome.Fallback);
            Assert.AreEqual(0, s.Launches);
        }
    }

    [TestMethod]
    public async Task FinalBoundaryRechecksLateActivationAndSupersededRequest()
    {
        foreach (var targetArrived in new[] { true, false })
        {
            var deadlineReads = 0;
            var s = new Scenario { BeforeRead = x =>
            {
                if (x.Now >= 250 && ++deadlineReads == 2)
                    x.State = targetArrived ? x.State with { TargetForeground = true } : x.State with { RequestCurrent = false };
            } };
            var result = await s.Run();
            Assert.AreEqual(0, s.Launches);
            Assert.AreEqual(targetArrived ? TaskWindowActivationResult.Requested : TaskWindowActivationResult.Rejected, result.Activation);
        }
    }

    [TestMethod]
    public async Task ReopeningRetiredTargetAndNewInputSuppressFallback()
    {
        foreach (var state in new[] {
            new TaskSwitchObservation(false, true, false, 42),
            new TaskSwitchObservation(true, false, false, 42),
            new TaskSwitchObservation(true, true, false, 43),
            new TaskSwitchObservation(true, true, false, null) })
        {
            var s = new Scenario { AfterDelay = x => x.State = state };
            var result = await s.Run();
            Assert.AreEqual(0, s.Launches);
            if (!state.TargetCurrent) Assert.IsNull(result.Fallback);
            else Assert.AreEqual(WindowsTaskViewResult.Cancelled, result.Fallback);
        }
    }

    [TestMethod]
    public async Task CancellationDuringWaitDoesNotSubmitShortcut()
    {
        var s = new Scenario { AfterDelay = x => x.Cancellation.Cancel() };
        await Assert.ThrowsAsync<OperationCanceledException>(() => s.Run());
        Assert.AreEqual(0, s.Launches);
    }

    [TestMethod]
    public async Task CancellationDuringFinalObservationStillPreventsSubmission()
    {
        var deadlineReads = 0;
        var s = new Scenario { BeforeRead = x => { if (x.Now >= 250 && ++deadlineReads == 2) x.Cancellation.Cancel(); } };
        await Assert.ThrowsAsync<OperationCanceledException>(() => s.Run());
        Assert.AreEqual(0, s.Launches);
    }

    [TestMethod]
    public async Task AdmissionRejectionDoesNotEvenStartConfirmation()
    {
        var s = new Scenario();
        Assert.AreEqual(TaskWindowActivationResult.Rejected, (await s.Run(TaskWindowActivationResult.Rejected)).Activation);
        Assert.AreEqual(0, s.Reads);
        Assert.AreEqual(0, s.Launches);
    }

    [TestMethod]
    public async Task MissingInputObservationNeverAuthorizesAutomaticShortcut()
    {
        var s = new Scenario { InitialInput = null };
        var result = await s.Run();
        Assert.AreEqual(TaskWindowActivationResult.Denied, result.Activation);
        Assert.IsNull(result.Fallback);
        Assert.AreEqual(0, s.Launches);
    }

    [TestMethod]
    public async Task HeldModifiersAndRejectedShortcutAreReportedWithoutRetry()
    {
        foreach (var status in new[] { WindowsTaskViewResult.KeyboardBusy, WindowsTaskViewResult.InputRejected })
        {
            var s = new Scenario { LaunchResult = status };
            Assert.AreEqual(status, (await s.Run()).Fallback);
            Assert.AreEqual(1, s.Launches);
        }
    }
}
