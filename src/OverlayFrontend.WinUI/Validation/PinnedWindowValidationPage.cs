using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WinUIEx;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Native HWND lifecycle/input probe, independent of widget projection correctness.</summary>
internal sealed partial class PinnedWindowValidationPage : Page, IAsyncDisposable
{
    private readonly Window owner;
    private readonly TextBlock status = new() { Text = "Preparing pinned window" };
    private readonly Button underneath = new() { Content = "Underlying target", Width = 240, Height = 72 };
    private readonly Button advance = new() { Content = "Next pinned check" };
    private readonly Button pinnedButton = new() { Content = "Pinned target" };
    private readonly List<string> checks = [];
    private PinnedWidgetWindow? pin;
    private int underlyingClicks;
    private int pinnedClicks;
    private int stage;
    internal PinnedWindowValidationPage(Window owner)
    {
        this.owner = owner;
        AutomationProperties.SetAutomationId(status, "PinWindow.Status");
        AutomationProperties.SetAutomationId(underneath, "PinWindow.Under");
        AutomationProperties.SetAutomationId(advance, "PinWindow.Next");
        AutomationProperties.SetAutomationId(pinnedButton, "PinWindow.Target");
        Content = new StackPanel { Spacing = 20, Children = { status, underneath, advance } };
        underneath.Click += (_, _) => { ++underlyingClicks; status.Text = $"Underlying clicks: {underlyingClicks}"; };
        pinnedButton.Click += (_, _) => { ++pinnedClicks; status.Text = $"Pinned clicks: {pinnedClicks}"; };
        advance.Click += async (_, _) => await AdvanceAsync();
        Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(100);
                var scale = XamlRoot.RasterizationScale;
                var rect = underneath.TransformToVisual(owner.Content).TransformBounds(new(0, 0, underneath.ActualWidth, underneath.ActualHeight));
                var origin = new NativePoint();
                if (ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(owner), ref origin) == 0) throw new InvalidOperationException("Client placement unavailable.");
                pin = new("native validation");
                pinnedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                pinnedButton.VerticalAlignment = VerticalAlignment.Stretch;
                pin.SetContent(new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 96, 192)),
                    Children = { pinnedButton } });
                pin.Place(new(origin.X + (int)Math.Round(rect.X * scale), origin.Y + (int)Math.Round(rect.Y * scale),
                    (int)Math.Round(rect.Width * scale), (int)Math.Round(rect.Height * scale)));
                var before = GetForegroundWindow();
                pin.Show();
                await Task.Delay(150);
                Check(pin.IsVisible && GetForegroundWindow() == before, "passive native peer appears without acquiring foreground");
                Check((pin.NativeWindow.GetExtendedWindowStyle() & (ExtendedWindowStyle.Transparent | ExtendedWindowStyle.NoActivate | ExtendedWindowStyle.Layered)) ==
                    (ExtendedWindowStyle.Transparent | ExtendedWindowStyle.NoActivate | ExtendedWindowStyle.Layered), "passive peer has layered click-through/noactivate styles");
                Check(SendMessageW(pin.Handle, 0x84, 0, 0) == -1, "passive window hit-test returns HTTRANSPARENT");
                Check(pin.Bounds.Width == (int)Math.Round(rect.Width * scale) && pin.Bounds.Height == (int)Math.Round(rect.Height * scale), "native window matches requested physical bounds");
                pin.SetOpacity(45);
                Check(GetLayeredWindowAttributes(pin.Handle, out _, out var alpha, out _) != 0 && alpha == 115,
                    "whole-window opacity uses configured bounded native alpha");
                status.Text = "Click the underlying target through the pin";
                Write("passive");
            }
            catch (Exception error) { Fail(error); }
        };
    }
    private async Task AdvanceAsync()
    {
        try
        {
            if (pin is null) throw new InvalidOperationException("No native pin.");
            if (stage == 0)
            {
                Check(underlyingClicks == 1 && pinnedClicks == 0, "real pointer click passed to the underlying target exactly once");
                pin.SetInteraction(true);
                Check((pin.NativeWindow.GetExtendedWindowStyle() & (ExtendedWindowStyle.Transparent | ExtendedWindowStyle.NoActivate)) == 0,
                    "interactive mode removes native click-through and noactivate flags");
                pin.SetOpacity(100); pin.NativeWindow.Activate();
                Check(GetLayeredWindowAttributes(pin.Handle, out _, out var alpha, out _) != 0 && alpha == 255,
                    "interactive test surface restores full native opacity");
                await Task.Delay(100);
                Check(pinnedButton.Focus(FocusState.Keyboard), "interactive native peer admits keyboard focus");
                status.Text = "Click the pinned target";
                ++stage; Write("interactive");
            }
            else if (stage == 1)
            {
                Check(pinnedClicks == 1 && underlyingClicks == 1, "interactive pointer click reaches the pin exactly once");
                pin.SetInteraction(false);
                Check(pinnedButton.IsEnabled, "returning to passive preserves authored enabled presentation");
                owner.AppWindow.Hide();
                Check(pin.IsVisible, "hiding the main overlay does not hide its pinned peer");
                owner.AppWindow.Show(); owner.Activate();
                pin.Hide(); Check(!pin.IsVisible && !pin.Interactive, "explicit pin hide withdraws its interaction mode");
                pin.Show(); Check(pin.IsVisible && !pin.Interactive, "pin reopens passively");
                pin.SetOpacity(60);
                status.Text = "Passed pinned window checks";
                ++stage; Write("passed");
            }
        }
        catch (Exception error) { Fail(error); }
    }
    private void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
    private void Fail(Exception error) { status.Text = "Failed: " + error.Message; Write("failed", error.ToString()); }
    private void Write(string phase, string? error = null)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "pinned-window-result.json"), JsonSerializer.Serialize(new
        { phase, error, checks, underlyingClicks, pinnedClicks, handle = pin?.Handle.ToInt64(), bounds = pin?.Bounds }));
    }
    public ValueTask DisposeAsync() { pin?.Dispose(); pin = null; return ValueTask.CompletedTask; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [LibraryImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ClientToScreen(nint hwnd, ref NativePoint point);
    [LibraryImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SendMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetLayeredWindowAttributes(nint hwnd, out uint colorKey, out byte alpha, out uint flags);
}
