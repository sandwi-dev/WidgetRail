using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private static readonly CompiledWidgetIntentContract browseIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.CaptureLibrary);
    private static readonly CompiledWidgetIntentContract namedBrowseIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.CaptureLibraryV2);
    private CaptureLibraryDestinationsPage? contexts;
    private bool showContexts;
    private string? contextError;
    private CaptureDestinationChange? undoDestination;
    private string? undoDestinationLabel;
    private FocusGroupEntryRequest? destinationFocus;
    private const string ContextBody = "capture-library.context.content";
    private const string ContextEdit = "capture-library.context.edit";

    private static string DestinationId(CaptureLibraryDestination d) => WidgetIds.Scope("capture-library.context")
        .KeyedId("destination", d.PackageId + "/" + d.Kind + "/" + d.Id);
    private static string UnlinkId(CaptureLibraryDestination d, string capture, string revision) => WidgetIds.Scope("capture-library.context")
        .KeyedId("unlink", DestinationId(d) + "/" + capture + "/" + revision);

    public override async ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        var contract = request.Version == namedBrowseIntent.Version ? namedBrowseIntent : browseIntent;
        if (!IsActive || !request.IsWellFormed() || request.ContractId != contract.Id || request.Version != contract.Version ||
            request.SchemaDigest != contract.SchemaDigest || !contract.Accepts(request.Payload)) return WidgetIntentResult.Rejected;
        var label = request.Version == namedBrowseIntent.Version ? request.Payload.GetProperty("referenceLabel").GetString()?.Trim() : null;
        var filter = DefaultGalleryFilter with
        {
            OriginPackageId = request.Payload.GetProperty("originPackageId").GetString(),
            Reference = new(request.Payload.GetProperty("referenceKind").GetString()!, request.Payload.GetProperty("referenceId").GetString()!),
        };
        if (!filter.IsWellFormed() || model.Value.Copying || pendingBatch is not null) return WidgetIntentResult.Rejected;
        // Activation may still be loading the initial gallery. Await the existing
        // work lane rather than discarding it or rejecting a normal cold launch.
        await Operations.WhenIdleAsync("capture-library.work", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var current = model.Value;
        if (!IsActive || current.Busy || current.Copying || pendingBatch is not null ||
            current.ConfirmDelete) return WidgetIntentResult.Rejected;
        Work(async token =>
        {
            await ReleaseAsync(current, token);
            token.ThrowIfCancellationRequested();
            lock (galleryGate)
            {
                galleryNavigation.Back(); gallery.EndSelection();
                galleryQuery = galleryQuery with { Filter = filter, Revision = null, RelatedLabel = string.IsNullOrEmpty(label) ? null : label };
                selectedCapture = null; galleryReturn = null; dateFilter = "all";
            }
            showContexts = false; contexts = null; showSources = false; pendingSourceRemoval = null;
            model.Update(state => state with { Preview = null, SelectedId = null, Detail = false, Settings = false });
            await RefreshGalleryAsync(token);
        });
        return WidgetIntentResult.Accepted;
    }

    private bool HandleContextAction(WidgetActionEvent action)
    {
        if (action.ActionId == "capture-library.context.close")
        {
            showContexts = false;
            destinationFocus = null;
            Operations.Cancel("capture-library.context"); Operations.Cancel(ContextEdit); Invalidate(); return true;
        }
        if (model.Value.Busy || selectedCapture is not { } selected || model.Value.SelectedId != selected.Id) return false;
        if (showContexts && contexts is { } visiblePage && selected.Available)
        {
            var destination = visiblePage.Items.FirstOrDefault(item => item.HasRelatedAction && UnlinkId(item, selected.Id, visiblePage.Revision) == action.ActionId);
            if (destination is not null)
            {
                ChangeContext(selected, visiblePage, new(selected.Id, selected.Revision, destination.PackageId, destination.Kind,
                    destination.Id, Guid.NewGuid().ToString("N"), visiblePage.Revision), destination.Label ?? destination.Kind);
                return true;
            }
            if (undoDestination is { } undo && undo.CaptureId == selected.Id && undo.ContentRevision == selected.Revision &&
                action.ActionId == "capture-library.context.undo." + undo.OperationId)
            {
                ChangeContext(selected, visiblePage, undo with { Undo = true }, undoDestinationLabel!);
                return true;
            }
        }
        if (Operations.IsBusy(ContextEdit)) return true;
        int offset;
        switch (action.ActionId)
        {
            case "capture-library.context.open": offset = 0; break;
            case "capture-library.context.previous" when showContexts && contexts is { Offset: > 0 }: offset = Math.Max(0, contexts.Offset - 4); break;
            case "capture-library.context.next" when showContexts && contexts is { } page && page.Offset + page.Items.Count < page.TotalCount: offset = page.Offset + page.Items.Count; break;
            default: return false;
        }
        showContexts = true; contextError = null; destinationFocus = null;
        var revision = offset == 0 ? null : contexts?.Revision;
        contexts = null; Invalidate();
        Operations.RunLatest("capture-library.context", async operation =>
        {
            try
            {
                var page = await HostServices.CaptureLibrary.GetDestinationsAsync(selected.Id, offset, 4, revision, operation.CancellationToken);
                if (operation.IsCurrent && showContexts && model.Value.SelectedId == selected.Id) { contexts = page; Invalidate(); }
            }
            catch (WidgetCapabilityException)
            {
                if (operation.IsCurrent && showContexts && model.Value.SelectedId == selected.Id)
                { contextError = "Related details changed or are unavailable. Close and reopen to try again."; Invalidate(); }
            }
        }, WidgetOperationLifetime.Widget);
        return true;
    }

    private void ChangeContext(CaptureLibraryItem selected, CaptureLibraryDestinationsPage page, CaptureDestinationChange change, string label)
    {
        Operations.RunSingleFlight(ContextEdit, async op =>
        {
            bool Current() => op.IsCurrent && showContexts && model.Value.SelectedId == selected.Id;
            try
            {
                await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], op.CancellationToken);
                if (!Current() || !await HostServices.CaptureLibrary.ChangeDestinationAsync(change, op.CancellationToken) || !Current()) return;
                undoDestination = change.Undo ? null : change;
                undoDestinationLabel = change.Undo ? null : label;
                contextError = null;
                var metadata = await HostServices.CaptureLibrary.GetMetadataAsync(selected.Id, op.CancellationToken);
                var fresh = await HostServices.CaptureLibrary.GetDestinationsAsync(selected.Id, page.Offset, 4, cancellationToken: op.CancellationToken);
                if (fresh.Offset > 0 && fresh.Items.Count == 0)
                    fresh = await HostServices.CaptureLibrary.GetDestinationsAsync(selected.Id, Math.Max(0, (fresh.TotalCount - 1) / 4 * 4), 4,
                        fresh.Revision, op.CancellationToken);
                if (!Current()) return;
                selectedCapture = metadata;
                contexts = fresh;
                var restored = fresh.Items.FirstOrDefault(d => d.PackageId == change.PackageId && d.Kind == change.Kind && d.Id == change.DestinationId);
                var target = !change.Undo ? "capture-library.context.undo" : restored is { HasRelatedAction: true } ? DestinationId(restored) : null;
                destinationFocus = target is null ? null : CreateFocusRequest().FocusElement(ContextBody, target);
            }
            catch (Exception e) when (e is WidgetCapabilityException or WidgetCapabilityUnavailableException)
            {
                if (Current()) contextError = "Could not update related details. Reopen to check the current links.";
            }
            finally { if (Current()) Invalidate(); }
        }, WidgetOperationLifetime.Widget);
    }

    private WidgetModal ContextDetails()
    {
        var content = new List<WidgetElement>();
        if (contexts is not { } page) content.Add(UI.Text(contextError ?? "Loading related details…", "capture-library.context.status"));
        else
        {
            foreach (var destination in page.Items)
            {
                var id = DestinationId(destination);
                WidgetElement entry = destination.HasRelatedAction
                    ? UI.Button(destination.Label ?? destination.Kind, id, id).OpenIntent(WidgetIntentContracts.RelatedItem,
                        JsonSerializer.SerializeToElement(new { packageId = destination.PackageId, kind = destination.Kind, id = destination.Id }))
                    : UI.Text(destination.Label ?? destination.Kind,id).Classes("capture-heading");
                if (destination.HasRelatedAction && selectedCapture is { Available: true } item)
                {
                    var unlink = UI.Button("Unlink", UnlinkId(destination, item.Id, page.Revision), id + ".unlink") with
                    {
                        AccessibilityLabel = "Unlink from " + (destination.Label ?? destination.Kind),
                    };
                    entry = UI.Grid(id + ".row", [], [GridTrack.Star(), GridTrack.Auto()], entry, unlink.InGrid(column: 1)).Spacing(0, 8);
                }
                content.Add(entry);
            }
            if (page.TotalCount == 0) content.Add(UI.Text("No related details.", "capture-library.context.empty"));
            if (contextError is not null) content.Add(UI.Text(contextError, "capture-library.context.error"));
            if (page.Offset > 0 || page.Offset + page.Items.Count < page.TotalCount)
                content.Add(UI.Row("capture-library.context.navigation",
                    UI.Button("Previous", "capture-library.context.previous", "capture-library.context.previous").Disabled(page.Offset == 0),
                    UI.Button("Next", "capture-library.context.next", "capture-library.context.next").Disabled(page.Offset + page.Items.Count >= page.TotalCount)));
        }
        if (undoDestination is { } undo && selectedCapture is { } selected && selected.Id == undo.CaptureId && selected.Revision == undo.ContentRevision)
            content.Add(UI.Button("Undo unlink · " + undoDestinationLabel, "capture-library.context.undo." + undo.OperationId, "capture-library.context.undo"));
        var body = UI.Stack(ContextBody, content.ToArray());
        if (destinationFocus?.TargetId is { } target) body = body.RememberChildFocus(target);
        return new("capture-library.context.modal", "Related details", body,
            "capture-library.context.modal.close", "capture-library.context.close");
    }
}
