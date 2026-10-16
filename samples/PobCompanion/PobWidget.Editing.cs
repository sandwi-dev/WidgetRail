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

    private void Edit(object command,bool closeDetail=false)=>Work(async token=>
    {
        var before=result!;
        var next=await engine.ExecuteAsync(command,token,before.Xml);
        var options=await ReadVisibleSkillOptionsAsync(next,token);
        // Cancellation or calculation failure before this boundary never saves
        // a partial edit or changes history. Publication follows atomic file save.
        token.ThrowIfCancellationRequested();
        SaveWork(next, new(next.Xml, undo.Prepend(before.Xml).Take(12).ToArray(), [], ItemDraft: pastedItem));
        lock(gate)
        {
            PushHistory(undo,before.Xml);redo.Clear();result=next;
            if(closeDetail)detailKind="";
            else PublishVisibleSkillOptions(options);
        }
        await RefreshCommittedMapAsync(token);
    },"Updating build…");

    private void MoveHistory(bool backwards)=>Work(async token=>
    {
        var source=backwards?undo:redo;var destination=backwards?redo:undo;
        var before=result!;var xml=source.Peek();
        var next=await engine.ExecuteAsync(new{op="load",xml,name="Working build"},token);
        var options=await ReadVisibleSkillOptionsAsync(next,token);
        BuildDocument.VerifySkillPreservation(xml,next.Xml);
        token.ThrowIfCancellationRequested();
        var remaining=source.Skip(1).ToArray();var moved=destination.Prepend(before.Xml).Take(12).ToArray();
        SaveWork(next,new(next.Xml,backwards?remaining:moved,backwards?moved:remaining,ItemDraft:pastedItem));
        lock(gate)
        {
            source.Pop();PushHistory(destination,before.Xml);result=next;
            PublishVisibleSkillOptions(options);
            historyFocusRequested=true;focusRequestSequence++;
        }
        await RefreshCommittedMapAsync(token);
    },backwards?"Undoing change…":"Redoing change…");

    private async Task<JsonElement?> ReadVisibleSkillOptionsAsync(PobResult next,CancellationToken token)
    {
        if(detailKind!="skill-options")return null;
        if(!A(next.State,"skills").Any(s=>I(s,"id")==detailId))return null;
        return await ReadSkillOptionsAsync(detailId,next.Xml,token);
    }

    private void PublishVisibleSkillOptions(JsonElement? options)
    {
        if(detailKind!="skill-options")return;
        if(options is {} value){inspectedSkillOptions=value;inspectedSkillGroup=detailId;}
        else detailKind="";
    }

    private async Task RefreshCommittedMapAsync(CancellationToken token)
    {
        // Once saved, late cancellation only skips the optional map refresh. It
        // must not report that a successful edit was cancelled.
        try{await RefreshMapAsync(token);}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
    }
}
