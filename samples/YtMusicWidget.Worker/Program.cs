using GameBarAlternative.Samples.YtMusicWidget;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.Samples.YtMusicWidget.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
        => await WidgetWorkerBootstrap.RunAsync(args, () => new YtMusicWidget())
            .ConfigureAwait(false);
}
