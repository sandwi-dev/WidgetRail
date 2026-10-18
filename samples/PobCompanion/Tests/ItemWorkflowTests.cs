using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ItemWorkflowTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await ItemCandidateInspectionTests.RunAsync(game,root,runtime,adapter,xml,check);
        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-item-workflow-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var buildId=Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory,buildId+".xml"),xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),buildId);
        PobWidget? widget=null;PobEngine? engine=null;var sequence=0;
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat((node.Children??[]).SelectMany(Nodes));
        ViewSnapshot Snapshot()
        {
            try{return widget!.Render().CreateSnapshot("items-workflow",++sequence);}
            catch(ProtocolValidationException error){Console.WriteLine(JsonSerializer.Serialize(error.Errors));throw;}
        }
        bool Has(string id)=>Nodes(Snapshot().Root).Any(n=>n.Id==id);
        PobWorkState Work()=>PobWorkStore.Read(directory,buildId,game)!;
        async Task Settled()
        {
            var end=DateTime.UtcNow.AddSeconds(65);
            while(Has("pob.cancel-operation")&&DateTime.UtcNow<end)await Task.Delay(20);
            if(Has("pob.cancel-operation"))throw new TimeoutException("Item workflow did not settle.");
        }
        async Task Action(string id,string? value=null){await widget!.OnActionAsync(new(id,"items-workflow"){CommittedText=value});await Settled();}
        async Task Open()
        {
            engine=new(root,runtime,adapter);widget=new(game,engine,directory);
            WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Settled();
        }
        async Task Close(){await WidgetTestHost.DestroyAsync(widget!);widget=null;engine=null;}
        try
        {
            await Open();await Action("section.3");
            check(Has("pob.inventory")&&Has("pob.item-create"),"Equipment exposes crafting and full inventory without requiring an equipped item");
            await Action("item-create");
            check(Has("pob.craft-rarity"),"Base picker exposes rarity without a separate navigation step");
            var catalog=await engine!.ExecuteAsync(new{op="item-bases"},default,xml);
            var basis=catalog.State.GetProperty("choices").EnumerateArray().First(c=>c.GetProperty("kind").GetString()!.Contains("Helmet",StringComparison.OrdinalIgnoreCase));
            await Action("craft-base."+basis.GetProperty("id").GetInt32());
            check(Has("pob.author.controls")&&Has("pob.author-custom")&&Has("pob.draft-store"),"Crafted item opens the reusable affix editor with a distinct modifier input");
            var compact=Nodes(Snapshot().Root).FirstOrDefault(n=>n.Id.StartsWith("pob.author-setting.")&&n.Kind==ViewNodeKind.Select&&n.IsDisabled!=true&&n.SelectOptions.Count>1);
            if(compact is not null)
            {
                var original=compact.SelectOptions.Single(o=>o.IsSelected);
                var alternative=compact.SelectOptions.First(o=>!o.IsSelected&&!o.IsDisabled);
                await Action(alternative.ActionId);
                check(Has("pob.author.controls")&&!Has("pob.choice-picker")&&Nodes(Snapshot().Root).Single(n=>n.Id==compact.Id).SelectOptions.Single(o=>o.IsSelected).Id==alternative.Id,
                    "Short item settings change in place through a dropdown without a picker page");
                await Action(original.ActionId);
                check(Work().Undo.Length==0,"Changing and restoring a draft dropdown does not create build history");
            }
            check(!PobWidget.UsesCompactChoice(Enumerable.Range(0,13).Select(i=>JsonSerializer.SerializeToElement(new{id=i,title="Choice "+i})).ToArray())&&
                !PobWidget.UsesCompactChoice([JsonSerializer.SerializeToElement(new{id=1,title="Rune",description="A modifier requiring explanation"})]),
                "Long catalogs and descriptive choices retain the searchable detail picker");
            var draft=Work().ItemDraft;
            check(!string.IsNullOrEmpty(draft)&&Work().Undo.Length==0,"New crafted draft is persisted without an inventory edit or Undo entry");
            await Action("author-custom","+25 to maximum Mana");
            check(Work().ItemDraft!.Contains("+25 to maximum Mana"),"Committed modifier input is retained in the saved draft");
            var authored=Work().ItemDraft;
            await Action("back");await Action("item-create");
            check(Work().ItemDraft==authored&&Has("pob.author.controls"),"Craft action resumes an existing draft instead of replacing it");
            await Close();await Open();
            check(Work().ItemDraft==authored&&Has("pob.draft-edit"),"Unloading and reopening preserves the unfinished item and exposes edit or compare actions");
            await Action("draft-edit");await Action("draft-store");
            check(Work().ItemDraft is null&&Work().Undo.Length==1&&Has("pob.stored.items"),"Saving draft creates one undoable inventory edit and clears only the stored draft");
            var saved=Work().CommittedXml;
            var items=await engine!.ExecuteAsync(new{op="inventory-list"},default,saved);
            var added=items.State.GetProperty("inventory").EnumerateArray().Last().GetProperty("id").GetInt32();
            await Action("inventory-edit");await Action("author-custom","+30 to maximum Life");
            var edited=Work().CommittedXml;
            check(edited.Contains("+30 to maximum Life")&&Work().Undo.Length==2,"Editing a saved item persists its modifier and history immediately");
            await Action("undo");check(WorkStateTests.SameBuild(Work().CommittedXml,saved),"Undo from the item editor restores the prior item and refreshes its fields");
            await Action("redo");check(WorkStateTests.SameBuild(Work().CommittedXml,edited),"Redo from the item editor restores the edited item");
            await Action("back");await Action("inventory-copy");
            var copied=Work().CommittedXml;
            await Action("inventory-compare");
            check(Has("pob.feature-reading.body")&&Work().CommittedXml==copied,"Inventory comparison shows calculation differences without modifying equipment");
            await Action("feature-close");await Action("inventory-equip");
            check(!WorkStateTests.SameBuild(Work().CommittedXml,copied),"Equip applies the compared inventory item to the selected compatible slot");
            await Action("inventory-select."+added);await Action("inventory-delete");
            check(Has("pob.feature-removal.body")&&Nodes(Snapshot().Root).Single(n=>n.Id=="pob.feature-confirm").Text=="Delete","Inventory deletion uses a generic confirmation rather than misleading group labels");
            await Action("feature-close");
            var beforeDelete=Work().CommittedXml;await Action("inventory-delete");await Action("feature-confirm");
            check(!WorkStateTests.SameBuild(Work().CommittedXml,beforeDelete),"Confirmed inventory deletion commits an undoable build edit");
            await Close();await Open();await Action("undo");
            check(WorkStateTests.SameBuild(Work().CommittedXml,beforeDelete),"Deleted inventory item is recoverable through Undo after unloading");
            await Action("section.3");
            var equippedState=await engine!.ExecuteAsync(new{op="snapshot"},default,Work().CommittedXml);
            // Exercise the helmet this workflow crafted and equipped. The input
            // build's first item may be a unique weapon with no eligible affixes.
            var equippedItem=equippedState.State.GetProperty("items").EnumerateArray().Single(item=>item.GetProperty("slot").GetString()=="Helmet");
            await Action("item."+equippedItem.GetProperty("id").GetInt32());await Action("item-manage");await Action("equipped-author");
            var beforeModifier=Work().CommittedXml;var beforeHistory=Work().Undo.Length;
            await Action("author-add-modifier");
            check(Has("pob.author-source")&&Has("pob.choice-search")&&Has("pob.choices"),"Existing item opens source dropdown and a searchable virtualized modifier list");
            var source=Nodes(Snapshot().Root).Single(n=>n.Id=="pob.author-source").SelectOptions.Last();
            await Action(source.ActionId);
            var modifierList=(await engine.ExecuteAsync(new{op="item-modifiers",itemId=equippedItem.GetProperty("itemId").GetInt32(),source=source.Id},default,beforeModifier)).State.GetProperty("choices");
            var modifier=modifierList.EnumerateArray().First();
            var description=modifier.GetProperty("description").GetString()!.Split('\n')[0].Trim();
            await Action("choice-search",description[..Math.Min(25,description.Length)]);
            check(Has("pob.author-source")&&Nodes(Snapshot().Root).Single(n=>n.Id=="pob.author-source").SelectOptions.Single(o=>o.IsSelected).Id==source.Id,"Changing modifier source stays in the picker and supports searching descriptions");
            await Action("author-modifier."+modifier.GetProperty("id").GetInt32());
            check(Has("pob.author.controls")&&Work().Undo.Length==beforeHistory+1&&!WorkStateTests.SameBuild(beforeModifier,Work().CommittedXml),"Predefined modifier selection updates the existing item and creates one Undo entry");
            await Action("undo");
            check(WorkStateTests.SameBuild(beforeModifier,Work().CommittedXml),"Undo restores the original item after predefined modifier editing");
            await Action("section.3");await Action("item-uniques");
            check(Has("pob.choices")&&Has("pob.choice-search"),"Unique library is searchable and virtualized");
            var library=await engine!.ExecuteAsync(new{op="item-uniques"},default,Work().CommittedXml);
            var unique=library.State.GetProperty("choices").EnumerateArray().First(u=>u.GetProperty("title").GetString()!.Contains("Goldrim",StringComparison.Ordinal));
            var committed=Work().CommittedXml;
            await Action("unique-item."+unique.GetProperty("id").GetInt32());
            check(Has("pob.author.controls")&&Work().ItemDraft!.Contains("Goldrim")&&WorkStateTests.SameBuild(committed,Work().CommittedXml),"Selecting a library unique opens a recoverable draft without equipping it");
            var uniqueDraft=Work().ItemDraft;await Action("item-uniques");
            check(Work().ItemDraft==uniqueDraft,"A second library request cannot overwrite unfinished item work");
            await Close();await Open();await Action("draft-edit");
            check(Work().ItemDraft==uniqueDraft&&Has("pob.author.controls"),"Unique draft survives engine and widget restart");
        }
        finally{if(widget is not null)await WidgetTestHost.DestroyAsync(widget);if(engine is not null)await engine.DisposeAsync();}
    }
}
