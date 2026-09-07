using System.Globalization;
using System.Diagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsAudioProvider;
using WidgetRail.WindowsNetworkProvider;
using WidgetRail.WindowsActivityProvider;
using WidgetRail.WindowsBluetoothProvider;
using WidgetRail.WindowsMediaProvider;
using WidgetRail.WindowsCommunityProvider;

namespace WidgetRail.WidgetBridge;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var pipeName = RequiredValue(args, "--host-pipe");
            var catalogPath = RequiredValue(args, "--catalog");
            var acceptTimeout = OptionalInt(args, "--accept-timeout-ms", 10_000, 100, 60_000);
            var maximumBytes = OptionalInt(args, "--max-message-bytes",
                BridgeProtocol.DefaultMaximumMessageBytes, 256, BridgeProtocol.AbsoluteMaximumMessageBytes);
            var bridgeSessionGeneration = OptionalLong(
                args, "--bridge-session-generation", 1, 1, long.MaxValue);
            var residencyBudget = ResolveWorkerResidencyBudget(args);
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            var settingsPaths = OptionalValue(args, "--settings-root") is { } settingsRoot
                ? new PlatformSettingsPaths(settingsRoot)
                : PlatformSettingsPaths.CreateDefault();
            var installationRoot = Path.GetDirectoryName(Path.GetFullPath(catalogPath))
                ?? Environment.CurrentDirectory;
            var installedCatalogRoot = ResolveInstalledCatalogRoot(args, settingsPaths.RootDirectory);
            var workerHostExecutable = Path.Combine(
                installationRoot, "runtime", "WidgetWorkerHost", "WidgetWorkerHost.exe");
            await using var mediaDiagnostics = new MediaSessionsDiagnosticLog(
                Path.Combine(settingsPaths.RootDirectory, "overlay.log"),
                bridgeSessionGeneration);
            var trustedCatalogStarted = Stopwatch.GetTimestamp();
            var trustedCatalog = BridgeCatalog.LoadTrustedObserved(
                catalogPath, installedCatalogRoot);
            var catalog = trustedCatalog.Catalog;
            mediaDiagnostics.RecordBridgeStartupPhase(
                "trusted-catalog-ready",
                (long)Stopwatch.GetElapsedTime(trustedCatalogStarted).TotalMilliseconds);
            await using var catalogMonitor = new BridgeCatalogMonitor(
                catalogPath, installedCatalogRoot, workerHostExecutable, catalog,
                initialDiagnostics: trustedCatalog.Warnings,
                installedCatalogPending: true,
                loadCatalog: null,
                catalogLoadObserved: mediaDiagnostics.RecordInstalledCatalogLoad,
                initialWidgetRejections: trustedCatalog.WidgetRejections);
            catalogMonitor.Diagnostics += (_, warnings) =>
            {
                foreach (var warning in warnings)
                    Console.Error.WriteLine($"Widget catalog warning: {warning}");
            };
            foreach (var warning in trustedCatalog.Warnings)
                Console.Error.WriteLine($"Widget catalog warning: {warning}");
            var settingsStore = new PlatformSettingsStore(settingsPaths);
            mediaDiagnostics.RecordBridgeSessionStarted(Environment.ProcessId);
            await using var appearance = new PlatformAppearanceService(
                settingsPaths,
                new ThemeManager(settingsStore, new ThemeCatalog(settingsPaths)));
            await appearance.StartAsync(shutdown.Token).ConfigureAwait(false);
            var consentStore = new ConsentStore(
                Path.Combine(settingsPaths.RootDirectory, "consent"));
            await using var communityBackend = new WindowsCommunityPlatformBackend(
                Path.Combine(settingsPaths.RootDirectory, "widget-state"));
            await using var platformBackend = new CompositePlatformBrokerBackend(
                new WindowsAudioPlatformBackend(),
                new WindowsNetworkPlatformBackend(),
                new WindowsActivityPlatformBackend(),
                new WindowsBluetoothPlatformBackend(),
                new WindowsMediaPlatformBackend(),
                new WidgetRail.WindowsAppLibraryProvider.WindowsAppLibraryProvider(),
                communityBackend,
                communityBackend,
                communityBackend);
            await using var server = new WidgetBridgeServer(
                pipeName, catalog, maximumBytes, appearance, consentStore, platformBackend,
                catalogMonitor, residencyBudget,
                mediaDiagnostics.Record,
                mediaDiagnostics.RecordLifetime,
                mediaDiagnostics.RecordRequestFailure,
                Path.Combine(settingsPaths.RootDirectory, "worker-diagnostics"));
            mediaDiagnostics.RecordBridgeStartupPhase("control-plane-created");
            catalogMonitor.Start();
            mediaDiagnostics.RecordBridgeStartupPhase("installed-catalog-pending");
            await server.RunAsync(TimeSpan.FromMilliseconds(acceptTimeout), shutdown.Token)
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Widget bridge failed: {exception.Message}");
            return 1;
        }
    }

    private static string RequiredValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"Missing required argument {name}.");
        return args[index + 1];
    }

    private static int OptionalInt(string[] args, string name, int fallback, int minimum, int maximum)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return fallback;
        if (index + 1 >= args.Length ||
            !int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < minimum || value > maximum)
            throw new ArgumentException($"Invalid value for {name}.");
        return value;
    }

    private static long OptionalLong(
        string[] args,
        string name,
        long fallback,
        long minimum,
        long maximum)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return fallback;
        if (index + 1 >= args.Length ||
            !long.TryParse(
                args[index + 1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value) ||
            value < minimum || value > maximum)
            throw new ArgumentException($"Invalid value for {name}.");
        return value;
    }

    private static string? OptionalValue(string[] args, string name)
    {
        var matches = Enumerable.Range(0, args.Length)
            .Where(index => string.Equals(args[index], name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || matches[0] + 1 >= args.Length ||
            string.IsNullOrWhiteSpace(args[matches[0] + 1]) ||
            args[matches[0] + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Invalid value for {name}.");
        return args[matches[0] + 1];
    }

    internal static string ResolveInstalledCatalogRoot(string[] args, string settingsRoot)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsRoot);
        return OptionalValue(args, "--installed-catalog-root") is { } requestedCatalog
            ? Path.GetFullPath(requestedCatalog)
            : Path.Combine(Path.GetFullPath(settingsRoot), "widgets");
    }

    internal static WorkerResidencyBudgetOptions ResolveWorkerResidencyBudget(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Contains("--max-resident-memory-mb", StringComparer.Ordinal))
            throw new ArgumentException(
                "--max-resident-memory-mb is no longer supported; worker memory is reported, not capped.");
        var requestedMaximum = OptionalValue(args, "--max-resident-workers");
        int? maximumApplicationWorkers = null;
        if (requestedMaximum is not null)
        {
            if (!int.TryParse(
                    requestedMaximum,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedMaximum) || parsedMaximum < 1)
                throw new ArgumentException("Invalid value for --max-resident-workers.");
            maximumApplicationWorkers = parsedMaximum;
        }
        return new WorkerResidencyBudgetOptions
        {
            MaximumApplicationWorkers = maximumApplicationWorkers,
        };
    }
}
