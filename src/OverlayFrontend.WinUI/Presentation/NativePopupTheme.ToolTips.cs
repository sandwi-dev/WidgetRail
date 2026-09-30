using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class NativePopupTheme
{
    private static readonly ConditionalWeakTable<FrameworkElement, ToolTipRegistration> ToolTips = new();

    internal static void SetToolTip(FrameworkElement origin, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (ToolTips.TryGetValue(origin, out var previous)) { previous.Dispose(); ToolTips.Remove(origin); }
            ToolTipService.SetToolTip(origin, null);
            return;
        }
        var registration = ToolTips.GetValue(origin, static element => new(element));
        registration.Update(text);
    }

    // The origin owns its registration. A theme retains the tooltip only while
    // open; recycled/retired native controls cannot leave a live popup lease.
    private sealed class ToolTipRegistration : IDisposable
    {
        private readonly FrameworkElement origin;
        private readonly ToolTip tip = new();
        private IDisposable? lease;
        internal ToolTipRegistration(FrameworkElement origin)
        {
            this.origin = origin;
            tip.Opened += Opened; tip.Closed += Closed;
            origin.Loaded += Loaded; origin.Unloaded += Unloaded;
            ToolTipService.SetToolTip(origin, tip);
        }
        internal void Update(string text) { tip.Content = text; Find(origin)?.Apply(tip); }
        private void Loaded(object sender, RoutedEventArgs args) => Find(origin)?.Apply(tip);
        private void Opened(object sender, RoutedEventArgs args)
        {
            lease?.Dispose();
            var owner = Find(origin);
            lease = owner?.Track(() => owner.Apply(tip));
        }
        private void Closed(object sender, RoutedEventArgs args) { lease?.Dispose(); lease = null; }
        private void Unloaded(object sender, RoutedEventArgs args) { tip.IsOpen = false; Closed(sender, args); }
        public void Dispose()
        {
            tip.IsOpen = false; lease?.Dispose(); lease = null;
            tip.Opened -= Opened; tip.Closed -= Closed;
            origin.Loaded -= Loaded; origin.Unloaded -= Unloaded;
        }
    }

    private void Apply(ToolTip tip)
    {
        // Native ToolTip still owns placement, wrapping and all opening/closing
        // timing. Popup coordinates are outside the shell's scale transform.
        var zoom = appearance.InterfaceScale;
        var hint = styles?.GetValueOrDefault("hint")?.Base;
        var size = hint?.GetValueOrDefault("font-size")?.Number ?? fontSize / appearance.TextScale * .875;
        var weight = hint?.GetValueOrDefault("font-weight");
        ushort authoredWeight = weight?.Number is { } number ? (ushort)Math.Clamp(number, 100, 900) :
            weight?.Text == "bold" ? (ushort)700 : fontWeight.Weight;
        tip.Background = surface;
        tip.Foreground = ink;
        tip.BorderBrush = border;
        tip.BorderThickness = new Thickness(zoom);
        tip.CornerRadius = new CornerRadius(Math.Min(6, cornerRadius) * zoom);
        tip.Padding = new Thickness(8 * zoom, 4 * zoom, 8 * zoom, 4 * zoom);
        tip.MinWidth = tip.MinHeight = 0;
        tip.MaxWidth = 280 * zoom * Math.Max(1, appearance.TextScale);
        tip.FontSize = Math.Clamp(size, 8, 96) * appearance.TextScale * zoom;
        tip.FontWeight = new() { Weight = WidgetAccessibilityPolicy.From(appearance).FontWeight(authoredWeight) ?? authoredWeight };
        var family = hint?.GetValueOrDefault("font-family")?.Text;
        if (!string.IsNullOrEmpty(family)) tip.FontFamily = new FontFamily(family);
        else if (font is not null) tip.FontFamily = font;
        else tip.ClearValue(Control.FontFamilyProperty);
    }
}
