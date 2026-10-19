using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string tradeSlot="",tradeLeague="",tradeBudget="",tradeCurrency="divine",tradePriority="balanced",tradeMode="securable";
    private string? cachedTradeKey,cachedTradeUrl;
    private string[] tradeLeagues=[];
    private bool tradeLeaguesLoaded;
    private bool TradeSearchAvailable=>TradeCatalogReady&&TradeSlotAvailable&&tradeLeagues.Contains(tradeLeague,StringComparer.Ordinal);
    internal Func<int,CancellationToken,Task<string[]>> TradeLeagueLoader { get; init; } = PobTradeSearch.ReadLeaguesAsync;
    private static readonly (string Key,string Label)[] TradeModes=[("securable","Instant buyout"),("available","Instant buyout and in person"),("onlineleague","In person (online in league)"),("online","In person (online)"),("any","Any (includes offline)")];
    private static readonly (string Key,string Label,string Help)[] TradePriorities=[
        ("balanced","Damage and defence","Uses PoB’s default weights: Full DPS × 1 and effective hit pool × 0.5."),
        ("damage","Damage","Weights modifiers by their effect on your build’s Full DPS."),
        ("defence","Defence","Weights modifiers by effective hit pool under your current configuration."),
        ("life","Life","Weights modifiers by their effect on maximum life."),
        ("energyshield","Energy shield","Weights modifiers by their effect on maximum energy shield."),
        ("custom","Custom weights","Uses your selected statistics and their relative importance for search and ranking.")];

    private bool HandleTradeAction(WidgetActionEvent action)
    {
        var id=action.ActionId;var parts=id.Split('.');
        if(HandleTradeOptions(action))return true;
        if(id=="trade")
        {
            tradeSlot=ItemSlot;ResetTradeCatalog();detailKind="trade";focusRequestSequence++;ReadTradePreferences();
            RefreshTradeLeagues();return true;
        }
        if(id=="trade-browse")
        {if(detailKind!="trade")tradeSlot=ItemSlot;ReadTradePreferences();Work(async token=>{await LoadTradeLeaguesAsync(token);await OpenWebsiteAsync(BrowseTradeUrl(),token);},"Opening trade website…");return true;}
        if(detailKind!="trade")return false;
        if(id=="trade-open"&&TradeSearchAvailable&&result is {} current)
        {
            var slot=tradeSlot;var priority=tradePriority;var league=tradeLeague;var price=tradeBudget;var currency=tradeCurrency;var mode=tradeMode;
            Work(async token=>await OpenWebsiteAsync(await CalculateTradeUrlAsync(current.Xml,slot,priority,league,price,currency,mode,token),token),"Calculating upgrade search…");
            return true;
        }
        if(id=="back"){detailKind="item";focusRequestSequence++;return true;}
        if(id=="trade-refresh"){RefreshTradeLeagues(force:true);return true;}
        if(id=="trade-budget")tradeBudget=action.CommittedText??"";
        else if(parts is["trade-league",var league]&&int.TryParse(league,out var li)&&li>=0&&li<tradeLeagues.Length)tradeLeague=tradeLeagues[li];
        else if(parts is["trade-mode",var mode]&&TradeModes.Any(m=>m.Key==mode))tradeMode=mode;
        else if(parts is["trade-priority",var priority]&&TradePriorities.Any(p=>p.Key==priority))tradePriority=priority;
        else if(parts is["trade-currency",var currency]&&currency is "divine"or"chaos"or"exalted")tradeCurrency=currency;
        else return false;
        Work(_=>{SaveTradePreferences();return Task.CompletedTask;},"Saving search preferences…");
        return true;
    }
    private string BrowseTradeUrl()=>PobTradeSearch.SearchUrl(game,tradeLeague,
        gameDefinition.CompressedSearch?JsonSerializer.SerializeToElement(new{status=new{option=tradeMode}}):JsonSerializer.SerializeToElement(new{query=new{status=new{option=tradeMode}}}));
    private ButtonElement BrowseTradeButton()=>Button("Browse trade website","trade-browse");
    private ButtonElement TradeSearchButton()=>Button("Search trade","trade-open").Disabled(!TradeSearchAvailable);
    private async Task<string> CalculateTradeUrlAsync(string xml,string slot,string priority,string league,string price,string currency,string mode,CancellationToken token)
    {
        var budget=0d;
        if(price.Length>0&&(!double.TryParse(price,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out budget)||!double.IsFinite(budget)||budget<0))throw new IOException("Enter a positive budget, or leave it empty for no price limit.");
        var request=new{op="trade-search",slot,priority,budget,currency,mode,weights=TradeWeights(),advanced=TradeAdvanced(),@base=tradeBase};
        var key=XmlHash(xml)+league+JsonSerializer.Serialize(request);
        if(key==cachedTradeKey&&cachedTradeUrl is not null)return cachedTradeUrl;
        var query=await engine.ExecuteAsync(request,token,xml);
        token.ThrowIfCancellationRequested();
        var url=PobTradeSearch.SearchUrl(game,league,query.State.GetProperty("query"),weighted:true);
        cachedTradeKey=key;cachedTradeUrl=url;return url;
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
            if(root.TryGetProperty("weights",out var weights)&&weights.ValueKind==JsonValueKind.Object)
            {tradeWeights.Clear();foreach(var w in weights.EnumerateObject().Take(20))if(int.TryParse(w.Name,out var id)&&w.Value.TryGetDouble(out var value)&&double.IsFinite(value)&&value>=0&&value<=100)tradeWeights[id]=value;}
            if(root.TryGetProperty("flags",out var flags)&&flags.ValueKind==JsonValueKind.Object)
            {tradeFlags.Clear();foreach(var f in flags.EnumerateObject())if(f.Name is "includeMirrored" or "includeCorrupted" or "includeRunes" or "includeTalisman" or "includeScourge" or "includeEldritch"&&f.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)tradeFlags[f.Name]=f.Value.GetBoolean();}
            if(root.TryGetProperty("numbers",out var numbers)&&numbers.ValueKind==JsonValueKind.Object)
            {tradeNumbers.Clear();foreach(var n in numbers.EnumerateObject())if(n.Name is "maxLevel" or "sockets" or "links"&&n.Value.TryGetInt32(out var value)&&value>=0&&value<=100)tradeNumbers[n.Name]=value;}
            if(root.TryGetProperty("batchLimit",out var batch)&&batch.TryGetInt32(out var count)&&count is 20 or 50 or 100)tradeBatchLimit=count;
            if(tradeLeaguesLoaded&&!tradeLeagues.Contains(tradeLeague,StringComparer.Ordinal))tradeLeague=tradeLeagues[0];
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException){ }
    }
    private void RefreshTradeLeagues(bool force=false)=>Work(async token=>
    {
        await ReadTradeCatalogAsync(token);
        await LoadTradeLeaguesAsync(token,force);
    },"Loading trade leagues…");
    private async Task LoadTradeLeaguesAsync(CancellationToken token,bool force=false)
    {
        // Reuse successful discovery for this widget instance; explicit refresh
        // or a fresh instance after unloading/restart fetches the list again.
        lock(gate)if(tradeLeaguesLoaded&&!force)
        {if(!tradeLeagues.Contains(tradeLeague,StringComparer.Ordinal))tradeLeague=tradeLeagues[0];return;}
        var leagues=await TradeLeagueLoader(game,token);
        token.ThrowIfCancellationRequested();
        if(leagues.Length==0)throw new IOException("No trade leagues were returned. Use Refresh leagues to retry.");
        lock(gate)
        {
            tradeLeagues=leagues.Distinct(StringComparer.Ordinal).ToArray();
            if(!tradeLeagues.Contains(tradeLeague,StringComparer.Ordinal))tradeLeague=tradeLeagues[0];
            tradeLeaguesLoaded=true;
        }
    }

    private WidgetElement TradeWorkspace()=>UI.Stack("pob.trade.page",ManagementHeader("Find equipment upgrades"),
        QuickControls(UI.Row("pob.trade.actions",new WidgetElement[]{TradeSearchButton(),BrowseTradeButton()}.Concat(websiteSession is not null||savedTradeUrl is not null?new[]{Button("Return to listings","website-resume")}:Array.Empty<WidgetElement>()).ToArray()).Classes("pob-toolbar"),TradeSearchAvailable?"pob.trade-open":"pob.trade-browse","pob.trade.scroll","Search options",true),
        QuickControls(UI.VerticalScroll("pob.trade.scroll",
            TradeSlotControl(),
            Text(!TradeCatalogReady?"Equipment options are not ready. Wait for loading to finish, or choose Refresh leagues to retry.":!TradeSlotAvailable
                ?"This equipment slot is inactive in the current calculation. Choose an active slot above; Search trade will not substitute another item."
                :"","trade.slot-status","pob-small pob-muted"),
            Text("Search for non-unique equipment that may improve this build. PoB calculates the filters; listings and trading stay on the official website.","trade.help","pob-muted"),
            Text("Trade opens in the overlay so you can compare listings beside your build. Sign in once; this widget remembers your login.","trade.website-help","pob-small pob-muted"),
            SelectWithUnavailable("League",tradeLeagues.Select((l,i)=>new SelectOption("league"+i,l,"trade-league."+i,l==tradeLeague)).ToArray(),"pob.trade-league","Load leagues to choose a league").Classes("pob-select"),
            UI.Select("Trade method",TradeModes.Select(m=>new SelectOption(m.Key,m.Label,"trade-mode."+m.Key,m.Key==tradeMode)).ToArray(),"pob.trade-mode").Classes("pob-select"),
            Text("Instant buyout lets you travel to the merchant and buy in game. In-person listings require contacting the seller.","trade.mode-help","pob-small"),
            UI.Select("Prioritize",TradePriorities.Select(p=>new SelectOption(p.Key,p.Label,"trade-priority."+p.Key,p.Key==tradePriority)).ToArray(),"pob.trade-priority").Classes("pob-select"),
            Text(TradePriorities.Single(p=>p.Key==tradePriority).Help,"trade.priority-help","pob-small pob-muted"),
            TradeOptionControls(),
            UI.Row("pob.trade.price",Field("Maximum price (optional)",UI.TextEntry(tradeBudget,"No price limit","trade-budget","pob.trade-budget",20).Classes("pob-input"),"trade-budget"),
                UI.Select("Currency",new[]{"divine","chaos","exalted"}.Select(c=>new SelectOption(c,char.ToUpperInvariant(c[0])+c[1..]+" Orb","trade-currency."+c,c==tradeCurrency)).ToArray(),"pob.trade-currency").Classes("pob-select")).Classes("pob-toolbar","pob-price-fields"),
            Text("Weights estimate an upgrade; they do not guarantee it. Compare an item before applying it.","trade.caution","pob-small pob-muted"),
            UI.Row("pob.trade.return",Button("Paste item to compare","item-paste"),Button("Refresh leagues","trade-refresh")).Classes("pob-toolbar"),
            Text(gameDefinition.ItemCopyHelp,"trade.paste-help","pob-small pob-muted"))
            .Classes("pob-scroll","pob-panel"),TradeCatalogReady?"pob.trade-slot":"pob.trade-league","pob.trade.actions","Search controls",true)).Classes("pob-workspace");
}
