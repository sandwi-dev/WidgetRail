using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ItemCandidateInspectionTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var bases=await engine.ExecuteAsync(new{op="item-bases"},default,xml);
        var basis=bases.State.GetProperty("choices").EnumerateArray().First(c=>c.GetProperty("kind").GetString()!.Contains("Helmet",StringComparison.OrdinalIgnoreCase));
        var draft=await engine.ExecuteAsync(new{op="item-draft",mode="create",kind=basis.GetProperty("kind").GetString(),@base=basis.GetProperty("base").GetString(),rarity="RARE"},default,xml);
        var seed=await engine.ExecuteAsync(new{op="inventory-edit",mode="add",raw=draft.State.GetProperty("authoring").GetProperty("raw").GetString()},default,xml);
        var added=seed.State.GetProperty("editedInventoryId").GetInt32();
        var directory=Path.Combine(Path.GetTempPath(),"pob-candidate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var build=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,build+".xml"),seed.Xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),build);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        object? Get(string name)=>typeof(PobWidget).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget);
        void Set(string name,object? value)=>typeof(PobWidget).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,value);
        PobResult Result()=>(PobResult)Get("result")!;
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        var sequence=0;
        ViewSnapshot View()=>widget.Render().CreateSnapshot("candidate-test",++sequence);
        async Task Until(Func<bool> ready,int seconds=65){var watch=Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>seconds)throw new TimeoutException("Item candidate operation did not settle");await Task.Delay(15);}}
        async Task Idle()=>await Until(()=>!(bool)Get("busy")!);
        async Task Action(string id,string? text=null){await widget.OnActionAsync(new(id,"test"){CommittedText=text});await Idle();_=View();}
        string ModalAction(string prefix)=>Nodes(View().Root).Where(n=>n.ActionId?.StartsWith(prefix,StringComparison.Ordinal)==true).Select(n=>n.ActionId!).Distinct(StringComparer.Ordinal).Single();
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();await Action("inventory");
            var before=Result();var selected=(int)Get("inventoryId")!;
            var entries=((JsonElement)Get("inventoryState")!).GetProperty("inventory").EnumerateArray().ToArray();
            var index=Array.FindIndex(entries,e=>e.GetProperty("id").GetInt32()==added);
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"inventory-inspector",500))
            {
                using var range=await host.AcquireAsync("pob.stored.items",index,1);var row=range.Range.Items.Single();
                var query=Get("inventoryQuery");var source=Nodes(host.CurrentSnapshot.Root).Single(n=>n.Id=="pob.stored.items").IndexedCollection;
                check(row.Root.ActionId=="select"&&row.Root.Shortcuts.Any(s=>s.Button==ControllerButton.X&&s.ActionId=="inspect")&&row.Root.ContextActions.Any(c=>c.ActionId=="inspect"),
                    "Inventory preserves A selection and offers X and context item inspection");
                check(row.Root.FocusPresentation is not null&&Nodes(host.CurrentSnapshot.Root).Any(n=>n.FocusPreview is not null),"Inventory preview uses cached list facts through the shared passive host");
                check(range.RouteAction(row.Key,ControllerButton.X)==WidgetOperationAdmission.Enqueued,"Inventory inspection admits exact indexed item");
                await Until(()=>Get("candidateInspection") is not null);await Idle();
                var modal=View();var anchor=modal.Root.AnchoredPresentation!.Anchor.IndexedItem!;
                check(anchor.ItemKey==row.Key&&anchor.Index==index&&anchor.QueryGeneration==source!.QueryGeneration,"Inventory inspection anchors exact occurrence");
                check(ReferenceEquals(before,Result())&&selected==(int)Get("inventoryId")!&&ReferenceEquals(query,Get("inventoryQuery")),
                    "Inspecting an unselected inventory item does not select, equip, mutate build or reset query");
                check(Nodes(modal.Root).Any(n=>n.Id.StartsWith("pob.candidate-inspection.line.",StringComparison.Ordinal)),"Inspector displays upstream item tooltip details");
                var close=ModalAction("candidate-inspection-close.");var equip=ModalAction("candidate-inspection-equip.");
                await Action("inventory-search","background attempt");check((string)Get("inventorySearch")! == "","Modal prevents background inventory actions");
                await Action(close);await Action(equip);
                check(ReferenceEquals(before,Result())&&View().Root.Kind!=ViewNodeKind.ModalLayer&&ReferenceEquals(query,Get("inventoryQuery")),"Close restores retained inventory and rejects late equip");
                host.PublishSnapshot();
                check(range.RouteAction(row.Key,ControllerButton.A,contextActionOwnerId:row.Root.Id,contextActionId:"inspect")==WidgetOperationAdmission.Enqueued,"Inventory right-click uses the same inspection path");
                await Until(()=>Get("candidateInspection") is not null);await Idle();
                var secondEquip=ModalAction("candidate-inspection-equip.");
                await Action(equip);check(Get("candidateInspection") is not null&&ReferenceEquals(before,Result()),"Old inspector token cannot equip a new panel");
                Set("documentId","other-build");await Action(secondEquip);Set("documentId",build);
                check(ReferenceEquals(before,Result())&&Get("candidateInspection") is null,"Changed build rejects captured inventory equip");
                host.PublishSnapshot();
                using var fresh=await host.AcquireAsync("pob.stored.items",index,1);var freshRow=fresh.Range.Items.Single();
                check(fresh.RouteAction(freshRow.Key,ControllerButton.X)==WidgetOperationAdmission.Enqueued,"Fresh inventory source can inspect after context retirement");
                await Until(()=>Get("candidateInspection") is not null);await Idle();
                Export("inventory",View(),freshRow.Root);
                var slot=Nodes(View().Root).Single(n=>n.Id=="pob.candidate-inspection.slot");
                var option=slot.SelectOptions.Last();await Action(option.ActionId);
                check(Nodes(View().Root).Single(n=>n.Id==slot.Id).SelectOptions.Single(o=>o.IsSelected).Id==option.Id,"Inspector slot selection is explicit and retained inside the panel");
                await Action(ModalAction("candidate-inspection-equip."));
                check(!WorkStateTests.SameBuild(before.Xml,Result().Xml)&&Get("candidateInspection") is null,"Only explicit Equip commits the captured inventory item and slot");
            }
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"inventory-direct",800))
            {
                using var range=await host.AcquireAsync("pob.stored.items",0,1);var row=range.Range.Items.Single();var committed=Result().Xml;
                check(range.RouteAction(row.Key,ControllerButton.A)==WidgetOperationAdmission.Enqueued,"Inventory A still selects without opening inspection");
                await Until(()=>(bool)Get("busy")!||(int)Get("inventoryId")! == int.Parse(row.Key));await Idle();
                check((int)Get("inventoryId")! == int.Parse(row.Key)&&Get("candidateInspection") is null&&Result().Xml==committed,"Direct inventory selection preserves equipment and follows the existing details workflow");
                await Action("inventory-search","missing-candidate-filter");host.PublishSnapshot();
                check(range.RouteAction(row.Key,ControllerButton.X) is null&&Get("candidateInspection") is null,"Filtered-out inventory occurrence cannot reopen inspection");
                await Action("inventory-search","");
            }
            await Action("item-uniques");await Action("choice-search","Goldrim");
            var uniqueBefore=Result();
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"unique-inspector",900))
            {
                using var range=await host.AcquireAsync("pob.choices",0,1);var row=range.Range.Items.Single();var query=Get("uniqueCandidateQuery");
                check(row.Root.ActionId=="select"&&row.Root.FocusPresentation is not null,"Unique library keeps direct A choice and previews cached source metadata");
                check(range.RouteAction(row.Key,ControllerButton.X)==WidgetOperationAdmission.Enqueued,"Unique inspection opens through captured X action");
                await Until(()=>Get("candidateInspection") is not null);await Idle();
                check(ReferenceEquals(uniqueBefore,Result())&&Get("pastedItem") is null&&ReferenceEquals(query,Get("uniqueCandidateQuery")),"Unique inspection creates no draft, equipment edit or query reset");
                Export("unique",View(),row.Root);
                var close=ModalAction("candidate-inspection-close.");var choose=ModalAction("candidate-inspection-choose.");
                await Action(close);await Action(choose);check(Get("pastedItem") is null,"Closed unique inspector cannot create a draft from a late callback");
                host.PublishSnapshot();check(range.RouteAction(row.Key,ControllerButton.X)==WidgetOperationAdmission.Enqueued,"Unique inspection can reopen without refiltering results");
                await Until(()=>Get("candidateInspection") is not null);await Idle();
                var currentChoose=ModalAction("candidate-inspection-choose.");await Action(choose);check(Get("pastedItem") is null,"Prior unique inspector token cannot choose the reopened candidate");
                await Action(currentChoose);
                check((string)Get("detailKind")! == "item-author"&&((string)Get("pastedItem")!).Contains("Goldrim",StringComparison.Ordinal)&&
                    WorkStateTests.SameBuild(uniqueBefore.Xml,Result().Xml),"Explicit unique choice opens its cached recoverable draft without equipping or changing the build");
            }
            await Action("draft-compare");await Action("back");await Action("item-uniques");await Action("choice-search","Goldrim");
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"unique-direct",1200))
            {
                using var range=await host.AcquireAsync("pob.choices",0,1);var row=range.Range.Items.Single();var committed=Result().Xml;
                check(range.RouteAction(row.Key,ControllerButton.A)==WidgetOperationAdmission.Enqueued,"Unique A still opens the selected draft directly");
                await Until(()=>Get("pastedItem") is not null);await Idle();
                check(((string)Get("pastedItem")!).Contains("Goldrim",StringComparison.Ordinal)&&Get("candidateInspection") is null&&
                    WorkStateTests.SameBuild(committed,Result().Xml),"Direct unique choice preserves the existing unequipped draft workflow");
            }
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
        void Export(string name,ViewSnapshot snapshot,ViewNode row)
        {
            var output=Environment.GetEnvironmentVariable("POB_CANDIDATE_INSPECTION_FIXTURES");if(string.IsNullOrWhiteSpace(output))return;
            Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"poe"+game+"-"+name+"-inspection.json"),SnapshotJson.Serialize(snapshot));
            File.WriteAllText(Path.Combine(output,"poe"+game+"-"+name+"-row.json"),JsonSerializer.Serialize(row));
        }
    }
}
