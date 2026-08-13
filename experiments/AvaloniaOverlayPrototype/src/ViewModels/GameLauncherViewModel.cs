using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBarAlternative.AvaloniaPrototype.Remote;

namespace GameBarAlternative.AvaloniaPrototype.ViewModels;

public sealed record GameLauncherItemViewModel(
    RemoteWidgetItemId Id,
    string Title,
    string Subtitle,
    string AutomationId,
    string LabelAutomationId,
    string ActionLabelAutomationId,
    string AutomationName)
{
    public string DisplayText => $"{Title}  —  {Subtitle}";
}

public sealed record GameLauncherPageState(
    string Title,
    IReadOnlyList<GameLauncherItemViewModel> Items,
    RemoteWidgetItemId? SelectedItemId,
    string Status,
    bool IsLoading,
    string? Failure);

public sealed partial class GameLauncherViewModel : ObservableObject, IDisposable
{
    private readonly RemoteWidgetProjection projection;

    [ObservableProperty]
    private GameLauncherPageState state = new("Game Launcher", Array.Empty<GameLauncherItemViewModel>(), null, "Waiting for remote snapshot", false, null);

    [ObservableProperty]
    private GameLauncherItemViewModel? selectedItem;

    public GameLauncherViewModel(RemoteWidgetProjection projection)
    {
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        projection.StateChanged += ProjectionStateChanged;
        ApplyProjection(projection.State);
    }

    public Task ActivateAsync(CancellationToken cancellationToken = default) => projection.ActivateAsync(cancellationToken);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => projection.RefreshAsync(cancellationToken);

    public void Deactivate() => projection.Deactivate();

    public Task InvokeItemAsync(GameLauncherItemViewModel item, CancellationToken cancellationToken = default) =>
        projection.InvokeAsync(new RemoteWidgetAction(item.Id, "open"), cancellationToken);

    [RelayCommand]
    private Task InvokeSelectedAsync() =>
        SelectedItem is null ? Task.CompletedTask : InvokeItemAsync(SelectedItem);

    partial void OnSelectedItemChanged(GameLauncherItemViewModel? value)
    {
        var id = value?.Id;
        if (State.SelectedItemId != id)
        {
            State = State with { SelectedItemId = id };
        }
    }

    public void Dispose()
    {
        projection.StateChanged -= ProjectionStateChanged;
        projection.Deactivate();
    }

    private void ProjectionStateChanged(object? sender, RemoteWidgetProjectionState next) => ApplyProjection(next);

    private void ApplyProjection(RemoteWidgetProjectionState next)
    {
        var selectedId = SelectedItem?.Id ?? State.SelectedItemId;
        IReadOnlyList<GameLauncherItemViewModel> items = next.LastGoodSnapshot is null
            ? State.Items
            : next.LastGoodSnapshot.Items.Select((item, index) => new GameLauncherItemViewModel(
                item.Id,
                item.Title,
                item.Subtitle,
                $"launcher.game.{index + 1:D5}",
                $"launcher.game.{index + 1:D5}.label",
                $"launcher.game.{index + 1:D5}.action.label",
                $"Open {item.Title}"))
                .ToArray();
        var selected = selectedId is { } stableId
            ? items.FirstOrDefault(item => item.Id == stableId)
            : items.FirstOrDefault();
        State = new GameLauncherPageState(
            "Game Launcher",
            items,
            selected?.Id,
            next.Failure ?? next.LastGoodSnapshot?.Status ?? State.Status,
            next.IsLoading,
            next.Failure);
        SelectedItem = selected;
    }
}
