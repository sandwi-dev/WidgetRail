using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public const string EditKeyboardShortcutAction = "controllers.keyboard-shortcut.edit";

    /// <summary>Only the displayed Settings command may open the host's keyboard recorder.</summary>
    public void ValidateKeyboardShortcutCommand(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            if (displayed.Authority.WidgetId != "settings" || action.ActionId != EditKeyboardShortcutAction ||
                action.SourceElementId != "controllers.keyboard-shortcut" || action.InputScopeId != "controllers.page" ||
                action.Phase != ControllerEventPhase.Pressed || action.RequestedValue is not null || action.CommittedText is not null ||
                ResolveDisplayedAction(displayed.Snapshot, action) is not { } declared || declared != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("Keyboard shortcut editing requires the current Settings command.");
        }
    }
}
