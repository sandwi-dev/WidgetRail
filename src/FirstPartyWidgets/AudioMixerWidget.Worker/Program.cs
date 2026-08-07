using GameBarAlternative.FirstPartyWidgets.AudioMixer;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.FirstPartyWidgets.AudioMixer.Worker;

internal static class Program
{
    public static async Task<int> Main(string[] args) =>
        await WidgetWorkerBootstrap.RunAsync(args, () => new AudioMixerWidget())
            .ConfigureAwait(false);
}
