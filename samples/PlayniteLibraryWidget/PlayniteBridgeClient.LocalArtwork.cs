using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteBridgeClient
{
    internal const int MaximumArtworkPathsBytes = 8 * 1024 * 1024;
    private readonly object _artworkPathGate = new();
    private readonly CancellationTokenSource _artworkLifetime = new();
    private Task<IReadOnlyDictionary<string, LocalArtworkPaths>>? _artworkPaths;
    private int _disposed;
    private sealed record LocalArtworkPaths(string? Cover, string? Background);

    // Fixed read-only code, never caller-authored. Both count and total path text are
    // bounded inside Playnite before serialization. No image bytes are loaded here.
    internal const string ArtworkPathsScript = """
        var rows = new List<object>();
        int characters = 0;
        Func<string, string> resolve = path => {
            if (string.IsNullOrEmpty(path)) return null;
            try {
                var full = PlayniteApi.Database.GetFullFilePath(path);
                return full != null && full.Length <= 1024 ? full : null;
            } catch { return null; }
        };
        foreach (var game in PlayniteApi.Database.Games.Take(10000)) {
            var cover = resolve(game.CoverImage);
            var background = resolve(game.BackgroundImage);
            characters += (cover == null ? 0 : cover.Length) + (background == null ? 0 : background.Length);
            if (characters > 1000000) break;
            rows.Add(new { id = game.Id.ToString("D"), cover = cover, background = background });
        }
        return rows;
        """;

    internal static string ArtworkPathsRequestBody { get; } =
        JsonSerializer.Serialize(new { code = ArtworkPathsScript, timeoutMs = 3000, onUiThread = false });

    private Task<IReadOnlyDictionary<string, LocalArtworkPaths>> LocalArtworkPathsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Task<IReadOnlyDictionary<string, LocalArtworkPaths>> lookup;
        lock (_artworkPathGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            // A canceled page request stops waiting, not the single shared lookup.
            lookup = _artworkPaths ??= LoadLocalArtworkPathsAsync(_artworkLifetime.Token);
        }
        return lookup.WaitAsync(token);
    }

    private async Task<IReadOnlyDictionary<string, LocalArtworkPaths>> LoadLocalArtworkPathsAsync(CancellationToken token)
    {
        try
        {
            var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.ResolveArtworkPaths), token)
                .ConfigureAwait(false);
            if (response.StatusCode != 200 || response.Body.Length > MaximumArtworkPathsBytes) return EmptyPaths();
            using var document = JsonDocument.Parse(response.Body, new() { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("result", out var rows) || rows.ValueKind != JsonValueKind.Array ||
                rows.GetArrayLength() > MaximumGames) return EmptyPaths();
            var paths = new Dictionary<string, LocalArtworkPaths>(StringComparer.Ordinal);
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object ||
                    !TryString(row, "id", 36, out var rawId) || !Guid.TryParseExact(rawId, "D", out var id) ||
                    !paths.TryAdd(id.ToString("D"), new(ReadPath(row, "cover"), ReadPath(row, "background"))))
                    return EmptyPaths();
            }
            return paths;
        }
        catch (Exception error) when (error is PlayniteBridgeTransportException or
            PlayniteBridgeDataException or PlayniteCredentialException or JsonException ||
            error is OperationCanceledException && token.IsCancellationRequested)
        {
            return EmptyPaths();
        }

        static IReadOnlyDictionary<string, LocalArtworkPaths> EmptyPaths() =>
            new Dictionary<string, LocalArtworkPaths>();
    }

    private static string? ReadPath(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var path = value.GetString()!;
        // Only ordinary local drive paths; no UNC, device paths, URLs or alternate streams.
        return path.Length is >= 3 and <= 1024 && char.IsAsciiLetter(path[0]) &&
            path[1] == ':' && path[2] is '\\' or '/' && path.IndexOf(':', 2) < 0 &&
            !path.Contains('\0') && Path.IsPathFullyQualified(path) ? path : null;
    }

    private async ValueTask<PlayniteBridgeArtworkResult?> ReadLocalArtworkAsync(
        string gameId, PlayniteBridgeArtworkKind kind, CancellationToken token)
    {
        var paths = await LocalArtworkPathsAsync(token).ConfigureAwait(false);
        if (!paths.TryGetValue(gameId, out var entry)) return null;
        var path = kind switch
        {
            PlayniteBridgeArtworkKind.Cover => entry.Cover,
            PlayniteBridgeArtworkKind.Background => entry.Background,
            _ => null,
        };
        if (path is null) return null;
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length is <= 0 or > PlayniteBridgeHttpTransport.MaximumArtworkBytes) return null;
            var bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            var type = ClassifyArtworkContentType(bytes);
            return type is null ? null : new(new(type.Value, bytes), "resolved-local", ArtworkSizeClass(bytes.Length));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
