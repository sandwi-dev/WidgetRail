using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private readonly PobBuildComparisonService buildComparisonService = new();
    private PobBuildComparison? buildComparison;
    private PobResult? buildComparisonSource;
    private string? buildComparisonDocument, buildComparisonTarget;
    private string buildComparisonCategory = "Statistics", buildComparisonFilter = "", buildComparisonReturn = "menu";
    private bool buildComparisonRunning;
    private long buildComparisonRequest;
    private int buildComparisonDetail = -1;
    private string? buildComparisonReturnRow;
    private BuildComparisonRow? buildComparisonReading;
    private sealed record BuildComparisonRow(string Key, string Label, string Summary, int DifferenceIndex, string? ImpactText = null);
    private sealed record BuildComparisonQuery(PobBuildComparison? Comparison, string Category, string Filter, BuildComparisonRow[] Rows);
    private BuildComparisonQuery buildComparisonQuery = new(null, "", "", []);
    private WidgetIndexedCollection<BuildComparisonQuery, BuildComparisonRow>? buildComparisonCollection;
    private sealed record BuildComparisonChoice(string Id, string Label, string? SavedId, string? LoadoutName);
    private BuildComparisonChoice[] BuildComparisonChoices() => A(State, "loadouts")
        .Where(loadout => !B(loadout, "active")).Select(loadout => new BuildComparisonChoice(
            "loadout-" + PobBuildComparisonService.Identity(S(loadout, "name")), "Loadout · " + S(loadout, "title"), null, S(loadout, "name")))
        .Concat(savedBuilds.Where(build => build.Id != documentId).Select(build => new BuildComparisonChoice("saved-" + build.Id,
            "Saved build · " + build.Name, build.Id, null))).Take(120).ToArray();
    private bool BuildComparisonCurrent => buildComparison is not null && ReferenceEquals(buildComparisonSource, result) &&
        buildComparisonDocument == documentId && detailKind == "build-comparison";

    private bool HandleBuildComparisonAction(WidgetActionEvent action)
    {
        var id = action.ActionId;
        if (id == "build-comparison")
        {
            if (result is null || pendingBefore is not null || pastedItem is not null) return true;
            buildComparisonReturn = detailKind == "library" ? "library" : "menu";
            detailKind = "build-comparison"; buildComparison = null; buildComparisonDetail = -1;
            buildComparisonTarget = BuildComparisonChoices().FirstOrDefault(choice => choice.SavedId == librarySelection)?.Id ?? BuildComparisonChoices().FirstOrDefault()?.Id;
            buildComparisonReturnRow = null; buildComparisonReading = null; buildComparisonFilter = ""; focusRequestSequence++; return true;
        }
        if (detailKind != "build-comparison") return false;
        if (id is "back" or "build-comparison-cancel")
        {
            ++buildComparisonRequest; Operations.Cancel("pob.build-comparison"); buildComparisonRunning = false;
            if (buildComparisonDetail >= 0 || buildComparisonReading is not null) { buildComparisonDetail = -1; buildComparisonReading = null; }
            else detailKind = buildComparisonReturn;
            focusRequestSequence++; return true;
        }
        if (id.StartsWith("build-comparison-target.", StringComparison.Ordinal))
        {
            var target = id["build-comparison-target.".Length..];
            if (!buildComparisonRunning && BuildComparisonChoices().Any(choice => choice.Id == target))
            { buildComparisonTarget = target; buildComparison = null; buildComparisonDetail = -1; buildComparisonReturnRow = null; buildComparisonReading = null; }
            return true;
        }
        if (id == "build-comparison-run") { StartBuildComparison(); return true; }
        if (id == "build-comparison-filter") { buildComparisonFilter = Limit(action.CommittedText ?? "", 256); buildComparisonReturnRow = null; return true; }
        if (id.StartsWith("build-comparison-category.", StringComparison.Ordinal))
        {
            var category = id["build-comparison-category.".Length..];
            if (category == "Impact" || Enum.TryParse<PobBuildComparisonCategory>(category, out _))
            { buildComparisonCategory = category; buildComparisonDetail = -1; buildComparisonReturnRow = null; buildComparisonReading = null; }
            return true;
        }
        return false;
    }

    private void RetireBuildComparisonRequest()
    {
        ++buildComparisonRequest; Operations.Cancel("pob.build-comparison"); buildComparisonRunning = false;
    }
    private void StartBuildComparison()
    {
        if (buildComparisonRunning || Operations.IsBusy("pob.build-comparison") || result is not { } source || documentId is not { } sourceId || pendingBefore is not null || pastedItem is not null) return;
        var selected = BuildComparisonChoices().FirstOrDefault(choice => choice.Id == buildComparisonTarget);
        if (selected is null) return;
        var generation = ++buildComparisonRequest;
        var sourceName = BuildName();
        buildComparisonRunning = true; buildComparison = null; buildComparisonDetail = -1; buildComparisonReading = null; buildComparisonReturnRow = null;
        var operation = Operations.RunSingleFlight("pob.build-comparison", async context =>
        {
            try
            {
                var xml = selected.SavedId is { } saved
                    ? PobWorkStore.ReadCommitted(dataDirectory, saved, game) ?? await BuildDocument.ReadAsync(Path.Combine(dataDirectory, saved + ".xml"), game, context.CancellationToken)
                    : source.Xml;
                var input = new PobBuildComparisonInput(selected.Id, selected.Label, xml, selected.LoadoutName);
                var compared = await buildComparisonService.CompareAsync(source, sourceId, sourceName, input,
                    (command, token, expected) => engine.ProjectReadOnlyAsync(command, token, expected, compare: true), context.CancellationToken);
                context.CancellationToken.ThrowIfCancellationRequested();
                // A saved file can be replaced outside the widget during calculation.
                // Reject that stale comparison instead of silently labelling old data current.
                if (selected.SavedId is { } savedId)
                {
                    var latest = PobWorkStore.ReadCommitted(dataDirectory, savedId, game) ??
                        await BuildDocument.ReadAsync(Path.Combine(dataDirectory, savedId + ".xml"), game, context.CancellationToken);
                    if (latest != xml) throw new IOException("The saved build changed during comparison. Compare again to use its latest contents.");
                }
                lock (gate)
                {
                    if (!context.IsCurrent || generation != buildComparisonRequest || !ReferenceEquals(result, source) || documentId != sourceId || detailKind != "build-comparison") return;
                    buildComparison = compared; buildComparisonSource = source; buildComparisonDocument = sourceId;
                    // Publication is not a navigation request: retain Compare (or the user's newer focus).
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
            {
                lock (gate) if (generation == buildComparisonRequest && ReferenceEquals(result, source) && detailKind == "build-comparison")
                    ShowFeedback("Comparison unavailable", Limit(error.Message, 350), ToastTone.Warning);
            }
            finally
            {
                lock (gate) if (generation == buildComparisonRequest) buildComparisonRunning = false;
                Invalidate();
            }
        }); // Active lifetime: leaving/hiding cancels; late engine replies are fenced.
        if (!operation.IsAccepted) buildComparisonRunning = false;
    }

    private WidgetElement BuildComparisonWorkspace()
    {
        if (BuildComparisonCurrent && buildComparisonReading?.ImpactText is { } impactText)
            return UI.Stack("pob.build-comparison.page",
                UI.Stack("pob.build-comparison.tools", ManagementHeader("PoB calculated impact")).RememberChildFocus("pob.back"),
                UI.VerticalScroll("pob.build-comparison.detail-scroll", Text(buildComparisonReading.Label, "build-comparison.impact.title", "pob-heading"),
                    Text(impactText, "build-comparison.impact.text")).Classes("pob-scroll")).Classes("pob-workspace");
        if (BuildComparisonCurrent && buildComparisonDetail >= 0 && buildComparisonDetail < buildComparison!.Differences.Count)
            return BuildComparisonDetail(buildComparison.Differences[buildComparisonDetail]);
        var choices = BuildComparisonChoices();
        if (!choices.Any(choice => choice.Id == buildComparisonTarget)) buildComparisonTarget = choices.FirstOrDefault()?.Id;
        var tools = new List<WidgetElement> { ManagementHeader("Compare builds & loadouts") };
        if (choices.Length > 0)
        {
            tools.Add(UI.Select("Compare current with", choices.Select(choice => new SelectOption(choice.Id, Limit(choice.Label, 180),
                "build-comparison-target." + choice.Id, choice.Id == buildComparisonTarget)).ToArray(), "pob.build-comparison.target").Busy(buildComparisonRunning).Classes("pob-select"));
            tools.Add(UI.Row("pob.build-comparison.actions", Button("Compare", "build-comparison-run").Busy(buildComparisonRunning),
                buildComparisonRunning ? Button("Cancel comparison", "build-comparison-cancel") : Text("Read-only · your build stays unchanged", "build-comparison.readonly", "pob-small pob-muted")).Classes("pob-toolbar"));
        }
        else tools.Add(Text("Save another build or create another linked loadout to compare it here.", "build-comparison.empty", "pob-muted"));
        var content = new List<WidgetElement>();
        if (buildComparisonRunning) content.Add(UI.Row("pob.build-comparison.progress",
            UI.LoadingIndicator("pob.build-comparison.loading", "Calculating comparison"), Text("Calculating with PoB…", "build-comparison.progress-text")).Classes("pob-toolbar"));
        else if (BuildComparisonCurrent)
        {
            var comparison = buildComparison!;
            content.Add(BuildComparisonContext(comparison.Current.Context, "Current", "current"));
            content.Add(BuildComparisonContext(comparison.Other.Context, "Comparison", "other"));
            content.Add(Text("Each side uses its own main skill, equipment, passives and configuration. These are total build differences, not equipment-only gains.",
                "build-comparison.context-warning", "pob-small pob-muted"));
            tools.Add(UI.Select("Differences", Enum.GetValues<PobBuildComparisonCategory>().Select(category => new SelectOption(category.ToString(),
                category + " (" + comparison.Differences.Count(entry => entry.Category == category) + ")", "build-comparison-category." + category,
                buildComparisonCategory == category.ToString())).Prepend(new("Impact", "PoB calculated impact (" + comparison.Impact.Sum(group => group.Lines.Count) + ")",
                    "build-comparison-category.Impact", buildComparisonCategory == "Impact")).ToArray(), "pob.build-comparison.category").Classes("pob-select"));
            tools.Add(UI.TextEntry(buildComparisonFilter, "Find a difference", "build-comparison-filter", "pob.build-comparison.search", 256).Classes("pob-input"));
        }
        else if (buildComparison is not null) content.Add(Text("The current build changed. Compare again for current results.", "build-comparison.stale", "pob-muted"));
        var information = UI.VerticalScroll("pob.build-comparison.content", content.ToArray()).Classes("pob-scroll");
        var body = BuildComparisonCurrent ? Split("build-comparison", information, BuildComparisonRows(), 1, 1.25) : information;
        return UI.Stack("pob.build-comparison.page", UI.Stack("pob.build-comparison.tools", tools.ToArray()).RememberChildFocus(choices.Length > 0 ? "pob.build-comparison.target" : "pob.back"), body).Classes("pob-workspace");
    }
    private WidgetElement BuildComparisonRows()
    {
        var comparison = buildComparison!;
        buildComparisonCollection ??= CreateIndexedCollection<BuildComparisonQuery, BuildComparisonRow>("pob.build-comparison.source", buildComparisonQuery, 0, new()
        {
            ReadRange = (query, start, count, token) =>
            { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<IReadOnlyList<BuildComparisonRow>>(query.Rows.Skip(start).Take(count).ToArray()); },
            ItemKey = row => new(row.Key),
            RenderItem = (_, row, context) => UI.ActionSurface("inspect", context.Id("entry"), row.Label + ", inspect difference", ActionSurfaceOrientation.Vertical,
                UI.Text(row.Label, context.Id("label")).Classes("pob-heading"),
                UI.Text(row.Summary, context.Id("summary")).Classes("pob-text")).Classes("pob-card"),
            OnAction = (query, row, action, token) =>
            {
                lock (gate)
                {
                    if (action.ActionId != "inspect" || !ReferenceEquals(query, buildComparisonQuery) ||
                        !ReferenceEquals(query.Comparison, buildComparison) || !BuildComparisonCurrent || buildComparisonRunning || busy) return ValueTask.CompletedTask;
                    buildComparisonReading = row; buildComparisonDetail = row.DifferenceIndex; buildComparisonReturnRow = row.Key;
                    focusRequestSequence++;
                }
                Invalidate(); return ValueTask.CompletedTask;
            }
        });
        if (!ReferenceEquals(buildComparisonQuery.Comparison, comparison) || buildComparisonQuery.Category != buildComparisonCategory || buildComparisonQuery.Filter != buildComparisonFilter)
        {
            var rows = buildComparisonCategory == "Impact"
                ? comparison.Impact.SelectMany((group, groupIndex) => group.Lines.Select((line, lineIndex) =>
                    new BuildComparisonRow($"impact.{groupIndex}.{lineIndex}", group.Title, line.Text, -1, line.Text)))
                : comparison.Differences.Select((entry, index) => (entry, index)).Where(item => item.entry.Category.ToString() == buildComparisonCategory)
                    .Select(item => new BuildComparisonRow(WidgetIds.Scope("comparison").KeyedId(item.entry.Category.ToString(), item.entry.Key),
                        item.entry.Category == PobBuildComparisonCategory.Statistics ? BuildComparisonStatLabel(item.entry.Label) : item.entry.Label,
                        item.entry.Kind + "\nCurrent: " + BuildComparisonSummary(item.entry.Current, item.entry.Category) +
                        "\nComparison: " + BuildComparisonSummary(item.entry.Other, item.entry.Category), item.index));
            var filtered = rows.Where(row => buildComparisonFilter.Length == 0 || row.Label.Contains(buildComparisonFilter, StringComparison.OrdinalIgnoreCase) ||
                row.Summary.Contains(buildComparisonFilter, StringComparison.OrdinalIgnoreCase) || row.DifferenceIndex >= 0 &&
                ((comparison.Differences[row.DifferenceIndex].Current?.Details.Contains(buildComparisonFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 (comparison.Differences[row.DifferenceIndex].Other?.Details.Contains(buildComparisonFilter, StringComparison.OrdinalIgnoreCase) ?? false))).ToArray();
            buildComparisonQuery = new(comparison, buildComparisonCategory, buildComparisonFilter, filtered);
            buildComparisonCollection.PublishQuery(buildComparisonQuery, filtered.Length);
        }
        var list = UI.CollectionList("pob.build-comparison.rows", buildComparisonCollection, 64, "Build differences").Classes("pob-scroll");
        return QuickControls(UI.Stack("pob.build-comparison.results", Text(buildComparisonQuery.Rows.Length == 0 ? "No differences in this view." :
                $"{buildComparisonQuery.Rows.Length} differences · Select to inspect", "build-comparison.count", "pob-muted"), list).Classes("pob-workspace"),
            "pob.build-comparison.rows", "pob.build-comparison.tools", "Comparison controls", NeedsQuickControls(buildComparisonQuery.Rows.Length));
    }
    private FocusGroupEntryRequest BuildComparisonFocusRequest()
    {
        if (buildComparisonDetail < 0 && buildComparisonReading is null && BuildComparisonCurrent && buildComparisonReturnRow is { } key && buildComparisonCollection is not null)
        {
            var index = Array.FindIndex(buildComparisonQuery.Rows, row => row.Key == key);
            if (index >= 0) return buildComparisonCollection.Enter("pob.build-comparison.rows", focusRequestSequence,
                buildComparisonCollection.FocusTarget("pob.build-comparison.rows", new(key), index));
        }
        return new() { RequestId = focusRequestSequence, GroupId = "pob.build-comparison.tools" };
    }
    private static string BuildComparisonStatLabel(string key) => key switch
    {
        "CombinedDPS" => "Main skill output (DPS / average damage)", "FullDPS" => "All included skills DPS",
        "TotalDPS" => "Main skill hit DPS", _ => System.Text.RegularExpressions.Regex.Replace(key, "([a-z])([A-Z])", "$1 $2")
    };
    private static string BuildComparisonStat(PobBuildComparisonEntry entry) => double.TryParse(entry.Value, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var value) ? StatValue(entry.Key, value) : entry.Value;
    private static string BuildComparisonSummary(PobBuildComparisonEntry? entry, PobBuildComparisonCategory category) => entry is null ? "Not present" :
        Limit(category == PobBuildComparisonCategory.Statistics ? BuildComparisonStat(entry) : entry.Details.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? entry.Label, 200);
    private static WidgetElement BuildComparisonContext(PobBuildComparisonContext context, string side, string key) => UI.Stack("pob.build-comparison.context." + key,
        Text(side + " · " + context.Name, "build-comparison.context." + key + ".name", "pob-heading"),
        Text(context.Loadout + "\n" + context.Class + " · Level " + context.Level + "\nMain skill: " + context.MainSkill + "\n" + context.WeaponSet +
            "\nTree: " + context.Tree + "\nEquipment: " + context.Equipment + "\nSkills: " + context.Skills + "\nConfiguration: " + context.Configuration +
            "\nCalculation detail context: " + context.CalculationSkill + "\nTree version " + context.TreeVersion + " · Engine " + context.EngineVersion +
            (context.SkillOptions.Length > 0 ? "\n" + context.SkillOptions : ""), "build-comparison.context." + key + ".value", "pob-small")).Classes("pob-card");
    private WidgetElement BuildComparisonDetail(PobBuildDifference difference)
    {
        WidgetElement Side(PobBuildComparisonEntry? entry, string label, string key)
        {
            var text = entry?.Details ?? "Not present in this build";
            var rows = new List<WidgetElement> { Text(label, "build-comparison.detail." + key + ".title", "pob-heading") };
            if (difference.Category == PobBuildComparisonCategory.Statistics && entry is not null)
                rows.Add(Text(BuildComparisonStat(entry), "build-comparison.detail." + key + ".value", "pob-heading"));
            // Preserve long item/modifier text without relying on Text's display truncation.
            for (var offset = 0; offset < text.Length; offset += 2800)
                rows.Add(Text(text.Substring(offset, Math.Min(2800, text.Length - offset)), "build-comparison.detail." + key + "." + offset));
            return UI.Stack("pob.build-comparison.detail." + key, rows.ToArray()).Classes("pob-card");
        }
        return UI.Stack("pob.build-comparison.page", UI.Stack("pob.build-comparison.tools", ManagementHeader(difference.Category == PobBuildComparisonCategory.Statistics ? BuildComparisonStatLabel(difference.Label) : difference.Label)).RememberChildFocus("pob.back"),
            UI.VerticalScroll("pob.build-comparison.detail-scroll", Text(difference.Kind, "build-comparison.detail.kind", "pob-muted"),
                UI.Grid("pob.build-comparison.detail", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
                    Side(difference.Current, "Current", "current"), Side(difference.Other, "Comparison", "other").InGrid(column: 1)).Spacing(0, 16)).Classes("pob-scroll")).Classes("pob-workspace");
    }
}
