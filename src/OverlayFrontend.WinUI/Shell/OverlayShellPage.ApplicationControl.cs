using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal event Action<WidgetApplicationControl>? ApplicationControlRequested;
    private Task? applicationControlPump;

    private async Task PumpApplicationControlAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                // Take is destructive: abandoning a slow reply could lose a
                // request already consumed by Bridge. Keep one async read in
                // flight, cancelled only when this host retires. Busy work
                // cannot accumulate polls or permanently disable the consumer.
                var action = await owner!.Session.TakeApplicationControlAsync(lifetime.Token);
                if (retired) return;
                if (action == WidgetApplicationControl.None) continue;
                // Dispatch outside this task so window cleanup can await the
                // pump without reentrant shutdown or a self-await deadlock.
                DispatcherQueue.TryEnqueue(() => { if (!retired) ApplicationControlRequested?.Invoke(action); });
                return;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!retired) ReportFailure(error); }
    }
}
