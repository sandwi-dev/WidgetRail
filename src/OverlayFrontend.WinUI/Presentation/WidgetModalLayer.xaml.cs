using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// A bounded widget-local overlay. Native Grid measures both layers in the same
/// viewport; the parent is retained in its original size and scroll coordinates.
/// The input-blocking scrim is below the dialog and above the unchanged parent.
/// </summary>
internal sealed partial class WidgetModalLayer : Grid
{
    private FrameworkElement? dialog;
    private double preferredWidth;
    private double preferredHeight;
    private double authoredMinWidth;
    private double authoredMinHeight;
    private double authoredMaxWidth;
    private double authoredMaxHeight;
    private readonly RectangleGeometry boundsClip = new();
    internal UIElement Chrome => Scrim;

    public WidgetModalLayer()
    {
        InitializeComponent();
        Clip = boundsClip;
        SizeChanged += (_, _) => ConstrainDialog();
    }

    internal void Configure(FrameworkElement parent, FrameworkElement panel,
        double width, double height, double maximumWidth, double maximumHeight)
    {
        dialog = panel;
        preferredWidth = width;
        preferredHeight = height;
        authoredMinWidth = panel.MinWidth;
        authoredMinHeight = panel.MinHeight;
        authoredMaxWidth = maximumWidth;
        authoredMaxHeight = maximumHeight;
        Canvas.SetZIndex(parent, 0);
        Canvas.SetZIndex(panel, 2);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.TabFocusNavigation = Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Cycle;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetIsDialog(panel, true);
        ConstrainDialog();
    }

    private void ConstrainDialog()
    {
        boundsClip.Rect = new(0, 0, ActualWidth, ActualHeight);
        if (dialog is null) return;
        // Clamping a native control's requested dimensions is host geometry
        // policy, not a replacement measure/arrange pass. Zero is valid while
        // opening; the first SizeChanged supplies the actual widget viewport.
        var availableWidth = Math.Max(0, ActualWidth - 32);
        var availableHeight = Math.Max(0, ActualHeight - 32);
        dialog.MinWidth = Math.Min(authoredMinWidth, availableWidth);
        dialog.MinHeight = Math.Min(authoredMinHeight, availableHeight);
        dialog.MaxWidth = Math.Min(authoredMaxWidth, availableWidth);
        dialog.MaxHeight = Math.Min(authoredMaxHeight, availableHeight);
        dialog.Width = Math.Min(preferredWidth, dialog.MaxWidth);
        dialog.Height = Math.Min(preferredHeight, dialog.MaxHeight);
    }
}
