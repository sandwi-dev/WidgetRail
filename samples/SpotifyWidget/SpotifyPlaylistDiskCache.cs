using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WidgetRail.Samples.SpotifyWidget;

/// <summary>Optional page storage. Every IO runs outside presentation locks and fails open.</summary>
internal sealed class SpotifyPlaylistDiskCache(string root, string partition,
    long maximumBytes = 50L * 1024 * 1024, int maximumPlaylists = 100)
{
    internal const int MaximumPageBytes = 2 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _retired;
    private int _pendingWrites;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 16 };
    private sealed record Document(int Version, string Partition, string PlaylistId,
        string SnapshotId, int Offset, int Limit, SpotifyPlaylistItemsPageSummary Page);

    internal static string? DefaultRoot()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(local) ? null : Path.Combine(local,
                "WidgetRail", "community-apps", "widgetrail.samples.spotify", "cache", "playlists-v1");
        }
        catch { return null; }
    }

    internal Task<SpotifyPlaylistItemsPageSummary?> ReadAsync(string playlistId, string version,
        int offset, int limit, CancellationToken cancellationToken) => RunAsync(() =>
    {
        var directory = DirectoryFor(playlistId);
        var path = PagePath(directory, offset, limit);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > MaximumPageBytes ||
            IsReparse(directory) || IsReparse(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumPageBytes) return null;
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
        if (!json.RootElement.TryGetProperty("Page", out var pageNode) ||
            !pageNode.TryGetProperty("Items", out var itemNodes) ||
            itemNodes.ValueKind != JsonValueKind.Array || itemNodes.GetArrayLength() > 100) return null;
        var document = json.RootElement.Deserialize<Document>(JsonOptions);
        if (document is null || document.Version != 1 || document.Partition != partition ||
            document.PlaylistId != playlistId || document.SnapshotId != version ||
            document.Offset != offset || document.Limit != limit || !ValidPage(document.Page))
        {
            return null;
        }
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return document.Page;
    }, cancellationToken);

    internal async Task<bool> WriteAsync(string playlistId, string version, int offset, int limit,
        SpotifyPlaylistItemsPageSummary page, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _pendingWrites) > 4)
        { Interlocked.Decrement(ref _pendingWrites); return false; }
        try { return await WriteCoreAsync(playlistId, version, offset, limit, page, cancellationToken).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _pendingWrites); }
    }
    private Task<bool> WriteCoreAsync(string playlistId, string version, int offset, int limit,
        SpotifyPlaylistItemsPageSummary page, CancellationToken cancellationToken) => RunAsync(() =>
    {
        if (!ValidPage(page) || string.IsNullOrWhiteSpace(version) || version.Length > 256 ||
            playlistId.Length > 256 || offset < 0 || limit is < 1 or > 100) return false;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, partition,
            playlistId, version, offset, limit, page), JsonOptions);
        if (bytes.Length > MaximumPageBytes || bytes.Length > maximumBytes) return false;
        Directory.CreateDirectory(root);
        if (IsReparse(root)) return false;
        var volume = Path.GetPathRoot(Path.GetFullPath(root));
        if (volume is null || new DriveInfo(volume).AvailableFreeSpace < bytes.Length + 64L * 1024 * 1024) return false;
        using var fileLock = new FileStream(Path.Combine(root, "writer.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var directory = DirectoryFor(playlistId);
        if (IsReparse(directory)) return false;
        Directory.CreateDirectory(directory);
        var path = PagePath(directory, offset, limit);
        if (!Trim(directory, path, bytes.Length)) return false;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes); stream.Flush(true);
            }
            File.Move(temp, path, true);
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
            return true;
        }
        finally { TryDelete(temp); }
    }, cancellationToken);

    internal Task RemoveAsync(string playlistId) => RunAsync(() =>
    {
        RemoveDirectory(DirectoryFor(playlistId)); return true;
    }, CancellationToken.None);

    internal Task RetireAndClearAsync()
    {
        Interlocked.Exchange(ref _retired, 1);
        return Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Directory.Exists(root) || IsReparse(root)) return;
                foreach (var directory in Directory.EnumerateDirectories(root, partition + "-*").Take(201))
                    RemoveDirectory(directory);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
            finally { _gate.Release(); }
        });
    }

    private async Task<T?> RunAsync<T>(Func<T?> action, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _gate.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false)) return default;
            try
            {
                if (Volatile.Read(ref _retired) != 0) return default;
                return await Task.Run(() =>
                {
                    if (Volatile.Read(ref _retired) != 0 || IsReparse(root)) return default;
                    return action();
                }, cancellationToken).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { return default; }
    }
    private string DirectoryFor(string id) => Path.Combine(root,
        partition + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant());
    private static string PagePath(string directory, int offset, int limit) =>
        Path.Combine(directory, $"{offset}-{limit}.page");
    private static bool IsReparse(string path) => (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private void RemoveDirectory(string directory)
    {
        if (!Directory.Exists(directory) || IsReparse(directory) ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(directory)), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return;
        foreach (var file in Directory.EnumerateFiles(directory).Take(257))
            if (Path.GetExtension(file) is ".page" or ".tmp") File.Delete(file);
        Directory.Delete(directory, false);
    }
    private static bool IsCacheDirectoryName(string name) => name.Length == 97 && name[32] == '-' &&
        name.Where((_, index) => index != 32).All(character => Uri.IsHexDigit(character));
    private bool Trim(string incomingDirectory, string incomingPath, int bytes)
    {
        var directories = new DirectoryInfo(root).EnumerateDirectories()
            .Where(item => IsCacheDirectoryName(item.Name)).Take(201).ToList();
        if (directories.Count > 200 || directories.Any(item => IsReparse(item.FullName))) return false;
        foreach (var directory in directories.OrderBy(item => item.LastWriteTimeUtc).ToArray())
        {
            if (directories.Count <= maximumPlaylists) break;
            if (directory.FullName == incomingDirectory) continue;
            RemoveDirectory(directory.FullName); directories.Remove(directory);
        }
        var files = directories.SelectMany(item => item.EnumerateFiles().Take(257)).ToList();
        if (files.Any(item => IsReparse(item.FullName)) || files.Count > 6400) return false;
        foreach (var temp in files.Where(item => item.Extension == ".tmp")) File.Delete(temp.FullName);
        files = files.Where(item => item.Extension == ".page").ToList();
        var existing = files.FirstOrDefault(item => item.FullName == incomingPath);
        if (existing is not null) { File.Delete(existing.FullName); files.Remove(existing); }
        long total = files.Sum(item => item.Length);
        var samePlaylist = files.Count(item => item.DirectoryName == incomingDirectory);
        foreach (var file in files.OrderBy(item => item.LastWriteTimeUtc))
        {
            if (total + bytes <= maximumBytes && samePlaylist < 64) break;
            if (total + bytes <= maximumBytes && file.DirectoryName != incomingDirectory) continue;
            File.Delete(file.FullName); total -= file.Length;
            if (file.DirectoryName == incomingDirectory) samePlaylist--;
        }
        return total + bytes <= maximumBytes && directories.Count <= maximumPlaylists;
    }
    private static bool ValidPage(SpotifyPlaylistItemsPageSummary? page) => page is not null &&
        page.Offset >= 0 && page.Limit is >= 1 and <= 100 && page.Total >= 0 &&
        page.Items is { Count: <= 100 } && page.Items.All(item => item is not null &&
            Enum.IsDefined(item.ItemType) && Text(item.Title, 160) && Text(item.Subtitle, 160) &&
            item.DurationMilliseconds is >= 0 and <= 604_800_000 && Text(item.Uri, 2048) &&
            (item.Uri.StartsWith("spotify:track:", StringComparison.Ordinal) || item.Uri.StartsWith("spotify:episode:", StringComparison.Ordinal)) &&
            Https(item.SpotifyUrl, 4096) && (item.ArtworkUrl is null || Https(item.ArtworkUrl, 2048)));
    private static bool Text(string? value, int maximum) => value is not null && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Https(string? value, int maximum) => Text(value, maximum) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo);
}
