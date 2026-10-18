using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement inspectedSkillOptions;
    private int inspectedSkillGroup;
    private string? inspectedSkillXml;
    private string? inspectedSkillDocument;
    private string? readingSkillXml;
    private int readingSkillGroup;
    private long readingSkillToken;
    private string? failedSkillXml;
    private int failedSkillGroup;
    private string? failedSkillDocument;
    private readonly Dictionary<int,JsonElement> skillOptionsCache=[];
    private string? skillOptionsCacheXml;
    private string? skillOptionsCacheDocument;
    private static bool CommonSkillOption(string key)=>key is "mainSkill" or "mainSkillPart" or "statSet";
    private bool SkillOptionsCurrent=>result is not null&&inspectedSkillGroup==detailId&&inspectedSkillXml==result.Xml&&inspectedSkillDocument==documentId&&inspectedSkillOptions.ValueKind==JsonValueKind.Array;

    // Read-only inspection has a latest lane, so group navigation stays usable.
    // Exact XML/group identity fences late responses and bounds the one-build cache.
    private void EnsureInlineSkillOptions()
    {
        if(result is null||busy||section!="Skills"||detailKind!="skill")return;
        var group=detailId;var xml=result.Xml;var document=documentId;
        if(SkillOptionsCurrent||readingSkillGroup==group&&readingSkillXml==xml||failedSkillGroup==group&&failedSkillXml==xml&&failedSkillDocument==document)return;
        if(skillOptionsCacheXml!=xml||skillOptionsCacheDocument!=document){skillOptionsCache.Clear();skillOptionsCacheXml=xml;skillOptionsCacheDocument=document;}
        if(skillOptionsCache.TryGetValue(group,out var cached)){inspectedSkillOptions=cached;inspectedSkillGroup=group;inspectedSkillXml=xml;inspectedSkillDocument=documentId;return;}
        if(I(State,"skillOptionsGroup")==group&&State.TryGetProperty("skillOptions",out var mainOptions))
        {inspectedSkillOptions=mainOptions;inspectedSkillGroup=group;inspectedSkillXml=xml;inspectedSkillDocument=documentId;skillOptionsCache[group]=mainOptions;return;}
        var request=++readingSkillToken;readingSkillGroup=group;readingSkillXml=xml;
        var admission=Operations.RunLatest("pob.skill-options",async context=>
        {
            try
            {
                await Task.Delay(100,context.CancellationToken);
                CancellationToken active;
                lock(gate)
                {
                    if(!context.IsCurrent||busy||documentId!=document||result?.Xml!=xml||detailId!=group||section!="Skills")return;
                    active=ActiveCancellationToken;
                }
                // Superseding a UI read must not retire the shared engine process.
                // Complete an admitted command, discard its stale result, then run
                // only the newest queued group. Hiding/unloading still cancels it.
                var options=await ReadSkillOptionsAsync(group,xml,active);
                lock(gate)
                {
                    if(!context.IsCurrent||request!=readingSkillToken||documentId!=document||result?.Xml!=xml||section!="Skills"||detailId!=group||readingSkillGroup!=group||readingSkillXml!=xml)return;
                    if(skillOptionsCache.Count>=32)skillOptionsCache.Clear();
                    skillOptionsCache[group]=options;inspectedSkillOptions=options;inspectedSkillGroup=group;inspectedSkillXml=xml;inspectedSkillDocument=documentId;
                }
            }
            catch(OperationCanceledException){}
            catch(Exception error)when(error is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {lock(gate)if(context.IsCurrent&&request==readingSkillToken&&documentId==document&&result?.Xml==xml&&detailId==group){failedSkillGroup=group;failedSkillXml=xml;failedSkillDocument=document;}}
            finally
            {
                lock(gate)if(request==readingSkillToken&&readingSkillGroup==group&&readingSkillXml==xml){readingSkillGroup=0;readingSkillXml=null;}
                Invalidate();
            }
        },WidgetOperationLifetime.Active);
        if(!admission.IsAccepted){readingSkillGroup=0;readingSkillXml=null;}
    }

    private List<WidgetElement> InlineSkillOptions()
    {
        EnsureInlineSkillOptions();
        if(!SkillOptionsCurrent)return [];
        return inspectedSkillOptions.EnumerateArray().Where(o=>CommonSkillOption(S(o,"id"))).Select(SkillOptionControl).Where(c=>c is not null).Cast<WidgetElement>().ToList();
    }

    private bool HandleInlineSkillOption(WidgetActionEvent action)
    {
        var parts=action.ActionId.Split('.');
        if(parts is not ["skill-option",var groupText,var control,var choiceText])return false;
        if(detailKind is not ("skill" or "skill-options")||!SkillOptionsCurrent||!int.TryParse(groupText,out var group)||group!=detailId||!int.TryParse(choiceText,out var choice))return true;
        var option=inspectedSkillOptions.EnumerateArray().FirstOrDefault(o=>S(o,"id")==control);
        if(option.ValueKind==JsonValueKind.Object&&A(option,"options").Any(o=>I(o,"id")==choice))Edit(new{op="skill-option",group,control,value=choice});
        return true;
    }

    private WidgetElement? SkillOptionControl(JsonElement option)
    {
        var key=S(option,"id");
        if(S(option,"kind")=="number")return Field(S(option,"title"),UI.TextEntry(S(option,"value"),"PoB default",$"skill-number.{detailId}.{key}","pob.option."+key,9).Classes("pob-input"),"skill-number."+key);
        var choices=A(option,"options");if(choices.Length==0)return null;
        var title=key switch{"mainSkill"=>"Skill in group","statSet"=>"Stat set","mainSkillPart"=>"Skill part","mainSkillMinion"=>"Minion","mainSkillMinionSkill"=>"Minion skill","mainSkillMinionSkillStatSet"=>"Minion stat set",_=>StatLabel(key)};
        return UI.Select(title,choices.Select(o=>new SelectOption("choice"+I(o,"id"),InlineLabel(S(o,"title")),$"skill-option.{detailId}.{key}.{I(o,"id")}",B(o,"active"))).ToArray(),"pob.option."+key).Classes("pob-select");
    }

    private async Task RefreshSkillOptionsAsync(int group,CancellationToken token)
    {
        string xml;lock(gate)xml=result!.Xml;
        var options=await ReadSkillOptionsAsync(group,xml,token);
        lock(gate){if(result?.Xml==xml&&detailId==group){inspectedSkillOptions=options;inspectedSkillGroup=group;inspectedSkillXml=xml;inspectedSkillDocument=documentId;}}
    }

    private async Task<JsonElement> ReadSkillOptionsAsync(int group,string xml,CancellationToken token)
    {
        var inspected=await engine.ExecuteAsync(new{op="inspect-skill",group},token,xml);
        if(I(inspected.State,"skillOptionsGroup")!=group)throw new IOException("PoB returned options for a different skill group.");
        return inspected.State.GetProperty("skillOptions").Clone();
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
        if(!SkillOptionsCurrent)
        {rows.Add(Text("Open this skill's options again to refresh them.","no-options","pob-muted"));return rows;}
        foreach(var option in inspectedSkillOptions.EnumerateArray())
            if(SkillOptionControl(option)is {} control)rows.Add(control);
        if(rows.Count==2)rows.Add(Text("No additional choices for this skill.","no-options","pob-muted"));
        return rows;
    }
}
