using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GameLauncher;

internal interface IGameLauncherApplicationService : IAsyncDisposable
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

    ValueTask<WidgetPrivateStateValue<GameLauncherPrivateState>> ReadStateAsync(
        CancellationToken cancellationToken);

    ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
        GameLauncherPrivateState state,
        long? expectedRevision,
        CancellationToken cancellationToken);
}
