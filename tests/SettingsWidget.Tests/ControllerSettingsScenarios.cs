using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetSdk;

internal static class ControllerSettingsScenarios
{
    public static async Task ActionsAndRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-controller-settings-" + Guid.NewGuid().ToString("N"));
        var paths = new PlatformSettingsPaths(root);
        var store = new PlatformSettingsStore(paths);
        var service = new ControllerService(store);
        var widget = new SettingsWidget(store, new ThemeCatalog(paths), diagnostics: service);
        try
        {
            await widget.InitializeAsync(default);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, default);
            await widget.OnActionAsync(new WidgetActionEvent("open.controllers", "test"));
            await widget.OnActionAsync(new WidgetActionEvent("controllers.exclusive-control.toggle", "controllers.exclusive-control"));
            if ((await store.LoadAsync()).Controllers.ExclusiveControl) throw new Exception("Unavailable enable persisted.");
            service.Status = new(ControllerControlState.Off, true, true, true);
            await widget.OnActionAsync(new WidgetActionEvent("controllers.exclusive-control.toggle", "controllers.exclusive-control"));
            if (!(await store.LoadAsync()).Controllers.ExclusiveControl) throw new Exception("Ready enable did not persist.");
            service.Status = ControllerControlStatus.Unavailable;
            await widget.OnActionAsync(new WidgetActionEvent("controllers.exclusive-control.toggle", "controllers.exclusive-control"));
            if ((await store.LoadAsync()).Controllers.ExclusiveControl) throw new Exception("Disable requires available drivers.");
            service.Status = new(ControllerControlState.RecoveryRequired, true, true, true);
            await widget.OnActionAsync(new WidgetActionEvent("refresh", "controllers.refresh"));
            var json = JsonSerializer.Serialize(widget.Render().CreateSnapshot("settings", 1));
            if (!json.Contains("Restore controller access")) throw new Exception("Recovery action is missing while already off.");
            await widget.OnActionAsync(new WidgetActionEvent("controllers.restore", "controllers.refresh"));
            if (service.Requests.Last() || (await store.LoadAsync()).Controllers.Revision != 3)
                throw new Exception("Recovery must reissue off with a new revision.");
            await widget.OnActionAsync(new WidgetActionEvent("back", "test"));
            if (widget.CurrentPage != SettingsPage.Root) throw new Exception("Controller page Back scope is wrong.");
        }
        finally
        {
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class ControllerService(PlatformSettingsStore store) : IPlatformDiagnosticsService
    {
        public ControllerControlStatus Status { get; set; } = ControllerControlStatus.Unavailable;
        public List<bool> Requests { get; } = [];
        public ValueTask<PlatformDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformDiagnosticsSnapshot.Unavailable() with { Controllers = Status });
        public async ValueTask<ControllerControlResult> SetExclusiveControlAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            Requests.Add(enabled);
            if (enabled && !Status.CanEnable) return new(false, Status);
            await store.UpdateAsync(current => current with
            {
                Controllers = new() { ExclusiveControl = enabled, Revision = current.Controllers.Revision + 1 },
            }, cancellationToken);
            return new(true, Status with { State = enabled ? ControllerControlState.Starting : ControllerControlState.Stopping });
        }
    }

    public static Task LayoutAndGates()
    {
        var document = PlatformSettingsDocument.Default;
        if (document.Controllers.ExclusiveControl) throw new Exception("Exclusive control must default off.");
        foreach (var flags in Enumerable.Range(0, 8))
        {
            var status = new ControllerControlStatus(ControllerControlState.Off,
                (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
            if (status.CanEnable != (flags == 7)) throw new Exception("Prerequisites must all be ready.");
            var state = new SettingsPresentationState(SettingsPage.Controllers, document,
                new ThemeCatalogSnapshot([]), PlatformDiagnosticsSnapshot.Unavailable() with { Controllers = status },
                null, "Ready", true, false, false);
            if (!SettingsPresentation.TryRender(state, out var view)) throw new Exception("Missing Controllers page.");
            var snapshot = view.CreateSnapshot("settings", 1);
            var json = JsonSerializer.Serialize(snapshot);
            foreach (var text in new[] { "Input behavior", "Required drivers", "Exclusive control", "HidHide", "ViGEmBus",
                "your game also reacts", "controller stops working in a game" })
                if (!json.Contains(text, StringComparison.Ordinal)) throw new Exception("Missing guidance: " + text);
            if (snapshot.InitialFocusId != (status.CanEnable ? "controllers.exclusive-control" : "controllers.refresh"))
                throw new Exception("Unavailable toggle must not own initial focus.");
            var enabled = state with { Settings = document with { Controllers = new() { ExclusiveControl = true } } };
            SettingsPresentation.TryRender(enabled, out var enabledView);
            if (enabledView.CreateSnapshot("settings", 1).InitialFocusId != "controllers.exclusive-control")
                throw new Exception("Disable must remain reachable when drivers fail.");
        }
        if (!SettingsNavigationPolicy.TryResolve("open.controllers", SettingsPage.Root, SettingsPage.Root, out var target) ||
            target != SettingsPage.Controllers || SettingsNavigationPolicy.Parent(target, SettingsPage.Root) != SettingsPage.Root)
            throw new Exception("Controllers navigation and Back must remain scoped.");
        return Task.CompletedTask;
    }
}
