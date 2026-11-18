using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.CaptureLibrary;

public sealed partial class CaptureLibraryWidget
{
    private bool showSources;
    private readonly WidgetResource<IReadOnlyList<CaptureLibrarySource>> captureSources;
    private IReadOnlyList<CaptureLibrarySource>? linkedSources => captureSources.Snapshot.Value;
    private CaptureLibrarySource? pendingSourceRemoval;

    private WidgetResource<IReadOnlyList<CaptureLibrarySource>> CreateSourcesResource() => CreateResource<IReadOnlyList<CaptureLibrarySource>>(
        "capture-library.sources.data",new()
        {
            Load = token => HostServices.CaptureLibrary.GetSourcesAsync(token), Lifetime = WidgetOperationLifetime.Widget,
            MapError = error => error is WidgetCapabilityException capability ? new(capability.ErrorCode,Explain(capability.ErrorCode)) :
                new("capture_sources_unavailable","Capture folders could not be read. Refresh to try again."),
        });

    private bool HandleSourceAction(WidgetActionEvent action)
    {
        if (action.ActionId == "capture-library.sources.close" && showSources)
        {
            showSources = false; pendingSourceRemoval = null;
            captureSources.Cancel();
            Operations.Cancel("capture-library.work");
            model.Update(state => state with { Busy = false }); Invalidate(); return true;
        }
        if (model.Value.Busy) return false;
        if (action.ActionId == "capture-library.sources.open")
        {
            showSources = true; pendingSourceRemoval = null;
            Work(LoadSourcesAsync); return true;
        }
        if (!showSources && action.ActionId != "capture-library.sources.windows") return false;
        if (action.ActionId == "capture-library.sources.refresh") { Work(LoadSourcesAsync); return true; }
        if (action.ActionId is "capture-library.sources.add" or "capture-library.sources.windows")
        {
            Work(async token =>
            {
                await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ReadPermission,WidgetCaptureLibraryCapabilities.ManagePermission],token);
                var source = action.ActionId == "capture-library.sources.windows"
                    ? await HostServices.CaptureLibrary.ChooseKnownSourceAsync(CaptureLibraryKnownLocation.WindowsGameCaptures, "Windows captures", token)
                    : await HostServices.CaptureLibrary.ChooseSourceAsync("Capture folder",token);
                token.ThrowIfCancellationRequested();
                await LoadSourcesAsync(token);
                if (source is not null) UpdateFeedback(state => state with { Message = "Folder connected. Supported captures will appear as it is scanned." });
                QueueGalleryRefresh();
            }); return true;
        }
        if (action.ActionId == "capture-library.sources.cancel-remove")
        { pendingSourceRemoval = null; Invalidate(); return true; }
        if (action.ActionId == "capture-library.sources.confirm-remove" && pendingSourceRemoval is { } selected)
        {
            Work(async token =>
            {
                await RequirePermissionsAsync([WidgetCaptureLibraryCapabilities.ManagePermission],token);
                await HostServices.CaptureLibrary.RemoveSourceAsync(selected.Id,token);
                token.ThrowIfCancellationRequested(); pendingSourceRemoval = null;
                await LoadSourcesAsync(token); await RefreshGalleryAsync(token);
                UpdateFeedback(state => state with { Message = "Folder disconnected. Its original files were kept." });
            }); return true;
        }
        if (linkedSources?.FirstOrDefault(source => action.ActionId == "capture-library.sources.remove." + source.Id) is { } removal)
        { pendingSourceRemoval = removal; Invalidate(); return true; }
        return false;
    }

    private async Task LoadSourcesAsync(CancellationToken token)
    {
        var result = await captureSources.Refresh().Completion.WaitAsync(token);
        if (result.Exception is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        token.ThrowIfCancellationRequested();
    }

    private WidgetView WithSources(WidgetView view, State state) => !showSources ? view : view.WithModal(SourceModal(state));

    private WidgetModal SourceModal(State state)
    {
        if (pendingSourceRemoval is { } removal)
            return new("capture-library.sources.confirm", "Disconnect capture folder?",
                UI.Stack("capture-library.sources.confirm.content",
                    UI.Text(removal.CanonicalFolderPath,"capture-library.sources.confirm.path").Classes("capture-help"),
                    UI.Text("Its linked entries will be removed from this library. Original files stay in the folder.","capture-library.sources.confirm.help"),
                    UI.Row("capture-library.sources.confirm.actions",
                        UI.Button("Cancel","capture-library.sources.cancel-remove","capture-library.sources.cancel-remove").Busy(state.Busy),
                        UI.Button("Disconnect","capture-library.sources.confirm-remove","capture-library.sources.confirm-remove").Busy(state.Busy))),
                "capture-library.sources.cancel-remove","capture-library.sources.cancel-remove");
        var items = new List<WidgetElement>
        {
            UI.Text("Link capture folders to browse their files here without moving or copying the originals.","capture-library.sources.help").Classes("capture-help"),
            UI.Row("capture-library.sources.actions",
                UI.Button("Add folder…","capture-library.sources.add","capture-library.sources.add").Disabled(linkedSources?.Count >= CaptureLibrarySourceLimits.MaximumSources).Busy(state.Busy),
                UI.Button("Link Windows captures…","capture-library.sources.windows","capture-library.sources.windows").Disabled(linkedSources?.Count >= CaptureLibrarySourceLimits.MaximumSources).Busy(state.Busy),
                UI.Button("Refresh","capture-library.sources.refresh","capture-library.sources.refresh").Busy(state.Busy)),
        };
        if (linkedSources is null) items.Add(UI.Text(state.Busy ? "Loading folders…" : "Folders could not be loaded.","capture-library.sources.loading"));
        else if (linkedSources.Count == 0) items.Add(UI.Text("No folders linked here yet. Integrations manage their own linked folders.","capture-library.sources.empty").Classes("capture-help"));
        else foreach (var source in linkedSources)
        {
            var id = "capture-library.sources." + source.Id;
            items.Add(UI.Stack(id,
                UI.Text(source.CanonicalFolderPath,id+".path").Classes("capture-heading"),
                UI.Text(source.Available ? $"{source.LinkedCount:N0} captures" + (source.UnsupportedCount > 0 ? $" · {source.UnsupportedCount:N0} unsupported files" : "") : "Folder unavailable",id+".status").Classes("capture-help"),
                UI.Button("Disconnect","capture-library.sources.remove."+source.Id,id+".remove").Busy(state.Busy)).Classes("capture-settings-section"));
        }
        if (state.PermissionRequired && state.Message is { } message) items.Add(UI.Text(message,"capture-library.sources.status").Classes("capture-help"));
        else if (captureSources.Snapshot.Error is { } error) items.Add(UI.Text(error.Message,"capture-library.sources.status").Classes("capture-help"));
        if (captureSources.Snapshot.Error?.Code is "permission_denied" or "capability_not_declared") items.Add(PermissionsButton());
        return new("capture-library-sources","Capture folders",UI.Stack("capture-library.sources.content",items.ToArray()),
            "capture-library-sources.close","capture-library.sources.close");
    }
}
