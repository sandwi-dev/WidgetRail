namespace WidgetRail.PlatformDiagnostics;

public sealed record AppUpdateCommand(string Action, string? ReviewToken = null);
public sealed record AppUpdateSnapshot(long Revision, string State, string CurrentVersion,
    string? AvailableVersion = null, string Notes = "", string Message = "", long DownloadedBytes = 0,
    long? TotalBytes = null, bool CanInstall = false, string? ReviewToken = null,
    bool AutomaticallyCheck = true, bool RequiresInteractiveSetup = false, bool InstallerOutcomePending = false)
{
    public static AppUpdateSnapshot Unavailable { get; } = new(0, "unavailable", "unknown", Message: "Updates are unavailable in this session.");
}

public static class AppUpdateContract
{
    public static void ValidateCommand(AppUpdateCommand value)
    {
        if (value is null || value.Action is not ("status" or "check" or "download" or "cancel" or "install" or "auto-enable" or "auto-disable") ||
            (value.Action == "install" ? !Hex(value.ReviewToken) : value.ReviewToken is not null))
            throw new PlatformDiagnosticsException("invalid_app_update_command");
    }
    public static void ValidateSnapshot(AppUpdateSnapshot value)
    {
        if (value is null || value.Revision < 0 || value.State is not ("unavailable" or "idle" or "checking" or "available" or "current" or "downloading" or "ready" or "handoff" or "failed" or "cancelled") ||
            !Text(value.CurrentVersion, 80) || value.AvailableVersion is not null && !Text(value.AvailableVersion, 80) ||
            value.Notes is null || value.Notes.Length > 8000 || value.Message is null || value.Message.Length > 512 ||
            value.DownloadedBytes < 0 || value.TotalBytes < 0 || value.ReviewToken is not null && !Hex(value.ReviewToken) ||
            value.State == "ready" && !Hex(value.ReviewToken))
            throw new PlatformDiagnosticsException("invalid_app_update_snapshot");
    }
    private static bool Hex(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
}
