using GameBarAlternative.FirstPartyWidgets.RecentApps;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.FirstPartyWidgets.RecentApps.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args) =>
        await WidgetWorkerBootstrap.RunAsync(args, () => new RecentAppsWidget())
            .ConfigureAwait(false);
}
