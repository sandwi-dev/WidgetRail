using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly PobPreferences preferences;
    private bool overlayWebsites;
    private WidgetWebSession? websiteSession;
    private string? websiteUrl;
    private string websiteHeading="Website";
    private string? websiteBuild;
    private bool websiteVisible;
    private readonly Dictionary<string,string> websiteActions=new(StringComparer.Ordinal);
    internal static string WebsiteHeading(string? url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not("http"or"https"))return "Website";
        var host=uri.IdnHost.ToLowerInvariant();
        if(host.StartsWith("www.",StringComparison.Ordinal))host=host[4..];
        return host switch
        {
            "pathofexile.com" when uri.AbsolutePath.StartsWith("/trade2/",StringComparison.Ordinal)||uri.AbsolutePath=="/trade2"=>"PoE2 Trade",
            "pathofexile.com" when uri.AbsolutePath.StartsWith("/trade/",StringComparison.Ordinal)||uri.AbsolutePath=="/trade"=>"PoE1 Trade",
            "pathofexile.com"=>"Path of Exile",
            "pathofexile2.com"=>"Path of Exile 2",
            _=>host
        };
    }
    private WidgetElement WebsitePreference()=>UI.Stack("pob.website-preference",
        UI.Select("Open websites in",[new("windows","Windows default browser","websites.windows",!overlayWebsites),new("overlay","Overlay browser","websites.overlay",overlayWebsites)],"pob.websites").Classes("pob-select"),
        Text("Shared by PoE1 and PoE2. Trade always opens in the overlay for comparisons.","websites.help","pob-small pob-muted")).Classes("pob-field");

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
        if(websiteVisible&&!action.ActionId.StartsWith("trade-review-",StringComparison.Ordinal)&&!action.ActionId.StartsWith("trade-rank-",StringComparison.Ordinal))return true;
        if(action.ActionId=="website-resume")
        {
            if(websiteSession is null||websiteSession.Completion.IsCompleted||websiteBuild!=documentId)
            {
                var retry=savedTradeUrl??tradeListing?.Url??websiteUrl;
                if(retry is not null)Work(token=>OpenWebsiteAsync(retry,token),"Restoring trade results…");
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
             PageAdapter=new(){ScriptAsset="payload/web/trade-actions.js",Origins=["https://www.pathofexile.com"],Responses=[new("https://www.pathofexile.com",gameDefinition.TradeApiPath+"fetch/")]}},token);
            if(token.IsCancellationRequested){await session.DisposeAsync();token.ThrowIfCancellationRequested();}
            websiteSession=session;
            _=ReadWebsiteActionsAsync(session);
        }
        else if(websiteUrl!=url)await session.NavigateAsync(url,token);
        token.ThrowIfCancellationRequested();
        lock(gate){websiteUrl=url;websiteHeading=WebsiteHeading(url);websiteBuild=documentId;SaveTradeReturn(url);websiteVisible=true;if(CanReplaceTradeComparison&&pendingBefore is not null)websiteReview=true;}
    }

    private WidgetView WithWebsiteModal(WidgetView view)
    {
        if(!websiteVisible||websiteSession is not {} session)return view;
        var ranking=rankingVisible&&HasRanking;
        var reviewing=!ranking&&websiteReview&&CanReplaceTradeComparison&&pendingBefore is not null;
        // Native header actions remain reachable even when the side panel is hidden.
        // B leaves website interaction; a subsequent B closes the modal.
        var web=UI.Stack("pob.website.browser",UI.WebView(session.Reference,"pob.website",interactionMode:BrowserInteractionMode.ActivateToInteract).Classes("pob-website-view"))
            .RememberChildFocus("pob.website").Classes("pob-workspace");
        if(!reviewing&&!ranking&&HasRanking)web=web.Shortcut(ControllerButton.RightStick,"trade-rank-show","Ranked results");
        else if(!reviewing&&!ranking&&CanReplaceTradeComparison)web=web.Shortcut(ControllerButton.RightStick,"trade-review-show","Show comparison");
        WidgetElement body=ranking?Split("website",web,TradeRankingPanel(),3,2):reviewing?Split("website",web,TradeReviewPanel(),3,2):web;
        var modal=view.WithModal(new("pob-website",websiteHeading,WithFeedback(body,null),"pob.website","website-close")
        {
            ScrollContent=false,
            HeaderActions=UI.Row("pob.website.header-actions",
                (ranking
                    ?new[]{Button("Hide panel","trade-rank-hide","website.rank-hide"),Button("Close","website-close","website.close")}
                    :reviewing
                    ?new[]{Button("Hide panel","trade-review-hide","website.review-hide"),Button("Close","website-close","website.close")}
                    :HasRanking
                    ?new[]{Button("Ranked results","trade-rank-show","website.rank-shortcut"),Button("Close","website-close","website.close")}
                    :!reviewing&&CanReplaceTradeComparison
                    ?new[]{Button("Show comparison","trade-review-show","website.review-shortcut"),Button("Close","website-close","website.close")}
                    :new[]{Button("Close","website-close","website.close")}).Cast<WidgetElement>().ToArray()).Classes("pob-toolbar")
        });
        if(!busy&&websiteFocusSequence>0)
            modal=modal with{FocusGroupEntryRequest=new(){RequestId=websiteFocusSequence,GroupId=reviewing||ranking?websiteFocus:"pob.website.browser"}};
        return modal;
    }
}
