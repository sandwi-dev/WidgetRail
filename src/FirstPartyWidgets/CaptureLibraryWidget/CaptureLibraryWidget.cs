using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget : Widget
{
    private sealed record State(CaptureLibraryPage? Page = null, CaptureAttachment? Preview = null, bool FromLibrary = false,
        string? SelectedId = null, bool Detail = false, bool Busy = false, bool Capturing = false, bool ConfirmDelete = false, string? Message = null, long FocusRequest = 0,
        WidgetNavigationOperation Navigation = WidgetNavigationOperation.Forward, bool Settings = false, CapturePreferences? Preferences = null, CaptureCapabilities? Capabilities = null, long PreferencesRevision = 0);
    private readonly WidgetModel<State> model;
    private long nextFocusRequest;
    public CaptureLibraryWidget() => model = CreateModel(new State());
    protected override ValueTask OnActivatedAsync(CancellationToken token)
    {
        var state = model.Value;
        if (!state.Busy && state.Preview is { } preview && preview.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            if (state.SelectedId is { } id)
                Work(async lifetime => { var reopened = await HostServices.CaptureLibrary.OpenAsync(id, cancellationToken: lifetime); model.Update(s => s with { Preview = reopened, FromLibrary = true }); });
            else
                Work(async lifetime => { model.Update(s => s with { Preview = null, Detail = false }); await RefreshAsync(state.Page?.Offset ?? 0, lifetime); model.Update(s => s with { Message = "The unsaved capture expired. Take another capture to save it." }); });
        }
        else if (!state.Busy) Load(state.Page?.Offset ?? 0);
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        var state = model.Value;
        if (action.ActionId == "capture-library.cancel")
        { Operations.Cancel("capture-library.work"); model.Update(s => s with { Busy = false, Capturing = false, Message = "Capture cancelled." }); return ValueTask.CompletedTask; }
        if (action.ActionId == "capture-library.settings.back")
        { model.Update(s => s with { Settings = false, Navigation = WidgetNavigationOperation.Back, Message = null }); return ValueTask.CompletedTask; }
        if (state.Busy) return ValueTask.CompletedTask;
        if (action.ActionId.StartsWith("capture-library.option.", StringComparison.Ordinal))
        { ChangeSetting(action.ActionId); return ValueTask.CompletedTask; }
        switch (action.ActionId)
        {
            case "capture-library.allow": Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ReadPermission, WidgetCaptureCapabilities.Permission, WidgetCaptureLibraryCapabilities.SavePermission], token); await RefreshAsync(state.Page?.Offset ?? 0, token); }); break;
            case "capture-library.settings":
                model.Update(s => s with { Settings = true, Navigation = WidgetNavigationOperation.Forward });
                Work(LoadSettingsAsync); break;
            case "capture-library.photo": Capture(WindowCaptureKind.Screenshot); break;
            case "capture-library.video": Capture(WindowCaptureKind.Video); break;
            case "capture-library.refresh": Work(async token => { var page=await HostServices.CaptureLibrary.RefreshAsync(state.Page?.Offset??0,token); model.Update(s=>s with {Page=page}); }); break;
            case "capture-library.next": Load((int)Math.Min(int.MaxValue, (long)(state.Page?.Offset ?? 0) + CaptureLibraryLimits.PageSize), enterPage: true); break;
            case "capture-library.previous": Load(Math.Max(0, (state.Page?.Offset ?? 0) - CaptureLibraryLimits.PageSize), enterPage: true); break;
            case "capture-library.back":
                Work(async token => { await ReleaseAsync(state, token); model.Update(s => s with { Preview = null, Detail = false, Navigation = WidgetNavigationOperation.Back, ConfirmDelete = false }); await RefreshAsync(state.Page?.Offset ?? 0, token); }); break;
            case "capture-library.save" when state.Preview is { } preview:
                Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.SavePermission], token); var item = await HostServices.CaptureLibrary.SaveAsync(preview, token); model.Update(s => s with { SelectedId = item.Id, Message = "Saved to Pictures → WidgetRail → Captures." }); }); break;
            case "capture-library.folder":
                Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token); await HostServices.CaptureLibrary.OpenFolderAsync(token); }); break;
            case "capture-library.delete" when state.SelectedId is not null: model.Update(s => s with { ConfirmDelete = true }); break;
            case "capture-library.delete.cancel": model.Update(s => s with { ConfirmDelete = false }); break;
            case "capture-library.delete.confirm" when state.ConfirmDelete && state.SelectedId is { } deleted:
                Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token); await HostServices.CaptureLibrary.DeleteAsync(deleted, token); await ReleaseAsync(state, token); model.Update(s => s with { SelectedId = null, Preview = null, Detail = false, Navigation = WidgetNavigationOperation.Back, ConfirmDelete = false, Message = "Deleted the saved file from Pictures → WidgetRail → Captures." }); await RefreshAsync(state.Page?.Offset ?? 0, token); }); break;
            default:
                if (action.ActionId.StartsWith("capture-library.open.", StringComparison.Ordinal))
                {
                    var selected = action.ActionId["capture-library.open.".Length..];
                    if (state.Page?.Items.Any(item => item.Id == selected) == true)
                        Work(async token => { await ReleaseAsync(state, token); model.Update(s => s with { Preview = null, Detail = true, SelectedId = selected, Navigation = WidgetNavigationOperation.Forward }); var media = await HostServices.CaptureLibrary.OpenAsync(selected, cancellationToken: token); model.Update(s => s with { Preview = media, FromLibrary = true, SelectedId = selected }); });
                }
                break;
        }
        return ValueTask.CompletedTask;
    }
    private void Load(int offset, bool enterPage = false) => Work(token => RefreshAsync(offset, token, enterPage));
    private async Task RefreshAsync(int offset, CancellationToken token, bool enterPage = false)
    {
        var page = await HostServices.CaptureLibrary.ListAsync(offset, token);
        if (page.Items.Count == 0 && page.TotalCount > 0 && offset > 0) page = await HostServices.CaptureLibrary.ListAsync(Math.Max(0, ((page.TotalCount - 1) / CaptureLibraryLimits.PageSize) * CaptureLibraryLimits.PageSize), token);
        model.Update(s => s with { Page = page, FocusRequest = enterPage ? Interlocked.Increment(ref nextFocusRequest) : s.FocusRequest });
        await LoadPreferencesAsync(token);
    }
    private void Work(Func<CancellationToken, Task> work)
    {
        model.Update(s => s with { Busy = true, Message = null });
        Operations.RunLatest("capture-library.work", async context =>
        {
            try { await work(context.CancellationToken); }
            catch (OperationCanceledException) { }
            catch (WidgetCapabilityException error) { if (context.IsCurrent) model.Update(s => s with { Message = Explain(error.ErrorCode), ConfirmDelete = false }); }
            finally { if (context.IsCurrent) model.Update(s => s with { Busy = false }); }
        }, WidgetOperationLifetime.Widget);
    }
    private void Capture(WindowCaptureKind kind)
    {
        var prior = model.Value;
        model.Update(s => s with { Busy = true, Capturing = true, ConfirmDelete = false, Message = null });
        Operations.RunLatest("capture-library.work", async context =>
        {
            WindowCaptureTicket? ticket = null; var completed = false;
            try
            {
                await LoadPreferencesAsync(context.CancellationToken);
                await RequirePermissionsAsync([WidgetCaptureCapabilities.Permission], context.CancellationToken);
                if (kind == WindowCaptureKind.Video)
                {
                    var capabilities = await HostServices.Capture.GetRecordingCapabilitiesAsync(model.Value.Preferences?.Video, context.CancellationToken);
                    model.Update(s => s with { Preferences = s.Preferences is { Video: null } preferences ? preferences with { Video = DefaultVideo(capabilities) } : s.Preferences });
                }
                var settings = model.Value.Preferences ?? throw new WidgetCapabilityException("capture_preferences_invalid", "Invalid saved settings.");
                var request = settings.Request(kind);
                var validation = await HostServices.Capture.ValidateAsync(request, context.CancellationToken);
                if (!validation.Supported) throw new WidgetCapabilityException(validation.ErrorCode ?? "capture_unavailable", "Capture settings are unavailable.");
                await ReleaseAsync(prior, context.CancellationToken);
                model.Update(s => s with { Preview = null, SelectedId = null, Detail = false });
                ticket = await HostServices.Capture.StartAsync(request, context.CancellationToken);
                while (true)
                {
                    await Task.Delay(300, context.CancellationToken);
                    var status = await HostServices.Capture.GetStatusAsync(ticket, context.CancellationToken);
                    if (status.Phase == WindowCapturePhase.Ready)
                    {
                        completed = true;
                        if (context.IsCurrent) model.Update(s => s with { Preview = status.Attachment, Navigation = WidgetNavigationOperation.Forward, FromLibrary = false, SelectedId = null, Message = "Review your capture, then save it to keep it." });
                        else if (status.Attachment is { } abandoned) { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await HostServices.Capture.DiscardAsync(abandoned, cleanup.Token); }
                        break;
                    }
                    if (status.Phase is WindowCapturePhase.Cancelled or WindowCapturePhase.Failed)
                        throw new WidgetCapabilityException(status.ErrorCode ?? "capture_failed", "Capture did not finish.");
                }
            }
            catch (OperationCanceledException) { }
            catch (WidgetCapabilityException error) { if (context.IsCurrent) model.Update(s => s with { Message = Explain(error.ErrorCode) }); }
            finally
            {
                if (!completed && ticket is not null) try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await HostServices.Capture.CancelAsync(ticket, cleanup.Token); } catch (Exception) { }
                if (context.IsCurrent) model.Update(s => s with { Busy = false, Capturing = false });
            }
        }, WidgetOperationLifetime.Widget);
    }
    private static WidgetView Page(WidgetView view, string key, WidgetNavigationOperation operation) => view with
    {
        Root = view.Root.TransitionPage("capture-library.pages", key, operation),
    };

    private async Task ReleaseAsync(State state, CancellationToken token)
    {
        if (state.Preview is null) return;
        try
        {
            if (state.FromLibrary) await HostServices.CaptureLibrary.ReleaseAsync(state.Preview, token);
            else await HostServices.Capture.DiscardAsync(state.Preview, token);
        }
        catch (WidgetCapabilityException) { } // An expired temporary view must not trap navigation.
    }
    public override WidgetView Render()
    {
        var state = model.Value;
        if (state.Settings) return Page(RenderSettings(state), "settings", state.Navigation);
        var items = new List<WidgetElement> { UI.Text("Capture Library", "capture-library.title").Classes("capture-title") };
        string initial;
        if (state.Preview is not null || state.Detail)
        {
            items.Add(UI.Text(state.SelectedId is null ? "Review capture" : "Saved capture", "capture-library.heading").Classes("capture-heading"));
            if (state.Preview is { } media)
            {
                items.Add(UI.Text(Describe(media), "capture-library.metadata").Classes("capture-help"));
                if (media.ContentType == "video/mp4" && media.Properties is { } actual)
                    items.Add(UI.Text($"{actual.RequestedFramesPerSecond} fps · {actual.Encoder} encoding · " +
                        (actual.Audio == CaptureAudioMode.Application ? "Application audio" : "No audio") +
                        (actual.MissedFrames > 0 ? $" · {actual.MissedFrames} missed frames" : ""), "capture-library.recording-properties").Classes("capture-help"));
                items.Add(UI.MediaPlayer(MediaPlayerSource.FromCapture(media), "capture-library.preview", "Capture preview",
                    new(Muted: media.Properties?.Audio is null or CaptureAudioMode.None)).Classes("capture-preview"));
            }
            else items.Add(UI.Text(state.Busy ? "Opening capture…" : "Preview unavailable. You can return to the library or delete this entry.", "capture-library.unavailable").Classes("capture-help"));
            var actions = new List<WidgetElement>();
            if (state.SelectedId is null) actions.Add(UI.Button("Save to library", "capture-library.save", "capture-library.save").Busy(state.Busy));
            else
            {
                actions.Add(UI.Button("Open folder", "capture-library.folder", "capture-library.folder").Busy(state.Busy));
                actions.Add(UI.Button("Delete", "capture-library.delete", "capture-library.delete").Busy(state.Busy));
            }
            actions.Add(UI.Button(state.SelectedId is null ? "Discard" : "Back to library", "capture-library.back", "capture-library.back").Busy(state.Busy));
            items.Add(UI.ResponsiveGrid("capture-library.actions", 150, 3, actions.ToArray()));
            initial = state.SelectedId is null ? "capture-library.save" : "capture-library.back";
        }
        else
        {
            items.Add(UI.ResponsiveGrid("capture-library.capture-actions", 150, 4,
                UI.Button("Take screenshot", "capture-library.photo", "capture-library.photo").Busy(state.Busy),
                UI.Button("Record clip", "capture-library.video", "capture-library.video").Busy(state.Busy),
                UI.Button("Capture settings", "capture-library.settings", "capture-library.settings").Busy(state.Busy),
                UI.Button("Open folder", "capture-library.folder", "capture-library.folder").Busy(state.Busy)));
            if (state.Page is { } page)
            {
                var entries = new List<WidgetElement>();
                foreach (var item in page.Items)
                {
                    var id = "capture-library.item." + item.Id;
                    WidgetElement artwork = item.Available
                        ? UI.Artwork(new WidgetArtworkHandle(ArtworkKey(item)), id + ".thumbnail", item.Media.ContentType == "image/png" ? "Screenshot thumbnail" : "Video thumbnail", ImageFit.Cover).Classes("capture-thumbnail")
                        : UI.Text(item.Available ? "Video clip" : "Unavailable", id + ".placeholder").Classes("capture-thumbnail-placeholder");
                    entries.Add(UI.ActionSurface("capture-library.open." + item.Id, id, Describe(item.Media), ActionSurfaceOrientation.Vertical,
                        artwork,
                        UI.Stack(id + ".copy", UI.Text(item.Available ? item.Media.SourceApplication?.ApplicationName ?? "Application capture" : "Unavailable capture", id + ".name").Classes("capture-heading"),
                            UI.Text(item.Available ? (item.Media.ContentType == "image/png" ? "Screenshot" : $"Clip · {item.Media.DurationSeconds:0.#} s") + $" · {item.Media.Width} × {item.Media.Height}" : "This entry can be opened for deletion.", id + ".info").Classes("capture-help"),
                            UI.Text(DateTimeOffset.FromUnixTimeMilliseconds(item.SavedAtUnixMilliseconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture), id + ".date").Classes("capture-help")).Classes("capture-entry-copy")).Busy(state.Busy).Classes("capture-entry"));
                }
                if (page.Items.Count > 0) items.Add(UI.ResponsiveGrid("capture-library.entries", 220, 3, entries.ToArray()).RememberChildFocus("capture-library.item." + page.Items[0].Id));
                items.Add(UI.Text($"Page {page.Offset / CaptureLibraryLimits.PageSize + 1} of {Math.Max(1, (page.TotalCount + CaptureLibraryLimits.PageSize - 1) / CaptureLibraryLimits.PageSize)}", "capture-library.page-number").Classes("capture-help"));
                if (page.TotalCount == 0) items.Add(UI.Text("Your saved screenshots and clips will appear here.", "capture-library.empty").Classes("capture-help"));
                items.Add(UI.Row("capture-library.pages", UI.Button("Previous", "capture-library.previous", "capture-library.previous").Disabled(page.Offset == 0).Busy(state.Busy),
                    UI.Button("Next", "capture-library.next", "capture-library.next").Disabled(page.Offset + page.Items.Count >= page.TotalCount).Busy(state.Busy),
                    UI.Button("Refresh", "capture-library.refresh", "capture-library.refresh").Busy(state.Busy)));
            }
            initial = state.Page?.Items.Any(item => item.Id == state.SelectedId) == true ? "capture-library.item." + state.SelectedId : "capture-library.photo";
        }
        if (state.Capturing)
        {
            items.Add(UI.Text("Return to your application. Reopen WidgetRail after the capture to review it.", "capture-library.wait").Classes("capture-help"));
            items.Add(UI.Button("Cancel capture", "capture-library.cancel", "capture-library.cancel"));
        }
        if (state.Message is { } message)
        {
            items.Add(UI.Text(message, "capture-library.status").Classes("capture-help"));
            if (message == Explain("permission_denied"))
            {
                items.Add(UI.Button("Allow capture and library access", "capture-library.allow", "capture-library.allow").Busy(state.Busy));
                items.Add(PermissionsButton());
            }
        }
        ContainerElement root = state.Preview is not null || state.Detail
            ? UI.Stack("capture-library.page", items.ToArray())
            : UI.VerticalScroll("capture-library.page", items.ToArray());
        root = root.InputScope("capture-library.page").Classes("capture-library");
        if (state.Preview is not null || state.Detail) root = root.Shortcut(ControllerButton.B, "capture-library.back", label: "Library");
        else if (!state.Busy) root = root.Shortcut(ControllerButton.Y, "capture-library.photo", label: "Screenshot").Shortcut(ControllerButton.X, "capture-library.video", label: "Record clip");
        if (state.Preview is null && !state.Detail && !state.Busy && state.Page is { } pages)
        {
            if (pages.Offset > 0) root = root.Shortcut(ControllerButton.LeftBumper, "capture-library.previous", label: "Previous page");
            if (pages.Offset + pages.Items.Count < pages.TotalCount) root = root.Shortcut(ControllerButton.RightBumper, "capture-library.next", label: "Next page");
        }
        var view = new WidgetView(root.TransitionPage("capture-library.pages", state.Preview is not null || state.Detail ? "preview" : "home", state.Navigation), initial)
        {
            FocusGroupEntryRequest = state.Preview is null && !state.Detail && state.Page?.Items.Count > 0 && state.FocusRequest > 0
                ? new() { RequestId = state.FocusRequest, GroupId = "capture-library.entries" } : null,
        };
        if (state.ConfirmDelete)
            view = view.WithModal(new WidgetModal("capture-library.confirm", "Delete saved capture?",
                UI.Stack("capture-library.confirm.content", UI.Text("This permanently deletes the saved file from Pictures → WidgetRail → Captures.", "capture-library.confirm.help"),
                    UI.Row("capture-library.confirm.actions", UI.Button("Cancel", "capture-library.delete.cancel", "capture-library.delete.cancel").Busy(state.Busy),
                        UI.Button("Delete", "capture-library.delete.confirm", "capture-library.delete.confirm").Busy(state.Busy))), "capture-library.delete.cancel", "capture-library.delete.cancel"));
        return view;
    }
    private static string Describe(CaptureAttachment media) => (media.SourceApplication?.ApplicationName is { } name ? name + " · " : "") +
        (media.ContentType == "image/png" ? "Screenshot" : $"{media.DurationSeconds:0.#}-second clip") + $" · {media.Width} × {media.Height} · {media.ByteLength / 1048576d:0.0} MiB";
    private static string Explain(string code) => code switch
    {
        "capture_preferences_invalid" => "Saved capture settings could not be read. Open Capture settings and reset them to supported defaults.",
        "capture_preferences_conflict" or "private_state_revision_conflict" => "Capture settings changed elsewhere. Reopen Capture settings and try again.",
        "permission_cancelled" => "Permission request cancelled. Nothing was changed.",
        "permission_unavailable" => "Permission approval is unavailable right now. Return to this widget and try again, or manage access in Settings.",
        "permission_denied" or "capability_not_declared" => "Allow Capture Library's required permissions in Settings → Widgets. Deleting captures and opening the folder also need its optional manage permission.",
        "capture_library_linked_path" => "This storage location uses a link that WidgetRail cannot follow. Your existing data was not changed.",
        "capture_library_disk_full" => "There is not enough free disk space to save this capture. Free some space and try again.",
        "capture_too_large" or "capture_output_budget_too_small" => "The capture exceeds its size limit. Use a lower resolution or shorter recording and try again.",
        "capture_encoding_unsupported" or "capture_duration_unsupported" or "capture_image_format_unsupported" or "capture_audio_unsupported" => "Those capture settings are unavailable on this PC. Choose a supported option.",
        "capture_limit" => "Too many temporary captures are open. Return to the library and try again.",
        "cancelled" => "Capture cancelled. Nothing was saved.",
        "capture_library_invalid_media" or "capture_library_not_found" => "This saved capture is missing or damaged. Its original was not changed.",
        _ => "The operation could not finish. Check permissions and free disk space, then retry. Keep your application visible during capture.",
    };
    private async Task RequirePermissionsAsync(IReadOnlyList<string> ids, CancellationToken token)
    {
        var outcome = await HostServices.Permissions.RequestAsync(ids, token);
        if (outcome != WidgetPermissionOutcome.Granted) throw new WidgetCapabilityException(outcome switch {
            WidgetPermissionOutcome.Cancelled => "permission_cancelled", WidgetPermissionOutcome.Denied => "permission_denied", _ => "permission_unavailable" }, "Permission was not granted.");
    }
}
