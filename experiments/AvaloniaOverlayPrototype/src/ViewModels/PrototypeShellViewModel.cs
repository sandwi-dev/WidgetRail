using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBarAlternative.AvaloniaPrototype.Navigation;

namespace GameBarAlternative.AvaloniaPrototype.ViewModels;

public sealed record PrototypeShellState(
    PrototypeRoute SelectedRoute,
    bool IsLoading,
    string LoadingText);

public sealed partial class PrototypeShellViewModel : ObservableObject
{
    [ObservableProperty]
    private PrototypeShellState state = new(PrototypeRoute.Settings, false, string.Empty);

    public event EventHandler<PrototypeRoute>? RouteRequested;

    public void BeginNavigation(PrototypeRoute route, string title) =>
        State = new PrototypeShellState(route, true, $"Preparing {title}…");

    public void CompleteNavigation(PrototypeRoute route) =>
        State = new PrototypeShellState(route, false, string.Empty);

    public void CancelNavigation() => State = State with { IsLoading = false };

    [RelayCommand]
    private void ShowSettings() => RouteRequested?.Invoke(this, PrototypeRoute.Settings);

    [RelayCommand]
    private void ShowAudio() => RouteRequested?.Invoke(this, PrototypeRoute.AudioMixer);

    [RelayCommand]
    private void ShowSpotify() => RouteRequested?.Invoke(this, PrototypeRoute.SpotifyPlayer);

    [RelayCommand]
    private void ShowLauncher() => RouteRequested?.Invoke(this, PrototypeRoute.GameLauncher);
}
