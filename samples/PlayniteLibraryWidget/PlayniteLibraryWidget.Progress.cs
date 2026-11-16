using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteProgressTarget(string ConnectionId, PlayniteAchievementProvider Provider, string GameId,
    string GameName, string AchievementId, PlayniteAchievement Initial);
internal sealed record PlayniteProgressState(PlayniteProgressTarget? Target = null, bool Pending = false);

public sealed partial class PlayniteLibraryWidget
{
    private readonly PinnedLayoutHandle progressPin;
    private readonly WidgetQueryResource<PlayniteProgressTarget, PlayniteAchievements> progressData;

    private void UpdateProgress(Func<PlayniteProgressState, PlayniteProgressState> update) =>
        _model.Update(state => state with { Progress = update(state.Progress) });

    private (PinnedLayoutHandle, WidgetQueryResource<PlayniteProgressTarget, PlayniteAchievements>) CreateProgressServices()
    {
        var pin = CreatePinnedLayoutHandle("playnite.achievement-progress", "Achievement progress", new()
        {
            PreferredWidth = 340, PreferredHeight = 180, MinimumWidth = 300, MinimumHeight = 180,
            Appearance = WidgetSurfaceAppearance.Transparent,
        }, PinnedInteractionMode.Passive);
        var dataResource = CreateQueryResource<PlayniteProgressTarget, PlayniteAchievements>("playnite-library.progress-data", new()
        {
            Load = async (target, token) =>
            {
                var data = await _application.GetAchievementsAsync(target.GameId, token).ConfigureAwait(false);
                if (data.ConnectionId != target.ConnectionId || data.Provider != target.Provider)
                    throw new PlayniteBridgeDataException("connection_changed");
                return data;
            },
            MapError = _ => new("progress_unavailable", "Progress unavailable. Check Playnite."),
            CacheDuration = TimeSpan.Zero, RetainLastGoodValue = true,
        });
        return (pin, dataResource);
    }

    private WidgetView WithProgressPin(WidgetView page)
    {
        if (_model.Value.Progress.Target is not { } target) return page;
        var item = CurrentProgressItem(target);
        var children = new List<WidgetElement>
        {
            UI.Text(target.GameName, "playnite.progress.game").Classes("playnite-library-progress-game"),
            UI.Grid("playnite.progress.heading", [GridTrack.Auto()], [GridTrack.Pixel(36), GridTrack.Star()],
                ProgressArtwork(target, item).Classes("playnite-library-progress-artwork"),
                UI.Text(item?.Name ?? "Progress unavailable", "playnite.progress.name")
                    .Classes("playnite-library-progress-title").InGrid(column: 1)).Spacing(0, 10),
        };
        if (item is null)
            children.Add(UI.Text("Open Playnite to check this achievement.", "playnite.progress.status").Classes("playnite-library-progress-game"));
        else
        {
            var progress = item.Unlocked ? target.Initial.Progress! with { Current = target.Initial.Progress!.Maximum } : item.Progress!;
            children.Add(UI.Row("playnite.progress.numbers",
                UI.Text(item.Unlocked ? "Completed" : $"{progress.Current:N0} / {progress.Maximum:N0}", "playnite.progress.status")
                    .Classes(item.Unlocked ? "playnite-library-positive" : "playnite-library-progress-count"),
                UI.Text($"{100d * progress.Current / progress.Maximum:0}%", "playnite.progress.percent")
                    .Classes("playnite-library-progress-game")).Classes("playnite-library-progress-numbers"));
            children.Add(UI.Progress(progress.Current, progress.Maximum, "playnite.progress.bar"));
        }
        return page with { PinnedLayouts = [progressPin.Present(UI.Grid("playnite.progress.surface", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            UI.Stack("playnite.progress", children.ToArray()).Classes("playnite-library-progress-pin").InGrid(row: 0))) with { IndependentSelection = true, Name = "Achievement progress · " + BoundProgress(target.GameName, 96) }] };
    }

    private PlayniteAchievement? CurrentProgressItem(PlayniteProgressTarget target)
    {
        var snapshot = progressData.GetSnapshot(target);
        var matches = snapshot.Status == WidgetResourceStatus.Error ? null :
            snapshot.Value?.Items.Where(item => item.Id == target.AchievementId).Take(2).ToArray();
        var item = matches?.Length == 1 ? matches[0] : !snapshot.HasValue &&
            snapshot.Status is WidgetResourceStatus.NotLoaded or WidgetResourceStatus.Loading ? target.Initial : null;
        // A filtered/removed entry is unavailable. Never expose a newly hidden
        // achievement's title or replace it with a similar display name.
        if (item is { Hidden: true, Unlocked: false }) item = null;
        if (item is { Unlocked: false } && !PlayniteAchievementPresentation.CanTrack(item)) item = null;
        return item;
    }

    private WidgetElement ProgressArtwork(PlayniteProgressTarget target, PlayniteAchievement? item) =>
        item?.Icon is { Length: > 0 } icon
            ? ArtworkFileContract.IsLocalPath(icon)
                ? UI.Artwork(ProgressArtworkHandle(target, icon), "playnite.progress.icon", "Achievement artwork", ImageFit.Contain)
                : UI.Image(icon, "playnite.progress.icon", "Achievement artwork", ImageFit.Contain)
            : UI.Icon(PlayniteIcons.Achievements, "playnite.progress.icon", "Achievement");

    private static WidgetArtworkHandle ProgressArtworkHandle(PlayniteProgressTarget target, string icon) => new(
        "playnite.progress." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new[]
                { target.ConnectionId, target.Provider.ToString(), target.GameId, target.AchievementId, icon })))));

    private WidgetArtworkSource? ResolveProgressArtwork(WidgetArtworkHandle handle)
    {
        if (_model.Value.Progress.Target is not { } target || CurrentProgressItem(target)?.Icon is not { } path ||
            !ArtworkFileContract.IsLocalPath(path) || handle != ProgressArtworkHandle(target, path)) return null;
        return ReferenceEquals(_model.Value.Progress.Target, target) ? new WidgetFileArtwork(path, handle.Value) : null;
    }

    private async ValueTask TrackAchievementAsync(PlayniteAchievementBrowse query, PlayniteAchievementEntry entry, CancellationToken token)
    {
        var state = WithDetailsResources(_model.Value);
        if (LifecycleState != WidgetLifecycleState.Interactive || state.DetailsCapturedTarget is { IsCurrent: false } || query.Generation != state.DetailsGeneration || entry.Secret || !PlayniteAchievementPresentation.CanTrack(entry.Item) ||
            entry.Item.Id is not { Length: > 0 } id || state.DetailsItem is not { } game || query.State.Achievements?.ConnectionId is not { } connection ||
            !ReferenceEquals(query.State.Achievements, state.DetailsExtras.Achievements)) return;
        if (_model.Value.Progress.Pending) return;
        var selected = progressPin.IsSelected;
        var target = new PlayniteProgressTarget(connection, query.State.Achievements.Provider, game.Value.SavedId,
            game.Value.Presentation.DisplayName, id, entry.Item);
        // Same layout, new domain target: update the selected window in place.
        // QueryResource retires the previous value/work before admitting this target.
        UpdateProgress(_ => new(target, Pending: !selected));
        progressData.EnsureLoaded(target, selected ? progressPin.SelectionCancellationToken : token);
        if (selected) return;
        try
        {
            var result = await HostServices.Pinning.RequestOpenAsync(progressPin, token).ConfigureAwait(false);
            if (!result.Accepted) { UpdateProgress(_ => new()); progressData.Reset(); }
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
        {
            UpdateProgress(_ => new()); progressData.Reset();
            UpdateDetails(state.DetailsGeneration, value => value with { AchievementRefreshMessage = "The progress pin could not open. Check the host's pin limit." });
        }
        catch (OperationCanceledException)
        {
            if (!progressPin.IsSelected) { UpdateProgress(_ => new()); progressData.Reset(); }
            throw;
        }
        finally
        {
            // The receipt only admits a host request. Capacity/replacement UI or
            // later host eligibility may prevent a selection notification.
            UpdateProgress(value => ReferenceEquals(value.Target, target) ? value with { Pending = false } : value);
        }
    }

    public override ValueTask OnPinnedSurfaceChangedAsync(string layoutId, bool selected, CancellationToken cancellationToken = default)
    {
        if (layoutId != progressPin.Id) return ValueTask.CompletedTask;
        if (!selected) { UpdateProgress(_ => new()); progressData.Reset(); }
        else if (_model.Value.Progress.Target is { } target)
        {
            UpdateProgress(value => value with { Pending = false });
            progressData.EnsureLoaded(target, progressPin.SelectionCancellationToken, forceRefresh: true);
        }
        return ValueTask.CompletedTask;
    }

    private void HandleProgressUpdates(PlayniteEventBatch batch)
    {
        if (_model.Value.Progress.Target is { } target)
        {
            if (target.ConnectionId != batch.ConnectionId) { UpdateProgress(_ => new()); progressData.Reset(); }
            else if (batch.ResetRequired || batch.Changes.Any(change => change.GameId is null || change.GameId == target.GameId))
                progressData.EnsureLoaded(target, progressPin.SelectionCancellationToken, forceRefresh: true);
        }

    }
    private static string BoundProgress(string text, int limit) => text.Length <= limit ? text : text[..(char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit)];
}
