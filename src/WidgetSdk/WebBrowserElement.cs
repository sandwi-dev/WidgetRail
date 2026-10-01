using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A reusable native browser with host navigation chrome and controller interaction.</summary>
public sealed record WebBrowserElement : WidgetElement
{
    internal WebBrowserElement(WebBrowserDocument document, string id) : base(RequireId(id))
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.IsWellFormed()) throw new ArgumentException("Invalid browser document.", nameof(document));
        Document = document;
    }
    public WebBrowserDocument Document { get; init; }
    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id, Kind = ViewNodeKind.WebBrowser, WebBrowser = Document,
        AccessibilityLabel = Document.AccessibleName, StyleClasses = StyleClasses,
    };
}
