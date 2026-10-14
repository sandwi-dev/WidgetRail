using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Notifications;
internal static class NotificationPlacement
{
    internal static bool Overlaps(PinnedBounds a, PinnedBounds b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    internal static PinnedBounds? Choose(PinnedBounds area, int width, int height, IReadOnlyList<PinnedBounds> obstacles, PinnedBounds? previous = null)
    {
        const int gap = 16;
        bool Fits(PinnedBounds candidate) => candidate.X >= area.X + gap && candidate.Y >= area.Y + gap &&
            candidate.X + candidate.Width <= area.X + area.Width - gap && candidate.Y + candidate.Height <= area.Y + area.Height - gap &&
            obstacles.All(other => !Overlaps(candidate, new(other.X - gap, other.Y - gap, other.Width + gap * 2, other.Height + gap * 2)));
        if (width <= 0 || height <= 0) return null;
        if (previous is { } old && old.Width == width && old.Height == height && Fits(old)) return old;
        var candidates = new PinnedBounds[] {
            new(area.X + area.Width - width - gap, area.Y + gap, width, height),
            new(area.X + gap, area.Y + gap, width, height),
            new(area.X + area.Width - width - gap, area.Y + area.Height - height - gap, width, height),
            new(area.X + gap, area.Y + area.Height - height - gap, width, height) };
        foreach (var candidate in candidates) if (Fits(candidate)) return candidate;
        return null;
    }
}
