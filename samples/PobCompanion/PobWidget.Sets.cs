using System.Text.Json;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string managedSetKind="tree",newSetName="";
    private int managedSetId;
    private ListQuery managedSetsQuery=new("",[]);
    private WidgetLoadedCollection<ListQuery,ListEntry>? managedSetsCollection;
    private static readonly (string Kind,string Field,string Title)[] SetKinds=[("tree","trees","Passive trees"),("items","itemSets","Equipment"),("skills","skillSets","Skills"),("config","configSets","Configuration")];
    private JsonElement[] ManagedSets()=>A(State,SetKinds.Single(k=>k.Kind==managedSetKind).Field);
    private bool HandleSetAction(WidgetActionEvent action)
    {
        var id=action.ActionId;var parts=id.Split('.');
        if(id=="sets"){detailKind="sets";managedSetId=I(ManagedSets().FirstOrDefault(s=>B(s,"active")),"id");lastFocusRequestId = CreateFocusRequest().Id;return true;}
        if(detailKind!="sets")return false;
        if(id=="back"){detailKind="menu";lastFocusRequestId = CreateFocusRequest().Id;return true;}
        if(parts.Length==2&&parts[0]=="sets-kind"&&SetKinds.Any(k=>k.Kind==parts[1]))
        {managedSetKind=parts[1];managedSetId=I(ManagedSets().FirstOrDefault(s=>B(s,"active")),"id");newSetName="";lastFocusRequestId = CreateFocusRequest().Id;return true;}
        if(parts.Length==2&&parts[0]=="sets-select"&&int.TryParse(parts[1],out var selected)&&ManagedSets().Any(s=>I(s,"id")==selected))
        {managedSetId=selected;newSetName="";return true;}
        if(id=="sets-name"){newSetName=action.CommittedText??"";return true;}
        if(id=="sets-activate"){Edit(new{op="set",kind=managedSetKind,id=managedSetId});return true;}
        if(id is "sets-new" or "sets-copy" or "sets-rename" or "sets-delete")
        {
            var selectedSet=ManagedSets().FirstOrDefault(s=>I(s,"id")==managedSetId);
            if(selectedSet.ValueKind!=JsonValueKind.Object)return true;
            var mode=id[5..];
            var command=new{op="set-manage",kind=managedSetKind,id=managedSetId,mode,name=newSetName};
            if(mode=="delete")featureRemoval=($"Delete {S(selectedSet,"title")} ?",command);
            else if(string.IsNullOrWhiteSpace(newSetName))ShowFeedback("Enter a set name","Use the name field above before creating, duplicating or renaming a set.",ToastTone.Info);
            else Edit(command);
            return true;
        }
        return false;
    }

    private WidgetElement SetsWorkspace()
    {
        var sets=ManagedSets();
        if(!sets.Any(s=>I(s,"id")==managedSetId))managedSetId=I(sets.FirstOrDefault(),"id");
        var selected=sets.FirstOrDefault(s=>I(s,"id")==managedSetId);
        managedSetsCollection??=CreateLoadedCollection<ListQuery,ListEntry>("pob.sets.source",managedSetsQuery.Context,managedSetsQuery,managedSetsQuery.Entries,new()
        {
            ItemKey=entry=>new(entry.Key),
            RenderItem=(_,entry,context)=>UI.Button(entry.Label,"select",context.Id("entry")).Selected(entry.Selected).FocusRight("pob.sets-name").Classes("pob-list-item"),
            OnAction=(query,entry,action,token)=>
            {lock(gate){if(!ReferenceEquals(query,managedSetsQuery)||busy||detailKind!="sets"||action.ActionId!="select")return ValueTask.CompletedTask;
                return OnActionAsync(action with{ActionId=entry.Action},token);}}
        });
        var entries=sets.Select(s=>new ListEntry(I(s,"id").ToString(),S(s,"title")+(B(s,"active")?" · Active":"")+(I(s,"id")==managedSetId?" · Selected":""),"sets-select."+I(s,"id"),I(s,"id")==managedSetId)).ToArray();
        if(managedSetsQuery.Context!=managedSetKind||!managedSetsQuery.Entries.SequenceEqual(entries))
        {
            managedSetsQuery=new(managedSetKind,entries);
            managedSetsCollection.Publish(managedSetKind,managedSetsQuery,entries);
        }
        var list=QuickControls(UI.Stack("pob.sets.list-panel",UI.CollectionList("pob.sets.list",managedSetsCollection,64,"Build sets").Classes("pob-scroll")).Classes("pob-panel","pob-workspace"),"pob.sets.list","pob.sets-kind","Set type",NeedsQuickControls(entries.Length));
        var details=UI.VerticalScroll("pob.sets.details",
            Text(S(selected,"title"),"sets.selected","pob-heading"),
            Text(B(selected,"active")?"Active set":"Not active — selecting this row does not change your build.","sets.state","pob-small pob-muted"),
            Button("Use this set","sets-activate").Disabled(B(selected,"active")),
            Field("New name",UI.TextEntry(newSetName,"e.g. Bossing","sets-name","pob.sets-name",100).FocusLeft("pob.sets.list").Classes("pob-input"),"sets-name"),
            UI.Row("pob.sets.edit",Button("Rename","sets-rename"),Button("Duplicate","sets-copy")).Classes("pob-toolbar"),
            Button("Create empty set","sets-new"),Button("Delete set…","sets-delete").Disabled(sets.Length<=1),
            Text("Changes save immediately and support Undo. Matching set names form linked loadouts. Renaming or deleting one set can change those links.","sets.help","pob-small pob-muted"),
            Text(managedSetKind=="items"?"Equipment sets share the item inventory. Duplicating a set copies equipment choices; editing an item affects every set using that item.":"Each set keeps its own selections and settings.","sets.sharing","pob-small pob-muted"))
            .RememberChildFocus("pob.sets-name").Classes("pob-scroll","pob-panel");
        return UI.Stack("pob.sets.page",ManagementHeader("Manage sets"),
            UI.Select("Set type",SetKinds.Select(k=>new SelectOption(k.Kind,k.Title,"sets-kind."+k.Kind,k.Kind==managedSetKind)).ToArray(),"pob.sets-kind").Classes("pob-select"),
            Split("sets",list,details,1,1.5)).Classes("pob-workspace");
    }
}
