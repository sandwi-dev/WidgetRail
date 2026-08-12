using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal sealed record GameLauncherDetailsSelection(
    string SavedId,
    WidgetCollectionItemKey Key,
    string ReturnFocusId,
    string DisplayName,
    string SourceAttribution,
    bool PageBumpers);

internal sealed record GameLauncherDetailsState(
    GameLauncherDetailsSelection Selection,
    string DisplayName,
    string SourceAttribution,
    string Availability,
    string LaunchStatus,
    bool Favorite,
    bool Preferred,
    int GroupSize,
    bool Interactive,
    bool Resolved,
    bool Busy);

internal static class GameLauncherDetailsPolicy
{
    internal const string ActionSourceId = "game-launcher.details.launch";

    internal static GameLauncherDetailsSelection? Select(
        string sourceElementId,
        WidgetCursorResourceSnapshot<GameLauncherItem> collection,
        GameLauncherFixedRows fixedRows)
    {
        var item = collection.Items.Concat(fixedRows.All).FirstOrDefault(candidate =>
            string.Equals(GameLauncherIdentity.FocusId("grid", candidate.Key),
                sourceElementId, StringComparison.Ordinal));
        return item is null ? null : new(
            item.Value.SavedId,
            item.Key,
            sourceElementId,
            item.Value.DisplayName,
            item.Value.SourceAttribution,
            collection.HasBefore || collection.HasAfter);
    }

    internal static GameLauncherDetailsState Project(
        GameLauncherDetailsSelection selection,
        WidgetCursorResourceSnapshot<GameLauncherItem> collection,
        GameLauncherFixedRows fixedRows,
        GameLauncherPrivateState organization,
        string? launchingSavedId,
        IReadOnlyDictionary<string, GameLauncherLaunchState> launchStates,
        bool organizationBusy,
        bool interactive)
    {
        var current = collection.Items.Concat(fixedRows.All).FirstOrDefault(item =>
            item.Key == selection.Key && string.Equals(item.Value.SavedId,
                selection.SavedId, StringComparison.Ordinal));
        var resolved = current is not null;
        var launching = string.Equals(selection.SavedId, launchingSavedId,
            StringComparison.Ordinal);
        var launchStatus = launching ? "Pending" : launchStates.GetValueOrDefault(
            selection.SavedId) switch
        {
            GameLauncherLaunchState.RequestAccepted => "Request accepted",
            GameLauncherLaunchState.LauncherStarted => "Launcher started",
            GameLauncherLaunchState.Running => "Running",
            GameLauncherLaunchState.Failed => "Failed",
            GameLauncherLaunchState.Ended => "Ended",
            _ => resolved ? interactive ? "Ready" : "Paused" : "Unavailable",
        };
        var group = GameLauncherOrganizationPolicy.GroupFor(
            organization, selection.SavedId);
        return new(
            selection,
            current?.Value.DisplayName ?? selection.DisplayName,
            current?.Value.SourceAttribution ?? selection.SourceAttribution,
            resolved ? interactive ? "Available" : "Paused" : "Unavailable",
            launchStatus,
            organization.FavoriteSavedIds.Contains(
                selection.SavedId, StringComparer.Ordinal),
            group?.PreferredSavedId == selection.SavedId,
            group?.SavedIds.Count ?? 0,
            interactive,
            resolved,
            launching || organizationBusy);
    }

    internal static string? ResolveActionSource(
        GameLauncherDetailsSelection? selection,
        string sourceElementId,
        WidgetCursorResourceSnapshot<GameLauncherItem> collection,
        GameLauncherFixedRows fixedRows)
    {
        if (selection is null || !sourceElementId.StartsWith(
                "game-launcher.details.", StringComparison.Ordinal))
            return sourceElementId;
        return collection.Items.Concat(fixedRows.All).Any(item =>
            item.Key == selection.Key && string.Equals(item.Value.SavedId,
                selection.SavedId, StringComparison.Ordinal))
            ? selection.ReturnFocusId
            : null;
    }
}

internal static class GameLauncherDetailsPresentation
{
    private static readonly WidgetSurfaceHints Surface = new()
    {
        Mode = WidgetSurfaceMode.Wide,
        PreferredWidth = 980,
        PreferredHeight = 700,
        MinimumWidth = 420,
        MinimumHeight = 340,
    };

    internal static WidgetView Render(GameLauncherDetailsState state)
    {
        var enabled = state.Interactive && state.Resolved && !state.Busy;
        var groupStatus = state.GroupSize > 1
            ? $"{state.GroupSize} grouped variants" +
                (state.Preferred ? " · Preferred" : string.Empty)
            : "Not grouped";
        var favorite = state.Favorite ? "Favorite" : "Not favorite";
        var scroll = UI.VerticalScroll("game-launcher.details.scroll",
                UI.Stack("game-launcher.details.content",
                    UI.Text("GAME DETAILS", "game-launcher.details.eyebrow", "Game details")
                        .Classes("game-launcher-eyebrow"),
                    UI.Text(state.DisplayName, "game-launcher.details.title",
                            state.DisplayName)
                        .Classes("game-launcher-title"),
                    UI.Text($"Source · {state.SourceAttribution}",
                        "game-launcher.details.source", "Game source"),
                    UI.Text($"Availability · {state.Availability}",
                        "game-launcher.details.availability", "Game availability"),
                    UI.Text($"Launch state · {state.LaunchStatus}",
                        "game-launcher.details.launch-state", "Game launch state"),
                    UI.Text($"Organization · {favorite} · {groupStatus}",
                        "game-launcher.details.organization", "Game organization"),
                    UI.HorizontalScroll("game-launcher.details.actions",
                        UI.Button("Launch", "game-launcher.launch",
                                GameLauncherDetailsPolicy.ActionSourceId)
                            .Busy(state.Busy)
                            .Disabled(!enabled),
                        UI.Button(state.Favorite ? "Remove favorite" : "Add favorite",
                                "game-launcher.favorite", "game-launcher.details.favorite")
                            .Disabled(!enabled),
                        UI.Button("Hide", "game-launcher.hide", "game-launcher.details.hide")
                            .Disabled(!enabled),
                        UI.Button(state.GroupSize > 1 ? "Ungroup variant" : "Group variant",
                                "game-launcher.variant", "game-launcher.details.variant")
                            .Disabled(!enabled),
                        UI.Button("Prefer variant", "game-launcher.prefer",
                                "game-launcher.details.prefer")
                            .Disabled(!enabled || state.GroupSize < 2 || state.Preferred)),
                    UI.Row("game-launcher.details.hints",
                        UI.ControllerHint(ControllerButton.A, "Launch", "game-launcher.details.hint.launch"),
                        UI.ControllerHint(ControllerButton.X, "Favorite", "game-launcher.details.hint.favorite"),
                        UI.ControllerHint(ControllerButton.Y, "Hide", "game-launcher.details.hint.hide"))))
            .Classes("game-launcher-scroll", "game-launcher-main")
            .Shortcut(ControllerButton.X, actionId: "game-launcher.favorite")
            .Shortcut(ControllerButton.Y, actionId: "game-launcher.hide");
        if (!state.Selection.PageBumpers)
            scroll = scroll
                .Shortcut(ControllerButton.LeftBumper, actionId: "game-launcher.variant")
                .Shortcut(ControllerButton.RightBumper, actionId: "game-launcher.prefer");
        return new WidgetView(
            UI.Stack("game-launcher.details.root", scroll).Classes("game-launcher-widget"),
            GameLauncherDetailsPolicy.ActionSourceId,
            Surface: Surface);
    }
}
