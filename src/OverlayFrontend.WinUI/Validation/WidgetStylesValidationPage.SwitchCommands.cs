using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task SwitchCommandsAsync()
    {
        var styles = new Dictionary<string, BridgeNodeRenderStyles>();
        var node = new WidgetView(UI.Switch("Wi-Fi", true, "toggle", "radio")).CreateSnapshot("radio-test", 1).Root;
        ViewNode Root(ViewNode value) => new() { Id = "switches", Kind = ViewNodeKind.Stack, Children = new[] {
            value, new ViewNode { Id = "next", Kind = ViewNodeKind.Button, Text = "Next", ActionId = "next" } } };
        var before = actions;
        presenter.Apply(CreateFrame(Root(node), styles));
        await Wait(() => Find<Button>("Widget.radio") is { IsLoaded: true });
        var control = Find<Button>("Widget.radio")!;
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(control);
        Check(peer.GetPattern(PatternInterface.Toggle) is null && peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider,
            "UI.Switch restores command semantics without an independent native toggle state");
        control.Focus(FocusState.Keyboard);
        await presenter.HandleControllerButtonAsync(ControllerButton.A);
        await Wait(() => actions == before + 1);
        Check(control.Content is TextBlock { Text: "Wi-Fi  On" }, "activation does not optimistically invert the displayed provider state");
        for (var pass = 0; pass < 3; pass++)
        {
            presenter.Apply(CreateFrame(Root(node with { IsBusy = true }), styles));
            Check(ReferenceEquals(control, Find<Button>("Widget.radio")) && control.IsEnabled &&
                ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), control), "busy radio snapshot retains its native focus target");
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Repeated);
            await presenter.HandleControllerButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            Check(actions == before + 1, "busy updates and repeated input cannot issue another radio command");
        }
        presenter.Apply(CreateFrame(Root(node with { IsSelected = false, Text = "Wi-Fi  Off" }), styles));
        Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), control) && control.Content is TextBlock { Text: "Wi-Fi  Off" },
            "authoritative Off publication changes the value once without moving focus");
        Check(actions == before + 1, "publishing the result never dispatches a command");
        presenter.Apply(CreateFrame(Root(node with { IsDisabled = true }), styles));
        Check(!control.IsEnabled, "genuine radio unavailability still disables the control");
    }
}
