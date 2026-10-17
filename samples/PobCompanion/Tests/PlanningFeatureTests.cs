using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class PlanningFeatureTests
{
    internal static async Task RunAsync(string root,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"pob-planning-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var id=Guid.NewGuid().ToString("N");var file=Path.Combine(directory,id+".xml");File.WriteAllText(file,xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        await using var engine=new PobEngine(root,runtime,adapter);
        var widget=new PobWidget(2,engine,directory,()=>"+123 to maximum Life\n+20% to Fire Resistance\nnot a real modifier");WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
        object? Get(string name)=>typeof(PobWidget).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget);
        PobResult Result()=>(PobResult)Get("result")!;
        JsonElement[] Array(string name)=>Result().State.GetProperty(name).EnumerateArray().ToArray();
        JsonElement Group()=>Array("customModGroups").Single(g=>g.GetProperty("id").GetInt32()==(int)Get("customGroup")!);
        IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat((node.Children??[]).SelectMany(Nodes));
        var sequence=0;
        ViewSnapshot View(){try{return widget.Render().CreateSnapshot("planning-test",++sequence);}catch(ProtocolValidationException e){Console.WriteLine(JsonSerializer.Serialize(e.Errors));throw;}}
        async Task Idle(){var timer=Stopwatch.StartNew();while((bool)Get("busy")!){if(timer.Elapsed.TotalSeconds>65)throw new TimeoutException();await Task.Delay(25);}View();}
        async Task Action(string action,string? value=null){await widget.OnActionAsync(new(action,"fixture"){CommittedText=value});await Idle();if(Get("feedback") is {} error)throw new Exception("Feedback after "+action+": "+JsonSerializer.Serialize(error));}
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Idle();
            await Action("section.4");
            var category=Nodes(View().Root).Single(n=>n.Text=="Custom modifiers"&&n.Id.StartsWith("pob.config.category."));
            await Action("config-section."+category.Id.Split('.').Last());
            check(Nodes(View().Root).Any(n=>n.Id=="pob.custom-enabled")&&Nodes(View().Root).Any(n=>n.Id=="pob.custom-new-line"),"Custom modifier group exposes labelled inputs and a distinct enable switch");
            var count=Array("customModGroups").Length;await Action("custom-add");await Action("custom-title","Boss setup");await Action("custom-paste");
            check(Array("customModGroups").Length==count+1&&Group().GetProperty("title").GetString()=="Boss setup","Add and rename a custom modifier group through widget actions");
            check(Group().GetProperty("lines").GetArrayLength()==3&&!Group().GetProperty("lines")[2].GetProperty("supported").GetBoolean(),"Multiline clipboard modifiers retain unsupported lines and identify them explicitly");
            using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"modifier-lines",400))
            {using var range=await host.AcquireAsync("pob.custom.lines",0,3);check(range.Range.Items.Count==3,"Custom modifier editing rows materialize through the virtualized list");}
            await Action("custom-line.2","");await Action("custom-new-line","+15 to maximum Mana");
            check(Group().GetProperty("text").GetString()!.Contains("+15 to maximum Mana")&&!Group().GetProperty("text").GetString()!.Contains("not a real modifier"),"Individual modifier editing preserves other lines");
            var saved=File.ReadAllText(file);await Action("custom-compare");
            check(Get("featureReading") is not null&&File.ReadAllText(file)==saved,"Custom modifier comparison is readable and does not modify saved work");await Action("feature-close");
            await Action("custom-enabled");check(!Group().GetProperty("enabled").GetBoolean(),"Custom modifier enable action saves upstream state");
            await Action("undo");check(Group().GetProperty("enabled").GetBoolean(),"Undo restores custom modifier enable state");await Action("redo");check(!Group().GetProperty("enabled").GetBoolean(),"Redo restores custom modifier disable state");
            await Action("custom-delete");await Action("feature-confirm");check(Array("customModGroups").Length==count,"Confirmed deletion removes only the selected modifier group");await Action("undo");
            await Action("search","Boss setup");check(Nodes(View().Root).Any(n=>n.Id.StartsWith("pob.custom-open.")),"Configuration search finds custom modifier groups");
            var condition=Array("conditions").Select((c,i)=>(c,i)).First(c=>c.c.GetProperty("value").ValueKind is JsonValueKind.True or JsonValueKind.False);
            saved=File.ReadAllText(file);await Action("config-compare."+condition.i);check(Get("featureReading") is not null&&File.ReadAllText(file)==saved,"Configuration toggle comparisons leave committed settings unchanged");await Action("feature-close");
            await Action("section.2");var skill=Array("skills").First(s=>s.GetProperty("gems").GetArrayLength()>0&&!s.GetProperty("generated").GetBoolean());await Action("skill."+skill.GetProperty("id").GetInt32());
            saved=File.ReadAllText(file);await Action("gem-quality.1");check(Get("featureReading") is not null&&File.ReadAllText(file)==saved,"Quality effects use the selected gem's upstream tooltip without modifying the build");await Action("feature-close");
            await Action("group-manage");await Action("gem-add");saved=File.ReadAllText(file);
            await Action("gem-sort.CombinedDPS");
            var gems=(JsonElement[])Get("gemCatalog")!;
            check(gems.Any(g=>g.TryGetProperty("comparison",out var number)&&number.GetDouble()>0)&&File.ReadAllText(file)==saved,"Lazy upstream gem ranking produces totals without committing any gem replacement");
            await Action("back");await Action("back");
            await Action("section.3");var item=Array("items").First();await Action("item."+item.GetProperty("id").GetInt32());
            saved=File.ReadAllText(file);await Action("item-manage");
            var editor=(JsonElement)Get("itemEditor")!;check(editor.GetProperty("mods").GetArrayLength()>0&&File.ReadAllText(file)==saved,"Item settings expose real upstream modifiers without saving inspection changes");
            var firstMod=editor.GetProperty("mods")[0];await Action("item-mod."+firstMod.GetProperty("id").GetInt32());
            editor=(JsonElement)Get("itemEditor")!;
            check(editor.GetProperty("mods")[0].GetProperty("enabled").GetBoolean()!=firstMod.GetProperty("enabled").GetBoolean(),"Item modifier toggle applies and refreshes its displayed state");
            await Action("undo");editor=(JsonElement)Get("itemEditor")!;
            check(editor.GetProperty("mods")[0].GetProperty("enabled").GetBoolean()==firstMod.GetProperty("enabled").GetBoolean(),"Undo refreshes the item settings in place");
            var editable=editor.GetProperty("controls").EnumerateArray().Select((c,i)=>(c,i)).FirstOrDefault(x=>x.c.GetProperty("options").GetArrayLength()>1);
            if(editable.c.ValueKind==JsonValueKind.Object)
            {
                await Action("item-control."+editable.i);var option=editable.c.GetProperty("options").EnumerateArray().First(o=>!o.GetProperty("active").GetBoolean());
                await Action("item-choice."+editable.i+"."+option.GetProperty("id").GetInt32());
                editor=(JsonElement)Get("itemEditor")!;
                check(editor.GetProperty("controls")[editable.i].GetProperty("options").EnumerateArray().Single(o=>o.GetProperty("active").GetBoolean()).GetProperty("title").GetString()==option.GetProperty("title").GetString(),"Item selector applies upstream rune/variant choice and refreshes selection");
                await Action("undo");
            }
            await Action("back");
            // This guide chooses another ascendancy branch. Allocate the actual
            // upstream Runic Meridians node in this isolated test build.
            typeof(PobWidget).GetField("section",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(widget,"Tree");
            var runic=Array("nodes").Single(n=>n.GetProperty("name").GetString()=="Runic Meridians");
            if(!runic.GetProperty("allocated").GetBoolean())
            {await Action("node."+runic.GetProperty("id").GetInt32());await Action("node-change");await Action("apply");}
            check(Array("nodes").Single(n=>n.GetProperty("id").GetInt32()==runic.GetProperty("id").GetInt32()).GetProperty("allocated").GetBoolean(),"Rune test actually allocates Runic Meridians");
            await Action("section.3");
            var runes=Array("characterRunes");check(runes.Length>0,"Runic Meridians exposes the upstream character rune slots");
            if(runes.Length>0)
            {
                await Action("character-rune.0");check(Nodes(View().Root).Any(n=>n.Id=="pob.choices"&&n.IndexedCollection is not null),"Character runes use a searchable virtualized picker");
                using(var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"rune-options",800))
                {using var range=await host.AcquireAsync("pob.choices",0,6);check(range.Range.Items.Count==6,"Rune names and rich descriptions materialize as valid collection rows");}
                var option=runes[0].GetProperty("options").EnumerateArray().First(o=>!o.GetProperty("active").GetBoolean());
                await Action("rune-choice.0."+option.GetProperty("id").GetInt32());
                check(Array("characterRunes")[0].GetProperty("options").EnumerateArray().Single(o=>o.GetProperty("active").GetBoolean()).GetProperty("title").GetString()==option.GetProperty("title").GetString(),"Rune selection follows the upstream slot callback and persists");
            }
            typeof(PobWidget).GetField("section",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(widget,"Tree");
            var node=Array("nodes").First(n=>n.GetProperty("allocated").GetBoolean()&&n.GetProperty("interactive").GetBoolean());await Action("node."+node.GetProperty("id").GetInt32());await Action("node-note","Take this after upgrading your weapon.");
            check(Array("nodes").Single(n=>n.GetProperty("id").GetInt32()==node.GetProperty("id").GetInt32()).GetProperty("note").GetString()=="Take this after upgrading your weapon.","Node author notes save on the selected passive");
            var reopened=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(file),name="Planning reload"},default);
            check(reopened.State.GetProperty("nodes").EnumerateArray().Single(n=>n.GetProperty("id").GetInt32()==node.GetProperty("id").GetInt32()).GetProperty("note").GetString()=="Take this after upgrading your weapon.","Node notes survive XML export and reload");
            await Action("menu");await Action("library");await Action("library-search","Monk");
            check(Nodes(View().Root).Single(n=>n.Id=="pob.saved-builds").IndexedCollection!.Count==1&&Nodes(View().Root).Any(n=>n.Id=="pob.library.class"),"Library search finds saved class metadata and exposes a class filter");
        }
        finally{await WidgetTestHost.DestroyAsync(widget);Directory.Delete(directory,true);}
    }
}
