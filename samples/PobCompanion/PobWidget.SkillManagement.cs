using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement[] gemCatalog=[];
    private long gemCatalogRevision;
    private GemInspectionData[] gemInspectionCatalog=[];
    private PobGemSearchCatalog gemSearchCatalog=new([]);

    private void SetGemCatalog(JsonElement state)
    {
        if (!state.TryGetProperty("gemCatalog", out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("PoB returned an invalid gem catalog.");
        var items=value.EnumerateArray().ToArray();
        var search=new PobGemSearchCatalog(items);
        var inspection=items.Select(ReadGemInspection).ToArray();
        gemCatalog=items;gemSearchCatalog=search;gemInspectionCatalog=inspection;gemCatalogRevision++;
    }
    private string gemSearch="",gemType="all";
    private string gemSort="name";
    private int pickerGem;
    private int gemPickerGroup;
    private string? gemPickerXml;
    private bool skillOrderFocusRequested;
    private bool skillWorkspaceFocusRequested;
    private string? skillWorkspaceReturnControl;
    private (int Group,int Gem,string Name)? skillRemoval;
    private sealed record GemEntry(string Key,string Name,string Action,string Description="",GemInspectionData? Inspection=null);
    private sealed record GemQuery(GemEntry[] Entries,GemPickerContext? Context=null);
    private GemQuery gemQuery=new([]);
    private WidgetIndexedCollection<GemQuery,GemEntry>? gemCollection;

    private bool HandleSkillManagementAction(WidgetActionEvent action)
    {
        if(result is null)return false;
        if(HandleInlineSkillOption(action))return true;
        var id=action.ActionId;var parts=id.Split('.');
        if(skillRemoval is {} removal)
        {
            if(id=="skill-remove-cancel"){skillRemoval=null;focusRequestSequence++;}
            else if(id=="skill-remove-confirm")
            {
                skillRemoval=null;
                if(removal.Group==0)Edit(new{op="skill-group",mode="delete-all"},skillDestination:"skill");
                else if(removal.Gem==0)Edit(new{op="skill-group",mode="delete",group=removal.Group},skillDestination:"skill");
                else Edit(new{op="gem-manage",mode="delete",group=removal.Group,gem=removal.Gem},skillDestination:"skill");
            }
            return true;
        }
        if(id=="group-create"){Edit(new{op="skill-group",mode="create"},skillDestination:"skill");return true;}
        if(id=="group-paste"){Edit((Func<object>)(()=>new{op="skill-group",mode="paste",text=readClipboard()}),skillDestination:"skill");return true;}
        if(id=="group-delete-all"){skillRemoval=(0,0,"All editable groups in this skill set. Automatically granted groups are kept.");return true;}
        if(id=="back"&&detailKind is "skill-manage" or "gem-picker" or "skill-options")
        {skillWorkspaceReturnControl=detailKind switch{"skill-manage"=>"pob.group-manage","skill-options"=>"pob.skill-options",_=>null};detailKind="skill";skillWorkspaceFocusRequested=true;focusRequestSequence++;return true;}
        if(detailKind=="gem-picker")
        {
            if(id=="gem-search"){gemSearch=action.CommittedText??"";return true;}
            if(parts is ["gem-sort",var metric]&&(metric=="name"||A(State,"gemSortMetrics").Any(m=>S(m,"id")==metric)))
            {
                var targetGroup=detailId;var targetGem=pickerGem;
                Work(async token=>
                {
                    var count=A(A(State,"skills").First(g=>I(g,"id")==targetGroup),"gems").Length;
                    var sorted=await engine.ExecuteAsync(new{op="gem-catalog",group=targetGroup,gem=targetGem==0?count+1:targetGem,sort=metric},token,result!.Xml);
                    lock(gate){SetGemCatalog(sorted.State);gemSort=metric;RefreshGemSuggestions();}
                },"Comparing gems with PoB…");return true;
            }
            if(parts is ["gem-type",var type]&&type is "all" or "active" or "support"){gemType=type;RefreshGemSuggestions();return true;}
            if(parts is ["gem-pick",var choice]&&int.TryParse(choice,out var option)&&option>=0&&option<gemCatalog.Length)
            {Edit(new{op="gem-manage",mode=pickerGem==0?"add":"replace",group=detailId,gem=pickerGem,gemId=S(gemCatalog[option],"id")},skillDestination:"skill");return true;}
        }
        var group=A(State,"skills").FirstOrDefault(g=>I(g,"id")==detailId);
        if(group.ValueKind!=JsonValueKind.Object)return false;
        if(id=="group-manage"){detailKind="skill-manage";focusRequestSequence++;return true;}
        if(id=="group-name"){Edit(new{op="skill-group",mode="rename",group=detailId,name=action.CommittedText??""},skillDestination:detailKind=="skill-manage"?"skill-manage":"skill");return true;}
        if(id=="group-count"&&double.TryParse(action.CommittedText,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var count))
        {Edit(new{op="skill-group",mode="count",group=detailId,value=count},skillDestination:detailKind=="skill-manage"?"skill-manage":"skill");return true;}
        if(id=="group-duplicate"){Edit(new{op="skill-group",mode="duplicate",group=detailId},skillDestination:detailKind=="skill-manage"?"skill-manage":"skill");return true;}
        if(parts is ["group-weapon-set",var setValue]&&int.TryParse(setValue,out var weaponSet)&&weaponSet is 1 or 2)
        {Edit(new{op="skill-group",mode="weapon-set",group=detailId,set=weaponSet},skillDestination:detailKind=="skill-manage"?"skill-manage":"skill");return true;}
        if(id=="group-copy")
        {
            var target=detailId;
            Work(async token=>{var copied=await engine.ExecuteAsync(new{op="skill-copy",group=target},token,result!.Xml);ClipboardText.Write(S(copied.State,"groupClipboard"));lock(gate)ShowFeedback("Group copied","Paste it into a skill set or desktop PoB.",ToastTone.Info);},"Copying group…");
            return true;
        }
        if(id=="group-delete"){if(!B(group,"generated"))skillRemoval=(detailId,0,S(group,"title"));return true;}
        if(id is "group-up" or "group-down")
        {skillOrderFocusRequested=true;Edit(new{op="skill-group",mode="move",group=detailId,direction=id=="group-up"?-1:1},skillDestination:detailKind=="skill-manage"?"skill-manage":"skill");return true;}
        if(parts is ["group-slot",var slotValue]&&int.TryParse(slotValue,out var slot)&&slot>=0&&slot<A(State,"skillSlots").Length)
        {Edit(new{op="skill-group",mode="slot",group=detailId,slot=S(A(State,"skillSlots")[slot],"name")},skillDestination:detailKind=="skill-manage"?"skill-manage":"skill");return true;}
        if(id=="gem-add"){if(!B(group,"generated"))OpenGemPicker(0);return true;}
        if(parts.Length==2&&int.TryParse(parts[1],out var gemId)&&A(group,"gems").Any(g=>I(g,"id")==gemId))
        {
            if(parts[0]=="gem-replace"){if(!B(group,"generated"))OpenGemPicker(gemId);return true;}
            if(parts[0]=="gem-remove"){if(!B(group,"generated"))skillRemoval=(detailId,gemId,S(A(group,"gems").First(g=>I(g,"id")==gemId),"name"));return true;}
            if(parts[0]=="gem-enabled"){Edit(new{op="gem-manage",mode="enabled",group=detailId,gem=gemId},skillDestination:"skill");return true;}
        }
        return false;
    }

    private void OpenGemPicker(int gem)
    {
        if(gemPickerGroup==detailId&&pickerGem==gem&&gemPickerXml==result!.Xml&&gemCatalog.Length>0)
        {detailKind="gem-picker";focusRequestSequence++;return;}
        var group=detailId;var xml=result!.Xml;
        Work(async token=>
        {
            var count=A(A(State,"skills").First(g=>I(g,"id")==group),"gems").Length;
            var response=await engine.ExecuteAsync(new{op="gem-catalog",group,gem=gem==0?count+1:gem},token,xml);
            lock(gate){SetGemCatalog(response.State);gemSearch="";gemType=count==0?"active":"all";gemSort="name";pickerGem=gem;gemPickerGroup=group;gemPickerXml=xml;detailKind="gem-picker";RefreshGemSuggestions();focusRequestSequence++;}
        },"Loading gems…");
    }

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
        else if(A(State,"skillSlots").Length>0)properties.Add(UI.Select("Socketed in",A(State,"skillSlots").Select((s,i)=>new SelectOption("slot"+i,S(s,"label"),"group-slot."+i,S(s,"name")==S(group,"slot"))).ToArray(),"pob.group-slot").Classes("pob-select"));
        foreach(var set in A(group,"weaponSets"))
        {
            var setId=I(set,"id");
            properties.Add(UI.Switch("Weapon set "+setId,B(set,"enabled"),"group-weapon-set."+setId,"pob.group-weapon-set."+setId).Disabled(!B(set,"editable")).Classes("pob-toggle"));
        }
        foreach(var reason in A(group,"weaponSets").Select(s=>S(s,"reason")).Where(s=>s.Length>0).Distinct())
            properties.Add(Text(reason,"group-weapon-reason."+properties.Count,"pob-small pob-muted"));
        properties.Add(Text($"Saved order · {position+1} of {groups.Length}","group-order","pob-field-label"));
        properties.Add(Text("The main group is pinned first in Skills. These controls change the saved order.","group-order.hint","pob-small pob-muted"));
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
        return UI.VerticalScroll("pob.skill-manage.tools",properties.ToArray()).RememberChildFocus("pob.group-name").Classes("pob-scroll");
    }

    private WidgetFocusPreviewSource<GemQuery>? gemPreviews;
    private GemQuery? gemPreviewQuery;
    private WidgetElement GemPickerWorkspace()
    {
        gemCollection??=CreateIndexedCollection<GemQuery,GemEntry>("pob.gem-source",gemQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>ValueTask.FromResult<IReadOnlyList<GemEntry>>(query.Entries.Skip(start).Take(count).ToArray()),
            ItemKey=entry=>new(entry.Key),
            RenderItem=RenderGemCandidate,
            OnAction=(query,entry,action,token)=>
            {lock(gate){if(!GemQueryIsCurrent(query)||busy)return ValueTask.CompletedTask;
                return action.ActionId=="choose"?OnActionAsync(action with{ActionId=entry.Action},token):ValueTask.CompletedTask;}},
        });
        var entries=gemSearchCatalog.Entries.Where(entry=>(gemType=="all"||entry.Support==(gemType=="support"))&&entry.Matches(gemSearch))
            .Select(entry=>(gem:gemCatalog[entry.Index],i:entry.Index))
            .Select(x=>new GemEntry(WidgetIds.Scope("gem").KeyedId("entry",S(x.gem,"id")),S(x.gem,"name")+(B(x.gem,"support")?" · Support":" · Active skill")+
                (gemSort!="name"&&x.gem.TryGetProperty("comparison",out var value)?" · "+value.GetDouble().ToString("N0",System.Globalization.CultureInfo.InvariantCulture):""),"gem-pick."+x.i,Inspection:gemInspectionCatalog[x.i])).ToArray();
        var context=CaptureGemPickerContext();
        if(!gemQuery.Entries.SequenceEqual(entries)||gemQuery.Context!=context||!ReferenceEquals(gemQuery.Context?.Build,context.Build)){gemQuery=new(entries,context);gemCollection.PublishQuery(gemQuery,entries.Length);}
        gemPreviews??=CreateFocusPreviewSource("pob.gem-preview-source",gemQuery,new WidgetFocusPreviewOptions<GemQuery>
        {
            Resolve=(query,key,token)=>{token.ThrowIfCancellationRequested();return ValueTask.FromResult(RenderGemCandidatePreview(query,query.Entries.Single(entry=>entry.Key==key),WidgetIds.Scope(WidgetIds.Scope("pob.preview").KeyedId("gem",key))));},
            OnAction=(query,key,action,token)=>{lock(gate){if(!GemQueryIsCurrent(query)||busy||action.ActionId!="choose")return ValueTask.CompletedTask;
                return OnActionAsync(action with{ActionId=query.Entries.Single(entry=>entry.Key==key).Action},token);}},
        });
        if(!ReferenceEquals(gemPreviewQuery,gemQuery)){gemPreviewQuery=gemQuery;gemPreviews.Publish(gemQuery);}
        ContainerElement results=UI.Stack("pob.gem-picker.results",entries.Length==0?Text("No matching gems.","gem-picker.empty","pob-muted"):
            UI.FocusPresentationSurface(UI.CollectionList("pob.gems",gemCollection,64,"Available gems").Classes("pob-scroll"),
                UI.Text("","pob.gem-preview.empty"),"pob.gem-preview") with{Preview=new(Width:480,Height:560){SizeToContentHeight=true,EnterButton=ControllerButton.X},PreviewSource=gemPreviews.Descriptor}).Classes("pob-workspace");
        if(entries.Length>0)results=QuickControls(results,"pob.gems","pob.gem-picker.tools","Search / type",NeedsQuickControls(entries.Length));
        return UI.Stack("pob.gem-picker",
            QuickControls(UI.Stack("pob.gem-picker.tools",UI.Row("pob.gem-picker.header",Button("Back to group","back"),Text((pickerGem==0?"Add gem · ":"Replace gem · ")+S(A(State,"skills").FirstOrDefault(g=>I(g,"id")==detailId),"title"),"gem-picker.title","pob-heading")).Classes("pob-toolbar"),
                UI.Grid("pob.gem-picker.search-row",[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Auto()],
                    Field("Search gems",UI.TextEntry(gemSearch,"Name or tag","gem-search","pob.gem-search",100).Suggest(gemSuggestionSource).Classes("pob-input"),"gem-search"),
                    UI.Select("Type",new[]{("all","All gems"),("active","Active skills"),("support","Support gems")}.Select(x=>new SelectOption(x.Item1,x.Item2,"gem-type."+x.Item1,x.Item1==gemType)).ToArray(),"pob.gem-type").Classes("pob-select").InGrid(column:1)).Spacing(0,12),
                UI.Select("Sort",new[]{new SelectOption("name","Name","gem-sort.name",gemSort=="name")}.Concat(A(State,"gemSortMetrics").Select(m=>new SelectOption(S(m,"id"),S(m,"title"),"gem-sort."+S(m,"id"),gemSort==S(m,"id")))).ToArray(),"pob.gem-sort").Classes("pob-select"),
                Text(gemSort=="name"?$"{entries.Length} gems · Defaults and compatibility follow the current PoB engine.":$"{entries.Length} gems · PoB ranks supports first, then the chosen total. Main skill: {S(State,"mainSkill")}. This is a comparison, not a recommendation.","gem-picker.count","pob-muted")),"pob.gem-search","pob.gem-picker.results","Gems",NeedsQuickControls(entries.Length)),
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
