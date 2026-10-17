using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement[] gemCatalog=[];
    private string gemSearch="",gemType="all";
    private int pickerGem;
    private bool skillOrderFocusRequested;
    private (int Group,int Gem,string Name)? skillRemoval;
    private sealed record GemEntry(string Key,string Name,string Action);
    private sealed record GemQuery(GemEntry[] Entries);
    private GemQuery gemQuery=new([]);
    private WidgetIndexedCollection<GemQuery,GemEntry>? gemCollection;

    private bool HandleSkillManagementAction(WidgetActionEvent action)
    {
        if(result is null)return false;
        var id=action.ActionId;var parts=id.Split('.');
        if(skillRemoval is {} removal)
        {
            if(id=="skill-remove-cancel"){skillRemoval=null;focusRequestSequence++;}
            else if(id=="skill-remove-confirm")
            {
                skillRemoval=null;
                if(removal.Group==0)Edit(new{op="skill-group",mode="delete-all"},skillDestination:"skill");
                else if(removal.Gem==0)Edit(new{op="skill-group",mode="delete",group=removal.Group},skillDestination:"skill");
                else Edit(new{op="gem-manage",mode="delete",group=removal.Group,gem=removal.Gem},skillDestination:"skill-manage");
            }
            return true;
        }
        if(id=="group-create"){Edit(new{op="skill-group",mode="create"},skillDestination:"skill-manage");return true;}
        if(id=="group-paste"){Edit((Func<object>)(()=>new{op="skill-group",mode="paste",text=readClipboard()}),skillDestination:"skill-manage");return true;}
        if(id=="group-delete-all"){skillRemoval=(0,0,"All editable groups in this skill set. Automatically granted groups are kept.");return true;}
        if(id=="back"&&detailKind is "skill-manage" or "gem-picker")
        {detailKind=detailKind=="gem-picker"?"skill-manage":"skill";focusRequestSequence++;return true;}
        if(detailKind=="gem-picker")
        {
            if(id=="gem-search"){gemSearch=action.CommittedText??"";return true;}
            if(parts is ["gem-type",var type]&&type is "all" or "active" or "support"){gemType=type;return true;}
            if(parts is ["gem-pick",var choice]&&int.TryParse(choice,out var option)&&option>=0&&option<gemCatalog.Length)
            {Edit(new{op="gem-manage",mode=pickerGem==0?"add":"replace",group=detailId,gem=pickerGem,gemId=S(gemCatalog[option],"id")},skillDestination:"skill-manage");return true;}
        }
        var group=A(State,"skills").FirstOrDefault(g=>I(g,"id")==detailId);
        if(group.ValueKind!=JsonValueKind.Object)return false;
        if(id=="group-manage"){detailKind="skill-manage";focusRequestSequence++;return true;}
        if(id=="group-name"){Edit(new{op="skill-group",mode="rename",group=detailId,name=action.CommittedText??""},skillDestination:"skill-manage");return true;}
        if(id=="group-count"&&double.TryParse(action.CommittedText,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var count))
        {Edit(new{op="skill-group",mode="count",group=detailId,value=count},skillDestination:"skill-manage");return true;}
        if(id=="group-duplicate"){Edit(new{op="skill-group",mode="duplicate",group=detailId},skillDestination:"skill-manage");return true;}
        if(id=="group-copy")
        {
            var target=detailId;
            Work(async token=>{var copied=await engine.ExecuteAsync(new{op="skill-copy",group=target},token,result!.Xml);ClipboardText.Write(S(copied.State,"groupClipboard"));lock(gate)ShowFeedback("Group copied","Paste it into a skill set or desktop PoB.",ToastTone.Info);},"Copying group…");
            return true;
        }
        if(id=="group-delete"){if(!B(group,"generated"))skillRemoval=(detailId,0,S(group,"title"));return true;}
        if(id is "group-up" or "group-down")
        {skillOrderFocusRequested=true;Edit(new{op="skill-group",mode="move",group=detailId,direction=id=="group-up"?-1:1},skillDestination:"skill-manage");return true;}
        if(parts is ["group-slot",var slotValue]&&int.TryParse(slotValue,out var slot)&&slot>=0&&slot<A(State,"skillSlots").Length)
        {Edit(new{op="skill-group",mode="slot",group=detailId,slot=S(A(State,"skillSlots")[slot],"name")},skillDestination:"skill-manage");return true;}
        if(id=="gem-add"){OpenGemPicker(0);return true;}
        if(parts.Length==2&&int.TryParse(parts[1],out var gemId)&&A(group,"gems").Any(g=>I(g,"id")==gemId))
        {
            if(parts[0]=="gem-replace"){OpenGemPicker(gemId);return true;}
            if(parts[0]=="gem-remove"){if(!B(group,"generated"))skillRemoval=(detailId,gemId,S(A(group,"gems").First(g=>I(g,"id")==gemId),"name"));return true;}
            if(parts[0]=="gem-enabled"){Edit(new{op="gem-manage",mode="enabled",group=detailId,gem=gemId},skillDestination:"skill-manage");return true;}
        }
        return false;
    }

    private void OpenGemPicker(int gem)=>Work(async token=>
    {
        var group=detailId;
        var count=A(A(State,"skills").First(g=>I(g,"id")==group),"gems").Length;
        var response=await engine.ExecuteAsync(new{op="gem-catalog",group,gem=gem==0?count+1:gem},token,result!.Xml);
        lock(gate){gemCatalog=A(response.State,"gemCatalog");gemSearch="";gemType=count==0?"active":"all";pickerGem=gem;detailKind="gem-picker";focusRequestSequence++;}
    },"Loading gems…");

    private WidgetElement SkillManagementWorkspace()
    {
        var groups=A(State,"skills");
        var position=Array.FindIndex(groups,g=>I(g,"id")==detailId);
        var group=position>=0?groups[position]:default;
        if(group.ValueKind!=JsonValueKind.Object)return UI.Stack("pob.skill-manage.tools",Button("Back to skills","back"),Text("This group is no longer available.","skill-manage.missing"));
        var generated=B(group,"generated");var gems=A(group,"gems");
        var properties=new List<WidgetElement>{Button("Back to skills","back"),Text("Manage skill group","skill-manage.title","pob-heading"),
            Field("Group label",UI.TextEntry(S(group,"label"),"Automatic from skill name","group-name","pob.group-name",50).Classes("pob-input"),"group-name")};
        if(generated)
        {
            properties.Add(Text("Granted by "+(S(group,"sourceName").Length>0?S(group,"sourceName"):"this build's effects")+(S(group,"slot").Length>0?" · "+S(group,"slot"):"")+". Its source controls the skill and level.","group-source","pob-muted"));
            properties.Add(Field("Group count (Full DPS)",UI.TextEntry(S(group,"groupCount"),"Count","group-count","pob.group-count",12).Classes("pob-input"),"group-count"));
        }
        else properties.Add(UI.Select("Socketed in",A(State,"skillSlots").Select((s,i)=>new SelectOption("slot"+i,S(s,"label"),"group-slot."+i,S(s,"name")==S(group,"slot"))).ToArray(),"pob.group-slot").Classes("pob-select"));
        properties.Add(Text($"Order · {position+1} of {groups.Length}","group-order","pob-field-label"));
        properties.Add(UI.Row("pob.group-order.actions",Button("Move up","group-up").Disabled(position==0),Button("Move down","group-down").Disabled(position==groups.Length-1))
            .RememberChildFocus(position==0?"pob.group-down":"pob.group-up").Classes("pob-toolbar"));
        properties.Add(UI.Stack("pob.group-order.context",
            Text("Above: "+(position>0?S(groups[position-1],"title"):"None — first group"),"group-above","pob-small pob-order-neighbor"),
            Text("Below: "+(position+1<groups.Length?S(groups[position+1],"title"):"None — last group"),"group-below","pob-small pob-order-neighbor")).Classes("pob-order-context"));
        if(!generated)
        {
            properties.Add(UI.Row("pob.group-management.copy",Button("Duplicate","group-duplicate"),Button("Copy group","group-copy").Disabled(gems.Length==0)).Classes("pob-toolbar"));
            properties.Add(Button("Delete group…","group-delete"));
        }
        if(A(State,"skills").Any(g=>!B(g,"generated")))properties.Add(Button("Delete all editable groups…","group-delete-all"));
        properties.Add(Text("Changes save immediately. Undo and Redo are available above.","group-save-hint","pob-small pob-muted"));
        var cards=SkillDetails().Skip(5).ToArray();var rows=new List<WidgetElement>();
        for(var i=0;i<gems.Length;i++)
        {
            var gem=gems[i];var gemId=I(gem,"id");
            var actions=new List<WidgetElement>{UI.Switch("Enabled",B(gem,"enabled"),"gem-enabled."+gemId,"pob.gem-enabled."+gemId).Classes("pob-toggle")};
            if(!generated){actions.Add(Button("Replace gem","gem-replace."+gemId));actions.Add(Button("Remove…","gem-remove."+gemId));}
            var card=(ContainerElement)cards[i];
            rows.Add(card with{Children=card.Children.Append(UI.Row("pob.gem-actions."+gemId,actions.ToArray()).Classes("pob-toolbar")).ToArray()});
        }
        if(gems.Length==0)rows.Add(Text("No gems yet. Add an active skill, then its support gems.","group-empty","pob-muted"));
        var heading=generated?Text("Granted skills","group-gems-title","pob-heading"):Button("Add gem","gem-add");
        var quick=NeedsQuickControls(gems.Length*3);
        var first=gems.Length>0?"pob.gem."+I(gems[0],"id")+(B(gems[0],"levelReadOnly")?"quality":"level"):"pob.gem-add";
        ContainerElement tools=UI.Stack("pob.skill-manage.gem-tools",heading);
        ContainerElement gemRows=UI.VerticalScroll("pob.skill-manage.gems",rows.Select(row=>LinkLeft(row,"pob.skill-manage.tools")).ToArray()).Classes("pob-scroll");
        if(!generated)tools=QuickControls(tools,"pob.gem-add","pob.skill-manage.gems","Gems",quick);
        if(gems.Length>0)gemRows=QuickControls(gemRows,first,"pob.skill-manage.gem-tools","Add gem",quick&&!generated);
        var right=UI.Stack("pob.skill-manage.right",
            tools,gemRows).RememberChildFocus(generated?first:"pob.gem-add").Classes("pob-panel","pob-tree-main");
        return Split("skill-manage",UI.VerticalScroll("pob.skill-manage.tools",properties.Select(LinkToGems).ToArray()).RememberChildFocus("pob.group-name").Classes("pob-scroll","pob-panel"),right,1,2);
    }

    private static WidgetElement LinkToGems(WidgetElement element)=>element switch
    {
        ButtonElement button=>button.FocusRight("pob.skill-manage.right"),
        TextEntryElement field=>field.FocusRight("pob.skill-manage.right"),
        SelectElement select=>select.FocusRight("pob.skill-manage.right"),
        ContainerElement container=>container with{Children=container.Children.Select(LinkToGems).ToArray()},
        _=>element,
    };

    private WidgetElement GemPickerWorkspace()
    {
        gemCollection??=CreateIndexedCollection<GemQuery,GemEntry>("pob.gem-source",gemQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>ValueTask.FromResult<IReadOnlyList<GemEntry>>(query.Entries.Skip(start).Take(count).ToArray()),
            ItemKey=entry=>new(entry.Key),
            RenderItem=(_,entry,context)=>UI.Button(entry.Name,"choose",context.Id("gem")).Classes("pob-button"),
            OnAction=(query,entry,action,token)=>
            {lock(gate){if(!ReferenceEquals(query,gemQuery)||detailKind!="gem-picker"||busy||action.ActionId!="choose")return ValueTask.CompletedTask;return OnActionAsync(action with{ActionId=entry.Action},token);}},
        });
        var entries=gemCatalog.Select((gem,i)=>(gem,i)).Where(x=>(gemType=="all"||B(x.gem,"support")== (gemType=="support"))&&S(x.gem,"name").Contains(gemSearch,StringComparison.OrdinalIgnoreCase))
            .Select(x=>new GemEntry(WidgetIds.Scope("gem").KeyedId("entry",S(x.gem,"id")),S(x.gem,"name")+(B(x.gem,"support")?" · Support":" · Active skill"),"gem-pick."+x.i)).ToArray();
        if(!gemQuery.Entries.SequenceEqual(entries)){gemQuery=new(entries);gemCollection.PublishQuery(gemQuery,entries.Length);}
        ContainerElement results=UI.Stack("pob.gem-picker.results",entries.Length==0?Text("No matching gems.","gem-picker.empty","pob-muted"):
            UI.CollectionList("pob.gems",gemCollection,64,"Available gems").Classes("pob-scroll")).Classes("pob-workspace");
        if(entries.Length>0)results=QuickControls(results,"pob.gems","pob.gem-picker.tools","Search / type",NeedsQuickControls(entries.Length));
        return UI.Stack("pob.gem-picker",
            QuickControls(UI.Stack("pob.gem-picker.tools",UI.Row("pob.gem-picker.header",Button("Back to group","back"),Text(pickerGem==0?"Add gem":"Replace gem","gem-picker.title","pob-heading")).Classes("pob-toolbar"),
                UI.Grid("pob.gem-picker.search-row",[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Auto()],
                    Field("Search gems",UI.TextEntry(gemSearch,"Gem name","gem-search","pob.gem-search",100).Classes("pob-input"),"gem-search"),
                    UI.Select("Type",new[]{("all","All gems"),("active","Active skills"),("support","Support gems")}.Select(x=>new SelectOption(x.Item1,x.Item2,"gem-type."+x.Item1,x.Item1==gemType)).ToArray(),"pob.gem-type").Classes("pob-select").InGrid(column:1)).Spacing(0,12),
                Text($"{entries.Length} gems · Defaults and compatibility follow the current PoB engine.","gem-picker.count","pob-muted")),"pob.gem-search","pob.gem-picker.results","Gems",NeedsQuickControls(entries.Length)),
            results).Classes("pob-workspace");
    }

    private WidgetView WithSkillConfirmation(WidgetView view)
    {
        if(skillRemoval is not {} removal)return view;
        var what=removal.Group==0?"all editable groups":removal.Gem==0?"group":"gem";
        return view.WithModal(new("pob.skill-remove-confirmation","Remove "+what+"?",UI.Stack("pob.skill-remove.content",
            Text(removal.Name,"skill-remove.name","pob-heading"),Text("This change is saved immediately. You can Undo it.","skill-remove.help"),
            UI.Row("pob.skill-remove.actions",Button("Cancel","skill-remove-cancel"),Button("Remove "+what,"skill-remove-confirm")).Classes("pob-toolbar")).Classes("pob-card"),"pob.skill-remove-cancel","skill-remove-cancel"));
    }
}
