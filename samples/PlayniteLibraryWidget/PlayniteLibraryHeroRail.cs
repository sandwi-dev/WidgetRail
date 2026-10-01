using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryHeroRailItem(
    PlayniteLibraryItem? Current,
    PlayniteLibraryDisplayItem Display,
    bool Favorite,
    bool Preferred,
    int GroupSize,
    bool CollectionItem)
{
    internal WidgetCollectionItemKey Key =>
        Current?.Key ?? PlayniteLibraryIdentity.Key(Display.SavedId);
}

internal static class PlayniteLibraryHeroRailPresentation
{
    internal static WidgetElement Fallback(string title, string detail) =>
        UI.Card("playnite-library.hero",
                UI.Icon(WidgetGlyph.Play, "playnite-library.hero.artwork", title)
                    .Classes("playnite-library-hero-artwork"),
                UI.Stack("playnite-library.hero.content",
                        UI.Text(title, "playnite-library.hero.title", title)
                            .Classes("playnite-library-hero-title"),
                        UI.Text(detail, "playnite-library.hero.state", detail)
                            .Classes("playnite-library-hero-state"))
                    .Classes("playnite-library-hero-content"))
            .Classes("playnite-library-hero", "wrail-surface-raised");
}
