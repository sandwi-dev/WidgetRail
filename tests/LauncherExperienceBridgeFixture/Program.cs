using System.Globalization;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetRuntime;

namespace GameBarAlternative.LauncherExperienceBridgeFixture;

internal static class Program
{
    private const string ControlFileName = "launcher-experience-provider-fixture.txt";
    private static readonly BrokerWidgetIdentity GameLauncherIdentity = new(
        "org.gbar.firstparty.game-launcher",
        "org.gbar.firstparty",
        "game-launcher.default");

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var pipeName = RequiredValue(args, "--host-pipe");
            var catalogPath = Path.GetFullPath(RequiredValue(args, "--catalog"));
            var control = await File.ReadAllLinesAsync(
                Path.Combine(AppContext.BaseDirectory, ControlFileName)).ConfigureAwait(false);
            if (control.Length != 2 || string.IsNullOrWhiteSpace(control[1]))
                throw new InvalidOperationException("Launcher Experience fixture control is invalid.");
            var scenario = control[0].Trim();
            var settingsRoot = Path.GetFullPath(control[1]);
            var installedCatalogRoot = OptionalValue(args, "--installed-catalog-root") is { } root
                ? Path.GetFullPath(root)
                : Path.Combine(settingsRoot, "widgets");
            var acceptTimeout = OptionalInt(
                args, "--accept-timeout-ms", 10_000, 100, 60_000);
            var maximumBytes = OptionalInt(
                args,
                "--max-message-bytes",
                BridgeProtocol.DefaultMaximumMessageBytes,
                256,
                BridgeProtocol.AbsoluteMaximumMessageBytes);
            if (scenario is not ("adoption" or "fallback"))
                throw new InvalidOperationException("Unknown Launcher Experience fixture scenario.");

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            var installationRoot = Path.GetDirectoryName(catalogPath)
                ?? throw new InvalidOperationException("Catalog installation root was absent.");
            var workerHostExecutable = Path.Combine(
                installationRoot, "runtime", "WidgetWorkerHost", "WidgetWorkerHost.exe");
            var catalogLoad = await BridgeCatalog.LoadWithInstalledAsync(
                catalogPath,
                installedCatalogRoot,
                workerHostExecutable,
                shutdown.Token).ConfigureAwait(false);
            foreach (var warning in catalogLoad.Warnings)
                Console.Error.WriteLine($"Widget catalog warning: {warning}");

            var consent = new ConsentStore(Path.Combine(settingsRoot, "consent"));
            await consent.SetDecisionAsync(
                GameLauncherIdentity,
                PlatformCapabilities.AppLibraryReadV1,
                ConsentDecision.Grant,
                shutdown.Token).ConfigureAwait(false);
            await consent.SetDecisionAsync(
                GameLauncherIdentity,
                PlatformCapabilities.AppLibraryLaunchV1,
                ConsentDecision.Grant,
                shutdown.Token).ConfigureAwait(false);

            var simulator = new SimulatedPlatformBrokerBackend();
            await using var backend = new CompositePlatformBrokerBackend(
                simulator,
                simulator,
                appLibrary: scenario == "adoption"
                    ? new SeededAppLibrary(Path.Combine(
                        settingsRoot, "launcher-experience-backend.txt"))
                    : UnavailableAppLibrary.Instance,
                privateState: simulator);

            await using var server = new WidgetBridgeServer(
                pipeName,
                catalogLoad.Catalog,
                maximumBytes,
                consentStore: consent,
                platformBackend: backend);
            await server.RunAsync(
                TimeSpan.FromMilliseconds(acceptTimeout), shutdown.Token)
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Launcher Experience fixture failed: {exception.Message}");
            return 1;
        }
    }

    private static string RequiredValue(string[] args, string name)
    {
        var value = OptionalValue(args, name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Missing required argument {name}.")
            : value;
    }

    private static string? OptionalValue(string[] args, string name)
    {
        var matches = Enumerable.Range(0, args.Length)
            .Where(index => string.Equals(args[index], name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || matches[0] + 1 >= args.Length ||
            args[matches[0] + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Invalid value for {name}.");
        return args[matches[0] + 1];
    }

    private static int OptionalInt(
        string[] args, string name, int fallback, int minimum, int maximum)
    {
        var value = OptionalValue(args, name);
        if (value is null) return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ||
            result < minimum || result > maximum)
            throw new ArgumentException($"Invalid value for {name}.");
        return result;
    }

    private sealed class UnavailableAppLibrary : IAppLibraryPlatformBrokerBackend
    {
        internal static UnavailableAppLibrary Instance { get; } = new();
    }

    private sealed class SeededAppLibrary(string diagnosticPath) : IAppLibraryPlatformBrokerBackend
    {
        private const string ArtworkPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
            "AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3xWQAAAABJRU5ErkJggg==";
        private static readonly AppLibraryBackendItemSummary[] Games =
        [
            new(
                "dlv146-provider-game",
                "dlv146-stable-game",
                "DLV-146 Trusted Game",
                AppLibraryKind.Game,
                ArtworkRevision: "dlv148-artwork-1",
                SourceAttribution: "DLV-146 Fixture")
            {
                SourceIdentity = "dlv146-fixture",
            },
            new(
                "dlv148-provider-game",
                "dlv148-stable-game",
                "DLV-148 Motion Game",
                AppLibraryKind.Game,
                ArtworkRevision: "dlv148-artwork-2",
                SourceAttribution: "DLV-146 Fixture")
            {
                SourceIdentity = "dlv146-fixture",
            },
        ];

        public Task<AppLibraryBackendCursorPage> QueryAppLibraryAsync(
            AppLibraryBackendCursorRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(request);
            var admitted = request.Cursor is null &&
                request.Query.Kind is null or AppLibraryKind.Game;
            IReadOnlyList<AppLibraryBackendItemSummary> items = admitted
                ? Games.Where(game =>
                    (request.Query.SourceAttribution is null ||
                        request.Query.SourceAttribution == game.SourceAttribution) &&
                    (request.Query.SearchText is null || game.DisplayName.Contains(
                        request.Query.SearchText, StringComparison.OrdinalIgnoreCase)) &&
                    (request.Query.StableIdentityFilter is null ||
                        request.Query.StableIdentityFilter.Contains(
                            game.StableProviderIdentity, StringComparer.Ordinal)))
                    .ToArray()
                : [];
            File.AppendAllText(diagnosticPath,
                $"query limit={request.Limit} items={items.Count} cursor={request.Cursor ?? "first"}{Environment.NewLine}");
            return Task.FromResult(new AppLibraryBackendCursorPage(
                items, null, null, "dlv146-seeded-revision"));
        }

        public Task<AppLibraryIconSummary> GetAppLibraryIconAsync(
            string appId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AppLibraryIconSummary(
                Games.Any(game => game.ProviderAppId == appId)
                    ? ArtworkPngBase64
                    : null));
        }

        public Task LaunchAppLibraryItemAsync(
            string appId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Games.Any(game => game.ProviderAppId == appId))
                throw new BrokerException("app_not_found", "Seeded game was not found.");
            return Task.CompletedTask;
        }
    }
}
