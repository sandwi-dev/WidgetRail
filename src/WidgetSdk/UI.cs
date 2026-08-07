using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

public static class UI
{
    public static StackElement Stack(string id, params WidgetElement[] children) =>
        new(id, CopyChildren(children));

    public static RowElement Row(string id, params WidgetElement[] children) =>
        new(id, CopyChildren(children));

    public static TextElement Text(string text, string id, string? accessibilityLabel = null) =>
        new(id, text, accessibilityLabel);

    public static ButtonElement Button(string label, string action, string id) =>
        new(id, label, action);

    public static ProgressElement Progress(double value, double maximum, string id, string? accessibilityLabel = null) =>
        new(id, value, maximum, accessibilityLabel);

    public static SpacerElement Spacer(string id) => new(id);

    public static ImageElement Image(
        string source,
        string id,
        string accessibilityLabel,
        ImageFit fit = ImageFit.Cover) => new(id, source, accessibilityLabel, fit);

    public static IconElement Icon(WidgetGlyph glyph, string id, string accessibilityLabel) =>
        new(id, glyph, accessibilityLabel);

    private static IReadOnlyList<WidgetElement> CopyChildren(WidgetElement[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Any(child => child is null))
            throw new ArgumentException("Children cannot contain null values.", nameof(children));
        return children.ToArray();
    }
}
