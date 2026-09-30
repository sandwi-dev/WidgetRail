using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class SelectControlValidationPage
{
    private async Task VerifySelectProvidersAsync()
    {
        var count = actions.Count;
        await OpenAsync();
        var selected = Provider("first");
        var other = Provider("third");
        Check(selected.ToggleState == ToggleState.On && other.ToggleState == ToggleState.Off,
            "native Select menu Toggle providers expose the authored checked option");
        foreach (var id in new[] { "disabled", "busy" })
        {
            var unavailable = Provider(id);
            var rejected = false;
            try { unavailable.Toggle(); }
            catch (Exception error) when (error.HResult == unchecked((int)0x80040200)) { rejected = true; }
            Check(rejected && actions.Count == count && presenter.HasTransientControl,
                "native " + id + " Select option rejects UIA Toggle without dispatch");
        }
        selected.Toggle();
        await WaitAsync(() => actions.Count == count + 1);
        Check(actions[^1].Action.ActionId == "choose.first" && !presenter.HasTransientControl,
            "UIA Toggle on the already checked option dispatches its command once");
        count = actions.Count;
        await OpenAsync();
        other = Provider("third");
        other.Toggle();
        await WaitAsync(() => actions.Count == count + 1);
        Check(actions[^1].Action.ActionId == thirdAction && !presenter.HasTransientControl,
            "UIA Toggle on an unchecked option uses its current per-option command");

        await OpenAsync();
        other = Provider("third");
        presenter.SetPresentationInputEnabled(false);
        count = actions.Count;
        ToggleRetired(other);
        await Task.Delay(150);
        Check(actions.Count == count && !presenter.HasTransientControl,
            "cached Select Toggle provider cannot dispatch after presentation input revocation");
        presenter.SetPresentationInputEnabled(true);
        await OpenAsync();
        other = Provider("third");
        ++owner; Apply();
        count = actions.Count;
        ToggleRetired(other);
        await Task.Delay(150);
        Check(actions.Count == count && !presenter.HasTransientControl,
            "cached Select Toggle provider cannot dispatch after runtime owner replacement");

        IToggleProvider Provider(string id)
        {
            var option = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
                .Select(popup => Find(popup.Child, "Widget.picker.Option." + id))
                .OfType<ToggleMenuFlyoutItem>().Single();
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(option);
            if (peer.GetAutomationControlType() != AutomationControlType.MenuItem)
                throw new InvalidOperationException("Select option lost native menu-item automation semantics.");
            return (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
        }
        static void ToggleRetired(IToggleProvider provider)
        {
            try { provider.Toggle(); }
            catch (Exception error) when (error.HResult is unchecked((int)0x80040200) or unchecked((int)0x80040201)) { }
        }
    }
}
