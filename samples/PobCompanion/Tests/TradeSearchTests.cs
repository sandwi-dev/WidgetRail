using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class TradeSearchTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var loaded=await engine.ExecuteAsync(new{op="load",xml,name="Trade search"},default);
        if(game==1)loaded=await engine.ExecuteAsync(new{op="item",slot="Helmet",raw="Rarity: RARE\nTrade Test\nIron Hat\nImplicits: 0\n+50 to maximum Life"},default,loaded.Xml);
        var result=await engine.ExecuteAsync(new{op="trade-search",slot="Helmet",priority=Environment.GetEnvironmentVariable("POB_TRADE_PRIORITY")??"life",budget=10,currency="divine"},default,loaded.Xml);
        var raw=result.State.GetProperty("query");var query=raw.TryGetProperty("query",out var nested)?nested:raw;
        check(query.GetProperty("stats").EnumerateArray().Any(g=>g.GetProperty("type").GetString()=="weight"&&g.GetProperty("filters").GetArrayLength()>0),"Upstream generates weighted item filters");
        check(query.GetProperty("filters").GetProperty("trade_filters").GetProperty("filters").GetProperty("price").GetProperty("max").GetInt32()==10,"Search honors the selected budget");
        check(WorkStateTests.SameBuild(loaded.Xml,result.Xml),"Generating a trade query does not change equipment or settings");
        var output=Environment.GetEnvironmentVariable("POB_TRADE_QUERY_OUTPUT");if(output is not null)File.WriteAllText(output,raw.GetRawText());
        Console.WriteLine("Query characters: "+raw.GetRawText().Length);
        Console.WriteLine("URL characters: "+PobTradeSearch.SearchUrl(game,"Standard",raw).Length);
    }
}
