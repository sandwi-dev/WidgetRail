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
public sealed class CaptureLibraryStore
{
    private readonly string root;
    private readonly string exportRoot;
    private readonly Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? normalize;
    private readonly Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? thumbnail;
    private readonly int maximumItems;
    private readonly long maximumBytes;
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Saved(int Schema, CaptureLibraryItem Item, string Sha256);

    public CaptureLibraryStore(string root, string exportRoot,
        Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? normalize = null)
        : this(root, exportRoot, normalize, CaptureLibraryLimits.MaximumItems, CaptureLibraryLimits.MaximumBytes) { }
    public CaptureLibraryStore(string root, string exportRoot,
        Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? normalize,
        Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? thumbnail)
        : this(root, exportRoot, normalize, CaptureLibraryLimits.MaximumItems, CaptureLibraryLimits.MaximumBytes, thumbnail) { }
    internal CaptureLibraryStore(string root, string exportRoot, Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? normalize, int maximumItems, long maximumBytes,
        Func<string, string, CancellationToken, Task<CaptureImageDimensions>>? thumbnail = null)
    { this.maximumItems = maximumItems; this.maximumBytes = maximumBytes; this.root = Path.GetFullPath(root); this.exportRoot = Path.IsPathFullyQualified(exportRoot) ? Path.GetFullPath(exportRoot) : string.Empty; this.normalize = normalize; this.thumbnail = thumbnail; }

    public async Task<CaptureLibraryThumbnail> ThumbnailAsync(string id, CancellationToken token)
    {
        DemandId(id);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var saved = Read(id);
            if (saved.Item.Media.ContentType is not ("image/png" or "video/mp4")) throw Error("capture_library_thumbnail_unavailable");
            var original = new FileInfo(MediaPath(saved.Item));
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
                await thumbnail(MediaPath(saved.Item), path, token).ConfigureAwait(false);
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

    public async Task<CaptureLibraryPage> ListAsync(int offset, CancellationToken token)
    {
        if (offset is < 0 or > CaptureLibraryLimits.MaximumItems) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var items = Inventory();
            return new(items.OrderByDescending(i => i.Item.SavedAtUnixMilliseconds).ThenBy(i => i.Item.Id, StringComparer.Ordinal)
                .Skip(offset).Take(CaptureLibraryLimits.PageSize).Select(i => i.Item).ToArray(), offset, items.Count, StoredBytes());
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
            var items = Inventory();
            PlainPath(source.Path);
            await using var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            if (input.Length != source.Attachment.ByteLength) throw Error("capture_unavailable");
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)); input.Position = 0;
            // Repeated Save (including a lost reply) returns the existing identical item.
            var existing = items.FirstOrDefault(i => i.Item.Available && i.Sha256 == hash && SameMedia(i.Item.Media, source.Attachment));
            if (existing is not null) return existing.Item;
            if (items.Count >= maximumItems || StoredBytes() + input.Length > maximumBytes)
                throw Error("capture_library_full");
            var id = Guid.NewGuid().ToString("N");
            var item = new CaptureLibraryItem(id, source.Attachment with { Id = id, ExpiresAtUnixMilliseconds = long.MaxValue }, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var path = MediaPath(item);
            var metadata = MetadataPath(id);
            var partial = metadata + ".pending";
            try
            {
                await File.WriteAllTextAsync(partial, JsonSerializer.Serialize(new Saved(1, item, hash)), token).ConfigureAwait(false);
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                { await input.CopyToAsync(output, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false); }
                token.ThrowIfCancellationRequested();
                File.Move(partial, metadata);
                return item;
            }
            catch { if (DeleteOwned(path)) DeleteOwned(partial); throw; }
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
            var saved = Read(id);
            var path = MediaPath(saved.Item);
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
            var metadata = MetadataPath(id);
            var tombstone = Path.Combine(root, id + ".deleted"); PlainPath(tombstone);
            var mediaPaths = new[] { Path.Combine(root, id + ".png"), Path.Combine(root, id + ".mp4") };
            foreach (var media in mediaPaths) PlainPath(media);
            if (!File.Exists(metadata) && (!mediaPaths.Any(File.Exists) || File.Exists(tombstone))) return;
            token.ThrowIfCancellationRequested();
            if (File.Exists(metadata)) File.Move(metadata, tombstone);
            else { using var marker = new FileStream(tombstone, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            var removed = true;
            foreach (var extension in new[] { ".png", ".mp4", ".thumbnail" })
            {
                var media = Path.Combine(root, id + extension); PlainPath(media);
                try { File.Delete(media); } catch (IOException) { removed = false; } catch (UnauthorizedAccessException) { removed = false; }
            }
            if (removed) File.Delete(tombstone);
        }
        finally { gate.Release(); }
    }
    public async Task<CaptureLibraryExport> ExportAsync(string id, CancellationToken token)
    {
        DemandId(id); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var saved = Read(id); await using var input = await VerifiedAsync(saved, token).ConfigureAwait(false);
            if (exportRoot.Length == 0) throw Error("capture_library_export_unavailable");
            PlainPath(exportRoot); Directory.CreateDirectory(exportRoot);
            var name = "WidgetRail-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + Extension(saved.Item.Media);
            var path = Path.Combine(exportRoot, name); var partial = path + ".partial";
            try
            {
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                { await input.CopyToAsync(output, token).ConfigureAwait(false); await output.FlushAsync(token).ConfigureAwait(false); }
                token.ThrowIfCancellationRequested(); File.Move(partial, path);
                return new(name, saved.Item.Media.ByteLength);
            }
            catch { if (File.Exists(partial)) File.Delete(partial); throw; }
        }
        finally { gate.Release(); }
    }
    private FileStream Own()
    {
        PlainPath(root); Directory.CreateDirectory(root);
        PlainPath(Path.Combine(root, ".lock"));
        var lease = new FileStream(Path.Combine(root, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            // Finish only previously authorized deletions; never evict saved captures.
            foreach (var deleted in Directory.EnumerateFiles(root, "*.deleted").Take(CaptureLibraryLimits.MaximumItems))
            {
                var id = Path.GetFileNameWithoutExtension(deleted); if (!CaptureLimits.ValidToken(id)) continue;
                PlainPath(deleted);
                var removed = true;
                foreach (var extension in new[] { ".png", ".mp4", ".thumbnail" })
                {
                    var path = Path.Combine(root, id + extension); PlainPath(path);
                    try { File.Delete(path); } catch (IOException) { removed = false; } catch (UnauthorizedAccessException) { removed = false; }
                }
                if (removed) File.Delete(deleted);
            }
            foreach (var path in Directory.EnumerateFiles(root).Take(CaptureLibraryLimits.MaximumItems * 4 + 10))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var extension = Path.GetExtension(path);
                if (extension == ".thumbnail" && CaptureLimits.ValidToken(name) && !File.Exists(MetadataPath(name))) DeleteOwned(path);
                var context = extension == ".png" && name.StartsWith("context-", StringComparison.Ordinal) && CaptureLimits.ValidToken(name[8..]);
                if (context) DeleteOwned(path); // Active read leases deny deletion; a later operation retries.
                if (extension == ".pending" && Path.GetFileName(path).EndsWith(".json.pending", StringComparison.Ordinal) && CaptureLimits.ValidToken(Path.GetFileName(path)[..^13]))
                {
                    var id = Path.GetFileName(path)[..^13]; var removed = true;
                    if (!File.Exists(MetadataPath(id)))
                        foreach (var mediaExtension in new[] { ".png", ".mp4" }) removed &= DeleteOwned(Path.Combine(root, id + mediaExtension));
                    if (removed) DeleteOwned(path);
                }
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    private List<Saved> Inventory()
    {
        var files = Directory.EnumerateFiles(root, "*.json").Take(CaptureLibraryLimits.MaximumItems + 1).ToArray();
        if (files.Length > CaptureLibraryLimits.MaximumItems) throw Error("capture_library_full");
        var result = new List<Saved>();
        foreach (var path in files)
        {
            var id = Path.GetFileNameWithoutExtension(path); DemandId(id); PlainPath(path);
            try
            {
                var saved = Read(id); var media = new FileInfo(MediaPath(saved.Item));
                result.Add(media.Exists && media.Length == saved.Item.Media.ByteLength ? saved : saved with { Item = saved.Item with { Available = false } });
            }
            catch (Exception error) when (error is JsonException or IOException || error is BrokerException broker && broker.Code == "capture_library_invalid_media")
            {
                // Keep damaged entries visible and deletable without treating their
                // untrusted metadata as display text or a file path.
                result.Add(new(1, new(id, new(id, "image/png", 1, 1, 1, 0, long.MaxValue), 1, Available: false), new string('0', 64)));
            }
        }
        // Missing metadata alone is not proof of an unfinished save. Preserve the
        // original as an unavailable, explicitly deletable entry rather than evict it.
        foreach (var path in Directory.EnumerateFiles(root).Take(CaptureLibraryLimits.MaximumItems * 4 + 10))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (Path.GetExtension(path) is not (".png" or ".mp4") || !CaptureLimits.ValidToken(id) ||
                result.Any(entry => entry.Item.Id == id) || File.Exists(Path.Combine(root, id + ".deleted")) || File.Exists(MetadataPath(id) + ".pending")) continue;
            PlainPath(path);
            result.Add(new(1, new(id, new(id, "image/png", 1, 1, 1, 0, long.MaxValue), 1, Available: false), new string('0', 64)));
        }
        if (result.Count > CaptureLibraryLimits.MaximumItems) throw Error("capture_library_full");
        return result;
    }
    private Saved Read(string id)
    {
        DemandId(id); var path = MetadataPath(id); PlainPath(path);
        if (!File.Exists(path)) throw Error("capture_library_not_found");
        if (new FileInfo(path).Length > 8192) throw Error("capture_library_invalid_media");
        var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path)) ?? throw Error("capture_library_invalid_media");
        if (saved.Schema != 1 || !CaptureLibraryLimits.Valid(saved.Item) || saved.Item.Id != id || saved.Sha256 is not { Length: 64 } || !saved.Sha256.All(char.IsAsciiHexDigit))
            throw Error("capture_library_invalid_media");
        PlainPath(MediaPath(saved.Item));
        return saved;
    }
    private async Task<FileStream> VerifiedAsync(Saved saved, CancellationToken token)
    {
        var input = new FileStream(MediaPath(saved.Item), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        try
        {
            var actual = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
            if (input.Length != saved.Item.Media.ByteLength || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(saved.Sha256), actual))
                throw Error("capture_library_invalid_media");
            input.Position = 0; return input;
        }
        catch { await input.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private long StoredBytes()
    {
        long total = 0; var count = 0;
        foreach (var path in Directory.EnumerateFiles(root))
        {
            if (++count > CaptureLibraryLimits.MaximumItems * 4 + 10) throw Error("capture_library_full");
            PlainPath(path);
            if (Path.GetExtension(path) is ".png" or ".mp4") total = checked(total + new FileInfo(path).Length);
        }
        if (total > 128L * 1024 * 1024 * CaptureLibraryLimits.MaximumItems * 4) throw Error("capture_library_full");
        return total;
    }
    private string MetadataPath(string id) => Path.Combine(root, id + ".json");
    private string MediaPath(CaptureLibraryItem item) => Path.Combine(root, item.Id + Extension(item.Media));
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
