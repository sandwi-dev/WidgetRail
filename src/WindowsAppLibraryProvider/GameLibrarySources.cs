using System.Security.Cryptography;
using System.Text;

namespace GameBarAlternative.WindowsAppLibraryProvider;

[Flags]
internal enum GameLibrarySourceActions
{
    None = 0,
    Launch = 1,
    Artwork = 2,
}

internal enum GameLibrarySourceHealth
{
    Healthy,
    Degraded,
    Unavailable,
}

internal interface IGameLibrarySourceAuthority;

internal sealed record GameLibrarySourceItem(
    string SourceIdentity,
    string Attribution,
    string StableIdentity,
    string DisplayName,
    WindowsAppLibraryKind Kind,
    bool Installed,
    bool Available,
    GameLibrarySourceActions SupportedActions,
    string ArtworkRevision,
    string SourceItemIdentity);

internal sealed record GameLibrarySourceCandidate(
    GameLibrarySourceHealth Health,
    IReadOnlyList<GameLibrarySourceItem> Items,
    IReadOnlyDictionary<string, IGameLibrarySourceAuthority> Authorities);

internal sealed record GameLibrarySourceSnapshot(
    string SourceIdentity,
    string Attribution,
    long SourceVersion,
    GameLibrarySourceHealth Health,
    IReadOnlyList<GameLibrarySourceItem> Items);

internal interface IGameLibrarySource : IDisposable
{
    string SourceIdentity { get; }
    string Attribution { get; }
    GameLibrarySourceSnapshot Snapshot { get; }
    GameLibrarySourceSnapshot Refresh(CancellationToken cancellationToken);
    GameLibrarySourceItem? ResolveExact(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken);
    void Launch(GameLibrarySourceItem exactItem, CancellationToken cancellationToken);
    string? LoadArtwork(GameLibrarySourceItem exactItem, CancellationToken cancellationToken);
}

internal abstract class GameLibrarySourceBase : IGameLibrarySource
{
    private static readonly TimeSpan DisposeDrainDeadline = TimeSpan.FromSeconds(5);
    private readonly object _lifetimeGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ManualResetEventSlim _drained = new(initialState: true);
    private long _requestedGeneration;
    private int _activeOperations;
    private bool _disposed;
    private GameLibrarySourceSnapshot _snapshot;
    private IReadOnlyDictionary<string, IGameLibrarySourceAuthority> _authorities =
        new Dictionary<string, IGameLibrarySourceAuthority>(StringComparer.Ordinal);

    protected GameLibrarySourceBase(string sourceIdentity, string attribution)
    {
        SourceIdentity = sourceIdentity;
        Attribution = attribution;
        _snapshot = new(sourceIdentity, attribution, 0,
            GameLibrarySourceHealth.Unavailable, []);
    }

    public string SourceIdentity { get; }
    public string Attribution { get; }

    public GameLibrarySourceSnapshot Snapshot
    {
        get
        {
            lock (_lifetimeGate) return _snapshot;
        }
    }

    public GameLibrarySourceSnapshot Refresh(CancellationToken cancellationToken)
    {
        var operation = BeginOperation(cancellationToken, advancesGeneration: true);
        try
        {
            var candidate = RefreshCore(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            lock (_lifetimeGate)
            {
                ThrowIfDisposedLocked();
                if (operation.Generation == _requestedGeneration)
                {
                    _snapshot = new GameLibrarySourceSnapshot(
                        SourceIdentity,
                        Attribution,
                        operation.Generation,
                        candidate.Health,
                        Array.AsReadOnly(candidate.Items.ToArray()));
                    _authorities = new Dictionary<string, IGameLibrarySourceAuthority>(
                        candidate.Authorities, StringComparer.Ordinal);
                }
                return _snapshot;
            }
        }
        finally
        {
            operation.Dispose();
            EndOperation();
        }
    }

    public GameLibrarySourceItem? ResolveExact(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken) =>
        Execute(cancellationToken, token => ResolveExactCore(item, token));

    public void Launch(GameLibrarySourceItem exactItem, CancellationToken cancellationToken) =>
        Execute(cancellationToken, token =>
        {
            LaunchCore(exactItem, token);
            return true;
        });

    public string? LoadArtwork(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken) =>
        Execute(cancellationToken, token => LoadArtworkCore(exactItem, token));

    protected abstract GameLibrarySourceCandidate RefreshCore(
        CancellationToken cancellationToken);
    protected abstract GameLibrarySourceItem? ResolveExactCore(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken);
    protected abstract void LaunchCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken);
    protected abstract string? LoadArtworkCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken);

    protected bool TryGetAuthority<T>(GameLibrarySourceItem item, out T authority)
        where T : class, IGameLibrarySourceAuthority
    {
        lock (_lifetimeGate)
        {
            if (string.Equals(item.SourceIdentity, SourceIdentity, StringComparison.Ordinal) &&
                _authorities.TryGetValue(item.SourceItemIdentity, out var candidate) &&
                candidate is T typed)
            {
                authority = typed;
                return true;
            }
            authority = null!;
            return false;
        }
    }

    protected GameLibrarySourceCandidate RetainCurrent(GameLibrarySourceHealth health)
    {
        lock (_lifetimeGate)
            return new(health, _snapshot.Items, _authorities);
    }

    protected IReadOnlyList<T> CurrentAuthorities<T>()
        where T : class, IGameLibrarySourceAuthority
    {
        lock (_lifetimeGate)
            return _authorities.Values.OfType<T>().ToArray();
    }

    private T Execute<T>(CancellationToken cancellationToken, Func<CancellationToken, T> action)
    {
        var operation = BeginOperation(cancellationToken, advancesGeneration: false);
        try
        {
            var result = action(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            operation.Dispose();
            EndOperation();
        }
    }

    private OperationLease BeginOperation(
        CancellationToken cancellationToken,
        bool advancesGeneration)
    {
        lock (_lifetimeGate)
        {
            ThrowIfDisposedLocked();
            _activeOperations++;
            _drained.Reset();
            var generation = advancesGeneration
                ? ++_requestedGeneration
                : _requestedGeneration;
            return new OperationLease(
                generation,
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _lifetime.Token));
        }
    }

    private void EndOperation()
    {
        lock (_lifetimeGate)
        {
            _activeOperations--;
            if (_activeOperations == 0) _drained.Set();
        }
    }

    private void ThrowIfDisposedLocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            _disposed = true;
            _requestedGeneration++;
            _lifetime.Cancel();
        }
        if (!_drained.Wait(DisposeDrainDeadline))
            throw new InvalidOperationException(
                $"{Attribution} source did not drain within its bounded deadline.");
        _lifetime.Dispose();
        _drained.Dispose();
    }

    private sealed class OperationLease(long generation, CancellationTokenSource source) :
        IDisposable
    {
        internal long Generation { get; } = generation;
        internal CancellationToken Token => source.Token;
        public void Dispose() => source.Dispose();
    }
}

internal sealed class WindowsInstalledGameLibrarySource : GameLibrarySourceBase
{
    internal const string StableSourceIdentity = "source-windows-installed";
    private readonly IStartMenuApplicationSource _startMenu;
    private readonly IAppsFolderApplicationSource _appsFolder;
    private readonly IWindowsShellLauncher _shellLauncher;
    private readonly IWindowsPackagedAppLauncher _packagedLauncher;
    private readonly IWindowsAppIconSource _iconSource;

    internal WindowsInstalledGameLibrarySource(
        IStartMenuApplicationSource startMenu,
        IAppsFolderApplicationSource appsFolder,
        IWindowsShellLauncher shellLauncher,
        IWindowsPackagedAppLauncher packagedLauncher,
        IWindowsAppIconSource iconSource) : base(StableSourceIdentity, "Windows")
    {
        _startMenu = startMenu;
        _appsFolder = appsFolder;
        _shellLauncher = shellLauncher;
        _packagedLauncher = packagedLauncher;
        _iconSource = iconSource;
    }

    protected override GameLibrarySourceCandidate RefreshCore(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StartMenuRegistration> startMenu;
        try
        {
            startMenu = _startMenu.Enumerate(cancellationToken);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            return RetainCurrent(GameLibrarySourceHealth.Unavailable);
        }

        IReadOnlyList<AppsFolderRegistration> appsFolder;
        var health = GameLibrarySourceHealth.Healthy;
        try
        {
            appsFolder = _appsFolder.Enumerate(cancellationToken);
        }
        catch (AppsFolderEnumerationException)
        {
            appsFolder = CurrentAuthorities<WindowsAuthority>()
                .Select(authority => authority.Registration)
                .OfType<AppsFolderRegistration>()
                .ToArray();
            health = GameLibrarySourceHealth.Degraded;
        }

        var registrations = startMenu.Cast<WindowsLaunchRegistration>()
            .Concat(appsFolder)
            .Where(IsValidAuthority)
            .GroupBy(registration => registration.IdentityKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(registration => registration is StartMenuRegistration
                    { Scope: StartMenuScope.CurrentUser } ? 0 :
                    registration is StartMenuRegistration ? 1 : 2)
                .ThenBy(registration => registration.RevalidationKey,
                    StringComparer.Ordinal)
                .First())
            .ToArray();
        var authorities = new Dictionary<string, IGameLibrarySourceAuthority>(
            StringComparer.Ordinal);
        var items = registrations.Select(registration =>
        {
            var item = ToItem(registration);
            authorities.Add(item.SourceItemIdentity, new WindowsAuthority(registration));
            return item;
        }).ToArray();
        return new(health, Array.AsReadOnly(items), authorities);
    }

    protected override GameLibrarySourceItem? ResolveExactCore(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<WindowsAuthority>(item, out var expected))
            return null;
        WindowsLaunchRegistration? current = expected.Registration switch
        {
            StartMenuRegistration shortcut => _startMenu.ReadExact(
                shortcut.ShortcutPath, shortcut.Scope, cancellationToken),
            AppsFolderRegistration packaged => _appsFolder.ReadExact(
                packaged.Aumid, cancellationToken),
            _ => null,
        };
        return current is not null && IsValidAuthority(current) &&
            ExactIdentityMatches(expected.Registration, current)
            ? ToItem(current)
            : null;
    }

    protected override void LaunchCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<WindowsAuthority>(exactItem, out var authority))
            throw new InvalidOperationException("Windows launch authority is invalid.");
        switch (authority.Registration)
        {
            case StartMenuRegistration shortcut:
                _shellLauncher.Launch(shortcut.ShortcutPath, cancellationToken);
                return;
            case AppsFolderRegistration packaged:
                _packagedLauncher.Launch(packaged.Aumid, cancellationToken);
                return;
            default:
                throw new InvalidOperationException("Windows launch authority is invalid.");
        }
    }

    protected override string? LoadArtworkCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken) =>
        TryGetAuthority<WindowsAuthority>(exactItem, out var authority)
        ? authority.Registration switch
        {
            StartMenuRegistration shortcut => _iconSource.TryRasterizePngBase64(
                shortcut.ShortcutPath, cancellationToken),
            AppsFolderRegistration packaged => _iconSource.TryRasterizeAppsFolderPngBase64(
                packaged.Aumid, cancellationToken),
            _ => null,
        }
        : null;

    private GameLibrarySourceItem ToItem(WindowsLaunchRegistration registration) => new(
        SourceIdentity,
        Attribution,
        registration.IdentityKey,
        registration.DisplayName,
        WindowsAppLibraryKind.Application,
        Installed: true,
        Available: true,
        GameLibrarySourceActions.Launch | GameLibrarySourceActions.Artwork,
        registration.RevalidationKey,
        SourceItemIdentity(SourceIdentity, registration));

    private static bool ExactIdentityMatches(
        WindowsLaunchRegistration expected,
        WindowsLaunchRegistration current) =>
        expected.GetType() == current.GetType() &&
        string.Equals(expected.IdentityKey, current.IdentityKey,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.RevalidationKey, current.RevalidationKey,
            StringComparison.Ordinal) &&
        (expected, current) switch
        {
            (StartMenuRegistration left, StartMenuRegistration right) =>
                left.Scope == right.Scope &&
                string.Equals(left.ShortcutPath, right.ShortcutPath,
                    StringComparison.OrdinalIgnoreCase),
            (AppsFolderRegistration left, AppsFolderRegistration right) =>
                string.Equals(left.Aumid, right.Aumid, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool IsValidAuthority(WindowsLaunchRegistration registration)
    {
        if (registration is null ||
            string.IsNullOrWhiteSpace(registration.IdentityKey) ||
            registration.IdentityKey.Length > 128 ||
            string.IsNullOrWhiteSpace(registration.RevalidationKey) ||
            registration.RevalidationKey.Length > 128)
            return false;
        try
        {
            return registration switch
            {
                StartMenuRegistration shortcut =>
                    Enum.IsDefined(shortcut.Scope) &&
                    Path.IsPathFullyQualified(shortcut.ShortcutPath) &&
                    Path.GetExtension(shortcut.ShortcutPath)
                        .Equals(".lnk", StringComparison.OrdinalIgnoreCase),
                AppsFolderRegistration packaged =>
                    WindowsAppsFolderApplicationSource.NormalizeAumid(packaged.Aumid) is
                        { } normalized &&
                    string.Equals(normalized, packaged.Aumid, StringComparison.Ordinal),
                _ => false,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSourceFailure(Exception exception) => exception is IOException or
        UnauthorizedAccessException or InvalidOperationException or
        System.Security.SecurityException;

    private static string SourceItemIdentity(
        string sourceIdentity,
        WindowsLaunchRegistration registration) =>
        "record-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            sourceIdentity + "\0" + registration.IdentityKey + "\0" +
            registration.RevalidationKey)));

    private sealed record WindowsAuthority(WindowsLaunchRegistration Registration) :
        IGameLibrarySourceAuthority;
}

internal sealed class SteamGameLibrarySource : GameLibrarySourceBase
{
    internal const string StableSourceIdentity = "source-steam-installed";
    private readonly ISteamApplicationSource _source;
    private readonly IWindowsSteamLauncher _launcher;

    internal SteamGameLibrarySource(
        ISteamApplicationSource source,
        IWindowsSteamLauncher launcher) : base(StableSourceIdentity, "Steam")
    {
        _source = source;
        _launcher = launcher;
    }

    protected override GameLibrarySourceCandidate RefreshCore(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SteamRegistration> registrations;
        try
        {
            registrations = _source.Enumerate(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidOperationException or
            System.Security.SecurityException)
        {
            return RetainCurrent(GameLibrarySourceHealth.Unavailable);
        }
        var authorities = new Dictionary<string, IGameLibrarySourceAuthority>(
            StringComparer.Ordinal);
        var items = registrations.Where(IsValidAuthority).Select(registration =>
        {
            var item = ToItem(registration);
            authorities.Add(item.SourceItemIdentity, new SteamAuthority(registration));
            return item;
        }).ToArray();
        return new(GameLibrarySourceHealth.Healthy,
            Array.AsReadOnly(items), authorities);
    }

    protected override GameLibrarySourceItem? ResolveExactCore(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<SteamAuthority>(item, out var authority))
            return null;
        var expected = authority.Registration;
        var current = _source.ReadExact(
            expected.SteamAppId, expected.ManifestPath, cancellationToken);
        return current is not null && IsValidAuthority(current) &&
            string.Equals(current.IdentityKey, expected.IdentityKey,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.ManifestPath, expected.ManifestPath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.RevalidationKey, expected.RevalidationKey,
                StringComparison.Ordinal)
            ? ToItem(current)
            : null;
    }

    protected override void LaunchCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<SteamAuthority>(exactItem, out var authority))
            throw new InvalidOperationException("Steam launch authority is invalid.");
        _launcher.Launch(authority.Registration.SteamAppId, cancellationToken);
    }

    protected override string? LoadArtworkCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken) => null;

    private GameLibrarySourceItem ToItem(SteamRegistration registration) => new(
        SourceIdentity,
        Attribution,
        registration.IdentityKey,
        registration.DisplayName,
        WindowsAppLibraryKind.Game,
        Installed: true,
        Available: true,
        GameLibrarySourceActions.Launch,
        registration.RevalidationKey,
        "record-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SourceIdentity + "\0" + registration.IdentityKey + "\0" +
            registration.RevalidationKey))));

    private static bool IsValidAuthority(SteamRegistration registration)
    {
        try
        {
            return registration is not null &&
                !string.IsNullOrWhiteSpace(registration.IdentityKey) &&
                registration.IdentityKey.Length <= 128 &&
                !string.IsNullOrWhiteSpace(registration.RevalidationKey) &&
                registration.RevalidationKey.Length <= 128 &&
                WindowsSteamApplicationSource.IsValidAppId(registration.SteamAppId) &&
                Path.IsPathFullyQualified(registration.ManifestPath) &&
                Path.GetFileName(registration.ManifestPath).Equals(
                    $"appmanifest_{registration.SteamAppId}.acf",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    private sealed record SteamAuthority(SteamRegistration Registration) :
        IGameLibrarySourceAuthority;
}
