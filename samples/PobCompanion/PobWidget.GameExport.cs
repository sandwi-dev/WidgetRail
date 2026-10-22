using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string gameExportName="", gameExportAuthor="", gameExportDescription="";
    private string gameExportDirectory=PobGameExport.DefaultDirectory;
    private string[] gameExportPaths=[];

    private bool HandleGameExportAction(WidgetActionEvent action)
    {
        var id=action.ActionId;
        if(id=="game-export" && gameDefinition.SupportsGameFileExport)
        { gameExportName=BuildName(); gameExportPaths=[]; detailKind="game-export"; lastFocusRequestId = CreateFocusRequest().Id; return true; }
        if(detailKind!="game-export")return false;
        if(id=="back"){detailKind="menu";lastFocusRequestId = CreateFocusRequest().Id;return true;}
        if(id=="game-export.name")gameExportName=action.CommittedText??gameExportName;
        else if(id=="game-export.author")gameExportAuthor=action.CommittedText??gameExportAuthor;
        else if(id=="game-export.description")gameExportDescription=action.CommittedText??gameExportDescription;
        else if(id=="game-export.directory")gameExportDirectory=action.CommittedText??gameExportDirectory;
        else if(id is "game-export.selected" or "game-export.all")
        {
            var all=id=="game-export.all";
            Work(async token=>
            {
                if(string.IsNullOrWhiteSpace(gameExportName))throw new IOException("Enter a build name before exporting.");
                var exported=await engine.ExecuteAsync(new{op="game-export",all,name=gameExportName,author=gameExportAuthor,description=gameExportDescription},token,result!.Xml);
                var paths=PobGameExport.Save(gameExportDirectory,exported.State.GetProperty("files"),token);
                lock(gate){gameExportPaths=paths;ShowFeedback("Export complete",$"Created {paths.Length} game build file{(paths.Length==1?"":"s")}. Select the guide in PoE2’s Build Planner.",ToastTone.Success);}
            },"Exporting game build…");
        }
        else return false;
        return true;
    }

    private WidgetElement GameExportWorkspace()=>UI.Stack("pob.game-export.page",ManagementHeader("Export to PoE2"),
        UI.VerticalScroll("pob.game-export.scroll",
            Text("Follow your build inside the game","game-export.title","pob-heading"),
            Text("Creates a .build guide with passive, skill and equipment recommendations. It does not equip gear or spend points. Existing files are kept.","game-export.help","pob-muted"),
            Field("Build name",BuildNameEntry(gameExportName,"game-export.name","pob.game-export.name").Classes("pob-input"),"game-export.name"),
            Field("Author (optional)",UI.TextEntry(gameExportAuthor,"Author","game-export.author","pob.game-export.author",100).Classes("pob-input"),"game-export.author"),
            Field("Description (optional)",UI.TextEntry(gameExportDescription,"Description","game-export.description","pob.game-export.description",1000).Classes("pob-input"),"game-export.description"),
            Field("PoE2 BuildPlanner folder",(UI.TextEntry(gameExportDirectory,"Folder path","game-export.directory","pob.game-export.directory",2048) with { InputKind = TextEntryInputKind.Ordinary }).Classes("pob-input"),"game-export.directory"),
            Text("Selected sets exports the active passive tree, skills and equipment. Each linked loadout exports to its own file.","game-export.sets-help","pob-small pob-muted"),
            UI.Row("pob.game-export.actions",Button("Export selected sets","game-export.selected"),Button("Export all loadouts","game-export.all").Disabled(A(State,"loadouts").Length==0)).Classes("pob-toolbar"),
            UI.Stack("pob.game-export.results",gameExportPaths.Select((path,i)=>(WidgetElement)Text(path,"game-export.file."+i,"pob-small")).ToArray()))
            .Classes("pob-scroll","pob-panel")).Classes("pob-workspace");
}
