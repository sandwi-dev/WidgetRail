using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal interface IGameLauncherApplicationService : IAsyncDisposable
{
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

    ValueTask<WidgetPrivateStateValue<GameLauncherPrivateState>> ReadStateAsync(
        CancellationToken cancellationToken);

    ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
        GameLauncherPrivateState state,
        long? expectedRevision,
        CancellationToken cancellationToken);
}

internal sealed class HostGameLauncherApplicationService(
    Func<WidgetHostServices> services) : IGameLauncherApplicationService
{
    private WidgetHostServices Services => services();

    public ValueTask<WidgetAppLibraryPage> QueryAsync(
        WidgetAppLibraryQuery query,
        WidgetCollectionCursor? cursor,
        WidgetCursorDirection? direction,
        int limit,
        bool refresh,
        CancellationToken cancellationToken) => Services.AppLibrary.QueryAsync(
            query, cursor, direction, limit, refresh, cancellationToken);

    public ValueTask<IReadOnlyList<WidgetAppLibraryItem>> ResolveSavedAsync(
        IReadOnlyList<string> savedIds,
        CancellationToken cancellationToken) => Services.AppLibrary.ResolveSavedAsync(
            savedIds, cancellationToken);

    public ValueTask<WidgetRunningAppObservation> ObserveRunningAsync(
        CancellationToken cancellationToken) =>
        Services.AppLibrary.ObserveRunningAsync(cancellationToken);

    public ValueTask<WidgetAppLibraryItem?> ConfirmRunningAsync(
        string savedId,
        string revision,
        CancellationToken cancellationToken) => Services.AppLibrary.ConfirmRunningAsync(
            savedId, revision, cancellationToken);

    public ValueTask<WidgetAppLaunchObservation> LaunchObservedAsync(
        string appId,
        WidgetAppLaunchOverlayBehavior overlayBehavior,
        CancellationToken cancellationToken) => Services.AppLibrary.LaunchObservedAsync(
            appId, overlayBehavior, cancellationToken);

    public ValueTask<WidgetPrivateStateValue<GameLauncherPrivateState>> ReadStateAsync(
        CancellationToken cancellationToken) =>
        Services.PrivateState.ReadAsync<GameLauncherPrivateState>(
            cancellationToken: cancellationToken);

    public ValueTask<WidgetPrivateStateMutation> WriteStateAsync(
        GameLauncherPrivateState state,
        long? expectedRevision,
        CancellationToken cancellationToken) => Services.PrivateState.WriteAsync(
            state, expectedRevision, cancellationToken: cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
