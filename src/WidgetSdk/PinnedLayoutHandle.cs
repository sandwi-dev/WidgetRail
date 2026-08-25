using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// Owns one package-authored pinned-layout identity and its current
/// widget-instance selection demand. The handle grants no host or window authority.
/// </summary>
public sealed class PinnedLayoutHandle
{
    private static readonly CancellationToken InactiveToken = new(canceled: true);
    private readonly object _gate = new();
    private CancellationToken _selectionToken = InactiveToken;
    private bool _isSelected;

    internal PinnedLayoutHandle(
        string id,
        string name,
        WidgetSurfaceHints surface,
        string? initialFocusId,
        string? activeInputScopeId)
    {
        Id = id;
        Name = name;
        Surface = surface;
        InitialFocusId = initialFocusId;
        ActiveInputScopeId = activeInputScopeId;
    }

    /// <summary>The stable package-authored layout identifier.</summary>
    public string Id { get; }

    /// <summary>The stable visible layout name.</summary>
    public string Name { get; }

    /// <summary>The immutable bounded surface hints registered for this layout.</summary>
    public WidgetSurfaceHints Surface { get; }

    /// <summary>The stable initial focus identity, when the layout has one.</summary>
    public string? InitialFocusId { get; }

    /// <summary>The stable input-scope identity, when the layout has one.</summary>
    public string? ActiveInputScopeId { get; }

    /// <summary>Whether this layout is the current effective package selection.</summary>
    public bool IsSelected
    {
        get { lock (_gate) return _isSelected; }
    }

    /// <summary>
    /// Canceled when this selection is revoked, replaced, or the widget is destroyed.
    /// A later selection receives a distinct token.
    /// </summary>
    public CancellationToken SelectionCancellationToken
    {
        get { lock (_gate) return _selectionToken; }
    }

    /// <summary>
    /// Presents this handle's current immutable root with its registered metadata.
    /// Call this from <see cref="Widget.Render"/> and include the result in
    /// <see cref="WidgetView.PinnedLayouts"/>.
    /// </summary>
    public PinnedPresentationLayout Present(WidgetElement? root) =>
        WidgetView.PinnedLayout(
            Id, Name, Surface, root, InitialFocusId, ActiveInputScopeId);

    /// <summary>
    /// Presents this handle's current immutable root with a focus target chosen
    /// for this exact presentation. Stable identity, name, surface, and input
    /// scope remain those registered by the handle.
    /// </summary>
    public PinnedPresentationLayout Present(
        WidgetElement? root,
        string? initialFocusId) =>
        WidgetView.PinnedLayout(
            Id, Name, Surface, root, initialFocusId, ActiveInputScopeId);

    internal void SetSelection(bool selected, CancellationToken token)
    {
        lock (_gate)
        {
            _isSelected = selected;
            _selectionToken = selected ? token : InactiveToken;
        }
    }
}
