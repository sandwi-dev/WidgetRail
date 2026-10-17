using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetSdk;

internal static class TradeImprovementTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var reader=new WebSessionPageActionBatchReader();var batchId=Guid.NewGuid().ToString();
        JsonElement Part(int index)=>JsonSerializer.SerializeToElement(new{batchId,index,total=2,context=new{url="https://example.com"},items=new[]{new{id=index}}});
        check(!reader.TryRead(Part(0),out _,out _)&&reader.TryRead(Part(1),out _,out var complete)&&complete.Length==2,"Batch reader returns only complete ordered batches");
        var rejected=false;try{reader.TryRead(Part(1),out _,out _);}catch(IOException){rejected=true;}
        check(rejected,"Missing first batch chunk is rejected");
        var prefix=$"https://www.pathofexile.com/trade{(game==2?"2":"")}/search/Standard/";
        check(PobWidget.SafeTradeReturnUrl(prefix+"abcd?token=secret&q=test#purchase",game)==prefix+"abcd?q=test"&&PobWidget.SafeTradeReturnUrl("https://www.pathofexile.com/login?token=secret",game)is null,"Return URL keeps public search only and excludes credentials and fragments");
        await using var engine=new PobEngine(root,runtime,adapter);
        var before=await engine.ExecuteAsync(new{op="load",xml,name="Trade improvements"},default);
        const string ring="Rarity: RARE\nTest Ring\nGold Ring\nImplicits: 0\n+25 to maximum Mana";
        var modifiers=(await engine.ExecuteAsync(new{op="item-modifiers",raw=ring},default,before.Xml)).State;
        check(modifiers.GetProperty("sources").EnumerateArray().Any(s=>s.GetProperty("key").GetString()=="PREFIX")&&modifiers.GetProperty("sources").EnumerateArray().Any(s=>s.GetProperty("key").GetString()=="SUFFIX"),"Imported item exposes upstream Prefix and Suffix sources");
        var choices=modifiers.GetProperty("choices").EnumerateArray().ToArray();
        check(choices.Length>0,"Predefined modifier catalogue includes numeric descriptions");
        var choice=choices.First(c=>c.GetProperty("description").GetString()!.Contains("maximum Life"));
        var edited=await engine.ExecuteAsync(new{op="item-draft",mode="modifier-add",raw=ring,source=modifiers.GetProperty("source").GetString(),id=choice.GetProperty("id").GetInt32(),modId=choice.GetProperty("key").GetString()},default,before.Xml);
        check(edited.State.GetProperty("authoring").GetProperty("raw").GetString()!.Contains("maximum Life")&&WorkStateTests.SameBuild(before.Xml,edited.Xml),"Adding a predefined modifier preserves draft-only state and its numeric description");
        check(edited.State.GetProperty("authoring").GetProperty("ranges").GetArrayLength()>0,"Predefined modifier ranges use upstream roll controls");
        var catalogue=(await engine.ExecuteAsync(new{op="trade-catalog",slot="Ring 1"},default,before.Xml)).State;
        var mana=catalogue.GetProperty("stats").EnumerateArray().First(s=>s.GetProperty("title").GetString()=="Mana").GetProperty("id").GetInt32();
        var weights=new[]{new{id=mana,weight=1.0}};
        var workingBefore=engine.WorkingSet;var timer=Stopwatch.StartNew();
        await engine.ExecuteAsync(new{op="trade-rank-begin",slot="Ring 1",priority="custom",weights},default,before.Xml);
        JsonElement last=default;
        for(var i=1;i<=100;i++)
        {
            last=(await engine.ExecuteAsync(new{op="trade-rank-item",raw=$"Rarity: RARE\nCandidate {i}\nGold Ring\nImplicits: 0\n+{i} to maximum Mana"},default,before.Xml)).State;
            if(last.TryGetProperty("error",out var failure))throw new IOException(failure.GetString());
            if(i==20)Console.WriteLine($"PERF {game}: 20 items {timer.Elapsed.TotalSeconds:F2}s, working set {engine.WorkingSet/1048576.0:F1} MiB");
        }
        var order=await engine.ExecuteAsync(new{op="trade-rank-finish"},default,before.Xml);
        check(order.State.GetProperty("order").GetArrayLength()==100&&order.State.GetProperty("order")[0].GetInt32()==100,"Upstream sorts 100 candidates with custom Mana weight, highest first");
        check(WorkStateTests.SameBuild(before.Xml,order.Xml),"Batch evaluation does not change build equipment or XML");
        Console.WriteLine($"PERF {game}: 100 items {timer.Elapsed.TotalSeconds:F2}s, working set before {workingBefore/1048576.0:F1} MiB, after {engine.WorkingSet/1048576.0:F1} MiB");
        var equipped=await engine.ExecuteAsync(new{op="item",slot="Ring 1",raw=last.GetProperty("raw").GetString()},default,before.Xml,compare:true);
        var manaAfter=equipped.State.GetProperty("stats").EnumerateArray().Single(s=>s.GetProperty("name").GetString()=="Mana").GetProperty("value").GetDouble();
        check(Math.Abs(last.GetProperty("changes").EnumerateArray().Single(c=>c.GetProperty("stat").GetString()=="Mana").GetProperty("after").GetDouble()-manaAfter)<.001,"Ranked change agrees with a full independent PoB equipment calculation");
        var empty=XDocument.Parse(before.Xml);foreach(var s in empty.Descendants("Slot").Where(s=>(string?)s.Attribute("name")=="Ring 2"))s.SetAttributeValue("itemId",0);
        var emptyBuild=await engine.ExecuteAsync(new{op="load",xml=empty.ToString(),name="Empty slot"},default);
        var emptyCatalog=(await engine.ExecuteAsync(new{op="trade-catalog",slot="Ring 2"},default,emptyBuild.Xml)).State;
        check(emptyCatalog.GetProperty("slots").EnumerateArray().Single(s=>s.GetProperty("name").GetString()=="Ring 2").GetProperty("empty").GetBoolean(),"Search catalogue names an empty target explicitly");
        var basis=emptyCatalog.GetProperty("bases")[0].GetProperty("base").GetString();
        var query=await engine.ExecuteAsync(new{op="trade-search",slot="Ring 2",priority="custom",weights,@base=basis,budget=10,currency="divine",advanced=new{includeMirrored=true,maxLevel=50}},default,emptyBuild.Xml);
        check(query.State.GetProperty("query").GetRawText().Contains("accessory.ring")&&WorkStateTests.SameBuild(emptyBuild.Xml,query.Xml),"Empty-slot weighted search uses detached category and leaves equipment empty");
        await engine.ExecuteAsync(new{op="trade-rank-begin",slot="Ring 1",priority="custom",weights},default,before.Xml);
        var bad=(await engine.ExecuteAsync(new{op="trade-rank-item",raw="not an item"},default,before.Xml)).State;
        var good=(await engine.ExecuteAsync(new{op="trade-rank-item",raw=ring},default,before.Xml)).State;
        check(bad.TryGetProperty("error",out _)&&good.GetProperty("index").GetInt32()==1,"Rejected candidates do not poison the next evaluation or its index");
        await engine.ExecuteAsync(new{op="trade-rank-finish"},default,before.Xml);
        using(var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            var cancelled=false;
            try
            {
                await engine.ExecuteAsync(new{op="trade-rank-begin",slot="Ring 1",priority="custom",weights},cancellation.Token,before.Xml);
                for(var i=0;i<100;i++)await engine.ExecuteAsync(new{op="trade-rank-item",raw=ring},cancellation.Token,before.Xml);
            }
            catch(OperationCanceledException){cancelled=true;}
            var restored=await engine.ExecuteAsync(new{op="snapshot"},default,before.Xml);
            check(cancelled&&WorkStateTests.SameBuild(before.Xml,restored.Xml),"Cancellation retires in-flight evaluation and restores the committed build safely");
        }
        await Task.Delay(300);
        using var process=Process.GetProcessById(engine.ProcessId!.Value);process.Refresh();var cpu=process.TotalProcessorTime;
        await Task.Delay(1200);process.Refresh();var idle=process.TotalProcessorTime-cpu;
        Console.WriteLine($"PERF {game}: idle CPU over 1.2s = {idle.TotalMilliseconds:F1}ms");
        check(idle.TotalMilliseconds<100,"Idle engine does not keep evaluating after completion or cancellation");
    }
}
