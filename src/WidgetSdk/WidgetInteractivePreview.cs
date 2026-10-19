using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>The interactive state of a source-owned rich preview. Open it from the source's
/// configured preview shortcut, then handle DismissActionId by returning the ordinary view.
/// The host owns bounded placement, outside-click/Escape dismissal and B-to-source focus return.</summary>
public sealed record WidgetInteractivePreview(string Id, string Title, WidgetElement Content,
    string InitialFocusId, string DismissActionId, PresentationAnchor Anchor)
{
    public double Width { get; init; } = 420;
    public double Height { get; init; } = 420;
    public AnchorPlacement Placement { get; init; } = AnchorPlacement.Auto;
    public bool SizeToContentHeight { get; init; } = true;
    public bool ScrollContent { get; init; } = true;
}
