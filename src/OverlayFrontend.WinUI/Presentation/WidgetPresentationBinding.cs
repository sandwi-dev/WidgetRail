using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>A render view plus its genuine authority. Projection never creates an input frame.</summary>
internal sealed class WidgetPresentationBinding
{
    private WidgetPresentationBinding(WidgetPresentationFrame frame, WidgetPinnedProjection? projection,
        WidgetPinnedSelection? selection, IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles = null)
    {
        Frame = frame; Projection = projection; Selection = selection;
        RenderStyles = styles ?? projection?.RenderStyles ?? frame.RenderStyles;
    }
    internal WidgetPresentationFrame Frame { get; }
    internal WidgetPinnedProjection? Projection { get; }
    internal WidgetPinnedSelection? Selection { get; }
    internal ViewSnapshot View => Projection?.Snapshot ?? Frame.Snapshot;
    internal string Scope => View.ActiveInputScopeId;
    internal string? PinnedLayoutId => Projection?.LayoutId;
    internal IReadOnlyDictionary<string, BridgeNodeRenderStyles> RenderStyles { get; }
    internal bool IsCurrent => Selection?.IsCurrent != false;
    internal static WidgetPresentationBinding ForMain(WidgetPresentationFrame frame) => new(frame, null, null);
    internal static WidgetPresentationBinding ForPinned(PresentationSession session, WidgetPinnedSelection selection, WidgetPinnedProjection projection)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(projection);
        // Session provenance and layout epoch must survive the UI dispatcher hop.
        _ = session.ResolvePinnedProjection(projection.Frame, projection.LayoutId);
        if (!session.IsPinnedSelectionCurrent(selection) || selection.WidgetId != projection.Frame.Authority.WidgetId || selection.LayoutId != projection.LayoutId)
            throw new WidgetPresentationSessionException("pinned_input_stale", "The pinned presentation selection retired.");
        return new(projection.Frame, projection, selection);
    }
    internal WidgetPresentationBinding WithStyles(IReadOnlyDictionary<string, BridgeNodeRenderStyles> styles) =>
        new(Frame, Projection, Selection, styles);
    internal bool SameSurface(WidgetPresentationBinding? other) => other is not null &&
        SameOwner(Frame.Authority, other.Frame.Authority) &&
        PinnedLayoutId == other.PinnedLayoutId && ReferenceEquals(Selection, other.Selection);
    private static bool SameOwner(WidgetPresentationAuthority first, WidgetPresentationAuthority second) =>
        first.WidgetId == second.WidgetId && first.WidgetInstanceId == second.WidgetInstanceId &&
        first.RuntimeGeneration == second.RuntimeGeneration && first.PresentationGeneration == second.PresentationGeneration &&
        first.SessionGeneration == second.SessionGeneration;
    internal bool SameInput(WidgetPresentationBinding? other) => IsCurrent && other?.IsCurrent == true && SameSurface(other) && Scope == other.Scope;
}
