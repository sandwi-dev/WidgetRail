using GameBarAlternative.AvaloniaPrototype.Remote;
using GameBarAlternative.AvaloniaPrototype.ViewModels;
using GameBarAlternative.AvaloniaPrototype.Views;

namespace GameBarAlternative.AvaloniaPrototype.Composition;

public sealed class PrototypeComposition : IDisposable
{
    private readonly RemoteWidgetProjection remoteProjection;

    private PrototypeComposition(
        PrototypeShellViewModel shell,
        SettingsPageViewModel settings,
        AudioMixerPageViewModel audio,
        SpotifyPlayerPageViewModel spotify,
        GameLauncherViewModel launcher,
        RemoteWidgetProjection remoteProjection,
        IRemoteWidgetEndpoint remoteEndpoint)
    {
        Shell = shell;
        Settings = settings;
        Audio = audio;
        Spotify = spotify;
        Launcher = launcher;
        this.remoteProjection = remoteProjection;
        RemoteEndpoint = remoteEndpoint;
        PageFactory = new PrototypePageFactory(settings, audio, spotify, launcher);
    }

    public PrototypeShellViewModel Shell { get; }
    public SettingsPageViewModel Settings { get; }
    public AudioMixerPageViewModel Audio { get; }
    public SpotifyPlayerPageViewModel Spotify { get; }
    public GameLauncherViewModel Launcher { get; }
    public PrototypePageFactory PageFactory { get; }
    public IRemoteWidgetEndpoint RemoteEndpoint { get; }

    public static PrototypeComposition Create(IRemoteWidgetEndpoint? remoteEndpoint = null)
    {
        var endpoint = remoteEndpoint ?? new FakeRemoteWidgetEndpoint();
        var projection = new RemoteWidgetProjection(endpoint);
        return new PrototypeComposition(
            new PrototypeShellViewModel(),
            new SettingsPageViewModel(),
            new AudioMixerPageViewModel(),
            new SpotifyPlayerPageViewModel(),
            new GameLauncherViewModel(projection),
            projection,
            endpoint);
    }

    public void Dispose()
    {
        Launcher.Dispose();
        remoteProjection.Dispose();
    }
}
