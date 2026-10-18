using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private sealed record ItemCandidateContext(PobResult Build,string? Document,object Catalog,string Search,bool Unique);
    private sealed record ItemCandidateEntry(string Key,string Title,string Action,int ItemId,string UniqueKey,string Description,
        string Rarity,string Uses,bool Variants,bool Selected=false);
    private sealed record ItemCandidateQuery(ItemCandidateContext? Context,ItemCandidateEntry[] Entries);
    private sealed record ItemCandidateInspection(long Token,ItemCandidateQuery Query,ItemCandidateEntry Entry,
        IndexedCollectionFocusTarget Anchor,JsonElement Item,string[] Slots,string Slot);
    private ItemCandidateQuery uniqueCandidateQuery=new(null,[]);
    private WidgetIndexedCollection<ItemCandidateQuery,ItemCandidateEntry>? uniqueCandidateCollection;
    private ItemCandidateInspection? candidateInspection;
    private long candidateInspectionToken;

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
    private WidgetIndexedCollection<ItemCandidateQuery,ItemCandidateEntry> CreateItemCandidateCollection(string sourceId,ItemCandidateQuery initial,string collectionId)=>
        CreateIndexedCollection<ItemCandidateQuery,ItemCandidateEntry>(sourceId,initial,0,new()
        {
            ReadRange=(query,start,count,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult<IReadOnlyList<ItemCandidateEntry>>(query.Entries.Skip(start).Take(count).ToArray());},
            ItemKey=entry=>new(entry.Key),
            RenderItem=(query,entry,context)=>RenderItemCandidate(query,entry,context),
            OnAction=(query,entry,action,token)=>
            {
                lock(gate)
                {
                    if(busy||candidateInspection is not null||!ItemCandidateIsCurrent(query))return ValueTask.CompletedTask;
                    if(action.ActionId=="inspect"){OpenItemCandidateInspection(query,entry,collectionId);return ValueTask.CompletedTask;}
                    return action.ActionId=="select"?OnActionAsync(action with{ActionId=entry.Action},token):ValueTask.CompletedTask;
                }
            },
        });
    private static WidgetElement RenderItemCandidate(ItemCandidateQuery query,ItemCandidateEntry entry,WidgetIndexedItemContext context)
    {
        var unique=query.Context!.Unique;
        var label=entry.Title+(unique?"":entry.Uses.Length>0?" · In use":" · Unequipped");
        var candidate=UI.ActionSurface("select",context.Id("entry"),label,ActionSurfaceOrientation.Horizontal,
            UI.Text(label,context.Id("title")).Classes("pob-inline")) with{ContextActions=[new("inspect","Inspect item")]};
        candidate=candidate.Selected(entry.Selected).Shortcut(ControllerButton.X,"Inspect",actionId:"inspect");
        if(!unique)candidate=candidate.FocusRight("pob.inventory-edit");
        var preview=new List<WidgetElement>{UI.Text(entry.Title,context.Id("preview-title")).Classes("pob-heading")};
        if(entry.Rarity.Length>0)preview.Add(UI.Text(entry.Rarity,context.Id("preview-rarity")).Classes("pob-small","pob-gold"));
        if(entry.Description.Length>0)preview.Add(UI.Text(CandidateText(entry.Description,320),context.Id("preview-description")).Classes("pob-small"));
        if(!unique)preview.Add(UI.Text(entry.Uses.Length==0?"Not equipped in any set":"Used by: "+CandidateText(entry.Uses,180),context.Id("preview-uses")).Classes("pob-small","pob-muted"));
        if(entry.Variants)preview.Add(UI.Text("Variants available in the item editor",context.Id("preview-variants")).Classes("pob-small","pob-muted"));
        preview.Add(UI.ControllerHint(ControllerPrompt.X,"Inspect item",context.Id("preview-inspect")));
        return candidate.Classes("pob-list-item").PresentOnFocus(UI.Stack(context.Id("preview"),preview.ToArray()));
    }
    private static WidgetElement ItemCandidatePreview(WidgetElement list,string id)=>
        UI.FocusPresentationSurface(list,UI.Text("",id+".empty"),id) with{Preview=new(Width:420,Height:280)};

    private WidgetElement UniqueCandidateWorkspace(JsonElement[] choices)
    {
        uniqueCandidateCollection??=CreateItemCandidateCollection("pob.unique.source",uniqueCandidateQuery,"pob.choices");
        var context=ItemCandidateContextFor(choices,choiceSearch,unique:true);
        var entries=choices.Where(c=>Plain(S(c,"title")+" "+S(c,"description")).Contains(choiceSearch,StringComparison.OrdinalIgnoreCase))
            .Select(c=>new ItemCandidateEntry("unique"+I(c,"id"),CandidateText(S(c,"title"),512),"unique-item."+I(c,"id"),0,S(c,"key"),
                CandidateText(S(c,"description"),2400),"Unique","",B(c,"hasVariants"))).ToArray();
        if(!uniqueCandidateQuery.Entries.SequenceEqual(entries)||uniqueCandidateQuery.Context!=context||!ReferenceEquals(uniqueCandidateQuery.Context?.Build,result))
        {uniqueCandidateQuery=new(context,entries);uniqueCandidateCollection.PublishQuery(uniqueCandidateQuery,entries.Length);}
        var tools=QuickControls(UI.Stack("pob.choice-tools",
            UI.Row("pob.choice.header",Button("Back","back"),Text("Unique item library","choice.title","pob-heading")).Classes("pob-toolbar"),
            SearchField(UI.TextEntry(choiceSearch,"Search unique names or bases","choice-search","pob.choice-search",100),choices,
                ()=>choices.Select(c=>(Plain(S(c,"title")),Plain(S(c,"title")+" "+S(c,"description")),Plain(S(c,"description"))))).Classes("pob-input"),
            Text($"{entries.Length} unique items","choice.count","pob-muted")),"pob.choice-search","pob.choice-results","Items",NeedsQuickControls(entries.Length));
        var list=entries.Length==0?Text("No matching items.","choice.empty"):
            ItemCandidatePreview(UI.CollectionList("pob.choices",uniqueCandidateCollection,64,"Unique item library").Classes("pob-scroll"),"pob.unique.preview");
        return UI.Stack("pob.choice-picker",tools,
            QuickControls(UI.Stack("pob.choice-results",list).Classes("pob-workspace"),"pob.choices","pob.choice-tools","Search",NeedsQuickControls(entries.Length))).Classes("pob-workspace");
    }

    private void OpenItemCandidateInspection(ItemCandidateQuery query,ItemCandidateEntry entry,string collectionId)
    {
        var collection=query.Context!.Unique?uniqueCandidateCollection:inventoryCollection;
        var index=Array.FindIndex(query.Entries,candidate=>ReferenceEquals(candidate,entry));
        if(collection is null||index<0||!ItemCandidateIsCurrent(query))return;
        var anchor=collection.FocusTarget(collectionId,new(entry.Key),index);var tokenId=++candidateInspectionToken;
        if(!query.Context.Unique&&entry.ItemId==inventoryId&&authoredItem.ValueKind==JsonValueKind.Object)
        {
            candidateInspection=new(tokenId,query,entry,anchor,authoredItem,inventorySlots.ToArray(),inventorySlot);
            Invalidate();return;
        }
        Work(async token=>
        {
            var xml=query.Context.Build.Xml;
            var response=await engine.ExecuteAsync(query.Context.Unique?new{op="item-unique-draft",key=entry.UniqueKey}:
                (object)new{op="inventory-inspect",itemId=entry.ItemId},token,xml);
            var item=response.State.GetProperty("authoring").Clone();
            if(S(item,"raw").Length>32768||A(item,"details").Length>128)
                throw new InvalidDataException("PoB returned item details beyond the supported inspection limit.");
            var slots=await ItemSlotsAsync(S(item,"raw"),xml,token);
            token.ThrowIfCancellationRequested();
            lock(gate)if(candidateInspectionToken==tokenId&&ItemCandidateIsCurrent(query))
                candidateInspection=new(tokenId,query,entry,anchor,item,slots,slots.Contains(inventorySlot)?inventorySlot:slots.FirstOrDefault()??"");
        },"Reading item details…");
    }
    private bool HandleItemCandidateInspectionAction(WidgetActionEvent action)
    {
        var id=action.ActionId;
        if(candidateInspection is not {} inspection)return id.StartsWith("candidate-inspection-",StringComparison.Ordinal);
        if(!ItemCandidateIsCurrent(inspection.Query)){candidateInspection=null;return true;}
        if(id=="candidate-inspection-close."+inspection.Token){candidateInspection=null;return true;}
        var slotPrefix="candidate-inspection-slot."+inspection.Token+".";
        if(id.StartsWith(slotPrefix,StringComparison.Ordinal)&&int.TryParse(id[slotPrefix.Length..],out var index)&&index>=0&&index<inspection.Slots.Length)
        {candidateInspection=inspection with{Slot=inspection.Slots[index]};return true;}
        if(id=="candidate-inspection-equip."+inspection.Token&&!inspection.Query.Context!.Unique&&inspection.Slots.Contains(inspection.Slot))
        {
            candidateInspection=null;
            Edit(new{op="inventory-edit",mode="equip",id=inspection.Entry.ItemId,slot=inspection.Slot});
        }
        else if(id=="candidate-inspection-choose."+inspection.Token)
        {
            candidateInspection=null;
            if(inspection.Query.Context!.Unique)ChooseInspectedUnique(inspection);
            else OpenInventory(inspection.Entry.ItemId);
        }
        return true;
    }
    private void ChooseInspectedUnique(ItemCandidateInspection inspection)=>Work(token=>
    {
        token.ThrowIfCancellationRequested();
        var before=inspection.Query.Context!.Build;var raw=S(inspection.Item,"raw");
        lock(gate)
        {
            if(!ItemCandidateIsCurrent(inspection.Query))return Task.CompletedTask;
            SaveWork(before,CurrentWork(before.Xml,draft:raw) with{TradeBrowse=false},refreshXml:false);
            tradeBrowsing=false;pastedItem=raw;itemDraftSlots=inspection.Slots;authoredItem=inspection.Item;
            authoringId=0;featurePicker=null;detailKind="item-author";
        }
        return Task.CompletedTask;
    },"Opening item draft…");
    private WidgetView WithItemCandidateInspection(WidgetView view)
    {
        if(candidateInspection is not {} inspection)return view;
        if(!ItemCandidateIsCurrent(inspection.Query)){candidateInspection=null;return view;}
        var unique=inspection.Query.Context!.Unique;var close="candidate-inspection-close."+inspection.Token;
        var rows=new List<WidgetElement>();
        if(!unique)rows.Add(Text(inspection.Entry.Uses.Length==0?"Not equipped in any set.":"Used by: "+inspection.Entry.Uses,"candidate-inspection.uses","pob-small pob-muted"));
        foreach(var (line,i) in A(inspection.Item,"details").Select((line,i)=>(line,i)))
            AddFormatted(rows,line,"text","runs","candidate-inspection.line."+i,emphasizeStats:true);
        if(!unique&&inspection.Slots.Length>0)rows.Add(UI.Select("Equip in",inspection.Slots.Select((slot,i)=>new SelectOption("slot"+i,slot,
            "candidate-inspection-slot."+inspection.Token+"."+i,slot==inspection.Slot)).ToArray(),"pob.candidate-inspection.slot").Classes("pob-select"));
        var actions=new List<WidgetElement>{Button("Back to items",close),Button(unique?"Open item draft":"Select item","candidate-inspection-choose."+inspection.Token)};
        if(!unique&&inspection.Slots.Length>0)actions.Add(Button("Equip in "+inspection.Slot,"candidate-inspection-equip."+inspection.Token));
        rows.Add(UI.Row("pob.candidate-inspection.actions",actions.ToArray()).Classes("pob-toolbar"));
        return view.WithModal(new("pob.candidate-inspection",inspection.Entry.Title,UI.Stack("pob.candidate-inspection.content",rows.ToArray()).Classes("pob-card"),
            "pob."+close,close){AnchoredPresentation=new(new(IndexedItem:inspection.Anchor),Width:620,Height:620)});
    }
}
