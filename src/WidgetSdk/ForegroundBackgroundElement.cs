using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

internal sealed record ForegroundBackgroundElement : WidgetElement
{
    private readonly WidgetElement child;
    private readonly string key;

    internal ForegroundBackgroundElement(WidgetElement child, string key) : base(child.Id)
    {
        if (key is not { Length: 64 } || !key.All(char.IsAsciiHexDigitLower))
            throw new ArgumentException("Expected a lowercase SHA-256 process-instance key.", nameof(key));
        this.child = child;
        this.key = key;
        RequiredStyleClasses = child.RequiredStyleClasses;
        AuthorStyleClasses = child.AuthorStyleClasses;
    }

    internal override ViewNode ToProtocolNode() => child.ToProtocolNode() with
    { ForegroundBackgroundProcessKey = key, StyleClasses = StyleClasses };
}
