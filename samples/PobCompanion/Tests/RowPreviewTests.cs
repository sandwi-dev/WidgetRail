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
