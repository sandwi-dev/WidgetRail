using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string tradeSlot="",tradeLeague="Standard",tradeBudget="",tradeCurrency="divine",tradePriority="balanced",tradeMode="securable";
    private string[] tradeLeagues=["Standard","Hardcore"];
    internal Func<int,CancellationToken,Task<string[]>> TradeLeagueLoader { get; init; } = PobTradeSearch.ReadLeaguesAsync;
    private static readonly (string Key,string Label)[] TradeModes=[("securable","Instant buyout"),("available","Instant buyout and in person"),("onlineleague","In person (online in league)"),("online","In person (online)"),("any","Any (includes offline)")];
    private static readonly (string Key,string Label,string Help)[] TradePriorities=[
        ("balanced","Damage and defence","Uses PoB’s default weights: Full DPS × 1 and effective hit pool × 0.5."),
        ("damage","Damage","Weights modifiers by their effect on your build’s Full DPS."),
        ("defence","Defence","Weights modifiers by effective hit pool under your current configuration."),
        ("life","Life","Weights modifiers by their effect on maximum life."),
        ("energyshield","Energy shield","Weights modifiers by their effect on maximum energy shield.")];

    private bool HandleTradeAction(WidgetActionEvent action)
    {
        var id=action.ActionId;var parts=id.Split('.');
        if(id=="trade")
        {
            tradeSlot=ItemSlot;detailKind="trade";focusRequestSequence++;ReadTradePreferences();
            RefreshTradeLeagues();return true;
        }
        if(id=="trade-browse")
        {ReadTradePreferences();Work(token=>OpenWebsiteAsync(BrowseTradeUrl(),token),"Opening trade website…");return true;}
        if(detailKind!="trade")return false;
        if(id=="trade-open"&&result is {} current)
        {
            var slot=tradeSlot;var priority=tradePriority;var league=tradeLeague;var price=tradeBudget;var currency=tradeCurrency;var mode=tradeMode;
            Work(async token=>await OpenWebsiteAsync(await CalculateTradeUrlAsync(current.Xml,slot,priority,league,price,currency,mode,token),token),"Calculating upgrade search…");
            return true;
        }
        if(id=="back"){detailKind="item";focusRequestSequence++;return true;}
        if(id=="trade-refresh"){RefreshTradeLeagues();return true;}
        if(id=="trade-budget")tradeBudget=action.CommittedText??"";
        else if(parts is["trade-league",var league]&&int.TryParse(league,out var li)&&li>=0&&li<tradeLeagues.Length)tradeLeague=tradeLeagues[li];
        else if(parts is["trade-mode",var mode]&&TradeModes.Any(m=>m.Key==mode))tradeMode=mode;
        else if(parts is["trade-priority",var priority]&&TradePriorities.Any(p=>p.Key==priority))tradePriority=priority;
        else if(parts is["trade-currency",var currency]&&currency is "divine"or"chaos"or"exalted")tradeCurrency=currency;
        else return false;
        Work(_=>{BuildDocument.SaveAtomic(Path.Combine(dataDirectory,"trade-settings.json"),JsonSerializer.Serialize(new{league=tradeLeague,budget=tradeBudget,currency=tradeCurrency,priority=tradePriority,mode=tradeMode}));return Task.CompletedTask;},"Saving search preferences…");
        return true;
    }
    private string BrowseTradeUrl()=>PobTradeSearch.SearchUrl(game,tradeLeague,
        game==2?JsonSerializer.SerializeToElement(new{status=new{option=tradeMode}}):JsonSerializer.SerializeToElement(new{query=new{status=new{option=tradeMode}}}));
    private ButtonElement BrowseTradeButton()=>Button("Browse trade website","trade-browse");
    private ButtonElement TradeSearchButton()=>Button("Search trade","trade-open");
    private async Task<string> CalculateTradeUrlAsync(string xml,string slot,string priority,string league,string price,string currency,string mode,CancellationToken token)
    {
        var budget=0d;
        if(price.Length>0&&(!double.TryParse(price,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out budget)||!double.IsFinite(budget)||budget<0))throw new IOException("Enter a positive budget, or leave it empty for no price limit.");
        var query=await engine.ExecuteAsync(new{op="trade-search",slot,priority,budget,currency,mode},token,xml);
        token.ThrowIfCancellationRequested();
        return PobTradeSearch.SearchUrl(game,league,query.State.GetProperty("query"));
    }
    private void ReadTradePreferences()
    {
        try
        {
            var path=Path.Combine(dataDirectory,"trade-settings.json");if(!File.Exists(path)||new FileInfo(path).Length>4096)return;
            using var json=JsonDocument.Parse(File.ReadAllText(path));var root=json.RootElement;
            var league=S(root,"league");if(league.Length is >0 and <=160&&!league.Any(char.IsControl))tradeLeague=league;
            var budget=S(root,"budget");if(budget.Length<=20)tradeBudget=budget;
            var currency=S(root,"currency");if(currency is "divine"or"chaos"or"exalted")tradeCurrency=currency;
            var mode=S(root,"mode");if(TradeModes.Any(m=>m.Key==mode))tradeMode=mode;
            var priority=S(root,"priority");if(TradePriorities.Any(p=>p.Key==priority))tradePriority=priority;
            tradeLeagues=tradeLeagues.Append(tradeLeague).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){ }
    }
    private void RefreshTradeLeagues()=>Work(async token=>
    {
        var leagues=await TradeLeagueLoader(game,token);
        lock(gate)tradeLeagues=leagues.Append(tradeLeague).Distinct(StringComparer.Ordinal).ToArray();
    },"Loading trade leagues…");

    private WidgetElement TradeWorkspace()=>UI.Stack("pob.trade.page",ManagementHeader("Find equipment upgrades"),
        UI.VerticalScroll("pob.trade.scroll",
            Text(tradeSlot,"trade.slot","pob-heading"),
            BrowseTradeButton(),
            Text("Search for non-unique equipment that may improve this build. PoB calculates the filters; listings and trading stay on the official website.","trade.help","pob-muted"),
            Text("Trade opens in the overlay so you can compare listings beside your build. Sign in once; this widget remembers your login.","trade.website-help","pob-small pob-muted"),
            UI.Select("League",tradeLeagues.Select((l,i)=>new SelectOption("league"+i,l,"trade-league."+i,l==tradeLeague)).ToArray(),"pob.trade-league").Classes("pob-select"),
            UI.Select("Trade method",TradeModes.Select(m=>new SelectOption(m.Key,m.Label,"trade-mode."+m.Key,m.Key==tradeMode)).ToArray(),"pob.trade-mode").Classes("pob-select"),
            Text("Instant buyout lets you travel to the merchant and buy in game. In-person listings require contacting the seller.","trade.mode-help","pob-small"),
            UI.Select("Prioritize",TradePriorities.Select(p=>new SelectOption(p.Key,p.Label,"trade-priority."+p.Key,p.Key==tradePriority)).ToArray(),"pob.trade-priority").Classes("pob-select"),
            Text(TradePriorities.Single(p=>p.Key==tradePriority).Help,"trade.priority-help","pob-small pob-muted"),
            UI.Row("pob.trade.price",Field("Maximum price (optional)",UI.TextEntry(tradeBudget,"No price limit","trade-budget","pob.trade-budget",20).Classes("pob-input"),"trade-budget"),
                UI.Select("Currency",new[]{"divine","chaos","exalted"}.Select(c=>new SelectOption(c,char.ToUpperInvariant(c[0])+c[1..]+" Orb","trade-currency."+c,c==tradeCurrency)).ToArray(),"pob.trade-currency").Classes("pob-select")).Classes("pob-toolbar","pob-price-fields"),
            UI.Row("pob.trade.actions",(websiteSession is not null?new[]{TradeSearchButton(),Button("Return to website","website-resume")}:new[]{TradeSearchButton()}).Cast<WidgetElement>().ToArray()).Classes("pob-toolbar"),
            Text("Weights estimate an upgrade; they do not guarantee it. Compare an item before applying it.","trade.caution","pob-small pob-muted"),
            UI.Row("pob.trade.return",Button("Paste item to compare","item-paste"),Button("Refresh leagues","trade-refresh")).Classes("pob-toolbar"),
            Text(game==2
                ?"In the overlay browser, use Compare in PoB beside a listing. For clipboard import, hover over an item in game and press Ctrl+C. The PoE2 website has no built-in Copy item button in an external browser."
                :"In the overlay browser, use Compare in PoB beside a listing. On the trade site, use Copy item for clipboard import. In game, hover over an item and press Ctrl+C, then paste here.","trade.paste-help","pob-small pob-muted"))
            .Classes("pob-scroll","pob-panel")).Classes("pob-workspace");
}
