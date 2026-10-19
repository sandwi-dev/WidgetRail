using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// A focus-associated presentation consumer. The host lays out
/// exactly one admitted presentation fragment followed by the ordinary content
/// subtree. Inline fragments remain passive; an anchored preview can explicitly
/// opt into host-managed interaction through Preview.EnterButton.
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
    /// <summary>Retains the last valid associated fragment when focus leaves it. Defaults to false.</summary>
    public bool RetainLastPresentation { get; init; }

    /// <summary>Optional anchored preview; omission preserves immediate passive inline presentation.</summary>
    public FocusPreviewOptions? Preview { get; init; }
    /// <summary>Optional source for keys resolved only after host focus/hover settles.</summary>
    public IndexedCollectionDescriptor? PreviewSource { get; init; }

    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id,
        Kind = ViewNodeKind.FocusPresentationSurface,
        DefaultFocusPresentation = DefaultPresentation.ToProtocolNode(),
        FocusPreview = Preview,
        FocusPreviewSource = PreviewSource,
        RetainLastPresentation = RetainLastPresentation ? true : null,
        StyleClasses = StyleClasses,
        Children = [Content.ToProtocolNode()],
    };
}

internal sealed record DeferredFocusPresentationElement : WidgetElement
{
    private readonly WidgetElement child;
    private readonly string key;
    internal DeferredFocusPresentationElement(WidgetElement child,string key):base(child.Id)
    { StableIdentifier.Validate(key,nameof(key));this.child=child;this.key=key;RequiredStyleClasses=child.RequiredStyleClasses;AuthorStyleClasses=child.AuthorStyleClasses; }
    internal override ViewNode ToProtocolNode()=>child.ToProtocolNode() with{FocusPreviewKey=key,StyleClasses=StyleClasses};
}
