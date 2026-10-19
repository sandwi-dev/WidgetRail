using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class RowPreviewTests
{
    internal static async Task RunAsync(int game, string root, string runtime, string adapter, string xml, Action<bool, string> check)
    {
        await using var engine = new PobEngine(root, runtime, adapter);
        var directory = Path.Combine(Path.GetTempPath(), "pob-row-previews-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var build=Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory, build+".xml"), xml);
        File.WriteAllText(Path.Combine(directory, "selected.txt"), build);
        var widget = new PobWidget(game, engine, directory);
        WidgetTestHost.Attach(widget, new WidgetTestHostServicesBuilder().Build());
        object? Get(string name) => typeof(PobWidget).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget);
        async Task Idle()
        {
            var watch = Stopwatch.StartNew();
            while ((bool)Get("busy")!)
            {
                if (watch.Elapsed.TotalSeconds > 65) throw new TimeoutException("Build did not settle.");
                await Task.Delay(20);
            }
        }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
            await Idle();
            foreach (var (section, name) in new[] { (2, "skills"), (3, "equipment") })
            {
                await widget.OnActionAsync(new("section." + section, "test"));
                await Idle();
                using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "row-previews-" + name);
                var snapshot = host.CurrentSnapshot;
                var surface = Nodes(snapshot.Root).Single(n => n.Id == "pob.rows.preview");
                var rows = Nodes(surface).Where(n => n.FocusPreviewKey is not null).ToArray();
                check(rows.Length > 0 && rows.All(n => n.FocusPresentation is null), name + " rows publish keys only");
                check(surface.FocusPreview?.EnterButton is null, name + " preview is read-only");
                var before = Get("result"); var selection = Get("detailId");
                using var lease = await host.AcquirePreviewAsync(surface.Id, rows[0].FocusPreviewKey!);
                var content = lease.Range.Items.Single().Root;
                check(!Nodes(content).Any(n => n.ActionId is not null || n.Shortcuts.Count > 0), name + " preview contains no editing commands");
                check(ReferenceEquals(before, Get("result")) && Equals(selection, Get("detailId")), name + " preview does not change selection or build");
                check(lease.RoutePreviewAction(rows[0].FocusPreviewKey!, content.Id, "apply") is null, name + " rejects forged preview actions");
                var output = Environment.GetEnvironmentVariable("POB_CANDIDATE_INSPECTION_FIXTURES");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    ViewNode Materialize(ViewNode node) => node.Id == rows[0].Id
                        ? node with { FocusPreviewKey = null, FocusPresentation = content }
                        : node with { Children = node.Children.Select(Materialize).ToArray(), FocusPreviewKey = null };
                    File.WriteAllBytes(Path.Combine(output, $"poe{game}-{name}-readonly.json"), SnapshotJson.Serialize(snapshot with { Root = Materialize(snapshot.Root) }));
                }
            }
            var group = JsonSerializer.SerializeToElement(new
            {
                id = 99, title = "Granted test group", enabled = false, main = true, generated = true, sourceName = "Example item", fullDps = true,
                gems = Enumerable.Range(0, 9).Select(i => new { name = "Gem " + i, level = 20, quality = 10, enabled = i != 0, count = 2 }).ToArray(),
            });
            var summary = Invoke("SkillGroupPreview", group);
            var text = string.Join(" ", Nodes(summary).Select(n => n.Text));
            check(text.Contains("Disabled") && text.Contains("Main skill") && text.Contains("Granted by Example item") && text.Contains("+ 3 more gems"), "Generated, disabled and long groups retain meaningful context");
            var empty = Invoke("SkillGroupPreview", JsonSerializer.SerializeToElement(new { id = 100, title = "", gems = Array.Empty<object>() }));
            check(Nodes(empty).Any(n => n.Text == "No gems in this group"), "Empty group preview explains its empty state");
            var item = JsonSerializer.SerializeToElement(new { id = 99, name = "Test item", rarity = "RARE", @base = "Helmet", slot = "Helmet", details = new[] { new { lines = new[] { new { text = "+25 maximum Life", tone = "blue", runs = new[] { new { text = "+25", tone = "gold" }, new { text = " maximum Life", tone = "blue" } } } } } } });
            var equipment = Invoke("EquipmentSummaryPreview", item);
            check(Nodes(equipment).Any(n => n.Kind == ViewNodeKind.RichText) && Nodes(equipment).Any(n => n.StyleClasses.Contains("pob-gold")), "Equipment summary preserves rich modifier colors");
            await widget.OnActionAsync(new("section.1","test"));await Idle();
            var passiveBuild=(PobResult)Get("result")!;
            var passiveNodes=passiveBuild.State.GetProperty("nodes").EnumerateArray().Where(node=>node.GetProperty("interactive").GetBoolean()).ToArray();
            var repeatedName=passiveNodes.GroupBy(node=>node.GetProperty("name").GetString()!).First(group=>group.Count()>1).Key;
            foreach(var search in new[]{"",repeatedName})
            {
                await widget.OnActionAsync(new("search","test"){CommittedText=search});await Idle();
                using var host=WidgetTestHost.CreateIndexedCollectionHost(widget,"passive-previews");
                var snapshot=host.CurrentSnapshot;
                var surface=Nodes(snapshot.Root).Single(node=>node.Id=="pob.passives.preview");
                var list=Nodes(snapshot.Root).Single(node=>node.Id=="pob.passives");
                using var range=await host.AcquireAsync(list.Id,0,Math.Min(64,list.IndexedCollection!.Count));
                check(range.Range.Items.Count>0&&range.Range.Items.All(row=>row.Root.FocusPreviewKey==row.Key&&row.Root.FocusPresentation is null),
                    "Passive rows carry exact node keys without pre-rendering descriptions: "+search);
                check(surface.FocusPreview is {EnterButton:null,SizeToContentHeight:true},"Passive preview is read-only and fits its content");
                foreach(var row in range.Range.Items.Take(2))
                {
                    var selection=Get("detailId");
                    using var preview=await host.AcquirePreviewAsync(surface.Id,row.Key);
                    var content=preview.Range.Items.Single().Root;
                    var node=passiveNodes.Single(node=>"node."+node.GetProperty("id").GetInt32()==row.Key);
                    check(Nodes(content).Any(n=>n.Text==node.GetProperty("name").GetString())&&Nodes(content).Any(n=>n.Kind==ViewNodeKind.RichText),
                        "Passive preview resolves exact node identity and formatted description: "+row.Key);
                    check(ReferenceEquals(passiveBuild,Get("result"))&&Equals(selection,Get("detailId"))&&!Nodes(content).Any(n=>n.ActionId is not null||n.Shortcuts.Count>0)&&row.Root.ActionId=="open",
                        "Reading a passive keeps A navigation and does not allocate or select it");
                    check(preview.RoutePreviewAction(row.Key,content.Id,"node-change") is null,"Passive preview rejects forged allocation actions");
                }
                var generation=surface.FocusPreviewSource!.QueryGeneration;
                await widget.OnActionAsync(new("search","test"){CommittedText=search+" no match fixture"});await Idle();host.PublishSnapshot();
                check(Nodes(host.CurrentSnapshot.Root).Single(n=>n.Id==surface.Id).FocusPreviewSource!.QueryGeneration>generation,
                    "Changing passive search retires the old preview source");
                check(range.RouteAction(range.Range.Items[0].Key,ControllerButton.A) is null,"Old passive rows cannot navigate after the search changes");
            }
            var granted=Invoke("PassivePreview",JsonSerializer.SerializeToElement(new{id=99,name="Granted passive",granted=true,description="+10 to Intelligence",socketedItem="Test jewel",canChoose=true}));
            check(Nodes(granted).Any(n=>n.Text=="Granted")&&Nodes(granted).Any(n=>n.Text=="Socketed: Test jewel")&&Nodes(granted).Any(n=>n.Text=="Effect choice available"),"Granted/socketed/choice passive context is readable without editing commands");
            var longPassive=Invoke("PassivePreview",JsonSerializer.SerializeToElement(new{id=100,name="Long passive",description=string.Join("\n",Enumerable.Repeat(new string('x',700),8)),canAllocate=true,pathCount=3}));
            check(Nodes(longPassive).Any(n=>n.Text=="Open passive for the full description")&&Nodes(longPassive).Any(n=>n.Text=="Allocation path: 3 node(s)"),"Long passive previews remain bounded and disclose the full-description route");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); Directory.Delete(directory, true); }
    }

    private static ViewNode Invoke(string method, JsonElement value)
    {
        var content = (WidgetElement)typeof(PobWidget).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [value])!;
        // Validation uses the same independent content contract as the worker.
        var root = new WidgetView(content).CreateSnapshot("summary-test", 1).Root;
        FocusPreviewContentContract.Validate(root, false);
        return root;
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
}
