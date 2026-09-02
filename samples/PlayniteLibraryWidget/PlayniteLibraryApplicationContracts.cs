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
    internal static PlayniteLibraryAuthorityProjection Empty { get; } =
        new([], [], [], new Dictionary<string, string?>(StringComparer.Ordinal));
}

internal sealed record PlayniteLibraryQueryResult(
    WidgetAppLibraryPage Page,
    PlayniteLibraryAuthorityProjection Authority,
    bool RetainedLastGood = false);

internal interface IPlayniteLibraryApplicationService : IAsyncDisposable
{
    bool OwnsArtworkContent { get; }

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
