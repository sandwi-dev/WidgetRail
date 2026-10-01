using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// A host-owned theme context for native popups, whose visual trees leave their
/// declaration's ancestor chain. Leases retain only open controls and update
/// their existing brushes when appearance changes; native templates/input remain.
/// </summary>
internal sealed partial class NativePopupTheme
{
    private static readonly ConditionalWeakTable<FrameworkElement, NativePopupTheme> Owners = new();
    private readonly HashSet<Lease> leases = [];
    private readonly SolidColorBrush surface = new(), ink = new(), muted = new(), selected = new(), selectedInk = new(), border = new(), focus = new(), key = new();
    private readonly SolidColorBrush clear = new(Microsoft.UI.Colors.Transparent);
    private IReadOnlyDictionary<string, BridgeNodeRenderStyles>? styles;
    private AppearanceSettings appearance = AppearanceSettings.Default;
    private double fontSize = 16;
    private FontFamily? font;
    private Windows.UI.Text.FontWeight fontWeight = new() { Weight = 400 };
    private ShellChromePalette? palette;
    private readonly LinearGradientBrush panelPaint = new() { StartPoint = new(0, 0), EndPoint = new(0, 1) };
    private double cornerRadius = 10;
    private readonly Dictionary<Control, NativePopupSelection> decorations = [];

    internal void Attach(FrameworkElement root) { Owners.Remove(root); Owners.Add(root, this); }
    internal static IDisposable? Menu(MenuFlyout menu, FrameworkElement origin)
    { var owner = Find(origin); return owner?.TrackMenu(menu); }
    internal static IDisposable? Dialog(ContentDialog dialog, FrameworkElement origin)
    { var owner = Find(origin); return owner?.Track(() => owner.Apply(dialog)); }
    private static NativePopupTheme? Find(FrameworkElement origin)
    {
        for (DependencyObject? current = origin; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement element && Owners.TryGetValue(element, out var owner)) return owner;
        return null;
    }
    private IDisposable TrackMenu(MenuFlyout menu)
    {
        var controls = menu.Items.OfType<Control>().ToArray();
        RoutedEventHandler changed = (sender, _) => PaintMenuItem((Control)sender);
        foreach (var control in controls)
        { control.GotFocus += changed; control.LostFocus += changed; decorations.Add(control, new(control)); }
        return Track(() => Apply(menu), () =>
        { foreach (var control in controls)
          { control.GotFocus -= changed; control.LostFocus -= changed; if (decorations.Remove(control, out var decoration)) decoration.Dispose(); } });
    }
    private IDisposable Track(Action apply, Action? release = null)
    {
        var lease = new Lease(this, apply, release); leases.Add(lease);
        try { apply(); return lease; } catch { lease.Dispose(); throw; }
    }
    private sealed class Lease(NativePopupTheme owner, Action apply, Action? release) : IDisposable
    {
        internal void Apply() => apply();
        public void Dispose() { if (owner.leases.Remove(this)) release?.Invoke(); }
    }

    internal void Update(IReadOnlyDictionary<string, BridgeNodeRenderStyles>? next, AppearanceSettings settings)
    {
        var resolved = ShellChromePalette.Resolve(next, settings);
        if (ReferenceEquals(styles, next) && appearance == settings && palette == resolved) return;
        styles = next; appearance = settings; palette = resolved;
        surface.Color = resolved.Surface; ink.Color = resolved.Text; muted.Color = resolved.Muted;
        selected.Color = ShellChromePalette.Composite(resolved.Surface, resolved.Selected); selectedInk.Color = resolved.SelectedText;
        focus.Color = resolved.Focus;
        border.Color = resolved.HighContrast ? resolved.Focus : resolved.Muted with { A = (byte)Math.Round(resolved.Muted.A * .55) };
        panelPaint.GradientStops.Clear();
        panelPaint.GradientStops.Add(new() { Offset = 0, Color = Shade(resolved.Surface, .06) });
        panelPaint.GradientStops.Add(new() { Offset = .5, Color = resolved.Surface });
        panelPaint.GradientStops.Add(new() { Offset = 1, Color = Shade(resolved.Surface, -.06) });
        cornerRadius = Math.Clamp(OverlaySurfacePaint.CornerRadius(next) * .75, 0, 12);
        key.Color = resolved.HighContrast ? resolved.Surface : ShellChromePalette.Blend(resolved.Surface, resolved.Text, .055);
        fontSize = Math.Clamp((next?.GetValueOrDefault("body")?.Base.GetValueOrDefault("font-size")?.Number ?? 16) * settings.TextScale, 8, 96);
        font = next?.GetValueOrDefault("body")?.Base.GetValueOrDefault("font-family")?.Text is { Length: > 0 } family ? new FontFamily(family) : null;
        var authoredWeight = next?.GetValueOrDefault("body")?.Base.GetValueOrDefault("font-weight");
        ushort weight = authoredWeight?.Number is { } number ? (ushort)Math.Clamp(number, 100, 900) :
            authoredWeight?.Text == "bold" ? (ushort)700 : (ushort)400;
        fontWeight = new() { Weight = WidgetAccessibilityPolicy.From(settings).FontWeight(weight) ?? weight };
        foreach (var lease in leases.ToArray()) lease.Apply();
    }

    private void Apply(MenuFlyout menu)
    {
        // Flyouts live outside OverlayScaleRoot's transform. Apply its layout
        // zoom once here so native popup metrics match the originating widget.
        var zoom = appearance.InterfaceScale;
        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(Control.BackgroundProperty, ResolvedPanel));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ink));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, border));
        style.Setters.Add(new Setter(Control.FontSizeProperty, fontSize * zoom));
        style.Setters.Add(new Setter(Control.FontWeightProperty, fontWeight));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6 * zoom)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(cornerRadius * zoom)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(zoom)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 220 * zoom));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 360 * zoom * Math.Max(1, appearance.TextScale)));
        if (font is not null) style.Setters.Add(new Setter(Control.FontFamilyProperty, font));
        menu.MenuFlyoutPresenterStyle = style;
        foreach (var item in menu.Items)
        {
            if (item is MenuFlyoutSeparator separator)
            { separator.Resources["MenuFlyoutSeparatorBackground"] = border; continue; }
            if (item is not Control control) continue;
            control.FontSize = fontSize * zoom;
            control.FontWeight = fontWeight;
            control.MinHeight = 48 * zoom * Math.Max(1, appearance.TextScale);
            control.Padding = new Thickness(18 * zoom, 8 * zoom, 12 * zoom, 8 * zoom);
            control.CornerRadius = new(cornerRadius * .65 * zoom);
            if (font is not null) control.FontFamily = font;
            else control.ClearValue(Control.FontFamilyProperty);
            foreach (var prefix in new[] { "MenuFlyoutItem", "MenuFlyoutSubItem", "ToggleMenuFlyoutItem" })
            foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
            {
                var active = state is "PointerOver" or "Pressed";
                control.Resources[prefix + "Background" + state] = active ? selected : clear;
                control.Resources[prefix + "Foreground" + state] = state == "Disabled" ? muted : active ? selectedInk : ink;
                control.Resources[prefix + "KeyboardAcceleratorTextForeground" + state] = muted;
                control.Resources[prefix + "CheckGlyphForeground" + state] = state == "Disabled" ? muted : active ? selectedInk : ink;
            }
            control.Resources["MenuFlyoutSubItemChevron"] = focus;
            control.FocusVisualPrimaryBrush = focus;
            control.UseSystemFocusVisuals = palette?.HighContrast == true;
            PaintMenuItem(control);
        }
    }

    private void PaintMenuItem(Control control)
    {
        var active = control.IsEnabled && control.FocusState != FocusState.Unfocused;
        control.Background = active ? selected : clear;
        control.Foreground = !control.IsEnabled ? muted : active ? selectedInk : ink;
        if (decorations.TryGetValue(control, out var decoration))
            decoration.Apply(active, palette?.HighContrast == true, appearance.InterfaceScale,
                cornerRadius * .65, selected.Color, focus.Color);
    }

    private Brush ResolvedPanel => palette?.HighContrast == true ? surface : panelPaint;
    private static Windows.UI.Color Shade(Windows.UI.Color color, double amount)
    {
        byte Channel(byte value) => (byte)Math.Clamp(Math.Round(amount >= 0 ? value + (255 - value) * amount : value * (1 + amount)), 0, 255);
        return Windows.UI.Color.FromArgb(color.A, Channel(color.R), Channel(color.G), Channel(color.B));
    }

    private void Apply(ContentDialog dialog)
    {
        var zoom = appearance.InterfaceScale;
        var size = fontSize * zoom;
        dialog.Background = surface; dialog.Foreground = ink; dialog.BorderBrush = border; dialog.FontSize = size;
        dialog.BorderThickness = new Thickness(zoom);
        dialog.CornerRadius = new CornerRadius(cornerRadius * zoom);
        dialog.FontWeight = fontWeight;
        if (font is not null) dialog.FontFamily = font;
        else dialog.ClearValue(Control.FontFamilyProperty);
        dialog.Resources["ContentDialogBackground"] = surface;
        dialog.Resources["ContentDialogForeground"] = ink;
        dialog.Resources["ContentDialogContentForegroundBrush"] = ink;
        dialog.Resources["ContentDialogBorderBrush"] = border;
        dialog.Resources["ContentDialogTopOverlay"] = surface;
        dialog.Resources["ControlContentThemeFontSize"] = size;
        if (dialog is WidgetTextEntryDialog textEntry) textEntry.ApplyPopupMetrics(zoom, size, fontWeight, font);
        dialog.Resources["FocusVisualPrimaryBrush"] = focus;
        dialog.Resources["TextControlSelectionHighlightColor"] = selected;
        dialog.Resources["TextControlHeaderForeground"] = ink;
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            dialog.Resources["ButtonBackground" + state] = state is "PointerOver" or "Pressed" ? selected : key;
            dialog.Resources["ButtonForeground" + state] = state == "Disabled" ? muted : state is "PointerOver" or "Pressed" ? selectedInk : ink;
            dialog.Resources["ButtonBorderBrush" + state] = border;
            dialog.Resources["TextControlButtonForeground" + state] = state == "Disabled" ? muted : ink;
            dialog.Resources["TextControlButtonBackground" + state] = state is "PointerOver" or "Pressed" ? selected : clear;
        }
        foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            dialog.Resources["TextControlBackground" + state] = key;
            dialog.Resources["TextControlForeground" + state] = state == "Disabled" ? muted : ink;
            dialog.Resources["TextControlPlaceholderForeground" + state] = muted;
            dialog.Resources["TextControlBorderBrush" + state] = state == "Focused" ? focus : border;
        }
    }
}
