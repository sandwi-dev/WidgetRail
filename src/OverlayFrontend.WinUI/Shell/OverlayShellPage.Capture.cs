using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal Func<CancellationToken, Task<bool>>? HideForCaptureRequested { get; set; }
    private Task? capturePump, capturing;
    private async Task PumpCaptureAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(350));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (capturing is { IsCompleted: false }) continue;
                try
                {
                    var next = await owner!.Session.TakeCaptureAsync(lifetime.Token);
                    if (next.Request is not null) capturing = RunCaptureAsync(next);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-pump", error); }
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-pump", error); }
    }
    private async Task RunCaptureAsync(BridgeCaptureTake next)
    {
        var request = next.Request!;
        var session = owner!.Session;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(2));
        var token = cancellation.Token;
        Task? renewal = null;
        var committed = false;
        try
        {
            if (retired || !visible || !foreground || !interactive || activeWidget != next.WidgetId || HostChoiceActive || LocalInstallActive)
                throw new OperationCanceledException();
            var video = request.Kind == WindowCaptureKind.Video;
            var selected = await ShowHostChoiceAsync(video ? "Record game context?" : "Capture game context?",
                "WidgetRail will hide. Bring the application you want to capture to the foreground.\n\n" +
                (video ? "Recording starts after a 5-second countdown and lasts 5 seconds. No audio will be recorded." :
                    "A screenshot will be taken after a 5-second countdown.") +
                " The application in front when the countdown ends will be captured." +
                "\n\nReopen WidgetRail when finished to preview and send the capture.", (HostDialogChoice[])[new("proceed", "Proceed")], token);
            if (selected != "proceed" || HideForCaptureRequested is null || !await session.IsCaptureCurrentAsync(request.RequestId, token) ||
                !await HideForCaptureRequested(token)) throw new OperationCanceledException();
            renewal = RenewAsync();
            CaptureIndicator? indicator = null;
            try
            {
                try { indicator = new(ShellPalette, Appearance, systemUi.AnimationsEnabled); }
                catch (InvalidOperationException) { /* Countdown availability is platform-dependent; confirmation states the delay. */ }
                for (var seconds = CaptureLimits.PreparationSeconds; seconds > 0; --seconds)
                {
                    token.ThrowIfCancellationRequested();
                    if (visible) throw new OperationCanceledException();
                    indicator?.Show((video ? "Recording starts in " : "Screenshot in ") + seconds + "…");
                    await Task.Delay(1000, token);
                }
                await session.SetCaptureRecordingAsync(request.RequestId, token);
                indicator?.Show(video ? "Recording · 5 seconds · no audio" : "Taking screenshot…");
                var result = await NativeWindowCapture.RunAsync(request, token);
                committed = await session.CompleteCaptureAsync(new(request.RequestId, (int)result.Width, (int)result.Height, result.DurationSeconds, Target: result.Target), token);
            }
            finally { indicator?.Dispose(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!retired)
            {
                if (error is not OperationCanceledException) Diagnostics.FrontendFailureLog.Current.Write("capture-run", error);
                try { await session.CompleteCaptureAsync(new(request.RequestId, 0, 0, 0,
                    error is OperationCanceledException ? "cancelled" : "capture_target_unavailable"), lifetime.Token); }
                catch (Exception report) when (report is not OutOfMemoryException) { Diagnostics.FrontendFailureLog.Current.Write("capture-report", report); }
            }
        }
        finally
        {
            cancellation.Cancel();
            if (renewal is not null) await renewal;
            if (!committed) { try { File.Delete(request.OutputPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(300, token);
                    if (retired || visible || !await session.IsCaptureCurrentAsync(request.RequestId, token)) { cancellation.Cancel(); return; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException) { cancellation.Cancel(); }
        }
    }
}
