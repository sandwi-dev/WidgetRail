using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ControllerSettingsScenarios
{
    public static async Task HostFeatures()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-host-features-" + Guid.NewGuid().ToString("N"));
        var store = new PlatformSettingsStore(new(root));
        var service = new ControllerService(store) { Status = new(ControllerControlState.Off, true, true, true) };
        var startup = new RejectStartupMutation();
        var widget = new SettingsWidget(store: store, diagnostics: service, hostFeatures: new(false, false, false),
            startup: startup);
        try
        {
            await widget.InitializeAsync(default);
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Visible, default);
            await widget.InitializationTask;
            await widget.OnActionAsync(new("open.controllers", "test"));
            var snapshot = widget.Render().CreateSnapshot("settings", 1);
            var json = JsonSerializer.Serialize(snapshot);
            if (!json.Contains("controllers.open-shortcut") || json.Contains("controllers.hold-scroll") ||
                json.Contains("controllers.exclusive-control") || json.Contains("HidHide"))
                throw new Exception("Settings must present only host-supported controller controls.");
            var errors = ViewSnapshotValidator.Validate(snapshot);
            if (errors.Count != 0) throw new Exception(string.Join(Environment.NewLine, errors));
            await widget.OnActionAsync(new("controllers.hold-scroll.toggle", "controllers.hold-scroll"));
            await widget.OnActionAsync(new("controllers.exclusive-control.toggle", "controllers.exclusive-control"));
            var saved = await store.LoadAsync();
            if (saved.Controllers.HoldDpadToScroll || saved.Controllers.ExclusiveControl || service.Requests.Count != 0)
                throw new Exception("Stale unsupported controller actions must not mutate the profile or call providers.");
            await widget.OnActionAsync(new("controllers.open-shortcut.toggle", "controllers.open-shortcut"));
            if ((await store.LoadAsync()).Controllers.OpenShortcut != ControllerOpenShortcut.Guide)
                throw new Exception("Supported shortcut behavior must remain available.");
            await widget.OnActionAsync(new("open.overlay", "test"));
            if (JsonSerializer.Serialize(widget.Render().CreateSnapshot("settings", 2)).Contains("overlay.startup"))
                throw new Exception("Host-unavailable startup controls must not be exposed.");
            await widget.OnActionAsync(new("startup.toggle", "overlay.startup"));
        }
        finally
        {
            await widget.SetLifecycleStateAsync(WidgetLifecycleState.Background, default);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class RejectStartupMutation : IStartupRegistration
    {
        public StartupRegistrationStatus Read() => new(false, true, "Available");
        public StartupRegistrationStatus SetEnabled(bool enabled) => throw new Exception("Unsupported startup action reached system settings.");
    }

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
            await widget.InitializationTask;
            await widget.OnActionAsync(new WidgetActionEvent("open.controllers", "test"));
            await widget.OnActionAsync(new WidgetActionEvent("controllers.open-shortcut.toggle", "controllers.open-shortcut"));
            var shortcutSettings = await store.LoadAsync();
            if (shortcutSettings.Controllers.OpenShortcut != ControllerOpenShortcut.Guide ||
                shortcutSettings.Controllers.ExclusiveControl || shortcutSettings.Controllers.Revision != 0)
                throw new Exception("Shortcut must save independently of driver readiness and exclusive ownership.");
            await widget.OnActionAsync(new WidgetActionEvent("controllers.open-shortcut.toggle", "controllers.open-shortcut"));
            if ((await store.LoadAsync()).Controllers.OpenShortcut != ControllerOpenShortcut.ViewMenu)
                throw new Exception("Shortcut must switch back to View + Menu.");
            await widget.OnActionAsync(new WidgetActionEvent("controllers.open-shortcut.guide", "controllers.open-shortcut"));
            await widget.OnActionAsync(new WidgetActionEvent("controllers.open-shortcut.guide", "controllers.open-shortcut"));
            if ((await store.LoadAsync()).Controllers.OpenShortcut != ControllerOpenShortcut.Guide)
                throw new Exception("Selecting the current shortcut must not toggle it.");
            await widget.OnActionAsync(new WidgetActionEvent("controllers.open-shortcut.view-menu", "controllers.open-shortcut"));
            if ((await store.LoadAsync()).Controllers.HoldDpadToScroll)
                throw new Exception("Held D-pad scrolling must be opt-in.");
            await widget.OnActionAsync(new WidgetActionEvent("controllers.hold-scroll.toggle", "controllers.hold-scroll"));
            var heldSettings = await store.LoadAsync();
            if (!heldSettings.Controllers.HoldDpadToScroll || heldSettings.Controllers.ExclusiveControl ||
                heldSettings.Controllers.OpenShortcut != ControllerOpenShortcut.ViewMenu)
                throw new Exception("Held scrolling must persist independently of shortcut and controller isolation.");
            await widget.OnActionAsync(new WidgetActionEvent("controllers.hold-scroll.toggle", "controllers.hold-scroll"));
            if ((await store.LoadAsync()).Controllers.HoldDpadToScroll)
                throw new Exception("Held scrolling can be disabled.");
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
            await store.UpdateAsync(current => current with { Controllers = current.Controllers with { ExclusiveControl = true } });
            service.Status = new(ControllerControlState.Failed, true, true, true);
            await widget.OnActionAsync(new WidgetActionEvent("refresh", "controllers.refresh"));
            json = JsonSerializer.Serialize(widget.Render().CreateSnapshot("settings", 1));
            if (!json.Contains("Exclusive control, Off") || !json.Contains("Normal controller input has been restored"))
                throw new Exception("Failed startup must display Off and explain ordinary input restoration.");
            await widget.OnActionAsync(new WidgetActionEvent("controllers.exclusive-control.toggle", "controllers.exclusive-control"));
            if (!service.Requests.Last()) throw new Exception("Retry after failed startup must request enable, not another disable.");
            service.Status = new(ControllerControlState.Failed, true, true, true);
            await widget.OnActionAsync(new WidgetActionEvent("refresh", "controllers.refresh"));
            await widget.OnActionAsync(new WidgetActionEvent("controllers.restore", "controllers.refresh"));
            if (service.Requests.Last() || (await store.LoadAsync()).Controllers.ExclusiveControl)
                throw new Exception("Keep off must cancel the saved enable request.");
            await widget.OnActionAsync(new WidgetActionEvent("open.controller-help", "controllers.help"));
            var previousRequests = service.Requests.Count;
            await widget.OnActionAsync(new WidgetActionEvent("controllers.restore", "controllers.refresh"));
            if (service.Requests.Count != previousRequests + 1 || service.Requests.Last())
                throw new Exception("Recovery must remain actionable from driver help.");
            await widget.OnActionAsync(new WidgetActionEvent("back", "test"));
            if (widget.CurrentPage != SettingsPage.Controllers) throw new Exception("Driver help must return to Controllers.");
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
        if (document.Controllers.OpenShortcut != ControllerOpenShortcut.ViewMenu ||
            JsonSerializer.Deserialize<ControllerSettings>("{\"ExclusiveControl\":true,\"Revision\":3}")?.OpenShortcut != ControllerOpenShortcut.ViewMenu)
            throw new Exception("Older settings without a shortcut and new installs must default to View + Menu.");
        if (PlatformSettingsValidator.Validate(document with
            { Controllers = new() { OpenShortcut = (ControllerOpenShortcut)99 } }).Count == 0)
            throw new Exception("Unknown shortcut values must be rejected.");
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
            AssertNavigation(snapshot, status.CanEnable);
            var json = JsonSerializer.Serialize(snapshot) + JsonSerializer.Serialize(ControllerSettingsPresentation.Render(state, details: true).CreateSnapshot("settings", 2));
            foreach (var text in new[] { "Input behavior", "Required drivers", "Exclusive control", "HidHide", "ViGEmBus",
                "your game also reacts", "controller stops working in a game",
                "single controller press may register twice in Settings or the Windows app switcher",
                "Status - " })
                if (!json.Contains(text, StringComparison.Ordinal)) throw new Exception("Missing guidance: " + text);
            if (snapshot.InitialFocusId != "controllers.open-shortcut")
                throw new Exception("Shortcut must be the first option regardless of driver readiness.");
            var enabled = state with { Settings = document with { Controllers = new() { ExclusiveControl = true } } };
            SettingsPresentation.TryRender(enabled, out var enabledView);
            AssertNavigation(enabledView.CreateSnapshot("settings", 1), true);
            if (enabledView.CreateSnapshot("settings", 1).InitialFocusId != "controllers.open-shortcut")
                throw new Exception("Shortcut must retain initial focus when Exclusive control is enabled.");
        }
        if (!SettingsNavigationPolicy.TryResolve("open.controllers", SettingsPage.Root, SettingsPage.Root, out var target) ||
            target != SettingsPage.Controllers || SettingsNavigationPolicy.Parent(target, SettingsPage.Root) != SettingsPage.Root)
            throw new Exception("Controllers navigation and Back must remain scoped.");
        return Task.CompletedTask;
    }

    private static void AssertNavigation(ViewSnapshot snapshot, bool canChange)
    {
        static IEnumerable<ViewNode> Nodes(ViewNode node) =>
            new[] { node }.Concat(node.Children.SelectMany(Nodes));
        var nodes = Nodes(snapshot.Root).ToArray();
        var toggle = nodes.Single(node => node.Id == "controllers.exclusive-control");
        var help = nodes.Single(node => node.Id == "controllers.help");
        if (toggle.Focus?.Down != "controllers.help" || help.Focus?.Up != "controllers.exclusive-control")
            throw new Exception("Controller actions must follow the visible row order.");
        if (nodes.Single(node => node.Id == "controllers.open-shortcut").Focus?.Up != "settings.back")
            throw new Exception("Shortcut must be connected to the header.");
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0) throw new Exception(string.Join(Environment.NewLine, errors));
    }
}
