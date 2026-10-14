using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Exposes native template parts to shared chrome/placement code. ContentDialog
/// can move its template into a popup, outside the dialog's visual descendants.
/// Specialized content, input handling and completion stay in the derived types.
/// </summary>
internal abstract partial class FrameworkDialog : ContentDialog
{
    internal FrameworkElement? DialogTemplatePart(string name) => GetTemplateChild(name) as FrameworkElement;
    internal event Action? DialogTemplateChanged;
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        DialogTemplateChanged?.Invoke();
    }
}
