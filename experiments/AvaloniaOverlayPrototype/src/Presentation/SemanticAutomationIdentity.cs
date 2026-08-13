using System.Text;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.AvaloniaPrototype.Presentation;

public static class SemanticAutomationIdentity
{
    public const string WidgetNodePrefix = "widget.node.";
    public const int MaximumWidgetAutomationIdLength = 768;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ForWidgetNode(string widgetId, ViewNode node)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        ArgumentNullException.ThrowIfNull(node);
        var automationId = string.Join('.',
            WidgetNodePrefix.TrimEnd('.'),
            Encode(widgetId),
            Encode(node.Id),
            Encode(node.CollectionItemKey ?? string.Empty));
        if (automationId.Length > MaximumWidgetAutomationIdLength)
            throw new InvalidOperationException("Encoded widget semantic identity exceeded its bounded contract.");
        return automationId;
    }

    private static string Encode(string value) => Convert.ToBase64String(StrictUtf8.GetBytes(value))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
