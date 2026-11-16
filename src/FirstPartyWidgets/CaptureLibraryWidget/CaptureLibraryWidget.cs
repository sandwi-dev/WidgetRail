using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget : Widget
{
    private sealed record State(CaptureLibraryPage? Page = null, CaptureAttachment? Preview = null, bool FromLibrary = false,
        string? SelectedId = null, bool Detail = false, bool Busy = false, bool Capturing = false, bool Copying = false, bool ConfirmDelete = false, string? Message = null,
        WidgetNavigationOperation Navigation = WidgetNavigationOperation.Forward, bool Settings = false, CapturePreferences? Preferences = null, CaptureCapabilities? Capabilities = null, long PreferencesRevision = 0);
    // Before declaring material, all library routes used the host's 1180 x 700
    // legacy extent. An appearance-only hint would otherwise select 880 x 520.
    private static WidgetSurfaceHints Surface(WidgetSurfaceAppearance appearance) => new()
    {
        Appearance = appearance, PreferredWidth = 1180, PreferredHeight = 700,
    };

    private readonly WidgetModel<State> model;
    public CaptureLibraryWidget()
    {
        model = CreateModel(new State());
        galleryNavigation = CreateNavigatorWithOptions("capture-library.navigation", GalleryRoute.Gallery, new WidgetNavigatorOptions<GalleryRoute>
        {
            SharedRootScopeId = "capture-library.page", RootRoutes = [GalleryRoute.Gallery],
            RouteScopeIds = new Dictionary<GalleryRoute, string> { [GalleryRoute.Preview] = "capture-library.preview-page" },
        }, maximumDepth: 1, maximumRoutes: 2);
        gallery = CreateGallery();
        captureSources = CreateSourcesResource();
    }
    protected override ValueTask OnActivatedAsync(CancellationToken token)
    {
        WatchLibrary();
        var state = model.Value;
        if (!state.Busy && state.Preview is { } preview && preview.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            if (state.SelectedId is { } id)
                Work(async lifetime => { var reopened = await HostServices.CaptureLibrary.OpenAsync(id, cancellationToken: lifetime); model.Update(s => s with { Preview = reopened, FromLibrary = true }); });
            else
                Work(async lifetime => { model.Update(s => s with { Preview = null, Detail = false }); await RefreshAsync(lifetime); model.Update(s => s with { Message = "The unsaved capture expired. Take another capture to save it." }); });
        }
        else if (!state.Busy) Load();
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        var state = model.Value;
        if (HandleSourceAction(action)) return ValueTask.CompletedTask;
        if (HandleContextAction(action)) return ValueTask.CompletedTask;
        if (HandleBatchAction(action)) return ValueTask.CompletedTask;
        if (action.ActionId == "capture-library.cancel")
        { Operations.Cancel("capture-library.work"); model.Update(s => s with { Busy = false, Capturing = false, Message = "Capture cancelled." }); return ValueTask.CompletedTask; }
        if (action.ActionId == "capture-library.settings.back")
        { model.Update(s => s with { Settings = false, Navigation = WidgetNavigationOperation.Back, Message = null }); return ValueTask.CompletedTask; }
        if (state.Busy) return ValueTask.CompletedTask;
        if (!state.Detail && state.Preview is null && !state.Settings && HandleGalleryAction(action)) return ValueTask.CompletedTask;
        if (action.ActionId.StartsWith("capture-library.option.", StringComparison.Ordinal))
        { ChangeSetting(action.ActionId); return ValueTask.CompletedTask; }
        switch (action.ActionId)
        {
            case "capture-library.allow": Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ReadPermission], token); await RefreshAsync(token); }); break;
            case "capture-library.settings":
                model.Update(s => s with { Settings = true, Navigation = WidgetNavigationOperation.Forward });
                Work(LoadSettingsAsync); break;
            case "capture-library.photo": Capture(WindowCaptureKind.Screenshot); break;
            case "capture-library.video": Capture(WindowCaptureKind.Video); break;
            case "capture-library.refresh": Work(async token => { await HostServices.CaptureLibrary.RefreshAsync(cancellationToken: token); await RefreshAsync(token); WatchLibrary(); }); break;
            case "capture-library.back":
                galleryNavigation.Back();
                Work(async token => { await ReleaseAsync(state, token); model.Update(s => s with { Preview = null, Detail = false, Navigation = WidgetNavigationOperation.Back, ConfirmDelete = false }); await RefreshAsync(token); }); break;
            case "capture-library.save" when state.Preview is { } preview:
                Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.SavePermission], token); var item = await HostServices.CaptureLibrary.SaveAsync(preview, token);
                    var metadata = await HostServices.CaptureLibrary.GetMetadataAsync(item.Id, token);
                    token.ThrowIfCancellationRequested();
                    selectedCapture = item with { Title=metadata.Title,IsFavorite=metadata.IsFavorite,MetadataRevision=metadata.MetadataRevision };
                    model.Update(s => s with { SelectedId = item.Id, Message = "Saved to Pictures → WidgetRail → Captures." }); }); break;
            case "capture-library.folder":
                Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token); await HostServices.CaptureLibrary.OpenFolderAsync(token); }); break;
            case "capture-library.copy" when selectedCapture is { } copy:
                Work(token=>ApplyBatchAsync(new("copy",[copy]),token)); break;
            case "capture-library.reveal" when state.SelectedId is { } reveal:
                Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token); await HostServices.CaptureLibrary.RevealAsync(reveal, token); }); break;
            case "capture-library.delete" when state.SelectedId is not null: model.Update(s => s with { ConfirmDelete = true }); break;
            case "capture-library.delete.cancel": model.Update(s => s with { ConfirmDelete = false }); break;
            case "capture-library.delete.confirm" when state.ConfirmDelete && state.SelectedId is { } deleted:
                DeleteSelectedCapture(state, deleted); break;
            case "capture-library.favorite" when selectedCapture is not null:
                ChangeCaptureMetadata(null, toggleFavorite: true); break;
            case "capture-library.title" when selectedCapture is not null:
                ChangeCaptureMetadata(action.CommittedText, toggleFavorite: false); break;
            case "capture-library.viewer.previous": NavigateCapture(-1); break;
            case "capture-library.viewer.next": NavigateCapture(1); break;
        }
        return ValueTask.CompletedTask;
    }
    private void Load() => Work(RefreshAsync);
    private async Task RefreshAsync(CancellationToken token)
    {
        await RefreshGalleryAsync(token);
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
            finally { if (context.IsCurrent) model.Update(s => s with { Busy = false, Copying = false }); }
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
                await RequirePermissionsAsync([WidgetCaptureCapabilities.Permission, WidgetCaptureLibraryCapabilities.SavePermission], context.CancellationToken);
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
    public override WidgetView Render() { lock (galleryGate) return RenderGalleryView(); }
    private WidgetView RenderGalleryView()
    {
        var state = model.Value;
        if (state.Settings) return WithSources(Page(RenderSettings(state), "settings", state.Navigation),state);
        var items = new List<WidgetElement>();
        string initial;
        if (state.Preview is not null || state.Detail)
        {
            items.Add(UI.Text("Capture Library", "capture-library.title").Classes("capture-title"));
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
            if (state.SelectedId is not null && selectedCapture is { } selected)
            {
                items.Add(UI.Row("capture-library.viewer.navigation",
                    UI.Button("Previous", "capture-library.viewer.previous", "capture-library.viewer.previous").Busy(state.Busy),
                    UI.Button("Next", "capture-library.viewer.next", "capture-library.viewer.next").Busy(state.Busy),
                    UI.Button(selected.IsFavorite ? "Remove favorite" : "Favorite", "capture-library.favorite", "capture-library.favorite").Busy(state.Busy)));
                items.Add(UI.TextEntry(selected.Title ?? "", "Add a title", "capture-library.title", "capture-library.edit-title", 160).Disabled(state.Busy));
                if (selected.ContextCount > 0)
                    items.Add(UI.Button("Related details", "capture-library.context.open", "capture-library.context.open").Busy(state.Busy));
            }
            var actions = new List<WidgetElement>();
            if (state.SelectedId is null) actions.Add(UI.Button("Save to library", "capture-library.save", "capture-library.save").Busy(state.Busy));
            else
            {
                actions.Add(UI.Button("Save a copy…", "capture-library.copy", "capture-library.copy").Busy(state.Busy));
                actions.Add(UI.Button("Show in folder", "capture-library.reveal", "capture-library.reveal").Busy(state.Busy));
                actions.Add(UI.Button(selectedCapture?.SourceId is not null ? "Remove from library" : selectedCapture?.Available == false ? "Remove unavailable entry" : "Move to Recycle Bin", "capture-library.delete", "capture-library.delete").Busy(state.Busy));
            }
            actions.Add(UI.Button(state.SelectedId is null ? "Discard" : "Back to library", "capture-library.back", "capture-library.back").Busy(state.Busy));
            items.Add(UI.ResponsiveGrid("capture-library.actions", 150, 3, actions.ToArray()));
            initial = state.SelectedId is null ? "capture-library.save" : "capture-library.back";
        }
        else
        {
            items.Add(UI.ResponsiveGrid("capture-library.header", 400, 2,
                UI.Text("Capture Library", "capture-library.title").Classes("capture-title"),
                UI.Row("capture-library.capture-actions",
                    UI.Button("Screenshot", "capture-library.photo", "capture-library.photo").Busy(state.Busy),
                    UI.Button("Record clip", "capture-library.video", "capture-library.video").Busy(state.Busy),
                    UI.Button("Settings", "capture-library.settings", "capture-library.settings").Busy(state.Busy),
                    UI.Button("Open folder", "capture-library.folder", "capture-library.folder").Busy(state.Busy)).Classes("capture-header-actions")));
            items.AddRange(GalleryControls(state));
            initial = "capture-library.photo";
        }
        if (state.Capturing)
        {
            items.Add(UI.Text("Return to your application. Reopen WidgetRail after the capture to review it.", "capture-library.wait").Classes("capture-help"));
            items.Add(UI.Button("Cancel capture", "capture-library.cancel", "capture-library.cancel"));
        }
        if (state.Copying)
        {
            items.Add(UI.Text("Saving copies. Completed copies are kept if you cancel.", "capture-library.copy.status").Classes("capture-help"));
            items.Add(UI.Button("Cancel copying", "capture-library.batch.stop", "capture-library.copy.cancel"));
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
        if (batchFailures.Count > 0)
            items.Add(UI.Button("Review failed captures", "capture-library.batch.failures", "capture-library.batch.failures"));
        ContainerElement root = UI.Stack("capture-library.page", items.ToArray());
        root = root.InputScope(galleryNavigation.Value.InputScopeId).Classes("capture-library");
        if (state.Preview is null && !state.Detail) root = root.AddClasses("capture-library-base");
        if (state.Preview is not null || state.Detail) root = root.Shortcut(ControllerButton.B, "capture-library.back", label: "Library");
        else if (gallery.Selection?.IsActive == true) root = root.Shortcut(ControllerButton.B, "capture-library.selection.toggle", label: "Done selecting");
        else if (!state.Busy) root = root.Shortcut(ControllerButton.Y, "capture-library.photo", label: "Screenshot").Shortcut(ControllerButton.X, "capture-library.video", label: "Record clip");
        var view = new WidgetView(root.TransitionPage("capture-library.pages", state.Preview is not null || state.Detail ? "preview" : "home", state.Navigation), initial)
        {
            Surface = Surface(state.Preview is not null || state.Detail
                ? WidgetSurfaceAppearance.Theme : WidgetSurfaceAppearance.DesktopAcrylic),
            FocusGroupEntryRequest = state.Preview is null && !state.Detail
                ? galleryReturn ?? galleryNavigation.Value.CollectionReturnFocus?.Resolve(GalleryId, gallery.Descriptor) : null,
        };
        if (showContexts) view = view.WithModal(ContextDetails());
        else if (showBatchFailures) view = view.WithModal(BatchFailures());
        else if (state.ConfirmDelete) view = view.WithModal(DeleteConfirmation(state));
        else if (pendingBatch is {Operation:"recycle"} batch) view = view.WithModal(BatchConfirmation(state,batch));
        return WithSources(view,state);
    }
    private WidgetModal DeleteConfirmation(State state)
    {
        var saved = selectedCapture;
        var media = state.Preview ?? (saved?.Available == true ? saved.Media : null);
        var kind = media is null ? "saved capture" : media.ContentType == "image/png" ? "screenshot" : "clip";
        var details = new List<WidgetElement>
        {
            UI.Text(media?.SourceApplication?.ApplicationName ?? (media is null ? "Saved capture" : media.ContentType == "image/png" ? "Screenshot" : "Video clip"),
                "capture-library.confirm.name").Classes("capture-heading"),
        };
        if (media is not null)
            details.Add(UI.Text(Describe(media with { SourceApplication = null }), "capture-library.confirm.metadata").Classes("capture-help"));
        if (saved is not null)
            details.Add(UI.Text((saved.FileModifiedAtUnixMilliseconds is null ? "Saved " : "File modified ") + DateTimeOffset.FromUnixTimeMilliseconds(saved.FileModifiedAtUnixMilliseconds ?? saved.SavedAtUnixMilliseconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                "capture-library.confirm.date").Classes("capture-help"));
        if (media is null)
            details.Add(UI.Text("The preview is unavailable.",
                "capture-library.confirm.unavailable").Classes("capture-help"));
        return new("capture-library.confirm", saved?.SourceId is not null || saved?.Available == false ? $"Remove {kind}?" : $"Recycle {kind}?",
            UI.Stack("capture-library.confirm.content",
                UI.VerticalScroll("capture-library.confirm.body",
                    UI.Stack("capture-library.confirm.summary", details.ToArray()).Classes("capture-confirm-summary"),
                    UI.Text(saved?.SourceId is not null ? "This removes the linked entry from this library. The original stays in its capture folder." : saved?.Available == false ? "This removes the unavailable library entry. Any original file that still exists is kept." : "The original goes to the Windows Recycle Bin. You can restore it there.", "capture-library.confirm.help").Classes("capture-confirm-warning"))
                    .Classes("capture-confirm-body"),
                UI.Row("capture-library.confirm.actions",
                    UI.Button("Cancel", "capture-library.delete.cancel", "capture-library.delete.cancel").Busy(state.Busy)
                        .FocusRight("capture-library.delete.confirm").Classes("capture-confirm-button"),
                    UI.Button(state.Busy ? "Removing…" : saved?.SourceId is not null || saved?.Available == false ? "Remove entry" : "Move to Recycle Bin", "capture-library.delete.confirm", "capture-library.delete.confirm").Busy(state.Busy)
                        .FocusLeft("capture-library.delete.cancel").Classes("capture-confirm-button", "capture-delete"))
                    .Classes("capture-confirm-actions")).Classes("capture-confirm-content"),
            "capture-library.delete.cancel", "capture-library.delete.cancel")
        {
            ScrollContent = false,
            HeaderActions = UI.Row("capture-library.confirm.header-actions"),
        };
    }
    private static string Describe(CaptureAttachment media) => (media.SourceApplication?.ApplicationName is { } name ? name + " · " : "") +
        (media.ContentType == "image/png" ? "Screenshot" : $"{media.DurationSeconds:0.#}-second clip") + $" · {media.Width} × {media.Height} · {media.ByteLength / 1048576d:0.0} MiB";
    private static string Explain(string code) => code switch
    {
        "capture_library_source_overlap" => "This folder overlaps an existing capture-folder connection. Manage it from the widget or integration that connected it.",
        "capture_library_source_limit" => "The maximum number of connected folders has been reached. Disconnect an unused folder first.",
        "capture_library_source_unavailable" => "The capture folder is unavailable. Check that it still exists and can be opened.",
        "capture_library_read_only_source" => "This capture belongs to another application. You can remove its library entry while keeping the original.",
        "capture_preferences_invalid" => "Saved capture settings could not be read. Open Capture settings and reset them to supported defaults.",
        "capture_preferences_conflict" or "private_state_revision_conflict" => "Capture settings changed elsewhere. Reopen Capture settings and try again.",
        "permission_cancelled" => "Permission request cancelled. Nothing was changed.",
        "permission_unavailable" => "Permission approval is unavailable right now. Return to this widget and try again, or manage access in Settings.",
        "permission_denied" or "capability_not_declared" => "Allow Capture Library's required permissions in Settings → Widgets. Deleting captures and opening the folder also need its optional manage permission.",
        "capture_library_query_changed" => "The library changed. Refresh and try again.",
        "capture_library_metadata_conflict" => "This capture changed elsewhere. Refresh before editing it again.",
        "capture_library_recycle_unavailable" => "Windows could not move this capture to the Recycle Bin. The file has not been permanently deleted.",
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
