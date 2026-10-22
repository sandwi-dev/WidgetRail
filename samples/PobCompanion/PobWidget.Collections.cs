using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record ListEntry(string Key,string Label,string Action,bool Selected=false);
    private sealed record ListQuery(string Context,ListEntry[] Entries);
    private ListQuery libraryQuery=new("",[]);
    private WidgetLoadedCollection<ListQuery,ListEntry>? library;
    private bool libraryOpenFocusRequested;

    private WidgetLoadedCollection<ListQuery,ListEntry> CreateLibraryList()=>
        CreateLoadedCollection<ListQuery,ListEntry>("pob.saved-source",libraryQuery.Context,libraryQuery,libraryQuery.Entries,new()
        {
            ItemKey=entry=>new(entry.Key),
            RenderItem=(_,entry,context)=>UI.Button(entry.Label,"open",context.Id("entry")).Selected(entry.Selected).FocusRight("pob.library.detail").Classes("pob-button"),
            OnAction=(query,entry,action,token)=>
            {
                lock(gate)
                {
                    if(!ReferenceEquals(query,libraryQuery)||action.ActionId!="open"||busy||pendingBefore is not null)
                        return ValueTask.CompletedTask;
                    return OnActionAsync(action with {ActionId=entry.Action},token);
                }
            },
        });

    private WidgetElement LibraryWorkspace()
    {
        library??=CreateLibraryList();
        if(libraryClass.Length>0&&!LibraryClasses().Contains(libraryClass))libraryClass="";
        var builds=savedBuilds.Where(b=>(b.Name+" "+libraryMetadata.GetValueOrDefault(b.Id).Class+" "+libraryMetadata.GetValueOrDefault(b.Id).Ascendancy).Contains(librarySearch,StringComparison.OrdinalIgnoreCase)&&
            (libraryClass.Length==0||libraryMetadata.GetValueOrDefault(b.Id).Class==libraryClass));
        if(librarySort=="name")builds=builds.OrderBy(b=>b.Name,StringComparer.OrdinalIgnoreCase);
        var visible=builds.ToArray();
        if(!visible.Any(b=>b.Id==librarySelection))librarySelection=visible.FirstOrDefault().Id;
        var entries=visible.Select(b=>new ListEntry(b.Id,b.Name+(b.Id==documentId?" · Current":"")+(b.Id==librarySelection?" · Selected":""),"library-select."+b.Id,b.Id==librarySelection)).ToArray();
        if(!libraryQuery.Entries.SequenceEqual(entries))
        {
            libraryQuery=new("saved",entries);
            library.Publish(libraryQuery.Context,libraryQuery,entries);
        }
        return UI.Stack("pob.library.workspace",ManagementHeader($"Saved builds · {entries.Length}"),
            QuickControls(UI.Grid("pob.library.tools",[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Auto(),GridTrack.Auto()],
                SearchField(UI.TextEntry(librarySearch,"Search saved builds","library-search","pob.library.search"),savedBuilds,
                    ()=>savedBuilds.Where(b=>libraryClass.Length==0||libraryMetadata.GetValueOrDefault(b.Id).Class==libraryClass).Select(b=>
                        (b.Name,b.Name+" "+libraryMetadata.GetValueOrDefault(b.Id).Class+" "+libraryMetadata.GetValueOrDefault(b.Id).Ascendancy,
                        libraryMetadata.GetValueOrDefault(b.Id).Class+" "+libraryMetadata.GetValueOrDefault(b.Id).Ascendancy))).Classes("pob-input"),
                UI.Select("Sort",[new("recent","Recent","library-sort.recent",librarySort=="recent"),new("name","Name","library-sort.name",librarySort=="name")],"pob.library.sort").Classes("pob-select").InGrid(column:1),
                UI.Select("Class",new[]{new SelectOption("all","All classes","library-class.all",libraryClass.Length==0)}.Concat(LibraryClasses().Select((name,i)=>new SelectOption("class"+i,name,"library-class."+i,name==libraryClass))).ToArray(),"pob.library.class").Classes("pob-select").InGrid(column:2)).Spacing(0,12),
                "pob.library.search","pob.library.results","Builds",NeedsQuickControls(entries.Length)),
            Split("library",QuickControls(UI.Stack("pob.library.results",UI.CollectionList("pob.saved-builds",library,64,"Saved builds").Classes("pob-scroll")).Classes("pob-workspace"),
                "pob.saved-builds","pob.library.tools","Search / sort",NeedsQuickControls(entries.Length)),
                UI.VerticalScroll("pob.library.detail.scroll",LibraryDetails()).Classes("pob-scroll"),1,1))
            .Classes("pob-workspace");
    }

    private FocusGroupEntryRequest? ContentFocusRequest()
    {
        if(busy||lastFocusRequestId==0)return null;
        if(similarReturnGroup is {} similarReturn)return new(){RequestId=lastFocusRequestId,GroupId=similarReturn};
        if(detailKind=="similar-search"&&SimilarCurrent)return new(){RequestId=lastFocusRequestId,GroupId="pob.similar.tools"};
        if(detailKind=="build-comparison")return BuildComparisonFocusRequest();
        if(UnifiedIndexedFocusRequest() is {} unified)return unified;
        if(detailKind=="choose-slot"&&pendingBefore is not null)return new(){RequestId=lastFocusRequestId,GroupId=tradeWorkspaceFocus};
        if(detailKind=="library"&&libraryOpenFocusRequested)return new(){RequestId=lastFocusRequestId,GroupId="pob.library.open-action"};
        if(modifierFocusRequested&&section=="Configuration"&&configSection=="Custom modifiers"&&string.IsNullOrWhiteSpace(filter))return new(){RequestId=lastFocusRequestId,GroupId="pob.custom.edit-tools"};
        if(quickControlTarget is {} target && quickControlTargets.Contains(target))return new(){RequestId=lastFocusRequestId,GroupId=target};
        if(historyFocusRequested)return new(){RequestId=lastFocusRequestId,GroupId="pob.buildtools"};
        if(detailKind=="skill"&&skillWorkspaceFocusRequested)return new(){RequestId=lastFocusRequestId,
            GroupId=skillWorkspaceGemReturn is {} gem?"pob.gem-options."+gem+".return":skillWorkspaceReturnControl is not null?"pob.skill.actions":"pob.detail.scroll"};
        if(detailKind=="skill-options")return new(){RequestId=lastFocusRequestId,GroupId="pob.skill-options.content"};
        if(detailKind=="skill-manage"&&skillOrderFocusRequested)return new(){RequestId=lastFocusRequestId,GroupId="pob.group-order.actions"};
        if(detailKind is "skill-manage" or "gem-picker")return new(){RequestId=lastFocusRequestId,GroupId=detailKind=="gem-picker"?"pob.gem-picker.tools":"pob.skill-manage.tools"};
        if(detailKind is "item-manage" or "feature-picker")return new(){RequestId=lastFocusRequestId,GroupId=detailKind=="feature-picker"?"pob.choice-tools":"pob.item-edit.controls"};
        if(detailKind is "inventory" or "item-author")return new(){RequestId=lastFocusRequestId,GroupId=detailKind=="inventory"?"pob.stored.tools":"pob.author.controls"};
        if(detailKind=="timeless")return new(){RequestId=lastFocusRequestId,GroupId="pob.timeless.settings"};
        if(detailKind=="calculations")return new(){RequestId=lastFocusRequestId,GroupId="pob.calculation.tools"};
        if(detailKind=="gem-options")return new(){RequestId=lastFocusRequestId,GroupId="pob.gem-options.page"};
        if(detailKind.Length>0&&!InlineDetail)return null;
        if(section=="Tree"&&detailKind!="node"&&passiveReturnKey is not null&&passives is not null)
        {
            var index=Array.FindIndex(passiveQuery.Entries,e=>e.Key==passiveReturnKey);
            if(index>=0)return passives.Enter("pob.passives",lastFocusRequestId,
                passives.FocusTarget("pob.passives",new(passiveReturnKey),index));
        }
        return new(){RequestId=lastFocusRequestId,GroupId=section=="Tree"?"pob.tree.side":"pob.root"};
    }
}
