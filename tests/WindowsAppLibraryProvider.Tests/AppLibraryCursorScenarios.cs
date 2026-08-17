using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAppLibraryProvider;

internal static class AppLibraryCursorScenarios
{
    internal static async Task TraversesTenThousandWithoutFullPages()
    {
        var source = new CursorSource(10_000);
        await using var provider = new WindowsAppLibraryProvider(
            [source], CursorSta.Instance);
        var query = new AppLibraryBackendQuery(Kind: AppLibraryKind.Game);
        var first = await provider.QueryAppLibraryAsync(
            new(query, null, null, 64, Refresh: true), CancellationToken.None);
        Assert.Equal(64, first.Items.Count);
        Assert.Equal<string?>(null, first.Before);
        Assert.True(first.After is not null);

        var cursor = first.After;
        AppLibraryBackendCursorPage page = first;
        var pages = 1;
        var retained = page.Items.Count;
        while (cursor is not null)
        {
            page = await provider.QueryAppLibraryAsync(
                new(query, cursor, AppLibraryCursorDirection.After, 64),
                CancellationToken.None);
            Assert.True(page.Items.Count is > 0 and <= 64);
            retained = Math.Max(retained, page.Items.Count);
            cursor = page.After;
            pages++;
        }
        Assert.Equal(157, pages);
        Assert.Equal(64, retained);
        Assert.Equal(16, page.Items.Count);
        Assert.True(page.Before is not null);

        var previous = await provider.QueryAppLibraryAsync(
            new(query, page.Before, AppLibraryCursorDirection.Before, 64),
            CancellationToken.None);
        Assert.Equal(64, previous.Items.Count);
        Assert.Equal("Game 009920", previous.Items[0].DisplayName);
        Assert.Equal("Steam", previous.Items[0].SourceAttribution);
    }

    internal static async Task CursorsRejectTamperQueryAndRefreshChurn()
    {
        var source = new CursorSource(130);
        await using var provider = new WindowsAppLibraryProvider(
            [source], CursorSta.Instance);
        var query = new AppLibraryBackendQuery(Kind: AppLibraryKind.Game);
        var first = await provider.QueryAppLibraryAsync(
            new(query, null, null, 64, Refresh: true), CancellationToken.None);
        var cursor = first.After!;

        var tampered = cursor[..^1] + (cursor[^1] == '0' ? '1' : '0');
        await Assert.ThrowsAsync<BrokerException>(() => provider.QueryAppLibraryAsync(
            new(query, tampered, AppLibraryCursorDirection.After, 64),
            CancellationToken.None));
        await Assert.ThrowsAsync<BrokerException>(() => provider.QueryAppLibraryAsync(
            new(query with { SourceAttribution = "Steam" }, cursor,
                AppLibraryCursorDirection.After, 64), CancellationToken.None));
        await Assert.ThrowsAsync<BrokerException>(() => provider.QueryAppLibraryAsync(
            new(query, cursor, AppLibraryCursorDirection.Before, 64),
            CancellationToken.None));

        source.Count = 131;
        var refreshed = await provider.QueryAppLibraryAsync(
            new(query, null, null, 64, Refresh: true), CancellationToken.None);
        Assert.True(refreshed.Revision != first.Revision);
        await Assert.ThrowsAsync<BrokerException>(() => provider.QueryAppLibraryAsync(
            new(query, cursor, AppLibraryCursorDirection.After, 64),
            CancellationToken.None));
        Assert.Equal(64, refreshed.Items.Count);

        source.Count = 17;
        var afterDeletion = await provider.QueryAppLibraryAsync(
            new(query, null, null, 64, Refresh: true), CancellationToken.None);
        Assert.Equal(17, afterDeletion.Items.Count);
        Assert.Equal<string?>(null, afterDeletion.After);
        await Assert.ThrowsAsync<BrokerException>(() => provider.QueryAppLibraryAsync(
            new(query, refreshed.After, AppLibraryCursorDirection.After, 64),
            CancellationToken.None));
    }

    internal static async Task QueryCriteriaAreRevisionBound()
    {
        var source = new CursorSource(130);
        await using var provider = new WindowsAppLibraryProvider([source], CursorSta.Instance);
        var query = new AppLibraryBackendQuery(Kind: AppLibraryKind.Game,
            Sort: AppLibrarySortOrder.DisplayNameDescending)
        {
            SearchText = "Game 0001",
            StableIdentityFilter = ["stable-000100", "stable-000101"],
        };
        var page = await provider.QueryAppLibraryAsync(
            new(query, null, null, 64, Refresh: true), CancellationToken.None);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Game 000101", page.Items[0].DisplayName);
        Assert.Equal("Game 000100", page.Items[1].DisplayName);
        var empty = await provider.QueryAppLibraryAsync(
            new(query with { StableIdentityFilter = [] }, null, null, 64, Refresh: true),
            CancellationToken.None);
        Assert.Equal(0, empty.Items.Count);
        var unfilteredQuery = query with
        {
            SearchText = "Game",
            StableIdentityFilter = null,
        };
        var unfiltered = await provider.QueryAppLibraryAsync(
            new(unfilteredQuery, null, null, 64, Refresh: true), CancellationToken.None);
        Assert.True(unfiltered.After is not null);
        await Assert.ThrowsAsync<BrokerException>(() => provider.QueryAppLibraryAsync(
            new(unfilteredQuery with { StableIdentityFilter = [] }, unfiltered.After,
                AppLibraryCursorDirection.After, 64), CancellationToken.None));
        await Assert.ThrowsAsync<BrokerException>(() => provider.QueryAppLibraryAsync(
            new(query with { SearchText = "Game" }, page.After ?? "invalid",
                AppLibraryCursorDirection.After, 64), CancellationToken.None));
    }

    internal static async Task ControlledLaunchEvidenceIsPreserved()
    {
        var source = new CursorSource(1)
        {
            LaunchResult = new(AppLibraryLaunchObservationState.Running,
                GameLibraryLaunchEvidence.LauncherStarted |
                GameLibraryLaunchEvidence.Running |
                GameLibraryLaunchEvidence.Ended),
        };
        await using var provider = new WindowsAppLibraryProvider(
            [source], CursorSta.Instance);
        var page = await provider.QueryAppLibraryAsync(
            new(new(Kind: AppLibraryKind.Game), null, null, 1, Refresh: true),
            CancellationToken.None);

        var running = await provider.LaunchAppLibraryItemObservedAsync(
            page.Items.Single().ProviderAppId, CancellationToken.None);
        Assert.Equal(AppLibraryLaunchObservationState.Running, running.State);
        Assert.True(running.SupportsRunning);
        Assert.True(running.SupportsEnded);

        source.LaunchResult = new(AppLibraryLaunchObservationState.Ended,
            GameLibraryLaunchEvidence.LauncherStarted |
            GameLibraryLaunchEvidence.Running |
            GameLibraryLaunchEvidence.Ended);
        var ended = await provider.LaunchAppLibraryItemObservedAsync(
            page.Items.Single().ProviderAppId, CancellationToken.None);
        Assert.Equal(AppLibraryLaunchObservationState.Ended, ended.State);
    }

    private sealed class CursorSource(int count) : IGameLibrarySource
    {
        internal int Count { get; set; } = count;
        internal GameLibraryLaunchResult LaunchResult { get; set; } = new(
            AppLibraryLaunchObservationState.RequestAccepted,
            GameLibraryLaunchEvidence.None);
        public string SourceIdentity => "source-cursor";
        public string Attribution => "Steam";
        public GameLibrarySourceSnapshot Snapshot { get; private set; } =
            new("source-cursor", "Steam", 0, GameLibrarySourceHealth.Unavailable, []);

        public GameLibrarySourceSnapshot Refresh(CancellationToken cancellationToken)
        {
            var items = Enumerable.Range(0, Count).Select(index =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new GameLibrarySourceItem(
                    SourceIdentity, Attribution, $"stable-{index:D6}",
                    $"Game {index:D6}", WindowsAppLibraryKind.Game,
                    true, true, GameLibrarySourceActions.Launch,
                    $"art-{index:D6}", $"item-{index:D6}");
            }).ToArray();
            Snapshot = new(SourceIdentity, Attribution, Snapshot.SourceVersion + 1,
                GameLibrarySourceHealth.Healthy, items);
            return Snapshot;
        }

        public GameLibrarySourceItem? ResolveExact(
            GameLibrarySourceItem item, CancellationToken cancellationToken) => item;
        public GameLibraryLaunchResult Launch(GameLibrarySourceItem exactItem,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LaunchResult;
        }
        public string? LoadArtwork(GameLibrarySourceItem exactItem,
            CancellationToken cancellationToken) => null;
        public void Dispose() { }
    }

    private sealed class CursorSta : IShellStaExecutor
    {
        internal static CursorSta Instance { get; } = new();
        public Task<T> RunAsync<T>(Func<CancellationToken, T> operation,
            CancellationToken cancellationToken) => Task.FromResult(operation(cancellationToken));
    }
}
