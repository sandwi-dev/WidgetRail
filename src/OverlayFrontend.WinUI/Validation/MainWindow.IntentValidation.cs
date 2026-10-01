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
            Check(!await OpenExternalWebPageAsync(page, new Uri("https://example.com"), default), "Browser launch failure is reported");
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
