using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class EmptyBuildWorkflowTests
{
    internal static async Task RunAsync(int game, string root, string runtime, string adapter, Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pob-empty-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var engine = new PobEngine(root, runtime, adapter);
        var widget = new PobWidget(game, engine, directory) { TradeLeagueLoader = (_, _) => Task.FromResult(new[] { "Standard" }) };
        WidgetTestHost.Attach(widget, new WidgetTestHostServicesBuilder().Build());
        long sequence = 0;
        object? Get(string name) => typeof(PobWidget).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget);
        IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        ViewNode[] View() => Nodes(widget.Render().CreateSnapshot("empty-build", ++sequence).Root).ToArray();
        async Task Idle()
        {
            var deadline = Environment.TickCount64 + 65000;
            while ((bool)Get("busy")!)
            {
                View();
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Sparse build operation did not settle.");
                await Task.Delay(25);
            }
            View();
        }
        async Task Action(string action, string? text = null)
        { await widget.OnActionAsync(new(action, "empty-build") { CommittedText = text }); await Idle(); }
        async Task Collections()
        {
            using var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "sparse-rows", 500);
            var lists = Nodes(host.CurrentSnapshot.Root).Where(n => n.IndexedCollection is { Count: > 0 }).ToArray();
            foreach (var list in lists)
            {
                using var first = await host.AcquireAsync(list.Id, 0, Math.Min(3, list.IndexedCollection!.Count));
                if (list.IndexedCollection.Count > 3)
                { using var last = await host.AcquireAsync(list.Id, list.IndexedCollection.Count - 1, 1); }
            }
        }
        async Task Scenario(string name, Func<Task> run)
        {
            try { await run(); await Collections(); check(true, name); }
            catch (Exception error)
            {
                Console.WriteLine(error is ProtocolValidationException validation ? JsonSerializer.Serialize(validation.Errors) : error.ToString());
                check(false, name);
            }
        }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive); await Idle();
            await Scenario("A fresh profile renders import controls and can create its first build", async () =>
            {
                if (!View().Any(n => n.Id == "pob.new")) throw new InvalidOperationException("Missing first-build control.");
                await Action("new");
                if (Get("result") is null) throw new InvalidOperationException("First build did not open.");
            });
            foreach (var section in new[] { 0, 2, 3, 4, 1, 5 })
                await Scenario("New-build section " + section + " renders valid controls and collection rows", () => Action("section." + section));
            await Scenario("Empty passive and configuration searches retain valid navigation", async () =>
            {
                foreach (var section in new[] { 1, 4 })
                { await Action("section." + section); await Action("search", "no-match-xyz123"); await Action("search", ""); }
            });
            await Scenario("Configuration categories and an empty custom modifier group remain usable", async () =>
            {
                await Action("section.4");
                var categories = View().Where(n => n.ActionId?.StartsWith("config-section.", StringComparison.Ordinal) == true).Select(n => n.ActionId!).ToArray();
                foreach (var category in categories) { await Action(category); await Collections(); }
                await Action("custom-add"); await Action("custom-delete"); await Action("feature-confirm");
                await Action("custom-delete"); await Action("feature-confirm");
            });
            await Scenario("Empty skill groups expose management and an empty gem search safely", async () =>
            {
                await Action("section.2"); await Action("group-create");
                if (!View().Any(n => n.Id == "pob.group-empty")) throw new InvalidOperationException("Expected empty skill group.");
                await Action("gem-add"); await Action("gem-search", "no-match-xyz123"); await Action("back");
                await Action("back"); await Action("skill-options"); await Action("back");
                await Action("group-manage"); await Action("group-delete"); await Action("skill-remove-confirm");
            });
            await Scenario("Deleting all editable groups leaves valid Skills and Calcs pages", async () =>
            {
                await Action("section.2"); await Action("group-create"); await Action("group-delete-all"); await Action("skill-remove-confirm");
                await Action("section.2"); await Action("section.0"); await Action("calculations");
                var sections = View().Single(n => n.Id == "pob.calculation.section").SelectOptions.Select(o => o.ActionId).ToArray();
                foreach (var section in sections) { Console.WriteLine("AUDIT " + section); await Action(section); await Collections(); }
                await Action("calculation-search", "no-match-xyz123"); await Action("back");
            });
            await Scenario("New empty sets and the last remaining set have valid controls", async () =>
            {
                await Action("section.0"); await Action("menu"); await Action("sets");
                foreach (var kind in new[] { "tree", "items", "skills", "config" })
                {
                    await Action("sets-kind." + kind); await Action("sets-name", "Empty audit"); await Action("sets-new");
                    await Collections(); await Action("sets-delete"); await Action("feature-confirm");
                }
                await Action("back");
            });
            await Scenario("Blank notes and zero-match item catalogs retain valid back paths", async () =>
            {
                await Action("section.5");
                if(!View().Any(n=>n.Id=="pob.notes.empty"&&n.Text=="No notes in this build."))
                    throw new InvalidOperationException("An empty Notes page must explain its empty state.");
                await Action("section.3"); await Action("inventory");
                await Action("item-uniques"); await Action("choice-search", "no-match-xyz123"); await Action("back");
                await Action("item-create"); await Action("choice-search", "no-match-xyz123"); await Action("back");
            });
            await Scenario("Empty equipment slots expose upgrade setup without an equipped item", async () =>
            {
                await Action("section.3"); await Action("trade");
                var slots = View().Single(n => n.Id == "pob.trade-slot").SelectOptions.Where(o => o.Id != "requested").ToArray();
                if (slots.Length == 0) throw new InvalidOperationException("No comparison targets for empty equipment.");
                foreach (var slot in slots)
                {
                    await Action(slot.ActionId);
                    if (!View().Single(n => n.Id == "pob.trade-slot").SelectOptions.Any(o => o.Id == slot.Id && o.IsSelected))
                        throw new InvalidOperationException("Empty slot selection did not persist.");
                }
                await Action("trade-advanced"); await Action("trade-add-weight"); await Action("choice-search", "no-match-xyz123");
                await Action("back"); await Action("back");
            });
            await Scenario("A plain crafted item with no added affixes has valid item settings", async () =>
            {
                await Action("section.3"); await Action("item-create"); await Action("craft-rarity.NORMAL");
                var choices = ((ValueTuple<string,string,JsonElement[],string>)Get("featurePicker")!).Item3;
                var helmet = choices.First(c => c.GetProperty("kind").GetString()!.Contains("Helmet", StringComparison.OrdinalIgnoreCase));
                await Action("craft-base." + helmet.GetProperty("id").GetInt32()); await Action("draft-store"); await Action("inventory-equip");
                await Action("section.3"); await Action("item-manage");
                if (!View().Any(n => n.Id == "pob.item-edit.controls")) throw new InvalidOperationException("Plain item settings did not open.");
                await Action("back");
            });
            await Scenario("Saved-build no-match searches and deleting the only build render safely", async () =>
            {
                await Action("section.0"); await Action("library"); await Action("library-search", "no-match-xyz123"); await Action("library-search", "");
                await Action("library-delete"); await Action("delete-confirm");
                if (View().Single(n => n.Id == "pob.saved-builds").IndexedCollection!.Count != 0)
                    throw new InvalidOperationException("Deleting the sole build did not empty the library.");
                await Action("back");
                if (!View().Any(n => n.Id == "pob.new")) throw new InvalidOperationException("Deleting the sole build did not return to import controls.");
            });
        }
        finally { await WidgetTestHost.DestroyAsync(widget); Directory.Delete(directory, true); }
    }
}
