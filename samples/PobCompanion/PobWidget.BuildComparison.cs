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
    private readonly PobDisclosureState buildComparisonContextDisclosure = new();
    private sealed record BuildComparisonRow(string Key, string Label, string Summary, int DifferenceIndex, string? ImpactText = null,
        string Current = "", string Other = "", string Change = "");
    private sealed record BuildComparisonQuery(PobBuildComparison? Comparison, string Category, string Filter, BuildComparisonRow[] Rows);
    private BuildComparisonQuery buildComparisonQuery = new(null, "", "", []);
    private WidgetIndexedCollection<BuildComparisonQuery, BuildComparisonRow>? buildComparisonCollection;
    private sealed record BuildComparisonChoice(string Id, string Label, string Name, string? SavedId, string? LoadoutName);
    private BuildComparisonChoice[] BuildComparisonChoices() => A(State, "loadouts")
        .Where(loadout => !B(loadout, "active")).Select(loadout => new BuildComparisonChoice(
            "loadout-" + PobBuildComparisonService.Identity(S(loadout, "name")), "Loadout · " + S(loadout, "title"), BuildName(), null, S(loadout, "name")))
        .Concat(savedBuilds.Where(build => build.Id != documentId).Select(build => new BuildComparisonChoice("saved-" + build.Id,
            "Saved build · " + build.Name, build.Name, build.Id, null))).Take(120).ToArray();
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
            buildComparisonReturnRow = null; buildComparisonReading = null; buildComparisonFilter = ""; lastFocusRequestId = CreateFocusRequest().Id; return true;
        }
        if (detailKind != "build-comparison") return false;
        if (id.StartsWith("build-comparison-context.", StringComparison.Ordinal))
        {
            if (BuildComparisonCurrent && !buildComparisonRunning && buildComparisonDetail < 0 && buildComparisonReading is null &&
                buildComparisonContextDisclosure.Current?.Context == buildComparison!.InputKey &&
                long.TryParse(id["build-comparison-context.".Length..], out var token))
                buildComparisonContextDisclosure.Toggle(token);
            return true;
        }
        if (id is "back" or "build-comparison-cancel")
        {
            ++buildComparisonRequest; Operations.Cancel("pob.build-comparison"); buildComparisonRunning = false;
            if (buildComparisonDetail >= 0 || buildComparisonReading is not null) { buildComparisonDetail = -1; buildComparisonReading = null; }
            else detailKind = buildComparisonReturn;
            lastFocusRequestId = CreateFocusRequest().Id; return true;
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
                var input = new PobBuildComparisonInput(selected.Id, selected.Name, xml, selected.LoadoutName);
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
        {
            var rows = new List<WidgetElement> { Text(buildComparisonReading.Label, "build-comparison.impact.title", "pob-heading") };
            AddFormatted(rows, JsonSerializer.SerializeToElement(new { text = impactText }), "text", "runs", "build-comparison.impact.text");
            return UI.Stack("pob.build-comparison.page",
                UI.Stack("pob.build-comparison.tools", ManagementHeader("PoB calculated impact")).RememberChildFocus("pob.back"),
                UI.VerticalScroll("pob.build-comparison.detail-scroll", rows.ToArray()).Classes("pob-scroll")).Classes("pob-workspace");
        }
        if (BuildComparisonCurrent && buildComparisonDetail >= 0 && buildComparisonDetail < buildComparison!.Differences.Count)
            return BuildComparisonDetail(buildComparison.Differences[buildComparisonDetail]);
        var choices = BuildComparisonChoices();
        if (!choices.Any(choice => choice.Id == buildComparisonTarget)) buildComparisonTarget = choices.FirstOrDefault()?.Id;
        var tools = new List<WidgetElement> { ManagementHeader("Compare builds & loadouts") };
        if (!BuildComparisonCurrent)
            tools.Add(Text("From: " + PobBuildComparisonPresentation.Identity(BuildName(),
                S(A(State, "loadouts").FirstOrDefault(loadout => B(loadout, "active")), "title", "Independent sets")),
                "build-comparison.source", "pob-build-comparison-summary"));
        if (choices.Length > 0)
        {
            tools.Add(UI.Select("Compare with", choices.Select(choice => new SelectOption(choice.Id, Limit(choice.Label, 180),
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
            var disclosure = buildComparisonContextDisclosure.Select(comparison.InputKey, defaultExpanded: false);
            content.Add(UI.Grid("pob.build-comparison.context", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
                BuildComparisonContext(comparison.Current.Context, "From", "current", disclosure.Expanded),
                BuildComparisonContext(comparison.Other.Context, "To", "other", disclosure.Expanded).InGrid(column: 1)).Spacing(0, 16));
            tools.Add(UI.Row("pob.build-comparison.context-tools",
                UI.Button(disclosure.Expanded ? "Hide build context" : "Show build context", "build-comparison-context." + disclosure.Token,
                    "pob.build-comparison.context-toggle").Classes("pob-button"),
                Text("Whole builds · each uses its own skill, equipment, passives and configuration", "build-comparison.context-warning", "pob-small pob-muted")).Classes("pob-toolbar"));
            var categoryPicker = UI.Select("Differences", Enum.GetValues<PobBuildComparisonCategory>().Select(category => new SelectOption(category.ToString(),
                category + " (" + comparison.Differences.Count(entry => entry.Category == category) + ")", "build-comparison-category." + category,
                buildComparisonCategory == category.ToString())).Prepend(new("Impact", "PoB calculated impact (" + comparison.Impact.Sum(group => group.Lines.Count) + ")",
                    "build-comparison-category.Impact", buildComparisonCategory == "Impact")).ToArray(), "pob.build-comparison.category").Classes("pob-select");
            tools.Add(UI.Grid("pob.build-comparison.filters", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()], categoryPicker,
                UI.TextEntry(buildComparisonFilter, "Find a difference", "build-comparison-filter", "pob.build-comparison.search", 256).Classes("pob-input").InGrid(column: 1)).Spacing(0, 12));
        }
        else if (buildComparison is not null) content.Add(Text("The current build changed. Compare again for current results.", "build-comparison.stale", "pob-muted"));
        var information = UI.VerticalScroll("pob.build-comparison.content", content.ToArray()).Classes("pob-scroll", "pob-build-comparison-context-scroll");
        WidgetElement body = BuildComparisonCurrent ? UI.Grid("pob.build-comparison.body", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
            information, BuildComparisonRows().InGrid(row: 1)).Spacing(8, 0).Classes("pob-workspace") : information;
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
            RenderItem = (query, row, context) => row.ImpactText is not null
                ? UI.ActionSurface("inspect", context.Id("entry"), Limit(row.Label + ", " + row.ImpactText, 3000) + ", inspect impact", ActionSurfaceOrientation.Vertical,
                    UI.Text(row.Label, context.Id("label")).Classes("pob-build-comparison-label"),
                    UI.Text(row.ImpactText, context.Id("summary")).Classes("pob-text")).Classes("pob-build-comparison-row")
                : UI.ActionSurface("inspect", context.Id("entry"), Limit(row.Label + ", " +
                    "from " + PobBuildComparisonPresentation.Identity(query.Comparison!.Current.Context) + ": " + row.Current + ", " +
                    "to " + PobBuildComparisonPresentation.Identity(query.Comparison.Other.Context) + ": " + row.Other + ", " + row.Change, 3000) + ", inspect difference",
                    ActionSurfaceOrientation.Vertical, BuildComparisonColumns(context.Id("columns"),
                        UI.Text(row.Label, context.Id("label")).Classes("pob-build-comparison-label"),
                        UI.Text(row.Current, context.Id("current")).Classes("pob-build-comparison-value"),
                        UI.Text(row.Other, context.Id("other")).Classes("pob-build-comparison-value"),
                        UI.Text(row.Change, context.Id("change")).Classes("pob-build-comparison-change")))
                    .Classes("pob-build-comparison-row"),
            OnAction = (query, row, action, token) =>
            {
                lock (gate)
                {
                    if (action.ActionId != "inspect" || !ReferenceEquals(query, buildComparisonQuery) ||
                        !ReferenceEquals(query.Comparison, buildComparison) || !BuildComparisonCurrent || buildComparisonRunning || busy) return ValueTask.CompletedTask;
                    buildComparisonReading = row; buildComparisonDetail = row.DifferenceIndex; buildComparisonReturnRow = row.Key;
                    lastFocusRequestId = CreateFocusRequest().Id;
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
                        "\nComparison: " + BuildComparisonSummary(item.entry.Other, item.entry.Category), item.index,
                        Current: BuildComparisonSummary(item.entry.Current, item.entry.Category), Other: BuildComparisonSummary(item.entry.Other, item.entry.Category),
                        Change: PobBuildComparisonPresentation.Change(item.entry)));
            var filtered = rows.Where(row => buildComparisonFilter.Length == 0 || row.Label.Contains(buildComparisonFilter, StringComparison.OrdinalIgnoreCase) ||
                row.Summary.Contains(buildComparisonFilter, StringComparison.OrdinalIgnoreCase) || row.DifferenceIndex >= 0 &&
                ((comparison.Differences[row.DifferenceIndex].Current?.Details.Contains(buildComparisonFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                 (comparison.Differences[row.DifferenceIndex].Other?.Details.Contains(buildComparisonFilter, StringComparison.OrdinalIgnoreCase) ?? false))).ToArray();
            buildComparisonQuery = new(comparison, buildComparisonCategory, buildComparisonFilter, filtered);
            buildComparisonCollection.PublishQuery(buildComparisonQuery, filtered.Length);
        }
        var list = UI.CollectionList("pob.build-comparison.rows", buildComparisonCollection, 64, "Build differences").Classes("pob-scroll");
        var headings = new List<WidgetElement> { Text(buildComparisonQuery.Rows.Length == 0 ? "No differences in this view." :
            $"{buildComparisonQuery.Rows.Length} differences · Select to inspect", "build-comparison.count", "pob-small pob-muted") };
        if (buildComparisonCategory != "Impact") headings.Add(BuildComparisonColumns("pob.build-comparison.columns",
            Text(buildComparisonCategory == "Statistics" ? "Statistic" : "Difference", "build-comparison.column.label", "pob-build-comparison-label"),
            Text("From · " + PobBuildComparisonPresentation.Identity(comparison.Current.Context), "build-comparison.column.current", "pob-build-comparison-column-heading"),
            Text("To · " + PobBuildComparisonPresentation.Identity(comparison.Other.Context), "build-comparison.column.other", "pob-build-comparison-column-heading"),
            Text("Change", "build-comparison.column.change", "pob-build-comparison-column-heading")).Classes("pob-build-comparison-table-header"));
        return QuickControls(UI.Grid("pob.build-comparison.results", [GridTrack.Auto(), GridTrack.Star()], [GridTrack.Star()],
                UI.Stack("pob.build-comparison.headings", headings.ToArray()), list.InGrid(row: 1)).Classes("pob-workspace"),
            "pob.build-comparison.rows", "pob.build-comparison.tools", "Comparison controls", NeedsQuickControls(buildComparisonQuery.Rows.Length));
    }
    private FocusGroupEntryRequest BuildComparisonFocusRequest()
    {
        if (buildComparisonDetail < 0 && buildComparisonReading is null && BuildComparisonCurrent && buildComparisonReturnRow is { } key && buildComparisonCollection is not null)
        {
            var index = Array.FindIndex(buildComparisonQuery.Rows, row => row.Key == key);
            if (index >= 0) return buildComparisonCollection.Enter("pob.build-comparison.rows", lastFocusRequestId,
                buildComparisonCollection.FocusTarget("pob.build-comparison.rows", new(key), index));
        }
        return new() { RequestId = lastFocusRequestId, GroupId = "pob.build-comparison.tools" };
    }
    private static string BuildComparisonStatLabel(string key) => key switch
    {
        "CombinedDPS" => "Main skill output (DPS / average damage)", "FullDPS" => "All included skills DPS",
        "TotalDPS" => "Main skill hit DPS", _ => System.Text.RegularExpressions.Regex.Replace(key, "([a-z])([A-Z])", "$1 $2")
    };
    private static string BuildComparisonStat(PobBuildComparisonEntry entry) => double.TryParse(entry.Value, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var value) ? StatValue(entry.Key, value) : entry.Value;
    private static string BuildComparisonSummary(PobBuildComparisonEntry? entry, PobBuildComparisonCategory category)
    {
        if (entry is null) return "Not present";
        if (category == PobBuildComparisonCategory.Statistics) return BuildComparisonStat(entry);
        return Limit(entry.Summary ?? entry.Details.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? entry.Label, 200);
    }
    private static ContainerElement BuildComparisonColumns(string id, WidgetElement label, WidgetElement current, WidgetElement other, WidgetElement change) =>
        UI.Grid(id, [GridTrack.Auto()], [GridTrack.Star(2.4), GridTrack.Star(1.35), GridTrack.Star(1.35), GridTrack.Star(1.2)],
            label, current.InGrid(column: 1), other.InGrid(column: 2), change.InGrid(column: 3)).Spacing(0, 12);
    private static WidgetElement BuildComparisonContext(PobBuildComparisonContext context, string side, string key, bool expanded)
    {
        var rows = new List<WidgetElement>
        {
            Text(side + " · " + PobBuildComparisonPresentation.Identity(context), "build-comparison.context." + key + ".name", "pob-build-comparison-label"),
            Text(context.Class + " · Level " + context.Level + " · " + context.MainSkill + " · " + context.WeaponSet,
                "build-comparison.context." + key + ".summary", "pob-build-comparison-summary")
        };
        if (expanded)
        {
            rows.Add(Text("Tree: " + context.Tree + "\nEquipment: " + context.Equipment + "\nSkills: " + context.Skills + "\nConfiguration: " + context.Configuration +
                "\nCalculation detail context: " + context.CalculationSkill + "\nTree version " + context.TreeVersion + " · Engine " + context.EngineVersion,
                "build-comparison.context." + key + ".value", "pob-small"));
            if (context.SkillOptions.Length > 0)
                AddFormatted(rows, JsonSerializer.SerializeToElement(new { text = context.SkillOptions }), "text", "runs", "build-comparison.context." + key + ".options");
        }
        return UI.Stack("pob.build-comparison.context." + key, rows.ToArray()).Classes("pob-build-comparison-context");
    }
    private WidgetElement BuildComparisonDetail(PobBuildDifference difference)
    {
        WidgetElement Side(PobBuildComparisonEntry? entry, string label, string key)
        {
            var text = entry?.Details ?? "Not present in this build";
            var rows = new List<WidgetElement> { Text(label, "build-comparison.detail." + key + ".title", "pob-heading") };
            if (difference.Category == PobBuildComparisonCategory.Statistics && entry is not null)
                rows.Add(Text(BuildComparisonStat(entry), "build-comparison.detail." + key + ".value", "pob-heading"));
            if(difference.Category==PobBuildComparisonCategory.Equipment&&entry is not null)rows.Add(SimilarEntry(key));
            // Preserve long item/modifier text without relying on Text's display truncation.
            AddFormatted(rows, JsonSerializer.SerializeToElement(new { text }), "text", "runs", "build-comparison.detail." + key + ".text");
            return UI.Stack("pob.build-comparison.detail." + key, rows.ToArray()).Classes("pob-card");
        }
        return UI.Stack("pob.build-comparison.page", UI.Stack("pob.build-comparison.tools", ManagementHeader(difference.Category == PobBuildComparisonCategory.Statistics ? BuildComparisonStatLabel(difference.Label) : difference.Label)).RememberChildFocus("pob.back"),
            UI.VerticalScroll("pob.build-comparison.detail-scroll", Text(PobBuildComparisonPresentation.Change(difference), "build-comparison.detail.kind", "pob-muted"),
                UI.Grid("pob.build-comparison.detail", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
                    Side(difference.Current, "From · " + PobBuildComparisonPresentation.Identity(buildComparison!.Current.Context), "current"),
                    Side(difference.Other, "To · " + PobBuildComparisonPresentation.Identity(buildComparison.Other.Context), "other").InGrid(column: 1)).Spacing(0, 16)).Classes("pob-scroll")).Classes("pob-workspace");
    }
}
