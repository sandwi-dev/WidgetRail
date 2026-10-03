using WidgetRail.WidgetProtocol;
namespace WidgetRail.WidgetSdk;

/// <summary>Reusable inline native image/video playback from this widget's host-issued capture.</summary>
public sealed record CapturedMediaElement : WidgetElement
{
    internal CapturedMediaElement(CaptureAttachment attachment, string id, string accessibleName) : base(RequireId(id))
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (!attachment.IsWellFormed()) throw new ArgumentException("Capture attachment is invalid.", nameof(attachment));
        ArgumentException.ThrowIfNullOrWhiteSpace(accessibleName);
        Attachment = attachment; AccessibleName = accessibleName;
    }
    public CaptureAttachment Attachment { get; init; }
    public string AccessibleName { get; init; }
    internal override ViewNode ToProtocolNode() => new()
    { Id = Id, Kind = ViewNodeKind.CapturedMedia, CapturedMedia = Attachment, AccessibilityLabel = AccessibleName, StyleClasses = StyleClasses };
}
public static partial class UI
{
    public static CapturedMediaElement CapturedMedia(CaptureAttachment attachment, string id, string accessibleName = "Captured game context") => new(attachment, id, accessibleName);
}
