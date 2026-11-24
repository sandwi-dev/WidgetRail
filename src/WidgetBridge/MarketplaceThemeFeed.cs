using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

// The host selects the publication origin. Listings contain identities, never request URLs.
internal sealed class MarketplaceThemeFeed : IAsyncDisposable
{
    internal const int MaximumIndexBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 12 };
    private readonly string cacheFile;
    private readonly TimeProvider clock;
    private readonly HttpClient client;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private Task? refresh;
    private Task? initialize;
    private Inventory inventory = null!;
    private readonly Dictionary<(string Id, string Version, string Digest), IReadOnlyList<CatalogueDetail>> bundledDetails = new();
    private string? etag;
    private DateTimeOffset checkedAt;
    private DateTimeOffset nextCheck;
    private bool offline;
    private bool disposed;

    private sealed record Collection(string Name, string[] Themes);
    private sealed record Index(int SchemaVersion, string? Featured, Collection[] Collections, CatalogueTheme[] Themes);
    private sealed record Inventory(CatalogueTheme[] Ordered, CatalogueTheme? Featured, ThemeBrowseShelf[] Shelves);
    private sealed record CacheEntry(string? ETag, JsonElement Index);

    internal MarketplaceThemeFeed(string directory, HttpMessageHandler? handler, TimeProvider clock)
    {
        cacheFile = Path.Combine(directory, "catalogue.json");
        this.clock = clock;
        client = handler is null ? new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) : new(handler, false);
        client.Timeout = TimeSpan.FromSeconds(8);
    }

    internal async ValueTask<ThemeBrowsePage> BrowseAsync(ThemeBrowseQuery query, bool force, CancellationToken token)
    {
        Task ready;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ready = initialize ??= Task.Run(() => LoadAsync(lifetime.Token));
        }
        await ready.WaitAsync(token).ConfigureAwait(false);
        Inventory current;
        bool running, unavailable;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var now = clock.GetUtcNow();
            if (refresh?.IsCompleted != false && now >= nextCheck && (force || offline || now - checkedAt >= TimeSpan.FromMinutes(15)))
                refresh = Task.Run(() => RefreshAsync(lifetime.Token));
            current = inventory;
            running = refresh?.IsCompleted == false;
            unavailable = offline;
        }
        var terms = query.Search.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var matches = terms.Length == 0 ? current.Ordered : current.Ordered.Where(theme => terms.All(term =>
            theme.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || theme.Creator.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            theme.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
            theme.Details?.Any(detail => detail.Matches(term)) == true)).ToArray();
        var page = Math.Min(query.Page, Math.Max(0, (matches.Length - 1) / 8));
        bool curated = terms.Length == 0 && page == 0;
        return new(page, matches.Length, matches.Skip(page * 8).Take(8).ToArray(), curated ? current.Featured : null,
            curated ? current.Shelves : [], running, unavailable);
    }

    internal CatalogueTheme Resolve(CatalogueThemeSelection selection)
    {
        lock (gate)
        {
            return inventory?.Ordered.FirstOrDefault(t => t.Id == selection.Id && t.Version == selection.Version && t.Sha256 == selection.Sha256)
                ?? throw new WidgetRail.PlatformDiagnostics.PlatformDiagnosticsException("theme_listing_changed");
        }
    }

    internal CatalogueTheme ResolvePreview(string key)
    {
        lock (gate)
        {
            return inventory?.Ordered.FirstOrDefault(t => ThemeCataloguePublication.PreviewKeyFor(t) == key)
                ?? throw new WidgetRail.PlatformDiagnostics.PlatformDiagnosticsException("theme_listing_changed");
        }
    }

    internal async Task<byte[]> GetPackageAsync(CatalogueTheme theme, CancellationToken token)
    {
        using var bundled = typeof(MarketplaceThemeFeed).Assembly.GetManifestResourceStream("WidgetRail.ThemeCatalogue." + theme.Sha256 + ".wrtheme");
        if (bundled is not null) return await ReadBoundedAsync(bundled, theme.Bytes, token).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await client.GetAsync(ThemeCataloguePublication.Origin + "packages/" + theme.Sha256 + ".wrtheme",
            HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != theme.Bytes) throw new InvalidDataException();
        using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        var result = await ReadBoundedAsync(body, theme.Bytes, deadline.Token).ConfigureAwait(false);
        if (result.Length != theme.Bytes) throw new InvalidDataException();
        return result;
    }

    private async Task LoadAsync(CancellationToken token)
    {
        using var seed = typeof(MarketplaceThemeFeed).Assembly.GetManifestResourceStream("WidgetRail.ThemeCatalogue.index.json")!;
        using var document = await JsonDocument.ParseAsync(seed, cancellationToken: token).ConfigureAwait(false);
        inventory = Validate(document.RootElement);
        foreach (var theme in inventory.Ordered)
            if (theme.Details is { } details) bundledDetails.Add((theme.Id, theme.Version, theme.Sha256), details);
        try
        {
            using var stream = File.OpenRead(cacheFile);
            var bytes = await ReadBoundedAsync(stream, MaximumIndexBytes + 65536, token).ConfigureAwait(false);
            var cached = JsonSerializer.Deserialize<CacheEntry>(bytes, Json);
            if (cached is null || cached.Index.ValueKind != JsonValueKind.Object || cached.ETag is not null && (cached.ETag.Length > 256 || !EntityTagHeaderValue.TryParse(cached.ETag, out _)))
                throw new InvalidDataException();
            inventory = Validate(cached.Index);
            etag = cached.ETag;
        }
        catch (Exception error) when (Recoverable(error))
        {
            // The bundled inventory remains available until a valid feed arrives.
        }
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ThemeCataloguePublication.Origin + "index.json");
            if (etag is not null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.NotModified)
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaximumIndexBytes) throw new InvalidDataException();
                using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                var bytes = await ReadBoundedAsync(body, MaximumIndexBytes, deadline.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
                var parsed = Validate(document.RootElement);
                var validator = response.Headers.ETag?.ToString();
                if (validator?.Length > 256) validator = null;
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
                var pending = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await JsonSerializer.SerializeAsync(file, new CacheEntry(validator, document.RootElement), Json, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    File.Move(pending, cacheFile, true);
                }
                finally { File.Delete(pending); }
                lock (gate) { inventory = parsed; etag = validator; }
            }
            else if (etag is null) throw new InvalidDataException();
            lock (gate) { checkedAt = clock.GetUtcNow(); nextCheck = checkedAt.AddSeconds(30); offline = false; }
        }
        catch (Exception error) when (Recoverable(error) || error is OperationCanceledException)
        {
            lock (gate) { nextCheck = clock.GetUtcNow().AddMinutes(1); offline = true; }
        }
    }

    private Inventory Validate(JsonElement element)
    {
        var index = element.Deserialize<Index>(Json);
        if (index is not { SchemaVersion: 1, Themes.Length: <= 1000, Collections.Length: <= 32 })
            throw new InvalidDataException("Invalid theme index.");
        var byId = new Dictionary<string, CatalogueTheme>(StringComparer.Ordinal);
        foreach (var theme in index.Themes)
        {
            if (!MarketplaceValidation.ValidTheme(theme) || !ThemeIdentity.IsValid(theme.Id) || ThemeIdentity.IsBuiltIn(theme.Id) ||
                !ThemeIdentity.IsValidPublisher(theme.Publisher) || !(theme.Id == theme.Publisher || theme.Id.StartsWith(theme.Publisher + ".", StringComparison.Ordinal)) ||
                !byId.TryAdd(theme.Id, theme.Details is null && bundledDetails.TryGetValue((theme.Id, theme.Version, theme.Sha256), out var details)
                    ? theme with { Details = details } : theme)) throw new InvalidDataException("Invalid theme listing.");
        }
        CatalogueTheme? featured = null;
        if (index.Featured is not null && !byId.TryGetValue(index.Featured, out featured))
            throw new InvalidDataException("Unknown featured theme.");
        var shelves = new List<ThemeBrowseShelf>();
        foreach (var collection in index.Collections)
        {
            if (collection is null || string.IsNullOrWhiteSpace(collection.Name) || collection.Name.Length > 64 || collection.Name.Any(char.IsControl) ||
                collection.Themes is not { Length: <= 1000 } ids || ids.Any(id => id is null || !byId.ContainsKey(id)) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new InvalidDataException("Invalid theme collection.");
            if (collection.Themes.Length > 0 && shelves.Count < 2)
                shelves.Add(new(collection.Name, collection.Themes.Take(4).Select(id => byId[id]).ToArray()));
        }
        return new(byId.Values.OrderByDescending(theme => theme.Released).ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(theme => theme.Id, StringComparer.Ordinal).ToArray(), featured, shelves.ToArray());
    }

    private static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or InvalidDataException;

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken token)
    {
        if (stream.CanSeek && stream.Length > limit) throw new InvalidDataException();
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > limit) throw new InvalidDataException();
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (gate) { disposed = true; pending = refresh ?? initialize ?? Task.CompletedTask; }
        lifetime.Cancel();
        try { await pending.ConfigureAwait(false); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { client.Dispose(); lifetime.Dispose(); }
    }
}
