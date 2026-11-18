using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static partial class WidgetCaptureLibraryCapabilities
{
    public static WidgetCapabilityOperation<CaptureLibraryEditRequest, CaptureLibraryEditResult> EditCopy { get; } =
        new(WidgetCaptureLibraryCapabilities.SavePermission, "capture-library.edit-copy");
}

public sealed partial class WidgetCaptureLibraryService
{
    /// <summary>Opens the host editor for the exact saved revision. Requires library read and save consent.
    /// The original is never modified; cancelling the editor creates no item. Does not retry on transport failure.</summary>
    public ValueTask<CaptureLibraryEditResult> EditCopyAsync(CaptureLibraryMutationReference source, CancellationToken cancellationToken = default) =>
        EditCopyCoreAsync(new(Saved: source ?? throw new ArgumentNullException(nameof(source))), cancellationToken);

    /// <summary>Opens the host editor for this widget's owned temporary capture. Requires source capture and library save consent.
    /// The source stays owned by its existing capture lifecycle; the returned item is a separate saved copy.</summary>
    public ValueTask<CaptureLibraryEditResult> EditCopyAsync(CaptureAttachment source, CancellationToken cancellationToken = default) =>
        EditCopyCoreAsync(new(Attachment: source ?? throw new ArgumentNullException(nameof(source))), cancellationToken);

    private async ValueTask<CaptureLibraryEditResult> EditCopyCoreAsync(CaptureLibraryEditRequest request, CancellationToken token)
    {
        if (!request.IsWellFormed()) throw new ArgumentException("Choose one valid saved revision or owned temporary capture.", nameof(request));
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.EditCopy, request, token).ConfigureAwait(false);
        if (result is null || !Enum.IsDefined(result.Outcome) ||
            result.Outcome == CaptureLibraryEditOutcome.Saved && (!CaptureLibraryLimits.Valid(result.Item) || result.Item!.Id == request.Saved?.Id || result.Item.Id == request.Attachment?.Id || result.Item.SourceId is not null) ||
            result.Outcome == CaptureLibraryEditOutcome.Cancelled && result.Item is not null) throw Malformed();
        return result;
    }
}
