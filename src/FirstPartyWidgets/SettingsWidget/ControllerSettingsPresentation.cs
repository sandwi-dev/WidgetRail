using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal static class ControllerSettingsPresentation
{
    public static WidgetView Render(SettingsPresentationState state, bool details = false)
    {
        var requested = state.Settings.Controllers.ExclusiveControl;
        var status = state.Diagnostics.Controllers;
        var failed = status.State == ControllerControlState.Failed;
        var displayedOn = requested && !failed;
        var canChange = displayedOn || status.CanEnable;
        var toggle = UI.Switch("Exclusive control", displayedOn,
                "controllers.exclusive-control.toggle", "controllers.exclusive-control")
            .Busy(state.Busy).Disabled(!canChange).AddClasses("setting-row");
        var recovery = status.State == ControllerControlState.RecoveryRequired;
        var refresh = UI.Button(recovery ? "Restore controller access" : failed ? "Keep Exclusive control off" : "Check again",
                recovery || failed ? "controllers.restore" : "refresh", "controllers.refresh")
            .Busy(state.Busy).Classes("setting-row");
        var statusText = status.State switch
        {
            ControllerControlState.Off => "Off",
            ControllerControlState.Starting => "Starting — release the controller controls",
            ControllerControlState.Stopping => "Turning off…",
            ControllerControlState.Failed => "Exclusive control could not start. Normal controller input has been restored. Turn this on to try again.",
            ControllerControlState.Active => "Active",
            ControllerControlState.WaitingForController => "Waiting for a controller",
            ControllerControlState.RecoveryRequired => "Controller access needs to be restored. Select Restore controller access to turn this off and retry cleanup.",
            _ => "Controller status unavailable",
        };
        var features = state.HostFeatures ?? new();
        if (!details)
        {
            var navigation = new List<WidgetElement>
            {
                UI.Text("Controllers", "controllers.heading").Classes("page-heading"),
                UI.Text("Open WidgetRail", "controllers.open-heading").Classes("section-heading"),
                UI.Select("Controller shortcut", [
                    new("guide", "Guide", "controllers.open-shortcut.guide", IsSelected: state.Settings.Controllers.OpenShortcut == ControllerOpenShortcut.Guide),
                    new("view-menu", "View + Menu", "controllers.open-shortcut.view-menu", IsSelected: state.Settings.Controllers.OpenShortcut == ControllerOpenShortcut.ViewMenu)
                ], "controllers.open-shortcut").Busy(state.Busy),
                UI.Text("Open and close the overlay with the selected shortcut.", "controllers.open-help").Classes("page-help"),
            };
            if (features.HeldDpadScroll) navigation.AddRange([
                UI.Text("Input behavior", "controllers.input-heading").Classes("section-heading"),
                UI.Switch("Hold D-pad to scroll", state.Settings.Controllers.HoldDpadToScroll,
                    "controllers.hold-scroll.toggle", "controllers.hold-scroll").Busy(state.Busy),
                UI.Text("Tap to move focus. Hold Up or Down to scroll, then release to focus a visible item.", "controllers.hold-scroll.help").Classes("page-help")]);
            if (features.ExclusiveControllerControl) navigation.AddRange([
                UI.Button($"Exclusive control: {(recovery ? "Needs attention" : displayedOn ? "On" : "Off")}",
                    "open.controller-help", "controllers.help").Classes("setting-row"),
                UI.Text("Manage controller isolation, driver status, and recovery.", "controllers.exclusive.summary").Classes("page-help")]);
            return SettingsPresentation.View(SettingsPresentation.Header(state),
                SettingsPresentation.PageScope("controllers.page", navigation.ToArray()), "controllers.open-shortcut", "controllers.page");
        }
        if (!features.ExclusiveControllerControl)
            return SettingsPresentation.View(SettingsPresentation.Header(state),
                SettingsPresentation.PageScope("controllers.exclusive.page",
                    UI.Text("Exclusive control", "controllers.exclusive.heading").Classes("page-heading"),
                    UI.Text("Exclusive control is unavailable on this host.", "controllers.help.unavailable").Classes("page-help")),
                "settings.back", "controllers.exclusive.page");
        return SettingsPresentation.View(SettingsPresentation.Header(state),
            SettingsPresentation.PageScope("controllers.exclusive.page",
                UI.Text("Exclusive control", "controllers.exclusive.heading").Classes("page-heading"),
                toggle,
                UI.Text("Keep controller input in WidgetRail while the overlay is open. Turn this on if your game also reacts when you use the overlay.",
                    "controllers.enable-help").Classes("page-help", "controllers-help"),
                UI.Text($"Status - {statusText}", "controllers.status").Classes("settings-status", "controllers-status"),
                refresh,
                UI.Text("Required drivers", "controllers.drivers-heading").Classes("section-heading"),
                Driver("HidHide", "controllers.hidhide", status.HidHideReady,
                    "Keeps the physical controller from also reaching your game."),
                Driver("ViGEmBus", "controllers.vigem", status.ViGEmBusReady,
                    "Provides the virtual controller your game uses."),
                UI.Text(!status.InputReady ? "Windows controller support is unavailable. Check again before turning this on."
                    : !canChange ? "Exclusive control is unavailable until the required drivers are ready."
                    : "The required drivers are ready.", "controllers.requirements-help").Classes("page-help"),
                UI.Text("Using exclusive control", "controllers.usage-heading").Classes("section-heading"),
                UI.Text("Turn this off if your controller stops working in a game or you notice unexpected input.",
                    "controllers.disable-help").Classes("page-help", "controllers-help"),
                UI.Text("Known issue: a single controller press may register twice in Settings or the Windows app switcher. Turn Exclusive control off if this happens.",
                    "controllers.compatibility-warning").Classes("page-help", "controllers-help", "controllers-warning"),
                UI.Text("Games and apps that are already open may keep using the previous controller setup. Close and reopen them after changing this setting.",
                    "controllers.restart-help").Classes("page-help", "controllers-help")),
            canChange ? "controllers.exclusive-control" : "controllers.refresh", "controllers.exclusive.page");
    }

    private static WidgetElement Driver(string name, string id, bool ready, string help) =>
        UI.Stack(id,
            UI.Text($"{name}: {(ready ? "Ready" : "Unavailable")}", id + ".status",
                $"{name} driver {(ready ? "ready" : "unavailable")}"),
            UI.Text(help, id + ".help", help).Classes("page-help"))
        .Classes("controller-driver");
}
