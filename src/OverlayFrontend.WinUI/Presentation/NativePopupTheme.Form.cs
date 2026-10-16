using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class NativePopupTheme
{
    internal static IDisposable? Form(Border panel, FrameworkElement origin, Action<double>? metricsChanged = null)
    {
        var owner = Find(origin);
        return owner?.Track(() =>
        {
            var zoom = owner.appearance.InterfaceScale;
            panel.Padding = new Thickness(24 * zoom);
            panel.Background = owner.dialogSurface; panel.BorderBrush = owner.dialogBorder; panel.CornerRadius = new CornerRadius(owner.cornerRadius * zoom);
            Paint(panel);
            metricsChanged?.Invoke(zoom);
            void Paint(DependencyObject node)
            {
                if (node is TextBlock text) { text.Foreground = owner.dialogText; text.FontWeight = owner.fontWeight; text.FontSize = owner.fontSize * zoom * (text.Tag as string == "form-title" ? 1.5 : 1); if (owner.font is not null) text.FontFamily = owner.font; }
                if (node is Control control && control is Button or CheckBox)
                {
                    control.Foreground = owner.dialogButtonText; control.FontWeight = owner.fontWeight; control.Background = owner.dialogButton;
                    control.BorderBrush = owner.dialogBorder; control.FontSize = owner.fontSize * zoom; control.MinHeight = 44 * zoom;
                    control.FocusVisualPrimaryBrush = owner.dialogFocus; control.UseSystemFocusVisuals = true;
                    if (owner.font is not null) control.FontFamily = owner.font;
                    if (control is CheckBox)
                    {
                        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
                        {
                            control.Resources["CheckBoxCheckBackgroundFillChecked" + state] = owner.dialogText;
                            control.Resources["CheckBoxCheckBackgroundStrokeChecked" + state] = owner.dialogText;
                            control.Resources["CheckBoxCheckGlyphForegroundChecked" + state] = owner.dialogSurface;
                            control.Resources["CheckBoxCheckBackgroundFillUnchecked" + state] = owner.dialogSurface;
                            control.Resources["CheckBoxCheckBackgroundStrokeUnchecked" + state] = owner.dialogBorder;
                            control.Resources["CheckBoxForegroundChecked" + state] = owner.dialogText;
                            control.Resources["CheckBoxForegroundUnchecked" + state] = owner.dialogText;
                        }
                    }
                }
                // Before the popup loads its native templates, enumerate content explicitly.
                if (node is Border border && border.Child is { } child) Paint(child);
                else if (node is Panel stack) foreach (var item in stack.Children) Paint(item);
                else if (node is ContentControl content && content.Content is DependencyObject contentChild) Paint(contentChild);
            }
        });
    }
}
