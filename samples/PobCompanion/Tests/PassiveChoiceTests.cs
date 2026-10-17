using System.Text.Json;
using System.Reflection;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class PassiveChoiceTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var state=await engine.ExecuteAsync(game==1?new{op="new"}:(object)new{op="load",xml,name="Passive choice fixture"},default);
        // Fresh desktop builds materialize their default set titles/flags on load.
        state=await engine.ExecuteAsync(new{op="load",xml=state.Xml,name="Normalized choice fixture"},default);
        JsonElement[] Nodes()=>state.State.GetProperty("nodes").EnumerateArray().ToArray();
        var kind=game==1?"mastery":"attribute";
        JsonElement Find()=>Nodes().FirstOrDefault(n=>n.GetProperty("choiceKind").GetString()==kind&&n.GetProperty("canChoose").GetBoolean());
        for(var attempts=0;Find().ValueKind!=JsonValueKind.Object&&attempts<10;attempts++)
        {
            var notable=Nodes().Where(n=>n.GetProperty("kind").GetString()=="Notable"&&n.GetProperty("canAllocate").GetBoolean()&&!n.GetProperty("ascendancy").GetBoolean()).OrderBy(n=>n.GetProperty("pathCount").GetInt32()).First();
            state=await engine.ExecuteAsync(new{op="node",id=notable.GetProperty("id").GetInt32(),mode="allocate"},default,state.Xml);
        }
        var node=Find();check(node.ValueKind==JsonValueKind.Object,"Fixture has an available "+kind+" choice through upstream pathfinding");
        var id=node.GetProperty("id").GetInt32();var before=state;
        var response=await engine.ExecuteAsync(new{op="passive-choices",id},default,before.Xml);
        var choices=response.State.GetProperty("choices").EnumerateArray().ToArray();
        check(choices.Length>=2&&choices.All(c=>!string.IsNullOrWhiteSpace(c.GetProperty("title").GetString())),"Upstream passive choices contain readable descriptions and multiple effects");
        check(WorkStateTests.SameBuild(before.Xml,response.Xml),"Reading passive choices does not allocate or change the tree");
        var first=choices[0].GetProperty("id").GetInt32();var second=choices[1].GetProperty("id").GetInt32();
        state=await engine.ExecuteAsync(new{op="node-choice",id,choice=first},default,before.Xml,compare:true);
        var allocated=Nodes().Single(n=>n.GetProperty("id").GetInt32()==id);
        check(allocated.GetProperty("allocated").GetBoolean()&&state.State.TryGetProperty("comparison",out _),"Choosing an effect allocates through upstream and returns comparison totals");
        var firstState=state;
        state=await engine.ExecuteAsync(new{op="node-choice",id,choice=second},default,state.Xml,compare:true);
        var changed=Nodes().Single(n=>n.GetProperty("id").GetInt32()==id);
        check(changed.GetProperty("description").GetString()!=allocated.GetProperty("description").GetString(),"Changing an allocated effect updates its modifier description");
        check(state.State.GetProperty("passivePoints").GetRawText()==firstState.State.GetProperty("passivePoints").GetRawText(),"Changing an effect does not spend another passive point");
        var messages=TreeMapMessages.Create(state.State,firstState.State,true,id);
        check(messages.Where(m=>m.GetProperty("type").GetString()=="nodes").SelectMany(m=>m.GetProperty("items").EnumerateArray()).Any(n=>n.GetProperty("id").GetInt32()==id&&n.GetProperty("change").GetString()=="changed"),"Map highlights changed effects even when their allocation state is unchanged");
        var reloaded=await engine.ExecuteAsync(new{op="load",xml=state.Xml,name="Reload choices"},default);
        check(reloaded.State.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("id").GetInt32()==id).GetProperty("description").GetString()==changed.GetProperty("description").GetString(),"Chosen passive effect survives XML reload");
        var rejected=false;
        try{await engine.ExecuteAsync(new{op="node-choice",id,choice=-1},default,state.Xml);}catch(IOException){rejected=true;}
        var afterFailure=await engine.ExecuteAsync(new{op="snapshot"},default);
        check(rejected&&WorkStateTests.SameBuild(afterFailure.Xml,reloaded.Xml),"Unavailable effect fails without changing the saved tree");

        var directory=Path.Combine(Path.GetTempPath(),"pob-passive-choices-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var buildId=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,buildId+".xml"),firstState.Xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),buildId);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        IEnumerable<ViewNode> ViewNodes(ViewNode n)=>new[]{n}.Concat((n.Children??[]).SelectMany(ViewNodes));var sequence=0;
        ViewSnapshot View()=>widget.Render().CreateSnapshot("passive-choices",++sequence);
        async Task Idle(){var end=DateTime.UtcNow.AddSeconds(65);while(ViewNodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(DateTime.UtcNow>end)throw new TimeoutException();await Task.Delay(20);}}
        async Task Action(string action){await widget.OnActionAsync(new(action,"passive-choices"));await Idle();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            typeof(PobWidget).GetField("section",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,"Tree");
            await Action("node."+id);
            if(game==2)
                check(ViewNodes(View().Root).Any(n=>n.Id=="pob.node-choices"&&n.Kind==ViewNodeKind.Select&&n.SelectOptions.Count==choices.Length),"Attributes use an inline dropdown beside the selected passive");
            else
            {
                await Action("node-choices");
                check(View().Root.Kind==ViewNodeKind.ModalLayer&&!ViewNodes(View().Root).Any(n=>n.Id=="pob.choice-picker")&&ViewNodes(View().Root).Any(n=>n.Id=="pob.passive-choice."+second),
                    "Mastery choices keep the tree underneath a compact effect dialog");
                await Action("passive-choice-close");
                check(View().Root.Kind!=ViewNodeKind.ModalLayer&&WorkStateTests.SameBuild(File.ReadAllText(Path.Combine(directory,buildId+".xml")),firstState.Xml),"Closing the effect dialog preserves the build and returns to the node");
                await Action("node-choices");
            }
            await Action($"passive-choice.{id}.{second}");
            check(ViewNodes(View().Root).Any(n=>n.Id=="pob.effect.after."+id),"Changing an existing effect shows previous and proposed modifiers in the preview");
            check(!ViewNodes(View().Root).Any(n=>n.Id=="pob.preview-nodes"),"An effect-only preview omits the empty allocation and refund list");
            var pending=PobWorkStore.Read(directory,buildId,game)!;
            check(pending.Pending?.Kind=="node"&&WorkStateTests.SameBuild(pending.CommittedXml,firstState.Xml),"Effect preview persists separately from the committed build");
            await Action("apply");await Action("undo");
            check(WorkStateTests.SameBuild(PobWorkStore.Read(directory,buildId,game)!.CommittedXml,firstState.Xml),"Applied passive choice is undoable");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
}
