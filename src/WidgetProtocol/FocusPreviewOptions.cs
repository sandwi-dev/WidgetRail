namespace WidgetRail.WidgetProtocol;

/// <summary>
/// Presents an existing focus fragment beside its current source instead of inline.
/// The host owns timing and selection; no action is dispatched on hover or focus.
/// </summary>
public sealed record FocusPreviewOptions(double Width = 360, double Height = 240,
    AnchorPlacement Placement = AnchorPlacement.Auto, bool OnFocus = true, bool OnHover = true,
    int DelayMilliseconds = 350)
{
    /// <summary>Fit initial content height up to Height. Width stays stable. The host remeasures
    /// for viewport/text-size changes, not late updates to the same source. Requires protocol 93.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool SizeToContentHeight { get; init; }

    /// <summary>Optional source shortcut used to enter this preview. The source must declare a
    /// pressed shortcut for this button. Its label supplies the host's enter hint; A keeps its normal action.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ControllerButton? EnterButton { get; init; }
}

public static class FocusPreviewContract
{
    public static void Validate(FocusPreviewOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!double.IsFinite(value.Width) || value.Width is < 160 or > 960 ||
            !double.IsFinite(value.Height) || value.Height is < 96 or > 800 ||
            !Enum.IsDefined(value.Placement) || (!value.OnFocus && !value.OnHover) ||
            value.DelayMilliseconds is < 0 or > 1500)
            throw new ArgumentException("Choose bounded preview dimensions, delay, and at least one activation mode.", nameof(value));
        if (value.EnterButton is { } button && button is not (ControllerButton.X or ControllerButton.Y or ControllerButton.LeftStick or ControllerButton.RightStick))
            throw new ArgumentException("Choose X, Y, left stick or right stick to enter a preview; A and B retain their normal roles.", nameof(value));
    }
}
