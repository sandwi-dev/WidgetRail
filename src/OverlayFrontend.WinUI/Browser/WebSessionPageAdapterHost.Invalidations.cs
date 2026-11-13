using Microsoft.UI.Dispatching;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class WebSessionPageAdapterHost
{
    private DispatcherQueueTimer? invalidationTimer;
    private bool invalidationPending, invalidationOutstanding;
    private long invalidationEpoch;

    // Level-triggered, payload-free change notification. At most one event can
    // await a consumer read, regardless of how many provider stores change.
    private void InvalidateBackend()
    {
        if (!AllowsBackgroundRequests || !Current(epoch) || !canRequest() ||
            invalidationOutstanding || invalidationPending) return;
        invalidationPending = true;
        invalidationEpoch = epoch;
        if (invalidationTimer is null)
        {
            invalidationTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            invalidationTimer.Interval = TimeSpan.FromMilliseconds(100);
            invalidationTimer.IsRepeating = false;
            invalidationTimer.Tick += PublishInvalidation;
        }
        invalidationTimer.Start();
    }

    private void PublishInvalidation(DispatcherQueueTimer sender, object args)
    {
        if (!invalidationPending) return;
        invalidationPending = false;
        if (!Current(invalidationEpoch) || !canRequest() || invalidationOutstanding) return;
        invalidationOutstanding = true;
        publish("{\"kind\":\"backend-invalidated\"}");
    }

    private void RetireInvalidations()
    {
        invalidationTimer?.Stop();
        invalidationPending = invalidationOutstanding = false;
    }
}
