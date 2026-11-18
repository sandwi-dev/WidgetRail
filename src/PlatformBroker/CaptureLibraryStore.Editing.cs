using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private readonly object editLifetimeGate = new();
    private readonly CancellationTokenSource editStop = new();
    private int activeEdits, resourcesDisposed;
    // Standalone outputs (copies and original recordings) have no edit receipt.
    private sealed record EditJournal(string Id, string ContentType, long SavedAt,
        [property: System.Text.Json.Serialization.JsonPropertyName("Copy")] bool Standalone = false);
    private string EditDirectory => Path.Combine(root, "edits");
    private string EditTemporary(string id, string contentType) => Path.Combine(captureDirectory, ".widgetrail-edits", id + (contentType == "image/png" ? ".png" : ".mp4"));
    private string EditJournalPath(string id) => Path.Combine(EditDirectory, id + ".json");

    internal async Task<CaptureLibraryItem> SaveEditedCopyAsync(CaptureLibraryMutationReference reference, CaptureEditRecipe recipe,
        BrokerWidgetIdentity actor, Func<bool> isAuthorized, CaptureEditCodec codec, IProgress<double>? progress, CancellationToken token)
    {
        using var session = await OpenEditSessionAsync(reference, actor, isAuthorized, token).ConfigureAwait(false);
        return await session.SaveAsync(recipe, codec, progress, token).ConfigureAwait(false);
    }
    internal async Task<CaptureLibraryItem> SaveSessionAsync(CaptureLibraryEditSession session, CaptureEditRecipe recipe,
        CaptureEditCodec codec, IProgress<double>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(recipe); ArgumentNullException.ThrowIfNull(codec);
        var actor = session.Actor; var reference = session.Reference; var original = session.Item; var isAuthorized = session.Authorized;
        if (inspect is null) throw Error("capture_library_codec_unavailable");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, session.LifetimeToken, editStop.Token);
        lifetime.CancelAfter(CaptureEditLimits.Timeout);
        var source = session.Source; FileStream? jobLock = null; Task? monitor = null;
        var id = session.ResultId; string? temporary = null; Saved? result = null;
        var published = false; var associations = new List<SavedAssociation>();
        string? aborted = null;
        try
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                using var ownership = Own();
                PlainPath(EditDirectory); Directory.CreateDirectory(EditDirectory);
                jobLock = AcquireEditLock(); RecoverEditsOwned(); PruneEditReceipts();
                DemandEditAuthority();
                if (reference is not null) { RefreshEntry(reference.Id, forceMetadata: true); DemandRevision(Read(reference.Id)); }
                if (!recipe.IsValid(original.Media)) throw Error("capture_library_edit_invalid_recipe");
                using (var count = Command("SELECT count(*) FROM capture_associations WHERE capture_id=$id", ("$id", reference?.Id ?? "")))
                    if ((long)count.ExecuteScalar()! > CaptureEditLimits.MaximumAssociations) throw Error("capture_library_edit_context_limit");
                DemandFreeSpace(EstimatedBytes(original.Media, recipe));
                temporary = EditTemporary(id, original.Media.ContentType);
                var staging = Path.GetDirectoryName(temporary)!; PlainPath(staging);
                if (!Directory.Exists(staging)) { Directory.CreateDirectory(staging); File.SetAttributes(staging, File.GetAttributes(staging) | FileAttributes.Hidden); }
                File.WriteAllText(EditJournalPath(id), JsonSerializer.Serialize(new EditJournal(id, original.Media.ContentType, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
            }
            finally { gate.Release(); }
            monitor = MonitorAsync();
            var maximum = recipe.Image is not null ? CaptureLimits.MaximumAttachmentBytes : CaptureEditLimits.MaximumVideoBytes;
            PlainPath(temporary);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                using var bounded = new CaptureEditOutput(output, maximum);
                await codec(new(source.Media, source.Stream.Name, source.Stream), recipe, bounded, progress, lifetime.Token).ConfigureAwait(false);
                await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
            }
            DemandEditAuthority();
            var media = await inspect(temporary, original.Media with { Id = id, Origin = CaptureAttachmentOrigin.SavedLibrary, Properties = null }, lifetime.Token).ConfigureAwait(false);
            if (!media.IsWellFormed() || media.Id != id || media.ContentType != original.Media.ContentType || media.ByteLength > maximum ||
                media.Properties is not null || media.Origin != CaptureAttachmentOrigin.SavedLibrary) throw Error("capture_library_invalid_media");
            if (recipe.Image is { } image && (media.Width != image.Width || media.Height != image.Height) ||
                recipe.Video is { } trim && (media.Width != original.Media.Width || media.Height != original.Media.Height ||
                    Math.Abs(media.DurationSeconds - (trim.EndTicks - trim.StartTicks) / (double)TimeSpan.TicksPerSecond) > .25)) throw Error("capture_library_invalid_media");
            string hash;
            await using (var encoded = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            {
                if (encoded.Length != media.ByteLength) throw Error("capture_library_invalid_media");
                hash = Convert.ToHexString(await SHA256.HashDataAsync(encoded, lifetime.Token).ConfigureAwait(false));
            }
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                using var ownership = Own();
                DemandEditAuthority(); if (reference is not null) { RefreshEntry(reference.Id, forceMetadata: true); DemandRevision(Read(reference.Id)); } DemandFreeSpace(0);
                var journal = ReadEditJournal(EditJournalPath(id));
                var item = new CaptureLibraryItem(id, media with { ExpiresAtUnixMilliseconds = long.MaxValue }, journal.SavedAt)
                {
                    Origin = OriginFor(actor), CapturedAtUnixMilliseconds = original.CapturedAtUnixMilliseconds,
                    Title = original.Title, IsFavorite = false,
                };
                var stamp = File.GetLastWriteTimeUtc(temporary).Ticks;
                item = item with { Revision = stamp };
                result = new(2, item, hash, PublicName(item), stamp)
                { Derivation = new(original.Id, reference?.ContentRevision ?? 0, reference?.MetadataRevision ?? 0, recipe, reference is null, SourceFingerprint(session)) };
                using (var query = Command("SELECT key_hash FROM capture_associations WHERE capture_id=$id LIMIT $limit", ("$id", reference?.Id ?? ""), ("$limit", CaptureEditLimits.MaximumAssociations + 1)))
                using (var reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (associations.Count == CaptureEditLimits.MaximumAssociations) throw Error("capture_library_edit_context_limit");
                        var previous = ReadAssociation(AssociationPath(reader.GetString(0)), reader.GetString(0));
                        associations.Add(previous with { KeyHash = HashText("edit\0" + id + "\0" + previous.KeyHash),
                            Association = previous.Association with { Id = Guid.NewGuid().ToString("N"), CaptureId = id, RecordedAtUnixMilliseconds = journal.SavedAt } });
                    }
                }
                foreach (var association in associations) File.WriteAllText(AssociationPath(association.KeyHash) + ".pending", JsonSerializer.Serialize(association));
                File.WriteAllText(MetadataPath(id) + ".pending", JsonSerializer.Serialize(result));
                File.WriteAllText(EditReceiptPath(id) + ".pending", JsonSerializer.Serialize(new EditReceipt(1, id, actor, SourceFingerprint(session), RecipeFingerprint(recipe), journal.SavedAt)));
                DemandEditAuthority();
                File.Move(temporary, MediaPath(result), overwrite: false);
                File.Move(MetadataPath(id) + ".pending", MetadataPath(id), overwrite: false);
                published = true;
                File.Move(EditReceiptPath(id) + ".pending", EditReceiptPath(id), overwrite: false);
                IndexPut(result, media.ByteLength);
                foreach (var association in associations)
                { File.Move(AssociationPath(association.KeyHash) + ".pending", AssociationPath(association.KeyHash), overwrite: false); IndexAssociation(association); }
                DeleteOwned(EditJournalPath(id));
                return ItemView(result);
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !editStop.IsCancellationRequested)
        {
            if (!isAuthorized()) throw Error("permission_denied");
            if (!source.IsCurrent) throw Error("capture_library_edit_source_retired");
            if (session.LifetimeToken.IsCancellationRequested) throw;
            throw Error(Volatile.Read(ref aborted) ?? "capture_library_edit_timeout");
        }
        finally
        {
            lifetime.Cancel();
            if (monitor is not null) try { await monitor.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (!published)
            {
                if (temporary is not null) DeleteOwned(temporary);
                if (result is not null) DeleteOwned(MediaPath(result));
                DeleteOwned(MetadataPath(id) + ".pending");
                DeleteOwned(EditReceiptPath(id) + ".pending");
                foreach (var association in associations) DeleteOwned(AssociationPath(association.KeyHash) + ".pending");
                DeleteOwned(EditJournalPath(id));
            }
            jobLock?.Dispose();
        }

        void DemandRevision(Saved saved)
        { if (reference is not null && (saved.Item.Revision != reference.ContentRevision || saved.Item.MetadataRevision != reference.MetadataRevision)) throw Error("capture_library_metadata_conflict"); }
        void DemandEditAuthority()
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (!isAuthorized()) throw Error("permission_denied");
            if (source is not null && !source.IsCurrent) throw Error("capture_library_edit_source_retired");
        }
        async Task MonitorAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(100, lifetime.Token).ConfigureAwait(false);
                    var denied = !isAuthorized();
                    if (denied || source?.IsCurrent != true)
                    { Volatile.Write(ref aborted, denied ? "permission_denied" : "capture_library_edit_source_retired"); lifetime.Cancel(); return; }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            { Volatile.Write(ref aborted, "capture_library_edit_source_retired"); lifetime.Cancel(); }
        }
    }

    private FileStream AcquireEditLock()
    {
        var path = Path.Combine(root, ".edit.lock"); PlainPath(path);
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw Error("capture_library_edit_busy"); }
    }
    internal async Task RecoverEditsAsync(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var ownership = Own(); using var active = AcquireEditLock(); RecoverEditsOwned(); }
        finally { gate.Release(); }
    }
    private EditJournal ReadEditJournal(string path)
    {
        PlainPath(path);
        if (new FileInfo(path).Length is <= 0 or > 1024) throw Error("capture_library_edit_invalid_journal");
        var journal = JsonSerializer.Deserialize<EditJournal>(File.ReadAllText(path));
        if (journal is null || !CaptureLimits.ValidToken(journal.Id) || journal.ContentType is not ("image/png" or "video/mp4") ||
            !CaptureContextLimits.Timestamp(journal.SavedAt) || Path.GetFileName(path) != journal.Id + ".json") throw Error("capture_library_edit_invalid_journal");
        return journal;
    }
    private void RecoverEditsOwned()
    {
        if (!Directory.Exists(EditDirectory)) return; PlainPath(EditDirectory);
        foreach (var path in Directory.EnumerateFiles(EditDirectory, "*.json").Where(path => CaptureLimits.ValidToken(Path.GetFileNameWithoutExtension(path))).Take(1024))
        {
            try
            {
                var outputLock=EditOutputLockPath(Path.GetFileNameWithoutExtension(path));PlainPath(outputLock);
                if(File.Exists(outputLock))
                {
                    // Reviewable recording drafts retain this job lock instead
                    // of blocking the whole editor/replay publication lane.
                    try{using var abandoned=new FileStream(outputLock,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}
                    catch(IOException){continue;}
                    DeleteOwned(outputLock);
                }
                var journal = ReadEditJournal(path); if (!DeleteOwned(EditTemporary(journal.Id, journal.ContentType))) continue;
                if (!File.Exists(MetadataPath(journal.Id)))
                {
                    var name = $"WidgetRail-{DateTimeOffset.FromUnixTimeMilliseconds(journal.SavedAt):yyyyMMdd-HHmmss}-{journal.Id}" + (journal.ContentType == "image/png" ? ".png" : ".mp4");
                    if (!DeleteOwned(Path.Combine(captureDirectory, name)) || !DeleteOwned(MetadataPath(journal.Id) + ".pending") || !DeleteOwned(EditReceiptPath(journal.Id) + ".pending")) continue;
                }
                else
                {
                    var saved = ReadMetadata(MetadataPath(journal.Id));
                    if (journal.Standalone) { RefreshEntry(journal.Id, forceMetadata: true); ReconcileAssociations(); }
                    else if (saved.Item.Origin is { } actor) _ = RecoverEditResultOwned(journal.Id, new(actor.PackageId, actor.PublisherId, actor.InstanceId));
                }
                DeleteOwned(path);
            }
            catch (Exception error) when (error is IOException or JsonException or BrokerException) { /* Corrupt journals cannot authorize arbitrary file removal. */ }
        }
        // A retired encoder can finish after its journal was reclaimed. Only
        // generated stage names without any journal are orphan candidates; live
        // jobs publish their journal while holding this same edit lock.
        var staging=Path.Combine(captureDirectory,".widgetrail-edits");PlainPath(staging);
        if(Directory.Exists(staging))
            foreach(var path in Directory.EnumerateFiles(staging).Take(1024))
                if(CaptureLimits.ValidToken(Path.GetFileNameWithoutExtension(path)) && Path.GetExtension(path) is ".png" or ".mp4" &&
                    !File.Exists(EditJournalPath(Path.GetFileNameWithoutExtension(path))))DeleteOwned(path);
        foreach(var path in Directory.EnumerateFiles(EditDirectory,"*.lock").Take(1024))
        {
            if(!CaptureLimits.ValidToken(Path.GetFileNameWithoutExtension(path)) || File.Exists(EditJournalPath(Path.GetFileNameWithoutExtension(path))))continue;
            PlainPath(path);
            try{using(var abandoned=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}DeleteOwned(path);}
            catch(IOException){}
        }
    }
    private static long EstimatedBytes(CaptureAttachment source, CaptureEditRecipe recipe) => recipe.Image is { } image
        ? Math.Min(CaptureLimits.MaximumAttachmentBytes, (long)image.Width * image.Height * 4 + 65536)
        : Math.Min(CaptureEditLimits.MaximumVideoBytes, Math.Max(16 * 1024 * 1024, (long)(source.ByteLength * ((recipe.Video!.EndTicks - recipe.Video.StartTicks) / (source.DurationSeconds * TimeSpan.TicksPerSecond)) * 2)));
    private void DisposeStoreResources()
    {
        if (Interlocked.Exchange(ref resourcesDisposed, 1) != 0) return;
        StopSources(); lock (changeGate) changeTimer?.Dispose(); metadataWatcher?.Dispose(); mediaWatcher?.Dispose(); database?.Dispose(); gate.Dispose(); editStop.Dispose();
    }
    private void DisposeWithEdits()
    {
        lock (editLifetimeGate)
        {
            if (disposed != 0) return; Volatile.Write(ref disposed, 1); cancellingEditors = true;
        }
        try { editStop.Cancel(); }
        finally { lock (editLifetimeGate) { cancellingEditors = false; if (activeEdits == 0) DisposeStoreResources(); } }
    }
}
