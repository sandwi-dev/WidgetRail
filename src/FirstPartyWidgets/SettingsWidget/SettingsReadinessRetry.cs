using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetCatalog;

namespace WidgetRail.FirstPartyWidgets.Settings;

/// <summary>
/// Bounded readiness retry used by Settings while the Bridge registry or the
/// installed catalog is completing an authoritative replacement. Permanent
/// validation, identity, authentication, and access failures remain visible on
/// their first attempt.
/// </summary>
internal static class SettingsReadinessRetry
{
    internal const int MaximumAttempts = 2;
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(75);

    internal static Task<T> CatalogAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        ExecuteAsync(operation, IsTransientCatalogFailure, cancellationToken, delay);

    internal static async ValueTask<T> RegistryAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        await ExecuteAsync(
                token => operation(token).AsTask(),
                IsTransientRegistryFailure,
                cancellationToken,
                delay)
            .ConfigureAwait(false);

    private static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool> isTransient,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        ArgumentNullException.ThrowIfNull(operation);
        delay ??= Task.Delay;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                attempt < MaximumAttempts && isTransient(exception))
            {
                await delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransientCatalogFailure(Exception exception) => exception switch
    {
        IOException => true,
        WidgetPackageException package => package.Code is
            "catalog_busy" or
            "active_version_missing" or
            "package_not_found",
        _ => false,
    };

    private static bool IsTransientRegistryFailure(Exception exception) =>
        exception is PlatformDiagnosticsException diagnostics &&
        diagnostics.Code is "diagnostics_timeout" or "diagnostics_unavailable";
}
