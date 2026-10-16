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
            var choices = loadouts.Select((l, i) => new SelectOption("loadout" + i, S(l, "title"), "loadout." + i, B(l, "active"))).ToList();
            if (!choices.Any(c => c.IsSelected)) choices.Insert(0, new("custom", "Independent sets", "noop", true));
            controls.Add(UI.Select("Loadout", choices, "pob.loadout").Disabled(pendingBefore is not null).Classes("pob-select", "pob-loadout"));
        }
        controls.Add(UI.Button("Build & import", "menu", "pob.menu").Disabled(pendingBefore is not null).Classes("pob-button"));
        var history=UI.Row("pob.history",
            UI.Button("Undo","undo","pob.undo").Disabled(undo.Count==0||pendingBefore is not null).Classes("pob-button","pob-history-button"),
            UI.Button("Redo","redo","pob.redo").Disabled(redo.Count==0||pendingBefore is not null).Classes("pob-button","pob-history-button"))
            .RememberChildFocus(undo.Count>0?"pob.undo":"pob.redo").Classes("pob-toolbar");
        var heading = new List<WidgetElement>();
        if (section != "Tree") heading.Add(Text($"POE {game}  /  BUILD COMPANION", "eyebrow", "pob-eyebrow"));
        heading.Add(Text(BuildName(), "title", section == "Tree" ? "pob-tree-title" : "pob-title"));
        heading.Add(Text($"{identity}  ·  {(B(State,"autoLevel")?"Auto level":"Level")} {S(State, "level")}  ·  Tree {S(State, "treeVersion").Replace('_', '.')}", "context", "pob-muted"));
        return UI.Grid("pob.header", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Auto()],
            UI.Stack("pob.identity", heading.ToArray()),
            UI.Stack("pob.buildtools",UI.Row("pob.buildcontrols", controls.ToArray()).Classes("pob-toolbar"),history)
                .Classes("pob-buildtools").InGrid(column: 1)).Spacing(0, 20);
    }

    private WidgetElement Navigation()
    {
        var tabs = Sections.Select((s, i) => (WidgetElement)UI.Button(s, "section." + i, i == 0 ? "pob.section" : "pob.tab." + i)
            .Selected(section == s).Disabled(pendingBefore is not null).Classes("pob-tab").InGrid(column: i + 1)).ToList();
        tabs.Insert(0, UI.ControllerGlyph(ControllerButton.LeftBumper, "pob.navigation.previous", "Previous section")
            .Classes("pob-section-hint").InGrid(column: 0));
        tabs.Add(UI.ControllerGlyph(ControllerButton.RightBumper, "pob.navigation.next", "Next section")
            .Classes("pob-section-hint").InGrid(column: Sections.Length + 1));
        return UI.Grid("pob.navigation", [GridTrack.Auto()],
            [GridTrack.Auto(), .. Sections.Select(s => GridTrack.Star(s == "Configuration" ? 1.5 : 1)), GridTrack.Auto()],
            tabs.ToArray()).Spacing(0, 5).Classes("pob-tabs");
    }

    private WidgetElement Headlines()
    {
        string[] keys = ["CombinedDPS", "Life", "EnergyShield", "Mana"];
        return UI.Grid("pob.headlines", [GridTrack.Auto()], keys.Select(_ => GridTrack.Star()).ToArray(),
            keys.Select((key, i) => StatCard(key, i).InGrid(column: i)).ToArray()).Spacing(0, 10);
    }

    private WidgetElement CompactHeadlines()
    {
        var values = new List<WidgetElement>();
        foreach(var key in new[]{"CombinedDPS","Life","EnergyShield","Mana"})
        {
            var stat=A(State,"stats").FirstOrDefault(s=>S(s,"name")==key);
            if(stat.ValueKind!=JsonValueKind.Object)continue;
            var label=key=="CombinedDPS"?S(State,"mainSkill","Main skill")+" DPS":StatLabel(key);
            values.Add(Text(label+"  "+DisplayStatValue(key,stat.GetProperty("value").GetDouble()),"compact."+key,"pob-compact-value "+StatColor(key)));
        }
        values.Add(Text($"Passives {S(State,"passivePoints")}  |  Ascendancy {S(State,"ascendancyPoints")}","compact.points","pob-muted"));
        return UI.Row("pob.headlines",values.ToArray()).Classes("pob-compact-stats");
    }

    private WidgetElement StatCard(string key, int index)
    {
        var value = A(State, "stats").FirstOrDefault(s => S(s, "name") == key);
        var caption = key == "CombinedDPS" ? "Main skill DPS" : StatLabel(key);
        var number = value.ValueKind == JsonValueKind.Object ? DisplayStatValue(key, value.GetProperty("value").GetDouble()) : "—";
        return UI.ActionSurface("stat." + key, "pob.headline." + index, caption + " " + number, ActionSurfaceOrientation.Vertical,
            Text(caption, "headline.label." + index, "pob-eyebrow pob-inset-muted"),
            Text(number, "headline.value." + index, "pob-number pob-inset " + StatColor(key)),
            Text(Delta(key), "headline.delta." + index, "pob-small pob-inset-muted"))
            .Classes("pob-metric");
    }

    private string Delta(string key)
    {
        if (pendingBefore is null) return key == "CombinedDPS" ? S(State,"mainSkill","No main skill")+(B(State,"mainSkillDisabled")?" · Skill disabled":S(State,"mainSkillDamageKind")=="buff"?" · No direct damage":S(State,"mainSkillDamageKind")=="minion"?" · Minion skill":"") : "Current loadout";
        if(key=="CombinedDPS"&&(B(State,"mainSkillDisabled")||B(pendingBefore.State,"mainSkillDisabled")))
            return B(State,"mainSkillDisabled")?"Skill disabled · see calculation setup":"Skill is now usable";
        var before = A(pendingBefore.State, "stats").FirstOrDefault(s => S(s, "name") == key);
        var after = A(State, "stats").FirstOrDefault(s => S(s, "name") == key);
        if (before.ValueKind != JsonValueKind.Object || after.ValueKind != JsonValueKind.Object) return "Not available";
        var old = before.GetProperty("value").GetDouble(); var next = after.GetProperty("value").GetDouble();
        var change = next - old;
        return Math.Abs(change) < .01 ? "Unchanged" : $"{StatValue(key, old)} → {StatValue(key, next)} ({(change > 0 ? "+" : "")}{StatValue(key, change)})";
    }

    private WidgetElement Workspace()
    {
        if(detailKind=="library")return LibraryWorkspace();
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
        var showSkillContext=detailKind!="stat"||statDetail is "CombinedDPS" or "TotalDPS";
        if(showSkillContext)
        {
            var skills=A(State,"skills");
            if(skills.Length>0)notes.Add(UI.Select("Main skill group",skills.Select(s=>new SelectOption("group"+I(s,"id"),S(s,"title"),"main-group."+I(s,"id"),B(s,"main"))).ToArray(),"pob.main-group")
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
                notes.Add(Text("Saved desktop Calcs context", "breakdown.context.heading", "pob-eyebrow pob-gold"));
                notes.Add(Text(S(context,"skill")+" · "+S(context,"mode"),"breakdown.context", "pob-muted"));
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
        var warnings = A(State, "warnings");
        if (warnings.Length > 0) notes.Add(Text("Check this build", "warnings.heading", "pob-eyebrow pob-gold"));
        foreach (var (warning, index) in warnings.Select((w, i) => (w, i))) notes.Add(Text(warning.GetString()!, "warning." + index, "pob-muted"));
        return Split("overview", UI.VerticalScroll("pob.scroll", grid).Classes("pob-scroll"), Panel("inspector", notes), 2, 1);
    }

    private WidgetElement InventoryWorkspace()
    {
        var skills = section == "Skills";
        var entries = A(State, skills ? "skills" : "items");
        var kind = skills ? "skill" : "item";
        if (entries.Length > 0 && (detailKind != kind || !entries.Any(e => I(e, "id") == detailId)))
        { detailKind = kind; detailId = selections.TryGetValue(kind, out var remembered) && entries.Any(e => I(e, "id") == remembered) ? remembered : I(entries[0], "id"); }
        var list = new List<WidgetElement> { Text(skills ? "Skill groups" : "Equipped items", "list.heading", "pob-eyebrow") };
        AddSet(list, skills ? "skillSets" : "itemSets", skills ? "skills" : "items");
        foreach (var entry in entries)
        {
            var id = I(entry, "id");
            var title = skills ? S(entry, "title") : S(entry, "name", S(entry, "base"));
            var subtitle = skills ? $"{A(entry, "gems").Length} gems" + (B(entry, "main") ? " · Main skill" : "") + (B(entry,"fullDps")?" · Full DPS":"") + (B(entry, "enabled") ? "" : " · Disabled") : S(entry, "slot");
            if(id==detailId)subtitle+=" · Inspecting";
            list.Add(UI.ActionSurface(kind + "." + id, "pob." + kind + "." + id, title + ", " + subtitle, ActionSurfaceOrientation.Vertical,
                Text(Limit(title, 160), kind + ".title." + id, "pob-list-title"),
                Text(subtitle, kind + ".subtitle." + id, "pob-small"))
                .Selected(id == detailId).FocusRight("pob.detail").Classes("pob-list-item"));
        }
        if (entries.Length == 0) list.Add(Text("No entries in this set.", "empty", "pob-muted"));
        var details = new List<WidgetElement>();
        if (entries.Length > 0)
        {
            details = skills ? SkillDetails() : ItemDetails(entries.First(e => I(e, "id") == detailId));
            if (skills) details.RemoveAt(0); // Inline inspection keeps the list visible; no Back row.
        }
        else details.Add(Text("Import a build to see its " + section.ToLowerInvariant() + ".", "detail.empty", "pob-muted"));
        var returnId=entries.Length>0?"pob."+kind+"."+detailId:"pob.tab."+(skills?2:3);
        var action=skills?"skill-options":pastedItem is null?"item-paste":"item-resume";
        var detailPanel=UI.Stack("pob.detail",
            Text(skills ? "Skill details" : "Item details", "detail.heading", "pob-eyebrow"),
            UI.Button(skills?"Selected skill options":pastedItem is null?"Replace from clipboard":"Continue item comparison",action,"pob."+action)
                .FocusLeft(returnId).Disabled(pendingBefore is not null).Classes("pob-button"),
            UI.VerticalScroll("pob.detail.scroll",details.Select(row=>LinkLeft(row,returnId)).ToArray()).Classes("pob-scroll"))
            .RememberChildFocus("pob."+action).Classes("pob-panel","pob-tree-main");
        return Split("inventory", Panel("list", list), detailPanel, 1, 1.55);
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

    private List<WidgetElement> ItemDetails(JsonElement item)
    {
        var rarity = S(item,"rarity");
        var css = rarity is "UNIQUE" or "RELIC" ? "pob-orange" : rarity == "RARE" ? "pob-gold" : rarity == "MAGIC" ? "pob-blue" : "pob-text";
        var rows = new List<WidgetElement> { Text(S(item, "slot").ToUpperInvariant(), "item.slot", "pob-eyebrow"), Text(S(item, "name"), "item.name", "pob-heading " + css) };
        foreach (var (block, index) in A(item,"details").Select((b,i)=>(b,i)))
        {
            var lines=new List<WidgetElement>();
            foreach(var (line,i) in A(block,"lines").Select((line,i)=>(line,i)))
                AddFormatted(lines,line,"text","runs","item.copy."+index+"."+i);
            rows.Add(UI.Stack("pob.item.block."+index,lines.ToArray()).Classes("pob-context-card"));
        }
        return rows;
    }

    private WidgetElement TreeWorkspace()
    {
        var main = new List<WidgetElement>();
        AddSet(main, "trees", "tree");
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
            side.Add(Text("Find a passive", "tree.searchtitle", "pob-eyebrow"));
            side.Add(UI.TextEntry(filter, "Search passives", "search", "pob.search").Classes("pob-input"));
            var list=PassiveList();
            side.Add(Text($"{passiveQuery.Entries.Length} passives","tree.count","pob-muted"));
            side.Add(list);
        }
        var node=A(State,"nodes").FirstOrDefault(n=>I(n,"id")==detailId);
        var entry=detailKind=="node" ? node.ValueKind==JsonValueKind.Object && (B(node,"canAllocate")||B(node,"canRefund")) ? "pob.node-change" : "pob.back" : "pob.search";
        WidgetElement sidebar;
        if(pendingBefore is not null && detailKind=="node")
        {
            side.RemoveAt(0);
            sidebar=UI.Stack("pob.tree.side",
                Text("PREVIEW · not applied","tree.preview.heading","pob-eyebrow pob-gold"),
                UI.Button("Apply change","apply","pob.apply").FocusDown("pob.cancel").FocusLeft("pob.map").Classes("pob-button"),
                UI.Button("Discard","cancel","pob.cancel").FocusUp("pob.apply").FocusLeft("pob.map").Classes("pob-button"),
                UI.VerticalScroll("pob.tree.preview-details",side.ToArray()).Classes("pob-scroll"))
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
    private static string ShortStatLabel(string key) => key switch
    {
        "CombinedDPS" => "Main skill", "FullDPS" => "Full DPS (included groups)", "TotalDPS" => "Hit damage / second",
        "PhysicalMaximumHitTaken" => "Physical", "FireMaximumHitTaken" => "Fire", "ColdMaximumHitTaken" => "Cold", "LightningMaximumHitTaken" => "Lightning", "ChaosMaximumHitTaken" => "Chaos",
        "FireResist" => "Fire", "ColdResist" => "Cold", "LightningResist" => "Lightning", "ChaosResist" => "Chaos",
        _ => StatLabel(key),
    };
}
