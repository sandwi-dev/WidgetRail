using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SkillOptionTests
{
    internal static async Task RunAsync(int game,string root,string runtime,string adapter,Action<bool,string> check)
    {
        await using var engine=new PobEngine(root,runtime,adapter);
        var state=await engine.ExecuteAsync(new{op="new"},default);
        async Task<int> Add(string name)
        {
            state=await engine.ExecuteAsync(new{op="skill-group",mode="create"},default,state.Xml);
            var id=state.State.GetProperty("editedSkillGroup").GetInt32();
            var catalog=await engine.ExecuteAsync(new{op="gem-catalog",group=id,gem=1},default,state.Xml);
            var gem=catalog.State.GetProperty("gemCatalog").EnumerateArray().First(g=>g.GetProperty("name").GetString()==name);
            state=await engine.ExecuteAsync(new{op="gem-manage",mode="add",group=id,gemId=gem.GetProperty("id").GetString()},default,state.Xml);
            return id;
        }
        var main=await Add("Fireball");
        state=await engine.ExecuteAsync(new{op="skill",id=main,field="main"},default,state.Xml);
        var stages=await Add("Flameblast");
        var cases=new List<(int Group,string Control,string Attribute)>{(stages,"mainSkillStageCount","skillStageCount")};
        if(game==1)cases.Add((await Add("Pyroclast Mine"),"mainSkillMineCount","skillMineCount"));
        foreach(var entry in cases)
        {
            var inspected=await engine.ExecuteAsync(new{op="inspect-skill",group=entry.Group},default,state.Xml);
            var option=inspected.State.GetProperty("skillOptions").EnumerateArray().First(o=>o.GetProperty("id").GetString()==entry.Control);
            check(option.GetProperty("kind").GetString()=="number",entry.Control+" is available only in its applicable upstream skill context");
            check(WorkStateTests.SameBuild(state.Xml,inspected.Xml),"Inspecting numeric options leaves the main build unchanged");
            foreach(var value in new[]{"1","5",""})
            {
                state=await engine.ExecuteAsync(new{op="skill-number",group=entry.Group,control=entry.Control,value},default,state.Xml);
                var groups=XDocument.Parse(state.Xml).Descendants("Skill").ToArray();
                check(value.Length==0?!groups.SelectMany(g=>g.Elements("Gem")).Any(g=>g.Attribute(entry.Attribute)is not null):
                    groups.SelectMany(g=>g.Elements("Gem")).Any(g=>g.Attribute(entry.Attribute)?.Value==value),entry.Control+": upstream XML retains the edit or clears the override ("+value+")");
                check(state.State.GetProperty("skills").EnumerateArray().Single(g=>g.GetProperty("main").GetBoolean()).GetProperty("id").GetInt32()==main,"Editing a different skill's numeric option preserves the main calculation group");
            }
            var before=state.Xml;var rejected=false;
            try{await engine.ExecuteAsync(new{op="skill-number",group=entry.Group,control=entry.Control,value="-2"},default,before);}
            catch(IOException){rejected=true;}
            var after=await engine.ExecuteAsync(new{op="snapshot"},default);
            check(rejected&&WorkStateTests.SameBuild(before,after.Xml),"Rejected numeric input leaves the authored build intact");
        }
        var special=game==1?await Add("Vaal Haste"):stages;
        var extra=await engine.ExecuteAsync(new{op="gem-controls",group=special,gem=1},default,state.Xml);
        var extras=extra.State.GetProperty("controls").EnumerateArray().ToArray();
        check(game==1?extras.Count(c=>c.GetProperty("id").GetString()!.StartsWith("enableGlobal"))==2:extras.Any(c=>c.GetProperty("id").GetString()=="corruptLevel"),"Specialized gem controls reflect this engine's Vaal effects or corruption options");
        foreach(var control in extras)
        {
            var key=control.GetProperty("id").GetString()!;object value;
            if(control.TryGetProperty("options",out var options))value=options[options.GetArrayLength()-1].GetProperty("id").GetInt32();
            else value=!control.GetProperty("value").GetBoolean();
            var changed=await engine.ExecuteAsync(new{op="gem-control",group=special,gem=1,control=key,value},default,state.Xml);
            var roundtrip=await engine.ExecuteAsync(new{op="load",xml=changed.Xml,name="Gem controls reload"},default);
            var inspected=await engine.ExecuteAsync(new{op="gem-controls",group=special,gem=1},default,roundtrip.Xml);
            var actual=inspected.State.GetProperty("controls").EnumerateArray().Single(c=>c.GetProperty("id").GetString()==key);
            check(value is int number?actual.GetProperty("options").EnumerateArray().Single(c=>c.GetProperty("active").GetBoolean()).GetProperty("id").GetInt32()==number:actual.GetProperty("value").GetBoolean()==(bool)value,"Upstream "+key+" callback persists its selection across XML reload");
            check(roundtrip.State.GetProperty("skills").EnumerateArray().Single(g=>g.GetProperty("main").GetBoolean()).GetProperty("id").GetInt32()==main,"Specialized gem changes preserve the main skill group");
        }
        var directory=Path.Combine(Path.GetTempPath(),"pob-skill-options-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var buildId=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(directory,buildId+".xml"),state.Xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),buildId);
        var widget=new PobWidget(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        IEnumerable<ViewNode> Nodes(ViewNode n)=>new[]{n}.Concat((n.Children??[]).SelectMany(Nodes));
        var sequence=0;
        ViewSnapshot View()=>widget.Render().CreateSnapshot("skill-options",++sequence);
        async Task Idle(){var end=DateTime.UtcNow.AddSeconds(65);while(Nodes(View().Root).Any(n=>n.Id=="pob.cancel-operation")){if(DateTime.UtcNow>end)throw new TimeoutException();await Task.Delay(20);}}
        async Task Action(string id,string? text=null){await widget.OnActionAsync(new(id,"skill-options"){CommittedText=text});await Idle();}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();await Action("section.2");await Action("skill."+stages);await Action("skill-options");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.inventory.rows"),"Uncommon skill options retain the group list");
            var input=Nodes(View().Root).Single(n=>n.Id=="pob.option.mainSkillStageCount");
            check(input.Kind==ViewNodeKind.TextEntry,"Stages uses a real text-entry control in the selected skill's options");
            await Action($"skill-number.{stages}.mainSkillStageCount","4");
            check(Nodes(View().Root).Single(n=>n.Id==input.Id).TextEntryValue=="4","Committed stage count refreshes from the upstream skill context");
            await Action("undo");
            check(Nodes(View().Root).Single(n=>n.Id==input.Id).TextEntryValue!="4","Undo restores the prior stage count and refreshes the option");
            await Action("back");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.gem-replace.1")&&Nodes(View().Root).Any(n=>n.Id=="pob.gem-enabled.1"),"Common gem operations are inline beside the selected group");
            var beforeInspection=PobWorkStore.Read(directory,buildId,game)!.CommittedXml;
            var inspectionProcess=engine.ProcessId;
            await Action("skill."+main);await Action("skill."+stages);await Action("skill."+main);
            var stop=DateTime.UtcNow.AddSeconds(15);
            while((PobSkillOptions?)typeof(PobWidget).GetProperty("CurrentSkillOptions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget) is not {} current||current.Group!=main)
            {if(DateTime.UtcNow>stop)throw new TimeoutException("Inline skill context did not settle");_ = View();await Task.Delay(20);}
            check(engine.ProcessId==inspectionProcess,"Coalesced group inspection retains the existing single engine process");
            check(WorkStateTests.SameBuild(PobWorkStore.Read(directory,buildId,game)!.CommittedXml,beforeInspection),"Rapid skill inspection never changes the saved main group or build");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.skill.context"&&n.Text=="Main calculation skill"),"Inline context distinguishes the real main group");
            var resultField=typeof(PobWidget).GetField("result",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var validResult=(PobResult)resultField.GetValue(widget)!;
            var malformedState=JsonNode.Parse(validResult.State.GetRawText())!.AsObject();malformedState["skillOptions"]=new JsonObject();
            resultField.SetValue(widget,new PobResult(JsonSerializer.SerializeToElement(malformedState),validResult.Xml));
            var optionOwner=(PobSkillOptionsState)typeof(PobWidget).GetField("skillOptionsState",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget)!;optionOwner.Clear();
            check(Nodes(View().Root).Any(n=>n.ActionId=="skill-options-retry"),"Malformed inline skill metadata shows explicit Retry instead of disappearing or crashing");
            await Action("skill-options-retry");
            stop=DateTime.UtcNow.AddSeconds(15);
            while((PobSkillOptions?)typeof(PobWidget).GetProperty("CurrentSkillOptions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget) is null)
            {if(DateTime.UtcNow>stop)throw new TimeoutException("Skill option retry did not settle");_=View();await Task.Delay(20);}
            check(!Nodes(View().Root).Any(n=>n.ActionId=="skill-options-retry")&&engine.ProcessId==inspectionProcess,
                "Explicit retry bypasses malformed main-group snapshot and recovers through the existing engine");
            check(WorkStateTests.SameBuild(PobWorkStore.Read(directory,buildId,game)!.CommittedXml,beforeInspection),"Read retry leaves the saved build and main group unchanged");
            resultField.SetValue(widget,validResult);
            await Action("skill."+stages);await Action("skill-options");await Action("back");
            await Action("gem-options.1");
            check(Nodes(View().Root).Any(n=>n.Id=="pob.gem-extra.enabled"),"Gem options exposes the selected gem's upstream enabled control");
            var beforeToggle=PobWorkStore.Read(directory,buildId,game)!.CommittedXml;
            await Action("gem-extra.enabled");await Action("undo");
            check(WorkStateTests.SameBuild(PobWorkStore.Read(directory,buildId,game)!.CommittedXml,beforeToggle),"Gem option edits participate in durable Undo");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);}
    }
}
