using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal sealed record GameLauncherPresentationState(
    WidgetCursorResourceSnapshot<GameLauncherItem> Collection,
    IReadOnlyList<GameLauncherDisplayItem> WarmItems,
    string Status,
    string? LaunchingSavedId,
    bool Interactive);

internal static class GameLauncherPresentation
{
    internal const string ScrollId = "game-launcher.library.scroll";
    internal const string RetryId = "game-launcher.retry";
    private static readonly WidgetSurfaceHints Surface = new()
    {
        Mode = WidgetSurfaceMode.Wide,
        PreferredWidth = 980,
        PreferredHeight = 700,
        MinimumWidth = 420,
        MinimumHeight = 340,
    };

    internal static WidgetView Render(GameLauncherPresentationState state)
    {
        var snapshot = state.Collection;
        var header = UI.Stack("game-launcher.header",
                UI.Text("INSTALLED GAMES", "game-launcher.eyebrow", "Installed games")
                    .Classes("game-launcher-eyebrow"),
                UI.Text("Game Launcher", "game-launcher.title", "Game Launcher")
                    .Classes("game-launcher-title"),
                UI.Text(state.Status, "game-launcher.status", state.Status)
                    .Classes("game-launcher-status"))
            .Classes("game-launcher-header");

        WidgetElement content;
        string? initialFocus = snapshot.RequestedFocusId;
        if (snapshot.Items.Count != 0)
        {
            var tiles = snapshot.Items.Select(item => Tile(
                item.Value.DisplayName,
                item.Value.SourceAttribution,
                item.Value.SavedId,
                item.Value.ArtworkHandle,
                state.LaunchingSavedId,
                state.Interactive,
                item.Key)).ToArray();
            var grid = UI.ResponsiveGrid("game-launcher.library.grid", 170, 5, tiles)
                .Classes("game-launcher-grid");
            var scroll = UI.VerticalScroll(ScrollId, grid).Classes("game-launcher-scroll");
            if (snapshot.Status != WidgetPagedResourceStatus.Error &&
                (snapshot.HasBefore || snapshot.HasAfter))
                scroll = scroll.Paginate(
                    snapshot.HasBefore ? "game-launcher.library.cursor.before" : null,
                    snapshot.HasAfter ? "game-launcher.library.cursor.after" : null, 2);
            scroll = scroll with { CollectionAnchorKey = snapshot.Anchor?.Value };
            var controls = UI.Row("game-launcher.actions",
                    UI.Button("Previous page", "game-launcher.previous", "game-launcher.previous")
                        .Disabled(!snapshot.HasBefore || !state.Interactive),
                    UI.Button("Refresh", "game-launcher.refresh", "game-launcher.refresh")
                        .Disabled(!state.Interactive),
                    UI.Button("Next page", "game-launcher.next", "game-launcher.next")
                        .Disabled(!snapshot.HasAfter || !state.Interactive))
                .Classes("game-launcher-actions");
            var children = new List<WidgetElement> { scroll, controls };
            if (snapshot.Error is { } retained)
                children.Add(UI.Alert("Some games are unavailable", retained.Message,
                        AlertTone.Warning, "game-launcher.retained-error",
                        new ComponentAction("Try again", "game-launcher.retry", WidgetGlyph.Refresh))
                    .Classes("game-launcher-warning"));
            content = UI.Stack("game-launcher.content", children.ToArray())
                .Classes("game-launcher-content");
            initialFocus ??= snapshot.Anchor is { } anchor
                ? GameLauncherIdentity.FocusId("grid", anchor)
                : GameLauncherIdentity.FocusId("grid", snapshot.Items[0].Key);
        }
        else if (state.WarmItems.Count != 0 && snapshot.Status is
                     WidgetPagedResourceStatus.NotLoaded or
                     WidgetPagedResourceStatus.Loading or
                     WidgetPagedResourceStatus.Refreshing or
                     WidgetPagedResourceStatus.Error)
        {
            var warm = state.WarmItems.Select(item => Tile(
                item.DisplayName, item.SourceAttribution, item.SavedId, null,
                null, false, GameLauncherIdentity.Key(item.SavedId))).ToArray();
            var warmScroll = UI.VerticalScroll(ScrollId,
                    UI.ResponsiveGrid("game-launcher.library.grid", 170, 5, warm)
                        .Classes("game-launcher-grid")) with
                { CollectionAnchorKey = GameLauncherIdentity.Key(
                    state.WarmItems[0].SavedId).Value };
            content = UI.Stack("game-launcher.content",
                    UI.Alert("Checking installed games",
                        snapshot.Error?.Message ?? "Saved display rows cannot launch until current provider resolution succeeds.",
                        snapshot.Error is null ? AlertTone.Info : AlertTone.Warning,
                        "game-launcher.warm-status",
                        new ComponentAction("Try again", "game-launcher.retry", WidgetGlyph.Refresh)),
                    warmScroll)
                .Classes("game-launcher-content");
            initialFocus = "game-launcher.warm-status.action";
        }
        else if (snapshot.Status is WidgetPagedResourceStatus.Loading or
                 WidgetPagedResourceStatus.Refreshing or WidgetPagedResourceStatus.NotLoaded)
        {
            content = UI.Card("game-launcher.loading",
                UI.LoadingIndicator("game-launcher.loading.indicator", "Loading installed games"),
                UI.Text("Reading the trusted installed-game catalog…",
                    "game-launcher.loading.text", "Loading installed games"));
        }
        else if (snapshot.Status == WidgetPagedResourceStatus.Error)
        {
            content = UI.Alert("Game library unavailable",
                snapshot.Error?.Message ?? "The installed game library could not be loaded.",
                AlertTone.Danger, "game-launcher.error",
                new ComponentAction("Try again", "game-launcher.retry", WidgetGlyph.Refresh));
            initialFocus = "game-launcher.error.action";
        }
        else
        {
            content = UI.EmptyState("No installed games",
                "No trusted installed game registrations are currently available.",
                "game-launcher.empty",
                new ComponentAction("Refresh", "game-launcher.refresh", WidgetGlyph.Refresh),
                WidgetGlyph.Play);
            initialFocus = "game-launcher.empty.action";
        }

        var root = UI.Stack("game-launcher.root", header, content)
            .InputScope("game-launcher")
            .Classes("game-launcher-widget");
        return new WidgetView(root, initialFocus,
            ActiveInputScopeId: "game-launcher", Surface: Surface);
    }

    private static WidgetElement Tile(
        string title,
        string source,
        string savedId,
        string? artworkHandle,
        string? launchingSavedId,
        bool interactive,
        WidgetCollectionItemKey key)
    {
        var id = GameLauncherIdentity.FocusId("grid", key);
        var launching = string.Equals(savedId, launchingSavedId, StringComparison.Ordinal);
        var artwork = artworkHandle is { Length: > 0 }
            ? TileArtwork.FromHandle(new WidgetArtworkHandle(artworkHandle), title, ImageFit.Cover)
            : TileArtwork.FromGlyph(WidgetGlyph.Play, title);
        return UI.AppTile(title, launching ? "Opening…" : interactive ? "Ready" : "Checking…",
                "game-launcher.launch", id, subtitle: source, artwork: artwork,
                accessibilityLabel: $"{title}, {source}, {(interactive ? "ready" : "checking")}",
                orientation: ActionSurfaceOrientation.Vertical)
            .Busy(launching)
            .Disabled(!interactive)
            .CollectionItem(key)
            .Classes("game-launcher-tile");
    }
}
