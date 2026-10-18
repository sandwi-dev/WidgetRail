using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string gemSuggestionSource = "pob.gems.initial";
    private TextSuggestionCatalog? gemSuggestions;

    // Called only when the upstream catalog or its type filter changes, never per keystroke/render.
    private void RefreshGemSuggestions()
    {
        gemSuggestionSource = "pob.gems." + Guid.NewGuid().ToString("N");
        gemSuggestions = new(gemCatalog.Select((gem, index) => (gem, index))
            .Where(entry => gemType == "all" || B(entry.gem, "support") == (gemType == "support"))
            .Select(entry => new TextSuggestion("gem." + entry.index, S(entry.gem, "name"), S(entry.gem, "name"),
                B(entry.gem, "support") ? "Support gem" : "Skill gem")));
    }

    public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken cancellationToken)
    {
        TextSuggestionCatalog? catalog;
        lock (gate)
        {
            if (query.FieldId != "pob.gem-search" || detailKind != "gem-picker" || query.SourceKey != gemSuggestionSource)
                return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
            catalog = gemSuggestions;
        }
        // The immutable index is independent of PoB calculations and the committed search state.
        return ValueTask.FromResult(catalog?.Search(query, cancellationToken) ?? TextSuggestionResult.Empty);
    }
}
