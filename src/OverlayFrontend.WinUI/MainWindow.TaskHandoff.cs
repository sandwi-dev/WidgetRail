using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WindowsWindowActivation;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private sealed class TaskHandoff : IDisposable
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly TaskCompletionSource MotionFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long Version;
        internal bool Started, MotionStarted, Committed;
        public void Dispose() => Cancellation.Dispose();
    }
    private TaskHandoff? taskHandoff;

    private Func<OverlayShellPage, HostTaskActivationRequest, CancellationToken, Task<WidgetTaskActivationResult>> hostTaskActivator =
        static (page, request, token) => page.CompleteHostTaskActivationAsync(request, token);

    private async Task<HostTaskActivationOutcome> RunHostTaskHandoffAsync(OverlayShellPage page, HostTaskActivationRequest request)
    {
        if (cleanupStarted || taskHandoff is not null) return new(TaskWindowActivationResult.Rejected);
        using var handoff = new TaskHandoff();
        taskHandoff = handoff;
        handoff.Cancellation.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(request.InitiatedAt + 2000 - Environment.TickCount64, 1, 2000)));
        void Trace(string message)
        { input?.TraceInput("Host task handoff " + message); page.WindowActivationDiagnostic?.Invoke("Host task handoff " + message); }
        var native = new WindowsTaskWindowActivation(Trace);
        uint? inputAtHide = null;
        try
        {
            // Preserve the established permission refresh -> hide -> broker
            // activation sequence. HWND ownership alone is not an activation grant.
            var result = await new TaskWindowActivation(native).ExecuteAsync(request.Target, request.InitiatedAt,
                request.VisibleSince, () => !cleanupStarted && !handoff.Cancellation.IsCancellationRequested && request.IsCurrent(),
                () =>
                {
                    handoff.Committed = true;
                    var observation = WindowsTaskWindowActivation.Observe(0);
                    inputAtHide = observation.LastInputAvailable ? observation.LastInputTick : null;
                    input?.SetVisible(false);
                    CompleteOverlayHide();
                }, async () =>
                {
                    var completed = await hostTaskActivator(page, request, handoff.Cancellation.Token);
                    Trace($"brokerPid={completed.ProcessId} code={completed.Code} {completed.NativeTrace}");
                    return completed.Result switch { "Requested" => TaskWindowActivationResult.Requested,
                        "Denied" => TaskWindowActivationResult.Denied, _ => TaskWindowActivationResult.Rejected };
                }, Trace, async () =>
                {
                    if (!await PrepareOverlayHandoffAsync(page, handoff)) return false;
                    // Match widget activation: refreshing the grant is best effort.
                    // Its failure does not prove the broker lacks an existing grant.
                    // Only the broker's activation result and observed foreground
                    // determine success; all identity/expiry checks still apply.
                    page.RefreshHostTaskActivationPermission();
                    return true;
                });
            var outcome = await TaskSwitchConfirmation.ConfirmAsync(result, inputAtHide, () =>
            {
                var current = !cleanupStarted && handoff.Committed && ReferenceEquals(taskHandoff, handoff) &&
                    handoff.Version == overlayVisibilityVersion && !overlayRequestedVisible && !AppWindow.IsVisible && request.IsCurrent();
                var observation = WindowsTaskWindowActivation.Observe(0);
                return new(current, native.IsCurrent(request.Target), native.IsTargetForeground(request.Target),
                    observation.LastInputAvailable ? observation.LastInputTick : null);
            }, systemTaskViewLauncher, handoff.Cancellation.Token);
            Trace($"result={outcome.Activation} taskViewFallback={outcome.Fallback?.ToString() ?? "none"}");
            return outcome;
        }
        catch (OperationCanceledException) when (handoff.Cancellation.IsCancellationRequested)
        { Trace("cancelled"); return new(TaskWindowActivationResult.Rejected); }
        finally { FinishOverlayHandoff(page, handoff); }
    }

    private async Task RunTaskHandoffAsync(OverlayShellPage page, WidgetPresentationHostEffect effect, TaskWindowActivation activation)
    {
        if (cleanupStarted || taskHandoff is not null) return;
        using var handoff = new TaskHandoff();
        taskHandoff = handoff;
        handoff.Cancellation.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(
            effect.InitiatedAtMilliseconds + 2000 - Environment.TickCount64, 1, 2000)));
        try
        {
            await page.ActivateTaskWindowAsync(effect, activation, () =>
            {
                // The activation coordinator revalidates foreground, exact target,
                // authority and expiry after preparation and again after hiding.
                handoff.Committed = true;
                input?.SetVisible(false);
                CompleteOverlayHide();
            }, () => PrepareOverlayHandoffAsync(page, handoff), handoff.Cancellation.Token);
        }
        catch (OperationCanceledException) when (handoff.Cancellation.IsCancellationRequested)
        { input?.TraceInput("Task handoff cancelled"); }
        finally
        {
            FinishOverlayHandoff(page, handoff);
        }
    }

    private async Task<bool> PrepareOverlayHandoffAsync(OverlayShellPage page, TaskHandoff handoff)
    {
        mouseSession?.End();
        handoff.Started = true;
        handoff.Version = ++overlayVisibilityVersion;
        overlayRequestedVisible = false;
        overlayOpenPending = false;
        ShellRoot.IsHitTestVisible = false;
        page.SetVisible(false, retainExitPresentation: true);
        // Retain native input ownership until both the initiating gesture and
        // any new press during closing have been released.
        input?.TraceInput("Task handoff awaiting controller release");
        if (input is not null && !await input.WaitForHandoffReleaseAsync().WaitAsync(handoff.Cancellation.Token)) return false;
        if (!HandoffCurrent(handoff)) return false;
        input?.TraceInput("Task handoff controller release observed");
        handoff.MotionStarted = true;
        _ = PlayOverlayVisibilityAsync(handoff.Version, opening: false);
        await handoff.MotionFinished.Task.WaitAsync(handoff.Cancellation.Token);
        if (input is not null && !await input.WaitForHandoffReleaseAsync().WaitAsync(handoff.Cancellation.Token)) return false;
        if (!HandoffCurrent(handoff)) return false;
        // Both release waits have completed. The shell may have suppressed the
        // release frame while retaining its closing presentation.
        page.CompleteControllerHandoffRelease();
        input?.TraceInput("Task handoff motion and release ready");
        return true;
    }

    private bool HandoffCurrent(TaskHandoff handoff) => !cleanupStarted && !handoff.Cancellation.IsCancellationRequested &&
        ReferenceEquals(taskHandoff, handoff) && handoff.Version == overlayVisibilityVersion &&
        !overlayRequestedVisible && AppWindow.IsVisible && input?.IsForeground != false;

    private void FinishOverlayHandoff(OverlayShellPage page, TaskHandoff handoff)
    {
        if (!ReferenceEquals(taskHandoff, handoff)) return;
        taskHandoff = null;
        input?.CancelHandoffRelease();
        if (handoff.Started && !handoff.Committed && handoff.Version == overlayVisibilityVersion && !cleanupStarted && AppWindow.IsVisible)
        {
            if (input?.IsForeground == false) HideOverlayImmediately();
            else
            {
                overlayRequestedVisible = true;
                ++overlayVisibilityVersion;
                overlayMotion?.Snap(true);
                ShellRoot.IsHitTestVisible = true;
                input?.SetVisible(true);
                page.SetVisible(true);
            }
        }
    }

    private void CancelTaskHandoff()
    {
        if (taskHandoff is not null && RootFrame.Content is OverlayShellPage page) page.CancelPendingHostTaskActivation();
        taskHandoff?.Cancellation.Cancel();
        input?.CancelHandoffRelease();
    }
}
