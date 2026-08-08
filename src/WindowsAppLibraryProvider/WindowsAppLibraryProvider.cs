using System.Text;
using System.Globalization;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Lazily builds a bounded catalog of executable-backed .lnk registrations in
/// the current-user and all-user Start Menu Programs folders. Launch resolves a
/// provider-owned opaque ID and re-reads that exact shortcut before invoking the
/// Windows Shell; packaged AppsFolder entries are not yet inventoried.
/// </summary>
public sealed class WindowsAppLibraryProvider : IAppLibraryPlatformBrokerBackend
{
    internal const int MaximumApps = 512;
    internal const int MaximumDisplayNameLength = 120;

    private readonly IStartMenuApplicationSource _source;
    private readonly IWindowsShellLauncher _shellLauncher;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _opaqueIdsByIdentity =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<WindowsAppLibraryItem>? _snapshot;
    private Dictionary<string, StartMenuRegistration> _registrationsByOpaqueId =
        new(StringComparer.Ordinal);

    public WindowsAppLibraryProvider() : this(new WindowsStartMenuApplicationSource())
    {
    }

    internal WindowsAppLibraryProvider(IStartMenuApplicationSource source) :
        this(source, new WindowsShellLauncher())
    {
    }

    internal WindowsAppLibraryProvider(
        IStartMenuApplicationSource source,
        IWindowsShellLauncher shellLauncher)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _shellLauncher = shellLauncher ?? throw new ArgumentNullException(nameof(shellLauncher));
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

    public async Task<IReadOnlyList<AppLibraryItemSummary>> GetAppLibraryAsync(
        CancellationToken cancellationToken)
    {
        var apps = await GetAppsAsync(cancellationToken).ConfigureAwait(false);
        return ProjectForBroker(apps);
    }

    public async Task<IReadOnlyList<AppLibraryItemSummary>> RefreshAppLibraryAsync(
        CancellationToken cancellationToken)
    {
        var apps = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return ProjectForBroker(apps);
    }

    private static IReadOnlyList<AppLibraryItemSummary> ProjectForBroker(
        IReadOnlyList<WindowsAppLibraryItem> apps) =>
        apps.Select(app => new AppLibraryItemSummary(
                app.AppId,
                app.DisplayName,
                app.Kind switch
                {
                    WindowsAppLibraryKind.Unknown => AppLibraryKind.Unknown,
                    WindowsAppLibraryKind.Application => AppLibraryKind.Application,
                    WindowsAppLibraryKind.Game => AppLibraryKind.Game,
                    _ => AppLibraryKind.Unknown,
                }))
            .ToArray();

    public async Task LaunchAppLibraryItemAsync(
        string appId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > 128)
            throw AppUnavailable();

        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StartMenuRegistration? registered;
            lock (_stateGate)
                _registrationsByOpaqueId.TryGetValue(appId, out registered);
            if (registered is null || !IsExactLaunchRegistration(registered))
                throw AppUnavailable();

            var current = await Task.Run(
                () => _source.ReadExact(
                    registered.ShortcutPath, registered.Scope, cancellationToken),
                cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (current is null || !IsExactLaunchRegistration(current) ||
                current.Scope != registered.Scope ||
                !string.Equals(current.IdentityKey, registered.IdentityKey,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.ShortcutPath, registered.ShortcutPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.RevalidationKey, registered.RevalidationKey,
                    StringComparison.Ordinal))
                throw AppUnavailable();

            cancellationToken.ThrowIfCancellationRequested();
            _shellLauncher.Launch(current.ShortcutPath, cancellationToken);
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
            ArgumentException or NotSupportedException)
        {
            throw new BrokerException(
                "launch_failed", "Windows could not launch this app.", exception);
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

            var registrations = await Task.Run(
                () => _source.Enumerate(cancellationToken), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Commit(registrations);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private IReadOnlyList<WindowsAppLibraryItem> Commit(
        IReadOnlyList<StartMenuRegistration>? registrations)
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
                .OrderBy(candidate => candidate.Registration.Scope)
                .ThenBy(candidate => candidate.Registration.ShortcutPath,
                    StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Registration.IdentityKey,
                StringComparer.OrdinalIgnoreCase)
            .Take(MaximumApps)
            .ToArray();

        lock (_stateGate)
        {
            var liveIdentities = candidates
                .Select(candidate => candidate.Registration.IdentityKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _opaqueIdsByIdentity.Keys
                         .Where(identity => !liveIdentities.Contains(identity)).ToArray())
                _opaqueIdsByIdentity.Remove(stale);

            var byId = new Dictionary<string, StartMenuRegistration>(StringComparer.Ordinal);
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
            return _registrationsByOpaqueId.TryGetValue(opaqueId, out registration);
    }

    private static bool IsStructurallyValid(StartMenuRegistration registration) =>
        registration is not null &&
        Enum.IsDefined(registration.Scope) &&
        !string.IsNullOrWhiteSpace(registration.IdentityKey) &&
        registration.IdentityKey.Length <= 128 &&
        !string.IsNullOrWhiteSpace(registration.ShortcutPath) &&
        registration.ShortcutPath.Length <= 32_767 &&
        !string.IsNullOrWhiteSpace(registration.RevalidationKey) &&
        registration.RevalidationKey.Length <= 128;

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

    private static BrokerException AppUnavailable() =>
        new("app_not_found", "The selected app is no longer available.");

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
        StartMenuRegistration Registration,
        string? DisplayName);
}
