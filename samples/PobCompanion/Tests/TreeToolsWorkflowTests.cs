using System.Reflection;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

internal static class TreeToolsWorkflowTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"pob-tree-tools-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var id=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,id+".xml"),xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        await using var engine=new PobEngine(root,runtime,adapter);var widget=new PobWidget(game,engine,directory);
        WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());var sequence=0;
        IEnumerable<ViewNode> Nodes(ViewNode n)=>new[]{n}.Concat((n.Children??[]).SelectMany(Nodes));
        ViewSnapshot View(){try{return widget.Render().CreateSnapshot("tree-tools",++sequence);}catch(ProtocolValidationException e){throw new InvalidOperationException(System.Text.Json.JsonSerializer.Serialize(e.Errors),e);}}
        async Task Idle(){var end=DateTime.UtcNow.AddSeconds(65);while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(DateTime.UtcNow>end)throw new TimeoutException();await Task.Delay(20);}}
        async Task Action(string action){await widget.OnActionAsync(new(action,"tree-tools"));await Idle();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            typeof(PobWidget).GetField("section",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(widget,"Tree");
            if(game==1)
            {
                await Action("timeless-open");
                check(Nodes(View().Root).Any(n=>n.Id=="pob.timeless.jewelSelect"),"Finder opens in a valid native workspace");
                await Action("timeless-choice.jewelSelect.2");
                await Action("timeless-picker.socketSelect");
                check(Nodes(View().Root).Any(n=>n.Id=="pob.choice-search"),"Long socket list uses the existing searchable picker");
                await Action("timeless-choice.socketSelect.2");
                await Action("timeless-picker.nodeSelect");await Action("timeless-choice.nodeSelect.2");
                check(Nodes(View().Root).Any(n=>n.Id=="pob.timeless.desired"&&n.TextEntryValue?.Length>0),"Selecting a modifier returns to the finder with its saved weight");
                await Action("timeless-fallback-picker");await Action("timeless-fallback-choice.3");
                check(Nodes(View().Root).Any(n=>n.Id=="pob.timeless.fallback"&&n.TextEntryValue?.Length>0),"Fallback modifier picker adds upstream weighted modifiers");
                await Action("timeless-search");
                check(Nodes(View().Root).Any(n=>n.Id=="pob.timeless.list")&&ViewSnapshotValidator.Validate(View()).Count==0,"Seed results remain in a valid virtualized native list");
                await Action("timeless-inspect.1");
                check(View().Root.Kind==ViewNodeKind.ModalLayer&&Nodes(View().Root).Any(n=>n.Text?.Contains("Combined Node Weight")==true),"Selected seed opens upstream modifier attribution before adding an item");
                await Action("timeless-detail-close");
                check(View().Root.Kind!=ViewNodeKind.ModalLayer,"Closing jewel details returns to the retained results");
            }
            else
            {
                check(Nodes(View().Root).Any(n=>n.Id=="pob.tree.allocation-mode"),"Weapon allocation selector is reachable above the tree");
                await Action("tree-mode.2");
                check(Nodes(View().Root).Single(n=>n.Id=="pob.tree.allocation-mode").SelectOptions.Single(o=>o.IsSelected).Label=="Weapon set 2","Native selector reflects the engine's allocation target");
            }
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
}
