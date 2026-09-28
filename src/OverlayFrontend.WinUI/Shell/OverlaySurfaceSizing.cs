using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal readonly record struct SurfaceExtent(double Width, double Height);

/// <summary>Host window policy in design DIPs; WinUI retains all child measurement/layout.</summary>
internal static class OverlaySurfaceSizing
{
    internal static SurfaceExtent Resolve(WidgetSurfaceHints? hints, SurfaceExtent available,
        SurfaceExtent chrome, double textScale, Func<SurfaceExtent, SurfaceExtent?>? measure = null)
    {
        if (!Valid(available) || !Valid(chrome)) throw new ArgumentOutOfRangeException(nameof(available));
        var maximum = new SurfaceExtent(Math.Max(0, available.Width - chrome.Width), Math.Max(0, available.Height - chrome.Height));
        var preferred = hints?.Mode switch
        {
            WidgetSurfaceMode.Compact => new SurfaceExtent(560, 420),
            WidgetSurfaceMode.Wide => new SurfaceExtent(1120, 620),
            _ => new SurfaceExtent(880, 520),
        };
        if (hints is null) return new(Math.Min(1180, available.Width), Math.Min(700, available.Height));
        if (!Enum.IsDefined(hints.Mode) || !Enum.IsDefined(hints.WidthMode) || !Enum.IsDefined(hints.HeightMode))
            throw new ArgumentException("Unsupported widget surface policy.", nameof(hints));
        if (Pair(hints.PreferredWidth, hints.PreferredHeight)) preferred = new(hints.PreferredWidth!.Value, hints.PreferredHeight!.Value);
        var minimum = new SurfaceExtent(ProtocolConstants.MinimumSurfaceWidth, ProtocolConstants.MinimumSurfaceHeight);
        if (Pair(hints.MinimumWidth, hints.MinimumHeight) && hints.MinimumWidth <= preferred.Width && hints.MinimumHeight <= preferred.Height)
            minimum = new(hints.MinimumWidth!.Value, hints.MinimumHeight!.Value);
        preferred = new(Math.Max(preferred.Width, minimum.Width), Math.Max(preferred.Height, minimum.Height));
        var extraText = Math.Max(0, (double.IsFinite(textScale) ? Math.Clamp(textScale, .85, 1.5) : 1) - 1);
        preferred = Expand(preferred);
        minimum = Expand(minimum);
        SurfaceExtent? measured = null;
        if ((hints.WidthMode == WidgetSurfaceAxisMode.Content || hints.HeightMode == WidgetSurfaceAxisMode.Content) &&
            maximum.Width > 0 && maximum.Height > 0 && measure is not null)
        {
            var constraint = new SurfaceExtent(hints.WidthMode == WidgetSurfaceAxisMode.FillAvailable ? maximum.Width : Math.Min(preferred.Width, maximum.Width),
                hints.HeightMode == WidgetSurfaceAxisMode.FillAvailable ? maximum.Height : Math.Min(preferred.Height, maximum.Height));
            var value = measure(constraint);
            if (value is { } valid && Valid(valid)) measured = valid;
        }
        return new(Math.Min(available.Width, Axis(hints.WidthMode, preferred.Width, minimum.Width, maximum.Width, measured?.Width) + chrome.Width),
            Math.Min(available.Height, Axis(hints.HeightMode, preferred.Height, minimum.Height, maximum.Height, measured?.Height) + chrome.Height));

        SurfaceExtent Expand(SurfaceExtent extent) => new(
            Math.Clamp(extent.Width * (1 + extraText * .5), ProtocolConstants.MinimumSurfaceWidth, ProtocolConstants.MaximumSurfaceWidth),
            Math.Clamp(extent.Height * (1 + extraText), ProtocolConstants.MinimumSurfaceHeight, ProtocolConstants.MaximumSurfaceHeight));
    }

    internal static DisplayScaleSettings Scale(AppearanceSettings appearance, string? displayId) => DisplayScalePolicy.Resolve(appearance, displayId);
    private static bool Valid(SurfaceExtent value) => double.IsFinite(value.Width) && double.IsFinite(value.Height) && value.Width >= 0 && value.Height >= 0;
    private static bool Pair(double? width, double? height) => width is >= ProtocolConstants.MinimumSurfaceWidth and <= ProtocolConstants.MaximumSurfaceWidth &&
        height is >= ProtocolConstants.MinimumSurfaceHeight and <= ProtocolConstants.MaximumSurfaceHeight;
    private static double Axis(WidgetSurfaceAxisMode mode, double preferred, double minimum, double available, double? content) => mode switch
    {
        WidgetSurfaceAxisMode.FillAvailable => available,
        WidgetSurfaceAxisMode.Content => Math.Clamp(content ?? preferred, Math.Min(minimum, available), Math.Min(preferred, available)),
        _ => Math.Min(preferred, available),
    };
}
