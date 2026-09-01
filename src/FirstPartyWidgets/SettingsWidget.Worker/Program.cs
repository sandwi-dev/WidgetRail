using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.WidgetRuntime;

namespace WidgetRail.FirstPartyWidgets.Settings.Worker;

internal static class Program
{
    // Two classified readiness attempts plus the shared retry delay must remain
    // comfortably inside the host's two-second lifecycle request boundary.
    private static readonly TimeSpan DiagnosticsReadinessAttemptTimeout =
        TimeSpan.FromMilliseconds(500);

    public static async Task<int> Main(string[] args)
    {
        return await WidgetWorkerBootstrap.RunAsync(
            args,
            () =>
            {
                var installedCatalogRoot = OptionalPath(args, "--installed-widget-catalog-root");
                return new SettingsWidget(
                    widgetCatalog: installedCatalogRoot is null
                        ? null
                        : new WidgetRail.WidgetCatalog.WidgetCatalog(installedCatalogRoot),
                    diagnostics: CreateDiagnostics(args),
                    bundledWidgetRoot: OptionalPath(args, "--bundled-widget-root"));
            })
            .ConfigureAwait(false);
    }

    private static IPlatformDiagnosticsService CreateDiagnostics(string[] args)
    {
        var pipe = OptionalToken(args, "--diagnostics-pipe", 200);
        var nonce = OptionalToken(args, "--diagnostics-nonce", 64);
        var serverProcessId = OptionalPositiveInt(args, "--diagnostics-server-pid");
        if (pipe is null && nonce is null && serverProcessId is null)
            return UnavailablePlatformDiagnosticsService.Instance;
        if (pipe is null || nonce is null || serverProcessId is null || nonce.Length != 64 ||
            !nonce.All(char.IsAsciiHexDigit))
            throw new WidgetWorkerBootstrapException(
                "invalid_diagnostics_channel", "The diagnostics channel arguments are invalid.");
        return new PlatformDiagnosticsPipeClient(
            pipe,
            nonce,
            serverProcessId.Value,
            DiagnosticsReadinessAttemptTimeout);
    }

    private static int? OptionalPositiveInt(string[] args, string name)
    {
        var value = OptionalToken(args, name, 10);
        if (value is null) return null;
        if (!int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            throw new WidgetWorkerBootstrapException(
                "invalid_diagnostics_channel", "The diagnostics channel arguments are invalid.");
        return parsed;
    }

    private static string? OptionalToken(string[] args, string name, int maximumLength)
    {
        var indexes = Enumerable.Range(0, args.Length)
            .Where(index => string.Equals(args[index], name, StringComparison.Ordinal))
            .ToArray();
        if (indexes.Length == 0) return null;
        if (indexes.Length != 1)
            throw new WidgetWorkerBootstrapException(
                "invalid_diagnostics_channel", "The diagnostics channel arguments are invalid.");
        var index = indexes[0];
        var value = index + 1 < args.Length ? args[index + 1] : null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength ||
            value.StartsWith("--", StringComparison.Ordinal) ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new WidgetWorkerBootstrapException(
                "invalid_diagnostics_channel", "The diagnostics channel arguments are invalid.");
        return value;
    }

    private static string? OptionalPath(string[] args, string name)
    {
        var indexes = Enumerable.Range(0, args.Length)
            .Where(index => string.Equals(args[index], name, StringComparison.Ordinal))
            .ToArray();
        if (indexes.Length == 0) return null;
        if (indexes.Length != 1)
            throw new WidgetWorkerBootstrapException(
                "invalid_settings_root", "The bundled widget root argument is invalid.");
        var index = indexes[0];
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) ||
            args[index + 1].Length > 1024 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new WidgetWorkerBootstrapException(
                "invalid_settings_root", "The bundled widget root argument is invalid.");
        try
        {
            return Path.GetFullPath(args[index + 1]);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new WidgetWorkerBootstrapException(
                "invalid_settings_root", "The bundled widget root argument is invalid.",
                innerException: exception);
        }
    }
}
