using WidgetRail.Samples.ClockWidget;
using WidgetRail.WidgetRuntime;

namespace WidgetRail.Samples.ClockWidget.Worker;

public static class WorkerMarker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await WidgetWorkerBootstrap.RunAsync(
            args, () => new ClockWidget()).ConfigureAwait(false);
    }
}
