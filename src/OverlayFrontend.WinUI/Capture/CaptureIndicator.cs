using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

/// <summary>Passive existing WinUI peer; no task-switcher entry, activation, or captured pixels.</summary>
internal sealed partial class CaptureIndicator : IDisposable
{
    private readonly PinnedWidgetWindow window = new("Capture countdown");
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ShellChromeStyles styles = new();
    internal CaptureIndicator(IReadOnlyDictionary<string, WidgetRail.WidgetBridge.BridgeNodeRenderStyles>? palette,
        WidgetRail.PlatformSettings.AppearanceSettings appearance, bool animations)
    {
        try
        {
            var target = GetForegroundWindow();
            if (GetWindowRect(target, out var bounds) == 0) throw new InvalidOperationException("Capture target is unavailable.");
            var scale = Math.Max(1, GetDpiForWindow(target) / 96d);
            var width = (int)(360 * scale); var height = (int)(64 * scale);
            var panel = new Border { Child = label, Padding = new(12), CornerRadius = new(12) };
            styles.Register(panel, "tray"); styles.Register(label, "hint"); styles.Update(palette, appearance, animations);
            window.SetContent(panel); window.Place(new(bounds.Left + Math.Max(0, (bounds.Right - bounds.Left - width) / 2), bounds.Top + (int)(24 * scale), width, height));
            AutomationProperties.SetName(label, "Capture status");
            // Window capture already selects only the target. Exclusion is also
            // applied to the indicator so desktop capture cannot include it.
            if (SetWindowDisplayAffinity(window.Handle, 0x11) == 0) throw new InvalidOperationException("Capture indicator exclusion is unavailable.");
        }
        catch { styles.Dispose(); window.Dispose(); throw; }
    }
    internal void Show(string text) { label.Text = text; window.Show(); }
    public void Dispose() { styles.Dispose(); window.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll")] private static partial int GetWindowRect(nint window, out Rect rect);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint window);
    [LibraryImport("user32.dll")] private static partial int SetWindowDisplayAffinity(nint window, uint affinity);
}
