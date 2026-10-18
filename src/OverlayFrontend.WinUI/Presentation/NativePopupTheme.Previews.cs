using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class NativePopupTheme
{
    // Portal geometry already carries interface scale; chrome must not apply it twice.
    internal static IDisposable Preview(Border chrome, FrameworkElement origin,
        IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles, AppearanceSettings appearance)
    {
        var owner = Find(origin);
        if (owner is null) { owner = new(); owner.Update(styles, appearance); }
        return owner.Track(() =>
        {
            chrome.Background = owner.surface;
            chrome.BorderBrush = owner.border;
            chrome.BorderThickness = new Thickness(1);
            chrome.CornerRadius = new CornerRadius(owner.cornerRadius);
            chrome.Padding = new Thickness(12);
        });
    }
}
