using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement inventoryState, authoredItem;
    private int inventoryId, authoringId;
    private string inventorySearch="", inventorySlot="";
    private string craftRarity="RARE";
    private ListQuery inventoryQuery=new("",[]);
    private WidgetIndexedCollection<ListQuery,ListEntry>? inventoryCollection;
    private sealed record InventoryView(JsonElement State,int Id,JsonElement Item,string[] Slots);
    private string[] inventorySlots=[];

    private async Task<InventoryView> ReadInventoryAsync(string xml,int selected,CancellationToken token)
    {
        var list=(await engine.ExecuteAsync(new{op="inventory-list"},token,xml)).State;
        var rows=A(list,"inventory");
        if(!rows.Any(i=>I(i,"id")==selected))selected=I(rows.FirstOrDefault(),"id");
        if(selected==0)return new(list,0,default,[]);
        var item=(await engine.ExecuteAsync(new{op="inventory-inspect",itemId=selected},token,xml)).State.GetProperty("authoring");
        return new(list,selected,item,await ItemSlotsAsync(S(item,"raw"),xml,token));
    }

    private void PublishInventory(InventoryView view)
    {
        inventoryState=view.State;inventoryId=view.Id;authoredItem=view.Item;inventorySlots=view.Slots;
        if(!inventorySlots.Contains(inventorySlot))inventorySlot=inventorySlots.FirstOrDefault()??"";
        if(detailKind=="item-author")
        {
            authoringId=inventoryId;
            if(authoringId==0)detailKind="inventory";
        }
    }

    private void OpenInventory(int selected=0)=>Work(async token=>
    {
        var view=await ReadInventoryAsync(result!.Xml,selected,token);
        lock(gate){var entering=detailKind!="inventory";PublishInventory(view);detailKind="inventory";section="Items";if(entering)focusRequestSequence++;}
    },"Reading inventory…");

    private void OpenItemAuthoring(int itemId)=>Work(async token=>
    {
        var response=await engine.ExecuteAsync(itemId>0?new{op="inventory-inspect",itemId}:(object)new{op="item-draft",mode="inspect",raw=pastedItem},token,result!.Xml);
        lock(gate){authoredItem=response.State.GetProperty("authoring");authoringId=itemId;detailKind="item-author";focusRequestSequence++;}
    },"Reading item editor…");

    private void ChangeItemDraft(object command)=>Work(async token=>
    {
        var response=await engine.ExecuteAsync(command,token,result!.Xml);
        var next=response.State.GetProperty("authoring");var raw=S(next,"raw");
        var slots=await ItemSlotsAsync(raw,result.Xml,token);
        token.ThrowIfCancellationRequested();SaveWork(result,CurrentWork(result.Xml,draft:raw),refreshXml:false);
        lock(gate){pastedItem=raw;itemDraftSlots=slots;authoredItem=next;authoringId=0;featurePicker=null;detailKind="item-author";}
    },"Updating item draft…");

    private object AuthorCommand(string mode,int slot=0,string? modId=null,int value=0,int id=0,string? text=null,string? control=null)=>
        authoringId>0?new{op="item-author",mode,itemId=authoringId,slot,modId,value,id,text,control}:
        new{op="item-draft",mode,raw=pastedItem,slot,modId,value,id,text,control};

    private void ChangeAuthoredItem(object command)
    {if(authoringId>0)Edit(command);else ChangeItemDraft(command);}

    private void StoreItemDraft()=>Work(async token=>
    {
        var before=result!;
        var next=await engine.ExecuteAsync(new{op="inventory-edit",mode="add",raw=pastedItem},token,before.Xml);
        var view=await ReadInventoryAsync(next.Xml,I(next.State,"editedInventoryId"),token);
        token.ThrowIfCancellationRequested();SaveWork(next,new(next.Xml,undo.Prepend(before.Xml).Take(12).ToArray(),[]));
        lock(gate)
        {
            PushHistory(undo,before.Xml);redo.Clear();result=next;pastedItem=null;itemDraftSlots=[];
            detailKind="inventory";PublishInventory(view);focusRequestSequence++;
        }
        await RefreshCommittedMapAsync(token);
    },"Saving item to inventory…");

    private bool HandleItemAuthoringAction(WidgetActionEvent action)
    {
        if(result is null)return false;
        var id=action.ActionId;var parts=id.Split('.');
        if(id=="inventory"){OpenInventory(inventoryId);return true;}
        if(id=="inventory-search"){inventorySearch=action.CommittedText??"";return true;}
        if(parts is ["inventory-select",var selected]&&int.TryParse(selected,out var selectedId)&&A(inventoryState,"inventory").Any(i=>I(i,"id")==selectedId))
        {OpenInventory(selectedId);return true;}
        if(parts is ["inventory-slot",var slot]&&int.TryParse(slot,out var slotIndex)&&slotIndex>=0&&slotIndex<inventorySlots.Length)
        {inventorySlot=inventorySlots[slotIndex];return true;}
        if(id=="inventory-edit"&&inventoryId>0){OpenItemAuthoring(inventoryId);return true;}
        if(id=="equipped-author")
        {var item=A(State,"items").FirstOrDefault(i=>I(i,"id")==detailId);if(I(item,"itemId")>0)OpenItemAuthoring(I(item,"itemId"));return true;}
        if(id=="draft-edit"&&pastedItem is not null){OpenItemAuthoring(0);return true;}
        if(id=="draft-store"&&pastedItem is not null){StoreItemDraft();return true;}
        if(id=="draft-compare"&&pastedItem is not null){detailKind="choose-slot";focusRequestSequence++;return true;}
        if(id=="item-uniques")
        {
            if(pastedItem is not null){ShowFeedback("An item draft is already open","Save or discard the draft before choosing another item.",ToastTone.Info);return true;}
            Work(async token=>
            {
                var response=await engine.ExecuteAsync(new{op="item-uniques"},token,result.Xml);
                lock(gate)OpenFeaturePicker("Unique item library","unique-item.",A(response.State,"choices"));
            },"Reading unique items…");return true;
        }
        if(parts is ["unique-item",var uniqueId]&&int.TryParse(uniqueId,out var uniqueIndex)&&featurePicker is {Prefix:"unique-item."} uniques&&pastedItem is null)
        {
            var unique=uniques.Choices.FirstOrDefault(c=>I(c,"id")==uniqueIndex);
            if(unique.ValueKind==JsonValueKind.Object)ChangeItemDraft(new{op="item-unique-draft",key=S(unique,"key")});
            return true;
        }
        if(parts is ["craft-rarity",var rarity]&&(rarity is "NORMAL" or "MAGIC" or "RARE")&&featurePicker is {Prefix:"craft-base."})
        {craftRarity=rarity;return true;}
        if(id=="item-create")
        {
            if(pastedItem is not null){OpenItemAuthoring(0);return true;}
            Work(async token=>
            {
                var response=await engine.ExecuteAsync(new{op="item-bases"},token,result.Xml);
                lock(gate)OpenFeaturePicker("Choose an item base","craft-base.",A(response.State,"choices"));
            },"Reading item bases…");return true;
        }
        if(parts is ["craft-base",var baseId]&&int.TryParse(baseId,out var baseIndex)&&featurePicker is {Prefix:"craft-base."} bases&&pastedItem is null)
        {
            var basis=bases.Choices.FirstOrDefault(c=>I(c,"id")==baseIndex);
            if(basis.ValueKind==JsonValueKind.Object)
            {ChangeItemDraft(new{op="item-draft",mode="create",kind=S(basis,"kind"),@base=S(basis,"base"),rarity=craftRarity});}
            return true;
        }
        if(id is "inventory-copy" or "inventory-delete" or "inventory-equip" or "inventory-compare"&&detailKind=="inventory"&&inventoryId>0)
        {
            if(id=="inventory-delete")featureRemoval=("Delete this item from inventory and every set using it?",new{op="inventory-edit",mode="delete",id=inventoryId});
            else if(id=="inventory-copy")Edit(new{op="inventory-edit",mode="copy",id=inventoryId});
            else if(inventorySlot.Length>0)
            {
                var command=new{op="inventory-edit",mode="equip",id=inventoryId,slot=inventorySlot};
                if(id=="inventory-compare")ShowPlanningComparison(command,"Equip in "+inventorySlot);
                else Edit(command);
            }
            return true;
        }
        if(id=="back"&&detailKind=="inventory"){detailKind="";focusRequestSequence++;return true;}
        if(id=="back"&&detailKind=="item-author")
        {if(authoringId>0)OpenInventory(authoringId);else{detailKind="";focusRequestSequence++;}return true;}
        if(parts is ["author-setting-choice",var settingId,var selection]&&int.TryParse(settingId,out var settingIndex)&&int.TryParse(selection,out var selectedOption))
        {
            if(!(detailKind=="item-author"||featurePicker?.Prefix=="author-setting-choice."+settingIndex+".")||settingIndex<0||settingIndex>=A(authoredItem,"controls").Length)return true;
            var selectedSetting=A(authoredItem,"controls")[settingIndex];
            if(B(selectedSetting,"enabled")&&A(selectedSetting,"options").Any(o=>I(o,"id")==selectedOption))
            {featurePicker=null;detailKind="item-author";ChangeAuthoredItem(AuthorCommand("control",control:S(selectedSetting,"id"),value:selectedOption));}
            return true;
        }
        if(parts is ["author-affix",var affix]&&int.TryParse(affix,out var affixId)&&detailKind=="item-author")
        {
            Work(async token=>
            {
                var response=await engine.ExecuteAsync(new{op="item-draft",mode="choices",raw=S(authoredItem,"raw"),slot=affixId},token,result.Xml);
                lock(gate)OpenFeaturePicker("Choose affix","author-choice."+affixId+".",A(response.State.GetProperty("authoring"),"choices"));
            },"Reading eligible affixes…");return true;
        }
        if(parts is ["author-choice",var choiceSlot,var choiceId]&&int.TryParse(choiceSlot,out var chosenSlot)&&int.TryParse(choiceId,out var chosenId)&&featurePicker is {} choices)
        {
            var choice=choices.Choices.FirstOrDefault(c=>I(c,"id")==chosenId);
            if(choice.ValueKind==JsonValueKind.Object){featurePicker=null;detailKind="item-author";ChangeAuthoredItem(AuthorCommand("affix",slot:chosenSlot,modId:S(choice,"modId")));}
            return true;
        }
        if(detailKind!="item-author")return false;
        if(parts is ["author-setting",var setting]&&int.TryParse(setting,out var settingNumber)&&settingNumber>=0&&settingNumber<A(authoredItem,"controls").Length)
        {
            var control=A(authoredItem,"controls")[settingNumber];if(!B(control,"enabled"))return true;
            var options=A(control,"options");
            if(options.Length>0)OpenFeaturePicker(Plain(S(control,"title")),"author-setting-choice."+settingNumber+".",options);
            else if(int.TryParse(action.CommittedText,out var value)&&value>=0&&value<=99)ChangeAuthoredItem(AuthorCommand("control",control:S(control,"id"),value:value));
            else ShowFeedback("Invalid quality","Enter a whole value from 0 to 99.",ToastTone.Warning);
            return true;
        }
        if(parts is ["author-roll" or "author-range",var rollId]&&int.TryParse(rollId,out var range))
        {
            if(int.TryParse(action.CommittedText,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)&&value>=0&&value<=100)
                ChangeAuthoredItem(AuthorCommand(parts[0]=="author-roll"?"roll":"range",slot:range,id:range,value:value));
            else ShowFeedback("Invalid roll","Enter a whole percentage from 0 to 100.",ToastTone.Warning);
            return true;
        }
        if(id=="author-custom"){ChangeAuthoredItem(AuthorCommand("custom-add",text:action.CommittedText??""));return true;}
        if(parts is ["author-remove",var custom]&&int.TryParse(custom,out var customId))
        {ChangeAuthoredItem(AuthorCommand("custom-delete",id:customId));return true;}
        return false;
    }

    private WidgetElement StoredInventoryWorkspace()
    {
        inventoryCollection??=CreateIndexedCollection<ListQuery,ListEntry>("pob.stored.source",inventoryQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult<IReadOnlyList<ListEntry>>(query.Entries.Skip(start).Take(count).ToArray());},
            ItemKey=entry=>new(entry.Key),
            RenderItem=(_,entry,context)=>UI.Button(entry.Label,"select",context.Id("entry")).Selected(entry.Selected).FocusRight("pob.inventory-edit").Classes("pob-list-item"),
            OnAction=(query,entry,action,token)=>{lock(gate){if(!ReferenceEquals(query,inventoryQuery)||busy||detailKind!="inventory"||action.ActionId!="select")return ValueTask.CompletedTask;return OnActionAsync(action with{ActionId=entry.Action},token);}}
        });
        var all=A(inventoryState,"inventory");
        var entries=all.Where(i=>(S(i,"title")+" "+S(i,"base")).Contains(inventorySearch,StringComparison.OrdinalIgnoreCase))
            .Select(i=>new ListEntry(I(i,"id").ToString(),S(i,"title")+(A(i,"uses").Length>0?" · In use":" · Unequipped"),"inventory-select."+I(i,"id"),I(i,"id")==inventoryId)).ToArray();
        if(!inventoryQuery.Entries.SequenceEqual(entries))
        {
            var same=inventoryQuery.Entries.Select(e=>e.Key).SequenceEqual(entries.Select(e=>e.Key));inventoryQuery=new("inventory",entries);
            if(same)inventoryCollection.UpdateContent(inventoryQuery);else inventoryCollection.PublishQuery(inventoryQuery,entries.Length);
        }
        var tools=QuickControls(UI.Stack("pob.stored.tools",UI.Row("pob.stored.header",Button("Back to equipment","back"),Text("Item inventory","stored.title","pob-heading")).Classes("pob-toolbar"),
            UI.Row("pob.stored.create",Button(pastedItem is null?"Craft item":"Continue item draft","item-create"),Button("Unique library","item-uniques").Disabled(pastedItem is not null),Button("Paste item","item-paste").Disabled(pastedItem is not null)).Classes("pob-toolbar"),
            UI.TextEntry(inventorySearch,"Search inventory","inventory-search","pob.inventory-search",100).Classes("pob-input"),Text($"{entries.Length} of {all.Length} items","stored.count","pob-muted")),"pob.inventory-search","pob.stored.list","Items",NeedsQuickControls(entries.Length));
        var list=QuickControls(UI.Stack("pob.stored.list",UI.CollectionList("pob.stored.items",inventoryCollection,64,"Item inventory").Classes("pob-scroll")).Classes("pob-workspace"),"pob.stored.items","pob.stored.tools","Search",NeedsQuickControls(entries.Length));
        var details=new List<WidgetElement>();
        if(inventoryId>0)
        {
            details.Add(Text(S(authoredItem,"name"),"stored.name","pob-heading"));
            details.Add(UI.Row("pob.stored.actions",Button("Edit item","inventory-edit").FocusLeft("pob.stored.items"),Button("Duplicate","inventory-copy"),Button("Delete…","inventory-delete")).Classes("pob-toolbar"));
            var uses=A(all.FirstOrDefault(i=>I(i,"id")==inventoryId),"uses").Select(u=>u.GetString()??"").ToArray();
            details.Add(Text(uses.Length==0?"Not equipped in any set.":"Used by: "+string.Join("; ",uses),"stored.uses","pob-small pob-muted"));
            if(inventorySlots.Length>0)
            {
                details.Add(UI.Select("Equipment slot",inventorySlots.Select((s,i)=>new SelectOption(i.ToString(),s,"inventory-slot."+i,s==inventorySlot)).ToArray(),"pob.inventory-slot").Classes("pob-select"));
                details.Add(UI.Row("pob.stored.equip",Button("Compare","inventory-compare"),Button("Equip","inventory-equip")).Classes("pob-toolbar"));
            }
            AddAuthoredDescription(details,"stored");
        }
        else details.Add(Text("Craft or paste an item to add it to this build's inventory.","stored.empty","pob-muted"));
        return Split("stored",UI.Stack("pob.stored.left",tools,list).Classes("pob-panel","pob-workspace"),
            UI.VerticalScroll("pob.stored.details",details.ToArray()).RememberChildFocus("pob.inventory-edit").Classes("pob-scroll","pob-panel"),1,1.5);
    }

    private void AddAuthoredDescription(List<WidgetElement> rows,string prefix)
    {foreach(var (line,i) in A(authoredItem,"details").Select((l,i)=>(l,i)))AddFormatted(rows,line,"text","runs",prefix+".description."+i,emphasizeStats:true);}

    private WidgetElement ItemAuthoringWorkspace()
    {
        var controls=new List<WidgetElement>{Button(authoringId>0?"Back to inventory":"Keep draft and return","back"),
            Text(authoringId>0?"Edit item":"Crafted / imported item draft","author.title","pob-heading"),
            Text(authoringId>0?"Edits save immediately and affect every set using this item. Duplicate it first for an independent version. Undo restores edits.":"Your draft is saved automatically. Save to inventory to keep it unequipped, or compare it with your equipment.","author.help","pob-small pob-muted")};
        if(authoringId==0)controls.Add(UI.Row("pob.author.draft-actions",Button("Save to inventory","draft-store"),Button("Compare equipment","draft-compare")).Classes("pob-toolbar"));
        foreach(var (control,i) in A(authoredItem,"controls").Select((c,i)=>(c,i)))
        {
            var options=A(control,"options");
            if(options.Length>0)controls.Add(ItemChoiceControl(control,"author-setting."+i,"author-setting-choice."+i+"."));
            else controls.Add(Field(S(control,"title"),UI.TextEntry(S(control,"value"),S(control,"title"),"author-setting."+i,"pob.author-setting."+i,2).Disabled(!B(control,"enabled")).Classes("pob-input"),"author-setting."+i));
        }
        foreach(var affix in A(authoredItem,"affixes"))
        {
            var id=I(affix,"id");
            controls.Add(UI.Stack("pob.author.affix."+id,Text(S(affix,"title"),"author.affix.label."+id,"pob-eyebrow"),
                Button(S(affix,"selected","None"),"author-affix."+id),
                Field("Roll %",UI.TextEntry(I(affix,"roll").ToString(),"0–100","author-roll."+id,"pob.author-roll."+id,3).Disabled(S(affix,"modId")=="None").Classes("pob-input"),"author-roll."+id)).Classes("pob-context-card"));
        }
        if(!B(authoredItem,"crafted"))foreach(var range in A(authoredItem,"ranges"))
            controls.Add(Field(S(range,"title")+" · Roll %",UI.TextEntry(I(range,"roll").ToString(),"0–100","author-range."+I(range,"id"),"pob.author-range."+I(range,"id"),3).Classes("pob-input"),"author-range."+I(range,"id")));
        if(B(authoredItem,"canAddCustom"))controls.Add(Field("Add custom modifier",UI.TextEntry("","e.g. +25 to maximum Mana","author-custom","pob.author-custom",2048).Classes("pob-input"),"author-custom"));
        foreach(var custom in A(authoredItem,"custom"))
        {
            var rows=new List<WidgetElement>();AddFormatted(rows,custom,"text","runs","author.custom."+I(custom,"id"),emphasizeStats:true);
            rows.Add(Button("Remove modifier","author-remove."+I(custom,"id")));controls.Add(UI.Stack("pob.author.custom."+I(custom,"id"),rows.ToArray()).Classes("pob-context-card"));
        }
        var description=new List<WidgetElement>{Text(S(authoredItem,"name"),"author.name","pob-heading")};AddAuthoredDescription(description,"author");
        return Split("author",QuickControls(UI.VerticalScroll("pob.author.controls",controls.ToArray()).Classes("pob-scroll","pob-panel"),"pob.back","pob.author.controls","Item controls",NeedsQuickControls(controls.Count)),
            UI.VerticalScroll("pob.author.description",description.ToArray()).Classes("pob-scroll","pob-panel"),1.2,1);
    }
}
