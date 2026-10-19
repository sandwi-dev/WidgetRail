using System.Text.Json.Nodes;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class ManagementTests
{
    internal static async Task RunAsync(int game,string engineRoot,string runtime,string adapter,string xml,Action<bool,string> check)
    {
        var directory=Path.Combine(Path.GetTempPath(),"widgetrail-pob-management-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);var id=Guid.NewGuid().ToString("N");
        var working=Path.Combine(directory,id+".xml");var checkpoint=Path.Combine(directory,id+".work.json");
        File.WriteAllText(working,xml);File.WriteAllText(Path.Combine(directory,"selected.txt"),id);
        PobWidget? widget=null;PobEngine? engine=null;var sequence=0;
        IEnumerable<ViewNode> Nodes(ViewNode n)=>new[]{n}.Concat((n.Children??[]).SelectMany(Nodes));
        ViewSnapshot Snapshot()=>widget!.Render().CreateSnapshot("management-test",++sequence);
        bool Has(string name)=>Nodes(Snapshot().Root).Any(n=>n.Id==name);
        async Task Settled()
        {
            var end=DateTime.UtcNow.AddSeconds(65);
            while(Has("pob.cancel-operation")&&DateTime.UtcNow<end)await Task.Delay(20);
            if(Has("pob.cancel-operation"))throw new TimeoutException("Management operation did not finish.");
        }
        async Task Open(){engine=new(engineRoot,runtime,adapter);widget=new(game,engine,directory);WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);await Settled();}
        async Task Close(){await WidgetTestHost.DestroyAsync(widget!);widget=null;engine=null;}
        async Task Action(string name,string? value=null){await widget!.OnActionAsync(new(name,"management-test"){CommittedText=value});await Settled();}
        try
        {
            await Open();await Action("level","77");
            var validRecord=File.ReadAllText(checkpoint);var committed=PobWorkStore.Read(directory,id,game)!.CommittedXml;
            await Action("stat.Life");
            check(!Has("pob.main-group")&&!Has("pob.main-skill.explanation")&&!Has("pob.full-dps.explanation")&&Has("pob.inspector.title"),"Life inspector excludes unrelated skill and Full DPS context");
            await Action("stat.CombinedDPS");check(Has("pob.main-group"),"Main-skill DPS retains the skill selector and relevant context");
            await Action("stat.FullDPS");check(!Has("pob.main-group")&&!Has("pob.main-skill.explanation"),"Full DPS has group context rather than the selected skill's description");
            await Action("section.1");var treeSurface=widget!.Render().Surface!;var treeWidth=treeSurface.PreferredWidth;
            var stableWorkspace=true;
            foreach(var section in new[]{0,2,3,4,5,1})
            {
                await Action("section."+section);
                stableWorkspace&=widget.Render().Surface==treeSurface;
            }
            check(stableWorkspace&&treeSurface.HeightMode==WidgetSurfaceAxisMode.FillAvailable&&treeSurface.WidthMode==WidgetSurfaceAxisMode.Preferred,
                "Every planning section uses the same bounded width and available-height policy");
            await Action("menu");
            check(Nodes(Snapshot().Root).Single(n=>n.Id=="pob.rename").TextEntryInputKind==TextEntryInputKind.Prose,"Current build names enable offline word completion");
            if(game==2)
            {
                await Action("game-export");
                check(Nodes(Snapshot().Root).Single(n=>n.Id=="pob.game-export.name").TextEntryInputKind==TextEntryInputKind.Prose,"Export build names enable the same word completion");
                await Action("back");
            }
            check(widget.Render().Surface!.PreferredWidth<treeWidth&&!Has("pob.navigation")&&Has("pob.field.label.name")&&Has("pob.field.label.level"),"Build menu has its own compact size and persistent field labels");
            check(widget.Render().Surface!.HeightMode==WidgetSurfaceAxisMode.Preferred,"Management does not inherit the planning workspace height");
            check(Has("pob.manage.character")&&Has("pob.manage.import")&&Has("pob.manage.export")&&Has("pob.new"),"Build menu groups character, import, export and new-build actions");
            check(Nodes(Snapshot().Root).Single(n=>n.Id=="pob.import").TextEntryMaximumLength==2048&&Has("pob.import.example.link")&&Has("pob.import.example.file"),"Import field supports long paths and provides nearby link and file examples");
            var linkHelp=Nodes(Snapshot().Root).Single(n=>n.Id=="pob.import.example.link").Text!;
            check(linkHelp.Contains("https://pobb.in/")&&linkHelp.Contains("copy its PoB code")&&!linkHelp.Contains("/profile/"),"Import guidance offers supported share links and manual poe.ninja codes");
            var online=Nodes(Snapshot().Root).Single(n=>n.Id=="pob.open-online-import").Intent!;
            check(online.Routing==WidgetIntentRouting.Windows&&online.Payload.GetProperty("url").GetString()==$"https://pob.cool/poe{game}","Online import explicitly uses the Windows browser for the correct game");
            var browserLinks=Nodes(Snapshot().Root).Single(n=>n.Id=="pob.import.browser-links");
            var ninja=Nodes(browserLinks).Single(n=>n.Id=="pob.open-poe-ninja").Intent!;
            check(ninja.Routing==WidgetIntentRouting.Windows&&ninja.Payload.GetProperty("url").GetString()==$"https://poe.ninja/poe{game}/builds","poe.ninja opens the correct game's builds in the Windows browser");
            check(Nodes(browserLinks).Any(n=>n.Id=="pob.open-online-import")&&!Nodes(browserLinks).Any(n=>n.Id=="pob.paste"),"External sites share their own row, separate from import controls");
            check(Has("pob.import.online-help")&&Has("pob.paste")&&!Nodes(Snapshot().Root).Any(n=>n.Kind==ViewNodeKind.WebBrowser),"Browser handoff leaves clear return-and-paste instructions without an embedded page");
            var importSource=Path.Combine(directory,"replacement-source.xml");File.WriteAllText(importSource,xml);
            var previousName=File.ReadAllText(Path.Combine(directory,id+".name.txt"));
            await Action("import-mode.replace");await Action("import",importSource);
            check(Snapshot().ActiveInputScopeId=="pob.import-confirmation.scope"&&File.ReadAllText(checkpoint)==validRecord,"Replacement waits for confirmation without changing saved work");
            await Action("import-cancel");check(File.ReadAllText(checkpoint)==validRecord,"Cancelling replacement keeps the original checkpoint");
            await Action("import",importSource);await Action("import-confirm");
            var replaced=PobWorkStore.Read(directory,id,game)!;
            check(!WorkStateTests.SameBuild(replaced.CommittedXml,committed)&&replaced.Undo.Length>0&&
                File.ReadAllText(Path.Combine(directory,id+".name.txt"))==previousName&&
                Directory.EnumerateFiles(directory,"*.work.json").Count()==1,"Confirmed import replaces the current contents while retaining its name, identity and Undo");
            await Close();await Open();await Action("undo");
            check(WorkStateTests.SameBuild(PobWorkStore.Read(directory,id,game)!.CommittedXml,committed),"Replacement can be undone after destroying and reopening the widget and engine");
            await Action("redo");check(WorkStateTests.SameBuild(PobWorkStore.Read(directory,id,game)!.CommittedXml,replaced.CommittedXml),"Redo reapplies the imported replacement");
            await Action("undo");
            await Action("library");await Action("library-rename","Renamed test build");
            check(Nodes(Snapshot().Root).Single(n=>n.Id=="pob.library.rename").TextEntryInputKind==TextEntryInputKind.Prose,"Saved build names enable the same word completion");
            check(File.ReadAllText(Path.Combine(directory,id+".name.txt"))=="Renamed test build","Library can rename its selected build");
            await Action("library-rename",new string('N',100));
            check(Nodes(Snapshot().Root).Single(n=>n.Id=="pob.library.rename").TextEntryValue?.Length==100,"Every accepted build name can render in the library text field");
            await Action("library-rename","Renamed test build");
            await Action("library-duplicate");
            var copy=Directory.EnumerateFiles(directory,"*.work.json").Select(p=>Path.GetFileName(p).Split('.')[0]).Single(key=>key!=id);
            check(WorkStateTests.SameBuild(PobWorkStore.Read(directory,copy,game)!.CommittedXml,committed)&&PobWorkStore.Read(directory,copy,game)!.Undo.Length==0,"Duplicate creates an independent build with fresh history");
            await Action("library-search","no matching build");check(Has("pob.library.empty"),"Library search has a usable empty state");
            await Action("library-search","");await Action("library-sort.name");await Action("library-select."+copy);
            var selectionFocus=widget.Render().FocusGroupEntryRequest;
            check(selectionFocus?.GroupId=="pob.library.open-action"&&Nodes(Snapshot().Root).Single(n=>n.Id==selectionFocus.GroupId).InitialChildFocusId=="pob.library.open","Activating a saved build explicitly focuses Open build, independently of remembered detail focus");
            await Action("library-select."+copy);
            check(widget.Render().FocusGroupEntryRequest?.RequestId>selectionFocus!.RequestId,"Activating the same saved build issues a fresh Open build focus request");
            await Action("library-delete");check(Snapshot().ActiveInputScopeId=="pob.delete-confirmation.scope","Delete uses an isolated confirmation scope");
            await Action("delete-cancel");check(File.Exists(Path.Combine(directory,copy+".work.json")),"Cancel leaves the selected build intact");
            await Action("library-delete");await Action("delete-confirm");
            check(!File.Exists(Path.Combine(directory,copy+".work.json"))&&File.Exists(checkpoint),"Confirmed deletion removes only the selected copy");
            await Close();

            // A corrupt history must recover the checkpoint's newer committed
            // build, not an older XML mirror. Keep exact failed bytes separately.
            var broken=JsonNode.Parse(validRecord)!;broken["Work"]!["Undo"]=new JsonArray("invalid compressed history");
            var damaged=broken.ToJsonString();File.WriteAllText(checkpoint,damaged);File.WriteAllText(working,xml);
            await Open();
            check(Has("pob.section")&&Has("pob.feedback.toast")&&WorkStateTests.SameBuild(PobWorkStore.Read(directory,id,game)!.CommittedXml,committed),"Broken history falls back to the latest committed build in the checkpoint");
            check(Directory.EnumerateFiles(directory,id+".recovery.*.json").Any(p=>File.ReadAllText(p)==damaged),"Failed recovery bytes are preserved before normal editing resumes");
            await Action("level","78");
            check(Directory.EnumerateFiles(directory,id+".recovery.*.json").Any(p=>File.ReadAllText(p)==damaged),"Subsequent edits cannot overwrite the preserved recovery record");
            await Close();

            broken=JsonNode.Parse(validRecord)!;broken["Work"]!["Undo"]=new JsonObject{["invalid"]="history shape"};
            File.WriteAllText(checkpoint,broken.ToJsonString());File.WriteAllText(working,xml);
            await Open();
            check(Has("pob.section")&&WorkStateTests.SameBuild(PobWorkStore.Read(directory,id,game)!.CommittedXml,committed),"Invalid history shape still recovers the newer committed checkpoint independently");
            await Close();

            File.WriteAllText(checkpoint,"{unreadable recovery");File.WriteAllText(working,committed);
            await Open();check(Has("pob.section")&&Has("pob.feedback.toast")&&PobWorkStore.Read(directory,id,game) is not null,"Unreadable recovery record falls back to saved XML and repairs the active record");
            check(Directory.EnumerateFiles(directory,id+".recovery.*.json").Any(p=>File.ReadAllText(p)=="{unreadable recovery"),"Unreadable recovery data is retained unchanged");
            await Close();

            broken=JsonNode.Parse(validRecord)!;
            broken["Work"]!["Pending"]=new JsonObject{["Xml"]=broken["Work"]!["CommittedXml"]!.GetValue<string>(),["Kind"]="node",["NodeId"]=int.MaxValue};
            File.WriteAllText(checkpoint,broken.ToJsonString());await Open();
            check(Has("pob.section")&&!Has("pob.apply")&&Has("pob.feedback.toast"),"Undisplayable preview falls back to committed calculations without applying the proposal");
            await Close();

            File.WriteAllText(checkpoint,damaged);File.SetAttributes(checkpoint,FileAttributes.ReadOnly);
            try
            {
                await Open();check(Has("pob.section"),"Saved build remains viewable when recovery preservation cannot finish");
                await Action("level","79");check(File.ReadAllText(checkpoint)==damaged,"Read-only recovery protects the original from later edits");
                await Close();
            }
            finally{File.SetAttributes(checkpoint,FileAttributes.Normal);}

            File.WriteAllText(checkpoint,"{invalid");File.WriteAllText(working,"invalid xml");await Open();
            check(!Has("pob.section")&&Has("pob.feedback.toast")&&File.ReadAllText(checkpoint)=="{invalid","When both sources fail the widget reports failure without crashing or deleting data");
            await Close();File.WriteAllText(checkpoint,validRecord);File.WriteAllText(working,committed);await Open();
            await Action("library");await Action("library-delete");await Action("delete-confirm");
            check(Has("pob.library.workspace")&&!File.Exists(checkpoint)&&!File.Exists(Path.Combine(directory,"selected.txt")),"Deleting the current build returns to the library and clears its startup selection");
            await Action("back");
            check(Nodes(Snapshot().Root).Single(n=>n.Id=="pob.open-online-import").Intent?.Routing==WidgetIntentRouting.Windows&&Has("pob.paste"),"Windows browser import is also available before a build is loaded");
        }
        finally{if(widget is not null)await WidgetTestHost.DestroyAsync(widget);if(engine is not null)await engine.DisposeAsync();}
    }
}
