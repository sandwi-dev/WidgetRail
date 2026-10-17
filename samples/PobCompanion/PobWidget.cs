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
    private readonly string? artworkDirectory;
    private readonly Func<string> readClipboard = ClipboardText.Read;
    private PobResult? result, pendingBefore;
    private readonly Stack<string> undo = new();
    private string? documentId;
    private (string Id,string Name)[] savedBuilds = [];
    private string? pastedItem;
    private string? previewSlot;
    private string section = "Calcs", filter = "", status = "", detailKind = "";
    private int detailId;
    private string statDetail = "";
    private string compatibilityNotice = "";
    private bool busy, activated;
    private bool nodeReturnToMap;
    private bool showChangedNodes;
    private ToastElement? feedback;
    private WidgetTimedMutation? feedbackExpiry;
    internal static readonly TimeSpan InformationFeedbackDuration=TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan ErrorFeedbackDuration=TimeSpan.FromSeconds(8);
    private readonly Dictionary<string,int> selections = new();
    private long focusRequestSequence;
    private static readonly string[] Sections = ["Calcs", "Tree", "Skills", "Items", "Configuration", "Notes"];
    public PobWidget(int game, string payloadDirectory, string? dataDirectory = null)
    {
        this.game = game;
        this.dataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "applications", "pob" + game);
        preferences=new(Environment.GetEnvironmentVariable("WIDGETRAIL_POB_PREFERENCES")??Path.Combine(Path.GetDirectoryName(Path.GetFullPath(this.dataDirectory))!,"pob-preferences"));
        engine = new(Path.Combine(payloadDirectory, "pob-engine"), Path.Combine(payloadDirectory, "runtime"), Path.Combine(payloadDirectory, "Engine", "bridge.lua"));
        artworkDirectory=Path.Combine(payloadDirectory,"web","art");
    }
    public PobWidget(int game, PobEngine engine, string dataDirectory, Func<string>? readClipboard = null, string? preferencesDirectory=null) { this.game = game; this.engine = engine; this.dataDirectory = dataDirectory; this.readClipboard = readClipboard ?? ClipboardText.Read; preferences=new(preferencesDirectory??Path.Combine(dataDirectory,"preferences")); }
    private JsonElement State => result?.State ?? default;
    private static string S(JsonElement e, string key, string fallback = "") => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString() : fallback;
    private static int I(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static bool B(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static JsonElement[] A(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToArray() : [];
    private static string Limit(string text, int length = 3000) => text.Length <= length ? text : text[..length] + "…";
    private static WidgetElement Text(string text, string id, string css = "pob-text") => UI.Text(Limit(text), "pob." + id).Classes(css.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static ButtonElement Button(string text, string action, string? id = null) => UI.Button(Limit(text, 180), action, "pob." + (id ?? action)).Classes("pob-button");

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
                await OpenSavedWorkAsync(id, token);
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
        lock (gate) { if (busy) return; busy = true; status = message; feedback=null; }
        Invalidate();
        Operations.RunSingleFlight("pob.work", async context =>
        {
            try { await operation(context.CancellationToken); RefreshLibrary(); }
            catch (OperationCanceledException) { lock (gate) { ShowFeedback("Cancelled", "You can continue working.", ToastTone.Info); } }
            catch (Exception error) when (error is WidgetCapabilityException or IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or HttpRequestException)
            {
                lock (gate)
                {
                    var failureMessage = error is System.ComponentModel.Win32Exception native ? $"The build engine could not start (Windows error {native.NativeErrorCode}). Reinstall this widget or retry." : Limit(error.Message, 350);
                    ShowFeedback("Action not completed",failureMessage,ToastTone.Warning);
                }
            }
            finally { lock (gate) { busy = false;status=""; } Invalidate(); }
        }, WidgetOperationLifetime.Widget);
    }

    private void ShowFeedback(string title,string message,ToastTone tone)
    {
        var duration=tone is ToastTone.Warning or ToastTone.Danger?ErrorFeedbackDuration:InformationFeedbackDuration;
        var next=UI.Toast(title,message,tone,"pob.feedback.toast",duration);
        feedback=next;
        feedbackExpiry??=CreateTimedMutation(WidgetOperationLifetime.Widget);
        _=feedbackExpiry.ScheduleLatest(duration,()=>
        {
            lock(gate)if(ReferenceEquals(feedback,next))feedback=null;
            Invalidate();
        });
    }

    private WidgetElement WithFeedback(WidgetElement body,string scope)
    {
        // Both layers occupy the full widget bounds; a toast never takes height
        // away from the content or changes its input scope or initial focus.
        var layers=new List<WidgetElement>{body.InGrid(rowSpan:2,columnSpan:3)};
        if(busy)
            layers.Add(UI.Row("pob.operation.toast",
                UI.LoadingIndicator("pob.operation.progress",status,LoadingIndicatorSize.Compact),
                UI.Stack("pob.operation.copy",Text(status,"operation.title","pob-operation-title"),Text("B to cancel","operation.help","pob-small"))
                    .Classes("pob-operation-copy"),
                UI.Button("Cancel","cancel-operation","pob.cancel-operation").Classes("pob-button"))
                .Classes("wrail-toast","wrail-toast--info","pob-operation-toast","pob-feedback-toast").InGrid(row:1,column:1));
        else if(feedback is not null)layers.Add(feedback.Classes("pob-feedback-toast").InGrid(row:1,column:1));
        var layer=UI.Grid("pob.feedback.layer",[GridTrack.Star(),GridTrack.Auto()],
            [GridTrack.Star(),GridTrack.Star(2),GridTrack.Star()],layers.ToArray()).InputScope(scope).Classes("pob-feedback-layer");
        return busy?layer.Shortcut(ControllerButton.B,"cancel-operation"):layer;
    }

    public override WidgetView Render()
    {
        lock (gate)
        {
            quickControlTargets.Clear();
            overlayWebsites=preferences.OverlayBrowser;
            if (busy && result is null)
            {
                var loading=UI.Stack("pob.root",
                    Text($"PoE{game} Path of Building","title","pob-title"),
                    UI.Stack("pob.loading.panel",
                        UI.LoadingIndicator("pob.loading",status,LoadingIndicatorSize.Large),
                        Text(status,"loading.status","pob-heading"),
                        Text("Starting the calculation engine and preparing your build.","loading.help","pob-muted"))
                        .Classes("pob-loading-panel"),
                    Button("Cancel","cancel-operation"))
                    .Classes("pob-root").InputScope("pob.loading").Shortcut(ControllerButton.B,"cancel-operation");
                return new(loading,"pob.cancel-operation",ActiveInputScopeId:"pob.loading",
                    Surface:new(){Mode=WidgetSurfaceMode.Standard,PreferredWidth=760,PreferredHeight=440,MinimumWidth=620,MinimumHeight=360});
            }
            var children = new List<WidgetElement>();
            if (result is null && detailKind=="library")
                children.Add(LibraryWorkspace());
            else if(ManagementView&&result is not null)
            {
                if(pendingBefore is not null)children.Add(UI.Row("pob.preview",Text("PREVIEW · not applied","preview.label","pob-eyebrow pob-gold"),Button("Apply change","apply"),Button("Discard","cancel")).Classes("pob-preview"));
                children.Add(detailKind=="library"?LibraryWorkspace():detailKind=="game-export"?GameExportWorkspace():detailKind=="sets"?SetsWorkspace():BuildMenu());
            }
            else if (result is null)
            {
                children.Add(Text($"PoE{game} Path of Building", "title", "pob-title"));
                children.Add(Text("Bring your Path of Building build into the overlay.", "intro"));
                children.Add(OnlineBuildLinks());
                children.Add(Text(OnlineImportHelp,"import.online-help","pob-small pob-muted"));
                children.Add(WebsitePreference());
                children.Add(Button("Paste build from clipboard", "paste"));
                children.Add(ImportField());
                children.Add(Button("Create a new build", "new"));
                children.Add(Text("Calculations run locally. Imported originals are kept separately from your working copies. Not affiliated with Grinding Gear Games.", "notice", "pob-muted"));
                AddLibrary(children);
            }
            else
            {
                children.Add(BuildHeader());
                children.Add(section=="Calcs" ? Headlines() : CompactHeadlines());
                children.Add(Navigation());
                if (pendingBefore is not null && !(section == "Tree" && detailKind == "node"))
                    children.Add(UI.Row("pob.preview", Text("PREVIEW · not applied", "preview.label", "pob-eyebrow pob-gold"), Button("Apply change", "apply"), Button("Discard", "cancel")).Classes("pob-preview"));
                children.Add(UI.Stack("pob.section.content",Workspace()).Classes("pob-workspace")
                    .TransitionContent("pob.sections",section.ToLowerInvariant(),Array.IndexOf(Sections,section)));
            }
            var scope = "pob.scope." + section.ToLowerInvariant() + "." + (pendingBefore is not null ? "preview" : detailKind.Length == 0 || InlineDetail ? "main" : detailKind);
            var initial = detailKind=="library" ? "pob.library.search" : detailKind=="menu"?"pob.rename":result is null ? "pob.paste" : detailKind.Length > 0 && !InlineDetail ? "pob.back" : SectionEntry();
            if (pendingBefore is not null) initial = "pob.apply";
            if(result is not null && !ManagementView)
            {
                var body=UI.Stack("pob.body",children.Skip(1).ToArray()).RememberChildFocus(initial).Classes("pob-body");
                children=[children[0],body];
            }
            var root = UI.Stack("pob.root", children.ToArray()).Classes("pob-root").RememberChildFocus(initial);
            if(BuildControlsAvailable)
            {
                quickControlTargets.Add("pob.buildtools");quickControlTargets.Add("pob.body");
                root=root.Shortcut(ControllerButton.RightStick,"quick-controls.pob.buildtools","Build controls");
            }
            if (busy) root = root.Shortcut(ControllerButton.B, "cancel-operation");
            else if(result is null&&detailKind=="library")root=root.Shortcut(ControllerButton.B,"back");
            if (result is not null)
            {
                if(!ManagementView)root = root.Shortcut(ControllerButton.LeftBumper, "previous-section").Shortcut(ControllerButton.RightBumper, "next-section");
                if (!busy && ((detailKind.Length != 0 && !InlineDetail) || pendingBefore is not null)) root = root.Shortcut(ControllerButton.B, pendingBefore is not null ? "cancel" : "back");
            }
            var view=new WidgetView(WithFeedback(root,scope), initial, ActiveInputScopeId: scope,
                Surface: new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Standard, PreferredWidth = ManagementView?(detailKind=="library"?1040:840):section == "Tree" ? 1320 : 1180, PreferredHeight = ManagementView?(detailKind=="library"&&savedBuilds.Length<=5?680:800):section == "Tree" ? 900 : 820, MinimumWidth = ManagementView?680:820, MinimumHeight = 600 })
            {
                FocusGroupEntryRequest = ContentFocusRequest(),
            };
            return WithPlanningModal(WithSkillConfirmation(WithBuildConfirmation(view)));
        }
    }

    private string SectionEntry() => section switch
    {
        "Calcs" => "pob.headline.0",
        "Tree" => detailKind=="node" ? A(State,"nodes").Any(n=>I(n,"id")==detailId && (B(n,"canAllocate")||B(n,"canRefund"))) ? "pob.node-change" : "pob.back" : "pob.search",
        "Skills" when A(State,"skills").Length>0 => "pob.skill."+detailId,
        "Skills" => "pob.group-create",
        "Items" when A(State,"items").Length>0 => "pob.item."+detailId,
        "Configuration" when ConfigurationSections().Length>0 => "pob.config.category."+Math.Max(0,Array.IndexOf(ConfigurationSections(),configSection)),
        _ => "pob.tab."+Array.IndexOf(Sections,section),
    };

    private void AddLibrary(List<WidgetElement> children)
    {
        if (savedBuilds.Length == 0) return;
        children.Add(Button($"Saved builds ({savedBuilds.Length})","library"));
    }

    private List<WidgetElement> Content()
    {
        var rows = new List<WidgetElement>();
        if (detailKind == "custom") { rows.Add(Button("Back to conditions", "back")); AddReading(rows, S(State, "customMods", "No custom modifiers in this configuration."), "custom"); return rows; }
        if (detailKind == "choose-slot")
        {
            if(pendingBefore is not null)return ItemComparisonContent();
            rows.Add(Button("Discard item draft", "back")); rows.Add(Text("Choose the slot to replace", "slot.heading", "pob-heading"));
            rows.Add(UI.Row("pob.draft.actions",Button("Edit item","draft-edit"),Button("Save to inventory","draft-store")).Classes("pob-toolbar"));
            var slots = A(State, "slots"); rows.AddRange(slots.Select((slot,index)=>(slot,index))
                .Where(x=>itemDraftSlots.Contains(S(x.slot,"name"))).Select(x=>Button(S(x.slot,"name"),"replace-slot."+x.index)));
            if(itemDraftSlots.Length==0)rows.Add(Text("No compatible slots are available in this setup.","slot.empty","pob-muted"));
            return rows;
        }
        if (detailKind == "skill-options") return SkillOptionsContent();
        return rows;
    }
    private WidgetElement Condition(JsonElement c)
    {
        var key=Array.FindIndex(A(State,"conditions"),v=>S(v,"id")==S(c,"id")).ToString(CultureInfo.InvariantCulture); var value=c.GetProperty("value");
        var title=InlineLabel(S(c,"title"),S(c,"id"));
        var categoryTarget="pob.config.category."+Math.Max(0,Array.IndexOf(ConfigurationSections(),configSection));
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return UI.Switch(title,value.GetBoolean(),"condition."+key,"pob.condition."+key).FocusLeft(categoryTarget).Shortcut(ControllerButton.X,"Compare toggle",actionId:"config-compare."+key).Classes("pob-toggle");
        var options=A(c,"options");
        if (options.Length>0)
        {
            var selected = Array.FindIndex(options,o=>S(o,"value")==S(c,"value"));
            var choices=options.Select((o,i)=>new SelectOption("value"+i,InlineLabel(S(o,"label"),"Default"),"option."+key+"."+i,i==selected)).ToList();
            if(selected<0) choices.Insert(0,new("current",InlineLabel("Current · "+S(c,"value")),"noop",true));
            return UI.Select(title.TrimEnd(' ',':'),choices,"pob.condition."+key).FocusLeft(categoryTarget).Classes("pob-select");
        }
        return UI.Stack("pob.conditionrow."+key, Text(title,"conditionlabel."+key,"pob-field-label"),
            UI.TextEntry(S(c,"value"),title,"condition."+key,"pob.condition."+key,2048).FocusLeft(categoryTarget).Classes("pob-input"));
    }
    private List<WidgetElement> NodeDetails()
    {
        var n=A(State,"nodes").First(n=>I(n,"id")==detailId);
        if(pendingBefore is not null&&!B(n,"allocated"))
        {
            var original=A(pendingBefore.State,"nodes").FirstOrDefault(node=>I(node,"id")==detailId);
            if(B(original,"allocated"))n=original;
        }
        var rows = new List<WidgetElement> { Button("Back to tree","back"), Text(S(n,"name"),"node.name","pob-heading") };
        if(S(n,"socketedItem").Length>0)rows.Add(Text("Socketed: "+S(n,"socketedItem"),"node.socket","pob-gold"));
        if(pendingBefore is null)AddNodeDescription(rows,n,"node.description");
        if (pendingBefore is not null)
        {
            var previous = A(pendingBefore.State,"nodes").Where(x=>B(x,"allocated")).Select(x=>I(x,"id")).ToHashSet();
            var current = A(State,"nodes").Where(x=>B(x,"allocated")).Select(x=>I(x,"id")).ToHashSet();
            var added=current.Except(previous).ToHashSet();var removed=previous.Except(current).ToHashSet();
            var oldNodes=A(pendingBefore.State,"nodes").ToDictionary(x=>I(x,"id"));
            var modified=A(State,"nodes").Where(x=>B(x,"allocated")&&oldNodes.TryGetValue(I(x,"id"),out var old)&&B(old,"allocated")&&S(x,"description")!=S(old,"description")).ToArray();
            rows.Add(Text(string.Join(" · ",new[]{added.Count>0?$"{added.Count} allocated":null,removed.Count>0?$"{removed.Count} refunded":null,modified.Length>0?$"{modified.Length} effect(s) changed":null}.Where(s=>s is not null)), "node.preview", "pob-heading"));
            foreach(var changed in modified)
            {
                var id=I(changed,"id");
                rows.Add(Text("Previous effect","effect.before."+id,"pob-eyebrow pob-muted"));AddNodeDescription(rows,oldNodes[id],"effect.old."+id);
                rows.Add(Text("Proposed effect","effect.after."+id,"pob-eyebrow pob-gold"));AddNodeDescription(rows,changed,"effect.new."+id);
            }
            rows.Add(Text($"Passive points: {S(pendingBefore.State,"passivePoints")} to {S(State,"passivePoints")}\nAscendancy points: {S(pendingBefore.State,"ascendancyPoints")} to {S(State,"ascendancyPoints")}", "node.points"));
            if(I(pendingBefore.State,"level")!=I(State,"level"))
                rows.Add(Text($"{(B(State,"autoLevel")?"Automatic character level":"Character level")}: {S(pendingBefore.State,"level")} to {S(State,"level")}","node.level","pob-muted"));
            AddComparison(rows,pendingBefore.State,State,"node.comparison");
            if(added.Count+removed.Count>0)
                rows.Add(Button($"{(showChangedNodes?"Hide":"Show")} changed nodes ({added.Count+removed.Count})","preview-nodes").Selected(showChangedNodes));
            if(showChangedNodes)foreach(var (source,ids,allocated) in new[]{(State,added,true),(pendingBefore.State,removed,false)})
                foreach(var changed in A(source,"nodes").Where(x=>ids.Contains(I(x,"id"))))
                {
                    var id="changed."+I(changed,"id");
                    var description=new List<WidgetElement>{Text((allocated?"Allocated · ":"Refunded · ")+S(changed,"name"),id,allocated?"pob-heading pob-green":"pob-heading pob-life")};
                    AddNodeDescription(description,changed,id+".description");
                    rows.Add(UI.Stack("pob."+id+".card",description.ToArray()).Classes("pob-context-card"));
                }
            rows.Add(Text("Apply saves this preview. Discard restores the previous tree and totals.","node.preview.help","pob-muted"));
        }
        else
        {
            rows.Add(Text(B(n,"allocated")?$"Allocated. Refunding affects {I(n,"refundCount")} node(s).":B(n,"granted")?"Granted by an item; no passive points spent.":B(n,"canChoose")?"Choose an effect to preview its allocation path and calculation changes.":B(n,"canAllocate")?$"Allocation path: {I(n,"pathCount")} node(s).":"No available allocation path for this build.","node.state","pob-muted"));
            if(B(n,"canChoose"))rows.Add(PassiveChoiceControl(n));
            if(B(n,"canAllocate") || B(n,"canRefund")) rows.Add(Button(B(n,"allocated")?"Preview refund":"Preview allocation path","node-change"));
            if(B(State,"canEditNodeNotes")&&B(n,"allocated"))rows.Add(Field("Node note",UI.TextEntry(S(n,"note"),"Add a note for this passive","node-note","pob.node-note",1024).Classes("pob-input"),"node-note"));
        }
        return rows;
    }
    private List<WidgetElement> SkillDetails()
    {
        var skill=A(State,"skills").First(s=>I(s,"id")==detailId);
        var rows=new List<WidgetElement>{Button("Back to skills","back"),Text(S(skill,"title"),"skill.name","pob-heading"),Button("Use as main skill","skill-main"),Button(B(skill,"enabled")?"Disable group":"Enable group","skill-enabled"),Button(B(skill,"fullDps")?"Exclude from Full DPS":"Include in Full DPS","skill-fullDps")};
        foreach(var gem in A(skill,"gems"))
        {
            var id=I(gem,"id");
            var fields = new[]{"level","quality","count"};
            var controls = fields.Select((field, index) => UI.Stack("pob.gemfield." + id + field,
                Text(char.ToUpperInvariant(field[0]) + field[1..], "gemlabel."+id+field, "pob-small"),
                field=="level"&&B(gem,"levelReadOnly")
                    ? Text(S(gem,field),"gem."+id+field,"pob-field-label")
                    : UI.TextEntry(S(gem,field),field,"gem."+id+"."+field,"pob.gem."+id+field).Classes("pob-input"))
                .InGrid(column:index)).ToArray();
            var card=new List<WidgetElement>{Text(S(gem,"name"),"gem.name."+id,"pob-heading pob-blue"),
                UI.Grid("pob.gemfields."+id,[GridTrack.Auto()],fields.Select(_=>GridTrack.Star()).ToArray(),controls).Spacing(0,8)};
            card.Add(UI.Row("pob.gem.actions."+id,Button("Quality effects","gem-quality."+id),Button("Gem options","gem-options."+id)).Classes("pob-toolbar"));
            if(B(gem,"levelReadOnly"))
            {
                var source=S(skill,"sourceName");var slot=S(skill,"slot");
                var origin=source.Length>0?source+(slot.Length>0?" · "+slot:""):"this build's effects";
                card.Add(Text("Granted by "+origin+". Level is determined by its source.","gem.source."+id,"pob-muted"));
            }
            rows.Add(UI.Stack("pob.gemcard."+id,card.ToArray()).Classes("pob-context-card"));
        }
        return rows;
    }
    private void AddSet(List<WidgetElement> rows,string field,string kind)
    {
        var sets=A(State,field); if(sets.Length<2)return;
        rows.Add(UI.Select("Set",sets.Select(s=>new SelectOption("set"+I(s,"id"),S(s,"title"),"set."+kind+"."+I(s,"id"),B(s,"active"))).ToArray(),"pob.set."+kind).Disabled(pendingBefore is not null).Classes("pob-select"));
    }
    public override ValueTask<bool> OnIntentCompletedAsync(WidgetIntentFeedback feedback,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(PobNoteLinks.UseFallback(feedback));
    }
    private string StatLabel(string key)=>key switch {"CombinedDPS"=>B(State,"mainSkillShowsAverage")?"Main skill average damage":"Main skill DPS (combined)","FullDPS"=>"All included skills DPS","TotalDPS"=>"Main skill hit DPS","EnergyShield"=>"Energy shield","EffectiveHitPool"=>"Effective hit pool",_=>System.Text.RegularExpressions.Regex.Replace(key,"([a-z])([A-Z])","$1 $2")};
}
