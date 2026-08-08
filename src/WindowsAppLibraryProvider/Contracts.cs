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
internal sealed record StartMenuRegistration(
    string IdentityKey,
    string DisplayName,
    StartMenuScope Scope,
    string ShortcutPath,
    string RevalidationKey);

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

internal interface IWindowsAppIconSource
{
    string? TryRasterizePngBase64(
        string shortcutPath,
        CancellationToken cancellationToken);
}
