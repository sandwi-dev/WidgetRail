using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private bool recoveryReadOnly;
    private string[] itemDraftSlots=[];
    private async Task<string[]> ItemSlotsAsync(string text,string xml,CancellationToken token)
    {
        var response=await engine.ExecuteAsync(new{op="item-slots",raw=text},token,xml);
        return A(response.State,"slots").Select(s=>S(s,"name")).ToArray();
    }
    private PobWorkState CurrentWork(string committedXml, PobPendingChange? pending = null, string? draft = null) =>
        new(committedXml, undo.ToArray(), redo.ToArray(), pending, draft, draft is null ? null : tradeListing,
            draft is not null&&tradeBrowsing,tradeCandidates.ToArray());

    private void SaveWork(PobResult committed, PobWorkState work, string? targetId = null, bool refreshXml = true)
    {
        targetId ??= documentId;
        if(targetId==documentId&&work.TradeCandidates is null)work=work with{TradeCandidates=tradeCandidates.ToArray()};
        if (targetId is null) throw new InvalidOperationException("No working build is selected.");
        if(recoveryReadOnly&&targetId==documentId)
            throw new IOException("This recovered build is read-only because its recovery data could not be preserved. Duplicate the build to continue editing.");
        EnsureBuildName(committed, targetId);
        // This is the commit point. No visible mutation is published before it.
        PobWorkStore.Save(dataDirectory, targetId, game, work);
        if (targetId == documentId) PrepareHistoryPreviews(work, committed.State);
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
        PobWorkState? work;PobResult committed,shown;var recovered=false;var readOnly=false;string[] draftSlots=[];
        async Task<PobResult> Load(string xml,bool compare=false)
        {
            var loaded=await engine.ExecuteAsync(new{op="load",xml,name="Saved build"},token,compare:compare);
            BuildDocument.VerifySkillPreservation(xml,loaded.Xml);return loaded;
        }
        try
        {
            work=PobWorkStore.Read(dataDirectory,id,game);
            var xml=work?.CommittedXml??await BuildDocument.ReadAsync(Path.Combine(dataDirectory,id+".xml"),game,token);
            committed=await Load(xml);shown=committed;
            if(work?.Pending is {} pending)
            {
                shown=await Load(pending.Xml,compare:true);
                if(pending.Kind=="node"&&!A(shown.State,"nodes").Any(n=>I(n,"id")==pending.NodeId)||
                    pending.Kind=="item"&&(string.IsNullOrWhiteSpace(pending.Slot)||!A(shown.State,"slots").Any(s=>S(s,"name")==pending.Slot)))
                    throw new IOException("The saved comparison cannot be displayed by this engine.");
            }
            if(work?.ItemDraft is {} draft)draftSlots=await ItemSlotsAsync(draft,committed.Xml,token);
        }
        catch(Exception error)when(!token.IsCancellationRequested&&RecoveryFailure(error)&&PobWorkStore.Exists(dataDirectory,id))
        {
            // A broken draft/history must not prevent opening the committed
            // build. Try the atomic record first, then the compatible XML mirror.
            try
            {
                var xml=PobWorkStore.ReadCommitted(dataDirectory,id,game)??throw new IOException("No committed build in recovery record.");
                committed=await Load(xml);
            }
            catch(Exception fallback)when(!token.IsCancellationRequested&&RecoveryFailure(fallback))
            {
                committed=await Load(await BuildDocument.ReadAsync(Path.Combine(dataDirectory,id+".xml"),game,token));
            }
            token.ThrowIfCancellationRequested();
            work=new(committed.Xml,[],[]);shown=committed;recovered=true;
            try
            {
                PobWorkStore.PreserveRecovery(dataDirectory,id);
                PobWorkStore.Save(dataDirectory,id,game,work);
            }
            catch(Exception preservation)when(preservation is IOException or UnauthorizedAccessException)
            {readOnly=true;}
        }
        token.ThrowIfCancellationRequested();
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory, "selected.txt"), id);
        EnsureBuildName(committed, id);
        PrepareHistoryPreviews(work is null ? new(committed.Xml, [], []) : work with { CommittedXml = committed.Xml }, committed.State);
        lock (gate)
        {
            documentId = id; result = shown; pendingBefore = work?.Pending is null ? null : committed;recoveryReadOnly=readOnly;showChangedNodes=false;
            undo.Clear(); redo.Clear();
            foreach (var entry in (work?.Undo ?? []).Reverse()) undo.Push(entry);
            foreach (var entry in (work?.Redo ?? []).Reverse()) redo.Push(entry);
            pastedItem = work?.ItemDraft; previewSlot = work?.Pending?.Slot; itemDraftSlots=draftSlots;
            tradeListing=work?.TradeListing;
            if(tradeListing is {} legacy&&string.IsNullOrWhiteSpace(legacy.BaseType)&&work?.Pending is {Kind:"item",Slot:{} savedSlot})
            {
                var item=A(shown.State,"items").FirstOrDefault(i=>S(i,"slot")==savedSlot);
                if(item.ValueKind==System.Text.Json.JsonValueKind.Object)
                    tradeListing=legacy with{BaseType=S(item,"base"),ItemName=S(item,"name")};
            }
            tradeBrowsing=work?.TradeBrowse==true;
            tradeCandidates.Clear();tradeCandidates.AddRange(work?.TradeCandidates??[]);
            RestoreRanking(id);
            RestoreTradeReturn(id);
            selections.Clear(); detailId = 0; section = "Calcs"; detailKind = ""; filter = "";
            passiveReturnKey = null; nodeReturnToMap = false; historyFocusRequested = false; compatibilityNotice = "";
            // Restore only the context needed to finish the user's pending work.
            if (work?.Pending is { Kind: "node" } node) { section = "Tree"; detailKind = "node"; detailId = node.NodeId; }
            else if (work?.Pending is { Kind: "item" }) { section = "Items"; detailKind = "choose-slot"; }
            else if (work?.Pending is not null) detailKind = "menu";
            else if (pastedItem is not null) { section = "Items"; detailKind = "choose-slot"; }
            focusRequestSequence++;
            if(recovered)ShowFeedback("Opened saved build",readOnly
                ?"Pending work could not be restored or safely preserved. This build is read-only; duplicate it to continue editing."
                :"Pending work or history could not be restored. Your saved build is open, and the failed recovery data has been kept separately.",ToastTone.Warning);
        }
        if (section == "Tree" && artworkDirectory is not null && mapSession is null)
        {
            try { await OpenMapAsync(token); }
            catch (WidgetCapabilityException) { /* The native comparison remains usable without the map. */ }
        }
        await RefreshCommittedMapAsync(token);
    }

    private static bool RecoveryFailure(Exception error)=>error is IOException or System.Text.Json.JsonException or
        InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException or OperationCanceledException;

    private void SetItemDraft(string? raw) => Work(async token =>
    {
        string text;
        try{text=raw??readClipboard();}
        catch(ClipboardTextUnavailableException){throw new IOException("The clipboard has no item text. Hover over an item in game and press Ctrl+C, then try Replace from clipboard again.");}
        if(string.IsNullOrWhiteSpace(text))throw new IOException("The clipboard has no item text. Hover over an item in game and press Ctrl+C, then try Replace from clipboard again.");
        if(text.Length>32768)throw new IOException("The copied text is too long for one item (maximum 32,768 characters). Copy a single item description.");
        string[] slots;
        try{slots=await ItemSlotsAsync(text,result!.Xml,token);}
        catch(PobCommandException error)when(error.Message.Contains("Item was not recognized",StringComparison.Ordinal))
        {throw new IOException("PoB could not recognize an item in the copied text. Copy the complete item description, not a build code or website link.");}
        if(slots.Length==0)throw new IOException("PoB recognized the item, but it has no compatible equipment slot in this build’s current setup.");
        SaveWork(result!, new(result!.Xml,undo.ToArray(),redo.ToArray(),ItemDraft:text), refreshXml: false);
        lock (gate) { tradeBrowsing=false;tradeListing=null;pastedItem = text; itemDraftSlots=slots; detailKind = "choose-slot"; section = "Items"; }
    }, "Reading item…");

    private void DiscardItemDraft() => Work(_ =>
    {
        SaveWork(result!, CurrentWork(result!.Xml), refreshXml: false);
        lock (gate) { tradeBrowsing=false;tradeListing=null;pastedItem = null; previewSlot = null; detailKind = ""; }
        return Task.CompletedTask;
    }, "Discarding item draft…");
}
