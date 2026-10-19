using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SimilarWorkflowTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var raw="Rarity: RARE\nGuide helmet\n"+(game==1?"Iron Hat":"Golden Visage")+"\nImplicits: 0\n+90 to maximum Life\n+35% to Fire Resistance";
        var seeded=await engine.ExecuteAsync(new{op="item",slot="Helmet",raw},default,xml);
        var directory=Path.Combine(Path.GetTempPath(),"pob-similar-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var ids=Enumerable.Range(0,24).Select(_=>Guid.NewGuid().ToString("N")).ToArray();
        foreach(var id in ids){File.WriteAllText(Path.Combine(directory,id+".xml"),seeded.Xml);File.WriteAllText(Path.Combine(directory,id+".name.txt"),"Build "+Array.IndexOf(ids,id));}
        File.WriteAllText(Path.Combine(directory,"selected.txt"),ids[0]);
        var leagueReads=0;var opened=new List<WebSessionDefinition>();
        var widget=new PobWidget(game,engine,directory){TradeLeagueLoader=(_,_)=>{leagueReads++;return Task.FromResult(new[]{"Newest league","Standard"});}};
        WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().WithHandler(WidgetWebSessionCapabilities.Command,async(command,token)=>
        {
            if(command.Operation is WebSessionOperation.Open or WebSessionOperation.OpenAdapted){opened.Add(command.Definition!);return new WebSessionReply{Session=new(new string('b',32))};}
            if(command.Operation==WebSessionOperation.ReadMessages)await Task.Delay(60,token);
            return new WebSessionReply();
        }).Build());
        object? Get(string name)=>typeof(PobWidget).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget);
        void Set(string name,object? value)=>typeof(PobWidget).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,value);
        PobResult Result()=>(PobResult)Get("result")!;
        var sequence=0;
        ViewSnapshot View(){try{return widget.Render().CreateSnapshot("similar-workflow",++sequence);}catch(ProtocolValidationException error){Console.WriteLine(JsonSerializer.Serialize(error.Errors));throw;}}
        async Task Idle(){var deadline=Environment.TickCount64+65000;while((bool)Get("busy")!){if(Environment.TickCount64>deadline)throw new TimeoutException();await Task.Delay(15);}_=View();}
        async Task Act(string id,string? value=null){await widget.OnActionAsync(new(id,"test"){CommittedText=value});await Idle();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            var recent=Nodes(View().Root).Single(n=>n.Id=="pob.recent-builds");
            check(recent.SelectOptions.Count==21&&recent.SelectOptions.Single(o=>o.IsSelected).Id==ids[0],"Header shows current build, at most 20 builds and the library route");
            var next=recent.SelectOptions.First(o=>!o.IsSelected&&o.Id!="library");
            await Act(next.ActionId);
            check((string)Get("documentId")! ==next.Id&&Nodes(View().Root).Single(n=>n.Id=="pob.recent-builds").SelectOptions[0].Id==next.Id,"Selecting a recent build opens it and promotes it to first position");
            check(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(directory,"recent-builds.json")))![0]==next.Id,"Recent-build order persists after successful switching");
            // Re-read persisted metadata as a restarted widget would, without loading all builds.
            File.WriteAllText(Path.Combine(directory,ids[0]+".name.txt"),"Renamed saved build");
            var removed=recent.SelectOptions.First(option=>option.Id!=next.Id&&option.Id!=ids[0]&&option.Id!="library").Id;
            File.Delete(Path.Combine(directory,removed+".xml"));
            Set("recentBuildsLoaded",false);
            typeof(PobWidget).GetMethod("RefreshLibrary",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(widget,null);
            recent=Nodes(View().Root).Single(n=>n.Id=="pob.recent-builds");
            check(recent.SelectOptions[0].Id==next.Id&&recent.SelectOptions.All(option=>option.Id!=removed),"Reloaded recent history preserves order and drops missing builds");
            check(recent.SelectOptions.Any(option=>option.Id==ids[0]&&option.Label=="Renamed saved build"),"Recent entries use current saved names rather than stale history labels");
            await Act("section.3");
            var before=Result();var history=((System.Collections.ICollection)Get("undo")!).Count;
            await Act("similar-start.equipped");
            check((string)Get("detailKind")! =="similar-search"&&View().FocusGroupEntryRequest?.GroupId=="pob.similar.tools","Equipped item opens Find similar with valid initial focus");
            var generation=(long)Get("similarGeneration")!;
            var prefix="similar."+generation+".";
            var mod=Nodes(View().Root).First(n=>n.Id.StartsWith("pob.similar.label.mod")&&n.Text!.Contains("maximum Life"));
            var key=mod.Id["pob.similar.label.".Length..];
            await Act(prefix+"toggle."+key);await Act(prefix+"min."+key,"65");
            await Act(prefix+"max."+key,"40");await Act(prefix+"search");
            check(opened.Count==0,"Reversed bounds cannot open a broken trade search");
            await Act(prefix+"max."+key,"");
            check(ReferenceEquals(before,Result())&&history==((System.Collections.ICollection)Get("undo")!).Count,"Filter edits do not alter build or Undo history");
            await Act(prefix+"search");
            check(opened.Count==1&&(bool)Get("websiteVisible")!,"Search opens the existing adapted browser session");
            var query=SimilarSearchTests.Decode((string)Get("websiteUrl")!,game);var actual=query.TryGetProperty("query",out var nested)?nested:query;
            check(actual.GetProperty("stats")[0].GetProperty("filters")[0].GetProperty("value").GetProperty("min").GetDouble()==65,"Browser receives the user's chosen modifier bound");
            await Act("website-close");
            if(Environment.GetEnvironmentVariable("POB_SIMILAR_FIXTURES") is {Length:>0} output)
            {Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,$"poe{game}-similar.json"),SnapshotJson.Serialize(View()));}
            await Act("back");
            check((string)Get("detailKind")! =="item"&&View().FocusGroupEntryRequest?.GroupId=="pob.similar.entry.equipped","Back restores the equipped-item opener");
            await Act("similar-start.equipped");
            await Act(prefix+"toggle."+key);
            check(leagueReads==1&&!Nodes(View().Root).Single(n=>n.Id=="pob.similar.toggle."+key).IsSelected.GetValueOrDefault(),"Reopening reuses leagues and ignores retired form actions");
            await Act("back");await Act("inventory");await Act("similar-start.inventory");
            check((string)Get("detailKind")! =="similar-search","Stored inventory offers the same workflow");
            await Act("back");await Act("inventory-edit");await Act("similar-start.draft");
            check((string)Get("detailKind")! =="similar-search","Item editor offers the same workflow without saving a new draft");
            await Act("back");
            var source=Result();
            var entry=new PobBuildComparisonEntry("Helmet","Helmet",raw,"Guide helmet", "Guide helmet");
            var context=new PobBuildComparisonContext("Guide","Default","Skill","Class","1","Tree","Items","Skills","Config","Set 1","tree","engine","skill","");
            var projection=new PobBuildProjection(context,new Dictionary<PobBuildComparisonCategory,IReadOnlyList<PobBuildComparisonEntry>>(),0);
            Set("buildComparison",new PobBuildComparison("test",projection,projection,[new(PobBuildComparisonCategory.Equipment,"Helmet","Helmet",entry,entry)],[]));
            Set("buildComparisonSource",source);Set("buildComparisonDocument",Get("documentId"));Set("buildComparisonDetail",0);Set("detailKind","build-comparison");Set("similarReturnGroup",null);
            check(Nodes(View().Root).Any(n=>n.Id=="pob.similar-start.other"),"Guide comparison exposes Find similar on its reference item");
            await Act("similar-start.other");
            check(ReferenceEquals(source,Result())&&(string)Get("tradeSlot")! =="Helmet","Guide search preserves the player's build and chooses its matching comparison slot");
            await Act("back");
            check((string)Get("detailKind")! =="build-comparison"&&View().FocusGroupEntryRequest?.GroupId=="pob.similar.entry.other","Guide search Back returns to its original comparison detail");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
}
