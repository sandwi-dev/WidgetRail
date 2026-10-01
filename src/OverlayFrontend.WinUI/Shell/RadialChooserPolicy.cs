namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>Semantic radial paging and normalized-stick ownership, independent of native layout.</summary>
internal sealed class RadialChooserPolicy
{
    internal const int PageSize = 8;
    private bool radial, waitingNeutral, leftArmed;
    private int pageDirection;
    private long repeatAt;
    internal bool AllowScroll => !radial && !waitingNeutral;

    internal void UpdateOwner(bool active, short rightX, short rightY)
    {
        var neutral = Math.Abs((int)rightX) < 7849 && Math.Abs((int)rightY) < 7849;
        if (radial != active) { radial = active; waitingNeutral = !neutral; pageDirection = 0; leftArmed = false; }
        if (neutral) waitingNeutral = false;
    }
    internal void Reset() { waitingNeutral |= radial; radial = false; leftArmed = false; pageDirection = 0; }
    internal void RebasePage() => leftArmed = false;
    internal int? SelectSector(short x, short y)
    {
        var magnitude = (long)x * x + (long)y * y;
        if (magnitude < 7849L * 7849) leftArmed = true;
        return radial && leftArmed ? Sector(x, y) : null;
    }
    internal int PageStep(short x, long now)
    {
        if (!radial || waitingNeutral) return 0;
        var magnitude = Math.Abs((int)x);
        if (magnitude < 9000) { pageDirection = 0; return 0; }
        var next = Math.Sign(x);
        if (pageDirection != next)
        {
            if (magnitude < 15000) return 0;
            pageDirection = next; repeatAt = now + 360; return next;
        }
        if (now < repeatAt) return 0;
        repeatAt = now + 125; return next;
    }
    internal static int? Sector(short x, short y)
    {
        if ((long)x * x + (long)y * y <= 20000L * 20000) return null;
        var angle = Math.Atan2(x, y);
        if (angle < 0) angle += Math.Tau;
        return (int)Math.Floor((angle + Math.PI / 8) / (Math.PI / 4)) % PageSize;
    }
    internal static int PageCount(int count) => Math.Max(1, (Math.Max(0, count) + PageSize - 1) / PageSize);
    internal static int NextPage(int count, int page, int direction)
    { var pages = PageCount(count); return ((Math.Clamp(page, 0, pages - 1) + direction) % pages + pages) % pages; }
    internal static int StepSelection(int count, int page, int selected, int direction)
    {
        if (count <= 0) return -1;
        var first = Math.Clamp(page, 0, PageCount(count) - 1) * PageSize;
        var size = Math.Min(PageSize, count - first);
        if (selected < first || selected >= first + size) return direction < 0 ? first + size - 1 : first;
        return first + ((selected - first + direction) % size + size) % size;
    }
}
