using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public const string EditKeyboardShortcutAction = "controllers.keyboard-shortcut.edit";
    public const string EditReplayShortcutAction = "controllers.replay-shortcut.edit";
    public const string EditTrayFavoritesAction = "controllers.tray-actions.edit";

    /// <summary>Only the displayed Settings command may open the host's keyboard recorder.</summary>
    public void ValidateKeyboardShortcutCommand(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            var expectedElement=action.ActionId switch { EditKeyboardShortcutAction=>"controllers.keyboard-shortcut",EditReplayShortcutAction=>"controllers.replay-shortcut",EditTrayFavoritesAction=>"controllers.tray-actions",_=>null };
            if (displayed.Authority.WidgetId != "settings" || expectedElement is null ||
                action.SourceElementId != expectedElement || action.InputScopeId != "controllers.page" ||
                action.Phase != ControllerEventPhase.Pressed || action.RequestedValue is not null || action.CommittedText is not null ||
                ResolveDisplayedAction(displayed.Snapshot, action) is not { } declared || declared != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("Keyboard shortcut editing requires the current Settings command.");
        }
    }
}
