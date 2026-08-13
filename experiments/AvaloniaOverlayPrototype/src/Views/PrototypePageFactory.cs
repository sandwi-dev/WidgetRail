using Avalonia.Controls;
using GameBarAlternative.AvaloniaPrototype.ViewModels;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed class PrototypePageFactory
{
    private readonly SettingsPageViewModel settings;
    private readonly AudioMixerPageViewModel audio;
    private readonly SpotifyPlayerPageViewModel spotify;
    private readonly GameLauncherViewModel launcher;

    public PrototypePageFactory(
        SettingsPageViewModel settings,
        AudioMixerPageViewModel audio,
        SpotifyPlayerPageViewModel spotify,
        GameLauncherViewModel launcher)
    {
        this.settings = settings;
        this.audio = audio;
        this.spotify = spotify;
        this.launcher = launcher;
    }

    public async Task<Control> CreateAsync(PrototypeRoute route, CancellationToken cancellationToken)
    {
        var readinessDelay = route switch
        {
            PrototypeRoute.Settings => 30,
            PrototypeRoute.AudioMixer => 55,
            PrototypeRoute.SpotifyPlayer => 85,
            PrototypeRoute.GameLauncher => 120,
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        await Task.Delay(readinessDelay, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (route == PrototypeRoute.GameLauncher)
        {
            await launcher.ActivateAsync(cancellationToken);
        }

        return route switch
        {
            PrototypeRoute.Settings => new SettingsPage(settings),
            PrototypeRoute.AudioMixer => new AudioMixerPage(audio),
            PrototypeRoute.SpotifyPlayer => new SpotifyPlayerPage(spotify),
            PrototypeRoute.GameLauncher => new GameLauncherPage(launcher),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
    }
}
