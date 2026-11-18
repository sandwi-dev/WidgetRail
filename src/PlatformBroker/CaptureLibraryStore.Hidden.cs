using System.Text.Json;
using Microsoft.Data.Sqlite;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    // Canonical user choices are deliberately separate from the disposable media index.
    // ATTACH permits indexed membership checks without a process-wide exclusion cache.
    private bool hiddenStateAvailable;
    private bool hiddenMigrationComplete;
    private bool hiddenStateFaulted;
    private void OpenHiddenState()
    {
        hiddenStateAvailable = false;
        if (hiddenStateFaulted) return;
        var path = Path.Combine(root, "library-user-state.sqlite");
        var marker = Path.Combine(root, "library-user-state.initialized");
        PlainPath(path); PlainPath(marker); PlainPath(path + "-journal");
        PlainPath(path + "-wal"); PlainPath(path + "-shm");
        if (File.Exists(marker) && !File.Exists(path)) return;
        var attached = false;
        try
        {
            Execute("ATTACH DATABASE $path AS userstate", ("$path", path)); attached = true;
            if (!File.Exists(marker))
            {
                Execute("CREATE TABLE IF NOT EXISTS userstate.hidden(id TEXT PRIMARY KEY,legacy INTEGER NOT NULL CHECK(legacy IN(0,1)),snapshot TEXT)");
                // Commit the schema before the sentinel. A crash before this marker can
                // retry initialization; a missing canonical file afterwards fails closed.
                WriteSourceMarker(marker);
            }
            using (var check = Command("SELECT id,legacy,snapshot FROM userstate.hidden LIMIT 0")) check.ExecuteNonQuery();
            hiddenStateAvailable = true;
            foreach (var legacy in hiddenMigrationComplete ? [] : Directory.EnumerateFiles(root, "*.unlinked"))
            {
                var id = Path.GetFileNameWithoutExtension(legacy);
                if (!CaptureLimits.ValidToken(id)) continue;
                PlainPath(legacy);
                var before = ChangeCount();
                Execute("INSERT OR IGNORE INTO userstate.hidden(id,legacy,snapshot) VALUES($id,1,NULL)", ("$id", id));
                if (ChangeCount() != before) DiscoveryChanged();
                // SQLite's durable commit precedes deleting the old marker. Retrying
                // after either boundary cannot resurrect an excluded path.
                DeleteOwned(legacy);
            }
            hiddenMigrationComplete = true;
        }
        catch (Exception error) when (error is SqliteException or IOException or UnauthorizedAccessException)
        {
            hiddenStateAvailable = false; hiddenStateFaulted = true;
            if (attached) try { Execute("DETACH DATABASE userstate"); } catch (SqliteException) { }
        }
    }

    private void DemandHiddenState()
    {
        if (!hiddenStateAvailable) throw new BrokerException("capture_library_user_state_unavailable",
            "Hidden capture records are unavailable. Restore library-user-state.sqlite from a backup and restart WidgetRail; linked discovery is paused to preserve your choices.");
    }

    private void DiscoveryChanged()
    {
        Execute("UPDATE library_revision SET sequence=sequence+1 WHERE id=1; UPDATE library_ordering SET sequence=sequence+1 WHERE id=1");
        NotifyChanged();
    }

    private (bool Exists, bool Legacy, Saved? Snapshot) HiddenRecord(string id)
    {
        DemandHiddenState();
        try
        {
            using var command = Command("SELECT legacy,snapshot,length(snapshot) FROM userstate.hidden WHERE id=$id", ("$id", id));
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return default;
            Saved? saved = null;
            if (!reader.IsDBNull(1))
            {
                if (reader.GetInt64(2) is <= 0 or > 8192) throw HiddenStateError();
                try { saved = JsonSerializer.Deserialize<Saved>(reader.GetString(1)); }
                catch (JsonException) { throw HiddenStateError(); }
                if (saved is null || saved.Schema != 2 || saved.Item is null || saved.Item.Id != id || saved.Item.SourceId is null ||
                    !CaptureLibraryLimits.Valid(saved.Item) || !CaptureLibrarySourceLimits.ValidRelativePath(saved.RelativePath) ||
                    saved.FileName is not { } name || !ValidName(name) ||
                    !Path.GetExtension(name).Equals(Extension(saved.Item.Media), StringComparison.OrdinalIgnoreCase) ||
                    saved.Sha256 is not { Length:64 } || !saved.Sha256.All(char.IsAsciiHexDigit))
                    throw HiddenStateError();
            }
            return (true, reader.GetInt32(0) != 0, saved);
        }
        catch (SqliteException) { throw HiddenStateError(); }
    }

    private BrokerException HiddenStateError()
    {
        hiddenStateAvailable = false; hiddenStateFaulted = true;
        return new("capture_library_user_state_unavailable", "Hidden capture records are damaged. Restore library-user-state.sqlite from a backup and restart WidgetRail; linked discovery is paused.");
    }

    private void UpdateHiddenSnapshot(Saved saved)
    {
        if (saved.Item.SourceId is null) return;
        DemandHiddenState();
        Execute("UPDATE userstate.hidden SET snapshot=$snapshot WHERE id=$id", ("$snapshot", JsonSerializer.Serialize(saved)), ("$id", saved.Item.Id));
    }

    private static bool SamePhysicalFile(CaptureFileIdentity? left, CaptureFileIdentity right) => left is not null &&
        left.VolumeSerialNumber == right.VolumeSerialNumber && left.FileIdLow == right.FileIdLow && left.FileIdHigh == right.FileIdHigh;
    private static bool HasStableFileIdentity(CaptureFileIdentity? identity) => identity is { VolumeSerialNumber: not 0 } &&
        (identity.FileIdLow != 0 || identity.FileIdHigh != 0);

    private bool ReleaseReplacedHidden(string id, CaptureFileIdentity identity)
    {
        var hidden = HiddenRecord(id);
        if (!hidden.Exists || hidden.Legacy || hidden.Snapshot?.ExternalIdentity is not { } expected || SamePhysicalFile(expected, identity)) return false;
        // Retire stale derived metadata before releasing its exclusion. If a
        // locked sidecar prevents cleanup, retain the canonical choice for retry.
        if (!DeleteOwned(MetadataPath(id)) || !DeleteThumbnails(id)) throw Error("capture_library_io_failed");
        IndexRemove(id); verified.Remove(id);
        Execute("DELETE FROM userstate.hidden WHERE id=$id", ("$id", id));
        DiscoveryChanged();
        // Metadata belonging to the old physical file must not be inherited by its replacement.
        return true;
    }

    private void RestoreHiddenIndex()
    {
        if (!hiddenStateAvailable) return;
        try { RestoreHiddenIndexCore(); }
        catch (SqliteException) { _ = HiddenStateError(); }
        catch (BrokerException error) when (error.Code == "capture_library_user_state_unavailable") { }
    }

    private void RestoreHiddenIndexCore()
    {
        var after = string.Empty;
        while (true)
        {
            using var command = Command("SELECT id FROM userstate.hidden WHERE id>$after AND snapshot IS NOT NULL ORDER BY id LIMIT 64", ("$after", after));
            using var reader = command.ExecuteReader(); var ids = new List<string>();
            while (reader.Read()) ids.Add(reader.GetString(0)); reader.Close();
            if (ids.Count == 0) break;
            foreach (var id in ids)
            {
                if (!CaptureLimits.ValidToken(id)) throw HiddenStateError();
                if (File.Exists(Path.Combine(root, id + ".recycled"))) continue;
                var saved = HiddenRecord(id).Snapshot!;
                try
                {
                    if (!SourceEnabled(ReadSource(saved.Item.SourceId!))) continue;
                    if (!File.Exists(MetadataPath(id))) WriteMetadata(saved);
                    IndexPut(saved, saved.Item.Available ? saved.Item.Media.ByteLength : 0);
                    RefreshEntry(id, true);
                }
                catch (BrokerException error) when (error.Code is "capture_library_source_unavailable" or "capture_library_linked_path") { }
            }
            after = ids[^1];
        }
    }

    public async Task<CaptureLibraryItem> UnhideAsync(CaptureLibraryMutationReference reference, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(reference); DemandId(reference.Id);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); DemandHiddenState();
            var saved = Read(reference.Id);
            if (!HiddenRecord(reference.Id).Exists) throw Error("capture_library_not_hidden");
            using var original = File.Exists(MediaPath(saved)) ? CapturePinnedFile.Open(MediaPath(saved)) : null;
            if (original is not null && saved.ExternalIdentity is not null && !SamePhysicalFile(saved.ExternalIdentity, original.Identity))
                throw Error("capture_library_metadata_conflict");
            if (saved.Item.Revision != reference.ContentRevision || saved.Item.MetadataRevision != reference.MetadataRevision)
                throw Error("capture_library_metadata_conflict");
            token.ThrowIfCancellationRequested();
            var updated = saved with { Item = saved.Item with { MetadataRevision = checked(saved.Item.MetadataRevision + 1) } };
            // Publish recoverable metadata first. If interrupted, the canonical
            // record still hides the item; a completed deletion restores discovery.
            try
            {
                WriteMetadata(updated);
                Execute("DELETE FROM userstate.hidden WHERE id=$id", ("$id", reference.Id));
                DiscoveryChanged();
                IndexPut(updated, updated.Item.Available ? updated.Item.Media.ByteLength : 0);
            }
            catch { Interlocked.Exchange(ref rescan, 1); throw; }
            return ItemView(updated);
        }
        finally { gate.Release(); }
    }
}
