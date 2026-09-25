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
    /// <summary>Shows the ordinary scroll indicator and reserves its gutter. Defaults to true.</summary>
    public bool ShowScrollbar { get; init; } = true;
}

internal sealed record ModalLayerElement : WidgetElement
{
    internal ModalLayerElement(string id, WidgetElement background, string backgroundScope,
        WidgetElement dialog) : base(id)
    {
        Background = background;
        BackgroundScope = backgroundScope;
        Dialog = dialog;
        RequiredStyleClasses = ["wrail-modal-layer"];
    }

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
            StyleClasses = StyleClasses,
            Children = [background, Dialog.ToProtocolNode()],
        };
    }
}
