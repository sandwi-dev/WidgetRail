namespace WidgetRail.WindowsDisplayProvider;

/// <summary>Resize an adjacent row/column around the unchanged primary origin.
/// Irregular layouts are retained only when resizing preserves their connectivity
/// and non-overlap; otherwise Windows Settings owns the arrangement decision.</summary>
internal static class DisplayControlLayout
{
    internal static void Resize(NativePath[] paths, NativeMode[] modes, uint changedSource, uint width, uint height)
    {
        var sources = paths.Select(path => path.Source.ModeIndex).Distinct().ToArray();
        var before = sources.ToDictionary(index => index, index => modes[index].Source);
        var old = before[changedSource];
        if (old.Width == width && old.Height == height) return; // Refresh-only changes never move displays.
        var primary = sources.Where(index => before[index].X == 0 && before[index].Y == 0).ToArray();
        if (primary.Length != 1) throw Unavailable();
        var horizontal = sources.OrderBy(index => before[index].X).ToArray();
        var vertical = sources.OrderBy(index => before[index].Y).ToArray();
        var row = sources.All(index => before[index].Y == old.Y) &&
            horizontal.Zip(horizontal.Skip(1), (a, b) => (long)before[a].X + before[a].Width == before[b].X).All(value => value);
        var column = sources.All(index => before[index].X == old.X) &&
            vertical.Zip(vertical.Skip(1), (a, b) => (long)before[a].Y + before[a].Height == before[b].Y).All(value => value);
        modes[changedSource].Source.Width = width;
        modes[changedSource].Source.Height = height;
        if (row) Arrange(horizontal, horizontalAxis: true);
        else if (column) Arrange(vertical, horizontalAxis: false);
        var rectangles = sources.Select(index => modes[index].Source).ToArray();
        if (rectangles.Any(rectangle => rectangle.X is < -65536 or > 65536 || rectangle.Y is < -65536 or > 65536) ||
            rectangles.SelectMany((a, index) => rectangles.Skip(index + 1).Select(b => Overlap(a.X, a.Width, b.X, b.Width) &&
                Overlap(a.Y, a.Height, b.Y, b.Height))).Any(value => value) || !Connected(rectangles))
            throw Unavailable();

        void Arrange(uint[] order, bool horizontalAxis)
        {
            var anchor = Array.IndexOf(order, primary[0]);
            for (var index = anchor + 1; index < order.Length; ++index)
            {
                var preceding = modes[order[index - 1]].Source;
                if (horizontalAxis) modes[order[index]].Source.X = checked(preceding.X + (int)preceding.Width);
                else modes[order[index]].Source.Y = checked(preceding.Y + (int)preceding.Height);
            }
            for (var index = anchor - 1; index >= 0; --index)
            {
                var following = modes[order[index + 1]].Source;
                if (horizontalAxis) modes[order[index]].Source.X = checked(following.X - (int)modes[order[index]].Source.Width);
                else modes[order[index]].Source.Y = checked(following.Y - (int)modes[order[index]].Source.Height);
            }
        }
    }
    private static bool Overlap(int a, uint sizeA, int b, uint sizeB) => Math.Max(a, b) < Math.Min((long)a + sizeA, (long)b + sizeB);
    private static bool Adjacent(NativeSourceMode a, NativeSourceMode b) =>
        (((long)a.X + a.Width == b.X || (long)b.X + b.Width == a.X) && Overlap(a.Y, a.Height, b.Y, b.Height)) ||
        (((long)a.Y + a.Height == b.Y || (long)b.Y + b.Height == a.Y) && Overlap(a.X, a.Width, b.X, b.Width));
    private static bool Connected(NativeSourceMode[] rectangles)
    {
        var visited = new HashSet<int> { 0 };
        var pending = new Queue<int>(); pending.Enqueue(0);
        while (pending.TryDequeue(out var current))
            for (var index = 0; index < rectangles.Length; ++index)
                if (!visited.Contains(index) && Adjacent(rectangles[current], rectangles[index]))
                { visited.Add(index); pending.Enqueue(index); }
        return visited.Count == rectangles.Length;
    }
    private static WidgetRail.PlatformBroker.BrokerException Unavailable() => new("display_layout_unavailable",
        "Use Windows Display settings to adjust this arrangement before changing resolution.");
}
