using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class TradeMenuTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-trade-menu-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var id=Guid.NewGuid().ToString("N");
        await using var engine=new PobEngine(root,runtime,adapter);
        // Include a second equipped item in an inactive weapon set even in the
        // minimal CI fixture, which otherwise contains only a helmet.
        var fixture=await engine.ExecuteAsync(new{op="item",slot="Weapon 1 Swap",raw="Rarity: NORMAL\n"+(game==1?"Driftwood Wand":"Attuned Wand")},default,xml);
        File.WriteAllText(Path.Combine(directory,id+".xml"),fixture.Xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        var leagueRequests=0;
        var widget=new PobWidget(game,engine,directory){TradeLeagueLoader=(_,_)=>
        {leagueRequests++;return Task.FromResult(new[]{"Standard","Hardcore"});}};
        WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());var sequence=0;
        IEnumerable<ViewNode> Nodes(ViewNode n)=>new[]{n}.Concat((n.Children??[]).SelectMany(Nodes));
        ViewSnapshot View()=>widget.Render().CreateSnapshot("trade-menu",++sequence);
        async Task Idle(){var deadline=DateTime.UtcNow.AddSeconds(65);while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(DateTime.UtcNow>deadline)throw new TimeoutException();await Task.Delay(10);}}
        async Task Action(string action){await widget.OnActionAsync(new(action,"trade-menu"));await Idle();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            var baseline=await engine.ExecuteAsync(new{op="snapshot"},default);
            await Action("section.3");
            foreach(var item in baseline.State.GetProperty("items").EnumerateArray())
            {
                Console.WriteLine("Opening upgrades: "+item.GetProperty("slot").GetString());
                await Action("item."+item.GetProperty("id").GetInt32());await Action("trade");
                var nodes=Nodes(View().Root).ToArray();
                var selected=nodes.Single(n=>n.Id=="pob.trade-slot").SelectOptions.Single(o=>o.IsSelected);
                var slot=item.GetProperty("slot").GetString()!;
                check(selected.Label.StartsWith(slot+" \u2014 ",StringComparison.Ordinal),"Equipment menu retains requested target through loading: "+slot);
                check((selected.Id=="requested")== (nodes.Single(n=>n.Id=="pob.trade-open").IsDisabled==true),"Inactive targets cannot silently search another item: "+slot);
                var controlsTarget=nodes.Single(n=>n.Id=="pob.trade.actions").InitialChildFocusId;
                check(nodes.Single(n=>n.Id==controlsTarget).IsDisabled!=true,"Controller shortcut enters an enabled search control: "+slot);
                await Action("back");
            }
            await Action("trade");
            check(leagueRequests==1,"Reopening trade for different equipment reuses the in-memory league list");
            await Action("trade-refresh");
            check(leagueRequests==2,"Explicit Refresh leagues fetches a new list");
            await Action("back");await Action("trade");
            check(leagueRequests==2,"Successful manual refresh also populates the league cache");
            var slots=Nodes(View().Root).Single(n=>n.Id=="pob.trade-slot").SelectOptions.Where(o=>o.Id!="requested").ToArray();
            foreach(var slot in slots)
            {
                await Action(slot.ActionId);
                check(Nodes(View().Root).Single(n=>n.Id=="pob.trade-slot").SelectOptions.Single(o=>o.IsSelected).Id==slot.Id,"Slot switch renders through loading: "+slot.Label);
            }
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
