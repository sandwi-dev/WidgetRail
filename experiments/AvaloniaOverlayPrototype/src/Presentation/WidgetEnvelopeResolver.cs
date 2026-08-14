using Avalonia;
using Avalonia.Platform;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.AvaloniaPrototype.Presentation;

public enum ContentResponsiveMode
{
    Compact,
    Standard,
    Wide,
}

public readonly record struct EnvelopeInsets(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public static EnvelopeInsets PlatformPlacement { get; } = new(24, 24, 24, 32);
}

public readonly record struct WidgetEnvelopeConstraints(
    PixelRect WorkArea,
    double RenderScaling,
    double AccessibilityScale = 1,
    EnvelopeInsets SafeInsets = default);

public readonly record struct WidgetEnvelopeResolution(
    Size AuthoredPreferredContent,
    Size AuthoredMinimumContent,
    Size AdmittedContent,
    Size Window,
    Rect ContentBounds,
    Rect GuideBounds,
    Rect TrayBounds,
    Size LogicalWorkArea,
    ContentResponsiveMode ResponsiveMode,
    bool PreferredPairAdmitted,
    bool MinimumPairSatisfied,
    bool WorkAreaClamped,
    bool UsesFullWorkAreaBackdrop);

/// <summary>
/// Resolves one validated semantic view envelope against host-owned physical constraints.
/// It has no widget identity, top-level size profiles, or replacement compact/standard/wide table.
/// </summary>
public static class WidgetEnvelopeResolver
{
    public const double TrayWidthDip = 920;
    public const double TrayHeightDip = 76;
    public const double GuideWidthDip = 620;
    public const double GuideHeightDip = 32;
    public const double ContentGuideGapDip = 8;
    public const double GuideTrayGapDip = 4;
    public const double ChromeHeightDip = GuideHeightDip + TrayHeightDip +
        ContentGuideGapDip + GuideTrayGapDip;

    public static WidgetEnvelopeResolution Resolve(
        WidgetSurfaceHints? hints,
        WidgetEnvelopeConstraints constraints,
        Size? intrinsicContent = null)
    {
        var renderScaling = FinitePositive(constraints.RenderScaling, 1);
        var accessibilityScale = Math.Clamp(FinitePositive(constraints.AccessibilityScale, 1), 0.85, 2);
        var workArea = new Size(
            Math.Max(1, constraints.WorkArea.Width / renderScaling),
            Math.Max(1, constraints.WorkArea.Height / renderScaling));
        var safeInsets = constraints.SafeInsets == default
            ? EnvelopeInsets.PlatformPlacement
            : constraints.SafeInsets;
        var maximumWindow = new Size(
            Math.Max(1, workArea.Width - safeInsets.Left - safeInsets.Right),
            Math.Max(1, workArea.Height - safeInsets.Top - safeInsets.Bottom));
        var maximumContent = new Size(
            maximumWindow.Width,
            Math.Max(1, maximumWindow.Height - ChromeHeightDip));

        var preferred = PairOrFallback(
            hints?.PreferredWidth,
            hints?.PreferredHeight,
            intrinsicContent,
            new Size(ProtocolConstants.MinimumSurfaceWidth, ProtocolConstants.MinimumSurfaceHeight));
        var minimum = PairOrFallback(
            hints?.MinimumWidth,
            hints?.MinimumHeight,
            null,
            new Size(
                Math.Min(preferred.Width, ProtocolConstants.MinimumSurfaceWidth),
                Math.Min(preferred.Height, ProtocolConstants.MinimumSurfaceHeight)));

        var widthScale = 1 + Math.Max(0, accessibilityScale - 1) * 0.5;
        var scaledPreferred = new Size(preferred.Width * widthScale, preferred.Height * accessibilityScale);
        var scaledMinimum = new Size(minimum.Width * widthScale, minimum.Height * accessibilityScale);
        var admitted = new Size(
            Math.Max(1, Math.Min(scaledPreferred.Width, maximumContent.Width)),
            Math.Max(1, Math.Min(scaledPreferred.Height, maximumContent.Height)));
        var minimumSatisfied = admitted.Width + 0.01 >= scaledMinimum.Width &&
            admitted.Height + 0.01 >= scaledMinimum.Height;
        var chromeWidth = Math.Min(TrayWidthDip, maximumWindow.Width);
        var guideWidth = Math.Min(GuideWidthDip, chromeWidth);
        var window = new Size(
            Math.Min(maximumWindow.Width, Math.Max(admitted.Width, chromeWidth)),
            Math.Min(maximumWindow.Height, admitted.Height + ChromeHeightDip));
        var contentBounds = new Rect(
            CenteredLocalOffset(admitted.Width, window.Width, workArea.Width, renderScaling),
            0,
            admitted.Width,
            admitted.Height);
        var guideBounds = new Rect(
            CenteredLocalOffset(guideWidth, window.Width, workArea.Width, renderScaling),
            admitted.Height + ContentGuideGapDip,
            guideWidth,
            GuideHeightDip);
        var trayBounds = new Rect(
            CenteredLocalOffset(chromeWidth, window.Width, workArea.Width, renderScaling),
            guideBounds.Bottom + GuideTrayGapDip,
            chromeWidth,
            TrayHeightDip);
        var clamped = admitted.Width + 0.01 < scaledPreferred.Width ||
            admitted.Height + 0.01 < scaledPreferred.Height;
        var usesFullBackdrop = window.Width + 0.5 >= workArea.Width &&
            window.Height + 0.5 >= workArea.Height;
        return new WidgetEnvelopeResolution(
            preferred,
            minimum,
            admitted,
            window,
            contentBounds,
            guideBounds,
            trayBounds,
            workArea,
            ResolveContentMode(hints, preferred, minimum, admitted, minimumSatisfied),
            !clamped,
            minimumSatisfied,
            clamped,
            usesFullBackdrop);
    }

    internal static WidgetEnvelopeResolution ResolveAdmittedContent(
        Size content,
        WidgetSurfaceHints? authoredHints = null)
    {
        var safeContent = new Size(Math.Max(1, content.Width), Math.Max(1, content.Height));
        var fixtureHints = new WidgetSurfaceHints
        {
            Mode = authoredHints?.Mode ?? WidgetSurfaceMode.Adaptive,
            PreferredWidth = safeContent.Width,
            PreferredHeight = safeContent.Height,
            MinimumWidth = Math.Min(safeContent.Width, ProtocolConstants.MinimumSurfaceWidth),
            MinimumHeight = Math.Min(safeContent.Height, ProtocolConstants.MinimumSurfaceHeight),
        };
        var workArea = new PixelRect(
            0,
            0,
            (int)Math.Ceiling(Math.Max(safeContent.Width, TrayWidthDip) + 48),
            (int)Math.Ceiling(safeContent.Height + ChromeHeightDip + 56));
        var resolved = Resolve(fixtureHints, new WidgetEnvelopeConstraints(workArea, 1));
        var preferred = PairOrFallback(
            authoredHints?.PreferredWidth,
            authoredHints?.PreferredHeight,
            null,
            safeContent);
        var minimum = PairOrFallback(
            authoredHints?.MinimumWidth,
            authoredHints?.MinimumHeight,
            null,
            new Size(
                Math.Min(preferred.Width, ProtocolConstants.MinimumSurfaceWidth),
                Math.Min(preferred.Height, ProtocolConstants.MinimumSurfaceHeight)));
        var minimumSatisfied = safeContent.Width + 0.01 >= minimum.Width &&
            safeContent.Height + 0.01 >= minimum.Height;
        return resolved with
        {
            AuthoredPreferredContent = preferred,
            AuthoredMinimumContent = minimum,
            ResponsiveMode = ResolveContentMode(
                authoredHints,
                preferred,
                minimum,
                safeContent,
                minimumSatisfied),
            PreferredPairAdmitted = Math.Abs(safeContent.Width - preferred.Width) < 0.01 &&
                Math.Abs(safeContent.Height - preferred.Height) < 0.01,
            MinimumPairSatisfied = minimumSatisfied,
        };
    }

    private static ContentResponsiveMode ResolveContentMode(
        WidgetSurfaceHints? hints,
        Size preferred,
        Size minimum,
        Size admitted,
        bool minimumSatisfied)
    {
        if (!minimumSatisfied) return ContentResponsiveMode.Compact;
        if (hints?.Mode == WidgetSurfaceMode.Compact) return ContentResponsiveMode.Compact;
        if (hints?.Mode == WidgetSurfaceMode.Standard) return ContentResponsiveMode.Standard;
        if (hints?.Mode == WidgetSurfaceMode.Wide) return ContentResponsiveMode.Wide;

        var midpointWidth = minimum.Width + (preferred.Width - minimum.Width) * 0.5;
        var midpointHeight = minimum.Height + (preferred.Height - minimum.Height) * 0.5;
        if (admitted.Width + 0.01 < midpointWidth || admitted.Height + 0.01 < midpointHeight)
            return ContentResponsiveMode.Compact;
        return preferred.Width / Math.Max(1, preferred.Height) >= 1.45
            ? ContentResponsiveMode.Wide
            : ContentResponsiveMode.Standard;
    }

    private static Size PairOrFallback(
        double? width,
        double? height,
        Size? candidate,
        Size fallback)
    {
        if (width is { } pairWidth && height is { } pairHeight &&
            double.IsFinite(pairWidth) && double.IsFinite(pairHeight) &&
            pairWidth > 0 && pairHeight > 0)
            return new Size(pairWidth, pairHeight);
        if (candidate is { } desired && double.IsFinite(desired.Width) &&
            double.IsFinite(desired.Height) && desired.Width > 0 && desired.Height > 0)
            return desired;
        return fallback;
    }

    private static double FinitePositive(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;

    private static double CenteredLocalOffset(
        double elementWidthDip,
        double windowWidthDip,
        double physicalWorkWidth,
        double renderScaling)
    {
        var workWidthPx = Math.Max(1, (int)Math.Round(physicalWorkWidth, MidpointRounding.AwayFromZero));
        var windowWidthPx = Math.Max(1, (int)Math.Round(
            windowWidthDip * renderScaling,
            MidpointRounding.AwayFromZero));
        var elementWidthPx = Math.Max(1, (int)Math.Round(
            elementWidthDip * renderScaling,
            MidpointRounding.AwayFromZero));
        var windowLeftPx = (workWidthPx - windowWidthPx) / 2;
        var elementLeftPx = (workWidthPx - elementWidthPx) / 2;
        return (elementLeftPx - windowLeftPx) / renderScaling;
    }
}
