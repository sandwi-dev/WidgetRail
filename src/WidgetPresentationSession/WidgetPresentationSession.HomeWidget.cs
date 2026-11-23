using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession;

public sealed partial class WidgetPresentationSession
{
    public const string ChooseHomeWidgetAction = "overlay.home-widget.choose";

    public void ValidateHomeWidgetCommand(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        using (_gate.Enter())
        {
            var current = ValidateDisplayedOrdinaryFrameLocked(displayed, action.InputScopeId);
            if (displayed.Descriptor.Id != "settings" || action.SourceElementId != "overlay.home-widget" ||
                action.InputScopeId != "overlay.page" || action.ActionId != ChooseHomeWidgetAction ||
                action.Phase != ControllerEventPhase.Pressed || action.RequestedValue is not null || action.CommittedText is not null ||
                ResolveDisplayedAction(displayed.Snapshot, action) != ResolveDisplayedAction(current.Snapshot, action))
                throw OrdinaryInputStale("The home widget picker requires the current General Settings command.");
        }
    }
}
