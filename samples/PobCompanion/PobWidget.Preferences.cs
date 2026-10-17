using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly PobPreferences preferences;
    private bool overlayWebsites;
    private WidgetWebSession? websiteSession;
    private string? websiteUrl;
    private bool websiteVisible;
    private readonly Dictionary<string,string> websiteActions=new(StringComparer.Ordinal);
    private WidgetElement WebsitePreference()=>UI.Stack("pob.website-preference",
        UI.Select("Open websites in",[new("windows","Windows default browser","websites.windows",!overlayWebsites),new("overlay","Overlay browser","websites.overlay",overlayWebsites)],"pob.websites").Classes("pob-select"),
        Text("Preference shared by PoE1 and PoE2. Each widget keeps its own website logins and extensions. Close the page to return to your build.","websites.help","pob-small pob-muted")).Classes("pob-field");

    private bool HandleWebsitePreference(WidgetActionEvent action)
    {
        if(action.ActionId is not("websites.windows"or"websites.overlay"))return false;
        var overlay=action.ActionId=="websites.overlay";
        Work(_=>{preferences.OverlayBrowser=overlay;lock(gate)overlayWebsites=overlay;return Task.CompletedTask;},"Saving website preference…");
        return true;
    }
    private ButtonElement WebsiteButton(string label,string action,string url)
    {
        websiteActions[action]=url;
        return Button(label,action)with{Intent=overlayWebsites?null:PobPreferences.Web(url)};
    }

    private bool HandleWebsiteAction(WidgetActionEvent action)
    {
        if(action.ActionId=="website-close"){websiteVisible=false;Invalidate();return true;}
        if(websiteVisible)return true;
        if(action.ActionId=="website-resume"&&websiteSession is not null){websiteVisible=true;Invalidate();return true;}
        if(overlayWebsites&&websiteActions.TryGetValue(action.ActionId,out var url))
        {Work(token=>OpenWebsiteAsync(url,token),"Opening website…");return true;}
        return false;
    }

    private async Task OpenWebsiteAsync(string url,CancellationToken token)
    {
        if(!WebBrowserDocument.IsWebUrl(url))throw new IOException("This website address is invalid.");
        var session=websiteSession;
        if(session is not null&&session.Completion.IsCompleted)
        {websiteSession=null;await session.DisposeAsync();session=null;}
        if(session is null)
        {
            session=await HostServices.WebSessions.OpenAsync(new("websites","PoB website",WebSessionSource.Remote(url))
            {Browser=new(){Storage=BrowserStorage.Persistent,ProfileKey="websites",Background=BrowserBackgroundMode.Automatic,Chrome=BrowserChrome.Toolbar,Popups=BrowserPopupMode.Hosted}},token);
            if(token.IsCancellationRequested){await session.DisposeAsync();token.ThrowIfCancellationRequested();}
            websiteSession=session;
        }
        else if(websiteUrl!=url)await session.NavigateAsync(url,token);
        token.ThrowIfCancellationRequested();
        lock(gate){websiteUrl=url;websiteVisible=true;}
    }

    private WidgetView WithWebsiteModal(WidgetView view)=>websiteVisible&&websiteSession is {} session
        ?view.WithModal(new("pob-website","Website",UI.WebView(session.Reference,"pob.website",interactionMode:BrowserInteractionMode.InteractOnFocus).Classes("pob-website-view"),
            "pob.website","website-close"){ScrollContent=false}):view;
}
