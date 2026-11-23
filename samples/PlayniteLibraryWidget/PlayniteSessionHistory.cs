using System.Globalization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record SessionOccurrence(string Key, PlayniteSession Value, DateOnly? Day, long Start, long End)
{
    private List<PlayniteTimelineEvent>? highlights;
    internal IReadOnlyList<PlayniteTimelineEvent> Highlights => highlights ?? (IReadOnlyList<PlayniteTimelineEvent>)[];
    internal void AddHighlight(PlayniteTimelineEvent item) => (highlights ??= []).Add(item);
}

internal sealed class SessionPeriod(DateOnly first, DateOnly last, string label, TimeZoneInfo zone)
{
    internal DateOnly First { get; } = first;
    internal DateOnly Last { get; } = last;
    internal string Key => First.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    internal string Label { get; } = label;
    internal string Dates => First.Year == Last.Year
        ? $"{First:MMM d} – {Last:MMM d, yyyy}" : $"{First:MMM d, yyyy} – {Last:MMM d, yyyy}";
    internal long Start { get; } = Boundary(first, zone);
    internal long End { get; } = last == DateOnly.MaxValue ? DateTime.MaxValue.Ticks + 1 : Boundary(last.AddDays(1), zone);
    internal long Seconds { get; private set; }
    internal int Starts { get; private set; }
    internal int ActiveDays { get; private set; }
    internal int Visits { get; private set; }
    private int lastDay = -1;

    internal bool Includes(SessionOccurrence item) => item.Day.HasValue && item.Start < End &&
        (item.End > Start || item.Start >= Start);

    // Called in chronological order. Merge day intervals without allocating one item per calendar day.
    internal void Add(SessionOccurrence item, TimeZoneInfo timeZone)
    {
        if (!Includes(item)) return;
        Visits++;
        if (item.Start >= Start) Starts++;
        var from = Math.Max(Start, item.Start);
        var until = Math.Min(End, item.End);
        // Round cumulative elapsed time at each boundary so the pieces still sum
        // to the recorded duration when the session starts between whole seconds.
        Seconds += (until - item.Start) / TimeSpan.TicksPerSecond - (from - item.Start) / TimeSpan.TicksPerSecond;
        var a = PlayniteSessionHistory.LocalDay(from, timeZone).DayNumber;
        var b = PlayniteSessionHistory.LocalDay(Math.Max(from, until - 1), timeZone).DayNumber;
        ActiveDays += Math.Max(0, b - Math.Max(a - 1, lastDay));
        lastDay = Math.Max(lastDay, b);
    }

    private static long Boundary(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return Math.Clamp(local.Ticks - offset.Ticks, 0, DateTime.MaxValue.Ticks);
    }
}

internal sealed record SessionLengths(long MedianSeconds, int[] Distribution)
{
    internal static SessionLengths From(IEnumerable<SessionOccurrence> rows)
    {
        var lengths = rows.Select(r => r.Value.Seconds).Order().ToArray();
        var median = lengths.Length == 0 ? 0 : (long)(((decimal)lengths[(lengths.Length - 1) / 2] + lengths[lengths.Length / 2]) / 2);
        return new(median, [lengths.Count(s => s < 1800), lengths.Count(s => s is >= 1800 and <= 5400), lengths.Count(s => s > 5400)]);
    }
}

internal sealed class PlayniteSessionHistory
{
    internal SessionOccurrence[] Rows { get; }
    internal SessionPeriod[] Periods { get; }
    internal SessionOccurrence[] RangeRows { get; }
    internal SessionLengths Lengths { get; }
    internal long RecordedSeconds { get; }
    internal bool All { get; }
    internal int Undated { get; }

    internal PlayniteSessionHistory(PlayniteActivity activity, bool all, PlayniteAchievements? achievements,
        IReadOnlyList<CaptureLibraryItem> captures, TimeZoneInfo zone, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        RecordedSeconds = activity.TotalSeconds;
        var duplicates = new Dictionary<PlayniteSession, int>();
        Rows = activity.Sessions.OrderByDescending(s => s.Date).Select(s =>
        {
            token.ThrowIfCancellationRequested();
            var occurrence = duplicates.GetValueOrDefault(s);
            duplicates[s] = occurrence + 1;
            var start = s.Date?.UtcTicks ?? 0;
            var end = s.Seconds <= (DateTime.MaxValue.Ticks - start) / TimeSpan.TicksPerSecond
                ? start + s.Seconds * TimeSpan.TicksPerSecond : start;
            var key = WidgetRail.Playnite.CaptureRelatedIdentity.Hash(FormattableString.Invariant($"{s.Date?.UtcTicks}/{s.Seconds}/{occurrence}/{s.Action?.Length}/{s.Action}"));
            return new SessionOccurrence("run." + key, s, s.Date.HasValue ? LocalDay(start, zone) : null, start, end);
        }).ToArray();
        var dated = Rows.Where(r => r.Day.HasValue).Reverse().ToArray();
        Undated = Rows.Length - dated.Length;
        All = all || dated.Length == 0;
        Periods = dated.Length == 0 ? [] : MakePeriods(dated[0].Day!.Value,
            LocalDay(dated.Max(r => Math.Max(r.Start, r.End - 1)), zone), All, zone);
        foreach (var row in dated)
        {
            token.ThrowIfCancellationRequested();
            foreach (var period in Periods) period.Add(row, zone);
        }
        RangeRows = All ? Rows : Rows.Where(r => r.Day.HasValue && (r.End > Periods[0].Start || r.Start >= Periods[0].Start)).ToArray();
        Lengths = SessionLengths.From(RangeRows);
        AttachHighlights(dated, achievements, captures);
    }

    internal static DateOnly LocalDay(long ticks, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(ticks, TimeSpan.Zero), zone).DateTime);

    private static SessionPeriod[] MakePeriods(DateOnly first, DateOnly last, bool all, TimeZoneInfo zone)
    {
        if (!all)
        {
            var monday = last.DayNumber - ((int)last.DayOfWeek + 6) % 7;
            return Enumerable.Range(0, 8).Select(i => monday - 7 * (7 - i)).Where(n => n >= 0).Select(n =>
            {
                var start = DateOnly.FromDayNumber(n);
                return new SessionPeriod(start, DateOnly.FromDayNumber(Math.Min(DateOnly.MaxValue.DayNumber, n + 6)),
                    start.ToString("MMM d"), zone);
            }).ToArray();
        }
        var periods = new List<SessionPeriod>();
        var months = (last.Year - first.Year) * 12 + last.Month - first.Month + 1;
        var monthly = months <= 12;
        var stepYears = (last.Year - first.Year + 12) / 12;
        var cursor = new DateOnly(first.Year, monthly ? first.Month : 1, 1);
        while (cursor <= last)
        {
            var endYear = Math.Min(9999, cursor.Year + stepYears - 1);
            var end = monthly ? new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month))
                : new DateOnly(endYear, 12, 31);
            var label = monthly ? cursor.ToString("MMM") : endYear == cursor.Year ? cursor.Year.ToString() : $"{cursor.Year}–{endYear}";
            periods.Add(new(cursor, end, label, zone));
            if (end >= last) break;
            cursor = end.AddDays(1);
        }
        return periods.ToArray();
    }

    private static void AttachHighlights(SessionOccurrence[] dated, PlayniteAchievements? achievements, IReadOnlyList<CaptureLibraryItem> captures)
    {
        var events = PlayniteGameTimeline.Events(achievements, captures).Where(e => e.HasEventTime).OrderBy(e => e.When);
        var active = new SortedSet<(long End, int Index)>();
        var index = 0;
        foreach (var item in events)
        {
            while (index < dated.Length && dated[index].Start <= item.When.UtcTicks)
            {
                active.Add((dated[index].End, index));
                index++;
            }
            while (active.Count > 0 && active.Min.End <= item.When.UtcTicks) active.Remove(active.Min);
            if (active.Count == 1) dated[active.Min.Index].AddHighlight(item);
        }
    }
}
