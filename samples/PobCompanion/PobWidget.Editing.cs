using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly Stack<string> redo = new();
    private bool historyFocusRequested;

    private static void PushHistory(Stack<string> history,string xml)
    {
        history.Push(xml);
        if(history.Count<=12)return;
        var retained=history.Take(12).Reverse().ToArray();
        history.Clear();foreach(var entry in retained)history.Push(entry);
    }

    private void Edit(object command,bool closeDetail=false,string? skillDestination=null)=>Work(async token=>
    {
        var before=result!;
        var next=await engine.ExecuteAsync(command is Func<object> create?create():command,token,before.Xml);
        var options=await ReadVisibleSkillOptionsAsync(next,token,skillDestination);
        var gem=await ReadVisibleGemOptionsAsync(next,token);
        await RefreshItemEditorAsync(next,token);
        var inventory=detailKind=="inventory"||detailKind=="item-author"&&authoringId>0
            ?await ReadInventoryAsync(next.Xml,I(next.State,"editedInventoryId")>0?I(next.State,"editedInventoryId"):inventoryId,token):null;
        var calculations=detailKind=="calculations"?await ReadCalculationsAsync(next.Xml,token):null;
        JsonElement? finderProjection=null;TimelessRow[]? finderRows=null;
        if(detailKind=="timeless")
        {
            finderProjection=next.State.TryGetProperty("timeless",out var finderState)?finderState:(await engine.ExecuteAsync(new{op="timeless-catalog"},token,next.Xml)).State;
            finderRows=await ReadTimelessRowsAsync(finderProjection.Value,next.Xml,token);
        }
        // Cancellation or calculation failure before this boundary never saves
        // a partial edit or changes history. Publication follows atomic file save.
        token.ThrowIfCancellationRequested();
        SaveWork(next, new(next.Xml, undo.Prepend(before.Xml).Take(12).ToArray(), [], ItemDraft: pastedItem,TradeListing:pastedItem is null?null:tradeListing));
        lock(gate)
        {
            PushHistory(undo,before.Xml);redo.Clear();result=next;
            if(finderProjection is {} finder){timelessState=finder;timelessRows=finderRows??[];}
            if(I(next.State,"timelessAddedSeed")>0)ShowFeedback("Jewel added","Seed "+I(next.State,"timelessAddedSeed")+" was added to item inventory. Equip it there to compare the build change.",WidgetRail.WidgetSdk.ToastTone.Info);
            if(gem is {} gemValue)gemOptions=gemValue;else if(detailKind=="gem-options")detailKind="skill";
            if(inventory is not null)PublishInventory(inventory);
            if(calculations is not null)PublishCalculations(calculations);
            if(detailKind=="sets" && I(next.State,"editedSetId")>0){managedSetId=I(next.State,"editedSetId");newSetName="";}
            if(skillDestination is not null)
            {
                var editedGroup=I(next.State,"editedSkillGroup");
                var changedTarget=detailId!=editedGroup||detailKind!=skillDestination;
                detailId=editedGroup;selections["skill"]=detailId;
                detailKind=detailId>0?skillDestination:"";
                if(changedTarget||skillOrderFocusRequested){focusRequestSequence++;skillWorkspaceFocusRequested=detailKind=="skill";}
            }
            else if(closeDetail)detailKind="";
            PublishVisibleSkillOptions(options);
        }
        await RefreshCommittedMapAsync(token);
    },"Updating build…");

    private void MoveHistory(bool backwards)=>Work(async token=>
    {
        var source=backwards?undo:redo;var destination=backwards?redo:undo;
        var before=result!;var xml=source.Peek();
        var next=await engine.ExecuteAsync(new{op="load",xml,name="Working build"},token);
        var options=await ReadVisibleSkillOptionsAsync(next,token);
        var gem=await ReadVisibleGemOptionsAsync(next,token);
        await RefreshItemEditorAsync(next,token);
        var inventory=detailKind=="inventory"||detailKind=="item-author"&&authoringId>0
            ?await ReadInventoryAsync(next.Xml,detailKind=="item-author"?authoringId:inventoryId,token):null;
        var calculations=detailKind=="calculations"?await ReadCalculationsAsync(next.Xml,token):null;
        var timeless=detailKind=="timeless"?(await engine.ExecuteAsync(new{op="timeless-catalog"},token,next.Xml)).State:(JsonElement?)null;
        BuildDocument.VerifySkillPreservation(xml,next.Xml);
        token.ThrowIfCancellationRequested();
        var remaining=source.Skip(1).ToArray();var moved=destination.Prepend(before.Xml).Take(12).ToArray();
        SaveWork(next,new(next.Xml,backwards?remaining:moved,backwards?moved:remaining,ItemDraft:pastedItem,TradeListing:pastedItem is null?null:tradeListing));
        lock(gate)
        {
            source.Pop();PushHistory(destination,before.Xml);result=next;
            if(timeless is {} finder){timelessState=finder;timelessRows=[];}
            if(gem is {} gemValue)gemOptions=gemValue;else if(detailKind=="gem-options")detailKind="skill";
            if(inventory is not null)PublishInventory(inventory);
            if(calculations is not null)PublishCalculations(calculations);
            PublishVisibleSkillOptions(options);
            historyFocusRequested=true;focusRequestSequence++;
        }
        await RefreshCommittedMapAsync(token);
    },backwards?"Undoing change…":"Redoing change…");

    private async Task<JsonElement?> ReadVisibleSkillOptionsAsync(PobResult next,CancellationToken token,string? skillDestination=null)
    {
        var kind=skillDestination??detailKind;
        if(section!="Skills"||kind is not ("skill" or "skill-options" or "skill-manage"))return null;
        var group=skillDestination is null?detailId:I(next.State,"editedSkillGroup");
        if(!A(next.State,"skills").Any(s=>I(s,"id")==group))return null;
        if(I(next.State,"skillOptionsGroup")==group&&next.State.TryGetProperty("skillOptions",out var options))return options;
        return await ReadSkillOptionsAsync(group,next.Xml,token);
    }

    private void PublishVisibleSkillOptions(JsonElement? options)
    {
        if(section!="Skills"||detailKind is not ("skill" or "skill-options" or "skill-manage"))return;
        if(options is {} value){inspectedSkillOptions=value;inspectedSkillGroup=detailId;inspectedSkillXml=result!.Xml;inspectedSkillDocument=documentId;}
        else {inspectedSkillGroup=0;inspectedSkillXml=null;if(detailKind=="skill-options")detailKind="";}
    }

    private async Task RefreshCommittedMapAsync(CancellationToken token)
    {
        // Once saved, late cancellation only skips the optional map refresh. It
        // must not report that a successful edit was cancelled.
        try{await RefreshMapAsync(token);}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
    }
}
