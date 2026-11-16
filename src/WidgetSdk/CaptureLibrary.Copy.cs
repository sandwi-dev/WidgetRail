using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetSdk;

public sealed partial class WidgetCaptureLibraryService
{
    /// <summary>Copy at most 256 originals to one folder selected by the user in host UI.
    /// Existing destination files are never replaced. Each item reports its own outcome;
    /// completed copies remain if later items fail or the operation is cancelled.</summary>
    public async ValueTask<CaptureLibraryBatchResult> CopyAsync(IReadOnlyList<CaptureLibraryMutationReference> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var captured = items.ToArray();
        if (captured.Length is < 1 or > CaptureLibraryLimits.MaximumCopyItems ||
            captured.Any(item => item is null || !CaptureLimits.ValidToken(item.Id) || item.ContentRevision < 0 || item.MetadataRevision < 0) ||
            captured.Select(item => item.Id).Distinct().Count() != captured.Length) throw new ArgumentException("Invalid capture copy batch.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Copy, new(captured), cancellationToken).ConfigureAwait(false);
        return ValidateBatch(result, captured.Select(item => item.Id).ToArray());
    }
}
