using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.AvaloniaPrototype.Remote;

namespace GameBarAlternative.AvaloniaPrototype.ViewModels;

public sealed record GameLauncherItemViewModel(
    RemoteWidgetItemId Id,
    string Title,
    string Subtitle,
    string AutomationId,
    string LabelAutomationId,
    string ActionLabelAutomationId,
    string AutomationName,
    RemoteWidgetActionId PrimaryActionId)
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
    public static readonly RemoteWidgetActionId OpenActionId = new("open");
    private readonly RemoteWidgetProjection projection;
    private readonly IPresentationScheduler scheduler;

    [ObservableProperty]
    private GameLauncherPageState state = new("Game Launcher", Array.Empty<GameLauncherItemViewModel>(), null, "Waiting for remote snapshot", false, null);

    private GameLauncherItemViewModel? selectedItem;

    public GameLauncherViewModel(RemoteWidgetProjection projection, IPresentationScheduler scheduler)
    {
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public GameLauncherItemViewModel? SelectedItem
    {
        get => selectedItem;
        set
        {
            EnsurePresentationAccess();
            if (SetProperty(ref selectedItem, value))
            {
                var id = value?.Id;
                if (State.SelectedItemId != id)
                {
                    State = State with { SelectedItemId = id };
                }
            }
        }
    }

    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        var activation = projection.ActivateAsync(cancellationToken);
        await PublishProjectionAsync().ConfigureAwait(false);
        await activation.ConfigureAwait(false);
        await PublishProjectionAsync().ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var refresh = projection.RefreshAsync(cancellationToken);
        await PublishProjectionAsync().ConfigureAwait(false);
        await refresh.ConfigureAwait(false);
        await PublishProjectionAsync().ConfigureAwait(false);
    }

    public void Deactivate() => projection.Deactivate();

    public Task InvokeItemAsync(GameLauncherItemViewModel item, CancellationToken cancellationToken = default) =>
        projection.InvokeAsync(new RemoteWidgetAction(item.Id, item.PrimaryActionId), cancellationToken);

    [RelayCommand]
    private Task InvokeSelectedAsync() =>
        SelectedItem is null ? Task.CompletedTask : InvokeItemAsync(SelectedItem);

    public void Dispose()
    {
        projection.Deactivate();
    }

    private Task PublishProjectionAsync() => scheduler.InvokeAsync(() => ApplyProjection(projection.State));

    private void ApplyProjection(RemoteWidgetProjectionState next)
    {
        EnsurePresentationAccess();
        var selectedId = SelectedItem?.Id ?? State.SelectedItemId;
        IReadOnlyList<GameLauncherItemViewModel> items = next.LastGoodSnapshot is null
            ? State.Items
            : next.LastGoodSnapshot.Items.Select(item =>
            {
                var automationId = SemanticAutomationIdentity.ForLauncherItem(item.Id);
                var primaryAction = item.Actions.FirstOrDefault(action => action.Id == OpenActionId) ??
                    item.Actions.FirstOrDefault() ??
                    throw new InvalidDataException($"Remote item '{item.Id}' declared no action.");
                return new GameLauncherItemViewModel(
                item.Id,
                item.Title,
                item.Subtitle,
                automationId,
                $"{automationId}.label",
                $"{automationId}.action.label",
                $"{primaryAction.Name} {item.Title}",
                primaryAction.Id);
            })
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

    private void EnsurePresentationAccess()
    {
        if (!scheduler.CheckAccess())
        {
            throw new InvalidOperationException("Bound launcher state may mutate only on the presentation scheduler.");
        }
    }
}
