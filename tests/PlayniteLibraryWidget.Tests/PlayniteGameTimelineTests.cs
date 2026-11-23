using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Tests.PlayniteLibrary;

[TestClass]
public sealed class PlayniteGameTimelineTests
{
    [TestMethod]
    public void OnlyAnUnambiguousEventTimeJoinsASession()
    {
        var start = DateTimeOffset.UnixEpoch;
        var activity = new PlayniteActivity(true, 180, [new(start, 120, "Play"), new(start.AddSeconds(60), 60, "Play")]);
        var unlocked = new PlayniteAchievement("First win", "", true, start, null, null, false);
        var achievements = new PlayniteAchievements(true, 4, 3,
            [unlocked, unlocked with { Name = "During overlap", UnlockedAt = start.AddSeconds(90) },
             unlocked with { UnlockedAt = null }, unlocked with { Unlocked = false }]);
        var capture = new CaptureLibraryItem("original", new("original", "image/png", 80, 80, 120, 0, long.MaxValue), 20000);
        var captured = capture with { CapturedAtUnixMilliseconds = 30000 };
        var saved = capture with { Id = "saved-copy" };
        var after = capture with { Id = "at-end", CapturedAtUnixMilliseconds = 120000 };
        var history = PlayniteGameTimeline.Build(activity, achievements, [captured, captured, saved, after]);
        var events = history.Days.SelectMany(day => day.Events).Select(item => item.Capture?.Id ?? item.Achievement!.Name).ToArray();
        CollectionAssert.AreEquivalent(new[] { "original", "First win", "saved-copy", "at-end", "During overlap" }, events);
        Assert.AreEqual(0, history.LatestSession!.Events.Count, "Neither the overlapping unlock nor the end boundary belongs to the latest session.");
        var single = PlayniteGameTimeline.Build(activity with { Sessions = [activity.Sessions[0]] }, achievements,
            [captured, captured, saved, after]);
        CollectionAssert.AreEquivalent(new[] { "original", "First win", "During overlap" },
            single.LatestSession!.Events.Select(item => item.Capture?.Id ?? item.Achievement!.Name).ToArray());
    }

    [TestMethod]
    public void PlayWeekUsesSessionStartDatesAndKeepsQuietDays()
    {
        var end = new DateTimeOffset(new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Local));
        var sessions = new[] { new PlayniteSession(end.AddDays(-7), 3000, null), new(end.AddDays(-6), 30, null),
            new(end.AddDays(-2), 100, null), new(end, 90, null), new(end.AddHours(6), 30, null) };
        var week = PlayniteGameTimeline.Build(new(true, 3250, sessions), null, []).Week!;
        Assert.AreEqual(DateOnly.FromDateTime(end.LocalDateTime.AddDays(-6)), week.FirstDay);
        CollectionAssert.AreEqual(new long[] { 30, 0, 0, 0, 100, 0, 120 }, week.DailySeconds.ToArray());
    }

    [TestMethod]
    public void RecentHistoryIsBoundedAndPreservesDuplicateOccurrences()
    {
        var start = DateTimeOffset.UnixEpoch;
        var sessions = Enumerable.Range(0, 10000).Select(i => new PlayniteSession(start.AddDays(i / 2), 60, null)).ToArray();
        var rows = PlayniteGameTimeline.Build(new(true, 600000, sessions), null, []).Days;
        Assert.HasCount(PlayniteGameTimeline.MaximumRows, rows);
        Assert.AreEqual(rows.Count, rows.Select(row => row.Key).Distinct().Count());
        Assert.AreEqual(DateOnly.FromDateTime(sessions[^1].Date!.Value.LocalDateTime), rows[0].Date);
        Assert.HasCount(2, rows[0].Sessions);
        Assert.IsTrue(rows.Zip(rows.Skip(1), (left, right) => left.Date >= right.Date).All(ordered => ordered));
    }
}
