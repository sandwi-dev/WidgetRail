using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Marketplace;

public sealed partial class MarketplaceWidget
{
    private sealed record BrowseContext(MarketplaceSnapshot Snapshot, Section Section, string Search,
        string? Selected, string? ReviewToken, string SourceKey, MarketplaceItem[] Items, TextSuggestionCatalog Suggestions)
    {
        internal bool Matches(State state) => Section == state.Section && Search == state.Search &&
            Selected == state.Selected && ReviewToken == state.ReviewToken &&
            (ReferenceEquals(Snapshot, state.Snapshot) || SameListings(Snapshot.Items, state.Snapshot.Items) &&
                (Section != Section.Updates ||
                    (Snapshot.UpdateBatch?.Items.Select(item => item.PackageId) ?? []).SequenceEqual(
                        state.Snapshot.UpdateBatch?.Items.Select(item => item.PackageId) ?? [], StringComparer.Ordinal)));
    }

    // Transport snapshots own new list instances. Only listing changes affect
    // these read-only previews; operation progress and check times do not.
    private static bool SameListings(IReadOnlyList<MarketplaceItem> left, IReadOnlyList<MarketplaceItem> right) =>
        ReferenceEquals(left, right) || left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First with { Screenshots = pair.Second.Screenshots, SetupRequirements = pair.Second.SetupRequirements } == pair.Second &&
            pair.First.Screenshots.SequenceEqual(pair.Second.Screenshots, StringComparer.Ordinal) &&
            (pair.First.SetupRequirements ?? []).SequenceEqual(pair.Second.SetupRequirements ?? [], StringComparer.Ordinal));

    private readonly object browseGate = new();
    private BrowseContext? browseContext;
    private WidgetFocusPreviewSource<BrowseContext>? browsePreviews;

    private BrowseContext BrowseContextFor(State state)
    {
        lock (browseGate)
        {
            if (browseContext?.Matches(state) == true) return browseContext;
            var inSection = state.Snapshot.Items.Where(item => state.Section switch
            {
                Section.Installed => item.InstalledVersion is not null,
                Section.Updates => UpdateAvailable(item) || state.Snapshot.UpdateBatch?.Items.Any(result => result.PackageId == item.Id) == true,
                _ => true,
            }).ToArray();
            var suggestions = inSection.Select(item => new TextSuggestion(Key(item.Id), item.Name, item.Name))
                .Where(item => TextSuggestionContract.IsValid(new([item]), 1, 128));
            var terms = state.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var items = state.Selected is not null || state.ReviewToken is not null ? [] : inSection.Where(item => terms.All(term =>
                item.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.SetupRequirements?.Any(text => text.Contains(term, StringComparison.OrdinalIgnoreCase)) == true)).ToArray();
            browseContext = new(state.Snapshot, state.Section, state.Search, state.Selected, state.ReviewToken,
                "marketplace.search." + Guid.NewGuid().ToString("N"), items, new(suggestions));
            if (browsePreviews is null)
                browsePreviews = CreateFocusPreviewSource("marketplace.preview.source", browseContext, new WidgetFocusPreviewOptions<BrowseContext>
                {
                    Resolve = (context, key, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        var item = context.Items.SingleOrDefault(item => Key(item.Id) == key)
                            ?? throw new ArgumentException("Unknown catalogue preview key.");
                        return ValueTask.FromResult(BrowsePreview(item));
                    },
                });
            else browsePreviews.Publish(browseContext);
            return browseContext;
        }
    }

    public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (browseGate)
        {
            var state = model.Value;
            if (!IsActive || state.Selected is not null || state.ReviewToken is not null || browseContext is not { } context ||
                !context.Matches(state) || query.FieldId != "marketplace.search" || query.SourceKey != context.SourceKey)
                return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
            return ValueTask.FromResult(context.Suggestions.Search(query, token));
        }
    }

    private WidgetElement WithBrowsePreviews(WidgetElement grid) =>
        UI.FocusPresentationSurface(grid, UI.Text("", "marketplace.preview.empty"), "marketplace.previews") with
        {
            Preview = new(Width: 440, Height: 480) { SizeToContentHeight = true },
            PreviewSource = browsePreviews!.Descriptor,
        };

    private static WidgetElement BrowsePreview(MarketplaceItem item)
    {
        var id = "marketplace.preview.item";
        var content = new List<WidgetElement>
        {
            UI.Text(item.Name, id + ".title").Classes("section-heading"),
            ItemBadges(item, id),
            UI.Text(item.Description, id + ".description").Classes("diagnostic-line"),
            UI.Text(ItemStatus(item), id + ".version").Classes("page-help"),
        };
        if (item.DownloadBytes is { } size)
            content.Add(UI.Text($"{size / 1048576d:0.0} MiB download", id + ".size").Classes("page-help"));
        if (item.SetupRequirements is { Count: > 0 } requirements)
        {
            content.Add(UI.Text("Before you install", id + ".setup.title").Classes("catalogue-gold"));
            content.AddRange(requirements.Select((text, index) => UI.Text("• " + text, id + ".setup." + index).Classes("diagnostic-line")));
        }
        return UI.Stack(id, content.ToArray()).Classes("marketplace-rich-preview");
    }
}
