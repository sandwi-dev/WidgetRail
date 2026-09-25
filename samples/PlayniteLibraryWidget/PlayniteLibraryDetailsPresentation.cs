using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteLibraryDetailsPresentation
{
    internal const string PlayId = "playnite-library.details.play";

    internal static WidgetModal Create(PlayniteLibraryItem item, bool current,
        string? launchingSavedId, string status, PlayniteLibraryLaunchState? launchState,
        bool loading = false, string? error = null)
    {
        var presentation = item.Presentation;
        var metadata = presentation.Metadata;
        var availability = PlayniteLibraryAvailabilityPresentation.Tile(item);
        var launching = item.Value.SavedId == launchingSavedId;
        var artwork = presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Hero) ??
            presentation.Artwork.Find(WidgetAppLibraryArtworkRole.Tile);
        var children = new List<WidgetElement>();
        var play = UI.Button(launching ? "Starting…" : "Play", PlayniteLibraryActions.Launch, PlayId)
            .Disabled(!current || !availability.Launchable)
            .Busy(launching).Classes("playnite-library-details-play");
        var hero = new List<WidgetElement>
        {
            UI.Text(presentation.Source.DisplayName, "playnite-library.details.source")
                .Classes("playnite-library-details-meta"), play,
        };
        hero.Add(UI.Text(!current ? "This game is no longer in the current results. Close details and refresh the library."
            : PlayniteLibraryAvailabilityPresentation.IsUninstalled(item)
                ? "Not installed. Install this game in Playnite, then refresh the library."
                : launching || launchState is not null ? status
                : availability.Status == "Play" ? "Installed" : availability.Status,
            "playnite-library.details.status").Classes("playnite-library-details-meta"));
        var heroContent = UI.Stack("playnite-library.details.hero.content", hero.ToArray())
            .Classes("playnite-library-details-hero-content");
        children.Add(UI.BackgroundSurface(heroContent, "playnite-library.details.hero",
            artwork is { Handle.Length: > 0 }
                ? BackgroundSurfaceArtwork.FromHandle(new WidgetArtworkHandle(artwork.Handle)) : null)
            .Classes("playnite-library-details-hero"));
        if (loading) children.Add(UI.Row("playnite-library.details.loading",
            UI.LoadingIndicator("playnite-library.details.spinner", size: LoadingIndicatorSize.Compact),
            UI.Text("Loading game details...", "playnite-library.details.loading.text")));
        if (error is not null) children.Add(UI.Text(error, "playnite-library.details.error"));
        if (metadata?.PlaytimeMinutes is { } minutes)
            children.Add(UI.ValueRow("Time played", minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m",
                "playnite-library.details.playtime"));
        if (metadata?.LastPlayedAtUnixMilliseconds is { } played &&
            played is >= -62135596800000 and <= 253402300799999)
            children.Add(UI.ValueRow("Last played", DateTimeOffset.FromUnixTimeMilliseconds(played)
                .ToLocalTime().ToString("d"), "playnite-library.details.last-played"));
        if (!string.IsNullOrWhiteSpace(metadata?.Version))
            children.Add(UI.ValueRow("Version", metadata.Version, "playnite-library.details.version"));
        if (metadata?.Categories.Count > 0)
            children.Add(UI.Text(string.Join(" · ", metadata.Categories), "playnite-library.details.categories")
                .Classes("playnite-library-details-meta"));
        if (error is null && (!loading || !string.IsNullOrWhiteSpace(metadata?.Description)))
            children.Add(UI.Text(string.IsNullOrWhiteSpace(metadata?.Description)
                ? "No description is available for this game." : metadata.Description,
            "playnite-library.details.description").Classes("playnite-library-details-description"));
        return new("playnite-library.details", presentation.DisplayName,
            UI.Stack("playnite-library.details.content", children.ToArray()).Classes("playnite-library-details-content"),
            PlayId, PlayniteLibraryActions.DetailsClose);
    }
}
