using Avalonia;
using Avalonia.Platform;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.AvaloniaPrototype.Presentation;

public enum ShellProfile
{
    Compact,
    Standard,
    Wide,
}

public readonly record struct ShellGeometry(
    ShellProfile Profile,
    double WidthDip,
    double HeightDip,
    double GuideHeightDip,
    double TrayHeightDip);

public enum ContentResponsiveMode
{
    Compact,
    Standard,
    Wide,
}

/// <summary>Owns outer geometry independently from any admitted widget.</summary>
public static class ShellGeometryPolicy
{
    public static ShellGeometry Resolve(PixelRect workArea, double renderScaling)
    {
        var scaling = renderScaling > 0 && double.IsFinite(renderScaling) ? renderScaling : 1;
        var workWidth = workArea.Width / scaling;
        var workHeight = workArea.Height / scaling;
        var profile = workWidth < 1050 || workHeight < 620
            ? ShellProfile.Compact
            : workWidth >= 1700 && workHeight >= 900
                ? ShellProfile.Wide
                : ShellProfile.Standard;
        var horizontalInset = profile == ShellProfile.Compact ? 16 : 32;
        var verticalInset = profile == ShellProfile.Compact ? 16 : 28;
        var width = Math.Min(workWidth - horizontalInset, Math.Clamp(workWidth * 0.84, 978, 1440));
        var height = Math.Min(workHeight - verticalInset, Math.Clamp(workHeight * 0.78, 540, 810));
        return new ShellGeometry(
            profile,
            Math.Max(420, width),
            Math.Max(340, height),
            profile == ShellProfile.Compact ? 26 : 32,
            profile == ShellProfile.Compact ? 62 : 76);
    }

    public static ContentResponsiveMode ResolveContentMode(
        WidgetSurfaceHints? hints,
        Size contentViewport)
    {
        if (hints?.Mode == WidgetSurfaceMode.Compact) return ContentResponsiveMode.Compact;
        if (hints?.Mode == WidgetSurfaceMode.Wide) return ContentResponsiveMode.Wide;
        if (hints?.Mode == WidgetSurfaceMode.Standard) return ContentResponsiveMode.Standard;
        if (contentViewport.Width < 760 || contentViewport.Height < 280)
            return ContentResponsiveMode.Compact;
        return contentViewport.Width >= 1240 && contentViewport.Height >= 620
            ? ContentResponsiveMode.Wide
            : ContentResponsiveMode.Standard;
    }
}
