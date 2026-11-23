using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private sealed record PendingBatch(string Operation, IReadOnlyList<CaptureLibraryItem> Items)
    {
        private int started;
        internal bool Started => Volatile.Read(ref started) != 0;
        internal bool TryStart() => Interlocked.CompareExchange(ref started, 1, 0) == 0;
        internal volatile int Processed;
    }
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
        if (operation == "hide" && selected.All(item => item.SourceId is null))
        {
            UpdateFeedback(state => state with { Message = "Only captures from linked folders can be hidden. Your WidgetRail originals and selection were kept." });
            return;
        }
        batchFailures = []; showBatchFailures = false;
        var batch = new PendingBatch(operation, selected.ToArray());
        pendingBatch = batch;
        if (operation is not ("recycle" or "hide") && batch.TryStart())
            await ApplyBatchAsync(batch, token);
    });

    private async Task ApplyBatchAsync(PendingBatch batch, CancellationToken token)
    {
        try
        {
            await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token);
            if (batch.Operation == "recycle" && batch.Items.Any(item => item.SourceId is not null && item.Available))
                await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.RecycleLinkedPermission], token);
            if (batch.Operation == "copy") model.Update(state => state with { Copying = true });

            var results = new Dictionary<string, CaptureLibraryMutationResult>();
            var interrupted = false;
            // One reply covers one authorization domain. Losing the linked-file reply must
            // never erase already confirmed results for WidgetRail originals (or vice versa).
            var candidates = batch.Items.AsEnumerable();
            if (batch.Operation == "recycle")
            {
                foreach (var item in batch.Items.Where(item => !item.Available))
                    results.Add(item.Id, new(item.Id, ErrorCode: "capture_library_not_found"));
                candidates = candidates.Where(item => item.Available);
            }
            var groups = candidates.GroupBy(item => batch.Operation == "recycle" && item.SourceId is not null);
            var batchSize = batch.Operation is "copy" or "hide" or "unhide" ? 1 : 16;
            foreach (var chunk in groups.SelectMany(group => group.Chunk(batchSize)))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    foreach (var result in await ApplyChunkAsync(batch.Operation, chunk, token))
                        results.Add(result.Id, result);
                }
                catch (Exception error) when (!token.IsCancellationRequested && error is
                    (TimeoutException or IOException or InvalidDataException or WidgetCapabilityException or OperationCanceledException))
                {
                    token.ThrowIfCancellationRequested();
                    var reason = error is WidgetCapabilityException { ErrorCode: "permission_denied" or "lifecycle_denied" } denied
                        ? denied.ErrorCode : "batch_unconfirmed";
                    foreach (var item in chunk) results.TryAdd(item.Id, new(item.Id, ErrorCode: reason));
                    interrupted = true;
                    break;
                }
                token.ThrowIfCancellationRequested();
                batch.Processed = results.Count;
                Invalidate();
            }
            token.ThrowIfCancellationRequested();
            batchFailures = batch.Items
                .Where(item => !results.TryGetValue(item.Id, out var result) || result.ErrorCode is not null)
                .Select(item => (item.Title ?? item.FileName ?? item.Media.SourceApplication?.ApplicationName ?? "Capture",
                    BatchFailureLabel(results.TryGetValue(item.Id, out var result) ? result.ErrorCode! : "batch_not_started"))).ToArray();
            var succeeded = results.Values.Count(result => result.ErrorCode is null);
            gallery.ClearSelection();
            Interlocked.CompareExchange(ref pendingBatch, null, batch);
            var verb = batch.Operation switch
            {
                "recycle" => "moved to the Recycle Bin", "hide" => "hidden", "unhide" => "unhidden",
                "copy" => "copied", _ => "updated",
            };
            var message = interrupted ? $"{succeeded} confirmed. The operation was interrupted. Review the results before retrying." :
                batchFailures.Count == 0 ? $"{succeeded} captures {verb}." :
                $"{succeeded} completed; {batchFailures.Count} could not be changed. Review the results before retrying.";
            UpdateFeedback(state => state with { Message = message });
            await RefreshGalleryAsync(token);
        }
        finally
        {
            // Consume the confirmation even if access is refused, refresh fails or a
            // transport reply is lost. A retired operation cannot clear its successor.
            if (ReferenceEquals(Interlocked.CompareExchange(ref pendingBatch, null, batch), batch))
                QueueGalleryRefresh();
        }
    }

    private async Task<IReadOnlyList<CaptureLibraryMutationResult>> ApplyChunkAsync(string operation, CaptureLibraryItem[] items, CancellationToken token)
    {
        var library = HostServices.CaptureLibrary;
        static CaptureLibraryMutationReference Reference(CaptureLibraryItem item) => new(item.Id, item.Revision, item.MetadataRevision);
        if (operation == "recycle")
        {
            var references = items.Select(Reference).ToArray();
            var reply = items[0].SourceId is null ? await library.RecycleAsync(references, token)
                : await library.RecycleLinkedAsync(references, token);
            return reply.Results;
        }
        if (operation is "favorite" or "unfavorite")
            return (await library.UpdateMetadataBatchAsync(items.Select(item => new CaptureLibraryMetadataRequest(
                item.Id, item.MetadataRevision, item.Title, operation == "favorite")).ToArray(), token)).Results;

        var item = items.Single();
        try
        {
            var reference = Reference(item);
            switch (operation)
            {
                case "copy": return [new(item.Id, await library.SaveCopyAsync(reference, token))];
                case "unhide": return [new(item.Id, await library.UnhideAsync(reference, token))];
                case "hide" when item.SourceId is null: return [new(item.Id, ErrorCode: "capture_library_not_linked")];
                case "hide":
                    await library.HideAsync(reference, token);
                    return [new(item.Id)];
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
        catch (WidgetCapabilityException error) when (error.ErrorCode.StartsWith("capture_library_", StringComparison.Ordinal))
        { return [new(item.Id, ErrorCode: error.ErrorCode)]; }
    }

    private bool HandleBatchAction(WidgetActionEvent action)
    {
        if (action.ActionId == "capture-library.batch.stop" && model.Value.Busy)
        {
            if (pendingBatch is { Started: true } || model.Value.Copying)
            {
                Operations.Cancel("capture-library.work");
                pendingBatch = null;
                UpdateFeedback(state => state with { Busy = false, Copying = false, Message = state.Copying
                    ? "Stopped copying. Completed copies were kept." : "Stopped remaining work. Some changes may already have completed." });
                QueueGalleryRefresh();
            }
            return true;
        }
        if(model.Value.Busy)return false;
        if(action.ActionId=="capture-library.batch.failures") {showBatchFailures=true;Invalidate();return true;}
        if(action.ActionId=="capture-library.batch.failures.close") {showBatchFailures=false;Invalidate();return true;}
        if(action.ActionId=="capture-library.batch.cancel" && pendingBatch is { Started: false }) {pendingBatch=null;Invalidate();return true;}
        if(action.ActionId=="capture-library.batch.confirm"&&pendingBatch is { Operation: "recycle" or "hide" } batch && batch.TryStart())
        {Work(token=>ApplyBatchAsync(batch,token));return true;}
        return false;
    }

    private static string BatchFailureLabel(string error)=>error switch
    {
        "capture_library_copy_exists"=>"A file with this name already exists at the destination.",
        "capture_library_metadata_conflict"=>"Changed since it was selected. Refresh before retrying.",
        "capture_library_recycle_unavailable"=>"Windows could not recycle this file; the original was kept.",
        "capture_library_not_found" or "capture_library_invalid_media"=>"Missing or unreadable.",
        "capture_library_not_linked"=>"Only captures from linked folders can be hidden. This WidgetRail original was kept.",
        "cancelled"=>"Not completed after cancellation.",
        "batch_unconfirmed"=>"No completion receipt. Check the library before retrying.",
        "batch_not_started"=>"Skipped after the operation was interrupted.",
        _=>Explain(error),
    };
    private WidgetModal BatchFailures()=>new("capture-library.batch.failures.modal","Batch results",
        UI.Stack("capture-library.batch.failures.content",batchFailures.Select((item,index)=>
            UI.Text(item.Name+": "+item.Error,"capture-library.batch.failure."+index).Classes("capture-help")).ToArray()),
        "capture-library.batch.failures.modal.close","capture-library.batch.failures.close");

    private WidgetModal BatchConfirmation(State state, PendingBatch batch) => new("capture-library.batch.confirmation",
        batch.Started ? (batch.Operation == "hide" ? "Hiding captures" : "Moving captures to the Recycle Bin") :
            $"{(batch.Operation == "hide" ? "Hide" : "Delete")} {batch.Items.Count} captures?",
        batch.Started ? UI.Stack("capture-library.batch.content",
            UI.Text($"{batch.Processed} of {batch.Items.Count} processed", "capture-library.batch.progress"),
            UI.Progress(batch.Processed, batch.Items.Count, "capture-library.batch.progress-bar", "Captures processed"),
            UI.Button("Stop remaining", "capture-library.batch.stop", "capture-library.batch.cancel")) :
        UI.Stack("capture-library.batch.content",
            UI.Text(batch.Operation == "hide"
                ? "Captures from linked folders are hidden from this library. Their originals stay in place. Restore them using Type → Hidden."
                : "The original files, including files in linked folders, go to the Windows Recycle Bin. Other apps will no longer find them there. You can restore them from the Recycle Bin.","capture-library.batch.help"),
            UI.Text(batch.Operation == "hide" && batch.Items.Any(item => item.SourceId is null)
                ? $"{batch.Items.Count(item => item.SourceId is null)} WidgetRail originals cannot be hidden and will be kept."
                : "Captures changed elsewhere are skipped. Unavailable files and files Windows cannot recycle are kept.","capture-library.batch.safety"),
            UI.Row("capture-library.batch.buttons",
                UI.Button("Cancel","capture-library.batch.cancel","capture-library.batch.cancel"),
                UI.Button(batch.Operation=="hide"?"Hide captures":"Move to Recycle Bin","capture-library.batch.confirm","capture-library.batch.confirm").Busy(state.Busy).Classes("capture-delete")))
            .Classes("capture-confirm-content"),"capture-library.batch.cancel",batch.Started?"capture-library.batch.stop":"capture-library.batch.cancel") {ScrollContent=false};
}
