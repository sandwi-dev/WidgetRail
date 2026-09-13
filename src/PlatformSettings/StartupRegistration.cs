using Microsoft.Win32;
using System.Runtime.Versioning;

namespace WidgetRail.PlatformSettings;

public sealed record StartupRegistrationStatus(bool Registered, bool CanChange, string Message, bool Error = false);

public interface IStartupRegistration
{
    StartupRegistrationStatus Read();
    StartupRegistrationStatus SetEnabled(bool enabled);
}

// The trusted Settings worker and installer share the named Run entry. There is
// deliberately no second preference in settings.json to drift out of sync.
public sealed class StartupRegistration : IStartupRegistration
{
    public const string EntryName = "WidgetRail";
    public const string InstallKey = @"Software\WidgetRail\Installation";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly IStartupRegistrationStore _store;
    private readonly string _applicationRoot;

    public StartupRegistration(IStartupRegistrationStore store, string applicationRoot)
    {
        _store = store;
        _applicationRoot = Path.GetFullPath(applicationRoot).TrimEnd(Path.DirectorySeparatorChar);
    }

    public static IStartupRegistration CreateForSettingsWorker() => OperatingSystem.IsWindows()
        ? new StartupRegistration(new WindowsStartupRegistrationStore(),
            Path.Combine(AppContext.BaseDirectory, "..", ".."))
        : new UnavailableStartupRegistration();

    private string? OwnedCommand()
    {
        var root = _store.ReadInstallRoot();
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) ||
            root.Contains('"') || !string.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                _applicationRoot, StringComparison.OrdinalIgnoreCase)) return null;
        return $"\"{Path.Combine(_applicationRoot, "OverlayHost.exe")}\"";
    }

    public StartupRegistrationStatus Read()
    {
        try { return ReadCore(); }
        catch (Exception ex) when (IsStorageFailure(ex))
        { return new(false, false, "Windows startup settings could not be read. Try again.", true); }
    }

    private StartupRegistrationStatus ReadCore()
    {
        var expected = OwnedCommand();
        if (expected is null) return new(false, false, "Available when using an installed copy of WidgetRail.");
        var current = _store.ReadCommand();
        if (current is not null && !string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            return new(false, false, "Another startup entry uses the WidgetRail name. Startup changes are unavailable here.");
        if (current is null) return new(false, true, "Off — open WidgetRail yourself when you need it.");
        // StartupApproved is Windows-owned. Unknown formats must not be called
        // enabled, and no mutation here may overwrite Windows' disablement.
        var approval = _store.ReadApproval();
        if (approval is null || approval.Length == 12 && approval[0] is 2 or 6)
            return new(true, true, "On — starts quietly when you sign in. Use your controller shortcut to open the overlay.");
        if (approval.Length == 12 && approval[0] is 3 or 7)
            return new(true, true, "Disabled in Windows. Enable WidgetRail in Windows Settings > Apps > Startup.");
        return new(true, true, "Registered. Check Windows Settings > Apps > Startup to see whether Windows allows it.");
    }

    public StartupRegistrationStatus SetEnabled(bool enabled)
    {
        try
        {
            var before = ReadCore();
            if (!before.CanChange) return before;
            var command = OwnedCommand()!;
            // The backend compares again immediately before mutation. Never
            // remove or overwrite an entry belonging to a different command.
            _store.ChangeOwnedCommand(command, enabled);
            return ReadCore();
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        { return Read() with { Message = "Could not change Windows startup settings. Please try again.", Error = true }; }
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException;

    private sealed class UnavailableStartupRegistration : IStartupRegistration
    {
        public StartupRegistrationStatus Read() => new(false, false, "Available on Windows when WidgetRail is installed.");
        public StartupRegistrationStatus SetEnabled(bool enabled) => Read();
    }
}

public interface IStartupRegistrationStore
{
    string? ReadInstallRoot();
    string? ReadCommand();
    byte[]? ReadApproval();
    void ChangeOwnedCommand(string command, bool enabled);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsStartupRegistrationStore : IStartupRegistrationStore
{
    public string? ReadInstallRoot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.InstallKey);
        return key?.GetValue("ApplicationRoot", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKey);
        var value = key?.GetValue(StartupRegistration.EntryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is null ? null : value as string ?? "invalid-entry";
    }
    public byte[]? ReadApproval()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.ApprovalKey);
        var value = key?.GetValue(StartupRegistration.EntryName);
        return value is null ? null : value as byte[] ?? [];
    }
    public void ChangeOwnedCommand(string command, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistration.RunKey);
        var current = key.GetValue(StartupRegistration.EntryName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (current is not null && (current is not string text ||
            !string.Equals(text, command, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Startup entry changed.");
        if (enabled) key.SetValue(StartupRegistration.EntryName, command, RegistryValueKind.String);
        else key.DeleteValue(StartupRegistration.EntryName, throwOnMissingValue: false);
    }
}
