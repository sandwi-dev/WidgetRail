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
        if (state.Busy || state.Capturing || state.LibraryBusy) return true;
        if (action.ActionId is "library.open" or "library.next" or "library.previous")
        {
            var offset = action.ActionId == "library.open" ? 0 : (int)Math.Clamp((long)(state.LibraryPage?.Offset ?? 0) + (action.ActionId == "library.next" ? 20 : -20), 0, int.MaxValue);
            model.Update(s => s with { LibraryOpen = true, LibraryBusy = true, CaptureOptions = false, Error = null });
            Operations.RunLatest("library", async context =>
            {
                try
                {
                    await RequirePermissionAsync(WidgetCaptureLibraryCapabilities.ReadPermission, context.CancellationToken);
                    var page = await HostServices.CaptureLibrary.ListAsync(offset, context.CancellationToken);
                    if (context.IsCurrent) model.Update(s => s with { LibraryPage = page, LibraryFocusRequest = Interlocked.Increment(ref nextFocusRequest) });
                }
                catch (OperationCanceledException) { }
                catch (WidgetCapabilityException error) { if (context.IsCurrent) model.Update(s => s with { Error = Friendly(error) }); }
                catch (GameHelpException error) { if (context.IsCurrent) model.Update(s => s with { Error = Friendly(error) }); }
                finally { if (context.IsCurrent) model.Update(s => s with { LibraryBusy = false }); }
            }, WidgetOperationLifetime.Widget);
        }
        else if (action.ActionId.StartsWith("library.select.", StringComparison.Ordinal))
        {
            var id = action.ActionId["library.select.".Length..];
            if (state.LibraryPage?.Items.Any(item => item.Id == id) != true) return true;
            if (!string.IsNullOrWhiteSpace(state.Draft)) AddQuestion(state.Draft, true);
            else if (state.PendingQuestion is null || state.Messages.LastOrDefault()?.Role == "assistant") AddQuestion("Help me understand this part of the game.", true);
            model.Update(s => s with { LibraryBusy = true, Error = null });
            Operations.RunLatest("library", async context =>
            {
                CaptureAttachment? opened = null;
                try
                {
                    // A context-sized owned copy fits the existing Gemini request budget.
                    // The library original remains at its saved resolution.
                    opened = await HostServices.CaptureLibrary.OpenAsync(id, CaptureImageResolution.Context, context.CancellationToken);
                    if (!context.IsCurrent) return;
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
    private static WidgetView LibraryView(WidgetView view, State state)
    {
        var items = new List<WidgetElement> { UI.Text("Choose saved context. You can review it and edit your question before anything is sent to Gemini.", "help.library.help").Classes("help-muted") };
        if (state.LibraryPage is { } page)
        {
            var entries = new List<WidgetElement>();
            foreach (var item in page.Items)
                entries.Add(UI.Button((item.Media.SourceApplication?.ApplicationName ?? "Application capture") + " · " +
                    DateTimeOffset.FromUnixTimeMilliseconds(item.SavedAtUnixMilliseconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture) +
                    (item.Media.ContentType == "video/mp4" ? " · Clip" : " · Screenshot"), "library.select." + item.Id, "help.library.item." + item.Id).Busy(state.LibraryBusy));
            if (page.Items.Count > 0) items.Add(UI.Stack("help.library.items", entries.ToArray()).RememberChildFocus("help.library.item." + page.Items[0].Id));
            items.Add(UI.Text($"Page {page.Offset / CaptureLibraryLimits.PageSize + 1} of {Math.Max(1, (page.TotalCount + CaptureLibraryLimits.PageSize - 1) / CaptureLibraryLimits.PageSize)}", "help.library.page-number").Classes("help-muted"));
            if (page.TotalCount == 0) items.Add(UI.Text("Save a screenshot or clip in Capture Library first.", "help.library.empty").Classes("help-muted"));
            items.Add(UI.Row("help.library.paging", UI.Button("Previous", "library.previous", "help.library.previous").Disabled(page.Offset == 0).Busy(state.LibraryBusy),
                UI.Button("Next", "library.next", "help.library.next").Disabled(page.Offset + page.Items.Count >= page.TotalCount).Busy(state.LibraryBusy)));
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
