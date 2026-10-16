using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget : Widget
{
    private readonly object gate = new();
    private readonly int game;
    private readonly string dataDirectory;
    private readonly PobEngine engine;
    private readonly Func<string> readClipboard = ClipboardText.Read;
    private PobResult? result, pendingBefore;
    private readonly Stack<string> undo = new();
    private string? documentId;
    private (string Id,string Name)[] savedBuilds = [];
    private string? pastedItem;
    private string section = "Calcs", filter = "", status = "", detailKind = "";
    private int page, detailId;
    private string statDetail = "";
    private string compatibilityNotice = "";
    private bool busy, activated;
    private bool nodeReturnToMap;
    private readonly Dictionary<string,int> selections = new();
    private long treeFocusRequest;
    private static readonly string[] Sections = ["Calcs", "Tree", "Skills", "Items", "Configuration", "Notes"];
    private const int PageSize = 9;
    public PobWidget(int game, string payloadDirectory, string? dataDirectory = null)
    {
        this.game = game;
        this.dataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "applications", "pob" + game);
        engine = new(Path.Combine(payloadDirectory, "pob-engine"), Path.Combine(payloadDirectory, "runtime"), Path.Combine(payloadDirectory, "Engine", "bridge.lua"));
    }
    public PobWidget(int game, PobEngine engine, string dataDirectory, Func<string>? readClipboard = null) { this.game = game; this.engine = engine; this.dataDirectory = dataDirectory; this.readClipboard = readClipboard ?? ClipboardText.Read; }
    private JsonElement State => result?.State ?? default;
    private static string S(JsonElement e, string key, string fallback = "") => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString() : fallback;
    private static int I(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static bool B(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static JsonElement[] A(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToArray() : [];
    private static string Limit(string text, int length = 3000) => text.Length <= length ? text : text[..length] + "…";
    private static WidgetElement Text(string text, string id, string css = "pob-text") => UI.Text(Limit(text), "pob." + id).Classes(css.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static WidgetElement Button(string text, string action, string? id = null) => UI.Button(Limit(text, 180), action, "pob." + (id ?? action)).Classes("pob-button");

    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        if (!activated)
        {
            activated = true;
            Work(async token =>
            {
                RefreshLibrary();
                var selected = Path.Combine(dataDirectory, "selected.txt");
                if (!File.Exists(selected)) return;
                var id = File.ReadAllText(selected).Trim();
                if (!Guid.TryParseExact(id, "N", out _)) return;
                var xml = await BuildDocument.ReadAsync(Path.Combine(dataDirectory, id + ".xml"), game, token);
                var loaded = await engine.ExecuteAsync(new { op = "load", xml, name = "Saved build" }, token);
                BuildDocument.VerifySkillPreservation(xml, loaded.Xml);
                lock (gate) { documentId = id; result = loaded; }
                EnsureBuildName(loaded);
            }, "Opening your saved build…");
        }
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        if (mapSession is not null) await mapSession.DisposeAsync();
        await engine.DisposeAsync();
    }

    private void Work(Func<CancellationToken, Task> operation, string message)
    {
        lock (gate) { if (busy) return; busy = true; status = message; }
        Invalidate();
        Operations.RunSingleFlight("pob.work", async context =>
        {
            try { await operation(context.CancellationToken); RefreshLibrary(); lock (gate) status = ""; }
            catch (OperationCanceledException) { lock (gate) status = "Operation cancelled."; }
            catch (Exception error) when (error is WidgetCapabilityException or IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or HttpRequestException)
            { lock (gate) status = error is System.ComponentModel.Win32Exception native ? $"The build engine could not start (Windows error {native.NativeErrorCode}). Reinstall this widget or retry." : Limit(error.Message, 350); }
            finally { lock (gate) { busy = false; } Invalidate(); }
        }, WidgetOperationLifetime.Widget);
    }

    public override WidgetView Render()
    {
        lock (gate)
        {
            var children = new List<WidgetElement>();
            if (!string.IsNullOrWhiteSpace(status)) children.Add(Text(status, "status", "pob-muted"));
            if (busy) children.Add(Button("Cancel operation", "cancel-operation"));
            if (result is null)
            {
                children.Add(Text($"PoE {game} Build Companion", "title", "pob-title"));
                children.Add(Text("Bring your Path of Building build into the overlay.", "intro"));
                children.Add(Button("Paste build from clipboard", "paste"));
                children.Add(UI.TextEntry("", "Build link or XML file path", "import", "pob.import").Classes("pob-button"));
                children.Add(Button("Create a new build", "new"));
                children.Add(Text("Calculations run locally. Imported originals are kept separately from your working copies. Not affiliated with Grinding Gear Games.", "notice", "pob-muted"));
                AddLibrary(children);
            }
            else
            {
                children.Add(BuildHeader());
                children.Add(section == "Tree" ? CompactHeadlines() : Headlines());
                children.Add(Navigation());
                if (pendingBefore is not null)
                    children.Add(UI.Row("pob.preview", Text("PREVIEW · not saved", "preview.label", "pob-eyebrow pob-gold"), Button("Apply change", "apply"), Button("Discard", "cancel")).Classes("pob-preview"));
                children.Add(Workspace());
                children.Add(Text("LB / RB  Switch section     ·     A  Inspect or change     ·     Working copy saved locally", "footer", "pob-footer"));
            }
            var scope = "pob.scope." + section.ToLowerInvariant() + "." + (pendingBefore is not null ? "preview" : detailKind.Length == 0 || InlineDetail ? "main" : detailKind);
            var activeTab = Array.IndexOf(Sections,section);
            var initial = result is null ? "pob.paste" : detailKind.Length > 0 && !InlineDetail ? "pob.back" : activeTab == 0 ? "pob.section" : "pob.tab."+activeTab;
            if (pendingBefore is not null) initial = "pob.apply";
            var root = UI.Stack("pob.root", children.ToArray()).Classes("pob-root").InputScope(scope).RememberChildFocus(initial);
            if (busy) root = root.Shortcut(ControllerButton.B, "cancel-operation");
            if (result is not null)
            {
                root = root.Shortcut(ControllerButton.LeftBumper, "previous-section").Shortcut(ControllerButton.RightBumper, "next-section");
                if (!busy && ((detailKind.Length != 0 && !InlineDetail) || pendingBefore is not null)) root = root.Shortcut(ControllerButton.B, pendingBefore is not null ? "cancel" : "back");
            }
            return new(root, initial, ActiveInputScopeId: scope,
                Surface: new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Standard, PreferredWidth = section == "Tree" ? 1320 : 1180, PreferredHeight = section == "Tree" ? 900 : 820, MinimumWidth = 820, MinimumHeight = 600 })
            {
                FocusGroupEntryRequest = section == "Tree" && pendingBefore is null && !busy && treeFocusRequest > 0 && (detailKind.Length == 0 || InlineDetail)
                    ? new() { RequestId = treeFocusRequest, GroupId = "pob.tree.side" } : null,
            };
        }
    }

    private void AddLibrary(List<WidgetElement> children)
    {
        if (savedBuilds.Length == 0) return;
        children.Add(Text("Saved builds", "library.heading", "pob-heading"));
        var pages = Math.Max(1,(savedBuilds.Length+PageSize-1)/PageSize);
        var selectedPage = Math.Clamp(page,0,pages-1);
        foreach (var build in savedBuilds.Skip(selectedPage*PageSize).Take(PageSize))
            children.Add(Button(build.Name + (build.Id==documentId ? " · Current" : ""), "saved." + build.Id));
        if (selectedPage>0) children.Add(Button("Previous builds","page-previous"));
        if (selectedPage+1<pages) children.Add(Button("More builds","page-next"));
    }

    private List<WidgetElement> Content()
    {
        var rows = new List<WidgetElement>();
        if (detailKind == "menu")
        {
            rows.Add(Button("Back", "back"));
            rows.Add(UI.TextEntry(BuildName(), "Build name", "rename", "pob.rename").Classes("pob-button"));
            rows.Add(Button("Paste another build", "paste"));
            rows.Add(UI.TextEntry("", "Build link or XML file path", "import", "pob.import").Classes("pob-button"));
            rows.Add(Button("Copy PoB share code", "export"));
            rows.Add(Button("Save XML beside working copy", "export-file"));
            if (undo.Count > 0) rows.Add(Button("Undo last change", "undo"));
            rows.Add(UI.TextEntry(S(State, "level"), "Character level", "level", "pob.level").Classes("pob-button"));
            foreach (var field in new[] { "classes", "ascendancies" })
            {
                var kind = field == "classes" ? "class" : "ascendancy";
                var options = A(State, field);
                if (options.Length > 0) rows.Add(UI.Select(kind,
                    options.Select(o => new SelectOption(kind + I(o,"id"), S(o,"title"), kind + "." + I(o,"id"), B(o,"active"))).ToArray(), "pob." + kind).Classes("pob-button"));
            }
            AddLibrary(rows); return rows;
        }
        if (detailKind == "custom") { rows.Add(Button("Back to conditions", "back")); AddTextPages(rows, S(State, "customMods", "No custom modifiers in this configuration.")); return rows; }
        if (detailKind == "choose-slot")
        {
            rows.Add(Button("Back to equipment", "back")); rows.Add(Text("Choose the slot to replace", "slot.heading", "pob-heading"));
            var slots = A(State, "slots"); AddPage(rows, slots, slot => Button(S(slot,"name"), "replace-slot." + Array.FindIndex(slots,s=>S(s,"name")==S(slot,"name")))); return rows;
        }
        if (detailKind == "skill-options")
        {
            rows.Add(Button("Back to skills","back")); rows.Add(Text(S(State,"mainSkill"),"mainSkill","pob-heading"));
            foreach(var option in A(State,"skillOptions"))
            {
                var choices=A(option,"options");if(choices.Length==0)continue;
                var selected=Array.FindIndex(choices,o=>B(o,"active"));if(selected<0)selected=0;
                var key=S(option,"id"); rows.Add(UI.Select(StatLabel(key.Replace("mainSkill","")),choices.Select((o,i)=>new SelectOption("choice"+i,S(o,"title"),"skill-option."+key+"."+I(o,"id"),i==selected)).ToArray(),"pob.option."+key).Classes("pob-button"));
            }
            if(rows.Count==2)rows.Add(Text("No additional choices for this skill.","no-options","pob-muted"));return rows;
        }
        switch (section)
        {
            case "Configuration":
                AddSet(rows,"configSets","config");
                rows.Add(Text("Imported assumptions remain active. Change them only if they match your intended scenario.","conditions.help","pob-muted"));
                rows.Add(UI.TextEntry(filter,"Search conditions","search","pob.search").Classes("pob-button"));
                rows.Add(Button("Read custom modifiers","custom"));
                AddPage(rows,A(State,"conditions").Where(c => filter.Length == 0 || S(c,"title").Contains(filter,StringComparison.OrdinalIgnoreCase)).ToArray(),c=>Condition(c)); break;
            case "Notes":
                foreach(var link in GuideLinks().Select((url,index)=>(url,index))) rows.Add(UI.Button("Open guide · "+new Uri(link.url).Host,"guide-link."+link.index,"pob.guide-link."+link.index).OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url=link.url})).Classes("pob-button"));
                AddTextPages(rows,S(State,"notes","No notes in this build.")); break;
        }
        return rows;
    }

    private WidgetElement Condition(JsonElement c)
    {
        var key=Array.FindIndex(A(State,"conditions"),v=>S(v,"id")==S(c,"id")).ToString(CultureInfo.InvariantCulture); var value=c.GetProperty("value");
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return Button(S(c,"title")+" · "+(value.GetBoolean()?"On":"Off"),"condition."+key);
        var options=A(c,"options");
        if (options.Length>0)
        {
            var selected = Array.FindIndex(options,o=>S(o,"value")==S(c,"value"));
            var choices=options.Select((o,i)=>new SelectOption("value"+i,S(o,"label"),"option."+key+"."+i,i==selected)).ToList();
            if(selected<0) choices.Insert(0,new("current","Current · "+S(c,"value"),"noop",true));
            return UI.Select(S(c,"title").TrimEnd(' ',':'),choices,"pob.condition."+key).Classes("pob-button");
        }
        return UI.Stack("pob.conditionrow."+key, Text(S(c,"title"),"conditionlabel."+key),UI.TextEntry(Limit(S(c,"value"),500),"Value","condition."+key,"pob.condition."+key).Classes("pob-button"));
    }
    private List<WidgetElement> NodeDetails()
    {
        var n=A(State,"nodes").First(n=>I(n,"id")==detailId);
        var rows = new List<WidgetElement> { Button("Back to tree","back"), Text(S(n,"name"),"node.name","pob-heading"), Text(S(n,"description"),"node.description") };
        if (pendingBefore is not null)
        {
            var previous = A(pendingBefore.State,"nodes").Where(x=>B(x,"allocated")).Select(x=>I(x,"id")).ToHashSet();
            var current = A(State,"nodes").Where(x=>B(x,"allocated")).Select(x=>I(x,"id")).ToHashSet();
            var added=current.Except(previous).ToHashSet();var removed=previous.Except(current).ToHashSet();
            rows.Add(Text($"Preview: +{added.Count} allocated, -{removed.Count} refunded", "node.preview", "pob-heading"));
            rows.Add(Text($"Passive points: {S(pendingBefore.State,"passivePoints")} to {S(State,"passivePoints")}\nAscendancy points: {S(pendingBefore.State,"ascendancyPoints")} to {S(State,"ascendancyPoints")}", "node.points"));
            rows.Add(Text(Compare(pendingBefore.State, State),"node.comparison","pob-muted"));
            foreach(var changed in A(State,"nodes").Where(x=>added.Contains(I(x,"id")) || removed.Contains(I(x,"id"))).Take(30))
                rows.Add(Text((added.Contains(I(changed,"id"))?"+ ":"- ")+S(changed,"name"),"changed."+I(changed,"id"),added.Contains(I(changed,"id"))?"pob-green":"pob-life"));
            rows.Add(Text("Apply saves this preview. Discard restores the previous tree and totals.","node.preview.help","pob-muted"));
        }
        else
        {
            rows.Add(Text(B(n,"allocated")?$"Allocated. Refunding affects {I(n,"refundCount")} node(s).":B(n,"canAllocate")?$"Allocation path: {I(n,"pathCount")} node(s).":B(n,"requiresChoice")?"This node needs a choice in desktop PoB before importing.":"No available allocation path for this build.","node.state","pob-muted"));
            if(B(n,"canAllocate") || B(n,"canRefund")) rows.Add(Button(B(n,"allocated")?"Preview refund":"Preview allocation path","node-change"));
        }
        return rows;
    }
    private List<WidgetElement> SkillDetails()
    {
        var skill=A(State,"skills").First(s=>I(s,"id")==detailId);
        var rows=new List<WidgetElement>{Button("Back to skills","back"),Text(S(skill,"title"),"skill.name","pob-heading"),Button("Use as main skill","skill-main"),Button(B(skill,"enabled")?"Disable group":"Enable group","skill-enabled"),Button(B(skill,"fullDps")?"Exclude from combined DPS":"Include in combined DPS","skill-fullDps")};
        foreach(var gem in A(skill,"gems"))
        {
            var id=I(gem,"id");
            var fields = new[]{"level","quality","count"};
            var controls = fields.Select((field, index) => UI.Stack("pob.gemfield." + id + field,
                Text(char.ToUpperInvariant(field[0]) + field[1..], "gemlabel."+id+field, "pob-small"),
                UI.TextEntry(S(gem,field),field,"gem."+id+"."+field,"pob.gem."+id+field).Classes("pob-button"))
                .InGrid(column:index)).ToArray();
            rows.Add(UI.Stack("pob.gemcard." + id, Text(S(gem,"name"),"gem.name."+id,"pob-heading pob-blue"),
                UI.Grid("pob.gemfields."+id,[GridTrack.Auto()],fields.Select(_=>GridTrack.Star()).ToArray(),controls).Spacing(0,8))
                .Classes("pob-context-card"));
        }
        return rows;
    }
    private void AddSet(List<WidgetElement> rows,string field,string kind)
    {
        var sets=A(State,field); if(sets.Length<2)return;
        rows.Add(UI.Select("Set",sets.Select(s=>new SelectOption("set"+I(s,"id"),S(s,"title"),"set."+kind+"."+I(s,"id"),B(s,"active"))).ToArray(),"pob.set."+kind).Classes("pob-button"));
    }
    private void AddPage(List<WidgetElement> rows,JsonElement[] entries,Func<JsonElement,WidgetElement> render)
    {
        page=Math.Clamp(page,0,Math.Max(0,(entries.Length-1)/PageSize));
        rows.Add(Text(entries.Length+" entries · page "+(page+1)+" of "+Math.Max(1,(entries.Length+PageSize-1)/PageSize),"pagecount","pob-muted"));
        rows.AddRange(entries.Skip(page*PageSize).Take(PageSize).Select(render));
        if(page>0)rows.Add(Button("Previous page","page-previous"));
        if((page+1)*PageSize<entries.Length)rows.Add(Button("Next page","page-next"));
    }
    private void AddTextPages(List<WidgetElement> rows,string text)
    {
        var chunks=Enumerable.Range(0,Math.Max(1,(text.Length+2499)/2500)).Select(i=>text.Substring(Math.Min(i*2500,text.Length),Math.Min(2500,Math.Max(0,text.Length-i*2500)))).ToArray();
        page=Math.Clamp(page,0,chunks.Length-1);rows.Add(Text(chunks[page],"reading"));
        if(page>0)rows.Add(Button("Previous page","page-previous"));if(page+1<chunks.Length)rows.Add(Button("Next page","page-next"));
    }
    private static string Format(double n)=>n.ToString("N1",CultureInfo.CurrentCulture);
    private string[] GuideLinks()=>System.Text.RegularExpressions.Regex.Matches(S(State,"notes"),@"https://[^\s<>""\[\]]+",System.Text.RegularExpressions.RegexOptions.None,TimeSpan.FromMilliseconds(100)).Select(m=>m.Value.TrimEnd('.',',',')')).Where(s=>s.Length<=2048&&Uri.TryCreate(s,UriKind.Absolute,out var uri)&&uri.UserInfo.Length==0).Distinct().Take(12).ToArray();
    private static string StatLabel(string key)=>key switch {"CombinedDPS"=>"Selected skill DPS (combined)","FullDPS"=>"All included skills DPS","TotalDPS"=>"Selected skill hit DPS","EnergyShield"=>"Energy shield","EffectiveHitPool"=>"Effective hit pool",_=>System.Text.RegularExpressions.Regex.Replace(key,"([a-z])([A-Z])","$1 $2")};
    private static string Compare(JsonElement before,JsonElement after)
    {
        var old=A(before,"stats").ToDictionary(s=>S(s,"name"),s=>s.GetProperty("value").GetDouble());
        var changes = A(after, "stats")
            .Where(s => S(s, "name") is "CombinedDPS" or "Life" or "EnergyShield")
            .Where(s => old.TryGetValue(S(s, "name"), out var v) && Math.Abs(v - s.GetProperty("value").GetDouble()) > .01)
            .Select(s => StatLabel(S(s, "name")) + " " + Format(old[S(s, "name")]) + " → " + Format(s.GetProperty("value").GetDouble()));
        var summary = string.Join(" · ", changes);
        return summary.Length > 0 ? summary : "No change to headline stats";
    }
}
