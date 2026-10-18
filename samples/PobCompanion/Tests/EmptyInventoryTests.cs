using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class EmptyInventoryTests
{
    internal static async Task RunAsync(int game, string root, string runtime, string adapter, Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pob-empty-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var engine = new PobEngine(root, runtime, adapter);
        var empty = await engine.ExecuteAsync(new { op = "new" }, default);
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory, id + ".xml"), empty.Xml);
        File.WriteAllText(Path.Combine(directory, "selected.txt"), id);
        var widget = new PobWidget(game, engine, directory);
        WidgetTestHost.Attach(widget, new WidgetTestHostServicesBuilder().Build());
        long sequence = 0;
        IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        ViewNode[] View() => Nodes(widget.Render().CreateSnapshot("empty-inventory", ++sequence).Root).ToArray();
        async Task Idle()
        {
            var deadline = Environment.TickCount64 + 65000;
            while (View().Any(n => n.Id == "pob.cancel-operation"))
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Inventory workflow did not settle.");
                await Task.Delay(25);
            }
        }
        async Task Action(string action, string? text = null)
        { await widget.OnActionAsync(new(action, "empty-inventory") { CommittedText = text }); await Idle(); }
        void CheckEmpty(string label)
        {
            var view = View();
            check(view.Any(n => n.Id == "pob.stored.empty") && !view.Any(n => n.Id == "pob.inventory-edit") &&
                view.Single(n => n.Id == "pob.stored.details").InitialChildFocusId is null &&
                view.Any(n => n.Id == "pob.item-create"), label);
        }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive); await Idle();
            await Action("section.3"); await Action("inventory");
            CheckEmpty("A new build opens empty inventory with valid focus and usable creation controls");
            var field = View().Single(n => n.Id == "pob.inventory-search");
            var suggestions = await widget.OnSuggestTextAsync(new(field.Id, field.TextEntrySuggestions!.Key,
                Guid.NewGuid().ToString("N"), 1, "helmet", 6, 100), default);
            check(!suggestions.Unavailable && suggestions.Items.Count == 0, "Empty inventory search returns no suggestions without failing");
            await Action("item-create");
            var bases = await engine.ExecuteAsync(new { op = "item-bases" }, default, empty.Xml);
            var basis = bases.State.GetProperty("choices").EnumerateArray().First(c => c.GetProperty("kind").GetString()!.Contains("Helmet", StringComparison.OrdinalIgnoreCase));
            await Action("craft-base." + basis.GetProperty("id").GetInt32()); await Action("draft-store");
            check(View().Single(n => n.Id == "pob.stored.details").InitialChildFocusId == "pob.inventory-edit",
                "Adding the first item restores the item detail focus target");
            await Action("inventory-search", "no-such-item");
            check(View().Single(n => n.Id == "pob.stored.items").IndexedCollection!.Count == 0 && View().Any(n => n.Id == "pob.inventory-edit"),
                "A zero-match filter keeps the selected item's valid detail controls");
            await Action("inventory-search", ""); await Action("inventory-delete"); await Action("feature-confirm");
            CheckEmpty("Deleting the last item returns to valid empty inventory");
            await Action("undo");
            check(View().Any(n => n.Id == "pob.inventory-edit"), "Undo restores the last item and its detail controls");
            await Action("redo"); CheckEmpty("Redo of last-item deletion retires the missing focus target again");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); Directory.Delete(directory, true); }
    }
}
