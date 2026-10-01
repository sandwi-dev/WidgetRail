using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal Func<CancellationToken, Task<ControllerControlStatus>>? ControllerControlStatusRequested { get; set; }
    internal event Action<ControllerSettings>? ControllerControlPreferenceReceived;
    private Task? controllerControlPump;

    private async Task PumpControllerControlAsync()
    {
        // One ordered report/reply/apply cycle, including while the shell is
        // hidden. No visible-frame polling or action replay is involved.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                var status = await ControllerControlStatusRequested!(lifetime.Token);
                if (retired) return;
                var preference = await owner!.Session.ExchangeControllerControlAsync(status, lifetime.Token);
                if (retired) return;
                ControllerControlPreferenceReceived?.Invoke(preference);
            } while (await timer.WaitForNextTickAsync(lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!retired) Diagnostics.FrontendFailureLog.Current.Write("controller-control-exchange", error);
            // The broker expires readiness after five seconds. Do not fabricate
            // a successful state or retry/replay a partially completed exchange.
        }
    }
}
