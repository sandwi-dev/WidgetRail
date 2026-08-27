using WidgetRail.WidgetProtocol;
using System.Globalization;

namespace WidgetRail.WidgetSdk;

public static partial class UI
{
    /// <summary>
    /// Applies host-resolved responsive visibility without adding a layout
    /// container. Prefer the fluent <see cref="WidgetElement.VisibleWhen"/>
    /// form when it reads more naturally.
    /// </summary>
    public static WidgetElement ResponsiveBranch(
        ResponsiveVisibility visibility,
        WidgetElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return child.VisibleWhen(visibility);
    }

    public static StackElement Stack(string id, params WidgetElement[] children) =>
        new(id, CopyChildren(children));

    public static RowElement Row(string id, params WidgetElement[] children) =>
        new(id, CopyChildren(children));

    public static ScrollElement Scroll(
        string id,
        ScrollAxis axis,
        params WidgetElement[] children) =>
        new(id, axis, CopyChildren(children));

    public static ScrollElement VerticalScroll(string id, params WidgetElement[] children) =>
        Scroll(id, ScrollAxis.Vertical, children);

    public static ScrollElement HorizontalScroll(string id, params WidgetElement[] children) =>
        Scroll(id, ScrollAxis.Horizontal, children);

    public static TextElement Text(string text, string id, string? accessibilityLabel = null) =>
        new(id, text, accessibilityLabel);

    public static ButtonElement Button(string label, string action, string id) =>
        new(id, label, action);

    /// <summary>Creates a host-owned bounded text-entry trigger.</summary>
    public static TextEntryElement TextEntry(
        string value,
        string placeholder,
        string action,
        string id,
        int maximumLength = ProtocolConstants.MaximumTextEntryLength) =>
        new(id, value, placeholder, maximumLength, action, TextEntryInputKind.Ordinary);

    /// <summary>
    /// Creates a host-owned sensitive text-entry trigger. The authored view
    /// always carries an empty value; only the final bounded commit reaches
    /// the widget action callback.
    /// </summary>
    public static TextEntryElement SensitiveTextEntry(
        string placeholder,
        string action,
        string id,
        int maximumLength = ProtocolConstants.MaximumTextEntryLength) =>
        new(id, string.Empty, placeholder, maximumLength, action,
            TextEntryInputKind.Sensitive);

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
        var decrementId = StableIdentifier.Child(id, "decrement");
        var incrementId = StableIdentifier.Child(id, "increment");
        var labelId = StableIdentifier.Child(id, "label");
        var valueId = StableIdentifier.Child(id, "value");
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
            new TextElement(labelId, label, label)
            {
                StyleClasses = ["setting-stepper-label"],
            },
            decrement,
            new TextElement(valueId, value, $"{label}: {value}")
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

    /// <summary>
    /// Creates a controller-native value control. The host publishes a
    /// quantized absolute target through the value-changed action, and the SDK
    /// coalesces contiguous pending changes latest-wins.
    /// </summary>
    public static SliderElement Slider(
        double value,
        double minimum,
        double maximum,
        double step,
        string valueChangedAction,
        string id,
        string accessibilityLabel,
        string? accessibilityValue = null,
        string? activationAction = null) => new(
            id,
            value,
            minimum,
            maximum,
            step,
            valueChangedAction,
            accessibilityLabel,
            accessibilityValue ?? value.ToString("0.###", CultureInfo.InvariantCulture),
            activationAction);

    public static SpacerElement Spacer(string id) => new(id);

    /// <summary>
    /// Places one declared embedded-media surface inside the ordinary native
    /// declarative layout. The returned element grants no browser, DOM, HWND,
    /// focus, or input authority.
    /// </summary>
    public static MediaViewportElement MediaViewport(
        EmbeddedMediaSurface surface,
        string id) => new(id, surface);

    public static ImageElement Image(
        string source,
        string id,
        string accessibilityLabel,
        ImageFit fit = ImageFit.Cover) => new(id, source, accessibilityLabel, fit);

    /// <summary>
    /// Creates an image from bounded PNG pixels supplied by a trusted platform
    /// service. The host validates the PNG again before decoding it; use this
    /// for broker-projected icons rather than exposing native file paths.
    /// </summary>
    public static ImageElement InlinePngImage(
        string pngBase64,
        string id,
        string accessibilityLabel,
        ImageFit fit = ImageFit.Contain)
    {
        var source = CanonicalInlinePngSource(pngBase64, nameof(pngBase64));
        return new ImageElement(id, source, accessibilityLabel, fit);
    }

    /// <summary>
    /// Creates lazy artwork identified only by a bounded opaque host handle.
    /// The value is not a URL or path and grants no fetch or decode authority.
    /// </summary>
    public static ImageElement Artwork(
        WidgetArtworkHandle handle,
        string id,
        string accessibilityLabel,
        ImageFit fit = ImageFit.Cover)
    {
        StableIdentifier.Validate(handle.Value, nameof(handle));
        return new(id, string.Empty, accessibilityLabel, fit)
        {
            ArtworkHandle = handle.Value,
        };
    }

    internal static string CanonicalInlinePngSource(string pngBase64, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pngBase64, parameterName);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(pngBase64);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Inline PNG data must be canonical base64.",
                parameterName, exception);
        }
        if (bytes.Length > ProtocolConstants.MaximumInlinePngBytes ||
            !pngBase64.Equals(Convert.ToBase64String(bytes), StringComparison.Ordinal))
            throw new ArgumentException("Inline PNG data exceeds its bound or is not canonical.",
                parameterName);
        return "data:image/png;base64," + pngBase64;
    }

    public static IconElement Icon(WidgetGlyph glyph, string id, string accessibilityLabel) =>
        new(id, glyph, accessibilityLabel);

    /// <summary>
    /// Creates a non-focusable indeterminate activity indicator. The label is
    /// announced by accessibility services but is not rendered as visible text.
    /// </summary>
    public static LoadingIndicatorElement LoadingIndicator(
        string id,
        string accessibilityLabel = "Loading",
        LoadingIndicatorSize size = LoadingIndicatorSize.Standard) =>
        new(id, accessibilityLabel, size);

    private static IReadOnlyList<WidgetElement> CopyChildren(WidgetElement[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Any(child => child is null))
            throw new ArgumentException("Children cannot contain null values.", nameof(children));
        return children.ToArray();
    }
}
