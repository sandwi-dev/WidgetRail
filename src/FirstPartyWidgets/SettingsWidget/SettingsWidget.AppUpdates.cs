using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal sealed record SettingsAppUpdateState(AppUpdateSnapshot Snapshot, bool Confirm = false, string? Error = null, bool NotesExpanded = false, string? RecoveryReview = null)
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
            case "app-update.recovery.review":
                lock (_stateLock) _appUpdateState = state with { RecoveryReview = state.Snapshot.Recovery?.Id };
                break;
            case "app-update.recovery.dismiss": lock (_stateLock) _appUpdateState = state with { RecoveryReview = null }; break;
            case "app-update.recovery.restore":
                if (state.RecoveryReview is { } recovery && recovery == state.Snapshot.Recovery?.Id)
                {
                    lock (_stateLock) _appUpdateState = state with { RecoveryReview = null };
                    await ReadAppUpdatesAsync(new("recover", recovery), token).ConfigureAwait(false);
                }
                break;
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
                _appUpdateState = _appUpdateState with { Snapshot = snapshot, Error = null,
                    Confirm = _appUpdateState.Confirm && snapshot.State == "ready" };
            }
            if (changed) PublishSettingsChanged();
        }
        catch (PlatformDiagnosticsException)
        {
            lock (_stateLock) _appUpdateState = _appUpdateState with { Confirm = false, Error = "The update request could not complete. Check status before retrying." };
            PublishSettingsChanged();
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
            UI.Switch("Automatically check for updates", snapshot.AutomaticallyCheck, "app-update.auto", "app-update.auto")
                .Disabled(snapshot.State == "unavailable"),
            UI.Text("Check shortly after startup and notify when an update is available. Downloads and installation remain manual.", "app-update.auto.help").Classes("page-help"),
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
        items.Add(UI.Text("Restore the previous version and its pre-update profile from here. Your newer profile is preserved separately. Windows credentials and external widget data are not rolled back.", "app-update.recovery.help").Classes("page-help"));
        if (snapshot.Recovery is { } previous)
            items.Add(UI.Button("Restore version " + previous.PreviousVersion, "app-update.recovery.review", "app-update.recovery.review").Disabled(busy || snapshot.State == "handoff").Classes("secondary-button"));
        else items.Add(UI.Text("No recovery is available for this installation.", "app-update.recovery.empty").Classes("page-help"));
        items.Add(UI.Button("Back", "back", "app-update.back").Classes("secondary-button"));
        var view = View(header, PageScope("app-update.page", items.ToArray()), "app-update.auto", "app-update.page");
        if (state.Confirm && snapshot.State == "ready")
            view = view.WithModal(new WidgetModal("app-update.confirm", "Prepare to install",
                UI.Stack("app-update.confirm.content",
                    UI.Text("Both installers have been verified. Continue, then quit WidgetRail from Settings. A Windows confirmation opens after the app exits; your profile is backed up before the interactive installer starts. After updating, recovery is available on this page.", "app-update.confirm.help"),
                    UI.Text("The backup may contain private settings and widget data. Keep the recovery folder private. Installation is stopped if a complete backup cannot be made.", "app-update.confirm.privacy"),
                    UI.Row("app-update.confirm.actions",
                        UI.Button("Cancel", "app-update.dismiss", "app-update.dismiss"),
                        UI.Button("Continue", "app-update.install", "app-update.install"))), "app-update.dismiss", "app-update.dismiss"));
        if (state.RecoveryReview is { } reviewed && snapshot.Recovery is { } recovery && recovery.Id == reviewed)
            view = view.WithModal(new WidgetModal("app-update.recovery.confirm", "Restore previous version?",
                UI.Stack("app-update.recovery.content",
                    UI.Text("Restore " + recovery.PreviousVersion + " and its pre-update profile. Your current profile will be kept separately. Continue, then quit WidgetRail to complete recovery.", "app-update.recovery.description"),
                    UI.Row("app-update.recovery.actions",
                        UI.Button("Cancel", "app-update.recovery.dismiss", "app-update.recovery.dismiss"),
                        UI.Button("Continue", "app-update.recovery.restore", "app-update.recovery.restore"))),
                "app-update.recovery.dismiss", "app-update.recovery.dismiss"));
        return view;
    }
}
