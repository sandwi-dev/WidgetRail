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

    /// <summary>
    /// Creates a controller-ready two-state button. The visual and
    /// accessibility labels expose the current state, while IsSelected gives
    /// themes a semantic state instead of requiring label inspection.
    /// </summary>
    public static ButtonElement ToggleButton(string label, bool isOn, string action, string id)
    {
        ArgumentNullException.ThrowIfNull(label);
        var state = isOn ? "On" : "Off";
        return new ButtonElement(id, $"{label}: {state}", action)
        {
            AccessibilityLabel = $"{label}, {state}",
            IsSelected = isOn ? true : null,
            StyleClasses = ["setting-toggle"],
        };
    }

    /// <summary>
    /// Creates a semantic label/value/decrement/increment row from existing
    /// protocol primitives. Child IDs are stable suffixes of <paramref name="id"/>
    /// and the two buttons are explicitly linked for reliable controller focus.
    /// </summary>
    public static RowElement Stepper(
        string label,
        string value,
        string decrementAction,
        string incrementAction,
        string id,
        bool canDecrement = true,
        bool canIncrement = true)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);
        var decrementId = $"{id}.decrement";
        var incrementId = $"{id}.increment";
        var decrement = new ButtonElement(decrementId, "−", decrementAction)
        {
            AccessibilityLabel = $"Decrease {label}",
            IsDisabled = canDecrement ? null : true,
            FocusNeighbors = new FocusNeighbors(Right: incrementId),
            StyleClasses = ["setting-stepper-button", "setting-stepper-decrement"],
        };
        var increment = new ButtonElement(incrementId, "+", incrementAction)
        {
            AccessibilityLabel = $"Increase {label}",
            IsDisabled = canIncrement ? null : true,
            FocusNeighbors = new FocusNeighbors(Left: decrementId),
            StyleClasses = ["setting-stepper-button", "setting-stepper-increment"],
        };
        return new RowElement(id,
        [
            new TextElement($"{id}.label", label, label)
            {
                StyleClasses = ["setting-stepper-label"],
            },
            decrement,
            new TextElement($"{id}.value", value, $"{label}: {value}")
            {
                StyleClasses = ["setting-stepper-value"],
            },
            increment,
        ])
        {
            StyleClasses = ["setting-stepper"],
        };
    }

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
