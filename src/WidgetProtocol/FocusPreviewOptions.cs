namespace WidgetRail.WidgetProtocol;

/// <summary>
/// Presents an existing focus fragment beside its current source instead of inline.
/// The host owns timing and selection; no action is dispatched on hover or focus.
/// </summary>
public sealed record FocusPreviewOptions(double Width = 360, double Height = 240,
    AnchorPlacement Placement = AnchorPlacement.Auto, bool OnFocus = true, bool OnHover = true,
    int DelayMilliseconds = 350)
{
    /// <summary>Admits one native media player in this interactive preview. Playback is muted before entry;
    /// withdrawal releases playback resources. Requires protocol 116.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowMedia { get; init; }
    /// <summary>Fit initial content height up to Height. Width stays stable. The host remeasures
    /// for viewport/text-size changes, not late updates to the same source. Requires protocol 93.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool SizeToContentHeight { get; init; }

    /// <summary>Enables interactive content. This host-owned button moves focus into the
    /// already visible fragment; it does not dispatch an action or replace its content.
    /// B returns to the source. Omit for a read-only preview. A explicitly makes primary
    /// activation (controller, keyboard, or source click) enter this preview instead of
    /// invoking the source action. Other entry buttons preserve primary activation.
    /// Primary entry requires protocol 115.</summary>
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
        if (value.EnterButton is { } button && button is not (ControllerButton.A or ControllerButton.X or ControllerButton.Y or ControllerButton.LeftStick or ControllerButton.RightStick))
            throw new ArgumentException("Choose A, X, Y, left stick or right stick to enter a preview; B returns to the source.", nameof(value));
        if (value.AllowMedia && value.EnterButton is null)
            throw new ArgumentException("Media previews require explicit native-control entry.", nameof(value));
    }
}
