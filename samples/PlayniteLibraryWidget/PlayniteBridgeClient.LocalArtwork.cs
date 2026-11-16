using System.Text.Json;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteBridgeClient
{
    private readonly object _artworkPathGate = new();
    private readonly CancellationTokenSource _artworkLifetime = new();
    private Task<IReadOnlyDictionary<string, LocalArtworkPaths>>? _artworkPaths;
    private int _disposed;
    private readonly string _artworkPathRevision = Guid.NewGuid().ToString("N");
    private sealed record LocalArtworkPaths(string? Cover, string? Background, string? CoverRevision = null, string? BackgroundRevision = null);

    public async ValueTask<WidgetFileArtwork?> ResolveArtworkFileAsync(
        string gameId, PlayniteBridgeArtworkKind kind, CancellationToken cancellationToken)
    {
        if (!preferLocalArtwork) return null;
        var paths = await LocalArtworkPathsAsync(cancellationToken).ConfigureAwait(false);
        if (!paths.TryGetValue(CanonicalGameId(gameId), out var entry)) return null;
        var path = kind switch { PlayniteBridgeArtworkKind.Cover => entry.Cover,
            PlayniteBridgeArtworkKind.Background => entry.Background, _ => null };
        var revision = kind == PlayniteBridgeArtworkKind.Cover ? entry.CoverRevision : entry.BackgroundRevision;
        return path is null ? null : new WidgetFileArtwork(path, revision is null ? _artworkPathRevision : _artworkPathRevision + ":" + revision);
    }

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

    internal void ResetArtworkPathCache()
    {
        lock (_artworkPathGate) _artworkPaths = null;
    }

    private async Task<IReadOnlyDictionary<string, LocalArtworkPaths>> LoadLocalArtworkPathsAsync(CancellationToken token)
    {
        try
        {
            return await LoadTypedArtworkPathsAsync(token).ConfigureAwait(false);
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

    private async Task<IReadOnlyDictionary<string, LocalArtworkPaths>> LoadTypedArtworkPathsAsync(CancellationToken token)
    {
        var paths = new Dictionary<string, LocalArtworkPaths>(StringComparer.Ordinal);
        string? revision = null;
        var characters = 0;
        var total = MaximumGames;
        for (var offset = 0; offset < total;)
        {
            var response = await SendRequiredAsync(new(PlayniteBridgeCommandKind.ResolveArtworkPaths)
                { ArtworkOffset = offset, ArtworkLimit = 128, Revision = revision }, token).ConfigureAwait(false);
            EnsureSuccess(response);
            using var document = JsonDocument.Parse(response.Body, new() { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryNonNegativeInteger(root, "total", out var count) || count > MaximumGames ||
                !TryNonNegativeInteger(root, "offset", out var position) || position != offset ||
                !root.TryGetProperty("artwork", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 128 ||
                OptionalString(root, "revision", 80) is not { } current || revision is not null && revision != current)
                throw new PlayniteBridgeDataException("invalid_playnite_data");
            if (revision is not null && count != total) throw new PlayniteBridgeDataException("invalid_playnite_data");
            revision = current; total = count;
            if (rows.GetArrayLength() == 0 && offset != total) throw new PlayniteBridgeDataException("invalid_playnite_data");
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !TryString(row, "id", 36, out var rawId) ||
                    !Guid.TryParseExact(rawId, "D", out var id) || !paths.TryAdd(id.ToString("D"),
                        new(ReadPath(row, "cover"), ReadPath(row, "background"), OptionalString(row, "coverRevision", 64), OptionalString(row, "backgroundRevision", 64))))
                    throw new PlayniteBridgeDataException("invalid_playnite_data");
                var entry = paths[id.ToString("D")];
                characters += (entry.Cover?.Length ?? 0) + (entry.Background?.Length ?? 0);
                if (characters > 1_000_000) throw new PlayniteBridgeDataException("invalid_playnite_data");
            }
            offset += rows.GetArrayLength();
            if (offset > total) throw new PlayniteBridgeDataException("invalid_playnite_data");
        }
        return paths;
    }

    private static string? ReadPath(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var path = value.GetString()!;
        // Only ordinary local drive paths; no UNC, device paths, URLs or alternate streams.
        return ArtworkFileContract.IsLocalPath(path) ? path : null;
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
