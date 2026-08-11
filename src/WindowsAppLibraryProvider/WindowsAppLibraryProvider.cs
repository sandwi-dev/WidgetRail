using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsAppLibraryProvider;

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
    internal const int MaximumApps = 512;
    internal const int MaximumDisplayNameLength = 120;
    private static readonly TimeSpan TerminalDrainDeadline = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IGameLibrarySource> _sources;
    private readonly IReadOnlyDictionary<string, IGameLibrarySource> _sourcesByIdentity;
    private readonly IShellStaExecutor _shellSta;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _opaqueIdsByIdentity =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<WindowsAppLibraryItem>? _snapshot;
    private Dictionary<string, GameLibrarySourceItem> _registrationsByOpaqueId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _iconsByRevalidationKey =
        new(StringComparer.Ordinal);
    private TaskCompletionSource? _terminalCompletion;

    public WindowsAppLibraryProvider() : this(
        new WindowsStartMenuApplicationSource(),
        new WindowsAppsFolderApplicationSource(),
        new WindowsSteamApplicationSource(),
        new WindowsShellLauncher(),
        new WindowsPackagedAppLauncher(),
        new WindowsSteamLauncher(),
        new WindowsAppIconSource(),
        ShellStaExecutor.Shared)
    {
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
        IShellStaExecutor shellSta)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0 || sources.Count > 16 ||
            sources.Any(source => source is null) ||
            sources.Select(source => source.SourceIdentity)
                .Distinct(StringComparer.Ordinal).Count() != sources.Count)
            throw new ArgumentException("Game-library sources are invalid.", nameof(sources));
        _sources = Array.AsReadOnly(sources.ToArray());
        _sourcesByIdentity = _sources.ToDictionary(
            source => source.SourceIdentity, StringComparer.Ordinal);
        _shellSta = shellSta ?? throw new ArgumentNullException(nameof(shellSta));
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

    public async Task<IReadOnlyList<AppLibraryBackendItemSummary>> GetAppLibraryAsync(
        CancellationToken cancellationToken)
    {
        await GetAppsAsync(cancellationToken).ConfigureAwait(false);
        return ProjectCurrentForBroker();
    }

    public async Task<IReadOnlyList<AppLibraryBackendItemSummary>> RefreshAppLibraryAsync(
        CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return ProjectCurrentForBroker();
    }

    private IReadOnlyList<AppLibraryBackendItemSummary> ProjectCurrentForBroker()
    {
        ThrowIfTerminating();
        lock (_stateGate)
        {
            if (_snapshot is null)
                throw new BrokerException(
                    "invalid_backend_data", "App library snapshot is unavailable.");
            var projected = new AppLibraryBackendItemSummary[_snapshot.Count];
            for (var index = 0; index < _snapshot.Count; index++)
            {
                var app = _snapshot[index];
                if (!_registrationsByOpaqueId.TryGetValue(app.AppId, out var registration))
                    throw new BrokerException(
                        "invalid_backend_data", "App library snapshot is inconsistent.");
                projected[index] = new AppLibraryBackendItemSummary(
                    app.AppId,
                    registration.StableIdentity,
                    app.DisplayName,
                    registration.Kind switch
                {
                    WindowsAppLibraryKind.Unknown => AppLibraryKind.Unknown,
                    WindowsAppLibraryKind.Application => AppLibraryKind.Application,
                    WindowsAppLibraryKind.Game => AppLibraryKind.Game,
                    _ => AppLibraryKind.Unknown,
                }, ArtworkRevision(registration.ArtworkRevision));
            }
            return Array.AsReadOnly(projected);
        }
    }

    private static string ArtworkRevision(string revalidationKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revalidationKey)));

    public async Task LaunchAppLibraryItemAsync(
        string appId, CancellationToken cancellationToken)
    {
        ThrowIfTerminating();
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > 128)
            throw AppUnavailable();

        using var operation = await EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GameLibrarySourceItem? registered;
            lock (_stateGate)
                _registrationsByOpaqueId.TryGetValue(appId, out registered);
            if (registered is null || !IsStructurallyValid(registered) ||
                !_sourcesByIdentity.TryGetValue(
                    registered.SourceIdentity, out var source))
                throw AppUnavailable();

            await _shellSta.RunAsync(
                token =>
                {
                    var exact = source.ResolveExact(registered, token);
                    token.ThrowIfCancellationRequested();
                    if (exact is null || !IsStructurallyValid(exact)) throw AppUnavailable();
                    source.Launch(exact, token);
                    return true;
                }, operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
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

        using var operation = await EnterOperationAsync(cancellationToken).ConfigureAwait(false);
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
                if (_iconsByRevalidationKey.TryGetValue(
                        registration.ArtworkRevision, out var cached))
                    return new AppLibraryIconSummary(cached);
            }

            var png = await _shellSta.RunAsync(
                token =>
                {
                    var exact = source.ResolveExact(registration, token);
                    return exact is null ? null : source.LoadArtwork(exact, token);
                },
                operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            if (png is not null)
            {
                lock (_stateGate)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    _iconsByRevalidationKey[registration.ArtworkRevision] = png;
                }
            }
            return new AppLibraryIconSummary(png);
        }
        finally
        {
            operation.Release(_scanGate);
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
            return Commit(registrations, clearIcons: force);
        }
        finally
        {
            operation.Release(_scanGate);
        }
    }

    private IReadOnlyList<WindowsAppLibraryItem> Commit(
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
            return _snapshot;
        }
    }

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
        registration.SupportedActions.HasFlag(GameLibrarySourceActions.Launch) &&
        (registration.SupportedActions &
            ~(GameLibrarySourceActions.Launch | GameLibrarySourceActions.Artwork)) == 0 &&
        !string.IsNullOrWhiteSpace(registration.SourceItemIdentity) &&
        registration.SourceItemIdentity.Length <= 96;

    private static BrokerException AppUnavailable() =>
        new("app_not_found", "The selected app is no longer available.");

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
        try
        {
            var disposals = _sources
                .Select(source => Task.Run(() => DisposeSource(source)))
                .ToArray();

            try
            {
                await _scanGate.WaitAsync().WaitAsync(TerminalDrainDeadline)
                    .ConfigureAwait(false);
                gateHeld = true;
            }
            catch (TimeoutException exception)
            {
                failures.Add(new InvalidOperationException(
                    "Game-library provider work did not drain within its bounded deadline.",
                    exception));
            }

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
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            if (gateHeld) _scanGate.Release();
            _lifetime.Dispose();
        }

        if (failures.Count == 0)
            completion.TrySetResult();
        else
            completion.TrySetException(new AggregateException(
                "One or more game-library sources failed bounded terminal cleanup.",
                failures));
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
}
