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

internal interface IShellStaExecutor
{
    Task<T> RunAsync<T>(
        Func<CancellationToken, T> operation,
        CancellationToken cancellationToken);
}
