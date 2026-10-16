using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WorkStateTests
{
    internal static async Task RunAsync(int game, string engineRoot, string runtime, string adapter, string xml, Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-pob-work-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var id=Guid.NewGuid().ToString("N");
        var working=Path.Combine(directory,id+".xml");var checkpoint=Path.Combine(directory,id+".work.json");
        File.WriteAllText(working,xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        PobEngine? engine=null;PobWidget? widget=null;var clipboard="";var sequence=0;
        ViewSnapshot Snapshot()=>widget!.Render().CreateSnapshot("work-test",++sequence);
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat((node.Children??[]).SelectMany(Nodes));
        bool Has(string name)=>Nodes(Snapshot().Root).Any(n=>n.Id==name);
        async Task Settled()
        {
            var until=DateTime.UtcNow.AddSeconds(65);
            while(Has("pob.cancel-operation")&&DateTime.UtcNow<until)await Task.Delay(20);
            if(Has("pob.cancel-operation"))throw new TimeoutException("Work did not settle.");
        }
        async Task Open()
        {
            engine=new(engineRoot,runtime,adapter);widget=new(game,engine,directory,()=>clipboard);
            WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
            await Settled();
            if(!Has("pob.section")&&!Has("pob.build-menu"))throw new IOException("Saved build did not open: "+string.Join(" ",Nodes(Snapshot().Root).Select(n=>n.Text)));
        }
        async Task Close()
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget!,WidgetLifecycleState.Background);
            await WidgetTestHost.DestroyAsync(widget!);
            check(engine!.ProcessId is null,"Unloading releases the calculation process even with saved pending work");
            widget=null;engine=null;
        }
        async Task Action(string action,string? value=null)
        {
            await widget!.OnActionAsync(new(action,"work-test"){CommittedText=value});await Settled();
        }
        PobWorkState Saved()=>PobWorkStore.Read(directory,id,game)!;
        try
        {
            await Open();
            await Action("level","77");var edited=Saved().CommittedXml;
            check(Saved().Undo.Length==1,"Immediate edit saves its undo entry in the same checkpoint");
            await Action("section.5");await Close();await Open();
            check(Snapshot().ActiveInputScopeId=="pob.scope.calcs.main"&&Nodes(Snapshot().Root).Single(n=>n.Id=="pob.undo").IsDisabled!=true,
                "Reopen restores Undo without retaining browsing navigation");
            await Action("undo");var before=Saved().CommittedXml;
            check(before!=edited&&Saved().Redo.Length==1,"Restored Undo returns to the prior committed build");
            await Close();await Open();await Action("redo");
            check(SameBuild(Saved().CommittedXml,edited)&&Saved().Redo.Length==0,"Redo survives a second process restart");

            var state=await engine!.ExecuteAsync(new{op="snapshot"},default);
            var node=state.State.GetProperty("nodes").EnumerateArray().First(n=>n.GetProperty("canAllocate").GetBoolean()&&n.GetProperty("pathCount").GetInt32() is >=2 and <=5);
            var nodeId=node.GetProperty("id").GetInt32();
            await Action("section.1");await Action("node."+nodeId);await Action("node-change");
            var preview=Saved().Pending!;
            string Summary()=>string.Join("\n",Nodes(Snapshot().Root).Where(n=>n.Id.StartsWith("pob.node.comparison.")).Select(n=>n.Text));
            var previewSummary=Summary();
            check(Has("pob.node.comparison.heading"),"Pending tree preview has an upstream impact summary");
            check(preview.Kind=="node"&&preview.NodeId==nodeId&&preview.Xml!=Saved().CommittedXml&&File.ReadAllText(working)==Saved().CommittedXml,
                "Preview is durable without applying it to the committed XML");
            await Close();await Open();
            check(Has("pob.apply")&&Snapshot().ActiveInputScopeId=="pob.scope.tree.preview"&&Snapshot().InitialFocusId=="pob.apply",
                "New widget restores the pending node comparison and its actions");
            check(Summary()==previewSummary&&!Nodes(Snapshot().Root).Any(n=>n.Id.StartsWith("pob.changed.")),
                "Restart regenerates the same comparison summary with node details collapsed");
            await Action("cancel");check(Saved().Pending is null&&SameBuild(Saved().CommittedXml,edited),"Discard persists the original build and clears the pending comparison");
            await Close();await Open();check(!Has("pob.apply"),"Discarded preview does not return on the next restart");
            await Action("section.1");await Action("node."+nodeId);await Action("node-change");await Close();await Open();
            await Action("apply");
            check(Saved().Pending is null&&SameBuild(Saved().CommittedXml,preview.Xml),"Applying a restored comparison commits the proposed build's authored values");
            await Close();await Open();await Action("undo");
            check(SameBuild(Saved().CommittedXml,edited),"Applied restored preview participates in durable Undo");

            state=await engine!.ExecuteAsync(new{op="snapshot"},default);
            var item=state.State.GetProperty("items").EnumerateArray().First();clipboard=item.GetProperty("raw").GetString()!;
            await Action("section.3");await Action("item-paste");await Action("section.5");await Close();await Open();
            check(Has("pob.slot.heading")&&Saved().ItemDraft==clipboard,"Pasted item survives leaving the page and unloading before slot selection");
            var slots=state.State.GetProperty("slots").EnumerateArray().ToArray();
            var slot=Array.FindIndex(slots,s=>s.GetProperty("name").GetString()==item.GetProperty("slot").GetString());
            await Action("replace-slot."+slot);var itemXml=Saved().Pending!.Xml;await Close();await Open();
            check(Has("pob.item.compare.names")&&Has("pob.apply"),"Equipment comparison reopens with the replaced and proposed items");
            await Action("apply");
            check(SameBuild(Saved().CommittedXml,itemXml)&&Saved().ItemDraft is null&&Saved().Pending is null,"Applying equipment consumes its saved draft");
            await Action("item-paste");await Action("back");await Close();await Open();
            check(Saved().ItemDraft is null&&!Has("pob.slot.heading"),"Explicitly discarded item draft does not return");

            state=await engine!.ExecuteAsync(new{op="snapshot"},default);
            var otherClass=state.State.GetProperty("classes").EnumerateArray().First(c=>!c.GetProperty("active").GetBoolean()).GetProperty("id").GetInt32();
            await Action("item-paste");await Action("section.0");
            await Action("menu");await Action("class."+otherClass);await Close();await Open();
            check(Has("pob.class")&&Has("pob.apply")&&Saved().Pending?.Kind=="character","Character changes restore as comparisons, not silently applied changes");
            check(Saved().ItemDraft==clipboard&&!Has("pob.item.compare.names"),"An unrelated item draft cannot replace the restored character comparison");
            await Action("cancel");
            await Action("section.3");await Action("item-resume");await Action("back");

            // A failed atomic write must not publish a preview or damage prior work.
            var safeXml=File.ReadAllText(working);var safeCheckpoint=File.ReadAllText(checkpoint);
            File.Move(checkpoint,checkpoint+".held");Directory.CreateDirectory(checkpoint);
            try
            {
                await Action("level","78");
                check(File.ReadAllText(working)==safeXml&&Has("pob.feedback.toast"),"Failed checkpoint write leaves the previous committed work intact and reports failure");
            }
            finally {Directory.Delete(checkpoint);File.Move(checkpoint+".held",checkpoint);}
            check(File.ReadAllText(checkpoint)==safeCheckpoint,"Failed save does not replace the previous checkpoint");
            await Action("level","79");var recovered=Saved().CommittedXml;
            await Close();
            File.WriteAllText(working,safeXml); // Simulate exit between checkpoint commit and XML mirror write.
            await Open();
            var restored=await engine!.ExecuteAsync(new{op="snapshot"},default);
            check(SameBuild(restored.Xml,recovered),"Atomic checkpoint recovers latest work even when the XML mirror is stale");

            var oldUndo=Saved().Undo;
            await Action("new");var newId=File.ReadAllText(Path.Combine(directory,"selected.txt"));
            check(newId!=id&&Nodes(Snapshot().Root).Single(n=>n.Id=="pob.undo").IsDisabled==true,"New build starts with independent history");
            await Action("saved."+id);
            check(Saved().Undo.SequenceEqual(oldUndo)&&Nodes(Snapshot().Root).Single(n=>n.Id=="pob.undo").IsDisabled!=true,"Returning to another saved build restores that build's history");
            await Close();

            File.WriteAllText(checkpoint,"{broken json");
            try {PobWorkStore.Read(directory,id,game);check(false,"Corrupt checkpoint rejected");}
            catch(JsonException){check(File.ReadAllText(checkpoint)=="{broken json","Corrupt checkpoint is retained instead of silently replaced");}
        }
        finally {if(widget is not null)await WidgetTestHost.DestroyAsync(widget);if(engine is not null)await engine.DisposeAsync();}
    }

    internal static bool SameBuild(string first,string second)
    {
        // Lua table iteration changes XML map order between processes. Preserve
        // every value and ordered list (including gems, skills and loadouts),
        // normalizing only known keyed maps and unordered node membership.
        XElement Normalize(string xml)
        {
            var root=XDocument.Parse(xml).Root!;
            foreach(var element in root.DescendantsAndSelf())
            {
                // These defaults are explicit in upstream SkillsTab:Load and
                // Build:Load. Loading historical exports materializes them.
                if(element.Name.LocalName=="Skill"&&(string?)element.Attribute("includeInFullDPS") is null or "nil")
                    element.SetAttributeValue("includeInFullDPS","false");
                if(element.Name.LocalName=="TimelessData"&&element.Attribute("socketFilterDistance") is null)
                    element.SetAttributeValue("socketFilterDistance","0");
                if(element.Name.LocalName=="Skill")
                    foreach(var key in new[]{"mainActiveSkill","mainActiveSkillCalcs"})
                        if((string?)element.Attribute(key) is null or "nil")element.SetAttributeValue(key,"1");
                if(element.Name.LocalName=="Gem")
                {
                    foreach(var (key,value) in new[]{("count","1"),("enableGlobal2","false"),("corruptLevel","0"),("corrupted","false")})
                        if((string?)element.Attribute(key)=="nil")element.SetAttributeValue(key,value);
                    foreach(var key in new[]{"statSetIndex","statSetIndexCalcs"})
                        if((string?)element.Attribute(key)=="nil")element.Attribute(key)!.Remove();
                }
                var attributes=element.Attributes().OrderBy(a=>a.Name.ToString(),StringComparer.Ordinal).ToArray();
                element.ReplaceAttributes(attributes);
                foreach(var key in new[]{"nodes","strNodes","dexNodes","intNodes"})
                    if(element.Attribute(key) is {} value)value.Value=string.Join(',',value.Value.Split(',').Order(StringComparer.Ordinal));
                if(element.Name.LocalName=="Spec"&&element.Attribute("nodes") is not null)
                    element.Elements("URL").Remove(); // Redundant encoded representation of the same allocation.
            }
            foreach(var element in root.Descendants().Where(e=>e.Name.LocalName is "Config" or "ConfigSet" or "ItemSet" or "Sockets" or "Calcs").ToArray())
                element.ReplaceNodes(element.Elements().OrderBy(e=>e.Name.ToString(),StringComparer.Ordinal)
                    .ThenBy(e=>(string?)e.Attribute("name")??(string?)e.Attribute("nodeId")??(string?)e.Attribute("id"),StringComparer.Ordinal)
                    .ThenBy(e=>(string?)e.Attribute("subsection"),StringComparer.Ordinal).ToArray());
            root.ReplaceNodes(root.Elements().OrderBy(e=>e.Name.ToString(),StringComparer.Ordinal).ToArray());
            return root;
        }
        var a=Normalize(first);var b=Normalize(second);
        if(XNode.DeepEquals(a,b))return true;
        var evidence=Path.Combine(Path.GetTempPath(),"pob-work-diff-"+Guid.NewGuid().ToString("N"));
        File.WriteAllText(evidence+"-actual.xml",a.ToString());File.WriteAllText(evidence+"-expected.xml",b.ToString());
        Console.WriteLine("Build comparison evidence: "+evidence);
        return false;
    }
}
