using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    public Task<CaptureLibraryBatchResult> UpdateMetadataBatchAsync(CaptureLibraryBatchMetadataRequest request, CancellationToken token)
    {
        if (request.Items is not { Count: > 0 and <= CaptureLibraryLimits.MaximumQueryItems } ||
            request.Items.Any(item => item is null || !CaptureLimits.ValidToken(item.Id) || item.ExpectedRevision < 0 || !CaptureLibraryLimits.ValidText(item.Title, 160)) ||
            request.Items.Select(item => item.Id).Distinct().Count() != request.Items.Count) throw Error("invalid_payload");
        var items = request.Items.ToArray();
        return BatchAsync(items, item => item.Id, async item => await UpdateMetadataAsync(item, token).ConfigureAwait(false), token);
    }

    public Task<CaptureLibraryBatchResult> RecycleAsync(CaptureLibraryRecycleRequest request, CancellationToken token)
    {
        if (request.Items is not { Count: > 0 and <= CaptureLibraryLimits.MaximumQueryItems } ||
            request.Items.Any(item => item is null || !CaptureLimits.ValidToken(item.Id) || item.ContentRevision < 0 || item.MetadataRevision < 0) ||
            request.Items.Select(item => item.Id).Distinct().Count() != request.Items.Count) throw Error("invalid_payload");
        var items = request.Items.ToArray();
        return BatchAsync(items, item => item.Id, async item => { await RecycleOneAsync(item, token).ConfigureAwait(false); return null; }, token);
    }

    private static async Task<CaptureLibraryBatchResult> BatchAsync<T>(IReadOnlyList<T> items, Func<T,string> id,
        Func<T,Task<CaptureLibraryItem?>> mutate, CancellationToken token)
    {
        var results = new List<CaptureLibraryMutationResult>(items.Count);
        foreach (var item in items)
        {
            if (token.IsCancellationRequested) { results.Add(new(id(item), ErrorCode: "cancelled")); continue; }
            try { results.Add(new(id(item), await mutate(item).ConfigureAwait(false))); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { results.Add(new(id(item), ErrorCode: "cancelled")); }
            catch (BrokerException error) { results.Add(new(id(item), ErrorCode: error.Code)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { results.Add(new(id(item), ErrorCode: "capture_library_io_failed")); }
        }
        return new(results.ToArray());
    }

    private async Task RecycleOneAsync(CaptureLibraryMutationReference reference, CancellationToken token)
    {
        if (recycle is null) throw Error("capture_library_recycle_unavailable");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            RefreshEntry(reference.Id, forceMetadata: true);
            var saved = Read(reference.Id);
            if (saved.Item.SourceId is not null) throw Error("capture_library_read_only_source");
            if (saved.Item.Revision != reference.ContentRevision || saved.Item.MetadataRevision != reference.MetadataRevision)
                throw Error("capture_library_metadata_conflict");
            var path = MediaPath(saved); PlainPath(path);
            if (!File.Exists(path)) throw Error("capture_library_not_found");
            // Persist recovery metadata before the OS operation. It cannot authorize
            // permanent deletion. On restore, the same original filename rejoins the library.
            var marker = Path.Combine(root, reference.Id + ".recycled"); PlainPath(marker);
            token.ThrowIfCancellationRequested();
            File.WriteAllText(marker, JsonSerializer.Serialize(saved));
            bool removed;
            try { removed = await recycle(path, token).ConfigureAwait(false); }
            catch when (!File.Exists(path)) { removed = true; }
            catch { DeleteOwned(marker); throw; }
            if (!removed || File.Exists(path)) { DeleteOwned(marker); throw Error("capture_library_recycle_unavailable"); }
            IndexRecycled(saved); DeleteOwned(MetadataPath(reference.Id));
            IndexRemove(reference.Id); verified.Remove(reference.Id); DeleteOwned(Path.Combine(root, reference.Id + ".thumbnail"));
        }
        finally { gate.Release(); }
    }

    public async Task RemoveUnavailableAsync(CaptureLibraryMutationReference reference, CancellationToken token)
    {
        DemandId(reference.Id);
        if (reference.ContentRevision < 0 || reference.MetadataRevision < 0) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); RefreshEntry(reference.Id, forceMetadata: true);
            var saved = IndexRows("WHERE id=$id", ("$id", reference.Id)).FirstOrDefault() ?? throw Error("capture_library_not_found");
            var path = MediaPath(saved); PlainPath(path);
            if (File.Exists(path) || saved.Item.Revision != reference.ContentRevision || saved.Item.MetadataRevision != reference.MetadataRevision)
                throw Error("capture_library_metadata_conflict");
            token.ThrowIfCancellationRequested();
            // This operation removes only unavailable library metadata. Never
            // delete the public media path, even if a file appears after checking.
            if (!DeleteOwned(MetadataPath(reference.Id))) throw Error("capture_library_io_failed");
            IndexRemove(reference.Id); verified.Remove(reference.Id);
            DeleteOwned(Path.Combine(root, reference.Id + ".thumbnail"));
        }
        finally { gate.Release(); }
    }

    internal async Task<string> RevealPathAsync(string id, CancellationToken token)
    {
        DemandId(id); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); var path = MediaPath(Read(id)); PlainPath(path);
            if (!File.Exists(path)) throw Error("capture_library_not_found");
            return path;
        }
        finally { gate.Release(); }
    }
    private void IndexRecycled(Saved saved) => Execute("INSERT INTO recycled VALUES($id,$name) ON CONFLICT(id) DO UPDATE SET name=excluded.name",
        ("$id", saved.Item.Id), ("$name", saved.FileName!));
    private void RefreshRecycled(string id)
    {
        var marker = Path.Combine(root, id + ".recycled"); PlainPath(marker);
        if (!File.Exists(marker)) { Execute("DELETE FROM recycled WHERE id=$id", ("$id",id)); return; }
        try
        {
            var saved = ReadMetadata(marker); var path = MediaPath(saved); PlainPath(path);
            if (File.Exists(path))
            {
                // A failed recycle can leave a marker beside newer metadata.
                // Never replace a newer user edit during recovery.
                if (File.Exists(MetadataPath(id)))
                {
                    var current = ReadMetadata(MetadataPath(id));
                    if (current.Item.MetadataRevision >= saved.Item.MetadataRevision) saved = current;
                }
                WriteMetadata(saved); DeleteOwned(marker); Execute("DELETE FROM recycled WHERE id=$id", ("$id",id)); RefreshEntry(id);
            }
            else { IndexRecycled(saved); DeleteOwned(MetadataPath(id)); IndexRemove(id); }
        }
        catch (Exception error) when (error is IOException or JsonException or BrokerException) { /* Unreadable marker never authorizes a file operation. */ }
    }
}
