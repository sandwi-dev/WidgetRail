using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private List<WidgetElement> SkillGemCards(JsonElement skill,string returnId)
    {
        var rows=new List<WidgetElement>();
        foreach(var gem in A(skill,"gems"))
        {
            var id=I(gem,"id");
            var editable=!B(skill,"generated");
            var levelReadOnly=B(gem,"levelReadOnly");
            var firstField="pob.gem."+id+(levelReadOnly?"quality":"level");
            var fields = new[]{"level","quality","count"};
            TextEntryElement GemField(string field,int index)
            {
                var input=(UI.TextEntry(S(gem,field),field,"gem."+id+"."+field,"pob.gem."+id+field) with
                    { InputKind=field=="count"?TextEntryInputKind.Decimal:TextEntryInputKind.Integer }).Classes("pob-input")
                    .FocusLeft(index==0||index==1&&levelReadOnly?returnId:"pob.gem."+id+fields[index-1])
                    .FocusRight(index==2?"pob.gem-options."+id:"pob.gem."+id+fields[index+1]);
                return editable?input.FocusDown(index<2?"pob.gem-replace."+id:"pob.gem-remove."+id):input;
            }
            var controls = fields.Select((field, index) => UI.Stack("pob.gemfield." + id + field,
                Text(field=="level"&&B(gem,"levelReadOnly")?"Level · Granted":char.ToUpperInvariant(field[0]) + field[1..], "gemlabel."+id+field, "pob-small"),
                field=="level"&&B(gem,"levelReadOnly")
                    ? UI.Stack("pob.gem.level-readonly."+id,Text(S(gem,field),"gem."+id+field,"pob-field-label")).Classes("pob-gem-level-readonly")
                    : GemField(field,index))
                .InGrid(column:index)).ToArray();
            var title=UI.ActionSurface("gem-details."+id,"pob.gem-details."+id,S(gem,"name")+" details",ActionSurfaceOrientation.Horizontal,
                Text(S(gem,"name"),"gem.name."+id,"pob-heading pob-blue")).FocusLeft(returnId).FocusRight("pob.gem-enabled."+id)
                .Classes("pob-list-item","pob-gem-name").PresentOnFocus(id.ToString());
            var optionsButton=Button("Gem options","gem-options."+id).FocusLeft("pob.gem."+id+"count");
            if(editable)optionsButton=optionsButton.FocusDown("pob.gem-remove."+id);
            var options=UI.Stack("pob.gem-options."+id+".return",optionsButton)
                .RememberChildFocus("pob.gem-options."+id).Classes("pob-gem-options").InGrid(column:3);
            var card=new List<WidgetElement>{
                UI.Grid("pob.gem.header."+id,[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Auto()],title,
                    UI.Switch("Enabled",B(gem,"enabled"),"gem-enabled."+id,"pob.gem-enabled."+id).FocusLeft(title.Id).Classes("pob-toggle").InGrid(column:1)).Spacing(0,8),
                UI.Grid("pob.gemfields."+id,[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Star(),GridTrack.Star(),GridTrack.Auto()],
                    controls.Concat(new WidgetElement[]{options}).ToArray()).Spacing(0,8)};
            if(editable)card.Add(UI.Row("pob.gem.actions."+id,
                Button("Replace","gem-replace."+id).FocusLeft(returnId).FocusUp(firstField).FocusRight("pob.gem-remove."+id),
                Button("Remove…","gem-remove."+id).FocusUp("pob.gem."+id+"count").FocusLeft("pob.gem-replace."+id)).Classes("pob-toolbar"));
            rows.Add(UI.Stack("pob.gemcard."+id,card.ToArray()).ContextMenu(ControllerButton.X,new WidgetContextAction("gem-quality."+id,"Quality effects")).Classes("pob-context-card"));
        }
        return rows;
    }
    private ContainerElement SkillWorkspaceDetails(JsonElement group,string returnId)
    {
        var generated=B(group,"generated");var gems=A(group,"gems");
        var heading=new List<WidgetElement>{
            Text(S(group,"title"),"skill.name","pob-heading"),
            Text(B(group,"main")?"Main calculation skill":"Inspecting this group · Main calculation skill: "+S(State,"mainSkill"),"skill.context","pob-small pob-muted"),
            UI.Row("pob.skill.tools",Button("Group settings","group-manage").FocusLeft(returnId).FocusRight("pob.skill-options"),
                Button("More skill options","skill-options").FocusLeft("pob.group-manage")).Classes("pob-toolbar"),
            UI.ResponsiveGrid("pob.skill.group-actions",140,3,Button(B(group,"main")?"Main skill":"Use as main","skill-main").FocusLeft(returnId),
                UI.Switch("Enabled",B(group,"enabled"),"skill-enabled","pob.skill-enabled").Classes("pob-toggle"),
                UI.Switch("Full DPS",B(group,"fullDps"),"skill-fullDps","pob.skill-fullDps").Classes("pob-toggle")).Classes("pob-buildtools")
        };
        var choices=InlineSkillOptions();
        if(choices.Count>0)heading.Add(UI.ResponsiveGrid("pob.skill.common-options",220,2,choices.ToArray()).Classes("pob-toolbar"));
        var rows=new List<WidgetElement>();
        if(gems.Length==0)rows.Add(Field("Group label",UI.TextEntry(S(group,"label"),"Automatic from skill name","group-name","pob.group-name",50).FocusLeft(returnId).Classes("pob-input"),"group-name"));
        if(!generated)rows.Add(Button("Add gem","gem-add").FocusLeft(returnId));
        rows.AddRange(SkillGemCards(group,returnId));
        if(gems.Length==0)rows.Add(Text("No gems yet. Add an active skill, then its supports.","group-empty","pob-muted"));
        var quick=NeedsQuickControls(gems.Length*2);
        var tools=QuickControls(UI.Stack("pob.skill.actions",heading.ToArray()),skillWorkspaceReturnControl??"pob.group-manage","pob.detail.scroll","Gems",quick);
        var first=gems.Length==0?"pob.group-name":!generated?"pob.gem-add":"pob.gem."+I(gems[0],"id")+(B(gems[0],"levelReadOnly")?"quality":"level");
        // The group rail remains fixed; the entire selected-group editor scrolls.
        // A fixed context header otherwise consumes nearly all compact view height.
        var scroll=QuickControls(UI.VerticalScroll("pob.detail.scroll",new WidgetElement[]{tools}.Concat(rows).ToArray()).Classes("pob-scroll"),first,"pob.skill.actions","Skill controls",quick);
        return UI.Stack("pob.detail",WithGemEffectsPreviews(scroll,group)).RememberChildFocus("pob.group-manage").Classes("pob-panel","pob-tree-main");
    }
}
