using WidgetRail.PlatformDiagnostics;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal readonly record struct SettingsAuthorityRecoveryRequest(
    string RecoveryId,
    string DisplayName,
    string ConfirmationToken);

internal sealed record SettingsAuthorityRecoverySelection(
    string RecoveryId,
    string? ConfirmationToken);

internal readonly record struct SettingsAuthorityRecoveryTransition(
    SettingsPage Page,
    SettingsAuthorityRecoverySelection? Selection,
    bool IsError,
    string Status);

/// <summary>
/// Exact-token admission and closed result projection for privileged content-authority recovery.
/// Ordinary preference actions never enter this boundary.
/// </summary>
internal static class SettingsAuthorityRecoveryPolicy
{
    public static int[] OrderedIndexes(PlatformDiagnosticsSnapshot diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return Enumerable.Range(0, diagnostics.AuthorityRecoveries.Count)
            .OrderBy(index => diagnostics.AuthorityRecoveries[index].DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(index => diagnostics.AuthorityRecoveries[index].RecoveryId,
                StringComparer.Ordinal)
            .ToArray();
    }

    public static bool TrySelect(
        PlatformDiagnosticsSnapshot diagnostics,
        int displayIndex,
        out SettingsAuthorityRecoverySelection selection)
    {
        var order = OrderedIndexes(diagnostics);
        if (displayIndex < 0 || displayIndex >= order.Length)
        {
            selection = null!;
            return false;
        }
        var recovery = diagnostics.AuthorityRecoveries[order[displayIndex]];
        selection = new(recovery.RecoveryId, recovery.ConfirmationToken);
        return true;
    }

    public static bool TryAuthorize(
        PlatformDiagnosticsSnapshot diagnostics,
        SettingsAuthorityRecoverySelection? selection,
        out SettingsAuthorityRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var recovery = diagnostics.AuthorityRecoveries.FirstOrDefault(item =>
            string.Equals(item.RecoveryId, selection?.RecoveryId, StringComparison.Ordinal));
        if (recovery is null ||
            !recovery.CanRetry ||
            recovery.ConfirmationToken is null ||
            selection?.ConfirmationToken is null ||
            !string.Equals(
                recovery.ConfirmationToken,
                selection.ConfirmationToken,
                StringComparison.Ordinal))
        {
            request = default;
            return false;
        }
        request = new(
            recovery.RecoveryId,
            recovery.DisplayName,
            selection.ConfirmationToken);
        return true;
    }

    public static SettingsAuthorityRecoveryTransition Changed() => new(
        SettingsPage.Diagnostics,
        null,
        true,
        "Authority recovery request changed; review current diagnostics");

    public static SettingsAuthorityRecoveryTransition Cancelled(
        SettingsAuthorityRecoveryRequest request) => new(
        SettingsPage.AuthorityRecovery,
        new(request.RecoveryId, request.ConfirmationToken),
        false,
        "Authority recovery was cancelled; review current diagnostics before retrying");

    public static SettingsAuthorityRecoveryTransition Failure(
        PlatformDiagnosticsSnapshot diagnostics,
        SettingsAuthorityRecoveryRequest request,
        string code)
    {
        var stillPending = Contains(diagnostics, request.RecoveryId);
        return new(
            stillPending ? SettingsPage.AuthorityRecovery : SettingsPage.Diagnostics,
            stillPending ? new(request.RecoveryId, request.ConfirmationToken) : null,
            true,
            stillPending
                ? $"Authority recovery failed ({code}); the record remains pending"
                : $"Authority recovery request changed ({code}); review current diagnostics");
    }

    public static SettingsAuthorityRecoveryTransition RefreshFailure(
        PlatformAuthorityRecoveryRetryResult result,
        SettingsAuthorityRecoveryRequest request,
        string code)
    {
        var returnsToDiagnostics = result.Status is
            PlatformAuthorityRecoveryRetryStatus.Recovered or
            PlatformAuthorityRecoveryRetryStatus.Stale;
        return new(
            returnsToDiagnostics ? SettingsPage.Diagnostics : SettingsPage.AuthorityRecovery,
            returnsToDiagnostics ? null : new(request.RecoveryId, request.ConfirmationToken),
            true,
            $"Authority recovery returned {result.Status}; diagnostics refresh failed ({code})");
    }

    public static SettingsAuthorityRecoveryTransition Result(
        PlatformAuthorityRecoveryRetryResult result,
        PlatformDiagnosticsSnapshot diagnostics,
        SettingsAuthorityRecoveryRequest request)
    {
        var stillPending = Contains(diagnostics, request.RecoveryId);
        var returnsToDiagnostics = result.Status is
                PlatformAuthorityRecoveryRetryStatus.Recovered or
                PlatformAuthorityRecoveryRetryStatus.Stale ||
            !stillPending;
        return new(
            returnsToDiagnostics ? SettingsPage.Diagnostics : SettingsPage.AuthorityRecovery,
            returnsToDiagnostics ? null : new(request.RecoveryId, request.ConfirmationToken),
            result.Status != PlatformAuthorityRecoveryRetryStatus.Recovered,
            result.Status switch
            {
                PlatformAuthorityRecoveryRetryStatus.Recovered =>
                    $"Recovered content authority for {request.DisplayName}",
                PlatformAuthorityRecoveryRetryStatus.StillPending =>
                    $"Authority recovery remains pending ({result.Code})",
                PlatformAuthorityRecoveryRetryStatus.Stale =>
                    $"Authority recovery request changed ({result.Code}); review current diagnostics",
                PlatformAuthorityRecoveryRetryStatus.Unavailable =>
                    $"Authority recovery is unavailable ({result.Code}); the record remains pending",
                _ => $"Authority recovery was refused ({result.Code})",
            });
    }

    private static bool Contains(PlatformDiagnosticsSnapshot diagnostics, string recoveryId) =>
        diagnostics.AuthorityRecoveries.Any(item =>
            string.Equals(item.RecoveryId, recoveryId, StringComparison.Ordinal));
}
