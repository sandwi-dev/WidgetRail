using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly PobPreferences preferences;
    private bool overlayWebsites;
    private WidgetElement WebsitePreference()=>UI.Stack("pob.website-preference",
        UI.Select("Open websites in",[new("windows","Windows default browser","websites.windows",!overlayWebsites),new("overlay","Overlay browser","websites.overlay",overlayWebsites)],"pob.websites").Classes("pob-select"),
        Text("Shared by PoE1 and PoE2. The overlay browser keeps its own logins and extensions. B returns to your build after closing browser dialogs.","websites.help","pob-small pob-muted")).Classes("pob-field");

    private bool HandleWebsitePreference(WidgetActionEvent action)
    {
        if(action.ActionId is not("websites.windows"or"websites.overlay"))return false;
        var overlay=action.ActionId=="websites.overlay";
        Work(_=>{preferences.OverlayBrowser=overlay;lock(gate)overlayWebsites=overlay;return Task.CompletedTask;},"Saving website preference…");
        return true;
    }
    private ButtonElement WebsiteButton(string label,string action,string url)=>Button(label,action)with{Intent=PobPreferences.Web(url,overlayWebsites)};
}
