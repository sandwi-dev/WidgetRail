using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetTextEntryDialog
{
    private Popup? keyboardPopup;
    private FrameworkElement? keyboardOrigin;
    private bool arrangingKeyboard;
    private double floatingWidth;
    internal bool IsFloating => keyboardPopup is not null;
    internal void RefreshKeyboardPlacement() => ArrangeKeyboard();

    // Presentation varies; keys, editing, focus, repeat and theme remain in the
    // same control. A native Popup can extend beyond a small XAML island without
    // creating another controller owner or resizing the underlying WebView.
    internal async Task ShowKeyboardAsync(FrameworkElement origin)
    {
        if (XamlRoot is not { } root || completed) return;
        keyboardOrigin = origin;
        var host = new Grid();
        var popup = new Popup { XamlRoot = root, Child = host, ShouldConstrainToRootBounds = false,
            IsLightDismissEnabled = false };
        keyboardPopup = popup;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        if (Content is ScrollViewer scroll)
        {
            scroll.MaxHeight = double.PositiveInfinity;
            scroll.VerticalScrollMode = ScrollMode.Disabled;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
        Resources["ContentDialogMaxHeight"] = double.PositiveInfinity;
        Resources["ContentDialogMinHeight"] = 0d;
        host.Children.Add(this);
        FieldAnchorChanged += ArrangeKeyboard;
        LayoutUpdated += KeyboardLayoutUpdated;
        origin.LayoutUpdated += KeyboardLayoutUpdated;
        root.Changed += KeyboardRootChanged;
        try
        {
            ArrangeKeyboard();
            popup.IsOpen = true;
            await ShowAsync(ContentDialogPlacement.InPlace);
        }
        finally
        {
            root.Changed -= KeyboardRootChanged;
            origin.LayoutUpdated -= KeyboardLayoutUpdated;
            LayoutUpdated -= KeyboardLayoutUpdated;
            FieldAnchorChanged -= ArrangeKeyboard;
            popup.IsOpen = false;
            host.Children.Remove(this);
            popup.Child = null;
            keyboardPopup = null;
            keyboardOrigin = null;
        }
    }

    private void KeyboardLayoutUpdated(object? sender, object args) => ArrangeKeyboard();
    private void KeyboardRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ArrangeKeyboard();
    private void ArrangeKeyboard()
    {
        if (arrangingKeyboard || keyboardPopup is not { } popup || keyboardOrigin is not { IsLoaded: true } origin ||
            XamlRoot is not { } root || !ReferenceEquals(origin.XamlRoot, root)) return;
        arrangingKeyboard = true;
        try
        {
            var id = root.ContentIslandEnvironment.AppWindowId;
            var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(id);
            var screen = new KeyboardPoint();
            if (KeyboardClientToScreen(hwnd, ref screen) == 0) return;
            var work = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;
            var dpi = root.RasterizationScale;
            var available = new Rect((work.X - screen.X) / dpi + 16, (work.Y - screen.Y) / dpi + 16,
                Math.Max(1, work.Width / dpi - 32), Math.Max(1, work.Height / dpi - 32));
            var owner = origin.TransformToVisual(root.Content).TransformBounds(new(0, 0, origin.ActualWidth, origin.ActualHeight));
            var minimumWidth = Math.Max(440 * interfaceScale, FontSize * 27.5);
            var desiredWidth = Math.Clamp(owner.Width, minimumWidth, Math.Max(minimumWidth, 688 * interfaceScale));
            var width = Math.Min(desiredWidth, available.Width);
            if (Math.Abs(floatingWidth - width) >= .5)
            {
                floatingWidth = width;
                Width = MinWidth = MaxWidth = width;
                Resources["ContentDialogMaxWidth"] = width;
                Resources["ContentDialogMinWidth"] = width;
                ConstrainWidth();
            }
            var panel = NativeDialogPresentation.FindPart<FrameworkElement>(this, "BackgroundElement");
            if (panel is not null)
            {
                // The native template can retain its opening width after a text-scale
                // change. Keep its viewport in sync with the keyboard's owned popup.
                panel.Width = panel.MinWidth = panel.MaxWidth = width;
                panel.MaxHeight = double.PositiveInfinity;
                panel.MinHeight = 0;
                panel.HorizontalAlignment = HorizontalAlignment.Left;
                panel.VerticalAlignment = VerticalAlignment.Top;
                panel.Margin = new Thickness(0);
            }
            var height = panel?.ActualHeight ?? ActualHeight;
            var suggestionHeight = suggestionRegion?.ActualHeight ?? 0;
            var baseHeight = Math.Max(0, height - suggestionHeight);
            var anchor = FieldAnchor ?? owner;
            var x = Math.Clamp(anchor.X + (anchor.Width - width) / 2, available.Left, Math.Max(available.Left, available.Right - width));
            var y = anchor.Y + (anchor.Height - baseHeight) / 2;
            if (FieldAnchor is not null)
            {
                var below = available.Bottom - anchor.Bottom - 8;
                var above = anchor.Top - available.Top - 8;
                y = below >= baseHeight || below >= above ? anchor.Bottom + 8 : anchor.Top - baseHeight - 8;
                // If neither vertical side fits, try beside the field before
                // clamping over it. Never reduce the keyboard's key viewport.
                if (below < baseHeight && above < baseHeight && available.Right - anchor.Right >= width + 8)
                { x = anchor.Right + 8; y = anchor.Y; }
                else if (below < baseHeight && above < baseHeight && anchor.Left - available.Left >= width + 8)
                { x = anchor.Left - width - 8; y = anchor.Y; }
            }
            // Anchor the editor/keys using only their height. Suggestions grow
            // above that baseline, with their own bounded scrolling viewport.
            var minimumTopSpace = suggestionRegion is null ? 0 : 40 * interfaceScale + body.Spacing;
            y = Math.Clamp(y, Math.Min(available.Top + minimumTopSpace, Math.Max(available.Top, available.Bottom - baseHeight)),
                Math.Max(available.Top, available.Bottom - baseHeight));
            if (suggestionRegion is not null)
            {
                suggestionAvailableHeight = Math.Max(0, y - available.Top);
                ResizeSuggestionRegion();
            }
            y -= suggestionHeight;
            x = Math.Round(x); y = Math.Round(y);
            if (Math.Abs(popup.HorizontalOffset - x) >= 1) popup.HorizontalOffset = x;
            if (Math.Abs(popup.VerticalOffset - y) >= 1) popup.VerticalOffset = y;
        }
        finally { arrangingKeyboard = false; }
    }

    // HWND is borrowed from XamlRoot; POINT is caller-owned, two Win32 LONGs.
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardPoint { internal int X, Y; }
    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen")]
    private static partial int KeyboardClientToScreen(nint hwnd, ref KeyboardPoint point);
}
