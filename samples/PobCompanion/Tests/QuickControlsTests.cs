using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class QuickControlsTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var baseline=await engine.ExecuteAsync(new{op="load",xml,name="Shortcut fixture"},default);
        var directory=Path.Combine(Path.GetTempPath(),"pob-quick-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        void Set(string key,object? value)=>typeof(PobWidget).GetField(key,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(widget,value);
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat((node.Children??[]).SelectMany(Nodes));
        var sequence=0;
        ViewSnapshot View()=>widget.Render().CreateSnapshot("quick-controls-test",++sequence);
        bool Shortcut(ViewSnapshot view,string id)=>Nodes(view.Root).Single(n=>n.Id==id).Shortcuts?.Any(s=>s.Button==ControllerButton.LeftStick)==true;
        async Task Jump(string target){await widget.OnActionAsync(new("quick-controls."+target,"fixture"));check(View().FocusGroupEntryRequest?.GroupId==target,"L3 requests remembered group "+target);}
        try
        {
            foreach(var count in new[]{0,8,9,30})
            {
                var state=JsonNode.Parse(baseline.State.GetRawText())!;
                state["nodes"]=JsonSerializer.SerializeToNode(Enumerable.Range(1,count).Select(i=>new{id=i,name="Passive "+i,description="",interactive=true,allocated=true,kind="Normal"}));
                Set("result",new PobResult(JsonSerializer.SerializeToElement(state),xml));Set("section","Tree");Set("detailKind","");
                var view=View();
                check(Shortcut(view,"pob.tree.results")== (count>8),"Passive shortcut depends on long results: "+count);
                check(!Nodes(view.Root).Single(n=>n.Id=="pob.tree.main").Shortcuts!.Any(s=>s.Button==ControllerButton.LeftStick),"Tree browser region never inherits Search");
                if(count>8){await Jump("pob.tree.search-controls");await Jump("pob.tree.results");}
            }
            foreach(var section in new[]{"Skills","Items"})foreach(var count in new[]{8,12})foreach(var sets in new[]{1,2})
            {
                var state=JsonNode.Parse(baseline.State.GetRawText())!;var skills=section=="Skills";
                state[skills?"skills":"items"]=JsonSerializer.SerializeToNode(Enumerable.Range(1,count).Select(i=>new{id=i,title="Skill "+i,name="Item "+i,slot="Slot "+i,gems=Array.Empty<object>(),details=Array.Empty<object>()}));
                state[skills?"skillSets":"itemSets"]=JsonSerializer.SerializeToNode(Enumerable.Range(1,sets).Select(i=>new{id=i,title="Set "+i,active=i==1}));
                Set("result",new PobResult(JsonSerializer.SerializeToElement(state),xml));Set("section",section);Set("detailKind","");
                check(Shortcut(View(),"pob.inventory.rows")== (count>8&&sets>1),section+" needs both a long list and a set control: "+count+" / "+sets);
                if(count>8&&sets>1){await Jump("pob.inventory.tools");await Jump("pob.inventory.rows");}
            }
            foreach(var count in new[]{3,12})
            {
                var state=JsonNode.Parse(baseline.State.GetRawText())!;
                state["conditions"]=JsonSerializer.SerializeToNode(Enumerable.Range(1,count).Select(i=>new{id="flag"+i,title="Flag "+i,section="Section "+i,value=false,kind="check",options=Array.Empty<object>()}));
                Set("result",new PobResult(JsonSerializer.SerializeToElement(state),xml));Set("section","Configuration");Set("detailKind","");
                check(Shortcut(View(),"pob.config.categories")== (count>8),"Configuration shortcut uses section count: "+count);
                if(count>8){await Jump("pob.config.tools");await Jump("pob.config.footer");await Jump("pob.config.categories");}
                Set("savedBuilds",Enumerable.Range(1,count).Select(i=>(i.ToString("x32"),"Build "+i)).ToArray());Set("detailKind","library");
                check(Shortcut(View(),"pob.library.results")== (count>8),"Library shortcut depends on long results: "+count);
                if(count>8){await Jump("pob.library.tools");await Jump("pob.library.results");}
            }
            foreach(var count in new[]{2,5})
            {
                var state=JsonNode.Parse(baseline.State.GetRawText())!;
                state["skills"]=JsonSerializer.SerializeToNode(new[]{new{id=1,title="Gem group",gems=Enumerable.Range(1,count).Select(i=>new{id=i,name="Gem "+i,level=20,quality=0,count=1})}});
                Set("result",new PobResult(JsonSerializer.SerializeToElement(state),xml));Set("section","Skills");Set("detailKind","");
                check(Shortcut(View(),"pob.detail.scroll")== (count==5),"Tall gem-card lists qualify by visual rows: "+count);
                if(count==5){await Jump("pob.skill.actions");await Jump("pob.detail.scroll");}
            }
            foreach(var count in new[]{8,9})
            {
                var state=JsonNode.Parse(baseline.State.GetRawText())!;
                state["nodes"]=JsonSerializer.SerializeToNode(Enumerable.Range(1,count).Select(i=>new{id=i,name="Changed "+i,description="+10 Strength",interactive=true,allocated=true,kind="Normal",canRefund=true}));
                var before=state.DeepClone();foreach(var node in before["nodes"]!.AsArray())node!["allocated"]=false;
                Set("result",new PobResult(JsonSerializer.SerializeToElement(state),xml));Set("pendingBefore",new PobResult(JsonSerializer.SerializeToElement(before),xml));
                Set("section","Tree");Set("detailKind","node");Set("detailId",1);Set("showChangedNodes",true);
                check(Shortcut(View(),"pob.tree.preview-details")== (count>8),"Expanded changed-node list shortcut uses its length: "+count);
                if(count>8){await Jump("pob.tree.preview.actions");await Jump("pob.tree.preview-details");}
                Set("showChangedNodes",false);
                check(!Shortcut(View(),"pob.tree.preview-details"),"Collapsed preview is not treated as a long list");
            }
            Set("pendingBefore",null);Set("detailKind","");Set("section","Notes");
            check(Shortcut(View(),"pob.notes.scroll"),"Notes exposes the explicitly requested navigation-tab shortcut");
            await Jump("pob.notes.tab-target");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.notes.tab-target").InitialChildFocusId=="pob.tab.5"&&View().ScrollRevealRequest is null,
                "Notes shortcut focuses its navigation tab without scrolling to the content heading");
            await widget.OnActionAsync(new("quick-controls.pob.library.tools","fixture"));
            check(View().FocusGroupEntryRequest?.GroupId!="pob.library.tools","Retired panel shortcut cannot focus a missing group");
            Set("section","Configuration");Set("pendingBefore",baseline);
            check(!Nodes(View().Root).SelectMany(n=>n.Shortcuts??[]).Any(s=>s.Button==ControllerButton.LeftStick),"Preview disables quick navigation");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
