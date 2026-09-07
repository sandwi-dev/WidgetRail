using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.GamesApps;

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
    WidgetNavigationSnapshot<GamesAppsPage> Navigation,
    GamesAppsViewState ViewState,
    string Status,
    IReadOnlyList<WidgetAppLibraryItem> Items,
    IReadOnlyList<string> LibrarySavedIds,
    IReadOnlySet<string> ResolvedSavedIds,
    string? SelectedAppId,
    string? LaunchingAppId,
    bool LoadingMore,
    bool LibraryMutationBusy,
    bool CatalogRefreshBusy,
    bool HasNextPage,
    bool CanLoadPrevious,
    WidgetLifecycleState LifecycleState,
    GamesAppsToastNotice? Toast);

internal static class GamesAppsPresentation
{
    private const string RetryActionId = "games.retry";
    private const string RefreshActionId = "games.refresh-catalog";
    private const double GridMinimumColumnWidth = 240;
    private const int GridMaximumColumns = 3;

    private static readonly WidgetSurfaceHints LibrarySurface = new()
    {
        Mode = WidgetSurfaceMode.Standard,
        PreferredWidth = 820,
        PreferredHeight = 600,
        MinimumWidth = 420,
        MinimumHeight = 300,
    };

    private static readonly WidgetSurfaceHints StateSurface = LibrarySurface with
    {
        PreferredHeight = 280,
        MinimumHeight = 250,
    };

    internal static WidgetView Render(GamesAppsPresentationState state)
    {
        var page = state.Navigation.RootRoute;
        if (page == GamesAppsPage.Library &&
            state.ViewState != GamesAppsViewState.Ready)
            return RenderState(state);

        var contentReady = state.ViewState == GamesAppsViewState.Ready;
        var entry = contentReady ? ContentEntryFocusId(state) : null;
        var contentEntry = entry is null
            ? NavigationShellContentEntry.Unavailable
            : NavigationShellContentEntry.Available(entry);
        var content = contentReady
            ? page switch
            {
                GamesAppsPage.Library => RenderLibraryContent(state),
                GamesAppsPage.Catalog => RenderCatalogContent(state, running: false),
                GamesAppsPage.Running => RenderCatalogContent(state, running: true),
                _ => throw new InvalidOperationException("The Games & Apps root is unsupported."),
            }
            : RenderRouteProgress(state);
        var pageContent = UI.Stack(FocusGroupId(page), content)
            .Classes("games-page", PageClass(page));
        if (entry is not null)
            pageContent = pageContent.RememberChildFocus(entry);
        var parts = UI.NavigationShellParts(
            "games.sections",
            DestinationId(page),
            contentEntry,
            pageContent,
            Destinations(state),
            compactLeadingAdornment: SectionBumperBadge(
                "LB",
                "Previous section",
                "games.section.previous.hint"),
            compactTrailingAdornment: SectionBumperBadge(
                "RB",
                "Next section",
                "games.section.next.hint"));
        var header = RenderHeader(state, parts.CompactNavigation);
        var initialFocus = entry is null ? null : InitialFocusId(state, entry);
        var root = UI.Stack(
                "games.root",
                header,
                parts.Body.AddClasses("games-section-body"))
            .Shortcut(ControllerButton.LeftBumper,
                "games.section.previous", "Previous section")
            .Shortcut(ControllerButton.RightBumper,
                "games.section.next", "Next section")
            .Shortcut(ControllerButton.Y,
                RefreshActionId, "Refresh current section")
            .Classes("games-apps-widget");
        return new WidgetView(root, initialFocus, Surface: LibrarySurface);
    }

    internal static string FocusGroupId(GamesAppsPage page) => page switch
    {
        GamesAppsPage.Library => "games.library.page",
        GamesAppsPage.Catalog => "games.catalog.page",
        GamesAppsPage.Running => "games.running.page",
        _ => throw new ArgumentOutOfRangeException(nameof(page)),
    };

    private static StackElement RenderHeader(
        GamesAppsPresentationState state,
        WidgetElement compactNavigation)
    {
        var page = state.Navigation.RootRoute;
        var count = UI.StatusBadge(
            CountLabel(state),
            StatusTone.Info,
            "games.header.count");
        var refresh = UI.IconButton(
                WidgetGlyph.Refresh,
                RefreshActionId,
                "games.refresh-catalog",
                RefreshAccessibilityLabel(page),
                IconButtonVariant.Quiet,
                IconButtonSize.Small)
            .Busy(state.CatalogRefreshBusy || state.LoadingMore)
            .Disabled(state.CatalogRefreshBusy || state.LoadingMore ||
                state.LibraryMutationBusy ||
                state.LifecycleState != WidgetLifecycleState.Interactive)
            .AddClasses("games-refresh-catalog");
        var summary = UI.Row(
                "games.header.summary",
                UI.Text("Games & Apps", "games.title", "Games and Apps")
                    .Classes("games-title"),
                count,
                UI.Row(
                        "games.header.refresh",
                        UI.ControllerHint(
                            ControllerButton.Y,
                            "Refresh",
                            "games.refresh.hint"),
                        refresh)
                    .Classes("games-header-refresh"))
            .Classes("games-header-summary");
        var expandedBumpers = UI.Row(
                "games.section.expanded-hints",
                SectionBumperBadge(
                    "LB",
                    "Previous section",
                    "games.section.expanded.previous"),
                SectionBumperBadge(
                    "RB",
                    "Next section",
                    "games.section.expanded.next"))
            .VisibleWhen(ResponsiveVisibility.ExpandedOnly)
            .Classes("games-section-expanded-hints");
        var children = new List<WidgetElement>
        {
            summary,
            compactNavigation.AddClasses("games-section-navigation"),
            expandedBumpers,
        };
        if (ReadyStatus(state) is { } status)
            children.Add(UI.Text(status, "games.status", status)
                .Classes("games-status", "is-notice"));
        if (state.Toast is not null)
            children.Add(UI.Toast(
                state.Toast.Title,
                state.Toast.Message,
                state.Toast.Tone,
                "games.toast",
                state.Toast.Duration));
        return UI.Stack("games.header", children.ToArray())
            .Classes("games-header");
    }

    private static RowElement SectionBumperBadge(
        string text,
        string accessibilityLabel,
        string id) => UI.Row(
            id,
            UI.Text(text, id + ".label", $"{text}, {accessibilityLabel}")
                .Classes("games-section-bumper-label"))
        .Classes("wrail-controller-hint__key", "games-section-bumper-key");

    private static WidgetElement RenderLibraryContent(GamesAppsPresentationState state)
    {
        var byId = state.Items.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var curated = state.LibrarySavedIds.Where(byId.ContainsKey)
            .Select(savedId => byId[savedId]).ToArray();
        if (curated.Length == 0)
            return ConfigureStateAction(
                UI.EmptyState(
                    "Build your library",
                    "Trusted games appear automatically. Use Add apps for anything else.",
                    "games.state",
                    new ComponentAction(
                        "Add apps", "games.open-catalog", WidgetGlyph.Play),
                    WidgetGlyph.Play),
                state.LibraryMutationBusy ||
                state.LifecycleState != WidgetLifecycleState.Interactive);

        var tiles = curated.Select(item => LibraryTile(state, item)).ToArray();
        return UI.VerticalScroll(
                "games.library.scroll",
                UI.ResponsiveGrid(
                    "games.library.grid",
                    GridMinimumColumnWidth,
                    GridMaximumColumns,
                    tiles))
            .Classes("games-page-scroll", "games-library-scroll");
    }

    private static WidgetElement RenderCatalogContent(
        GamesAppsPresentationState state,
        bool running)
    {
        if (state.Items.Count == 0)
            return ConfigureStateAction(
                UI.EmptyState(
                    running ? "No matching running apps" : "No applications found",
                    running
                        ? "Only visible applications that exactly match the installed library can be added."
                        : "Refresh this section to check the installed application catalog again.",
                    running ? "games.running.empty" : "games.catalog.empty",
                    new ComponentAction(
                        "Check again", RefreshActionId, WidgetGlyph.Refresh),
                    WidgetGlyph.Play),
                state.LifecycleState != WidgetLifecycleState.Interactive);

        var curated = state.LibrarySavedIds.ToHashSet(StringComparer.Ordinal);
        var tiles = state.Items.Select(item => CatalogTile(
            state, item, running, curated.Contains(item.SavedId))).ToArray();
        var children = new List<WidgetElement>();
        if (!running && state.CanLoadPrevious)
            children.Add(PageButton(
                "Previous page",
                "games.previous-page",
                WidgetGlyph.Previous,
                "Load the previous application page",
                state));
        children.Add(UI.ResponsiveGrid(
            running ? "games.running.grid" : "games.catalog.grid",
            GridMinimumColumnWidth,
            GridMaximumColumns,
            tiles));
        if (!running && state.HasNextPage)
            children.Add(PageButton(
                "Next page",
                "games.load-more",
                WidgetGlyph.Refresh,
                "Load the next application page",
                state));
        return UI.VerticalScroll(
                running ? "games.running.scroll" : "games.catalog.scroll",
                children.ToArray())
            .Classes("games-page-scroll", running
                ? "games-running-scroll"
                : "games-catalog-scroll");
    }

    private static WidgetElement RenderRouteProgress(GamesAppsPresentationState state)
    {
        var page = state.Navigation.RootRoute;
        var label = page == GamesAppsPage.Running
            ? "Checking running apps"
            : "Loading applications";
        return UI.Stack(
                "games.route.progress",
                UI.LoadingIndicator(
                    "games.route.loading", label, LoadingIndicatorSize.Large))
            .Classes("games-route-progress");
    }

    private static ActionSurfaceElement LibraryTile(
        GamesAppsPresentationState state,
        WidgetAppLibraryItem item)
    {
        var isOpening = string.Equals(
            state.LaunchingAppId, item.AppId, StringComparison.Ordinal);
        var isResolved = state.ResolvedSavedIds.Contains(item.SavedId);
        var canLaunch = GamesAppsAppLibraryPresentation.CanLaunch(item);
        var visibleState = isOpening
            ? "Opening…"
            : !isResolved
                ? "Checking availability"
                : !canLaunch
                    ? "Unavailable"
                    : null;
        var semanticState = visibleState ?? "Ready";
        return AppTile(
                item,
                "games.launch",
                LibraryElementId(item.SavedId),
                semanticState,
                visibleState,
                disabled: !isResolved || !canLaunch ||
                    state.LaunchingAppId is not null ||
                    state.LibraryMutationBusy ||
                    state.LifecycleState != WidgetLifecycleState.Interactive,
                busy: isOpening,
                selected: false)
            .Shortcut(ControllerButton.X, "Remove", actionId: "games.remove")
            .AddClasses(GamesAppsAppLibraryPresentation.Kind(item) == WidgetAppLibraryKind.Game
                ? "is-game"
                : "is-application");
    }

    private static ActionSurfaceElement CatalogTile(
        GamesAppsPresentationState state,
        WidgetAppLibraryItem item,
        bool running,
        bool saved) => AppTile(
            item,
            "games.toggle-curation",
            CatalogElementId(item.SavedId),
            saved ? "Included" : running ? "Running" : "Available",
            saved ? "Included" : null,
            disabled: saved && running ||
                state.LaunchingAppId is not null ||
                state.LoadingMore ||
                state.LibraryMutationBusy ||
                state.LifecycleState != WidgetLifecycleState.Interactive,
            busy: state.LibraryMutationBusy,
            selected: saved);

    private static ActionSurfaceElement AppTile(
        WidgetAppLibraryItem item,
        string actionId,
        string id,
        string semanticState,
        string? visibleState,
        bool disabled,
        bool busy,
        bool selected)
    {
        var name = GamesAppsAppLibraryPresentation.DisplayName(item);
        var source = SourceLabel(item);
        var copy = new List<WidgetElement>
        {
            UI.Text(name, id + ".title", name).Classes("games-app-tile-title"),
            UI.Text(source, id + ".subtitle", source).Classes("games-app-tile-subtitle"),
        };
        if (visibleState is not null)
            copy.Add(UI.Text(
                    visibleState,
                    id + ".state",
                    $"State: {visibleState}")
                .Classes("games-app-tile-state"));
        return UI.ActionSurface(
                actionId,
                id,
                $"{name}, {source}, {semanticState}",
                ActionSurfaceOrientation.Horizontal,
                AppArtwork(item, id + ".artwork", $"{name} icon"),
                UI.Stack(id + ".content", copy.ToArray())
                    .Classes("games-app-tile-content"))
            .Disabled(disabled)
            .Busy(busy)
            .Selected(selected)
            .AddClasses("games-app-tile");
    }

    private static ButtonElement PageButton(
        string label,
        string actionId,
        WidgetGlyph glyph,
        string accessibilityLabel,
        GamesAppsPresentationState state) =>
        UI.Button(label, actionId, actionId)
            .Icon(glyph, accessibilityLabel)
            .Busy(state.LoadingMore)
            .Disabled(state.LoadingMore ||
                state.LifecycleState != WidgetLifecycleState.Interactive)
            .Classes("games-page-action");

    private static WidgetView RenderState(GamesAppsPresentationState state)
    {
        var (title, help) = state.ViewState switch
        {
            GamesAppsViewState.Initial =>
                ("Your library", "Trusted installed games are added automatically."),
            GamesAppsViewState.Loading =>
                ("Loading your library", "The host is reconciling trusted games and applications you saved."),
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
            stateSurface = UI.Card(
                    "games.state",
                    state.ViewState == GamesAppsViewState.Loading
                        ? UI.LoadingIndicator(
                                "games.state.loading",
                                "Loading saved applications")
                            .Classes("games-state-loading")
                        : UI.Icon(
                                WidgetGlyph.Play,
                                "games.state.icon",
                                "Application library")
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
        var root = UI.Stack(
                "games.root",
                UI.Row(
                        "games.header.summary",
                        UI.Text("Games & Apps", "games.title", "Games and Apps")
                            .Classes("games-title"))
                    .Classes("games-header-summary"),
                UI.Stack("games.content", stateSurface)
                    .Classes("games-content", "games-state-shell"))
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

    private static NavigationShellDestination[] Destinations(
        GamesAppsPresentationState state)
    {
        var disabled = state.LifecycleState != WidgetLifecycleState.Interactive;
        return
        [
            new("games.section.library", "Library", "games.open-library",
                WidgetGlyph.Play, IsDisabled: disabled),
            new("games.section.catalog", "Add apps", "games.open-catalog",
                WidgetGlyph.Connection, IsDisabled: disabled),
            new("games.section.running", "Running", "games.open-running",
                WidgetGlyph.Refresh, IsDisabled: disabled),
        ];
    }

    private static string ContentEntryFocusId(GamesAppsPresentationState state)
    {
        var page = state.Navigation.RootRoute;
        if (page == GamesAppsPage.Library)
        {
            var selectedLibraryItem = state.Items.FirstOrDefault(item => string.Equals(
                item.AppId, state.SelectedAppId, StringComparison.Ordinal));
            var saved = selectedLibraryItem?.SavedId ?? state.LibrarySavedIds.FirstOrDefault(savedId =>
                state.Items.Any(item => string.Equals(
                    item.SavedId, savedId, StringComparison.Ordinal)));
            return saved is null ? "games.state.action" : LibraryElementId(saved);
        }
        if (state.Items.Count == 0)
            return page == GamesAppsPage.Running
                ? "games.running.empty.action"
                : "games.catalog.empty.action";
        if (page == GamesAppsPage.Catalog && state.CanLoadPrevious)
            return "games.previous-page";
        var selected = state.Items.FirstOrDefault(item => string.Equals(
            item.AppId, state.SelectedAppId, StringComparison.Ordinal)) ?? state.Items[0];
        return CatalogElementId(selected.SavedId);
    }

    private static string InitialFocusId(
        GamesAppsPresentationState state,
        string contentEntryFocusId)
    {
        if (state.Navigation.RootRoute != GamesAppsPage.Library)
            return contentEntryFocusId;
        var firstSavedId = state.LibrarySavedIds.FirstOrDefault(savedId =>
            state.Items.Any(item => string.Equals(
                item.SavedId, savedId, StringComparison.Ordinal)));
        return firstSavedId is null
            ? contentEntryFocusId
            : LibraryElementId(firstSavedId);
    }

    private static string DestinationId(GamesAppsPage page) => page switch
    {
        GamesAppsPage.Library => "games.section.library",
        GamesAppsPage.Catalog => "games.section.catalog",
        GamesAppsPage.Running => "games.section.running",
        _ => throw new ArgumentOutOfRangeException(nameof(page)),
    };

    private static string PageClass(GamesAppsPage page) => page switch
    {
        GamesAppsPage.Library => "is-library",
        GamesAppsPage.Catalog => "is-catalog",
        GamesAppsPage.Running => "is-running",
        _ => throw new ArgumentOutOfRangeException(nameof(page)),
    };

    private static string CountLabel(GamesAppsPresentationState state) =>
        state.Navigation.RootRoute switch
        {
            GamesAppsPage.Library => $"{state.LibrarySavedIds.Count} saved",
            GamesAppsPage.Catalog =>
                $"{state.Items.Count}{(state.HasNextPage ? "+" : string.Empty)} available",
            GamesAppsPage.Running => $"{state.Items.Count} matched",
            _ => string.Empty,
        };

    private static string RefreshAccessibilityLabel(GamesAppsPage page) => page switch
    {
        GamesAppsPage.Library => "Refresh the installed application library",
        GamesAppsPage.Catalog => "Refresh applications available to add",
        GamesAppsPage.Running => "Check visible running applications again",
        _ => "Refresh current section",
    };

    private static string? ReadyStatus(GamesAppsPresentationState state)
    {
        if (state.CatalogRefreshBusy || state.LoadingMore)
            return state.Status;
        return state.Status.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
               state.Status.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               state.Status.Contains("full", StringComparison.OrdinalIgnoreCase)
            ? state.Status
            : null;
    }

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

    private static string SourceLabel(WidgetAppLibraryItem item) =>
        $"{AppKindLabel(GamesAppsAppLibraryPresentation.Kind(item))} · " +
        GamesAppsAppLibraryPresentation.Source(item);

    private static WidgetElement AppArtwork(
        WidgetAppLibraryItem item,
        string id,
        string accessibilityLabel) =>
        GamesAppsAppLibraryPresentation.TileArtwork(item) is { } artwork
            ? UI.Artwork(
                    new WidgetArtworkHandle(artwork.Handle),
                    id,
                    accessibilityLabel,
                    ImageFit.Contain)
                .Classes("games-app-tile-artwork")
            : UI.Icon(WidgetGlyph.Play, id, accessibilityLabel)
                .Classes("games-app-tile-artwork", "is-fallback");
}
