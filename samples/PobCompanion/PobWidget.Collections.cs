using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record ListEntry(string Key,string Label,string Action,bool Selected=false);
    private sealed record ListQuery(string Context,ListEntry[] Entries);
    private ListQuery passiveQuery=new("",[]),libraryQuery=new("",[]);
    private WidgetIndexedCollection<ListQuery,ListEntry>? passives,library;
    private PobResult? passiveResult;
    private string? passiveFilter,passiveReturnKey;
    private bool libraryOpenFocusRequested;

    private WidgetIndexedCollection<ListQuery,ListEntry> CreateList(string id,bool isPassive)=>
        CreateIndexedCollection<ListQuery,ListEntry>(id,isPassive?passiveQuery:libraryQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<ListEntry>>(query.Entries.Skip(start).Take(count).ToArray());
            },
            ItemKey=entry=>new(entry.Key),
            RenderItem=(_,entry,context)=>isPassive?UI.Button(entry.Label,"open",context.Id("entry")).Classes("pob-button"):
                UI.Button(entry.Label,"open",context.Id("entry")).Selected(entry.Selected).FocusRight("pob.library.detail").Classes("pob-button"),
            OnAction=(query,entry,action,token)=>
            {
                lock(gate)
                {
                    if(!ReferenceEquals(query,isPassive?passiveQuery:libraryQuery)||action.ActionId!="open"||busy||pendingBefore is not null)
                        return ValueTask.CompletedTask;
                    if(isPassive)passiveReturnKey=entry.Key;
                    return OnActionAsync(action with {ActionId=entry.Action},token);
                }
            },
        });

    private WidgetElement PassiveList()
    {
        passives??=CreateList("pob.passive-source",true);
        if(!ReferenceEquals(passiveResult,result)||passiveFilter!=filter)
        {
            var entries=A(State,"nodes").Where(n=>B(n,"interactive")&&(filter.Length>0
                ? (S(n,"name")+" "+S(n,"description")).Contains(filter,StringComparison.OrdinalIgnoreCase):B(n,"allocated")||B(n,"granted")))
                .Select(n=>new ListEntry("node."+I(n,"id"),S(n,"name"),"node."+I(n,"id"))).ToArray();
            var next=new ListQuery(documentId+":"+S(State,"treeVersion")+":"+filter,entries);
            var same=passiveQuery.Context==next.Context&&passiveQuery.Entries.Select(e=>e.Key).SequenceEqual(entries.Select(e=>e.Key));
            passiveQuery=next;passiveResult=result;passiveFilter=filter;
            if(same)passives.UpdateContent(next);else passives.PublishQuery(next,entries.Length);
        }
        return UI.CollectionList("pob.passives",passives,64,"Passive skill results").Classes("pob-scroll");
    }

    private WidgetElement LibraryWorkspace()
    {
        library??=CreateList("pob.saved-source",false);
        if(libraryClass.Length>0&&!LibraryClasses().Contains(libraryClass))libraryClass="";
        var builds=savedBuilds.Where(b=>(b.Name+" "+libraryMetadata.GetValueOrDefault(b.Id).Class+" "+libraryMetadata.GetValueOrDefault(b.Id).Ascendancy).Contains(librarySearch,StringComparison.OrdinalIgnoreCase)&&
            (libraryClass.Length==0||libraryMetadata.GetValueOrDefault(b.Id).Class==libraryClass));
        if(librarySort=="name")builds=builds.OrderBy(b=>b.Name,StringComparer.OrdinalIgnoreCase);
        var visible=builds.ToArray();
        if(!visible.Any(b=>b.Id==librarySelection))librarySelection=visible.FirstOrDefault().Id;
        var entries=visible.Select(b=>new ListEntry(b.Id,b.Name+(b.Id==documentId?" · Current":"")+(b.Id==librarySelection?" · Selected":""),"library-select."+b.Id,b.Id==librarySelection)).ToArray();
        if(!libraryQuery.Entries.SequenceEqual(entries))
        {
            var same=libraryQuery.Entries.Select(e=>e.Key).SequenceEqual(entries.Select(e=>e.Key));
            libraryQuery=new("saved",entries);
            if(same)library.UpdateContent(libraryQuery);else library.PublishQuery(libraryQuery,entries.Length);
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
        if(busy||focusRequestSequence==0)return null;
        if(CanReplaceTradeComparison&&pendingBefore is not null)return new(){RequestId=focusRequestSequence,GroupId="pob.item.compare.controls"};
        if(detailKind=="library"&&libraryOpenFocusRequested)return new(){RequestId=focusRequestSequence,GroupId="pob.library.open-action"};
        if(modifierFocusRequested&&section=="Configuration"&&configSection=="Custom modifiers"&&string.IsNullOrWhiteSpace(filter))return new(){RequestId=focusRequestSequence,GroupId="pob.custom.edit-tools"};
        if(quickControlTarget is {} target && quickControlTargets.Contains(target))return new(){RequestId=focusRequestSequence,GroupId=target};
        if(historyFocusRequested)return new(){RequestId=focusRequestSequence,GroupId="pob.history"};
        if(detailKind=="skill-manage"&&skillOrderFocusRequested)return new(){RequestId=focusRequestSequence,GroupId="pob.group-order.actions"};
        if(detailKind is "skill-manage" or "gem-picker")return new(){RequestId=focusRequestSequence,GroupId=detailKind=="gem-picker"?"pob.gem-picker.tools":"pob.skill-manage.tools"};
        if(detailKind is "item-manage" or "feature-picker")return new(){RequestId=focusRequestSequence,GroupId=detailKind=="feature-picker"?"pob.choice-tools":"pob.item-edit.controls"};
        if(detailKind is "inventory" or "item-author")return new(){RequestId=focusRequestSequence,GroupId=detailKind=="inventory"?"pob.stored.tools":"pob.author.controls"};
        if(detailKind=="timeless")return new(){RequestId=focusRequestSequence,GroupId="pob.timeless.settings"};
        if(detailKind=="calculations")return new(){RequestId=focusRequestSequence,GroupId="pob.calculation.tools"};
        if(detailKind=="gem-options")return new(){RequestId=focusRequestSequence,GroupId="pob.gem-options.page"};
        if(detailKind.Length>0&&!InlineDetail)return null;
        if(section=="Tree"&&detailKind!="node"&&passiveReturnKey is not null&&passives is not null)
        {
            var index=Array.FindIndex(passiveQuery.Entries,e=>e.Key==passiveReturnKey);
            if(index>=0)return passives.Enter("pob.passives",focusRequestSequence,
                passives.FocusTarget("pob.passives",new(passiveReturnKey),index));
        }
        return new(){RequestId=focusRequestSequence,GroupId=section=="Tree"?"pob.tree.side":"pob.root"};
    }
}
