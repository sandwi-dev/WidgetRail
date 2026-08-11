using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsAppLibraryProvider;

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

    private sealed class CursorSource(int count) : IGameLibrarySource
    {
        internal int Count { get; set; } = count;
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
        public void Launch(GameLibrarySourceItem exactItem,
            CancellationToken cancellationToken) => cancellationToken.ThrowIfCancellationRequested();
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
