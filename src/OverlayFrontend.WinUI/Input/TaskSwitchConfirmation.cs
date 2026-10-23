using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal readonly record struct HostTaskActivationOutcome(TaskWindowActivationResult Activation, WindowsTaskViewResult? Fallback = null);
internal readonly record struct TaskSwitchObservation(bool RequestCurrent, bool TargetCurrent, bool TargetForeground, uint? LastInputTick);

/// <summary>One post-hide confirmation window. No activation retry or overlay reopening.</summary>
internal static class TaskSwitchConfirmation
{
    internal const int ConfirmationMilliseconds = 250;

    internal static async Task<HostTaskActivationOutcome> ConfirmAsync(TaskWindowActivationResult activation,
        uint? inputAtHide, Func<TaskSwitchObservation> observe, Func<WindowsTaskViewResult> openTaskView,
        CancellationToken token, Func<long>? clock = null, Func<int, CancellationToken, Task>? delay = null)
    {
        // A request rejected by admission/identity/expiry checks must not gain a
        // different external effect merely because the normal switch was rejected.
        if (activation is not (TaskWindowActivationResult.Requested or TaskWindowActivationResult.Denied)) return new(activation);
        clock ??= () => Environment.TickCount64;
        delay ??= (milliseconds, cancellation) => Task.Delay(milliseconds, cancellation);
        var deadline = clock() + ConfirmationMilliseconds;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (Check(observe()) is { } finished) return finished;
            var remaining = deadline - clock();
            if (remaining <= 0) break;
            await delay((int)Math.Min(25, remaining), token);
        }
        // Recheck at the effect boundary, including activation exactly at the
        // deadline. Keep this synchronous with the single shortcut submission.
        token.ThrowIfCancellationRequested();
        if (Check(observe()) is { } final) return final;
        if (inputAtHide is null) return new(TaskWindowActivationResult.Denied);
        token.ThrowIfCancellationRequested();
        return new(TaskWindowActivationResult.Denied, openTaskView());

        HostTaskActivationOutcome? Check(TaskSwitchObservation state)
        {
            if (!state.RequestCurrent)
                return new(TaskWindowActivationResult.Rejected, WindowsTaskViewResult.Cancelled);
            if (!state.TargetCurrent) return new(TaskWindowActivationResult.Rejected);
            if (state.TargetForeground) return new(TaskWindowActivationResult.Requested);
            if (inputAtHide is { } expected && state.LastInputTick != expected)
                return new(TaskWindowActivationResult.Denied, WindowsTaskViewResult.Cancelled);
            return null;
        }
    }
}
