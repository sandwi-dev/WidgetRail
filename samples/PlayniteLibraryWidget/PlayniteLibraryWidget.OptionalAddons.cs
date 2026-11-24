using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private const string InstallGameActivity = SetupPrefix + "install-gameactivity";
    private const string InstallAchievements = SetupPrefix + "install-achievements";
    private const string AchievementsSetupHelp = "In Playnite, open the add-on, set up your achievement sources, and start the initial refresh there.";

    internal static WidgetElement? MissingAddon(PlayniteSetupView setup, PlayniteOptionalAddon addon, string id)
    {
        if (!setup.Connected || setup.Plan is not { } plan || (plan.MissingAddons & addon) == 0) return null;
        var achievements = addon == PlayniteOptionalAddon.Achievements;
        var button = UI.Button(achievements ? "Install Playnite Achievements" : "Install GameActivity",
            achievements ? InstallAchievements : InstallGameActivity, id + ".install")
            .Disabled(setup.Busy).Classes("playnite-library-control");
        var content = new List<WidgetElement>();
        if (achievements)
            content.Add(UI.Text(AchievementsSetupHelp, id + ".setup").Classes("playnite-library-details-copy"));
        content.Add(button);
        if (setup.AddonMessage is { } message && setup.MessageAddon == addon)
            content.Add(UI.Text(message, id + ".status").Classes("playnite-library-details-copy"));
        return content.Count == 1 ? button : UI.Stack(id, content.ToArray());
    }

    private async ValueTask<bool> OpenOptionalAddonAsync(PlayniteOptionalAddon addon, CancellationToken token)
    {
        var setup = _model.Value.Setup;
        if (_setupService is null || !setup.Connected || setup.Plan is not { } plan ||
            (plan.MissingAddons & addon) == 0 || !BeginSetup()) return true;
        string? message = null;
        try
        {
            await _setupService.OpenAddonInstallerAsync(plan.Installation, addon, token).ConfigureAwait(false);
            message = "Continue in Playnite to approve the installation.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        { message = error is PlayniteSetupException ? error.Message : "Couldn’t open the add-on installer in Playnite. Try again."; }
        finally
        {
            _model.Update(state => state with { Setup = state.Setup with { Busy = false, Progress = null,
                AddonMessage = message, MessageAddon = addon } });
        }
        return true;
    }
}
