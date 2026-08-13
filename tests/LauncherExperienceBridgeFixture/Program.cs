using System.Globalization;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformSettings;
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
        string? failureRoot = null;
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
            failureRoot = settingsRoot;
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
            if (scenario is not ("adoption" or "fallback" or "selection" or "matrix" or "lifecycle"))
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
            var settingsPaths = new PlatformSettingsPaths(settingsRoot);
            var settingsStore = new PlatformSettingsStore(settingsPaths);
            await using var appearance = new PlatformAppearanceService(
                settingsPaths,
                new ThemeManager(settingsStore, new ThemeCatalog(settingsPaths)));
            await appearance.StartAsync(shutdown.Token).ConfigureAwait(false);
            await using var launcherExperience = new LauncherExperienceSelectionService(
                settingsStore);
            await launcherExperience.StartAsync(shutdown.Token).ConfigureAwait(false);
            if (scenario == "matrix")
            {
                try
                {
                    await new LauncherExperienceSelectionPolicy(settingsStore).RetireAsync(
                        "dev.example.production", "10.0.0", shutdown.Token)
                        .ConfigureAwait(false);
                    throw new InvalidOperationException(
                        "Selected Launcher Experience removal unexpectedly succeeded.");
                }
                catch (PlatformSettingsException exception) when (
                    exception.Code == "selected_launcher_experience_protected")
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(settingsRoot, "launcher-experience-removal-denial.txt"),
                        exception.Code,
                        shutdown.Token).ConfigureAwait(false);
                }
            }
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
                appLibrary: scenario is "adoption" or "selection" or "matrix" or "lifecycle"
                    ? new SeededAppLibrary(Path.Combine(
                        settingsRoot, scenario == "lifecycle"
                            ? $"launcher-experience-backend-{Environment.ProcessId}.txt"
                            : "launcher-experience-backend.txt"),
                        matrix: scenario == "matrix")
                    : UnavailableAppLibrary.Instance,
                privateState: simulator);

            await using var server = new WidgetBridgeServer(
                pipeName,
                catalogLoad.Catalog,
                maximumBytes,
                appearance: appearance,
                consentStore: consent,
                platformBackend: backend,
                launcherExperience: launcherExperience);
            await server.RunAsync(
                TimeSpan.FromMilliseconds(acceptTimeout), shutdown.Token)
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            if (failureRoot is not null)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(failureRoot, "launcher-experience-fixture-error.txt"),
                        exception.ToString());
                }
                catch { }
            }
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

    private sealed class SeededAppLibrary(
        string diagnosticPath,
        bool matrix = false) : IAppLibraryPlatformBrokerBackend
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
        private static readonly AppLibraryBackendItemSummary[] MatrixGames =
        [
            new(
                "dlv152-missing-art",
                "dlv152-missing-art-stable",
                "A deliberately long installed game title that remains fully targetable when artwork is unavailable",
                AppLibraryKind.Game,
                ArtworkRevision: "dlv152-missing-art-revision",
                SourceAttribution: "DLV-152 Fixture")
            {
                SourceIdentity = "dlv152-fixture",
            },
            Games[0],
        ];

        private IReadOnlyList<AppLibraryBackendItemSummary> CurrentGames =>
            matrix ? MatrixGames : Games;

        public Task<AppLibraryBackendCursorPage> QueryAppLibraryAsync(
            AppLibraryBackendCursorRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(request);
            var admitted = request.Cursor is null &&
                request.Query.Kind is null or AppLibraryKind.Game;
            IReadOnlyList<AppLibraryBackendItemSummary> items = admitted
                ? CurrentGames.Where(game =>
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
                CurrentGames.Any(game => game.ProviderAppId == appId) &&
                    appId != "dlv152-missing-art" ? ArtworkPngBase64 : null));
        }

        public Task LaunchAppLibraryItemAsync(
            string appId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CurrentGames.Any(game => game.ProviderAppId == appId))
                throw new BrokerException("app_not_found", "Seeded game was not found.");
            return Task.CompletedTask;
        }
    }
}
