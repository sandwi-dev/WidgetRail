using System.Security.Cryptography;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Anonymous catalogue images are immutable and share a bounded encoded-byte cache.</summary>
internal static class CataloguePreviewImageCache
{
    private const int MaximumImageBytes = 2 * 1024 * 1024;
    private const long MaximumCacheBytes = 64L * 1024 * 1024;
    private const int MaximumFiles = 512;
    private static readonly SemaphoreSlim Writes = new(1, 1);
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetRail", "cache", "catalogue-theme-previews");

    internal static async Task<ReadOnlyMemory<byte>> ReadAsync(Uri uri, string digest, CancellationToken token)
    {
        var path = Path.Combine(Root, digest + ".png");
        var cached = await ReadCachedAsync(path, digest, token).ConfigureAwait(false);
        if (!cached.IsEmpty) return cached;
        var content = await NativePublicMediaTransport.Client.DownloadAsync(uri, PublicMediaPolicy.SameOrigin(uri),
            MaximumImageBytes, token).ConfigureAwait(false);
        if (!Matches(content.Bytes, digest)) throw new InvalidDataException("The preview image differs from its catalogue digest.");
        await SaveAsync(path, content.Bytes, token).ConfigureAwait(false);
        return content.Bytes;
    }

    private static async Task<ReadOnlyMemory<byte>> ReadCachedAsync(string path, string digest, CancellationToken token)
    {
        try
        {
            if (!SafeRoot() || !File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return default;
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length is <= 0 or > MaximumImageBytes) return default;
            var buffer = new byte[file.Length];
            await file.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
            if (!Matches(buffer, digest)) return default;
            try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return buffer;
        }
        catch (IOException) { return default; }
        catch (UnauthorizedAccessException) { return default; }
    }

    private static async Task SaveAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        await Writes.WaitAsync(token).ConfigureAwait(false);
        var temporary = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(Root);
            if (!SafeRoot()) return;
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
                await file.WriteAsync(bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
            var entries = new DirectoryInfo(Root).EnumerateFiles("*.png")
                .Where(file => file.Name.Length == 68 && file.Name[..64].All(char.IsAsciiHexDigit) &&
                    !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .OrderByDescending(file => file.LastAccessTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal);
            long total = 0;
            int count = 0;
            foreach (var entry in entries)
            {
                total += entry.Length;
                if (++count > MaximumFiles || total > MaximumCacheBytes) entry.Delete();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            try
            {
                if (SafeRoot() && File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { Writes.Release(); }
        }
    }

    private static bool SafeRoot() => Directory.Exists(Root) && !File.GetAttributes(Root).HasFlag(FileAttributes.ReparsePoint);

    private static bool Matches(ReadOnlyMemory<byte> bytes, string digest) =>
        PublicMediaFormat.Identify(bytes.Span) == "image/png" &&
        Convert.ToHexString(SHA256.HashData(bytes.Span)).Equals(digest, StringComparison.OrdinalIgnoreCase);
}
