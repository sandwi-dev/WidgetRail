using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.FirstPartyWidgets.MediaSessions;

public sealed partial class MediaSessionsWidget
{
    private string[] ActionPermissions(WidgetActionEvent action)
    {
        return action.ActionId switch { "media.previous" or "media.next" or "media.toggle" => ["system.media.sessions.control.v1"], "media.retry" => ["system.media.sessions.read.v1"], _ => [] };
    }
    private void PermissionFeedback(string text) { _model.Update(state => state with { Status = text }); }
}
