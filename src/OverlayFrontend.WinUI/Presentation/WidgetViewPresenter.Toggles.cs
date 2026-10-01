using WidgetRail.WidgetProtocol;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private readonly Dictionary<ToggleSwitch, WidgetNativeToggle> toggles = [];

    // UI.Switch has always emitted this SDK-owned semantic marker and selected
    // state. Native presentation does not change its action or grant authority.
    private static bool IsSwitch(ViewNode node) => node.Kind == ViewNodeKind.Button && node.StyleClasses.Contains("wrail-switch");

    private ToggleSwitch CreateSwitch(WidgetElementIdentity identity, object token)
    {
        var toggle = new WidgetNativeToggle();
        toggles.Add(toggle.Control, toggle);
        toggle.ActivationRequested = () =>
        {
            if (applying || disposed || !toggle.Control.IsLoaded || !CanDispatchAction ||
                !bindings.TryGetValue(identity.Id, out var binding) || binding.Identity != identity ||
                !ReferenceEquals(binding.Token, token) || !Eligible(binding))
            {
                if (bindings.TryGetValue(identity.Id, out var current) && ReferenceEquals(current.Token, token))
                    toggle.Publish(declarations[identity.Id].Node);
                return;
            }
            _ = InvokeAsync(identity, token);
        };
        return toggle.Control;
    }
}
