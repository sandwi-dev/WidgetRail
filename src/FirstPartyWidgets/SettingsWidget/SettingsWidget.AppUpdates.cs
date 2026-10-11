using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsAppUpdateState(AppUpdateSnapshot Snapshot, bool Confirm = false, string? Error = null, bool NotesExpanded = false)
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
            case "app-update.check": await ReadAppUpdatesAsync(new("check"), token).ConfigureAwait(false); break;
            case "app-update.download": await ReadAppUpdatesAsync(new("download"), token).ConfigureAwait(false); break;
            case "app-update.cancel": await ReadAppUpdatesAsync(new("cancel"), token).ConfigureAwait(false); break;
            case "app-update.review":
                if (state.Snapshot.State == "ready") lock (_stateLock) _appUpdateState = state with { Confirm = true };
                break;
            case "app-update.notes": lock (_stateLock) _appUpdateState = state with { NotesExpanded = !state.NotesExpanded }; break;
            case "app-update.dismiss": lock (_stateLock) _appUpdateState = state with { Confirm = false }; break;
            case "app-update.install":
                if (state.Confirm && state.Snapshot is { State: "ready", ReviewToken: { } review })
                    await ReadAppUpdatesAsync(new("install", review), token).ConfigureAwait(false);
                break;
        }
        Invalidate(); return true;
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
                _appUpdateState = _appUpdateState with { Snapshot = snapshot, Error = null,
                    Confirm = _appUpdateState.Confirm && snapshot.State == "ready" };
            }
            if (changed) Invalidate();
        }
        catch (PlatformDiagnosticsException)
        {
            lock (_stateLock) _appUpdateState = _appUpdateState with { Confirm = false, Error = "The update request could not complete. Check status before retrying." };
            Invalidate();
        }
    }
}

internal static partial class SettingsPresentation
{
    private static WidgetView RenderAppUpdates(StackElement header, SettingsPresentationState presentation)
    {
        var state = presentation.AppUpdates ?? SettingsAppUpdateState.Empty;
        var snapshot = state.Snapshot;
        var busy = snapshot.State is "checking" or "downloading";
        var items = new List<WidgetElement>
        {
            UI.Text("App updates & recovery", "app-update.heading").Classes("page-heading"),
            UI.Text("Current version " + snapshot.CurrentVersion, "app-update.current").Classes("page-help"),
            UI.Text("Updates only start when you choose. WidgetRail must be closed before setup can continue.", "app-update.help").Classes("page-help"),
            UI.Button("Check for updates", "app-update.check", "app-update.check").Busy(busy).Classes("setting-row"),
        };
        if (snapshot.AvailableVersion is { } available)
        {
            items.Add(UI.Text("Available " + available, "app-update.available").Classes("section-heading"));
            if (snapshot.Notes.Length != 0)
            {
                items.Add(UI.Button(state.NotesExpanded ? "Hide release notes" : "Show release notes", "app-update.notes", "app-update.notes.toggle").Classes("setting-row"));
                if (state.NotesExpanded) items.Add(UI.Text(snapshot.Notes, "app-update.notes.text").Classes("page-help"));
            }
            if (snapshot.CanInstall)
                items.Add(UI.Button(snapshot.State == "ready" ? "Review installation" : "Download update & recovery", snapshot.State == "ready" ? "app-update.review" : "app-update.download", "app-update.primary")
                    .Busy(busy || snapshot.State == "handoff").Classes("primary-button"));
            else items.Add(UI.Text("To install with recovery, open your installed WidgetRail from Start. Development candidates cannot replace it.", "app-update.candidate").Classes("page-help"));
        }
        items.Add(UI.Text(state.Error ?? snapshot.Message, "app-update.status").Classes(state.Error is not null || snapshot.State == "failed" ? "diagnostic-error" : "page-help"));
        if (snapshot.State == "downloading")
        {
            items.Add(UI.Text($"Current installer: {snapshot.DownloadedBytes / 1048576d:0.0} MiB downloaded", "app-update.bytes").Classes("page-help"));
            if (snapshot.TotalBytes is > 0) items.Add(UI.Progress(Math.Min(snapshot.DownloadedBytes, snapshot.TotalBytes.Value), snapshot.TotalBytes.Value, "app-update.progress", "Installer download"));
        }
        if (busy) items.Add(UI.Button("Cancel", "app-update.cancel", "app-update.cancel").Classes("secondary-button"));
        items.Add(UI.Text("Recovery", "app-update.recovery.heading").Classes("section-heading"));
        items.Add(UI.Text("A desktop recovery shortcut is created before installation. It restores the previous version and pre-update profile even if WidgetRail will not open. Your newer profile is preserved separately. Windows credentials and external widget data are not rolled back.", "app-update.recovery.help").Classes("page-help"));
        items.Add(UI.Button("Back", "back", "app-update.back").Classes("secondary-button"));
        var view = View(header, PageScope("app-update.page", items.ToArray()), "app-update.check", "app-update.page");
        if (state.Confirm && snapshot.State == "ready")
            view = view.WithModal(new WidgetModal("app-update.confirm", "Prepare to install",
                UI.Stack("app-update.confirm.content",
                    UI.Text("Both installers have been verified. Continue to the Windows confirmation, quit WidgetRail from Settings, then choose OK there. Your profile is backed up before the interactive installer opens.", "app-update.confirm.help"),
                    UI.Text("The backup may contain private settings and widget data. Keep the recovery folder private. Installation is stopped if a complete backup cannot be made.", "app-update.confirm.privacy"),
                    UI.Row("app-update.confirm.actions",
                        UI.Button("Cancel", "app-update.dismiss", "app-update.dismiss"),
                        UI.Button("Continue", "app-update.install", "app-update.install"))), "app-update.dismiss", "app-update.dismiss"));
        return view;
    }
}
