using Avalonia.Controls;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed class PrototypePageFactory
{
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

        return route switch
        {
            PrototypeRoute.Settings => new SettingsPage(),
            PrototypeRoute.AudioMixer => new AudioMixerPage(),
            PrototypeRoute.SpotifyPlayer => new SpotifyPlayerPage(),
            PrototypeRoute.GameLauncher => new GameLauncherPage(),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
    }
}
