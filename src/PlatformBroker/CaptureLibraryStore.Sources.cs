using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private sealed record SourceGrant(string Id, string Label, string Directory, BrokerWidgetIdentity Owner, bool Enabled = true);
    private sealed class SourceWatch : IDisposable
    {
        internal readonly object Gate = new();
        internal bool Stopped;
        internal FileSystemWatcher? Watcher;
        internal readonly ConcurrentDictionary<string, byte> Pending = new(StringComparer.OrdinalIgnoreCase);
        internal int Rescan = 1;
        internal readonly ConcurrentDictionary<string, byte> Unsupported = new(StringComparer.OrdinalIgnoreCase);
        public void Dispose() { lock (Gate) { Stopped = true; Watcher?.Dispose(); } }
    }
    private readonly ConcurrentDictionary<string, SourceGrant> sourceGrants = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SourceWatch> sourceWatches = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource sourceStop = new();
    private Task? sourceTask;
    private int sourcesStopped;
    private const string ContextSuffix = ".widgetrail-context.json";

    /// <summary>Starts bounded discovery for persisted grants independently of widget residency.</summary>
    public void StartSources()
    {
        if (sourceTask is not null) return;
        sourceTask = Task.Run(SourceLoopAsync);
    }
    private void StopSources()
    {
        if (Interlocked.Exchange(ref sourcesStopped, 1) != 0) return;
        sourceStop.Cancel();
        try { sourceTask?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        foreach (var watcher in sourceWatches.Values) watcher.Dispose();
        sourceStop.Dispose();
    }
    private string SourceFile(string id) { DemandId(id); return Path.Combine(root, "source-" + id + ".grant"); }
    private SourceGrant ReadSource(string id)
    {
        DemandId(id);
        if (sourceGrants.TryGetValue(id, out var cached)) return cached;
        var path = SourceFile(id); PlainPath(path);
        if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) throw Error("capture_library_source_unavailable");
        var source = JsonSerializer.Deserialize<SourceGrant>(File.ReadAllText(path));
        if (source is null || source.Owner is null || source.Id != id || !CaptureLibrarySourceLimits.ValidLabel(source.Label) || !Path.IsPathFullyQualified(source.Directory))
            throw Error("capture_library_source_unavailable");
        source.Owner.Validate(); sourceGrants[id] = source; return source;
    }
    private string ResolveSourcePath(SourceGrant source, string relative)
    {
        if (!SourceEnabled(source) || !CaptureLibrarySourceLimits.ValidRelativePath(relative)) throw Error("capture_library_source_unavailable");
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.Directory));
        var path = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(DirectoryPrefix(directory), StringComparison.OrdinalIgnoreCase)) throw Error("invalid_payload");
        PlainPath(path); return path;
    }
    private static string DirectoryPrefix(string directory) => Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
    private static long? FileModifiedTime(DateTime value)
    {
        var timestamp = new DateTimeOffset(value).ToUnixTimeMilliseconds();
        return CaptureContextLimits.Timestamp(timestamp) ? timestamp : null;
    }
    private static bool Overlaps(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase) ||
        first.StartsWith(DirectoryPrefix(second), StringComparison.OrdinalIgnoreCase) || second.StartsWith(DirectoryPrefix(first), StringComparison.OrdinalIgnoreCase);
    private void WriteSource(SourceGrant source)
    {
        var path = SourceFile(source.Id); PlainPath(path); PlainPath(path + ".pending");
        File.WriteAllText(path + ".pending", JsonSerializer.Serialize(source)); File.Move(path + ".pending", path, true);
        sourceGrants[source.Id] = source;
    }
    private static bool SameOwner(BrokerWidgetIdentity first, BrokerWidgetIdentity second) => first.PackageId == second.PackageId &&
        first.PublisherId == second.PublisherId && first.InstanceId == second.InstanceId;
    private static CaptureSaveContext SourceContext(string sourceId, CaptureSaveContext context) => context with
    {
        IdempotencyKey = "source-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId + "/" + context.IdempotencyKey))).ToLowerInvariant()
    };
    private void LoadSources()
    {
        foreach (var path in Directory.EnumerateFiles(root, "source-*.grant"))
        {
            var id = Path.GetFileNameWithoutExtension(path)[7..];
            try
            {
                if (!CaptureLimits.ValidToken(id)) continue;
                var source = ReadSource(id);
                if (!source.Enabled && SourceRows(id, 0).Length == 0 && DeleteOwned(path)) sourceGrants.TryRemove(id, out _);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or BrokerException) { }
        }
    }
    /// <summary>Host-only grant creation: directory must come from native user selection.</summary>
    internal async Task<CaptureLibrarySource> AddSourceAsync(string directory, string label, BrokerWidgetIdentity owner, CancellationToken token)
    {
        owner.Validate();
        if (!CaptureLibrarySourceLimits.ValidLabel(label) || !Path.IsPathFullyQualified(directory)) throw Error("invalid_payload");
        directory = Path.GetFullPath(directory); PlainPath(directory);
        directory = Path.TrimEndingDirectorySeparator(directory);
        if (Overlaps(directory, Path.TrimEndingDirectorySeparator(root)) || captureDirectory.Length > 0 && Overlaps(directory, Path.TrimEndingDirectorySeparator(captureDirectory)))
            throw Error("capture_library_source_overlap");
        if (!Directory.Exists(directory)) throw Error("capture_library_source_unavailable");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); LoadSources();
            var source = sourceGrants.Values.FirstOrDefault(s => HasSourceAccess(s, owner) && s.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                if (sourceGrants.Values.Any(s => SourceEnabled(s) && (s.Directory.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
                    s.Directory.StartsWith(DirectoryPrefix(directory), StringComparison.OrdinalIgnoreCase) ||
                    directory.StartsWith(DirectoryPrefix(Path.TrimEndingDirectorySeparator(s.Directory)), StringComparison.OrdinalIgnoreCase))))
                    throw Error("capture_library_source_overlap");
                if (sourceGrants.Values.Count(SourceEnabled) >= CaptureLibrarySourceLimits.MaximumSources) throw Error("capture_library_source_limit");
                source = new(Guid.NewGuid().ToString("N"), label, directory, owner);
                token.ThrowIfCancellationRequested();
                WriteSource(source);
            }
            StartSources(); NotifyChanged(); return SourceView(source);
        }
        finally { gate.Release(); }
    }
    public async Task<CaptureLibrarySources> SourcesAsync(BrokerWidgetIdentity owner, CancellationToken token)
    {
        owner.Validate(); await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var ownership = Own(); LoadSources(); return new(sourceGrants.Values.Where(s => HasSourceAccess(s, owner)).Select(SourceView).ToArray()); }
        finally { gate.Release(); }
    }
    private CaptureLibrarySource SourceView(SourceGrant source)
    {
        using var query = Command("SELECT count(*) FROM captures WHERE json_extract(data,'$.Item.SourceId')=$id", ("$id", source.Id));
        return new(source.Id, source.Label, SourceEnabled(source) && Directory.Exists(source.Directory), Convert.ToInt32(query.ExecuteScalar()),
            sourceWatches.TryGetValue(source.Id, out var watch) ? watch.Unsupported.Count : 0, source.Directory);
    }
    public async Task RemoveSourceAsync(string id, BrokerWidgetIdentity owner, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); var source = ReadSource(id);
            if (!HasSourceAccess(source, owner)) throw Error("permission_denied");
            token.ThrowIfCancellationRequested();
            // Persist revocation before cleanup. A cancelled/failed cleanup resumes
            // in the broker, without reauthorizing or rediscovering provider files.
            WriteSource(source with { Enabled = false });
            if (sourceWatches.TryRemove(id, out var watch)) watch.Dispose();
            while (true)
            {
                var batch = SourceRows(id, 0);
                if (batch.Length == 0) break;
                foreach (var saved in batch) { token.ThrowIfCancellationRequested(); RemoveLinkedMetadata(saved); }
            }
            if (DeleteOwned(SourceFile(id))) sourceGrants.TryRemove(id, out _);
            NotifyChanged();
        }
        finally { gate.Release(); }
    }
    public async Task RemoveLinkAsync(CaptureLibraryMutationReference reference, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); var saved = Read(reference.Id);
            if (saved.Item.SourceId is null) throw Error("capture_library_not_linked");
            if (saved.Item.Revision != reference.ContentRevision || saved.Item.MetadataRevision != reference.MetadataRevision) throw Error("capture_library_metadata_conflict");
            var suppressed = Path.Combine(root, reference.Id + ".unlinked"); PlainPath(suppressed);
            File.WriteAllText(suppressed, string.Empty); RemoveLinkedMetadata(saved);
        }
        finally { gate.Release(); }
    }
    private void RemoveLinkedMetadata(Saved saved)
    {
        // Provider files and context receipts are deliberately untouched.
        if (!DeleteOwned(MetadataPath(saved.Item.Id))) throw Error("capture_library_io_failed");
        DeleteOwned(Path.Combine(root, saved.Item.Id + ".thumbnail"));
        IndexRemove(saved.Item.Id); verified.Remove(saved.Item.Id);
    }
    private Saved[] SourceRows(string sourceId, int offset) => IndexRows("WHERE json_extract(data,'$.Item.SourceId')=$source ORDER BY id LIMIT 64 OFFSET $offset", ("$source", sourceId), ("$offset", offset));
    private void RefreshSourceRows(string sourceId, CancellationToken token)
    {
        var after = string.Empty;
        while (true)
        {
            var batch = IndexRows("WHERE json_extract(data,'$.Item.SourceId')=$source AND id>$after ORDER BY id LIMIT 64", ("$source", sourceId), ("$after", after));
            if (batch.Length == 0) break;
            foreach (var saved in batch) { token.ThrowIfCancellationRequested(); RefreshEntry(saved.Item.Id); }
            after = batch[^1].Item.Id;
        }
    }
    public async Task<CaptureLibraryLinkResult> LinkAsync(CaptureLibraryLinkRequest request, BrokerWidgetIdentity owner, CancellationToken token)
    {
        if (request is null || !CaptureLimits.ValidToken(request.SourceId) || !CaptureLibrarySourceLimits.ValidRelativePath(request.RelativePath) ||
            request.Context is not null && !request.Context.IsWellFormed()) throw Error("invalid_payload");
        var item = await LinkFileAsync(request.SourceId, request.RelativePath, owner, token).ConfigureAwait(false);
        if (request.Context is { } context)
        {
            var attached = await AttachContextAsync(item.Id, ReadSource(request.SourceId).Owner, SourceContext(request.SourceId, context), token).ConfigureAwait(false);
            if (attached.Association.CaptureId != item.Id) throw Error("capture_library_context_conflict");
            item = attached.Item ?? item;
        }
        return new(item);
    }
    private async Task<CaptureLibraryItem> LinkFileAsync(string sourceId, string relative, BrokerWidgetIdentity owner, CancellationToken token, bool trustedScan = false)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); var source = ReadSource(sourceId);
            if (!SourceEnabled(source) || !trustedScan && !HasSourceAccess(source, owner)) throw Error("permission_denied");
            var path = ResolveSourcePath(source, relative);
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Id + "/" + relative.Replace('\\', '/').ToUpperInvariant())))[..32].ToLowerInvariant();
            var suppressed = Path.Combine(root, id + ".unlinked"); PlainPath(suppressed);
            if (File.Exists(suppressed)) throw Error("capture_library_link_removed");
            var previous = IndexRows("WHERE id=$id", ("$id", id)).FirstOrDefault();
            var file = new FileInfo(path);
            if (!file.Exists) throw Error("capture_library_not_found");
            if (!ValidName(file.Name)) throw Error("capture_library_unsupported_media");
            using var pinned = CapturePinnedFile.Open(path);
            if (previous is not null && previous.ExternalIdentity == pinned.Identity)
            { RefreshEntry(id); return ItemView(Read(id)); }
            if (inspect is null) throw Error("capture_library_codec_unavailable");
            var contentType = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".mp4" => "video/mp4", _ => throw Error("capture_library_unsupported_media") };
            if (file.Length <= 0 || file.Length > (contentType == "video/mp4" ? CaptureLibraryLimits.MaximumVideoBytes : CaptureLimits.MaximumAttachmentBytes)) throw Error("capture_library_unsupported_media");
            var stream = pinned.Stream;
            var seed = new CaptureAttachment(id, contentType, 1, 1, file.Length, contentType == "image/png" ? 0 : 1, long.MaxValue) { Origin = CaptureAttachmentOrigin.SavedLibrary };
            var media = await inspect(path, seed, token).ConfigureAwait(false);
            if (!media.IsWellFormed() || media.Id != id || media.Origin != CaptureAttachmentOrigin.SavedLibrary || media.ByteLength != stream.Length || media.ContentType != contentType)
                throw Error("capture_library_unsupported_media");
            // External originals are bound to a verified file incarnation, not
            // deduplicated by hashing potentially hours of recording on discovery.
            var hash = contentType == "video/mp4" ? new string('0', 64) : Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
            var stamp = file.LastWriteTimeUtc.Ticks;
            var item = previous?.Item ?? new(id, media, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                { Origin = OriginFor(source.Owner), SourceId = source.Id, FileName = file.Name };
            item = item with { Media = media, Available = true, Revision = pinned.Identity.ChangeTime, FileModifiedAtUnixMilliseconds = FileModifiedTime(file.LastWriteTimeUtc) };
            var saved = new Saved(2, item, hash, file.Name, stamp) { RelativePath = relative.Replace('\\', '/'), ExternalIdentity = pinned.Identity };
            token.ThrowIfCancellationRequested(); WriteMetadata(saved); IndexPut(saved, file.Length);
            return ItemView(saved);
        }
        finally { gate.Release(); }
    }
    private async Task SourceLoopAsync()
    {
        var token = sourceStop.Token;
        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try { using var ownership = Own(); LoadSources(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or BrokerException) { }
            finally { gate.Release(); }
            while (!token.IsCancellationRequested)
            {
                foreach (var source in sourceGrants.Values.Where(s => !SourceEnabled(s)).ToArray())
                {
                    if (sourceWatches.TryRemove(source.Id, out var retiredWatch)) retiredWatch.Dispose();
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        using var ownership = Own(); var batch = SourceRows(source.Id, 0);
                        foreach (var saved in batch) RemoveLinkedMetadata(saved);
                        if (batch.Length == 0 && DeleteOwned(SourceFile(source.Id))) sourceGrants.TryRemove(source.Id, out _);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or BrokerException) { }
                    finally { gate.Release(); }
                }
                foreach (var source in sourceGrants.Values.Where(SourceEnabled).ToArray())
                {
                    token.ThrowIfCancellationRequested();
                    try { await ScanSourceAsync(source, token).ConfigureAwait(false); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or BrokerException) { }
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task ScanSourceAsync(SourceGrant source, CancellationToken token)
    {
        PlainPath(source.Directory);
        if (!Directory.Exists(source.Directory))
        {
            if (sourceWatches.TryRemove(source.Id, out var missing))
            {
                missing.Dispose();
                await gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    using var ownership = Own();
                    RefreshSourceRows(source.Id, token);
                }
                finally { gate.Release(); }
            }
            return;
        }
        var watch = sourceWatches.GetOrAdd(source.Id, _ => new());
        lock (watch.Gate)
        {
            if (watch.Stopped || !sourceGrants.TryGetValue(source.Id, out var currentGrant) || !SourceEnabled(currentGrant)) return;
            if (watch.Watcher is null)
            {
            var watcher = new FileSystemWatcher(source.Directory) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            void Queue(string path)
            {
                if (path.EndsWith(ContextSuffix, StringComparison.OrdinalIgnoreCase)) path = path[..^ContextSuffix.Length];
                if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".mp4")) { if (Directory.Exists(path)) Interlocked.Exchange(ref watch.Rescan, 1); return; }
                var relative = Path.GetRelativePath(source.Directory, path);
                if (!CaptureLibrarySourceLimits.ValidRelativePath(relative)) return;
                if (watch.Pending.Count >= 1024) Interlocked.Exchange(ref watch.Rescan, 1); else watch.Pending[relative] = 0;
            }
            watcher.Created += (_, e) => Queue(e.FullPath); watcher.Changed += (_, e) => Queue(e.FullPath); watcher.Deleted += (_, e) => Queue(e.FullPath);
            watcher.Renamed += (_, e) => { Queue(e.OldFullPath); Queue(e.FullPath); };
            watcher.Error += (_, _) => Interlocked.Exchange(ref watch.Rescan, 1);
            watch.Watcher = watcher; watcher.EnableRaisingEvents = true;
            }
        }
        if (Interlocked.Exchange(ref watch.Rescan, 0) != 0)
        {
            watch.Unsupported.Clear();
            var count = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 16 };
            foreach (var path in Directory.EnumerateFiles(source.Directory, "*", options))
            {
                token.ThrowIfCancellationRequested();
                if (!SourceEnabled(source)) return;
                if (++count > 100_000) { watch.Unsupported.TryAdd("<scan-limit>", 0); break; }
                if (Path.GetExtension(path).ToLowerInvariant() is ".png" or ".mp4") await TryLinkAsync(path).ConfigureAwait(false);
                if (count % 32 == 0) await Task.Delay(10, token).ConfigureAwait(false);
            }
            // Overflow recovery also observes deletions that no longer enumerate.
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using var ownership = Own();
                RefreshSourceRows(source.Id, token);
            }
            finally { gate.Release(); }
        }
        foreach (var relative in watch.Pending.Keys.Take(32).ToArray())
        {
            if (!SourceEnabled(source)) return;
            watch.Pending.TryRemove(relative, out var attempts); await TryLinkAsync(Path.Combine(source.Directory, relative), attempts).ConfigureAwait(false);
        }
        async Task TryLinkAsync(string path, byte attempts = 0)
        {
            var relative = Path.GetRelativePath(source.Directory, path);
            if (!CaptureLibrarySourceLimits.ValidRelativePath(relative)) return;
            try
            {
                if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddSeconds(-2)) { watch.Pending.TryAdd(relative, attempts); return; }
                if (!File.Exists(path))
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        using var ownership = Own();
                        foreach (var saved in IndexRows("WHERE json_extract(data,'$.Item.SourceId')=$id AND json_extract(data,'$.RelativePath')=$path COLLATE NOCASE LIMIT 1",
                            ("$id", source.Id), ("$path", relative.Replace('\\', '/')))) RefreshEntry(saved.Item.Id);
                    }
                    finally { gate.Release(); }
                    return;
                }
                var item = await LinkFileAsync(source.Id, relative, source.Owner, token, trustedScan: true).ConfigureAwait(false);
                watch.Unsupported.TryRemove(relative, out _);
                var contextFile = path + ContextSuffix; PlainPath(contextFile);
                if (File.Exists(contextFile))
                {
                    using var contextLease = CapturePinnedFile.Open(contextFile);
                    if (contextLease.Stream.Length is <= 0 or > 8192) return;
                    var bytes = new byte[(int)contextLease.Stream.Length];
                    await contextLease.Stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                    var context = JsonSerializer.Deserialize<CaptureSaveContext>(bytes, BrokerJson.StrictOptions);
                    if (context?.IsWellFormed() == true)
                    {
                        var attached = await AttachContextAsync(item.Id, source.Owner, SourceContext(source.Id, context), token).ConfigureAwait(false);
                        if (attached.Association.CaptureId != item.Id) throw Error("capture_library_context_conflict");
                    }
                }
            }
            catch (IOException)
            {
                if (attempts < 5 && watch.Pending.Count < 1024) watch.Pending.TryAdd(relative, (byte)(attempts + 1));
                else if (watch.Unsupported.Count < 1024) watch.Unsupported.TryAdd(relative, 0);
            }
            catch (Exception error) when (error is BrokerException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or JsonException)
            { if (error is not JsonException and not BrokerException { Code: "capture_library_link_removed" } && watch.Unsupported.Count < 1024) watch.Unsupported.TryAdd(relative, 0); }
        }
    }
}
