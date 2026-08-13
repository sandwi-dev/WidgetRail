using GameBarAlternative.AvaloniaPrototype.Remote;
using GameBarAlternative.AvaloniaPrototype.Presentation;
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
        IRemoteWidgetEndpoint remoteEndpoint,
        AvaloniaUiScheduler presentationScheduler)
    {
        Shell = shell;
        Settings = settings;
        Audio = audio;
        Spotify = spotify;
        Launcher = launcher;
        this.remoteProjection = remoteProjection;
        RemoteEndpoint = remoteEndpoint;
        PresentationScheduler = presentationScheduler;
        PageFactory = new PrototypePageFactory(settings, audio, spotify, launcher);
    }

    public PrototypeShellViewModel Shell { get; }
    public SettingsPageViewModel Settings { get; }
    public AudioMixerPageViewModel Audio { get; }
    public SpotifyPlayerPageViewModel Spotify { get; }
    public GameLauncherViewModel Launcher { get; }
    public PrototypePageFactory PageFactory { get; }
    public IRemoteWidgetEndpoint RemoteEndpoint { get; }
    public RemoteWidgetProjection RemoteProjection => remoteProjection;
    public AvaloniaUiScheduler PresentationScheduler { get; }

    public static PrototypeComposition Create(IRemoteWidgetEndpoint? remoteEndpoint = null)
    {
        var endpoint = remoteEndpoint ?? new FakeRemoteWidgetEndpoint();
        var projection = new RemoteWidgetProjection(endpoint);
        var scheduler = new AvaloniaUiScheduler();
        return new PrototypeComposition(
            new PrototypeShellViewModel(),
            new SettingsPageViewModel(),
            new AudioMixerPageViewModel(),
            new SpotifyPlayerPageViewModel(),
            new GameLauncherViewModel(projection, scheduler),
            projection,
            endpoint,
            scheduler);
    }

    public void Dispose()
    {
        Launcher.Dispose();
        remoteProjection.Dispose();
    }
}
