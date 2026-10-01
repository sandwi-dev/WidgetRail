using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Stock WinUI toggle; worker publications never count as user input.</summary>
internal sealed class WidgetNativeToggle : IDisposable
{
    private bool publishing;
    internal ToggleSwitch Control { get; } = new();
    internal Action? ActivationRequested { get; set; }

    internal WidgetNativeToggle()
    {
        Control.Toggled += Toggled;
    }

    private void Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    { if (!publishing) ActivationRequested?.Invoke(); }

    public void Dispose()
    { Control.Toggled -= Toggled; ActivationRequested = null; }

    internal void Publish(ViewNode node)
    {
        publishing = true;
        try
        {
            Control.IsOn = node.IsSelected == true;
            // UI.Switch supplies label + state. SettingsField already supplies
            // the label outside the control and leaves only the state text.
            var label = node.Text ?? string.Empty;
            var separator = label.LastIndexOf("  ", StringComparison.Ordinal);
            Control.Header = separator > 0 ? label[..separator] : null;
        }
        finally { publishing = false; }
    }
}
