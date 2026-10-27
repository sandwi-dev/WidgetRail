using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Browser.Capture;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private sealed class HostedCapture(WebCaptureStreamTicket ticket, BrowserDocumentLease document)
    {
        internal readonly WebCaptureStreamTicket Ticket = ticket;
        internal readonly BrowserDocumentLease Document = document;
        internal readonly CancellationTokenSource Preparation = new();
        internal CancellationTokenRegistration Retirement;
        internal WebViewCaptureWorker? Transport;
        internal WebCaptureStreamStatus Status = new(WebCaptureStreamPhase.Unavailable) { Ticket = ticket };
        internal Task? Stopping;
    }

    private HostedCapture? hostedCapture;
    internal event Action<WebCaptureStreamStatus>? HostedCaptureChanged;
#if ENABLE_WIDGET_VALIDATION
    // Deterministic fault injection: never starts a native worker or selects a user window.
    internal Func<Task>? FixtureBeforeCaptureStop { get; set; }
    internal WebCaptureStreamTicket? FixtureRetainedCaptureTicket => hostedCapture?.Ticket;
#endif

    private async Task ConfigureNativeCaptureAsync()
    {
        if (!SupportsNativeCapture || core is null) return;
        // Ordinary remote sessions keep WebView messaging disabled. Opted-in
        // native media uses only the transport's bounded epoch/sequence parser;
        // it does not forward page-authored messages to the widget RPC channel.
        core.Settings.IsWebMessageEnabled = true;
        using var resource = typeof(BrowserSurface).Assembly.GetManifestResourceStream("WidgetRail.WebCapture.NativeMedia.js")
            ?? throw new InvalidOperationException("The native capture runtime is not installed.");
        using var reader = new StreamReader(resource);
        var script = await reader.ReadToEndAsync();
        await core.AddScriptToExecuteOnDocumentCreatedAsync("(()=>{" + script +
            "\ninitializeWidgetRailNativeCapture(" + JsonSerializer.Serialize(hostedDefinition!.PageAdapter!.Origins.ToArray(), BrowserJsonContext.Default.StringArray) + ");})()");
    }

    internal async Task<WebCaptureStreamStatus> PrepareHostedCaptureAsync(WebCaptureStreamTicket ticket,
        WebCaptureStreamOptions options, Func<CancellationToken, Task<WebCaptureSourceSelection?>> choose,
        CancellationToken token,IReadOnlyList<WebCaptureStreamOptions>? permitted=null)
    {
        if (!SupportsNativeCapture || !ticket.IsWellFormed() || !options.IsWellFormed() || core is not { } browser ||
            CurrentBackendDocumentOrigin is not { } origin)
            return new(WebCaptureStreamPhase.Unavailable) { Ticket = ticket, Reason = "document_unavailable" };
        var allowed=LiveCaptureQuality.Allowed(permitted).Select(quality=>quality.Options).ToArray();
        if(!allowed.Any(options.IsSameQuality))
            return new(WebCaptureStreamPhase.Unavailable) { Ticket=ticket,Reason="quality_unavailable" };
        if (hostedCapture is { } previous)
        {
            // The previous transport must finish retiring before its mappings or
            // document hook can be reused. A failed stop is not a completed stop.
            await StopHostedCaptureAsync(previous.Ticket);
            if (ReferenceEquals(hostedCapture, previous)) throw new InvalidOperationException("Capture retirement is incomplete.");
        }
        var documentLease = CaptureDocumentLease(lifetime.Token);
        if (documentLease is null) return new(WebCaptureStreamPhase.Unavailable) { Ticket = ticket, Reason = "document_unavailable" };
        var capture = new HostedCapture(ticket, documentLease);
        hostedCapture = capture;
        capture.Retirement = documentLease.Token.Register(() => DispatcherQueue.TryEnqueue(() =>
            Run(() => RetireHostedCaptureAsync(capture, "document_retired"))));
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(token, documentLease.Token, capture.Preparation.Token);
        bool Current() => ReferenceEquals(hostedCapture, capture) && capture.Stopping is null &&
            !preparation.IsCancellationRequested && documentLease.IsCurrent;
        try
        {
            var selected = await choose(preparation.Token);
            if (selected is null || !Current())
            {
                await RetireHostedCaptureAsync(capture, "selection_cancelled");
                return new(WebCaptureStreamPhase.Cancelled) { Ticket = ticket };
            }
            if(!selected.Options.IsWellFormed()||!allowed.Any(selected.Options.IsSameQuality))
            {
                await RetireHostedCaptureAsync(capture,"quality_not_allowed");
                return new(WebCaptureStreamPhase.Failed) {Ticket=ticket,Reason="quality_not_allowed"};
            }
            var transport = capture.Transport = new WebViewCaptureWorker(browser, DispatcherQueue, origin,
                () => ReferenceEquals(hostedCapture, capture) && capture.Stopping is null && documentLease.IsCurrent);
            transport.Ended += () => DispatcherQueue.TryEnqueue(() =>
                Run(() => RetireHostedCaptureAsync(capture, transport.Reason ?? "source_ended")));
            await transport.StartAsync(selected.Target, selected.ApplicationAudio,
                LiveCaptureQuality.FromOptions(selected.Options), preparation.Token);
            if (!Current()) throw new OperationCanceledException(preparation.Token);
            capture.Status = new(WebCaptureStreamPhase.Prepared)
            {
                Ticket = ticket, Quality = selected.Options, ApplicationName = selected.ApplicationName,
                ApplicationAudio = selected.ApplicationAudio,
            };
            PublishHostedCapture(capture);
            _ = MonitorHostedCaptureAsync(capture);
            return capture.Status;
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested)
        {
            await RetireHostedCaptureAsync(capture, "request_cancelled");
            return new(WebCaptureStreamPhase.Cancelled) { Ticket = ticket };
        }
        catch
        {
            await RetireHostedCaptureAsync(capture, "capture_failed");
            throw;
        }
    }

    private async Task MonitorHostedCaptureAsync(HostedCapture capture)
    {
        try
        {
            // This host-owned monitor only publishes phase changes. Frame/audio
            // transport remains bounded and never passes through widget RPC.
            while (ReferenceEquals(hostedCapture, capture) && capture.Stopping is null)
            {
                await Task.Delay(250, capture.Document.Token);
                if (!ReferenceEquals(hostedCapture, capture) || capture.Stopping is not null) return;
                if (capture.Transport?.HasEnded == true)
                { await RetireHostedCaptureAsync(capture, capture.Transport.Reason ?? "source_ended"); return; }
                if (capture.Transport?.IsCapturing == true && capture.Status.Phase == WebCaptureStreamPhase.Prepared)
                { capture.Status = capture.Status with { Phase = WebCaptureStreamPhase.Capturing }; PublishHostedCapture(capture); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Diagnostics.FrontendFailureLog.Current.Write("web-capture-monitor", null, "capture_monitor_failed"); }
    }

    internal async Task<WebCaptureStreamStatus> StopHostedCaptureAsync(WebCaptureStreamTicket ticket)
    {
        if (hostedCapture is not { } capture || capture.Ticket != ticket)
            return new(WebCaptureStreamPhase.Stopped) { Ticket = ticket, Reason = "stream_retired" };
        await RetireHostedCaptureAsync(capture, "stopped");
        return capture.Status;
    }

    private Task RetireHostedCaptureAsync(HostedCapture capture, string reason)
    {
        if (capture.Stopping is { } stopping) return stopping;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.Stopping = completion.Task;
        _ = FinishAsync();
        return completion.Task;
        async Task FinishAsync()
        {
            try { await StopHostedCaptureCoreAsync(capture, reason); completion.TrySetResult(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { capture.Stopping = null; completion.TrySetException(error); }
        }
    }

    private async Task StopHostedCaptureCoreAsync(HostedCapture capture, string reason)
    {
        capture.Preparation.Cancel();
        try
        {
#if ENABLE_WIDGET_VALIDATION
            if (FixtureBeforeCaptureStop is { } beforeStop) await beforeStop();
#endif
            if (capture.Transport is { } transport)
            {
                await transport.StopAsync();
                await transport.DisposeAsync();
            }
            capture.Status = capture.Status with { Phase = WebCaptureStreamPhase.Stopped, Reason = reason };
            capture.Retirement.Dispose();
            capture.Document.Dispose();
            capture.Preparation.Dispose();
            if (ReferenceEquals(hostedCapture, capture)) hostedCapture = null;
            PublishHostedCapture(capture);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Retain the exact ticket; neither the host nor widget may silently
            // start another source while shutdown is unconfirmed.
            throw new InvalidOperationException("Capture shutdown could not be confirmed.", error);
        }
    }

    private void PublishHostedCapture(HostedCapture capture) => HostedCaptureChanged?.Invoke(capture.Status);

    private async Task DisposeHostedCaptureAsync()
    {
        if (hostedCapture is { } capture) await RetireHostedCaptureAsync(capture, "session_closed");
        HostedCaptureChanged = null;
    }
}
