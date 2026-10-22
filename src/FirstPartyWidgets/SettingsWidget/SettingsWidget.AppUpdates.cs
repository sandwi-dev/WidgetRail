using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsAppUpdateState(AppUpdateSnapshot Snapshot, string? Error = null, bool NotesExpanded = false)
{
    internal static SettingsAppUpdateState Empty { get; } = new(AppUpdateSnapshot.Unavailable);
}

public sealed partial class SettingsWidget
{
    private SettingsAppUpdateState _appUpdateState = SettingsAppUpdateState.Empty;
    private async Task<bool> HandleAppUpdateActionAsync(WidgetActionEvent action, CancellationToken token)
    {
        if (action.ActionId == "open.app-updates")
        {
            Navigate(SettingsPage.AppUpdates);
            await ReadAppUpdatesAsync(new("status"), token).ConfigureAwait(false);
            return true;
        }
        if (!action.ActionId.StartsWith("app-update.", StringComparison.Ordinal)) return false;
        if (CurrentPage != SettingsPage.AppUpdates) return true;
        SettingsAppUpdateState state;
        lock (_stateLock) state = _appUpdateState;
        switch (action.ActionId)
        {
            case "app-update.auto": await ReadAppUpdatesAsync(new(state.Snapshot.AutomaticallyCheck ? "auto-disable" : "auto-enable"), token).ConfigureAwait(false); break;
            case "app-update.check": await ReadAppUpdatesAsync(new("check"), token).ConfigureAwait(false); break;
            case "app-update.download": await ReadAppUpdatesAsync(new("download"), token).ConfigureAwait(false); break;
            case "app-update.cancel": await ReadAppUpdatesAsync(new("cancel"), token).ConfigureAwait(false); break;
            case "app-update.notes": lock (_stateLock) _appUpdateState = state with { NotesExpanded = !state.NotesExpanded }; break;
            case "app-update.install":
                if (state.Snapshot is { State: "ready", ReviewToken: { } review })
                    await ReadAppUpdatesAsync(new("install", review), token).ConfigureAwait(false);
                break;
        }
        PublishSettingsChanged(); return true;
    }
    private async Task ReadAppUpdatesAsync(AppUpdateCommand command, CancellationToken token)
    {
        try
        {
            var snapshot = await _diagnosticsService.AppUpdatesAsync(command, token).ConfigureAwait(false);
            bool changed;
            lock (_stateLock)
            {
                changed = snapshot.Revision != _appUpdateState.Snapshot.Revision || _appUpdateState.Error is not null || command.Action != "status";
                _appUpdateState = _appUpdateState with { Snapshot = snapshot, Error = null };
            }
            if (changed) PublishSettingsChanged();
        }
        catch (PlatformDiagnosticsException)
        {
            lock (_stateLock) _appUpdateState = _appUpdateState with { Error = "The update request could not complete. Check status before retrying." };
            PublishSettingsChanged();
        }
    }
}

internal static partial class SettingsPresentation
{
    private static IEnumerable<WidgetElement> ReleaseNoteElements(string notes)
    {
        // Release metadata permits more text than a single presentation node.
        // Keep every character, preferring line boundaries and never splitting
        // a UTF-16 surrogate pair or a Windows newline between adjacent nodes.
        var part = 0;
        foreach (var chunk in WidgetTextChunks.Split(notes, ProtocolConstants.MaximumStringLength))
        {
            var id = part == 0 ? "app-update.notes.text" : $"app-update.notes.text.{part}";
            yield return UI.Text(chunk, id).Classes("app-update-notes");
            part++;
        }
    }

    private static WidgetView RenderAppUpdates(StackElement header, SettingsPresentationState presentation)
    {
        var state = presentation.AppUpdates ?? SettingsAppUpdateState.Empty;
        var snapshot = state.Snapshot;
        var busy = snapshot.State is "checking" or "downloading";
        var handingOff = snapshot.State == "handoff";
        var installerWarning = UI.Text("Interactive setup needs mouse and keyboard. Windows may request approval for required Microsoft components. WidgetRail closes only when setup is ready.",
            "app-update.input-warning").Classes("app-update-input-warning");
        var items = new List<WidgetElement>
        {
            UI.Text("App updates", "app-update.heading").Classes("page-heading"),
            UI.Text("Current version " + snapshot.CurrentVersion, "app-update.current").Classes("page-help"),
            UI.Text("Download an update, then choose Install and restart. WidgetRail closes automatically and reopens when setup finishes.", "app-update.help").Classes("page-help"),
            UI.Switch("Automatically check for updates", snapshot.AutomaticallyCheck, "app-update.auto", "app-update.auto")
                .Disabled(snapshot.State == "unavailable" || handingOff),
            UI.Text("Check shortly after startup and notify when an update is available. Downloads and installation remain manual.", "app-update.auto.help").Classes("page-help"),
            UI.Button("Check for updates", "app-update.check", "app-update.check").Busy(busy).Disabled(handingOff).Classes("secondary-button"),
        };
        if (snapshot.AvailableVersion is { } available)
        {
            items.Add(UI.Text("Available " + available, "app-update.available").Classes("section-heading"));
            if (snapshot.Notes.Length != 0)
            {
                items.Add(UI.Button(state.NotesExpanded ? "Hide release notes" : "Show release notes", "app-update.notes", "app-update.notes.toggle").Classes("secondary-button"));
                if (state.NotesExpanded) items.AddRange(ReleaseNoteElements(snapshot.Notes));
            }
            if (snapshot.RequiresInteractiveSetup) items.Add(installerWarning);
            if (snapshot.CanInstall)
                items.Add(UI.Button(snapshot.State == "ready" ? snapshot.RequiresInteractiveSetup ? "Open interactive setup" : "Install and restart" : handingOff ? "Preparing setup…" : "Download update", snapshot.State == "ready" ? "app-update.install" : "app-update.download", "app-update.primary")
                    .Busy(busy || handingOff).Disabled(handingOff).Classes("primary-button"));
            else items.Add(UI.Text("To install updates, open your installed WidgetRail from Start. Development candidates cannot replace it.", "app-update.candidate").Classes("page-help"));
        }
        items.Add(UI.Text(state.Error ?? snapshot.Message, "app-update.status").Classes(state.Error is not null || snapshot.State == "failed" ? "diagnostic-error" : "page-help"));
        if (snapshot.State == "downloading")
        {
            items.Add(UI.Text($"Current installer: {snapshot.DownloadedBytes / 1048576d:0.0} MiB downloaded", "app-update.bytes").Classes("page-help"));
            if (snapshot.TotalBytes is > 0) items.Add(UI.Progress(Math.Min(snapshot.DownloadedBytes, snapshot.TotalBytes.Value), snapshot.TotalBytes.Value, "app-update.progress", "Installer download"));
        }
        if (busy) items.Add(UI.Button("Cancel", "app-update.cancel", "app-update.cancel").Classes("secondary-button"));
        items.Add(UI.Button("Back", "back", "app-update.back").Classes("secondary-button"));
        return View(header, PageScope("app-update.page", items.ToArray()), "app-update.auto", "app-update.page");
    }
}
