using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetRuntime;

// This executable is never shipped. Only its provider is fake; widget, application
// service, state persistence and the supervised process protocol are production code.
var fixtureRoot = Environment.GetEnvironmentVariable("WRAIL_PLAYNITE_PROCESS_FIXTURE_ROOT");
if (string.IsNullOrWhiteSpace(fixtureRoot) || !Path.IsPathFullyQualified(fixtureRoot) ||
    !File.Exists(Path.Combine(fixtureRoot, "fixture-owned"))) return 2;
return await WidgetApplicationBootstrap.RunAsync(args, () =>
{
    var client = new JournalClient(fixtureRoot);
    return new PlayniteLibraryWidget(new PlayniteLibraryApplicationService(client,
        new PlayniteLibraryStateFileStore(Path.Combine(fixtureRoot, "state.json"))), client);
});

file sealed class JournalClient(string root) : IPlayniteLibraryBridgeClient, IPlayniteBridgeClient
{
    private readonly PlayniteBridgeGame[] games = Enumerable.Range(1, 3).Select(index =>
        new PlayniteBridgeGame($"00000000-0000-0000-0000-{index:D12}",
            index == 2 ? "Process Target" : $"Other Game {index}", "Fixture",
            true, false, false, null, [], [], ["Windows"], 120, null)
        { Description = "Cross-process production details" }).ToArray();

    public ValueTask<PlayniteBridgeGamePage> QueryGamesAsync(PlayniteBridgeGameQuery query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new PlayniteBridgeGamePage(games.Length, query.Offset,
            query.Limit, games.Skip(query.Offset).Take(query.Limit).ToArray()));
    }
    public ValueTask<PlayniteBridgeGame?> ResolveGameAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(games.SingleOrDefault(game => game.Id == id));
    }
    public ValueTask<PlayniteBridgeArtworkResult> ResolveArtworkAsync(string id, PlayniteBridgeArtworkKind kind, CancellationToken token)
        => ValueTask.FromResult(new PlayniteBridgeArtworkResult(null, "not-found", "none"));
    public async ValueTask<bool> LaunchAsync(string id, CancellationToken token)
    {
        if (!games.Any(game => game.Id == id)) throw new InvalidOperationException("Unknown fixture game.");
        await File.AppendAllTextAsync(Path.Combine(root, "launches.txt"), id + Environment.NewLine, token);
        return true;
    }
    // Unused mutations fail loudly so adding workflow coverage cannot silently
    // bypass either the fake's contract or real system-action restrictions.
    public ValueTask<PlayniteBridgeGame?> SetFavoriteAsync(string id, bool value, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<PlayniteBridgeGame?> SetHiddenAsync(string id, bool value, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<PlayniteBridgeGame?> SetCategoriesAsync(string id, IReadOnlyList<string> values, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<PlayniteBridgeGame?> SetCompletionStatusAsync(string id, string value, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<PlayniteBridgeNamedItem?> CreateCategoryAsync(string name, CancellationToken token) => throw new NotSupportedException();
    public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCategoriesAsync(CancellationToken token) => ValueTask.FromResult<IReadOnlyList<PlayniteBridgeNamedItem>>([]);
    public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCompletionStatusesAsync(CancellationToken token) => ValueTask.FromResult<IReadOnlyList<PlayniteBridgeNamedItem>>([]);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
    public ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(CancellationToken token) =>
        ValueTask.FromResult(new PlayniteBridgeConnectionResult(PlayniteBridgeConnectionKind.Connected, "fixture"));
    public ValueTask SaveCredentialAsync(string value, CancellationToken token) => throw new NotSupportedException();
    public ValueTask DeleteCredentialAsync(CancellationToken token) => throw new NotSupportedException();
}
