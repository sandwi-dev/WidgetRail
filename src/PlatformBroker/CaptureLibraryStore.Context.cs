using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private sealed record SavedAssociation(int Schema, string KeyHash, string Fingerprint, CaptureLibraryAssociation Association);
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
            using var count = Command("SELECT count(*) FROM capture_associations WHERE capture_id=$id", ("$id", request.Id));
            var total = checked((int)(long)count.ExecuteScalar()!);
            using var command = Command("SELECT data FROM capture_associations WHERE capture_id=$id ORDER BY recorded_at DESC,association_id LIMIT $limit OFFSET $offset",
                ("$id", request.Id), ("$limit", request.Count), ("$offset", request.Offset));
            using var reader = command.ExecuteReader(); var items = new List<CaptureLibraryAssociation>();
            while (reader.Read()) { token.ThrowIfCancellationRequested(); items.Add(JsonSerializer.Deserialize<SavedAssociation>(reader.GetString(0))!.Association); }
            return new(items.ToArray(), request.Offset, total, revision);
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
        IndexAssociation(saved);
        var capture = IndexRows("WHERE id=$id", ("$id", saved.Association.CaptureId)).FirstOrDefault();
        if (capture is not null) { RefreshEntry(capture.Item.Id); capture = IndexRows("WHERE id=$id", ("$id", capture.Item.Id)).FirstOrDefault(); }
        var item = capture is null ? null : ItemView(capture);
        return new(item?.Available == true ? CaptureContextSaveOutcome.AlreadySaved : CaptureContextSaveOutcome.Unavailable, item, saved.Association);
    }
    private CaptureLibraryItem ItemView(Saved saved)
    {
        using var count = Command("SELECT count(*) FROM capture_associations WHERE capture_id=$id", ("$id", saved.Item.Id));
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
    private void IndexAssociation(SavedAssociation document)
    {
        var serialized = JsonSerializer.Serialize(document);
        using (var prior = Command("SELECT data FROM capture_associations WHERE key_hash=$hash", ("$hash", document.KeyHash)))
            if (prior.ExecuteScalar() is string existing && existing == serialized) return;
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
    }
    private void RemoveAssociationIndex(string hash)
    {
        Execute("DELETE FROM capture_association_refs WHERE key_hash=$hash", ("$hash", hash));
        var before = ChangeCount(); Execute("DELETE FROM capture_associations WHERE key_hash=$hash", ("$hash", hash));
        if (ChangeCount() != before) NotifyChanged();
    }
}
