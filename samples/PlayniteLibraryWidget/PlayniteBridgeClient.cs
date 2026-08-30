using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteBridgeConnectionKind
{
    NotConfigured,
    Connected,
    Unavailable,
    AuthenticationRequired,
    Incompatible,
    Malformed,
}

internal sealed record PlayniteBridgeConnectionResult(
    PlayniteBridgeConnectionKind Kind,
    string Code);

internal enum PlayniteBridgeCommandKind
{
    Probe,
    QueryGames,
    ResolveGame,
    ResolveArtwork,
    Launch,
    SetFavorite,
    SetHidden,
    SetCategories,
    SetCompletionStatus,
    ListCategories,
    CreateCategory,
    ListCompletionStatuses,
}

internal sealed record PlayniteBridgeCommand(
    PlayniteBridgeCommandKind Kind,
    string? GameId = null,
    PlayniteBridgeGameQuery? Query = null,
    string? JsonBody = null,
    PlayniteBridgeArtworkKind? ArtworkKind = null);

internal sealed record PlayniteBridgeResponse(
    int StatusCode,
    byte[] Body,
    string? ContentType = null)
{
    internal PlayniteBridgeResponse(int statusCode, string jsonBody)
        : this(statusCode, Encoding.UTF8.GetBytes(jsonBody), "application/json") { }

    internal string JsonBody
    {
        get
        {
            try { return new UTF8Encoding(false, true).GetString(Body); }
            catch (DecoderFallbackException exception)
            {
                throw new PlayniteBridgeTransportException(isMalformed: true, exception);
            }
        }
    }
}

internal enum PlayniteBridgeArtworkKind { Cover, Background, Icon }

internal sealed record PlayniteBridgeGameQuery(
    int Offset,
    int Limit,
    bool InstalledOnly,
    bool FavoriteOnly = false,
    bool IncludeHidden = false,
    string? SearchText = null,
    string? Source = null,
    string? Category = null,
    string? CompletionStatus = null);

internal sealed record PlayniteBridgeGame(
    string Id,
    string Name,
    string Source,
    bool IsInstalled,
    bool Favorite,
    bool Hidden,
    string? CompletionStatus,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Platforms,
    long PlaytimeSeconds,
    long? LastActivityUnixMilliseconds)
{
    internal string? Description { get; init; }
    internal string? Version { get; init; }
}

internal sealed record PlayniteBridgeGamePage(
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<PlayniteBridgeGame> Games);

internal sealed record PlayniteBridgeNamedItem(string Id, string Name);

internal interface IPlayniteBridgeTransport : IDisposable
{
    ValueTask<PlayniteBridgeResponse> SendAsync(
        PlayniteBridgeCommand command,
        string bearerToken,
        CancellationToken cancellationToken);
}

internal interface IPlayniteCredentialStore
{
    ValueTask<string?> ReadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(string token, CancellationToken cancellationToken);
    ValueTask DeleteAsync(CancellationToken cancellationToken);
}

internal interface IPlayniteBridgeClient : IDisposable
{
    ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(CancellationToken cancellationToken);
    ValueTask SaveCredentialAsync(string token, CancellationToken cancellationToken);
    ValueTask DeleteCredentialAsync(CancellationToken cancellationToken);
}

internal interface IPlayniteLibraryBridgeClient : IAsyncDisposable
{
    ValueTask<PlayniteBridgeGamePage> QueryGamesAsync(
        PlayniteBridgeGameQuery query,
        CancellationToken cancellationToken);
    ValueTask<PlayniteBridgeGame?> ResolveGameAsync(
        string gameId,
        CancellationToken cancellationToken);
    ValueTask<byte[]?> ResolveArtworkAsync(
        string gameId,
        PlayniteBridgeArtworkKind kind,
        CancellationToken cancellationToken);
    ValueTask<bool> LaunchAsync(string gameId, CancellationToken cancellationToken);
    ValueTask<PlayniteBridgeGame?> SetFavoriteAsync(
        string gameId, bool favorite, CancellationToken cancellationToken);
    ValueTask<PlayniteBridgeGame?> SetHiddenAsync(
        string gameId, bool hidden, CancellationToken cancellationToken);
    ValueTask<PlayniteBridgeGame?> SetCategoriesAsync(
        string gameId, IReadOnlyList<string> categories,
        CancellationToken cancellationToken);
    ValueTask<PlayniteBridgeGame?> SetCompletionStatusAsync(
        string gameId, string completionStatus, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCategoriesAsync(
        CancellationToken cancellationToken);
    ValueTask<PlayniteBridgeNamedItem?> CreateCategoryAsync(
        string name, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCompletionStatusesAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Bounded client for the package-owned Playnite connection and library workflow.
/// No caller can provide a URI, HTTP header, or arbitrary HTTP method.
/// </summary>
internal sealed class PlayniteBridgeClient(
    IPlayniteBridgeTransport transport,
    IPlayniteCredentialStore credentials) : IPlayniteBridgeClient, IPlayniteLibraryBridgeClient
{
    internal const int Port = 19821;
    internal const string CompatibilityPath = "/api/games?installed=true&limit=1&offset=0";
    internal const int MaximumTokenCharacters = 512;
    internal const int MaximumGames = 10_000;
    internal const int MaximumPageSize = 64;
    internal const int MaximumNameCharacters = 256;
    internal const int MaximumFilterCharacters = 96;
    internal const int MaximumCategoriesPerGame = 64;
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = false,
    };
    private readonly IPlayniteBridgeTransport _transport = transport ??
        throw new ArgumentNullException(nameof(transport));
    private readonly IPlayniteCredentialStore _credentials = credentials ??
        throw new ArgumentNullException(nameof(credentials));

    internal static PlayniteBridgeClient CreateDefault() => new(
        new PlayniteBridgeHttpTransport(),
        new WindowsCredentialPlayniteTokenStore());

    public async ValueTask<PlayniteBridgeConnectionResult> ProbeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAuthenticatedAsync(
                    new(PlayniteBridgeCommandKind.Probe), cancellationToken)
                .ConfigureAwait(false);
            if (response is null)
                return new(PlayniteBridgeConnectionKind.NotConfigured, "credential_missing");
            if (response.StatusCode == 401)
            {
                await _credentials.DeleteAsync(cancellationToken).ConfigureAwait(false);
                return new(PlayniteBridgeConnectionKind.AuthenticationRequired,
                    "authentication_required");
            }
            if (response.StatusCode == 403)
                return new(PlayniteBridgeConnectionKind.AuthenticationRequired,
                    "authentication_required");
            if (response.StatusCode is 404 or 405)
                return new(PlayniteBridgeConnectionKind.Incompatible, "api_incompatible");
            if (response.StatusCode is < 200 or > 299)
                return new(PlayniteBridgeConnectionKind.Unavailable, "service_unavailable");
            return TryParsePage(response, expectedOffset: 0, expectedLimit: 1, out _)
                ? new(PlayniteBridgeConnectionKind.Connected, "connected")
                : new(PlayniteBridgeConnectionKind.Malformed, "malformed_response");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlayniteBridgeTransportException exception)
        {
            return new(
                exception.IsMalformed
                    ? PlayniteBridgeConnectionKind.Malformed
                    : PlayniteBridgeConnectionKind.Unavailable,
                exception.IsMalformed ? "malformed_response" : "service_unavailable");
        }
        catch (PlayniteCredentialException)
        {
            return new(PlayniteBridgeConnectionKind.Unavailable,
                "secret_store_unavailable");
        }
    }

    public ValueTask SaveCredentialAsync(string token, CancellationToken cancellationToken)
    {
        ValidateToken(token);
        return _credentials.SaveAsync(token, cancellationToken);
    }

    public ValueTask DeleteCredentialAsync(CancellationToken cancellationToken) =>
        _credentials.DeleteAsync(cancellationToken);

    public async ValueTask<PlayniteBridgeGamePage> QueryGamesAsync(
        PlayniteBridgeGameQuery query,
        CancellationToken cancellationToken)
    {
        ValidateQuery(query);
        var response = await SendRequiredAsync(
                new(PlayniteBridgeCommandKind.QueryGames, Query: query), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response);
        return TryParsePage(response, query.Offset, query.Limit, out var page)
            ? page!
            : throw new PlayniteBridgeDataException("invalid_playnite_data");
    }

    public async ValueTask<PlayniteBridgeGame?> ResolveGameAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        var id = CanonicalGameId(gameId);
        var response = await SendRequiredAsync(
                new(PlayniteBridgeCommandKind.ResolveGame, id), cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 404) return null;
        EnsureSuccess(response);
        return TryParseGameDocument(response, out var game)
            ? game
            : throw new PlayniteBridgeDataException("invalid_playnite_data");
    }

    public async ValueTask<byte[]?> ResolveArtworkAsync(
        string gameId,
        PlayniteBridgeArtworkKind kind,
        CancellationToken cancellationToken)
    {
        var response = await SendRequiredAsync(new(
                PlayniteBridgeCommandKind.ResolveArtwork,
                CanonicalGameId(gameId), ArtworkKind: kind), cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 404) return null;
        EnsureSuccess(response);
        if (!string.Equals(response.ContentType, "image/png", StringComparison.OrdinalIgnoreCase) ||
            response.Body.Length is < 8 or > PlayniteBridgeHttpTransport.MaximumArtworkBytes ||
            !response.Body.AsSpan(0, 8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return null;
        return response.Body.ToArray();
    }

    public async ValueTask<bool> LaunchAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        var response = await SendRequiredAsync(
                new(PlayniteBridgeCommandKind.Launch, CanonicalGameId(gameId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == 404) return false;
        EnsureSuccess(response);
        return true;
    }

    public ValueTask<PlayniteBridgeGame?> SetFavoriteAsync(
        string gameId, bool favorite, CancellationToken cancellationToken) =>
        MutateAndResolveAsync(new(PlayniteBridgeCommandKind.SetFavorite,
            CanonicalGameId(gameId), JsonBody: BooleanBody("favorite", favorite)),
            cancellationToken);

    public ValueTask<PlayniteBridgeGame?> SetHiddenAsync(
        string gameId, bool hidden, CancellationToken cancellationToken) =>
        MutateAndResolveAsync(new(PlayniteBridgeCommandKind.SetHidden,
            CanonicalGameId(gameId), JsonBody: BooleanBody("hidden", hidden)),
            cancellationToken);

    public ValueTask<PlayniteBridgeGame?> SetCategoriesAsync(
        string gameId,
        IReadOnlyList<string> categories,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeNames(categories, MaximumCategoriesPerGame);
        return MutateAndResolveAsync(new(PlayniteBridgeCommandKind.SetCategories,
            CanonicalGameId(gameId), JsonBody: NamesBody("categories", normalized)),
            cancellationToken);
    }

    public ValueTask<PlayniteBridgeGame?> SetCompletionStatusAsync(
        string gameId,
        string completionStatus,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeName(completionStatus, MaximumFilterCharacters) ??
            throw new ArgumentException("Completion status is invalid.",
                nameof(completionStatus));
        return MutateAndResolveAsync(new(PlayniteBridgeCommandKind.SetCompletionStatus,
            CanonicalGameId(gameId), JsonBody: StringBody("status", normalized)),
            cancellationToken);
    }

    public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCategoriesAsync(
        CancellationToken cancellationToken) => ListNamedAsync(
            PlayniteBridgeCommandKind.ListCategories, cancellationToken);

    public ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListCompletionStatusesAsync(
        CancellationToken cancellationToken) => ListNamedAsync(
            PlayniteBridgeCommandKind.ListCompletionStatuses, cancellationToken);

    public async ValueTask<PlayniteBridgeNamedItem?> CreateCategoryAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeName(name, MaximumFilterCharacters) ??
            throw new ArgumentException("Category is invalid.", nameof(name));
        var response = await SendRequiredAsync(new(
                PlayniteBridgeCommandKind.CreateCategory,
                JsonBody: StringBody("name", normalized)), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response);
        return TryParseNamed(response, out var value)
            ? value
            : throw new PlayniteBridgeDataException("invalid_playnite_data");
    }

    public void Dispose() => _transport.Dispose();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal static void ValidateToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        int bytes;
        try { bytes = new UTF8Encoding(false, true).GetByteCount(token); }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("The Playnite Bridge token is invalid.",
                nameof(token), exception);
        }
        if (token.Length is < 16 or > MaximumTokenCharacters || bytes > MaximumTokenCharacters ||
            token.Any(character => character > 0x7e || character < 0x21 ||
                char.IsWhiteSpace(character)))
            throw new ArgumentException("The Playnite Bridge token is invalid.", nameof(token));
    }

    internal static string CanonicalGameId(string value) =>
        Guid.TryParseExact(value, "D", out var parsed)
            ? parsed.ToString("D")
            : throw new ArgumentException("The Playnite game identifier is invalid.",
                nameof(value));

    private async ValueTask<PlayniteBridgeResponse?> SendAuthenticatedAsync(
        PlayniteBridgeCommand command,
        CancellationToken cancellationToken)
    {
        var token = await _credentials.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (token is null) return null;
        try
        {
            return await _transport.SendAsync(command, token, cancellationToken)
                .ConfigureAwait(false);
        }
        finally { token = null; }
    }

    private async ValueTask<PlayniteBridgeResponse> SendRequiredAsync(
        PlayniteBridgeCommand command,
        CancellationToken cancellationToken)
    {
        var response = await SendAuthenticatedAsync(command, cancellationToken)
            .ConfigureAwait(false);
        return response ?? throw new PlayniteBridgeDataException("credential_missing");
    }

    private async ValueTask<PlayniteBridgeGame?> MutateAndResolveAsync(
        PlayniteBridgeCommand command,
        CancellationToken cancellationToken)
    {
        var response = await SendRequiredAsync(command, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == 404) return null;
        EnsureSuccess(response);
        return await ResolveGameAsync(command.GameId!, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<PlayniteBridgeNamedItem>> ListNamedAsync(
        PlayniteBridgeCommandKind kind,
        CancellationToken cancellationToken)
    {
        var response = await SendRequiredAsync(new(kind), cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response);
        try
        {
            using var document = ParseJson(response);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() > 256)
                throw new PlayniteBridgeDataException("invalid_playnite_data");
            var values = new List<PlayniteBridgeNamedItem>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!TryParseNamed(element, out var value) || !ids.Add(value!.Id))
                    throw new PlayniteBridgeDataException("invalid_playnite_data");
                values.Add(value);
            }
            return values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new PlayniteBridgeDataException("invalid_playnite_data", exception);
        }
    }

    private static void ValidateQuery(PlayniteBridgeGameQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset is < 0 or > MaximumGames ||
            query.Limit is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(query));
        _ = NormalizeOptional(query.SearchText, MaximumFilterCharacters, nameof(query));
        _ = NormalizeOptional(query.Source, MaximumFilterCharacters, nameof(query));
        _ = NormalizeOptional(query.Category, MaximumFilterCharacters, nameof(query));
        _ = NormalizeOptional(query.CompletionStatus, MaximumFilterCharacters, nameof(query));
    }

    private static void EnsureSuccess(PlayniteBridgeResponse response)
    {
        if (response.StatusCode is >= 200 and <= 299) return;
        throw new PlayniteBridgeDataException(response.StatusCode switch
        {
            401 or 403 => "authentication_required",
            404 => "game_not_found",
            _ => "playnite_unavailable",
        });
    }

    private static bool TryParsePage(
        PlayniteBridgeResponse response,
        int expectedOffset,
        int expectedLimit,
        out PlayniteBridgeGamePage? page)
    {
        page = null;
        try
        {
            using var document = ParseJson(response);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryNonNegativeInteger(root, "total", out var total) || total > MaximumGames ||
                !TryNonNegativeInteger(root, "offset", out var offset) ||
                offset != expectedOffset ||
                !TryNonNegativeInteger(root, "limit", out var limit) ||
                limit != expectedLimit ||
                !root.TryGetProperty("games", out var games) ||
                games.ValueKind != JsonValueKind.Array || games.GetArrayLength() > expectedLimit)
                return false;
            var values = new List<PlayniteBridgeGame>(games.GetArrayLength());
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in games.EnumerateArray())
            {
                if (!TryParseGame(element, out var game) || !ids.Add(game!.Id)) return false;
                values.Add(game);
            }
            if (offset > total || offset + values.Count > total) return false;
            page = new(total, offset, limit, values);
            return true;
        }
        catch (JsonException) { return false; }
        catch (PlayniteBridgeTransportException) { return false; }
    }

    private static bool TryParseGameDocument(
        PlayniteBridgeResponse response,
        out PlayniteBridgeGame? game)
    {
        game = null;
        try
        {
            using var document = ParseJson(response);
            return TryParseGame(document.RootElement, out game);
        }
        catch (JsonException) { return false; }
        catch (PlayniteBridgeTransportException) { return false; }
    }

    private static bool TryParseGame(JsonElement value, out PlayniteBridgeGame? game)
    {
        game = null;
        if (value.ValueKind != JsonValueKind.Object ||
            !TryString(value, "id", 36, out var rawId) ||
            !Guid.TryParseExact(rawId, "D", out var id) ||
            !TryString(value, "name", MaximumNameCharacters, out var name) ||
            !TryRequiredBoolean(value, "isInstalled", out var installed) ||
            !TryRequiredBoolean(value, "favorite", out var favorite) ||
            !TryRequiredBoolean(value, "hidden", out var hidden) ||
            !TryStrings(value, "categories", 64, out var categories) ||
            !TryStrings(value, "genres", 64, out var genres) ||
            !TryStrings(value, "platforms", 64, out var platforms) ||
            !TryNonNegativeLong(value, "playtime", out var playtime))
            return false;
        var source = OptionalString(value, "source", MaximumFilterCharacters) ?? "Playnite";
        var completion = OptionalString(value, "completionStatus", MaximumFilterCharacters);
        long? lastActivity = null;
        var rawActivity = OptionalString(value, "lastActivity", 64);
        if (rawActivity is not null)
        {
            if (!DateTimeOffset.TryParse(rawActivity, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed)) return false;
            lastActivity = parsed.ToUnixTimeMilliseconds();
        }
        game = new(
            id.ToString("D"), name!, source, installed, favorite, hidden,
            completion, categories!, genres!, platforms!, playtime, lastActivity)
        {
            Description = OptionalString(value, "description", 4096),
            Version = OptionalString(value, "version", 128),
        };
        return true;
    }

    private static bool TryParseNamed(
        PlayniteBridgeResponse response,
        out PlayniteBridgeNamedItem? value)
    {
        value = null;
        try
        {
            using var document = ParseJson(response);
            return TryParseNamed(document.RootElement, out value);
        }
        catch (JsonException) { return false; }
    }

    private static bool TryParseNamed(JsonElement element, out PlayniteBridgeNamedItem? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryString(element, "id", 128, out var id) ||
            !TryString(element, "name", MaximumFilterCharacters, out var name))
            return false;
        value = new(id!, name!);
        return true;
    }

    private static JsonDocument ParseJson(PlayniteBridgeResponse response)
    {
        if (response.Body.Length > PlayniteBridgeHttpTransport.MaximumJsonBytes)
            throw new PlayniteBridgeTransportException(isMalformed: true);
        return JsonDocument.Parse(response.Body, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
    }

    private static bool TryNonNegativeInteger(
        JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value) && value >= 0;
    }

    private static bool TryNonNegativeLong(
        JsonElement root, string name, out long value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out value) && value >= 0;
    }

    private static bool TryRequiredBoolean(
        JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = property.GetBoolean();
        return true;
    }

    private static bool TryString(
        JsonElement root, string name, int maximum, out string? value)
    {
        value = OptionalString(root, name, maximum);
        return value is not null;
    }

    private static string? OptionalString(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.String) return null;
        return NormalizeName(property.GetString(), maximum);
    }

    private static bool TryStrings(
        JsonElement root,
        string name,
        int maximumCount,
        out IReadOnlyList<string>? values)
    {
        values = null;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Array ||
            property.GetArrayLength() > maximumCount) return false;
        var result = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in property.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String ||
                NormalizeName(element.GetString(), MaximumFilterCharacters) is not { } value ||
                !unique.Add(value)) return false;
            result.Add(value);
        }
        values = result;
        return true;
    }

    private static IReadOnlyList<string> NormalizeNames(
        IReadOnlyList<string> values, int maximumCount)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > maximumCount)
            throw new ArgumentOutOfRangeException(nameof(values));
        var normalized = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var item = NormalizeName(value, MaximumFilterCharacters) ??
                throw new ArgumentException("A Playnite name is invalid.", nameof(values));
            if (!unique.Add(item))
                throw new ArgumentException("Playnite names must be unique.", nameof(values));
            normalized.Add(item);
        }
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximum, string parameter)
    {
        if (value is null) return null;
        return NormalizeName(value, maximum) ??
            throw new ArgumentException("A Playnite filter is invalid.", parameter);
    }

    private static string? NormalizeName(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length is > 0 && normalized.Length <= maximum &&
            !normalized.Any(char.IsControl) ? normalized : null;
    }

    private static string BooleanBody(string name, bool value) =>
        $"{{\"{name}\":{(value ? "true" : "false")}}}";

    private static string StringBody(string name, string value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(name, value);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string NamesBody(string name, IReadOnlyList<string> values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartArray(name);
            foreach (var value in values) writer.WriteStringValue(value);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

internal sealed class PlayniteBridgeHttpTransport : IPlayniteBridgeTransport
{
    internal const int MaximumJsonBytes = 96 * 1024;
    internal const int MaximumArtworkBytes = 256 * 1024;
    private static readonly Uri Origin = new(
        $"http://localhost:{PlayniteBridgeClient.Port}/", UriKind.Absolute);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private readonly HttpClient _http;

    internal PlayniteBridgeHttpTransport()
    {
        _http = new HttpClient(CreateHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    internal static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        Proxy = null,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        PreAuthenticate = false,
        Credentials = null,
        MaxConnectionsPerServer = 1,
        MaxResponseHeadersLength = 16,
    };

    public async ValueTask<PlayniteBridgeResponse> SendAsync(
        PlayniteBridgeCommand command,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        PlayniteBridgeClient.ValidateToken(bearerToken);
        var (method, relativePath, maximumBytes) = Resolve(command);
        using var request = new HttpRequestMessage(method, new Uri(Origin, relativePath))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (command.JsonBody is not null)
            request.Content = new StringContent(command.JsonBody, Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var body = await ReadBoundedAsync(response.Content, maximumBytes, timeout.Token)
                .ConfigureAwait(false);
            return new((int)response.StatusCode, body,
                response.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new PlayniteBridgeTransportException(isMalformed: false, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new PlayniteBridgeTransportException(isMalformed: false, exception);
        }
        catch (IOException exception)
        {
            throw new PlayniteBridgeTransportException(isMalformed: false, exception);
        }
    }

    public void Dispose() => _http.Dispose();

    internal static (HttpMethod Method, string RelativePath, int MaximumBytes) Resolve(
        PlayniteBridgeCommand command)
    {
        static string Game(string? value) =>
            "api/games/" + PlayniteBridgeClient.CanonicalGameId(value ?? string.Empty);
        return command.Kind switch
        {
            PlayniteBridgeCommandKind.Probe =>
                (HttpMethod.Get, PlayniteBridgeClient.CompatibilityPath[1..], MaximumJsonBytes),
            PlayniteBridgeCommandKind.QueryGames when command.Query is { } query =>
                (HttpMethod.Get, QueryPath(query), MaximumJsonBytes),
            PlayniteBridgeCommandKind.ResolveGame =>
                (HttpMethod.Get, Game(command.GameId), MaximumJsonBytes),
            PlayniteBridgeCommandKind.ResolveArtwork =>
                (HttpMethod.Get, Game(command.GameId) + "/cover" +
                    (command.ArtworkKind == PlayniteBridgeArtworkKind.Cover
                        ? string.Empty
                        : "?type=" + command.ArtworkKind!.Value.ToString().ToLowerInvariant()),
                    MaximumArtworkBytes),
            PlayniteBridgeCommandKind.Launch =>
                (HttpMethod.Post, Game(command.GameId) + "/launch", MaximumJsonBytes),
            PlayniteBridgeCommandKind.SetFavorite or
                PlayniteBridgeCommandKind.SetHidden =>
                (HttpMethod.Put, Game(command.GameId), MaximumJsonBytes),
            PlayniteBridgeCommandKind.SetCategories =>
                (HttpMethod.Put, Game(command.GameId) + "/categories", MaximumJsonBytes),
            PlayniteBridgeCommandKind.SetCompletionStatus =>
                (HttpMethod.Put, Game(command.GameId) + "/status", MaximumJsonBytes),
            PlayniteBridgeCommandKind.ListCategories =>
                (HttpMethod.Get, "api/categories", MaximumJsonBytes),
            PlayniteBridgeCommandKind.CreateCategory =>
                (HttpMethod.Post, "api/categories", MaximumJsonBytes),
            PlayniteBridgeCommandKind.ListCompletionStatuses =>
                (HttpMethod.Get, "api/completion-statuses", MaximumJsonBytes),
            _ => throw new ArgumentException("The Playnite Bridge command is invalid.",
                nameof(command)),
        };
    }

    private static string QueryPath(PlayniteBridgeGameQuery query)
    {
        var parts = new List<string>
        {
            "limit=" + query.Limit.ToString(CultureInfo.InvariantCulture),
            "offset=" + query.Offset.ToString(CultureInfo.InvariantCulture),
        };
        if (query.InstalledOnly) parts.Insert(0, "installed=true");
        if (query.FavoriteOnly) parts.Add("favorite=true");
        if (query.IncludeHidden) parts.Add("hidden=true");
        Add("q", query.SearchText);
        Add("source", query.Source);
        Add("category", query.Category);
        Add("completionStatus", query.CompletionStatus);
        return "api/games?" + string.Join('&', parts);

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(name + "=" + Uri.EscapeDataString(value));
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length && length > maximumBytes)
            throw new PlayniteBridgeTransportException(isMalformed: true);
        await using var stream = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > maximumBytes)
                throw new PlayniteBridgeTransportException(isMalformed: true);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}

internal sealed class WindowsCredentialPlayniteTokenStore : IPlayniteCredentialStore
{
    private const int GenericCredential = 1;
    private const int LocalMachinePersistence = 2;
    private const int NotFound = 1168;
    internal const string Target =
        "WidgetRail/PlayniteLibrary/PlayniteBridge/v1/widgetrail.samples.playnite-library";

    public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredRead(Target, GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return ValueTask.FromResult<string?>(null);
            throw new PlayniteCredentialException(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize is < 1 or >
                    PlayniteBridgeClient.MaximumTokenCharacters ||
                credential.CredentialBlob == IntPtr.Zero)
                throw new PlayniteCredentialException();
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                var token = new UTF8Encoding(false, true).GetString(bytes);
                PlayniteBridgeClient.ValidateToken(token);
                return ValueTask.FromResult<string?>(token);
            }
            catch (DecoderFallbackException exception)
            {
                throw new PlayniteCredentialException(exception);
            }
            catch (ArgumentException exception)
            {
                throw new PlayniteCredentialException(exception);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { NativeMethods.CredFree(pointer); }
    }

    public ValueTask SaveAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlayniteBridgeClient.ValidateToken(token);
        var bytes = Encoding.UTF8.GetBytes(token);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = Target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = "WidgetRail Playnite Library",
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
                throw new PlayniteCredentialException(Marshal.GetLastWin32Error());
            return ValueTask.CompletedTask;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            for (var index = 0; index < bytes.Length; index++) Marshal.WriteByte(blob, index, 0);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.CredDelete(Target, GenericCredential, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw new PlayniteCredentialException(error);
        }
        return ValueTask.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(
            string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWrite(ref NativeCredential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        internal static extern void CredFree(IntPtr credential);
    }
}

internal sealed class PlayniteBridgeTransportException : Exception
{
    internal PlayniteBridgeTransportException(bool isMalformed, Exception? inner = null)
        : base("The bounded Playnite Bridge transport failed.", inner) =>
        IsMalformed = isMalformed;

    internal bool IsMalformed { get; }
}

internal sealed class PlayniteBridgeDataException : Exception
{
    internal PlayniteBridgeDataException(string code, Exception? inner = null)
        : base("The bounded Playnite Bridge data operation failed.", inner) => Code = code;

    internal string Code { get; }
}

internal sealed class PlayniteCredentialException : Exception
{
    internal PlayniteCredentialException() : base("The Playnite credential store failed.") { }
    internal PlayniteCredentialException(int error)
        : base($"The Playnite credential store failed with Windows error {error}.") { }
    internal PlayniteCredentialException(Exception inner)
        : base("The Playnite credential store failed.", inner) { }
}
