using System.Text.Json;
using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class StartupSettingsScenarios
{
    public static async Task Run()
    {
        var service = new Startup();
        var widget = new SettingsWidget(startup: service);
        await widget.OnActionAsync(new WidgetActionEvent("open.overlay", "category.overlay"));
        var view = widget.Render();
        if (view.InitialFocusId != "interface.stepper.decrement") throw new Exception("Startup took initial focus.");
        var json = JsonSerializer.Serialize(view.CreateSnapshot("settings", 1));
        if (!json.Contains("Start WidgetRail when I sign in") || !json.Contains("Status")) throw new Exception("Startup control or status missing.");
        await widget.OnActionAsync(new WidgetActionEvent("startup.toggle", "overlay.startup"));
        if (!service.Status.Registered || service.Writes != 1) throw new Exception("Toggle did not use startup service.");
        service.Status = new(true, false, "Access unavailable");
        await widget.OnActionAsync(new WidgetActionEvent("startup.toggle", "overlay.startup"));
        if (service.Writes != 1) throw new Exception("Unavailable startup mutated.");
        await widget.OnActionAsync(new WidgetActionEvent("back", "test"));
        if (widget.CurrentPage != SettingsPage.Root) throw new Exception("Startup page trapped Back.");
    }
    private sealed class Startup : IStartupRegistration
    {
        public StartupRegistrationStatus Status = new(false, true, "Off");
        public int Writes;
        public StartupRegistrationStatus Read() => Status;
        public StartupRegistrationStatus SetEnabled(bool enabled)
        { Writes++; return Status = new(enabled, true, enabled ? "On" : "Off"); }
    }
}
