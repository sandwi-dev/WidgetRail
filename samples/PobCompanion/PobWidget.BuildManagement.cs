using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string librarySearch="",librarySort="recent";
    private string? librarySelection,deleteBuildId;
    private bool replaceOnImport;
    private sealed record BuildImport(string TargetId,string OriginalXml,PobResult Loaded,int Renamed);
    private BuildImport? pendingImport;
    private const string OnlineImportHelp="Find a build on poe.ninja, or use pob.cool to import your character. Copy its PoB code, then return here and choose Paste build from clipboard.";
    private bool ManagementView=>detailKind is "menu" or "library" or "game-export" or "sets" or "build-comparison";

    private WidgetElement OnlineBuildLinks()=>UI.Stack("pob.import.websites",
        WebsitePreference(),
        UI.Row("pob.import.browser-links",
            WebsiteButton("Open poe.ninja","open-poe-ninja",$"https://poe.ninja/poe{game}/builds"),
            WebsiteButton("Open pob.cool","open-online-import",$"https://pob.cool/poe{game}")).Classes("pob-toolbar"),
        Text("pob.cool: Controller not supported. Use a mouse and keyboard.","import.controller-warning","pob-small pob-controller-warning"))
        .Classes("pob-field");

    private static WidgetElement Field(string label,WidgetElement input,string id)=>
        UI.Stack("pob.field."+id,Text(label,"field.label."+id,"pob-small"),input).Classes("pob-field");
    private WidgetElement ImportField()=>Field("Build link or XML file path",
        UI.Stack("pob.import.field",
            UI.TextEntry("","Build link or XML file path","import","pob.import",2048).Classes("pob-input"),
            Text("PoB share link: https://pobb.in/your-build-code. For poe.ninja, copy its PoB code and choose Paste build from clipboard.","import.example.link","pob-small pob-muted"),
            Text(@"For an exported XML build, enter its full Windows path, such as C:\Users\YourName\Documents\PoB\MyBuild.xml","import.example.file","pob-small pob-muted")).Classes("pob-field"),"import");
    private static WidgetElement ManagementSection(string title,string id,params WidgetElement[] content)=>
        UI.Stack("pob.manage."+id,new[]{Text(title,"manage.heading."+id,"pob-heading")}.Concat(content).ToArray()).Classes("pob-card");
    private WidgetElement ManagementHeader(string title)=>UI.Row("pob.management.header",
        Button("Back","back"),Text(title,"management.title","pob-title")).Classes("pob-toolbar");

    private WidgetElement BuildMenu()
    {
        if(pendingBefore is not null)
        {
            var comparison=new List<WidgetElement>{Text("Character comparison","management.title","pob-title"),
                Text($"Class: {S(pendingBefore.State,"class")} → {S(State,"class")}","class","pob-heading"),
                Text($"Ascendancy: {S(pendingBefore.State,"ascendancy","None")} → {S(State,"ascendancy","None")}","ascendancy","pob-muted")};
            AddComparison(comparison,pendingBefore.State,State,"character.comparison");
            return UI.VerticalScroll("pob.build-menu",comparison.ToArray()).Classes("pob-scroll","pob-panel");
        }
        var character=new List<WidgetElement>{UI.Grid("pob.manage.identity",[GridTrack.Auto()],[GridTrack.Star(3),GridTrack.Star()],
            Field("Build name",UI.TextEntry(BuildName(),"Build name","rename","pob.rename",100).Classes("pob-input"),"name"),
            Field("Character level",UI.TextEntry(S(State,"level"),"Character level","level","pob.level").Classes("pob-input"),"level").InGrid(column:1)).Spacing(0,12)};
        var choices=new List<WidgetElement>();
        foreach(var (field,kind,title) in new[]{("classes","class","Class"),("ascendancies","ascendancy","Ascendancy")})
            if(A(State,field).Length>0)choices.Add(SelectWithUnavailable(title,A(State,field).Select(o=>new SelectOption(kind+I(o,"id"),S(o,"title"),kind+"."+I(o,"id"),B(o,"active"))).ToArray(),"pob."+kind).Classes("pob-select").InGrid(column:choices.Count));
        if(choices.Count>0)character.Add(UI.Grid("pob.manage.class",[GridTrack.Auto()],choices.Select(_=>GridTrack.Star()).ToArray(),choices.ToArray()).Spacing(0,12));
        if(recoveryReadOnly)character.Insert(0,Text("Recovered build is read-only. Duplicate it from Saved builds to continue editing.","recovery.readonly","pob-muted"));
        character.Add(Text("PoB engine · "+S(State,"engineVersion","Unknown"),"engine-version","pob-small pob-muted"));
        if(Capabilities.CanCreateLoadout)
        {
            character.Add(Field("Save current setup as a new loadout",UI.TextEntry("","Loadout name, e.g. Midgame","loadout-create","pob.loadout-create",60).Classes("pob-input"),"loadout-create"));
            character.Add(Text("Creates independent copies of the current tree, equipment, skills and configuration. Switch between saved setups using Loadout above.","loadout-create.help","pob-small pob-muted"));
        }
        return UI.Stack("pob.build-menu",ManagementHeader("Build & import"),
            UI.VerticalScroll("pob.manage.scroll",
                ManagementSection("Your builds","library",UI.Row("pob.manage.library.actions",
                    Button($"Saved builds ({savedBuilds.Length})","library"),Button("New build","new"),Button("Manage sets…","sets"),Button("Compare builds & loadouts","build-comparison").Disabled(pastedItem is not null)).Classes("pob-toolbar")),
                ManagementSection("Current build","character",character.ToArray()),
                ManagementSection("Import a build","import",
                    OnlineBuildLinks(),
                    Text(OnlineImportHelp,"import.online-help","pob-small pob-muted"),
                    UI.Row("pob.manage.paste",Button("Paste build from clipboard","paste"),
                        UI.Select("Destination",[new("new","New build","import-mode.new",!replaceOnImport),new("replace","Replace current build","import-mode.replace",replaceOnImport)],"pob.import-mode").Classes("pob-select")).Classes("pob-toolbar"),
                    Text(replaceOnImport?"Review the confirmation before replacing this build. Undo restores its previous contents.":"Imports create a separate saved build.","import.help","pob-muted"),
                    ImportField()),
                ManagementSection("Export","export",UI.Row("pob.manage.export.actions",
                    (gameDefinition.SupportsGameFileExport?new[]{Button("Copy PoB import code","export"),Button("Save XML","export-file"),Button("Export to PoE2…","game-export")}:
                        new[]{Button("Copy PoB import code","export"),Button("Save XML","export-file")}).Cast<WidgetElement>().ToArray()).Classes("pob-toolbar")))
                .Classes("pob-scroll")).Classes("pob-workspace");
    }

    private WidgetElement LibraryDetails()
    {
        var selected=savedBuilds.FirstOrDefault(b=>b.Id==librarySelection);
        if(selected.Id is null)return UI.Stack("pob.library.detail",Text("No matching builds","library.empty","pob-heading"),Text("Try another search, or import a build.","library.empty.help","pob-muted")).Classes("pob-panel");
        return UI.Stack("pob.library.detail",
            Text(selected.Name,"library.selected.title","pob-heading"),
            Text(selected.Id==documentId?"Current build":"Saved build","library.selected.status","pob-muted"),
            UI.Stack("pob.library.open-action",Button("Open build","saved."+selected.Id,"library.open").FocusLeft("pob.saved-builds")).RememberChildFocus("pob.library.open"),
            Field("Build name",UI.TextEntry(selected.Name,"Build name","library-rename","pob.library.rename",100).FocusLeft("pob.saved-builds").Classes("pob-input"),"library-name"),
            Button("Duplicate build","library-duplicate").FocusLeft("pob.saved-builds"),
            Button("Compare builds & loadouts","build-comparison").Disabled(result is null||pendingBefore is not null||pastedItem is not null).FocusLeft("pob.saved-builds"),
            Button("Delete build…","library-delete").FocusLeft("pob.saved-builds"))
            .RememberChildFocus("pob.library.open").Classes("pob-panel","pob-library-detail");
    }

    private WidgetView WithBuildConfirmation(WidgetView view)
    {
        if(pendingImport is not null)return view.WithModal(new("pob.import-confirmation","Replace current build?",
            UI.Stack("pob.import-content",Text(BuildName(),"import.target","pob-heading"),
                Text("Replace this build's contents with the imported build? Its name stays the same. You can Undo the replacement.","import.confirm.help"),
                UI.Row("pob.import.actions",Button("Cancel","import-cancel"),Button("Replace build","import-confirm")).Classes("pob-toolbar")).Classes("pob-card"),
            "pob.import-cancel","import-cancel"));
        if(deleteBuildId is not {} id)return view;
        var name=savedBuilds.FirstOrDefault(b=>b.Id==id).Name??"this build";
        return view.WithModal(new("pob.delete-confirmation","Delete build?",
            UI.Stack("pob.delete-content",Text(name,"delete.name","pob-heading"),
                Text("This deletes the saved build, imported original, pending changes and Undo/Redo history. Exported XML and preserved recovery copies are kept.","delete.help"),
                UI.Row("pob.delete.actions",Button("Cancel","delete-cancel"),Button("Delete build","delete-confirm")).Classes("pob-toolbar")).Classes("pob-card"),
            "pob.delete-cancel","delete-cancel"));
    }

    private void ImportBuild(string? input)=>Work(async token=>
    {
        if(replaceOnImport&&result is not null&&(recoveryReadOnly||pastedItem is not null))
            throw new IOException(recoveryReadOnly?"Duplicate this recovered build before replacing it.":"Finish or discard the pasted item before replacing this build.");
        var xml=await BuildDocument.ReadAsync(input??readClipboard(),game,token);
        var loaded=await engine.ExecuteAsync(new{op="load",xml,name="Imported build"},token);
        var renamed=BuildDocument.VerifySkillPreservation(xml,loaded.Xml);
        token.ThrowIfCancellationRequested();
        if(replaceOnImport&&documentId is {} target&&result is not null)
        {
            lock(gate)pendingImport=new(target,xml,loaded,renamed);
            return;
        }
        var newId=Guid.NewGuid().ToString("N");
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory,newId+".original.xml"),xml);
        Persist(loaded,newId);
        lock(gate){undo.Clear();redo.Clear();AcceptImport(newId,loaded,renamed);}
        await RefreshMapAsync(token);
    },"Loading and calculating build…");

    private void ReplaceImportedBuild(BuildImport import)=>Work(async token=>
    {
        if(import.TargetId!=documentId||result is null||pendingBefore is not null||pastedItem is not null)
            throw new IOException("The current build changed. Import the replacement again.");
        token.ThrowIfCancellationRequested();
        var previous=result.Xml;
        SaveWork(import.Loaded,new(import.Loaded.Xml,undo.Prepend(previous).Take(12).ToArray(),[]));
        lock(gate){PushHistory(undo,previous);redo.Clear();AcceptImport(import.TargetId,import.Loaded,import.Renamed);}
        try{BuildDocument.SaveAtomic(Path.Combine(dataDirectory,import.TargetId+".original.xml"),import.OriginalXml);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException)
        {lock(gate)ShowFeedback("Build replaced","Your build and Undo were saved, but the original import file could not be refreshed.",ToastTone.Warning);}
        await RefreshMapAsync(token);
    },"Replacing build…");

    private void AcceptImport(string id,PobResult loaded,int renamed)
    {
        if(documentId!=id)tradeCandidates.Clear();
        tradeListing=null;tradeBrowsing=false;websiteReview=false;
        documentId=id;result=loaded;pendingImport=null;replaceOnImport=false;pendingBefore=null;pastedItem=null;previewSlot=null;
        RestoreRanking(id);RestoreTradeReturn(id);
        recoveryReadOnly=false;selections.Clear();detailKind="";SetSection("Calcs");filter="";passiveReturnKey=null;
        compatibilityNotice=renamed>0?$"PoB updated {renamed} gem names for this engine version.":"";
    }

    private void RenameBuild(string target,string value)=>Work(_=>
    {
        DemandSavedBuild(target);
        var name=value.Trim();if(name.Length is <1 or >100||name.Any(char.IsControl))throw new IOException("Use a build name between 1 and 100 characters.");
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory,target+".name.txt"),name);
        return Task.CompletedTask;
    },"Renaming build…");

    private void DemandSavedBuild(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _)||!savedBuilds.Any(b=>b.Id==id))throw new IOException("This saved build is no longer available.");
    }

    private void DuplicateBuild(string target)=>Work(async token=>
    {
        DemandSavedBuild(target);
        var name=savedBuilds.First(b=>b.Id==target).Name;
        var work=target==documentId&&recoveryReadOnly?new PobWorkState(result!.Xml,[],[]):PobWorkStore.Read(dataDirectory,target,game);
        work??=new(await BuildDocument.ReadAsync(Path.Combine(dataDirectory,target+".xml"),game,token),[],[]);
        var copy=work with{Undo=[],Redo=[]};var newId=Guid.NewGuid().ToString("N");
        token.ThrowIfCancellationRequested();
        PobWorkStore.Save(dataDirectory,newId,game,copy);
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory,newId+".xml"),copy.CommittedXml);
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory,newId+".name.txt"),Limit(name,90)+" (copy)");
        RefreshLibrary();lock(gate){librarySelection=newId;ShowFeedback("Build duplicated","The copy has its own saved work and history.",ToastTone.Info);}
    },"Duplicating build…");

    private void DeleteBuild(string target)=>Work(async token=>
    {
        DemandSavedBuild(target);token.ThrowIfCancellationRequested();
        // Each exact file belongs to the confirmed GUID. Never delete exports,
        // recovery copies, other builds, or a directory recursively.
        foreach(var suffix in new[]{".work.json",".xml",".original.xml",".name.txt",".ranking.json",".trade-url.txt"})File.Delete(Path.Combine(dataDirectory,target+suffix));
        if(target==documentId)
        {
            File.Delete(Path.Combine(dataDirectory,"selected.txt"));
            lock(gate){documentId=null;result=null;pendingBefore=null;pastedItem=null;previewSlot=null;undo.Clear();redo.Clear();recoveryReadOnly=false;}
            if(mapSession is {} session){mapSession=null;await session.DisposeAsync();}
        }
        RefreshLibrary();lock(gate){librarySelection=null;detailKind="library";ShowFeedback("Build deleted","The build was removed from your library.",ToastTone.Info);}
    },"Deleting build…");
}
