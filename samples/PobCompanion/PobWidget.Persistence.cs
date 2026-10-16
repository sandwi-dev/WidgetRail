using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private PobWorkState CurrentWork(string committedXml, PobPendingChange? pending = null, string? draft = null) =>
        new(committedXml, undo.ToArray(), redo.ToArray(), pending, draft);

    private void SaveWork(PobResult committed, PobWorkState work, string? targetId = null, bool refreshXml = true)
    {
        targetId ??= documentId;
        if (targetId is null) throw new InvalidOperationException("No working build is selected.");
        EnsureBuildName(committed, targetId);
        // This is the commit point. No visible mutation is published before it.
        PobWorkStore.Save(dataDirectory, targetId, game, work);
        // Keep the existing XML available to exports/tools. Recovery uses the
        // atomic work record even if the process exits before this mirror write.
        try
        {
            if (refreshXml) BuildDocument.SaveAtomic(Path.Combine(dataDirectory, targetId + ".xml"), committed.Xml);
            BuildDocument.SaveAtomic(Path.Combine(dataDirectory, "selected.txt"), targetId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            lock (gate) ShowFeedback("Work saved", "The build XML or last-opened selection could not be refreshed. Your work is retained in Saved builds.", ToastTone.Warning);
        }
    }

    private async Task OpenSavedWorkAsync(string id, CancellationToken token)
    {
        var work = PobWorkStore.Read(dataDirectory, id, game);
        var xml = work?.CommittedXml ?? await BuildDocument.ReadAsync(Path.Combine(dataDirectory, id + ".xml"), game, token);
        var committed = await engine.ExecuteAsync(new { op = "load", xml, name = "Saved build" }, token);
        BuildDocument.VerifySkillPreservation(xml, committed.Xml);
        var shown = committed;
        if (work?.Pending is {} pending)
        {
            shown = await engine.ExecuteAsync(new { op = "load", xml = pending.Xml, name = "Saved comparison" }, token);
            BuildDocument.VerifySkillPreservation(pending.Xml, shown.Xml);
            if (pending.Kind == "node" && !A(shown.State, "nodes").Any(n => I(n, "id") == pending.NodeId) ||
                pending.Kind == "item" && (string.IsNullOrWhiteSpace(pending.Slot) || !A(shown.State, "slots").Any(s => S(s, "name") == pending.Slot)))
                throw new IOException("The saved comparison cannot be displayed by this engine. Your saved work has been retained.");
        }
        token.ThrowIfCancellationRequested();
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory, "selected.txt"), id);
        EnsureBuildName(committed, id);
        lock (gate)
        {
            documentId = id; result = shown; pendingBefore = work?.Pending is null ? null : committed;
            undo.Clear(); redo.Clear();
            foreach (var entry in (work?.Undo ?? []).Reverse()) undo.Push(entry);
            foreach (var entry in (work?.Redo ?? []).Reverse()) redo.Push(entry);
            pastedItem = work?.ItemDraft; previewSlot = work?.Pending?.Slot;
            selections.Clear(); detailId = 0; section = "Calcs"; detailKind = ""; filter = "";
            passiveReturnKey = null; nodeReturnToMap = false; historyFocusRequested = false; compatibilityNotice = "";
            // Restore only the context needed to finish the user's pending work.
            if (work?.Pending is { Kind: "node" } node) { section = "Tree"; detailKind = "node"; detailId = node.NodeId; }
            else if (work?.Pending is { Kind: "item" }) { section = "Items"; detailKind = "choose-slot"; }
            else if (work?.Pending is not null) detailKind = "menu";
            else if (pastedItem is not null) { section = "Items"; detailKind = "choose-slot"; }
            focusRequestSequence++;
        }
        if (section == "Tree" && artworkDirectory is not null && mapSession is null)
        {
            try { await OpenMapAsync(token); }
            catch (WidgetCapabilityException) { /* The native comparison remains usable without the map. */ }
        }
        await RefreshCommittedMapAsync(token);
    }

    private void SetItemDraft(string? raw) => Work(_ =>
    {
        var text = raw ?? readClipboard();
        if (text.Length > 32768 || string.IsNullOrWhiteSpace(text)) throw new IOException("Copy an item description first (up to 32,768 characters).");
        SaveWork(result!, CurrentWork(result!.Xml, draft: text), refreshXml: false);
        lock (gate) { pastedItem = text; detailKind = "choose-slot"; section = "Items"; }
        return Task.CompletedTask;
    }, "Reading item…");

    private void DiscardItemDraft() => Work(_ =>
    {
        SaveWork(result!, CurrentWork(result!.Xml), refreshXml: false);
        lock (gate) { pastedItem = null; previewSlot = null; detailKind = ""; }
        return Task.CompletedTask;
    }, "Discarding item draft…");
}
