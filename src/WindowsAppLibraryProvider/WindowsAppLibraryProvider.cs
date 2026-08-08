using System.Text;
using System.Globalization;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Lazily builds one bounded catalog from Start Menu shortcuts and AppsFolder.
/// Launch resolves a provider-owned opaque ID and exactly revalidates its
/// trusted registration before invoking the corresponding Windows launcher.
/// </summary>
public sealed class WindowsAppLibraryProvider : IAppLibraryPlatformBrokerBackend
{
    internal const int MaximumApps = 512;
    internal const int MaximumDisplayNameLength = 120;

    private readonly IStartMenuApplicationSource _startMenuSource;
    private readonly IAppsFolderApplicationSource _appsFolderSource;
    private readonly IWindowsShellLauncher _shellLauncher;
    private readonly IWindowsPackagedAppLauncher _packagedAppLauncher;
    private readonly IWindowsAppIconSource _iconSource;
    private readonly IShellStaExecutor _shellSta;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _opaqueIdsByIdentity =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<WindowsAppLibraryItem>? _snapshot;
    private Dictionary<string, WindowsLaunchRegistration> _registrationsByOpaqueId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _iconsByRevalidationKey =
        new(StringComparer.Ordinal);
    private IReadOnlyList<AppsFolderRegistration> _lastGoodAppsFolderRegistrations = [];

    public WindowsAppLibraryProvider() : this(
        new WindowsStartMenuApplicationSource(),
        new WindowsAppsFolderApplicationSource(),
        new WindowsShellLauncher(),
        new WindowsPackagedAppLauncher(),
        new WindowsAppIconSource(),
        ShellStaExecutor.Shared)
    {
    }

    internal WindowsAppLibraryProvider(IStartMenuApplicationSource source) :
        this(source, EmptyAppsFolderApplicationSource.Instance,
            new WindowsShellLauncher(), new WindowsPackagedAppLauncher(),
            new WindowsAppIconSource(), ShellStaExecutor.Shared)
    {
    }

    internal WindowsAppLibraryProvider(
        IStartMenuApplicationSource source,
        IWindowsShellLauncher shellLauncher) :
        this(source, EmptyAppsFolderApplicationSource.Instance,
            shellLauncher, new WindowsPackagedAppLauncher(),
            new WindowsAppIconSource(), ShellStaExecutor.Shared)
    {
    }

    internal WindowsAppLibraryProvider(
        IStartMenuApplicationSource source,
        IWindowsShellLauncher shellLauncher,
        IWindowsAppIconSource iconSource) :
        this(source, EmptyAppsFolderApplicationSource.Instance,
            shellLauncher, new WindowsPackagedAppLauncher(), iconSource,
            ShellStaExecutor.Shared)
    {
    }

    internal WindowsAppLibraryProvider(
        IStartMenuApplicationSource startMenuSource,
        IAppsFolderApplicationSource appsFolderSource,
        IWindowsShellLauncher shellLauncher,
        IWindowsPackagedAppLauncher packagedAppLauncher,
        IWindowsAppIconSource iconSource,
        IShellStaExecutor shellSta)
    {
        _startMenuSource = startMenuSource ??
            throw new ArgumentNullException(nameof(startMenuSource));
        _appsFolderSource = appsFolderSource ??
            throw new ArgumentNullException(nameof(appsFolderSource));
        _shellLauncher = shellLauncher ?? throw new ArgumentNullException(nameof(shellLauncher));
        _packagedAppLauncher = packagedAppLauncher ??
            throw new ArgumentNullException(nameof(packagedAppLauncher));
        _iconSource = iconSource ?? throw new ArgumentNullException(nameof(iconSource));
        _shellSta = shellSta ?? throw new ArgumentNullException(nameof(shellSta));
    }

    /// <summary>Returns the cached immutable snapshot, scanning lazily on first use.</summary>
    public async Task<IReadOnlyList<WindowsAppLibraryItem>> GetAppsAsync(
        CancellationToken cancellationToken = default)
    {
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
                    registration.IdentityKey,
                    app.DisplayName,
                    app.Kind switch
                {
                    WindowsAppLibraryKind.Unknown => AppLibraryKind.Unknown,
                    WindowsAppLibraryKind.Application => AppLibraryKind.Application,
                    WindowsAppLibraryKind.Game => AppLibraryKind.Game,
                    _ => AppLibraryKind.Unknown,
                });
            }
            return Array.AsReadOnly(projected);
        }
    }

    public async Task LaunchAppLibraryItemAsync(
        string appId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > 128)
            throw AppUnavailable();

        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WindowsLaunchRegistration? registered;
            lock (_stateGate)
                _registrationsByOpaqueId.TryGetValue(appId, out registered);
            if (registered is null || !IsStructurallyValid(registered))
                throw AppUnavailable();

            await _shellSta.RunAsync(
                token =>
                {
                    RevalidateAndLaunch(registered, token);
                    return true;
                }, cancellationToken).ConfigureAwait(false);
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
            _scanGate.Release();
        }
    }

    public async Task<AppLibraryIconSummary> GetAppLibraryIconAsync(
        string appId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > 128)
            return new AppLibraryIconSummary(null);

        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WindowsLaunchRegistration? registration;
            lock (_stateGate)
                _registrationsByOpaqueId.TryGetValue(appId, out registration);
            if (registration is null) return new AppLibraryIconSummary(null);

            lock (_stateGate)
            {
                if (_iconsByRevalidationKey.TryGetValue(
                        registration.RevalidationKey, out var cached))
                    return new AppLibraryIconSummary(cached);
            }

            var png = await _shellSta.RunAsync(
                token => registration switch
                {
                    StartMenuRegistration shortcut =>
                        _iconSource.TryRasterizePngBase64(shortcut.ShortcutPath, token),
                    AppsFolderRegistration packaged =>
                        _iconSource.TryRasterizeAppsFolderPngBase64(packaged.Aumid, token),
                    _ => null,
                }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (png is not null)
            {
                lock (_stateGate)
                    _iconsByRevalidationKey[registration.RevalidationKey] = png;
            }
            return new AppLibraryIconSummary(png);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task<IReadOnlyList<WindowsAppLibraryItem>> ScanAsync(
        bool force, CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force)
            {
                lock (_stateGate)
                {
                    if (_snapshot is not null) return _snapshot;
                }
            }

            var scan = await _shellSta.RunAsync(
                token =>
                {
                    var startMenu = _startMenuSource.Enumerate(token);
                    try
                    {
                        return new AppsFolderScan(
                            startMenu, _appsFolderSource.Enumerate(token), true);
                    }
                    catch (AppsFolderEnumerationException)
                    {
                        return new AppsFolderScan(startMenu, [], false);
                    }
                }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<AppsFolderRegistration> appsFolder;
            if (scan.AppsFolderAuthoritative)
            {
                appsFolder = scan.AppsFolder;
            }
            else
            {
                lock (_stateGate) appsFolder = _lastGoodAppsFolderRegistrations;
            }
            var registrations = scan.StartMenu
                .Cast<WindowsLaunchRegistration>()
                .Concat(appsFolder)
                .ToArray();
            return Commit(
                registrations,
                clearAppsFolderIcons: force,
                scan.AppsFolderAuthoritative ? scan.AppsFolder : null);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private IReadOnlyList<WindowsAppLibraryItem> Commit(
        IReadOnlyList<WindowsLaunchRegistration>? registrations,
        bool clearAppsFolderIcons,
        IReadOnlyList<AppsFolderRegistration>? authoritativeAppsFolder)
    {
        var candidates = (registrations ?? [])
            .Where(IsStructurallyValid)
            .Select(registration => new Candidate(
                registration,
                SanitizeDisplayName(registration.DisplayName)))
            .Where(candidate => candidate.DisplayName is not null)
            .GroupBy(candidate => candidate.Registration.IdentityKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(candidate => SourceOrder(candidate.Registration))
                .ThenBy(candidate => SourceLocator(candidate.Registration),
                    StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Registration.IdentityKey,
                StringComparer.OrdinalIgnoreCase)
            .Take(MaximumApps)
            .ToArray();

        lock (_stateGate)
        {
            if (clearAppsFolderIcons)
            {
                foreach (var packaged in _registrationsByOpaqueId.Values
                             .OfType<AppsFolderRegistration>())
                    _iconsByRevalidationKey.Remove(packaged.RevalidationKey);
            }
            if (authoritativeAppsFolder is not null)
            {
                _lastGoodAppsFolderRegistrations = Array.AsReadOnly(
                    authoritativeAppsFolder.Where(IsStructurallyValid).ToArray());
            }
            var liveIdentities = candidates
                .Select(candidate => candidate.Registration.IdentityKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _opaqueIdsByIdentity.Keys
                         .Where(identity => !liveIdentities.Contains(identity)).ToArray())
                _opaqueIdsByIdentity.Remove(stale);

            var liveRevalidationKeys = candidates
                .Select(candidate => candidate.Registration.RevalidationKey)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var stale in _iconsByRevalidationKey.Keys
                         .Where(key => !liveRevalidationKeys.Contains(key)).ToArray())
                _iconsByRevalidationKey.Remove(stale);

            var byId = new Dictionary<string, WindowsLaunchRegistration>(StringComparer.Ordinal);
            var snapshot = new WindowsAppLibraryItem[candidates.Length];
            for (var index = 0; index < candidates.Length; index++)
            {
                var candidate = candidates[index];
                if (!_opaqueIdsByIdentity.TryGetValue(
                        candidate.Registration.IdentityKey, out var opaqueId))
                {
                    opaqueId = "app-" + Guid.NewGuid().ToString("N");
                    _opaqueIdsByIdentity.Add(candidate.Registration.IdentityKey, opaqueId);
                }
                byId.Add(opaqueId, candidate.Registration);
                snapshot[index] = new WindowsAppLibraryItem(
                    opaqueId,
                    candidate.DisplayName!,
                    WindowsAppLibraryKind.Application);
            }

            _registrationsByOpaqueId = byId;
            _snapshot = Array.AsReadOnly(snapshot);
            return _snapshot;
        }
    }

    internal bool TryResolveForLaunch(
        string opaqueId, out StartMenuRegistration? registration)
    {
        if (string.IsNullOrWhiteSpace(opaqueId))
        {
            registration = null;
            return false;
        }
        lock (_stateGate)
        {
            var found = _registrationsByOpaqueId.TryGetValue(opaqueId, out var candidate) &&
                candidate is StartMenuRegistration;
            registration = candidate as StartMenuRegistration;
            return found;
        }
    }

    internal bool TryResolvePackagedForLaunch(
        string opaqueId, out AppsFolderRegistration? registration)
    {
        if (string.IsNullOrWhiteSpace(opaqueId))
        {
            registration = null;
            return false;
        }
        lock (_stateGate)
        {
            var found = _registrationsByOpaqueId.TryGetValue(opaqueId, out var candidate) &&
                candidate is AppsFolderRegistration;
            registration = candidate as AppsFolderRegistration;
            return found;
        }
    }

    private static bool IsStructurallyValid(WindowsLaunchRegistration registration) =>
        registration is not null &&
        !string.IsNullOrWhiteSpace(registration.IdentityKey) &&
        registration.IdentityKey.Length <= 128 &&
        !string.IsNullOrWhiteSpace(registration.RevalidationKey) &&
        registration.RevalidationKey.Length <= 128 &&
        registration switch
        {
            StartMenuRegistration shortcut =>
                Enum.IsDefined(shortcut.Scope) &&
                !string.IsNullOrWhiteSpace(shortcut.ShortcutPath) &&
                shortcut.ShortcutPath.Length <= 32_767,
            AppsFolderRegistration packaged =>
                WindowsAppsFolderApplicationSource.NormalizeAumid(packaged.Aumid) is { } aumid &&
                string.Equals(aumid, packaged.Aumid, StringComparison.Ordinal),
            _ => false,
        };

    private static int SourceOrder(WindowsLaunchRegistration registration) =>
        registration switch
        {
            StartMenuRegistration { Scope: StartMenuScope.CurrentUser } => 0,
            StartMenuRegistration => 1,
            AppsFolderRegistration => 2,
            _ => int.MaxValue,
        };

    private static string SourceLocator(WindowsLaunchRegistration registration) =>
        registration switch
        {
            StartMenuRegistration shortcut => shortcut.ShortcutPath,
            AppsFolderRegistration packaged => packaged.Aumid,
            _ => string.Empty,
        };

    private static bool IsExactLaunchRegistration(StartMenuRegistration registration)
    {
        try
        {
            return IsStructurallyValid(registration) &&
                Path.IsPathFullyQualified(registration.ShortcutPath) &&
                Path.GetExtension(registration.ShortcutPath)
                    .Equals(".lnk", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void RevalidateAndLaunch(
        WindowsLaunchRegistration registered,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (registered)
        {
            case StartMenuRegistration shortcut:
            {
                if (!IsExactLaunchRegistration(shortcut)) throw AppUnavailable();
                var current = _startMenuSource.ReadExact(
                    shortcut.ShortcutPath, shortcut.Scope, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (current is null || !IsExactLaunchRegistration(current) ||
                    current.Scope != shortcut.Scope ||
                    !string.Equals(current.IdentityKey, shortcut.IdentityKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(current.ShortcutPath, shortcut.ShortcutPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(current.RevalidationKey, shortcut.RevalidationKey,
                        StringComparison.Ordinal))
                    throw AppUnavailable();
                _shellLauncher.Launch(current.ShortcutPath, cancellationToken);
                return;
            }
            case AppsFolderRegistration packaged:
            {
                var current = _appsFolderSource.ReadExact(packaged.Aumid, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (current is null || !IsStructurallyValid(current) ||
                    !string.Equals(current.IdentityKey, packaged.IdentityKey,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(current.Aumid, packaged.Aumid,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(current.RevalidationKey, packaged.RevalidationKey,
                        StringComparison.Ordinal))
                    throw AppUnavailable();
                _packagedAppLauncher.Launch(current.Aumid, cancellationToken);
                return;
            }
            default:
                throw AppUnavailable();
        }
    }

    private static BrokerException AppUnavailable() =>
        new("app_not_found", "The selected app is no longer available.");

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
        WindowsLaunchRegistration Registration,
        string? DisplayName);

    private sealed record AppsFolderScan(
        IReadOnlyList<StartMenuRegistration> StartMenu,
        IReadOnlyList<AppsFolderRegistration> AppsFolder,
        bool AppsFolderAuthoritative);
}
