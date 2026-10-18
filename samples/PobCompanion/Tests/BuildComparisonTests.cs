using System.Text.Json;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class BuildComparisonTests
{
    internal static async Task ModelsAsync(Action<bool, string> check)
    {
        var before = Fake(100, "First skill", false);
        var after = Fake(120, "Second skill", true);
        var service = new PobBuildComparisonService(); var requests = 0;
        Task<PobResult> Calculate(object command, CancellationToken token, string expected)
        { requests++; check(expected == before.Xml, "Comparison supplies the exact visible engine baseline"); return Task.FromResult(after); }
        var input = new PobBuildComparisonInput("other", "Other", after.Xml);
        var comparison = await service.CompareAsync(before, "current", "Current", input, Calculate, default);
        check(Enum.GetValues<PobBuildComparisonCategory>().All(category => comparison.Differences.Any(entry => entry.Category == category)),
            "Typed projection reports statistics, equipment, skills, passives and configuration differences");
        check(comparison.Current.Context.MainSkill.StartsWith("First skill", StringComparison.Ordinal) && comparison.Other.Context.MainSkill.StartsWith("Second skill", StringComparison.Ordinal) &&
            comparison.Other.Context.WeaponSet == "Weapon set 2", "Comparison exposes distinct main skill and weapon-set context");
        check(comparison.Impact.Single().Lines.Single().Text == "+20 Life", "Calculated impact preserves upstream wording");
        await service.CompareAsync(before, "current", "Current", input, Calculate, default);
        check(requests == 1, "Identical exact inputs reuse bounded projections");
        await service.CompareAsync(before, "current", "Current", input with { Xml = after.Xml + " " }, Calculate, default);
        check(requests == 2, "Changed source bytes invalidate cached calculation");
        for (var index = 0; index < 8; index++)
            await service.CompareAsync(before, "current", "Current", input with { Identity = "other" + index }, Calculate, default);
        check(service.CachedCount <= PobBuildComparisonService.MaximumCachedComparisons && service.CachedBytes <= PobBuildComparisonService.MaximumCacheBytes,
            "Comparison cache remains count and byte bounded");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var oldRequests = requests;
        try { await service.CompareAsync(before, "current", "Current", input, Calculate, canceled.Token); check(false, "Cancellation rejected"); }
        catch (OperationCanceledException) { check(requests == oldRequests, "Cancellation prevents cache or engine work"); }
        var changed = new PobResult(JsonSerializer.SerializeToElement(new { engineVersion = "other", stats = new[] { new { name = "Life", value = 100 } } }), before.Xml);
        await service.CompareAsync(changed, "current", "Current", input, (_, _, _) => { requests++; return Task.FromResult(after); }, default);
        check(requests == oldRequests + 1, "Engine identity participates in cache key");
        var noChanges = await new PobBuildComparisonService().CompareAsync(before, "current", "Current", new("same", "Same", before.Xml), (_, _, _) => Task.FromResult(before), default);
        check(noChanges.Differences.Count == 0, "Same build produces no invented differences");
    }
    private static PobResult Fake(int life, string skill, bool other)
    {
        var xml = $"<PathOfBuilding><Build/><Items activeItemSet=\"1\"><ItemSet id=\"1\" useSecondWeaponSet=\"{other.ToString().ToLowerInvariant()}\"/></Items></PathOfBuilding>";
        return new(JsonSerializer.SerializeToElement(new
        {
            engineVersion = "test", mainSkill = skill, level = 50, treeVersion = "tree-v1", @class = "Class", ascendancy = "Ascendancy",
            trees = new[] { new { title = "Tree", active = true } }, itemSets = new[] { new { title = "Equipment", active = true } },
            skillSets = new[] { new { title = "Skills", active = true } }, configSets = new[] { new { title = other ? "Boss" : "Mapping", active = true } },
            loadouts = new[] { new { title = other ? "Boss" : "Mapping", active = true } },
            stats = new[] { new { name = "Life", value = life } },
            items = new[] { new { slot = "Helmet", name = "Helmet", @base = "Base", raw = "Rarity: Rare\nHelmet\n+" + life + " to maximum Life" } },
            nodes = new[] { new { id = 12, name = "Intelligence", allocated = true, granted = false, allocationMode = other ? 2 : 0, description = "+10 to Intelligence" } },
            skills = new[] { new { id = 1, title = skill, label = "Group", slot = "Body Armour", enabled = true, main = true, fullDps = true, groupCount = 1,
                gems = new[] { new { name = skill, level = other ? 21 : 20, quality = 20, enabled = true, count = 1 } } } },
            conditions = new[] { new { id = "boss", title = "Boss", section = "Enemy", value = other, applicable = true, description = "Enemy type" } },
            comparison = other ? new[] { new { title = "Player", lines = new[] { new { text = "+20 Life", runs = new[] { new { text = "+20 Life", tone = "positive" } } } } } } : []
        }), xml);
    }

    internal static async Task RunAsync(int game, string engineRoot, string runtime, string adapter, string xml, Action<bool, string> check)
    {
        await ModelsAsync(check);
        await using var engine = new PobEngine(engineRoot, runtime, adapter);
        var baseline = await engine.ExecuteAsync(new { op = "load", xml, name = "Comparison baseline" }, default);
        var rejectedOperation = false;
        try { await engine.ProjectReadOnlyAsync(new { op = "level", value = 1 }, default, baseline.Xml); }
        catch (IOException) { rejectedOperation = true; }
        check(rejectedOperation, "Read-only engine transaction rejects unrelated mutation commands");
        var changed = await engine.ExecuteAsync(new { op = "level", value = Math.Max(1, baseline.State.GetProperty("level").GetInt32() - 1) }, default, baseline.Xml);
        var service = new PobBuildComparisonService();
        var comparisons = await service.CompareAsync(baseline, "baseline", "Current", new("saved", "Other", changed.Xml),
            (command, token, expected) => engine.ProjectReadOnlyAsync(command, token, expected, compare: true), default);
        var restored = await engine.ExecuteAsync(new { op = "snapshot" }, default);
        check(WorkStateTests.SameBuild(restored.Xml, baseline.Xml), "Read-only saved-build calculation restores engine contents");
        try { await engine.ProjectReadOnlyAsync(new { op = "loadout", name = "missing-loadout-" + Guid.NewGuid().ToString("N") }, default, baseline.Xml); check(false, "Invalid projection rejected"); }
        catch (PobCommandException) { check(engine.ProcessId is null, "Rejected read-only projection retires ambiguous engine state"); }
        restored = await engine.ExecuteAsync(new { op = "snapshot" }, default);
        check(WorkStateTests.SameBuild(restored.Xml, baseline.Xml), "Failed projection reloads the baseline on the next request");
        check(comparisons.Other.Context.Level == changed.State.GetProperty("level").ToString(), "Compared build uses its own level and calculation context");
        check(comparisons.Differences.Any(entry => entry.Category == PobBuildComparisonCategory.Statistics), "Real upstream recalculation reports level-related statistics");
        var loadouts = baseline.State.GetProperty("loadouts").EnumerateArray().Where(item => !item.GetProperty("active").GetBoolean()).ToArray();
        if (loadouts.Length > 0)
        {
            var loadout = loadouts[0];
            var compared = await service.CompareAsync(baseline, "baseline", "Current", new("loadout", "Other loadout", baseline.Xml, loadout.GetProperty("name").GetString()),
                (command, token, expected) => engine.ProjectReadOnlyAsync(command, token, expected, compare: true), default);
            check(compared.Other.Context.Loadout == loadout.GetProperty("title").GetString(), "Loadout comparison selects the upstream linked setup");
            restored = await engine.ExecuteAsync(new { op = "snapshot" }, default);
            check(WorkStateTests.SameBuild(restored.Xml, baseline.Xml), "Read-only loadout projection restores active sets and main skill");
        }
        var canceledDirectory = Path.Combine(Path.GetTempPath(), "widgetrail-comparison-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(canceledDirectory);
        try
        {
            var stalledAdapter = Path.Combine(canceledDirectory, "stalled.lua");
            await File.WriteAllTextAsync(stalledAdapter, "io.stdout:setvbuf('no')\nio.write('{\"ready\":true}\\n')\nio.read('*l')\nio.read('*l')\n");
            await using var stalled = new PobEngine(engineRoot, runtime, stalledAdapter);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            var canceled = false;
            try { await stalled.ProjectReadOnlyAsync(new { op = "load", xml = changed.Xml }, cancel.Token, baseline.Xml); }
            catch (OperationCanceledException) { canceled = true; }
            check(canceled && stalled.ProcessId is null, "Cancelled comparison retires its process");
            var cached = typeof(PobEngine).GetField("lastXml", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            check((string?)cached.GetValue(stalled) == baseline.Xml, "Cancelled projection retains the original baseline, never the compared build");
        }
        finally { Directory.Delete(canceledDirectory, true); }
        var directory = Path.Combine(Path.GetTempPath(), "widgetrail-build-comparison-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N"); var otherId = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(directory, id + ".xml"), baseline.Xml);
        File.WriteAllText(Path.Combine(directory, otherId + ".xml"), changed.Xml);
        File.WriteAllText(Path.Combine(directory, "selected.txt"), id);
        var widget = new PobWidget(game, engine, directory);
        WidgetTestHost.Attach(widget, new WidgetTestHostServicesBuilder().Build());
        IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        ViewSnapshot View() => widget.Render().CreateSnapshot("build-comparison-test", 1);
        bool Has(string id) => Nodes(View().Root).Any(node => node.Id == id);
        async Task Settled() { var end = DateTime.UtcNow.AddSeconds(65); while ((Has("pob.cancel-operation") || Has("pob.build-comparison.loading")) && DateTime.UtcNow < end) await Task.Delay(20); if (DateTime.UtcNow >= end) throw new TimeoutException(); }
        try
        {
            await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive); await Settled();
            var beforeFiles = Directory.GetFiles(directory).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
            await widget.OnActionAsync(new("build-comparison", "test"));
            check(View().Surface?.PreferredWidth == 1320 && View().Surface?.HeightMode == WidgetSurfaceAxisMode.FillAvailable,
                "Whole-build comparison uses the planning workspace dimensions");
            await widget.OnActionAsync(new("build-comparison-target.saved-" + otherId, "test"));
            var focusBefore = View().FocusGroupEntryRequest?.RequestId;
            await widget.OnActionAsync(new("build-comparison-run", "pob.build-comparison-run"));
            var waiting = View();
            var compareButton = Nodes(waiting.Root).Single(node => node.Id == "pob.build-comparison-run");
            check(compareButton.IsDisabled != true && compareButton.IsBusy == true, "Compare remains focusable during calculation");
            check(waiting.FocusGroupEntryRequest?.RequestId == focusBefore, "Starting comparison does not request a focus move");
            await Settled();
            check(View().FocusGroupEntryRequest?.RequestId == focusBefore, "Comparison completion does not request a focus move");
            check(Has("pob.build-comparison.context.current") && Has("pob.build-comparison.context.other"), "Comparison displays both labelled contexts");
            check(ViewSnapshotValidator.Validate(View()).Count == 0, "Whole-build comparison emits a valid bounded snapshot");
            using (var host = WidgetTestHost.CreateIndexedCollectionHost(widget, "build-comparison-test", 500))
            {
                var declaration = Nodes(host.CurrentSnapshot.Root).Single(node => node.Id == "pob.build-comparison.rows").IndexedCollection!;
                check(declaration.Count > 0 && !Has("pob.build-comparison-next"), "Differences use the shared indexed collection without pagination");
                using var lease = await host.AcquireAsync("pob.build-comparison.rows", 0, Math.Min(3, declaration.Count));
                var row = lease.Range.Items[0];
                check(lease.RouteAction(row.Key, ControllerButton.A) == WidgetOperationAdmission.Enqueued, "Indexed inspection routes the exact comparison row");
                var until = DateTime.UtcNow.AddSeconds(3);
                while (!Has("pob.build-comparison.detail.current") && DateTime.UtcNow < until) await Task.Delay(10);
                check(Has("pob.build-comparison.detail.current") && Has("pob.build-comparison.detail.other"), "Difference row navigates to both read-only details");
                await widget.OnActionAsync(new("back", "test"));
                check(View().FocusGroupEntryRequest?.GroupId == "pob.build-comparison.rows", "Back explicitly restores the inspected indexed occurrence");
                if (Environment.GetEnvironmentVariable("POB_BUILD_COMPARISON_FIXTURES") is { Length: > 0 } rowFixtures)
                { Directory.CreateDirectory(rowFixtures); File.WriteAllText(Path.Combine(rowFixtures, $"poe{game}-build-comparison-row.json"), JsonSerializer.Serialize(row.Root)); }
                await widget.OnActionAsync(new("build-comparison-filter", "test") { CommittedText = "no matching comparison entry" });
                host.PublishSnapshot();
                check(lease.RouteAction(row.Key, ControllerButton.A) != WidgetOperationAdmission.Enqueued, "Replaced comparison query rejects old row callbacks");
                await widget.OnActionAsync(new("build-comparison-filter", "test") { CommittedText = "" });
            }
            check(Directory.GetFiles(directory).Length == beforeFiles.Count && beforeFiles.All(file => File.ReadAllBytes(Path.Combine(directory, file.Key!)).SequenceEqual(file.Value)),
                "Whole-build comparison makes no saved-build, history, selection or recovery writes");
            if (Environment.GetEnvironmentVariable("POB_BUILD_COMPARISON_FIXTURES") is { Length: > 0 } fixtures)
            { Directory.CreateDirectory(fixtures); File.WriteAllBytes(Path.Combine(fixtures, $"poe{game}-build-comparison.json"), SnapshotJson.Serialize(View())); }
            await widget.OnActionAsync(new("back", "test"));
            check(!Has("pob.build-comparison.page"), "Back returns from comparison without switching builds");
        }
        finally { await WidgetTestHost.DestroyAsync(widget); Directory.Delete(directory, true); }
    }
}
