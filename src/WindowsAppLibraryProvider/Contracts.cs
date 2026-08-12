namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Conservative classification for one application registered with Windows.
/// The Start Menu source intentionally does not guess that an executable is a game.
/// </summary>
public enum WindowsAppLibraryKind
{
    Unknown,
    Application,
    Game,
}

/// <summary>
/// Sanitized application metadata safe to expose to trusted host callers.
/// AppId is a short-lived random provider-owned token; it never contains a path, AUMID,
/// command line, shortcut target, or launcher identifier.
/// </summary>
public sealed record WindowsAppLibraryItem(
    string AppId,
    string DisplayName,
    WindowsAppLibraryKind Kind);

internal enum StartMenuScope
{
    CurrentUser,
    AllUsers,
}

/// <summary>
/// Trusted source data. This record must remain inside the provider assembly.
/// The future launch operation will resolve the opaque ID to ShortcutPath and
/// re-read that exact shortcut before asking the Windows Shell to launch it.
/// </summary>
internal abstract record WindowsLaunchRegistration(
    string IdentityKey,
    string DisplayName,
    string RevalidationKey);

internal sealed record StartMenuRegistration(
    string IdentityKey,
    string DisplayName,
    StartMenuScope Scope,
    string ShortcutPath,
    string RevalidationKey) : WindowsLaunchRegistration(
        IdentityKey, DisplayName, RevalidationKey);

/// <summary>
/// Trusted AppsFolder registration. Aumid is never projected outside this
/// assembly; the public and broker layers receive only random/HMAC opaque IDs.
/// </summary>
internal sealed record AppsFolderRegistration(
    string IdentityKey,
    string DisplayName,
    string Aumid,
    string RevalidationKey) : WindowsLaunchRegistration(
        IdentityKey, DisplayName, RevalidationKey);

/// <summary>
/// Trusted installed Microsoft/Xbox game registration. Package and AUMID
/// evidence never leaves this provider assembly; widgets receive only the
/// provider and authority-scoped opaque identities derived from it.
/// </summary>
internal sealed record WindowsPackageGameRegistration(
    string IdentityKey,
    string DisplayName,
    string PackageFamilyName,
    string PackageFullName,
    string Aumid,
    string RevalidationKey);

internal sealed record WindowsPackageApplicationRegistration(
    string Aumid,
    string DisplayName);

internal sealed record WindowsPackageRegistrationCandidate(
    string PackageFamilyName,
    string PackageFullName,
    string InstalledLocation,
    IReadOnlyList<WindowsPackageApplicationRegistration> Applications);

/// <summary>
/// Trusted Steam library registration. The numeric AppId and manifest path stay
/// inside this assembly; widgets receive only provider and authority-scoped
/// opaque identifiers.
/// </summary>
internal sealed record SteamRegistration(
    string IdentityKey,
    string DisplayName,
    string SteamAppId,
    string ManifestPath,
    string RevalidationKey) : WindowsLaunchRegistration(
        IdentityKey, DisplayName, RevalidationKey)
{
    internal SteamArtworkRegistration? Artwork { get; init; }
}

/// <summary>
/// Host-only lazy locator for allowlisted Steam cache artwork. Trusted roots and
/// the numeric app identity never leave this provider assembly; file selection
/// and object evidence do not exist until explicit artwork demand.
/// </summary>
internal sealed record SteamArtworkRegistration(
    SteamArtworkLocator Locator,
    string Revision);

internal sealed record SteamApplicationSourceCandidate(
    IReadOnlyList<SteamRegistration> Registrations,
    IGameLibrarySourceCandidateCommit? Commit = null);

internal interface IStartMenuApplicationSource
{
    IReadOnlyList<StartMenuRegistration> Enumerate(CancellationToken cancellationToken);

    /// <summary>
    /// Re-reads one exact shortcut within the declared Start Menu scope. This
    /// avoids a long full-tree scan between fingerprint validation and launch.
    /// </summary>
    StartMenuRegistration? ReadExact(
        string shortcutPath,
        StartMenuScope scope,
        CancellationToken cancellationToken);
}

internal interface IAppsFolderApplicationSource
{
    IReadOnlyList<AppsFolderRegistration> Enumerate(CancellationToken cancellationToken);

    /// <summary>Re-reads one exact canonical AUMID immediately before launch.</summary>
    AppsFolderRegistration? ReadExact(
        string aumid,
        CancellationToken cancellationToken);
}

internal interface IWindowsPackageGameApplicationSource
{
    IReadOnlyList<WindowsPackageGameRegistration> Enumerate(
        CancellationToken cancellationToken);

    WindowsPackageGameRegistration? ReadExact(
        string packageFullName,
        string aumid,
        CancellationToken cancellationToken);
}

internal sealed record EpicGameRegistration(
    string IdentityKey,
    string DisplayName,
    string AppName,
    string CatalogNamespace,
    string CatalogItemId,
    string ManifestPath,
    string InstallLocation,
    string LaunchExecutable,
    string RevalidationKey);

internal sealed record EpicApplicationSourceCandidate(
    GameLibrarySourceHealth Health,
    IReadOnlyList<EpicGameRegistration> Registrations);

internal interface IEpicApplicationSource
{
    EpicApplicationSourceCandidate Enumerate(CancellationToken cancellationToken);
    EpicGameRegistration? ReadExact(
        string manifestPath,
        string appName,
        CancellationToken cancellationToken);
}

internal interface IWindowsEpicLauncher
{
    void Launch(
        string catalogNamespace,
        string catalogItemId,
        string appName,
        CancellationToken cancellationToken);
}

internal sealed record GogRegistryRecord(
    string RegistryView,
    string KeyName,
    object? GameId,
    object? GameName,
    object? InstallPath);

internal sealed record GogRegistrySnapshot(
    bool IsAvailable,
    IReadOnlyList<GogRegistryRecord> Records);

internal interface IGogRegistryReader
{
    GogRegistrySnapshot Enumerate(CancellationToken cancellationToken);
    GogRegistryRecord? ReadExact(
        string registryView,
        string keyName,
        CancellationToken cancellationToken);
}

internal sealed record GogGameRegistration(
    string IdentityKey,
    string DisplayName,
    string ProductId,
    string RevalidationKey);

internal sealed record GogApplicationSourceCandidate(
    GameLibrarySourceHealth Health,
    IReadOnlyList<GogGameRegistration> Registrations);

internal interface IGogApplicationSource
{
    GogApplicationSourceCandidate Enumerate(CancellationToken cancellationToken);
}

internal interface IWindowsPackageRegistrationCatalog
{
    IReadOnlyList<WindowsPackageRegistrationCandidate> Enumerate(
        CancellationToken cancellationToken);

    WindowsPackageRegistrationCandidate? ReadExact(
        string packageFullName,
        CancellationToken cancellationToken);
}

internal interface ISteamApplicationSource
{
    IReadOnlyList<SteamRegistration> Enumerate(CancellationToken cancellationToken);

    SteamApplicationSourceCandidate Stage(CancellationToken cancellationToken) =>
        new(Enumerate(cancellationToken));

    SteamRegistration? ReadExact(
        string steamAppId,
        string manifestPath,
        CancellationToken cancellationToken);

    string? LoadArtwork(
        SteamRegistration exactRegistration,
        CancellationToken cancellationToken) => null;
}

/// <summary>
/// Signals that AppsFolder could not produce an authoritative collection.
/// Callers must preserve their last-good packaged subset rather than treating
/// this as a successful empty enumeration.
/// </summary>
internal sealed class AppsFolderEnumerationException : InvalidOperationException
{
    internal AppsFolderEnumerationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class WindowsPackageEnumerationException : InvalidOperationException
{
    internal WindowsPackageEnumerationException(
        string message,
        Exception? innerException = null) : base(message, innerException)
    {
    }
}

internal interface IWindowsAppIconSource
{
    string? TryRasterizePngBase64(
        string shortcutPath,
        CancellationToken cancellationToken);

    string? TryRasterizeAppsFolderPngBase64(
        string aumid,
        CancellationToken cancellationToken) => null;
}

internal interface IWindowsPackagedAppLauncher
{
    void Launch(string exactAumid, CancellationToken cancellationToken);
}

internal interface IWindowsSteamLauncher
{
    void Launch(string exactSteamAppId, CancellationToken cancellationToken);
}

internal interface IShellStaExecutor
{
    Task<T> RunAsync<T>(
        Func<CancellationToken, T> operation,
        CancellationToken cancellationToken);
}
