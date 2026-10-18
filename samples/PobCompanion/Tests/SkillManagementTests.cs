using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SkillManagementTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var directory=Path.Combine(Path.GetTempPath(),"pob-groups-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var id=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,id+".xml"),xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        object? Get(string name)=>typeof(PobWidget).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget);
        PobResult Result()=>(PobResult)Get("result")!;
        JsonElement[] Groups()=>Result().State.GetProperty("skills").EnumerateArray().ToArray();
        JsonElement Group()=>Groups().Single(g=>g.GetProperty("id").GetInt32()==(int)Get("detailId")!);
        static string Identity(JsonElement group)=>group.GetProperty("title").GetString()+"|"+group.GetProperty("slot").GetString();
        string Main()=>Identity(Groups().Single(g=>g.GetProperty("main").GetBoolean()));
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat((node.Children??[]).SelectMany(Nodes));
        async Task Until(Func<bool> condition){var timer=Stopwatch.StartNew();while(!condition()){if(timer.Elapsed.TotalSeconds>8)throw new TimeoutException("Gem inspection did not settle");await Task.Delay(10);}}
        var sequence=0;
        ViewSnapshot View(){try{return widget.Render().CreateSnapshot("group-management",++sequence);}catch(ProtocolValidationException e){Console.WriteLine(JsonSerializer.Serialize(e.Errors));throw;}}
        async Task Idle(){var timer=Stopwatch.StartNew();while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(timer.Elapsed.TotalSeconds>65)throw new TimeoutException();await Task.Delay(25);}}
        async Task Action(string action,string? value=null){await widget.OnActionAsync(new(action,"fixture"){CommittedText=value});await Idle();if(Get("feedback") is not null)Console.WriteLine("Feedback after "+action);}
        async Task PickIndexedGem(int catalogIndex,bool inspect)
        {
            _=View();
            var query=Get("gemQuery")!;
            var entries=(Array)query.GetType().GetProperty("Entries")!.GetValue(query)!;
            var index=Enumerable.Range(0,entries.Length).Single(i=>(string)entries.GetValue(i)!.GetType().GetProperty("Action")!.GetValue(entries.GetValue(i))! == "gem-pick."+catalogIndex);
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"gem-apply-test",900);
            using var lease=await host.AcquireAsync("pob.gems",index,1);
            var row=lease.Range.Items.Single();var before=Result();
            check(lease.RouteAction(row.Key,inspect?ControllerButton.X:ControllerButton.A)==WidgetOperationAdmission.Enqueued,
                inspect?"Inspect admission captures the chosen indexed gem":"A still chooses directly from the indexed gem row");
            if(inspect)
            {
                await Until(()=>Get("gemInspection") is not null);
                check(ReferenceEquals(Result(),before),"Inspecting a candidate performs no engine edit");
                var apply=Nodes(View().Root).Single(n=>n.ActionId?.StartsWith("gem-inspection-apply.",StringComparison.Ordinal)==true).ActionId!;
                await Action(apply);
                check(Get("gemInspection") is null&&(string)Get("detailKind")! == "skill","Explicit inspector apply returns to inline group editing");
            }
            else {await Until(()=>!ReferenceEquals(Result(),before));await Idle();}
        }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();await Action("section.2");
            var count=Groups().Length;var main=Main();
            await Action("group-create");
            check(Groups().Length==count+1&&Group().GetProperty("gems").GetArrayLength()==0,"New group is empty, selected and saved");
            check(Main()==main,"Creating a group preserves the main calculation skill");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.group-name")&&Nodes(View().Root).Any(n=>n.Id=="pob.gem-add"),"New group immediately offers label and Add gem controls");
            await Action("group-name","Controller group");check(Group().GetProperty("title").GetString()=="Controller group","Rename applies the authored label");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.inventory.rows"),"Creating and renaming retain the group rail");
            await Action("group-manage");
            if(Result().State.GetProperty("skillWeaponSets").GetBoolean())
            {
                check(!Nodes(View().Root).Any(n=>n.Id=="pob.group-slot")&&Nodes(View().Root).Count(n=>n.Id.StartsWith("pob.group-weapon-set."))==2,"Beta exposes weapon sets instead of removed equipment slot control");
                await Action("group-weapon-set.1");
                check(!Group().GetProperty("weaponSets")[0].GetProperty("enabled").GetBoolean()&&Group().GetProperty("weaponSets")[1].GetProperty("enabled").GetBoolean(),"Weapon set toggle uses upstream callback");
                await Action("group-weapon-set.2");
                check(Group().GetProperty("weaponSets")[1].GetProperty("enabled").GetBoolean(),"Upstream prevents disabling both weapon sets");
                await Action("undo");await Action("group-weapon-set.1");
            }
            var slots=Result().State.GetProperty("skillSlots").EnumerateArray().ToArray();
            var equipmentSlot=Array.FindIndex(slots,s=>s.GetProperty("name").GetString()=="Weapon 1");
            if(equipmentSlot>=0){await Action("group-slot."+equipmentSlot);check(Group().GetProperty("slot").GetString()=="Weapon 1","Equipment assignment uses the upstream slot selector");}
            await Action("back");
            await Action("gem-add");
            var catalog=(JsonElement[])Get("gemCatalog")!;
            check(catalog.Length>50&&Nodes(View().Root).Any(n=>n.Id=="pob.gems"&&n.IndexedCollection is not null),"Gem picker exposes the upstream catalog as a virtualized list");
            var suggestionField=Nodes(View().Root).Single(n=>n.Id=="pob.gem-search");
            var suggestionQuery=new TextSuggestionQuery(suggestionField.Id,suggestionField.TextEntrySuggestions!.Key,Guid.NewGuid().ToString("N"),1,"fire",6,100);
            var xmlBeforeSuggestions=Result().Xml;
            var suggested=await widget.OnSuggestTextAsync(suggestionQuery,default);
            check(suggested.Items.Any(i=>i.Value=="Fireball")&&suggested.Items.Count<=6,"Live gem suggestions use real upstream names and bounded whole-entry matches");
            check(Result().Xml==xmlBeforeSuggestions&&(string)Get("gemSearch")! == "","Typing suggestions does not mutate build XML or committed gem search");
            await Action("gem-type.support");
            check((await widget.OnSuggestTextAsync(suggestionQuery,default)).Unavailable,"Changing the picker filter retires the old suggestion source");
            suggestionField=Nodes(View().Root).Single(n=>n.Id=="pob.gem-search");
            suggested=await widget.OnSuggestTextAsync(suggestionQuery with{SourceKey=suggestionField.TextEntrySuggestions!.Key,Text="a",Revision=2},default);
            check(suggested.Items.Count>0&&suggested.Items.All(i=>i.Description?.StartsWith("Support gem",StringComparison.Ordinal)==true),"Suggestion names respect the current support-only catalog");
            await Action("gem-type.all");
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"gem-catalog-test",500))
            {
                using var first=await host.AcquireAsync("pob.gems",0,12);
                var visible=Nodes(host.CurrentSnapshot.Root).Single(n=>n.Id=="pob.gems").IndexedCollection!.Count;
                using var later=await host.AcquireAsync("pob.gems",visible/2,12);
                check(first.Range.Items.Count==12&&later.Range.Items.Count==12,"Gem identities materialize into valid native rows at the start and middle of the catalog");
            }
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"gem-inspection-test",700))
            {
                using var lease=await host.AcquireAsync("pob.gems",0,3);
                var row=lease.Range.Items[1];var before=Result();
                var queryBefore=Get("gemQuery");var sourceBefore=Nodes(View().Root).Single(n=>n.Id=="pob.gems").IndexedCollection!;
                check(row.Root.Kind==ViewNodeKind.ActionSurface&&row.Root.ActionId=="choose"&&row.Root.Shortcuts.Any(s=>s.Button==ControllerButton.X&&s.ActionId=="inspect")&&
                    row.Root.ContextActions.Any(a=>a.ActionId=="inspect"),"Gem rows retain A choose and expose local X Inspect and context inspection");
                check(row.Root.FocusPresentation is not null&&Nodes(View().Root).Single(n=>n.Id=="pob.gem-preview").FocusPreview is not null,
                    "Gem picker adopts shared host passive previews without replacing indexed navigation");
                check(Nodes(row.Root.FocusPresentation!).All(n=>!n.IsFocusable&&n.ActionId is null),"Gem preview has no action or focus authority");
                check(lease.RouteAction(row.Key,ControllerButton.X)==WidgetOperationAdmission.Enqueued,"Indexed X opens the exact captured gem");
                await Until(()=>Get("gemInspection") is not null);
                var opened=View();var anchor=opened.Root.AnchoredPresentation!.Anchor.IndexedItem!;
                check(anchor.CollectionId=="pob.gems"&&anchor.ItemKey==row.Key&&anchor.Index==1&&anchor.QueryGeneration==sourceBefore.QueryGeneration,
                    "Gem inspection anchors to exact indexed occurrence, not an index or stale display name");
                check(ReferenceEquals(Result(),before)&&ReferenceEquals(queryBefore,Get("gemQuery"))&&Group().GetProperty("gems").GetArrayLength()==0,
                    "Opening inspection leaves engine result, saved build and picker query unchanged");
                var expectedContext=PobInspectionContext.Read(before.State,before.Xml,Group()).Summary;
                check(expectedContext.Length==0?!Nodes(opened.Root).Any(n=>n.Id=="pob.gem-inspection.context"):
                    Nodes(opened.Root).Any(n=>n.Id=="pob.gem-inspection.context"&&n.Text==expectedContext),
                    "Gem inspection presents active loadout and calculation weapon context separately from the group's weapon eligibility");
                var close=Nodes(opened.Root).First(n=>n.ActionId?.StartsWith("gem-inspection-close.",StringComparison.Ordinal)==true).ActionId!;
                var apply=Nodes(opened.Root).First(n=>n.ActionId?.StartsWith("gem-inspection-apply.",StringComparison.Ordinal)==true).ActionId!;
                var fixtureDirectory=Environment.GetEnvironmentVariable("POB_GEM_INSPECTION_FIXTURES");
                if(!string.IsNullOrWhiteSpace(fixtureDirectory))
                {
                    Directory.CreateDirectory(fixtureDirectory);
                    File.WriteAllBytes(Path.Combine(fixtureDirectory,"poe"+game+"-gem-inspection.json"),SnapshotJson.Serialize(opened));
                    File.WriteAllText(Path.Combine(fixtureDirectory,"poe"+game+"-gem-row.json"),JsonSerializer.Serialize(row.Root));
                    File.WriteAllBytes(Path.Combine(fixtureDirectory,"poe"+game+"-gem-picker.json"),SnapshotJson.Serialize(host.CurrentSnapshot));
                }
                await Action("gem-search","unexpected background change");
                check((string)Get("gemSearch")! == ""&&Get("gemInspection") is not null,"Inspection blocks background picker actions");
                await Action(close);
                check(View().Root.Kind!=ViewNodeKind.ModalLayer&&ReferenceEquals(queryBefore,Get("gemQuery"))&&
                    Nodes(View().Root).Single(n=>n.Id=="pob.gems").IndexedCollection!.QueryGeneration==sourceBefore.QueryGeneration,
                    "Closing inspection returns to the same search, indexed query and scroll ownership");
                await Action(apply);check(ReferenceEquals(Result(),before),"Closed inspector apply callbacks cannot mutate the build");
                host.PublishSnapshot();
                check(lease.RouteAction(row.Key,ControllerButton.A,contextActionOwnerId:row.Root.Id,contextActionId:"inspect")==WidgetOperationAdmission.Enqueued,
                    "Mouse context inspection uses the same candidate action");
                await Until(()=>Get("gemInspection") is not null);
                var reopened=View();var currentApply=Nodes(reopened.Root).First(n=>n.ActionId?.StartsWith("gem-inspection-apply.",StringComparison.Ordinal)==true).ActionId!;
                await Action(apply);check(ReferenceEquals(Result(),before)&&Get("gemInspection") is not null,"Superseded inspection token cannot apply a newer panel");
                typeof(PobWidget).GetField("pickerGem",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,99);
                await Action(currentApply);
                check(ReferenceEquals(Result(),before)&&Get("gemInspection") is null,"Changed target context rejects captured apply without a build mutation");
                typeof(PobWidget).GetField("pickerGem",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,0);
                host.PublishSnapshot();
                using(var currentLease=await host.AcquireAsync("pob.gems",0,1))
                {
                    var current=currentLease.Range.Items.Single();
                    check(currentLease.RouteAction(current.Key,ControllerButton.X)==WidgetOperationAdmission.Enqueued,"Fresh query can inspect after retiring a stale target");
                    await Until(()=>Get("gemInspection") is not null);
                    var buildAction=Nodes(View().Root).Single(n=>n.ActionId?.StartsWith("gem-inspection-apply.",StringComparison.Ordinal)==true).ActionId!;
                    typeof(PobWidget).GetField("documentId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,"another-build");
                    await Action(buildAction);
                    check(ReferenceEquals(Result(),before)&&Get("gemInspection") is null,"Build identity change rejects captured inspector apply");
                    typeof(PobWidget).GetField("documentId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,id);
                }
                host.PublishSnapshot();
                await Action("gem-search","not-present-in-catalog");host.PublishSnapshot();
                check(lease.RouteAction(row.Key,ControllerButton.X) is null&&Get("gemInspection") is null,"Retired query cannot reopen a stale gem inspector");
                await Action("gem-search","");
            }
            var oldQuery=Get("gemQuery");
            var equalReplacement=Result() with { };
            typeof(PobWidget).GetField("result",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,equalReplacement);
            _=View();
            check(!ReferenceEquals(oldQuery,Get("gemQuery")),"Equal-valued replacement builds retire the old gem query identity");
            check(catalog.Any(g=>g.GetProperty("tagString").GetString()!.Contains("Projectile",StringComparison.OrdinalIgnoreCase)),"Catalog carries upstream human-readable gem tags");
            await Action("gem-search","projectile");
            var expectedTags=catalog.Where(g=>g.GetProperty("name").GetString()!.Contains("projectile",StringComparison.OrdinalIgnoreCase)||
                g.GetProperty("tagString").GetString()!.Contains("projectile",StringComparison.OrdinalIgnoreCase)||
                g.GetProperty("searchTags").GetString()!.Contains("projectile",StringComparison.OrdinalIgnoreCase)).ToArray();
            check(expectedTags.Length>0&&Nodes(View().Root).Single(n=>n.Id=="pob.gems").IndexedCollection!.Count==expectedTags.Length,
                "Tag search retains precisely the eligible upstream catalog matches");
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"gem-tags-test",500))
            {
                using var range=await host.AcquireAsync("pob.gems",0,Math.Min(12,expectedTags.Length));
                var labels=range.Range.Items.SelectMany(item=>Nodes(item.Root)).Where(n=>n.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface).Select(n=>n.Text??n.AccessibilityLabel).ToArray();
                check(labels.Select((label,i)=>label!.StartsWith(expectedTags[i].GetProperty("name").GetString()+" ·",StringComparison.Ordinal)).All(ok=>ok),
                    "Tag filtering retains upstream order and indexed gem identity");
            }
            suggestionField=Nodes(View().Root).Single(n=>n.Id=="pob.gem-search");
            suggested=await widget.OnSuggestTextAsync(suggestionQuery with{SourceKey=suggestionField.TextEntrySuggestions!.Key,Text="AoE",Revision=3},default);
            var tagsExplained=suggested.Items.Count>0&&suggested.Items.Any(i=>!i.Value.Contains("AoE",StringComparison.OrdinalIgnoreCase)&&i.Description!.Contains("AoE",StringComparison.OrdinalIgnoreCase));
            if(!tagsExplained)Console.WriteLine("Tag suggestions: "+JsonSerializer.Serialize(suggested));
            check(tagsExplained,
                "Tag-only suggestions insert a complete gem name and explain its upstream tags");
            check(Result().Xml==xmlBeforeSuggestions,"Tag browsing and suggestions leave the saved build unchanged");
            var optionalTags=new PobGemSearchCatalog([JsonSerializer.SerializeToElement(new{name="Fixture",support=false})]);
            check(optionalTags.Entries.Single().Matches("fixture")&&!optionalTags.Entries.Single().Matches("projectile"),"Absent optional tags retain name search without invented metadata");
            var rankedSearch=new PobGemSearchCatalog([
                JsonSerializer.SerializeToElement(new{name="Z first",support=true,tagString="Projectile"}),
                JsonSerializer.SerializeToElement(new{name="A second",support=true,tagString="Projectile"})]);
            check(rankedSearch.Entries.Where(e=>e.Matches("projectile")).Select(e=>e.Index).SequenceEqual(new[]{0,1}),
                "Gem search never alphabetizes or recomputes an upstream ranked catalog");
            foreach(var invalid in new[]{"{\"name\":\"Fixture\",\"support\":false,\"tagString\":3}","{\"name\":\"Fixture\",\"support\":false,\"searchTags\":null}"})
            {
                var rejectedMetadata=false;
                try{_ = new PobGemSearchCatalog([JsonSerializer.Deserialize<JsonElement>(invalid)]);}catch(InvalidDataException){rejectedMetadata=true;}
                check(rejectedMetadata,"Malformed declared gem search metadata is rejected");
            }
            await Action("gem-search","Fireball");
            var retainedQuery=Get("gemQuery");_ = View();retainedQuery=Get("gemQuery");
            await Action("back");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.inventory.rows")&&Nodes(View().Root).Any(n=>n.Id=="pob.gem-add"),"Picker Back returns to inline editing with the group list retained");
            await Action("gem-add");_ = View();
            check((string)Get("gemSearch")! == "Fireball"&&ReferenceEquals(retainedQuery,Get("gemQuery")),"Reopening the unchanged picker retains search and indexed scroll ownership");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.gems").IndexedCollection!.Count>0,"Gem search retains matching results without pagination");
            var active=Array.FindIndex(catalog,g=>g.GetProperty("name").GetString()=="Fireball"&&!g.GetProperty("support").GetBoolean());
            if(active<0)active=Array.FindIndex(catalog,g=>!g.GetProperty("support").GetBoolean());
            var activeName=catalog[active].GetProperty("name").GetString();
            await PickIndexedGem(active,inspect:true);
            check(Group().GetProperty("gems").EnumerateArray().Single().GetProperty("name").GetString()==activeName,"Add gem uses the selected catalog identity");
            await Action("gem-add");catalog=(JsonElement[])Get("gemCatalog")!;
            var support=Array.FindIndex(catalog,g=>g.GetProperty("support").GetBoolean());
            await PickIndexedGem(support,inspect:false);check(Group().GetProperty("gems").GetArrayLength()==2,"Support gem is added to the existing group");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.gem-replace.1")&&Nodes(View().Root).Any(n=>n.Id=="pob.inventory.rows"),"Routine replace and enable actions are beside the selected group");
            var workspaceFixtures=Environment.GetEnvironmentVariable("POB_GEM_INSPECTION_FIXTURES");
            if(!string.IsNullOrWhiteSpace(workspaceFixtures))File.WriteAllBytes(Path.Combine(workspaceFixtures,"poe"+game+"-skill-workspace.json"),SnapshotJson.Serialize(View()));
            var focusBeforeEnable=View().FocusGroupEntryRequest?.RequestId;
            await Action("gem-enabled.2");
            check(View().FocusGroupEntryRequest?.RequestId==focusBeforeEnable,"Inline gem toggle does not introduce a new focus transfer");
            check(!Group().GetProperty("gems")[1].GetProperty("enabled").GetBoolean(),"Gem enable toggle edits the selected gem");
            await Action("undo");check(Group().GetProperty("gems")[1].GetProperty("enabled").GetBoolean(),"Undo restores gem enable state");
            await Action("redo");check(!Group().GetProperty("gems")[1].GetProperty("enabled").GetBoolean(),"Redo reapplies gem enable state");
            await Action("gem-replace.1");catalog=(JsonElement[])Get("gemCatalog")!;
            var replacement=Array.FindIndex(catalog,g=>!g.GetProperty("support").GetBoolean()&&g.GetProperty("name").GetString()!=activeName);
            var replacementName=catalog[replacement].GetProperty("name").GetString();await PickIndexedGem(replacement,inspect:true);
            check(Group().GetProperty("gems")[0].GetProperty("name").GetString()==replacementName&&Group().GetProperty("gems").GetArrayLength()==2,"Replace gem changes only the chosen slot");
            await Action("gem-remove.2");await Action("skill-remove-cancel");check(Group().GetProperty("gems").GetArrayLength()==2,"Cancelling gem removal preserves the group");
            await Action("gem-remove.2");await Action("skill-remove-confirm");check(Group().GetProperty("gems").GetArrayLength()==1,"Confirmed gem removal saves immediately");
            await Action("group-duplicate");check(Groups().Length==count+2&&Group().GetProperty("title").GetString()=="Controller group (copy)","Duplicate copies gems and selects the new group");
            var savedOrder=Groups().Select(g=>g.GetProperty("id").GetInt32()).ToArray();
            var originalMain=Groups().Single(g=>g.GetProperty("main").GetBoolean()).GetProperty("id").GetInt32();
            var copiedGroup=Group().GetProperty("id").GetInt32();
            await Action("back");await Action("skill-main");
            var displayedGroups=Nodes(View().Root).Where(n=>n.ActionId?.StartsWith("skill.",StringComparison.Ordinal)==true).ToArray();
            check(displayedGroups.First().ActionId=="skill."+copiedGroup&&displayedGroups.First().IsSelected==true,"New main group is pinned first without losing inspected selection");
            check(Groups().Select(g=>g.GetProperty("id").GetInt32()).SequenceEqual(savedOrder)&&displayedGroups.Skip(1).Select(n=>n.ActionId).SequenceEqual(savedOrder.Where(i=>i!=copiedGroup).Select(i=>"skill."+i)),"Pinning preserves saved group order and relative order of other groups");
            await Action("main-group."+originalMain);await Action("group-manage");
            var copyIndex=Array.FindIndex(Groups(),g=>g.GetProperty("title").GetString()=="Controller group (copy)");
            var aboveCopy=Groups()[copyIndex-1].GetProperty("title").GetString();
            await Action("group-up");check(Main()==main&&Group().GetProperty("title").GetString()=="Controller group (copy)","Reordering preserves selected group and main calculation skill");
            var orderView=View();var orderNodes=Nodes(orderView.Root).ToArray();
            check(orderNodes.Single(n=>n.Id=="pob.group-order").Text==$"Saved order · {copyIndex} of {count+2}"&&
                orderNodes.Single(n=>n.Id=="pob.group-below").Text=="Below: "+aboveCopy,"Move up immediately reports its new position and former neighbor below");
            check(orderView.FocusGroupEntryRequest?.GroupId=="pob.group-order.actions","Reordering keeps focus in the move controls");
            await Action("group-down");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.group-order").Text==$"Saved order · {copyIndex+1} of {count+2}"&&
                Nodes(View().Root).Single(n=>n.Id=="pob.group-above").Text=="Above: "+aboveCopy,"Move down restores the position and neighbor labels");
            await Action("skill.1");await Action("group-manage");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.group-up").IsDisabled==true&&Nodes(View().Root).Single(n=>n.Id=="pob.group-above").Text=="Above: None — first group","First group explains its boundary and disables Move up");
            await Action("skill."+Groups().Last().GetProperty("id").GetInt32());await Action("group-manage");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.group-down").IsDisabled==true&&Nodes(View().Root).Single(n=>n.Id=="pob.group-below").Text=="Below: None — last group","Last group explains its boundary and disables Move down");
            await Action("skill."+Groups().Single(g=>g.GetProperty("title").GetString()=="Controller group (copy)").GetProperty("id").GetInt32());await Action("group-manage");
            await Action("group-delete");await Action("skill-remove-confirm");check(Groups().Length==count+1&&Main()==main,"Deleting a group preserves other groups and the main skill");
            var saved=Result();await Action("undo");check(Groups().Length==count+2,"Undo restores deleted group");await Action("redo");check(Groups().Length==count+1,"Redo removes the restored group");
            var reloaded=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(Path.Combine(directory,id+".xml")),name="Reload management"},default);
            check(reloaded.State.GetProperty("skills").GetArrayLength()==saved.State.GetProperty("skills").GetArrayLength()&&reloaded.State.GetProperty("skills").EnumerateArray().Any(g=>g.GetProperty("title").GetString()=="Controller group"),"Saved XML reload retains group management changes");
            var mainIndex=Groups().Single(g=>g.GetProperty("main").GetBoolean()).GetProperty("id").GetInt32();
            if(mainIndex>1)
            {
                await Action("skill."+mainIndex);await Action("group-manage");await Action("group-up");
                check(Main()==main&&Group().GetProperty("main").GetBoolean(),"Moving the main group across another group preserves both main and inspected selection");
            }
            var authored=Groups().First(g=>g.GetProperty("title").GetString()=="Controller group");
            var copied=await engine.ExecuteAsync(new{op="skill-copy",group=authored.GetProperty("id").GetInt32()},default,Result().Xml);
            var clipboard=copied.State.GetProperty("groupClipboard").GetString()!;
            var pasted=await engine.ExecuteAsync(new{op="skill-group",mode="paste",text=clipboard},default,Result().Xml);
            check(pasted.State.GetProperty("skills").GetArrayLength()==Groups().Length+1,"Desktop group clipboard format can be pasted without replacing existing groups");
            var rejected=false;
            try{await engine.ExecuteAsync(new{op="skill-group",mode="paste",text=clipboard+"Unknown unparseable gem line\n"},default,Result().Xml);}catch(IOException){rejected=true;}
            check(rejected,"Partial clipboard imports are rejected instead of silently dropping gems");
            var generated=Groups().FirstOrDefault(g=>g.GetProperty("generated").GetBoolean());
            if(generated.ValueKind==JsonValueKind.Object)
            {
                await Action("skill."+generated.GetProperty("id").GetInt32());await Action("group-manage");
                check(!Nodes(View().Root).Any(n=>n.Id is "pob.gem-add" or "pob.group-delete" or "pob.group-duplicate" or "pob.group-slot"),"Generated group management omits unsupported authoring actions");
                var perGem=Group().GetProperty("gems")[0].GetProperty("count").GetDouble();
                await Action("group-count","2");
                check(Group().GetProperty("groupCount").GetDouble()==2&&Group().GetProperty("gems")[0].GetProperty("count").GetDouble()==perGem,"Granted group count is distinct from per-gem count");
            }
            var generatedCount=Groups().Count(g=>g.GetProperty("generated").GetBoolean());
            await Action("group-delete-all");await Action("skill-remove-confirm");
            check(Groups().Length==generatedCount&&Groups().All(g=>g.GetProperty("generated").GetBoolean()),"Bulk removal keeps generated groups and removes only authored groups");
            await Action("undo");check(Groups().Any(g=>!g.GetProperty("generated").GetBoolean()),"Undo restores bulk-removed groups");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
