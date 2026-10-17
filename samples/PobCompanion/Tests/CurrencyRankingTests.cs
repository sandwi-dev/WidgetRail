using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class CurrencyRankingTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        const string divine="Metadata/Items/Currency/CurrencyModValues",chaos="Metadata/Items/Currency/CurrencyRerollRare";
        var statics=new{result=new[]{new{entries=new[]{new{id="divine",text="Divine Orb"},new{id="chaos",text="Chaos Orb"}}}}};
        var markets=new{markets=new[]{new{league="Standard",market_pair=new[]{chaos,divine},lowest_ratio=new Dictionary<string,int>{{chaos,100},{divine,1}},highest_stock=new Dictionary<string,int>{{chaos,10000},{divine,100}}}}};
        var reply=await engine.ExecuteAsync(new{op="currency-rates",realm=game==2?"poe2":"pc",league="Standard",@static=statics,markets},default,xml);
        var rates=reply.State.GetProperty("rates");
        check(rates.GetProperty("divine").GetDouble()==1&&Math.Abs(rates.GetProperty("chaos").GetDouble()-.01)<1e-10,"Upstream converts the public exchange digest to Divine rates");
        var rows=new[]{new{score=1.10,amount=100d,currency="chaos"},new{score=1.12,amount=10d,currency="divine"}};
        foreach(var mode in new[]{"strength","value","price"})
        {
            var sorted=await engine.ExecuteAsync(new{op="trade-rank-sort",mode,rates,rows},default,xml);
            check(sorted.State.GetProperty("order")[0].GetInt32()==(mode=="strength"?2:1),"Upstream order matches expected "+mode+" with mixed currencies");
            check(WorkStateTests.SameBuild(xml,sorted.Xml),"Sorting "+mode+" preserves the build");
        }
        var rejected=false;
        try{await engine.ExecuteAsync(new{op="trade-rank-sort",mode="value",rates=new{divine=1},rows},default,xml);}catch(IOException){rejected=true;}
        check(rejected,"Unknown exchange rate rejects price order instead of assigning zero price");
        rejected=false;
        try{await engine.ExecuteAsync(new{op="trade-rank-sort",mode="price",rates,rows=new[]{new{score=1,amount=0,currency="divine"}}},default,xml);}catch(IOException){rejected=true;}
        check(rejected,"Zero listing prices cannot enter price-aware sorting");
        var now=DateTimeOffset.UtcNow;
        var snapshot=new CurrencyRateSnapshot(game==2?"poe2":"pc","Standard",now,now.AddHours(-1),new(){{"divine",1}});
        check(snapshot.Valid(snapshot.Realm,"Standard",now)&&!snapshot.Valid(snapshot.Realm,"Hardcore",now),"Rate cache is isolated by league");
        if(Environment.GetEnvironmentVariable("POB_CURRENCY_LIVE")=="1")
        {
            var cache=Path.Combine(Path.GetTempPath(),"pob-live-rates-"+game);Directory.CreateDirectory(cache);
            var live=await PobCurrencyRates.ReadAsync(game,snapshot.Realm,"Standard",cache,engine,xml,default);
            var cached=await PobCurrencyRates.ReadAsync(game,snapshot.Realm,"Standard",cache,engine,xml,default);
            Console.WriteLine($"LIVE currency rates: {live.Rates.Count}, market hour {live.MarketHour:O}, fetched {live.FetchedAt:O}");
            check(live.Rates.Count>1&&live.FetchedAt==cached.FetchedAt,"Public GGG data converts through PoB and the next read reuses persisted rates");
        }
        var context=PobWidget.ListingMarket("https://www.pathofexile.com/trade2/search/poe2/Rise%20of%20the%20Abyssal/id",2);
        check(context==("poe2","Rise of the Abyssal"),"Price ranking reads the actual listing league instead of stale search preferences");
    }
}
