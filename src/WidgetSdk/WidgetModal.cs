using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A single widget-owned dialog above an unchanged background view.</summary>
public sealed record WidgetModal(
    string Id,
    string Title,
    WidgetElement Content,
    string InitialFocusId,
    string DismissActionId)
{
    /// <summary>
    /// Optional content to replace the header's Close button. The title and B-to-dismiss
    /// shortcut remain. Use controller hints or ordinary controls with stable IDs.
    /// </summary>
    public WidgetElement? HeaderActions { get; init; }

    /// <summary>Optional widget-local popover placement. Missing, stale or off-screen anchors use a centered panel.</summary>
    public AnchoredPresentation? AnchoredPresentation { get; init; }

    /// <summary>
    /// Wraps the entire content in a vertical scroll viewport. Set false when
    /// the content supplies its own bounded layout and scroll regions.
    /// </summary>
    public bool ScrollContent { get; init; } = true;

    /// <summary>Shows the ordinary scroll indicator and reserves its gutter. Defaults to true.</summary>
    public bool ShowScrollbar { get; init; } = true;
}

internal sealed record ModalLayerElement : WidgetElement
{
    internal ModalLayerElement(string id, WidgetElement background, string backgroundScope,
        WidgetElement dialog, AnchoredPresentation? anchoredPresentation = null) : base(id)
    {
        Background = background;
        BackgroundScope = backgroundScope;
        Dialog = dialog;
        AnchoredPresentation = anchoredPresentation;
        RequiredStyleClasses = ["wrail-modal-layer"];
    }

    private AnchoredPresentation? AnchoredPresentation { get; }
    private WidgetElement Background { get; }
    private string BackgroundScope { get; }
    private WidgetElement Dialog { get; }

    internal override ViewNode ToProtocolNode()
    {
        var background = Background.ToProtocolNode();
        background = background.Kind is ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Scroll or ViewNodeKind.Grid
            ? background with { InputScopeId = BackgroundScope }
            : new ViewNode
            {
                Id = StableIdentifier.Child(Id, "background"),
                Kind = ViewNodeKind.Stack,
                InputScopeId = BackgroundScope,
                Children = [background],
            };
        return new()
        {
            Id = Id,
            Kind = ViewNodeKind.ModalLayer,
            AnchoredPresentation = AnchoredPresentation,
            StyleClasses = StyleClasses,
            Children = [background, Dialog.ToProtocolNode()],
        };
    }
}
