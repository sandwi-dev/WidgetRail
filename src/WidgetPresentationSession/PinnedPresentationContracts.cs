using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>A genuine frame's pinned view. It is not an ordinary input frame or a selection grant.</summary>
public sealed class WidgetPinnedProjection
{
    public const string FullWidgetLayoutId = PinnedSurfaceContract.FullWidgetLayoutId;
    internal WidgetPinnedProjection(WidgetPresentationSession owner, WidgetPresentationFrame frame, string layoutId,
        object epoch, ViewSnapshot snapshot, IReadOnlyDictionary<string, BridgeNodeRenderStyles> styles, bool inputSupported)
    { Owner = owner; Frame = frame; LayoutId = layoutId; Epoch = epoch; Snapshot = snapshot; RenderStyles = styles; SupportsOrdinaryInput = inputSupported; }
    internal WidgetPresentationSession Owner { get; }
    internal object Epoch { get; }
    public WidgetPresentationFrame Frame { get; }
    public string LayoutId { get; }
    public ViewSnapshot Snapshot { get; }
    public IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles { get; }
    /// <summary>Legacy sizing-only layouts render their parent but have no pinned controller route.</summary>
    public bool SupportsOrdinaryInput { get; }
}

/// <summary>One host selection lifetime. Deselecting and selecting the same layout creates a new lifetime.</summary>
public sealed class WidgetPinnedSelection
{
    internal WidgetPinnedSelection(WidgetPresentationSession owner, WidgetPinnedProjection projection)
    { Owner = owner; Authority = projection.Frame.Authority; LayoutId = projection.LayoutId; Epoch = projection.Epoch; }
    internal WidgetPresentationSession Owner { get; }
    internal WidgetPresentationAuthority Authority { get; }
    internal object Epoch { get; }
    internal bool Active { get; set; }
    public string WidgetId => Authority.WidgetId;
    public string LayoutId { get; }
    /// <summary>Whether the worker acknowledged package-authored selection demand; this is not permission.</summary>
    public bool DemandAcknowledged { get; internal set; }
    public bool IsCurrent => Owner.IsPinnedSelectionCurrent(this);
}
