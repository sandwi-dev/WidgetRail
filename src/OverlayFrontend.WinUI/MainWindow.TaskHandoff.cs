using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WindowsWindowActivation;

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
            }, async () =>
            {
                handoff.Started = true;
                handoff.Version = ++overlayVisibilityVersion;
                overlayRequestedVisible = false;
                overlayOpenPending = false;
                ShellRoot.IsHitTestVisible = false;
                page.SetVisible(false, retainExitPresentation: true);
                // Keep native foreground/read ownership and XAML gamepad-key
                // consumption until release. Logical widget actions are retired.
                input?.TraceInput("Task handoff awaiting controller release");
                if (input is not null && !await input.WaitForHandoffReleaseAsync().WaitAsync(handoff.Cancellation.Token)) return false;
                if (!Current()) return false;
                input?.TraceInput("Task handoff controller release observed");
                handoff.MotionStarted = true;
                _ = PlayOverlayVisibilityAsync(handoff.Version, opening: false);
                await handoff.MotionFinished.Task.WaitAsync(handoff.Cancellation.Token);
                // A new press during the animation must not cross the boundary
                // either. Require the newest drained controller frame at commit.
                if (input is not null && !await input.WaitForHandoffReleaseAsync().WaitAsync(handoff.Cancellation.Token)) return false;
                if (!Current()) return false;
                input?.TraceInput("Task handoff motion and release ready");
                return true;
            }, handoff.Cancellation.Token);
        }
        catch (OperationCanceledException) when (handoff.Cancellation.IsCancellationRequested)
        { input?.TraceInput("Task handoff cancelled"); }
        finally
        {
            if (ReferenceEquals(taskHandoff, handoff))
            {
                taskHandoff = null;
                input?.CancelHandoffRelease();
                if (handoff.Started && !handoff.Committed && handoff.Version == overlayVisibilityVersion && !cleanupStarted && AppWindow.IsVisible)
                {
                    if (input?.IsForeground == false) HideOverlayImmediately();
                    else
                    {
                        // Restore the still-owned presentation on expiry/cancel;
                        // never show, activate or reopen a window that was hidden.
                        overlayRequestedVisible = true;
                        ++overlayVisibilityVersion;
                        overlayMotion?.Snap(true);
                        ShellRoot.IsHitTestVisible = true;
                        page.SetVisible(true);
                    }
                }
            }
        }
        bool Current() => !cleanupStarted && !handoff.Cancellation.IsCancellationRequested &&
            ReferenceEquals(taskHandoff, handoff) && handoff.Version == overlayVisibilityVersion &&
            !overlayRequestedVisible && AppWindow.IsVisible && input?.IsForeground != false;
    }

    private void CancelTaskHandoff()
    {
        taskHandoff?.Cancellation.Cancel();
        input?.CancelHandoffRelease();
    }
}
