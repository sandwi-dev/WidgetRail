using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    internal Func<BrowserSurface, WebCaptureStreamOptions, IReadOnlyList<WebCaptureStreamOptions>?, bool, CancellationToken, Task<WebCaptureSourceSelection?>>? ChooseWebCaptureSource { get; set; }
    // A removed session is not proof that its native worker has exited. Retain
    // its exact owner until teardown succeeds, so Stop can retry a failed close.
    private readonly Dictionary<(string Widget, string Document), Entry> captureRetirements = [];
    private readonly Dictionary<(string Widget, string Document), Task> captureRetirementRetries = [];

    private void ScheduleCaptureRetirementRetry((string Widget, string Document) key, Entry entry)
    {
        if (retired || captureRetirementRetries.ContainsKey(key)) return;
        captureRetirementRetries.Add(key, RetryAsync());
        async Task RetryAsync()
        {
            try
            {
                // Stop is idempotent and bound to this retained owner. Retry only
                // cleanup, never source selection or a provider broadcast command.
                foreach (var delay in new[] { 250, 750, 1500 })
                {
                    await Task.Delay(delay, lifetime.Token);
                    if (!ReferenceEquals(captureRetirements.GetValueOrDefault(key), entry)) return;
                    if (retiringEntries.Add(key)) await RetireCoreAsync(key, entry);
                }
            }
            catch (OperationCanceledException) when (retired) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            { Diagnostics.FrontendFailureLog.Current.Write("web-capture-retirement", null, "cleanup_retry_failed"); }
            finally { captureRetirementRetries.Remove(key); }
        }
    }

    private async Task<WebCaptureStreamStatus> RetryRetiredCaptureStopAsync(
        (string Widget, string Document) key, Entry entry, WebCaptureStreamTicket ticket)
    {
        WebCaptureStreamStatus status;
        await entry.SessionGate.WaitAsync(lifetime.Token);
        try { status = await entry.Surface.StopHostedCaptureAsync(ticket); }
        finally { entry.SessionGate.Release(); }
        if (ReferenceEquals(captureRetirements.GetValueOrDefault(key), entry) && retiringEntries.Add(key))
            await RetireCoreAsync(key, entry);
        return status;
    }

    private async Task PublishHostedCaptureAsync(WebSessionReference reference, WebCaptureStreamStatus status)
    {
        if (retired) return;
        try { await session.PublishWebSessionMessagesAsync(new(reference, [], Capture: status), lifetime.Token); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("web-capture-state", null, "transport_unavailable"); }
    }
}
