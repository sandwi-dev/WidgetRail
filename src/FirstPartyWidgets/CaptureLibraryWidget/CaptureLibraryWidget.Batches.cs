using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private sealed record PendingBatch(string Operation, IReadOnlyList<CaptureLibraryItem> Items);
    private PendingBatch? pendingBatch;
    private IReadOnlyList<(string Name,string Error)> batchFailures = [];
    private bool showBatchFailures;

    private void PrepareBatch(string operation) => Work(async token =>
    {
        IReadOnlyList<CaptureLibraryItem> selected;
        try { selected = await gallery.ReadSelectedAsync(token); }
        catch (WidgetIndexedSelectionException)
        { UpdateFeedback(state => state with { Message = "The selection changed or is still loading. Try again." }); return; }
        catch (TimeoutException)
        { UpdateFeedback(state => state with { Message = "The selected captures could not be loaded in time. Try again." }); return; }
        token.ThrowIfCancellationRequested();
        if (selected.Count == 0) return;
        batchFailures = []; showBatchFailures = false;
        pendingBatch = new(operation, selected.ToArray());
        if (operation != "recycle") await ApplyBatchAsync(pendingBatch, token);
    });

    private async Task ApplyBatchAsync(PendingBatch batch, CancellationToken token)
    {
        await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token);
        token.ThrowIfCancellationRequested();
        var results = new List<CaptureLibraryMutationResult>();
        if (batch.Operation == "copy")
        {
            model.Update(state => state with { Copying = true });
            foreach (var item in batch.Items)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var copied = await HostServices.CaptureLibrary.SaveCopyAsync(new(item.Id, item.Revision, item.MetadataRevision), token);
                    results.Add(new(item.Id, copied));
                }
                catch (WidgetCapabilityException error) { results.Add(new(item.Id, ErrorCode: error.ErrorCode)); }
            }
        }
        else if (batch.Operation == "unhide")
        {
            foreach (var item in batch.Items)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var restored = await HostServices.CaptureLibrary.UnhideAsync(new(item.Id, item.Revision, item.MetadataRevision), token);
                    results.Add(new(item.Id, restored));
                }
                catch (WidgetCapabilityException error) { results.Add(new(item.Id, ErrorCode: error.ErrorCode)); }
            }
        }
        else foreach (var chunk in batch.Items.Chunk(64))
        {
            token.ThrowIfCancellationRequested();
            var result = batch.Operation == "recycle"
                ? await RemoveBatchAsync(chunk,token)
                : await HostServices.CaptureLibrary.UpdateMetadataBatchAsync(chunk.Select(item => new CaptureLibraryMetadataRequest(item.Id,item.MetadataRevision,item.Title,batch.Operation=="favorite")).ToArray(),token);
            token.ThrowIfCancellationRequested();
            results.AddRange(result.Results);
        }
        token.ThrowIfCancellationRequested();
        pendingBatch = null;
        gallery.ClearSelection();
        await RefreshGalleryAsync(token);
        token.ThrowIfCancellationRequested();
        batchFailures = results.Where(result=>result.ErrorCode is not null).Select(result=>
        {
            var item=batch.Items.Single(item=>item.Id==result.Id);
            return (item.Title??item.FileName??item.Media.SourceApplication?.ApplicationName??"Capture", BatchFailureLabel(result.ErrorCode!));
        }).ToArray();
        var succeeded = results.Count(result => result.ErrorCode is null);
        var failed = results.Count - succeeded;
        UpdateFeedback(state => state with { Message = failed == 0 ?
            $"{succeeded} captures {(batch.Operation=="unhide"?"unhidden":batch.Operation=="recycle"?(batch.Items.All(item=>item.SourceId is not null)?"hidden":"removed from the library"):batch.Operation=="copy"?"copied":"updated")}." :
            $"{succeeded} completed; {failed} could not be changed. Refresh and select the remaining captures to retry." });
    }

    private async Task<CaptureLibraryBatchResult> RemoveBatchAsync(IReadOnlyList<CaptureLibraryItem> items, CancellationToken token)
    {
        var results = new List<CaptureLibraryMutationResult>();
        var owned = items.Where(item => item.SourceId is null && item.Available).Select(item => new CaptureLibraryMutationReference(item.Id,item.Revision,item.MetadataRevision)).ToArray();
        if (owned.Length > 0) results.AddRange((await HostServices.CaptureLibrary.RecycleAsync(owned,token)).Results);
        foreach (var item in items.Where(item => item.SourceId is not null || !item.Available))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var reference = new CaptureLibraryMutationReference(item.Id,item.Revision,item.MetadataRevision);
                if (item.SourceId is not null) await HostServices.CaptureLibrary.HideAsync(reference,token);
                else await HostServices.CaptureLibrary.RemoveUnavailableAsync(reference,token);
                results.Add(new(item.Id));
            }
            catch (WidgetCapabilityException error) { results.Add(new(item.Id, ErrorCode:error.ErrorCode)); }
        }
        return new(results);
    }

    private bool HandleBatchAction(WidgetActionEvent action)
    {
        if(action.ActionId=="capture-library.batch.stop"&&model.Value.Busy)
        {
            Operations.Cancel("capture-library.work");pendingBatch=null;
            UpdateFeedback(state=>state with{Busy=false,Copying=false,Message=state.Copying ? "Stopped copying. Completed copies were kept." : "Stopped remaining work. Some changes may already have completed."});
            QueueGalleryRefresh();return true;
        }
        if(model.Value.Busy)return false;
        if(action.ActionId=="capture-library.batch.failures") {showBatchFailures=true;Invalidate();return true;}
        if(action.ActionId=="capture-library.batch.failures.close") {showBatchFailures=false;Invalidate();return true;}
        if(action.ActionId=="capture-library.batch.cancel") {pendingBatch=null;Invalidate();return true;}
        if(action.ActionId=="capture-library.batch.confirm"&&pendingBatch is { } batch)
        {Work(token=>ApplyBatchAsync(batch,token));return true;}
        return false;
    }

    private static string BatchFailureLabel(string error)=>error switch
    {
        "capture_library_copy_exists"=>"A file with this name already exists at the destination.",
        "capture_library_metadata_conflict"=>"Changed since it was selected. Refresh before retrying.",
        "capture_library_recycle_unavailable"=>"Windows could not recycle this file; the original was kept.",
        "capture_library_not_found" or "capture_library_invalid_media"=>"Missing or unreadable.",
        "cancelled"=>"Not completed after cancellation.",
        _=>Explain(error),
    };
    private WidgetModal BatchFailures()=>new("capture-library.batch.failures.modal","Captures that could not be changed",
        UI.Stack("capture-library.batch.failures.content",batchFailures.Select((item,index)=>
            UI.Text(item.Name+": "+item.Error,"capture-library.batch.failure."+index).Classes("capture-help")).ToArray()),
        "capture-library.batch.failures.modal.close","capture-library.batch.failures.close");

    private WidgetModal BatchConfirmation(State state, PendingBatch batch) => new("capture-library.batch.confirmation",$"{(batch.Items.All(item=>item.SourceId is not null)?"Hide":"Remove")} {batch.Items.Count} captures?",
        UI.Stack("capture-library.batch.content",
            UI.Text(batch.Items.All(item=>item.SourceId is null && item.Available) ? "The original files go to the Windows Recycle Bin. You can restore them there." : "WidgetRail originals go to the Recycle Bin. Linked captures are hidden and their originals are kept. Unavailable owned entries are removed.","capture-library.batch.help"),
            UI.Text("Captures changed elsewhere are skipped. Files that Windows cannot recycle are kept.","capture-library.batch.safety"),
            UI.Row("capture-library.batch.buttons",
                UI.Button(state.Busy?"Stop remaining":"Cancel",state.Busy?"capture-library.batch.stop":"capture-library.batch.cancel","capture-library.batch.cancel"),
                UI.Button(state.Busy?"Working…":batch.Items.All(item=>item.SourceId is not null)?"Hide captures":"Remove captures","capture-library.batch.confirm","capture-library.batch.confirm").Busy(state.Busy).Classes("capture-delete")))
            .Classes("capture-confirm-content"),"capture-library.batch.cancel",state.Busy?"capture-library.batch.stop":"capture-library.batch.cancel") {ScrollContent=false};
}
