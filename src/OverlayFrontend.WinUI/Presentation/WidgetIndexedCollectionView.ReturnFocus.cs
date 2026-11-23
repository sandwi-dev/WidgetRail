using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private Control? returnRevealTarget;

    private void PrepareReturnFocusReveal(Control target)
    {
        CancelReturnFocusReveal();
        if (ReferenceEquals(FocusManager.GetFocusedElement(target.XamlRoot), target)) return;
        // WinUI posts focus reveal after Focus() returns. Intercept at the row:
        // the ScrollViewer's native class handler has already scrolled by the
        // time a subscriber on the ScrollViewer sees the routed request.
        // The saved viewport owns this one reveal. Later reveals remain native.
        returnRevealTarget = target;
        target.BringIntoViewRequested += ConsumeReturnFocusReveal;
        target.LosingFocus += ReturnFocusDeparting;
        target.Unloaded += ReturnFocusUnloaded;
    }

    private void ConsumeReturnFocusReveal(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        if (!ReferenceEquals(args.TargetElement, returnRevealTarget)) return;
        args.Handled = true;
        CancelReturnFocusReveal();
    }

    private void ReturnFocusDeparting(UIElement sender, LosingFocusEventArgs args) => CancelReturnFocusReveal();
    private void ReturnFocusUnloaded(object sender, RoutedEventArgs args) => CancelReturnFocusReveal();
    private void CancelReturnFocusReveal()
    {
        if (returnRevealTarget is not { } target) return;
        returnRevealTarget = null;
        target.BringIntoViewRequested -= ConsumeReturnFocusReveal;
        target.LosingFocus -= ReturnFocusDeparting;
        target.Unloaded -= ReturnFocusUnloaded;
    }
}
