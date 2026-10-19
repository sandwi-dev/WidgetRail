using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private List<WidgetElement> SkillGemCards(JsonElement skill)
    {
        var rows=new List<WidgetElement>();
        foreach(var gem in A(skill,"gems"))
        {
            var id=I(gem,"id");
            var fields = new[]{"level","quality","count"};
            var controls = fields.Select((field, index) => UI.Stack("pob.gemfield." + id + field,
                Text(char.ToUpperInvariant(field[0]) + field[1..], "gemlabel."+id+field, "pob-small"),
                field=="level"&&B(gem,"levelReadOnly")
                    ? Text(S(gem,field),"gem."+id+field,"pob-field-label")
                    : (UI.TextEntry(S(gem,field),field,"gem."+id+"."+field,"pob.gem."+id+field) with { InputKind = field=="count"?TextEntryInputKind.Decimal:TextEntryInputKind.Integer }).Classes("pob-input"))
                .InGrid(column:index)).ToArray();
            var card=new List<WidgetElement>{Text(S(gem,"name"),"gem.name."+id,"pob-heading pob-blue"),
                UI.Grid("pob.gemfields."+id,[GridTrack.Auto()],fields.Select(_=>GridTrack.Star()).ToArray(),controls).Spacing(0,8)};
            var actions=new List<WidgetElement>{UI.Switch("Enabled",B(gem,"enabled"),"gem-enabled."+id,"pob.gem-enabled."+id).Classes("pob-toggle")};
            if(!B(skill,"generated")){actions.Add(Button("Replace","gem-replace."+id));actions.Add(Button("Remove…","gem-remove."+id));}
            actions.Add(Button("Gem options","gem-options."+id));
            card.Add(UI.ResponsiveGrid("pob.gem.actions."+id,140,4,actions.ToArray()).Classes("pob-buildtools"));
            if(B(gem,"levelReadOnly"))
            {
                var source=S(skill,"sourceName");var slot=S(skill,"slot");
                var origin=source.Length>0?source+(slot.Length>0?" · "+slot:""):"this build's effects";
                card.Add(Text("Granted by "+origin+". Level is determined by its source.","gem.source."+id,"pob-muted"));
            }
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
            UI.Row("pob.skill.tools",Button("Group settings","group-manage"),Button("More skill options","skill-options")).Classes("pob-toolbar"),
            UI.ResponsiveGrid("pob.skill.group-actions",140,3,Button(B(group,"main")?"Main skill":"Use as main","skill-main"),
                UI.Switch("Enabled",B(group,"enabled"),"skill-enabled","pob.skill-enabled").Classes("pob-toggle"),
                UI.Switch("Full DPS",B(group,"fullDps"),"skill-fullDps","pob.skill-fullDps").Classes("pob-toggle")).Classes("pob-buildtools")
        };
        var choices=InlineSkillOptions();
        if(choices.Count>0)heading.Add(UI.ResponsiveGrid("pob.skill.common-options",220,2,choices.ToArray()).Classes("pob-toolbar"));
        var rows=new List<WidgetElement>();
        if(gems.Length==0)rows.Add(Field("Group label",UI.TextEntry(S(group,"label"),"Automatic from skill name","group-name","pob.group-name",50).Classes("pob-input"),"group-name"));
        if(!generated)rows.Add(Button("Add gem","gem-add"));
        rows.AddRange(SkillGemCards(group));
        if(gems.Length==0)rows.Add(Text("No gems yet. Add an active skill, then its supports.","group-empty","pob-muted"));
        var quick=NeedsQuickControls(gems.Length*2);
        var tools=QuickControls(UI.Stack("pob.skill.actions",heading.Select(row=>LinkLeft(row,returnId)).ToArray()),skillWorkspaceReturnControl??"pob.group-manage","pob.detail.scroll","Gems",quick);
        var first=gems.Length==0?"pob.group-name":!generated?"pob.gem-add":"pob.gem."+I(gems[0],"id")+(B(gems[0],"levelReadOnly")?"quality":"level");
        // The group rail remains fixed; the entire selected-group editor scrolls.
        // A fixed context header otherwise consumes nearly all compact view height.
        var scroll=QuickControls(UI.VerticalScroll("pob.detail.scroll",new WidgetElement[]{tools}.Concat(rows.Select(row=>LinkLeft(row,returnId))).ToArray()).Classes("pob-scroll"),first,"pob.skill.actions","Skill controls",quick);
        return UI.Stack("pob.detail",scroll).RememberChildFocus("pob.group-manage").Classes("pob-panel","pob-tree-main");
    }
}
