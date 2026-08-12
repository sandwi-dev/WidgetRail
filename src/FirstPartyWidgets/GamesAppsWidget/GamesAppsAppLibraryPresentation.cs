using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GamesApps;

internal static class GamesAppsAppLibraryPresentation
{
    internal static string DisplayName(WidgetAppLibraryItem item) =>
        item.Presentation.DisplayName;

    internal static WidgetAppLibraryKind Kind(WidgetAppLibraryItem item) =>
        item.Presentation.Kind;

    internal static string Source(WidgetAppLibraryItem item) =>
        item.Presentation.Source.DisplayName;

    internal static WidgetAppLibraryArtwork? TileArtwork(WidgetAppLibraryItem item) =>
        item.Presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile);

    internal static bool CanLaunch(WidgetAppLibraryItem item) =>
        item.Presentation.Availability.IsLaunchable &&
        item.Presentation.Capabilities.Supports(WidgetAppLibraryAction.Launch);
}
