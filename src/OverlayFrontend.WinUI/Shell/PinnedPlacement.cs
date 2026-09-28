namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal readonly record struct PinnedBounds(int X, int Y, int Width, int Height);
internal sealed record PinnedMonitor(string Id, PinnedBounds WorkArea, double Scale, bool Primary = false);
internal sealed record PinnedPlacement(string MonitorId, double AnchorX, double AnchorY,
    double WidthDip, double HeightDip, string LayoutId, int OpacityPercent = 100)
{
    internal bool IsValid => MonitorId is { Length: > 0 and <= 2048 } && ShellPreferences.ValidId(LayoutId) &&
        double.IsFinite(AnchorX) && double.IsFinite(AnchorY) && AnchorX is >= 0 and <= 1 && AnchorY is >= 0 and <= 1 &&
        double.IsFinite(WidthDip) && double.IsFinite(HeightDip) && WidthDip is >= 240 and <= 8192 &&
        HeightDip is >= 135 and <= 8192 && OpacityPercent is >= 30 and <= 100;
}
internal readonly record struct PinnedPlacementLimits(double MinWidth = 240, double MinHeight = 135,
    double MaxWidth = 960, double MaxHeight = 540)
{
    internal static PinnedPlacementLimits Default => new(240, 135, 960, 540);
    internal bool IsValid => double.IsFinite(MinWidth) && double.IsFinite(MinHeight) && double.IsFinite(MaxWidth) &&
        double.IsFinite(MaxHeight) && MinWidth >= 240 && MinHeight >= 135 && MaxWidth >= MinWidth && MaxHeight >= MinHeight &&
        MaxWidth <= 8192 && MaxHeight <= 8192;
}
internal sealed record ResolvedPinnedPlacement(PinnedMonitor Monitor, PinnedBounds Bounds, bool UsedFallback);

/// <summary>Persist logical size and relative work-area position; WinUI owns content layout.</summary>
internal static class PinnedPlacementPolicy
{
    internal static ResolvedPinnedPlacement? Resolve(IReadOnlyList<PinnedMonitor> monitors, PinnedPlacement? saved,
        PinnedPlacementLimits limits, double initialWidth = 480, double initialHeight = 270)
    {
        if (!limits.IsValid) throw new ArgumentException("Invalid pinned placement limits.", nameof(limits));
        var available = monitors.Where(ValidMonitor).ToArray();
        var valid = saved?.IsValid == true;
        var exact = valid ? available.FirstOrDefault(monitor => monitor.Id == saved!.MonitorId) : null;
        var monitor = exact ?? available.FirstOrDefault(monitor => monitor.Primary) ?? available.FirstOrDefault();
        if (monitor is null) return null;
        var area = monitor.WorkArea;
        if (area.Width < Pixels(limits.MinWidth, monitor.Scale) || area.Height < Pixels(limits.MinHeight, monitor.Scale)) return null;
        var width = Math.Min(area.Width, Pixels(Math.Clamp(valid ? saved!.WidthDip : double.IsFinite(initialWidth) ? initialWidth : 480,
            limits.MinWidth, limits.MaxWidth), monitor.Scale));
        var height = Math.Min(area.Height, Pixels(Math.Clamp(valid ? saved!.HeightDip : double.IsFinite(initialHeight) ? initialHeight : 270,
            limits.MinHeight, limits.MaxHeight), monitor.Scale));
        var travelX = area.Width - width; var travelY = area.Height - height;
        var x = valid ? (int)Math.Round(saved!.AnchorX * travelX) : Math.Max(0, travelX - Pixels(16, monitor.Scale));
        var y = valid ? (int)Math.Round(saved!.AnchorY * travelY) : Math.Min(travelY, Pixels(16, monitor.Scale));
        return new(monitor, new(area.X + x, area.Y + y, width, height), saved is not null && (!valid || exact is null));
    }

    internal static PinnedBounds Constrain(PinnedBounds proposed, PinnedMonitor monitor, PinnedPlacementLimits limits)
    {
        if (!ValidMonitor(monitor) || !limits.IsValid) throw new ArgumentException("Invalid pinned monitor or limits.");
        var area = monitor.WorkArea;
        var width = Math.Clamp(proposed.Width, Math.Min(area.Width, Pixels(limits.MinWidth, monitor.Scale)),
            Math.Min(area.Width, Pixels(limits.MaxWidth, monitor.Scale)));
        var height = Math.Clamp(proposed.Height, Math.Min(area.Height, Pixels(limits.MinHeight, monitor.Scale)),
            Math.Min(area.Height, Pixels(limits.MaxHeight, monitor.Scale)));
        return new(Math.Clamp(proposed.X, area.X, area.X + area.Width - width),
            Math.Clamp(proposed.Y, area.Y, area.Y + area.Height - height), width, height);
    }

    internal static PinnedPlacement Capture(PinnedBounds bounds, PinnedMonitor monitor, PinnedPlacementLimits limits,
        string layoutId, int opacityPercent)
    {
        var safe = Constrain(bounds, monitor, limits);
        var xRange = monitor.WorkArea.Width - safe.Width; var yRange = monitor.WorkArea.Height - safe.Height;
        return new(monitor.Id, xRange == 0 ? 0 : (safe.X - monitor.WorkArea.X) / (double)xRange,
            yRange == 0 ? 0 : (safe.Y - monitor.WorkArea.Y) / (double)yRange,
            safe.Width / monitor.Scale, safe.Height / monitor.Scale, layoutId, Math.Clamp(opacityPercent, 30, 100));
    }

    private static bool ValidMonitor(PinnedMonitor monitor) => !string.IsNullOrWhiteSpace(monitor.Id) &&
        double.IsFinite(monitor.Scale) && monitor.Scale is >= .5 and <= 10 &&
        monitor.WorkArea.Width > 0 && monitor.WorkArea.Height > 0 &&
        (long)monitor.WorkArea.X + monitor.WorkArea.Width <= int.MaxValue &&
        (long)monitor.WorkArea.Y + monitor.WorkArea.Height <= int.MaxValue;
    private static int Pixels(double dip, double scale) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);
}
