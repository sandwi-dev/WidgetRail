using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    // Selection is independent of navigation: inspecting a row never removes its list.
    private bool InlineDetail => detailKind is "skill" or "item" or "node" or "stat" or "map";

    private WidgetElement BuildHeader()
    {
        var identity = S(State, "ascendancy");
        if (string.IsNullOrWhiteSpace(identity) || identity == "None") identity = S(State, "class");
        var loadouts = A(State, "loadouts");
        var controls = new List<WidgetElement>();
        if (loadouts.Length > 0)
        {
            var choices = loadouts.Select((l, i) => new SelectOption("loadout" + i, InlineLabel(S(l, "title"),"Loadout "+(i+1)), "loadout." + i, B(l, "active"))).ToList();
            if (!choices.Any(c => c.IsSelected)) choices.Insert(0, new("custom", "Independent sets", "noop", true));
            controls.Add(UI.Select("Loadout", choices, "pob.loadout").Disabled(pendingBefore is not null).Classes("pob-select", "pob-loadout"));
        }
        controls.Add(UI.Button("Search","unified-search","pob.unified-search").Disabled(pendingBefore is not null).Classes("pob-button","pob-header-action"));
        controls.Add(UI.Button("Build & import", "menu", "pob.menu").Disabled(pendingBefore is not null).Classes("pob-button","pob-header-action"));
        var history=UI.Row("pob.history",HistoryButton(true),HistoryButton(false)).Classes("pob-toolbar");
        var heading = new List<WidgetElement>();
        heading.Add(RecentBuildSelector());
        heading.Add(Text($"{identity}  ·  {(B(State,"autoLevel")?"Auto level":"Level")} {S(State, "level")}", "context", "pob-muted"));
        controls.Add(UI.FocusPresentationSurface(history, UI.Text("", "pob.history.preview.empty"), "pob.history.preview")
            with { Preview = new(Width: 440, Height: 440) { SizeToContentHeight = true } });
        var tools=UI.Grid("pob.buildtools",[GridTrack.Auto()],
            controls.Select((_,i)=>i==0&&loadouts.Length>0?GridTrack.Star():GridTrack.Auto()).ToArray(),
            controls.Select((control,i)=>control.InGrid(column:i)).ToArray()).Spacing(0,6)
            .RememberChildFocus(historyFocusRequested ? undo.Count>0 ? "pob.undo" : "pob.redo" :
                loadouts.Length>0 ? "pob.loadout" : "pob.menu").Classes("pob-buildtools");
        if(BuildControlsAvailable)tools=tools.Shortcut(ControllerButton.RightStick,"quick-controls.pob.body","Return to content");
        return UI.ResponsiveGrid("pob.header",520,2,
            UI.Stack("pob.identity", heading.ToArray()).Classes("pob-build-identity"),tools).Classes("pob-build-header");
    }

    private WidgetElement? TradeSessionControls()
    {
        var actions=new List<WidgetElement>();
        if(websiteSession is not null||savedTradeUrl is not null)
            actions.Add(UI.Button("Return to listings","website-resume","pob.website-return").Classes("pob-button","pob-header-action"));
        if(HasRanking)actions.Add(Button("Ranked results","trade-rank-show").Disabled((pendingBefore is not null||pastedItem is not null)&&!CanReplaceTradeComparison));
        return actions.Count==0?null:UI.Row("pob.trade-session",actions.ToArray()).Classes("pob-toolbar");
    }

    private WidgetElement Navigation()
    {
        var tabs = NavigationSections.Select((s, position) =>
        {
            var i=Array.IndexOf(Sections,s);
            WidgetElement tab=UI.Button(s,"section."+i,i==0?"pob.section":"pob.tab."+i)
                .Selected(section==s).Disabled(pendingBefore is not null).Classes("pob-tab");
            if(s=="Notes")tab=UI.Stack("pob.notes.tab-target",tab).RememberChildFocus("pob.tab."+i);
            return tab.InGrid(column:position+1);
        }).ToList();
        tabs.Insert(0, UI.ControllerGlyph(ControllerButton.LeftBumper, "pob.navigation.previous", "Previous section")
            .Classes("pob-section-hint").InGrid(column: 0));
        tabs.Add(UI.ControllerGlyph(ControllerButton.RightBumper, "pob.navigation.next", "Next section")
            .Classes("pob-section-hint").InGrid(column: NavigationSections.Length + 1));
        return UI.Grid("pob.navigation", [GridTrack.Auto()],
            [GridTrack.Auto(), .. NavigationSections.Select(s => GridTrack.Star(s == "Configuration" ? 1.5 : 1)), GridTrack.Auto()],
            tabs.ToArray()).Spacing(0, 5).Classes("pob-tabs");
    }

    private WidgetElement CompactHeadlines()
    {
        var values = new List<WidgetElement>();
        foreach(var key in new[]{"CombinedDPS","Life","EnergyShield","Mana"})
        {
            var stat=A(State,"stats").FirstOrDefault(s=>S(s,"name")==key);
            if(stat.ValueKind!=JsonValueKind.Object)continue;
            var label=key=="CombinedDPS"?S(State,"mainSkill","Main skill")+(B(State,"mainSkillShowsAverage")?" average damage":" DPS"):StatLabel(key);
            values.Add(Text(label+"  "+DisplayStatValue(key,stat.GetProperty("value").GetDouble()),"compact."+key,"pob-compact-value "+StatColor(key)));
        }
        values.Add(Text($"Passives {S(State,"passivePoints")}  |  Ascendancy {S(State,"ascendancyPoints")}","compact.points","pob-muted"));
        return UI.Row("pob.headlines",values.ToArray()).Classes("pob-compact-stats");
    }

    private WidgetElement Workspace()
    {
        if(detailKind=="choose-slot"&&pendingBefore is not null)return EquipmentComparisonWorkspace();
        if(detailKind=="game-export")return GameExportWorkspace();
        if(detailKind=="trade")return TradeWorkspace();
        if(detailKind=="similar-search")return SimilarSearchWorkspace();
        if(detailKind=="inventory")return StoredInventoryWorkspace();
        if(detailKind=="item-author")return ItemAuthoringWorkspace();
        if(detailKind=="calculations")return CalculationsWorkspace();
        if(detailKind=="timeless")return TimelessWorkspace();
        if(detailKind=="gem-options")return GemOptionsWorkspace();
        if(detailKind=="item-manage")return ItemManagementWorkspace();
        if(detailKind=="feature-picker")return FeaturePickerWorkspace();
        if(detailKind=="library")return LibraryWorkspace();
        if(detailKind is "skill-manage" or "gem-picker" or "skill-options")return InventoryWorkspace();
        if (detailKind.Length > 0 && !InlineDetail)
            return UI.VerticalScroll("pob.scroll", Content().ToArray()).Classes("pob-scroll", "pob-panel");
        if (section == "Calcs" || detailKind == "stat") return Overview();
        if (section is "Skills" or "Items") return InventoryWorkspace();
        if (section == "Tree") return TreeWorkspace();
        if (section == "Configuration") return ConfigurationWorkspace();
        if (section == "Notes") return NotesWorkspace();
        return UI.VerticalScroll("pob.scroll", Content().ToArray()).Classes("pob-scroll", "pob-panel");
    }

    private WidgetElement Overview()
    {
        var groups = new (string title, string color, string[] keys)[]
        {
            ("Offence", "pob-gold", ["CombinedDPS", "FullDPS", "TotalDPS"]),
            ("Defence", "pob-blue", ["Armour", "Evasion", "EffectiveHitPool"]),
            ("Resistances", "pob-green", ["FireResist", "ColdResist", "LightningResist", "ChaosResist"]),
            ("Maximum hit taken", "pob-violet", ["PhysicalMaximumHitTaken", "FireMaximumHitTaken", "ColdMaximumHitTaken", "LightningMaximumHitTaken", "ChaosMaximumHitTaken"]),
            ("Resources", "pob-life", ["Life", "Mana", "EnergyShield", "Spirit", "SpiritReserved"]),
            ("Minions", "pob-gold", A(State, "stats").Select(s => S(s, "name")).Where(k => k.StartsWith("Minion", StringComparison.Ordinal)).ToArray()),
        };
        var cards = new List<WidgetElement>();
        foreach (var (title, color, keys) in groups)
        {
            var stats = A(State, "stats").Where(s => keys.Contains(S(s, "name"))).ToArray();
            if (stats.Length == 0) continue;
            var id = cards.Count;
            var rows = new List<WidgetElement> { Text(title, "group.heading." + id, "pob-eyebrow pob-inset " + color) };
            foreach (var stat in stats)
            {
                var key = S(stat, "name");
                var label = ShortStatLabel(key); var value = DisplayStatValue(key, stat.GetProperty("value").GetDouble());
                rows.Add(UI.ActionSurface("stat." + key, "pob.stat." + key, label + " " + value, ActionSurfaceOrientation.Horizontal,
                    Text(label, "stat.label." + key, "pob-stat-label pob-inset-muted"), Text(value, "stat.value." + key, "pob-stat-value pob-inset " + StatColor(key)))
                    .Selected(detailKind == "stat" && statDetail == key).Classes("pob-stat-row"));
            }
            cards.Add(UI.Stack("pob.group." + id, rows.ToArray()).Classes("pob-card").InGrid(row: id / 2, column: id % 2));
        }
        var grid = UI.Grid("pob.statgrid", Enumerable.Range(0, (cards.Count + 1) / 2).Select(_ => GridTrack.Auto()).ToArray(), [GridTrack.Star(), GridTrack.Star()], cards.ToArray()).Spacing(12, 12);
        var notes = new List<WidgetElement> { Text(detailKind == "stat" ? StatLabel(statDetail) : "Calculation setup", "inspector.title", "pob-heading") };
        notes.Add(Button("Detailed calculations and sources","calculations"));
        var showSkillContext=detailKind!="stat"||statDetail is "CombinedDPS" or "TotalDPS";
        if(showSkillContext)
        {
            var skills=A(State,"skills");
            if(skills.Length>0)notes.Add(SelectWithUnavailable("Main skill group",skills.Select(s=>new SelectOption("group"+I(s,"id"),InlineLabel(S(s,"title"),"Group "+I(s,"id")),"main-group."+I(s,"id"),B(s,"main"))).ToArray(),"pob.main-group")
                .Disabled(pendingBefore is not null).Classes("pob-select"));
            if(!string.IsNullOrWhiteSpace(S(State,"mainSkillDisabledReason")))
                notes.Add(Text(S(State,"mainSkill")+": "+S(State,"mainSkillDisabledReason"),"main-skill.warning","pob-life"));
            else if(S(State,"mainSkillDamageKind") is "buff" or "minion")
            {
                notes.Add(Text(S(State,"mainSkillDamageKind")=="buff"?"This selection supplies a buff rather than direct damage. Select an attack or a damaging component to inspect its damage.":"This is a minion skill. Inspect the Minions statistics for its damage.","main-skill.explanation","pob-muted"));
                if(S(State,"mainSkillDescription").Length>0)AddFormatted(notes,State,"mainSkillDescription","mainSkillDescriptionRuns","main-skill.description",emphasizeStats:true);
            }
        }
        if((detailKind!="stat"||statDetail=="FullDPS")&&I(State,"fullDpsIncludedGroups")==0)
            notes.Add(Text("Full DPS is not configured. Include groups from Skills to calculate their combined total.","full-dps.explanation","pob-muted"));
        if (detailKind == "stat")
        {
            if(statDetail=="FullDPS")
            {
                foreach(var (skill,index) in A(State,"fullDpsContributions").Select((s,i)=>(s,i)))
                {
                    var count=skill.GetProperty("count").GetDouble();
                    notes.Add(UI.RichText("pob.contribution."+index,
                        Text(S(skill,"name")+(count!=1?$" ×{count}":"")+": ","contribution.name."+index,"pob-green"),
                        Text(StatValue("FullDPS",skill.GetProperty("dps").GetDouble()*count),"contribution.value."+index)));
                    if(S(skill,"source").Length>0)notes.Add(Text("From "+S(skill,"source"),"contribution.source."+index,"pob-muted"));
                }
            }
            var breakdown = A(State, "breakdowns").FirstOrDefault(b => S(b, "name") == statDetail);
            if(breakdown.ValueKind==JsonValueKind.Object)
            {
                // PoB's Calcs tab can select a different skill and buff mode from
                // the sidebar. Never present its breakdown as the headline total.
                var context=State.GetProperty("breakdownContext");
                var overview=A(State,"stats").FirstOrDefault(s=>S(s,"name")==statDetail);
                var different=breakdown.TryGetProperty("value",out var detailValue)&&detailValue.TryGetDouble(out var calculated)&&
                    overview.ValueKind==JsonValueKind.Object&&overview.TryGetProperty("value",out var overviewValue)&&overviewValue.TryGetDouble(out var total)&&Math.Abs(calculated-total)>1e-6;
                if(showSkillContext||statDetail.StartsWith("Minion",StringComparison.Ordinal)||different)
                {
                    notes.Add(Text("Calculation settings", "breakdown.context.heading", "pob-small"));
                    var mode=System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(S(context,"mode").ToLowerInvariant());
                    notes.Add(Text("Skill: "+S(context,"skill")+(mode.Length>0?" · Mode: "+mode:""),"breakdown.context", "pob-muted"));
                    if(different)notes.Add(Text("This breakdown uses different calculation settings from the overview, so its total differs.","breakdown.context.difference","pob-muted"));
                }
                if(breakdown.TryGetProperty("value",out var value)&&value.TryGetDouble(out var number))
                    notes.Add(Text(StatLabel(statDetail)+": "+StatValue(statDetail,number),"breakdown.value"));
                if(S(breakdown,"text").Length>0)AddFormatted(notes,breakdown,"text","runs","inspector.breakdown",emphasizeStats:true);
                AddBreakdownRows(notes,breakdown);
            }
            else if(statDetail!="FullDPS")notes.Add(Text("PoB provides no textual breakdown for this total.","inspector.breakdown","pob-muted"));
        }
        else
        {
            notes.Add(Text("Select any statistic to inspect its calculation here.", "overview.help", "pob-muted"));
            foreach (var (field, title) in new[] { ("trees", "PASSIVE TREE"), ("skillSets", "SKILLS"), ("itemSets", "EQUIPMENT"), ("configSets", "CONDITIONS") })
            {
                var active = A(State, field).FirstOrDefault(s => B(s, "active"));
                var setup=new List<WidgetElement>{Text(title,"active.label."+field,"pob-eyebrow")};
                var kind=field switch{"trees"=>"tree","skillSets"=>"skills","itemSets"=>"items",_=>"config"};
                if(A(State,field).Length>1)AddSet(setup,field,kind);
                else setup.Add(Text(S(active,"title","Default"),"active.value."+field));
                notes.Add(UI.Stack("pob.active."+field,setup.ToArray()).Classes("pob-context-card"));
            }
        }
        if (compatibilityNotice.Length > 0) notes.Add(Text(compatibilityNotice, "compatibility", "pob-muted"));
        if(UnifiedWarningCount()>0)notes.Add(UnifiedWarningSummary());
        return Split("overview", UI.VerticalScroll("pob.scroll", grid).Classes("pob-scroll"), Panel("inspector", notes), 2, 1);
    }

    private WidgetElement InventoryWorkspace()
    {
        var skills = section == "Skills";
        var entries = A(State, skills ? "skills" : "items");
        if(skills)entries=entries.OrderByDescending(entry=>B(entry,"main")).ToArray();
        var kind = skills ? "skill" : "item";
        if (entries.Length > 0 && ((detailKind != kind && !(skills && detailKind is "skill-manage" or "gem-picker" or "skill-options")) || !entries.Any(e => I(e, "id") == detailId)))
        { detailKind = kind; detailId = selections.TryGetValue(kind, out var remembered) && entries.Any(e => I(e, "id") == remembered) ? remembered : I(entries[0], "id"); }
        var header = new List<WidgetElement> { Text(skills ? "Skill groups" : "Equipped items", "list.heading", "pob-eyebrow") };
        AddSet(header, skills ? "skillSets" : "itemSets", skills ? "skills" : "items");
        if(!skills)foreach(var (rune,i) in A(State,"characterRunes").Select((r,i)=>(r,i)))header.Add(Button(S(rune,"title")+" "+Plain(S(A(rune,"options").FirstOrDefault(o=>B(o,"active")),"title","Choose rune")),"character-rune."+i));
        if(skills)header.Add(UI.Row("pob.group-create.actions",Button("New group","group-create"),Button("Paste group","group-paste")).Classes("pob-toolbar"));
        else header.Add(UI.Row("pob.inventory.actions",Button("Item inventory","inventory"),Button(pastedItem is null?"Craft item":"Continue item draft","item-create"),Button("Unique library","item-uniques").Disabled(pastedItem is not null)).Classes("pob-toolbar"));
        var list = new List<WidgetElement>();
        foreach (var entry in entries)
        {
            var id = I(entry, "id");
            var title = skills ? S(entry, "title") : S(entry, "name", S(entry, "base"));
            if(skills && string.IsNullOrWhiteSpace(title))title="Unnamed group";
            var subtitle = skills ? $"{A(entry, "gems").Length} gems" + (B(entry, "main") ? " · Main skill (pinned)" : "") + (B(entry,"fullDps")?" · Full DPS":"") + (B(entry, "enabled") ? "" : " · Disabled") : S(entry, "slot");
            if(skills&&B(entry,"generated"))subtitle="Granted skill"+(S(entry,"slot").Length>0?" · "+S(entry,"slot"):"")+(B(entry,"main")?" · Main skill (pinned)":"")+(B(entry,"fullDps")?" · Full DPS":"")+(B(entry,"enabled")?"":" · Disabled");
            if(id==detailId)subtitle+=" · Inspecting";
            list.Add(UI.ActionSurface(kind + "." + id, "pob." + kind + "." + id, title + ", " + subtitle, ActionSurfaceOrientation.Vertical,
                Text(Limit(title, 160), kind + ".title." + id, "pob-list-title"),
                Text(subtitle, kind + ".subtitle." + id, "pob-small"))
                .Selected(id == detailId).FocusRight("pob.detail").Classes("pob-list-item").PresentOnFocus(kind+"."+id));
        }
        if (entries.Length == 0) list.Add(Text("No entries in this set.", "empty", "pob-muted"));
        var returnId=entries.Length>0?"pob."+kind+"."+detailId:"pob.tab."+(skills?2:3);
        ContainerElement detailPanel;
        if(skills)detailPanel=UI.Stack("pob.detail");
        else
        {
            List<WidgetElement> details=entries.Length>0?ItemDetails(entries.First(e=>I(e,"id")==detailId)):[Text("Import a build to see its items.","detail.empty","pob-muted")];
            var action=pastedItem is null?"item-paste":"item-resume";
            detailPanel=UI.Stack("pob.detail",
                Text("Item details","detail.heading","pob-eyebrow"),
                UI.Grid("pob.item.tools",[GridTrack.Auto(),GridTrack.Auto()],[GridTrack.Star(),GridTrack.Star(),GridTrack.Star()],
                    Button(pastedItem is null?"Replace from clipboard":"Continue comparison",action).FocusLeft(returnId).FocusRight("pob.item-manage").FocusDown("pob.trade").Disabled(pendingBefore is not null).InGrid(columnSpan:2),
                    Button("Item settings","item-manage").FocusLeft("pob."+action).FocusDown("pob.trade-browse").Disabled(entries.Length==0).InGrid(column:2),
                    Button("Find upgrades","trade").FocusLeft(returnId).FocusRight("pob.similar-start.equipped").FocusUp("pob."+action).InGrid(row:1),
                    SimilarEntry("equipped").InGrid(row:1,column:1),
                    BrowseTradeButton().FocusLeft("pob.similar-start.equipped").FocusUp("pob.item-manage").InGrid(row:1,column:2)).Spacing(8,8),
                UI.VerticalScroll("pob.detail.scroll",details.Select(row=>LinkLeft(row,returnId)).ToArray()).Classes("pob-scroll"))
                .RememberChildFocus("pob."+action).Classes("pob-panel","pob-tree-main");
        }
        if(skills&&entries.Length==0)
            detailPanel=UI.Stack("pob.detail",Text("No skill groups in this set.","skill-empty.title","pob-heading"),
                Text("Choose New group to add a skill and its supports, or paste a group copied from desktop PoB.","skill-empty.help","pob-muted")).Classes("pob-panel");
        if(skills&&entries.Length>0)
        {
            var selected=entries.First(e=>I(e,"id")==detailId);
            detailPanel=detailKind switch
            {
                "skill-manage"=>UI.Stack("pob.detail",SkillManagementWorkspace()).RememberChildFocus("pob.group-name").Classes("pob-panel","pob-tree-main"),
                "gem-picker"=>UI.Stack("pob.detail",GemPickerWorkspace()).RememberChildFocus("pob.gem-search").Classes("pob-panel","pob-tree-main"),
                "skill-options"=>UI.Stack("pob.detail",UI.VerticalScroll("pob.skill-options.content",SkillOptionsContent().ToArray()).Classes("pob-scroll")).RememberChildFocus("pob.back").Classes("pob-panel","pob-tree-main"),
                _=>SkillWorkspaceDetails(selected,returnId)
            };
        }
        var sets=A(State,skills?"skillSets":"itemSets").Length>1;
        var quick=NeedsQuickControls(entries.Length);
        ContainerElement tools=UI.Stack("pob.inventory.tools",header.ToArray()).Classes("pob-buildtools");
        ContainerElement rows=UI.VerticalScroll("pob.inventory.rows",list.ToArray()).Classes("pob-scroll");
        tools=QuickControls(tools,skills?(sets?"pob.set.skills":"pob.group-create"):"pob.inventory","pob.inventory.rows",skills?"Skill groups":"Equipment",quick);
        if(entries.Length>0)rows=QuickControls(rows,"pob."+kind+"."+I(entries[0],"id"),"pob.inventory.tools",skills?"Group controls":"Equipment controls",quick);
        return Split("inventory",UI.Stack("pob.list",tools,WithRowPreviews(rows,skills)).Classes("pob-panel","pob-tree-main"), detailPanel, 1, 1.55);
    }

    private static WidgetElement LinkLeft(WidgetElement element,string target)=>element switch
    {
        ButtonElement button=>button.FocusLeft(target),
        ActionSurfaceElement action=>action.FocusLeft(target),
        TextEntryElement field=>field.FocusLeft(target),
        SelectElement select=>select.FocusLeft(target),
        ContainerElement container=>container with {Children=container.Children.Select(child=>LinkLeft(child,target)).ToArray()},
        _=>element,
    };

    private List<WidgetElement> ItemDetails(JsonElement item,string prefix="item")
    {
        var rarity = S(item,"rarity");
        var css = rarity is "UNIQUE" or "RELIC" ? "pob-orange" : rarity == "RARE" ? "pob-gold" : rarity == "MAGIC" ? "pob-blue" : "pob-text";
        var rows = new List<WidgetElement> { Text(S(item, "slot").ToUpperInvariant(), prefix+".slot", "pob-eyebrow"), Text(S(item, "name"), prefix+".name", "pob-heading " + css) };
        foreach (var (block, index) in A(item,"details").Select((b,i)=>(b,i)))
        {
            var lines=new List<WidgetElement>();
            foreach(var (line,i) in A(block,"lines").Select((line,i)=>(line,i)))
                AddFormatted(lines,line,"text","runs",prefix+".copy."+index+"."+i);
            rows.Add(UI.Stack("pob."+prefix+".block."+index,lines.ToArray()).Classes("pob-context-card"));
        }
        return rows;
    }

    private WidgetElement TreeWorkspace()
    {
        if(detailKind=="node"&&!A(State,"nodes").Any(n=>I(n,"id")==detailId))
        {detailKind="";detailId=0;attributeChoices=null;passiveChoicePopup=null;}
        var main = new List<WidgetElement>();
        AddSet(main, "trees", "tree");
        if(Capabilities.TimelessFinder)main.Add(Button("Find a Timeless Jewel…","timeless-open"));
        if(Capabilities.WeaponTreeModes)main.Add(TreeAllocationControl());
        if (mapSession is not null) main.Add(UI.WebView(mapSession.Reference, "pob.map").Classes("pob-map"));
        else
        {
            main.Add(Text("PASSIVE TREE", "tree.heading", "pob-eyebrow pob-blue"));
            main.Add(Text("Explore your allocation", "tree.title", "pob-title"));
            main.Add(Text("Inspect a node beside the map, preview an allocation path, and compare the change before saving.", "tree.help", "pob-muted"));
            main.Add(Button("Load interactive tree", "map"));
        }
        var side = new List<WidgetElement>();
        if (detailKind == "node" && A(State, "nodes").Any(n => I(n, "id") == detailId))
        {
            side = NodeDetails(); side[0] = pendingBefore is null ? Button("Search nodes", "back") : Text("Tree preview", "tree.preview.heading", "pob-eyebrow");
        }
        else
        {
            var list=PassiveList();
            var quick=NeedsQuickControls(passiveQuery.Entries.Length);
            side.Add(QuickControls(UI.Stack("pob.tree.search-controls",
                Text("Find a passive", "tree.searchtitle", "pob-eyebrow"),
                SearchField(UI.TextEntry(filter, "Search all passives", "search", "pob.search"), result!,
                    () => A(State,"nodes").Where(n=>B(n,"interactive")).Select(n =>
                        (S(n,"name"), S(n,"name")+" "+S(n,"description"), S(n,"description")))).Classes("pob-input"),
                Text("Search by name or modifier, including unallocated nodes.","tree.searchhint","pob-small pob-muted")).Classes("pob-buildtools"),
                "pob.search","pob.tree.results","Passives",quick));
            side.Add(Text($"{(filter.Length==0?"Allocated and granted passives":"Search results")} · {passiveQuery.NodeCount} nodes","tree.count","pob-muted"));
            side.Add(QuickControls(UI.Stack("pob.tree.results",list).Classes("pob-workspace"),
                "pob.passives","pob.tree.search-controls","Search",quick));
        }
        var node=A(State,"nodes").FirstOrDefault(n=>I(n,"id")==detailId);
        var entry=detailKind=="node" ? node.ValueKind==JsonValueKind.Object && (B(node,"canAllocate")||B(node,"canRefund")) ? "pob.node-change" : "pob.back" : "pob.search";
        WidgetElement sidebar;
        if(pendingBefore is not null && detailKind=="node")
        {
            side.RemoveAt(0);
            var quick=NeedsQuickControls(side.Count(row=>row.Id.StartsWith("pob.changed.",StringComparison.Ordinal)&&row.Id.EndsWith(".card",StringComparison.Ordinal)));
            ContainerElement previewDetails=UI.VerticalScroll("pob.tree.preview-details",side.ToArray()).Classes("pob-scroll");
            if(side.Any(row=>row.Id=="pob.preview-nodes"))
                previewDetails=QuickControls(previewDetails,"pob.preview-nodes","pob.tree.preview.actions","Preview actions",quick,preview:true);
            sidebar=UI.Stack("pob.tree.side",
                QuickControls(UI.Stack("pob.tree.preview.actions",Text("PREVIEW · not applied","tree.preview.heading","pob-eyebrow pob-gold"),
                UI.Button("Apply change","apply","pob.apply").FocusDown("pob.cancel").FocusLeft("pob.map").Classes("pob-button"),
                UI.Button("Discard","cancel","pob.cancel").FocusUp("pob.apply").FocusLeft("pob.map").Classes("pob-button")).Classes("pob-buildtools"),
                    "pob.apply","pob.tree.preview-details","Changed nodes",quick,preview:true),
                previewDetails)
                .RememberChildFocus("pob.apply").Classes("pob-panel","pob-tree-main");
        }
        else if(detailKind=="node")sidebar=UI.VerticalScroll("pob.tree.side",side.ToArray()).RememberChildFocus(entry).Classes("pob-scroll","pob-panel");
        else sidebar=UI.Stack("pob.tree.side",side.ToArray()).RememberChildFocus(entry).Classes("pob-tree-main","pob-panel");
        return Split("tree", UI.Stack("pob.tree.main", main.ToArray()).Classes("pob-panel", "pob-tree-main"), sidebar, 3, 1);
    }

    private static WidgetElement Panel(string id, List<WidgetElement> children) => UI.VerticalScroll("pob." + id, children.ToArray()).Classes("pob-scroll", "pob-panel");
    private static WidgetElement Split(string id, WidgetElement left, WidgetElement right, double leftWeight, double rightWeight) =>
        UI.Grid("pob.workspace." + id, [GridTrack.Star()], [GridTrack.Star(leftWeight), GridTrack.Star(rightWeight)], left, right.InGrid(column: 1)).Spacing(0, 14).Classes("pob-workspace");
    private static string StatColor(string key) => key.Contains("Life", StringComparison.Ordinal) || key.StartsWith("Fire", StringComparison.Ordinal) ? "pob-life" : key.Contains("Mana", StringComparison.Ordinal) || key.Contains("EnergyShield", StringComparison.Ordinal) || key.StartsWith("Cold", StringComparison.Ordinal) ? "pob-blue" : key.StartsWith("Chaos", StringComparison.Ordinal) ? "pob-violet" : key.Contains("DPS", StringComparison.Ordinal) || key.StartsWith("Lightning", StringComparison.Ordinal) ? "pob-gold" : "pob-green";
    private static string StatValue(string key, double number) => number.ToString(Math.Abs(number) >= 1000 ? "N0" : "0.#", System.Globalization.CultureInfo.CurrentCulture) + (key.EndsWith("Resist", StringComparison.Ordinal) ? "%" : "");
    private string DisplayStatValue(string key,double number)=>
        (key is "CombinedDPS" or "TotalDPS" && (B(State,"mainSkillDisabled")||S(State,"mainSkillDamageKind") is "buff" or "minion")) || (key=="FullDPS"&&I(State,"fullDpsIncludedGroups")==0)?"—":StatValue(key,number);
    private string ShortStatLabel(string key) => key switch
    {
        "CombinedDPS" => B(State,"mainSkillShowsAverage")?"Average damage":"Main skill DPS", "FullDPS" => "Full DPS (included groups)", "TotalDPS" => "Hit damage / second",
        "PhysicalMaximumHitTaken" => "Physical", "FireMaximumHitTaken" => "Fire", "ColdMaximumHitTaken" => "Cold", "LightningMaximumHitTaken" => "Lightning", "ChaosMaximumHitTaken" => "Chaos",
        "FireResist" => "Fire", "ColdResist" => "Cold", "LightningResist" => "Lightning", "ChaosResist" => "Chaos",
        _ => StatLabel(key),
    };
}
