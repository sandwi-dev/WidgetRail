using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record SessionTabView(WidgetElement Body, string Entry, string? Back, FocusGroupEntryRequest? Focus);

public sealed partial class PlayniteLibraryWidget
{
    private const string Sessions = DetailsPrefix + "sessions.";
    private const string SessionList = Sessions + "history";
    private const string SessionRange = Sessions + "range";
    private const string SessionBack = Sessions + "back";
    private const string SessionChartBack = Sessions + "chart-back";
    private sealed record SessionInput(TimelineInput Sources, bool All, TimeZoneInfo Zone);
    private const int SessionPreviewCount = 3;
    private sealed record HistoryRow(SessionOccurrence Run, string? Heading, PlayniteTimelineEvent? Highlight = null, string? HighlightKey = null)
    {
        internal string Key => HighlightKey ?? Run.Key;
    }
    // The collection context is the immutable projection for its exact game and date range.
    private sealed record HistoryContext(SessionInput? Input, PlayniteSessionHistory? History, string? Period,
        HistoryRow[] Rows, IndexedCollectionGroup[] Groups, string? Expanded, SessionLengths Lengths)
    {
        internal long Opening => Input?.Sources.Game.Opening ?? 0;
        internal bool InlineHeadings => Groups.Length > IndexedCollectionLimits.MaximumGroups;
        internal HistoryRow[] VisibleRows { get; init; } = Rows;
        internal IndexedCollectionGroup[] VisibleGroups { get; init; } = Groups;
        internal HistoryContext Expand(string? key)
        {
            var parent = key is null ? -1 : Array.FindIndex(Rows, row => row.Key == key && row.Run.Highlights.Count > 0);
            if (parent < 0) return Expanded is null ? this : this with { Expanded = null, VisibleRows = Rows, VisibleGroups = Groups };
            var run = Rows[parent].Run;
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var children = run.Highlights.Select(h =>
            {
                var identity = h.Capture is { } c ? "capture." + c.Id : "award." + PlayniteAchievementEntry.KeyFor(h.Achievement!);
                var hash = WidgetRail.Playnite.CaptureRelatedIdentity.Hash(FormattableString.Invariant($"{run.Key}/{h.When.UtcTicks}/{identity}"));
                var occurrence = occurrences.GetValueOrDefault(hash);
                occurrences[hash] = occurrence + 1;
                return new HistoryRow(run, null, h, "event." + hash + "." + occurrence.ToString(CultureInfo.InvariantCulture));
            });
            var groups = Groups.ToArray();
            var end = 0;
            for (var i = 0; i < groups.Length; i++)
            {
                end += groups[i].Count;
                if (parent >= end) continue;
                groups[i] = groups[i] with { Count = groups[i].Count + run.Highlights.Count };
                break;
            }
            return this with { Expanded = key, VisibleRows = [.. Rows.AsSpan(0, parent + 1), .. children, .. Rows.AsSpan(parent + 1)], VisibleGroups = groups };
        }
        internal static readonly HistoryContext Empty = new(null, null, null, [], [], null, SessionLengths.From([]));
    }
    private readonly WidgetQueryResource<SessionInput, PlayniteSessionHistory> sessionAnalysis;
    private WidgetLoadedCollection<HistoryContext, HistoryRow>? sessionHistoryRows;

    private void ClearSessionHistory()
    {
        lock (_gate)
        {
            sessionAnalysis.Reset(invalidate: false);
            sessionHistoryRows?.Publish("closed", HistoryContext.Empty, HistoryContext.Empty.Rows);
        }
    }

    private SessionTabView? PrepareSessions(PlayniteLibraryRenderState state)
    {
        var extras = state.DetailsExtras;
        if (extras.Activity is not { Available: true, Sessions.Count: > 0 } activity) return null;
        var key = DetailsKey(state);
        var input = new SessionInput(new(key, activity, extras.Achievements, timelineCaptures.GetSnapshot(key).Value),
            extras.SessionsAll, TimeZoneInfo.Local);
        var previous = sessionHistoryRows?.Context ?? HistoryContext.Empty;
        var changed = previous.Input != input;
        string? analysisMessage = null;
        PlayniteSessionHistory? history = previous.History;
        if (changed)
        {
            // Small histories fit in the immediate presentation. Large imports compute on
            // the worker pool under the SDK's query and navigation cancellation authority.
            if (activity.Sessions.Count < 512)
                history = new(activity, input.All, input.Sources.Unlocks, input.Sources.Captures ?? [], input.Zone);
            else
            {
                var snapshot = sessionAnalysis.GetSnapshot(input);
                if (snapshot.Status == WidgetResourceStatus.NotLoaded && LifecycleState == WidgetLifecycleState.Interactive)
                    sessionAnalysis.EnsureLoaded(input, _navigation.Value.RouteCancellationToken);
                history = snapshot.Value;
                if (history is null && previous.Input is { } old && old.Sources.Game == key &&
                    ReferenceEquals(old.Sources.Captures, input.Sources.Captures))
                {
                    analysisMessage = snapshot.Error?.Message;
                    history = previous.History;
                    input = old;
                    changed = false;
                }
                if (history is null)
                {
                    const string pendingId = Sessions + "preparing";
                    var message = snapshot.Error?.Message ?? "Preparing session history…";
                    return new(UI.ReadingSurface(pendingId, message, ActionSurfaceOrientation.Vertical,
                        UI.Text(message, pendingId + ".label")), pendingId, null, null);
                }
            }
        }
        if (history is null) return null;
        var filter = history.Periods.FirstOrDefault(p => p.Key == extras.SessionPeriodKey && p.Visits > 0);
        var context = previous;
        if (changed || previous.Period != filter?.Key)
        {
            var source = filter is null ? history.RangeRows : history.RangeRows.Where(filter.Includes);
            var groups = source.GroupBy(run => run.Day).ToArray();
            var rows = new List<HistoryRow>();
            var headers = new List<IndexedCollectionGroup>();
            foreach (var day in groups)
            {
                var seconds = (long)Math.Min(long.MaxValue, day.Sum(r => (decimal)r.Value.Seconds));
                var heading = day.Key.HasValue ? JournalDate(day.Key.Value) : "Date unknown";
                heading += $"   ·   {PlayniteLibraryDetailsPresentation.Duration(seconds)} across {CountLabel(day.Count(), "session")}";
                headers.Add(new(day.Key?.ToString("'day.'yyyyMMdd", CultureInfo.InvariantCulture) ?? "undated", heading, day.Count())
                    { ShowHeaderRule = true });
                var first = true;
                foreach (var run in day) { rows.Add(new(run, first ? heading : null)); first = false; }
            }
            context = new HistoryContext(input, history, filter?.Key, rows.ToArray(), headers.ToArray(), null,
                filter is null ? history.Lengths : SessionLengths.From(rows.Select(r => r.Run))).Expand(extras.ExpandedSession);
        }
        else if (extras.ExpandedSession != previous.Expanded) context = previous.Expand(extras.ExpandedSession);
        var identity = $"sessions.{key.Opening}";
        if (sessionHistoryRows is null)
            sessionHistoryRows = CreateLoadedCollection<HistoryContext, HistoryRow>(SessionList + ".source", identity, context, context.VisibleRows,
                new() { ItemKey = row => new(row.Key), RenderItem = SessionHistoryRow, OnAction = ToggleSession,
                    ResolveArtworkSource = SessionArtworkAsync, PreserveOccurrences = true });
        else if (!ReferenceEquals(context, previous)) sessionHistoryRows.Publish(identity, context, context.VisibleRows);
        var latest = history.Periods.LastOrDefault(p => p.Visits > 0);
        var chartEntry = filter is not null ? PeriodId(filter) : latest is null ? SessionList : PeriodId(latest);
        var list = UI.CollectionList(SessionList, sessionHistoryRows, 76, "Play session history")
            .WithDirectionalAnchor(FocusAnchorPoint.Leading, 20);
        if (filter is not null || chartEntry != SessionList) list = list.FocusUp(filter is not null ? SessionChartBack : chartEntry);
        if (!context.InlineHeadings) list = list.Grouped(context.VisibleGroups);
        var filtered = filter is not null;
        var body = SessionDashboard(history, context, filter, list.Classes("sessions-history"), extras.ActivityError ?? analysisMessage);
        var expanded = context.Expanded is not null;
        var back = expanded || filtered ? SessionBack : null;
        FocusGroupEntryRequest? focus = null;
        if (extras.SessionFocus is { } request && extras.SessionFocusTarget is { } target && !extras.ChoosingCompletion && !extras.ConfirmUninstall)
        {
            if (target == SessionList) focus = request.EnterCollection(sessionHistoryRows, SessionList);
            else if (target.StartsWith("run.", StringComparison.Ordinal))
            {
                var i = Array.FindIndex(context.VisibleRows, r => r.Key == target);
                if (i >= 0) focus = request.ReturnToCollection(sessionHistoryRows, SessionList, sessionHistoryRows.FocusTarget(SessionList, new(target), i));
            }
            else if (!filtered && (target == SessionRange || history.Periods.Any(p => PeriodId(p) == target)))
                focus = request.FocusElement(DetailsPrefix + "frame", target);
        }
        return new(body, chartEntry, back, focus);
    }

    private static string CountLabel(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
    private static string PeriodId(SessionPeriod period) => Sessions + "period." + period.Key;

    private bool HandleSessionAction(PlayniteLibraryRenderState state, string action)
    {
        var extras = state.DetailsExtras;
        if (extras.Tab != PlayniteDetailsTab.Activity || sessionHistoryRows?.Context is not { Input: { } input, History: { } history } || input.Sources.Game != DetailsKey(state)) return true;
        if (action == SessionRange)
            UpdateDetails(state.DetailsGeneration, e => e with { SessionsAll = !history.All, SessionPeriodKey = null,
                ExpandedSession = null, SessionFocus = CreateFocusRequest(), SessionFocusTarget = SessionRange });
        else if (action == SessionChartBack || action == SessionBack)
        {
            var expanded = action == SessionBack && extras.ExpandedSession is { } key &&
                sessionHistoryRows.Context.Rows.Any(r => r.Run.Key == key && r.Run.Highlights.Count > 0);
            UpdateDetails(state.DetailsGeneration, e => e with { SessionPeriodKey = expanded ? e.SessionPeriodKey : null,
                SessionFocusTarget = expanded ? e.ExpandedSession : Sessions + "period." + e.SessionPeriodKey,
                ExpandedSession = null, SessionFocus = CreateFocusRequest() });
        }
        else if (history.Periods.FirstOrDefault(p => PeriodId(p) == action && p.Visits > 0) is { } period)
            UpdateDetails(state.DetailsGeneration, e => e with { SessionPeriodKey = period.Key, ExpandedSession = null,
                SessionFocus = CreateFocusRequest(), SessionFocusTarget = SessionList });
        return true;
    }

    private async ValueTask ToggleSession(HistoryContext context, HistoryRow row, WidgetActionEvent action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = _model.Value;
        if (LifecycleState != WidgetLifecycleState.Interactive || state.DetailsItem is null || state.DetailsOpening != context.Opening ||
            state.DetailsExtras.Tab != PlayniteDetailsTab.Activity || !ReferenceEquals(sessionHistoryRows?.Context, context)) return;
        if (action.ActionId != Sessions + "expand" && action.ActionId != Sessions + "collapse") { await HandleActionAsync(action, null, token); return; }
        if (row.Highlight is null && row.Run.Highlights.Count > 0)
            UpdateDetails(state.DetailsGeneration, e => e with { ExpandedSession = action.ActionId == Sessions + "expand" ? row.Key : null,
                SessionFocus = CreateFocusRequest(), SessionFocusTarget = row.Key });
    }

    private async ValueTask<WidgetArtworkSource?> SessionArtworkAsync(HistoryContext context, HistoryRow row, WidgetArtworkHandle handle, CancellationToken token)
    {
        IEnumerable<PlayniteTimelineEvent> visible = row.Highlight is { } child ? [child] :
            row.Run.Key == context.Expanded ? [] : row.Run.Highlights.Take(SessionPreviewCount);
        foreach (var highlight in visible)
        {
            if (highlight.Achievement is { Icon: { } icon } achievement && ArtworkFileContract.IsLocalPath(icon) &&
                StoryIcon(PlayniteAchievementEntry.KeyFor(achievement)) == handle)
                return new WidgetFileArtwork(icon, context.Opening.ToString(CultureInfo.InvariantCulture));
            if (highlight.Capture is { } capture && CaptureArtwork(capture) == handle)
            {
                try { return new WidgetEncodedArtwork(WidgetArtworkContentType.Png, (await HostServices.CaptureLibrary.GetThumbnailAsync(capture.Id, token)).Bytes); }
                catch (WidgetCapabilityException) { return null; }
            }
        }
        return null;
    }
}
