using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using static WidgetRail.Samples.PlayniteLibrary.PlayniteLibraryDetailsPresentation;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private static WidgetElement SessionDashboard(PlayniteSessionHistory history, HistoryContext context, SessionPeriod? period, WidgetElement list,
        string? activityError, string? analysisError)
    {
        var sections = new List<WidgetElement>();
        void Add(WidgetElement section) => sections.Add(section.InGrid(row: sections.Count));
        if (activityError is not null) Add(PlayniteLibraryDetailsPresentation.ReadError(Sessions + "warning", activityError,
            PlayniteLibraryDetailsPresentation.RetryActivityAction));
        if (analysisError is not null) Add(PlayniteLibraryDetailsPresentation.ReadError(Sessions + "analysis-error", analysisError,
            PlayniteLibraryDetailsPresentation.RetrySessionsAction));
        var charts = new List<WidgetElement>();
        if (history.Periods.Length > 0) charts.Add(PeriodChart(history, period));
        charts.Add(SessionProfile(context).InGrid(column: charts.Count));
        Add(UI.Grid(Sessions + "analytics", [], charts.Count == 1 ? [GridTrack.Star()] : [GridTrack.Star(2.25), GridTrack.Star()],
            charts.ToArray()).Spacing(0, 24));
        Add(UI.Divider(Sessions + "rule"));
        var scope = history.All ? "All history" : "Last eight weeks";
        var count = period?.Visits ?? history.RangeRows.Length;
        var legend = CountLabel(count, "session");
        if (history.All && period is null) legend += $"  ·  GameActivity total {Duration(history.RecordedSeconds)}";
        if (history.Undated > 0 && !history.All) legend += $"  ·  {history.Undated} undated in All history";
        var heading = new List<WidgetElement>
        {
            UI.Text(period is null ? "Session history" : $"Sessions · {period.Dates}", Sessions + "history-title").Classes("sessions-subhead"),
            UI.Text(legend, Sessions + "history-count", (period?.Dates ?? scope) + ", " + legend)
                .Classes("sessions-caption").InGrid(column: 1).AlignInGrid(GridCellAlignment.End, GridCellAlignment.Center)
        };
        if (period is not null)
            heading.Add(UI.Button("Clear filter", SessionChartBack, SessionChartBack)
                .FocusDown(SessionList).FocusUp(PeriodId(period)).Classes("sessions-range").InGrid(column: 2));
        Add(UI.Grid(Sessions + "history-heading", [], [GridTrack.Star(), GridTrack.Auto(), GridTrack.Auto()], heading.ToArray()).Spacing(0, 10));
        Add(list);
        return UI.Grid(Sessions + "body", sections.Select((_, i) => i == sections.Count - 1 ? GridTrack.Star() : GridTrack.Auto()).ToArray(),
            [], sections.ToArray()).Spacing(8, 0).Classes("sessions-body");
    }

    private static string PeriodSummary(SessionPeriod p) => p.Visits == 0 ? "No sessions" :
        $"{Duration(p.Seconds)}   ·   {CountLabel(p.ActiveDays, "active day")}   ·   {CountLabel(p.Starts, "start")}";

    private static WidgetElement PeriodChart(PlayniteSessionHistory history, SessionPeriod? filter)
    {
        var periods = history.Periods;
        var high = Math.Max(1, periods.Max(p => p.Seconds));
        var selected = filter ?? periods.LastOrDefault(p => p.Visits > 0) ?? periods[^1];
        var bars = new List<WidgetElement>();
        var ticks = new List<WidgetElement>();
        WidgetElement Caption(SessionPeriod p, string id) =>
            UI.Text(p.Dates + "    " + PeriodSummary(p), id).Classes("sessions-caption");
        for (var i = 0; i < periods.Length; i++)
        {
            var p = periods[i];
            var id = PeriodId(p);
            var ratio = (double)p.Seconds / high;
            var height = p.Seconds > 0 ? Math.Max(3, Math.Round(66 * ratio)) : 2;
            var column = UI.Grid(id + ".column", [GridTrack.Pixel(68 - height), GridTrack.Pixel(height)], [],
                UI.Stack(id + ".bar").Classes(p.Seconds > 0 ? "sessions-bar" : "sessions-baseline").InGrid(row: 1))
                .Classes("sessions-plot-column");
            var name = p.Dates + ", " + PeriodSummary(p);
            var bar = p.Visits == 0 ? UI.ReadingSurface(id, name, ActionSurfaceOrientation.Vertical, column) :
                UI.ActionSurface(id, id, name, ActionSurfaceOrientation.Vertical, column);
            bar = bar.FocusDown(SessionList).FocusUp(SessionRange);
            if (i > 0) bar = bar.FocusLeft(PeriodId(periods[i - 1]));
            if (i + 1 < periods.Length) bar = bar.FocusRight(PeriodId(periods[i + 1]));
            bars.Add(bar.Classes("sessions-chart-target").PresentOnFocus(Caption(p, id + ".caption")).InGrid(column: i));
            if (i == 0 || i == periods.Length - 1 || (periods.Length >= 5 && i == periods.Length / 2))
                ticks.Add(UI.Text(p.Label, id + ".tick").Classes("sessions-axis-label").InGrid(column: i)
                    .AlignInGrid(GridCellAlignment.Center));
        }
        var tracks = periods.Select(_ => GridTrack.Star()).ToArray();
        var plot = UI.Grid(Sessions + "plot", [], tracks, bars.ToArray())
            .Spacing(0, 3).Classes("sessions-plot").RememberChildFocus(PeriodId(selected));
        var surface = (UI.FocusPresentationSurface(plot, Caption(selected, Sessions + "initial-caption"), Sessions + "chart") with
            { RetainLastPresentation = true }).Classes("sessions-chart");
        return UI.Stack(Sessions + "chart-block",
            UI.Grid(Sessions + "chart-heading", [], [GridTrack.Star(), GridTrack.Auto()],
                UI.Text(history.All ? "Playtime · All history" : "Playtime · Last eight weeks", Sessions + "title").Classes("sessions-heading"),
                UI.Button(history.All ? "Last eight weeks" : "All history", SessionRange, SessionRange)
                    .FocusDown(PeriodId(selected)).Classes("sessions-range").InGrid(column: 1)),
            surface,
            UI.Grid(Sessions + "axis", [], tracks, ticks.ToArray()).Spacing(0, 3))
            .Classes("sessions-chart-block");
    }

    private static WidgetElement SessionProfile(HistoryContext context)
    {
        var cells = new List<WidgetElement>();
        var single = context.Rows.Length == 1;
        cells.Add(UI.Text(single ? "One session" : "Typical session", Sessions + "profile-title").Classes("sessions-subhead"));
        cells.Add(UI.Text(Duration(context.Lengths.MedianSeconds), Sessions + "median", "Median session length, " + Duration(context.Lengths.MedianSeconds))
            .Classes("sessions-median"));
        if (single)
            cells.Add(UI.Text(context.Rows[0].Run.Day?.ToString("D") ?? "Date unknown", Sessions + "single-date").Classes("sessions-caption"));
        else
        {
            string[] labels = ["Under 30m", "30–90m", "Over 90m"];
            var maximum = Math.Max(1, context.Lengths.Distribution.Max());
            var distribution = new List<WidgetElement>();
            for (var i = 0; i < labels.Length; i++)
            {
                var n = context.Lengths.Distribution[i];
                var ratio = (double)n / maximum;
                var id = Sessions + "length." + i;
                var fill = UI.Grid(id + ".track", [], [GridTrack.Star(Math.Max(.001, ratio)), GridTrack.Star(Math.Max(.001, 1 - ratio))],
                    UI.Stack(id + ".fill").Classes(n > 0 ? "sessions-length-fill" : "sessions-quiet-bar"))
                    .Classes("sessions-length-track").InGrid(row: i, column: 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Center);
                distribution.Add(UI.Text(labels[i], id + ".label").Classes("sessions-caption").InGrid(row: i));
                distribution.Add(fill);
                distribution.Add(UI.Text(n.ToString(), id + ".count").Classes("sessions-caption").InGrid(row: i, column: 2)
                    .AlignInGrid(GridCellAlignment.End));
            }
            cells.Add(UI.Grid(Sessions + "distribution", labels.Select(_ => GridTrack.Auto()).ToArray(),
                [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto()], distribution.ToArray()).Spacing(4, 8));
        }
        return UI.Stack(Sessions + "profile", cells.ToArray()).Classes("sessions-profile");
    }

    private static WidgetElement SessionHistoryRow(HistoryContext context, HistoryRow row, WidgetIndexedItemContext ids)
    {
        if (row.Highlight is { } child)
        {
            var label = HighlightName(child);
            var detail = $"{(child.Capture is { } capture ? CaptureType(capture) : "Unlocked")}  ·  {child.When.LocalDateTime:t}";
            var body = UI.Grid(ids.Id("highlight"), [], [GridTrack.Auto(), GridTrack.Star()],
                HighlightArt(child, ids.Id("event")).Classes("sessions-highlight-art"),
                UI.Stack(ids.Id("text"), UI.Text(label, ids.Id("title")).Classes("sessions-clock"),
                    UI.Text(detail, ids.Id("detail")).Classes("sessions-caption")).InGrid(column: 1))
                .Spacing(0, 14).Classes("sessions-highlight-row");
            return UI.ReadingSurface(ids.Id("row"), $"{label}, {detail}", ActionSurfaceOrientation.Horizontal, body).Classes("sessions-row");
        }
        var run = row.Run;
        var time = run.Value.Date is { } start ? SessionClock(start, run.Value.Seconds) : "Time unknown";
        if (run.Day.HasValue && run.Value.Seconds < 60 && run.Start / TimeSpan.TicksPerMinute == run.End / TimeSpan.TicksPerMinute)
            time = run.Value.Date!.Value.LocalDateTime.ToString("t");
        var duration = Duration(run.Value.Seconds);
        var children = new List<WidgetElement>();
        if (context.InlineHeadings && row.Heading is not null)
            children.Add(UI.Grid(ids.Id("heading"), [], [GridTrack.Auto(), GridTrack.Star()],
                UI.Text(row.Heading, ids.Id("day")).Classes("sessions-subhead"),
                UI.Divider(ids.Id("rule")).InGrid(column: 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Center))
                .Spacing(0, 12));
        var copy = new List<WidgetElement> { UI.Text(time, ids.Id("clock")).Classes("sessions-clock") };
        var captions = new List<string>();
        if (run.Value.Action is { Length: > 0 } action && !action.Equals("Play", StringComparison.OrdinalIgnoreCase) &&
            !action.Equals("Default action", StringComparison.OrdinalIgnoreCase)) captions.Add(action);
        if (captions.Count > 0) copy.Add(UI.Text(string.Join("  ·  ", captions), ids.Id("caption")).Classes("sessions-caption"));
        var expanded = context.Expanded == run.Key;
        var summary = new List<WidgetElement>
        {
            UI.Icon(PlayniteIcons.Activity, ids.Id("icon"), "Session").Classes("sessions-run-icon").AlignInGrid(GridCellAlignment.Center, GridCellAlignment.Center),
            UI.Stack(ids.Id("copy"), copy.ToArray()).InGrid(column: 1),
            UI.Text(duration, ids.Id("duration")).Classes("sessions-length").InGrid(column: 3)
                .AlignInGrid(GridCellAlignment.End, GridCellAlignment.Center),
        };
        if (run.Highlights.Count > 0)
        {
            var previews = new List<WidgetElement>();
            if (!expanded)
                foreach (var highlight in run.Highlights.Take(SessionPreviewCount))
                    previews.Add(HighlightArt(highlight, ids.Id("preview" + previews.Count))
                        .Classes(highlight.Capture is null ? "sessions-award-preview" : "sessions-capture-preview"));
            var remaining = run.Highlights.Count - previews.Count;
            if (remaining > 0) previews.Add(UI.Text(expanded ? CountLabel(remaining, "highlight") : $"+{remaining}", ids.Id("count")).Classes("sessions-caption"));
            previews.Add(UI.Text(expanded ? "▴" : "▾", ids.Id("chevron"), expanded ? "Collapse" : "Expand")
                .Classes("sessions-chevron"));
            summary.Add(UI.Row(ids.Id("previews"), previews.ToArray()).Classes("sessions-preview-strip").InGrid(column: 2));
            captions.Add(CountLabel(run.Highlights.Count, "highlight") + (expanded ? ", expanded" : ", collapsed"));
        }
        children.Add(UI.Grid(ids.Id("summary"), [], [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto(), GridTrack.Auto()],
            summary.ToArray()).Spacing(0, 14));
        var content = UI.Stack(ids.Id("content"), children.ToArray()).Classes("sessions-row-body");
        var description = $"{(run.Day is { } day ? day.ToString("D") : "Date unknown")}, {time}, {duration}" +
            (captions.Count > 0 ? ", " + string.Join(", ", captions) : "");
        return (run.Highlights.Count == 0 ? UI.ReadingSurface(ids.Id("row"), description, ActionSurfaceOrientation.Vertical, content)
            : UI.ActionSurface(Sessions + (expanded ? "collapse" : "expand"), ids.Id("row"), description, ActionSurfaceOrientation.Vertical, content)).Classes("sessions-row");
    }

    private static string HighlightName(PlayniteTimelineEvent item) => item.Capture is { } capture
        ? capture.Title ?? CaptureType(capture) : item.Achievement!.Name;

    private static WidgetElement HighlightArt(PlayniteTimelineEvent item, string id)
    {
        var label = HighlightName(item);
        if (item.Capture is { } capture) return UI.Artwork(CaptureArtwork(capture), id, label, ImageFit.Contain);
        var award = item.Achievement!;
        return award.Icon is not { } icon ? UI.Icon(PlayniteIcons.Achievements, id, label) :
            ArtworkFileContract.IsLocalPath(icon) ? UI.Artwork(StoryIcon(PlayniteAchievementEntry.KeyFor(award)), id, label, ImageFit.Contain)
            : UI.Image(icon, id, label, ImageFit.Contain);
    }
}
