using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

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

    ValueTask<string?> ResolveArtworkAsync(
        WidgetAppLibraryArtwork artwork,
        CancellationToken cancellationToken);

    ValueTask<WidgetPrivateStateValue<PlayniteLibraryPrivateState>> ReadStateAsync(
        CancellationToken cancellationToken);

    ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
        PlayniteLibraryPrivateState state,
        long? expectedRevision,
        CancellationToken cancellationToken);
}
