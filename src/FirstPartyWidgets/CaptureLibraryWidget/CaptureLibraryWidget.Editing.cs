using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private void EditCaptureCopy(State original)
    {
        var saved = original.SelectedId is { } id && selectedCapture?.Id == id ? selectedCapture : null;
        if (saved is not { Available: true }) return;
        model.Update(state => state with { Busy = true, Message = null });
        Operations.RunLatest("capture-library.work", async context =>
        {
            CaptureAttachment? unclaimed = null;
            var committed = false;
            try
            {
                // This consumer opens the saved result, so it needs read as well as save.
                await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ReadPermission,
                    WidgetCaptureLibraryCapabilities.SavePermission], context.CancellationToken);
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!context.IsCurrent || !SamePreviewSource(original)) return;
                var result = await HostServices.CaptureLibrary.EditCopyAsync(
                    new CaptureLibraryMutationReference(saved.Id, saved.Revision, saved.MetadataRevision), context.CancellationToken);
                if (result.Outcome == CaptureLibraryEditOutcome.Cancelled) return;
                committed = true;
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!context.IsCurrent || !SamePreviewSource(original)) return;
                var item = result.Item!;
                unclaimed = await HostServices.CaptureLibrary.OpenAsync(item.Id, cancellationToken: context.CancellationToken);
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!context.IsCurrent || !SamePreviewSource(original)) return;
                lock (galleryGate)
                {
                    if (!context.IsCurrent || !SamePreviewSource(original)) return;
                    selectedCapture = item;
                    contexts = null; contextError = null; showContexts = false;
                    UpdateFeedback(state => state with { Preview = unclaimed, SelectedId = item.Id,
                        Detail = true, ConfirmDelete = false, Navigation = WidgetNavigationOperation.Replace, Message = "Edited copy saved." });
                }
                unclaimed = null;
                // The original is no longer published. Release only its exact lease;
                // cancellation cannot turn this cleanup into release of a newer preview.
                try { await ReleaseAsync(original, CancellationToken.None); }
                catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { }
                QueueGalleryRefresh();
            }
            catch (OperationCanceledException) { }
            catch (WidgetCapabilityException error)
            {
                if (context.IsCurrent) UpdateFeedback(state => state with { Message = committed
                    ? "The edited copy was saved, but could not be opened. You can find it in the library."
                    : ExplainEdit(error.ErrorCode) });
            }
            finally
            {
                if (unclaimed is not null)
                    try { await HostServices.CaptureLibrary.ReleaseAsync(unclaimed, CancellationToken.None); }
                    catch (Exception error) when (error is WidgetCapabilityException or OperationCanceledException or ObjectDisposedException) { }
                if (context.IsCurrent) model.Update(state => state with { Busy = false });
            }
        }, WidgetOperationLifetime.Widget);
    }

    private bool SamePreviewSource(State original)
    {
        var current = model.Value;
        return current.SelectedId == original.SelectedId && current.Preview?.Id == original.Preview?.Id &&
            current.Detail == original.Detail;
    }

    private static string ExplainEdit(string code) => code switch
    {
        "capture_library_edit_busy" => "Finish or cancel the current edit before opening another.",
        "capture_library_edit_unavailable" => "Editing is not available in this version of WidgetRail.",
        "capture_library_edit_unsupported_image" => "This image cannot be edited here. Choose a supported PNG screenshot.",
        "capture_library_edit_unsupported_video" => "This clip cannot be edited here. Choose a supported MP4 clip.",
        "capture_library_edit_output_limit" => "The edited copy is too large to save. Use a smaller crop or shorter clip.",
        "capture_library_edit_source_retired" or "capture_library_metadata_conflict" or "capture_library_not_found" =>
            "This capture changed or is no longer available. Open it again or take a new capture.",
        "capture_library_edit_invalid_recipe" => "Those edits could not be applied. Reopen the editor and try again.",
        "capture_library_edit_failed" => "The edited copy could not be saved. Your original capture is still available.",
        _ => Explain(code),
    };
}
