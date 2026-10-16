using System.Diagnostics;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SkillEditTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var initial=await engine.ExecuteAsync(new{op="load",xml,name="Skill edit fixture"},default);
        var item=initial.State.GetProperty("items").EnumerateArray().FirstOrDefault();
        if(item.ValueKind==JsonValueKind.Object)
        {
            var replaced=await engine.ExecuteAsync(new{op="item",raw=item.GetProperty("raw").GetString(),slot=item.GetProperty("slot").GetString()},default,initial.Xml);
            replaced=await engine.ExecuteAsync(new{op="load",xml=replaced.Xml,name="Equivalent item"},default);
            var before=initial.State.GetProperty("stats").EnumerateArray().ToDictionary(s=>s.GetProperty("name").GetString()!,s=>s.GetProperty("value").GetDouble());
            var changes=replaced.State.GetProperty("stats").EnumerateArray().Where(s=>Math.Abs(s.GetProperty("value").GetDouble()-before[s.GetProperty("name").GetString()!])>.0001).ToArray();
            check(initial.State.GetProperty("mainSkill").GetString()==replaced.State.GetProperty("mainSkill").GetString(),"Equivalent item replacement preserves the logical main skill");
            check(changes.Length==0,"Equivalent item replacement preserves calculation totals");
            if(changes.Length>0)Console.WriteLine("Changed totals: "+JsonSerializer.Serialize(changes));
        }
        var granted=initial.State.GetProperty("skills").EnumerateArray().LastOrDefault(g=>g.GetProperty("generated").GetBoolean());
        if(granted.ValueKind==JsonValueKind.Object)
        {
            var rejected=false;
            try
            {
                await engine.ExecuteAsync(new{op="gem",group=granted.GetProperty("id").GetInt32(),id=1,field="level",value=3},default,initial.Xml);
            }
            catch(IOException error){rejected=error.Message.Contains("source",StringComparison.OrdinalIgnoreCase);}
            check(rejected,"Source-controlled skill level is rejected before mutating the build");
            var unchanged=await engine.ExecuteAsync(new{op="snapshot"},default);
            static string Order(PobResult state)=>string.Join("|",state.State.GetProperty("skills").EnumerateArray().Select(g=>g.GetProperty("title").GetString()+g.GetProperty("slot").GetString()+g.GetProperty("main").GetBoolean()));
            check(Order(initial)==Order(unchanged),"Rejected source edit preserves group order and main skill");
        }
        var groups=initial.State.GetProperty("skills").EnumerateArray().Where(g=>!g.GetProperty("generated").GetBoolean()&&g.GetProperty("gems").EnumerateArray().Any(gem=>gem.GetProperty("level").GetDouble()>1)).Take(2).ToArray();
        check(groups.Length>0,"Fixture contains levelled gems");
        foreach(var group in groups)
        {
            var groupId=group.GetProperty("id").GetInt32();
            var gem=group.GetProperty("gems").EnumerateArray().First(g=>g.GetProperty("level").GetDouble()>1);
            var gemId=gem.GetProperty("id").GetInt32();
            foreach(var level in new[]{1,2,3,15})
            {
                var edited=await engine.ExecuteAsync(new{op="gem",group=groupId,id=gemId,field="level",value=level},default,initial.Xml);
                var actual=edited.State.GetProperty("skills").EnumerateArray().Single(g=>g.GetProperty("id").GetInt32()==groupId).GetProperty("gems").EnumerateArray().Single(g=>g.GetProperty("id").GetInt32()==gemId).GetProperty("level").GetDouble();
                check(actual==level,$"Engine applies group {groupId} gem {gemId} level {level}, got {actual}");
            }
        }
        var directory=Path.Combine(Path.GetTempPath(),"pob-skills-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var id=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,id+".xml"),initial.Xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat((node.Children??[]).SelectMany(Nodes));
        var sequence=0;
        ViewSnapshot View()=>widget.Render().CreateSnapshot("skills-edit-test",++sequence);
        async Task Idle(){var timer=Stopwatch.StartNew();while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(timer.Elapsed.TotalSeconds>65)throw new TimeoutException();await Task.Delay(25);}}
        async Task Action(string action,string? value=null){await widget.OnActionAsync(new(action,"fixture"){CommittedText=value});await Idle();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();await Action("section.2");
            if(granted.ValueKind==JsonValueKind.Object)
            {
                var generatedId=granted.GetProperty("id").GetInt32();
                await Action("skill."+generatedId);
                check(Nodes(View().Root).Single(n=>n.Id=="pob.gem.1level").Kind==ViewNodeKind.Text,"Granted level is a read-only value rather than a misleading editor");
                check(Nodes(View().Root).Any(n=>n.Id=="pob.gem.source.1"&&n.Text!.Contains("determined by its source")),"Granted skill explains the source of its level");
                var before=File.ReadAllText(Path.Combine(directory,id+".xml"));
                await Action("gem.1.level","15");
                check(File.ReadAllText(Path.Combine(directory,id+".xml"))==before&&Nodes(View().Root).Single(n=>n.Id=="pob.skill."+generatedId).IsSelected==true,"Stale source-level actions cannot save or change selection");
            }
            foreach(var group in groups)
            {
                var groupId=group.GetProperty("id").GetInt32();
                var gemId=group.GetProperty("gems").EnumerateArray().First(g=>g.GetProperty("level").GetDouble()>1).GetProperty("id").GetInt32();
                await Action("skill."+groupId);
                var field=Nodes(View().Root).Single(n=>n.Id=="pob.gem."+gemId+"level");
                await Action(field.ActionId!,"3");
                var selected=Nodes(View().Root).Single(n=>n.Id=="pob.skill."+groupId);
                check(selected.IsSelected==true,$"Editing group {groupId} preserves selected group");
                var saved=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(Path.Combine(directory,id+".xml")),name="Saved edit"},default);
                var actual=saved.State.GetProperty("skills").EnumerateArray().Single(g=>g.GetProperty("id").GetInt32()==groupId).GetProperty("gems").EnumerateArray().Single(g=>g.GetProperty("id").GetInt32()==gemId).GetProperty("level").GetDouble();
                check(actual==3,$"Widget saves group {groupId} gem {gemId} level 3, got {actual}");
            }
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
