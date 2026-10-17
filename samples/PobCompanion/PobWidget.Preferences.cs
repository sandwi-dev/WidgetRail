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
        UI.Select("Open general links in",[new("windows","Windows default browser","websites.windows",!overlayWebsites),new("overlay","Overlay browser","websites.overlay",overlayWebsites)],"pob.websites").Classes("pob-select"),
        Text("Shared by PoE1 and PoE2. Trade always opens in the overlay for item comparisons. Each widget keeps its own website logins.","websites.help","pob-small pob-muted")).Classes("pob-field");

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
        if(websiteVisible&&!action.ActionId.StartsWith("trade-review-",StringComparison.Ordinal))return true;
        if(action.ActionId=="website-resume")
        {
            if(websiteSession is null||websiteSession.Completion.IsCompleted)
            {
                var retry=tradeListing?.FindUrl(game)??websiteUrl;
                if(retry is not null)Work(token=>OpenWebsiteAsync(retry,token),"Finding saved listing…");
            }
            else{websiteVisible=true;websiteFocus="pob.website.browser";websiteFocusSequence=++focusRequestSequence;Invalidate();}
            return true;
        }
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
            {Browser=new(){Storage=BrowserStorage.Persistent,ProfileKey="websites",Background=BrowserBackgroundMode.Automatic,Chrome=BrowserChrome.Toolbar,Popups=BrowserPopupMode.Hosted},
             PageAdapter=new(){ScriptAsset="payload/web/trade-actions.js",Origins=["https://www.pathofexile.com"],Responses=[new("https://www.pathofexile.com",game==2?"/api/trade2/fetch/":"/api/trade/fetch/")]}},token);
            if(token.IsCancellationRequested){await session.DisposeAsync();token.ThrowIfCancellationRequested();}
            websiteSession=session;
            _=ReadWebsiteActionsAsync(session);
        }
        else if(websiteUrl!=url)await session.NavigateAsync(url,token);
        token.ThrowIfCancellationRequested();
        lock(gate){websiteUrl=url;websiteVisible=true;if(CanReplaceTradeComparison&&pendingBefore is not null)websiteReview=true;}
    }

    private WidgetView WithWebsiteModal(WidgetView view)
    {
        if(!websiteVisible||websiteSession is not {} session)return view;
        var reviewing=websiteReview&&CanReplaceTradeComparison&&pendingBefore is not null;
        var web=UI.Stack("pob.website.browser",UI.WebView(session.Reference,"pob.website",interactionMode:reviewing?BrowserInteractionMode.ActivateToInteract:BrowserInteractionMode.InteractOnFocus).Classes("pob-website-view"))
            .RememberChildFocus("pob.website").Classes("pob-workspace");
        if(!reviewing&&CanReplaceTradeComparison)web=web.Shortcut(ControllerButton.RightStick,"trade-review-show","Show comparison");
        WidgetElement body=reviewing?Split("website",web,TradeReviewPanel(),3,2):web;
        var modal=view.WithModal(new("pob-website","Website",WithFeedback(body,null),"pob.website","website-close")
        {
            ScrollContent=false,
            HeaderActions=UI.Row("pob.website.header-actions",
                (!reviewing&&CanReplaceTradeComparison
                    ?new[]{Button("Show comparison","trade-review-show","website.review-shortcut"),Button("Close","website-close","website.close")}
                    :new[]{Button("Close","website-close","website.close")}).Cast<WidgetElement>().ToArray()).Classes("pob-toolbar")
        });
        if(!busy&&websiteFocusSequence>0)
            modal=modal with{FocusGroupEntryRequest=new(){RequestId=websiteFocusSequence,GroupId=reviewing?websiteFocus:"pob.website.browser"}};
        if(!busy&&reviewing&&websiteReviewRevealSequence>0)
            modal=modal with{ScrollRevealRequest=new(websiteReviewRevealSequence,"pob.website.review.details","pob.website.review.skill")};
        return modal;
    }
}
