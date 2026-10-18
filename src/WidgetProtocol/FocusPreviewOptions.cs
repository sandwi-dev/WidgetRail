namespace WidgetRail.WidgetProtocol;

/// <summary>
/// Presents an existing focus fragment beside its current source instead of inline.
/// The host owns timing and selection; no action is dispatched on hover or focus.
/// </summary>
public sealed record FocusPreviewOptions(double Width = 360, double Height = 240,
    AnchorPlacement Placement = AnchorPlacement.Auto, bool OnFocus = true, bool OnHover = true,
    int DelayMilliseconds = 350);

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
    }
}
