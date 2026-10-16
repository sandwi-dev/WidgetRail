using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record ListEntry(string Key,string Label,string Action);
    private sealed record ListQuery(string Context,ListEntry[] Entries);
    private ListQuery passiveQuery=new("",[]),libraryQuery=new("",[]);
    private WidgetIndexedCollection<ListQuery,ListEntry>? passives,library;
    private PobResult? passiveResult;
    private string? passiveFilter,passiveReturnKey;

    private WidgetIndexedCollection<ListQuery,ListEntry> CreateList(string id,bool isPassive)=>
        CreateIndexedCollection<ListQuery,ListEntry>(id,isPassive?passiveQuery:libraryQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<ListEntry>>(query.Entries.Skip(start).Take(count).ToArray());
            },
            ItemKey=entry=>new(entry.Key),
            RenderItem=(_,entry,context)=>UI.Button(entry.Label,"open",context.Id("entry")).Classes("pob-button"),
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
            var entries=A(State,"nodes").Where(n=>filter.Length>0
                ? (S(n,"name")+" "+S(n,"description")).Contains(filter,StringComparison.OrdinalIgnoreCase):B(n,"allocated"))
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
        var entries=savedBuilds.Select(b=>new ListEntry(b.Id,b.Name+(b.Id==documentId?" · Current":""),"saved."+b.Id)).ToArray();
        if(!libraryQuery.Entries.SequenceEqual(entries))
        {
            var same=libraryQuery.Entries.Select(e=>e.Key).SequenceEqual(entries.Select(e=>e.Key));
            libraryQuery=new("saved",entries);
            if(same)library.UpdateContent(libraryQuery);else library.PublishQuery(libraryQuery,entries.Length);
        }
        return UI.Stack("pob.library.workspace",UI.Button("Back","back","pob.back").FocusDown("pob.saved-builds").Classes("pob-button"),
            Text($"Saved builds · {entries.Length}","library.heading","pob-heading"),
            UI.CollectionList("pob.saved-builds",library,64,"Saved builds").Classes("pob-scroll"))
            .Classes("pob-workspace","pob-panel");
    }

    private FocusGroupEntryRequest? ContentFocusRequest()
    {
        if(busy||focusRequestSequence==0)return null;
        if(historyFocusRequested)return new(){RequestId=focusRequestSequence,GroupId="pob.history"};
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
