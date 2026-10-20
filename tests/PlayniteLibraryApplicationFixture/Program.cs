using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using System.Text.Json;

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

file sealed class JournalClient : IPlayniteLibraryBridgeClient, IPlayniteBridgeClient
{
    private readonly string root;
    private readonly PerformanceOptions? performance;
    private readonly PlayniteBridgeGame[] games;
    public JournalClient(string root)
    {
        this.root=root;
        var path=Path.Combine(root,"performance.json");
        if(File.Exists(path))
        {
            performance=JsonSerializer.Deserialize<PerformanceOptions>(File.ReadAllText(path))
                ??throw new InvalidDataException("Missing performance fixture configuration.");
            if(performance.GameCount is < 1 or > 20000 || performance.DelayMilliseconds is < 0 or > 1000)
                throw new InvalidDataException("Invalid performance fixture bounds.");
        }
        games=Enumerable.Range(1,performance?.GameCount??3).Select(index =>
        new PlayniteBridgeGame($"00000000-0000-0000-0000-{index:D12}",
            performance is not null?$"Fixture Game {index:D5}":index == 2 ? "Process Target" : $"Other Game {index}", "Fixture",
            true, false, false, null, [], [], ["Windows"], 120, null)
        { Description = "Cross-process production details" }).ToArray();
    }

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
    public async ValueTask<PlayniteBridgeArtworkResult> ResolveArtworkAsync(string id, PlayniteBridgeArtworkKind kind, CancellationToken token)
    {
        if(performance is null||!performance.Artwork)return new(null,"not-found","none");
        if(!Guid.TryParse(id,out _)||!int.TryParse(id.AsSpan(id.Length-12),out var index)||index<1||index>games.Length)
            throw new InvalidDataException("Unknown fixture artwork.");
        if(performance.DelayMilliseconds>0)await Task.Delay(performance.DelayMilliseconds,token);
        var bytes=await File.ReadAllBytesAsync(Path.Combine(root,"artwork",$"{(kind==PlayniteBridgeArtworkKind.Background?"background":"cover")}-{index%8}.jpg"),token);
        return new(new WidgetEncodedArtwork(WidgetArtworkContentType.Jpeg,bytes),"fixture","large");
    }
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

file sealed record PerformanceOptions(int GameCount=2000,bool Artwork=true,int DelayMilliseconds=15);
