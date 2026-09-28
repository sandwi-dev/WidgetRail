using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private NativeComputedStyleAdapter? pressedStyle;
    internal void SetControllerPressedStyle(bool value)
    {
        pressedStyle?.SetControllerPressed(false); pressedStyle = null;
        if (!value || !CanReceiveInput || view?.XamlRoot is null) return;
        for (var current = FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;
            current is not null && !ReferenceEquals(current, view); current = VisualTreeHelper.GetParent(current))
            if (current is SelectorItem item && NativeComputedStyleAdapter.For(item) is { } adapter)
            { pressedStyle = adapter; adapter.SetControllerPressed(true); return; }
    }
}
