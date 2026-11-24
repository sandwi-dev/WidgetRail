using System.Text.Json;
using WidgetRail.Playnite;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private static string CardId(CaptureLibraryItem item) => RelatedCapturePrefix + ".item." + item.Id;
    private static WidgetArtworkHandle CaptureArtwork(CaptureLibraryItem item) => new("playnite.capture."+item.Id+"."+item.Revision);
    private static string ViewAction(RelatedCapturePage page, CaptureLibraryItem item) =>
        RelatedCapturePrefix + ".view." + CaptureRelatedIdentity.Hash($"{page.Connection}/{page.Reference}/{page.Query.Opening}/{item.Id}/{item.Revision}");

    private static string? CaptureEntry(RelatedCapturePage? page)
    {
        if (page?.NeedsPermission == true) return RelatedCapturePrefix+".allow";
        if (page?.Reference is null) return null;
        return page.Items.FirstOrDefault(item=>item.Available) is { } first ? CardId(first) :
            RelatedCapturePrefix + (page.Query.Achievement is null ? ".link" : ".all");
    }

    private static RemovedCaptureLink? AvailableUndo(RelatedCapturePage page, PlayniteDetailsExtras extras) =>
        page.CanLink && extras.RemovedLink is { } undo && undo.Connection == page.Connection && undo.Reference == page.Reference ? undo : null;

    private WidgetElement RenderRelatedCaptures(PlayniteLibraryRenderState state,WidgetResourceSnapshot<RelatedCapturePage> snapshot,string tab)
    {
        const string id = RelatedCapturePrefix;
        var page = snapshot.Value;
        var children = new List<WidgetElement>();
        if (snapshot.Error is { } error)
            children.Add(PlayniteLibraryDetailsPresentation.ReadError(id + ".read-error", error.Message, id + ".retry"));
        if (page?.NeedsPermission == true)
        {
            children.Add(UI.Text("See screenshots and clips saved for this game.",id+".intro").Classes("playnite-library-details-meta"));
            children.Add(UI.Button("Show captures",id+".allow",id+".allow").FocusUp(tab));
        }
        else if (page?.Reference is null)
        {
            if (snapshot.Error is null)
                children.Add(UI.Text(page is null ? "Loading captures…" : "Update WidgetRail for Playnite to browse game captures.", id + ".status"));
        }
        else
        {
            var items = page.Items.Where(item => item.Available).ToArray();
            ButtonElement Action(ButtonElement button) => items.Length == 0 ? button.FocusUp(tab) : button;
            var actions = new List<WidgetElement>();
            if (AvailableUndo(page, state.DetailsExtras) is { } undo)
                actions.Add(Action(UI.Button("Undo unlink", UndoLinkAction(undo.Change), id + ".undo")));
            if (state.DetailsExtras.CaptureAchievementName is { } achievement)
            {
                children.Add(UI.Text(achievement,id+".achievement").Classes("playnite-capture-title"));
                actions.Add(Action(UI.Button("All game captures",id+".all",id+".all")));
            }
            if (page.Query.Achievement is null)
                actions.Add(Action(UI.Button(Operations.IsBusy(OpenCapturePickerWork) ? "Finding captures…" :
                    page.CanLink ? "Link captures…" : "Allow capture linking",id+".link",id+".link")));
            if (page.Total > page.Items.Count)
                actions.Add(Action(UI.Button($"Browse all {page.Total:N0} captures", id + ".linked", id + ".linked")));
            if (page.Total > 0)
                actions.Add(Action(UI.Button("Open Capture Library",id+".browse",id+".browse"))
                    .OpenIntent(WidgetIntentContracts.CaptureLibraryV2,JsonSerializer.SerializeToElement(new { originPackageId=PlaynitePackage,
                        referenceKind=page.Kind,referenceId=page.Reference,referenceLabel=RelatedCaptureLabel(state.DetailsExtras.CaptureAchievementName ?? page.Query.Name) })));
            if (items.Length == 0)
            {
                children.Add(UI.Text(Operations.IsBusy(SessionLinks(page.Query.Opening)) ? "Looking for game captures…" :
                    page.Total > 0 ? "Capture files are unavailable" : "No linked captures yet",id+".empty").Classes("playnite-capture-title"));
                if (page.Query.Achievement is null)
                    children.Add(UI.Text(page.CanLink ? "Choose a screenshot or clip from Capture Library to add it here." :
                        "Allow linking to find captures from your Playnite sessions.",id+".help").Classes("playnite-library-details-meta"));
            }
            if (items.Length > 0)
            {
                var tiles = items.Select(item => LinkedCard(page, item)).ToArray();
                tiles[0] = tiles[0].FocusUp(tab);
                children.Add(UI.ResponsiveGrid(id + ".cards", 220, 2, tiles).Classes("playnite-capture-grid"));
            }
            if (actions.Count > 0)
                children.Add(UI.Row(id+".actions",actions.ToArray()).Classes("playnite-capture-header-actions"));
            if (state.DetailsExtras.CaptureLinkMessage is { } feedback)
                children.Add(UI.Text(feedback,id+".link-feedback").Classes("playnite-library-details-meta"));

        }
        return UI.Stack(id+".content",children.ToArray()).Classes("playnite-capture-content");
    }

    private static string CaptureType(CaptureLibraryItem item) => item.Media.ContentType == "video/mp4" ? "Video clip" : "Screenshot";
    private static ActionSurfaceElement LinkedCard(RelatedCapturePage page, CaptureLibraryItem item)
    {
        var card = page.Query.BrowseLinked ? CaptureRow(item, ViewAction(page,item), pick:false) : LinkedTile(page,item);
        return page.CanLink && page.Kind == CaptureRelatedIdentity.GameKind && page.Revision is not null && item.Available && item.ContextCount > 0
            ? card.ContextAction(UnlinkAction(page, item), "Unlink from this game").ContextMenuShortcut(ControllerButton.Menu)
            : card;
    }

    private static ActionSurfaceElement LinkedTile(RelatedCapturePage page, CaptureLibraryItem item)
    {
        var id = CardId(item);
        var kind = CaptureType(item);
        var title = string.IsNullOrWhiteSpace(item.Title) ? kind : item.Title;
        var local = DateTimeOffset.FromUnixTimeMilliseconds(item.CapturedAtUnixMilliseconds ?? item.SavedAtUnixMilliseconds).ToLocalTime();
        var stamp = $"{EventDay(local.DateTime)}, {local:t}";
        var format = $"{item.Media.Width} × {item.Media.Height}";
        var picture = new List<WidgetElement> {
            UI.Artwork(CaptureArtwork(item), id + ".art", title, ImageFit.Cover).Classes("story-photo")
        };
        if (item.Media.ContentType == "video/mp4")
            picture.Add(UI.Text(PlayniteLibraryDetailsPresentation.Duration((long)item.Media.DurationSeconds), id + ".duration")
                .Classes("story-photo-note").AlignInGrid(GridCellAlignment.End, GridCellAlignment.End));
        var labels = new List<WidgetElement> { UI.Text(title, id + ".name").Classes("playnite-capture-card-title"),
            UI.Text(stamp, id + ".time").Classes("story-credit-time"),
            UI.Text(format, id + ".format").Classes("playnite-library-details-meta") };
        var session = page.LinkedRuns?.FirstOrDefault(run => run.Latest.Id == item.Id);
        if (session is not null)
        {
            if (session == page.LinkedRuns![0]) labels.Add(UI.Text("Latest session",id+".session").Classes("playnite-capture-caption"));
            labels.Add(UI.Text(RunTime(session) + " · " + MediaCounts(session),id+".session-summary").Classes("playnite-library-details-meta"));
        }
        var copy = UI.Stack(id + ".labels",labels.ToArray()).Classes("home-digest-credit");
        var panel = UI.Grid(id + ".layout", [], [GridTrack.Star(1.15), GridTrack.Star()],
            UI.Grid(id + ".picture", [], [], picture.ToArray()).Classes("playnite-linked-picture"), copy.InGrid(column: 1))
            .Classes("playnite-capture-tile", "story-card-material");
        return UI.ActionSurface(ViewAction(page, item), id, $"View {title}, {kind}, {CaptureTime(item)}, {format}",
            ActionSurfaceOrientation.Vertical, panel).Classes("story-card");

    }
    private static string CaptureTime(CaptureLibraryItem item) => DateTimeOffset.FromUnixTimeMilliseconds(
        item.CapturedAtUnixMilliseconds ?? item.SavedAtUnixMilliseconds).ToLocalTime().ToString("g");

    private static ActionSurfaceElement CaptureRow(CaptureLibraryItem item,string action,bool pick)
    {
        var id = pick ? RelatedCapturePrefix + ".picker.item." + item.Id : CardId(item);
        var title = item.Title ?? (pick ? item.Media.SourceApplication?.ApplicationName : null) ?? CaptureType(item);
        var dimensions = $"{item.Media.Width} × {item.Media.Height}";
        var detail = (item.Media.DurationSeconds > 0 ? TimeSpan.FromSeconds(item.Media.DurationSeconds).ToString(@"m\:ss")+" · " : "")+dimensions;
        var copy = new List<WidgetElement>();
        if (title != CaptureType(item))
            copy.Add(UI.Text(CaptureType(item),id+".kind").Classes("playnite-capture-caption"));
        copy.Add(UI.Text(title,id+".name").Classes("playnite-capture-card-title"));
        copy.Add(UI.Text(CaptureTime(item),id+".time").Classes("playnite-library-details-meta"));
        copy.Add(UI.Text(detail,id+".format").Classes("playnite-library-details-meta"));
        var labels = UI.Stack(id+".labels",copy.ToArray()).Classes("playnite-capture-card-labels");
        return UI.ActionSurface(action,id,(pick ? "Link " : "View ") + title + ", " + CaptureTime(item),ActionSurfaceOrientation.Horizontal,
            UI.Artwork(CaptureArtwork(item),id+".art",title,ImageFit.Contain).Classes("playnite-capture-row-art"),labels)
            .Classes("playnite-capture-card");
    }

    private async ValueTask<WidgetArtworkSource?> ReadCaptureArtworkAsync(WidgetArtworkHandle handle,CancellationToken token)
    {
        var state = WithDetailsResources(_model.Value);
        if (_navigation.Value.Route == PlayniteLibraryRoute.Library && PlayingId(state) is { } game &&
            LatestPlayingCapture(game) is { Available: true } latest && CaptureArtwork(latest) == handle)
        {
            try { return new WidgetEncodedArtwork(WidgetArtworkContentType.Png, (await HostServices.CaptureLibrary.GetThumbnailAsync(latest.Id, token)).Bytes); }
            catch (WidgetCapabilityException) { return null; }
        }
        if (state.DetailsItem is null || state.DetailsExtras.Tab != PlayniteDetailsTab.Captures) return null;
        var query = RelatedCaptureKey(state);
        var item = relatedCaptures.GetSnapshot(query.Timeline).Value?.Items.FirstOrDefault(Matches)
            ?? capturePicker.GetSnapshot(query).Value?.Items.FirstOrDefault(Matches);
        bool Matches(CaptureLibraryItem candidate) => candidate.Available && CaptureArtwork(candidate) == handle;
        if (item is null) return null;
        try
        {
            var thumbnail = await HostServices.CaptureLibrary.GetThumbnailAsync(item.Id,token);
            return new WidgetEncodedArtwork(WidgetArtworkContentType.Png,thumbnail.Bytes);
        }
        catch (WidgetCapabilityException) { return null; }
    }

    private WidgetModal? CaptureDialog(PlayniteLibraryRenderState state)
    {
        const string id = RelatedCapturePrefix;
        if (state.DetailsExtras.CaptureViewer is { Count: > 0 } media)
        {
            var player = UI.MediaGallery(media,id+".player","Game captures",new(Muted:true)).Classes("playnite-capture-viewer");
            return new(id+".viewer","Game captures",UI.Grid(id+".stage",[GridTrack.Star()],[GridTrack.Star()],player.InGrid()).Classes("playnite-capture-stage"),
                id+".player",state.DetailsExtras.CaptureViewerFromHome ? PlayniteLibraryActions.DetailsClose : id+".viewer.back")
                { ScrollContent=false, ShowHeader=false, OverlayPresentation=new() };
        }
        if (state.DetailsExtras.CapturePickerOffset is not { } offset) return null;
        if (state.DetailsExtras.ReviewSession is { } review) return ReviewDialog(state, review);
        var snapshot = capturePicker.GetSnapshot(RelatedCaptureKey(state));
        var children = new List<WidgetElement>();
        var back = id+".picker.back";
        var entry = back;
        var linked = state.DetailsExtras.BrowsingLinkedCaptures;
        if (snapshot.Value is { Reference: not null, NeedsPermission: false } page && (linked || page.CanLink))
        {
            if (linked)
            {
                if (page.Items.FirstOrDefault(item => item.Available) is { } first) entry = CardId(first);
                if (page.Items.Any(item => item.Available && CardId(item) == state.DetailsExtras.CaptureReturnId))
                    entry = state.DetailsExtras.CaptureReturnId!;
                if (AvailableUndo(page, state.DetailsExtras) is { } undo)
                {
                    children.Add(UI.Button("Undo unlink", UndoLinkAction(undo.Change), id + ".undo"));
                    if (state.DetailsExtras.CaptureReturnId == id + ".undo") entry = id + ".undo";
                }
                if (page.Items.Count > 0)
                    children.Add(UI.Text($"{offset + 1:N0}–{offset + page.Items.Count:N0} of {page.Total:N0}", id + ".picker.range").Classes("playnite-library-details-meta"));
                foreach (var item in page.Items)
                    children.Add(item.Available ? LinkedCard(page, item) : UI.Text((item.Title ?? CaptureType(item)) + " · File unavailable", CardId(item) + ".missing"));
            }
            else if (page.Runs is { } runs) AddRunChoices(children, page, runs);
            else foreach (var item in page.Items)
                children.Add(CaptureRow(item,PickAction(page,item),pick:true));
            if (!linked && page.Items.FirstOrDefault() is { } choice) entry = id + ".picker.item." + choice.Id;
            if (page.Items.Count == 0) children.Add(UI.Text(linked ? "No captures on this page." : "No more captures to link.",id+".picker.empty"));
            var paging = new List<WidgetElement>();
            if (offset > 0) paging.Add(UI.Button("Previous captures",id+".picker.previous",id+".picker.previous"));
            if (page.HasMore) paging.Add(UI.Button("More captures",id+".picker.next",id+".picker.next"));
            if (paging.Count > 0) children.Add(UI.Row(id+".picker.pages",paging.ToArray()));
        }
        else children.Add(UI.Text(snapshot.Error?.Message ?? (snapshot.Value is null ? "Loading captures…" : "Capture linking is unavailable."),id+".picker.status"));
        if (state.DetailsExtras.CaptureLinkMessage is { } message) children.Add(UI.Text(message, id+".picker.feedback"));
        children.Add(UI.Button("Back to game captures",back,back));
        var body = UI.Stack(id+".picker",children.ToArray()).RememberChildFocus(entry).Classes("playnite-capture-content");
        if (snapshot.Value is { } loaded)
        {
            if (offset > 0) body = body.Shortcut(ControllerButton.LeftBumper,id+".picker.previous","Previous captures");
            if (loaded.HasMore) body = body.Shortcut(ControllerButton.RightBumper,id+".picker.next","More captures");
        }
        var title = (linked ? "Captures · " : "Link captures to ") + RelatedCaptureLabel(state.DetailsItem!.Presentation.DisplayName,120);
        return new(id + (linked ? ".linked-gallery" : ".choose"),title,body,entry,back)
            { StyleClass = offset == 0 && snapshot.Value is { HasMore: false, Items.Count: <= 3 } ? "playnite-short-capture-dialog" : null };
    }

    private bool HandleCaptureViewer(string action,PlayniteLibraryRenderState state)
    {
        if (action == RelatedCapturePrefix+".viewer.back")
        { UpdateDetails(state.DetailsGeneration,extras=>extras with { CaptureViewer=null }); return true; }
        if (!action.StartsWith(RelatedCapturePrefix+".view.",StringComparison.Ordinal)) return false;
        var query = RelatedCaptureKey(state);
        var page = ResourceFor(query).GetSnapshot(query).Value;
        if (page is null || query.PickerOffset is not null && !query.BrowseLinked) return true;
        var items = page.Items.Where(item=>item.Available).ToList();
        var index = items.FindIndex(item=>ViewAction(page,item)==action);
        if (index < 0) return true;
        var media = items.Skip(index).Concat(items.Take(index)).Select(item => new MediaPlayerGalleryItem(item.Id,
            CaptureType(item)+" · "+CaptureTime(item),MediaPlayerSource.FromSavedCapture(item.Id,item.Revision))).ToArray();
        UpdateDetails(state.DetailsGeneration, extras => extras with { CaptureViewer=media,CaptureReturnId=CardId(items[index]) });
        return true;
    }
}
