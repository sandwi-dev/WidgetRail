using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameHelp;

public sealed partial class GameHelpWidget
{
    private bool HandleLibraryAction(WidgetActionEvent action, State state)
    {
        if (!action.ActionId.StartsWith("library.", StringComparison.Ordinal)) return false;
        if (action.ActionId == "library.close")
        {
            Operations.Cancel("library"); model.Update(s => s with { LibraryOpen = false, LibraryBusy = false }); return true;
        }
        if (state.Busy || state.Capturing) return true;
        if (action.ActionId is "library.open" or "library.next" or "library.previous" or "library.refresh")
        {
            var paging = action.ActionId is "library.next" or "library.previous";
            var offset = action.ActionId == "library.open" ? 0 : (int)Math.Clamp((long)(state.LibraryPage?.Offset ?? 0) +
                (action.ActionId == "library.next" ? CaptureLibraryLimits.PageSize : action.ActionId == "library.previous" ? -CaptureLibraryLimits.PageSize : 0), 0, int.MaxValue);
            LoadLibrary(state.LibraryQuery, offset, paging ? state.LibraryPage?.Revision : null);
        }
        else if (action.ActionId == "library.search" && action.CommittedText is { Length: <= 160 } search && !search.Any(char.IsControl))
            LoadLibrary(state.LibraryQuery with { Search = search.Trim() }, 0, focusResults: false);
        else if (action.ActionId == "library.clear") LoadLibrary(state.LibraryQuery with { Search = null }, 0, focusResults: false);
        else if (action.ActionId == "library.favorites") LoadLibrary(state.LibraryQuery with { FavoritesOnly = !state.LibraryQuery.FavoritesOnly }, 0, focusResults: false);
        else if (action.ActionId.StartsWith("library.kind.", StringComparison.Ordinal) &&
            Enum.TryParse<CaptureLibraryMediaKind>(action.ActionId["library.kind.".Length..], out var kind) && Enum.IsDefined(kind))
            LoadLibrary(state.LibraryQuery with { MediaKind = kind }, 0, focusResults: false);
        else if (action.ActionId.StartsWith("library.select.", StringComparison.Ordinal))
        {
            var id = action.ActionId["library.select.".Length..];
            if (state.LibraryBusy || state.LibraryPage?.Items.Any(item => item.Id == id && item.Available && FitsLibraryContext(item)) != true) return true;
            if (!string.IsNullOrWhiteSpace(state.Draft)) AddQuestion(state.Draft, true);
            else if (state.PendingQuestion is null || state.Messages.LastOrDefault()?.Role == "assistant") AddQuestion("Help me understand this part of the game.", true);
            model.Update(s => s with { LibraryBusy = true, Error = null });
            Operations.RunLatest("library", async context =>
            {
                CaptureAttachment? opened = null;
                try
                {
                    // Images use a context rendition; videos retain the original under a bounded read lease.
                    opened = await HostServices.CaptureLibrary.OpenAsync(id, CaptureImageResolution.Context, context.CancellationToken);
                    if (!context.IsCurrent) return;
                    if (opened.ByteLength > CaptureLimits.MaximumContextBytes)
                    { model.Update(s => s with { Error = "This clip is too large for game context. Choose a shorter clip or a screenshot. Your question is kept." }); return; }
                    var old = model.Value.Preview;
                    model.Update(s => s with { Preview = opened, PreviewReadOnly = false, Context = s.Context == old ? null : s.Context,
                        LibraryOpen = false, OfferContext = false, CaptureOptions = false });
                    opened = null;
                    if (old is not null) try { await HostServices.Capture.DiscardAsync(old, context.CancellationToken); } catch (WidgetCapabilityException) { }
                }
                catch (OperationCanceledException) { }
                catch (WidgetCapabilityException) { if (context.IsCurrent) model.Update(s => s with { Error = "This saved capture could not be opened. It may have been removed or its permissions changed. Your question is kept." }); }
                finally
                {
                    if (opened is not null) try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await HostServices.CaptureLibrary.ReleaseAsync(opened, cleanup.Token); } catch (Exception) { }
                    if (context.IsCurrent) model.Update(s => s with { LibraryBusy = false });
                }
            }, WidgetOperationLifetime.Widget);
        }
        return true;
    }
    private void LoadLibrary(CaptureLibraryQuery query, int offset, string? revision = null, bool focusResults = true)
    {
        model.Update(s => s with { LibraryOpen = true, LibraryBusy = true, LibraryQuery = query, CaptureOptions = false, Error = null });
        Operations.RunLatest("library", async context =>
        {
            IWidgetCapabilitySubscription<CaptureLibraryChanged>? subscription = null;
            try
            {
                await RequirePermissionAsync(WidgetCaptureLibraryCapabilities.ReadPermission, context.CancellationToken);
                // Acknowledged subscription precedes the snapshot. Losing the event
                // channel must not prevent an otherwise valid current-state read.
                try { subscription = await HostServices.CaptureLibrary.OpenChangesAsync(context.CancellationToken); }
                catch (WidgetCapabilityException error) when (error.ErrorCode is "unsupported_event" or "service_unavailable" or "subscription_unavailable") { }
                await ReadPage(offset, revision, focusResults);
                if (subscription is not null)
                    await foreach (var change in subscription.ReadAllAsync(context.CancellationToken))
                    {
                        if (!context.IsCurrent || !model.Value.LibraryOpen) break;
                        await ReadPage(model.Value.LibraryPage?.Offset ?? 0, null, false);
                    }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
            catch (WidgetCapabilityException error) { if (context.IsCurrent) model.Update(s => s with { Error = Friendly(error) }); }
            catch (GameHelpException error) { if (context.IsCurrent) model.Update(s => s with { Error = Friendly(error) }); }
            finally
            {
                if (subscription is not null) await subscription.DisposeAsync();
                if (context.IsCurrent) model.Update(s => s with { LibraryBusy = false });
            }
            async Task ReadPage(int pageOffset, string? expectedRevision, bool focus)
            {
                CaptureLibraryQueryPage page;
                try { page = await HostServices.CaptureLibrary.QueryAsync(query, pageOffset, CaptureLibraryLimits.PageSize, expectedRevision, context.CancellationToken); }
                catch (WidgetCapabilityException error) when (error.ErrorCode == "capture_library_query_changed")
                { page = await HostServices.CaptureLibrary.QueryAsync(query, 0, CaptureLibraryLimits.PageSize, cancellationToken: context.CancellationToken); }
                if (page.TotalCount > 0 && page.Items.Count == 0 && page.Offset > 0)
                    page = await HostServices.CaptureLibrary.QueryAsync(query, (page.TotalCount - 1) / CaptureLibraryLimits.PageSize * CaptureLibraryLimits.PageSize,
                        CaptureLibraryLimits.PageSize, page.Revision, context.CancellationToken);
                if (context.IsCurrent) model.Update(s => s with { LibraryPage = page, LibraryBusy = false,
                    LibraryFocusRequest = focus ? CreateFocusRequest().Id : 0 });
            }
        }, WidgetOperationLifetime.Active);
    }

    private static bool FitsLibraryContext(CaptureLibraryItem item) => item.Media.ContentType != "video/mp4" || item.Media.ByteLength <= CaptureLimits.MaximumContextBytes;

    private static WidgetView LibraryView(WidgetView view, State state)
    {
        var items = new List<WidgetElement> { UI.Text("Choose saved context. You can review it and edit your question before anything is sent to Gemini.", "help.library.help").Classes("help-muted") };
        items.Add(UI.Row("help.library.filters",
            UI.TextEntry(state.LibraryQuery.Search ?? "", "Search saved captures", "library.search", "help.library.search", 160).Classes("help-library-search"),
            UI.Button("Clear", "library.clear", "help.library.clear").Disabled(string.IsNullOrEmpty(state.LibraryQuery.Search)),
            UI.Select("Capture type", Enum.GetValues<CaptureLibraryMediaKind>().Select(kind => new SelectOption(kind.ToString(),
                kind == CaptureLibraryMediaKind.All ? "All captures" : kind == CaptureLibraryMediaKind.Screenshot ? "Screenshots" : "Clips",
                "library.kind." + kind, state.LibraryQuery.MediaKind == kind)).ToArray(), "help.library.kind").Classes("help-library-kind")).Classes("help-library-filters"));
        items.Add(UI.Switch("Favorites only", state.LibraryQuery.FavoritesOnly, "library.favorites", "help.library.favorites"));
        if (state.LibraryPage is { } page)
        {
            var entries = new List<WidgetElement>();
            foreach (var item in page.Items)
                entries.Add(UI.Button((item.Title ?? item.Media.SourceApplication?.ApplicationName ?? "Application capture") + " · " +
                    DateTimeOffset.FromUnixTimeMilliseconds(item.SavedAtUnixMilliseconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture) +
                    (item.Media.ContentType == "video/mp4" ? " · Clip" : " · Screenshot") + (item.Available ? FitsLibraryContext(item) ? "" : " · Too large for game context" : " · Unavailable"), "library.select." + item.Id, "help.library.item." + item.Id).Busy(state.LibraryBusy).Disabled(!item.Available || !FitsLibraryContext(item)));
            if (page.Items.Count > 0) items.Add(UI.Stack("help.library.items", entries.ToArray()).RememberChildFocus("help.library.item." + page.Items[0].Id));
            items.Add(UI.Text($"Page {page.Offset / CaptureLibraryLimits.PageSize + 1} of {Math.Max(1, (page.TotalCount + CaptureLibraryLimits.PageSize - 1) / CaptureLibraryLimits.PageSize)}", "help.library.page-number").Classes("help-muted"));
            if (page.TotalCount == 0) items.Add(UI.Text("No captures match. Try another search or filter, or save a capture first.", "help.library.empty").Classes("help-muted"));
            items.Add(UI.Row("help.library.paging", UI.Button("Previous", "library.previous", "help.library.previous").Disabled(page.Offset == 0).Busy(state.LibraryBusy),
                UI.Button("Next", "library.next", "help.library.next").Disabled(page.Offset + page.Items.Count >= page.TotalCount).Busy(state.LibraryBusy),
                UI.Button("Refresh", "library.refresh", "help.library.refresh").Busy(state.LibraryBusy)));
        }
        if (state.LibraryBusy) items.Add(UI.Text("Loading saved captures…", "help.library.loading").Classes("help-muted"));
        if (state.Error is { } error) items.Add(ErrorPanel(error, "help.library.error"));
        items.Add(UI.Button("Back", "library.close", "help.library.back"));
        var content = UI.Stack("help.library.content", items.ToArray()).Classes("help-dialog");
        if (!state.LibraryBusy && state.LibraryPage is { } pages)
        {
            if (pages.Offset > 0) content = content.Shortcut(ControllerButton.LeftBumper, "library.previous", "Previous page");
            if (pages.Offset + pages.Items.Count < pages.TotalCount) content = content.Shortcut(ControllerButton.RightBumper, "library.next", "Next page");
        }
        return view.WithModal(new("help.library", "Saved captures", content,
            state.LibraryPage?.Items.FirstOrDefault() is { } first ? "help.library.item." + first.Id : "help.library.back", "library.close")) with
        {
            FocusGroupEntryRequest = state.LibraryPage?.Items.Count > 0 && state.LibraryFocusRequest > 0
                ? new() { RequestId = state.LibraryFocusRequest, GroupId = "help.library.items" } : null,
        };
    }
}
