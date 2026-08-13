using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal sealed record GameLauncherTileAvailability(
    string Status,
    bool Launchable,
    bool Busy)
{
    internal string? PrimaryAction { get; init; }
}

internal static class GameLauncherAvailabilityPresentation
{
    internal static GameLauncherTileAvailability Tile(
        GameLauncherItem? item,
        bool interactive)
    {
        if (item is null) return new("Play unavailable · Current provider data is missing", false, false);
        var presentation = item.Value.Presentation;
        if (presentation.ActiveOperation is { } operation && operation.State is
            WidgetAppLibraryOperationState.Pending or
            WidgetAppLibraryOperationState.RequestAccepted or
            WidgetAppLibraryOperationState.LauncherStarted or
            WidgetAppLibraryOperationState.Running or
            WidgetAppLibraryOperationState.Paused)
            return new($"Busy · {OperationLabel(operation)}", false, true);
        if (presentation.Availability.State == WidgetAppLibraryAvailabilityState.StaleSource)
            return IsOwned(presentation)
                ? new("Owned · Source degraded · Refresh to confirm install availability",
                    false, false)
                : new("Source degraded · Refresh to confirm availability", false, false);
        if (presentation.Availability.State == WidgetAppLibraryAvailabilityState.Unavailable)
            return OwnedUnavailable(presentation);
        if (!presentation.Availability.IsLaunchable ||
            !presentation.Capabilities.Supports(WidgetAppLibraryAction.Launch))
            return new(DisabledReason(presentation.Availability.StatusCode), false, false);
        return new(interactive ? "Ready" : "Paused", interactive, false);
    }

    private static GameLauncherTileAvailability OwnedUnavailable(
        WidgetAppLibraryPresentation presentation)
    {
        if (!IsOwned(presentation))
            return new("Offline · This game is currently unavailable", false, false);
        var status = presentation.Availability.StatusCode;
        if (status is "owned_signed_out" or "signed_out")
            return new("Owned · Sign in to install", false, false);
        if (status is "owned_offline" or "source_offline")
            return new("Owned · Offline · Install unavailable", false, false);
        if (status is "owned_unsupported" or "install_unsupported")
            return new("Owned · Install unsupported by this source", false, false);
        return presentation.Capabilities.Supports(WidgetAppLibraryAction.Install)
            ? new("Owned · Not installed · Install available from source", false, false)
                { PrimaryAction = "Install from source" }
            : new("Owned · Not installed · Install unavailable", false, false);
    }

    private static bool IsOwned(WidgetAppLibraryPresentation presentation) =>
        presentation.Capabilities.Supports(WidgetAppLibraryAction.Install) ||
        presentation.Availability.StatusCode.StartsWith("owned_",
            StringComparison.Ordinal);

    internal static (string Title, string Message) Error(WidgetResourceError? error) =>
        error?.Code switch
        {
            "permission_denied" => (
                "Game library permission denied",
                error.Message),
            "library_offline" => (
                "Game library offline",
                error.Message),
            _ => (
                "Game library unavailable",
                error?.Message ?? "The installed game library could not be loaded."),
        };

    internal static string SourceDetail(WidgetAppLibrarySource source) =>
        source.AccountState switch
        {
            WidgetAppLibrarySourceAccountState.Denied =>
                "Permission denied · Allow this source in Settings",
            WidgetAppLibrarySourceAccountState.SignedOut or
            WidgetAppLibrarySourceAccountState.Expired =>
                "Sign-in required · Current saved games remain visible",
            _ => source.Health switch
            {
                WidgetAppLibrarySourceHealth.Healthy =>
                    "Current installed games are available",
                WidgetAppLibrarySourceHealth.Degraded =>
                    "Degraded · Some installed games may be missing",
                WidgetAppLibrarySourceHealth.Unavailable =>
                    "Offline · This source could not be refreshed",
                WidgetAppLibrarySourceHealth.Refreshing =>
                    "Refreshing installed games",
                _ => "Source status is limited",
            },
        };

    private static string DisabledReason(string statusCode) => statusCode switch
    {
        "permission_denied" or "capability_revoked" =>
            "Permission denied · Allow launch access in Settings",
        "source_disabled" or "disabled" =>
            "Disabled · Enable this source in Settings",
        "operation_busy" or "busy" =>
            "Busy · Another operation is in progress",
        _ => "Play unavailable · This source does not provide launch authority",
    };

    private static string OperationLabel(WidgetAppLibraryOperation operation) =>
        $"{operation.Kind} {operation.State}";
}
