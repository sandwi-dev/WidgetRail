using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class PassiveGroupingTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        JsonElement Node(int id,string name,string description,bool allocated=false,bool granted=false)=>JsonSerializer.SerializeToElement(new{id,name,description,allocated,granted,interactive=true});
        var fixture=new[]{Node(1,"Intelligence","+10 to Intelligence",true),Node(2,"Intelligence","+10 to Intelligence",granted:true),
            Node(3,"Intelligence","+20 to Intelligence"),Node(4,"Unique passive","Other effect",true)};
        var collapsed=PobWidget.BuildPassiveList(fixture,"tree","",new HashSet<string>());
        check(collapsed.NodeCount==3&&collapsed.Entries.Length==2&&collapsed.Entries[0] is{IsGroup:true,Expanded:false}&&collapsed.Entries[0].Nodes.Length==2,
            "Default passive list groups allocated/granted duplicates without hiding their node count");
        var search=PobWidget.BuildPassiveList(fixture,"tree","Intelligence",new HashSet<string>());
        check(search.NodeCount==3&&search.Entries.Length==1&&search.Entries[0].Key==collapsed.Entries[0].Key,"Filtered group counts reflect all matching nodes with a stable name identity");
        var expanded=PobWidget.BuildPassiveList(fixture,"tree","Intelligence",new HashSet<string>{"Intelligence"});
        check(expanded.Entries.Skip(1).Select(entry=>entry.Key).SequenceEqual(new[]{"node.1","node.2","node.3"})&&expanded.Entries.Skip(1).All(entry=>entry.IsChild),
            "Expansion produces one flat ordered list of distinct stable node identities");
        var exact=PobWidget.BuildPassiveList(fixture,"tree","+20",new HashSet<string>());
        check(exact.NodeCount==1&&exact.Entries.Single() is{IsGroup:false,Key:"node.3"},"A single matching occurrence remains directly selectable");
        ViewNode GroupPreview(PobWidget.PassiveEntry entry)=>new WidgetView(PobWidget.PassiveGroupPreview(entry)).CreateSnapshot("group-summary",1).Root;
        check(Nodes(GroupPreview(collapsed.Entries[0])).Any(node=>node.Text=="Shared effect per node")&&Nodes(GroupPreview(collapsed.Entries[0])).Any(node=>node.Text?.Contains("1 allocated · 1 granted")==true),
            "Identical group effects are labelled per-node and allocation states remain separate");
        check(Nodes(GroupPreview(search.Entries[0])).Any(node=>node.Text?.StartsWith("Effects differ")==true)&&!Nodes(GroupPreview(search.Entries[0])).Any(node=>node.Kind==ViewNodeKind.RichText),
            "Same-name passives with different effects do not invent a shared or summed effect");
        check(PobWidget.BuildPassiveList(fixture,"tree","absent",new HashSet<string>()).Entries.Length==0,"Empty filtered membership has no empty group headers");

        var directory=Path.Combine(Path.GetTempPath(),"pob-grouping-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var document=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,document+".xml"),xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),document);
        await using var engine=new PobEngine(root,runtime,adapter);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        object? Get(string field)=>typeof(PobWidget).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(widget);
        void Set(string field,object value)=>typeof(PobWidget).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(widget,value);
        var sequence=0;
        ViewSnapshot View()=>widget.Render().CreateSnapshot("passive-groups",++sequence);
        PobWidget.PassiveQuery Query(){_=View();return (PobWidget.PassiveQuery)Get("passiveQuery")!;}
        async Task Until(Func<bool> ready){var until=DateTime.UtcNow.AddSeconds(65);while(!ready()){if(DateTime.UtcNow>until)throw new TimeoutException("Passive grouping did not settle");await Task.Delay(15);}}
        async Task Action(string id,string? text=null){await widget.OnActionAsync(new(id,"test"){CommittedText=text});await Until(()=>!(bool)Get("busy")!);_=View();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Until(()=>!(bool)Get("busy")!);
            var before=(PobResult)Get("result")!;
            var name=before.State.GetProperty("nodes").EnumerateArray().Where(node=>node.GetProperty("interactive").GetBoolean()).GroupBy(node=>node.GetProperty("name").GetString()!).First(group=>group.Count()>1).Key;
            await Action("section.1");await Action("search",name);
            using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"passive-groups");
            async Task Activate(string key)
            {
                var previous=Query();host.PublishSnapshot();var index=Array.FindIndex(previous.Entries,entry=>entry.Key==key);
                using var rows=await host.AcquireAsync("pob.passives",index,1);
                check(rows.RouteAction(key,ControllerButton.A)==WidgetOperationAdmission.Enqueued,"Group/node action is admitted for current row: "+key);
                await Until(()=>!ReferenceEquals(previous,Query())||(string?)Get("detailKind")=="node");
                await Until(()=>!(bool)Get("busy")!);host.PublishSnapshot();
            }
            var group=Query().Entries.First(entry=>entry.IsGroup&&entry.Name==name);
            check(!group.Expanded,"Repeated names start collapsed in live passive search");
            Set("quickControlTarget","pob.tree.search-controls");
            await Activate(group.Key);
            check(Query().Entries.Single(entry=>entry.Key==group.Key).Expanded&&View().FocusGroupEntryRequest?.IndexedItem?.ItemKey==group.Key,
                "Expanding retains focus on its group header");
            var child=Query().Entries.First(entry=>entry.IsChild);
            host.PublishSnapshot();using var oldChild=await host.AcquireAsync("pob.passives",Array.FindIndex(Query().Entries,entry=>entry.Key==child.Key),1);
            await Activate(child.Key);
            check((int)Get("detailId")! == child.Nodes[0].GetProperty("id").GetInt32()&&ReferenceEquals(before,Get("result")),"Child selection opens its exact node without allocating it");
            await Action("back");host.PublishSnapshot();
            check(Query().Entries.Any(entry=>entry.Key==child.Key)&&View().FocusGroupEntryRequest?.IndexedItem?.ItemKey==child.Key,"Back restores the exact child inside its expanded group");
            await Activate(group.Key);
            check(!Query().Entries.Any(entry=>entry.Key==child.Key)&&View().FocusGroupEntryRequest?.IndexedItem?.ItemKey==group.Key&&oldChild.RouteAction(child.Key,ControllerButton.A) is null,
                "Collapse returns focus to the header and rejects a retired child action");
            await Activate(group.Key);await Action("search","no matching passive fixture");await Action("search",name);
            check(Query().Entries.Single(entry=>entry.Key==group.Key).Expanded,"Expansion survives filtering out and restoring a group");
            Set("documentId","another-document");
            check(!Query().Entries.Single(entry=>entry.Key==group.Key).Expanded,"A different build does not inherit expansion");Set("documentId",document);
            check(Query().Entries.Single(entry=>entry.Key==group.Key).Expanded,"Returning to a build restores its session expansion");
            var otherTree=JsonNode.Parse(before.State.GetRawText())!.AsObject();otherTree["trees"]=new JsonArray(new JsonObject{["id"]=999,["active"]=true});
            Set("result",new PobResult(JsonSerializer.SerializeToElement(otherTree),before.Xml));
            check(!Query().Entries.Single(entry=>entry.Key==group.Key).Expanded,"Another passive tree has independent expansion state");Set("result",before);
            check(Query().Entries.Single(entry=>entry.Key==group.Key).Expanded,"Returning to the original tree restores expansion");
            host.PublishSnapshot();await Activate(group.Key);
            var external=group.Nodes.FirstOrDefault(node=>!node.GetProperty("allocated").GetBoolean()&&!node.GetProperty("granted").GetBoolean());
            if(external.ValueKind==JsonValueKind.Object)
            {
                await Action("search","");await Action("node."+external.GetProperty("id").GetInt32());await Action("back");
                var key="node."+external.GetProperty("id").GetInt32();
                check(Query().Entries.Any(entry=>entry.Key==key)&&View().FocusGroupEntryRequest?.IndexedItem?.ItemKey==key,"Returning from an externally opened unallocated passive reveals its group and exact node");
            }
            check(File.ReadAllText(Path.Combine(directory,document+".xml"))==xml&&ReferenceEquals(before,Get("result")),"Grouping, filtering and navigation never change saved or calculated build state");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
}
