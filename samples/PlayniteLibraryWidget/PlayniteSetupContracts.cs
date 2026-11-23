namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed class PlayniteSetupException(string message) : Exception(message);

[Flags]
internal enum PlayniteOptionalAddon { None = 0, GameActivity = 1, Achievements = 2 }

internal sealed record PlayniteSetupInstallation(string Id, string ProgramDirectory, string DataDirectory,
    string DisplayName, bool Running, bool Fullscreen);
internal enum PlayniteSetupAction { Connect, Start, Restart, Install, Repair, Enable, Manual }
internal sealed record PlayniteSetupPlan(PlayniteSetupInstallation Installation, PlayniteSetupAction Action,
    string Title, string Message, string? InstalledVersion, string? InstalledDigest, string? ExtensionDirectory)
{
    internal PlayniteOptionalAddon MissingAddons { get; init; }
    internal PlayniteOptionalAddon UncheckedAddons { get; init; }
    internal bool CanApply => Action != PlayniteSetupAction.Manual;
    internal string ActionLabel => Action switch
    {
        PlayniteSetupAction.Start => "Start Playnite and connect",
        PlayniteSetupAction.Restart => "Restart Playnite and connect",
        PlayniteSetupAction.Install => Installation.Running ? "Install integration and restart Playnite" : "Install integration and connect",
        PlayniteSetupAction.Repair => "Update integration and reconnect",
        PlayniteSetupAction.Enable => "Enable integration and reconnect",
        _ => "Connect Playnite",
    };
}
internal sealed record PlayniteSetupDiscovery(IReadOnlyList<PlayniteSetupInstallation> Installations, string? Message = null);
internal sealed record PlayniteSetupOffer(PlayniteSetupDiscovery Discovery, PlayniteSetupPlan? Plan, bool NeedsAttention);
internal sealed record PlayniteSetupResult(bool Connected, string Title, string Message);
internal sealed record PlayniteSetupView
{
    internal string? AddonMessage { get; init; }
    internal PlayniteOptionalAddon MessageAddon { get; init; }
    internal IReadOnlyList<PlayniteSetupInstallation> Installations { get; init; } = [];
    internal PlayniteSetupPlan? Plan { get; init; }
    internal string? Message { get; init; }
    internal string? Progress { get; init; }
    internal string ProgramPath { get; init; } = "";
    internal string DataPath { get; init; } = "";
    internal bool Busy { get; init; }
    internal bool Advanced { get; init; }
    internal bool Connected { get; init; }
}
internal interface IPlayniteSetupService
{
    ValueTask OpenAddonInstallerAsync(PlayniteSetupInstallation installation, PlayniteOptionalAddon addon, CancellationToken token);
    ValueTask<PlayniteSetupOffer> CheckSetupAsync(CancellationToken token);
    ValueTask<PlayniteSetupResult> StartAsync(PlayniteSetupInstallation installation, Action<string> progress, CancellationToken token);
    ValueTask<PlayniteSetupDiscovery> DiscoverAsync(string? programPath, string? dataPath, CancellationToken token);
    ValueTask<PlayniteSetupPlan> InspectAsync(PlayniteSetupInstallation installation, CancellationToken token);
    ValueTask<PlayniteSetupResult> ApplyAsync(PlayniteSetupPlan plan, Action<string> progress, CancellationToken token);
}
