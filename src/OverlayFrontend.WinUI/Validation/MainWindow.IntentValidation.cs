using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private async Task<IReadOnlyList<string>> ValidateWebIntentHandoffAsync(OverlayShellPage page)
    {
        var original = externalWebLauncher;
        var checks = new List<string>();
        var calls = 0;
        try
        {
            // This stage tests a main-window launch. Prior stages deliberately
            // activate and dispose other HWNDs; establish its own foreground
            // precondition instead of racing queued native activation events.
            ShowOverlay();
            await Task.Yield();
            var foregroundDeadline = Environment.TickCount64 + 2000;
            while (!page.HasForeground && Environment.TickCount64 < foregroundDeadline) await Task.Delay(20);
            Check(page.HasForeground, "Web handoff fixture owns main-window foreground");
            externalWebLauncher = _ => { calls++; return Task.FromResult(false); };
            Check(!await OpenExternalWebPageAsync(page, new Uri("file:///C:/not-a-web-page"), default) && calls == 0,
                "External fallback rejects non-web schemes before any launch");
            externalWebLauncher = _ =>
            {
                calls++;
                Check(taskHandoff is { MotionStarted: true } && overlayMotion?.Playback?.IsCompleted == true && AppWindow.IsVisible,
                    "Browser handoff waits for the ordinary close animation before launch");
                return Task.FromResult(false);
            };
            Check(!await OpenExternalWebPageAsync(page, new Uri("https://example.com"), default) && calls == 1, "Browser launch failure is reported after invoking the launcher");
            Check(overlayRequestedVisible && AppWindow.IsVisible && page.OverlayMotionValidationVisible,
                "Failed browser launch restores the still-owned overlay");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            externalWebLauncher = _ => { entered.TrySetResult(); return finish.Task; };
            var opening = OpenExternalWebPageAsync(page, new Uri("https://example.com"), default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            ShowOverlay();
            finish.TrySetResult(true);
            _ = await opening;
            Check(overlayRequestedVisible && AppWindow.IsVisible && page.OverlayMotionValidationVisible,
                "Late browser completion cannot hide a newly reopened overlay");
            externalWebLauncher = _ => Task.FromResult(true);
            Check(await OpenExternalWebPageAsync(page, new Uri("https://example.com"), default), "Successful browser handoff completes");
            Check(!AppWindow.IsVisible && !overlayRequestedVisible, "Successful browser handoff hides the overlay");
        }
        finally { externalWebLauncher = original; }
        return checks;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
    }
}
