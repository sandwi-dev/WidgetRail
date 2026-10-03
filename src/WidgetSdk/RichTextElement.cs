using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A native paragraph of text and inline intent links. Links retain their own stable focus/action identities.</summary>
public sealed record RichTextElement : WidgetElement
{
    internal RichTextElement(string id, IReadOnlyList<WidgetElement> spans) : base(RequireId(id))
    {
        ArgumentNullException.ThrowIfNull(spans);
        if (spans.Count is < 1 or > 512 || spans.Any(span => span is not TextElement && span is not ButtonElement { Intent: not null }))
            throw new ArgumentException("Rich text accepts 1-512 UI.Text or UI.InlineLink spans.", nameof(spans));
        Spans = spans.ToArray();
        RequiredStyleClasses = ["wrail-rich-text"];
    }
    public IReadOnlyList<WidgetElement> Spans { get; }
    internal override ViewNode ToProtocolNode() => new()
    {
        Id = Id, Kind = ViewNodeKind.RichText, StyleClasses = StyleClasses,
        Children = Spans.Select(span => span.ToProtocolNode()).ToArray(),
    };
}

public static partial class UI
{
    /// <summary>Wraps text and short inline links in one native paragraph, without HTML or a WebView.</summary>
    public static RichTextElement RichText(string id, params WidgetElement[] spans) => new(id, spans);

    /// <summary>An inline link for UI.RichText. Activation uses the ordinary authorized intent path.</summary>
    public static ButtonElement InlineLink(string text, string id, WidgetIntentRequest intent, string? accessibilityLabel = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new ButtonElement(id, text, "inline.intent")
        { Intent = intent, AccessibilityLabel = accessibilityLabel ?? text, RequiredStyleClasses = ["wrail-inline-link"] };
    }
}
