using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public sealed partial class WidgetCaptureLibraryService
{
    /// <summary>Save owned media with opaque author context. The host stamps origin and namespaces idempotency to the authenticated widget.
    /// Repeating a matching key returns its original association; a removed original is never recreated.</summary>
    public async ValueTask<CaptureLibraryContextSaveResult> SaveWithContextAsync(CaptureAttachment attachment, CaptureSaveContext context,
        CancellationToken cancellationToken = default)
    {
        if (attachment?.IsWellFormed() != true || context?.IsWellFormed() != true) throw new ArgumentException("Invalid capture context.");
        var frozen = context with { References = context.References.ToArray() };
        if (!frozen.IsWellFormed()) throw new ArgumentException("Invalid capture context.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.SaveWithContext, new(attachment, frozen), cancellationToken).ConfigureAwait(false);
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Association?.IsWellFormed() != true ||
            result.Item is not null && (!CaptureLibraryLimits.Valid(result.Item) || result.Item.Id != result.Association.CaptureId) ||
            result.Outcome != CaptureContextSaveOutcome.Unavailable && (result.Item is null || !result.Item.Available || result.Item.ContextCount < 1)) throw Malformed();
        return result with { Association = result.Association with { References = result.Association.References.ToArray() } };
    }
    /// <summary>Reads bounded context associations separately from gallery metadata; pass Revision across pages for a stable ordering.</summary>
    public async ValueTask<CaptureLibraryContextsPage> GetContextsAsync(string id, int offset = 0, int count = 16, string? revision = null,
        CancellationToken cancellationToken = default)
    {
        DemandId(id);
        if (offset < 0 || count is < 1 or > CaptureContextLimits.MaximumPageItems || revision is not null && !CaptureLibraryLimits.ValidRevision(revision))
            throw new ArgumentException("Invalid capture context page.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.GetContexts, new(id, offset, count, revision), cancellationToken).ConfigureAwait(false);
        if (result?.Items is null || result.Offset != offset || result.TotalCount < 0 || result.Items.Count > count ||
            result.Items.Count > Math.Max(0, result.TotalCount - offset) || !CaptureLibraryLimits.ValidRevision(result.Revision) ||
            revision is not null && result.Revision != revision || result.Items.Any(item => item?.IsWellFormed() != true || item.CaptureId != id) ||
            result.Items.Select(item => item.Id).Distinct().Count() != result.Items.Count) throw Malformed();
        return result with { Items = result.Items.Select(item => item with { References = item.References.ToArray() }).ToArray() };
    }
}
