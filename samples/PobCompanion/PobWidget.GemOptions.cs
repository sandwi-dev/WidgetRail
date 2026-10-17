using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement gemOptions;
    private int optionsGem;
    private async Task<JsonElement?> ReadVisibleGemOptionsAsync(PobResult next,CancellationToken token)
    {
        if(detailKind!="gem-options")return null;
        var group=A(next.State,"skills").FirstOrDefault(g=>I(g,"id")==detailId);
        if(!A(group,"gems").Any(g=>I(g,"id")==optionsGem))return null;
        return(await engine.ExecuteAsync(new{op="gem-controls",group=detailId,gem=optionsGem},token,next.Xml)).State;
    }
    private bool HandleGemOptionsAction(WidgetActionEvent action)
    {
        var parts=action.ActionId.Split('.');
        if(parts is ["gem-options",var id]&&int.TryParse(id,out var gem)&&detailKind=="skill")
        {
            Work(async token=>
            {
                var response=await engine.ExecuteAsync(new{op="gem-controls",group=detailId,gem},token,result!.Xml);
                lock(gate){optionsGem=gem;gemOptions=response.State;detailKind="gem-options";focusRequestSequence++;}
            },"Reading gem options…");return true;
        }
        if(detailKind!="gem-options")return false;
        if(action.ActionId=="back"){detailKind="skill";focusRequestSequence++;return true;}
        if(parts is ["gem-extra",var controlId])
        {
            var control=A(gemOptions,"controls").FirstOrDefault(c=>S(c,"id")==controlId);
            if(control.ValueKind==JsonValueKind.Object&&B(control,"enabled")&&A(control,"options").Length==0)
                Edit(new{op="gem-control",group=detailId,gem=optionsGem,control=controlId,value=!B(control,"value")});
            return true;
        }
        if(parts is ["gem-extra",var optionControl,var optionId]&&int.TryParse(optionId,out var option))
        {
            var control=A(gemOptions,"controls").FirstOrDefault(c=>S(c,"id")==optionControl);
            if(B(control,"enabled")&&A(control,"options").Any(c=>I(c,"id")==option))
                Edit(new{op="gem-control",group=detailId,gem=optionsGem,control=optionControl,value=option});
            return true;
        }
        return false;
    }
    private WidgetElement GemOptionsWorkspace()
    {
        var rows=new List<WidgetElement>{Button("Back to skill group","back"),Text(S(gemOptions,"name"),"gem-options.name","pob-heading pob-blue"),
            Text("Options follow this gem's upstream rules. Edits save immediately and support Undo.","gem-options.help","pob-muted")};
        foreach(var control in A(gemOptions,"controls"))
        {
            var id=S(control,"id");var choices=A(control,"options");
            if(choices.Length>0)rows.Add(UI.Select(S(control,"title"),choices.Select(c=>new SelectOption(I(c,"id").ToString(),S(c,"title"),$"gem-extra.{id}.{I(c,"id")}",B(c,"active"))).ToArray(),"pob.gem-extra."+id).Disabled(!B(control,"enabled")).Classes("pob-select"));
            else rows.Add(UI.Switch(S(control,"title").TrimEnd(' ',':'),B(control,"value"),"gem-extra."+id,"pob.gem-extra."+id).Disabled(!B(control,"enabled")).Classes("pob-toggle"));
        }
        return UI.VerticalScroll("pob.gem-options.page",rows.ToArray()).RememberChildFocus("pob.back").Classes("pob-scroll","pob-panel");
    }
}
