using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using System.Globalization;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryDetailsSelection(
    string SavedId,
    WidgetCollectionItemKey Key,
    string ReturnFocusId,
    string DisplayName,
    string SourceAttribution,
    bool PageBumpers);

internal sealed record PlayniteLibraryDetailsState(
    PlayniteLibraryDetailsSelection Selection,
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
    internal string? CompletionStatus { get; init; }
}

internal static class PlayniteLibraryDetailsPolicy
{
    internal const string ActionSourceId = "playnite-library.details.launch";

    internal static PlayniteLibraryDetailsSelection? Select(
        string sourceElementId,
        WidgetCursorResourceSnapshot<PlayniteLibraryItem> collection,
        PlayniteLibraryFixedRows fixedRows)
    {
        var item = collection.Items.Concat(fixedRows.All).FirstOrDefault(candidate =>
            string.Equals(PlayniteLibraryIdentity.FocusId("grid", candidate.Key),
                sourceElementId, StringComparison.Ordinal));
        return item is null ? null : new(
            item.Value.SavedId,
            item.Key,
            sourceElementId,
            item.Presentation.DisplayName,
            item.Presentation.Source.DisplayName,
            collection.HasBefore || collection.HasAfter);
    }

    internal static PlayniteLibraryDetailsState Project(
        PlayniteLibraryDetailsSelection selection,
        WidgetCursorResourceSnapshot<PlayniteLibraryItem> collection,
        PlayniteLibraryFixedRows fixedRows,
        PlayniteLibraryPrivateState organization,
        string? launchingSavedId,
        IReadOnlyDictionary<string, PlayniteLibraryLaunchState> launchStates,
        string status,
        string? variantSeedSavedId,
        bool organizationBusy,
        bool interactive,
        IReadOnlyDictionary<string, string?>? completionStatuses = null)
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
        var availability = PlayniteLibraryAvailabilityPresentation.Tile(
            current, interactive);
        var launching = string.Equals(selection.SavedId, launchingSavedId,
            StringComparison.Ordinal);
        var metadata = current?.Presentation.Metadata;
        var providerCategories = metadata?.Categories ?? [];
        var categories = organization.Categories
            .Where(category => PlayniteLibraryCategoryPolicy.Contains(
                category, selection.SavedId))
            .Select(category => category.Name)
            .Concat(providerCategories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var operation = current?.Presentation.ActiveOperation;
        var launchState = launchStates.TryGetValue(
            selection.SavedId, out var recorded) ? recorded : (PlayniteLibraryLaunchState?)null;
        var launchStatus = launching ? "Pending" : launchState switch
        {
            PlayniteLibraryLaunchState.RequestAccepted => "Request accepted",
            PlayniteLibraryLaunchState.LauncherStarted => "Launcher started",
            PlayniteLibraryLaunchState.Running => "Running",
            PlayniteLibraryLaunchState.Failed => "Failed",
            PlayniteLibraryLaunchState.Ended => "Ended",
            _ => launchable ? interactive ? "Ready" : "Paused" : "Play unavailable",
        };
        var group = PlayniteLibraryOrganizationPolicy.GroupFor(
            organization, selection.SavedId);
        var seedGroup = variantSeedSavedId is null ? null :
            PlayniteLibraryOrganizationPolicy.GroupFor(organization, variantSeedSavedId);
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
            CompletionStatus = completionStatuses?.GetValueOrDefault(selection.SavedId),
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
        PlayniteLibraryDetailsSelection? selection,
        string sourceElementId,
        WidgetCursorResourceSnapshot<PlayniteLibraryItem> collection,
        PlayniteLibraryFixedRows fixedRows)
    {
        if (selection is null || !sourceElementId.StartsWith(
                "playnite-library.details.", StringComparison.Ordinal) &&
            !sourceElementId.StartsWith(
                "playnite-library.actions.", StringComparison.Ordinal))
            return sourceElementId;
        return collection.Items.Concat(fixedRows.All).Any(item =>
            item.Key == selection.Key && string.Equals(item.Value.SavedId,
                selection.SavedId, StringComparison.Ordinal))
            ? selection.ReturnFocusId
            : null;
    }
}

internal static class PlayniteLibraryDetailsPresentation
{
    private static readonly WidgetSurfaceHints Surface = new()
    {
        Mode = WidgetSurfaceMode.Wide,
        WidthMode = WidgetSurfaceAxisMode.Preferred,
        HeightMode = WidgetSurfaceAxisMode.Preferred,
        PreferredWidth = 980,
        PreferredHeight = 720,
        MinimumWidth = 520,
        MinimumHeight = 420,
    };

    internal static WidgetView Render(PlayniteLibraryDetailsState state)
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
                    UI.Text("GAME DETAILS", "playnite-library.details.eyebrow", "Game details")
                        .Classes("playnite-library-eyebrow"),
                    UI.Text(state.DisplayName, "playnite-library.details.title",
                            state.DisplayName)
                        .Classes("playnite-library-title"),
                    UI.Text($"Source · {state.SourceAttribution}",
                        "playnite-library.details.source", "Game source"),
                    UI.Text($"Availability · {state.Availability}",
                        "playnite-library.details.availability", "Game availability"),
        };
        if (state.PrimaryAction is { } primaryAction)
            details.Add(UI.Text($"Primary action · {primaryAction}",
                "playnite-library.details.primary-action", "Game primary action"));
        if (state.Version is { } version)
            details.Add(UI.Text($"Version · {version}",
                "playnite-library.details.version", "Game version"));
        if (state.LastPlayed is { } lastPlayed)
            details.Add(UI.Text($"Last played · {lastPlayed}",
                "playnite-library.details.last-played", "Game last played"));
        if (state.Playtime is { } playtime)
            details.Add(UI.Text($"Playtime · {playtime}",
                "playnite-library.details.playtime", "Game playtime"));
        if (state.CompletionStatus is { } completionStatus)
            details.Add(UI.Text($"Completion · {completionStatus}",
                "playnite-library.details.completion", "Game completion status"));
        if (state.Operation is { } operation)
            details.Add(UI.Text($"Operation · {operation}",
                "playnite-library.details.operation", "Game operation"));
        details.AddRange(
        [
                    UI.Text($"Launch state · {state.LaunchStatus}",
                        "playnite-library.details.launch-state", "Game launch state"),
                    UI.Text(organization,
                        "playnite-library.details.organization", "Game organization"),
                    UI.Text($"Status · {state.Feedback}",
                        "playnite-library.details.feedback", "Game action status"),
                    UI.HorizontalScroll("playnite-library.details.actions",
                        UI.Button("Launch", "playnite-library.launch",
                                PlayniteLibraryDetailsPolicy.ActionSourceId)
                            .Busy(state.Busy)
                            .Disabled(!enabled || !state.Launchable),
                        UI.Button(state.Favorite ? "Remove favorite" : "Add favorite",
                                "playnite-library.favorite", "playnite-library.details.favorite")
                            .Disabled(!enabled),
                        UI.Button("Hide", "playnite-library.hide", "playnite-library.details.hide")
                            .Disabled(!enabled),
                        UI.Button(state.VariantActionLabel,
                                "playnite-library.variant", "playnite-library.details.variant")
                            .Disabled(!enabled || !state.VariantActionEnabled),
                         UI.Button("Prefer variant", "playnite-library.prefer",
                                 "playnite-library.details.prefer")
                             .Disabled(!enabled || state.GroupSize < 2 || state.Preferred))
                        .Classes("playnite-library-details-actions"),
                    UI.Row("playnite-library.details.hints",
                        UI.ControllerHint(ControllerButton.A, "Launch", "playnite-library.details.hint.launch"),
                        UI.ControllerHint(ControllerButton.X, "Favorite", "playnite-library.details.hint.favorite"),
                        UI.ControllerHint(ControllerButton.Y, "Game actions", "playnite-library.details.hint.actions")),
        ]);
        var scroll = UI.VerticalScroll("playnite-library.details.scroll",
                UI.Stack("playnite-library.details.content", details.ToArray()))
            .Classes("playnite-library-scroll", "playnite-library-main",
                "playnite-library-details-scroll")
            .Shortcut(ControllerButton.X, actionId: "playnite-library.favorite")
            .Shortcut(ControllerButton.Y, actionId: PlayniteLibraryActionSheet.OpenAction);
        return new WidgetView(
            UI.Stack("playnite-library.details.root", scroll)
                .Classes("playnite-library-widget", "playnite-library-details"),
            PlayniteLibraryDetailsPolicy.ActionSourceId,
            Surface: Surface);
    }
}
