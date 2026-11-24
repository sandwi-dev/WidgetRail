using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.Marketplace;

public sealed partial class MarketplaceWidget
{
    private sealed record Feature(string Id, string Summary, string Icon, WidgetGlyph Glyph,
        WidgetPackageIconColorMode Color = WidgetPackageIconColorMode.OriginalColor);

    private static readonly Feature[] Featured =
    [
        new("widgetrail.samples.discord-controls", "Voice, chat and screen sharing", "discord.mark", WidgetGlyph.Connection),
        new("widgetrail.firstparty.capture-library", "Screenshots, clips and replays", "captures.mark", WidgetGlyph.Fullscreen, WidgetPackageIconColorMode.ThemeTint),
        new("widgetrail.samples.playnite-library", "Your games and achievements", "playnite-library.mark", WidgetGlyph.Play),
        new("widgetrail.samples.ytmusic", "Albums, mixes and playlists", "ytmusic.mark", WidgetGlyph.Music),
        new("widgetrail.samples.spotify", "Your library and playback", "spotify.brand.green", WidgetGlyph.Music),
        new("widgetrail.samples.youtube-video", "Search, watch and pin videos", "youtube.brand.red", WidgetGlyph.Play),
    ];

    private static readonly string[] Essentials =
    [
        "widgetrail.firstparty.audio-mixer", "widgetrail.firstparty.performance-monitor", "widgetrail.samples.display-profiles",
        "widgetrail.firstparty.media-sessions", "widgetrail.firstparty.network-controls", "widgetrail.firstparty.games-apps",
        "widgetrail.firstparty.power",
    ];

    private static WidgetElement CuratedHome(MarketplaceItem[] items, out string? first)
    {
        var remaining = items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var shelves = new List<WidgetElement>();
        var cards = new List<WidgetElement>();
        foreach (var feature in Featured)
        {
            if (!remaining.Remove(feature.Id, out var item)) continue;
            var id = CardId(item);
            var mark = WidgetIcon.PackageSvg(feature.Icon, feature.Color, feature.Glyph);
            cards.Add(BrowseCard(item, id, [
                UI.Row(id + ".heading", UI.Icon(mark, id + ".mark", item.Name).Classes("feature-mark"),
                    UI.Text(item.Name, id + ".name").Classes("feature-title")).Classes("feature-heading"),
                UI.Text(feature.Summary, id + ".summary").Classes("feature-summary"),
                ItemBadges(item, id)], "feature-card"));
        }
        string? firstCard = null;
        Shelf("featured", "Featured", 265, 3);
        foreach (var key in Essentials)
            if (remaining.Remove(key, out var item)) cards.Add(BrowseCard(item, CardId(item)));
        Shelf("essentials", "Essentials", 310, 3);
        foreach (var item in items)
            if (remaining.ContainsKey(item.Id)) cards.Add(BrowseCard(item, CardId(item)));
        Shelf("more", "More widgets", 310, 3);
        first = firstCard;
        return UI.Stack("marketplace.items", shelves.ToArray()).Classes("catalogue-shelves");

        void Shelf(string key, string title, int width, int columns)
        {
            if (cards.Count == 0) return;
            firstCard ??= cards[0].Id;
            var id = "marketplace.shelf." + key;
            shelves.Add(UI.Stack(id, UI.Text(title, id + ".title").Classes("catalogue-shelf-title"),
                UI.ResponsiveGrid(id + ".items", width, columns, cards.ToArray())).Classes("catalogue-shelf"));
            cards.Clear();
        }
    }

    private static string CardId(MarketplaceItem item) => "marketplace.item." + Key(item.Id);

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
            pair.First with { Screenshots = pair.Second.Screenshots, SetupRequirements = pair.Second.SetupRequirements, Details = pair.Second.Details } == pair.Second &&
            (pair.First.Details ?? []).SequenceEqual(pair.Second.Details ?? []) &&
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
                item.SetupRequirements?.Any(text => text.Contains(term, StringComparison.OrdinalIgnoreCase)) == true ||
                item.Details?.Any(detail => detail.Matches(term)) == true)).ToArray();
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
            Preview = new(Width: 440, Height: 480, DelayMilliseconds: 1500) { SizeToContentHeight = true },
            PreviewSource = browsePreviews!.Descriptor,
        };

    private static WidgetElement BrowsePreview(MarketplaceItem item)
    {
        var id = "marketplace.preview.item";
        var content = new List<WidgetElement>
        {
            UI.Text(item.Name, id + ".title").Classes("section-heading"),
            ItemBadges(item, id),
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
