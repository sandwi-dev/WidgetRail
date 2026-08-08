using System.Globalization;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsAudioProvider;
using GameBarAlternative.WindowsNetworkProvider;
using GameBarAlternative.WindowsActivityProvider;
using GameBarAlternative.WindowsBluetoothProvider;
using GameBarAlternative.WindowsMediaProvider;

namespace GameBarAlternative.WidgetBridge;

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
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            var settingsPaths = PlatformSettingsPaths.CreateDefault();
            var installationRoot = Path.GetDirectoryName(Path.GetFullPath(catalogPath))
                ?? Environment.CurrentDirectory;
            var installedCatalogRoot = ResolveInstalledCatalogRoot(args, settingsPaths.RootDirectory);
            var workerHostExecutable = Path.Combine(
                installationRoot, "runtime", "WidgetWorkerHost", "WidgetWorkerHost.exe");
            var catalogLoad = await BridgeCatalog.LoadWithInstalledAsync(
                catalogPath,
                installedCatalogRoot,
                workerHostExecutable,
                shutdown.Token).ConfigureAwait(false);
            foreach (var warning in catalogLoad.Warnings)
                Console.Error.WriteLine($"Widget catalog warning: {warning}");
            var catalog = catalogLoad.Catalog;
            await using var catalogMonitor = new BridgeCatalogMonitor(
                catalogPath, installedCatalogRoot, workerHostExecutable, catalog,
                catalogLoad.Warnings);
            catalogMonitor.Diagnostics += (_, warnings) =>
            {
                foreach (var warning in warnings)
                    Console.Error.WriteLine($"Widget catalog warning: {warning}");
            };
            catalogMonitor.Start();
            var settingsStore = new PlatformSettingsStore(settingsPaths);
            await using var appearance = new PlatformAppearanceService(
                settingsPaths,
                new ThemeManager(settingsStore, new ThemeCatalog(settingsPaths)));
            await appearance.StartAsync(shutdown.Token).ConfigureAwait(false);
            var consentStore = new ConsentStore(
                Path.Combine(settingsPaths.RootDirectory, "consent"));
            await using var platformBackend = new CompositePlatformBrokerBackend(
                new WindowsAudioPlatformBackend(),
                new WindowsNetworkPlatformBackend(),
                new WindowsActivityPlatformBackend(),
                new WindowsBluetoothPlatformBackend(),
                new WindowsMediaPlatformBackend(),
                new GameBarAlternative.WindowsAppLibraryProvider.WindowsAppLibraryProvider());
            await using var server = new WidgetBridgeServer(
                pipeName, catalog, maximumBytes, appearance, consentStore, platformBackend,
                catalogMonitor);
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
}
