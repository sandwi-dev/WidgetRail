using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Trusted codec output. No filesystem paths cross widget IPC.</summary>
public sealed record CaptureImageDimensions(int Width, int Height);

public sealed class CaptureLibraryReadLease : IAsyncDisposable
{
    private readonly string? temporary;
    internal CaptureLibraryReadLease(CaptureAttachment media, FileStream stream, string? temporary = null)
    { Media = media; Stream = stream; this.temporary = temporary; }
    public CaptureAttachment Media { get; }
    public FileStream Stream { get; }
    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync().ConfigureAwait(false);
        if (temporary is not null) try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Profile-owned retained media. Writes publish metadata last; saved originals are never transformed.</summary>
public sealed partial class CaptureLibraryStore : IDisposable
{
    private readonly string root;
    private readonly string captureDirectory;
    private readonly Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? normalize;
    private readonly Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? thumbnail;
    private readonly Func<string,CaptureAttachment,CancellationToken,Task<CaptureAttachment>>? inspect;
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Saved(int Schema, CaptureLibraryItem Item, string Sha256, string? FileName = null, long MediaStamp = 0, bool MetadataValid = true);

    public CaptureLibraryStore(string root, string captureDirectory,
        Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? normalize = null)
        : this(root, captureDirectory, normalize, null) { }
    public CaptureLibraryStore(string root, string captureDirectory,
        Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? normalize,
        Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? thumbnail,
        Func<string,CaptureAttachment,CancellationToken,Task<CaptureAttachment>>? inspect = null)
    { this.root = Path.GetFullPath(root); this.captureDirectory = Path.IsPathFullyQualified(captureDirectory) ? Path.GetFullPath(captureDirectory) : string.Empty; this.normalize = normalize; this.thumbnail = thumbnail; this.inspect=inspect; }

    public async Task<CaptureLibraryThumbnail> ThumbnailAsync(string id, CancellationToken token)
    {
        DemandId(id);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var saved = await RefreshEditedAsync(Read(id),token).ConfigureAwait(false);
            if (saved.Item.Media.ContentType is not ("image/png" or "video/mp4")) throw Error("capture_library_thumbnail_unavailable");
            var original = new FileInfo(MediaPath(saved));
            if (!original.Exists || original.Length != saved.Item.Media.ByteLength) throw Error("capture_library_invalid_media");
            var path = Path.Combine(root, id + ".thumbnail"); PlainPath(path);
            if (File.Exists(path))
            {
                var cached = await ReadThumbnailAsync(path, token).ConfigureAwait(false);
                if (cached is not null) return cached;
                File.Delete(path);
            }
            if (thumbnail is null) throw Error("capture_library_codec_unavailable");
            await using var source = await VerifiedAsync(saved, token).ConfigureAwait(false);
            try
            {
                await thumbnail(MediaPath(saved), path, token).ConfigureAwait(false);
                return await ReadThumbnailAsync(path, token).ConfigureAwait(false) ?? throw Error("capture_library_invalid_media");
            }
            catch { DeleteOwned(path); throw; }
        }
        finally { gate.Release(); }
    }
    private static async Task<CaptureLibraryThumbnail?> ReadThumbnailAsync(string path, CancellationToken token)
    {
        PlainPath(path);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (input.Length is < 24 or > CaptureLibraryLimits.MaximumThumbnailBytes) return null;
        var bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var result = new CaptureLibraryThumbnail(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)), bytes);
        return result.IsWellFormed() ? result : null;
    }

    public async Task<CaptureLibraryPage> ListAsync(int offset, CancellationToken token, bool refresh = false)
    {
        if (offset < 0) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            if (refresh) Reconcile();
            var totals = IndexTotals();
            return new(IndexRows("ORDER BY saved_at DESC,id LIMIT $limit OFFSET $offset", ("$limit", CaptureLibraryLimits.PageSize), ("$offset", offset))
                .Select(s => s.Item).ToArray(), offset, totals.Count, totals.Bytes);
        }
        finally { gate.Release(); }
    }
    public async Task<CaptureLibraryItem> SaveAsync(HostCaptureAttachment source, CancellationToken token)
    {
        if (!source.Attachment.IsWellFormed()) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            PlainPath(source.Path);
            await using var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            if (input.Length != source.Attachment.ByteLength) throw Error("capture_unavailable");
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)); input.Position = 0;
            // Repeated Save (including a lost reply) returns the existing identical item.
            var existing = IndexRows("WHERE hash=$hash AND available=1", ("$hash", hash)).FirstOrDefault(i => SameMedia(i.Item.Media, source.Attachment));
            if (existing is not null && File.Exists(MediaPath(existing))) return existing.Item;
            DemandFreeSpace(input.Length);
            var id = Guid.NewGuid().ToString("N");
            var item = new CaptureLibraryItem(id, source.Attachment with { Id = id, ExpiresAtUnixMilliseconds = long.MaxValue }, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var saved = new Saved(2, item, hash, PublicName(item));
            var path = MediaPath(saved);
            var metadata = MetadataPath(id);
            var partial = metadata + ".pending";
            try
            {
                await File.WriteAllTextAsync(partial, JsonSerializer.Serialize(saved), token).ConfigureAwait(false);
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                { await input.CopyToAsync(output, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false); }
                token.ThrowIfCancellationRequested();
                var stamp=File.GetLastWriteTimeUtc(path).Ticks;
                saved = saved with { MediaStamp = stamp, Item=saved.Item with {Revision=stamp} };
                await File.WriteAllTextAsync(partial, JsonSerializer.Serialize(saved), token).ConfigureAwait(false);
                File.Move(partial, metadata);
                IndexPut(saved, input.Length);
                verified[item.Id] = (input.Length, saved.MediaStamp, hash);
                return saved.Item;
            }
            catch { if (!File.Exists(metadata) && DeleteOwned(path)) DeleteOwned(partial); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task<CaptureLibraryReadLease> OpenAsync(string id, CaptureImageResolution resolution, CancellationToken token)
    {
        DemandId(id); if (!Enum.IsDefined(resolution)) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var saved = await RefreshEditedAsync(Read(id),token).ConfigureAwait(false);
            var path = MediaPath(saved);
            var source = await VerifiedAsync(saved, token).ConfigureAwait(false);
            if (resolution != CaptureImageResolution.Context || saved.Item.Media.ContentType != "image/png" ||
                saved.Item.Media.Width <= 1920 && saved.Item.Media.Height <= 1080 && saved.Item.Media.ByteLength <= CaptureLimits.MaximumContextBytes)
                return new(saved.Item.Media, source);
            await using (source.ConfigureAwait(false))
            {
                if (normalize is null) throw Error("capture_library_codec_unavailable");
                var temporary = Path.Combine(root, "context-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    var dimensions = await normalize(path, temporary, token).ConfigureAwait(false);
                    PlainPath(temporary);
                    var size = new FileInfo(temporary).Length;
                    if (dimensions.Width is <= 0 or > 1920 || dimensions.Height is <= 0 or > 1080 || size is <= 0 or > CaptureLimits.MaximumContextBytes)
                        throw Error("capture_library_invalid_media");
                    var media = saved.Item.Media with { Width = dimensions.Width, Height = dimensions.Height, ByteLength = size };
                    return new(media, new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous), temporary);
                }
                catch { DeleteOwned(temporary); throw; }
            }
        }
        finally { gate.Release(); }
    }
    public async Task DeleteAsync(string id, CancellationToken token)
    {
        DemandId(id); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var saved = IndexRows("WHERE id=$id", ("$id", id)).FirstOrDefault();
            if (saved is null) return;
            token.ThrowIfCancellationRequested();
            var tombstone = Path.Combine(root, id + ".deleted"); PlainPath(tombstone);
            File.WriteAllText(tombstone, JsonSerializer.Serialize(saved));
            DeleteOwned(MetadataPath(id));
            IndexRemove(id); verified.Remove(id);
            FinishDelete(tombstone, saved);
        }
        finally { gate.Release(); }
    }
    // Compatibility for older widget packages: the capture already lives in the public folder.
    // No second file is produced. New widgets should offer OpenFolderAsync instead.
    public async Task<CaptureLibraryExport> ExportAsync(string id, CancellationToken token)
    {
        DemandId(id); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var saved = await RefreshEditedAsync(Read(id),token).ConfigureAwait(false); await using var input = await VerifiedAsync(saved, token).ConfigureAwait(false);
            return new(Path.GetFileName(MediaPath(saved)), input.Length);
        }
        finally { gate.Release(); }
    }
    internal async Task<string> FolderAsync(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var ownership = Own(); return captureDirectory; }
        finally { gate.Release(); }
    }
    private Saved Read(string id)
    {
        DemandId(id);
        var saved = IndexRows("WHERE id=$id", ("$id", id)).FirstOrDefault() ?? throw Error("capture_library_not_found");
        if (!saved.MetadataValid) throw Error("capture_library_invalid_media");
        PlainPath(MediaPath(saved));
        return saved;
    }
    private async Task<FileStream> VerifiedAsync(Saved saved, CancellationToken token)
    {
        var input = new FileStream(MediaPath(saved), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        try
        {
            var stamp=File.GetLastWriteTimeUtc(MediaPath(saved)).Ticks;
            if(verified.TryGetValue(saved.Item.Id,out var cached) && cached==(input.Length,stamp,saved.Sha256)) return input;
            var actual = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
            if (input.Length != saved.Item.Media.ByteLength || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(saved.Sha256), actual))
                throw Error("capture_library_invalid_media");
            if(verified.Count>=256) verified.Clear();
            verified[saved.Item.Id]=(input.Length,stamp,saved.Sha256);
            input.Position = 0; return input;
        }
        catch { await input.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private async Task<Saved> RefreshEditedAsync(Saved saved,CancellationToken token)
    {
        var path=MediaPath(saved); var file=new FileInfo(path);
        if(!file.Exists || inspect is null || file.Length==saved.Item.Media.ByteLength && file.LastWriteTimeUtc.Ticks==saved.MediaStamp) return saved;
        await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
        var media=await inspect(path,saved.Item.Media,token).ConfigureAwait(false);
        if(!media.IsWellFormed() || media.Id!=saved.Item.Id || media.ContentType!=saved.Item.Media.ContentType || media.ByteLength!=input.Length) throw Error("capture_library_invalid_media");
        var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,token).ConfigureAwait(false));
        var stamp=File.GetLastWriteTimeUtc(path).Ticks;
        var updated=saved with { Item=saved.Item with { Media=media,Available=true,Revision=stamp }, Sha256=hash,MediaStamp=stamp };
        DeleteOwned(Path.Combine(root,saved.Item.Id+".thumbnail")); WriteMetadata(updated); IndexPut(updated,input.Length);
        verified[saved.Item.Id]=(input.Length,updated.MediaStamp,hash); return updated;
    }
    private string MetadataPath(string id) => Path.Combine(root, id + ".json");
    private string MediaPath(Saved saved) => saved.FileName is { } name && ValidName(name) ? Path.Combine(captureDirectory, name) : throw Error("capture_library_invalid_media");
    private static string Extension(CaptureAttachment media) => media.ContentType == "image/png" ? ".png" : ".mp4";
    private static bool SameMedia(CaptureAttachment a, CaptureAttachment b) => a.ContentType == b.ContentType && a.Width == b.Width && a.Height == b.Height && a.ByteLength == b.ByteLength && a.DurationSeconds == b.DurationSeconds && a.SourceApplication == b.SourceApplication;
    private bool DeleteOwned(string path) { try { PlainPath(path); File.Delete(path); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }
    internal static void PlainPath(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try { if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw Error("capture_library_linked_path"); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static void DemandId(string id) { if (!CaptureLimits.ValidToken(id)) throw Error("invalid_payload"); }
    private static BrokerException Error(string code) => new(code, "Saved capture is unavailable or the library cannot complete this operation.");
}
