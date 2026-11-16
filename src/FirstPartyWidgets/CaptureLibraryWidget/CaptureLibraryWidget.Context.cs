using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private static readonly CompiledWidgetIntentContract browseIntent = CompiledWidgetIntentContract.Create(WidgetIntentContracts.CaptureLibrary);
    private CaptureLibraryContextsPage? contexts;
    private bool showContexts;
    private string? contextError;

    public override async ValueTask<WidgetIntentResult> OnIntentAsync(WidgetIntentRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsActive || !request.IsWellFormed() || request.ContractId != browseIntent.Id || request.Version != browseIntent.Version ||
            request.SchemaDigest != browseIntent.SchemaDigest || !browseIntent.Accepts(request.Payload)) return WidgetIntentResult.Rejected;
        var filter = new CaptureLibraryQuery
        {
            OriginPackageId = request.Payload.GetProperty("originPackageId").GetString(),
            Reference = new(request.Payload.GetProperty("referenceKind").GetString()!, request.Payload.GetProperty("referenceId").GetString()!),
        };
        if (!filter.IsWellFormed() || model.Value.Capturing || model.Value.Copying || pendingBatch is not null) return WidgetIntentResult.Rejected;
        // Activation may still be loading the initial gallery. Await the existing
        // work lane rather than discarding it or rejecting a normal cold launch.
        await Operations.WhenIdleAsync("capture-library.work", cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var current = model.Value;
        if (!IsActive || current.Busy || current.Capturing || current.Copying || pendingBatch is not null ||
            current.Preview is not null && !current.FromLibrary || current.ConfirmDelete) return WidgetIntentResult.Rejected;
        Work(async token =>
        {
            await ReleaseAsync(current, token);
            token.ThrowIfCancellationRequested();
            lock (galleryGate)
            {
                galleryNavigation.Back(); gallery.EndSelection();
                galleryQuery = galleryQuery with { Filter = filter, Revision = null };
                selectedCapture = null; galleryReturn = null; dateFilter = "all";
            }
            showContexts = false; contexts = null; showSources = false; pendingSourceRemoval = null;
            model.Update(state => state with { Preview = null, SelectedId = null, Detail = false, Settings = false, FromLibrary = false });
            await RefreshGalleryAsync(token);
        });
        return WidgetIntentResult.Accepted;
    }

    private bool HandleContextAction(WidgetActionEvent action)
    {
        if (action.ActionId == "capture-library.context.close")
        {
            showContexts = false;
            Operations.Cancel("capture-library.context"); Invalidate(); return true;
        }
        if (model.Value.Busy || selectedCapture is not { } selected || model.Value.SelectedId != selected.Id) return false;
        int offset;
        switch (action.ActionId)
        {
            case "capture-library.context.open": offset = 0; break;
            case "capture-library.context.previous" when showContexts && contexts is { Offset: > 0 }: offset = Math.Max(0, contexts.Offset - 4); break;
            case "capture-library.context.next" when showContexts && contexts is { } page && page.Offset + page.Items.Count < page.TotalCount: offset = page.Offset + page.Items.Count; break;
            default: return false;
        }
        showContexts = true; contextError = null;
        var revision = offset == 0 ? null : contexts?.Revision;
        contexts = null; Invalidate();
        Operations.RunLatest("capture-library.context", async operation =>
        {
            try
            {
                var page = await HostServices.CaptureLibrary.GetContextsAsync(selected.Id, offset, 4, revision, operation.CancellationToken);
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

    private WidgetModal ContextDetails()
    {
        var content = new List<WidgetElement>();
        if (contexts is not { } page) content.Add(UI.Text(contextError ?? "Loading related details…", "capture-library.context.status"));
        else
        {
            foreach (var association in page.Items)
            {
                foreach (var reference in association.References)
                {
                    var id = "capture-library.context." + association.Id + "." + content.Count;
                    content.Add(reference.HasRelatedAction
                        ? UI.Button(reference.Label ?? reference.Kind, id, id).OpenIntent(WidgetIntentContracts.RelatedItem,
                            JsonSerializer.SerializeToElement(new { packageId = association.Origin.PackageId, kind = reference.Kind, id = reference.Id }))
                        : UI.Text(reference.Label ?? reference.Kind,id).Classes("capture-heading"));
                }
            }
            if (page.TotalCount == 0) content.Add(UI.Text("No related details.", "capture-library.context.empty"));
            if (page.Offset > 0 || page.Offset + page.Items.Count < page.TotalCount)
                content.Add(UI.Row("capture-library.context.navigation",
                    UI.Button("Previous", "capture-library.context.previous", "capture-library.context.previous").Disabled(page.Offset == 0),
                    UI.Button("Next", "capture-library.context.next", "capture-library.context.next").Disabled(page.Offset + page.Items.Count >= page.TotalCount)));
        }
        return new("capture-library.context.modal", "Related details", UI.Stack("capture-library.context.content", content.ToArray()),
            "capture-library.context.modal.close", "capture-library.context.close");
    }
}
