using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

if(args is ["--bundle-only"])
{
    var passed=0;var failed=0;
    await BundleTests.RunAsync((ok,name)=>{Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(ok)passed++;else failed++;});
    Console.WriteLine($"Engine bundles: {passed} passed, {failed} failed");return failed==0?0:1;
}

if(args is ["--note-links-only"])
{
    var passed=0;var failed=0;
    NoteLinkTests.Run((ok,name)=>{Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(ok)passed++;else failed++;});
    Console.WriteLine($"Note links: {passed} passed, {failed} failed");return failed==0?0:1;
}

if(args.Length is not (4 or 5) || args.Length==5&&args[4] is not ("--work-state-only" or "--management-only" or "--comparison-only" or "--tree-only" or "--quick-controls-only" or "--skill-edits-only" or "--skill-management-only" or "--loadouts-only" or "--guide-workflow-only" or "--planning-features-only" or "--import-only" or "--import-export-only" or "--sets-only" or "--trade-only" or "--items-only" or "--skill-options-only" or "--passive-choices-only" or "--calculation-views-only" or "--engine-update-only" or "--engine-failure-only" or "--trade-workflow-only" or "--trade-improvements-only" or "--trade-menu-only" or "--tree-editing-only" or "--currency-ranking-only" or "--calculation-context-only" or "--suggestions-only")){Console.Error.WriteLine("Arguments: game engine-directory runtime-directory fixture-directory [--work-state-only|--management-only|--comparison-only|--tree-only|--quick-controls-only|--skill-edits-only|--skill-management-only|--loadouts-only|--guide-workflow-only]");return 2;}
var game=int.Parse(args[0]);var engineRoot=args[1];var runtime=args[2];var fixtures=args[3];
var adapter=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Engine/bridge.lua"));
// Explicit source override keeps probes independent of output layout.
adapter=Environment.GetEnvironmentVariable("POB_ADAPTER")??adapter;
var results=new List<object>();var failures=0;
void Check(bool passed,string name){results.Add(new{name,passed});Console.WriteLine((passed?"PASS ":"FAIL ")+name);if(!passed)failures++;}
if(args.Length==5)
{
    if(args[4]=="--engine-update-only")
    {
        await EngineUpdateTests.RunAsync(game,engineRoot,runtime,adapter,fixtures,Check);
        Console.WriteLine(JsonSerializer.Serialize(new{game,passed=results.Count-failures,failed=failures,results}));return failures==0?0:1;
    }
    var fixture=Directory.EnumerateFiles(fixtures,"*.xml").OrderBy(path=>path,StringComparer.Ordinal).First();
    var workXml=await BuildDocument.ReadAsync(fixture,game,default);
    if(args[4]=="--guide-workflow-only")
    {
        await GuideWorkflowTests.RunAsync(engineRoot,runtime,adapter,workXml,Check);
        await EngineCompatibilityTests.RunAsync(engineRoot,runtime,adapter,workXml,Check);
    }
    else if(args[4]=="--items-only")
    {
        await ItemAuthoringTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
        await ItemWorkflowTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    }
    else if(args[4]=="--engine-failure-only")await EngineFailureTests.RunAsync(engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--trade-improvements-only")await TradeImprovementTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--trade-menu-only")await TradeMenuTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--trade-workflow-only")await TradeWorkflowTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--trade-only")await TradeSearchTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--sets-only")await SetManagementTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--import-export-only")await ImportExportTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--import-only")await EngineImportTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--planning-features-only")await PlanningFeatureTests.RunAsync(engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--loadouts-only")await LoadoutTests.RunAsync(engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--suggestions-only")await SearchSuggestionTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--skill-management-only")await SkillManagementTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--skill-options-only")await SkillOptionTests.RunAsync(game,engineRoot,runtime,adapter,Check);
    else if(args[4]=="--skill-edits-only")await SkillEditTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--quick-controls-only")await QuickControlsTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--passive-choices-only")await PassiveChoiceTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--tree-editing-only")await TreeEditingTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--currency-ranking-only")await CurrencyRankingTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--calculation-context-only")await CalculationContextTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--calculation-views-only")await CalculationViewTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--tree-only")await TreePresentationTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--comparison-only")await ComparisonTests.RunAsync(engineRoot,runtime,adapter,workXml,Check);
    else if(args[4]=="--management-only")await ManagementTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    else await WorkStateTests.RunAsync(game,engineRoot,runtime,adapter,workXml,Check);
    Console.WriteLine(JsonSerializer.Serialize(new{game,passed=results.Count-failures,failed=failures,results}));return failures==0?0:1;
}
await BundleTests.RunAsync(Check);
var tiny=$"<{(game==2?"PathOfBuilding2":"PathOfBuilding")}><Build level=\"1\"/></{(game==2?"PathOfBuilding2":"PathOfBuilding")}>";
Check(BuildDocument.Decode(BuildDocument.Encode(tiny),game)==tiny,"Share code codec round trip");
var longNotes=string.Concat(Enumerable.Repeat("A paragraph with a complete sentence.\n\n",180))+"Last paragraph 🧭";
var reading=new List<WidgetElement>();PobWidget.AddReading(reading,longNotes,"notes");
var readingView=new WidgetView(UI.Stack("reading",reading.ToArray())).CreateSnapshot("reading",1);
Check(string.Concat(Nodes(readingView.Root).Where(n=>n.Kind==ViewNodeKind.Text).Select(n=>n.Text))==longNotes,"Continuous notes retain every character across bounded text blocks");
Check(!Nodes(readingView.Root).Any(n=>n.Kind==ViewNodeKind.Button),"Reading notes never requires a Next page action");
var formatted=new List<WidgetElement>();
var colored=JsonSerializer.SerializeToElement(new{text="Life: +20, Cold: +5",runs=new[]{new{text="Life: ",tone="text"},new{text="+20",tone="warning"},new{text=", Cold: ",tone="text"},new{text="+5",tone="blue"}}});
PobWidget.AddFormatted(formatted,colored,"text","runs","test");
var richView=new WidgetView(UI.Stack("rich",formatted.ToArray())).CreateSnapshot("rich",1);
Check(string.Concat(Nodes(richView.Root).Where(n=>n.Kind==ViewNodeKind.Text).Select(n=>n.Text))==colored.GetProperty("text").GetString() &&
    Nodes(richView.Root).Any(n=>n.Kind==ViewNodeKind.RichText) && Nodes(richView.Root).Any(n=>n.StyleClasses.Contains("pob-blue")),"Rich descriptions preserve text and semantic inline colors");
var plainStat="Unarmed Attacks have:\n+0.1% to Critical Hit Chance per 10 Item Energy Shield\n1% more Attack Speed per 75 Item Evasion on Equipped Armour Items";
var plainNode=JsonSerializer.SerializeToElement(new{id=1,name="Plain passive",kind="Keystone",description=plainStat,allocated=false});
var statRuns=PobWidget.ReadFormattedRuns(plainNode,"description","descriptionRuns",true);
Check(string.Concat(statRuns.Select(r=>r.Text))==plainStat && statRuns.Any(r=>r.Text=="+0.1%"&&r.Tone=="gold")&&statRuns.Any(r=>r.Text=="Energy Shield"&&r.Tone=="blue"),"Plain stat descriptions gain numeric and resource emphasis without changing wording");
var paragraphs=PobWidget.DescriptionParagraphs(plainNode);
Check(string.Join("\n",paragraphs.Select(p=>string.Concat(p.Select(r=>r.Text))))==plainStat&&paragraphs.Count==2,"Passive paragraphs preserve the condition and its following modifier");
var warning=JsonSerializer.SerializeToElement(new{text="25 Cold damage",runs=new[]{new{text="25 Cold damage",tone="warning"}}});
Check(PobWidget.ReadFormattedRuns(warning,"text","runs",true).All(r=>r.Tone=="warning"),"Stat emphasis never overrides upstream warning colors");
var mechanics="Empowering your next Repeatable Bow or Projectile Spear Attacks. Consume a Frenzy Charge or Power Charges. Cannot Empower Sustained Skills. Freeze, Ignite, Shock, Poison and Bleeding.";
var mechanicRuns=PobWidget.ReadFormattedRuns(JsonSerializer.SerializeToElement(new{text=mechanics}),"text","runs",true);
Check(string.Concat(mechanicRuns.Select(r=>r.Text))==mechanics && mechanicRuns.Any(r=>r.Text=="Empowering"&&r.Tone=="gold") &&
    mechanicRuns.Any(r=>r.Text=="Frenzy Charge"&&r.Tone=="green") && mechanicRuns.Any(r=>r.Text=="Power Charges"&&r.Tone=="blue") &&
    mechanicRuns.Any(r=>r.Text=="Sustained Skills"&&r.Tone=="gold") && mechanicRuns.Any(r=>r.Text=="Freeze"&&r.Tone=="blue") &&
    mechanicRuns.Any(r=>r.Text=="Ignite"&&r.Tone=="orange"),"Mechanics, charges and ailments gain consistent theme roles without rewriting descriptions");
var ordinary="Empowerment is not the same as empoweredness. Lifeform, Manacles, Poisonous.";
Check(PobWidget.ReadFormattedRuns(JsonSerializer.SerializeToElement(new{text=ordinary}),"text","runs",true).All(r=>r.Tone=="text"),"Description emphasis matches complete terms rather than word fragments");
var tooltip=PobWidget.CreateTooltip(plainNode,3);
Check(tooltip.GetProperty("paragraphs").GetArrayLength()==paragraphs.Count&&tooltip.GetProperty("paragraphs")[0].EnumerateArray().Any(r=>r.GetProperty("tone").GetString()=="gold"),"Hover and native descriptions share the same formatted paragraphs");
var hugeTooltip=PobWidget.CreateTooltip(JsonSerializer.SerializeToElement(new{id=2,name="Long node",kind="Normal",description=new string('界',12000)}),4);
Check(JsonSerializer.SerializeToUtf8Bytes(hugeTooltip).Length<=WebSessionContract.MaximumMessageBytes&&hugeTooltip.GetProperty("truncated").GetBoolean(),"Long Unicode hover descriptions remain within the web-session bound and identify truncation");
foreach(var path in new[]{@"\\example.invalid\share\build.xml",@"\\.\pipe\not-a-build"})
{
    try{await BuildDocument.ReadAsync(path,game,default);Check(false,"Reject network and device paths before opening a file");}
    catch(IOException){Check(true,"Reject network and device paths before opening a file");}
}
try{BuildDocument.Decode("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///private'>]><a>&x;</a>",game);Check(false,"Reject external XML entities");}catch(IOException){Check(true,"Reject external XML entities");}
try{BuildDocument.Decode(tiny,game==2?1:2);Check(false,"Reject other game");}catch(IOException){Check(true,"Reject other game");}
await using var engine=new PobEngine(engineRoot,runtime,adapter);
var stopwatch=Stopwatch.StartNew();var blank=await engine.ExecuteAsync(new{op="new"},default);Console.WriteLine($"Cold start {stopwatch.ElapsedMilliseconds} ms; working set {engine.WorkingSet/1048576.0:F1} MiB");
Check(blank.State.GetProperty("nodes").GetArrayLength()>100,"Real tree data loaded");
string? firstXml=null;Dictionary<string,double>? firstStats=null;
foreach(var file in Directory.EnumerateFiles(fixtures,"*.xml").Take(8))
{
    string xml;try{xml=BuildDocument.Decode(await File.ReadAllTextAsync(file),game);}catch(IOException){continue;}
    stopwatch.Restart();var loaded=await engine.ExecuteAsync(new{op="load",xml,name=Path.GetFileNameWithoutExtension(file)},default);
    Console.WriteLine($"Load {Path.GetFileName(file)}: {stopwatch.ElapsedMilliseconds} ms; {engine.WorkingSet/1048576.0:F1} MiB");
    var before=XDocument.Parse(xml);var after=XDocument.Parse(loaded.Xml);
    foreach(var path in new[]{("Tree","Spec"),("Items","ItemSet"),("Skills","SkillSet"),("Config","ConfigSet")})
    {
        var oldCount=before.Root!.Element(path.Item1)?.Elements(path.Item2).Count()??0;
        // Older PoE1 XML stored a single skill/config set directly in its parent.
        // PoB upgrades that representation to one explicit set when saving.
        if(oldCount==0 && path.Item1 is "Skills" or "Config") oldCount=1;
        Check(oldCount==after.Root!.Element(path.Item1)?.Elements(path.Item2).Count(),Path.GetFileName(file)+" preserves "+path.Item2);
    }
    // These historical fixtures contain skills removed from the pinned releases:
    // Blood Magic / Vaal Ancestral Warchief / Increased Duration (PoE 1),
    // and Energy Barrier / Flammability / Excoriate (PoE 2). Reject silently lost IDs.
    var incompatibleLegacy = Path.GetFileName(file) is "nbT07lyVk6mw.xml" or "Dual Wield Cospris CoC.xml" or "Generals Perforate Zerker.xml" or "Mirage Archer Toxic Rain.xml";
    try { var renamed=BuildDocument.VerifySkillPreservation(xml,loaded.Xml); Check(!incompatibleLegacy,Path.GetFileName(file)+" retains authored gem identities"); if(renamed>0)Console.WriteLine($"Upstream normalized {renamed} gem display names, retaining their stable IDs."); }
    catch(IOException){Check(incompatibleLegacy,Path.GetFileName(file)+" rejects upstream removal of authored gem IDs");}
    if(!incompatibleLegacy)
    {
        var itemLines=loaded.State.GetProperty("items").EnumerateArray()
            .SelectMany(item=>item.GetProperty("details").EnumerateArray())
            .SelectMany(group=>group.GetProperty("lines").EnumerateArray())
            .Select(line=>line.GetProperty("text").GetString()??"").ToArray();
        Check((loaded.State.GetProperty("items").GetArrayLength()==0||itemLines.Length>0) && !itemLines.Any(line=>line.Contains("could not be formatted",StringComparison.Ordinal)),Path.GetFileName(file)+" formats equipped items or has no items to describe");
        Check(!itemLines.Any(line=>line.StartsWith("Prefix:",StringComparison.Ordinal)||line.StartsWith("Suffix:",StringComparison.Ordinal)||line.Contains("{range:",StringComparison.Ordinal)),"Item details hide serialization metadata");
    }
    Check(before.Root!.Element("Notes")?.Value==after.Root!.Element("Notes")?.Value,Path.GetFileName(file)+" preserves notes");
    var reloaded=await engine.ExecuteAsync(new{op="load",xml=loaded.Xml,name="roundtrip"},default);
    Check(SameStats(loaded.State,reloaded.State),Path.GetFileName(file)+" calculation round trip");
    var changed=await engine.ExecuteAsync(new{op="level",value=75},default);
    Check(changed.State.GetProperty("level").GetInt32()==75,"Level edit is applied");
    var restored=await engine.ExecuteAsync(new{op="load",xml=loaded.Xml,name="restored"},default);
    Check(SameStats(loaded.State,restored.State),"Restoring a preview restores calculations");
    if (!incompatibleLegacy) { firstXml??=loaded.Xml;firstStats??=Stats(loaded.State); }
}
if(firstXml is not null){var again=await engine.ExecuteAsync(new{op="load",xml=firstXml,name="A after B"},default);Check(firstStats!.All(p=>Stats(again.State).TryGetValue(p.Key,out var n)&&Math.Abs(n-p.Value)<=Math.Max(1,Math.Abs(p.Value))*1e-8),"Build switching does not retain stale calculation state");}
Check(firstXml is not null,"At least one real fixture exercised");
if(firstXml is not null)
{
    var optionsBaseline=await engine.ExecuteAsync(new{op="load",xml=firstXml,name="skill options audit"},default);
    var mainGroup=optionsBaseline.State.GetProperty("mainSkillGroup").GetInt32();
    var inspectedCount=0;var editedOption=false;
    foreach(var skill in optionsBaseline.State.GetProperty("skills").EnumerateArray().Where(s=>s.GetProperty("id").GetInt32()!=mainGroup).Take(8))
    {
        var group=skill.GetProperty("id").GetInt32();
        // PoB serializes passive-node sets in hash order after a reload. Compare
        // against this load's XML, not an earlier serialization of the same sets.
        var queryBaseline=await engine.ExecuteAsync(new{op="load",xml=optionsBaseline.Xml,name="query baseline"},default);
        var inspected=await engine.ExecuteAsync(new{op="inspect-skill",group},default);
        Check(inspected.State.GetProperty("skillOptionsGroup").GetInt32()==group && inspected.Xml==queryBaseline.Xml,"Inspection returns selected non-main group options without changing authored XML");
        var restored=await engine.ExecuteAsync(new{op="snapshot"},default);
        Check(restored.State.GetProperty("mainSkillGroup").GetInt32()==mainGroup && SameStats(restored.State,optionsBaseline.State),"Inspecting skill options restores the original calculation group and totals");
        inspectedCount++;
        var option=inspected.State.GetProperty("skillOptions").EnumerateArray().FirstOrDefault();
        if(!editedOption&&option.ValueKind==JsonValueKind.Object)
        {
            var alternative=option.GetProperty("options").EnumerateArray().First(o=>!o.GetProperty("active").GetBoolean()).GetProperty("id").GetInt32();
            var key=option.GetProperty("id").GetString();
            var edited=await engine.ExecuteAsync(new{op="skill-option",group,control=key,value=alternative},default,optionsBaseline.Xml);
            var verified=await engine.ExecuteAsync(new{op="inspect-skill",group},default,edited.Xml);
            Check(edited.State.GetProperty("mainSkillGroup").GetInt32()==mainGroup && verified.State.GetProperty("skillOptions").EnumerateArray().Single(o=>o.GetProperty("id").GetString()==key)
                .GetProperty("options").EnumerateArray().Single(o=>o.GetProperty("active").GetBoolean()).GetProperty("id").GetInt32()==alternative,
                "Editing non-main skill options updates its upstream control and preserves the main group after reload");
            var other=await engine.ExecuteAsync(new{op="inspect-skill",group=mainGroup},default,edited.Xml);
            Check(other.State.GetProperty("skillOptions").GetRawText()==optionsBaseline.State.GetProperty("skillOptions").GetRawText(),"Non-main skill edit leaves the main skill options unchanged");
            editedOption=true;
        }
    }
    Check(inspectedCount>0,"Fixture exercises inspection of non-main groups");
    if(game==2)Check(editedOption,"PoE 2 fixture exercises a non-main skill option edit");
    var baseline=await engine.ExecuteAsync(new{op="load",xml=firstXml,name="Tree baseline"},default);
    var nodes=baseline.State.GetProperty("nodes").EnumerateArray().ToArray();
    var allocation=nodes.First(n=>n.GetProperty("canAllocate").GetBoolean() && n.GetProperty("pathCount").GetInt32() is >=2 and <=5);
    var target=allocation.GetProperty("id").GetInt32();
    var allocated=await engine.ExecuteAsync(new{op="node",id=target,mode="allocate"},default);
    var added=Allocated(allocated.State).Except(Allocated(baseline.State)).ToArray();
    Check(added.Contains(target) && added.Length==allocation.GetProperty("pathCount").GetInt32(),"Allocation adds the complete upstream path");
    Check(allocated.State.GetProperty("passivePoints").GetInt32()>baseline.State.GetProperty("passivePoints").GetInt32(),"Allocation updates passive point count");
    CheckMap(allocated.State,baseline.State,"added",added.Length,Check);
    var discarded=await engine.ExecuteAsync(new{op="load",xml=baseline.Xml,name="Discard allocation"},default);
    Check(Allocated(discarded.State).SetEquals(Allocated(baseline.State)) && SameStats(discarded.State,baseline.State),"Discard restores allocations and calculated totals");
    var refund=nodes.First(n=>n.GetProperty("canRefund").GetBoolean() && n.GetProperty("refundCount").GetInt32() is >=2 and <=5);
    var refunded=await engine.ExecuteAsync(new{op="node",id=refund.GetProperty("id").GetInt32(),mode="refund"},default);
    var removed=Allocated(baseline.State).Except(Allocated(refunded.State)).ToArray();
    Check(removed.Length==refund.GetProperty("refundCount").GetInt32(),"Refund removes exactly the upstream dependent-node count");
    CheckMap(refunded.State,baseline.State,"removed",removed.Length,Check);
    var reloaded=await engine.ExecuteAsync(new{op="load",xml=refunded.Xml,name="Applied refund"},default);
    Check(Allocated(reloaded.State).SetEquals(Allocated(refunded.State)) && SameStats(reloaded.State,refunded.State),"Applied refund survives XML reload");
    var reset=await engine.ExecuteAsync(new{op="load",xml=baseline.Xml,name="Undo refund"},default);
    Check(Allocated(reset.State).SetEquals(Allocated(baseline.State)) && SameStats(reset.State,baseline.State),"Undo refund restores allocations and calculations");
    await engine.ExecuteAsync(new{op="new"},default);
    var recovered=await engine.ExecuteAsync(new{op="snapshot"},default,baseline.Xml);
    Check(Allocated(recovered.State).SetEquals(Allocated(baseline.State)) && SameStats(recovered.State,baseline.State),"Next command restores the displayed build after a rejected replacement");
    var starts=nodes.Where(n=>n.GetProperty("kind").GetString() is "ClassStart" or "AscendClassStart").Select(n=>n.GetProperty("id").GetInt32()).ToHashSet();
    try{await engine.ExecuteAsync(new{op="node",id=starts.First(),mode="refund"},default);Check(false,"Starting-node refund is rejected");}catch(PobCommandException){Check(true,"Starting-node refund is rejected");}
    var links=baseline.State.GetProperty("links").EnumerateArray().ToArray();
    var lookup=nodes.ToDictionary(n=>n.GetProperty("id").GetInt32());
    var arcs=links.Where(l=>l.TryGetProperty("r",out _)).ToArray();
    Check(arcs.Length>100,"Map preserves upstream orbital curves");
    Check(arcs.All(l=>{
        var a=lookup[l.GetProperty("a").GetInt32()];var b=lookup[l.GetProperty("b").GetInt32()];
        var r=l.GetProperty("r").GetDouble();var start=l.GetProperty("start").GetDouble();var end=start+l.GetProperty("sweep").GetDouble();
        bool At(JsonElement node,double angle)=>Math.Abs(node.GetProperty("x").GetDouble()-(l.GetProperty("cx").GetDouble()+Math.Cos(angle)*r))<.01 && Math.Abs(node.GetProperty("y").GetDouble()-(l.GetProperty("cy").GetDouble()+Math.Sin(angle)*r))<.01;
        return At(a,start)&&At(b,end);
    }),"Every orbital curve meets both node positions");
    Check(!links.Any(l=>starts.Contains(l.GetProperty("a").GetInt32()) && starts.Contains(l.GetProperty("b").GetInt32())),"Virtual class-to-ascendancy root links are not drawn");
}
if(firstXml is not null)
{
    var data=Path.Combine(Path.GetTempPath(),"widgetrail-pob-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
    var id=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(data,id+".xml"),firstXml);File.WriteAllText(Path.Combine(data,"selected.txt"),id);
    for(var copy=0;copy<11;copy++)File.WriteAllText(Path.Combine(data,Guid.NewGuid().ToString("N")+".xml"),firstXml);
    var clipboard="";
    var widget=new PobWidget(game,engine,data,()=>clipboard);
    WidgetTestHost.Attach(widget,new WidgetTestHostServicesBuilder().Build());
    await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Interactive);
    for(var n=0;n<150&&!Nodes(Snapshot(widget,n).Root).Any(v=>v.Id=="pob.section");n++)await Task.Delay(100);
    for(var section=0;section<6;section++)
    {
        await widget.OnActionAsync(new("section."+section,"pob.section"));
        for (var n=0; n<100 && Nodes(Snapshot(widget,n).Root).Any(v=>v.Id=="pob.cancel-operation");n++) await Task.Delay(50);
        var snapshot=Snapshot(widget,section);
        Check(Nodes(snapshot.Root).Any(v=>v.Id=="pob.headlines"),"Section "+section+" retains build totals");
        Check(snapshot.FocusGroupEntryRequest is not null && Nodes(snapshot.Root).Any(v=>v.Transition?.GroupId=="pob.sections"),
            "Section "+section+" declares content focus and a shared section transition");
        if(section is 2 or 3)
            Check(Nodes(snapshot.Root).Where(v=>v.Kind==ViewNodeKind.ActionSurface&&v.Id.StartsWith(section==2?"pob.skill.":"pob.item.")).All(v=>v.Focus?.Right=="pob.detail"),
                "Inventory rows explicitly link Right to their detail focus group");
        if (section is 2 or 3) Check(Nodes(snapshot.Root).Any(v=>v.Id=="pob.workspace.inventory") && Nodes(snapshot.Root).Any(v=>v.Id=="pob.detail"),"Section "+section+" keeps list and details together");
        if (section == 0) Check(Nodes(snapshot.Root).Count(v=>v.Id.StartsWith("pob.group."))>=4,"Overview categorizes statistics");
    }
    async Task Action(string action, string? text=null)
    {
        await widget.OnActionAsync(new(action,"pob.test"){CommittedText=text});
        var limit=Stopwatch.StartNew();
        while(Nodes(Snapshot(widget,100).Root).Any(n=>n.Id=="pob.cancel-operation"))
        { if(limit.Elapsed>TimeSpan.FromSeconds(65))throw new TimeoutException(action);await Task.Delay(25); }
    }
    await Action("section.0");
    var calculationView=Snapshot(widget,280);
    var auditState=await engine.ExecuteAsync(new{op="load",xml=firstXml,name="presentation audit"},default);
    if(auditState.State.GetProperty("mainSkillDisabled").GetBoolean())
        Check(Nodes(calculationView.Root).Any(n=>n.Id=="pob.stat.value.CombinedDPS"&&n.Text=="—") &&
            Nodes(calculationView.Root).Any(n=>n.Id=="pob.main-skill.warning"&&n.Text!.Contains(auditState.State.GetProperty("mainSkillDisabledReason").GetString()!)),
            "Disabled main skill shows unavailable DPS and the actual upstream reason together");
    if(auditState.State.GetProperty("mainSkillDamageKind").GetString()=="buff")
        Check(Nodes(calculationView.Root).Any(n=>n.Id=="pob.stat.value.CombinedDPS"&&n.Text=="—")&&Nodes(calculationView.Root).Any(n=>n.Id=="pob.main-skill.explanation")&&!Nodes(calculationView.Root).Any(n=>n.Id=="pob.main-skill.warning"),"Buff-only main skill explains no direct damage without inventing an error");
    if(auditState.State.GetProperty("fullDpsIncludedGroups").GetInt32()==0)
        Check(Nodes(calculationView.Root).Any(n=>n.Id=="pob.stat.value.FullDPS"&&n.Text=="—")&&Nodes(calculationView.Root).Any(n=>n.Id=="pob.full-dps.explanation"),"Unconfigured Full DPS is distinguished from a legitimate zero");
    await Action("menu");await Action("library");
    using(var libraryHost=WidgetTestHost.CreateIndexedCollectionHost(widget,"pob-library-audit",400))
    {
        Check(Nodes(libraryHost.CurrentSnapshot.Root).Single(n=>n.Id=="pob.saved-builds").IndexedCollection?.Count==12,"Saved builds expose every entry through the indexed library");
        using var lease=await libraryHost.AcquireAsync("pob.saved-builds",9,3);
        Check(lease.Range.Items.Count==3,"Saved builds beyond the first nine are available without pagination");
    }
    await Action("back");
    Check(Nodes(Snapshot(widget,281).Root).Any(n=>n.Id=="pob.rename"),"Library Back returns to Build and import");
    await Action("back");
    await Action("section.3");
    Check(Nodes(Snapshot(widget,285).Root).Any(n=>n.Kind==ViewNodeKind.RichText),"Item inspector renders upstream inline text runs");
    await Action("section.1");
    await Action("search","life");
    using(var collectionHost=WidgetTestHost.CreateIndexedCollectionHost(widget,"pob-list-audit",500))
    {
        var list=Nodes(collectionHost.CurrentSnapshot.Root).Single(n=>n.Id=="pob.passives");
        Check(list.IndexedCollection?.Count>9,"Passive search exposes results beyond the old page size");
        using var lease=await collectionHost.AcquireAsync("pob.passives",9,3);
        var row=lease.Range.Items[1];
        Check(lease.Range.Items.Count==3,"Passive collection reads only its requested offscreen range");
        Check(lease.RouteAction(row.Key,ControllerButton.A)==WidgetOperationAdmission.Enqueued,"Offscreen passive action is admitted through its captured row lease");
        for(var n=0;n<100&&!Nodes(collectionHost.PublishSnapshot().Root).Any(v=>v.Id=="pob.node.name");n++)await Task.Delay(20);
        Check(Nodes(collectionHost.CurrentSnapshot.Root).Any(v=>v.Id=="pob.node.name"),"Leased passive action opens its inspector");
        await widget.OnActionAsync(new("back","pob.back"));
        var returned=collectionHost.PublishSnapshot();
        Check(returned.FocusGroupEntryRequest?.GroupId=="pob.passives","Back explicitly restores focus to the virtualized passive collection");
        await widget.OnActionAsync(new("search","pob.search"){CommittedText="no-matching-passive-xyz"});
        var empty=collectionHost.PublishSnapshot();
        Check(Nodes(empty.Root).Single(n=>n.Id=="pob.passives").IndexedCollection?.Count==0 && lease.RouteAction(row.Key,ControllerButton.A) is null,"Search replaces membership and retires stale passive actions");
    }
    await Action("section.3");
    var beforeFeedback=Snapshot(widget,290);
    clipboard=new string('x',32769);
    await Action("item-paste");
    var withFeedback=Snapshot(widget,291);
    Check(Nodes(withFeedback.Root).Any(n=>n.Id=="pob.feedback.toast") &&
        !Nodes(withFeedback.Root).Any(n=>n.Id=="pob.status") &&
        withFeedback.ActiveInputScopeId==beforeFeedback.ActiveInputScopeId && withFeedback.InitialFocusId==beforeFeedback.InitialFocusId,
        "Recoverable action errors use a toast without changing content focus or persistent status");
    Check(Nodes(withFeedback.Root).Any(n=>n.Kind==ViewNodeKind.Text && n.Text?.Contains("Inspecting")==true),
        "The item being inspected has a textual selection marker independent of focus");
    await Task.Delay(PobWidget.InformationFeedbackDuration+TimeSpan.FromMilliseconds(100));
    Check(Nodes(Snapshot(widget,2911).Root).Any(n=>n.Id=="pob.feedback.toast"),"Errors stay visible longer than informational messages");
    await Task.Delay(PobWidget.ErrorFeedbackDuration-PobWidget.InformationFeedbackDuration+TimeSpan.FromMilliseconds(150));
    Check(!Nodes(Snapshot(widget,292).Root).Any(n=>n.Id=="pob.feedback.toast"),"Transient feedback expires automatically");
    clipboard="";
    await Action("section.4");
    var configuration=await engine.ExecuteAsync(new{op="load",xml=firstXml,name="configuration fixture"},default);
    var exportedConditions=configuration.State.GetProperty("conditions").EnumerateArray().ToArray();
    var categories=exportedConditions.Select(c=>c.GetProperty("section").GetString()).Distinct().ToArray();
    var observed=new HashSet<string>();
    for(var category=0;category<categories.Length;category++)
    {
        await Action("config-section."+category);
        var configSnapshot=Snapshot(widget,300+category);
        var visible=Nodes(configSnapshot.Root).ToArray();
        Check(visible.Any(n=>n.Id=="pob.config.title"&&n.Text==categories[category]),"Configuration preserves section "+categories[category]);
        Check(!visible.Any(n=>n.Id is "pob.page-next" or "pob.page-previous"),"Configuration section scrolls without paging");
        foreach(var node in visible.Where(n=>n.Id.StartsWith("pob.config.row.")))observed.Add(node.Id);
    }
    Check(observed.Count==exportedConditions.Length,"Every exported condition renders in its upstream section");
    var multilineChoiceCount=0;
    for(var multilineIndex=0;multilineIndex<exportedConditions.Length;multilineIndex++)
    {
        var condition=exportedConditions[multilineIndex];
        if(!condition.TryGetProperty("options",out var choices))continue;
        var choiceIndex=0;
        foreach(var choice in choices.EnumerateArray())
        {
            if((choice.GetProperty("label").GetString()??"").Any(char.IsControl))
            {
                multilineChoiceCount++;
                await Action($"option.{multilineIndex}.{choiceIndex}");
                var saved=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(Path.Combine(data,id+".xml")),name="multiline choice"},default);
                var actual=saved.State.GetProperty("conditions").EnumerateArray().Single(c=>c.GetProperty("id").GetString()==condition.GetProperty("id").GetString()).GetProperty("value");
                Check(actual.GetRawText()==choice.GetProperty("value").GetRawText(),"Multiline choice labels preserve the exact backend value after immediate save and reload");
                await Action("undo");
                break;
            }
            choiceIndex++;
        }
    }
    if(game==2)Check(multilineChoiceCount>0,"PoE 2 fixture exercises upstream multiline quest choices");
    await Action("search","Enemy");
    Check(Nodes(Snapshot(widget,350).Root).Any(n=>n.Id=="pob.config.title"&&n.Text=="Search results"),"Configuration search spans sections");
    await Action("config-help");Snapshot(widget,351);
    await Action("search","no-matching-condition-xyz");
    Check(Nodes(Snapshot(widget,352).Root).Any(n=>n.Id=="pob.config.empty"),"Empty configuration search stays usable");
    await Action("config-clear-search");
    await Action("section.5");
    Check(!Nodes(Snapshot(widget,353).Root).Any(n=>n.Id is "pob.page-next" or "pob.page-previous"),"Notes use a continuous scroll view");
    var working=Path.Combine(data,id+".xml");
    var originalSaved=File.ReadAllText(working);
    var widgetBaseline=await engine.ExecuteAsync(new{op="load",xml=firstXml,name="widget baseline"},default);
    var allocationNode=widgetBaseline.State.GetProperty("nodes").EnumerateArray().First(n=>n.GetProperty("canAllocate").GetBoolean()&&n.GetProperty("pathCount").GetInt32() is >=2 and <=5);
    await Action("section.1");
    await Action("node."+allocationNode.GetProperty("id").GetInt32());await Action("node-change");
    var previewView=Snapshot(widget,1000);
    Check(!Nodes(previewView.Root).Any(n=>n.Id.StartsWith("pob.changed."))&&Nodes(previewView.Root).Any(n=>n.Id=="pob.preview-nodes"),
        "Tree preview starts with overall impact and collapsed changed-node details");
    Check(Nodes(previewView.Root).Any(n=>n.Id=="pob.node.comparison.heading"),"Tree preview uses the upstream comparison summary");
    await Action("preview-nodes");previewView=Snapshot(widget,10001);
    var allocatedPreview=await engine.ExecuteAsync(new{op="load",xml=PobWorkStore.Read(data,id,game)!.Pending!.Xml,name="verify allocation preview"},default);
    CheckPassiveDescriptions(widgetBaseline.State,allocatedPreview.State,previewView,Check);
    var previewPanel=Nodes(previewView.Root).Single(n=>n.Id=="pob.tree.side");
    Check(previewView.FocusGroupEntryRequest?.GroupId=="pob.tree.side" &&
        Nodes(previewPanel).Any(n=>n.Id=="pob.apply") && Nodes(previewPanel).Any(n=>n.Id=="pob.cancel"),
        "Tree preview explicitly enters the panel containing Apply and Discard");
    Check(!Nodes(previewView.Root).Any(n=>n.Id=="pob.preview") &&
        Nodes(previewView.Root).Single(n=>n.Id=="pob.tab.4").IsDisabled==true,
        "Tree preview keeps actions beside details and disables unrelated navigation");
    var previewEntry=previewView.FocusGroupEntryRequest?.RequestId;
    Check(Nodes(Snapshot(widget,101).Root).Any(n=>n.Id=="pob.apply")&&File.ReadAllText(working)==originalSaved,"Widget allocation preview does not mutate saved working copy");
    await Action("cancel");
    Check(Snapshot(widget,1001).FocusGroupEntryRequest?.RequestId>previewEntry,"Discard explicitly restores the node-inspector focus group");
    Check(!Nodes(Snapshot(widget,102).Root).Any(n=>n.Id=="pob.apply")&&File.ReadAllText(working)==originalSaved,"Widget Discard preserves the working file");
    await Action("node-change");await Action("apply");
    var appliedSaved=File.ReadAllText(working);
    var appliedResult=await engine.ExecuteAsync(new{op="load",xml=appliedSaved,name="verify saved"},default);
    Check(Allocated(appliedResult.State).Contains(allocationNode.GetProperty("id").GetInt32()),"Widget Apply persists allocated path to disk");
    await Action("undo");
    var undoneResult=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="verify undo"},default);
    Check(Allocated(undoneResult.State).SetEquals(Allocated(widgetBaseline.State))&&SameStats(undoneResult.State,widgetBaseline.State),"Widget Undo persists original allocations and totals");
    var refundNode=widgetBaseline.State.GetProperty("nodes").EnumerateArray().First(n=>n.GetProperty("canRefund").GetBoolean()&&n.GetProperty("refundCount").GetInt32() is >=2 and <=5);
    await Action("node."+refundNode.GetProperty("id").GetInt32());await Action("node-change");
    var refundedPreview=await engine.ExecuteAsync(new{op="load",xml=PobWorkStore.Read(data,id,game)!.Pending!.Xml,name="verify refund preview"},default);
    Check(!Nodes(Snapshot(widget,10020).Root).Any(n=>n.Id.StartsWith("pob.changed.")),"A new refund resets changed-node details to collapsed");
    await Action("preview-nodes");
    CheckPassiveDescriptions(widgetBaseline.State,refundedPreview.State,Snapshot(widget,1002),Check);
    await Action("apply");
    var refundedFile=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="verify refund"},default);
    Check(Allocated(widgetBaseline.State).Except(Allocated(refundedFile.State)).Count()==refundNode.GetProperty("refundCount").GetInt32(),"Widget refund Apply persists all dependent removals");
    await Action("undo");
    var firstSkill=widgetBaseline.State.GetProperty("skills").EnumerateArray().First();
    await Action("section.2");
    await Action("skill."+firstSkill.GetProperty("id").GetInt32());await Action("skill-enabled");
    var immediateSkill=Snapshot(widget,103);
    Check(!Nodes(immediateSkill.Root).Any(n=>n.Id=="pob.apply")&&Nodes(immediateSkill.Root).Single(n=>n.Id=="pob.undo").IsDisabled!=true,
        "Routine skill edits save immediately and expose Undo without a preview step");
    var skillResult=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="verify skill"},default);
    Check(skillResult.State.GetProperty("skills").EnumerateArray().First().GetProperty("enabled").GetBoolean()!=firstSkill.GetProperty("enabled").GetBoolean(),"Widget skill toggle survives reload");
    await Action("undo");
    Check(Snapshot(widget,1031).ActiveInputScopeId==immediateSkill.ActiveInputScopeId&&Nodes(Snapshot(widget,1032).Root).Any(n=>n.Id=="pob.workspace.inventory"),"Undo preserves the current skill workspace and input scope");
    Check(Snapshot(widget,10311).FocusGroupEntryRequest?.GroupId=="pob.history","Undo restores focus within the history controls rather than the first section tab");
    var undoneSkill=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="undo skill"},default);
    Check(undoneSkill.State.GetProperty("skills").EnumerateArray().First().GetProperty("enabled").GetBoolean()==firstSkill.GetProperty("enabled").GetBoolean(),"Undo restores the saved skill state");
    await Action("redo");
    var redoneSkill=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="redo skill"},default);
    Check(redoneSkill.State.GetProperty("skills").EnumerateArray().First().GetProperty("enabled").GetBoolean()!=firstSkill.GetProperty("enabled").GetBoolean(),"Redo reapplies the saved skill state");
    await Action("undo");
    await Action("level","76");
    Check(Nodes(Snapshot(widget,1033).Root).Single(n=>n.Id=="pob.redo").IsDisabled==true,"A new committed edit clears the redo branch");
    Check(XDocument.Parse(File.ReadAllText(working)).Root!.Element("Build")!.Attribute("level")!.Value=="76","Widget level edit survives reload");
    await Action("undo");
    var conditions=widgetBaseline.State.GetProperty("conditions").EnumerateArray().ToArray();
    var conditionIndex=Array.FindIndex(conditions,c=>c.GetProperty("value").ValueKind is JsonValueKind.True or JsonValueKind.False);
    Check(conditionIndex>=0,"Fixture supplies a boolean configuration condition");
    if(conditionIndex>=0)
    {
        await Action("condition."+conditionIndex);
        var changedConfig=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="verify config"},default);
        var key=conditions[conditionIndex].GetProperty("id").GetString();
        var value=changedConfig.State.GetProperty("conditions").EnumerateArray().Single(c=>c.GetProperty("id").GetString()==key).GetProperty("value").GetBoolean();
        Check(value!=conditions[conditionIndex].GetProperty("value").GetBoolean()&&!Nodes(Snapshot(widget,1034).Root).Any(n=>n.Id=="pob.apply"),"Configuration edit saves immediately without confirmation");
        await Action("undo");await Action("redo");
        var redoneConfig=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="redo configuration"},default);
        Check(redoneConfig.State.GetProperty("conditions").EnumerateArray().Single(c=>c.GetProperty("id").GetString()==key).GetProperty("value").GetBoolean()==value,"Configuration Undo and Redo restore the saved value");
        await Action("undo");
    }
    foreach(var candidate in widgetBaseline.State.GetProperty("skills").EnumerateArray().Where(s=>!s.GetProperty("main").GetBoolean()).Take(8))
    {
        var group=candidate.GetProperty("id").GetInt32();
        var inspected=await engine.ExecuteAsync(new{op="inspect-skill",group},default,File.ReadAllText(working));
        var option=inspected.State.GetProperty("skillOptions").EnumerateArray().FirstOrDefault();
        if(option.ValueKind!=JsonValueKind.Object)continue;
        var control=option.GetProperty("id").GetString();
        var choices=option.GetProperty("options").EnumerateArray().ToArray();
        var original=choices.Single(c=>c.GetProperty("active").GetBoolean()).GetProperty("id").GetInt32();
        var alternative=choices.First(c=>!c.GetProperty("active").GetBoolean()).GetProperty("id").GetInt32();
        await Action("section.2");await Action("skill."+group);await Action("skill-options");
        var optionScope=Snapshot(widget,1035).ActiveInputScopeId;
        await Action($"skill-option.{group}.{control}.{alternative}");
        var changed=await engine.ExecuteAsync(new{op="inspect-skill",group},default,File.ReadAllText(working));
        int SelectedOption(PobResult current)=>current.State.GetProperty("skillOptions").EnumerateArray().Single(o=>o.GetProperty("id").GetString()==control)
            .GetProperty("options").EnumerateArray().Single(c=>c.GetProperty("active").GetBoolean()).GetProperty("id").GetInt32();
        Check(SelectedOption(changed)==alternative&&!Nodes(Snapshot(widget,1036).Root).Any(n=>n.Id=="pob.apply"),"Non-main skill option saves immediately without changing to preview scope");
        await Action("undo");
        var reverted=await engine.ExecuteAsync(new{op="inspect-skill",group},default,File.ReadAllText(working));
        Check(SelectedOption(reverted)==original&&Snapshot(widget,1037).ActiveInputScopeId==optionScope&&Nodes(Snapshot(widget,1038).Root).Any(n=>n.Id=="pob.option."+control),"Undo refreshes the inspected skill's options in place");
        await Action("redo");
        var repeated=await engine.ExecuteAsync(new{op="inspect-skill",group},default,File.ReadAllText(working));
        Check(SelectedOption(repeated)==alternative,"Redo restores the inspected skill option");
        await Action("undo");await Action("back");break;
    }
    var savedBeforeFailure=File.ReadAllText(working);
    var undoBeforeFailure=Nodes(Snapshot(widget,1039).Root).Single(n=>n.Id=="pob.undo").IsDisabled;
    await Action("level","101");
    Check(File.ReadAllText(working)==savedBeforeFailure&&Nodes(Snapshot(widget,1040).Root).Single(n=>n.Id=="pob.undo").IsDisabled==undoBeforeFailure,"Rejected immediate edit leaves the saved build and undo availability unchanged");
    await Action("section.3");
    var firstItem=widgetBaseline.State.GetProperty("items").EnumerateArray().First();
    clipboard=firstItem.GetProperty("raw").GetString()!;
    var slots=widgetBaseline.State.GetProperty("slots").EnumerateArray().ToArray();
    var slotIndex=Array.FindIndex(slots,s=>s.GetProperty("name").GetString()==firstItem.GetProperty("slot").GetString());
    await Action("item-paste");await Action("replace-slot."+slotIndex);
    Check(Nodes(Snapshot(widget,104).Root).Any(n=>n.Id=="pob.apply"),"Widget item replacement enters comparison");
    Check(Nodes(Snapshot(widget,1041).Root).Any(n=>n.Id=="pob.item.compare.names"&&n.Text!.Contains(firstItem.GetProperty("name").GetString()!)),"Equipment preview identifies the actual replaced item beside the proposed item");
    await Action("apply");
    var changedItems=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="verify item"},default);
    Check(Nodes(Snapshot(widget,1042).Root).Any(n=>n.Id=="pob.workspace.inventory"),"Applying equipment comparison returns to the item workspace");
    var replaced=changedItems.State.GetProperty("items").EnumerateArray().Single(i=>i.GetProperty("slot").GetString()==firstItem.GetProperty("slot").GetString());
    Check(replaced.GetProperty("itemId").GetInt32()!=firstItem.GetProperty("itemId").GetInt32()&&SameStats(changedItems.State,widgetBaseline.State),"Item replacement persists the selected slot without changing equivalent-item totals");
    await Action("undo");
    var loadouts=widgetBaseline.State.GetProperty("loadouts").EnumerateArray().ToArray();
    if(loadouts.Length==1 && new[]{"trees","itemSets","skillSets","configSets"}.All(k=>widgetBaseline.State.GetProperty(k).GetArrayLength()==1))
        Check(loadouts[0].GetProperty("active").GetBoolean(),"Single historical loadout identifies the active coordinated sets");
    var nextLoadout=Array.FindIndex(loadouts,l=>!l.GetProperty("active").GetBoolean());
    if(nextLoadout>=0)
    {
        await Action("loadout."+nextLoadout);
        var switched=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="verify loadout"},default);
        Check(switched.State.GetProperty("loadouts").EnumerateArray().ElementAt(nextLoadout).GetProperty("active").GetBoolean(),"Switching loadouts persists coordinated active sets");
        await Action("undo");
        var undoSet=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="undo loadout"},default);
        Check(SameStats(undoSet.State,widgetBaseline.State),"Set changes participate in Undo without losing earlier edits");
        await Action("redo");
        var redoSet=await engine.ExecuteAsync(new{op="load",xml=File.ReadAllText(working),name="redo loadout"},default);
        Check(SameStats(redoSet.State,switched.State),"Redo restores the coordinated set selection");
    }
    await Action("rename","Controller test build");
    Check(File.ReadAllText(Path.Combine(data,id+".name.txt"))=="Controller test build","Build name persists independently of the imported format");
    var previousWorking=File.ReadAllText(working);
    await Action("new");
    var newId=File.ReadAllText(Path.Combine(data,"selected.txt"));
    Check(newId!=id && File.ReadAllText(working)==previousWorking,"New build preserves the previous working file");
    Check(Nodes(Snapshot(widget,106).Root).Single(n=>n.Id=="pob.undo").IsDisabled==true && Nodes(Snapshot(widget,106).Root).Single(n=>n.Id=="pob.redo").IsDisabled==true,"New build does not inherit another build's undo history");
    await Action("menu");
    await Action("import",working);
    var importedId=File.ReadAllText(Path.Combine(data,"selected.txt"));
    Check(XNode.DeepEquals(XDocument.Parse(previousWorking),XDocument.Parse(File.ReadAllText(Path.Combine(data,importedId+".original.xml")))),"Import retains the original separately from its working copy");
    await Action("saved."+id);
    var beforeCancel=File.ReadAllText(working);
    var beforeWork=Snapshot(widget,1065);
    await widget.OnActionAsync(new("level","pob.level"){CommittedText="77"});
    var duringWork=Snapshot(widget,1066);
    Check(Nodes(duringWork.Root).Any(n=>n.Id=="pob.operation.toast") &&
        !Nodes(duringWork.Root).Any(n=>n.Id=="pob.status") &&
        Nodes(duringWork.Root).Single(n=>n.Id=="pob.root").Children.Select(n=>n.Id).SequenceEqual(Nodes(beforeWork.Root).Single(n=>n.Id=="pob.root").Children.Select(n=>n.Id)),
        "Active calculations float above an unchanged content layout");
    Check(duringWork.ActiveInputScopeId==beforeWork.ActiveInputScopeId&&duringWork.InitialFocusId==beforeWork.InitialFocusId,
        "Progress notification does not change the content scope or initial focus");
    await widget.OnActionAsync(new("cancel-operation","pob.cancel-operation"));
    for(var n=0;n<600&&Nodes(Snapshot(widget,107).Root).Any(v=>v.Id=="pob.cancel-operation");n++)await Task.Delay(25);
    Check(File.ReadAllText(working)==beforeCancel && !Nodes(Snapshot(widget,108).Root).Any(v=>v.Id=="pob.apply"),"Cancelling an active edit preserves the working file without a partial preview");
    Check(Nodes(Snapshot(widget,1081).Root).Any(n=>n.Id=="pob.feedback.toast")&&!Nodes(Snapshot(widget,1082).Root).Any(n=>n.Id=="pob.operation.toast"),"Cancellation replaces progress with informational feedback");
    await Task.Delay(PobWidget.InformationFeedbackDuration+TimeSpan.FromMilliseconds(150));
    Check(!Nodes(Snapshot(widget,1083).Root).Any(n=>n.Id=="pob.feedback.toast"),"Informational feedback expires on its shorter schedule");
    await Action("level","78");
    Check(XDocument.Parse(File.ReadAllText(working)).Root!.Element("Build")!.Attribute("level")!.Value=="78","An edit after cancellation restores the engine session and saves correctly");
    await Action("undo");
    await using(var reopenedEngine=new PobEngine(engineRoot,runtime,adapter))
    {
        var reopened=new PobWidget(game,reopenedEngine,data);
        WidgetTestHost.Attach(reopened,new WidgetTestHostServicesBuilder().Build());
        await WidgetTestHost.SetLifecycleStateAsync(reopened,WidgetLifecycleState.Interactive);
        for(var n=0;n<600&&!Nodes(Snapshot(reopened,109).Root).Any(v=>v.Id=="pob.section");n++)await Task.Delay(25);
        Check(Nodes(Snapshot(reopened,110).Root).Any(v=>v.Id=="pob.title"&&v.Text=="Controller test build"),"A fresh widget and Lua process reopen the selected named working copy");
        await WidgetTestHost.DestroyAsync(reopened);
    }
    using(var cancelled=new CancellationTokenSource())
    {
        cancelled.Cancel();
        try {await widget.OnActionAsync(new("level","pob.level"){CommittedText="77"},cancelled.Token);Check(false,"Cancelled widget action is rejected");}
        catch(OperationCanceledException){Check(true,"Cancelled widget action is rejected");}
    }
    await WidgetTestHost.SetLifecycleStateAsync(widget,WidgetLifecycleState.Background);
}
await Task.Delay(1000); // Let the final post-response collection complete before measuring actual idle.
using(var process=Process.GetProcessById(engine.ProcessId!.Value)){var cpu=process.TotalProcessorTime;await Task.Delay(1500);process.Refresh();Console.WriteLine($"Idle CPU delta {(process.TotalProcessorTime-cpu).TotalMilliseconds:F1} ms over 1500 ms; working set {process.WorkingSet64/1048576.0:F1} MiB");}
await engine.DisposeAsync();Check(engine.ProcessId is null,"Worker retires on disposal");
Console.WriteLine(JsonSerializer.Serialize(new{game,passed=results.Count-failures,failed=failures,results}));return failures==0?0:1;
static Dictionary<string,double> Stats(JsonElement s)=>s.GetProperty("stats").EnumerateArray().ToDictionary(x=>x.GetProperty("name").GetString()!,x=>x.GetProperty("value").GetDouble());
static bool SameStats(JsonElement a,JsonElement b){var other=Stats(b);return Stats(a).All(p=>other.TryGetValue(p.Key,out var n)&&Math.Abs(n-p.Value)<=Math.Max(1,Math.Abs(p.Value))*1e-8);}
static IEnumerable<ViewNode> Nodes(ViewNode node){yield return node;foreach(var child in node.Children??[])foreach(var nested in Nodes(child))yield return nested;}
static HashSet<int> Allocated(JsonElement state)=>state.GetProperty("nodes").EnumerateArray().Where(n=>n.GetProperty("allocated").GetBoolean()).Select(n=>n.GetProperty("id").GetInt32()).ToHashSet();
static void CheckMap(JsonElement state,JsonElement before,string change,int expected,Action<bool,string> check)
{
    var messages=TreeMapMessages.Create(state,before,true,0);
    check(messages.All(m=>JsonSerializer.SerializeToUtf8Bytes(m).Length<=WebSessionContract.MaximumMessageBytes),"All "+change+" map messages fit the host limit");
    var projected=messages.Where(m=>m.GetProperty("type").GetString()=="nodes").SelectMany(m=>m.GetProperty("items").EnumerateArray());
    check(projected.Count(n=>n.GetProperty("change").GetString()==change)==expected,"Map marks every "+change+" preview node");
    check(messages.Last().GetProperty("preview").GetBoolean(),"Map receives explicit preview state");
    check(TreeMapMessages.GeometryKey(state)==TreeMapMessages.GeometryKey(before),"Allocation changes retain the same authoritative map geometry");
    var delta=TreeMapMessages.Create(state,before,true,0,reuseGeometry:true,revision:42,previousMapState:before);
    var states=delta.Where(m=>m.GetProperty("type").GetString()=="states").SelectMany(m=>m.GetProperty("items").EnumerateArray()).ToArray();
    check(states.Where(n=>n.GetProperty("allocated").GetBoolean()).Select(n=>n.GetProperty("id").GetInt32()).ToHashSet().SetEquals(Allocated(state)),"Incremental map reconstructs the complete allocation state");
    check(states.Count(n=>n.GetProperty("change").GetString()==change)==expected,"Incremental map retains every "+change+" marker");
    var labels=before.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("id").GetInt32(),n=>(n.GetProperty("name").GetString(),n.GetProperty("kind").GetString()));
    foreach(var n in states)labels[n.GetProperty("id").GetInt32()]=(n.GetProperty("name").GetString(),n.GetProperty("kind").GetString());
    check(state.GetProperty("nodes").EnumerateArray().All(n=>labels[n.GetProperty("id").GetInt32()]==(n.GetProperty("name").GetString(),n.GetProperty("kind").GetString())),"Incremental map preserves dynamic node names and shapes");
    check(delta.All(m=>m.GetProperty("revision").GetInt64()==42)&&delta.All(m=>JsonSerializer.SerializeToUtf8Bytes(m).Length<=WebSessionContract.MaximumMessageBytes),"Incremental transaction has bounded messages and one revision");
    check(delta.Sum(m=>JsonSerializer.SerializeToUtf8Bytes(m).Length)<messages.Sum(m=>JsonSerializer.SerializeToUtf8Bytes(m).Length)/10,"Preview transfers less than one tenth of a full tree");
}

static void CheckPassiveDescriptions(JsonElement before,JsonElement after,ViewSnapshot view,Action<bool,string> check)
{
    var oldIds=Allocated(before);var newIds=Allocated(after);
    var added=newIds.Except(oldIds).ToHashSet();var removed=oldIds.Except(newIds).ToHashSet();
    var cards=Nodes(view.Root).Where(n=>n.Id.StartsWith("pob.changed.")&&n.Id.EndsWith(".card")).ToArray();
    check(cards.Length==added.Count+removed.Count,"Preview includes every changed passive without truncating the list");
    foreach(var (source,ids) in new[]{(after,added),(before,removed)})
        foreach(var node in source.GetProperty("nodes").EnumerateArray().Where(n=>ids.Contains(n.GetProperty("id").GetInt32())))
        {
            var prefix="pob.changed."+node.GetProperty("id").GetInt32()+".description";
            var text=string.Concat(Nodes(view.Root).Where(n=>n.Kind==ViewNodeKind.Text&&n.Id.StartsWith(prefix)).Select(n=>n.Text));
            static string Normalize(string s)=>string.Concat(s.Where(c=>!char.IsWhiteSpace(c)));
            check(Normalize(text)==Normalize(node.GetProperty("description").GetString()??""),
                (ids==removed?"Refunded":"Allocated")+" passive description preserves upstream modifier values for "+node.GetProperty("id"));
        }
}

static ViewSnapshot Snapshot(PobWidget widget,int sequence)
{
    try{return widget.Render().CreateSnapshot("pob-test",sequence);}
    catch(ProtocolValidationException ex){Console.WriteLine(JsonSerializer.Serialize(ex.Errors));throw;}
}
