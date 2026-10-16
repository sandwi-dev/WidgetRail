using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement inspectedSkillOptions;
    private int inspectedSkillGroup;

    private async Task RefreshSkillOptionsAsync(int group,CancellationToken token)
    {
        string xml;lock(gate)xml=result!.Xml;
        var inspected=await engine.ExecuteAsync(new{op="inspect-skill",group},token,xml);
        if(I(inspected.State,"skillOptionsGroup")!=group)throw new IOException("PoB returned options for a different skill group.");
        lock(gate){inspectedSkillOptions=inspected.State.GetProperty("skillOptions").Clone();inspectedSkillGroup=group;}
    }

    private void OpenSkillOptions()
    {
        var group=detailId;
        Work(async token=>
        {
            await RefreshSkillOptionsAsync(group,token);
            lock(gate)detailKind="skill-options";
        },"Reading skill options…");
    }

    private void PreviewSkillOption(string control,int value)
    {
        var group=detailId;
        Work(async token=>
        {
            var before=result!;
            var next=await engine.ExecuteAsync(new{op="skill-option",group,control,value},token,before.Xml);
            lock(gate){pendingBefore=before;result=next;}
            await RefreshSkillOptionsAsync(group,token);
        },"Calculating comparison…");
    }

    private List<WidgetElement> SkillOptionsContent()
    {
        var group=A(State,"skills").FirstOrDefault(s=>I(s,"id")==detailId);
        var rows=new List<WidgetElement>{Button("Back to skills","back"),Text(S(group,"title"),"skill.options.title","pob-heading")};
        if(inspectedSkillGroup!=detailId || inspectedSkillOptions.ValueKind!=JsonValueKind.Array)
        {rows.Add(Text("Open this skill's options again to refresh them.","no-options","pob-muted"));return rows;}
        foreach(var option in inspectedSkillOptions.EnumerateArray())
        {
            var choices=A(option,"options");if(choices.Length==0)continue;
            var key=S(option,"id");
            var title=key switch{"mainSkill"=>"Skill in group","statSet"=>"Stat set","mainSkillPart"=>"Skill part", "mainSkillMinion"=>"Minion", "mainSkillMinionSkill"=>"Minion skill", "mainSkillMinionSkillStatSet"=>"Minion stat set",_=>StatLabel(key)};
            rows.Add(UI.Select(title,choices.Select(o=>new SelectOption("choice"+I(o,"id"),InlineLabel(S(o,"title")),
                    $"skill-option.{detailId}.{key}.{I(o,"id")}",B(o,"active"))).ToArray(),"pob.option."+key)
                .Disabled(pendingBefore is not null).Classes("pob-select"));
        }
        if(rows.Count==2)rows.Add(Text("No additional choices for this skill.","no-options","pob-muted"));
        return rows;
    }
}
