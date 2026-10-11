using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameHelp;

public sealed partial class GameHelpWidget
{
    private async Task RequirePermissionAsync(string id, CancellationToken token)
    {
        var outcome = await HostServices.Permissions.RequestAsync([id], token);
        if (outcome != WidgetPermissionOutcome.Granted)
            throw new GameHelpException("permission_required", outcome switch
            {
                WidgetPermissionOutcome.Cancelled => "Permission request cancelled. You can try again when ready.",
                WidgetPermissionOutcome.Denied => "Access was denied. You can change Game Help's permissions in Settings → Widgets.",
                _ => "Permission approval is unavailable. Return to Game Help and try again, or manage access in Settings → Widgets.",
            });
    }
}
