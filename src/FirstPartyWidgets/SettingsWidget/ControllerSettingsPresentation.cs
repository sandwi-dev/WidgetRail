using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal static class ControllerSettingsPresentation
{
    public static WidgetView Render(SettingsPresentationState state)
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
        return SettingsPresentation.View(SettingsPresentation.Header(state),
            SettingsPresentation.PageScope("controllers.page",
                UI.Text("Controllers", "controllers.heading", "Controllers settings").Classes("page-heading"),
                UI.Text("Input behavior", "controllers.input-heading", "Input behavior").Classes("section-heading"),
                toggle,
                UI.Text("Keep controller input in WidgetRail while the overlay is open. Turn this on if your game also reacts when you use the overlay.",
                    "controllers.enable-help", "When to enable Exclusive control").Classes("page-help", "controllers-help"),
                UI.Text("Turn this off if your controller stops working in a game or you notice unexpected input.",
                    "controllers.disable-help", "When to disable Exclusive control").Classes("page-help", "controllers-help"),
                UI.Text("Known issue: a single controller press may register twice in Settings or the Windows app switcher. Turn Exclusive control off if this happens.",
                    "controllers.compatibility-warning",
                    "Known issue: a single controller press may register twice in Settings or the Windows app switcher. Turn Exclusive control off if this happens.")
                    .Classes("page-help", "controllers-help", "controllers-warning"),
                UI.Text("Games and apps that are already open may keep using the previous controller setup. Close and reopen them after changing this setting.",
                    "controllers.restart-help", "Restart open games and apps after changing Exclusive control").Classes("page-help", "controllers-help"),
                UI.Text(statusText, "controllers.status", $"Exclusive control status: {statusText}").Classes("settings-status"),
                UI.Text("Exclusive control uses a virtual Xbox 360 controller created by WidgetRail. Windows may show it under the standard Xbox controller name.",
                    "controllers.output-help", "WidgetRail virtual controller").Classes("page-help", "controllers-help"),
                UI.Text("Required drivers", "controllers.drivers-heading", "Required drivers").Classes("section-heading"),
                Driver("HidHide", "controllers.hidhide", status.HidHideReady,
                    "Keeps the physical controller from also reaching your game."),
                Driver("ViGEmBus", "controllers.vigem", status.ViGEmBusReady,
                    "Provides the virtual controller your game uses."),
                UI.Text(!status.InputReady
                        ? "Windows controller support is unavailable. Check again before turning this on."
                        : !canChange
                            ? "Exclusive control is unavailable until the required drivers are ready."
                            : "The required drivers must be ready before Exclusive control can be turned on.",
                    "controllers.requirements-help", "Exclusive control requirements").Classes("page-help"),
                refresh),
            canChange ? "controllers.exclusive-control" : "controllers.refresh",
            "controllers.page");
    }

    private static WidgetElement Driver(string name, string id, bool ready, string help) =>
        UI.Stack(id,
            UI.Text($"{name}: {(ready ? "Ready" : "Unavailable")}", id + ".status",
                $"{name} driver {(ready ? "ready" : "unavailable")}"),
            UI.Text(help, id + ".help", help).Classes("page-help"))
        .Classes("controller-driver");
}
