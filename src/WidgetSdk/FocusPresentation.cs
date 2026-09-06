using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// A non-interactive focus-associated presentation consumer. The host lays out
/// exactly one admitted presentation fragment followed by the ordinary content
/// subtree. Only the content owns focus, actions, input, and navigation.
/// </summary>
public sealed record FocusPresentationSurfaceElement : WidgetElement
{
    internal FocusPresentationSurfaceElement(
        string id,
        WidgetElement content,
        WidgetElement defaultPresentation) : base(RequireId(id))
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        DefaultPresentation = defaultPresentation ??
            throw new ArgumentNullException(nameof(defaultPresentation));
        RequiredStyleClasses = ["wrail-focus-presentation-surface"];
    }

    public WidgetElement Content { get; init; }
    public WidgetElement DefaultPresentation { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.FocusPresentationSurface,
        DefaultFocusPresentation = DefaultPresentation.ToProtocolNode(),
        StyleClasses = StyleClasses,
        Children = [Content.ToProtocolNode()],
    };
}
