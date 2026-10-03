using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A reusable native browser with host navigation chrome and controller interaction.</summary>
public sealed record WebBrowserElement : WidgetElement
{
    internal WebBrowserElement(WebBrowserDocument document, BrowserInteractionMode interactionMode, string id) : base(RequireId(id))
    {
        ArgumentNullException.ThrowIfNull(document);
        document = document with { InteractionMode = interactionMode };
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

public static partial class UI
{
    /// <summary>Displays original provider attribution using an opaque host-issued reference.</summary>
    public static WebBrowserElement ProviderContent(ProviderDocumentReference reference, BrowserInteractionMode interactionMode,
        string id, string accessibleName = "Document content")
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!reference.IsWellFormed()) throw new ArgumentException("Invalid provider document.", nameof(reference));
        return new(new("provider." + reference.Id, 1, reference.Url, accessibleName) { ProviderDocument = reference }, interactionMode, id);
    }
}
