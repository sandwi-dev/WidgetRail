using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsAppLibraryProvider;

/// <summary>
/// Lazily builds one bounded catalog from Start Menu shortcuts, AppsFolder,
/// and reviewed launcher registrations.
/// Launch resolves a provider-owned opaque ID and exactly revalidates its
/// trusted registration before invoking the corresponding Windows launcher.
/// </summary>
public sealed class WindowsAppLibraryProvider :
    IAppLibraryPlatformBrokerBackend,
    IAsyncDisposable
{
    internal const int MaximumApps = 10_000;
    internal const int MaximumDisplayNameLength = 120;
    private static readonly TimeSpan TerminalDrainDeadline = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IGameLibrarySource> _sources;
    private readonly IReadOnlyDictionary<string, IGameLibrarySource> _sourcesByIdentity;
    private readonly IShellStaExecutor _shellSta;
    private readonly IWindowsRunningAppObserver _runningApps;
    private readonly IWindowsPortableAppStore _portableStore;
    private readonly IWindowsExecutableAuthorityReader _executableAuthority;
    private readonly IWindowsPortableAppLauncher _portableLauncher;
    private readonly TimeSpan _terminalDrainDeadline;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly SemaphoreSlim _artworkGate = new(4, 4);
    private readonly SemaphoreSlim _observationGate = new(1, 1);
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly byte[] _cursorKey = RandomNumberGenerator.GetBytes(32);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _opaqueIdsByIdentity =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<WindowsAppLibraryItem>? _snapshot;
    private IReadOnlyList<AppLibrarySourceSummary> _sourceObservations = [];
    private Dictionary<string, GameLibrarySourceItem> _registrationsByOpaqueId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _portableIdsByAuthority =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PortableLaunchRegistration>
        _portableLaunchByOpaqueId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _iconsByRevalidationKey =
        new(StringComparer.Ordinal);
    private TaskCompletionSource? _terminalCompletion;
    private long _catalogRevision;

    public WindowsAppLibraryProvider() : this(
        AutomaticInstalledSourceDiscovery,
        AutomaticInstalledSourceDiscovery)
    {
    }

    internal static bool AutomaticInstalledSourceDiscovery(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    internal WindowsAppLibraryProvider(
        Func<CancellationToken, bool> epicEnabled,
        Func<CancellationToken, bool> gogEnabled) : this(
        CreateDefaultSources(epicEnabled, gogEnabled), ShellStaExecutor.Shared)
    {
    }

    private static IReadOnlyList<IGameLibrarySource> CreateDefaultSources(
        Func<CancellationToken, bool> epicEnabled,
        Func<CancellationToken, bool> gogEnabled)
    {
        ArgumentNullException.ThrowIfNull(epicEnabled);
        ArgumentNullException.ThrowIfNull(gogEnabled);
        var packagedLauncher = new WindowsPackagedAppLauncher();
        var iconSource = new WindowsAppIconSource();
        return
        [
            new WindowsInstalledGameLibrarySource(
                new WindowsStartMenuApplicationSource(),
                new WindowsAppsFolderApplicationSource(),
                new WindowsShellLauncher(),
                packagedLauncher,
                iconSource),
            new WindowsPackageGameLibrarySource(
                new WindowsPackageGameApplicationSource(),
                packagedLauncher,
                iconSource),
            new SteamGameLibrarySource(
                new WindowsSteamApplicationSource(),
                new WindowsSteamLauncher()),
            new EpicGameLibrarySource(
                new EpicInstalledGameApplicationSource(
                    EpicInstalledGameApplicationSource.DefaultManifestRoot,
                    epicEnabled),
                new WindowsEpicLauncher()),
            new GogGameLibrarySource(
                new GogInstalledGameApplicationSource(
                    new WindowsGogRegistryReader(),
                    gogEnabled)),
        ];
    }

    internal WindowsAppLibraryProvider(IStartMenuApplicationSource source) :
        this(source, EmptyAppsFolderApplicationSource.Instance,
            EmptySteamApplicationSource.Instance, new WindowsShellLauncher(),
            new WindowsPackagedAppLauncher(), new WindowsSteamLauncher(),
            new WindowsAppIconSource(), ShellStaExecutor.Shared)
    {
    }

    internal WindowsAppLibraryProvider(
        IStartMenuApplicationSource source,
        IWindowsShellLauncher shellLauncher) :
        this(source, EmptyAppsFolderApplicationSource.Instance,
            EmptySteamApplicationSource.Instance, shellLauncher,
            new WindowsPackagedAppLauncher(), new WindowsSteamLauncher(),
            new WindowsAppIconSource(), ShellStaExecutor.Shared)
    {
    }

    internal WindowsAppLibraryProvider(
        IStartMenuApplicationSource source,
        IWindowsShellLauncher shellLauncher,
        IWindowsAppIconSource iconSource) :
        this(source, EmptyAppsFolderApplicationSource.Instance,
            EmptySteamApplicationSource.Instance, shellLauncher,
            new WindowsPackagedAppLauncher(), new WindowsSteamLauncher(),
            iconSource, ShellStaExecutor.Shared)
    {
    }

    internal WindowsAppLibraryProvider(
        IStartMenuApplicationSource startMenuSource,
        IAppsFolderApplicationSource appsFolderSource,
        IWindowsShellLauncher shellLauncher,
        IWindowsPackagedAppLauncher packagedAppLauncher,
        IWindowsAppIconSource iconSource,
        IShellStaExecutor shellSta)
        : this(startMenuSource, appsFolderSource, EmptySteamApplicationSource.Instance,
            shellLauncher, packagedAppLauncher, new WindowsSteamLauncher(), iconSource, shellSta)
    {
    }

    internal WindowsAppLibraryProvider(
        IStartMenuApplicationSource startMenuSource,
        IAppsFolderApplicationSource appsFolderSource,
        ISteamApplicationSource steamSource,
        IWindowsShellLauncher shellLauncher,
        IWindowsPackagedAppLauncher packagedAppLauncher,
        IWindowsSteamLauncher steamLauncher,
        IWindowsAppIconSource iconSource,
        IShellStaExecutor shellSta)
        : this(
            [
                new WindowsInstalledGameLibrarySource(
                    startMenuSource, appsFolderSource, shellLauncher,
                    packagedAppLauncher, iconSource),
                new SteamGameLibrarySource(steamSource, steamLauncher),
            ],
            shellSta)
    {
    }

    internal WindowsAppLibraryProvider(
        IReadOnlyList<IGameLibrarySource> sources,
        IShellStaExecutor shellSta) : this(
            sources, shellSta, new WindowsRunningAppObserver(), TerminalDrainDeadline)
    {
    }

    internal WindowsAppLibraryProvider(
        IReadOnlyList<IGameLibrarySource> sources,
        IShellStaExecutor shellSta,
        TimeSpan terminalDrainDeadline) : this(
            sources, shellSta, new WindowsRunningAppObserver(), terminalDrainDeadline)
    {
    }

    internal WindowsAppLibraryProvider(
        IReadOnlyList<IGameLibrarySource> sources,
        IShellStaExecutor shellSta,
        IWindowsRunningAppObserver runningApps) : this(
            sources, shellSta, runningApps, TerminalDrainDeadline)
    {
    }

    internal WindowsAppLibraryProvider(
        IReadOnlyList<IGameLibrarySource> sources,
        IShellStaExecutor shellSta,
        IWindowsRunningAppObserver runningApps,
        TimeSpan terminalDrainDeadline) : this(
            sources, shellSta, runningApps,
            new WindowsPortableAppStore(DefaultPortableStoreRoot()),
            new WindowsExecutableAuthorityReader(),
            new WindowsPortableAppLauncher(), terminalDrainDeadline)
    {
    }

    internal WindowsAppLibraryProvider(
        IReadOnlyList<IGameLibrarySource> sources,
        IShellStaExecutor shellSta,
        IWindowsRunningAppObserver runningApps,
        IWindowsPortableAppStore portableStore,
        IWindowsExecutableAuthorityReader executableAuthority,
        IWindowsPortableAppLauncher portableLauncher,
        TimeSpan terminalDrainDeadline)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0 || sources.Count > 16 ||
            sources.Any(source => source is null) ||
            sources.Any(source =>
                string.IsNullOrWhiteSpace(source.SourceIdentity) ||
                source.SourceIdentity.Length > 64 ||
                string.IsNullOrWhiteSpace(source.Attribution) ||
                source.Attribution.Length > 64 ||
                SanitizeDisplayName(source.Attribution) != source.Attribution) ||
            sources.Select(source => source.SourceIdentity)
                .Distinct(StringComparer.Ordinal).Count() != sources.Count)
            throw new ArgumentException("Game-library sources are invalid.", nameof(sources));
        _sources = Array.AsReadOnly(sources.ToArray());
        _sourcesByIdentity = _sources.ToDictionary(
            source => source.SourceIdentity, StringComparer.Ordinal);
        _shellSta = shellSta ?? throw new ArgumentNullException(nameof(shellSta));
        _runningApps = runningApps ?? throw new ArgumentNullException(nameof(runningApps));
        _portableStore = portableStore ?? throw new ArgumentNullException(nameof(portableStore));
        _executableAuthority = executableAuthority ??
            throw new ArgumentNullException(nameof(executableAuthority));
        _portableLauncher = portableLauncher ??
            throw new ArgumentNullException(nameof(portableLauncher));
        if (terminalDrainDeadline <= TimeSpan.Zero ||
            terminalDrainDeadline > TerminalDrainDeadline)
            throw new ArgumentOutOfRangeException(nameof(terminalDrainDeadline));
        _terminalDrainDeadline = terminalDrainDeadline;
    }

    private static string DefaultPortableStoreRoot()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            throw new BrokerException(
                "platform_unavailable", "Portable app registration storage is unavailable.");
        return Path.Combine(localData, "WidgetRail", "broker", "portable-apps");
    }

    public async Task<RunningAppBackendObservationPage> ObserveRunningAppsAsync(
        CancellationToken cancellationToken)
    {
        await GetAppsAsync(cancellationToken).ConfigureAwait(false);
        using var operation = await EnterObservationOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await _shellSta.RunAsync(ObserveRunningCore, operation.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            operation.Release(_observationGate);
        }
    }

    private RunningAppBackendObservationPage ObserveRunningCore(
        CancellationToken cancellationToken)
    {
        long catalogRevision;
        GameLibrarySourceItem[] registrations;
        lock (_stateGate)
        {
            catalogRevision = _catalogRevision;
            registrations = _registrationsByOpaqueId.Values.ToArray();
        }
        var observedWindows = _runningApps.Observe(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return BuildRunningPage(
            catalogRevision, registrations, observedWindows, cancellationToken);
    }

    private RunningAppBackendObservationPage BuildRunningPage(
        long catalogRevision,
        IReadOnlyList<GameLibrarySourceItem> registrations,
        IReadOnlyList<WindowsRunningAppObservation> observedWindows,
        CancellationToken cancellationToken)
    {
        var byIdentity = registrations
            .GroupBy(item => item.StableIdentity, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(),
                StringComparer.OrdinalIgnoreCase);
        var observations = observedWindows
            .GroupBy(item => item.RegistrationIdentity, StringComparer.OrdinalIgnoreCase)
            .Where(group => byIdentity.ContainsKey(group.Key) ||
                group.Any(item => item.PortableAuthority is not null))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToArray();
        var result = new List<RunningAppBackendObservation>(observations.Length);
        var revisionEvidence = new StringBuilder();
        foreach (var group in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instances = group.Select(item => item.InstanceEvidence)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (instances.Length == 0) continue;
            if (byIdentity.TryGetValue(group.Key, out var expected))
            {
                var source = _sourcesByIdentity[expected.SourceIdentity];
                var exact = source.ResolveExact(expected, cancellationToken);
                if (exact is null || !string.Equals(exact.StableIdentity,
                        expected.StableIdentity, StringComparison.OrdinalIgnoreCase))
                    continue;
                var displayName = SanitizeDisplayName(exact.DisplayName);
                if (displayName is null) continue;
                revisionEvidence.Append(exact.StableIdentity).Append('\0')
                    .AppendJoin(',', instances).Append('\0');
                result.Add(new RunningAppBackendObservation(
                    exact.StableIdentity, instances[0],
                    displayName, ToBrokerKind(exact.Kind),
                    exact.Attribution));
                continue;
            }

            var portable = group
                .Where(item => item.PortableAuthority is not null)
                .OrderBy(item => item.InstanceEvidence, StringComparer.Ordinal)
                .FirstOrDefault();
            var portableDisplayName = SanitizeDisplayName(portable?.DisplayName);
            if (portable?.PortableAuthority is null || portableDisplayName is null)
                continue;
            revisionEvidence.Append(portable.RegistrationIdentity).Append('\0')
                .AppendJoin(',', instances).Append('\0');
            result.Add(new RunningAppBackendObservation(
                portable.RegistrationIdentity,
                portable.InstanceEvidence,
                portableDisplayName,
                AppLibraryKind.Application,
                "Portable"));
        }
        lock (_stateGate)
        {
            if (_catalogRevision != catalogRevision)
                throw new BrokerException("stale_observation", "The app library changed during observation.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var revision = "running-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{catalogRevision:X16}\0{revisionEvidence}")));
        return new RunningAppBackendObservationPage(result.AsReadOnly(), revision);
    }

    /// <summary>Returns the cached immutable snapshot, scanning lazily on first use.</summary>
    public async Task<IReadOnlyList<WindowsAppLibraryItem>> GetAppsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfTerminating();
        lock (_stateGate)
        {
            if (_snapshot is not null) return _snapshot;
        }
        return await ScanAsync(force: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-enumerates registered Start Menu applications.</summary>
    public Task<IReadOnlyList<WindowsAppLibraryItem>> RefreshAsync(
        CancellationToken cancellationToken = default) =>
        ScanAsync(force: true, cancellationToken);

    public async Task<AppLibraryBackendCursorPage> QueryAppLibraryAsync(
        AppLibraryBackendCursorRequest request,
        CancellationToken cancellationToken)
    {
        ValidateQuery(request);
        if (request.Refresh)
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        else
            await GetAppsAsync(cancellationToken).ConfigureAwait(false);

        ThrowIfTerminating();
        lock (_stateGate)
        {
            if (_snapshot is null)
                throw new BrokerException(
                    "invalid_backend_data", "App library snapshot is unavailable.");
            var filtered = _snapshot
                .Select(app => (registration: _registrationsByOpaqueId.TryGetValue(
                    app.AppId, out var registration) ? registration : null, app))
                .Where(entry => entry.registration is not null)
                .Where(entry => request.Query.Kind is null ||
                    ToBrokerKind(entry.registration!.Kind) == request.Query.Kind)
                .Where(entry => request.Query.SourceAttribution is null ||
                    string.Equals(entry.registration!.Attribution,
                        request.Query.SourceAttribution, StringComparison.Ordinal))
                .Where(entry => request.Query.SearchText is null ||
                    entry.app.DisplayName.Contains(
                        request.Query.SearchText, StringComparison.OrdinalIgnoreCase))
                .Where(entry => request.Query.StableIdentityFilter is null ||
                    request.Query.StableIdentityFilter.Contains(
                        entry.registration!.StableIdentity, StringComparer.Ordinal))
                .ToArray();
            filtered = request.Query.Sort switch
            {
                AppLibrarySortOrder.DisplayNameDescending => filtered
                    .OrderByDescending(entry => entry.app.DisplayName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.registration!.StableIdentity,
                        StringComparer.Ordinal)
                    .ToArray(),
                AppLibrarySortOrder.SourceThenDisplayName => filtered
                    .OrderBy(entry => entry.registration!.Attribution,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.app.DisplayName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.registration!.StableIdentity,
                        StringComparer.Ordinal)
                    .ToArray(),
                _ => filtered,
            };
            var queryHash = QueryHash(request.Query, request.Limit);
            var offset = request.Cursor is null ? 0 :
                ParseCursor(request.Cursor, request.Direction!.Value,
                    _catalogRevision, queryHash);
            if (offset < 0 || offset > filtered.Length)
                throw InvalidCursor();
            var page = filtered.Skip(offset).Take(request.Limit).ToArray();
            var projected = page.Select(entry => new AppLibraryBackendItemSummary(
                entry.app.AppId,
                entry.registration!.StableIdentity,
                entry.app.DisplayName,
                ToBrokerKind(entry.registration.Kind),
                ArtworkRevision(entry.registration.ArtworkRevision),
                entry.registration.Attribution)
            {
                SourceIdentity = entry.registration.SourceIdentity,
                IsLaunchable = entry.registration.SupportedActions.HasFlag(
                    GameLibrarySourceActions.Launch),
            }).ToArray();
            var before = offset > 0
                ? CreateCursor(Math.Max(0, offset - request.Limit),
                    AppLibraryCursorDirection.Before, _catalogRevision, queryHash)
                : null;
            var consumed = offset + projected.Length;
            var after = consumed < filtered.Length
                ? CreateCursor(consumed, AppLibraryCursorDirection.After,
                    _catalogRevision, queryHash)
                : null;
            return new AppLibraryBackendCursorPage(
                projected, before, after, $"library-{_catalogRevision:X16}")
            {
                Sources = _sourceObservations,
            };
        }
    }

    private static AppLibraryKind ToBrokerKind(WindowsAppLibraryKind kind) => kind switch
    {
        WindowsAppLibraryKind.Application => AppLibraryKind.Application,
        WindowsAppLibraryKind.Game => AppLibraryKind.Game,
        _ => AppLibraryKind.Unknown,
    };

    private static void ValidateQuery(AppLibraryBackendCursorRequest? request)
    {
        if (request is null || request.Query is null ||
            !Enum.IsDefined(request.Query.Sort) ||
            request.Query.Kind is { } kind && !Enum.IsDefined(kind) ||
            request.Limit is < 1 or > 64 ||
            (request.Cursor is null) != (request.Direction is null) ||
            request.Cursor is { Length: > 128 } ||
            request.Query.SourceAttribution is { } source &&
                (string.IsNullOrWhiteSpace(source) || source.Length > 64 ||
                    source.Any(char.IsControl)) ||
            request.Query.SearchText is { } search &&
                (string.IsNullOrWhiteSpace(search) || search.Length > 96 ||
                 search.Any(char.IsControl) || search != NormalizeSearchText(search)) ||
            request.Query.StableIdentityFilter is { Count: > 128 } ||
            (request.Query.StableIdentityFilter is { } stableIdentityFilter &&
             (stableIdentityFilter.Distinct(StringComparer.Ordinal).Count() !=
                  stableIdentityFilter.Count ||
              stableIdentityFilter.Any(identity =>
                  string.IsNullOrWhiteSpace(identity) || identity.Length > 128))))
            throw new BrokerException("invalid_payload", "App-library query is invalid.");
    }

    private static string QueryHash(AppLibraryBackendQuery query, int limit)
    {
        var stableIdentityFilter = query.StableIdentityFilter is null
            ? "N"
            : "F" + string.Concat(query.StableIdentityFilter.Select(identity =>
                $"{identity.Length}:{identity}"));
        var value = string.Join('\u001f', query.InstalledOnly, query.Kind,
            query.SourceAttribution ?? string.Empty, query.SearchText ?? string.Empty,
            query.Sort, stableIdentityFilter, limit);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    }

    private static string? NormalizeSearchText(string? value) => value is null
        ? null
        : string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private string CreateCursor(int offset, AppLibraryCursorDirection direction,
        long revision, string queryHash)
    {
        var payload = $"{revision:X16}.{queryHash}.{(direction == AppLibraryCursorDirection.Before ? 'B' : 'A')}.{offset:X8}";
        using var hmac = new HMACSHA256(_cursorKey);
        return $"library.cursor.{payload}.{Convert.ToHexString(hmac.ComputeHash(Encoding.ASCII.GetBytes(payload)))}";
    }

    private int ParseCursor(string cursor, AppLibraryCursorDirection direction,
        long revision, string queryHash)
    {
        var parts = cursor.Split('.', StringSplitOptions.None);
        if (parts.Length != 7 || parts[0] != "library" || parts[1] != "cursor" ||
            parts[2] != $"{revision:X16}" || parts[3] != queryHash ||
            parts[4] != (direction == AppLibraryCursorDirection.Before ? "B" : "A") ||
            parts[5].Length != 8 || parts[6].Length != 64)
            throw InvalidCursor();
        var payload = string.Join('.', parts[2], parts[3], parts[4], parts[5]);
        using var hmac = new HMACSHA256(_cursorKey);
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.ASCII.GetBytes(payload)));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[6])) ||
            !int.TryParse(parts[5], System.Globalization.NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var offset))
            throw InvalidCursor();
        return offset;
    }

    private static BrokerException InvalidCursor() =>
        new("invalid_cursor", "The app-library cursor is invalid or stale.");

    private static string ArtworkRevision(string revalidationKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revalidationKey)));

    public async Task<RegisterRunningAppBackendSummary> RegisterRunningAppAsync(
        BrokerWidgetIdentity identity,
        RegisterRunningAppBackendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(request);
        identity.Validate();
        WindowsPortableAppStore.ValidateStableIdentity(
            request.StableProviderIdentity, "invalid_payload");
        await GetAppsAsync(cancellationToken).ConfigureAwait(false);
        using var operation = await EnterObservationOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var candidate = await _shellSta.RunAsync(
                token => ResolveRunningRegistrationCandidate(request, token),
                operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            if (candidate.InstalledItem is not null)
                return new RegisterRunningAppBackendSummary(
                    candidate.InstalledItem, true);

            var mutation = await _portableStore.UpsertAsync(
                identity, candidate.Portable!, operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            return new RegisterRunningAppBackendSummary(
                PortableBackendItem(
                    GetPortableAppId(identity, candidate.Portable!),
                    candidate.Portable!),
                AlreadyRegistered: !mutation.Changed);
        }
        finally
        {
            operation.Release(_observationGate);
        }
    }

    public async Task<IReadOnlyList<AppLibraryBackendItemSummary>>
        ResolveRegisteredRunningAppsAsync(
            BrokerWidgetIdentity identity,
            IReadOnlyList<string> savedIds,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(savedIds);
        identity.Validate();
        if (savedIds.Count > WindowsPortableAppStore.MaximumRegistrations ||
            savedIds.Distinct(StringComparer.Ordinal).Count() != savedIds.Count)
            throw new BrokerException(
                "invalid_payload", "Saved app identifiers are invalid.");
        await GetAppsAsync(cancellationToken).ConfigureAwait(false);
        using var operation = await EnterObservationOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var snapshot = await _portableStore.ReadAsync(identity, operation.Token)
                .ConfigureAwait(false);
            var bySavedId = snapshot.Items.ToDictionary(
                item => item.SavedId, StringComparer.Ordinal);
            var result = new List<AppLibraryBackendItemSummary>(savedIds.Count);
            foreach (var savedId in savedIds)
            {
                operation.Token.ThrowIfCancellationRequested();
                if (!bySavedId.TryGetValue(savedId, out var registration)) continue;
                if (TryInstalledBackendItem(registration.StableIdentity) is { } installed)
                {
                    result.Add(installed);
                    continue;
                }
                var current = _executableAuthority.ReadExact(
                    registration.ExecutablePath);
                if (!SameAuthority(registration, current)) continue;
                result.Add(PortableBackendItem(
                    GetPortableAppId(identity, registration), registration));
            }
            return result.AsReadOnly();
        }
        finally
        {
            operation.Release(_observationGate);
        }
    }

    public async Task ForgetRunningAppAsync(
        BrokerWidgetIdentity identity,
        string savedId,
        CancellationToken cancellationToken)
    {
        using var operation = await EnterObservationOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var mutation = await _portableStore.RemoveAsync(
                identity, savedId, operation.Token).ConfigureAwait(false);
            if (!mutation.Changed) return;
            lock (_stateGate)
            {
                var authorityPrefix = PortableAuthorityKey(identity, string.Empty);
                foreach (var key in _portableIdsByAuthority.Keys
                             .Where(key => key.StartsWith(
                                 authorityPrefix, StringComparison.OrdinalIgnoreCase))
                             .ToArray())
                {
                    var appId = _portableIdsByAuthority[key];
                    if (_portableLaunchByOpaqueId.TryGetValue(appId, out var launch) &&
                        string.Equals(launch.SavedId, savedId, StringComparison.Ordinal))
                    {
                        _portableIdsByAuthority.Remove(key);
                        _portableLaunchByOpaqueId.Remove(appId);
                    }
                }
            }
        }
        finally
        {
            operation.Release(_observationGate);
        }
    }

    public async Task<AppLibraryRegistrationStateSummary>
        GetRunningAppRegistrationStateAsync(
            BrokerWidgetIdentity identity,
            CancellationToken cancellationToken)
    {
        var snapshot = await _portableStore.ReadAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        return new(snapshot.Items.Count != 0, snapshot.Revision);
    }

    public async Task ClearRunningAppRegistrationsAsync(
        BrokerWidgetIdentity identity,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        await _portableStore.ClearAsync(identity, expectedRevision, cancellationToken)
            .ConfigureAwait(false);
        lock (_stateGate)
        {
            var authorityPrefix = PortableAuthorityKey(identity, string.Empty);
            foreach (var key in _portableIdsByAuthority.Keys
                         .Where(key => key.StartsWith(
                             authorityPrefix, StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                var appId = _portableIdsByAuthority[key];
                _portableIdsByAuthority.Remove(key);
                _portableLaunchByOpaqueId.Remove(appId);
            }
        }
    }

    public async Task<AppLibraryPackageRegistrationRetirementSummary>
        RetireRunningAppPackageRegistrationsAsync(
            string packageId,
            CancellationToken cancellationToken)
    {
        var result = await _portableStore.RetirePackageAsync(
            packageId, cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
        {
            foreach (var key in _portableIdsByAuthority.Keys
                         .Where(key => key.Contains(
                             "\0" + packageId + "\0", StringComparison.Ordinal))
                         .ToArray())
            {
                var appId = _portableIdsByAuthority[key];
                _portableIdsByAuthority.Remove(key);
                _portableLaunchByOpaqueId.Remove(appId);
            }
        }
        return new(result.Committed, result.CleanupPending);
    }

    private RunningRegistrationCandidate ResolveRunningRegistrationCandidate(
        RegisterRunningAppBackendRequest request,
        CancellationToken cancellationToken)
    {
        long catalogRevision;
        KeyValuePair<string, GameLibrarySourceItem>[] registrations;
        lock (_stateGate)
        {
            catalogRevision = _catalogRevision;
            registrations = _registrationsByOpaqueId.ToArray();
        }
        var observedWindows = _runningApps.Observe(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var page = BuildRunningPage(
            catalogRevision, registrations.Select(entry => entry.Value).ToArray(),
            observedWindows, cancellationToken);
        if (!string.Equals(
                page.Revision, request.ObservationRevision, StringComparison.Ordinal))
            throw new BrokerException(
                "stale_observation", "The running-app observation is stale.");
        var observed = page.Items.SingleOrDefault(item =>
            string.Equals(item.StableProviderIdentity,
                request.StableProviderIdentity, StringComparison.Ordinal) &&
            string.Equals(item.InstanceEvidence,
                request.InstanceEvidence, StringComparison.Ordinal));
        if (observed is null)
            throw new BrokerException(
                "stale_observation", "The running app is no longer current.");

        var installed = registrations.SingleOrDefault(entry => string.Equals(
            entry.Value.StableIdentity, request.StableProviderIdentity,
            StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(installed.Key))
            return new(BackendItem(installed.Key, installed.Value), null);

        var raw = observedWindows.SingleOrDefault(item =>
            string.Equals(item.RegistrationIdentity,
                request.StableProviderIdentity, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.InstanceEvidence,
                request.InstanceEvidence, StringComparison.Ordinal));
        if (raw?.PortableAuthority is null)
            throw new BrokerException(
                "app_not_supported", "This running app cannot be registered.");
        var current = _executableAuthority.ReadExact(raw.PortableAuthority.CanonicalPath);
        if (current is null ||
            !string.Equals(
                WindowsStartMenuApplicationSource.IdentityForExecutable(
                    current.CanonicalPath),
                request.StableProviderIdentity,
                StringComparison.OrdinalIgnoreCase) ||
            current.FileIdentity != raw.PortableAuthority.FileIdentity)
            throw new BrokerException(
                "stale_observation", "The running app executable changed.");
        return new(null, new PortableAppRegistration(
            request.SavedId,
            request.StableProviderIdentity,
            observed.DisplayName,
            current.CanonicalPath,
            current.FileIdentity));
    }

    public async Task LaunchAppLibraryItemAsync(
        BrokerWidgetIdentity identity,
        string appId,
        CancellationToken cancellationToken)
    {
        _ = await LaunchAppLibraryItemObservedAsync(identity, appId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AppLibraryLaunchObservationSummary> LaunchAppLibraryItemObservedAsync(
        BrokerWidgetIdentity identity,
        string appId,
        CancellationToken cancellationToken)
    {
        ThrowIfTerminating();
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > 128)
            throw AppUnavailable();

        using var operation = await EnterOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            PortableLaunchRegistration? portable;
            lock (_stateGate)
                _portableLaunchByOpaqueId.TryGetValue(appId, out portable);
            if (portable is not null)
            {
                if (!string.Equals(
                        portable.AuthorityKey, PortableAuthorityKey(identity, string.Empty),
                        StringComparison.OrdinalIgnoreCase))
                    throw AppUnavailable();
                var snapshot = await _portableStore.ReadAsync(identity, operation.Token)
                    .ConfigureAwait(false);
                var registration = snapshot.Items.SingleOrDefault(item =>
                    string.Equals(item.SavedId, portable.SavedId, StringComparison.Ordinal) &&
                    string.Equals(item.StableIdentity, portable.StableIdentity,
                        StringComparison.OrdinalIgnoreCase));
                var current = registration is null ? null :
                    _executableAuthority.ReadExact(registration.ExecutablePath);
                if (registration is null || !SameAuthority(registration, current))
                    throw AppUnavailable();
                operation.Token.ThrowIfCancellationRequested();
                _portableLauncher.Launch(current!, operation.Token);
                return new AppLibraryLaunchObservationSummary(
                    AppLibraryLaunchObservationState.RequestAccepted, false, false);
            }

            GameLibrarySourceItem? registered;
            lock (_stateGate)
                _registrationsByOpaqueId.TryGetValue(appId, out registered);
            if (registered is null || !IsStructurallyValid(registered) ||
                !registered.SupportedActions.HasFlag(GameLibrarySourceActions.Launch) ||
                !_sourcesByIdentity.TryGetValue(
                    registered.SourceIdentity, out var source))
                throw AppUnavailable();

            var result = await _shellSta.RunAsync(
                token =>
                {
                    var exact = source.ResolveExact(registered, token);
                    token.ThrowIfCancellationRequested();
                    if (exact is null || !IsStructurallyValid(exact)) throw AppUnavailable();
                    return source.Launch(exact, token);
                }, operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            return new AppLibraryLaunchObservationSummary(
                result.State,
                result.SupportedEvidence.HasFlag(GameLibraryLaunchEvidence.Running),
                result.SupportedEvidence.HasFlag(GameLibraryLaunchEvidence.Ended));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokerException)
        {
            throw;
        }
        catch (PlatformNotSupportedException exception)
        {
            throw new BrokerException(
                "platform_unavailable", "Windows Shell app launch is unavailable.", exception);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or System.Security.SecurityException or
            System.ComponentModel.Win32Exception or InvalidOperationException or
            ArgumentException or NotSupportedException or
            System.Runtime.InteropServices.COMException)
        {
            throw new BrokerException(
                "launch_failed", "Windows could not launch this app.", exception);
        }
        finally
        {
            operation.Release(_scanGate);
        }
    }

    public async Task<AppLibraryIconSummary> GetAppLibraryIconAsync(
        string appId, CancellationToken cancellationToken)
    {
        ThrowIfTerminating();
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > 128)
            return new AppLibraryIconSummary(null);

        using var operation = await EnterArtworkOperationAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            GameLibrarySourceItem? registration;
            lock (_stateGate)
                _registrationsByOpaqueId.TryGetValue(appId, out registration);
            if (registration is null ||
                !_sourcesByIdentity.TryGetValue(
                    registration.SourceIdentity, out var source))
                return new AppLibraryIconSummary(null);

            lock (_stateGate)
            {
                if (source.RequiresStaArtwork &&
                    _iconsByRevalidationKey.TryGetValue(
                        registration.ArtworkRevision, out var cached))
                    return new AppLibraryIconSummary(cached);
            }

            string? ResolveArtwork(CancellationToken token)
            {
                var exact = source.ResolveExact(registration, token);
                if (exact is null) return null;
                if (!string.Equals(exact.ArtworkRevision,
                        registration.ArtworkRevision, StringComparison.Ordinal))
                {
                    // Demand is the only artwork-I/O boundary. Let the exact
                    // source observe a changed lazy registration, but never
                    // publish its bytes through the stale generation.
                    _ = source.LoadArtwork(exact, token);
                    return null;
                }
                return source.LoadArtwork(exact, token);
            }

            var png = source.RequiresStaArtwork
                ? await _shellSta.RunAsync(ResolveArtwork, operation.Token)
                    .ConfigureAwait(false)
                : await Task.Run(() => ResolveArtwork(operation.Token), operation.Token)
                    .ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            if (png is not null)
            {
                lock (_stateGate)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    var isCurrent =
                        _registrationsByOpaqueId.TryGetValue(appId, out var current) &&
                        current.SourceIdentity == registration.SourceIdentity &&
                        current.SourceItemIdentity == registration.SourceItemIdentity &&
                        current.ArtworkRevision == registration.ArtworkRevision;
                    if (!isCurrent)
                        png = null;
                    else if (source.RequiresStaArtwork)
                        _iconsByRevalidationKey[registration.ArtworkRevision] = png;
                }
            }
            return new AppLibraryIconSummary(png);
        }
        finally
        {
            operation.Release(_artworkGate);
        }
    }

    private async Task<IReadOnlyList<WindowsAppLibraryItem>> ScanAsync(
        bool force, CancellationToken cancellationToken)
    {
        using var operation = await EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force)
            {
                lock (_stateGate)
                {
                    if (_snapshot is not null) return _snapshot;
                }
            }

            var snapshots = await _shellSta.RunAsync(
                token => _sources.Select(source => source.Refresh(token)).ToArray(),
                operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            var registrations = snapshots
                .SelectMany(snapshot => snapshot.Items)
                .ToArray();
            return Commit(snapshots, registrations, clearIcons: force);
        }
        finally
        {
            operation.Release(_scanGate);
        }
    }

    private IReadOnlyList<WindowsAppLibraryItem> Commit(
        IReadOnlyList<GameLibrarySourceSnapshot> sourceSnapshots,
        IReadOnlyList<GameLibrarySourceItem>? registrations,
        bool clearIcons)
    {
        var candidates = (registrations ?? [])
            .Where(IsStructurallyValid)
            .Select(registration => new Candidate(
                registration,
                SanitizeDisplayName(registration.DisplayName)))
            .Where(candidate => candidate.DisplayName is not null)
            .GroupBy(candidate => candidate.Registration.StableIdentity,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(candidate => candidate.Registration.SourceIdentity,
                    StringComparer.Ordinal)
                .First())
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Registration.StableIdentity,
                StringComparer.OrdinalIgnoreCase)
            .Take(MaximumApps)
            .ToArray();

        lock (_stateGate)
        {
            ThrowIfTerminating();
            if (clearIcons) _iconsByRevalidationKey.Clear();
            var liveIdentities = candidates
                .Select(candidate => candidate.Registration.StableIdentity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _opaqueIdsByIdentity.Keys
                         .Where(identity => !liveIdentities.Contains(identity)).ToArray())
                _opaqueIdsByIdentity.Remove(stale);

            var liveRevalidationKeys = candidates
                .Select(candidate => candidate.Registration.ArtworkRevision)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var stale in _iconsByRevalidationKey.Keys
                         .Where(key => !liveRevalidationKeys.Contains(key)).ToArray())
                _iconsByRevalidationKey.Remove(stale);

            var byId = new Dictionary<string, GameLibrarySourceItem>(StringComparer.Ordinal);
            var snapshot = new WindowsAppLibraryItem[candidates.Length];
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (!_opaqueIdsByIdentity.TryGetValue(
                        candidate.Registration.StableIdentity, out var opaqueId))
                {
                    opaqueId = "app-" + Guid.NewGuid().ToString("N");
                    _opaqueIdsByIdentity.Add(candidate.Registration.StableIdentity, opaqueId);
                }
                byId.Add(opaqueId, candidate.Registration);
                snapshot[index] = new WindowsAppLibraryItem(
                    opaqueId,
                    candidate.DisplayName!,
                    candidate.Registration.Kind);
            }

            _registrationsByOpaqueId = byId;
            _snapshot = Array.AsReadOnly(snapshot);
            _sourceObservations = Array.AsReadOnly(sourceSnapshots
                .Select(SourceObservation)
                .ToArray());
            _catalogRevision++;
            return _snapshot;
        }
    }

    private static AppLibrarySourceSummary SourceObservation(
        GameLibrarySourceSnapshot snapshot) => new(
        "source-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(snapshot.SourceIdentity)).AsSpan(0, 12))
            .ToLowerInvariant(),
        snapshot.Attribution,
        snapshot.Health switch
        {
            GameLibrarySourceHealth.Healthy => AppLibrarySourceHealth.Healthy,
            GameLibrarySourceHealth.Degraded => AppLibrarySourceHealth.Degraded,
            _ => AppLibrarySourceHealth.Unavailable,
        },
        snapshot.SourceVersion,
        snapshot.Health switch
        {
            GameLibrarySourceHealth.Healthy => "healthy",
            GameLibrarySourceHealth.Degraded => "source_degraded",
            GameLibrarySourceHealth.Disabled => "source_disabled",
            _ => "source_unavailable",
        });

    private bool IsStructurallyValid(GameLibrarySourceItem registration) =>
        registration is not null &&
        _sourcesByIdentity.ContainsKey(registration.SourceIdentity) &&
        registration.SourceIdentity.Length is > 0 and <= 64 &&
        !string.IsNullOrWhiteSpace(registration.Attribution) &&
        registration.Attribution.Length <= 64 &&
        SanitizeDisplayName(registration.Attribution) == registration.Attribution &&
        !string.IsNullOrWhiteSpace(registration.StableIdentity) &&
        registration.StableIdentity.Length <= 128 &&
        !string.IsNullOrWhiteSpace(registration.ArtworkRevision) &&
        registration.ArtworkRevision.Length <= 128 &&
        registration.Installed && registration.Available &&
        (registration.SupportedActions &
            ~(GameLibrarySourceActions.Launch | GameLibrarySourceActions.Artwork)) == 0 &&
        !string.IsNullOrWhiteSpace(registration.SourceItemIdentity) &&
        registration.SourceItemIdentity.Length <= 96;

    private static BrokerException AppUnavailable() =>
        new("app_not_found", "The selected app is no longer available.");

    private AppLibraryBackendItemSummary? TryInstalledBackendItem(
        string stableIdentity)
    {
        lock (_stateGate)
        {
            var installed = _registrationsByOpaqueId.SingleOrDefault(entry =>
                string.Equals(entry.Value.StableIdentity, stableIdentity,
                    StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrEmpty(installed.Key)
                ? null
                : BackendItem(installed.Key, installed.Value);
        }
    }

    private static AppLibraryBackendItemSummary BackendItem(
        string appId,
        GameLibrarySourceItem registration) => new(
        appId,
        registration.StableIdentity,
        registration.DisplayName,
        ToBrokerKind(registration.Kind),
        ArtworkRevision(registration.ArtworkRevision),
        registration.Attribution)
    {
        SourceIdentity = registration.SourceIdentity,
        IsLaunchable = registration.SupportedActions.HasFlag(
            GameLibrarySourceActions.Launch),
    };

    private static AppLibraryBackendItemSummary PortableBackendItem(
        string appId,
        PortableAppRegistration registration) => new(
        appId,
        registration.StableIdentity,
        registration.DisplayName,
        AppLibraryKind.Application,
        string.Empty,
        "Portable")
    {
        SourceIdentity = "source-portable",
        IsLaunchable = true,
        AvailabilityState = AppLibraryAvailabilityState.Installed,
        AvailabilityStatusCode = "registered_portable",
        SupportedActions = [AppLibraryAction.Launch],
    };

    private string GetPortableAppId(
        BrokerWidgetIdentity identity,
        PortableAppRegistration registration)
    {
        var key = PortableAuthorityKey(identity, registration.StableIdentity);
        lock (_stateGate)
        {
            if (!_portableIdsByAuthority.TryGetValue(key, out var appId))
            {
                appId = "app-" + Guid.NewGuid().ToString("N");
                _portableIdsByAuthority[key] = appId;
            }
            _portableLaunchByOpaqueId[appId] = new(
                PortableAuthorityKey(identity, string.Empty),
                registration.SavedId,
                registration.StableIdentity);
            return appId;
        }
    }

    private static string PortableAuthorityKey(
        BrokerWidgetIdentity identity,
        string stableIdentity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        return identity.PublisherId + "\0" + identity.PackageId + "\0" + stableIdentity;
    }

    private static bool SameAuthority(
        PortableAppRegistration registration,
        WindowsExecutableAuthority? current) =>
        current is not null &&
        string.Equals(registration.ExecutablePath, current.CanonicalPath,
            StringComparison.OrdinalIgnoreCase) &&
        registration.FileIdentity == current.FileIdentity;

    private async Task<ProviderOperation> EnterOperationAsync(
        CancellationToken cancellationToken)
    {
        CancellationTokenSource linked;
        lock (_lifetimeGate)
        {
            ThrowIfTerminatingLocked();
            linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetime.Token);
        }

        try
        {
            await _scanGate.WaitAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return new ProviderOperation(linked);
        }
        catch
        {
            linked.Dispose();
            throw;
        }
    }

    private async Task<ProviderOperation> EnterArtworkOperationAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfTerminating();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        try
        {
            await _artworkGate.WaitAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return new ProviderOperation(linked);
        }
        catch
        {
            linked.Dispose();
            throw;
        }
    }

    private async Task<ProviderOperation> EnterObservationOperationAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfTerminating();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token);
        try
        {
            await _observationGate.WaitAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return new ProviderOperation(linked);
        }
        catch
        {
            linked.Dispose();
            throw;
        }
    }

    private void ThrowIfTerminating()
    {
        lock (_lifetimeGate) ThrowIfTerminatingLocked();
    }

    private void ThrowIfTerminatingLocked() =>
        ObjectDisposedException.ThrowIf(_terminalCompletion is not null, this);

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        var startsTerminalWork = false;
        lock (_lifetimeGate)
        {
            if (_terminalCompletion is null)
            {
                _terminalCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                startsTerminalWork = true;
            }
            completion = _terminalCompletion;
        }

        if (startsTerminalWork)
        {
            _lifetime.Cancel();
            _ = CompleteDisposalAsync(completion);
        }
        return new ValueTask(completion.Task);
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        var failures = new List<Exception>();
        var gateHeld = false;
        var observationGateHeld = false;
        var artworkPermitsHeld = 0;
        try
        {
            using var deadline = new CancellationTokenSource(_terminalDrainDeadline);
            try
            {
                while (artworkPermitsHeld < 4)
                {
                    await _artworkGate.WaitAsync(deadline.Token).ConfigureAwait(false);
                    artworkPermitsHeld++;
                }
            }
            catch (OperationCanceledException exception)
            {
                failures.Add(new InvalidOperationException(
                    "Game-library artwork work did not drain within its bounded deadline.",
                    exception));
            }

            if (artworkPermitsHeld == 4)
            {
                try
                {
                    await _scanGate.WaitAsync(deadline.Token).ConfigureAwait(false);
                    gateHeld = true;
                    await _observationGate.WaitAsync(deadline.Token).ConfigureAwait(false);
                    observationGateHeld = true;
                }
                catch (OperationCanceledException exception)
                {
                    failures.Add(new InvalidOperationException(
                        "Game-library provider work did not drain within its bounded deadline.",
                        exception));
                }
                if (gateHeld && observationGateHeld)
                {
                    var disposals = _sources
                        .Select(source => Task.Run(() => DisposeSource(source)))
                        .ToArray();
                    var results = await Task.WhenAll(disposals).ConfigureAwait(false);
                    failures.AddRange(results.OfType<Exception>());

                    lock (_stateGate)
                    {
                        _snapshot = null;
                        _registrationsByOpaqueId = new(StringComparer.Ordinal);
                        _opaqueIdsByIdentity.Clear();
                        _iconsByRevalidationKey.Clear();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            if (observationGateHeld) _observationGate.Release();
            if (gateHeld) _scanGate.Release();
            if (artworkPermitsHeld != 0)
                _artworkGate.Release(artworkPermitsHeld);
            _lifetime.Dispose();
        }

        if (failures.Count == 0)
            completion.TrySetResult();
        else
            completion.TrySetException(new AggregateException(
                "One or more game-library sources failed bounded terminal cleanup.",
                failures));
    }

    internal bool HasRetainedCatalogState
    {
        get
        {
            lock (_stateGate)
                return _snapshot is not null && _registrationsByOpaqueId.Count != 0;
        }
    }

    private static Exception? DisposeSource(IGameLibrarySource source)
    {
        try
        {
            source.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class ProviderOperation(CancellationTokenSource cancellation) : IDisposable
    {
        private int _gateHeld = 1;
        internal CancellationToken Token => cancellation.Token;

        internal void Release(SemaphoreSlim gate)
        {
            if (Interlocked.Exchange(ref _gateHeld, 0) != 0) gate.Release();
        }

        public void Dispose() => cancellation.Dispose();
    }

    private sealed class EmptyAppsFolderApplicationSource : IAppsFolderApplicationSource
    {
        internal static EmptyAppsFolderApplicationSource Instance { get; } = new();
        public IReadOnlyList<AppsFolderRegistration> Enumerate(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        public AppsFolderRegistration? ReadExact(
            string aumid,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private sealed class EmptySteamApplicationSource : ISteamApplicationSource
    {
        internal static EmptySteamApplicationSource Instance { get; } = new();

        public IReadOnlyList<SteamRegistration> Enumerate(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        public SteamRegistration? ReadExact(
            string steamAppId,
            string manifestPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    internal static string? SanitizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalizedInput;
        try
        {
            normalizedInput = value.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return null;
        }
        var builder = new StringBuilder(Math.Min(value.Length, MaximumDisplayNameLength));
        var pendingSpace = false;
        foreach (var rune in normalizedInput.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = builder.Length != 0;
                continue;
            }
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or
                UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse)
                continue;
            if (pendingSpace && builder.Length < MaximumDisplayNameLength)
                builder.Append(' ');
            pendingSpace = false;
            if (builder.Length + rune.Utf16SequenceLength > MaximumDisplayNameLength) break;
            builder.Append(rune.ToString());
        }
        var normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private sealed record Candidate(
        GameLibrarySourceItem Registration,
        string? DisplayName);

    private sealed record RunningRegistrationCandidate(
        AppLibraryBackendItemSummary? InstalledItem,
        PortableAppRegistration? Portable);

    private sealed record PortableLaunchRegistration(
        string AuthorityKey,
        string SavedId,
        string StableIdentity);
}
