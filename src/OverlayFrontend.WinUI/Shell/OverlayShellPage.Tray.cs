using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private (object Item, long Selection)? pendingTrayFocus;

    private void RequestTrayFocus(object item)
    {
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
        for (var element = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
             element is not null && !ReferenceEquals(element, Tray); element = VisualTreeHelper.GetParent(element))
            if (element is ListViewItem item && Tray.ItemFromContainer(item) is BridgeWidgetDescriptor descriptor) return descriptor;
        return null;
    }

    private void TrayGotFocus(object sender, RoutedEventArgs args)
    {
        // ItemClick can precede the native focus event from that same click.
        // Keep its explicit enter intent rather than converting it to a preview.
        // Native fallback during a widget publication is not a domain transfer,
        // even if Windows could not cancel the focused-element-removal move.
        if (interactive && (switching || surface?.IsApplyingPresentation == true)) return;
        RecordFocusTransfer("tray");
        SetInteractive(false);
        if (FocusedTrayWidget() is not { } descriptor) return;
        if (descriptor.Id != requestedWidget) { trayHold.Cancel(); FinishTrayReorder(); CloseTrayMenu(false); }
        Tray.SelectedItem = descriptor;
        if (descriptor.Id != requestedWidget) _ = SelectAsync(descriptor.Id, enterWidget: false);
    }

    private void TrayGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (!retired && visible && interactive && surface?.IsApplyingPresentation == true)
            args.TryCancel();
    }
}
