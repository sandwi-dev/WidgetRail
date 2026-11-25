using System.Globalization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget : Widget
{
    private sealed record State(CaptureLibraryPage? Page = null, CaptureAttachment? Preview = null,
        string? SelectedId = null, bool Detail = false, bool Busy = false, bool Copying = false, bool ConfirmDelete = false, string? Message = null,
        WidgetNavigationOperation Navigation = WidgetNavigationOperation.Forward, bool Settings = false, CapturePreferences? Preferences = null, CaptureCapabilities? Capabilities = null, long PreferencesRevision = 0,
        CaptureReplayStatus? Replay = null, string? ReplayNotice = null, double? ReplaySavedSeconds = null, bool RecycleLinkedOriginal = false, bool PermissionRequired = false);
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
        feedback = CreateNotificationPublisher("capture-library.feedback", "capture-result", debounce: TimeSpan.Zero);
        InitializeRecordingChoices();
        galleryNavigation = CreateNavigatorWithOptions("capture-library.navigation", GalleryRoute.Gallery, new WidgetNavigatorOptions<GalleryRoute>
        {
            SharedRootScopeId = "capture-library.page", RootRoutes = [GalleryRoute.Gallery],
            RouteScopeIds = new Dictionary<GalleryRoute, string> { [GalleryRoute.Preview] = "capture-library.preview-page" },
        }, maximumDepth: 1, maximumRoutes: 2);
        gallery = CreateGallery();
        gallery.SelectionChanged+=GallerySelectionChanged;
        captureSources = CreateSourcesResource();
        approvedApplications = CreateQueryResource<long, ReplayApprovalList>("capture-library.approvals.list", new()
        {
            Load = (_, token) => HostServices.Capture.Replay.GetApprovedApplicationsAsync(token),
            MapError = error => error is WidgetCapabilityException capability ? new(capability.ErrorCode, Explain(capability.ErrorCode)) :
                new("replay_approvals_unavailable", "Approved applications could not be loaded."),
            RetainLastGoodValue = false,
        });
    }
    protected override ValueTask OnActivatedAsync(CancellationToken token)
    {
        WatchLibrary();
        WatchReplay();
        var state = model.Value;
        if (!state.Busy && state.SelectedId is { } id && state.Preview is { } preview &&
            preview.ExpiresAtUnixMilliseconds <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            Work(async lifetime =>
            {
                var reopened = await HostServices.CaptureLibrary.OpenAsync(id, cancellationToken: lifetime);
                model.Update(s => s with { Preview = reopened });
            });
        else if (!state.Busy) Load();
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        var state = model.Value;
        if (HandleApprovalAction(action.ActionId, state)) return ValueTask.CompletedTask;
        if (HandleRecordingChoiceAction(action)) return ValueTask.CompletedTask;
        if (HandleReplayAction(action)) return ValueTask.CompletedTask;
        if (HandleSourceAction(action)) return ValueTask.CompletedTask;
        if (HandleContextAction(action)) return ValueTask.CompletedTask;
        if (HandleBatchAction(action)) return ValueTask.CompletedTask;
        if (action.ActionId == "capture-library.cancel")
        {
            Operations.Cancel("capture-library.work");
            UpdateFeedback(s => s with { Busy = false, Message = "Operation cancelled." });
            return ValueTask.CompletedTask;
        }
        if (action.ActionId == "capture-library.settings.back")
        { model.Update(s => s with { Settings = false, Navigation = WidgetNavigationOperation.Back, Message = null }); return ValueTask.CompletedTask; }
        if (state.Busy) return ValueTask.CompletedTask;
        if (action.ActionId == "capture-library.focus-actions" && (state.Preview is not null || state.Detail) && !showSources && !showContexts && !state.ConfirmDelete)
        { detailEntry = CreateFocusRequest().EnterGroup("capture-library.actions"); Invalidate(); return ValueTask.CompletedTask; }
        if (!state.Detail && state.Preview is null && !state.Settings && HandleGalleryAction(action)) return ValueTask.CompletedTask;
        if (action.ActionId.StartsWith("capture-library.option.", StringComparison.Ordinal))
        { ChangeSetting(action.ActionId); return ValueTask.CompletedTask; }
        switch (action.ActionId)
        {
            case "capture-library.allow": Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ReadPermission], token); await RefreshAsync(token); WatchLibrary(); WatchReplay(); }); break;
            case "capture-library.settings":
                model.Update(s => s with { Settings = true, Navigation = WidgetNavigationOperation.Forward });
                Work(LoadSettingsAsync); break;
            case "capture-library.photo": Capture(WindowCaptureKind.Screenshot); break;
            case "capture-library.video": Capture(WindowCaptureKind.Video); break;
            case "capture-library.refresh": Work(async token => { await HostServices.CaptureLibrary.RefreshAsync(cancellationToken: token); await RefreshAsync(token); WatchLibrary(); WatchReplay(); }); break;
            case "capture-library.back":
                Work(async token =>
                {
                    await ReleaseAsync(state, token);
                    token.ThrowIfCancellationRequested();
                    lock (galleryGate)
                    {
                        galleryNavigation.Back();
                        model.Update(s => s with { Preview = null, Detail = false,
                            Navigation = WidgetNavigationOperation.Back, ConfirmDelete = false });
                    }
                    await RefreshGalleryAsync(token);
                });
                break;
            case "capture-library.folder":
                Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token); await HostServices.CaptureLibrary.OpenFolderAsync(token); }); break;
            case "capture-library.edit-copy": EditCaptureCopy(state); break;
            case "capture-library.copy" when selectedCapture is { } copy:
                Work(async token =>
                {
                    await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token);
                    model.Update(s => s with { Copying = true });
                    var saved = await HostServices.CaptureLibrary.SaveCopyAsync(new(copy.Id, copy.Revision, copy.MetadataRevision), token);
                    token.ThrowIfCancellationRequested();
                    await ReplacePreviewAsync(saved, state, token);
                    await RefreshGalleryAsync(token);
                    UpdateFeedback(s => s with { Message = "Copy saved to the capture folder." });
                }); break;
            case "capture-library.reveal" when state.SelectedId is { } reveal:
                Work(async token => { await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission], token); await HostServices.CaptureLibrary.RevealAsync(reveal, token); }); break;
            case "capture-library.delete" when state.SelectedId is not null: model.Update(s => s with { ConfirmDelete = true, RecycleLinkedOriginal = false }); break;
            case "capture-library.unhide": UnhideSelectedCapture(state); break;
            case "capture-library.recycle-original" when state.SelectedId is not null && selectedCapture is {SourceId:not null,Available:true}:
                model.Update(s => s with { ConfirmDelete = true, RecycleLinkedOriginal = true }); break;
            case "capture-library.delete.cancel": model.Update(s => s with { ConfirmDelete = false }); break;
            case "capture-library.delete.confirm" when state.ConfirmDelete && state.SelectedId is { } deleted:
                DeleteSelectedCapture(state, deleted, state.RecycleLinkedOriginal); break;
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
        // Preference-driven presentation updates must settle before the newest
        // query captures its identity, including a post-subscription refresh.
        await LoadPreferencesAsync(token);
        await RefreshGalleryAsync(token);
    }
    private void Work(Func<CancellationToken, Task> work)
    {
        model.Update(s => s with { Busy = true, Message = null });
        Operations.RunLatest("capture-library.work", async context =>
        {
            try { await work(context.CancellationToken); }
            catch (OperationCanceledException) { }
            catch (WidgetCapabilityException error) { if (context.IsCurrent) UpdateFeedback(s => s with { Message = Explain(error.ErrorCode), ConfirmDelete = false }, error.ErrorCode == "permission_denied"); }
            finally { if (context.IsCurrent) model.Update(s => s with { Busy = false, Copying = false }); }
        }, WidgetOperationLifetime.Widget);
    }
    private void Capture(WindowCaptureKind kind) => Work(async token =>
    {
        await LoadPreferencesAsync(token);
        await RequirePermissionsAsync([WidgetCaptureCapabilities.Permission, WidgetCaptureLibraryCapabilities.SavePermission], token);
        var settings = model.Value.Preferences ?? throw new WidgetCapabilityException("capture_preferences_invalid", "Invalid saved settings.");
        await HostServices.Capture.CaptureToLibraryAsync(settings.Request(kind), token);
    });

    private static WidgetView Page(WidgetView view, string key, WidgetNavigationOperation operation) => view with
    {
        Root = view.Root.TransitionPage("capture-library.pages", key, operation),
    };

    private async Task ReleaseAsync(State state, CancellationToken token)
    {
        if (state.Preview is null) return;
        try
        {
            await HostServices.CaptureLibrary.ReleaseAsync(state.Preview, token);
        }
        catch (WidgetCapabilityException) { } // An expired temporary view must not trap navigation.
    }
    public override WidgetView Render()
    {
        lock (galleryGate)
        {
            var state = model.Value;
            var view = RenderGalleryView() with { QuickActions = CaptureTrayActions(state) };
            return preferencesLoaded && state.Preferences is null
                ? view.WithUnavailableTrayActions(Explain("capture_preferences_invalid"), "screenshot", "record-clip")
                : view;
        }
    }
    private WidgetView RenderGalleryView()
    {
        var state = model.Value;
        if (state.Settings)
        {
            var settings = Page(RenderSettings(state), "settings", state.Navigation);
            return showApprovals ? settings.WithModal(ApprovalModal(state)) : WithSources(settings, state);
        }
        var items = new List<WidgetElement>();
        string initial;
        if (state.Preview is not null || state.Detail)
        {
            items.Add(DetailHeader(state));
            items.Add(DetailBody(state));
            initial = state.Preview is not null ? "capture-library.share" : "capture-library.back";
        }
        else
        {
            items.AddRange(GalleryControls(state));
            initial = "capture-library.photo";
        }
        if (state.Copying)
        {
            items.Add(UI.Text("Saving copies. Completed copies are kept if you cancel.", "capture-library.copy.status").Classes("capture-help"));
            items.Add(UI.Button("Cancel copying", "capture-library.batch.stop", "capture-library.copy.cancel"));
        }
        if (state.Message is { } message && state.PermissionRequired)
        {
            items.Insert(1, UI.Stack("capture-library.permission-feedback",
                UI.Text(message, "capture-library.status").Classes("capture-help"),
                UI.Button("Allow capture and library access", "capture-library.allow", "capture-library.allow").Busy(state.Busy),
                PermissionsButton()));
        }
        if (batchFailures.Count > 0)
            items.Add(UI.Button("Review batch results", "capture-library.batch.failures", "capture-library.batch.failures"));
        ContainerElement root = UI.Stack("capture-library.page", items.ToArray());
        root = root.InputScope(galleryNavigation.Value.InputScopeId).Classes("capture-library");
        if (state.Preview is null && !state.Detail) root = root.AddClasses("capture-library-base");
        if (state.Preview is not null || state.Detail)
        {
            root = root.AddClasses("capture-detail-page").Shortcut(ControllerButton.B, "capture-library.back", label: "Library");
            if(state.SelectedId is not null && !state.Busy)
                root=root.Shortcut(ControllerButton.LeftBumper,"capture-library.viewer.previous",label:"Previous capture")
                    .Shortcut(ControllerButton.RightBumper,"capture-library.viewer.next",label:"Next capture")
                    .Shortcut(ControllerButton.Y,"capture-library.favorite",label:selectedCapture?.IsFavorite == true ? "Remove favorite" : "Favorite");
            if (!state.Busy)
                root = root.Shortcut(ControllerButton.LeftStick,"capture-library.focus-actions",label:"Capture actions");
        }
        else if (gallery.Selection?.IsActive == true) root = root.Shortcut(ControllerButton.B, "capture-library.selection.toggle", label: "Done selecting");
        else if (!state.Busy)
        {
            root = root.Shortcut(ControllerButton.Y, "capture-library.photo", label: "Screenshot")
                .Shortcut(ControllerButton.X, "capture-library.video", label: "Record clip");
            root = ReplayShortcuts(root, state);
        }
        if (state.Preview is null && !state.Detail && !state.Busy && !showSources && !showContexts && !showBatchFailures && pendingBatch is null)
            root = root.Shortcut(ControllerButton.LeftStick, "capture-library.focus-controls", label: "Library controls");
        var view = new WidgetView(root.TransitionPage("capture-library.pages", state.Preview is not null || state.Detail ? "preview" : "home", state.Navigation), initial)
        {
            Surface = Surface(WidgetSurfaceAppearance.DesktopAcrylic),
            FocusGroupEntryRequest = state.Preview is null && !state.Detail
                ? GalleryReturnFocus() : detailEntry,
        };
        if (showContexts) view = view.WithModal(ContextDetails()) with { FocusGroupEntryRequest = destinationFocus };
        else if (showBatchFailures) view = view.WithModal(BatchFailures());
        else if (state.ConfirmDelete) view = view.WithModal(DeleteConfirmation(state));
        else if (pendingBatch is {Operation:"recycle" or "hide"} batch) view = view.WithModal(BatchConfirmation(state,batch));
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
        var removeEntry = !state.RecycleLinkedOriginal && (saved?.SourceId is not null || saved?.Available == false);
        var hideEntry = !state.RecycleLinkedOriginal && saved?.SourceId is not null;
        return new("capture-library.confirm", hideEntry ? $"Hide {kind}?" : removeEntry ? $"Remove {kind}?" : $"Recycle {kind}?",
            UI.Stack("capture-library.confirm.content",
                UI.VerticalScroll("capture-library.confirm.body",
                    UI.Stack("capture-library.confirm.summary", details.ToArray()).Classes("capture-confirm-summary"),
                    UI.Text(state.RecycleLinkedOriginal ? "The original file in its linked capture folder goes to the Windows Recycle Bin. Other apps using that file will no longer find it there. You can restore it from the Recycle Bin." : hideEntry ? "The original stays in its capture folder. You can unhide this capture using Type → Hidden." : saved?.Available == false ? "This removes the unavailable library entry. Any original file that still exists is kept." : "The original goes to the Windows Recycle Bin. You can restore it there.", "capture-library.confirm.help").Classes("capture-confirm-warning"))
                    .Classes("capture-confirm-body"),
                UI.Row("capture-library.confirm.actions",
                    UI.Button("Cancel", "capture-library.delete.cancel", "capture-library.delete.cancel").Busy(state.Busy)
                        .FocusRight("capture-library.delete.confirm").Classes("capture-confirm-button"),
                    UI.Button(state.Busy ? "Working…" : hideEntry ? "Hide" : removeEntry ? "Remove entry" : "Move to Recycle Bin", "capture-library.delete.confirm", "capture-library.delete.confirm").Busy(state.Busy)
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
        "capture_library_not_hidden" => "This capture is already visible. Refresh the library.",
        "capture_library_user_state_unavailable" => "Hidden-capture settings could not be read. Check access to WidgetRail’s data folder, or restore its metadata backup, then restart WidgetRail. Your originals are unchanged.",
        "capture_library_recycle_unavailable" => "Windows could not move this capture to the Recycle Bin. The file has not been permanently deleted.",
        "capture_library_linked_path" => "This storage location uses a link that WidgetRail cannot follow. Your existing data was not changed.",
        "capture_library_disk_full" => "There is not enough free disk space to save this capture. Free some space and try again.",
        "capture_output_budget_too_small" => "These recording settings exceed the storage budget. Shorten the duration or reduce the bitrate in Capture settings.",
        "capture_too_large" => "The recording reached its file-size limit. Shorten the duration or reduce the bitrate in Capture settings.",
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
