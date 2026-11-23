using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Tests.PlayniteLibrary;

[TestClass]
public sealed class PlayniteSessionHistoryTests
{
    private static PlayniteSessionHistory History(params PlayniteSession[] sessions) =>
        new(new(true, sessions.Sum(s => s.Seconds), sessions), false, null, [], TimeZoneInfo.Utc);

    [TestMethod]
    public void CarryInHasPlaytimeWithoutASecondStart()
    {
        var monday = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        var history = History(new(monday.AddSeconds(-45).AddMilliseconds(500), 120, null),
            new PlayniteSession(monday.AddDays(-70), 300, null));
        Assert.HasCount(8, history.Periods);
        Assert.AreEqual(6, history.Periods.Count(p => p.Visits == 0));
        var last = history.Periods[^1];
        Assert.AreEqual(0, last.Starts);
        Assert.AreEqual(1, last.ActiveDays);
        Assert.AreEqual(1, last.Visits);
        Assert.AreEqual(120L, history.Periods.Sum(p => p.Seconds));
        Assert.IsTrue(last.Includes(history.RangeRows.Single()));
    }

    [TestMethod]
    [DataRow(3, 8, -8, 23)]
    [DataRow(11, 1, -7, 25)]
    public void LocalDaysUseActualElapsedTimeAcrossDst(int month, int day, int offset, int hours)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var sunday = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.FromHours(offset));
        var history = new PlayniteSessionHistory(new(true, 0, [new(sunday, (hours + 1) * 3600, null)]),
            false, null, [], zone);
        Assert.AreEqual(hours * 3600L, history.Periods[^2].Seconds);
        Assert.AreEqual(3600L, history.Periods[^1].Seconds);
        Assert.AreEqual(1, history.Periods[^2].ActiveDays);
        Assert.AreEqual(1, history.Periods[^1].ActiveDays);
        Assert.AreEqual(0, history.Periods[^1].Starts);
    }

    [TestMethod]
    public void UndatedDuplicatesStayDistinctAndDurationBucketsIncludeTheirEndpoints()
    {
        PlayniteSession[] rows = [new(null, 1799, null), new(null, 1800, null), new(null, 1800, null),
            new(null, 5400, null), new(null, 5401, null)];
        var history = History(rows);
        Assert.IsTrue(history.All);
        Assert.AreEqual(5, history.Undated);
        Assert.IsEmpty(history.Periods);
        Assert.AreEqual(1800L, history.Lengths.MedianSeconds);
        CollectionAssert.AreEqual(new[] { 1, 3, 1 }, history.Lengths.Distribution);
        Assert.AreEqual(5, history.Rows.DistinctBy(r => r.Key).Count());
        CollectionAssert.AreEqual(history.Rows.Select(r => r.Key).ToArray(), History(rows).Rows.Select(r => r.Key).ToArray());
    }

    [TestMethod]
    public void TenThousandSessionsKeepAllTimeChartBoundedAndDatesOrdered()
    {
        var first = DateTimeOffset.UnixEpoch;
        var runs = Enumerable.Range(0, 10000).Select(i => new PlayniteSession(first.AddDays(i), 60, "Play")).ToArray();
        var history = new PlayniteSessionHistory(new(true, 600000, runs), true, null, [], TimeZoneInfo.Utc);
        Assert.IsTrue(history.Periods.Length <= 12);
        Assert.AreEqual(10000, history.Periods.Sum(p => p.ActiveDays));
        Assert.AreEqual(600000L, history.Periods.Sum(p => p.Seconds));
        Assert.AreEqual(runs[^1], history.RangeRows[0].Value);
        Assert.HasCount(10000, history.RangeRows);
    }

    [TestMethod]
    public void HighlightsUseActualEventTimeAndExactlyOneOpenInterval()
    {
        var start = DateTimeOffset.UnixEpoch;
        var runs = new PlayniteActivity(true, 180, [new(start, 90, null), new(start.AddSeconds(60), 30, null),
            new(start.AddSeconds(90), 60, null)]);
        var award = new PlayniteAchievement("Won", "", true, start.AddSeconds(75), null, null, false);
        var saved = new CaptureLibraryItem("copy", new("original", "image/png", 80, 80, 120, 0, long.MaxValue), 20000);
        var atBoundary = saved with { Id = "captured", CapturedAtUnixMilliseconds = 90000 };
        var history = new PlayniteSessionHistory(runs, true, new(true, 1, 1, [award]), [saved, atBoundary, atBoundary], TimeZoneInfo.Utc);
        Assert.AreEqual("captured", history.Rows[0].Highlights.Single().Capture!.Id);
        Assert.AreEqual(1, history.Rows.Sum(r => r.Highlights.Count));
        Assert.AreEqual(1, history.Periods.Single().ActiveDays);
    }
}
