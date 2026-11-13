using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Noninteractive reserved space. An optional authored aspect ratio uses the
/// same native measurement as poster content, including responsive width limits.
/// </summary>
internal sealed partial class WidgetSpacer : Grid
{
    private WidgetPosterPanel? ratioPanel;

    internal WidgetSpacer()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    internal void SetAspectRatio(double? ratio)
    {
        if (ratio is not > 0 || !double.IsFinite(ratio.Value))
        {
            if (ratioPanel is null) return;
            Children.Clear();
            ratioPanel = null;
            return;
        }
        if (ratioPanel is null) { ratioPanel = new WidgetPosterPanel(); Children.Add(ratioPanel); }
        if (ratioPanel.AspectRatio == ratio.Value) return;
        ratioPanel.AspectRatio = ratio.Value;
        ratioPanel.InvalidateMeasure();
    }
}
