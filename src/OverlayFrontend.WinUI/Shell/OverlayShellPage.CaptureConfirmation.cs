using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // This is presentation consent only. The broker's capture permission and current
    // request checks remain authoritative for both remembered and prompted requests.
    private async Task<bool> ConfirmCaptureAsync(string widgetId, bool recording, string description,
        Func<Task<bool>> current, CancellationToken token)
    {
        var store = new PlatformSettingsStore(new(options.SettingsRoot));
        var preferences = (await store.LoadAsync(token)).CaptureConfirmations;
        if (!await current()) return false;
        if (!preferences.ShouldConfirm(widgetId, recording)) return true;
        HostChoiceDialog? confirmation = null;
        var selected = await ShowHostChoiceAsync(recording ? "Record application?" : "Take screenshot?", description,
            (HostDialogChoice[])[new("proceed", "Proceed")], token, directDecisionButtons: true,
            configure: dialog =>
            {
                confirmation = dialog;
                dialog.AddRememberChoice(recording ? "Don’t ask again for recordings in this widget" : "Don’t ask again for screenshots in this widget");
            });
        if (selected != "proceed" || !await current()) return false;
        if (confirmation?.RememberChoice == true)
            await store.UpdateAsync(value => value with
            {
                CaptureConfirmations = value.CaptureConfirmations.Remember(widgetId, recording, proceeded: true,
                    remember: true, expectedResetVersion: preferences.ResetVersion),
            }, token);
        return await current();
    }
}

