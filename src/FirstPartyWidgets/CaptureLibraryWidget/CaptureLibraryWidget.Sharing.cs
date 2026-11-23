using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private const int MaximumSharedCaptures = 10;
    private sealed record CaptureShare(IReadOnlyList<WidgetFileReference> Files,WidgetFileShareOffer Offer,IReadOnlyList<CaptureAttachment> OwnedCaptures,string? PreviewId,long NavigationRevision);
    private CaptureShare? captureShare;
    private int preparingCaptureShare;
    private static string CaptureShareKey(CaptureAttachment media)=>"share."+media.Id;
    private static string PreviewShareKey(CaptureLibraryItem item) => $"share.preview.{item.Id}.{item.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    private static ButtonElement SharePreviewButton(CaptureLibraryItem item, string id) => UI.Button("Share", "capture-library.preview.share", id).Icon(WidgetGlyph.Share,"Share capture")
        .OpenIntent(WidgetIntentContracts.Files, JsonSerializer.SerializeToElement(new { offerId = new string('0', 32) }), WidgetIntentPresentation.OpenWidget)
        .PrepareIntent(PreviewShareKey(item)).WithIntentFeedback();
    private static SavedCaptureMediaReference? PreviewShareReference(string key)
    {
        const string prefix = "share.preview.";
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var parts = key[prefix.Length..].Split('.');
        return parts.Length == 2 && CaptureLimits.ValidToken(parts[0]) &&
            long.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var revision)
            ? new(parts[0], revision) : null;
    }
    private ButtonElement ShareCaptureButton(State state)=>UI.Button("Share","capture-library.share","capture-library.share").Icon(WidgetGlyph.Share,"Share capture")
        .OpenIntent(WidgetIntentContracts.Files,JsonSerializer.SerializeToElement(new{offerId=new string('0',32)}),WidgetIntentPresentation.OpenWidget)
        .PrepareIntent(CaptureShareKey(state.Preview!)).WithIntentFeedback().Busy(state.Busy).Classes("capture-primary");

    private string SelectionKey(string operation)
    {
        var selection = gallery.Selection!;
        var descriptor = gallery.Descriptor;
        var positions = string.Join(",", selection.Indices);
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(positions)))[..24];
        return $"{operation}.selection.{descriptor.QueryGeneration}.{descriptor.ContentRevision}.{selection.Epoch}.{digest}";
    }

    private string SelectionShareKey() => SelectionKey("share");

    private static System.Text.Json.JsonElement AssociationPayload(IEnumerable<CaptureLibraryItem> captures) =>
        JsonSerializer.SerializeToElement(new { captures = captures.Select(item => new { id = item.Id, revision = item.Revision }) });

    private ButtonElement AssociateSelectedButton(State state) => UI.Button("Link to game…", "capture-library.batch.link", "capture-library.batch.link")
        .OpenIntent(WidgetIntentContracts.CaptureAssociation, JsonSerializer.SerializeToElement(new {
            captures = new[] { new { id = new string('0', 32), revision = 0L } } }), WidgetIntentPresentation.OpenWidget)
        .PrepareIntent(SelectionKey("associate")).WithIntentFeedback().Busy(state.Busy).Disabled(gallery.Selection!.Count is < 1 or > 64);

    private ButtonElement ShareSelectionButton(State state) => UI.IconButton(WidgetGlyph.Share, "capture-library.batch.share", "capture-library.batch.share", "Share selected captures")
        .OpenIntent(WidgetIntentContracts.Files, JsonSerializer.SerializeToElement(new { offerId = new string('0', 32) }), WidgetIntentPresentation.OpenWidget)
        .PrepareIntent(SelectionShareKey()).WithIntentFeedback().Busy(state.Busy)
        .Disabled(gallery.Selection!.Count is < 1 or > MaximumSharedCaptures).Classes("capture-primary");

    public override async ValueTask<JsonElement?> OnPrepareIntentAsync(string key,WidgetIntentRequest request,CancellationToken cancellationToken=default)
    {
        var selected=model.Value;
        if (request.ContractId == WidgetIntentContracts.AssociateCaptures)
        {
            var scope = galleryNavigation.Value.Revision;
            bool SelectionCurrent() => !model.Value.Busy && !model.Value.Detail && galleryNavigation.Value.Revision == scope &&
                gallery.Selection is { IsActive: true, Count: > 0 and <= 64 } && key == SelectionKey("associate");
            if (!SelectionCurrent()) return null;
            try
            {
                var items = await gallery.ReadSelectedAsync(cancellationToken);
                if (SelectionCurrent() && items.All(item => item.Available)) return AssociationPayload(items);
            }
            catch (Exception error) when (error is WidgetIndexedSelectionException or WidgetCapabilityException or TimeoutException) { }
            if (SelectionCurrent()) UpdateFeedback(value => value with { Message = "The selection changed. Review the captures and try again." });
            return null;
        }
        var media = selected.Preview;
        var navigation = galleryNavigation.Value.Revision;
        var preview = PreviewShareReference(key);
        GalleryQuery previewQuery; lock (galleryGate) previewQuery = galleryQuery;
        var fromPreview = preview is not null && !selected.Detail && !selected.Busy && gallery.Selection is not { IsActive: true } && previewQuery.ClipPreviews;
        var batch = !selected.Detail && gallery.Selection is { IsActive: true, Count: > 0 and <= MaximumSharedCaptures } && key == SelectionShareKey();
        if(request.ContractId!=WidgetIntentContracts.FileShare || selected.Busy ||
            (!batch && !fromPreview && (media is null || key!=CaptureShareKey(media))) ||
            Interlocked.CompareExchange(ref preparingCaptureShare,1,0)!=0)return null;
        var owned = new List<CaptureAttachment>(); var files = new List<WidgetFileReference>(); WidgetFileShareOffer? offer=null;
        bool Current() => galleryNavigation.Value.Revision == navigation && (fromPreview
            ? !model.Value.Detail && gallery.Selection is not { IsActive: true } && ReferenceEquals(galleryQuery, previewQuery)
            : batch
            ? !model.Value.Detail && gallery.Selection is { IsActive: true } && key == SelectionShareKey()
            : model.Value.Preview?.Id == media!.Id);
        try
        {
            if(captureShare is {} prior)await ReleaseCaptureShareAsync(prior,cancellationToken);
            IReadOnlyList<CaptureLibraryItem> items = batch ? await gallery.ReadSelectedAsync(cancellationToken) : Array.Empty<CaptureLibraryItem>();
            if (fromPreview)
            {
                // Membership and original identity belong to the exact displayed query,
                // not a path supplied by a row or whichever detail was opened last.
                var position = await HostServices.CaptureLibrary.FindAsync(previewQuery.Filter, preview!.Id, previewQuery.Revision, cancellationToken);
                var item = await HostServices.CaptureLibrary.GetMetadataAsync(preview.Id, cancellationToken);
                if (position.Index is null || !item.Available || item.Revision != preview.Revision || !Current()) return null;
                items = [item];
            }
            if (!Current() || batch && (items.Count is < 1 or > MaximumSharedCaptures)) return null;
            if (items.Any(item => !item.Available)) throw new InvalidOperationException("Selected capture unavailable.");
            if (batch || fromPreview)
            {
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Current()) return null;
                    var attachment = await HostServices.CaptureLibrary.OpenAsync(item.Id, cancellationToken: cancellationToken);
                    owned.Add(attachment);
                    var current = await HostServices.CaptureLibrary.GetMetadataAsync(item.Id, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Current()) return null;
                    if (!current.Available || current.Revision != item.Revision) throw new InvalidOperationException("Selected capture changed.");
                    files.Add(await HostServices.Files.FromCaptureAsync(attachment, cancellationToken: cancellationToken));
                }
            }
            else
            {
                if(selected.SelectedId is {} id) owned.Add(await HostServices.CaptureLibrary.OpenAsync(id,cancellationToken:cancellationToken));
                files.Add(await HostServices.Files.FromCaptureAsync(owned.LastOrDefault() ?? media!,cancellationToken:cancellationToken));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if(!Current())return null;
            offer=await HostServices.Files.CreateShareOfferAsync(files,cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if(!Current())return null;
            var pending=new CaptureShare(files.ToArray(),offer,owned.ToArray(),batch || fromPreview ? null : media!.Id,navigation);captureShare=pending;
            Operations.RunLatest("capture-library.share-expiry",async operation=>
            {
                await Task.Delay(TimeSpan.FromSeconds(70),operation.CancellationToken);
                if(ReferenceEquals(captureShare,pending))await ReleaseCaptureShareAsync(pending,operation.CancellationToken);
            },WidgetOperationLifetime.Widget);
            files.Clear();offer=null;owned.Clear();
            return JsonSerializer.SerializeToElement(new{offerId=pending.Offer.Id});
        }
        catch(Exception) when(!cancellationToken.IsCancellationRequested)
        {if(Current())UpdateFeedback(value=>value with{Message=batch ? "Could not prepare the selected captures. Refresh the library and select them again." : "Could not prepare this capture for sharing. Try opening it again."});return null;}
        finally
        {
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if(offer is not null)try{await HostServices.Files.CancelShareOfferAsync(offer.Id,cleanup.Token);}catch(Exception){ }
            foreach(var file in files)try{await HostServices.Files.ReleaseAsync(file,cleanup.Token);}catch(Exception){ }
            foreach(var attachment in owned)try{await HostServices.CaptureLibrary.ReleaseAsync(attachment,cleanup.Token);}catch(Exception){ }
            Volatile.Write(ref preparingCaptureShare,0);
        }
    }

    public override async ValueTask<bool> OnIntentCompletedAsync(WidgetIntentFeedback feedback,CancellationToken cancellationToken=default)
    {
        if (feedback.Request.ContractId == WidgetIntentContracts.AssociateCaptures)
        {
            if (feedback.Status is WidgetIntentStatus.Unavailable or WidgetIntentStatus.Rejected)
                UpdateFeedback(value => value with { Message = "Game picker unavailable. Check capture permissions." });
            return false;
        }
        if(feedback.Request.ContractId!=WidgetIntentContracts.FileShare)return false;
        if(captureShare is not {} pending || !feedback.Request.Payload.TryGetProperty("offerId",out var offer) || offer.GetString()!=pending.Offer.Id)return false;
        await ReleaseCaptureShareAsync(pending,cancellationToken);
        if(galleryNavigation.Value.Revision == pending.NavigationRevision && model.Value.Preview?.Id==pending.PreviewId && feedback.Status is WidgetIntentStatus.Unavailable or WidgetIntentStatus.Rejected)
            UpdateFeedback(value=>value with{Message=$"No available app accepted {(pending.Files.Count == 1 ? "this capture" : "these captures")}. Open a compatible sharing widget and sign in, then try Share again."});
        return false;
    }

    private async Task ReleaseCaptureShareAsync(CaptureShare pending,CancellationToken token)
    {
        if(!ReferenceEquals(Interlocked.CompareExchange(ref captureShare,null,pending),pending))return;
        try{await HostServices.Files.CancelShareOfferAsync(pending.Offer.Id,token);}catch(Exception){ }
        foreach(var file in pending.Files)try{await HostServices.Files.ReleaseAsync(file,token);}catch(Exception){ }
        foreach(var capture in pending.OwnedCaptures)try{await HostServices.CaptureLibrary.ReleaseAsync(capture,token);}catch(Exception){ }
    }
}
