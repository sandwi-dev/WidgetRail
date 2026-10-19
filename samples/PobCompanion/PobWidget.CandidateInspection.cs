using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record ItemCandidateContext(PobResult Build,string? Document,object Catalog,string Search,bool Unique);
    private sealed record ItemCandidateEntry(string Key,string Title,string Action,int ItemId,string UniqueKey,string Description,
        string Rarity,string Uses,bool Selected=false,JsonElement Preview=default,string[]? Slots=null);
    private sealed record ItemCandidateQuery(ItemCandidateContext? Context,ItemCandidateEntry[] Entries)
    {
        internal readonly SemaphoreSlim PreviewGate = new(1,1);
        internal readonly Dictionary<string,ItemCandidateEntry> Previews = new(StringComparer.Ordinal);
    }
    private ItemCandidateQuery uniqueCandidateQuery=new(null,[]);
    private WidgetIndexedCollection<ItemCandidateQuery,ItemCandidateEntry>? uniqueCandidateCollection;

    private static string CandidateText(string value,int maximum)
    {
        var clean=new string(Plain(value).Where(c=>!char.IsControl(c)||c is '\n' or '\t').ToArray()).Trim();
        if(clean.Length<=maximum)return clean;
        return clean[..(char.IsHighSurrogate(clean[maximum-1])?maximum-1:maximum)]+"…";
    }
    private ItemCandidateContext ItemCandidateContextFor(object catalog,string search,bool unique)=>new(result!,documentId,catalog,search,unique);
    private bool ItemCandidateIsCurrent(ItemCandidateQuery query)
    {
        if(query.Context is not {} context||result is null||!ReferenceEquals(context.Build,result)||context.Document!=documentId||pendingBefore is not null)return false;
        return context.Unique?ReferenceEquals(query,uniqueCandidateQuery)&&detailKind=="feature-picker"&&featurePicker is {Prefix:"unique-item."} picker&&
            ReferenceEquals(context.Catalog,picker.Choices)&&context.Search==choiceSearch&&pastedItem is null:
            ReferenceEquals(query,inventoryQuery)&&detailKind=="inventory"&&context.Catalog.Equals(inventoryState)&&context.Search==inventorySearch;
    }
    private WidgetIndexedCollection<ItemCandidateQuery,ItemCandidateEntry> CreateItemCandidateCollection(string sourceId,ItemCandidateQuery initial)=>
        CreateIndexedCollection<ItemCandidateQuery,ItemCandidateEntry>(sourceId,initial,0,new()
        {
            ReadRange=ReadItemCandidatePreviewsAsync,
            ItemKey=entry=>new(entry.Key),
            RenderItem=(query,entry,context)=>RenderItemCandidate(query,entry,context),
            OnAction=(query,entry,action,token)=>
            {
                lock(gate)
                {
                    if(busy||!ItemCandidateIsCurrent(query))return ValueTask.CompletedTask;
                    if(action.ActionId.StartsWith("equip.",StringComparison.Ordinal)&&int.TryParse(action.ActionId[6..],out var slot)&&
                        entry.Slots is {} slots&&slot>=0&&slot<slots.Length&&!query.Context!.Unique)
                    {Edit(new{op="inventory-edit",mode="equip",id=entry.ItemId,slot=slots[slot]});return ValueTask.CompletedTask;}
                    return action.ActionId=="select"?OnActionAsync(action with{ActionId=entry.Action},token):ValueTask.CompletedTask;
                }
            },
        });
    private async ValueTask<IReadOnlyList<ItemCandidateEntry>> ReadItemCandidatePreviewsAsync(ItemCandidateQuery query,int start,int count,CancellationToken token)
    {
        await query.PreviewGate.WaitAsync(token);
        try
        {
            var entries=query.Entries.Skip(start).Take(count).ToArray();
            var missing=entries.Where(entry=>!query.Previews.ContainsKey(entry.Key)).ToArray();
            if(missing.Length>0)
            {
                var response=await engine.ExecuteAsync(new{op="item-previews",items=missing.Select(entry=>new
                    {key=entry.Key,itemId=entry.ItemId,uniqueKey=query.Context!.Unique?entry.UniqueKey:null}).ToArray()},token,query.Context!.Build.Xml);
                var returned=A(response.State,"previews");
                if(returned.Length!=missing.Length)throw new InvalidDataException("PoB returned an incomplete preview range.");
                for(var index=0;index<missing.Length;index++)
                {
                    var entry=missing[index];var item=returned[index];
                    if(S(item,"key")!=entry.Key||A(item,"details").Length>128)throw new InvalidDataException("PoB returned invalid preview metadata.");
                    query.Previews[entry.Key]=entry with{Preview=item.GetProperty("details").Clone(),Slots=A(item,"slots").Select(slot=>slot.GetString()!).ToArray()};
                }
            }
            var result=entries.Select(entry=>query.Previews[entry.Key]).ToArray();
            // This cache belongs to the immutable build/query, never crosses build edits.
            foreach(var key in query.Previews.Keys.Where(key=>!entries.Any(entry=>entry.Key==key)).Take(Math.Max(0,query.Previews.Count-128)).ToArray())
                query.Previews.Remove(key);
            return result;
        }
        finally{query.PreviewGate.Release();}
    }
    private static WidgetElement RenderItemCandidate(ItemCandidateQuery query,ItemCandidateEntry entry,WidgetIndexedItemContext context)
    {
        var unique=query.Context!.Unique;
        var label=entry.Title+(unique?"":entry.Uses.Length>0?" · In use":" · Unequipped");
        var summary=unique?entry.Description.Split('\n')[0]:entry.Rarity.ToLowerInvariant()+(entry.Uses.Length>0?" · In use":" · Unequipped");
        var candidate=UI.ActionSurface("select",context.Id("entry"),label,ActionSurfaceOrientation.Vertical,
            UI.Text(entry.Title,context.Id("title")).Classes("pob-list-title",RarityClass(entry.Rarity)),
            UI.Text(summary,context.Id("summary")).Classes("pob-candidate-summary")).Selected(entry.Selected);
        if(!unique)candidate=candidate.FocusRight("pob.inventory-edit");
        var controls=new List<WidgetElement>{UI.Button(unique?"Open item draft":"Select item","select",context.Id("choose")).Classes("pob-button")};
        if(!unique&&entry.Slots is {Length:>0} slots)
            controls.Add(UI.Select("Equip in…",new[]{new SelectOption("none","Choose slot","none",true)}.Concat(slots.Select((slot,index)=>new SelectOption("slot"+index,slot,"equip."+index))).ToArray(),context.Id("equip")).Classes("pob-select"));
        var lines=entry.Preview.ValueKind==JsonValueKind.Array?entry.Preview.EnumerateArray().ToArray():[];
        var runs=new List<FormattedRun>();
        foreach(var line in lines)
        {
            if(runs.Count>0)runs.Add(new("\n",runs[^1].Tone));
            runs.AddRange(ReadFormattedRuns(line,"text","runs"));
        }
        // Merge adjacent runs to keep a maximum-size leased range within its protocol budget.
        var merged=new List<FormattedRun>();
        foreach(var run in runs)
            if(merged.Count>0&&merged[^1].Tone==run.Tone)merged[^1]=merged[^1] with{Text=merged[^1].Text+run.Text};else merged.Add(run);
        var details=new List<WidgetElement>();
        if(merged.Count<=12)AddRuns(details,merged,context.Id("details"),"pob-text");
        else details.Add(UI.Text(string.Concat(merged.Select(run=>run.Text)),context.Id("details")).Classes("pob-text"));
        if(!unique)details.Add(UI.Text(entry.Uses.Length==0?"Not equipped in any set.":"Used by: "+entry.Uses,context.Id("uses")).Classes("pob-preview-usage"));
        var content=InspectionWorkspace(context.Id("preview"),details,
            UI.Stack(context.Id("controls"),UI.Text(entry.Title,context.Id("preview-title")).Classes("pob-preview-title",RarityClass(entry.Rarity)),
                UI.Row(context.Id("actions"),controls.ToArray()).Classes("pob-toolbar")));
        return candidate.Classes("pob-list-item","pob-candidate-row").PresentOnFocus(content);
    }
    private static string RarityClass(string rarity)=>rarity.ToUpperInvariant() switch
    {"MAGIC"=>"pob-blue","RARE"=>"pob-gold","UNIQUE"=>"pob-orange",_=>"pob-text"};
    private static WidgetElement ItemCandidatePreview(WidgetElement list,string id)=>
        UI.FocusPresentationSurface(list,UI.Text("",id+".empty"),id) with{Preview=new(Width:480,Height:560){SizeToContentHeight=true,EnterButton=ControllerButton.X}};

    private WidgetElement UniqueCandidateWorkspace(JsonElement[] choices)
    {
        uniqueCandidateCollection??=CreateItemCandidateCollection("pob.unique.source",uniqueCandidateQuery);
        var context=ItemCandidateContextFor(choices,choiceSearch,unique:true);
        var entries=choices.Where(c=>Plain(S(c,"title")+" "+S(c,"description")).Contains(choiceSearch,StringComparison.OrdinalIgnoreCase))
            .Select(c=>new ItemCandidateEntry("unique"+I(c,"id"),CandidateText(S(c,"title"),512),"unique-item."+I(c,"id"),0,S(c,"key"),
                CandidateText(S(c,"description"),2400),"Unique","")).ToArray();
        if(!uniqueCandidateQuery.Entries.SequenceEqual(entries)||uniqueCandidateQuery.Context!=context||!ReferenceEquals(uniqueCandidateQuery.Context?.Build,result))
        {uniqueCandidateQuery=new(context,entries);uniqueCandidateCollection.PublishQuery(uniqueCandidateQuery,entries.Length);}
        var tools=QuickControls(UI.Stack("pob.choice-tools",
            UI.Row("pob.choice.header",Button("Back","back"),Text("Unique item library","choice.title","pob-heading")).Classes("pob-toolbar"),
            SearchField(UI.TextEntry(choiceSearch,"Search unique names or bases","choice-search","pob.choice-search",100),choices,
                ()=>choices.Select(c=>(Plain(S(c,"title")),Plain(S(c,"title")+" "+S(c,"description")),Plain(S(c,"description"))))).Classes("pob-input"),
            Text($"{entries.Length} unique items","choice.count","pob-muted")),"pob.choice-search","pob.choice-results","Items",NeedsQuickControls(entries.Length));
        var list=ItemCandidatePreview(UI.CollectionList("pob.choices",uniqueCandidateCollection,64,"Unique item library").Classes("pob-scroll"),"pob.unique.preview");
        var results=entries.Length==0
            ? UI.Stack("pob.choice-results",Text("No matching items.","choice.empty"),list)
            : UI.Stack("pob.choice-results",list);
        return UI.Stack("pob.choice-picker",tools,
            QuickControls(results.Classes("pob-workspace"),"pob.choices","pob.choice-tools","Search",NeedsQuickControls(entries.Length))).Classes("pob-workspace");
    }

    // Enter the first action above the reading viewport so opening never scrolls the details to their bottom.
    private static WidgetElement InspectionWorkspace(string id,List<WidgetElement> details,WidgetElement controls)=>
        UI.Grid(id+".content",[GridTrack.Auto(),GridTrack.Star()],[GridTrack.Star()],
            controls,UI.VerticalScroll(id+".details",UI.Stack(id+".body",details.ToArray()).Classes("pob-inspection-body"))
                .InGrid(row:1).Classes("pob-workspace")).Spacing(10,0).Classes("pob-workspace");
}
