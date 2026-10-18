using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private JsonElement itemEditor;
    private int customGroup=1;
    private int editingModifier=-1;
    private bool modifierFocusRequested;
    private (string Title,WidgetElement[] Rows)? featureReading;
    private (string Title,object Command)? featureRemoval;
    private (string Title,string Prefix,JsonElement[] Choices,string ReturnTo)? featurePicker;
    private (int Node,string? Build,JsonElement[] Choices)? attributeChoices;
    private (int Node,JsonElement[] Choices)? passiveChoicePopup;
    private string choiceSearch="";
    private GemQuery choiceQuery=new([]);
    private WidgetIndexedCollection<GemQuery,GemEntry>? choiceCollection;
    private sealed record ModifierEntry(int Index,string Text,bool Supported);
    private sealed record ModifierQuery(int Group,ModifierEntry[] Entries);
    private ModifierQuery modifierQuery=new(0,[]);
    private WidgetIndexedCollection<ModifierQuery,ModifierEntry>? modifierCollection;
    private static string Plain(string text)=>Regex.Replace(text,@"\^x[0-9a-fA-F]{6}|\^\d","");
    // Short choices belong beside the item. Keep search and full descriptions
    // for catalogs rather than forcing every setting through another page.
    internal static bool UsesCompactChoice(JsonElement[] choices)=>choices.Length is >0 and <=12 &&
        choices.All(c=>Plain(S(c,"title")).Trim().Length is >0 and <=80 &&
            !S(c,"title").Contains('\n') && (S(c,"description").Length==0 || Plain(S(c,"description"))==Plain(S(c,"title"))));
    private WidgetElement ItemChoiceControl(JsonElement control,string action,string optionPrefix)
    {
        var options=A(control,"options");var title=InlineLabel(Plain(S(control,"title")));
        if(!UsesCompactChoice(options))return Button(title+": "+Plain(S(options.FirstOrDefault(o=>B(o,"active")),"title","Choose…")),action).Disabled(!B(control,"enabled"));
        var selected=Array.FindIndex(options,o=>B(o,"active"));
        var entries=options.Select((o,i)=>new SelectOption("choice"+I(o,"id"),InlineLabel(Plain(S(o,"title"))),optionPrefix+I(o,"id"),i==selected)).ToList();
        if(selected<0)entries.Insert(0,new("current","Choose…","noop",true,IsDisabled:true));
        return UI.Select(title,entries,"pob."+action).Disabled(!B(control,"enabled")).Classes("pob-select");
    }
    private string ItemSlot=>S(A(State,"items").FirstOrDefault(i=>I(i,"id")==detailId),"slot");

    private void ReadAttributeChoices(int nodeId)
    {
        attributeChoices=null;
        var node=A(State,"nodes").FirstOrDefault(n=>I(n,"id")==nodeId);
        if(!B(node,"canChoose")||S(node,"choiceKind")!="attribute")return;
        var buildId=documentId;var xml=result!.Xml;
        Work(async token=>
        {
            var response=await engine.ExecuteAsync(new{op="passive-choices",id=nodeId,allocationMode=Capabilities.WeaponTreeModes?(int?)I(State,"allocationMode"):null},token,xml);
            lock(gate)if(documentId==buildId&&detailId==nodeId)attributeChoices=(nodeId,buildId,A(response.State,"choices"));
        },"Reading attribute choices…");
    }

    private WidgetElement PassiveChoiceControl(JsonElement node)
    {
        if(S(node,"choiceKind")=="attribute"&&attributeChoices is {} data&&data.Node==detailId&&data.Build==documentId&&data.Choices.Length>0)
        {
            var selected=Array.FindIndex(data.Choices,c=>S(c,"title")==S(node,"name"));
            var choices=data.Choices.Select((c,i)=>new SelectOption("effect"+I(c,"id"),InlineLabel(Plain(S(c,"title"))),$"passive-choice.{detailId}.{I(c,"id")}",i==selected)).ToList();
            if(selected<0)choices.Insert(0,new("current","Choose…","noop",true,IsDisabled:true));
            return UI.Select("Attribute",choices,"pob.node-choices").Classes("pob-select");
        }
        return Button(B(node,"allocated")?"Change effect…":"Choose effect…","node-choices");
    }

    private async Task RefreshItemEditorAsync(PobResult next,CancellationToken token)
    {
        if(detailKind!="item-manage")return;
        var slot=S(A(next.State,"items").FirstOrDefault(i=>I(i,"id")==detailId),"slot");
        var inspected=await engine.ExecuteAsync(new{op="inspect-item",slot},token,next.Xml);
        lock(gate)itemEditor=inspected.State.GetProperty("itemEditor");
    }

    private bool HandlePlanningAction(WidgetActionEvent action)
    {
        var id=action.ActionId;var parts=id.Split('.');
        if(timelessDetail is {} jewel)
        {
            if(id=="timeless-detail-close")timelessDetail=null;
            else if(id=="timeless-add."+jewel.Row.Id){timelessDetail=null;HandleTimeless(action);}
            return true;
        }
        if(id=="passive-choice-close"){passiveChoicePopup=null;return true;}
        if(passiveChoicePopup is not null&&parts[0]!="passive-choice")return true;
        if(featureReading is not null){if(id=="feature-close")featureReading=null;return true;}
        if(featureRemoval is {} removal)
        {
            if(id=="feature-close")featureRemoval=null;
            else if(id=="feature-confirm"){featureRemoval=null;Edit(removal.Command);}
            return true;
        }
        if(parts is ["library-class",var selectedClass])
        {if(selectedClass=="all")libraryClass="";else if(int.TryParse(selectedClass,out var classIndex)&&classIndex>=0&&classIndex<LibraryClasses().Length)libraryClass=LibraryClasses()[classIndex];return true;}
        if(result is null)return false;
        if(HandleTreeEditing(action))return true;
        if(id=="node-choices"&&detailKind=="node")
        {
            var nodeId=detailId;
            Work(async token=>
            {
                var response=await engine.ExecuteAsync(new{op="passive-choices",id=nodeId,allocationMode=Capabilities.WeaponTreeModes?(int?)I(State,"allocationMode"):null},token,result.Xml);
                var choices=A(response.State,"choices");
                lock(gate)
                {
                    if(choices.Length==0)ShowFeedback("No available effects","The available effects may already be allocated at another mastery.",ToastTone.Info);
                    else passiveChoicePopup=(nodeId,choices);
                }
            },"Reading passive choices…");return true;
        }
        if(detailKind=="node"&&parts is ["passive-choice",var passiveId,var choiceId]&&int.TryParse(passiveId,out var node)&&node==detailId&&int.TryParse(choiceId,out var chosen)&&
            ((passiveChoicePopup is {} popup&&popup.Node==node&&popup.Choices.Any(c=>I(c,"id")==chosen))||
             (attributeChoices is {} attributes&&attributes.Node==node&&attributes.Build==documentId&&attributes.Choices.Any(c=>I(c,"id")==chosen))))
        {
            passiveChoicePopup=null;detailKind="node";Preview(new{op="node-choice",id=node,choice=chosen,allocationMode=Capabilities.WeaponTreeModes?(int?)I(State,"allocationMode"):null});return true;
        }
        if(id=="back"&&detailKind=="feature-picker"&&featurePicker is {} picker){detailKind=picker.ReturnTo;featurePicker=null;return true;}
        if(id=="choice-search"){choiceSearch=action.CommittedText??"";return true;}
        if(id=="item-manage")
        {
            Work(async token=>
            {
                var inspected=await engine.ExecuteAsync(new{op="inspect-item",slot=ItemSlot},token,result.Xml);
                lock(gate){itemEditor=inspected.State.GetProperty("itemEditor");detailKind="item-manage";focusRequestSequence++;}
            },"Reading item controls…");return true;
        }
        if(id=="back"&&detailKind=="item-manage"){detailKind="item";return true;}
        if(parts is ["item-control",var controlIndex]&&int.TryParse(controlIndex,out var ci)&&ci>=0&&ci<A(itemEditor,"controls").Length)
        {
            var control=A(itemEditor,"controls")[ci];var options=A(control,"options");
            if(options.Length>0)OpenFeaturePicker(Plain(S(control,"title")),"item-choice."+ci+".",options);
            else if(int.TryParse(action.CommittedText,out var value))Edit(new{op="item-edit",mode="control",slot=ItemSlot,control=S(control,"id"),value});
            return true;
        }
        if(parts is ["item-choice",var controlNumber,var choice]&&int.TryParse(controlNumber,out ci)&&ci>=0&&ci<A(itemEditor,"controls").Length&&int.TryParse(choice,out var option))
        {
            var control=A(itemEditor,"controls")[ci];
            if(!B(control,"enabled")||!A(control,"options").Any(o=>I(o,"id")==option)||
                !(detailKind=="item-manage"||featurePicker?.Prefix=="item-choice."+ci+"."))return true;
            featurePicker=null;detailKind="item-manage";
            Edit(new{op="item-edit",mode="control",slot=ItemSlot,control=S(A(itemEditor,"controls")[ci],"id"),value=option});return true;
        }
        if(parts is ["item-mod",var mod]&&int.TryParse(mod,out var modId))
        {Edit(new{op="item-edit",mode="mod",slot=ItemSlot,id=modId});return true;}
        if(parts is ["character-rune",var runeIndex]&&int.TryParse(runeIndex,out var ri)&&ri>=0&&ri<A(State,"characterRunes").Length)
        {var rune=A(State,"characterRunes")[ri];OpenFeaturePicker(S(rune,"title"),"rune-choice."+ri+".",A(rune,"options"));return true;}
        if(parts is ["rune-choice",var runeNumber,var runeChoice]&&int.TryParse(runeNumber,out ri)&&ri>=0&&ri<A(State,"characterRunes").Length&&int.TryParse(runeChoice,out var runeId))
        {var rune=A(State,"characterRunes")[ri];featurePicker=null;detailKind="item";Edit(new{op="character-rune",slot=S(rune,"id"),value=runeId});return true;}
        if(parts is ["gem-quality",var gemNumber]&&int.TryParse(gemNumber,out var gem))
        {
            Work(async token=>
            {
                var details=await engine.ExecuteAsync(new{op="gem-quality",group=detailId,gem},token,result.Xml);
                var rows=new List<WidgetElement>();
                foreach(var (line,i) in A(details.State,"lines").Select((l,i)=>(l,i)))AddFormatted(rows,line,"text","runs","quality."+i);
                if(rows.Count==0)rows.Add(Text("PoB provides no quality effects for this gem.","quality.empty","pob-muted"));
                lock(gate)featureReading=("Gem quality",rows.ToArray());
            },"Reading gem quality…");return true;
        }
        if(id=="node-note"){Edit(new{op="node-note",id=detailId,text=action.CommittedText??""});return true;}
        if(parts is ["custom-group",var number]&&int.TryParse(number,out var groupId)){customGroup=groupId;editingModifier=-1;return true;}
        if(parts is ["custom-open",var openNumber]&&int.TryParse(openNumber,out groupId)){customGroup=groupId;editingModifier=-1;configSection="Custom modifiers";filter="";return true;}
        if(id=="custom-new"){editingModifier=-1;return true;}
        var groups=A(State,"customModGroups");
        var group=groups.FirstOrDefault(g=>I(g,"id")==customGroup);
        if(id=="custom-add"){editingModifier=-1;Edit(new{op="custom-mod",mode="add"});customGroup=groups.Length+1;return true;}
        if(group.ValueKind==JsonValueKind.Object)
        {
            if(id=="custom-delete"){featureRemoval=("Delete modifier group “"+S(group,"title")+"”?",new{op="custom-mod",mode="delete",id=customGroup});return true;}
            if(id=="custom-title"){Edit(new{op="custom-mod",mode="title",id=customGroup,text=action.CommittedText??""});return true;}
            if(id=="custom-enabled"){Edit(new{op="custom-mod",mode="enabled",id=customGroup});return true;}
            if(id=="custom-compare"){ShowPlanningComparison(new{op="custom-mod",mode="enabled",id=customGroup},B(group,"enabled")?"Effect of disabling this group":"Effect of enabling this group");return true;}
            if(id=="custom-paste")
            {Edit((Func<object>)(()=>new{op="custom-mod",mode="text",id=customGroup,text=readClipboard()}));return true;}
            if(id=="custom-new-line"||parts is ["custom-line",_])
            {
                var text=S(group,"text");var lines=text.Split('\n').ToList();
                if(id=="custom-new-line")lines.Add(action.CommittedText??"");
                else if(int.TryParse(parts[1],out var line)&&line>=0&&line<lines.Count)lines[line]=action.CommittedText??"";
                else return true;
                Edit(new{op="custom-mod",mode="text",id=customGroup,text=string.Join('\n',lines)});return true;
            }
        }
        if(parts is ["config-compare",var condition]&&int.TryParse(condition,out var index)&&index>=0&&index<A(State,"conditions").Length)
        {
            var c=A(State,"conditions")[index];var v=c.GetProperty("value");
            if(v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                ShowPlanningComparison(new{op="config",key=S(c,"id"),value=!v.GetBoolean()},"Effect of toggling "+InlineLabel(S(c,"title")));
            return true;
        }
        return false;
    }

    private void ShowPlanningComparison(object command,string title)=>Work(async token=>
    {
        var before=result!;
        var proposed=await engine.ExecuteAsync(command,token,before.Xml,compare:true);
        var rows=new List<WidgetElement>();AddComparison(rows,before.State,proposed.State,"feature.comparison");
        await engine.ExecuteAsync(new{op="load",xml=before.Xml,name="Working build"},token);
        rows.Insert(0,Text("Comparison only. Your build has not changed.","feature.comparison.help","pob-muted"));
        lock(gate)featureReading=(title,rows.ToArray());
    },"Calculating comparison…");

    private void OpenFeaturePicker(string title,string prefix,JsonElement[] choices)
    {featurePicker=(title,prefix,choices,detailKind);choiceSearch="";detailKind="feature-picker";focusRequestSequence++;}

    private WidgetElement FeaturePickerWorkspace()
    {
        if(featurePicker is not {} picker)return Text("Choose a setting again.","choice.missing");
        if(picker.Prefix=="unique-item.")return UniqueCandidateWorkspace(picker.Choices);
        choiceCollection??=CreateIndexedCollection<GemQuery,GemEntry>("pob.choice-source",choiceQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>ValueTask.FromResult<IReadOnlyList<GemEntry>>(query.Entries.Skip(start).Take(count).ToArray()),
            ItemKey=entry=>new(entry.Key),
            RenderItem=(_,entry,context)=>entry.Description.Length==0?UI.Button(entry.Name,"choose",context.Id("choice")).Classes("pob-button"):
                UI.ActionSurface("choose",context.Id("choice"),entry.Name,ActionSurfaceOrientation.Vertical,
                    UI.Text(entry.Name,context.Id("name")).Classes("pob-list-title"),
                    UI.RichText(context.Id("description"),ReadFormattedRuns(JsonSerializer.SerializeToElement(new{text=Limit(entry.Description,240)}),"text","runs",true)
                        .Select((run,i)=>(WidgetElement)UI.Text(run.Text,context.Id("span"+i)).Classes("pob-small","pob-inline",ToneClass(run.Tone))).ToArray()).Classes("pob-small","pob-choice-description"))
                    .Shortcut(ControllerButton.X,"Details",actionId:"details").Classes("pob-list-item"),
            OnAction=(query,entry,action,token)=>{lock(gate){if(!ReferenceEquals(query,choiceQuery)||detailKind!="feature-picker"||busy)return ValueTask.CompletedTask;
                if(action.ActionId=="details"){var rows=new List<WidgetElement>();AddFormatted(rows,JsonSerializer.SerializeToElement(new{text=entry.Description}),"text","runs","choice.details",emphasizeStats:true);featureReading=(entry.Name,rows.ToArray());Invalidate();return ValueTask.CompletedTask;}
                return action.ActionId=="choose"?OnActionAsync(action with{ActionId=entry.Action},token):ValueTask.CompletedTask;}},
        });
        var entries=picker.Choices.Where(c=>Plain(S(c,"title")+" "+S(c,"description")).Contains(choiceSearch,StringComparison.OrdinalIgnoreCase))
            .Select(c=>new GemEntry("choice"+I(c,"id"),Plain(S(c,"title"))+(B(c,"active")?" · Selected":""),picker.Prefix+I(c,"id"),S(c,"description")==S(c,"title")?"":Plain(S(c,"description")))).ToArray();
        if(!choiceQuery.Entries.SequenceEqual(entries)){choiceQuery=new(entries);choiceCollection.PublishQuery(choiceQuery,entries.Length);}
        var toolRows=new List<WidgetElement>{UI.Row("pob.choice.header",Button("Back","back"),Text(picker.Title,"choice.title","pob-heading")).Classes("pob-toolbar")};
        if(picker.Prefix=="author-modifier.")
        {
            toolRows.Add(UI.Select("Source",modifierSources.Select(s=>new SelectOption(S(s,"key"),S(s,"title"),"author-source."+S(s,"key"),S(s,"key")==modifierSource)).ToArray(),"pob.author-source").Classes("pob-select"));
            toolRows.Add(Text("PoB's eligible modifiers for this item. Planning edits do not guarantee an in-game craft.","modifier.help","pob-small pob-muted"));
        }
        if(picker.Prefix=="craft-base.")toolRows.Add(UI.Select("Rarity",new[]{"NORMAL","MAGIC","RARE"}.Select(r=>new SelectOption(r,char.ToUpperInvariant(r[0])+r[1..].ToLowerInvariant(),"craft-rarity."+r,r==craftRarity)).ToArray(),"pob.craft-rarity").Classes("pob-select"));
        toolRows.Add(SearchField(UI.TextEntry(choiceSearch,"Search options","choice-search","pob.choice-search",100),
            picker.Choices,()=>picker.Choices.Select(c=>(Plain(S(c,"title")),Plain(S(c,"title")+" "+S(c,"description")),Plain(S(c,"description"))))).Classes("pob-input"));
        toolRows.Add(Text($"{entries.Length} options","choice.count","pob-muted"));
        return UI.Stack("pob.choice-picker",
            QuickControls(UI.Stack("pob.choice-tools",toolRows.ToArray()),"pob.choice-search","pob.choice-results","Options",NeedsQuickControls(entries.Length)),
            entries.Length==0?UI.Stack("pob.choice-results",Text("No matching options.","choice.empty")).Classes("pob-workspace"):
            QuickControls(UI.Stack("pob.choice-results",UI.CollectionList("pob.choices",choiceCollection,entries.Any(e=>e.Description.Length>0)?112:56,picker.Title).Classes("pob-scroll")).Classes("pob-workspace"),"pob.choices","pob.choice-tools","Search",NeedsQuickControls(entries.Length))).Classes("pob-workspace");
    }

    private WidgetElement ItemManagementWorkspace()
    {
        var controls=new List<WidgetElement>{Button("Back to items","back"),Text(S(A(State,"items").FirstOrDefault(i=>I(i,"id")==detailId),"name","Item settings"),"item-edit.title","pob-heading"),Text("Changes save immediately. Undo restores them. Shared items change in every set that uses that item.","item-edit.help","pob-muted")};
        controls.Add(Button("Edit affixes and modifiers","equipped-author"));
        foreach(var (control,i) in A(itemEditor,"controls").Select((c,i)=>(c,i)))
        {
            var options=A(control,"options");
            if(options.Length>0)controls.Add(ItemChoiceControl(control,"item-control."+i,"item-choice."+i+"."));
            else controls.Add(Field(S(control,"title"),UI.TextEntry(S(control,"value"),S(control,"title"),"item-control."+i,"pob.item-control."+i,2).Classes("pob-input"),"item-control."+i));
        }
        var mods=new List<WidgetElement>{Text("Item modifiers","item-edit.mods-title","pob-heading")};
        foreach(var mod in A(itemEditor,"mods"))
        {
            var lines=new List<WidgetElement>();AddFormatted(lines,mod,"text","runs","item-mod.text."+I(mod,"id"),emphasizeStats:true);
            lines.Add(UI.Switch("Enabled",B(mod,"enabled"),"item-mod."+I(mod,"id"),"pob.item-mod."+I(mod,"id")).FocusLeft(A(itemEditor,"controls").Length>0?"pob.item-control.0":"pob.back").Classes("pob-toggle"));
            mods.Add(UI.Stack("pob.item-mod.card."+I(mod,"id"),lines.ToArray()).Classes("pob-context-card"));
        }
        if(mods.Count==1)mods.Add(Text("This item has no modifiers that PoB can toggle.","item-edit.empty","pob-muted"));
        var modList=UI.VerticalScroll("pob.item-edit.mods",mods.ToArray()).Classes("pob-scroll","pob-panel");
        if(A(itemEditor,"mods").Length>0)modList=modList.RememberChildFocus("pob.item-mod."+I(A(itemEditor,"mods")[0],"id"));
        WidgetElement LinkRight(WidgetElement element)=>element switch
        {
            ButtonElement button=>button.FocusRight("pob.item-mod."+I(A(itemEditor,"mods")[0],"id")),TextEntryElement input=>input.FocusRight("pob.item-mod."+I(A(itemEditor,"mods")[0],"id")),
            SelectElement select=>select.FocusRight("pob.item-mod."+I(A(itemEditor,"mods")[0],"id")),
            ContainerElement container=>container with{Children=container.Children.Select(LinkRight).ToArray()},_=>element,
        };
        return Split("item-edit",UI.VerticalScroll("pob.item-edit.controls",(A(itemEditor,"mods").Length>0?controls.Select(LinkRight):controls).ToArray()).RememberChildFocus("pob.back").Classes("pob-scroll","pob-panel"),modList,1,1.6);
    }

    private WidgetElement CustomModsPanel()
    {
        var groups=A(State,"customModGroups");if(!groups.Any(g=>I(g,"id")==customGroup))customGroup=groups.Length>0?I(groups[0],"id"):1;
        var group=groups.FirstOrDefault(g=>I(g,"id")==customGroup);
        var rawLines=S(group,"text").Split('\n');
        if(editingModifier>=rawLines.Length)editingModifier=-1;
        var editValue=editingModifier>=0?rawLines[editingModifier]:"";
        var rows=new List<WidgetElement>{Text("Custom modifiers","custom.heading","pob-heading"),Text("Changes save immediately. Empty a line to remove it.","custom.help","pob-muted"),
            UI.Row("pob.custom.groups",UI.Select("Group",groups.Select(g=>new SelectOption("group"+I(g,"id"),Limit(S(g,"title"),100)+(B(g,"enabled")?"":" · Disabled"),"custom-group."+I(g,"id"),I(g,"id")==customGroup)).ToArray(),"pob.custom-group").Classes("pob-select"),Button("Add","custom-add"),Button("Delete…","custom-delete")).Classes("pob-toolbar"),
            UI.Grid("pob.custom.identity",[GridTrack.Auto()],[GridTrack.Star(),GridTrack.Auto()],
                Field("Group name",UI.TextEntry(Limit(S(group,"title"),100),"Group name","custom-title","pob.custom-title",101).Classes("pob-input"),"custom-title"),
                Field("Group state",UI.Switch("Enabled",B(group,"enabled"),"custom-enabled","pob.custom-enabled").Shortcut(ControllerButton.X,"Compare toggle",actionId:"custom-compare").Classes("pob-toggle"),"custom-enabled").InGrid(column:1)).Spacing(0,12),
            UI.Row("pob.custom.actions",Button("Compare toggle","custom-compare"),Button("Replace from clipboard","custom-paste"),Button("New modifier","custom-new").Disabled(editingModifier<0)).Classes("pob-toolbar"),
            UI.Stack("pob.custom.edit-tools",Field(editingModifier>=0?"Edit modifier "+(editingModifier+1):"Add modifier",
                editValue.Length<=2048?UI.TextEntry(editValue,"e.g. +100 to maximum Life",editingModifier>=0?"custom-line."+editingModifier:"custom-new-line","pob.custom-new-line",2048).Classes("pob-input"):
                UI.Stack("pob.custom.large-line",Text("This line is too long for the keyboard.","custom.large-line-help","pob-muted"),Button("Replace group from clipboard","custom-paste","custom-long-paste")),"custom-new-line")).RememberChildFocus(editValue.Length<=2048?"pob.custom-new-line":"pob.custom-long-paste")};
        var supported=A(group,"lines");var text=S(group,"text").Split('\n');var nonempty=0;
        var entries=new List<ModifierEntry>();
        for(var i=0;i<text.Length;i++)if(text[i].Length>0)
        {
            var recognized=nonempty<supported.Length&&B(supported[nonempty++],"supported");
            entries.Add(new(i,text[i],recognized));
        }
        modifierCollection??=CreateIndexedCollection<ModifierQuery,ModifierEntry>("pob.modifier-source",modifierQuery,0,new()
        {
            ReadRange=(query,start,count,token)=>ValueTask.FromResult<IReadOnlyList<ModifierEntry>>(query.Entries.Skip(start).Take(count).ToArray()),
            ItemKey=entry=>new("line"+entry.Index),
            RenderItem=(_,entry,context)=>UI.ActionSurface("edit",context.Id("row"),"Edit modifier "+(entry.Index+1),ActionSurfaceOrientation.Vertical,
                UI.Text((entry.Supported?"Modifier ":"Unsupported modifier ")+(entry.Index+1),context.Id("label")).Classes(entry.Supported?"pob-field-label":"pob-gold"),
                UI.Text(Limit(entry.Text),context.Id("text")).Classes("pob-small","pob-choice-description")).Classes("pob-list-item"),
            OnAction=(query,entry,action,token)=>{lock(gate){if(!ReferenceEquals(query,modifierQuery)||customGroup!=query.Group||busy||action.ActionId!="edit")return ValueTask.CompletedTask;
                editingModifier=entry.Index;modifierFocusRequested=true;focusRequestSequence++;Invalidate();return ValueTask.CompletedTask;}},
        });
        if(modifierQuery.Group!=customGroup||!modifierQuery.Entries.SequenceEqual(entries))
        {
            var same=modifierQuery.Group==customGroup&&modifierQuery.Entries.Select(e=>e.Index).SequenceEqual(entries.Select(e=>e.Index));
            modifierQuery=new(customGroup,entries.ToArray());
            if(same)modifierCollection.UpdateContent(modifierQuery);else modifierCollection.PublishQuery(modifierQuery,entries.Count);
        }
        var quick=NeedsQuickControls(entries.Count);
        return UI.Stack("pob.custom-mods",QuickControls(UI.Stack("pob.custom.tools",rows.ToArray()),"pob.custom-group","pob.custom.results","Modifiers",quick),
            entries.Count==0?UI.Stack("pob.custom.results",Text("No modifiers in this group.","custom.empty","pob-muted")).Classes("pob-workspace"):
            QuickControls(UI.Stack("pob.custom.results",UI.CollectionList("pob.custom.lines",modifierCollection,94,"Custom modifier lines").Classes("pob-scroll")).Classes("pob-workspace"),"pob.custom.lines","pob.custom.tools","Group controls",quick)).Classes("pob-panel","pob-tree-main");
    }

    private WidgetView WithPlanningModal(WidgetView view)
    {
        if(candidateInspection is not null)return WithItemCandidateInspection(view);
        if(timelessDetail is {} jewel)return view.WithModal(new("pob-timeless-detail",jewel.Row.Title,
            UI.Stack("pob.timeless.detail.body",UI.Row("pob.timeless.detail.actions",Button("Back to results","timeless-detail-close"),Button("Add to item inventory","timeless-add."+jewel.Row.Id)),
                UI.VerticalScroll("pob.timeless.detail.lines",jewel.Lines).Classes("pob-scroll")).Classes("pob-card","pob-workspace"),"pob.timeless-detail-close","timeless-detail-close"){ScrollContent=false});
        if(passiveChoicePopup is {} passive)
        {
            var options=passive.Choices.Select(c=>
            {
                var id=I(c,"id");var rows=new List<WidgetElement>();
                if(B(c,"active"))rows.Add(Text("Current effect","passive-current."+id,"pob-small pob-gold"));
                AddFormatted(rows,JsonSerializer.SerializeToElement(new{text=Plain(S(c,"title"))}),"text","runs","passive-option."+id,emphasizeStats:true);
                return (WidgetElement)UI.ActionSurface($"passive-choice.{passive.Node}.{id}","pob.passive-choice."+id,Plain(S(c,"title")),ActionSurfaceOrientation.Vertical,rows.ToArray())
                    .Selected(B(c,"active")).Classes("pob-list-item");
            }).ToArray();
            var selected=Array.FindIndex(passive.Choices,c=>B(c,"active"));
            return view.WithModal(new("pob-passive-choices","Choose passive effect",
                UI.Stack("pob.passive-choices.body",options).Classes("pob-card"),options[Math.Max(0,selected)].Id,"passive-choice-close"));
        }
        if(featureReading is {} reading)return view.WithModal(new("pob.feature-reading",reading.Title,
            UI.VerticalScroll("pob.feature-reading.body",reading.Rows).Classes("pob-scroll","pob-card"),"pob.feature-reading.close","feature-close"));
        if(featureRemoval is {} removal)return view.WithModal(new("pob.feature-removal",removal.Title,
            UI.Stack("pob.feature-removal.body",Text("Undo can restore this deletion.","feature-removal.help"),UI.Row("pob.feature-removal.actions",Button("Cancel","feature-close"),Button("Delete","feature-confirm"))).Classes("pob-card"),"pob.feature-close","feature-close"));
        return view;
    }
}
