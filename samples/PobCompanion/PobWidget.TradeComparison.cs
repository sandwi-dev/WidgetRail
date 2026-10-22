using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string tradeComparisonView = "changes";
    private bool tradeShowUnchanged, tradePurchaseExpanded;
    private string tradeWorkspaceFocus = "pob.item.compare.controls";
    private PobResult? modifierOwner;
    private string? modifierSlot;
    private PobItemModifierComparison modifierComparison = new([], false);
    private sealed record ItemModifierQuery(PobItemModifierComparison Comparison, bool ShowUnchanged, PobModifierDifference[] Rows);
    private ItemModifierQuery itemModifierQuery = new(new([], false), false, []);
    private WidgetIndexedCollection<ItemModifierQuery, PobModifierDifference>? itemModifierCollection;

    private PobItemModifierComparison CurrentModifierComparison()
    {
        if (!ReferenceEquals(modifierOwner, result) || modifierSlot != previewSlot)
        {
            modifierOwner = result; modifierSlot = previewSlot;
            modifierComparison = PobItemModifierComparison.Read(State, previewSlot ?? "");
        }
        return modifierComparison;
    }

    private bool HandleComparisonViewAction(string id)
    {
        if (pendingBefore is null || detailKind != "choose-slot") return false;
        if (id.StartsWith("trade-review-view.", StringComparison.Ordinal))
        {
            var value = id[18..];
            if (value is "changes" or "items" or "impact") tradeComparisonView = value;
            ReturnFromModifierReading();
            return true;
        }
        if (id == "trade-review-unchanged") { tradeShowUnchanged = !tradeShowUnchanged;ReturnFromModifierReading();return true; }
        if (id == "trade-review-candidates" && HasRanking && rankedItems.Length>0)
        { tradeWorkspaceFocus = "pob.rank.list"; lastFocusRequestId = CreateFocusRequest().Id; return true; }
        if (id == "trade-review-reading" && tradeComparisonView=="changes" && itemModifierQuery.Rows.Length>0)
        { tradeWorkspaceFocus="pob.item.compare.reading";websiteFocus="pob.website.review.reading";websiteFocusSequence=(lastFocusRequestId = CreateFocusRequest().Id);return true; }
        if (tradeListing is { } listing && id == "trade-review-purchase." + listing.Id)
        { tradePurchaseExpanded = !tradePurchaseExpanded; return true; }
        return false;
    }

    private void ReturnFromModifierReading()
    {
        if(tradeWorkspaceFocus!="pob.item.compare.reading"&&websiteFocus!="pob.website.review.reading")return;
        tradeWorkspaceFocus="pob.item.compare.controls";websiteFocus="pob.website.review.tools";websiteFocusSequence=(lastFocusRequestId = CreateFocusRequest().Id);
    }

    private static WidgetElement ModifierCell(PobModifierLine[] lines,string status,string side,string prefix)
    {
        var content = new List<WidgetElement>();
        if(lines.Length==0)content.Add(Text("Not on "+side+" item",prefix+".empty","pob-modifier-absent"));
        var tone=status switch
        {
            "removed"=>"warning", "added"=>"green", "same"=>"muted", _=>"gold",
        };
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            // Diff colour describes membership/change, not PoB's gameplay valuation.
            // Full items retains the untouched upstream formatting and warning colours.
            var runs=ReadFormattedRuns(line.Formatted,"text","runs").Select(run=>run with{Tone=tone}).ToList();
            if(line.Kind!="Explicit"&&(index==0||lines[index-1].Kind!=line.Kind))
                runs.Add(new(" · "+line.Kind,"muted"));
            AddRuns(content,runs,prefix+".line."+index,"pob-modifier-text");
        }
        return UI.Stack("pob." + prefix, content.ToArray()).Classes("pob-modifier-cell");
    }

    internal static WidgetElement ModifierDifferenceRow(PobModifierDifference row,string prefix)
    {
        var (label,tone,fallback)=row.Status switch
        {
            "added"=>("Added","pob-green",WidgetGlyph.Check),
            "removed"=>("Removed","pob-life",WidgetGlyph.Warning),
            "same"=>("Unchanged","pob-muted",WidgetGlyph.Repeat),
            _=>("Changed","pob-gold",WidgetGlyph.Refresh),
        };
        return UI.ActionSurface("inspect",prefix+".row",
            row.Status+" modifier. Equipped: "+string.Join("; ",row.Before.Select(line=>S(line.Formatted,"text")))+
            ". Proposed: "+string.Join("; ",row.After.Select(line=>S(line.Formatted,"text")))+". View full items.",ActionSurfaceOrientation.Vertical,
            UI.Grid(prefix+".values", [GridTrack.Auto()], [GridTrack.Pixel(24), GridTrack.Star(), GridTrack.Star()],
                UI.Icon(WidgetIcon.PackageSvg("change."+row.Status,WidgetPackageIconColorMode.ThemeTint,fallback),prefix+".status",label+" modifier").Classes("pob-modifier-status",tone),
                ModifierCell(row.Before,row.Status,"equipped",prefix+".before").InGrid(column:1), ModifierCell(row.After,row.Status,"proposed",prefix+".after").InGrid(column:2))
                .Spacing(0,12).Classes("pob-modifier-values"))
            .Classes("pob-modifier-row");
    }

    private WidgetElement ModifierChanges(string prefix)
    {
        var comparison = CurrentModifierComparison();
        itemModifierCollection ??= CreateIndexedCollection<ItemModifierQuery, PobModifierDifference>("pob.trade.modifiers", itemModifierQuery, 0, new()
        {
            ReadRange = (query, start, count, token) =>
            { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<IReadOnlyList<PobModifierDifference>>(query.Rows.Skip(start).Take(count).ToArray()); },
            ItemKey = row => new(row.Id),
            RenderItem = (_, row, context) => ModifierDifferenceRow(row,context.Id("modifier")),
            OnAction = (query, _, action, _) =>
            {
                lock(gate)
                {
                    if(!ReferenceEquals(query,itemModifierQuery)||busy||pendingBefore is null||action.ActionId!="inspect")return ValueTask.CompletedTask;
                    tradeComparisonView="items";tradeWorkspaceFocus="pob.item.compare.controls";
                    websiteFocus="pob.website.review.tools";websiteFocusSequence=(lastFocusRequestId = CreateFocusRequest().Id);
                    Invalidate();return ValueTask.CompletedTask;
                }
            },
        });
        if (!ReferenceEquals(itemModifierQuery.Comparison, comparison) || itemModifierQuery.ShowUnchanged != tradeShowUnchanged)
        {
            itemModifierQuery = new(comparison, tradeShowUnchanged, comparison.Rows.Where(row => tradeShowUnchanged || row.Status != "same").ToArray());
            itemModifierCollection.PublishQuery(itemModifierQuery, itemModifierQuery.Rows.Length);
        }
        if (!comparison.Available)
            return UI.VerticalScroll("pob." + prefix + ".details", Text("PoB modifier matching is unavailable for this comparison. Full items and Build impact remain available.", prefix + ".unavailable", "pob-muted")).Classes("pob-scroll");
        if (itemModifierQuery.Rows.Length == 0)
            return UI.VerticalScroll("pob." + prefix + ".details", Text("No modifier changes. Full items includes base properties, requirements and sockets.", prefix + ".empty", "pob-muted")).Classes("pob-scroll");
        return UI.CollectionList("pob." + prefix + ".details", itemModifierCollection, 52, "Equipped and proposed modifier changes").Classes("pob-scroll","pob-modifier-diff");
    }

    private WidgetElement ComparisonReading(string prefix)
    {
        var before = A(pendingBefore!.State, "items").FirstOrDefault(item => S(item, "slot") == previewSlot);
        var after = A(State, "items").FirstOrDefault(item => S(item, "slot") == previewSlot);
        WidgetElement body;
        if (tradeComparisonView == "changes") body = ModifierChanges(prefix);
        else
        {
            var rows = new List<WidgetElement> { Text(tradeComparisonView == "items" ? "Full item details" : "PoB calculated build impact", prefix + ".reading-start", "pob-eyebrow") };
            if (tradeComparisonView == "impact") rows.AddRange(TradeImpact(prefix));
            else
            {
                WidgetElement Item(JsonElement item, string side) => UI.Stack("pob." + prefix + "." + side,
                    item.ValueKind == JsonValueKind.Object ? ItemDetails(item, prefix + "." + side + ".item").Skip(2).ToArray()
                        : [Text("Empty slot", prefix + "." + side + ".empty", "pob-muted")]).Classes("pob-modifier-cell");
                rows.Add(UI.Grid("pob." + prefix + ".full-items", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
                    Item(before, "equipped"), Item(after, "proposed").InGrid(column: 1)).Spacing(0, 12));
            }
            body = UI.VerticalScroll("pob." + prefix + ".details", rows.ToArray()).Classes("pob-scroll");
        }
        var reading=UI.Stack("pob." + prefix + ".reading", body)
            .Shortcut(ControllerButton.LeftStick, "trade-review-focus", "Comparison controls").Classes("pob-workspace");
        return body is IndexedCollectionElement?reading.RememberChildFocus("pob."+prefix+".details"):reading;
    }

    private WidgetElement BuildTradeComparison(string prefix, bool expanded)
    {
        var before = A(pendingBefore!.State, "items").FirstOrDefault(item => S(item, "slot") == previewSlot);
        var after = A(State, "items").FirstOrDefault(item => S(item, "slot") == previewSlot);
        var controls = new List<WidgetElement>
        {
            Text(S(after, "name", "Item comparison"), prefix + ".title", "pob-heading"),
            Text((tradeListing is { } listing ? listing.Price + " · " : "") + previewSlot + " · not equipped", prefix + ".summary", "pob-gold"),
        };
        var selectors=new List<WidgetElement>();
        if (CanReplaceTradeComparison)
        {
            if (!expanded && HasRanking) controls.Add(Button("Ranked results", "trade-rank-show", prefix + ".rank-back"));
            else if(!HasRanking&&tradeCandidates.Count>1)controls.Add(Button("Rank recent","trade-rank-recent",prefix+".rank-recent"));
            if ((!expanded || !HasRanking) && tradeCandidates.Count > 1)
                selectors.Add(SelectWithUnavailable("Recent comparisons", tradeCandidates.Select(candidate => new SelectOption(candidate.Listing.Id,
                    TradeItemName(candidate.Listing), "trade-review-recent." + candidate.Listing.Id, candidate.Listing.Id == tradeListing?.Id)).ToArray(), "pob." + prefix + ".recent").Classes("pob-select"));
            if (itemDraftSlots.Length > 1)
                selectors.Add(SelectWithUnavailable("Compare against", itemDraftSlots.Select((slot, index) => new SelectOption("slot" + index,
                    ComparisonSlotLabel(slot), "trade-review-slot." + index, slot == previewSlot)).ToArray(), "pob." + prefix + ".slot").Classes("pob-select"));
        }
        if(selectors.Count>0)controls.Add(UI.Grid("pob."+prefix+".selectors",[GridTrack.Auto()],selectors.Select(_=>GridTrack.Star()).ToArray(),selectors.Select((element,index)=>element.InGrid(column:index)).ToArray()).Spacing(0,8));
        var viewOptions=new List<WidgetElement>{UI.Select("View", new[] { ("changes", "Changes"), ("items", "Full items"), ("impact", "Build impact") }
            .Select(mode => new SelectOption(mode.Item1, mode.Item2, "trade-review-view." + mode.Item1, tradeComparisonView == mode.Item1)).ToArray(), "pob." + prefix + ".view").Classes("pob-select")};
        if (tradeComparisonView == "changes")
        {
            var comparison = CurrentModifierComparison();
            var unchanged = comparison.Rows.Count(row => row.Status == "same");
            viewOptions.Add(Button((tradeShowUnchanged ? "Hide" : "Show") + $" unchanged ({unchanged})", "trade-review-unchanged", prefix + ".unchanged").Selected(tradeShowUnchanged));
        }
        controls.Add(UI.Grid("pob."+prefix+".view-options",[GridTrack.Auto()],viewOptions.Select(_=>GridTrack.Star()).ToArray(),viewOptions.Select((element,index)=>element.InGrid(column:index)).ToArray()).Spacing(0,8));
        if (CanReplaceTradeComparison)
        {
            var first=expanded?Button("Back to results","trade-review-back",prefix+".browse"):
                Button("Expand","trade-review-expand",prefix+".expand").FocusLeft("pob.website.browser");
            var actions = new List<WidgetElement>{first.FocusRight("pob."+prefix+".find"),
                Button("Find listing","trade-review-find",prefix+".find").FocusLeft(first.Id).FocusRight("pob."+prefix+".equip"),
                Button("Equip in build","trade-review-equip",prefix+".equip").FocusLeft("pob."+prefix+".find")};
            controls.Add(UI.Row("pob." + prefix + ".actions", actions.ToArray()).Classes("pob-toolbar"));
            controls.Add(UI.Row("pob." + prefix + ".secondary-actions",
                Button(tradePurchaseExpanded ? "Hide purchase details" : "Purchase details", "trade-review-purchase." + tradeListing!.Id, prefix + ".purchase-toggle").FocusRight("pob."+prefix+".close"),
                Button("Finish comparing", "trade-review-close", prefix + ".close").FocusLeft("pob."+prefix+".purchase-toggle").Classes("pob-button","pob-discard")).Classes("pob-toolbar"));
            if (tradePurchaseExpanded) AddTradeContext(controls, prefix);
        }
        if(!CanReplaceTradeComparison)
        {
            AddTradeListing(controls);
            if(tradeListing is not null)controls.Add(Button("Keep this item and continue comparing","trade-review-adopt"));
        }
        var tools = UI.Stack("pob." + prefix + ".tools", controls.ToArray()).RememberChildFocus("pob." + prefix + ".view").Classes("pob-buildtools");
        if(tradeComparisonView=="changes"&&CurrentModifierComparison().Rows.Any(row=>tradeShowUnchanged||row.Status!="same"))
            tools=tools.Shortcut(ControllerButton.X,"trade-review-reading","Modifier changes");
        WidgetElement header = tools;
        if (expanded)
        {
            header = UI.Stack("pob.item.compare.controls", tools).RememberChildFocus("pob." + prefix + ".view");
            if (HasRanking&&rankedItems.Length>0) header = ((ContainerElement)header).Shortcut(ControllerButton.LeftStick, "trade-review-candidates", "Ranked candidates");
        }
        var gutter=tradeComparisonView=="changes"?1:0;
        var columns = UI.Grid("pob." + prefix + ".columns", [GridTrack.Auto()], gutter==1?[GridTrack.Pixel(24),GridTrack.Star(),GridTrack.Star()]:[GridTrack.Star(),GridTrack.Star()],
            UI.Stack("pob." + prefix + ".before-heading", Text("EQUIPPED", prefix + ".before-label", "pob-eyebrow pob-muted"), Text(S(before, "name", "Empty slot"), prefix + ".before-name", "pob-list-title")).InGrid(column:gutter),
            UI.Stack("pob." + prefix + ".after-heading", Text("PROPOSED", prefix + ".after-label", "pob-eyebrow pob-blue"), Text(S(after, "name"), prefix + ".after-name", "pob-list-title")).InGrid(column:gutter+1)).Spacing(0, 12).Classes("pob-comparison-columns");
        var panel = UI.Stack("pob." + prefix + ".panel", header, columns,
            expanded&&websiteVisible?Text("Comparison open beside listings",prefix+".covered","pob-muted"):ComparisonReading(prefix)).Classes("pob-panel", "pob-tree-main");
        return expanded && !websiteVisible && CanReplaceTradeComparison && HasRanking && rankedItems.Length > 0
            ? Split("trade-candidates", TradeRankingPanel(workspace: true), panel, 1, 2.4) : panel;
    }
}
