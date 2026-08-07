using GameBarAlternative.FirstPartyWidgets.NetworkControls;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.FirstPartyWidgets.NetworkControls.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args) =>
        await WidgetWorkerBootstrap.RunAsync(args, () => new NetworkControlsWidget())
            .ConfigureAwait(false);
}
