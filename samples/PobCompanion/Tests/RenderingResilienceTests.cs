using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class RenderingResilienceTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var baseline=await engine.ExecuteAsync(new{op="load",xml,name="Render resilience"},default);
        var directory=Path.Combine(Path.GetTempPath(),"pob-render-resilience-"+Guid.NewGuid().ToString("N"));
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        void Set(string key,object value)=>typeof(PobWidget).GetField(key,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(widget,value);
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        ViewSnapshot View()=>widget.Render().CreateSnapshot("resilience",1);
        void Valid(string name){var snapshot=View();check(ViewSnapshotValidator.Validate(snapshot).Count==0,name);}
        try
        {
            var state=JsonNode.Parse(baseline.State.GetRawText())!;
            foreach(var group in state["skills"]!.AsArray())group!["main"]=false;
            foreach(var field in new[]{"trees","skillSets","itemSets","configSets"})
                state[field]=JsonSerializer.SerializeToNode(new[]{new{id=1,title="First",active=false},new{id=2,title="Second",active=false}});
            Set("result",new PobResult(JsonSerializer.SerializeToElement(state),baseline.Xml));
            Set("section","Calcs");Valid("Missing active main skill renders without selecting a replacement");
            var main=Nodes(View().Root).FirstOrDefault(n=>n.Id=="pob.main-group");
            if(main is not null)check(main.SelectOptions.Single(o=>o.IsSelected).IsDisabled,"Missing main skill is explicitly unavailable");
            foreach(var section in new[]{"Items","Skills"})
            {Set("section",section);Set("detailKind","");Valid(section+" renders sets with no current upstream selection");}
            Set("section","Tree");Set("detailKind","node");Set("detailId",int.MaxValue);
            Valid("Removed passive detail offers recovery instead of throwing during rendering");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.search")&&!Nodes(View().Root).Any(n=>n.Id=="pob.node-change"),"Removed passive returns to search without targeting another node");
            Set("section","Skills");Set("detailKind","gem-options");
            Set("gemOptions",JsonSerializer.SerializeToElement(new{name="Legacy gem",controls=new[]{new{id="corruptLevel",title="Corruption",enabled=true,
                options=new[]{new{id=1,title="None",active=false},new{id=2,title="Corrupted",active=false}}}}}));
            Valid("Legacy gem option with no matching choice remains renderable");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.gem-extra.corruptLevel").SelectOptions.Single(o=>o.IsSelected).IsDisabled,
                "Missing gem choice stays explicit rather than changing corruption state");
            var selectionField=typeof(PobWidget).GetField("calculationSelection",BindingFlags.NonPublic|BindingFlags.Instance)!;
            selectionField.SetValue(widget,Activator.CreateInstance(selectionField.FieldType,new object[]{999,1,"EFFECTIVE",false}));
            Set("calculationCatalog",JsonSerializer.SerializeToElement(new{context=new{skills=new[]{new{id=1,title="Changed skill",active=false},new{id=2,title="Other skill",active=false}}}}));
            var calculationControls=(WidgetElement)typeof(PobWidget).GetMethod("CalculationSkillControls",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(widget,null)!;
            var calculationView=new WidgetView(calculationControls,"pob.calculation.group").CreateSnapshot("calculation-resilience",1);
            check(ViewSnapshotValidator.Validate(calculationView).Count==0&&Nodes(calculationView.Root).Where(n=>n.Kind==ViewNodeKind.Select).All(n=>n.SelectOptions.Single(o=>o.IsSelected).IsDisabled),
                "Removed calculation group and unavailable skill remain explicit without changing inspection context");
            var method=typeof(PobWidget).GetMethod("SelectWithUnavailable",BindingFlags.NonPublic|BindingFlags.Static)!;
            var options=new[]{new SelectOption("valid","Valid value","choose.1",true)};
            var select=(SelectElement)method.Invoke(null,new object[]{"Test",options,"test.select","Unavailable"})!;
            check(select.Options.Count==1&&select.Options[0].ActionId=="choose.1","Valid selection keeps its original action and no placeholder");
            Set("section","Items");Set("detailKind","trade");Set("tradeSlot","Weapon 1");Set("tradeLeague","");Set("tradeLeagues",Array.Empty<string>());
            Set("tradeCatalog",JsonSerializer.SerializeToElement(new{slots=new[]{new{name="Weapon 1",title="Weapon 1",empty=false}},bases=Array.Empty<object>()}));
            var trade=View();
            check(Nodes(trade.Root).Single(n=>n.Id=="pob.trade-open").IsDisabled==true&&
                Nodes(trade.Root).Single(n=>n.Id=="pob.trade.actions").InitialChildFocusId=="pob.trade-browse",
                "Unavailable league discovery keeps rendering and quick-control focus on an enabled recovery action");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
