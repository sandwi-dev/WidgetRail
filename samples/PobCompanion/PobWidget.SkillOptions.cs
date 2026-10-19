using WidgetRail.WidgetProtocol;
using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly PobSkillOptionsState skillOptionsState=new();
    private static bool CommonSkillOption(string key)=>key is "mainSkill" or "mainSkillPart" or "statSet";
    private PobSkillContext? CurrentSkillContext=>result is null?null:new(documentId,result.Xml,detailId);
    private PobSkillOptions? CurrentSkillOptions=>CurrentSkillContext is {} context?skillOptionsState.Current(context):null;
    private bool SkillOptionsCurrent=>CurrentSkillOptions is not null;

    // The state owner retains only one exact build and at most 32 inspected groups.
    // Host operations remain responsible for scheduling and active cancellation.
    private void EnsureInlineSkillOptions()
    {
        if(result is null||busy||section!="Skills"||detailKind!="skill"||CurrentSkillContext is not {} target)return;
        skillOptionsState.Select(target);
        if(SkillOptionsCurrent||skillOptionsState.Reading(target)||skillOptionsState.Failed(target))return;
        if(I(State,"skillOptionsGroup")==target.Group&&skillOptionsState.UseSnapshot(target))
        {
            try {skillOptionsState.Publish(target,PobSkillOptions.Read(State,target.Group));}
            catch(IOException){if(skillOptionsState.Begin(target)is {} failed)skillOptionsState.Fail(failed);}
            return;
        }
        var request=skillOptionsState.Begin(target);if(request is null)return;
        var admission=Operations.RunLatest("pob.skill-options",async context=>
        {
            try
            {
                await Task.Delay(100,context.CancellationToken);
                CancellationToken active;
                lock(gate)
                {
                    if(!context.IsCurrent||busy||CurrentSkillContext!=target||section!="Skills")return;
                    active=ActiveCancellationToken;
                }
                // A changed selection retires publication, not an admitted engine
                // command. Hiding/unloading still cancels through active lifetime.
                var options=await ReadSkillOptionsAsync(target.Group,target.Xml,active);
                lock(gate)
                    if(context.IsCurrent&&CurrentSkillContext==target&&section=="Skills")skillOptionsState.Complete(request,options);
            }
            catch(OperationCanceledException){}
            catch(Exception error)when(error is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {lock(gate)if(context.IsCurrent&&CurrentSkillContext==target)skillOptionsState.Fail(request);}
            finally{lock(gate)skillOptionsState.End(request);Invalidate();}
        },WidgetOperationLifetime.Active);
        if(!admission.IsAccepted)skillOptionsState.End(request);
    }
    private List<WidgetElement> InlineSkillOptions()
    {
        EnsureInlineSkillOptions();
        if(CurrentSkillOptions is {} options)return options.Options.Where(o=>CommonSkillOption(o.Id)).Select(SkillOptionControl).ToList();
        if(CurrentSkillContext is {} target&&skillOptionsState.Failed(target))return [
            UI.Stack("pob.skill-options.unavailable",Text("Skill options unavailable","skill-options.error","pob-small pob-muted"),
                Button("Retry skill options","skill-options-retry"))];
        return [];
    }
    private bool HandleInlineSkillOption(WidgetActionEvent action)
    {
        if(action.ActionId=="skill-options-retry")
        {
            if(section=="Skills"&&detailKind is ("skill" or "skill-options")&&CurrentSkillContext is {} target)
            {skillOptionsState.Retry(target);if(detailKind=="skill-options")OpenSkillOptions();else EnsureInlineSkillOptions();}
            return true;
        }
        var parts=action.ActionId.Split('.');
        if(parts is not ["skill-option",var groupText,var control,var choiceText])return false;
        if(detailKind is not ("skill" or "skill-options")||CurrentSkillOptions is not {} options||!int.TryParse(groupText,out var group)||group!=detailId||!int.TryParse(choiceText,out var choice))return true;
        if(options.Options.Any(option=>option.Id==control&&!option.Numeric&&option.Choices.Any(c=>c.Id==choice)))
            Edit(new{op="skill-option",group,control,value=choice});
        return true;
    }
    private WidgetElement SkillOptionControl(PobSkillOption option)
    {
        var key=option.Id;
        if(option.Numeric)return Field(option.Title,(UI.TextEntry(option.Value,"PoB default",$"skill-number.{detailId}.{key}","pob.option."+key,9) with { InputKind = TextEntryInputKind.Decimal }).Classes("pob-input"),"skill-number."+key);
        var title=key switch{"mainSkill"=>"Skill in group","statSet"=>"Stat set","mainSkillPart"=>"Skill part","mainSkillMinion"=>"Minion","mainSkillMinionSkill"=>"Minion skill","mainSkillMinionSkillStatSet"=>"Minion stat set",_=>StatLabel(key)};
        return UI.Select(title,option.Choices.Select(o=>new SelectOption("choice"+o.Id,InlineLabel(o.Title),$"skill-option.{detailId}.{key}.{o.Id}",o.Active)).ToArray(),"pob.option."+key).Classes("pob-select");
    }
    private async Task RefreshSkillOptionsAsync(int group,CancellationToken token)
    {
        PobSkillContext target;lock(gate)target=new(documentId,result!.Xml,group);
        var options=await ReadSkillOptionsAsync(group,target.Xml,token);
        lock(gate)if(CurrentSkillContext==target)skillOptionsState.Publish(target,options);
    }
    private async Task<PobSkillOptions> ReadSkillOptionsAsync(int group,string xml,CancellationToken token)
    {
        var inspected=await engine.ExecuteAsync(new{op="inspect-skill",group},token,xml);
        return PobSkillOptions.Read(inspected.State,group);
    }
    private void OpenSkillOptions()
    {
        var group=detailId;
        Work(async token=>
        {
            if(!SkillOptionsCurrent)await RefreshSkillOptionsAsync(group,token);
            lock(gate){detailKind="skill-options";focusRequestSequence++;}
        },"Reading skill options…");
    }
    private List<WidgetElement> SkillOptionsContent()
    {
        var group=A(State,"skills").FirstOrDefault(s=>I(s,"id")==detailId);
        var rows=new List<WidgetElement>{Button("Back to skills","back"),Text(S(group,"title"),"skill.options.title","pob-heading")};
        if(CurrentSkillOptions is not {} options)
        {rows.Add(Text("Skill options unavailable","no-options","pob-muted"));rows.Add(Button("Retry skill options","skill-options-retry"));return rows;}
        rows.AddRange(options.Options.Select(SkillOptionControl));
        if(rows.Count==2)rows.Add(Text("No additional choices for this skill.","no-options","pob-muted"));
        return rows;
    }
}
