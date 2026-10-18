using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    private string gemSuggestionSource = "pob.gems.initial";
    private TextSuggestionCatalog? gemSuggestions;
    private sealed record SearchSuggestions(object Owner, string Context, string Source, PobSearchSuggestions Catalog);
    private readonly Dictionary<string, SearchSuggestions> searchSuggestions = new();
    private readonly HashSet<string> activeSearchSuggestions = new();
    private string suggestionRoute = "";

    private string SuggestionRoute => documentId + ":" + section + ":" + detailKind;
    private string SuggestionContext(string field) => SuggestionRoute + ":" + (field switch
    {
        "pob.library.search" => libraryClass + ":" + libraryMetadataRevision,
        "pob.calculation.search" => calculationSection,
        "pob.choice-search" => featurePicker?.Prefix,
        _ => "",
    });
    private object? SuggestionOwner(string field) => field switch
    {
        "pob.search" => result,
        "pob.choice-search" => featurePicker?.Choices,
        "pob.inventory-search" => inventoryState,
        "pob.library.search" => savedBuilds,
        "pob.calculation.search" => calculationCatalog,
        _ => null,
    };

    private void BeginSearchSuggestions()
    {
        activeSearchSuggestions.Clear();
        if (suggestionRoute != SuggestionRoute) { searchSuggestions.Clear(); suggestionRoute = SuggestionRoute; }
    }

    private TextEntryElement SearchField(TextEntryElement field, object owner,
        Func<IEnumerable<(string Title, string SearchText, string Description)>> entries)
    {
        var context = SuggestionContext(field.Id);
        if (!searchSuggestions.TryGetValue(field.Id, out var cache) || !Equals(cache.Owner, owner) || cache.Context != context)
        {
            cache = new(owner, context, "pob.search." + Guid.NewGuid().ToString("N"), new(entries(), field.MaximumLength));
            searchSuggestions[field.Id] = cache;
        }
        activeSearchSuggestions.Add(field.Id);
        return field.Suggest(cache.Source);
    }

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
        TextSuggestionCatalog? catalog = null;
        PobSearchSuggestions? search = null;
        lock (gate)
        {
            if (busy) return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
            if (query.FieldId == "pob.gem-search" && detailKind == "gem-picker" && query.SourceKey == gemSuggestionSource)
                catalog = gemSuggestions;
            else if (activeSearchSuggestions.Contains(query.FieldId) && searchSuggestions.TryGetValue(query.FieldId, out var cache) &&
                cache.Context == SuggestionContext(query.FieldId) && cache.Source == query.SourceKey && Equals(cache.Owner, SuggestionOwner(query.FieldId)))
                search = cache.Catalog;
            else return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
        }
        // The immutable index is independent of PoB calculations and the committed search state.
        return ValueTask.FromResult(catalog?.Search(query, cancellationToken) ?? search?.Search(query, cancellationToken) ?? TextSuggestionResult.Empty);
    }
}
