using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private sealed record SavedAssociation(int Schema, string KeyHash, string Fingerprint, CaptureLibraryAssociation Association)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Detached { get; init; }
    }
    private static CaptureContextOrigin OriginFor(BrokerWidgetIdentity identity)
    {
        var origin = new CaptureContextOrigin(identity.PackageId, identity.PublisherId, identity.InstanceId);
        if (!origin.IsWellFormed()) throw Error("invalid_identity");
        return origin;
    }
    private static CaptureSaveContext FreezeContext(CaptureSaveContext? context)
    {
        if (context?.IsWellFormed() != true) throw Error("invalid_payload");
        var frozen = context with { References = context.References.ToArray() };
        if (!frozen.IsWellFormed()) throw Error("invalid_payload");
        return frozen;
    }
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string ContextKey(CaptureContextOrigin origin, CaptureSaveContext context) =>
        HashText(origin.PublisherId + "\0" + origin.PackageId + "\0" + origin.InstanceId + "\0" + context.IdempotencyKey);
    private static string ContextFingerprint(CaptureSaveContext context) => HashText(JsonSerializer.Serialize(new
    {
        context.EventAtUnixMilliseconds,
        References = context.References.OrderBy(reference => reference.Role).ThenBy(reference => reference.Kind, StringComparer.Ordinal)
            .ThenBy(reference => reference.Id, StringComparer.Ordinal).Select(reference => new { reference.Role, reference.Kind, reference.Id }).ToArray(),
    }));
    private string AssociationPath(string hash) => Path.Combine(root, "association-" + hash + ".json");
    private static bool ValidAssociationHash(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);

    /// <summary>Checks an authenticated idempotency receipt before requesting another capture. Deleted/recycled originals remain retired.</summary>
    public async Task<CaptureLibraryContextSaveResult?> FindSavedContextAsync(BrokerWidgetIdentity identity, CaptureSaveContext context, CancellationToken token)
    {
        identity.Validate(); context = FreezeContext(context); var origin = OriginFor(identity);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var ownership = Own(); return ExistingContext(origin, context); }
        finally { gate.Release(); }
    }

    public async Task<CaptureLibraryContextSaveResult> SaveWithContextAsync(HostCaptureAttachment source, BrokerWidgetIdentity identity,
        CaptureSaveContext context, CancellationToken token)
    {
        identity.Validate(); context = FreezeContext(context); var origin = OriginFor(identity);
        if (!source.Attachment.IsWellFormed() || !CaptureContextLimits.Timestamp(source.CapturedAtUnixMilliseconds)) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            if (ExistingContext(origin, context) is { } previous) return previous;
            var key = ContextKey(origin, context); var final = AssociationPath(key); var pending = final + ".pending";
            SavedAssociation? document = null;
            var saved = await SaveOwnedAsync(source, origin, token, capture =>
            {
                document = new(1, key, ContextFingerprint(context), new(Guid.NewGuid().ToString("N"), capture.Item.Id, origin,
                    context.References, context.EventAtUnixMilliseconds, source.CapturedAtUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
                if (bytes.Length > CaptureContextLimits.MaximumAssociationFileBytes) throw Error("invalid_payload");
                PlainPath(pending); PlainPath(final); File.WriteAllBytes(pending, bytes);
            }).ConfigureAwait(false);
            // The pending receipt precedes image publication. Recovery commits it
            // only if the capture sidecar was published; it never deletes originals.
            File.Move(pending, final, overwrite: false);
            IndexAssociation(document!);
            return new(CaptureContextSaveOutcome.Saved, ItemView(saved), document!.Association);
        }
        finally { gate.Release(); }
    }

    /// <summary>Trusted source-registration adapter. Callers must first authorize the capture's linked source; no widget operation exposes arbitrary attachment.</summary>
    public async Task<CaptureLibraryContextSaveResult> AttachContextAsync(string captureId, BrokerWidgetIdentity identity,
        CaptureSaveContext context, CancellationToken token)
    {
        identity.Validate(); DemandId(captureId); context = FreezeContext(context); var origin = OriginFor(identity);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            return AttachContextOwned(captureId, origin, context, token);
        }
        finally { gate.Release(); }
    }
    // Shared by trusted source indexing while it already owns the store gate and profile lease.
    internal async Task<CaptureLibraryContextSaveResult> AttachSavedContextAsync(CaptureLibraryAttachContextRequest request,
        BrokerWidgetIdentity identity, CancellationToken token)
    {
        identity.Validate();
        DemandId(request.CaptureId);
        if (request.ContentRevision < 0) throw Error("invalid_payload");
        var context = FreezeContext(request.Context);
        var origin = OriginFor(identity);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            DemandVisibleCapture(request.CaptureId, request.ContentRevision);
            if (ExistingContext(origin, context) is { } prior)
                return prior.Association.CaptureId == request.CaptureId ? prior : throw Error("capture_library_context_conflict");
            DemandAssociationCapacity(request.CaptureId, origin);
            return AttachContextOwned(request.CaptureId, origin, context, token);
        }
        finally { gate.Release(); }
    }

    private void DemandAssociationCapacity(string captureId, CaptureContextOrigin origin)
    {
        using var count = Command("SELECT COUNT(*) FROM capture_associations WHERE capture_id=$id AND origin_package=$package AND origin_publisher=$publisher",
            ("$id", captureId), ("$package", origin.PackageId), ("$publisher", origin.PublisherId));
        if ((long)count.ExecuteScalar()! >= 64) throw Error("capture_library_context_limit");
    }

    internal async Task<CaptureLibrarySubjectResult> EnsureSubjectAsync(CaptureLibraryEnsureSubjectRequest request,
        BrokerWidgetIdentity identity, CancellationToken token)
    {
        identity.Validate();
        if (!request.IsWellFormed()) throw Error("invalid_payload");
        var origin = OriginFor(identity);
        var context = new CaptureSaveContext(request.IdempotencyKey, [request.Subject]);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            DemandHiddenState();
            DemandVisibleCapture(request.CaptureId, request.ContentRevision);
            var prior = ExistingContext(origin, context);
            if (prior is not null && prior.Association.CaptureId != request.CaptureId) throw Error("capture_library_context_conflict");
            if (prior?.Outcome == CaptureContextSaveOutcome.AlreadySaved) return new(prior.Outcome);

            // The related-item intent addresses package, kind and id. Comparing this public
            // destination does not grant authority over the matching receipt or expose its other references.
            using var active = Command("SELECT 1 FROM active_capture_associations a JOIN capture_association_refs r ON r.key_hash=a.key_hash " +
                "WHERE a.capture_id=$capture AND a.origin_package=$package AND r.kind=$kind AND r.reference_id=$id LIMIT 1",
                ("$capture", request.CaptureId), ("$package", origin.PackageId), ("$kind", request.Subject.Kind), ("$id", request.Subject.Id));
            if (active.ExecuteScalar() is not null) return new(CaptureContextSaveOutcome.AlreadySaved);
            if (hasDestinationMasks && !request.RestoreDetached)
            {
                using var dismissed = Command("SELECT 1 FROM userstate.destination_masks WHERE capture_id=$capture AND package=$package AND kind=$kind AND destination=$id LIMIT 1",
                    ("$capture", request.CaptureId), ("$package", origin.PackageId), ("$kind", request.Subject.Kind), ("$id", request.Subject.Id));
                if (dismissed.ExecuteScalar() is not null) return new(CaptureContextSaveOutcome.Detached);
            }
            if (prior is not null)
            {
                if (prior.Outcome != CaptureContextSaveOutcome.Detached || !request.RestoreDetached) return new(prior.Outcome);
                var hash = ContextKey(origin, context);
                var saved = ReadAssociation(AssociationPath(hash), hash);
                if (!saved.Detached && EffectiveAssociation(saved).Detached)
                {
                    // Restore just this authenticated subject. Another installation's or a compound
                    // receipt's references can only be restored by Undo of the reviewed change.
                    token.ThrowIfCancellationRequested();
                    Execute("SAVEPOINT restore_subject");
                    try
                    {
                        Execute("DELETE FROM userstate.destination_masks WHERE receipt=$hash AND association_id=$association AND kind=$kind AND destination=$id",
                            ("$hash", hash), ("$association", saved.Association.Id), ("$kind", request.Subject.Kind), ("$id", request.Subject.Id));
                        IndexAssociation(saved);
                        Execute("RELEASE restore_subject");
                    }
                    catch { Execute("ROLLBACK TO restore_subject"); Execute("RELEASE restore_subject"); throw; }
                    return new(CaptureContextSaveOutcome.Saved);
                }
                IndexAssociation(ChangeDetached(saved, false, token));
                return new(CaptureContextSaveOutcome.Saved);
            }
            DemandAssociationCapacity(request.CaptureId, origin);
            return new(AttachContextOwned(request.CaptureId, origin, context, token).Outcome);
        }
        finally { gate.Release(); }
    }

    private Saved DemandVisibleCapture(string id, long revision)
    {
        RefreshEntry(id, forceMetadata: true);
        var (where, parameters) = QueryWhere(new());
        var capture = IndexRows(where + " AND id=$capture AND available=1", [.. parameters, ("$capture", id)])
            .SingleOrDefault() ?? throw Error("capture_library_not_found");
        if (capture.Item.Revision != revision) throw Error("capture_library_metadata_conflict");
        return capture;
    }

    internal async Task<CaptureLibraryOwnedLinks> FindOwnedLinksAsync(CaptureLibraryOwnedLinksRequest request,
        BrokerWidgetIdentity identity, CancellationToken token)
    {
        identity.Validate();
        if (!request.IsWellFormed()) throw Error("invalid_payload");
        var origin = OriginFor(identity);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var (where, visibility) = QueryWhere(new() { AvailableOnly = true });
            var parameters = visibility.ToList();
            var ids = new List<string>(request.CaptureIds.Count);
            foreach (var id in request.CaptureIds)
            {
                var parameter = "$capture" + ids.Count;
                ids.Add(parameter);
                parameters.Add((parameter, id));
            }
            parameters.Add(("$package", origin.PackageId));
            parameters.Add(("$kind", request.Reference.Kind));
            parameters.Add(("$reference", request.Reference.Id));
            var roleFilter = "";
            if (request.Reference.Role is { } role)
            {
                roleFilter = " AND r.role=$role";
                parameters.Add(("$role", (int)role));
            }
            // Consider every match from this package before offering a removable link.
            // A compound receipt, another instance or a second association can independently keep a capture in the gallery.
            using var command = Command("SELECT a.data FROM active_capture_associations a JOIN capture_association_refs r ON r.key_hash=a.key_hash " +
                "WHERE a.capture_id IN (" + string.Join(",", ids) + ") AND a.origin_package=$package AND r.kind=$kind AND r.reference_id=$reference" + roleFilter +
                " AND EXISTS (SELECT 1 FROM captures " + where + " AND captures.id=a.capture_id) GROUP BY a.capture_id HAVING count(DISTINCT a.key_hash)=1",
                parameters.ToArray());
            using var reader = command.ExecuteReader();
            var links = new List<CaptureLibraryAssociation>();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                var link = JsonSerializer.Deserialize<SavedAssociation>(reader.GetString(0))!.Association;
                if (link.Origin == origin && link is { References.Count: 1, EventAtUnixMilliseconds: null }) links.Add(link);
            }
            return new(links);
        }
        finally { gate.Release(); }
    }

    internal async Task<CaptureLibraryContextSaveResult> SetContextLinkedAsync(CaptureLibrarySetContextLinkedRequest request,
        BrokerWidgetIdentity identity, CancellationToken token)
    {
        identity.Validate();
        if (!request.IsWellFormed()) throw Error("invalid_payload");
        var origin = OriginFor(identity);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var capture = DemandVisibleCapture(request.CaptureId, request.ContentRevision);
            using var key = Command("SELECT key_hash FROM capture_associations WHERE association_id=$association AND capture_id=$capture " +
                "AND origin_package=$package AND origin_publisher=$publisher AND origin_instance=$instance",
                ("$association", request.AssociationId), ("$capture", request.CaptureId), ("$package", origin.PackageId),
                ("$publisher", origin.PublisherId), ("$instance", origin.InstanceId));
            if (key.ExecuteScalar() is not string hash || !ValidAssociationHash(hash)) throw Error("capture_library_context_conflict");
            RecoverAssociation(hash);
            var path = AssociationPath(hash);
            PlainPath(path);
            if (!File.Exists(path)) throw Error("capture_library_context_conflict");
            var saved = ReadAssociation(path, hash);
            var link = saved.Association;
            if (EffectiveAssociation(saved).Detached != saved.Detached || link.Origin != origin || link.Id != request.AssociationId || link.CaptureId != request.CaptureId ||
                link.References.Count != 1 || link.EventAtUnixMilliseconds is not null)
                throw Error("capture_library_context_conflict");

            var change = saved.Detached == request.Linked;
            if (link.LinkRevision != request.ExpectedLinkRevision &&
                (change || link.LinkRevision - 1 != request.ExpectedLinkRevision))
                throw Error("capture_library_context_conflict");
            if (change)
                saved = ChangeDetached(saved, !request.Linked, token);
            // Reconcile even on a retry after a committed file replacement and interrupted index write.
            IndexAssociation(saved);
            var outcome = saved.Detached ? CaptureContextSaveOutcome.Detached :
                change ? CaptureContextSaveOutcome.Saved : CaptureContextSaveOutcome.AlreadySaved;
            return new(outcome, ItemView(capture), saved.Association);
        }
        finally { gate.Release(); }
    }

    private SavedAssociation ChangeDetached(SavedAssociation saved, bool detached, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var changed = saved with { Detached = detached, Association = saved.Association with { LinkRevision = checked(saved.Association.LinkRevision + 1) } };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(changed);
        if (bytes.Length > CaptureContextLimits.MaximumAssociationFileBytes) throw Error("invalid_payload");
        var path = AssociationPath(changed.KeyHash);
        var pending = path + ".pending";
        PlainPath(path);
        PlainPath(pending);
        File.WriteAllBytes(pending, bytes);
        File.Move(pending, path, overwrite: true);
        return changed;
    }

    private CaptureLibraryContextSaveResult AttachContextOwned(string captureId, CaptureContextOrigin origin, CaptureSaveContext context, CancellationToken token)
    {
        if (ExistingContext(origin, context) is { } prior) return prior;
        var capture = Read(captureId);
        if (!capture.Item.Available || !File.Exists(MediaPath(capture))) throw Error("capture_library_not_found");
        token.ThrowIfCancellationRequested();
        var hash = ContextKey(origin, context); var final = AssociationPath(hash); var pending = final + ".pending";
        var document = new SavedAssociation(1, hash, ContextFingerprint(context), new(Guid.NewGuid().ToString("N"), captureId, origin,
            context.References, context.EventAtUnixMilliseconds, capture.Item.CapturedAtUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        if (bytes.Length > CaptureContextLimits.MaximumAssociationFileBytes) throw Error("invalid_payload");
        PlainPath(final); PlainPath(pending); File.WriteAllBytes(pending, bytes); File.Move(pending, final, overwrite: false);
        IndexAssociation(document);
        return new(CaptureContextSaveOutcome.Saved, ItemView(capture), document.Association);
    }

    public async Task<CaptureLibraryContextsPage> GetContextsAsync(CaptureLibraryContextsRequest request, CancellationToken token)
    {
        DemandId(request.Id);
        if (request.Offset < 0 || request.Count is < 1 or > CaptureContextLimits.MaximumPageItems ||
            request.Revision is not null && !CaptureLibraryLimits.ValidRevision(request.Revision)) throw Error("invalid_payload");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own(); var revision = DemandRevision(request.Revision);
            using var count = Command("SELECT count(*) FROM active_capture_associations WHERE capture_id=$id", ("$id", request.Id));
            var total = checked((int)(long)count.ExecuteScalar()!);
            using var command = Command("SELECT data FROM active_capture_associations WHERE capture_id=$id ORDER BY recorded_at DESC,association_id LIMIT $limit OFFSET $offset",
                ("$id", request.Id), ("$limit", request.Count), ("$offset", request.Offset));
            using var reader = command.ExecuteReader(); var items = new List<CaptureLibraryAssociation>();
            while (reader.Read()) { token.ThrowIfCancellationRequested(); items.Add(JsonSerializer.Deserialize<SavedAssociation>(reader.GetString(0))!.Association); }
            return new(items.ToArray(), request.Offset, total, revision);
        }
        finally { gate.Release(); }
    }

    internal async Task<CaptureLibraryDestinationsPage> GetDestinationsAsync(CaptureLibraryContextsRequest request, CancellationToken token)
    {
        DemandId(request.Id);
        if (request.Offset < 0 || request.Count is < 1 or > CaptureContextLimits.MaximumPageItems ||
            request.Revision is not null && !CaptureLibraryLimits.ValidRevision(request.Revision)) throw Error("invalid_payload");
        const string matches = """
            FROM active_capture_associations a JOIN capture_association_refs r ON r.key_hash=a.key_hash
            WHERE a.capture_id=$capture
            """;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            var revision = DemandRevision(request.Revision);
            using var command = Command($"""
                WITH refs AS (
                    SELECT a.origin_package, r.kind, r.reference_id, r.label, a.recorded_at,
                        json_extract(a.data,'$.Association.References[' || r.position || '].HasRelatedAction') AS actionable,
                        row_number() OVER (PARTITION BY a.origin_package,r.kind,r.reference_id
                            ORDER BY r.label IS NULL,a.recorded_at DESC,a.association_id,r.position) AS label_rank
                    {matches}
                )
                SELECT origin_package,kind,reference_id,max(CASE WHEN label_rank=1 THEN label END),
                    max(coalesce(actionable,0)),count(*) OVER ()
                FROM refs GROUP BY origin_package,kind,reference_id
                ORDER BY max(recorded_at) DESC,origin_package,kind,reference_id LIMIT $count OFFSET $offset
                """, ("$capture", request.Id), ("$count", request.Count), ("$offset", request.Offset));
            var total = 0;
            var rows = new List<CaptureLibraryDestination>(request.Count);
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    total = reader.GetInt32(5);
                    rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4)));
                }
            if (rows.Count == 0 && request.Offset > 0)
            {
                using var count = Command($"SELECT count(*) FROM (SELECT 1 {matches} GROUP BY a.origin_package,r.kind,r.reference_id)", ("$capture", request.Id));
                total = checked((int)(long)count.ExecuteScalar()!);
            }
            return new(rows, request.Offset, total, revision);
        }
        finally { gate.Release(); }
    }

    private CaptureLibraryContextSaveResult? ExistingContext(CaptureContextOrigin origin, CaptureSaveContext context)
    {
        var hash = ContextKey(origin, context); var path = AssociationPath(hash); PlainPath(path);
        RecoverAssociation(hash);
        if (!File.Exists(path)) return null;
        var saved = ReadAssociation(path, hash);
        if (saved.Association.Origin != origin || saved.Fingerprint != ContextFingerprint(context)) throw Error("capture_library_context_conflict");
        saved = IndexAssociation(saved);
        var capture = IndexRows("WHERE id=$id", ("$id", saved.Association.CaptureId)).FirstOrDefault();
        if (capture is not null) { RefreshEntry(capture.Item.Id); capture = IndexRows("WHERE id=$id", ("$id", capture.Item.Id)).FirstOrDefault(); }
        var item = capture is null ? null : ItemView(capture);
        var outcome = item?.Available != true ? CaptureContextSaveOutcome.Unavailable :
            saved.Detached ? CaptureContextSaveOutcome.Detached : CaptureContextSaveOutcome.AlreadySaved;
        return new(outcome, item, saved.Association);
    }
    private CaptureLibraryItem ItemView(Saved saved)
    {
        using var count = Command("SELECT count(*) FROM active_capture_associations WHERE capture_id=$id", ("$id", saved.Item.Id));
        return saved.Item with { FileName = saved.FileName, ContextCount = checked((int)(long)count.ExecuteScalar()!) };
    }
    private SavedAssociation ReadAssociation(string path, string expectedHash)
    {
        PlainPath(path);
        if (new FileInfo(path).Length is <= 0 or > CaptureContextLimits.MaximumAssociationFileBytes) throw Error("capture_library_context_invalid");
        var document = JsonSerializer.Deserialize<SavedAssociation>(File.ReadAllText(path)) ?? throw Error("capture_library_context_invalid");
        if (document.Schema != 1 || document.KeyHash != expectedHash || !ValidAssociationHash(document.KeyHash) ||
            !ValidAssociationHash(document.Fingerprint) || document.Association?.IsWellFormed() != true) throw Error("capture_library_context_invalid");
        return document;
    }
    private void RecoverAssociation(string hash)
    {
        var path = AssociationPath(hash); var pending = path + ".pending"; PlainPath(path); PlainPath(pending);
        if (!File.Exists(pending)) return;
        if (File.Exists(path)) { DeleteOwned(pending); return; }
        var document = ReadAssociation(pending, hash);
        if (File.Exists(MetadataPath(document.Association.CaptureId))) File.Move(pending, path);
        else DeleteOwned(pending);
    }
    private void RefreshAssociation(string hash)
    {
        try
        {
            RecoverAssociation(hash); var path = AssociationPath(hash); PlainPath(path);
            if (File.Exists(path)) IndexAssociation(ReadAssociation(path, hash));
            else RemoveAssociationIndex(hash);
        }
        catch (Exception error) when (error is IOException or JsonException or BrokerException)
        { RemoveAssociationIndex(hash); } // Unreadable context never authorizes changing a media file.
    }
    private void ReconcileAssociations()
    {
        foreach (var path in Directory.EnumerateFiles(root, "association-*.json*"))
            if (AssociationHash(Path.GetFileName(path)) is { } hash) RefreshAssociation(hash);
        var after = "";
        while (true)
        {
            using var command = Command("SELECT key_hash FROM capture_associations WHERE key_hash>$after ORDER BY key_hash LIMIT 128", ("$after", after));
            using var reader = command.ExecuteReader(); var batch = new List<string>(128);
            while (reader.Read()) batch.Add(reader.GetString(0)); reader.Close();
            if (batch.Count == 0) break;
            after = batch[^1];
            foreach (var hash in batch) if (!File.Exists(AssociationPath(hash))) RemoveAssociationIndex(hash);
        }
    }
    private static string? AssociationHash(string name)
    {
        if (!name.StartsWith("association-", StringComparison.Ordinal) || !(name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".json.pending", StringComparison.Ordinal))) return null;
        var hash = name[12..].Split('.')[0]; return ValidAssociationHash(hash) ? hash : null;
    }
    private SavedAssociation IndexAssociation(SavedAssociation document)
    {
        document = EffectiveAssociation(document);
        var serialized = JsonSerializer.Serialize(document);
        using (var prior = Command("SELECT data FROM capture_associations WHERE key_hash=$hash", ("$hash", document.KeyHash)))
            if (prior.ExecuteScalar() is string existing && existing == serialized) return document;
        var association = document.Association;
        Execute("SAVEPOINT capture_association_write");
        try
        {
        Execute("INSERT INTO capture_associations(key_hash,capture_id,association_id,origin_package,origin_publisher,origin_instance,recorded_at,data) VALUES($hash,$capture,$id,$package,$publisher,$instance,$recorded,$data) " +
            "ON CONFLICT(key_hash) DO UPDATE SET capture_id=excluded.capture_id,association_id=excluded.association_id,origin_package=excluded.origin_package,origin_publisher=excluded.origin_publisher,origin_instance=excluded.origin_instance,recorded_at=excluded.recorded_at,data=excluded.data",
            ("$hash", document.KeyHash), ("$capture", association.CaptureId), ("$id", association.Id), ("$package", association.Origin.PackageId),
            ("$publisher", association.Origin.PublisherId), ("$instance", association.Origin.InstanceId), ("$recorded", association.RecordedAtUnixMilliseconds), ("$data", serialized));
        Execute("DELETE FROM capture_association_refs WHERE key_hash=$hash", ("$hash", document.KeyHash));
        for (var index = 0; index < association.References.Count; index++)
        {
            var reference = association.References[index];
            Execute("INSERT INTO capture_association_refs VALUES($hash,$position,$role,$kind,$id,$label)", ("$hash", document.KeyHash), ("$position", index),
                ("$role", (int)reference.Role), ("$kind", reference.Kind), ("$id", reference.Id), ("$label", (object?)reference.Label ?? DBNull.Value));
        }
        Execute("RELEASE capture_association_write");
        }
        catch { Execute("ROLLBACK TO capture_association_write"); Execute("RELEASE capture_association_write"); throw; }
        NotifyChanged();
        return document;
    }
    private void RemoveAssociationIndex(string hash)
    {
        Execute("DELETE FROM capture_association_refs WHERE key_hash=$hash", ("$hash", hash));
        var before = ChangeCount(); Execute("DELETE FROM capture_associations WHERE key_hash=$hash", ("$hash", hash));
        if (ChangeCount() != before) NotifyChanged();
    }
}
