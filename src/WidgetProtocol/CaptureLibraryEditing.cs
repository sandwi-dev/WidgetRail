using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Exactly one opaque source. Editing always creates a new owned library item.</summary>
public sealed record CaptureLibraryEditRequest(CaptureLibraryMutationReference? Saved = null, CaptureAttachment? Attachment = null)
{
    public bool IsWellFormed() => Saved is { } saved && Attachment is null
        ? CaptureLimits.ValidToken(saved.Id) && saved.ContentRevision >= 0 && saved.MetadataRevision >= 0
        : Saved is null && Attachment is { Origin: CaptureAttachmentOrigin.WindowCapture or CaptureAttachmentOrigin.LibraryRecordingDraft } attachment && attachment.IsWellFormed();
}

[JsonConverter(typeof(JsonStringEnumConverter<CaptureLibraryEditOutcome>))]
public enum CaptureLibraryEditOutcome { Saved, Cancelled }

public sealed record CaptureLibraryEditResult(CaptureLibraryEditOutcome Outcome, CaptureLibraryItem? Item = null);
