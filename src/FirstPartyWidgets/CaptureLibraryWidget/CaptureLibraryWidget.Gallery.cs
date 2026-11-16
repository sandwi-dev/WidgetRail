using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private const string GalleryId = "capture-library.entries";
    private sealed record GalleryQuery(CaptureLibraryQuery Filter, string? Revision = null, int Density = 1, bool Details = false)
    {
        internal IReadOnlyList<CaptureLibraryApplication> Applications { get; init; } = [];
        internal IReadOnlyList<CaptureLibraryGroup> Groups { get; init; } = [];
        internal bool MoreApplications { get; init; }
    }
    private enum GalleryRoute { Gallery, Preview }
    private readonly WidgetIndexedCollection<GalleryQuery, CaptureLibraryItem> gallery;
    private readonly WidgetNavigator<GalleryRoute> galleryNavigation;
    private readonly object galleryGate = new();
    private GalleryQuery galleryQuery = new(new());
    private CaptureLibraryItem? selectedCapture;
    private FocusGroupEntryRequest? galleryReturn;
    private string dateFilter = "all";

    private WidgetIndexedCollection<GalleryQuery, CaptureLibraryItem> CreateGallery() =>
        CreateIndexedCollection<GalleryQuery, CaptureLibraryItem>("capture-library.gallery-source", galleryQuery, 0, new()
        {
            FindIndex = async (query,key,token) => (await HostServices.CaptureLibrary.FindAsync(query.Filter,key.Value,query.Revision,token)).Index,
            EnableSelection = true, ItemKey = item => new(item.Id), ReadRange = ReadGalleryAsync,
            RenderItem = GalleryItem,
            OnAction = OpenGalleryItemAsync,
            ResolveArtwork = async (_, item, handle, token) =>
            {
                if (!item.Available || handle.Value != ArtworkKey(item)) return null;
                try { var art = await HostServices.CaptureLibrary.GetThumbnailAsync(item.Id, token); return new(WidgetArtworkContentType.Png, art.Bytes); }
                catch (WidgetCapabilityException) { return null; }
            },
        });

    private void WatchLibrary()
    {
        Operations.RunLatest("capture-library.changes", async context =>
        {
            try
            {
                await using var changes = await HostServices.CaptureLibrary.OpenChangesAsync(context.CancellationToken);
                if (!context.IsCurrent) return;
                QueueGalleryRefresh(); // Reconcile after subscription acknowledgement; no initial read/event gap.
                if (showSources) captureSources.Refresh();
                await foreach (var change in changes.ReadAllAsync(context.CancellationToken))
                {
                    if (!context.IsCurrent) return;
                    QueueGalleryRefresh();
                    if (showSources) captureSources.Refresh();
                }
            }
            catch (OperationCanceledException) { }
            catch (WidgetCapabilityException) { } // Explicit Refresh remains available after revoked access.
        }, WidgetOperationLifetime.Active);
    }

    private void QueueGalleryRefresh() => StartGalleryRefresh();

    private WidgetOperationHandle StartGalleryRefresh() => Operations.RunLatest("capture-library.query", async context =>
    {
        try { await ReadGallerySnapshotAsync(context.CancellationToken); }
        catch (WidgetCapabilityException error)
        {
            if (context.IsCurrent) model.Update(s => s with { Message = Explain(error.ErrorCode) });
            throw;
        }
    }, WidgetOperationLifetime.Active);

    private async Task RefreshGalleryAsync(CancellationToken token)
    {
        var result = await StartGalleryRefresh().Completion.WaitAsync(token);
        if (result.Exception is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        token.ThrowIfCancellationRequested();
        // Superseded reads have a newer publication queued on the same SDK lane.
    }

    private async ValueTask<IReadOnlyList<CaptureLibraryItem>> ReadGalleryAsync(GalleryQuery query, int start, int count, CancellationToken token)
    {
        try { return (await HostServices.CaptureLibrary.QueryAsync(query.Filter, start, count, query.Revision, token)).Items; }
        catch (WidgetCapabilityException error) when (error.ErrorCode == "capture_library_query_changed")
        { QueueGalleryRefresh(); throw; }
    }

    private async Task ReadGallerySnapshotAsync(CancellationToken token)
    {
        GalleryQuery request; CaptureLibraryItem? selected;
        lock (galleryGate) { request = galleryQuery; selected = selectedCapture; }
        var anchor = selected?.Id;
        // Index revision keeps every demanded range in one ordering. A concurrent
        // external edit is retried once; continued churn is reported for retry.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var page = await HostServices.CaptureLibrary.QueryAsync(request.Filter, count: CaptureLibraryLimits.PageSize, cancellationToken: token);
                var applications = await HostServices.CaptureLibrary.ApplicationsAsync(request.Filter, limit: 126, cancellationToken: token);
                var groups = request.Filter.Sort is CaptureLibrarySort.Newest or CaptureLibrarySort.Oldest
                    ? (await HostServices.CaptureLibrary.GroupsAsync(request.Filter, page.Revision, token)).Items : [];
                var position = anchor is null ? null : await HostServices.CaptureLibrary.FindAsync(request.Filter, anchor, page.Revision, token);
                CaptureLibraryItem? latest = null;
                string? notice = null;
                if (selected is not null)
                {
                    try
                    {
                        latest = await HostServices.CaptureLibrary.GetMetadataAsync(selected.Id, token);
                        if (latest.Revision != selected.Revision || latest.Available != selected.Available)
                            notice = "This capture changed outside WidgetRail. Return to the library and reopen it to see the latest version.";
                    }
                    catch (WidgetCapabilityException error) when (error.ErrorCode == "capture_library_not_found")
                    { notice = "This capture was removed outside WidgetRail. You can return to the library."; }
                }
                token.ThrowIfCancellationRequested();
                lock (galleryGate)
                {
                    if (!ReferenceEquals(galleryQuery, request)) return;
                    if (latest is not null && notice is null && ReferenceEquals(selectedCapture, selected)) selectedCapture = latest;
                    if (request.Revision != page.Revision || gallery.Descriptor.Count != page.TotalCount)
                    {
                        galleryQuery = request with { Revision = page.Revision, Applications = applications.Items, MoreApplications = applications.HasMore, Groups = groups };
                        gallery.PublishQuery(galleryQuery, page.TotalCount);
                    }
                    // Reuse the navigator's one-shot return request. New library
                    // revisions can remap it, but must not create fresh focus intent.
                    galleryReturn = galleryNavigation.Value.CollectionReturnFocus is { } returning && position?.Index is { } index
                        ? gallery.Enter(GalleryId, returning.RequestId, gallery.FocusTarget(GalleryId,new(anchor!),index)) : null;
                    model.Update(s => s with { Page = new(page.Items, 0, page.TotalCount, page.StoredBytes), Message = notice ?? s.Message });
                }
                return;
            }
            catch (WidgetCapabilityException error) when (attempt == 0 && error.ErrorCode == "capture_library_query_changed") { }
        }
    }

    private ValueTask OpenGalleryItemAsync(GalleryQuery query, CaptureLibraryItem item, WidgetActionEvent action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (galleryGate)
        {
            if (!ReferenceEquals(query, galleryQuery) || model.Value.Busy || model.Value.Detail || action.ActionId != "capture-library.open-item") return ValueTask.CompletedTask;
            if (galleryNavigation.PushFromCollectionAction(GalleryRoute.Preview, action, gallery.Descriptor) != WidgetNavigationResult.Changed) return ValueTask.CompletedTask;
            selectedCapture = item; galleryReturn = null;
        }
        OpenCapture(item);
        return ValueTask.CompletedTask;
    }

    private void OpenCapture(CaptureLibraryItem item) => Work(async token =>
    {
        await ReleaseAsync(model.Value, token);
        model.Update(s => s with { Preview = null, Detail = true, SelectedId = item.Id, Navigation = WidgetNavigationOperation.Forward });
        var media = await HostServices.CaptureLibrary.OpenAsync(item.Id, cancellationToken: token);
        model.Update(s => s with { Preview = media, FromLibrary = true });
    });

    private void DeleteSelectedCapture(State state, string id)
    {
        var selected = selectedCapture;
        if (selected?.Id != id) return;
        Work(async token =>
        {
            await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token);
            token.ThrowIfCancellationRequested();
            model.Update(s => s with { Preview = null, FromLibrary = false, Detail = true });
            await ReleaseAsync(state, token);
            try
            {
                if (selected.SourceId is not null) await HostServices.CaptureLibrary.RemoveLinkAsync(new(id,selected.Revision,selected.MetadataRevision), token);
                else if (!selected.Available) await HostServices.CaptureLibrary.RemoveUnavailableAsync(new(id,selected.Revision,selected.MetadataRevision), token);
                else
                {
                    var result = await HostServices.CaptureLibrary.RecycleAsync([new(id, selected.Revision, selected.MetadataRevision)], token);
                    if (result.Results.Single().ErrorCode is { } error) throw new WidgetCapabilityException(error, "Capture could not be recycled.");
                }
            }
            catch (WidgetCapabilityException)
            {
                // Recover a failed removal without upgrading a stale preview's
                // authority to different content or metadata changed elsewhere.
                try
                {
                    var current = await HostServices.CaptureLibrary.GetMetadataAsync(id,token);
                    if (current.Available && current.Revision == selected.Revision && current.MetadataRevision == selected.MetadataRevision)
                    {
                        var reopened = await HostServices.CaptureLibrary.OpenAsync(id,cancellationToken:token);
                        token.ThrowIfCancellationRequested(); model.Update(s => s with { Preview = reopened, FromLibrary = true });
                    }
                }
                catch (WidgetCapabilityException) { }
                throw;
            }
            token.ThrowIfCancellationRequested();
            galleryNavigation.Back(); selectedCapture = null; galleryReturn = null;
            model.Update(s => s with { SelectedId = null, Preview = null, Detail = false, Navigation = WidgetNavigationOperation.Back, ConfirmDelete = false });
            await RefreshAsync(token);
            model.Update(s => s with { Message = selected.SourceId is not null ? "Removed from the library. The original file was kept." : selected.Available ? "Moved to the Windows Recycle Bin." : "Removed the unavailable capture." });
        });
    }

    private void ChangeCaptureMetadata(string? title, bool toggleFavorite)
    {
        var selected = selectedCapture;
        if (selected is null || !toggleFavorite && title is null) return;
        Work(async token =>
        {
            await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token);
            var updated = await HostServices.CaptureLibrary.UpdateMetadataAsync(selected.Id, selected.MetadataRevision,
                toggleFavorite ? selected.Title : title!.Trim(), toggleFavorite ? !selected.IsFavorite : selected.IsFavorite, token);
            token.ThrowIfCancellationRequested();
            lock (galleryGate)
                if (ReferenceEquals(selectedCapture,selected))
                    selectedCapture = updated.Revision == selected.Revision && updated.Available == selected.Available ? updated :
                        selected with { Title=updated.Title,IsFavorite=updated.IsFavorite,MetadataRevision=updated.MetadataRevision };
            await RefreshGalleryAsync(token);
        });
    }

    private void NavigateCapture(int direction)
    {
        var selected = selectedCapture;
        GalleryQuery query; lock (galleryGate) query = galleryQuery;
        if (selected is null || query.Revision is null) return;
        Work(async token =>
        {
            var position = await HostServices.CaptureLibrary.FindAsync(query.Filter, selected.Id, query.Revision, token);
            if (position.Index is not { } index || index + direction < 0 || index + direction >= position.TotalCount) return;
            var page = await HostServices.CaptureLibrary.QueryAsync(query.Filter, index + direction, 1, query.Revision, token);
            var next = page.Items.Single();
            await ReleaseAsync(model.Value, token);
            lock (galleryGate)
            {
                selectedCapture = next;
                galleryReturn = null;
            }
            model.Update(s => s with { Preview = null, Detail = true, SelectedId = next.Id });
            var media = await HostServices.CaptureLibrary.OpenAsync(next.Id, cancellationToken: token);
            model.Update(s => s with { Preview = media, FromLibrary = true });
        });
    }

    private static WidgetElement GalleryItem(GalleryQuery query, CaptureLibraryItem item, WidgetIndexedItemContext context)
    {
        var name = item.Title ?? item.Media.SourceApplication?.ApplicationName ?? item.FileName ?? "Application capture";
        var art = item.Available ? (WidgetElement)UI.Artwork(new(ArtworkKey(item)), context.Id("thumbnail"), "Capture thumbnail", ImageFit.Contain).Classes("capture-thumbnail", query.Details ? "capture-row-thumbnail" : "capture-density-" + query.Density)
            : UI.Text("Unavailable", context.Id("thumbnail")).Classes("capture-thumbnail-placeholder");
        var info = item.Media.ContentType == "image/png" ? "Screenshot" : $"Clip · {item.Media.DurationSeconds:0.#} s";
        var date = (item.FileModifiedAtUnixMilliseconds is not null ? "Modified " : "") +
            DateTimeOffset.FromUnixTimeMilliseconds(item.FileModifiedAtUnixMilliseconds ?? item.SavedAtUnixMilliseconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var copy = new List<WidgetElement>
        {
            UI.Text((item.IsFavorite ? "★ " : "") + name, context.Id("title")).Classes("capture-heading", "capture-card-title"),
            UI.Text(query.Details ? info + $" · {item.Media.Width} × {item.Media.Height}" : info + " · " + date, context.Id("info")).Classes("capture-help", "capture-card-info"),
        };
        if (query.Details) copy.Add(UI.Text(date, context.Id("date")).Classes("capture-help"));
        return UI.ActionSurface("capture-library.open-item", context.Id("capture"), name, query.Details ? ActionSurfaceOrientation.Horizontal : ActionSurfaceOrientation.Vertical,
            art, UI.Stack(context.Id("copy"),copy.ToArray()).Classes("capture-entry-copy"))
            .Classes("capture-entry", query.Details ? "capture-detail-row" : "capture-grid-card");
    }

    private IEnumerable<WidgetElement> GalleryControls(State state)
    {
        GalleryQuery query; lock (galleryGate) query = galleryQuery;
        var filter = query.Filter;
        var selection = gallery.Selection!;
        yield return UI.Row("capture-library.search-row",
            UI.TextEntry(filter.Search ?? "", "Search captures", "capture-library.search", "capture-library.search", 160).Classes("capture-search"),
            UI.Button("Clear", "capture-library.clear", "capture-library.clear").Disabled(filter == new CaptureLibraryQuery()),
            UI.Button("Refresh", "capture-library.refresh", "capture-library.refresh").Icon(WidgetGlyph.Refresh).Busy(state.Busy));
        yield return UI.ResponsiveGrid("capture-library.filters", 180, 4,
            UI.Select("Type", new SelectOption[] {
                new("all", "All captures", "capture-library.filter.all", IsSelected:filter.MediaKind==CaptureLibraryMediaKind.All&&!filter.FavoritesOnly),
                new("screenshots", "Screenshots", "capture-library.filter.screenshots", IsSelected:filter.MediaKind==CaptureLibraryMediaKind.Screenshot&&!filter.FavoritesOnly),
                new("clips", "Clips", "capture-library.filter.clips", IsSelected:filter.MediaKind==CaptureLibraryMediaKind.Video&&!filter.FavoritesOnly),
                new("favorites", "Favorites", "capture-library.filter.favorites", IsSelected:filter.FavoritesOnly)}, "capture-library.filter"),
            UI.Select("Application", ApplicationOptions(query), "capture-library.application"),
            UI.Select("Date", new SelectOption[] { new("all","Any date","capture-library.date.all",IsSelected:dateFilter=="all"), new("week","Last 7 days","capture-library.date.week",IsSelected:dateFilter=="week"), new("month","Last 30 days","capture-library.date.month",IsSelected:dateFilter=="month") }, "capture-library.date"),
            UI.Select("Sort", Enum.GetValues<CaptureLibrarySort>().Select(sort=>new SelectOption(sort.ToString(),SortLabel(sort),"capture-library.sort."+sort,IsSelected:filter.Sort==sort)).ToArray(), "capture-library.sort"));
        yield return UI.Row("capture-library.gallery-toolbar",
            UI.Text(state.Page is { } page ? (selection.IsActive ? $"{selection.Count:N0} selected · " : "") + $"{page.TotalCount:N0} captures" : "Loading captures…", "capture-library.count").Classes("capture-gallery-count"),
            UI.Button(selection.IsActive ? "Done selecting" : "Select", "capture-library.selection.toggle", "capture-library.selection.toggle").Disabled(state.Page?.TotalCount is not > 0),
            UI.Select("View", new SelectOption[] {
                new("compact","Small thumbnails","capture-library.view.compact",IsSelected:!query.Details&&query.Density==0),
                new("normal","Medium thumbnails","capture-library.view.normal",IsSelected:!query.Details&&query.Density==1),
                new("large","Large thumbnails","capture-library.view.large",IsSelected:!query.Details&&query.Density==2),
                new("details","Details","capture-library.view.details",IsSelected:query.Details)}, "capture-library.view"));
        if (query.MoreApplications)
            yield return UI.Text("Use Search to narrow the application list.", "capture-library.applications.more").Classes("capture-help");
        if (selection.IsActive)
        {
            yield return UI.ResponsiveGrid("capture-library.selection.actions",180,6,
                UI.Button("Select all", "capture-library.selection.all", "capture-library.selection.all").Disabled(gallery.Descriptor.Count > 256),
                UI.Button("Clear selection", "capture-library.selection.clear", "capture-library.selection.clear").Disabled(selection.Count==0),
                UI.Button("Favorite", "capture-library.batch.favorite", "capture-library.batch.favorite").Disabled(selection.Count==0).Busy(state.Busy),
                UI.Button("Remove favorite", "capture-library.batch.unfavorite", "capture-library.batch.unfavorite").Disabled(selection.Count==0).Busy(state.Busy),
                UI.Button("Save copies…", "capture-library.batch.copy", "capture-library.batch.copy").Disabled(selection.Count==0).Busy(state.Busy),
                UI.Button("Remove…", "capture-library.batch.recycle", "capture-library.batch.recycle").Disabled(selection.Count==0).Busy(state.Busy));
            if (gallery.Descriptor.Count > 256 || selection.LimitReached)
                yield return UI.Text("Select up to 256 captures per batch. Narrow the filters to select all matching captures.", "capture-library.selection.limit").Classes("capture-help");
        }
        if (state.Page?.TotalCount == 0)
            yield return UI.Text(filter == new CaptureLibraryQuery() ? "Your saved screenshots and clips will appear here." : "No captures match these filters.", "capture-library.empty").Classes("capture-empty");
        else if (state.Page is not null)
        {
            var collection = query.Details ? UI.CollectionList(GalleryId, gallery, 110, "Saved captures") :
                UI.CollectionGrid(GalleryId, gallery, query.Density switch {0=>180,2=>340,_=>220}, query.Density switch {0=>160,2=>260,_=>185}, "Saved captures", maximumColumns: query.Density==2?3:query.Density==0?6:5);
            if (query.Groups.Count > 0) collection = collection.Grouped(query.Groups.Select(group => new IndexedCollectionGroup("month."+group.Key, GroupLabel(group.Key),group.Count)).ToArray());
            yield return collection.Classes("capture-gallery");
        }
    }

    private static string GroupLabel(string key) => key switch
    {
        "older" => "Older captures", "newer" => "Newer captures",
        _ => DateTime.TryParseExact(key,"yyyy-MM",CultureInfo.InvariantCulture,DateTimeStyles.None,out var month)
            ? month.ToString("MMMM yyyy",CultureInfo.CurrentCulture) : "Other captures",
    };
    private static string ApplicationAction(string name) => WidgetIds.Scope("capture-library.application").KeyedId("pick", "app:"+name);
    private static SelectOption[] ApplicationOptions(GalleryQuery query)
    {
        var choices = new List<SelectOption> { new("all","All applications","capture-library.application.all",IsSelected:query.Filter.Application is null) };
        foreach (var app in query.Applications)
            choices.Add(new(ApplicationAction(app.Name), app.Name.Length==0?"Unknown application":app.Name,ApplicationAction(app.Name),IsSelected:query.Filter.Application==app.Name));
        if(query.Filter.Application is { } selected && !query.Applications.Any(app=>app.Name==selected))
            choices.Add(new(ApplicationAction(selected),selected.Length==0?"Unknown application":selected,ApplicationAction(selected),IsSelected:true));
        return choices.ToArray();
    }

    private static string SortLabel(CaptureLibrarySort sort) => sort switch {
        CaptureLibrarySort.Newest=>"Newest first",CaptureLibrarySort.Oldest=>"Oldest first",CaptureLibrarySort.Title=>"Title",CaptureLibrarySort.Application=>"Application",CaptureLibrarySort.Largest=>"Largest files",_=>"Longest clips"};

    private bool HandleGalleryAction(WidgetActionEvent action)
    {
        if (model.Value.Busy) return false;
        lock (galleryGate)
        {
            switch(action.ActionId)
            {
                case "capture-library.selection.toggle": if(gallery.Selection!.IsActive)gallery.EndSelection();else gallery.BeginSelection();return true;
                case "capture-library.selection.all": gallery.SelectAll();return true;
                case "capture-library.selection.clear": gallery.ClearSelection();return true;
                case "capture-library.batch.favorite": PrepareBatch("favorite");return true;
                case "capture-library.batch.unfavorite": PrepareBatch("unfavorite");return true;
                case "capture-library.batch.recycle": PrepareBatch("recycle");return true;
                case "capture-library.batch.copy": PrepareBatch("copy");return true;
            }
            var filter = galleryQuery.Filter;
            var changed = true;
            switch(action.ActionId)
            {
                case "capture-library.search": filter=filter with{Search=action.CommittedText?.Trim()}; break;
                case "capture-library.application.all": filter=filter with{Application=null}; break;
                case "capture-library.clear": filter=new();dateFilter="all";break;
                case "capture-library.filter.all":filter=filter with{MediaKind=CaptureLibraryMediaKind.All,FavoritesOnly=false};break;
                case "capture-library.filter.screenshots":filter=filter with{MediaKind=CaptureLibraryMediaKind.Screenshot,FavoritesOnly=false};break;
                case "capture-library.filter.clips":filter=filter with{MediaKind=CaptureLibraryMediaKind.Video,FavoritesOnly=false};break;
                case "capture-library.filter.favorites":filter=filter with{MediaKind=CaptureLibraryMediaKind.All,FavoritesOnly=true};break;
                default:
                    if(galleryQuery.Applications.FirstOrDefault(app=>ApplicationAction(app.Name)==action.ActionId) is { } application)filter=filter with{Application=application.Name};
                    else if(action.ActionId.StartsWith("capture-library.sort.",StringComparison.Ordinal)&&Enum.TryParse<CaptureLibrarySort>(action.ActionId["capture-library.sort.".Length..],out var sort))filter=filter with{Sort=sort};
                    else if(action.ActionId.StartsWith("capture-library.date.",StringComparison.Ordinal)) {
                        dateFilter=action.ActionId["capture-library.date.".Length..];
                        if(dateFilter is not ("all" or "week" or "month"))return true;
                        filter=filter with{FromUnixMilliseconds=dateFilter=="all"?null:DateTimeOffset.UtcNow.AddDays(dateFilter=="week"?-7:-30).ToUnixTimeMilliseconds()};
                    }
                    else if(action.ActionId.StartsWith("capture-library.view.",StringComparison.Ordinal)) {
                        var mode=action.ActionId["capture-library.view.".Length..];
                        if(mode is not ("compact" or "normal" or "large" or "details"))return true;
                        galleryQuery=galleryQuery with{Density=mode=="compact"?0:mode=="large"?2:1,Details=mode=="details"};
                        gallery.UpdateContent(galleryQuery);Invalidate();return true;
                    }
                    else changed=false;
                    break;
            }
            if(!changed)return false;
            if(!filter.IsWellFormed())return true;
            galleryQuery=galleryQuery with{Filter=filter,Revision=null};selectedCapture=null;galleryReturn=null;
        }
        QueueGalleryRefresh();return true;
    }
}
