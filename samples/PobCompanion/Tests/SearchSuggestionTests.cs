using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SearchSuggestionTests
{
    internal static async Task RunAsync(int game, string root, string runtime, string adapter, string xml, Action<bool, string> check)
    {
        var request = new TextSuggestionQuery("test", "test", Guid.NewGuid().ToString("N"), 1, "life", 6, 100);
        var index = new PobSearchSuggestions(new[] {
            ("Life", "+10 maximum Life", "First node"), ("Life", "+20 maximum Life", "Second node"),
            ("Thick Skin", "increased maximum Life", "Notable"), ("^xFFFFFFLong modifier\nSecond line", "resistance", "Details") }, 100);
        var matches = index.Search(request, default);
        check(matches.Items.Select(i=>i.Value).SequenceEqual(new[]{"Life","Thick Skin"}), "Names rank before modifier matches and duplicate passive names collapse");
        check(index.Search(request with { Text = "+20" }, default).Items.Single().Value == "Life", "Deduplicated names retain each passive's searchable modifiers");
        check(index.Search(request with { Text = "resistance" }, default).Items.Single().Value == "Long modifier", "Multiline colored modifier completion inserts a literal first-line search fragment");
        var large = new PobSearchSuggestions(Enumerable.Range(0, 12000).Select(i => ("Name " + i.ToString("D5"), "effect " + i, "")), 100);
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) large.Search(request with { Text = "11999" }, default);
        Console.WriteLine($"PERF PoB suggestions: 100 queries / 12,000 names in {timer.ElapsedMilliseconds} ms");
        check(large.Search(request with { Text = "11999" }, default).Items.Single().Value == "Name 11999", "Large catalogs are not truncated to an arbitrary first page");

        var directory = Path.Combine(Path.GetTempPath(), "pob-search-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var build = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory, build + ".xml"), xml);
        File.WriteAllText(Path.Combine(directory, build + ".name.txt"), "Suggestion fixture");
        File.WriteAllText(Path.Combine(directory, "selected.txt"), build);
        await using var engine = new PobEngine(root, runtime, adapter);
        var widget = new PobWidget(game, engine, directory); WidgetTestHost.Attach(widget, new WidgetTestHostServicesBuilder().Build());
        object? Get(string name) => typeof(PobWidget).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget);
        PobResult Result() => (PobResult)Get("result")!;
        var sequence = 0;
        IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        ViewNode View() => widget.Render().CreateSnapshot("search-test", ++sequence).Root;
        async Task Idle()
        {
            var deadline = Environment.TickCount64 + 65000;
            while (Nodes(View()).Any(n=>n.Id=="pob.cancel-operation"))
            { if (Environment.TickCount64 > deadline) throw new TimeoutException("Search fixture did not settle"); await Task.Delay(25); }
        }
        async Task Action(string action, string? value = null) { await widget.OnActionAsync(new(action,"search-test") { CommittedText = value }); await Idle(); }
        async Task<TextSuggestionQuery> Verify(string fieldId, string text, string label)
        {
            var field = Nodes(View()).Single(n=>n.Id==fieldId);
            var source = field.TextEntrySuggestions!;
            var before = Result().Xml;
            var query = new TextSuggestionQuery(fieldId, source.Key, Guid.NewGuid().ToString("N"), 1, text, source.MaximumResults, Math.Min(512,field.TextEntryMaximumLength!.Value));
            var answer = await widget.OnSuggestTextAsync(query, default);
            check(!answer.Unavailable && answer.Items.Count > 0 && TextSuggestionContract.IsValid(answer,source.MaximumResults,field.TextEntryMaximumLength.Value), label+" returns valid suggestions");
            check(Result().Xml==before && Nodes(View()).Single(n=>n.Id==fieldId).TextEntrySuggestions==source, label+" lookup and harmless render preserve build and source identity");
            await Action(field.ActionId!,answer.Items.First().Value);
            check(Nodes(View()).Single(n=>n.Id==fieldId).TextEntryValue==answer.Items.First().Value, label+" completion goes through the existing search action");
            return query;
        }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive); await Idle();
            await Action("section.1");
            var passive = Result().State.GetProperty("nodes").EnumerateArray().First(n=>n.GetProperty("interactive").GetBoolean()&&!string.IsNullOrWhiteSpace(n.GetProperty("name").GetString()));
            var passiveQuery = await Verify("pob.search", passive.GetProperty("name").GetString()!, "Passive search");
            await Action("section.4");
            var condition = Result().State.GetProperty("conditions").EnumerateArray().First(n=>!string.IsNullOrWhiteSpace(n.GetProperty("title").GetString()));
            await Verify("pob.search", condition.GetProperty("title").GetString()!.Trim(), "Configuration search");
            check((await widget.OnSuggestTextAsync(passiveQuery,default)).Unavailable, "Reusing the search field ID in Configuration rejects the prior Tree source");
            await Action("section.3"); await Action("inventory");
            var inventory = ((JsonElement)Get("inventoryState")!).GetProperty("inventory").EnumerateArray().First();
            await Verify("pob.inventory-search",inventory.GetProperty("title").GetString()!,"Inventory search");
            await Action("item-uniques");
            // Broad prefix exercises full upstream unique names without binding to a particular engine's catalog.
            await Verify("pob.choice-search","a","Unique item search");
            await Action("back"); await Action("item-create");
            var choiceQuery = await Verify("pob.choice-search","a","Crafting base search");
            var choices = ((ValueTuple<string,string,JsonElement[],string>)Get("featurePicker")!).Item3;
            var helmet = choices.First(c=>c.GetProperty("kind").GetString()!.Contains("Helmet",StringComparison.OrdinalIgnoreCase));
            await Action("craft-base."+helmet.GetProperty("id").GetInt32());
            var authored = (JsonElement)Get("authoredItem")!;
            await Action("author-affix."+authored.GetProperty("affixes").EnumerateArray().First().GetProperty("id").GetInt32());
            await Verify("pob.choice-search","a","Eligible affix search");
            await Action("back"); await Action("author-add-modifier");
            var modifierQuery = await Verify("pob.choice-search","a","Predefined modifier search");
            var nextSource = ((JsonElement[])Get("modifierSources")!).Select(s=>s.GetProperty("key").GetString()!)
                .First(s=>s!=(string)Get("modifierSource")!);
            await Action("author-source."+nextSource);
            check((string)Get("modifierSource")! == nextSource && (await widget.OnSuggestTextAsync(modifierQuery,default)).Unavailable,
                "Changing modifier source rejects prior eligible choices");
            await Verify("pob.choice-search","a","Changed modifier source search");
            await Action("back");
            check((await widget.OnSuggestTextAsync(choiceQuery,default)).Unavailable,"Closing the choice picker retires its suggestion source");
            await Action("section.0"); await Action("calculations");
            var catalog = (JsonElement)Get("calculationCatalog")!;
            var section = catalog.GetProperty("sections").EnumerateArray().First(s=>s.GetProperty("id").GetString()==(string)Get("calculationSection")!);
            var cell = section.GetProperty("items").EnumerateArray().First();
            await Verify("pob.calculation.search",cell.GetProperty("title").GetString()!,"Calculation search");
            await Action("back"); await Action("library");
            await Verify("pob.library.search","Suggestion fixture","Saved build search");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); Directory.Delete(directory,true); }
    }
}
