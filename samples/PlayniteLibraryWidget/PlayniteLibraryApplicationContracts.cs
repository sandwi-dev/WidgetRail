using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteLibraryQueryScope
{
    Home,
    Library,
    RecentlyPlayed,
    Hidden,
    Category,
}

internal sealed record PlayniteLibraryQueryContext(
    PlayniteLibraryQueryScope Scope,
    string? CategoryName = null);

internal sealed record PlayniteLibraryAuthorityProjection(
    IReadOnlyList<string> FavoriteGameIds,
    IReadOnlyList<string> HiddenGameIds,
    IReadOnlyList<PlayniteLibraryCategory> Categories,
    IReadOnlyDictionary<string, string?> CompletionStatuses)
{
    // Projected from the full catalog before applying Home/Library filters.
    // Every known game has a key, including games with no completion status.
    internal bool ContainsGame(string savedId) => CompletionStatuses.ContainsKey(savedId);

    internal static PlayniteLibraryAuthorityProjection Empty { get; } =
        new([], [], [], new Dictionary<string, string?>(StringComparer.Ordinal));
}

internal sealed record PlayniteLibraryQueryResult(
    WidgetAppLibraryPage Page,
    PlayniteLibraryAuthorityProjection Authority,
    bool RetainedLastGood = false)
{
    public int? MatchingGameCount { get; init; }
}

internal interface IPlayniteLibraryApplicationService : IAsyncDisposable
{
    ValueTask<SteamAnnouncements> GetAnnouncementsAsync(uint appId, CancellationToken token) =>
        ValueTask.FromResult(new SteamAnnouncements(appId, []));
    ValueTask<bool> SetGameWindowAsync(string gameId, PlayniteWindowBinding run, string match, CancellationToken token) => ValueTask.FromResult(false);
    ValueTask<PlayniteWidgetLaunchResult> ReturnToGameAsync(string appId, WidgetHostServices host, Func<bool> current, CancellationToken token) =>
        ValueTask.FromResult(new PlayniteWidgetLaunchResult(new(WidgetAppLaunchObservationState.Ended, false, true), "The game is no longer available."));

    ValueTask<SteamStoreMedia> GetStoreMediaAsync(PlayniteStoreLookup lookup, CancellationToken token) =>
        ValueTask.FromResult(SteamStoreMedia.Empty with { PublisherTrailerId = lookup.LinkedTrailer });
    ValueTask<PlayniteCaptureSettings> GetCaptureSettingsAsync(CancellationToken token) => ValueTask.FromResult(PlayniteCaptureSettings.Unavailable);
    async ValueTask<PlayniteCaptureSettings> GetCaptureSettingsForConnectionAsync(string connectionId,CancellationToken token)
    {var value=await GetCaptureSettingsAsync(token);return value.ConnectionId==connectionId?value:throw new PlayniteBridgeDataException("capture_connection_changed");}
    ValueTask<PlayniteCaptureSettings> UpdateCaptureSettingsAsync(PlayniteCaptureSettings current, IReadOnlyDictionary<string, System.Text.Json.JsonElement> patch, CancellationToken token) => ValueTask.FromException<PlayniteCaptureSettings>(new NotSupportedException());
    ValueTask<PlayniteCaptureIntegration?> GetCaptureIntegrationAsync(CancellationToken token, string? gameId = null) => ValueTask.FromResult<PlayniteCaptureIntegration?>(null);
    ValueTask<PlayniteCaptureIntegration> ConfigureCaptureIntegrationAsync(PlayniteCaptureIntegration current, string path, bool enabled, CancellationToken token) => ValueTask.FromException<PlayniteCaptureIntegration>(new NotSupportedException());
    ValueTask<PlayniteProviderRefreshResult> RefreshAchievementsAsync(string gameId, CancellationToken token) =>
        ValueTask.FromResult(PlayniteProviderRefreshResult.Unavailable);
    ValueTask<PlayniteEventBatch?> ReadEventsAsync(long after, CancellationToken token) => ValueTask.FromResult<PlayniteEventBatch?>(null);
    ValueTask ResetConnectionCacheAsync() => ValueTask.CompletedTask;
    async ValueTask<WidgetArtworkSource?> ResolveArtworkSourceAsync(WidgetArtworkHandle handle, CancellationToken token) =>
        await ResolveArtworkAsync(handle, token).ConfigureAwait(false);
    bool OwnsArtworkContent { get; }
    ValueTask<PlayniteLibraryCapturedQuery> CaptureQueryAsync(WidgetAppLibraryQuery query,
        PlayniteLibraryQueryContext context, bool refresh, CancellationToken token) =>
        ValueTask.FromException<PlayniteLibraryCapturedQuery>(new NotSupportedException("This application service does not support captured queries."));
    async ValueTask<PlayniteGameDetails?> GetGameDetailsAsync(string gameId, CancellationToken token)
    {
        var items = await ResolveSavedAsync([gameId], token).ConfigureAwait(false);
        var item = items.SingleOrDefault(value => value.SavedId == gameId);
        return item is null ? null : new(item);
    }
    ValueTask<PlayniteAchievementOverview> GetAchievementOverviewAsync(CancellationToken token) => ValueTask.FromResult(PlayniteAchievementOverview.Unavailable);
    ValueTask<PlayniteAchievements> GetAchievementsAsync(string gameId, CancellationToken token) => ValueTask.FromResult(PlayniteAchievements.Unavailable);
    ValueTask<PlayniteActivity> GetActivityAsync(string gameId, CancellationToken token) => ValueTask.FromResult(PlayniteActivity.Unavailable);
    ValueTask<PlayniteRecentActivity> GetRecentActivityAsync(CancellationToken token) =>
        ValueTask.FromResult(PlayniteRecentActivity.Empty);
    ValueTask<bool> ChangeInstallationAsync(string gameId, bool install, CancellationToken token) => ValueTask.FromResult(false);

    ValueTask<WidgetAppLibraryPage> QueryAsync(
        WidgetAppLibraryQuery query,
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        bool refresh,
        CancellationToken cancellationToken);

    async ValueTask<PlayniteLibraryQueryResult> QueryWithAuthorityAsync(
        WidgetAppLibraryQuery query,
        PlayniteLibraryQueryContext context,
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        bool refresh,
        CancellationToken cancellationToken) => new(
            await QueryAsync(query, cursor, direction, limit, refresh, cancellationToken)
                .ConfigureAwait(false),
            PlayniteLibraryAuthorityProjection.Empty);

    ValueTask<IReadOnlyList<WidgetAppLibraryItem>> ResolveSavedAsync(
        IReadOnlyList<string> savedIds,
        CancellationToken cancellationToken);

    ValueTask<WidgetRunningAppObservation> ObserveRunningAsync(
        CancellationToken cancellationToken);

    ValueTask<WidgetAppLibraryItem?> ConfirmRunningAsync(
        string savedId,
        string revision,
        CancellationToken cancellationToken);

    ValueTask<WidgetAppLaunchObservation> LaunchObservedAsync(
        string appId,
        WidgetAppLaunchOverlayBehavior overlayBehavior,
        CancellationToken cancellationToken);

    async ValueTask<PlayniteWidgetLaunchResult> LaunchFromWidgetAsync(string appId, WidgetHostServices host, Func<bool> current, CancellationToken token,
        string? approval, Action admitted) =>
        new(await LaunchObservedAsync(appId, WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess, token).ConfigureAwait(false));

    ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(
        WidgetArtworkHandle handle,
        CancellationToken cancellationToken);

    void PinArtworkHandles(IReadOnlyList<string> handles) { }

    ValueTask<WidgetAppLibraryItem?> SetFavoriteAsync(
        string gameId, bool favorite, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetAppLibraryItem?>(null);

    ValueTask<WidgetAppLibraryItem?> SetHiddenAsync(
        string gameId, bool hidden, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetAppLibraryItem?>(null);

    ValueTask<WidgetAppLibraryItem?> SetCategoryMembershipAsync(
        string gameId, string categoryName, bool included,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetAppLibraryItem?>(null);

    ValueTask<WidgetAppLibraryItem?> SetCompletionStatusAsync(
        string gameId, string completionStatus, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetAppLibraryItem?>(null);

    ValueTask<PlayniteLibraryCategory?> CreateCategoryAsync(
        string name, CancellationToken cancellationToken) =>
        ValueTask.FromResult<PlayniteLibraryCategory?>(null);

    ValueTask<IReadOnlyList<string>> GetCompletionStatusesAsync(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<string>>([]);

    ValueTask<WidgetPrivateStateValue<PlayniteLibraryPrivateState>> ReadStateAsync(
        CancellationToken cancellationToken);

    ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
        PlayniteLibraryPrivateState state,
        long? expectedRevision,
        CancellationToken cancellationToken);
}
