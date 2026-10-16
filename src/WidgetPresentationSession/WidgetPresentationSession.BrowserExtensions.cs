using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public const string ManageBrowserExtensionsAction = "open.extensions";
    public static bool IsBrowserExtensionCommand(string action) => action.StartsWith("host.extensions.", StringComparison.Ordinal);
    /// <summary>Extension management belongs to trusted Settings, never an arbitrary widget action.</summary>
    public void ValidateBrowserExtensionManagement(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            var open = action.ActionId == ManageBrowserExtensionsAction && action.InputScopeId == "settings-root" && action.SourceElementId == "category.extensions";
            var command = IsBrowserExtensionCommand(action.ActionId) && (action.InputScopeId is "extensions.library" or "extensions.details");
            if (displayed.Authority.WidgetId != "settings" || !(open || command) ||
                action.Phase != ControllerEventPhase.Pressed || action.CommittedText is not null ||
                ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("Extension management requires the current Settings command.");
        }
    }
}
