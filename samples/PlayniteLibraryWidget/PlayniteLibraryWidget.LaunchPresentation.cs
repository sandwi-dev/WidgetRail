using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLaunchPrompt(PlayniteLibraryGameTarget Target, PlayniteLaunchConflict Conflict, long Generation);

public sealed partial class PlayniteLibraryWidget
{
    private const string LaunchUi = "playnite-library.launch.";

    private async Task ResumeGameAsync(string id, string title, Func<bool> sourceCurrent, CancellationToken token)
    {
        var process = _model.Value.RunningProcesses?.GetValueOrDefault(id);
        var handle = Operations.RunSingleFlight("playnite-library.launch-lifecycle", async context =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, token);
            bool Current() => !lifetime.IsCancellationRequested && LifecycleState == WidgetLifecycleState.Interactive &&
                sourceCurrent() && _model.Value.RunningGames?.Contains(id) == true &&
                _model.Value.RunningProcesses?.GetValueOrDefault(id) == process;
            if (!Current()) return;
            try
            {
                var result = await _application.ReturnToGameAsync(id, HostServices, Current, lifetime.Token).ConfigureAwait(false);
                if (Current() && result.Message is { } message) ShowActionToast(title, message, false);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (error is PlayniteBridgeDataException or PlayniteBridgeTransportException or
                WidgetCapabilityException or WidgetCapabilityUnavailableException)
            {
                if (Current()) ShowActionToast(title, "Couldn’t switch to the game. Open it from the task switcher.", false);
            }
        }, WidgetOperationLifetime.Active);
        await handle.Completion.ConfigureAwait(false);
    }

    private Dictionary<string, PlayniteLibraryLaunchState> EffectiveLaunchStates()
    {
        if (_model.Value.RunningGames is not { Count: > 0 }) return _launchStates;
        var states = new Dictionary<string, PlayniteLibraryLaunchState>(_launchStates, StringComparer.Ordinal);
        foreach (var id in _model.Value.RunningGames) states[id] = PlayniteLibraryLaunchState.Running;
        return states;
    }

    private void ApplyRunningGames(IReadOnlyList<string>? ids, IReadOnlyDictionary<string, long>? since = null, IReadOnlyDictionary<string, string>? names = null,
        IReadOnlyDictionary<string, string>? processes = null)
    {
        lock (_gate)
        {
            if (ids is { Count: 0 } && _model.Value.PlayingEntry is not null)
                _model.Update(state => state with { PlayingEntry = null });
            var prior = _model.Value;
            if ((names?.Count ?? 0) == (prior.RunningNames?.Count ?? 0) &&
                (processes?.Count ?? 0) == (prior.RunningProcesses?.Count ?? 0) &&
                (processes is null || processes.All(pair => prior.RunningProcesses?.GetValueOrDefault(pair.Key) == pair.Value)) &&
                (names is null || names.All(pair => prior.RunningNames?.GetValueOrDefault(pair.Key) == pair.Value)) &&
                (ids is null ? prior.RunningGames is null : prior.RunningGames?.SetEquals(ids) == true) &&
                (since?.Count ?? 0) == (prior.RunningSince?.Count ?? 0) &&
                (since is null || since.All(pair => prior.RunningSince?.GetValueOrDefault(pair.Key) == pair.Value))) return;
            var games = ids?.ToHashSet(StringComparer.Ordinal);
            _model.Update(state => state with { RunningGames = games, RunningSince = since, RunningNames = names, RunningProcesses = processes,
                PlayingEntry = games is { Count: 0 } ? null : state.PlayingEntry,
                DiscordPanelGame = state.DiscordPanelGame is { } panelGame && games?.Contains(panelGame) == true ? panelGame : null,
                PlayingGame = games is { Count: > 0 } ? state.PlayingGame is { } selected && games.Contains(selected) ? selected : games.Min(StringComparer.Ordinal) : null,
                NewsReader = state.NewsReader is { } reader &&
                games?.Contains(reader.GameId) == true ? reader : null });
            if (games is not { Count: > 0 }) ResetPlayingResources();
            foreach (var id in _launchStates.Where(pair => pair.Value == PlayniteLibraryLaunchState.Running ||
                         _model.Value.RunningGames?.Contains(pair.Key) == true).Select(pair => pair.Key).ToArray())
                RemoveLaunchStateLocked(id);
            UpdateIndexedHomePresentation();
            UpdateIndexedBrowsePresentation();
        }
        LoadPlaying();
        Invalidate();
    }

    private static WidgetModal LaunchDialog(PlayniteLaunchPrompt prompt)
    {
        var key = LaunchUi + prompt.Generation;
        var name = prompt.Conflict.Name;
        var subject = prompt.Conflict.Count == 1 ? name : $"{name} and {prompt.Conflict.Count - 1} more";
        return new WidgetModal(key, "Another game is running",
            UI.Stack(key + ".body",
                UI.Text($"Keep {subject} open and launch {prompt.Target.Item.Presentation.DisplayName}?", key + ".message"),
                UI.Row(key + ".actions",
                    UI.Button("Cancel", key + ".cancel", key + ".cancel"),
                    UI.Button("Launch anyway", key + ".confirm", key + ".confirm")).Classes("playnite-launch-actions")),
            key + ".cancel", key + ".cancel")
            { HeaderActions = UI.ControllerHint(ControllerButton.B, "Cancel", key + ".hint"),
                StyleClass = "playnite-launch-confirm", ScrollContent = false };
    }

    private WidgetView LaunchSurface(PlayniteLibraryItem item)
    {
        var game = item.Presentation;
        var id = LaunchUi + _launchGeneration.CurrentGeneration;
        var art = game.Artwork.Find(WidgetAppLibraryArtworkRole.Hero) ?? game.Artwork.Find(WidgetAppLibraryArtworkRole.Cover);
        var caption = UI.Stack(id + ".caption",
            UI.Row(id + ".progress", UI.LoadingIndicator(id + ".spinner"),
                UI.Text("Launching", id + ".status")).Classes("playnite-launch-progress"),
            UI.Text(game.DisplayName, id + ".title").Classes("playnite-launch-title")).Classes("playnite-launch-caption");
        var scene = UI.BackgroundSurface(caption, id + ".scene", art is { Handle.Length: > 0 }
            ? BackgroundSurfaceArtwork.FromHandle(new WidgetArtworkHandle(art.Handle)) : null).Classes("playnite-launch-scene");
        var root = UI.Stack("playnite-library.presentation", scene)
            .InputScope(id)
            .Classes("playnite-library-home-surface");
        return new(root, ActiveInputScopeId: id, Surface: PlayniteLibraryPresentation.CinematicSurface);
    }

    private void DismissLaunchPresentationLocked()
    {
        _launchGeneration.Invalidate();
        _model.Update(state => state with { LaunchingSavedId = null, LaunchPrompt = null, LaunchSplash = null });
    }

    private async ValueTask<bool> HandleLaunchPresentationAsync(WidgetActionEvent action, CancellationToken token)
    {
        PlayniteLaunchPrompt? accepted = null;
        lock (_gate)
        {
            if (_model.Value.LaunchPrompt is null) return _model.Value.LaunchSplash is not null;
            var key = LaunchUi + _launchGeneration.CurrentGeneration;
            if (action.ActionId == key + ".confirm" && _model.Value.LaunchPrompt is { } prompt) accepted = prompt;
            else if (action.ActionId != key + ".cancel") return true;
            DismissLaunchPresentationLocked();
        }
        Invalidate();
        if (accepted is not null)
        {
            await Operations.WhenIdleAsync("playnite-library.launch-lifecycle", token).ConfigureAwait(false);
            if (_launchGeneration.CurrentGeneration == accepted.Generation + 1 && accepted.Target.IsCurrent)
                await LaunchAsync(accepted.Target, token, accepted.Conflict.Revision).ConfigureAwait(false);
        }
        return true;
    }
}
