using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class UnifiedSearchTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var warningState=JsonSerializer.SerializeToElement(new{nodes=Array.Empty<object>(),skills=Array.Empty<object>(),warnings=new[]{"Ambiguous build warning"},items=new[]{new{id=7,itemId=91,slot="Helmet",name="Known item",@base="Hat",raw="",warnings=new[]{"Unmodelled exact modifier"}}}});
        var warningIndex=new PobUnifiedSearchCatalog(warningState,PobConfigurationCatalog.Empty,default,default,[]);
        var known=warningIndex.Entries.Single(e=>e.Title=="Unmodelled exact modifier");var unknown=warningIndex.Entries.Single(e=>e.Title=="Ambiguous build warning");
        check(known.Target.Kind==PobSearchTargetKind.EquippedItem&&known.Target.Id==7&&known.Target.Key=="91"&&unknown.Target.Kind==PobSearchTargetKind.Warning,
            "Warnings retain exact supplied equipment identity while ambiguous text has no inferred target");
        check(warningIndex.Match("exact modifier","Warnings").Single()==known,"Unified search matches all committed terms and preserves warning text");
        var directory=Path.Combine(Path.GetTempPath(),"pob-unified-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var build=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,build+".xml"),xml);File.WriteAllText(Path.Combine(directory,build+".name.txt"),"Search fixture");File.WriteAllText(Path.Combine(directory,"selected.txt"),build);
        await using var engine=new PobEngine(root,runtime,adapter);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        object? Get(string name)=>typeof(PobWidget).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget);
        void Set(string name,object? value)=>typeof(PobWidget).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,value);
        PobResult Result()=>(PobResult)Get("result")!;
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        var sequence=0;ViewSnapshot View()=>widget.Render().CreateSnapshot("unified-test",++sequence);
        async Task Until(Func<bool> ready){var timer=Stopwatch.StartNew();while(!ready()){if(timer.Elapsed.TotalSeconds>65)throw new TimeoutException("Unified search did not settle");await Task.Delay(15);}}
        async Task Idle()=>await Until(()=>!(bool)Get("busy")!);
        async Task Action(string action,string? text=null){await widget.OnActionAsync(new(action,"test"){CommittedText=text});await Idle();_=View();}
        PobSearchResult[] Results(){_=View();var query=Get("unifiedQuery")!;return (PobSearchResult[])query.GetType().GetProperty("Results")!.GetValue(query)!;}
        async Task Select(PobSearchTargetKind kind,Func<PobSearchResult,bool>? condition=null)
        {
            await Action("unified-search");var entries=Results();var index=Array.FindIndex(entries,e=>e.Target.Kind==kind&&(condition?.Invoke(e)??true));
            if(index<0)throw new InvalidOperationException("Missing search fixture kind "+kind);
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"unified-route",5000+sequence);
            using var range=await host.AcquireAsync("pob.unified.results",index,1);var row=range.Range.Items.Single();
            check(range.RouteAction(row.Key,ControllerButton.A)==WidgetOperationAdmission.Enqueued,"Search admits captured "+kind+" result");
            await Until(()=>!(bool)Get("unifiedSearchOpen")!);await Idle();_=View();
        }
        void ExportTarget(string name)
        {
            var output=Environment.GetEnvironmentVariable("POB_UNIFIED_SEARCH_FIXTURES");if(string.IsNullOrWhiteSpace(output))return;
            Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,$"poe{game}-search-target-{name}.json"),SnapshotJson.Serialize(View()));
        }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            var before=Result();var saved=File.ReadAllText(Path.Combine(directory,build+".xml"));
            await Action("unified-search");var cache=Get("unifiedCache");var entries=Results();
            foreach(var kind in new[]{PobSearchTargetKind.Passive,PobSearchTargetKind.Skill,PobSearchTargetKind.Gem,PobSearchTargetKind.EquippedItem,
                PobSearchTargetKind.InventoryItem,PobSearchTargetKind.Configuration,PobSearchTargetKind.Calculation,PobSearchTargetKind.SavedBuild,PobSearchTargetKind.Navigation})
                check(entries.Any(e=>e.Target.Kind==kind),"Unified index includes "+kind);
            check(ReferenceEquals(before,Result())&&File.ReadAllText(Path.Combine(directory,build+".xml"))==saved,"Opening search and loading catalogs leaves the working build and saved XML untouched");
            var field=Nodes(View().Root).Single(n=>n.Id=="pob.unified.search");
            var suggestion=new TextSuggestionQuery(field.Id,field.TextEntrySuggestions!.Key,Guid.NewGuid().ToString("N"),1,"life",6,160);
            var suggestions=await widget.OnSuggestTextAsync(suggestion,default);
            check(suggestions.Items.Count>0&&suggestions.Items.Count<=6&&ReferenceEquals(before,Result()),"Unified whole-entry suggestions use the loaded local index without changing the build");
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"unified-retired",3000))
            {
                using var lease=await host.AcquireAsync("pob.unified.results",0,1);var row=lease.Range.Items.Single();
                await Action("unified-search-text","no-such-build-entry-zzzz");host.PublishSnapshot();
                check(Results().Length==0&&lease.RouteAction(row.Key,ControllerButton.A) is null,"Changed query retires previous search result actions");
                await Action("unified-search-close");
                check((await widget.OnSuggestTextAsync(suggestion,default)).Unavailable,"Closing search retires its suggestion source");
            }
            await Action("unified-search");check(ReferenceEquals(cache,Get("unifiedCache")),"Repeated search opening reuses exact-build catalogs");
            using(var reopened=WidgetTestHost.CreateIndexedCollectionHost(widget,"unified-reopen",4000))
            {
                using var lease=await reopened.AcquireAsync("pob.unified.results",0,1);var row=lease.Range.Items.Single();
                await Action("unified-search-close");await Action("unified-search");reopened.PublishSnapshot();
                check(lease.RouteAction(row.Key,ControllerButton.A) is null,"Closing and reopening search retires old row callbacks even for the same cached query");
            }
            var fixture=Environment.GetEnvironmentVariable("POB_UNIFIED_SEARCH_FIXTURES");
            if(!string.IsNullOrWhiteSpace(fixture))
            {
                Directory.CreateDirectory(fixture);File.WriteAllBytes(Path.Combine(fixture,"poe"+game+"-search.json"),SnapshotJson.Serialize(View()));
                using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"unified-native",4500);
                using var range=await host.AcquireAsync("pob.unified.results",0,Math.Min(12,Results().Length));
                File.WriteAllText(Path.Combine(fixture,"poe"+game+"-search-rows.json"),JsonSerializer.Serialize(range.Range.Items.Select(item=>item.Root).ToArray()));
            }
            await Action("unified-search-close");
            await Select(PobSearchTargetKind.EquippedItem);
            ExportTarget("equipment");var equipped=View();check((string)Get("section")! == "Items"&&equipped.FocusGroupEntryRequest is {} itemFocus&&itemFocus.GroupId.StartsWith("pob.unified.",StringComparison.Ordinal)&&Nodes(equipped.Root).Any(n=>n.Id==itemFocus.GroupId&&n.InitialChildFocusId=="pob.item."+Get("detailId")),
                "Equipment result opens the correct item and authors a narrow focus group for that actual row");
            await Select(PobSearchTargetKind.Gem);
            ExportTarget("gem");var gemView=View();check((string)Get("section")! == "Skills"&&gemView.FocusGroupEntryRequest is {} gemFocus&&Nodes(gemView.Root).Any(n=>n.Id==gemFocus.GroupId&&n.InitialChildFocusId!.StartsWith("pob.gem.",StringComparison.Ordinal)),
                "Gem result identifies its group and focuses the actual gem field");
            await Select(PobSearchTargetKind.Configuration,e=>{var option=Result().Configuration.Options.Single(c=>c.Id==e.Target.Key);return option.Id!="customMods"&&option.Raw.GetProperty("kind").GetString()!="customMods"&&option.Raw.GetProperty("value").ToString().Length<=2048;});
            ExportTarget("configuration");var config=View();var focus=config.FocusGroupEntryRequest!;
            check((string)Get("section")! == "Configuration"&&Nodes(config.Root).Any(n=>n.Id==focus.GroupId&&n.InitialChildFocusId!.StartsWith("pob.condition.",StringComparison.Ordinal)),
                "Configuration result reaches its actual control without invoking it");
            await Select(PobSearchTargetKind.Calculation);
            check((string)Get("detailKind")! == "calculations"&&View().FocusGroupEntryRequest?.IndexedItem?.CollectionId=="pob.calculation.cells",
                "Calculation result opens upstream details and targets its exact indexed cell");
            await Select(PobSearchTargetKind.InventoryItem);
            check((string)Get("detailKind")! == "inventory"&&View().FocusGroupEntryRequest?.IndexedItem?.ItemKey==Get("inventoryId")!.ToString(),
                "Inventory result selects the correct item and restores an exact indexed focus target without equipping");
            await Select(PobSearchTargetKind.Passive);
            check((string)Get("section")! == "Tree"&&(string)Get("detailKind")! == "node"&&ReferenceEquals(before,Result()),"Passive result opens the exact node without allocating it; map selection uses the existing center-on-select message");
            await Select(PobSearchTargetKind.SavedBuild);
            check((string)Get("detailKind")! == "library"&&(string)Get("documentId")! == build&&View().FocusGroupEntryRequest?.IndexedItem?.ItemKey==build,
                "Saved-build result selects the library entry without replacing the currently open build");
            await Action("unified-search");await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Background);
            check(!(bool)Get("unifiedSearchOpen")!&&(await widget.OnSuggestTextAsync(suggestion,default)).Unavailable,"Lifecycle suspension closes search and revokes delayed completion authority");
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            Set("pendingBefore",before);await Action("unified-search");check(!(bool)Get("unifiedSearchOpen")!&&ReferenceEquals(Get("pendingBefore"),before),"Search cannot replace an unfinished equipment/tree comparison");Set("pendingBefore",null);
            Set("pastedItem","retained draft");await Select(PobSearchTargetKind.Navigation,e=>e.Target.Key=="notes");
            check((string)Get("pastedItem")! == "retained draft"&&ReferenceEquals(before,Result()),"Nonmutating navigation preserves unfinished item draft and exact build result");Set("pastedItem",null);
            var warningProjection=JsonNode.Parse(before.State.GetRawText())!.AsObject();
            warningProjection["mainSkillDisabled"]=true;warningProjection["mainSkillDisabledReason"]="Upstream disabled reason fixture";
            var mainGroup=before.State.GetProperty("skills").EnumerateArray().Single(g=>g.GetProperty("main").GetBoolean()).GetProperty("id").GetInt32();
            var disabledResult=new PobResult(JsonSerializer.SerializeToElement(warningProjection),before.Xml);Set("result",disabledResult);
            await Action("unified-warnings");
            check((string)Get("unifiedCategory")! == "Warnings"&&Results().Any(e=>e.Target.Kind==PobSearchTargetKind.Skill&&e.Target.Key=="main-disabled"&&e.Target.Id==mainGroup&&e.Title=="Upstream disabled reason fixture"),
                "Disabled main-skill reason appears in the warning index with the exact upstream group identity");
            await Action("unified-search-close");await Select(PobSearchTargetKind.Skill,e=>e.Target.Key=="main-disabled");
            check((string)Get("section")! == "Skills"&&(int)Get("detailId")! == mainGroup&&ReferenceEquals(disabledResult,Result()),
                "Main-skill warning navigates to the supplied group without enabling, replacing or recalculating it");
            Set("result",before);
            await Action("unified-search");Set("documentId","changed-document");await Action("unified-search-text","life");
            check(!(bool)Get("unifiedSearchOpen")!&&ReferenceEquals(before,Result()),"Document change rejects stale search instead of navigating another build");Set("documentId",build);
            check(File.ReadAllText(Path.Combine(directory,build+".xml"))==saved,"All search navigation leaves saved build bytes unchanged");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
