using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Stock Slider behavior/template with bounded, centered thumb focus geometry.</summary>
// Partial enables C#/WinRT to generate the inherited native override interfaces.
// Without them, trimmed builds fail RangeBase value/range callbacks with E_NOINTERFACE.
internal sealed partial class WidgetSlider : Slider
{
    private FrameworkElement? container;
    private Grid? horizontalTemplate;
    private double authoredMinimumHeight;

    internal void SetAuthoredMinimumHeight(double value)
    {
        authoredMinimumHeight = value;
        UpdateInset();
    }
    internal WidgetSlider()
    {
        RegisterPropertyChangedCallback(PaddingProperty, (_, _) => UpdateInset());
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        container = GetTemplateChild("SliderContainer") as FrameworkElement;
        horizontalTemplate = GetTemplateChild("HorizontalTemplate") as Grid;
        if (horizontalTemplate is { } horizontal)
            horizontal.VerticalAlignment = VerticalAlignment.Center;
        UpdateInset();
    }
    private void UpdateInset()
    {
        // Keep WinUI's full-size thumb focus visual (14 DIP horizontal outset)
        // and reserve its space at both ends, plus fractional-DPI clearance.
        // Slider measures its track from SliderContainer, so the inset belongs
        // there; insetting only HorizontalTemplate would desynchronize Value
        // from the thumb position at the maximum.
        if (container is not null)
            container.Margin = new Thickness(Math.Max(0, 15 - Padding.Left), 0, Math.Max(0, 15 - Padding.Right), 0);
        // The stock horizontal template needs 32 DIPs before theme padding;
        // reserve another DIP per grid row for independent layout rounding.
        // An undersized slot creates a WinUI layout clip on BOTH axes, cutting
        // off the negative-margin thumb paint and pushing its focus ring inward.
        // Keep the native minimum as well as any larger authored minimum.
        var trackHeight = horizontalTemplate is { } template
            ? template.MinHeight + template.RowDefinitions.Count
            : 35;
        MinHeight = Math.Max(authoredMinimumHeight, Padding.Top + Padding.Bottom + trackHeight);
    }
}
