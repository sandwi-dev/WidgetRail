using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    // Copying and editing share pinned source authority and crash-safe publication,
    // but a byte copy never goes through a codec or inherits trim/encoding limits.
    internal async Task<CaptureLibraryItem> SaveCopyAsync(CaptureLibraryMutationReference reference,
        BrokerWidgetIdentity actor, CancellationToken token)
    {
        using var session = await OpenEditSessionAsync(reference, actor, () => !token.IsCancellationRequested, token).ConfigureAwait(false);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, session.LifetimeToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(BrokerTransportLimits.CaptureCopyTimeoutSeconds));
        var cancellation = lifetime.Token;
        var original = session.Item;
        var id = session.ResultId;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var title = original.Title ?? original.Media.SourceApplication?.ApplicationName ?? "Capture";
        title = title[..Math.Min(title.Length, 153)] + " (copy)";
        var item = new CaptureLibraryItem(id, original.Media with { Id = id, Origin = CaptureAttachmentOrigin.SavedLibrary,
            ExpiresAtUnixMilliseconds = long.MaxValue }, now)
        { Title = title, Origin = OriginFor(actor), CapturedAtUnixMilliseconds = original.CapturedAtUnixMilliseconds };
        var temporary = EditTemporary(id, item.Media.ContentType);
        Saved? saved = null;
        FileStream? jobLock = null;
        var published = false;
        var associations = new List<SavedAssociation>();
        try
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                using var ownership = Own();
                PlainPath(EditDirectory); Directory.CreateDirectory(EditDirectory);
                jobLock = AcquireEditLock(); RecoverEditsOwned();
                DemandCurrent(); DemandFreeSpace(original.Media.ByteLength);
                var staging = Path.GetDirectoryName(temporary)!; PlainPath(staging); Directory.CreateDirectory(staging);
                File.SetAttributes(staging, File.GetAttributes(staging) | FileAttributes.Hidden);
                File.WriteAllText(EditJournalPath(id), JsonSerializer.Serialize(new EditJournal(id, item.Media.ContentType, now, Standalone: true)));
            }
            finally { gate.Release(); }
            PlainPath(temporary);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536]; long copied = 0;
                var input = session.Source.Stream; input.Position = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false);
                    if (count == 0) break;
                    copied += count;
                    if (copied > original.Media.ByteLength) throw Error("capture_library_invalid_media");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false);
                }
                if (copied != original.Media.ByteLength) throw Error("capture_library_invalid_media");
                await output.FlushAsync(cancellation).ConfigureAwait(false);
            }
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                using var ownership = Own(); DemandCurrent();
                RefreshEntry(reference.Id, forceMetadata: true);
                var current = Read(reference.Id);
                if (current.Item.Revision != reference.ContentRevision || current.Item.MetadataRevision != reference.MetadataRevision)
                    throw Error("capture_library_metadata_conflict");
                var digest = Convert.ToHexString(hash.GetHashAndReset());
                // Linked videos intentionally use file-incarnation authority instead
                // of hashing hours of video during discovery. We hash this copy once.
                var unhashedLinkedVideo = current.Item.SourceId is not null && current.Item.Media.ContentType == "video/mp4" && current.Sha256 == new string('0', 64);
                if (!unhashedLinkedVideo && !string.Equals(current.Sha256, digest, StringComparison.OrdinalIgnoreCase)) throw Error("capture_library_invalid_media");
                var stamp = File.GetLastWriteTimeUtc(temporary).Ticks;
                item = item with { Revision = stamp };
                saved = new(2, item, digest, PublicName(item), stamp);
                using (var query = Command("SELECT key_hash FROM capture_associations WHERE capture_id=$id LIMIT $limit",
                    ("$id", reference.Id), ("$limit", CaptureEditLimits.MaximumAssociations + 1)))
                using (var reader = query.ExecuteReader())
                    while (reader.Read())
                    {
                        if (associations.Count == CaptureEditLimits.MaximumAssociations) throw Error("capture_library_edit_context_limit");
                        var previous = ReadAssociation(AssociationPath(reader.GetString(0)), reader.GetString(0));
                        associations.Add(previous with { KeyHash = HashText("copy\0" + id + "\0" + previous.KeyHash),
                            Association = previous.Association with { Id = Guid.NewGuid().ToString("N"), CaptureId = id, RecordedAtUnixMilliseconds = now } });
                    }
                foreach (var association in associations) File.WriteAllText(AssociationPath(association.KeyHash) + ".pending", JsonSerializer.Serialize(association));
                File.WriteAllText(MetadataPath(id) + ".pending", JsonSerializer.Serialize(saved));
                DemandCurrent();
                File.Move(temporary, MediaPath(saved), overwrite: false);
                File.Move(MetadataPath(id) + ".pending", MetadataPath(id), overwrite: false);
                published = true;
                IndexPut(saved, item.Media.ByteLength);
                foreach (var association in associations)
                { File.Move(AssociationPath(association.KeyHash) + ".pending", AssociationPath(association.KeyHash), overwrite: false); IndexAssociation(association); }
                DeleteOwned(EditJournalPath(id));
                return ItemView(saved);
            }
            finally { gate.Release(); }
        }
        finally
        {
            if (!published)
            {
                DeleteOwned(temporary);
                if (saved is not null) DeleteOwned(MediaPath(saved));
                DeleteOwned(MetadataPath(id) + ".pending");
                foreach (var association in associations) DeleteOwned(AssociationPath(association.KeyHash) + ".pending");
                DeleteOwned(EditJournalPath(id));
            }
            jobLock?.Dispose();
        }
        void DemandCurrent()
        {
            cancellation.ThrowIfCancellationRequested();
            if (!session.IsCurrent) throw Error("capture_library_edit_source_retired");
        }
    }
}
