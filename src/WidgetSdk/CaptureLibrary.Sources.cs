using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetCaptureLibrarySourceCapabilities
{
    public static WidgetCapabilityOperation<CaptureLibraryChooseSourceRequest, CaptureLibrarySourceChoice> Choose { get; } = new(WidgetCaptureLibraryCapabilities.ManagePermission, "capture-library.choose-source");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, CaptureLibrarySources> List { get; } = new(WidgetCaptureLibraryCapabilities.ReadPermission, "capture-library.sources");
    public static WidgetCapabilityOperation<CaptureLibrarySourceReference, WidgetCapabilityAcknowledgement> Remove { get; } = new(WidgetCaptureLibraryCapabilities.ManagePermission, "capture-library.remove-source");
    public static WidgetCapabilityOperation<CaptureLibraryLinkRequest, CaptureLibraryLinkResult> Link { get; } = new(WidgetCaptureLibraryCapabilities.ManagePermission, "capture-library.link");
    public static WidgetCapabilityOperation<CaptureLibraryMutationReference, WidgetCapabilityAcknowledgement> RemoveLink { get; } = new(WidgetCaptureLibraryCapabilities.ManagePermission, "capture-library.remove-link");
    public static WidgetCapabilityOperation<CaptureLibraryMutationReference, CaptureLibraryItem> Unhide { get; } = new(WidgetCaptureLibraryCapabilities.ManagePermission, "capture-library.unhide");
}

public sealed partial class WidgetCaptureLibraryService
{
    /// <summary>Ask the user to link a folder read-only. Cancellation returns null. The chosen path is returned for provider configuration, not file-access authority.</summary>
    public async ValueTask<CaptureLibrarySource?> ChooseSourceAsync(string label, CancellationToken cancellationToken = default)
    {
        if (!CaptureLibrarySourceLimits.ValidLabel(label)) throw new ArgumentException("Invalid source label.", nameof(label));
        var result = await client.InvokeAsync(WidgetCaptureLibrarySourceCapabilities.Choose, new(label), cancellationToken).ConfigureAwait(false);
        if (result is null || result.Source is not null && !CaptureLibrarySourceLimits.Valid(result.Source)) throw Malformed();
        return result.Source;
    }
    /// <summary>Ask the host to resolve a known capture folder and obtain user approval. Falls back to its folder picker if unavailable.</summary>
    public async ValueTask<CaptureLibrarySource?> ChooseKnownSourceAsync(CaptureLibraryKnownLocation location, string label,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(location) || !CaptureLibrarySourceLimits.ValidLabel(label)) throw new ArgumentException("Invalid capture source.");
        var result = await client.InvokeAsync(WidgetCaptureLibrarySourceCapabilities.Choose,
            new(label) { KnownLocation = location }, cancellationToken).ConfigureAwait(false);
        if (result is null || result.Source is not null && !CaptureLibrarySourceLimits.Valid(result.Source)) throw Malformed();
        return result.Source;
    }
    public async ValueTask<IReadOnlyList<CaptureLibrarySource>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.InvokeAsync(WidgetCaptureLibrarySourceCapabilities.List, new(), cancellationToken).ConfigureAwait(false);
        if (result?.Items is null || result.Items.Count > CaptureLibrarySourceLimits.MaximumSources || result.Items.Any(s => !CaptureLibrarySourceLimits.Valid(s)) ||
            result.Items.Select(s => s.Id).Distinct().Count() != result.Items.Count) throw Malformed();
        return result.Items.ToArray();
    }
    public async ValueTask RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        DemandId(sourceId);
        var result = await client.InvokeAsync(WidgetCaptureLibrarySourceCapabilities.Remove, new(sourceId), cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }
    /// <summary>Associate an exact provider-relative file in a source owned by this widget. Original bytes are not copied.</summary>
    public async ValueTask<CaptureLibraryItem> LinkAsync(string sourceId, string relativePath, CaptureSaveContext? context = null, CancellationToken cancellationToken = default)
    {
        DemandId(sourceId);
        if (!CaptureLibrarySourceLimits.ValidRelativePath(relativePath) || context is not null && !context.IsWellFormed()) throw new ArgumentException("Invalid linked capture.");
        var result = await client.InvokeAsync(WidgetCaptureLibrarySourceCapabilities.Link,
            new(sourceId, relativePath, context is null ? null : context with { References = context.References.ToArray() }), cancellationToken).ConfigureAwait(false);
        if (result is null || !CaptureLibraryLimits.Valid(result.Item) || result.Item.SourceId != sourceId) throw Malformed();
        return result.Item;
    }
    /// <summary>Hide a linked entry from normal discovery while preserving its original and known-ID reads.</summary>
    public async ValueTask RemoveLinkAsync(CaptureLibraryMutationReference item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item); DemandId(item.Id);
        if (item.ContentRevision < 0 || item.MetadataRevision < 0) throw new ArgumentException("Invalid linked capture revision.");
        var result = await client.InvokeAsync(WidgetCaptureLibrarySourceCapabilities.RemoveLink, item, cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }
    /// <summary>Hide a linked entry; equivalent to the compatible RemoveLink operation.</summary>
    public ValueTask HideAsync(CaptureLibraryMutationReference item, CancellationToken cancellationToken = default) => RemoveLinkAsync(item, cancellationToken);

    /// <summary>Restore an exact hidden linked entry to normal discovery without modifying its original.</summary>
    public async ValueTask<CaptureLibraryItem> UnhideAsync(CaptureLibraryMutationReference item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item); DemandId(item.Id);
        if (item.ContentRevision < 0 || item.MetadataRevision < 0) throw new ArgumentException("Invalid linked capture revision.");
        var result = await client.InvokeAsync(WidgetCaptureLibrarySourceCapabilities.Unhide, item, cancellationToken).ConfigureAwait(false);
        if (!CaptureLibraryLimits.Valid(result) || result.Id != item.Id || result.SourceId is null) throw Malformed();
        return result;
    }
}
