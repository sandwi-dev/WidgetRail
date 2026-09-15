using System.ComponentModel;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsDisplayProvider;

internal sealed class DisplayPreviewUnstableException : Exception;
internal sealed class DisplayPreviewChangedException : Exception;

internal static class DisplayPreviewStabilizer
{
    internal static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(12);
    internal static readonly TimeSpan StableWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // The first temporary apply wakes the outputs. Wake-up can then cause Windows
    // to rediscover them and replace that temporary configuration with its saved one.
    internal static async Task<DisplayConfiguration> WaitAsync(IDisplayNative native,
        DisplayConfiguration target, Task<string?> parentInput, TimeProvider time, long started,
        DisplayRestoreDiagnosticLog? diagnostics)
    {
        DisplayConfiguration? previous = null;
        long stableSince = time.GetTimestamp();
        var retried = false;
        diagnostics?.Record("stabilization-begin");
        while (time.GetElapsedTime(started) < MaximumWait)
        {
            CheckParent();
            DisplayConfiguration? current = null;
            try { current = native.Capture(); }
            catch (Exception error) when (error is Win32Exception or BrokerException)
            {
                // A waking monitor can temporarily lack a display path/name.
                previous = null;
                diagnostics?.Record("stabilization-read-failed", error: error);
            }
            CheckParent();
            if (time.GetElapsedTime(started) >= MaximumWait) break;
            if (current is not null)
            {
                if (previous is null || !previous.Matches(current))
                {
                    previous = current;
                    stableSince = time.GetTimestamp();
                    diagnostics?.Record("stabilization-observed", current);
                }
                else if (time.GetElapsedTime(stableSince) >= StableWindow)
                {
                    if (target.Matches(current))
                    {
                        diagnostics?.Record("stabilization-ready", current);
                        return target;
                    }
                    if (retried) break;
                    // Re-resolve the monitor paths after wake-up, without relaxing
                    // identity matching or exact refresh-rate requirements.
                    DisplayConfiguration? remapped = null;
                    try
                    {
                        remapped = DisplayProfileMatching.Remap(target, native.ConnectedPaths());
                        native.Validate(remapped);
                    }
                    catch (Exception error) when (error is Win32Exception or BrokerException)
                    {
                        remapped = null;
                        diagnostics?.Record("stabilization-paths-not-ready", error: error);
                    }
                    CheckParent();
                    if (time.GetElapsedTime(started) >= MaximumWait) break;
                    if (remapped is not null)
                    {
                        target = remapped;
                        retried = true;
                        diagnostics?.Record("target", target);
                        diagnostics?.Record("stabilization-reapply-begin");
                        native.Apply(target, persist: false);
                        diagnostics?.Record("stabilization-reapply-complete", readback: true);
                        previous = null;
                    }
                }
            }
            using var cancellation = new CancellationTokenSource();
            var delay = Task.Delay(PollInterval, time, cancellation.Token);
            await Task.WhenAny(delay, parentInput).ConfigureAwait(false);
            cancellation.Cancel();
        }
        diagnostics?.Record("stabilization-failed");
        throw new DisplayPreviewUnstableException();

        void CheckParent()
        {
            // No valid confirmation can arrive before the applied reply. EOF or
            // early input must end the preview rather than permit another apply.
            if (!parentInput.IsCompleted) return;
            diagnostics?.Record("stabilization-parent-ended");
            throw new OperationCanceledException();
        }
    }
}
