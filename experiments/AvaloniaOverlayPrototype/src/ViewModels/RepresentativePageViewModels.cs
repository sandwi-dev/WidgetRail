using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GameBarAlternative.AvaloniaPrototype.ViewModels;

public sealed record SettingsPageState(
    string Title,
    string Description,
    double InterfaceScale,
    bool KeepTopmost,
    bool StartWithLastWidget,
    bool ReduceMotion);

public sealed partial class SettingsPageViewModel : ObservableObject
{
    [ObservableProperty]
    private SettingsPageState state = new(
        "Settings",
        "This deliberately long label verifies that a controller-first overlay can preserve readable hierarchy when descriptive settings text spans multiple lines at compact logical sizes.",
        125,
        true,
        true,
        false);

    public double InterfaceScale
    {
        get => State.InterfaceScale;
        set => State = State with { InterfaceScale = value };
    }

    partial void OnStateChanged(SettingsPageState value) => OnPropertyChanged(nameof(InterfaceScale));

    [RelayCommand]
    private void ToggleTopmost() => State = State with { KeepTopmost = !State.KeepTopmost };

    [RelayCommand]
    private void ToggleStartWithLast() => State = State with { StartWithLastWidget = !State.StartWithLastWidget };

    [RelayCommand]
    private void ToggleReduceMotion() => State = State with { ReduceMotion = !State.ReduceMotion };
}

public sealed record AudioMixerPageState(
    string Title,
    double MasterVolume,
    double GameVolume,
    double SpotifyVolume,
    double VoiceVolume,
    bool MasterMuted,
    bool GameMuted,
    bool SpotifyMuted,
    bool VoiceMuted);

public sealed partial class AudioMixerPageViewModel : ObservableObject
{
    [ObservableProperty]
    private AudioMixerPageState state = new("Audio Mixer", 78, 64, 52, 35, false, false, false, false);

    public double MasterVolume { get => State.MasterVolume; set => State = State with { MasterVolume = value }; }
    public double GameVolume { get => State.GameVolume; set => State = State with { GameVolume = value }; }
    public double SpotifyVolume { get => State.SpotifyVolume; set => State = State with { SpotifyVolume = value }; }
    public double VoiceVolume { get => State.VoiceVolume; set => State = State with { VoiceVolume = value }; }

    partial void OnStateChanged(AudioMixerPageState value)
    {
        OnPropertyChanged(nameof(MasterVolume));
        OnPropertyChanged(nameof(GameVolume));
        OnPropertyChanged(nameof(SpotifyVolume));
        OnPropertyChanged(nameof(VoiceVolume));
    }

    [RelayCommand] private void ToggleMasterMute() => State = State with { MasterMuted = !State.MasterMuted };
    [RelayCommand] private void ToggleGameMute() => State = State with { GameMuted = !State.GameMuted };
    [RelayCommand] private void ToggleSpotifyMute() => State = State with { SpotifyMuted = !State.SpotifyMuted };
    [RelayCommand] private void ToggleVoiceMute() => State = State with { VoiceMuted = !State.VoiceMuted };
}

public sealed record SpotifyPlayerPageState(
    string Title,
    string Track,
    string Artist,
    double Position,
    bool IsPlaying,
    string Readiness);

public sealed partial class SpotifyPlayerPageViewModel : ObservableObject
{
    [ObservableProperty]
    private SpotifyPlayerPageState state = new(
        "Spotify Player",
        "A Very Long Track Title Designed to Exercise Responsive Containment Without Ellipsis Hiding the Important State",
        "The Fixture Artists • Prototype Sessions",
        94,
        false,
        "Destination state: ready from an asynchronous credential-free fixture.");

    public double Position { get => State.Position; set => State = State with { Position = value }; }

    partial void OnStateChanged(SpotifyPlayerPageState value) => OnPropertyChanged(nameof(Position));

    [RelayCommand]
    private void TogglePlayback() => State = State with { IsPlaying = !State.IsPlaying };
}
