using System.Globalization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteTimelineEvent(DateTimeOffset When, bool HasEventTime,
    PlayniteAchievement? Achievement = null, CaptureLibraryItem? Capture = null);

internal sealed record PlayniteSessionSummary(PlayniteSession Session, IReadOnlyList<PlayniteTimelineEvent> Events);
internal sealed record PlaynitePlayWeek(DateOnly FirstDay, IReadOnlyList<long> DailySeconds);
internal sealed record PlayniteGameHistory(IReadOnlyList<PlayniteActivityDay> Days, PlayniteSessionSummary? LatestSession, PlaynitePlayWeek? Week = null);

internal sealed record PlayniteActivityDay(DateOnly Date, IReadOnlyList<PlayniteSession> Sessions,
    IReadOnlyList<PlayniteTimelineEvent> Events)
{
    internal string Key => "day." + Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}

internal static class PlayniteGameTimeline
{
    // Home is a recent history. The dedicated sections remain the complete records.
    internal const int MaximumRows = 96;

    internal static PlayniteGameHistory Build(PlayniteActivity? activity, PlayniteAchievements? achievements,
        IReadOnlyList<CaptureLibraryItem> captures)
    {
        var dated = (activity?.Sessions ?? []).Where(session => session.Date.HasValue).ToArray();
        var sessions = dated.ToLookup(session => DateOnly.FromDateTime(session.Date!.Value.LocalDateTime));
        var recent = Events(achievements, captures, MaximumRows).OrderByDescending(item => item.When).ToArray();
        var events = recent.ToLookup(item => DateOnly.FromDateTime(item.When.LocalDateTime));
        var days = sessions.Select(group => group.Key).Union(events.Select(group => group.Key)).OrderDescending().Take(MaximumRows)
            .Select(date => new PlayniteActivityDay(date, sessions[date].ToArray(), events[date].ToArray())).ToArray();
        if (dated.Length == 0) return new(days, null);

        var latest = dated.MaxBy(session => session.Date)!;
        var start = latest.Date!.Value.UtcTicks;
        var end = EndTicks(latest);
        // Every other session began at or before this one. The latest preceding end
        // is therefore the first instant that belongs exclusively to this session.
        var latestIndex = Array.IndexOf(dated, latest);
        var uniqueFrom = start;
        for (var i = 0; i < dated.Length; i++)
            if (i != latestIndex) uniqueFrom = Math.Max(uniqueFrom, EndTicks(dated[i]));
        var associated = recent.Where(item => item.HasEventTime && item.When.UtcTicks >= uniqueFrom && item.When.UtcTicks < end).ToArray();
        var last = DateOnly.FromDateTime(latest.Date!.Value.LocalDateTime);
        var first = DateOnly.FromDayNumber(Math.Max(0, last.DayNumber - 6));
        var daily = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(offset =>
            (long)Math.Min(long.MaxValue, sessions[first.AddDays(offset)].Sum(session => (decimal)session.Seconds))).ToArray();
        return new(days, new(latest, associated), new(first, daily));
    }

    private static long EndTicks(PlayniteSession session)
    {
        var start = session.Date!.Value.UtcTicks;
        return session.Seconds > 0 && session.Seconds <= (DateTime.MaxValue.Ticks - start) / TimeSpan.TicksPerSecond
            ? start + session.Seconds * TimeSpan.TicksPerSecond : start;
    }

    internal static IEnumerable<PlayniteTimelineEvent> Events(PlayniteAchievements? achievements, IReadOnlyList<CaptureLibraryItem> captures, int achievementLimit = int.MaxValue)
    {
        foreach (var item in (achievements?.Items ?? []).Where(item => item.Unlocked && item.UnlockedAt.HasValue)
                     .OrderByDescending(item => item.UnlockedAt).Take(achievementLimit))
        {
            yield return new(item.UnlockedAt!.Value, true, Achievement: item);
        }
        foreach (var item in captures.Where(item => item.Available).DistinctBy(item => item.Id))
        {
            var stamp = item.CapturedAtUnixMilliseconds ?? item.SavedAtUnixMilliseconds;
            if (stamp is < -62135596800000 or > 253402300799999) continue;
            yield return new(DateTimeOffset.FromUnixTimeMilliseconds(stamp),
                item.CapturedAtUnixMilliseconds.HasValue, Capture: item);
        }
    }

}
