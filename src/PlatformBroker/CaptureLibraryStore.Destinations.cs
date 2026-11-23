using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private bool hasDestinationMasks;

    private void CreateDestinationState() => Execute("""
        CREATE TABLE IF NOT EXISTS userstate.destination_edits(
            capture_id TEXT NOT NULL,package TEXT NOT NULL,kind TEXT NOT NULL,destination TEXT NOT NULL,
            operation TEXT NOT NULL UNIQUE,actor TEXT NOT NULL,revision INTEGER NOT NULL,undone INTEGER NOT NULL,
            PRIMARY KEY(capture_id,package,kind,destination));
        CREATE TABLE IF NOT EXISTS userstate.destination_masks(
            receipt TEXT NOT NULL,association_id TEXT NOT NULL,kind TEXT NOT NULL,destination TEXT NOT NULL,
            capture_id TEXT NOT NULL,package TEXT NOT NULL,operation TEXT NOT NULL,
            PRIMARY KEY(receipt,association_id,kind,destination));
        CREATE INDEX IF NOT EXISTS userstate.destination_mask_group ON destination_masks(capture_id,package,kind,destination);
        CREATE INDEX IF NOT EXISTS userstate.destination_mask_undo ON destination_masks(operation);
        """);

    // Only the effective projection enters the disposable index. Original context documents retain
    // their authenticated references and idempotency fingerprints even when one reference is hidden.
    private SavedAssociation EffectiveAssociation(SavedAssociation saved)
    {
        if (saved.Detached) return saved;
        if (!hiddenStateAvailable) return saved with { Detached = true };
        if (!hasDestinationMasks) return saved;
        using var query = Command("SELECT kind,destination FROM userstate.destination_masks WHERE receipt=$hash AND association_id=$id",
            ("$hash", saved.KeyHash), ("$id", saved.Association.Id));
        using var reader = query.ExecuteReader();
        if (!reader.Read()) return saved;
        var excluded = new HashSet<(string Kind, string Id)>();
        do { excluded.Add((reader.GetString(0), reader.GetString(1))); } while (reader.Read());
        var remaining = saved.Association.References.Where(r => !excluded.Contains((r.Kind, r.Id))).ToArray();
        return remaining.Length == 0 ? saved with { Detached = true } :
            saved with { Association = saved.Association with { References = remaining } };
    }

    internal async Task<bool> RequestDestinationChangeAsync(CaptureDestinationChange change, BrokerWidgetIdentity actor,
        Func<BrokerWidgetIdentity, string, string, CancellationToken, Task<bool>> review, CancellationToken token)
    {
        actor.Validate();
        if (!change.IsWellFormed()) throw Error("invalid_payload");
        string? captureLabel = null, destinationLabel = null;
        // The authenticated first-party library manages the user's whole collection.
        // Other callers may manage their own receipts directly and review foreign ones.
        var libraryManager = actor is { PackageId: "widgetrail.firstparty.capture-library", PublisherId: "widgetrail.firstparty" };
        if (!change.Undo && !libraryManager)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using var ownership = Own();
                DemandHiddenState();
                var capture = DemandVisibleCapture(change.CaptureId, change.ContentRevision);
                using var replay = Command("""
                    SELECT 1 FROM userstate.destination_edits WHERE operation=$op AND capture_id=$capture AND revision=$revision
                        AND package=$package AND kind=$kind AND destination=$destination AND actor=$actor AND undone=0
                    """, ("$op", change.OperationId), ("$capture", change.CaptureId), ("$revision", change.ContentRevision),
                    ("$package", change.PackageId), ("$kind", change.Kind), ("$destination", change.DestinationId), ("$actor", ContextOwnerKey(actor)));
                if (replay.ExecuteScalar() is not null) return true;
                DemandRevision(change.ExpectedRevision);
                using var foreign = Command("""
                    SELECT coalesce(r.label,r.kind) FROM active_capture_associations a JOIN capture_association_refs r ON r.key_hash=a.key_hash
                    WHERE a.capture_id=$capture AND a.origin_package=$package AND r.kind=$kind AND r.reference_id=$destination
                        AND (a.origin_package!=$caller OR a.origin_publisher!=$publisher OR a.origin_instance!=$instance)
                    ORDER BY a.recorded_at DESC,a.association_id,r.position LIMIT 1
                    """, ("$capture", change.CaptureId), ("$package", change.PackageId), ("$kind", change.Kind),
                    ("$destination", change.DestinationId), ("$caller", actor.PackageId), ("$publisher", actor.PublisherId), ("$instance", actor.InstanceId));
                if (foreign.ExecuteScalar() is string label)
                {
                    destinationLabel = label;
                    captureLabel = capture.Item.Title ?? capture.FileName ?? capture.Item.Id;
                }
            }
            finally { gate.Release(); }
        }
        if (destinationLabel is not null && !await review(actor, captureLabel!, destinationLabel, token).ConfigureAwait(false)) return false;
        await ApplyReviewedDestinationChangeAsync(change, actor, token).ConfigureAwait(false);
        return true;
    }

    // Both direct and reviewed edits check the current revision inside the write gate.
    internal async Task ApplyReviewedDestinationChangeAsync(CaptureDestinationChange change, BrokerWidgetIdentity actor, CancellationToken token)
    {
        actor.Validate();
        DemandId(change.CaptureId);
        if (!change.IsWellFormed()) throw Error("invalid_payload");
        var owner = ContextOwnerKey(actor);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var ownership = Own();
            DemandHiddenState();
            DemandVisibleCapture(change.CaptureId, change.ContentRevision);
            var args = new (string Name, object Value)[] { ("$capture", change.CaptureId), ("$package", change.PackageId),
                ("$kind", change.Kind), ("$destination", change.DestinationId) };
            const string scope = "capture_id=$capture AND package=$package AND kind=$kind AND destination=$destination";
            using (var previous = Command("SELECT operation,actor,revision,undone FROM userstate.destination_edits WHERE " + scope, args))
            using (var reader = previous.ExecuteReader())
            {
                if (reader.Read() && reader.GetString(0) == change.OperationId)
                {
                    if (reader.GetString(1) != owner || reader.GetInt64(2) != change.ContentRevision ||
                        !change.Undo && reader.GetBoolean(3)) throw Error("capture_library_context_conflict");
                    if (!change.Undo || reader.GetBoolean(3)) return;
                }
                else if (change.Undo) throw Error("capture_library_context_conflict");
            }
            if (!change.Undo) DemandRevision(change.ExpectedRevision);
            var hashes = new List<string>();
            using (var lookup = change.Undo
                ? Command("SELECT receipt FROM userstate.destination_masks WHERE operation=$op", ("$op", change.OperationId))
                : Command("""
                    SELECT DISTINCT a.key_hash FROM active_capture_associations a JOIN capture_association_refs r ON r.key_hash=a.key_hash
                    WHERE a.capture_id=$capture AND a.origin_package=$package AND r.kind=$kind AND r.reference_id=$destination
                    """, args))
            using (var reader = lookup.ExecuteReader())
                while (reader.Read()) hashes.Add(reader.GetString(0));
            if (!change.Undo && hashes.Count == 0) throw Error("capture_library_context_conflict");
            Execute("SAVEPOINT destination_change");
            try
            {
                if (change.Undo)
                {
                    Execute("DELETE FROM userstate.destination_masks WHERE operation=$op", ("$op", change.OperationId));
                    Execute("UPDATE userstate.destination_edits SET undone=1 WHERE operation=$op", ("$op", change.OperationId));
                }
                else
                {
                    Execute("""
                        INSERT INTO userstate.destination_edits(capture_id,package,kind,destination,operation,actor,revision,undone)
                        VALUES($capture,$package,$kind,$destination,$op,$actor,$rev,0)
                        ON CONFLICT(capture_id,package,kind,destination) DO UPDATE SET operation=excluded.operation,
                            actor=excluded.actor,revision=excluded.revision,undone=0
                        """, [.. args, ("$op", change.OperationId), ("$actor", owner), ("$rev", change.ContentRevision)]);
                    hasDestinationMasks = true;
                }
                foreach (var hash in hashes)
                {
                    token.ThrowIfCancellationRequested();
                    if (!ValidAssociationHash(hash)) throw Error("capture_library_context_invalid");
                    var saved = ReadAssociation(AssociationPath(hash), hash);
                    if (saved.Association.CaptureId != change.CaptureId || saved.Association.Origin.PackageId != change.PackageId ||
                        !saved.Association.References.Any(r => r.Kind == change.Kind && r.Id == change.DestinationId))
                        throw Error("capture_library_context_conflict");
                    if (!change.Undo)
                        Execute("""
                            INSERT INTO userstate.destination_masks(receipt,association_id,kind,destination,capture_id,package,operation)
                            VALUES($hash,$id,$kind,$destination,$capture,$package,$op)
                            """, [.. args, ("$hash", hash), ("$id", saved.Association.Id), ("$op", change.OperationId)]);
                    IndexAssociation(saved);
                }
                token.ThrowIfCancellationRequested();
                Execute("RELEASE destination_change");
            }
            catch { Execute("ROLLBACK TO destination_change"); Execute("RELEASE destination_change"); throw; }
        }
        finally { gate.Release(); }
    }

    private static string ContextOwnerKey(BrokerWidgetIdentity actor) => HashText(actor.PublisherId + "\0" + actor.PackageId + "\0" + actor.InstanceId);
}
