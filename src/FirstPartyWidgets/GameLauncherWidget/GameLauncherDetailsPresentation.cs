using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;
using System.Globalization;

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
    string Feedback,
    string VariantActionLabel,
    bool VariantActionEnabled,
    bool Interactive,
    bool Resolved,
    bool Launchable,
    bool Busy)
{
    internal string? PrimaryAction { get; init; }
    internal IReadOnlyList<string> Categories { get; init; } = [];
    internal string? Version { get; init; }
    internal string? LastPlayed { get; init; }
    internal string? Playtime { get; init; }
    internal string? Operation { get; init; }
}

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
            item.Presentation.DisplayName,
            item.Presentation.Source.DisplayName,
            collection.HasBefore || collection.HasAfter);
    }

    internal static GameLauncherDetailsState Project(
        GameLauncherDetailsSelection selection,
        WidgetCursorResourceSnapshot<GameLauncherItem> collection,
        GameLauncherFixedRows fixedRows,
        GameLauncherPrivateState organization,
        string? launchingSavedId,
        IReadOnlyDictionary<string, GameLauncherLaunchState> launchStates,
        string status,
        string? variantSeedSavedId,
        bool organizationBusy,
        bool interactive)
    {
        var current = collection.Items.Concat(fixedRows.All).FirstOrDefault(item =>
            item.Key == selection.Key && string.Equals(item.Value.SavedId,
                selection.SavedId, StringComparison.Ordinal));
        var resolved = current is not null;
        var launchable = current is not null &&
            current.Value.Presentation.Availability.State ==
                WidgetAppLibraryAvailabilityState.Installed &&
            current.Value.Presentation.Availability.IsLaunchable &&
            current.Value.Presentation.Capabilities.Supports(
                WidgetAppLibraryAction.Launch);
        var availability = GameLauncherAvailabilityPresentation.Tile(
            current, interactive);
        var launching = string.Equals(selection.SavedId, launchingSavedId,
            StringComparison.Ordinal);
        var metadata = current?.Presentation.Metadata;
        var providerCategories = metadata?.Categories ?? [];
        var categories = organization.Categories
            .Where(category => GameLauncherCategoryPolicy.Contains(
                category, selection.SavedId))
            .Select(category => category.Name)
            .Concat(providerCategories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var operation = current?.Presentation.ActiveOperation;
        var launchState = launchStates.TryGetValue(
            selection.SavedId, out var recorded) ? recorded : (GameLauncherLaunchState?)null;
        var launchStatus = launching ? "Pending" : launchState switch
        {
            GameLauncherLaunchState.RequestAccepted => "Request accepted",
            GameLauncherLaunchState.LauncherStarted => "Launcher started",
            GameLauncherLaunchState.Running => "Running",
            GameLauncherLaunchState.Failed => "Failed",
            GameLauncherLaunchState.Ended => "Ended",
            _ => launchable ? interactive ? "Ready" : "Paused" : "Play unavailable",
        };
        var group = GameLauncherOrganizationPolicy.GroupFor(
            organization, selection.SavedId);
        var seedGroup = variantSeedSavedId is null ? null :
            GameLauncherOrganizationPolicy.GroupFor(organization, variantSeedSavedId);
        var sameSeed = string.Equals(variantSeedSavedId, selection.SavedId,
            StringComparison.Ordinal);
        var pairedWithSeed = seedGroup is not null && group?.Id == seedGroup.Id;
        var variantLabel = variantSeedSavedId is null
            ? "Choose another variant"
            : sameSeed ? "Choose a different game"
            : pairedWithSeed ? "Remove from variant group"
            : "Group with selected game";
        return new(
            selection,
            current?.Presentation.DisplayName ?? selection.DisplayName,
            current?.Presentation.Source.DisplayName ?? selection.SourceAttribution,
            resolved ? availability.Status : "Unavailable",
            launchStatus,
            organization.FavoriteSavedIds.Contains(
                selection.SavedId, StringComparer.Ordinal),
            group?.PreferredSavedId == selection.SavedId,
            group?.SavedIds.Count ?? 0,
            status,
            variantLabel,
            !sameSeed,
            interactive,
            resolved,
            launchable,
            launching || organizationBusy)
        {
            PrimaryAction = launchable ? "Launch" : availability.PrimaryAction,
            Categories = categories,
            Version = metadata?.Version,
            LastPlayed = metadata?.LastPlayedAtUnixMilliseconds is { } lastPlayed
                ? FormatLastPlayed(lastPlayed)
                : null,
            Playtime = metadata?.PlaytimeMinutes is { } minutes
                ? FormatPlaytime(minutes)
                : null,
            Operation = operation is null
                ? null
                : $"{FormatName(operation.Kind.ToString())} · " +
                  FormatName(operation.State.ToString()),
        };
    }

    private static string FormatPlaytime(long minutes) => minutes < 60
        ? $"{minutes} min"
        : $"{minutes / 60} h {minutes % 60} min";

    private static string? FormatLastPlayed(long unixMilliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
                .UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string FormatName(string value) => string.Concat(
        value.Select((character, index) => index > 0 && char.IsUpper(character)
            ? " " + char.ToLowerInvariant(character)
            : character.ToString()));

    internal static string? ResolveActionSource(
        GameLauncherDetailsSelection? selection,
        string sourceElementId,
        WidgetCursorResourceSnapshot<GameLauncherItem> collection,
        GameLauncherFixedRows fixedRows)
    {
        if (selection is null || !sourceElementId.StartsWith(
                "game-launcher.details.", StringComparison.Ordinal) &&
            !sourceElementId.StartsWith(
                "game-launcher.actions.", StringComparison.Ordinal))
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
        var organization = $"Organization · {favorite} · {groupStatus}";
        if (state.Categories.Count != 0)
            organization += " · " + string.Join(", ", state.Categories);
        var details = new List<WidgetElement>
        {
                    UI.Text("GAME DETAILS", "game-launcher.details.eyebrow", "Game details")
                        .Classes("game-launcher-eyebrow"),
                    UI.Text(state.DisplayName, "game-launcher.details.title",
                            state.DisplayName)
                        .Classes("game-launcher-title"),
                    UI.Text($"Source · {state.SourceAttribution}",
                        "game-launcher.details.source", "Game source"),
                    UI.Text($"Availability · {state.Availability}",
                        "game-launcher.details.availability", "Game availability"),
        };
        if (state.PrimaryAction is { } primaryAction)
            details.Add(UI.Text($"Primary action · {primaryAction}",
                "game-launcher.details.primary-action", "Game primary action"));
        if (state.Version is { } version)
            details.Add(UI.Text($"Version · {version}",
                "game-launcher.details.version", "Game version"));
        if (state.LastPlayed is { } lastPlayed)
            details.Add(UI.Text($"Last played · {lastPlayed}",
                "game-launcher.details.last-played", "Game last played"));
        if (state.Playtime is { } playtime)
            details.Add(UI.Text($"Playtime · {playtime}",
                "game-launcher.details.playtime", "Game playtime"));
        if (state.Operation is { } operation)
            details.Add(UI.Text($"Operation · {operation}",
                "game-launcher.details.operation", "Game operation"));
        details.AddRange(
        [
                    UI.Text($"Launch state · {state.LaunchStatus}",
                        "game-launcher.details.launch-state", "Game launch state"),
                    UI.Text(organization,
                        "game-launcher.details.organization", "Game organization"),
                    UI.Text($"Status · {state.Feedback}",
                        "game-launcher.details.feedback", "Game action status"),
                    UI.HorizontalScroll("game-launcher.details.actions",
                        UI.Button("Launch", "game-launcher.launch",
                                GameLauncherDetailsPolicy.ActionSourceId)
                            .Busy(state.Busy)
                            .Disabled(!enabled || !state.Launchable),
                        UI.Button(state.Favorite ? "Remove favorite" : "Add favorite",
                                "game-launcher.favorite", "game-launcher.details.favorite")
                            .Disabled(!enabled),
                        UI.Button("Hide", "game-launcher.hide", "game-launcher.details.hide")
                            .Disabled(!enabled),
                        UI.Button(state.VariantActionLabel,
                                "game-launcher.variant", "game-launcher.details.variant")
                            .Disabled(!enabled || !state.VariantActionEnabled),
                        UI.Button("Prefer variant", "game-launcher.prefer",
                                "game-launcher.details.prefer")
                            .Disabled(!enabled || state.GroupSize < 2 || state.Preferred)),
                    UI.Row("game-launcher.details.hints",
                        UI.ControllerHint(ControllerButton.A, "Launch", "game-launcher.details.hint.launch"),
                        UI.ControllerHint(ControllerButton.X, "Favorite", "game-launcher.details.hint.favorite"),
                        UI.ControllerHint(ControllerButton.Y, "Game actions", "game-launcher.details.hint.actions")),
        ]);
        var scroll = UI.VerticalScroll("game-launcher.details.scroll",
                UI.Stack("game-launcher.details.content", details.ToArray()))
            .Classes("game-launcher-scroll", "game-launcher-main")
            .Shortcut(ControllerButton.X, actionId: "game-launcher.favorite")
            .Shortcut(ControllerButton.Y, actionId: GameLauncherActionSheet.OpenAction);
        return new WidgetView(
            UI.Stack("game-launcher.details.root", scroll).Classes("game-launcher-widget"),
            GameLauncherDetailsPolicy.ActionSourceId,
            Surface: Surface);
    }
}
