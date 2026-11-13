using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class PlaynitePerformanceValidationPage
{
    private readonly List<double> externalNormalWaits = [];
    private CancellationTokenSource? dispatcherProbeCancellation;
    private Task? dispatcherProbe;
    private int dispatcherProbePending;
    private int dispatcherProbeSkipped;
    private string? motionSynchronizationContext;

    private void StartDispatcherProbe()
    {
        motionSynchronizationContext = SynchronizationContext.Current?.GetType().FullName;
        if (!Environment.GetCommandLineArgs().Contains("--playnite-dispatcher-probe")) return;
        dispatcherProbeCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = dispatcherProbeCancellation.Token;
        dispatcherProbe = Task.Run(async () =>
        {
            for (var sample = 0; sample < 4096; sample++)
            {
                await Task.Delay(16,token).ConfigureAwait(false);
                // At most one outstanding callback; a stalled UI never builds a
                // diagnostic backlog that could manufacture its own congestion.
                if (Interlocked.CompareExchange(ref dispatcherProbePending,1,0) != 0)
                { Interlocked.Increment(ref dispatcherProbeSkipped); continue; }
                var queuedAt = Stopwatch.GetTimestamp();
                if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    Interlocked.Exchange(ref dispatcherProbePending,0);
                    if (!token.IsCancellationRequested)
                        externalNormalWaits.Add(Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds);
                })) break;
            }
        }, token);
    }

    private async Task StopDispatcherProbeAsync()
    {
        if (dispatcherProbeCancellation is null) return;
        dispatcherProbeCancellation.Cancel();
        try { if (dispatcherProbe is not null) await dispatcherProbe; }
        catch (OperationCanceledException) when (dispatcherProbeCancellation.IsCancellationRequested) { }
        dispatcherProbeCancellation.Dispose();
    }
}
