using System.Globalization;
using WidgetRail.Playnite;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string TimelineId = DetailsPrefix + "timeline";
    private sealed record TimelineInput(DetailsQuery Game, PlayniteActivity? Sessions, PlayniteAchievements? Unlocks,
        IReadOnlyList<CaptureLibraryItem>? Captures);
    private sealed record GameTimelineContent(TimelineInput Input, PlayniteGameHistory History, IndexedCollectionGroup[] Groups);
    private readonly WidgetQueryResource<DetailsQuery, IReadOnlyList<CaptureLibraryItem>> timelineCaptures;
    private WidgetLoadedCollection<GameTimelineContent, JournalEntry>? timelineRows;

    private async ValueTask<IReadOnlyList<CaptureLibraryItem>> ReadTimelineCapturesAsync(DetailsQuery game, CancellationToken token)
        => await ReadGameCapturesAsync(game.SavedId, 48, token);
    private async ValueTask<IReadOnlyList<CaptureLibraryItem>> ReadGameCapturesAsync(string gameId, int count, CancellationToken token)
    {
        if (!Guid.TryParseExact(gameId, "D", out var id)) return [];
        var access = await HostServices.Permissions.CheckAsync([WidgetCaptureLibraryCapabilities.ReadPermission], token);
        if (!access.Any(permission => permission.CapabilityId == WidgetCaptureLibraryCapabilities.ReadPermission &&
            permission.State == WidgetPermissionState.Granted)) return [];
        var integration = await _application.GetCaptureIntegrationAsync(token);
        if (integration is null) return [];
        return await QueryGameCapturesAsync(integration.ProfileId, id, count, token);
    }

    private async ValueTask<IReadOnlyList<CaptureLibraryItem>> QueryGameCapturesAsync(string profile, Guid id, int count, CancellationToken token)
    {
        var filter = new CaptureLibraryQuery { OriginPackageId = PlaynitePackage, AvailableOnly = true,
            Reference = new(CaptureRelatedIdentity.GameKind, CaptureRelatedIdentity.Game(profile, id)) };
        return (await HostServices.CaptureLibrary.QueryAsync(filter, count: count, cancellationToken: token)).Items;
    }

    private WidgetElement PrepareGameTimeline(PlayniteLibraryRenderState state)
    {
        var game = DetailsKey(state);
        var extras = state.DetailsExtras;
        var media = timelineCaptures.GetSnapshot(game);
        var input = new TimelineInput(game, extras.Activity, extras.Achievements, media.Value);
        if (timelineRows?.Context.Input != input)
        {
            var history = PlayniteGameTimeline.Build(input.Sessions, input.Unlocks, input.Captures ?? []);
            var rows = JournalEntries(history.Days);
            if (timelineRows?.Context.Input.Game == game && timelineRows.Items.FirstOrDefault(row => row.Expanded) is { } previous &&
                previous.Captures.FirstOrDefault(item => item.Id == previous.SelectedCaptureId) is { } selected)
            {
                var index = Array.FindIndex(rows, row => row.Captures.Any(item => item.Id == selected.Id && item.Revision == selected.Revision));
                if (index >= 0) rows[index] = rows[index] with { Expanded = true, SelectedCaptureId = selected.Id };
            }
            var groups = rows.GroupBy(row => row.Day.Date).Select(day =>
                new IndexedCollectionGroup(day.First().Day.Key, JournalDate(day.Key), day.Count()) { ShowHeaderRule = true }).ToArray();
            var context = new GameTimelineContent(input, history, groups);
            var identity = game.Opening.ToString(CultureInfo.InvariantCulture);
            if (timelineRows is null)
                timelineRows = CreateLoadedCollection(TimelineId + ".source", identity, context, rows,
                    new WidgetLoadedCollectionOptions<GameTimelineContent, JournalEntry>
                    {
                        ItemKey = row => new(row.Key), PreserveOccurrences = true, RenderItem = TimelineCard,
                        OnAction = ActivateTimelineRow, ResolveArtworkSource = ReadJournalArtworkAsync,
                    });
            else timelineRows.Publish(identity, context, rows);
        }
        if (timelineRows.Items.Count > 0)
            return UI.CollectionList(TimelineId, timelineRows, 120, "Recent activity")
                .WithDirectionalAnchor(FocusAnchorPoint.Leading, 25)
                .Grouped(timelineRows.Context.Groups).Classes("game-journal-list");
        if (extras.ActivityLoading || extras.AchievementsLoading || media.Status == WidgetResourceStatus.Loading)
            return UI.LoadingIndicator(TimelineId + ".loading", size: LoadingIndicatorSize.Compact);
        return UI.Text(extras.ActivityError ?? extras.AchievementsError ?? media.Error?.Message ??
            "No recent activity for this game yet.", TimelineId + ".empty").Classes("playnite-library-details-meta");
    }

    private sealed record JournalEntry(string Key, PlayniteActivityDay Day, DateTimeOffset When)
    {
        internal PlayniteAchievement? Award { get; init; }
        internal CaptureLibraryItem[] Captures { get; init; } = [];
        internal string? SelectedCaptureId { get; init; }
        internal bool Expanded { get; init; }
    }

    private static JournalEntry[] JournalEntries(IReadOnlyList<PlayniteActivityDay> days)
    {
        var entries = new List<JournalEntry>();
        foreach (var day in days)
        {
            var batch = new List<JournalEntry>();
            if (day.Sessions.Count > 0)
                batch.Add(new(day.Key + ".sessions", day, day.Sessions.Max(s => s.Date!.Value)));
            foreach (var chunk in day.Events.Where(e => e.Capture is not null).Chunk(MediaPlayerDefinition.MaximumGalleryItems))
                batch.Add(new(day.Key + ".photos." + chunk[^1].Capture!.Id, day, chunk[0].When)
                    { Captures = chunk.Select(e => e.Capture!).ToArray() });
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var earned in day.Events.Where(e => e.Achievement is not null))
            {
                var identity = PlayniteAchievementEntry.KeyFor(earned.Achievement!);
                var occurrence = occurrences.GetValueOrDefault(identity);
                occurrences[identity] = occurrence + 1;
                batch.Add(new(HomeEventIds.KeyedId("award", $"{day.Key}/{identity}/{occurrence}"), day, earned.When) { Award = earned.Achievement });
            }
            entries.AddRange(batch.OrderByDescending(entry => entry.When).ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Take(PlayniteGameTimeline.MaximumRows - entries.Count));
            if (entries.Count == PlayniteGameTimeline.MaximumRows) break;
        }
        return entries.ToArray();
    }

    private static WidgetElement TimelineCard(GameTimelineContent context, JournalEntry entry, WidgetIndexedItemContext ids)
    {
        var date = JournalDate(entry.Day.Date);
        var children = new List<WidgetElement>();
        string summary;
        if (entry.Award is { } award)
        {
            summary = "Unlocked " + award.Name;
            children.Add(AwardPanel(ids, "award", award.Name, award.Description, award.Percent, award.Icon, StoryIcon(entry.Key)));
        }
        else if (entry.Captures.Length > 0)
        {
            var items = entry.Captures.Select(item => new CaptureStripItem(item, CaptureArtwork(item),
                CaptureType(item) + ", " + CaptureTime(item), TimelineId + ".view." + item.Id)).ToArray();
            return UI.CaptureStrip(ids.Id("row"), date + ", " + CaptureSeriesTitle(entry.Captures), items,
                entry.SelectedCaptureId, entry.Expanded, TimelineId + ".close");
        }
        else
        {
            var sessions = entry.Day.Sessions;
            var total = (long)Math.Min(long.MaxValue, sessions.Sum(session => (decimal)session.Seconds));
            var duration = PlayniteLibraryDetailsPresentation.Duration(total);
            var label = sessions.Count == 1 ? "Play session" : $"{sessions.Count} sessions";
            summary = $"{label}, {duration} played";
            children.Add(PlayDayPanel(entry, ids, label, duration));
            if (sessions.Count == 1 && context.History.LatestSession is { } latest && sessions[0] == latest.Session &&
                latest.Events.FirstOrDefault(e => e.Achievement is not null)?.Achievement is { } reward)
                children.Add(UI.Row(ids.Id("earned"), UI.Icon(PlayniteIcons.Achievements, ids.Id("earned-icon"), "Achievement")
                    .Classes("playnite-journal-trophy"), UI.Text("Earned during play · " + reward.Name, ids.Id("earned-name"))
                    .Classes("story-clock")).Classes("journal-linked-unlock"));
        }
        return UI.ActionSurface(TimelineId + ".open", ids.Id("row"), date + ", " + summary,
            ActionSurfaceOrientation.Vertical, children.ToArray()).Classes("story-journal-entry");
    }

    private static string JournalDate(DateOnly date) => date.ToString(date.Year == DateTime.Today.Year ? "dddd, MMMM d" : "MMMM d, yyyy");

    private static WidgetElement PlayDayPanel(JournalEntry entry, WidgetIndexedItemContext ids, string label, string duration)
    {
        var day = entry.Day;
        var slots = new bool[12];
        foreach (var session in day.Sessions)
        {
            var start = session.Date!.Value;
            var local = start.ToLocalTime();
            var finish = session.Seconds <= (DateTimeOffset.MaxValue.UtcTicks - start.UtcTicks) / TimeSpan.TicksPerSecond
                ? start.ToUniversalTime().AddSeconds(session.Seconds).ToLocalTime() : DateTimeOffset.MaxValue;
            var last = finish.Date > local.Date ? 11 : Math.Clamp((int)Math.Ceiling(finish.TimeOfDay.TotalHours / 2) - 1, local.Hour / 2, 11);
            Array.Fill(slots, true, local.Hour / 2, last - local.Hour / 2 + 1);
        }
        var bands = slots.Select((played, i) => UI.Stack(ids.Id("band" + i)).Classes(played ? "journal-band-played" : "journal-band")
            .InGrid(column: i)).ToArray();
        var startTimes = day.Sessions.Count == 1 ? SessionClock(entry.When, day.Sessions[0].Seconds) : "Play windows";
        var graph = UI.Grid(ids.Id("graph"), [GridTrack.Auto(), GridTrack.Auto(), GridTrack.Auto()],
            [GridTrack.Star(), GridTrack.Star(), GridTrack.Star()],
            UI.Text(startTimes, ids.Id("window")).Classes("story-clock").InGrid(columnSpan: 3),
            UI.Grid(ids.Id("bands"), [], slots.Select(_ => GridTrack.Star()).ToArray(), bands).Spacing(0, 2)
                .Classes("journal-bands").InGrid(row: 1, columnSpan: 3),
            UI.Text("00:00", ids.Id("midnight")).Classes("story-credit-time").InGrid(row: 2),
            UI.Text("12:00", ids.Id("noon")).Classes("story-credit-time").InGrid(row: 2, column: 1).AlignInGrid(GridCellAlignment.Center),
            UI.Text("24:00", ids.Id("end")).Classes("story-credit-time").InGrid(row: 2, column: 2).AlignInGrid(GridCellAlignment.End)).Spacing(4, 0);
        return UI.Grid(ids.Id("session"), [], [GridTrack.Auto(), GridTrack.Star()],
            UI.Stack(ids.Id("duration-block"), UI.Text(label, ids.Id("label")).Classes("story-time-label"),
                UI.Text(duration, ids.Id("duration")).Classes("story-duration")).Classes("journal-duration"),
            graph.InGrid(column: 1).AlignInGrid(GridCellAlignment.Stretch, GridCellAlignment.Center)).Spacing(0, 18).Classes("journal-session");
    }

    private async ValueTask<WidgetArtworkSource?> ReadJournalArtworkAsync(GameTimelineContent context, JournalEntry entry,
        WidgetArtworkHandle handle, CancellationToken token)
    {
        if (entry.Award?.Icon is { } path && handle == StoryIcon(entry.Key) && ArtworkFileContract.IsLocalPath(path))
            return new WidgetFileArtwork(path, context.Input.Game.Opening.ToString(CultureInfo.InvariantCulture));
        var capture = entry.Captures.FirstOrDefault(item => CaptureArtwork(item) == handle);
        if (capture is null) return null;
        try { return new WidgetEncodedArtwork(WidgetArtworkContentType.Png, (await HostServices.CaptureLibrary.GetThumbnailAsync(capture.Id, token)).Bytes); }
        catch (WidgetCapabilityException) { return null; }
    }

    private async ValueTask ActivateTimelineRow(GameTimelineContent context, JournalEntry row, WidgetActionEvent action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = _model.Value;
        if (LifecycleState != WidgetLifecycleState.Interactive || state.DetailsItem is null || DetailsKey(state) != context.Input.Game ||
            state.DetailsExtras.Tab != PlayniteDetailsTab.Description || !ReferenceEquals(context, timelineRows?.Context)) return;
        if (row.Captures.Length > 0)
        {
            var selected = row.Captures.FirstOrDefault(item => action.ActionId == TimelineId + ".view." + item.Id);
            if (selected is null && (action.ActionId != TimelineId + ".close" || !row.Expanded)) return;
            var currentRows = timelineRows!.Items;
            var index = currentRows.ToList().FindIndex(entry => ReferenceEquals(entry, row));
            if (index < 0) return;
            var rows = currentRows.Select(entry =>
            {
                if (ReferenceEquals(entry, row)) return entry with { Expanded = selected is not null, SelectedCaptureId = selected?.Id ?? entry.SelectedCaptureId };
                return entry.Expanded ? entry with { Expanded = false } : entry;
            }).ToArray();
            timelineRows.Publish(context.Input.Game.Opening.ToString(CultureInfo.InvariantCulture), context, rows);
            UpdateDetails(state.DetailsGeneration, extras => extras with { TimelineFocus = CreateFocusRequest(), TimelineFocusKey = row.Key });
            return;
        }
        if (action.ActionId != TimelineId + ".open")
        {
            await HandleActionAsync(action, null, token);
            return;
        }
        var tab = row.Award is not null ? PlayniteDetailsTab.Achievements : PlayniteDetailsTab.Activity;
        UpdateDetails(state.DetailsGeneration, extras => extras with { Tab = tab, Page = 0, TabFocus = CreateFocusRequest(),
            AchievementSearch = row.Award?.Name is { } name ? name[..Math.Min(128, name.Length)] : extras.AchievementSearch });
        LoadDetailsSection(tab);
    }
}
