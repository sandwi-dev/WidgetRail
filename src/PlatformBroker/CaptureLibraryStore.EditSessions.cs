using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    private sealed record EditReceipt(int Schema, string ResultId, BrokerWidgetIdentity Actor, string SourceFingerprint, string RecipeFingerprint, long SavedAt);
    private bool cancellingEditors;
    private string EditReceiptPath(string id) => Path.Combine(EditDirectory, "receipt-" + id + ".json");
    internal async Task<CaptureLibraryEditSession> OpenEditSessionAsync(CaptureLibraryMutationReference reference, BrokerWidgetIdentity actor,
        Func<bool> isAuthorized, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(isAuthorized); actor.Validate(); DemandId(reference.Id);
        if (reference.ContentRevision < 0 || reference.MetadataRevision < 0) throw Error("invalid_payload");
        RetainEditor(); CaptureLibraryReadLease? source = null; CaptureLibraryEditSession? session = null;
        using var opening = CancellationTokenSource.CreateLinkedTokenSource(token, editStop.Token);
        try
        {
            await gate.WaitAsync(opening.Token).ConfigureAwait(false);
            CaptureLibraryItem item;
            try
            {
                using var ownership = Own(); if (!isAuthorized()) throw Error("permission_denied");
                RefreshEntry(reference.Id, forceMetadata: true); var saved = await RefreshEditedAsync(Read(reference.Id), opening.Token).ConfigureAwait(false);
                if (saved.Item.Revision != reference.ContentRevision || saved.Item.MetadataRevision != reference.MetadataRevision) throw Error("capture_library_metadata_conflict");
                source = await OpenPinnedLeaseAsync(saved, opening.Token).ConfigureAwait(false); item = saved.Item;
            }
            finally { gate.Release(); }
            opening.Token.ThrowIfCancellationRequested(); session = new(this, source, item, reference, actor, isAuthorized, editStop.Token, ReleaseEditor);
            if (!session.IsCurrent) throw Error("capture_library_edit_source_retired"); return session;
        }
        catch { if (session is not null) session.Dispose(); else { source?.Dispose(); ReleaseEditor(); } throw; }
    }
    /// <summary>Takes the registry lease; disposing its emptied wrapper after this call is safe.</summary>
    internal Task<CaptureLibraryEditSession> OpenEditSessionAsync(HostCaptureEditLease temporary, BrokerWidgetIdentity actor,
        Func<bool> isAuthorized, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(temporary); ArgumentNullException.ThrowIfNull(isAuthorized); actor.Validate(); token.ThrowIfCancellationRequested();
        if (temporary.Actor != actor) throw Error("capture_unavailable");
        RetainEditor(); CaptureLibraryReadLease? source = null; CaptureLibraryEditSession? session = null;
        try
        {
            source = temporary.Take();
            if (!source.IsCurrent || source.Media.Origin is not (CaptureAttachmentOrigin.WindowCapture or CaptureAttachmentOrigin.LibraryRecordingDraft) || !isAuthorized()) throw Error("capture_unavailable");
            var item = new CaptureLibraryItem(source.Media.Id, source.Media, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { CapturedAtUnixMilliseconds = temporary.CapturedAt };
            session = new(this, source, item, null, actor, isAuthorized, editStop.Token, ReleaseEditor);
            if (!session.IsCurrent) throw Error("capture_library_edit_source_retired"); return Task.FromResult(session);
        }
        catch { if (session is not null) session.Dispose(); else { source?.Dispose(); ReleaseEditor(); } throw; }
    }
    private void RetainEditor()
    {
        lock (editLifetimeGate)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            if (activeEdits >= CaptureLimits.MaximumPerWidget) throw Error("capture_library_edit_busy"); ++activeEdits;
        }
    }
    private void ReleaseEditor()
    { lock (editLifetimeGate) if (--activeEdits == 0 && disposed != 0 && !cancellingEditors) DisposeStoreResources(); }
    private static string SourceFingerprint(CaptureLibraryEditSession session) => HashText(session.Reference is { } source
        ? $"saved\n{source.Id}\n{source.ContentRevision}\n{source.MetadataRevision}"
        : "temporary\n" + JsonSerializer.Serialize(session.Media));
    private static string RecipeFingerprint(CaptureEditRecipe recipe) => HashText(JsonSerializer.Serialize(recipe));

    internal async Task<CaptureLibraryItem?> RecoverEditResultAsync(string resultId, BrokerWidgetIdentity actor, CancellationToken token)
    {
        DemandId(resultId); actor.Validate();
        var receiptPath = EditReceiptPath(resultId); PlainPath(receiptPath); PlainPath(receiptPath + ".pending");
        if (!File.Exists(receiptPath) && !File.Exists(receiptPath + ".pending")) return null;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var ownership = Own(); return RecoverEditResultOwned(resultId, actor); }
        finally { gate.Release(); }
    }
    private CaptureLibraryItem? RecoverEditResultOwned(string id, BrokerWidgetIdentity actor)
    {
        var final = EditReceiptPath(id); var pending = final + ".pending"; PlainPath(final); PlainPath(pending);
        var path = File.Exists(final) ? final : pending;
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length is <= 0 or > 4096) throw Error("capture_library_edit_invalid_receipt");
        var receipt = JsonSerializer.Deserialize<EditReceipt>(File.ReadAllText(path)) ?? throw Error("capture_library_edit_invalid_receipt");
        if (receipt.Schema != 1 || receipt.ResultId != id || receipt.Actor != actor || !CaptureContextLimits.Timestamp(receipt.SavedAt) ||
            !ValidAssociationHash(receipt.SourceFingerprint) || !ValidAssociationHash(receipt.RecipeFingerprint)) throw Error("capture_library_edit_invalid_receipt");
        if (!File.Exists(MetadataPath(id)))
        {
            if (File.Exists(final)) throw Error("capture_library_edit_result_removed"); return null;
        }
        var saved = ReadMetadata(MetadataPath(id));
        if (saved.Item.Origin != OriginFor(actor) || saved.Derivation is not { } derivation || derivation.SourceFingerprint != receipt.SourceFingerprint ||
            RecipeFingerprint(derivation.Recipe) != receipt.RecipeFingerprint) throw Error("capture_library_edit_invalid_receipt");
        if (!File.Exists(MediaPath(saved))) throw Error("capture_library_edit_result_removed");
        if (!File.Exists(final)) File.Move(pending, final, overwrite: false);
        RefreshEntry(id, forceMetadata: true); ReconcileAssociations(); DeleteOwned(EditJournalPath(id));
        return ItemView(saved);
    }
    internal async Task RecoverPendingEditsAsync(CancellationToken token)
    {
        if (!Directory.Exists(EditDirectory)) return;
        try { await RecoverEditsAsync(token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BrokerException) { /* Recovery cannot prevent ordinary library/host startup. */ }
    }
    private void PruneEditReceipts()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7); var retained = 0;
        foreach (var path in Directory.EnumerateFiles(EditDirectory, "receipt-*.json").Take(4097))
        {
            PlainPath(path);
            if (File.GetLastWriteTimeUtc(path) < cutoff) DeleteOwned(path); else ++retained;
        }
        if (retained >= 4096) throw Error("capture_library_edit_receipt_limit");
    }
}
