using System.IO.Compression;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;

internal static class SimilarSearchTests
{
    internal static async Task RunAsync(int game, string root, string runtime, string adapter, string xml, Action<bool,string> check)
    {
        await using var engine = new PobEngine(root,runtime,adapter);
        var loaded = await engine.ExecuteAsync(new { op="load", xml, name="Similar search" },default);
        var raw="Rarity: RARE\nGuide helmet\n"+(game==1?"Iron Hat":"Golden Visage")+"\nImplicits: 0\n+90 to maximum Life\n+35% to Fire Resistance";
        var catalog=await engine.ExecuteAsync(new {op="similar-catalog",raw,league="Standard"},default,loaded.Xml);
        var fields=catalog.State.GetProperty("fields").EnumerateArray().ToArray();
        check(fields.Any(f=>f.GetProperty("title").GetString()!.Contains("maximum Life")),"Upstream exposes the guide item's life modifier");
        check(fields.Where(f=>f.GetProperty("toggle").GetBoolean()).All(f=>!f.GetProperty("enabled").GetBoolean()),"Modifiers remain opt-in rather than silently mandatory");
        var life=fields.Single(f=>f.GetProperty("title").GetString()!.Contains("maximum Life"));
        var key=life.GetProperty("key").GetString()!;
        var options=new Dictionary<string,object>{{key,new{enabled=true,minimum="65",maximum=""}}};
        var response=await engine.ExecuteAsync(new {op="similar-search",raw,league="Standard",mode=2,options},default,loaded.Xml);
        var url=PobTradeSearch.SearchUrl(game,"Standard",response.State.GetProperty("query"));
        check(PobWidget.SafeTradeReturnUrl(url,game)==url,"Upstream URL uses the existing official trade return flow");
        var query=Decode(url,game);
        var actual=query.TryGetProperty("query",out var nested)?nested:query;
        var filters=actual.GetProperty("stats")[0].GetProperty("filters");
        check(filters.GetArrayLength()==1&&filters[0].GetProperty("value").GetProperty("min").GetDouble()==65,"Only the selected modifier is required with the relaxed roll");
        check(actual.GetProperty("status").GetProperty("option").GetString()=="available","Listing status comes from upstream");
        check(!actual.TryGetProperty("type",out _),"Specific base remains optional");
        check(WorkStateTests.SameBuild(loaded.Xml,response.Xml),"Guide search does not equip its reference or change build XML");
        options["base"]=new{enabled=true};
        response=await engine.ExecuteAsync(new {op="similar-search",raw,league="Standard",options},default,loaded.Xml);
        query=response.State.GetProperty("query");actual=query.TryGetProperty("query",out nested)?nested:query;
        check(actual.GetProperty("type").GetString()==(game==1?"Iron Hat":"Golden Visage"),"Same-base option uses the reference base");
        foreach(var bad in new[]{new Dictionary<string,object>{{key,new{enabled=true,minimum="90",maximum="65"}}},new Dictionary<string,object>{{"forged",new{enabled=true}}}})
        {
            try { await engine.ExecuteAsync(new {op="similar-search",raw,league="Standard",options=bad},default,loaded.Xml);check(false,"Invalid search controls rejected"); }
            catch(IOException){check(true,"Invalid search controls rejected");}
        }
        response=await engine.ExecuteAsync(new {op="similar-search",raw,league="Standard",options},default,loaded.Xml);
        check(response.State.GetProperty("query").ValueKind==JsonValueKind.Object,"Failed query restores upstream hooks for subsequent searches");
    }
    internal static JsonElement Decode(string url,int game)
    {
        string json;
        if(game==1)json=Uri.UnescapeDataString(new Uri(url).Query[3..]);
        else
        {
            var encoded=new Uri(url).Segments.Last().Replace('-','+').Replace('_','/');
            using var input=new MemoryStream(Convert.FromBase64String(encoded));
            using var zip=new GZipStream(input,CompressionMode.Decompress);using var reader=new StreamReader(zip);json=reader.ReadToEnd();
        }
        using var document=JsonDocument.Parse(json);return document.RootElement.Clone();
    }
}
