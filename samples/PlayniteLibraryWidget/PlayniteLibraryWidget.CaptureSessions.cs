using System.Text.RegularExpressions;
using WidgetRail.Playnite;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteSessionReview(CaptureLibraryQuery Filter, string Revision, string Connection,
    string Reference, CaptureLibraryRun Run, string BackAction);

public sealed partial class PlayniteLibraryWidget
{
    private const string ReviewPrefix = RelatedCapturePrefix + ".review";
    private const string ReviewLinkWork = ReviewPrefix + ".link";
    private readonly WidgetIndexedCollection<CaptureLibraryQueryRequest, CaptureLibraryItem> sessionRows;

    private async Task RememberCaptureProcessAsync(string game, string? process, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(process) || Organization.CaptureProcesses.Any(entry =>
            entry.Game == game && string.Equals(entry.Process, process, StringComparison.OrdinalIgnoreCase))) return;
        // A preference only ranks suggestions. Each association still needs its own explicit or exact-session evidence.
        await SaveStateAsync(state =>
        {
            var entries = state.CaptureProcesses.Where(entry => entry.Game != game).Prepend(new(game, process)).Take(64).ToArray();
            var updated = PlayniteLibraryOrganizationPolicy.Normalize(state with { CaptureProcesses = entries });
            return updated.CaptureProcesses.Count == 0 ? PlayniteLibraryStateMutation.Reject(state) : PlayniteLibraryStateMutation.Apply(updated);
        }, token);
    }

    private static string[] TitleTerms(string name) => Regex.Matches(name, @"[\p{L}\p{N}]+")
        .Select(match => match.Value.ToLowerInvariant())
        .Where(word => word.Length is >= 4 and <= 80 &&
            word is not ("edition" or "game" or "games" or "definitive" or "ultimate" or "remastered" or "deluxe" or "collection" or "complete"))
        .Distinct(StringComparer.Ordinal).OrderByDescending(word => word.Length).Take(8).ToArray();

    private static string RunAction(RelatedCapturePage page, CaptureLibraryRun run) =>
        ReviewPrefix + ".open." + CaptureRelatedIdentity.Hash($"{page.Connection}/{page.Reference}/{page.Query.Opening}/{page.Runs!.Revision}/{run.Latest.Id}");

    private static string RunLabel(CaptureLibraryRun run) => run.Latest.Media.SourceApplication?.ApplicationName
        ?? run.Latest.Title ?? CaptureType(run.Latest);

    private static bool SameRun(CaptureLibraryRun run, CaptureLibraryItem item) =>
        run.ProcessInstance is { } instance ? instance == item.Media.SourceProcess?.Instance : run.Latest.Id == item.Id;

    private static string RunTime(CaptureLibraryRun run)
    {
        var first = DateTimeOffset.FromUnixTimeMilliseconds(run.From).ToLocalTime();
        var last = DateTimeOffset.FromUnixTimeMilliseconds(run.Through).ToLocalTime();
        return first.ToString("g") == last.ToString("g") ? first.ToString("g") :
            first.ToString("g") + " – " + last.ToString(first.Date == last.Date ? "t" : "g");
    }

    private static string MediaCounts(CaptureLibraryRun run)
    {
        var counts = new List<string>(2);
        if (run.Screenshots > 0) counts.Add(run.Screenshots == 1 ? "1 screenshot" : $"{run.Screenshots:N0} screenshots");
        if (run.Videos > 0) counts.Add(run.Videos == 1 ? "1 clip" : $"{run.Videos:N0} clips");
        return string.Join(" · ", counts);
    }

    private static void AddRunChoices(List<WidgetElement> children, RelatedCapturePage page, CaptureLibraryRunsPage runs)
    {
        bool? previousGroup = null;
        foreach (var run in runs.Items)
        {
            var suggested = run.MatchesPreferredProcess || run.MatchesSource;
            if (previousGroup != suggested)
            {
                children.Add(UI.Text(suggested ? "Suggested for this game" : "Recent captures",
                    ReviewPrefix + (suggested ? ".suggested" : ".recent")).Classes("playnite-capture-title"));
                previousGroup = suggested;
            }
            var item = run.Latest;
            var id = RelatedCapturePrefix + ".picker.item." + item.Id;
            var multiple = (long)run.Screenshots + run.Videos > 1;
            var action = multiple ? RunAction(page, run) : PickAction(page, item);
            var name = RunLabel(run);
            var time = RunTime(run);
            var counts = MediaCounts(run);
            children.Add(UI.ActionSurface(action, id, $"{(multiple ? "Review" : "Link")} {name}, {counts}, {time}",
                ActionSurfaceOrientation.Horizontal,
                UI.Artwork(CaptureArtwork(item), id + ".art", name, ImageFit.Contain).Classes("playnite-capture-row-art"),
                UI.Stack(id + ".labels", UI.Text(name, id + ".name").Classes("playnite-capture-card-title"),
                    UI.Text(time, id + ".time").Classes("playnite-library-details-meta"),
                    UI.Text(counts, id + ".count").Classes("playnite-capture-caption")).Classes("playnite-capture-card-labels"))
                .Classes("playnite-capture-card"));
        }
    }

    private WidgetIndexedCollection<CaptureLibraryQueryRequest, CaptureLibraryItem> CreateSessionRows() =>
        CreateIndexedCollection<CaptureLibraryQueryRequest, CaptureLibraryItem>(ReviewPrefix + ".source", new(new()), 0, new()
        {
            EnableSelection = true,
            ItemKey = item => new(item.Id),
            ReadRange = async (source, start, count, token) =>
                (await HostServices.CaptureLibrary.QueryAsync(source.Query, start, count, source.Revision, token)).Items,
            OnAction = (_, _, _, _) => ValueTask.CompletedTask,
            RenderItem = (_, item, row) => UI.ActionSurface(ReviewPrefix + ".toggle", row.Id("row"),
                $"{CaptureType(item)}, {CaptureTime(item)}", ActionSurfaceOrientation.Horizontal,
                UI.Artwork(CaptureArtwork(item), row.Id("art"), CaptureType(item), ImageFit.Contain).Classes("playnite-capture-row-art"),
                UI.Stack(row.Id("labels"), UI.Text(item.Title ?? CaptureType(item), row.Id("title")).Classes("playnite-capture-card-title"),
                    UI.Text(CaptureTime(item), row.Id("time")).Classes("playnite-library-details-meta")).Classes("playnite-capture-card-labels"))
                .Classes("playnite-capture-card"),
            ResolveArtwork = async (_, item, handle, token) =>
            {
                if (!item.Available || handle != CaptureArtwork(item)) return null;
                var art = await HostServices.CaptureLibrary.GetThumbnailAsync(item.Id, token);
                return new(WidgetArtworkContentType.Png, art.Bytes);
            },
        });

    private string CommitAction(PlayniteSessionReview review)
    {
        var selection = sessionRows.Selection!;
        var indices = string.Join(",", selection.Indices);
        return ReviewPrefix + ".commit." + CaptureRelatedIdentity.Hash(
            $"{review.BackAction}/{sessionRows.Descriptor.QueryGeneration}/{selection.Epoch}/{indices}");
    }

    private WidgetModal ReviewDialog(PlayniteLibraryRenderState state, PlayniteSessionReview review)
    {
        var count = sessionRows.Selection!.Count;
        var total = sessionRows.Descriptor.Count;
        var busy = Operations.IsBusy(ReviewLinkWork);
        var label = busy ? "Linking captures…" : count == 1 ? "Link 1 capture" : $"Link {count:N0} captures";
        var summary = $"{RunTime(review.Run)} · {MediaCounts(review.Run)}";
        if (total > IndexedCollectionSelection.MaximumItems) summary += $" · Select up to {IndexedCollectionSelection.MaximumItems} per batch";
        var commit = CommitAction(review);
        var content = UI.Grid(ReviewPrefix + ".body", [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto()], [GridTrack.Star()],
            UI.Text(summary, ReviewPrefix + ".summary").Classes("playnite-library-details-meta"),
            UI.CollectionList(ReviewPrefix + ".rows", sessionRows, 90, "Captures to link").InGrid(row: 1),
            UI.Row(ReviewPrefix + ".actions",
                UI.ActionSurface(commit, ReviewPrefix + ".submit", label, ActionSurfaceOrientation.Horizontal,
                    UI.ControllerGlyph(ControllerButton.X, ReviewPrefix + ".submit.hint"), UI.Text(label, ReviewPrefix + ".submit.label"))
                    .Disabled(count == 0 || busy),
                UI.Button("Back to sessions", review.BackAction, ReviewPrefix + ".back").Disabled(busy)).InGrid(row: 2))
            .Spacing(12, 0).Classes("playnite-library-details-layout");
        if (count > 0 && !busy) content = content.Shortcut(ControllerButton.X, commit, label);
        return new(ReviewPrefix + ".dialog", "Link captures to " + RelatedCaptureLabel(state.DetailsItem!.Presentation.DisplayName, 120),
            content, ReviewPrefix + ".rows", review.BackAction) { ScrollContent = false,
                StyleClass = total <= 3 ? "playnite-short-capture-dialog" : null };
    }

    private bool HandleSessionReview(string action, PlayniteLibraryRenderState state)
    {
        if (!action.StartsWith(ReviewPrefix + ".", StringComparison.Ordinal)) return state.DetailsExtras.ReviewSession is not null;
        var query = RelatedCaptureKey(state);
        if (query.PickerOffset is null || query.Achievement is not null) return true;
        if (state.DetailsExtras.ReviewSession is { } review)
        {
            if (Operations.IsBusy(ReviewLinkWork)) return true;
            if (action == review.BackAction) EndReview(state, null, false);
            else if (action == CommitAction(review) && sessionRows.Selection!.Count > 0) LinkReviewed(state, review, action);
            return true;
        }
        var page = capturePicker.GetSnapshot(query).Value;
        if (page is not { CanLink: true, Connection: not null, Reference: not null, Runs: not null } || page.Query != query) return true;
        var run = page.Runs.Items.FirstOrDefault(value => RunAction(page, value) == action);
        if (run?.ProcessInstance is null || (long)run.Screenshots + run.Videos is < 2 or > int.MaxValue) return true;
        var filter = new CaptureLibraryQuery { OriginPackageId = PlaynitePackage, Reference = new(CaptureRelatedIdentity.GameKind, page.Reference),
            SourceIntervals = [new(run.ProcessInstance, run.From, run.Through)], ExcludeReference = true, AvailableOnly = true };
        var selected = new PlayniteSessionReview(filter, page.Runs.Revision, page.Connection, page.Reference, run,
            ReviewPrefix + ".back." + CaptureRelatedIdentity.Hash(action));
        sessionRows.PublishQuery(new(filter, Revision: selected.Revision), checked(run.Screenshots + run.Videos));
        sessionRows.BeginSelection();
        sessionRows.SelectFirst(Math.Min(sessionRows.Descriptor.Count, IndexedCollectionSelection.MaximumItems));
        UpdateDetails(state.DetailsGeneration, value => value with { ReviewSession = selected, CaptureLinkMessage = null });
        return true;
    }

    private void EndReview(PlayniteLibraryRenderState state, string? message, bool closePicker)
    {
        sessionRows.EndSelection();
        UpdateDetails(state.DetailsGeneration, extras => extras with { ReviewSession = null, CaptureLinkMessage = message,
            CapturePickerOffset = closePicker ? null : extras.CapturePickerOffset, CaptureReturnId = RelatedCapturePrefix + ".link" });
        LoadDetailsSection(PlayniteDetailsTab.Captures, true);
    }

    private void LinkReviewed(PlayniteLibraryRenderState state, PlayniteSessionReview review, string submittedAction)
    {
        Operations.RunSingleFlight(ReviewLinkWork, async operation =>
        {
            var token = operation.CancellationToken;
            bool Current() => operation.IsCurrent && _model.Value.DetailsGeneration == state.DetailsGeneration &&
                _model.Value.DetailsExtras.ReviewSession == review && _model.Value.DetailsExtras.Tab == PlayniteDetailsTab.Captures;
            var applied = 0;
            var existing = 0;
            try
            {
                // Resolve all selected identities against the reviewed revision before the first write changes it.
                var items = await sessionRows.ReadSelectedAsync(token);
                if (!Current() || CommitAction(review) != submittedAction) return;
                var integration = await _application.GetCaptureIntegrationAsync(token);
                if (!Current()) return;
                if (integration?.ConnectionId != review.Connection || !Guid.TryParseExact(state.DetailsItem!.Value.SavedId, "D", out var game) ||
                    CaptureRelatedIdentity.Game(integration.ProfileId, game) != review.Reference)
                { EndReview(state, "The Playnite library changed. Review the session again.", false); return; }
                foreach (var item in items)
                {
                    if (!Current()) return;
                    var outcome = await AttachGameAsync(item, review.Reference, state.DetailsItem.Presentation.DisplayName, token, explicitSelection: true);
                    if (outcome is not (CaptureContextSaveOutcome.Saved or CaptureContextSaveOutcome.AlreadySaved))
                    {
                        EndReviewIfCurrent();
                        return;
                    }
                    if (outcome == CaptureContextSaveOutcome.Saved) applied++;
                    else existing++;
                }
                await RememberCaptureProcessAsync(review.Reference, items.FirstOrDefault()?.Media.SourceApplication?.ProcessName, token);
                if (Current()) EndReview(state, existing > 0 ? LinkCounts(applied, existing) + "." :
                    applied == 1 ? "1 capture linked." : $"{applied:N0} captures linked.", true);
            }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or WidgetIndexedSelectionException or
                PlayniteBridgeDataException or PlayniteBridgeTransportException or TimeoutException)
            {
                EndReviewIfCurrent();
            }
            void EndReviewIfCurrent()
            {
                if (Current()) EndReview(state, applied + existing == 0 ? "The session changed or could not be linked. Review the available captures again."
                    : $"{LinkCounts(applied, existing)}. Review the remaining captures to continue.", false);
            }
        }, WidgetOperationLifetime.Active);
    }
}
