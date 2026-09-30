using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private (object Item, long Selection)? pendingTrayFocus;
    private long nativeTrayFocusSelection;
    private bool nativeTrayFocusTransferAllowed;

    private void RequestTrayFocus(object item)
    {
        if (FocusRadialWidget(item)) { ClearTrayFocus(); return; }
        pendingTrayFocus = (item, selectionVersion);
        Tray.LayoutUpdated -= RestoreTrayFocus;
        Tray.LayoutUpdated += RestoreTrayFocus;
        Tray.ScrollIntoView(item);
        DispatcherQueue.TryEnqueue(() => RestoreTrayFocus(null, null!));
    }

    private void RestoreTrayFocus(object? sender, object args)
    {
        if (pendingTrayFocus is not { } pending) return;
        if (retired || !visible || interactive || PinnedInputActive || pending.Selection != selectionVersion || !Tray.Items.Contains(pending.Item))
        { ClearTrayFocus(); return; }
        if (Tray.ContainerFromItem(pending.Item) is Control { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } target &&
            target.Focus(FocusState.Keyboard)) ClearTrayFocus();
    }

    private void ClearTrayFocus()
    {
        pendingTrayFocus = null;
        Tray.LayoutUpdated -= RestoreTrayFocus;
    }

    private BridgeWidgetDescriptor? FocusedTrayWidget()
    {
        if (RadialSelectedWidget() is { } radialItem) return radialItem;
        for (var element = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
             element is not null && !ReferenceEquals(element, Tray); element = VisualTreeHelper.GetParent(element))
            if (element is ListViewItem item && Tray.ItemFromContainer(item) is BridgeWidgetDescriptor descriptor) return descriptor;
        return null;
    }

    private void TrayGotFocus(object sender, RoutedEventArgs args)
    {
        // Collapsing the retained widget during hide makes XAML choose another
        // focusable control. That fallback is not a user transfer to the tray.
        if (retired || !visible) return;
        // ItemClick can precede the native focus event from that same click.
        // Keep its explicit enter intent rather than converting it to a preview.
        // Native fallback during a widget publication is not a domain transfer,
        // even if Windows could not cancel the focused-element-removal move.
        if (interactive && (!nativeTrayFocusTransferAllowed || switching || surface?.IsApplyingPresentation == true)) return;
        // GettingFocus and GotFocus can straddle an asynchronous selection. A
        // completion of the previous entry is not a fresh tray selection intent.
        if (switching && nativeTrayFocusSelection != selectionVersion) return;
        RecordFocusTransfer("tray");
        SetInteractive(false);
        if (FocusedTrayWidget() is not { } descriptor) return;
        if (descriptor.Id != requestedWidget) { trayHold.Cancel(); FinishTrayReorder(); CloseTrayMenu(false); }
        Tray.SelectedItem = descriptor;
        if (descriptor.Id != requestedWidget) _ = SelectAsync(descriptor.Id, enterWidget: false);
        else UpdateTrayHelp();
    }

    private void TrayGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        nativeTrayFocusSelection = selectionVersion;
        // Widget/tray ownership is a logical user choice, not XAML's fallback
        // when a control unloads, a window activates, or a view is preparing.
        // Explicit host transfers set interactive=false first; pointer and
        // keyboard traversal remain valid user transfers.
        nativeTrayFocusTransferAllowed = !interactive || (!switching && surface?.IsApplyingPresentation != true &&
            (args.FocusState == FocusState.Pointer || args.Direction != FocusNavigationDirection.None));
        if (!retired && !nativeTrayFocusTransferAllowed) args.TryCancel();
    }
}
