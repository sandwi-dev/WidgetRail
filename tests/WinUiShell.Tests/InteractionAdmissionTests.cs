using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class InteractionAdmissionTests
{
    [TestMethod]
    public async Task ExactCapturedActionWaitsForInteractiveAcknowledgment()
    {
        using var transitions = new SemaphoreSlim(1, 1);
        var admission = new InteractionAdmission(transitions);
        var owner = new object();
        var acknowledgment = new TaskCompletionSource();
        string focus = "poster-0";
        string? dispatched = null;
        async Task InvokeAsync()
        {
            var captured = focus;
            if (await admission.EnsureAsync(owner, () => true, _ => acknowledgment.Task, default))
                dispatched = captured;
        }
        var pending = InvokeAsync();
        Assert.IsNull(dispatched);
        Assert.IsFalse(pending.IsCompleted);
        focus = "poster-1";
        acknowledgment.SetResult();
        await pending;
        Assert.AreEqual("poster-0", dispatched);
    }

    [TestMethod]
    public async Task AwayAndBackDuringAcknowledgmentRevokesPendingInteraction()
    {
        using var transitions = new SemaphoreSlim(1, 1);
        var admission = new InteractionAdmission(transitions);
        var owner = new object();
        var acknowledgment = new TaskCompletionSource();
        var pending = admission.EnsureAsync(owner, () => true, _ => acknowledgment.Task, default);
        admission.Invalidate(); // Hide, tray, foreground loss, or widget selection.
        acknowledgment.SetResult();
        Assert.IsFalse(await pending);
        int acknowledgments = 0;
        Assert.IsTrue(await admission.EnsureAsync(owner, () => true, _ => { acknowledgments++; return Task.CompletedTask; }, default));
        Assert.AreEqual(1, acknowledgments);
    }

    [TestMethod]
    public async Task ScopeOrTargetRetirementDuringAcknowledgmentDoesNotDispatch()
    {
        using var transitions = new SemaphoreSlim(1, 1);
        var admission = new InteractionAdmission(transitions);
        var acknowledgment = new TaskCompletionSource();
        bool current = true;
        var pending = admission.EnsureAsync(new object(), () => current, _ => acknowledgment.Task, default);
        current = false;
        acknowledgment.SetResult();
        Assert.IsFalse(await pending);
    }

    [TestMethod]
    public async Task PendingLifecycleDowngradeFinishesBeforePromotion()
    {
        using var transitions = new SemaphoreSlim(0, 1);
        var admission = new InteractionAdmission(transitions);
        bool promoted = false;
        var pending = admission.EnsureAsync(new object(), () => true,
            _ => { promoted = true; return Task.CompletedTask; }, default);
        Assert.IsFalse(promoted);
        transitions.Release();
        Assert.IsTrue(await pending);
        Assert.IsTrue(promoted);
    }

    [TestMethod]
    public async Task SameAcknowledgedOwnerDoesNotAddIpcPerInput()
    {
        using var transitions = new SemaphoreSlim(1, 1);
        var admission = new InteractionAdmission(transitions);
        var owner = new object();
        int calls = 0;
        Task Establish(CancellationToken _) { calls++; return Task.CompletedTask; }
        Assert.IsTrue(await admission.EnsureAsync((owner, "generation-1"), () => true, Establish, default));
        Assert.IsTrue(await admission.EnsureAsync((owner, "generation-1"), () => true, Establish, default));
        Assert.AreEqual(1, calls);
        Assert.IsTrue(await admission.EnsureAsync((owner, "generation-2"), () => true, Establish, default));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task CanceledWaitNeverPromotesAndLeavesSerializerUsable()
    {
        using var transitions = new SemaphoreSlim(0, 1);
        using var cancellation = new CancellationTokenSource();
        var admission = new InteractionAdmission(transitions);
        bool promoted = false;
        var pending = admission.EnsureAsync(new object(), () => true,
            _ => { promoted = true; return Task.CompletedTask; }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
        Assert.IsFalse(promoted);
        transitions.Release();
        Assert.IsTrue(await admission.EnsureAsync(new object(), () => true, _ => Task.CompletedTask, default));
    }

    [TestMethod]
    public async Task RetiredWhileWaitingForEarlierTransitionNeverPromotes()
    {
        using var transitions = new SemaphoreSlim(0, 1);
        var admission = new InteractionAdmission(transitions);
        bool promoted = false;
        var pending = admission.EnsureAsync(new object(), () => true,
            _ => { promoted = true; return Task.CompletedTask; }, default);
        admission.Invalidate();
        transitions.Release();
        Assert.IsFalse(await pending);
        Assert.IsFalse(promoted);
    }

    [TestMethod]
    public async Task FailedAcknowledgmentDoesNotCacheSuccessOrHoldSerializer()
    {
        using var transitions = new SemaphoreSlim(1, 1);
        var admission = new InteractionAdmission(transitions);
        var owner = new object();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await admission.EnsureAsync(owner, () => true,
                _ => Task.FromException(new InvalidOperationException("retired worker")), default));
        bool promoted = false;
        Assert.IsTrue(await admission.EnsureAsync(owner, () => true,
            _ => { promoted = true; return Task.CompletedTask; }, default));
        Assert.IsTrue(promoted);
    }
}
