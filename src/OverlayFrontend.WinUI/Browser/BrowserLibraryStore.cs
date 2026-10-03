using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed record BrowserLibraryEntry(string Url, string Title, DateTimeOffset VisitedAt);
internal sealed record BrowserLibraryData(int Version, BrowserLibraryEntry[] Bookmarks, BrowserLibraryEntry[] History);

/// <summary>Host-only, bounded local metadata. Shared by all browser sessions of one widget.</summary>
internal sealed class BrowserLibraryStore(string path)
{
    internal const int MaximumBookmarks = 100;
    internal const int MaximumHistory = 200;
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile BrowserLibraryData data = new(1, [], []);
    internal IReadOnlyList<BrowserLibraryEntry> Bookmarks => data.Bookmarks;
    internal IReadOnlyList<BrowserLibraryEntry> History => data.History;
    internal bool IsBookmarked(string url) => data.Bookmarks.Any(item => item.Url == url);

    internal static async Task<BrowserLibraryStore> LoadAsync(string path, CancellationToken token = default)
    {
        var store = new BrowserLibraryStore(path);
        try
        {
            if (new FileInfo(path) is { Exists: true, Length: <= 3_000_000 })
            {
                await using var stream = File.OpenRead(path);
                var saved = await JsonSerializer.DeserializeAsync(stream, BrowserLibraryJsonContext.Default.BrowserLibraryData, token).ConfigureAwait(false);
                if (saved is { Version: 1, Bookmarks: not null, History: not null })
                    store.data = new(1, Clean(saved.Bookmarks, MaximumBookmarks), Clean(saved.History, MaximumHistory));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return store;
    }

    internal Task RecordAsync(string url, string title, CancellationToken token = default) => UpdateAsync(current =>
    {
        if (!WebBrowserDocument.IsWebUrl(url)) return current;
        var entry = new BrowserLibraryEntry(url, Title(title, url), DateTimeOffset.UtcNow);
        return current with { History = new[] { entry }.Concat(current.History.Where(item => item.Url != url)).Take(MaximumHistory).ToArray() };
    }, token);

    internal Task ToggleBookmarkAsync(string url, string title, CancellationToken token = default) => UpdateAsync(current =>
    {
        if (!WebBrowserDocument.IsWebUrl(url)) return current;
        if (current.Bookmarks.Any(item => item.Url == url)) return current with { Bookmarks = current.Bookmarks.Where(item => item.Url != url).ToArray() };
        if (current.Bookmarks.Length >= MaximumBookmarks) throw new InvalidOperationException("Bookmark limit reached.");
        return current with { Bookmarks = [new(url, Title(title, url), DateTimeOffset.UtcNow), .. current.Bookmarks] };
    }, token);

    internal async Task DrainAsync() { await gate.WaitAsync().ConfigureAwait(false); gate.Release(); }

    internal Task ClearHistoryAsync(CancellationToken token = default) => UpdateAsync(current => current with { History = [] }, token);

    private async Task UpdateAsync(Func<BrowserLibraryData, BrowserLibraryData> change, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        var temporary = path + ".tmp";
        try
        {
            var next = change(data);
            if (ReferenceEquals(next, data)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                await JsonSerializer.SerializeAsync(stream, next, BrowserLibraryJsonContext.Default.BrowserLibraryData, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            data = next;
        }
        finally { gate.Release(); }
    }
    private static BrowserLibraryEntry[] Clean(IEnumerable<BrowserLibraryEntry> items, int limit) => items
        .Where(item => item is not null && WebBrowserDocument.IsWebUrl(item.Url)).DistinctBy(item => item.Url).Take(limit)
        .Select(item => item with { Title = Title(item.Title, item.Url) }).ToArray();
    private static string Title(string? title, string url) => string.IsNullOrWhiteSpace(title) ? new Uri(url).IdnHost
        : new string(title.Where(c => !char.IsControl(c)).Take(256).ToArray());
}

[JsonSerializable(typeof(BrowserLibraryData))]
internal sealed partial class BrowserLibraryJsonContext : JsonSerializerContext;
