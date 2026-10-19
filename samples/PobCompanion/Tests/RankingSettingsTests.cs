using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

internal static class RankingSettingsTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var build=await engine.ExecuteAsync(new{op="load",xml,name="Ranking controls"},default);
        var catalog=await engine.ExecuteAsync(new{op="trade-catalog",slot="Weapon 1"},default,build.Xml);
        var directory=Path.Combine(Path.GetTempPath(),"pob-ranking-controls-"+Guid.NewGuid().ToString("N"));
        var widget=new PobWidget(game,engine,directory);
        WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        void Set(string key,object value)=>typeof(PobWidget).GetField(key,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(widget,value);
        try
        {
            Set("result",build);Set("rankingSlot","Weapon 1");Set("rankingEditing",true);
            Set("rankingSlots",catalog.State.GetProperty("slots").EnumerateArray().ToArray());
            var method=typeof(PobWidget).GetMethod("TradeRankingPanel",BindingFlags.NonPublic|BindingFlags.Instance)!;
            IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
            ViewSnapshot Snapshot()
            {
                WidgetElement panel;
                try{panel=(WidgetElement)method.Invoke(widget,null)!;}catch(TargetInvocationException error){throw error.InnerException!;}
                return new WidgetView(UI.Row("test.root",UI.Button("Website","website","pob.website.browser"),panel),"pob.rank.edit").CreateSnapshot("ranking-controls",1);
            }
            check(ViewSnapshotValidator.Validate(Snapshot()).Count==0,"Ranking settings render with the loaded build's actual equipment slots");
            Set("rankingSlot","Weapon from an unavailable set");
            var missing=Snapshot();var target=Nodes(missing.Root).Single(n=>n.Id=="pob.rank.slot");
            check(target.SelectOptions.Single(o=>o.IsSelected) is {Id:"unavailable-selection",IsDisabled:true}&&
                Nodes(missing.Root).Single(n=>n.Id=="pob.rank.evaluate").IsDisabled==true,
                "Unavailable saved ranking target remains visible without silently changing target or enabling evaluation");
            var choices=catalog.State.GetProperty("slots").EnumerateArray().ToArray();
            if(choices.Length>0)
            {
                await widget.OnActionAsync(new("trade-rank-slot.0","pob.rank.slot"));
                var selected=Snapshot();
                check(Nodes(selected.Root).Single(n=>n.Id=="pob.rank.slot").SelectOptions.Single(o=>o.IsSelected).Id=="slot0"&&
                    Nodes(selected.Root).Single(n=>n.Id=="pob.rank.evaluate").IsDisabled!=true,
                    "Choosing an available ranking target restores evaluation with the original option index");
            }
            Set("rankingSlots",Array.Empty<JsonElement>());
            check(ViewSnapshotValidator.Validate(Snapshot()).Count==0,"Ranking settings remain renderable when no equipment slots are available");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
