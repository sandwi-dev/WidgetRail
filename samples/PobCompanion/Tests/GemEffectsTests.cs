using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class GemEffectsTests
{
    internal static async Task RunAsync(int game, string root, string runtime, string adapter, Action<bool, string> check)
    {
        await using var engine = new PobEngine(root, runtime, adapter);
        var build = await engine.ExecuteAsync(new { op = "new" }, default);
        var groups = new List<int>();
        for (var index = 0; index < 2; ++index)
        {
            build = await engine.ExecuteAsync(new { op = "skill-group", mode = "create" }, default, build.Xml);
            var group = build.State.GetProperty("editedSkillGroup").GetInt32(); groups.Add(group);
            var catalog = await engine.ExecuteAsync(new { op = "gem-catalog", group, gem = 1 }, default, build.Xml);
            var gems = catalog.State.GetProperty("gemCatalog").EnumerateArray().ToArray();
            foreach (var gem in new[] { gems.First(g => g.GetProperty("name").GetString() == "Fireball"), gems.First(g => g.GetProperty("support").GetBoolean()) })
                build = await engine.ExecuteAsync(new { op = "gem-manage", mode = "add", group, gemId = gem.GetProperty("id").GetString() }, default, build.Xml);
        }
        build = await engine.ExecuteAsync(new { op = "skill", id = groups[0], field = "main" }, default, build.Xml);
        var baseline = build.Xml;
        foreach (var gem in new[] { 1, 2 })
        {
            var details = await engine.ReadGemDetailsAsync(groups[1], gem, baseline, default, default);
            check(details.State.GetProperty("lines").GetArrayLength() > 0 && details.State.GetProperty("lines").EnumerateArray().Any(l => l.GetProperty("runs").GetArrayLength() > 0),
                "Upstream tooltip supplies formatted details for " + (gem == 1 ? "a non-main active gem" : "its support gem"));
            check(WorkStateTests.SameBuild(details.Xml, baseline), "Reading gem effects preserves the build and main skill");
        }
        var folder = Path.Combine(Path.GetTempPath(), "pob-gem-effects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); var buildId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(folder, buildId + ".xml"), baseline); File.WriteAllText(Path.Combine(folder, "selected.txt"), buildId);
        var widget = WidgetTestHost.Attach(new PobWidget(game, engine, folder), new WidgetTestHostServicesBuilder().Build());
        object? Get(string name) => typeof(PobWidget).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget);
        async Task Idle()
        {
            var timer = Stopwatch.StartNew();
            while ((bool)Get("busy")!) { if (timer.Elapsed.TotalSeconds > 65) throw new TimeoutException(); await Task.Delay(20); }
        }
        async Task Act(string id) { await widget.OnActionAsync(new(id, "gem-effects-test")); await Idle(); }
        ViewSnapshot View() => widget.RenderSnapshot("gem-effects-test", 1);
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive); await Idle();
            await Act("section.2"); await Act("skill." + groups[1]);
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "gem-effects-test");
            var before = (PobResult)Get("result")!;
            foreach (var gem in new[] { 1, 2 })
            {
                host.PublishSnapshot();
                var title = Nodes(host.CurrentSnapshot.Root).Single(n => n.Id == "pob.gem-details." + gem);
                check(title.FocusPreviewKey == gem.ToString() && title.FocusPresentation is null, "Each gem name exposes a deferred effects preview");
                using (var preview = await host.AcquirePreviewAsync("pob.gem-effects", title.FocusPreviewKey!))
                {
                    var content = preview.Range.Items.Single().Root;
                    FocusPreviewContentContract.Validate(content, false);
                    check(Nodes(content).Any(n => n.Kind == ViewNodeKind.RichText) && !Nodes(content).Any(n => n.ActionId is not null), "Gem effect preview is formatted and read-only");
                    check(ReferenceEquals(before, Get("result")), "Preview does not replace the build result");
                    var output = Environment.GetEnvironmentVariable("POB_GEM_EFFECT_FIXTURES");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        ViewNode Materialize(ViewNode node) => node with { FocusPreviewSource = null, FocusPreviewKey = null,
                            FocusPresentation = node.Id == title.Id ? content : node.FocusPresentation, Children = node.Children.Select(Materialize).ToArray() };
                        File.WriteAllBytes(Path.Combine(output, $"poe{game}-gem{gem}.json"), SnapshotJson.Serialize(host.CurrentSnapshot with { Root = Materialize(host.CurrentSnapshot.Root), InitialFocusId = title.Id }));
                    }
                }
                await Act(title.ActionId!);
                check(Nodes(View().Root).Any(n => n.Id == "pob.feature-reading.body"), "Selecting a gem opens its full scrollable details");
                var fullOutput=Environment.GetEnvironmentVariable("POB_GEM_EFFECT_FIXTURES");
                if(!string.IsNullOrEmpty(fullOutput))File.WriteAllBytes(Path.Combine(fullOutput,$"poe{game}-gem{gem}-full.json"),SnapshotJson.Serialize(View()));
                await Act("feature-close"); await Act("gem-options." + gem); await Act("back");
                var view = View(); var target = view.FocusGroupEntryRequest!;
                check(target.GroupId == "pob.gem-options." + gem + ".return" &&
                    Nodes(view.Root).Single(n => n.Id == target.GroupId).InitialChildFocusId == "pob.gem-options." + gem,
                    "Gem options returns to its exact originating button, including non-first gems");
                check((int)Get("detailId")! == groups[1], "Gem details return preserves the selected non-main group");
            }
            check(WorkStateTests.SameBuild(((PobResult)Get("result")!).Xml, before.Xml), "All gem inspection and return paths leave the loaded build unchanged");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); Directory.Delete(folder, true); }
    }
    private static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
}
