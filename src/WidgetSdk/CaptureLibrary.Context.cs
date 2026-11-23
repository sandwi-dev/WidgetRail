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
        return ValidateContextResult(result);
    }

    /// <summary>Attach caller-owned references to a visible saved capture without copying its media or changing its origin.</summary>
    public async ValueTask<CaptureLibraryContextSaveResult> AttachContextAsync(string captureId, long contentRevision, CaptureSaveContext context,
        CancellationToken cancellationToken = default)
    {
        DemandId(captureId);
        if (contentRevision < 0 || context?.IsWellFormed() != true) throw new ArgumentException("Invalid capture context.");
        var request = new CaptureLibraryAttachContextRequest(captureId, contentRevision, context with { References = context.References.ToArray() });
        var result = ValidateContextResult(await client.InvokeAsync(WidgetCaptureLibraryCapabilities.AttachContext, request, cancellationToken).ConfigureAwait(false));
        return result.Association.CaptureId == captureId ? result : throw Malformed();
    }

    /// <summary>Ensure a subject is linked in the calling package's destination namespace.
    /// Existing matching references, including those inherited from an edit, satisfy the request.
    /// An explicit selection can restore the caller's previously detached receipt.</summary>
    public async ValueTask<CaptureContextSaveOutcome> EnsureSubjectAsync(CaptureLibraryEnsureSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request?.IsWellFormed() != true) throw new ArgumentException("Invalid capture subject link.", nameof(request));
        var response = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.EnsureSubject, request, cancellationToken).ConfigureAwait(false);
        return response is not null && Enum.IsDefined(response.Outcome) ? response.Outcome : throw Malformed();
    }

    /// <summary>Remove all references to a selected destination, or undo the exact previously accepted removal.
    /// The host asks the user before changing references owned by another installation. False means the review was cancelled.</summary>
    public async ValueTask<bool> ChangeDestinationAsync(CaptureDestinationChange request, CancellationToken cancellationToken = default)
    {
        if (request?.IsWellFormed() != true) throw new ArgumentException("Invalid capture destination change.", nameof(request));
        var response = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.ChangeDestination, request, cancellationToken).ConfigureAwait(false);
        return response?.Acknowledged ?? throw Malformed();
    }

    /// <summary>Find the caller's editable links for these captures and reference. Ambiguous, hidden, and unavailable captures are omitted.</summary>
    public async ValueTask<IReadOnlyList<CaptureLibraryAssociation>> FindOwnedLinksAsync(IReadOnlyList<string> captureIds,
        CaptureLibraryReferenceFilter reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureIds);
        var request = new CaptureLibraryOwnedLinksRequest(captureIds.ToArray(), reference);
        if (!request.IsWellFormed()) throw new ArgumentException("Invalid capture link lookup.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.FindOwnedLinks, request, cancellationToken).ConfigureAwait(false);
        if (result?.Items is null || result.Items.Count > request.CaptureIds.Count ||
            result.Items.Any(link => link?.IsWellFormed() != true || !request.CaptureIds.Contains(link.CaptureId) ||
                link.References.Count != 1 || link.EventAtUnixMilliseconds is not null || link.References[0].Kind != reference.Kind ||
                link.References[0].Id != reference.Id || reference.Role is { } role && link.References[0].Role != role) ||
            result.Items.Select(link => link.CaptureId).Distinct(StringComparer.Ordinal).Count() != result.Items.Count) throw Malformed();
        return result.Items.Select(link => link with { References = link.References.ToArray() }).ToArray();
    }

    /// <summary>Remove or restore one caller-owned, single-reference link. Keep the returned association for Undo.
    /// A detached receipt survives ordinary attachment retries and index rebuilding.</summary>
    public async ValueTask<CaptureLibraryContextSaveResult> SetContextLinkedAsync(long contentRevision,
        CaptureLibraryAssociation association, bool linked, CancellationToken cancellationToken = default)
    {
        if (association?.IsWellFormed() != true || association.References.Count != 1 || association.EventAtUnixMilliseconds is not null)
            throw new ArgumentException("Expected a single reference link.", nameof(association));
        var request = new CaptureLibrarySetContextLinkedRequest(association.CaptureId, contentRevision,
            association.Id, association.LinkRevision, linked);
        if (!request.IsWellFormed()) throw new ArgumentException("Invalid capture link.");
        var result = ValidateContextResult(await client.InvokeAsync(WidgetCaptureLibraryCapabilities.SetContextLinked, request, cancellationToken).ConfigureAwait(false));
        return result.Association.Id == association.Id && result.Association.CaptureId == association.CaptureId &&
            result.Association.Origin == association.Origin && result.Association.References.SequenceEqual(association.References) &&
            (linked ? result.Outcome is CaptureContextSaveOutcome.Saved or CaptureContextSaveOutcome.AlreadySaved : result.Outcome == CaptureContextSaveOutcome.Detached)
            ? result : throw Malformed();
    }

    private static CaptureLibraryContextSaveResult ValidateContextResult(CaptureLibraryContextSaveResult result)
    {
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Association?.IsWellFormed() != true ||
            result.Item is not null && (!CaptureLibraryLimits.Valid(result.Item) || result.Item.Id != result.Association.CaptureId) ||
            result.Outcome != CaptureContextSaveOutcome.Unavailable && (result.Item is null || !result.Item.Available ||
                result.Outcome != CaptureContextSaveOutcome.Detached && result.Item.ContextCount < 1)) throw Malformed();
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

    /// <summary>Pages distinct package, kind and ID destinations, including display-only references.</summary>
    public async ValueTask<CaptureLibraryDestinationsPage> GetDestinationsAsync(string id, int offset = 0, int count = 16, string? revision = null,
        CancellationToken cancellationToken = default)
    {
        DemandId(id);
        if (offset < 0 || count is < 1 or > CaptureContextLimits.MaximumPageItems || revision is not null && !CaptureLibraryLimits.ValidRevision(revision))
            throw new ArgumentException("Invalid capture destination page.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.GetDestinations, new(id, offset, count, revision), cancellationToken).ConfigureAwait(false);
        if (result?.Items is null || result.Offset != offset || result.TotalCount < 0 ||
            result.Items.Count != Math.Min(count, Math.Max(0, result.TotalCount - offset)) || !CaptureLibraryLimits.ValidRevision(result.Revision) ||
            revision is not null && result.Revision != revision || result.Items.Any(item => item?.IsWellFormed() != true) ||
            result.Items.DistinctBy(item => (item.PackageId, item.Kind, item.Id)).Count() != result.Items.Count) throw Malformed();
        return result with { Items = result.Items.ToArray() };
    }
}
