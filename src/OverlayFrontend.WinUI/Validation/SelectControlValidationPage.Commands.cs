using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class SelectControlValidationPage
{
    private async Task VerifyCommandMenuAsync()
    {
        commandMenu = true; Apply();
        await OpenAsync();
        var opener = FrameworkElementAutomationPeer.CreatePeerForElement((Control)Find(presenter, "Widget.picker")!);
        Check(opener.GetPattern(PatternInterface.Value) is null && opener.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider,
            "Command menu exposes expansion without pretending to edit a value");
        Check(FocusedId == "Widget.picker.Option.first", "Command menu initially focuses its first enabled command");
        var third = Command("third");
        var count = actions.Count;
        third.Invoke();
        await WaitAsync(() => actions.Count == count + 1);
        Check(actions[^1].Action.ActionId == thirdAction && !presenter.HasTransientControl,
            "Command menu native Invoke dispatches once and closes");
        await OpenAsync();
        presenter.MoveFocus(Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down);
        Check(FocusedId == "Widget.picker.Option.third", "Command controller traversal skips disabled and busy actions");
        count = actions.Count; presenter.ActivateFocused();
        await WaitAsync(() => actions.Count == count + 1);
        Check(actions[^1].Action.ActionId == thirdAction, "Controller A invokes focused command exactly once");
        await OpenAsync();
        count = actions.Count;
        presenter.DismissTransientControl();
        Check(actions.Count == count && !presenter.HasTransientControl, "Command dismissal does not dispatch");
        await OpenAsync();
        third = Command("third");
        thirdAction = "choose.changed-command"; Apply();
        InvokeRetired(third); await Task.Delay(150);
        Check(!presenter.HasTransientControl && actions.Count == count, "Changed command declaration revokes the old native item");
        await OpenAsync();
        third = Command("third"); commandMenu = false; Apply();
        InvokeRetired(third); await Task.Delay(150);
        Check(!presenter.HasTransientControl && actions.Count == count, "Changing command menu to a value selector revokes old command authority");
        IInvokeProvider Command(string id)
        {
            var item = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
                .Select(popup => Find(popup.Child, "Widget.picker.Option." + id)).OfType<MenuFlyoutItem>().Single();
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(item);
            Check(peer.GetAutomationControlType() == AutomationControlType.MenuItem && peer.GetPattern(PatternInterface.Toggle) is null,
                "Command uses native menu-item semantics without a selected checkmark");
            return (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
        }
        static void InvokeRetired(IInvokeProvider provider)
        {
            try { provider.Invoke(); }
            catch (Exception error) when (error.HResult is unchecked((int)0x80040200) or unchecked((int)0x80040201)) { }
        }
    }
}
