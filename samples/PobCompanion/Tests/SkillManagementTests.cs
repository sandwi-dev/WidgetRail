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
        var sequence=0;
        ViewSnapshot View(){try{return widget.Render().CreateSnapshot("group-management",++sequence);}catch(ProtocolValidationException e){Console.WriteLine(JsonSerializer.Serialize(e.Errors));throw;}}
        async Task Idle(){var timer=Stopwatch.StartNew();while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(timer.Elapsed.TotalSeconds>65)throw new TimeoutException();await Task.Delay(25);}}
        async Task Action(string action,string? value=null){await widget.OnActionAsync(new(action,"fixture"){CommittedText=value});await Idle();if(Get("feedback") is not null)Console.WriteLine("Feedback after "+action);}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();await Action("section.2");
            var count=Groups().Length;var main=Main();
            await Action("group-create");
            check(Groups().Length==count+1&&Group().GetProperty("gems").GetArrayLength()==0,"New group is empty, selected and saved");
            check(Main()==main,"Creating a group preserves the main calculation skill");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.group-name")&&Nodes(View().Root).Any(n=>n.Id=="pob.gem-add"),"New group immediately offers label and Add gem controls");
            await Action("group-name","Controller group");check(Group().GetProperty("title").GetString()=="Controller group","Rename applies the authored label");
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
            check(suggested.Items.Count>0&&suggested.Items.All(i=>i.Description=="Support gem"),"Suggestion names respect the current support-only catalog");
            await Action("gem-type.all");
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"gem-catalog-test",500))
            {
                using var first=await host.AcquireAsync("pob.gems",0,12);
                var visible=Nodes(host.CurrentSnapshot.Root).Single(n=>n.Id=="pob.gems").IndexedCollection!.Count;
                using var later=await host.AcquireAsync("pob.gems",visible/2,12);
                check(first.Range.Items.Count==12&&later.Range.Items.Count==12,"Gem identities materialize into valid native rows at the start and middle of the catalog");
            }
            await Action("gem-search","Fireball");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.gems").IndexedCollection!.Count>0,"Gem search retains matching results without pagination");
            var active=Array.FindIndex(catalog,g=>g.GetProperty("name").GetString()=="Fireball"&&!g.GetProperty("support").GetBoolean());
            if(active<0)active=Array.FindIndex(catalog,g=>!g.GetProperty("support").GetBoolean());
            var activeName=catalog[active].GetProperty("name").GetString();
            await Action("gem-pick."+active);
            check(Group().GetProperty("gems").EnumerateArray().Single().GetProperty("name").GetString()==activeName,"Add gem uses the selected catalog identity");
            await Action("gem-add");catalog=(JsonElement[])Get("gemCatalog")!;
            var support=Array.FindIndex(catalog,g=>g.GetProperty("support").GetBoolean());
            await Action("gem-pick."+support);check(Group().GetProperty("gems").GetArrayLength()==2,"Support gem is added to the existing group");
            await Action("gem-enabled.2");check(!Group().GetProperty("gems")[1].GetProperty("enabled").GetBoolean(),"Gem enable toggle edits the selected gem");
            await Action("undo");check(Group().GetProperty("gems")[1].GetProperty("enabled").GetBoolean(),"Undo restores gem enable state");
            await Action("redo");check(!Group().GetProperty("gems")[1].GetProperty("enabled").GetBoolean(),"Redo reapplies gem enable state");
            await Action("gem-replace.1");catalog=(JsonElement[])Get("gemCatalog")!;
            var replacement=Array.FindIndex(catalog,g=>!g.GetProperty("support").GetBoolean()&&g.GetProperty("name").GetString()!=activeName);
            var replacementName=catalog[replacement].GetProperty("name").GetString();await Action("gem-pick."+replacement);
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
