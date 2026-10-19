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
        using(var canceledPreview=new CancellationTokenSource())
        {
            var process=engine.ProcessId;
            var pending=engine.ReadItemPreviewAsync("canceled",added,null,seed.Xml,canceledPreview.Token,CancellationToken.None);
            canceledPreview.Cancel();
            await pending;
            check(engine.ProcessId==process,"Superseded admitted tooltip read drains its reply without restarting PoB");
        }
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
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();await Action("inventory");
            var before=Result();var selected=(int)Get("inventoryId")!;
            var entries=((JsonElement)Get("inventoryState")!).GetProperty("inventory").EnumerateArray().ToArray();
            var index=Array.FindIndex(entries,e=>e.GetProperty("id").GetInt32()==added);
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"inventory-preview",500))
            {
                using var range=await host.AcquireAsync("pob.stored.items",index,1);var row=range.Range.Items.Single();
                check(row.Root.FocusPresentation is null&&row.Root.FocusPreviewKey==row.Key,"Inventory rows carry keys without preconstructed previews");
                using var preview=await host.AcquirePreviewAsync("pob.inventory.preview",row.Key);
                var content=preview.Range.Items.Single().Root;var query=Get("inventoryQuery");
                check(Nodes(content).Any(node=>node.Kind==ViewNodeKind.RichText),"Inventory preserves the panel rich-text formatter");
                check(row.Root.ActionId=="select"&&row.Root.Shortcuts.Count==0,"Inventory preserves A and does not bind X to a widget action");
                check(Nodes(host.CurrentSnapshot.Root).Any(n=>n.FocusPreview?.EnterButton==ControllerButton.X),"Host owns X entry");
                check(content is not null&&Nodes(content).Any(n=>n.Kind==ViewNodeKind.Select),"Full item preview includes equipment actions before entry");
                check(ReferenceEquals(before,Result())&&selected==(int)Get("inventoryId")!&&ReferenceEquals(query,Get("inventoryQuery")),"Reading full preview does not select, equip or mutate build");
                var equip=Nodes(content!).Single(n=>n.Kind==ViewNodeKind.Select);var option=equip.SelectOptions.Last();
                check(preview.RoutePreviewAction(row.Key,equip.Id,"forged-action") is null,"Undeclared preview actions are rejected");
                check(preview.RoutePreviewAction(row.Key,"other-row",option.ActionId) is null,"Preview cannot target another row's control");
                Set("documentId","other-build");
                preview.RoutePreviewAction(row.Key,equip.Id,option.ActionId);await Task.Delay(100);await Idle();Set("documentId",build);
                check(ReferenceEquals(before,Result()),"Changed document rejects captured equipment action");
                host.PublishSnapshot();
                using var fresh=await host.AcquireAsync("pob.stored.items",index,1);var current=fresh.Range.Items.Single();
                using var currentPreview=await host.AcquirePreviewAsync("pob.inventory.preview",current.Key);
                var currentContent=currentPreview.Range.Items.Single().Root;
                Export("inventory",host.CurrentSnapshot,current.Root with{FocusPreviewKey=null,FocusPresentation=currentContent});
                equip=Nodes(currentContent).Single(n=>n.Kind==ViewNodeKind.Select);option=equip.SelectOptions.Last();
                check(currentPreview.RoutePreviewAction(current.Key,equip.Id,option.ActionId)==WidgetOperationAdmission.Enqueued,"Preview equipment selector uses exact leased action");
                await Until(()=>!ReferenceEquals(before,Result()));await Idle();
                check(!WorkStateTests.SameBuild(before.Xml,Result().Xml),"Explicit equipment option commits the captured item and slot");
                await Action("inventory-search","missing-candidate-filter");host.PublishSnapshot();
                check(currentPreview.RoutePreviewAction(current.Key,equip.Id,option.ActionId) is null,"Query retirement rejects late preview action");
                await Action("inventory-search","");
            }
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"inventory-direct",800))
            {
                using var range=await host.AcquireAsync("pob.stored.items",0,1);var row=range.Range.Items.Single();var committed=Result().Xml;
                check(range.RouteAction(row.Key,ControllerButton.A)==WidgetOperationAdmission.Enqueued,"Inventory A still selects directly");
                await Until(()=>(bool)Get("busy")!||(int)Get("inventoryId")! == int.Parse(row.Key));await Idle();
                check((int)Get("inventoryId")! == int.Parse(row.Key)&&Result().Xml==committed,"Selecting preserves equipped items");
            }
            await Action("item-uniques");
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"unique-range-budget",850))
            {
                var timer=Stopwatch.StartNew();using var all=await host.AcquireAsync("pob.choices",0,64);var cold=timer.ElapsedMilliseconds;
                timer.Restart();using var again=await host.AcquireAsync("pob.choices",0,64);var warm=timer.ElapsedMilliseconds;
                check(all.Range.Items.Count==64&&again.Range.Items.Count==64&&all.Range.Items.All(row=>row.Root.FocusPresentation is null&&row.Root.FocusPreviewKey==row.Key),"A 64-row batch contains references, with zero preview trees");
                Console.WriteLine($"Reference-only range 64: first {cold} ms; cached {warm} ms");
            }
            await Action("choice-search","Goldrim");
            var uniqueBefore=Result();
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"unique-preview",900))
            {
                using var range=await host.AcquireAsync("pob.choices",0,1);var row=range.Range.Items.Single();
                using var preview=await host.AcquirePreviewAsync("pob.unique.preview",row.Key);var content=preview.Range.Items.Single().Root;
                check(Nodes(content).Any(n=>n.Kind==ViewNodeKind.Scroll),"Unique preview includes complete scrollable tooltip");
                check(ReferenceEquals(uniqueBefore,Result())&&Get("pastedItem") is null,"Reading unique details does not create a draft or edit the build");
                Export("unique",host.CurrentSnapshot,row.Root with{FocusPreviewKey=null,FocusPresentation=content});
                var choose=Nodes(content).Single(n=>n.ActionId=="select");
                check(preview.RoutePreviewAction(row.Key,choose.Id,"select")==WidgetOperationAdmission.Enqueued,"Unique preview action routes through exact lease");
                await Until(()=>Get("pastedItem") is not null);await Idle();
                check((string)Get("detailKind")! == "item-author"&&((string)Get("pastedItem")!).Contains("Goldrim",StringComparison.Ordinal)&&
                    WorkStateTests.SameBuild(uniqueBefore.Xml,Result().Xml),"Choosing opens an unequipped recoverable draft");
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
