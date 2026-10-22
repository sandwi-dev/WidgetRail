using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    internal sealed record PassiveEntry(string Key,string Name,JsonElement[] Nodes,bool IsGroup=false,bool Expanded=false,bool IsChild=false);
    internal sealed record PassiveQuery(string Context,PassiveEntry[] Entries,int NodeCount);
    private PassiveQuery passiveQuery=new("",[],0);
    private WidgetIndexedCollection<PassiveQuery,PassiveEntry>? passives;
    private PobResult? passiveResult;
    private string? passiveFilter,passiveReturnKey;
    private readonly Dictionary<string,HashSet<string>> passiveExpansion=new(StringComparer.Ordinal);

    private string PassiveGroupContext()=>string.Join("\u001f",documentId,S(State,"treeVersion"),
        S(A(State,"trees").FirstOrDefault(tree=>B(tree,"active")),"id"));

    private HashSet<string> ExpandedPassives(string context)
    {
        if(passiveExpansion.TryGetValue(context,out var expanded))return expanded;
        // Session-local UI state, bounded independently of saved builds and tree size.
        if(passiveExpansion.Count>=32)passiveExpansion.Remove(passiveExpansion.Keys.First());
        return passiveExpansion[context]=new(StringComparer.Ordinal);
    }

    internal static PassiveQuery BuildPassiveList(JsonElement[] nodes,string context,string search,IReadOnlySet<string> expanded)
    {
        var matches=nodes.Where(node=>B(node,"interactive")&&(search.Length>0
            ?(S(node,"name")+" "+S(node,"description")).Contains(search,StringComparison.OrdinalIgnoreCase)
            :B(node,"allocated")||B(node,"granted"))).ToArray();
        var entries=new List<PassiveEntry>();
        foreach(var group in matches.GroupBy(node=>S(node,"name"),StringComparer.Ordinal))
        {
            var members=group.ToArray();
            if(members.Length==1){entries.Add(new("node."+I(members[0],"id"),group.Key,members));continue;}
            var key="group."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)));
            var open=expanded.Contains(group.Key);
            entries.Add(new(key,group.Key,members,IsGroup:true,Expanded:open));
            if(open)entries.AddRange(members.Select(node=>new PassiveEntry("node."+I(node,"id"),group.Key,[node],IsChild:true)));
        }
        return new(context,entries.ToArray(),matches.Length);
    }

    private static string PassiveState(JsonElement node)=>B(node,"granted")?"Granted":B(node,"allocated")?"Allocated":"Unallocated";
    private static WidgetElement PassiveListRow(PassiveEntry entry,WidgetIndexedItemContext context)
    {
        var title=entry.IsGroup?$"{entry.Name} · {entry.Nodes.Length} nodes":entry.Name;
        var summary=entry.IsGroup?(entry.Expanded?"Expanded":"Collapsed"):PassiveState(entry.Nodes[0]);
        if(!entry.IsGroup&&B(entry.Nodes[0],"canAllocate")&&I(entry.Nodes[0],"pathCount")>0)
            summary+=" · "+I(entry.Nodes[0],"pathCount")+"-node allocation path";
        var text=UI.Text(title,context.Id("title")).Classes("pob-list-title");
        WidgetElement heading=entry.IsGroup?UI.Grid(context.Id("heading"),[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Auto()],
            text,UI.Text(entry.Expanded?"▾":"▸",context.Id("disclosure")).Classes("pob-gold").InGrid(column:1)).Spacing(0,8).Classes("pob-passive-heading"):text;
        return UI.ActionSurface("open",context.Id("entry"),title+" · "+summary,ActionSurfaceOrientation.Vertical,
            heading,UI.Text(summary,context.Id("state")).Classes("pob-small","pob-muted"))
            .Classes("pob-list-item",entry.IsChild?"pob-passive-child":"pob-passive-entry").PresentOnFocus(entry.Key);
    }

    private WidgetElement PassiveList()
    {
        passives??=CreateIndexedCollection<PassiveQuery,PassiveEntry>("pob.passive-source",passiveQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult<IReadOnlyList<PassiveEntry>>(query.Entries.Skip(start).Take(count).ToArray());},
            ItemKey=entry=>new(entry.Key),RenderItem=(_,entry,context)=>PassiveListRow(entry,context),
            OccurrenceKeys=query=>query.Entries.Length<=512?query.Entries.Select(entry=>new WidgetCollectionItemKey(entry.Key)).ToArray():null,
            OnAction=(query,entry,action,token)=>
            {
                lock(gate)
                {
                    if(!ReferenceEquals(query,passiveQuery)||query.Context!=PassiveGroupContext()||!ReferenceEquals(passiveResult,result)||passiveFilter!=filter||
                        section!="Tree"||detailKind is not ("" or "map")||LifecycleState!=WidgetLifecycleState.Interactive||action.ActionId!="open"||busy||pendingBefore is not null)
                        return ValueTask.CompletedTask;
                    passiveReturnKey=entry.Key;
                    if(entry.IsGroup)
                    {
                        var expanded=ExpandedPassives(query.Context);
                        if(!expanded.Add(entry.Name))expanded.Remove(entry.Name);
                        passiveResult=null;quickControlTarget=null;historyFocusRequested=false;lastFocusRequestId = CreateFocusRequest().Id;Invalidate();return ValueTask.CompletedTask;
                    }
                    return OnActionAsync(action with{ActionId=entry.Key},token);
                }
            },
        });
        var context=PassiveGroupContext();
        if(!ReferenceEquals(passiveResult,result)||passiveFilter!=filter||passiveQuery.Context!=context)
        {
            var next=BuildPassiveList(A(State,"nodes"),context,filter,ExpandedPassives(context));
            var same=passiveQuery.Context==next.Context&&passiveFilter==filter&&passiveQuery.Entries.Select(entry=>entry.Key).SequenceEqual(next.Entries.Select(entry=>entry.Key));
            passiveQuery=next;passiveResult=result;passiveFilter=filter;
            if(same)passives.UpdateContent(next);else passives.PublishQuery(next,next.Entries.Length);
        }
        return WithPassivePreviews(UI.CollectionList("pob.passives",passives,64,"Passive skill results").Classes("pob-scroll"));
    }

    private void RevealPassiveOnReturn()
    {
        var node=A(State,"nodes").FirstOrDefault(node=>I(node,"id")==detailId&&B(node,"interactive"));
        if(node.ValueKind!=JsonValueKind.Object)return;
        var name=S(node,"name");
        if(filter.Length==0?!B(node,"allocated")&&!B(node,"granted"):!(name+" "+S(node,"description")).Contains(filter,StringComparison.OrdinalIgnoreCase))filter=name;
        ExpandedPassives(PassiveGroupContext()).Add(name);
        passiveReturnKey="node."+detailId;passiveResult=null;
    }
}
