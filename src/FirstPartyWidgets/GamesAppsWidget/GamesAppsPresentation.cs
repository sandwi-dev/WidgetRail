using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GamesApps;

internal sealed record GamesAppsToastNotice(
    string Title,
    string Message,
    ToastTone Tone,
    TimeSpan Duration);

/// <summary>
/// One immutable render-facing revision. Pure presentation consumes this value
/// and never reads widget locks, provider services, or persistence state.
/// </summary>
internal sealed record GamesAppsPresentationState(
    GamesAppsViewState ViewState,
    GamesAppsPage Page,
    string Status,
    IReadOnlyList<WidgetAppLibraryItem> Items,
    IReadOnlyList<string> LibrarySavedIds,
    IReadOnlySet<string> ResolvedSavedIds,
    string? SelectedAppId,
    string? LaunchingAppId,
    bool LoadingMore,
    bool LibraryMutationBusy,
    int? NextOffset,
    bool CanLoadPrevious,
    WidgetLifecycleState LifecycleState,
    GamesAppsToastNotice? Toast);

internal static class GamesAppsPresentation
{
    private const string RetryActionId = "games.retry";

    private static readonly WidgetSurfaceHints LibrarySurface = new()
    {
        Mode = WidgetSurfaceMode.Standard,
        PreferredWidth = 820,
        PreferredHeight = 600,
        MinimumWidth = 420,
        MinimumHeight = 300,
    };

    private static readonly WidgetSurfaceHints CatalogSurface = LibrarySurface with
    {
        MinimumHeight = 320,
    };

    private static readonly WidgetSurfaceHints StateSurface = LibrarySurface with
    {
        PreferredHeight = 280,
        MinimumHeight = 250,
    };

    internal static WidgetView Render(GamesAppsPresentationState state)
    {
        var headerChildren = new List<WidgetElement>
        {
            UI.Text(state.Page == GamesAppsPage.Catalog ? "CATALOG" : "LIBRARY",
                    "games.eyebrow", state.Page == GamesAppsPage.Catalog
                        ? "Add applications catalog"
                        : "Installed application library")
                .Classes("games-eyebrow"),
            UI.Text("Games & Apps", "games.title", "Games and Apps")
                .Classes("games-title"),
            UI.Text(state.Status, "games.status", state.Status).Classes(
                "games-status",
                state.ViewState == GamesAppsViewState.Ready ? "is-ready" :
                state.ViewState is GamesAppsViewState.PermissionDenied or
                    GamesAppsViewState.LifecycleDenied or
                    GamesAppsViewState.ServiceUnavailable or GamesAppsViewState.Error
                    ? "is-error" : "is-neutral"),
        };
        if (state.Toast is not null)
            headerChildren.Add(UI.Toast(
                state.Toast.Title, state.Toast.Message, state.Toast.Tone,
                "games.toast", state.Toast.Duration));
        var header = UI.Stack("games.header", headerChildren.ToArray())
            .Classes("games-header");

        if (state.ViewState != GamesAppsViewState.Ready)
            return RenderState(header, state);

        return state.Page == GamesAppsPage.Catalog
            ? RenderCatalog(header, state)
            : RenderLibrary(header, state);
    }

    private static WidgetView RenderLibrary(
        StackElement header,
        GamesAppsPresentationState state)
    {
        var byId = state.Items.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var curated = state.LibrarySavedIds.Where(byId.ContainsKey)
            .Select(id => byId[id]).ToArray();
        if (curated.Length == 0)
        {
            var empty = ConfigureStateAction(UI.EmptyState(
                    "Build your library",
                    "Trusted games appear automatically. Add other applications when you want them.",
                    "games.state",
                    new ComponentAction(
                        "Add applications", "games.open-catalog", WidgetGlyph.Play),
                    WidgetGlyph.Play),
                state.LibraryMutationBusy ||
                state.LifecycleState != WidgetLifecycleState.Interactive);
            var emptyRoot = UI.Stack("games.root",
                    header,
                    UI.Stack("games.content", empty).Classes(
                        "games-content", "games-state-shell"))
                .InputScope("games-apps")
                .Classes("games-apps-widget", "has-state");
            return new WidgetView(emptyRoot, "games.state.action", Surface: StateSurface);
        }

        var elementIds = curated.Select(item => LibraryElementId(item.SavedId)).ToArray();
        var rows = new List<WidgetElement>(curated.Length + 1);
        for (var index = 0; index < curated.Length; index++)
        {
            var item = curated[index];
            var id = elementIds[index];
            var isOpening = string.Equals(
                state.LaunchingAppId, item.AppId, StringComparison.Ordinal);
            var isResolved = state.ResolvedSavedIds.Contains(item.SavedId);
            var tileState = isOpening ? "Opening…" : isResolved ? "Ready" : "Checking…";
            var tile = UI.AppTile(
                    item.DisplayName,
                    tileState,
                    "games.launch",
                    id,
                    subtitle: AppKindLabel(item.Kind),
                    artwork: AppArtwork(item, $"{item.DisplayName} icon"),
                    accessibilityLabel:
                        $"{item.DisplayName}, {AppKindLabel(item.Kind)}, {tileState}")
                .Shortcut(ControllerButton.X, actionId: "games.remove")
                .Busy(isOpening)
                .Disabled(!isResolved || state.LaunchingAppId is not null ||
                    state.LibraryMutationBusy ||
                    state.LifecycleState != WidgetLifecycleState.Interactive)
                .Selected(string.Equals(
                    state.SelectedAppId, item.AppId, StringComparison.Ordinal))
                .FocusUp(index == 0 ? id : elementIds[index - 1])
                .FocusDown(index + 1 < curated.Length
                    ? elementIds[index + 1]
                    : "games.open-catalog")
                .FocusLeft(id)
                .FocusRight(id)
                .AddClasses("games-card-action", "games-app-row",
                    item.Kind == WidgetAppLibraryKind.Game ? "is-game" : "is-application");
            rows.Add(tile);
        }

        rows.Add(UI.Button("Add applications", "games.open-catalog", "games.open-catalog")
            .Icon(WidgetGlyph.Play, $"Browse {state.Items.Count} available applications")
            .Disabled(state.LaunchingAppId is not null || state.LibraryMutationBusy ||
                state.LifecycleState != WidgetLifecycleState.Interactive)
            .FocusUp(elementIds[^1])
            .FocusDown("games.open-catalog")
            .FocusLeft("games.open-catalog")
            .FocusRight("games.open-catalog")
            .Classes("games-card-action", "games-app-row", "games-load-more"));
        var selected = curated.FirstOrDefault(item => string.Equals(
            item.AppId, state.SelectedAppId, StringComparison.Ordinal)) ?? curated[0];
        var count = UI.StatusBadge(
            $"{curated.Length} saved", StatusTone.Info, "games.section.count");
        var section = UI.SectionHeader(
                "Your library",
                "games.section",
                eyebrow: "GAMES + APPLICATIONS",
                description: "A opens · X removes · Y refreshes",
                trailing: count)
            .AddClasses("games-section-heading");
        var root = UI.Stack("games.root",
                header,
                UI.Stack("games.content",
                        section,
                        UI.VerticalScroll("games.library.scroll", rows.ToArray())
                            .Classes("games-library-scroll"))
                    .Classes("games-content"))
            .InputScope("games-apps")
            .Shortcut(ControllerButton.Y, RetryActionId)
            .Classes("games-apps-widget");
        return new WidgetView(root, LibraryElementId(selected.SavedId), Surface: LibrarySurface);
    }

    private static WidgetView RenderCatalog(
        StackElement header,
        GamesAppsPresentationState state)
    {
        var curated = state.LibrarySavedIds.ToHashSet(StringComparer.Ordinal);
        var elementIds = state.Items.Select(item => CatalogElementId(item.SavedId)).ToArray();
        var rows = new List<WidgetElement>(
            state.Items.Count + (state.NextOffset is null ? 0 : 1) +
            (state.CanLoadPrevious ? 1 : 0));
        if (state.CanLoadPrevious)
        {
            rows.Add(UI.Button(
                    "Previous page", "games.previous-page", "games.previous-page")
                .Icon(WidgetGlyph.Previous, "Return to the previous application page")
                .Busy(state.LoadingMore)
                .Disabled(state.LaunchingAppId is not null || state.LoadingMore ||
                    state.LifecycleState != WidgetLifecycleState.Interactive)
                .FocusUp("games.previous-page")
                .FocusDown(elementIds[0])
                .FocusLeft("games.previous-page")
                .FocusRight("games.previous-page")
                .Classes("games-card-action", "games-page-action"));
        }
        for (var index = 0; index < state.Items.Count; index++)
        {
            var item = state.Items[index];
            var id = elementIds[index];
            var saved = curated.Contains(item.SavedId);
            var down = index + 1 < state.Items.Count
                ? elementIds[index + 1]
                : state.NextOffset is not null ? "games.load-more" : id;
            rows.Add(UI.AppTile(
                    item.DisplayName,
                    saved ? "Saved" : "Available",
                    "games.toggle-curation",
                    id,
                    subtitle: AppKindLabel(item.Kind),
                    artwork: AppArtwork(item, $"{item.DisplayName} icon"),
                    accessibilityLabel: saved
                        ? $"{item.DisplayName}, saved, A removes from library"
                        : $"{item.DisplayName}, available, A adds to library")
                .Selected(saved)
                .Disabled(state.LaunchingAppId is not null || state.LoadingMore ||
                    state.LifecycleState != WidgetLifecycleState.Interactive)
                .FocusUp(index == 0
                    ? state.CanLoadPrevious ? "games.previous-page" : id
                    : elementIds[index - 1])
                .FocusDown(down)
                .FocusLeft(id)
                .FocusRight(id)
                .AddClasses("games-card-action", "games-app-row",
                    saved ? "is-saved" : "is-available"));
        }
        if (state.NextOffset is not null)
        {
            rows.Add(UI.Button("Next page", "games.load-more", "games.load-more")
                .Icon(WidgetGlyph.Refresh, "Load the next application page")
                .Busy(state.LoadingMore)
                .Disabled(state.LaunchingAppId is not null || state.LoadingMore ||
                    state.LifecycleState != WidgetLifecycleState.Interactive)
                .FocusUp(elementIds[^1])
                .FocusDown("games.load-more")
                .FocusLeft("games.load-more")
                .FocusRight("games.load-more")
                .Classes("games-card-action", "games-page-action", "games-load-more"));
        }
        var selected = state.Items.FirstOrDefault(item => string.Equals(
            item.AppId, state.SelectedAppId, StringComparison.Ordinal)) ?? state.Items[0];
        var count = UI.StatusBadge(
            $"{state.Items.Count}{(state.NextOffset is null ? string.Empty : "+")} available",
            StatusTone.Info,
            "games.section.count");
        var section = UI.SectionHeader(
                "Add applications",
                "games.section",
                eyebrow: "CATALOG",
                description: "A adds or removes · B returns",
                trailing: count)
            .AddClasses("games-section-heading");
        var scope = UI.Stack("games.catalog",
                section,
                UI.VerticalScroll("games.library.scroll", rows.ToArray())
                    .Classes("games-library-scroll"))
            .InputScope("games.catalog")
            .Shortcut(ControllerButton.B, "back")
            .Classes("games-catalog");
        var root = UI.Stack("games.root", header,
                UI.Stack("games.content", scope).Classes("games-content"))
            .Classes("games-apps-widget");
        return new WidgetView(root, CatalogElementId(selected.SavedId),
            ActiveInputScopeId: "games.catalog", Surface: CatalogSurface);
    }

    private static WidgetView RenderState(
        StackElement header,
        GamesAppsPresentationState state)
    {
        var (title, help) = state.ViewState switch
        {
            GamesAppsViewState.Initial =>
                ("Your library", "Trusted installed games are added automatically."),
            GamesAppsViewState.Loading =>
                (state.Page == GamesAppsPage.Catalog
                    ? "Loading applications"
                    : "Loading your library",
                 state.Page == GamesAppsPage.Catalog
                    ? "The host is reading the bounded catalog only while you add an application."
                    : "The host is reconciling trusted games and applications you saved."),
            GamesAppsViewState.Empty =>
                ("No launchable apps found", "No executable Start Menu registrations were available."),
            GamesAppsViewState.PermissionDenied =>
                ("App library access is off", "Allow Games & Apps in Settings > Permissions."),
            GamesAppsViewState.LifecycleDenied =>
                ("App library is paused", "Return to this widget to load installed apps."),
            GamesAppsViewState.ServiceUnavailable =>
                ("App library unavailable", "The trusted Windows application catalog is unavailable."),
            _ => ("Installed apps could not be loaded", "Try again. No paths or command lines were exposed."),
        };
        StackElement stateSurface;
        string? initialFocus;
        if (state.ViewState is GamesAppsViewState.Initial or GamesAppsViewState.Loading)
        {
            stateSurface = UI.Card("games.state",
                    state.ViewState == GamesAppsViewState.Loading
                        ? UI.LoadingIndicator(
                                "games.state.loading",
                                state.Page == GamesAppsPage.Catalog
                                    ? "Loading available applications"
                                    : "Loading saved applications")
                            .Classes("games-state-loading")
                        : UI.Icon(WidgetGlyph.Play, "games.state.icon", "Application library")
                            .Classes("games-state-icon"),
                    UI.Text(title, "games.state.title", title).Classes("games-state-title"),
                    UI.Text(help, "games.state.help", help).Classes("games-state-help"))
                .AddClasses("games-state-surface", "games-state-progress");
            initialFocus = null;
        }
        else if (state.ViewState == GamesAppsViewState.Empty)
        {
            stateSurface = ConfigureStateAction(UI.EmptyState(
                    title,
                    help,
                    "games.state",
                    new ComponentAction("Try again", RetryActionId, WidgetGlyph.Refresh),
                    WidgetGlyph.Play),
                state.LifecycleState == WidgetLifecycleState.Background);
            initialFocus = "games.state.action";
        }
        else
        {
            var tone = state.ViewState is GamesAppsViewState.PermissionDenied or
                GamesAppsViewState.LifecycleDenied
                    ? AlertTone.Warning
                    : AlertTone.Danger;
            stateSurface = ConfigureStateAction(UI.Alert(
                title,
                help,
                tone,
                "games.state",
                new ComponentAction("Try again", RetryActionId, WidgetGlyph.Refresh)),
                state.LifecycleState == WidgetLifecycleState.Background);
            initialFocus = "games.state.action";
        }
        var root = UI.Stack("games.root",
                header,
                UI.Stack("games.content", stateSurface)
                    .Classes("games-content", "games-state-shell"))
            .InputScope("games-apps")
            .Classes("games-apps-widget", "has-state");
        return new WidgetView(root, initialFocus, Surface: StateSurface);
    }

    private static StackElement ConfigureStateAction(
        StackElement surface,
        bool disabled) => surface with
    {
        Children = surface.Children.Select(child => child is ButtonElement button
            ? button.Disabled(disabled).AddClasses("games-primary-action")
            : child).ToArray(),
        StyleClasses = surface.StyleClasses.Concat(["games-state-surface"]).ToArray(),
    };

    internal static string LibraryElementId(string opaqueId) =>
        HashedElementId("games.item.", opaqueId);

    internal static string CatalogElementId(string opaqueId) =>
        HashedElementId("games.catalog.item.", opaqueId);

    private static string HashedElementId(string prefix, string opaqueId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(opaqueId));
        return prefix + Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant();
    }

    private static string AppKindLabel(WidgetAppLibraryKind kind) =>
        kind == WidgetAppLibraryKind.Game ? "Game" : "Application";

    private static TileArtwork AppArtwork(
        WidgetAppLibraryItem item,
        string accessibilityLabel) =>
        item.ArtworkHandle is { Length: > 0 } handle
            ? TileArtwork.FromHandle(
                new WidgetArtworkHandle(handle), accessibilityLabel, ImageFit.Contain)
            : TileArtwork.FromGlyph(WidgetGlyph.Play, accessibilityLabel);
}
