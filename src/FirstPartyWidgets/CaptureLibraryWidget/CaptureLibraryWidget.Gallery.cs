using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private const string GalleryId = "capture-library.entries";
    private sealed record GalleryQuery(CaptureLibraryQuery Filter, string? Revision = null, int Density = 1, bool Details = false)
    {
        internal string? OrderingRevision { get; init; }
        internal IReadOnlyList<CaptureLibraryApplication> Applications { get; init; } = [];
        internal IReadOnlyList<CaptureLibraryFolder> Folders { get; init; } = [];
        internal string? FolderLabel { get; init; }
        internal IReadOnlyList<CaptureLibraryGroup> Groups { get; init; } = [];
        internal bool MoreApplications { get; init; }
        internal string? RelatedLabel { get; init; }
        internal bool RecoveryGrid { get; init; }
        internal bool ShowDetails => Filter.UnavailableOnly ? !RecoveryGrid : Details;
        internal bool Selecting { get; init; }
        internal bool ClipPreviews { get; init; }
    }
    private enum GalleryRoute { Gallery, Preview }
    private readonly WidgetIndexedCollection<GalleryQuery, CaptureLibraryItem> gallery;
    private readonly WidgetNavigator<GalleryRoute> galleryNavigation;
    private readonly object galleryGate = new();
    private static readonly CaptureLibraryQuery DefaultGalleryFilter = new() { AvailableOnly = true };
    private GalleryQuery galleryQuery = new(DefaultGalleryFilter);
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

    private void GallerySelectionChanged(object? sender,EventArgs args)
    {
        lock(galleryGate)
        {
            var selecting=gallery.Selection!.IsActive;
            if(galleryQuery.Selecting==selecting)return;
            galleryQuery=galleryQuery with{Selecting=selecting};
            gallery.UpdateContent(galleryQuery);
        }
    }

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

    private void QueueGalleryRefresh()
    {
        // The batch owns its immutable references and reconciles once on exit.
        if (Volatile.Read(ref pendingBatch) is not { Started: true }) StartGalleryRefresh();
    }

    private WidgetOperationHandle StartGalleryRefresh() => Operations.RunLatest("capture-library.query", async context =>
    {
        try { await ReadGallerySnapshotAsync(context.CancellationToken); }
        catch (WidgetCapabilityException error)
        {
            if (context.IsCurrent) UpdateFeedback(s => s with { Message = Explain(error.ErrorCode) }, error.ErrorCode == "permission_denied");
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
        GalleryQuery request; CaptureLibraryItem? selected; WidgetNavigationSnapshot<GalleryRoute> navigation;
        lock (galleryGate) { request = galleryQuery; selected = selectedCapture; navigation = galleryNavigation.Value; }
        var anchor = selected?.Id;
        // Index revision keeps every demanded range in one ordering. A concurrent
        // external edit is retried once; continued churn is reported for retry.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var snapshot = await HostServices.CaptureLibrary.QueryContinuityAsync(request.Filter, count: CaptureLibraryLimits.PageSize, cancellationToken: token);
                var page = snapshot.Page;
                var applications = await HostServices.CaptureLibrary.ApplicationsAsync(request.Filter, limit: 126, cancellationToken: token);
                var folders = await HostServices.CaptureLibrary.FoldersAsync(request.Filter, page.Revision, token);
                var groups = request.Filter.Sort is CaptureLibrarySort.Newest or CaptureLibrarySort.Oldest
                    ? (await HostServices.CaptureLibrary.GroupsAsync(request.Filter, page.Revision, token)).Items : [];
                var position = anchor is null ? null : await HostServices.CaptureLibrary.FindAsync(request.Filter, anchor, page.Revision, token);
                var returnIndex = position?.Index;
                var returnKey = anchor;
                if (returnIndex is null && navigation.CollectionReturnFocus is { } previousFocus && page.TotalCount > 0)
                {
                    var nearest = Math.Min(previousFocus.Item.Index, page.TotalCount - 1);
                    var neighbor = nearest < page.Items.Count ? page.Items[nearest] :
                        (await HostServices.CaptureLibrary.QueryAsync(request.Filter, nearest, 1, page.Revision, token)).Items.SingleOrDefault();
                    returnIndex = neighbor is null ? null : nearest;
                    returnKey = neighbor?.Id;
                }
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
                    token.ThrowIfCancellationRequested();
                    // UpdateContent may change selection/preview/layout policy
                    // while this same filter/revision is being read. Preserve
                    // those current presentation values without accepting a
                    // response for a different filter or library ordering.
                    var current = galleryQuery;
                    if (!ReferenceEquals(current.Filter, request.Filter) || current.Revision != request.Revision) return;
                    var ownsPreview = ReferenceEquals(selectedCapture, selected) && galleryNavigation.Value.Revision == navigation.Revision;
                    if (latest is not null && notice is null && ownsPreview) selectedCapture = latest;
                    if (request.Revision != page.Revision || gallery.Descriptor.Count != page.TotalCount)
                    {
                        var contentOnly = request.Revision is not null && request.OrderingRevision == snapshot.OrderingRevision && gallery.Descriptor.Count == page.TotalCount;
                        galleryQuery = current with { Revision = page.Revision, OrderingRevision = snapshot.OrderingRevision, Applications = applications.Items, MoreApplications = applications.HasMore, Groups = groups,
                            Folders = folders.Items, FolderLabel = folders.Items.FirstOrDefault(folder => folder.SourceId == current.Filter.SourceId)?.Label ?? current.FolderLabel };
                        if (contentOnly) gallery.UpdateContent(galleryQuery);
                        else if (request.Revision is not null) gallery.PublishRefresh(galleryQuery, page.TotalCount);
                        else gallery.PublishQuery(galleryQuery, page.TotalCount);
                    }
                    else if (!current.Folders.SequenceEqual(folders.Items))
                    {
                        galleryQuery = current with { Folders = folders.Items,
                            FolderLabel = folders.Items.FirstOrDefault(folder => folder.SourceId == current.Filter.SourceId)?.Label ?? current.FolderLabel };
                        gallery.UpdateContent(galleryQuery);
                    }
                    // Reuse the navigator's one-shot return request. New library
                    // revisions can remap it, but must not create fresh focus intent.
                    // A newer Next/Back owns its own warning and return target.
                    // This read can still publish current library data, but may
                    // remap only the exact return intent it captured on entry.
                    if (ownsPreview && navigation.CollectionReturnFocus is { } returning &&
                        (galleryReturn is null || galleryReturn.RequestId == returning.RequestId) &&
                        ReferenceEquals(galleryNavigation.Value.CollectionReturnFocus, returning))
                        galleryReturn = returnIndex is { } index
                            ? gallery.Enter(GalleryId, returning.RequestId, gallery.FocusTarget(GalleryId,new(returnKey!),index)) : null;
                    model.Update(s => s with { Page = new(page.Items, 0, page.TotalCount, page.StoredBytes),
                        Message = ownsPreview ? notice ?? s.Message : s.Message,
                        PermissionRequired = ownsPreview && notice is not null ? false : s.PermissionRequired });
                    if (ownsPreview && notice is not null) PublishFeedback(notice);
                }
                return;
            }
            catch (WidgetCapabilityException error) when (attempt == 0 && error.ErrorCode == "capture_library_query_changed") { }
        }
    }

    private ValueTask OpenGalleryItemAsync(GalleryQuery query, CaptureLibraryItem item, WidgetActionEvent action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var favorite = action.ActionId == "capture-library.item.favorite";
        lock (galleryGate)
        {
            if (!ReferenceEquals(query, galleryQuery) || model.Value.Busy || model.Value.Detail) return ValueTask.CompletedTask;
            if (favorite)
            {
                if (gallery.Selection!.IsActive) return ValueTask.CompletedTask;
            }
            else
            {
                if (action.ActionId != "capture-library.open-item" ||
                    galleryNavigation.PushFromCollectionAction(GalleryRoute.Preview, action, gallery.Descriptor) != WidgetNavigationResult.Changed)
                    return ValueTask.CompletedTask;
                selectedCapture = item; galleryReturn = null;
            }
        }
        if (favorite) ToggleGalleryFavorite(query, item);
        else OpenCapture(item);
        return ValueTask.CompletedTask;
    }

    private void ToggleGalleryFavorite(GalleryQuery query, CaptureLibraryItem item) => Work(async token =>
    {
        await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token);
        token.ThrowIfCancellationRequested();
        lock (galleryGate)
            if (!ReferenceEquals(query, galleryQuery) || model.Value.Detail || gallery.Selection!.IsActive) return;
        // Use the exact displayed metadata revision. A title/favorite edit from
        // another client must not be overwritten by this old context menu.
        var updated = await HostServices.CaptureLibrary.UpdateMetadataAsync(item.Id, item.MetadataRevision,
            item.Title, !item.IsFavorite, token);
        token.ThrowIfCancellationRequested();
        lock (galleryGate)
            if (ReferenceEquals(query, galleryQuery) && !model.Value.Detail)
                UpdateFeedback(state => state with { Message = updated.IsFavorite ? "Added to favorites." : "Removed from favorites." });
        // Reconcile the authoritative query: Favorites-only membership can change,
        // and an unrelated filesystem update can arrive during the command.
        await RefreshGalleryAsync(token);
    });

    private void OpenCapture(CaptureLibraryItem item) => Work(async token =>
    {
        await ReleaseAsync(model.Value, token);
        model.Update(s => s with { Preview = null, Detail = true, SelectedId = item.Id, Navigation = WidgetNavigationOperation.Forward });
        if (!item.Available) return;
        var media = await HostServices.CaptureLibrary.OpenAsync(item.Id, cancellationToken: token);
        model.Update(s => s with { Preview = media });
    });

    private void DeleteSelectedCapture(State state, string id,bool recycleLinkedOriginal=false)
    {
        var selected = selectedCapture;
        if (selected?.Id != id || recycleLinkedOriginal && (selected.SourceId is null || !selected.Available)) return;
        Work(async token =>
        {
            await RequirePermissionsAsync([recycleLinkedOriginal?WidgetCaptureLibraryCapabilities.RecycleLinkedPermission:WidgetCaptureLibraryCapabilities.ManagePermission], token);
            token.ThrowIfCancellationRequested();
            model.Update(s => s with { Preview = null, Detail = true });
            await ReleaseAsync(state, token);
            try
            {
                if(recycleLinkedOriginal)
                {
                    var result=await HostServices.CaptureLibrary.RecycleLinkedAsync([new(id,selected.Revision,selected.MetadataRevision)],token);
                    if(result.Results.Single().ErrorCode is {} error)throw new WidgetCapabilityException(error,"Capture could not be recycled.");
                }
                else if (selected.SourceId is not null) await HostServices.CaptureLibrary.HideAsync(new(id,selected.Revision,selected.MetadataRevision), token);
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
                        token.ThrowIfCancellationRequested(); model.Update(s => s with { Preview = reopened });
                    }
                }
                catch (WidgetCapabilityException) { }
                throw;
            }
            token.ThrowIfCancellationRequested();
            galleryNavigation.Back(); selectedCapture = null; galleryReturn = null;
            model.Update(s => s with { SelectedId = null, Preview = null, Detail = false, Navigation = WidgetNavigationOperation.Back, ConfirmDelete = false });
            await RefreshAsync(token);
            UpdateFeedback(s => s with { Message = recycleLinkedOriginal ? "Moved the original to the Windows Recycle Bin." : selected.SourceId is not null ? "Hidden from library. Find it under Type → Hidden." : selected.Available ? "Moved to the Windows Recycle Bin." : "Removed the unavailable capture." });
        });
    }

    private void UnhideSelectedCapture(State state)
    {
        var selected = selectedCapture;
        if (!galleryQuery.Filter.HiddenOnly || selected?.SourceId is null || selected.Id != state.SelectedId) return;
        Work(async token =>
        {
            await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token);
            await HostServices.CaptureLibrary.UnhideAsync(new(selected.Id, selected.Revision, selected.MetadataRevision), token);
            token.ThrowIfCancellationRequested();
            await ReleaseAsync(state, token);
            galleryNavigation.Back(); selectedCapture = null; galleryReturn = null;
            model.Update(value => value with { Preview = null, Detail = false, SelectedId = null,
                Navigation = WidgetNavigationOperation.Back });
            await RefreshGalleryAsync(token);
            UpdateFeedback(value => value with { Message = "Capture unhidden." });
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
        var original = model.Value;
        var selected = selectedCapture;
        GalleryQuery query; lock (galleryGate) query = galleryQuery;
        if (selected is null || query.Revision is null) return;
        Work(async token =>
        {
            var position = await HostServices.CaptureLibrary.FindAsync(query.Filter, selected.Id, query.Revision, token);
            if (position.Index is not { } index || index + direction < 0 || index + direction >= position.TotalCount) return;
            var page = await HostServices.CaptureLibrary.QueryAsync(query.Filter, index + direction, 1, query.Revision, token);
            var next = page.Items.Single();
            await ReplacePreviewAsync(next, original, token);
        });
    }

    private async Task ReplacePreviewAsync(CaptureLibraryItem next, State original, CancellationToken token)
    {
        CaptureAttachment? unclaimed = null;
        try
        {
            // Keep one coherent detail page while the next lease opens. Clearing
            // Preview here removed metadata and replaced the native player for
            // a few frames, moving the controls and losing its retained state.
            if (next.Available) unclaimed = await HostServices.CaptureLibrary.OpenAsync(next.Id, cancellationToken: token);
            token.ThrowIfCancellationRequested();
            lock (galleryGate)
            {
                if (!SamePreviewSource(original)) return;
                selectedCapture = next;
                galleryReturn = null;
                contexts = null; contextError = null; showContexts = false;
                model.Update(s => s with { Preview = unclaimed, Detail = true, SelectedId = next.Id });
                unclaimed = null;
            }
            // Retire only the old, now unpublished lease. A failed open leaves
            // the current capture usable; a late result cannot replace it.
            await ReleaseAsync(original, CancellationToken.None);
        }
        finally
        {
            if (unclaimed is not null)
                try { await HostServices.CaptureLibrary.ReleaseAsync(unclaimed, CancellationToken.None); }
                catch (Exception error) when (error is WidgetCapabilityException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private static WidgetElement GalleryItem(GalleryQuery query, CaptureLibraryItem item, WidgetIndexedItemContext context)
    {
        var name = item.Title ?? item.Media.SourceApplication?.ApplicationName ?? item.FileName ?? "Application capture";
        var video = item.Media.ContentType == "video/mp4";
        var density = query.ShowDetails ? "capture-row-thumbnail" : "capture-density-" + query.Density;
        var art = item.Available ? (WidgetElement)UI.Artwork(new(ArtworkKey(item)), context.Id("thumbnail"), "Capture thumbnail", ImageFit.Contain).Classes("capture-thumbnail", density)
            : UI.Text("Unavailable", context.Id("thumbnail")).Classes("capture-thumbnail-placeholder", density);
        var duration = TimeSpan.FromSeconds(item.Media.DurationSeconds);
        var durationText = duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}" : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
        var info = video ? "Clip · " + durationText : "Screenshot";
        WidgetElement thumbnail = query.ShowDetails ? art : UI.Stack(context.Id("thumbnail-stage"),art).Classes("capture-thumbnail-stage",density);
        if (!query.ShowDetails && video && item.Available)
            thumbnail = UI.Grid(context.Id("thumbnail-stage"), [GridTrack.Star()], [GridTrack.Star()], art.InGrid(),
                UI.Stack(context.Id("duration-overlay"), UI.Row(context.Id("duration-badge"),
                    UI.Icon(WidgetGlyph.Play,context.Id("duration-icon"),"Clip").Classes("capture-duration-icon"),
                    UI.Text(durationText, context.Id("duration"))).Classes("capture-duration-badge"))
                    .Classes("capture-duration-overlay").InGrid()).Classes("capture-thumbnail-stage", density);
        var captured = DateTimeOffset.FromUnixTimeMilliseconds(item.FileModifiedAtUnixMilliseconds ?? item.SavedAtUnixMilliseconds).ToLocalTime();
        var date = (item.FileModifiedAtUnixMilliseconds is not null ? "Modified " : "") +
            captured.ToString("g", CultureInfo.CurrentCulture);
        var copy = new List<WidgetElement>
        {
            UI.Row(context.Id("title-row"),
                UI.Text(name, context.Id("title")).Classes("capture-heading", "capture-card-title"),
                UI.Text(item.IsFavorite ? "★" : "", context.Id("favorite")).Classes("capture-card-favorite")).Classes("capture-card-title-row"),
            UI.Text(query.ShowDetails ? info + $" · {item.Media.Width} × {item.Media.Height}" :
                    captured.ToString("MMM d",CultureInfo.CurrentCulture)+" · "+captured.ToString("t",CultureInfo.CurrentCulture), context.Id("info"))
                    .Classes("capture-card-info"),
        };
        if (query.ShowDetails) copy.Add(UI.Text(date, context.Id("date")).Classes("capture-help", "capture-row-date"));
        var spoken = name + ". " + info + ". " + date + (item.IsFavorite ? ". Favorite" : "") + (item.Available ? "" : ". Unavailable");
        var card = UI.ActionSurface("capture-library.open-item", context.Id("capture"), spoken, query.ShowDetails ? ActionSurfaceOrientation.Horizontal : ActionSurfaceOrientation.Vertical,
            thumbnail, UI.Stack(context.Id("copy"),copy.ToArray()).Classes("capture-entry-copy"))
            .ContextAction("capture-library.item.favorite", item.IsFavorite ? "Remove favorite" : "Favorite", disabled: query.Selecting)
            .Classes("capture-entry", query.ShowDetails ? "capture-detail-row" : "capture-grid-card") with { NativeContextMenuOnly = true };
        if(!query.ShowDetails)card=card.FocusVisualTarget(context.Id("thumbnail-stage"));
        if (!item.Available || query.Selecting || !query.ClipPreviews) return card;
        return card.PresentOnFocus(UI.Stack(context.Id("clip-preview"),
            UI.Row(context.Id("preview-header"),
                UI.Text(name, context.Id("preview-title")).Classes("capture-preview-title"),
                UI.Divider(context.Id("preview-meta-divider")).Classes("capture-preview-meta-divider"),
                UI.Text(info + (video && item.Media.Properties is { } properties ? $" · {properties.RequestedFramesPerSecond} fps" : "") +
                    "\n" + captured.ToString("g",CultureInfo.CurrentCulture),context.Id("preview-info"))
                    .Classes("capture-help","capture-preview-info")).Classes("capture-preview-header"),
            UI.Divider(context.Id("preview-header-divider")).Classes("capture-preview-header-divider"),
            UI.Grid(context.Id("preview-stage"), [GridTrack.Star()], [GridTrack.Star()],
                UI.MediaPlayer(MediaPlayerSource.FromSavedCapture(item.Id, item.Revision), context.Id("preview-player"), video ? "Clip preview" : "Screenshot preview",
                    new(AutoPlay: video, Loop: video, Muted: true)).WithPoster(new(ArtworkKey(item))).Classes("capture-preview-media").InGrid())
                .Classes("capture-preview-stage"),
            UI.Grid(context.Id("preview-actions"),[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Star(),GridTrack.Star()],
                UI.Button("Open capture", "capture-library.open-item", context.Id("preview-open")).Classes("capture-preview-action","capture-preview-primary").InGrid(),
                UI.Button(item.IsFavorite ? "Favorited" : "Favorite", "capture-library.item.favorite", context.Id("preview-favorite"))
                    .Icon(item.IsFavorite ? WidgetGlyph.FavoriteFilled : WidgetGlyph.Favorite,item.IsFavorite?"Remove favorite":"Favorite").Selected(item.IsFavorite).Classes("capture-preview-action").InGrid(column:1),
                SharePreviewButton(item, context.Id("preview-share")).Classes("capture-preview-action").InGrid(column:2)).Spacing(0,8).Classes("capture-preview-actions"))
            .Classes("capture-clip-preview"));
    }

    private string GalleryCount(State state) => state.Page is { } page
        ? (gallery.Selection is { IsActive: true } selection ? $"{selection.Count:N0} selected / " : "") + $"{page.TotalCount:N0} capture{(page.TotalCount == 1 ? "" : "s")}"
        : "Loading captures...";

    private IEnumerable<WidgetElement> GalleryControls(State state)
    {
        GalleryQuery query; lock (galleryGate) query = galleryQuery;
        var filter = query.Filter;
        var selection = gallery.Selection!;
        var activeFilters = (filter.MediaKind != CaptureLibraryMediaKind.All || filter.FavoritesOnly || filter.HiddenOnly || filter.UnavailableOnly ? 1 : 0) +
            (filter.Application is not null ? 1 : 0) + (filter.SourceId is not null ? 1 : 0) + (dateFilter != "all" ? 1 : 0) + (filter.Reference is not null ? 1 : 0);
        var filterPanel = FilterPanel(query);
        var filterLabel = activeFilters == 0 ? "Filters" : $"Filters ({activeFilters})";
        var filterButton = UI.ReadingSurface("capture-library.filters.toggle", filterLabel, ActionSurfaceOrientation.Horizontal,
            UI.Icon(CaptureIcon("filter",WidgetGlyph.Settings),"capture-library.filters.icon","Filters"),
            UI.Text(activeFilters==0?"":activeFilters.ToString(CultureInfo.CurrentCulture), "capture-library.filters.label")).Classes("capture-filter-trigger")
            .PresentOnFocus(filterPanel);
        var controls=UI.Row("capture-library.search-row",
            UI.TextEntry(filter.Search ?? "", "Search captures", "capture-library.search", "capture-library.search", 160).Classes("capture-search"),
            HomeCaptureActions(state),
            UI.FocusPresentationSurface(filterButton, UI.Text("", "capture-library.filters.empty"), "capture-library.filters.preview") with
            { Preview = new(420, 580, AnchorPlacement.Below) { EnterButton = ControllerButton.A, SizeToContentHeight = true } },
            ViewControl(query),
            UI.IconButton(CaptureIcon("select",WidgetGlyph.Check), "capture-library.selection.toggle", "capture-library.selection.toggle",selection.IsActive ? "Done selecting" : "Select captures",IconButtonVariant.Quiet)
                .Selected(selection.IsActive).Disabled(!selection.IsActive && state.Page?.TotalCount is not > 0),MoreCommands(state.Busy))
            .RememberChildFocus("capture-library.search").Classes("capture-header-tools");
        yield return UI.Grid("capture-library.header",[],[GridTrack.Auto(),GridTrack.Star()],
            UI.Stack("capture-library.heading-row",UI.Text("Capture Library","capture-library.title").Classes("capture-title"),
                UI.Text(GalleryCount(state),"capture-library.count").Classes("capture-gallery-count")).Classes("capture-library-heading").InGrid(),
            controls.InGrid(column:1)).Spacing(0,16).Classes("capture-library-header");
        if (state.Replay is {} replay && (replay.Settings.Enabled || ReplaySettingsLocked(replay) || replay.Phase==CaptureReplayPhase.Error))
            yield return ReplayToolbar(state);
        else if (state.ReplayNotice is {} notice) yield return UI.Text(notice,"capture-library.replay.notice").Classes("capture-help");
        if (filter.Reference is not null)
            yield return UI.Row("capture-library.related-filter",
                UI.Text(query.RelatedLabel ?? "Related captures", "capture-library.related-filter.label").Classes("capture-heading", "capture-related-name"),
                UI.Button("Remove related filter", "capture-library.related-filter.clear", "capture-library.related-filter.clear"))
                .Classes("capture-related-filter");
        var appliedFilters = new List<WidgetElement>();
        if (filter.UnavailableOnly || filter.HiddenOnly || filter.FavoritesOnly || filter.MediaKind != CaptureLibraryMediaKind.All)
            appliedFilters.Add((UI.Button((filter.UnavailableOnly ? "Unavailable" : filter.HiddenOnly ? "Hidden" : filter.FavoritesOnly ? "Favorites" : filter.MediaKind == CaptureLibraryMediaKind.Screenshot ? "Screenshots" : "Clips") + " \u00d7",
                "capture-library.filter.all", "capture-library.active.type") with { AccessibilityLabel = "Remove media type filter" }).Classes("capture-active-filter"));
        if (filter.Application is { } application)
            appliedFilters.Add((UI.Button((application.Length == 0 ? "Unknown application" : application) + " \u00d7",
                "capture-library.application.all", "capture-library.active.application") with { AccessibilityLabel = "Remove application filter" }).Classes("capture-active-filter"));
        if (dateFilter != "all")
            appliedFilters.Add((UI.Button((dateFilter == "week" ? "Last 7 days" : "Last 30 days") + " \u00d7",
                "capture-library.date.all", "capture-library.active.date") with { AccessibilityLabel = "Remove date filter" }).Classes("capture-active-filter"));
        if (filter.SourceId is not null)
            appliedFilters.Add((UI.Button(SelectedFolderLabel(query) + " \u00d7",
                "capture-library.folder-filter.all", "capture-library.active.folder") with { AccessibilityLabel = "Remove folder filter" }).Classes("capture-active-filter"));
        if (appliedFilters.Count > 0)
            yield return UI.HorizontalScroll("capture-library.active-filters", appliedFilters.ToArray()).Classes("capture-active-filters");
        if (query.MoreApplications)
            yield return UI.Text("Use Search to narrow the application list.", "capture-library.applications.more").Classes("capture-help");
        if (selection.IsActive && selection.Count > 0)
        {
            yield return UI.HorizontalScroll("capture-library.selection.actions",
                ShareSelectionButton(state),
                AssociateSelectedButton(state),
                UI.Button("Favorite", "capture-library.batch.favorite", "capture-library.batch.favorite").Disabled(selection.Count==0).Busy(state.Busy),
                UI.Button("Save copies", "capture-library.batch.copy", "capture-library.batch.copy").Disabled(selection.Count==0).Busy(state.Busy),
                UI.Button(filter.HiddenOnly ? "Unhide" : "Hide…", filter.HiddenOnly ? "capture-library.batch.unhide" : "capture-library.batch.hide",
                    "capture-library.batch.hide").Busy(state.Busy),
                UI.Button("Delete…", "capture-library.batch.recycle", "capture-library.batch.recycle")
                    .Busy(state.Busy).Classes("capture-delete")).Classes("capture-selection-actions");
            if (selection.Count > MaximumSharedCaptures)
                yield return UI.Text("Share up to 10 captures at a time.", "capture-library.share.limit").Classes("capture-help");
            if (selection.Count > 64)
                yield return UI.Text("Link up to 64 captures to a game at a time.", "capture-library.associate.limit").Classes("capture-help");
        }
        if (selection.IsActive && (gallery.Descriptor.Count > IndexedCollectionSelection.MaximumItems || selection.LimitReached))
            yield return UI.Text($"Select up to {IndexedCollectionSelection.MaximumItems} captures per batch.", "capture-library.selection.limit").Classes("capture-help");
        if (state.Page?.TotalCount == 0)
            yield return UI.Text(filter == DefaultGalleryFilter ? "Your saved screenshots and clips will appear here." : filter.UnavailableOnly ? "No unavailable captures match these filters." : "No captures match these filters.", "capture-library.empty").Classes("capture-empty");
        else if (state.Page is not null)
        {
            var collection = query.ShowDetails ? UI.CollectionList(GalleryId, gallery, 110, "Saved captures") :
                UI.CollectionGrid(GalleryId, gallery, query.Density switch {0=>230,2=>420,_=>300}, query.Density switch {0=>208,2=>344,_=>264}, "Saved captures", maximumColumns: query.Density==2?2:query.Density==0?4:3);
            if (query.Groups.Count > 0) collection = collection.Grouped(query.Groups.Select(group => new IndexedCollectionGroup("month."+group.Key, GroupLabel(group.Key),group.Count)).ToArray());
            var content = collection.Classes("capture-gallery");
            yield return query.ClipPreviews && !selection.IsActive
                ? (UI.FocusPresentationSurface(content, UI.Text("", "capture-library.gallery.preview.empty"), "capture-library.gallery.preview") with
                { Preview = new(560, 560, AnchorPlacement.CenterOnAnchor, DelayMilliseconds: 1500) { EnterButton = ControllerButton.RightStick, AllowMedia = true, SizeToContentHeight = true } }).Classes("capture-gallery-preview")
                : content;
        }
    }

    private void ApplyClipPreviewPreference(bool enabled)
    {
        lock (galleryGate)
        {
            if (galleryQuery.ClipPreviews == enabled) return;
            galleryQuery = galleryQuery with { ClipPreviews = enabled };
            gallery.UpdateContent(galleryQuery);
        }
    }

    private WidgetElement FilterPanel(GalleryQuery query)
    {
        var filter = query.Filter;
        var choices = UI.Stack("capture-library.filters.choices",
            UI.Select("Type", new SelectOption[] {
                new("all", "All captures", "capture-library.filter.all", IsSelected:filter.MediaKind==CaptureLibraryMediaKind.All&&!filter.FavoritesOnly&&filter.AvailableOnly),
                new("screenshots", "Screenshots", "capture-library.filter.screenshots", IsSelected:filter.MediaKind==CaptureLibraryMediaKind.Screenshot&&!filter.FavoritesOnly&&filter.AvailableOnly),
                new("clips", "Clips", "capture-library.filter.clips", IsSelected:filter.MediaKind==CaptureLibraryMediaKind.Video&&!filter.FavoritesOnly&&filter.AvailableOnly),
                new("favorites", "Favorites", "capture-library.filter.favorites", IsSelected:filter.FavoritesOnly&&!filter.HiddenOnly),
                new("unavailable", "Unavailable", "capture-library.filter.unavailable", IsSelected:filter.UnavailableOnly),
                new("hidden", "Hidden", "capture-library.filter.hidden", IsSelected:filter.HiddenOnly)}, "capture-library.filter"),
            UI.Select("Application", ApplicationOptions(query), "capture-library.application"),
            UI.Select("Folder", FolderOptions(query), "capture-library.folder-filter"),
            UI.Select("Date", new SelectOption[] { new("all","Any date","capture-library.date.all",IsSelected:dateFilter=="all"), new("week","Last 7 days","capture-library.date.week",IsSelected:dateFilter=="week"), new("month","Last 30 days","capture-library.date.month",IsSelected:dateFilter=="month") }, "capture-library.date"),
            UI.Select("Sort", Enum.GetValues<CaptureLibrarySort>().Select(sort=>new SelectOption(sort.ToString(),SortLabel(sort),"capture-library.sort."+sort,IsSelected:filter.Sort==sort)).ToArray(), "capture-library.sort"));
        return UI.Stack("capture-library.filters",
            UI.Text("Filters", "capture-library.filters.heading").Classes("capture-heading"),
            choices,
            UI.Button("Clear filters", "capture-library.clear", "capture-library.clear").Disabled(filter == DefaultGalleryFilter))
            .Classes("capture-filter-panel");
    }

    private static WidgetElement ViewControl(GalleryQuery query)
    {
        var choices=new[]{("compact","Small thumbnails",!query.ShowDetails&&query.Density==0),
            ("normal","Medium thumbnails",!query.ShowDetails&&query.Density==1),
            ("large","Large thumbnails",!query.ShowDetails&&query.Density==2),("details","Details",query.ShowDetails)};
        var panel=UI.Stack("capture-library.view.choices",
            [UI.Text("View","capture-library.view.heading").Classes("capture-heading"),
             ..choices.Select(choice=>UI.Button(choice.Item2,"capture-library.view."+choice.Item1,"capture-library.view."+choice.Item1)
                .Selected(choice.Item3))]).Classes("capture-filter-panel");
        var trigger=UI.ReadingSurface("capture-library.view.toggle","View options",ActionSurfaceOrientation.Horizontal,
            UI.Icon(CaptureIcon("view",WidgetGlyph.Fullscreen),"capture-library.view.icon","View options"))
            .Classes("capture-filter-trigger").PresentOnFocus(panel);
        return UI.FocusPresentationSurface(trigger,UI.Text("","capture-library.view.empty"),"capture-library.view.preview") with
            {Preview=new(280,360,AnchorPlacement.Below){EnterButton=ControllerButton.A,SizeToContentHeight=true}};
    }

    private static string GroupLabel(string key) => key switch
    {
        "older" => "Older captures", "newer" => "Newer captures",
        _ => DateTime.TryParseExact(key,"yyyy-MM",CultureInfo.InvariantCulture,DateTimeStyles.None,out var month)
            ? month.ToString("MMMM yyyy",CultureInfo.CurrentCulture) : "Other captures",
    };
    private static string ApplicationAction(string name) => WidgetIds.Scope("capture-library.application").KeyedId("pick", "app:"+name);
    private static string FolderAction(string sourceId) => "capture-library.folder-filter.pick." + (sourceId.Length == 0 ? "owned" : sourceId);
    private static string SelectedFolderLabel(GalleryQuery query) => query.Folders.FirstOrDefault(folder => folder.SourceId == query.Filter.SourceId)?.Label ??
        query.FolderLabel ?? (query.Filter.SourceId == "" ? "WidgetRail captures" : "Disconnected folder");
    private static SelectOption[] FolderOptions(GalleryQuery query)
    {
        var choices = new List<SelectOption> { new("all", "All folders", "capture-library.folder-filter.all", IsSelected: query.Filter.SourceId is null) };
        foreach (var folder in query.Folders)
            choices.Add(new(FolderAction(folder.SourceId), folder.Label, FolderAction(folder.SourceId), IsSelected: query.Filter.SourceId == folder.SourceId));
        if (query.Filter.SourceId is { } selected && !query.Folders.Any(folder => folder.SourceId == selected))
            choices.Add(new(FolderAction(selected), SelectedFolderLabel(query), FolderAction(selected), IsSelected: true, IsDisabled: true));
        return choices.ToArray();
    }
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
                case "capture-library.focus-controls" when !showSources && !showContexts && !showBatchFailures && pendingBatch is null:
                    galleryReturn = CreateFocusRequest().FocusElement("capture-library.search-row", "capture-library.search");
                    Invalidate(); return true;
                case "capture-library.selection.toggle":
                    if(gallery.Selection!.IsActive)gallery.EndSelection();else gallery.BeginSelection();
                    return true;
                case "capture-library.selection.all" when gallery.Selection?.IsActive == true: gallery.SelectAll();return true;
                case "capture-library.selection.first" when gallery.Selection?.IsActive == true: gallery.SelectFirst(IndexedCollectionSelection.MaximumItems);return true;
                case "capture-library.selection.clear": gallery.ClearSelection();return true;
                case "capture-library.batch.favorite": PrepareBatch("favorite");return true;
                case "capture-library.batch.unfavorite": PrepareBatch("unfavorite");return true;
                case "capture-library.batch.unhide": if (galleryQuery.Filter.HiddenOnly) PrepareBatch("unhide");return true;
                case "capture-library.batch.recycle": PrepareBatch("recycle");return true;
                case "capture-library.batch.hide": if (!galleryQuery.Filter.HiddenOnly) PrepareBatch("hide");return true;
                case "capture-library.batch.copy": PrepareBatch("copy");return true;
            }
            var filter = galleryQuery.Filter;
            var changed = true;
            switch(action.ActionId)
            {
                case "capture-library.search": filter=filter with{Search=action.CommittedText?.Trim()}; break;
                case "capture-library.application.all": filter=filter with{Application=null}; break;
                case "capture-library.folder-filter.all": filter=filter with{SourceId=null}; break;
                case "capture-library.clear": filter=DefaultGalleryFilter;dateFilter="all";break;
                case "capture-library.related-filter.clear": filter=filter with{Reference=null,OriginPackageId=null};break;
                case "capture-library.filter.all":filter=filter with{MediaKind=CaptureLibraryMediaKind.All,FavoritesOnly=false,HiddenOnly=false,AvailableOnly=true,UnavailableOnly=false};break;
                case "capture-library.filter.screenshots":filter=filter with{MediaKind=CaptureLibraryMediaKind.Screenshot,FavoritesOnly=false,HiddenOnly=false,AvailableOnly=true,UnavailableOnly=false};break;
                case "capture-library.filter.clips":filter=filter with{MediaKind=CaptureLibraryMediaKind.Video,FavoritesOnly=false,HiddenOnly=false,AvailableOnly=true,UnavailableOnly=false};break;
                case "capture-library.filter.favorites":filter=filter with{MediaKind=CaptureLibraryMediaKind.All,FavoritesOnly=true,HiddenOnly=false,AvailableOnly=true,UnavailableOnly=false};break;
                case "capture-library.filter.hidden":filter=filter with{MediaKind=CaptureLibraryMediaKind.All,FavoritesOnly=false,HiddenOnly=true,AvailableOnly=false,UnavailableOnly=false};break;
                case "capture-library.filter.unavailable":filter=filter with{MediaKind=CaptureLibraryMediaKind.All,FavoritesOnly=false,HiddenOnly=false,AvailableOnly=false,UnavailableOnly=true};break;
                default:
                    if(galleryQuery.Applications.FirstOrDefault(app=>ApplicationAction(app.Name)==action.ActionId) is { } application)filter=filter with{Application=application.Name};
                    else if(galleryQuery.Folders.FirstOrDefault(folder=>FolderAction(folder.SourceId)==action.ActionId) is { } folder)filter=filter with{SourceId=folder.SourceId};
                    else if(action.ActionId.StartsWith("capture-library.sort.",StringComparison.Ordinal)&&Enum.TryParse<CaptureLibrarySort>(action.ActionId["capture-library.sort.".Length..],out var sort))filter=filter with{Sort=sort};
                    else if(action.ActionId.StartsWith("capture-library.date.",StringComparison.Ordinal)) {
                        dateFilter=action.ActionId["capture-library.date.".Length..];
                        if(dateFilter is not ("all" or "week" or "month"))return true;
                        filter=filter with{FromUnixMilliseconds=dateFilter=="all"?null:DateTimeOffset.UtcNow.AddDays(dateFilter=="week"?-7:-30).ToUnixTimeMilliseconds()};
                    }
                    else if(action.ActionId.StartsWith("capture-library.view.",StringComparison.Ordinal)) {
                        var mode=action.ActionId["capture-library.view.".Length..];
                        if(mode is not ("compact" or "normal" or "large" or "details"))return true;
                        galleryQuery=galleryQuery with{Density=mode=="compact"?0:mode=="large"?2:1,Details=galleryQuery.Filter.UnavailableOnly ? galleryQuery.Details : mode=="details",RecoveryGrid=galleryQuery.Filter.UnavailableOnly && mode!="details"};
                        gallery.UpdateContent(galleryQuery);Invalidate();return true;
                    }
                    else changed=false;
                    break;
            }
            if(!changed)return false;
            if(!filter.IsWellFormed())return true;
            galleryQuery=galleryQuery with{Filter=filter,Revision=null,RelatedLabel=filter.Reference is null?null:galleryQuery.RelatedLabel,
                FolderLabel=filter.SourceId is null?null:galleryQuery.Folders.FirstOrDefault(folder=>folder.SourceId==filter.SourceId)?.Label??galleryQuery.FolderLabel};selectedCapture=null;galleryReturn=null;
        }
        QueueGalleryRefresh();return true;
    }
}
