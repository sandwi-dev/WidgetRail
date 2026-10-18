using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace widgetrail.samples.MapGenie;

public sealed class MapGenie : Widget
{
    private readonly WebBrowserDocument page = new("webapp.session", 1, "https://mapgenie.io/", "MapGenie")
    {
        Options = WebBrowserOptions.WebApp,
        // Use Options with { ProfileKey = "work" } for a separate account.
        // Background = BrowserBackgroundMode.KeepAlive requests bounded hidden
        // residency; it does not grant microphone, camera, or other permissions.
    };

    public override WidgetView Render() => new(
        UI.Grid("webapp.root", [GridTrack.Star()], [GridTrack.Star()],
            UI.WebBrowser(page, BrowserInteractionMode.InteractOnFocus, "webapp.page")),
        "webapp.page", Surface: new()
        {
            Mode = WidgetSurfaceMode.Standard,
            WidthMode = WidgetSurfaceAxisMode.Preferred, HeightMode = WidgetSurfaceAxisMode.Preferred,
            PreferredWidth = 1100, PreferredHeight = 760, MinimumWidth = 480, MinimumHeight = 360,
        });
}

public static class Scenarios
{
    public static WidgetScenarioDefinition Ready() => new(
        new MapGenie(), new WidgetTestHostServicesBuilder().Build());
}
