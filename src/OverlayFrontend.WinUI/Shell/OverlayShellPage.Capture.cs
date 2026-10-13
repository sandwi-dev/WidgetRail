using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal Func<CancellationToken, Task<bool>>? HideForCaptureRequested { get; set; }
    private Task? capturePump, capturing;
    private readonly HashSet<string> captureDiscoveries = [];
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
                    if (next.NeedsCapabilities)
                        await owner.Session.PublishCaptureCapabilitiesAsync(NativeWindowCapture.ImageCapabilities(), lifetime.Token, encodingProbed: false);
                    if (next.NeedsEncodingCapabilities)
                        BeginCaptureDiscovery(null);
                    foreach (var discovery in next.RecordingDiscoveries) BeginCaptureDiscovery(discovery);
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
    private void BeginCaptureDiscovery(HostCaptureDiscovery? discovery)
    {
        var key = discovery?.Id ?? "catalog";
        if (!captureDiscoveries.Add(key)) return;
        _ = PublishAsync();
        async Task PublishAsync()
        {
            try
            {
                var capabilities = await (discovery is null ? NativeWindowCapture.GetCapabilitiesAsync() :
                    NativeWindowCapture.GetRecordingCapabilitiesAsync(discovery.Query)).WaitAsync(lifetime.Token);
                await owner!.Session.PublishCaptureCapabilitiesAsync(capabilities, lifetime.Token, discoveryId: discovery?.Id);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-discovery", error);
            }
            finally { captureDiscoveries.Remove(key); }
        }
    }
    private async Task RunCaptureAsync(BridgeCaptureTake next)
    {
        var request = next.Request!;
        var session = owner!.Session;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        using var stop = new CancellationTokenSource();
        var preparation = request.Options?.Preparation ?? new CapturePreparation();
        var duration = request.Options?.Recording?.MaximumDurationSeconds ?? CaptureLimits.VideoSeconds;
        cancellation.CancelAfter(TimeSpan.FromSeconds(120 + preparation.DelaySeconds + duration));
        var token = cancellation.Token;
        Task? renewal = null;
        var committed = false;
        var recording = false;
        try
        {
            if (retired || !visible || !foreground || !interactive || activeWidget != next.WidgetId || HostChoiceActive || LocalInstallActive)
                throw new OperationCanceledException();
            var video = request.Kind == WindowCaptureKind.Video;
            var delay = preparation.DelaySeconds == 0 ? "after WidgetRail hides" : $"after a {preparation.DelaySeconds}-second countdown";
            var imageSize = request.Options?.Screenshot is { } image ? image.MaximumWidth is null ? "full-resolution" : $"up to {image.MaximumWidth} × {image.MaximumHeight}" :
                request.Resolution == CaptureImageResolution.Original ? "full-resolution" : "up to 1920 × 1080";
            var encoding = request.Options?.Recording is { } settings ?
                $"\n{settings.MaximumWidth} × {settings.MaximumHeight} maximum · {settings.FramesPerSecond} fps · {settings.Encoder.ToString().ToLowerInvariant()} encoding." : "";
            var applicationAudio = request.Options?.Recording?.Audio == CaptureAudioMode.Application;
            var audioDescription = applicationAudio ? "Application audio will be recorded. The microphone will not be recorded." : "No audio will be recorded.";
            var selected = await ShowHostChoiceAsync(video ? "Record application?" : "Take screenshot?",
                "WidgetRail will hide. Bring the application you want to capture to the foreground.\n\n" +
                (video ? $"Recording starts {delay} and lasts up to {duration} seconds. {audioDescription}{encoding}" :
                    $"A {imageSize} screenshot will be taken {delay}.") +
                " The application in front when capture begins will be captured." +
                (video && request.Options is not null ? "\n\nReopen WidgetRail to finish recording and review it." : "\n\nReopen WidgetRail when finished to review your capture."),
                (HostDialogChoice[])[new("proceed", "Proceed")], token);
            if (selected != "proceed" || HideForCaptureRequested is null || !await session.IsCaptureCurrentAsync(request.RequestId, token) ||
                !await HideForCaptureRequested(token)) throw new OperationCanceledException();
            renewal = RenewAsync();
            CaptureIndicator? indicator = null;
            try
            {
                try { indicator = new(ShellPalette, Appearance, systemUi.AnimationsEnabled); }
                catch (InvalidOperationException) { /* Countdown availability is platform-dependent; confirmation states the delay. */ }
                for (var seconds = preparation.DelaySeconds; seconds > 0; --seconds)
                {
                    token.ThrowIfCancellationRequested();
                    if (visible) throw new OperationCanceledException();
                    if (preparation.ShowCountdown) indicator?.Show((video ? "Recording starts in " : "Screenshot in ") + seconds + "…");
                    await Task.Delay(1000, token);
                }
                await session.SetCaptureRecordingAsync(request.RequestId, token);
                recording = true;
                indicator?.Show(video ? $"Recording · up to {duration}s · {(applicationAudio ? "application audio" : "no audio")}" : "Taking screenshot…");
                var result = await NativeWindowCapture.RunAsync(request, token, stop.Token);
                committed = await session.CompleteCaptureAsync(new(request.RequestId, (int)result.Width, (int)result.Height, result.DurationSeconds, Target: result.Target)
                    { Properties = result.Properties }, token);
            }
            finally { indicator?.Dispose(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!retired)
            {
                if (error is not OperationCanceledException) Diagnostics.FrontendFailureLog.Current.Write("capture-run", error);
                try { await session.CompleteCaptureAsync(new(request.RequestId, 0, 0, 0,
                    error is OperationCanceledException ? "cancelled" : error is BrokerException configuration ? configuration.Code :
                    error.HResult == unchecked((int)0x800700DF) ? "capture_too_large" : "capture_target_unavailable"), lifetime.Token); }
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
                    var state = await session.GetCaptureStateAsync(request.RequestId, token);
                    if (retired || !state.Current) { cancellation.Cancel(); return; }
                    if (state.StopRequested || visible && recording && request.Options?.Recording is not null) stop.Cancel();
                    else if (visible) { cancellation.Cancel(); return; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException) { cancellation.Cancel(); }
        }
    }
}
