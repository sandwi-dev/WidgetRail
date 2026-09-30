using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetStyling;
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
    private NativeComputedStyleAdapter? pinnedStyle;
    private NativeComputedStyleAdapter? mainStyle;
    private int underlyingClicks;
    private int pinnedClicks;
    private int stage;
    private bool started;
    private bool advancing;
    private readonly string runId = Guid.NewGuid().ToString("N");
    private readonly List<object> inputTrace = [];
    private string phase = "initializing";
    private string? phaseError;
    private long publication;
    internal PinnedWindowValidationPage(Window owner)
    {
        this.owner = owner;
        AutomationProperties.SetAutomationId(status, "PinWindow.Status");
        AutomationProperties.SetAutomationId(underneath, "PinWindow.Under");
        AutomationProperties.SetAutomationId(advance, "PinWindow.Next");
        AutomationProperties.SetAutomationId(pinnedButton, "PinWindow.Target");
        Content = new StackPanel { Spacing = 20, Children = { status, underneath, advance } };
        underneath.Click += (_, _) => { ++underlyingClicks; TraceInput("under-click"); status.Text = $"Underlying clicks: {underlyingClicks}"; Write(phase); };
        pinnedButton.Click += (_, _) => { ++pinnedClicks; TraceInput("pin-click"); status.Text = $"Pinned clicks: {pinnedClicks}"; Write(phase); };
        foreach (var (button, name) in new[] { (underneath, "under"), (pinnedButton, "pin"), (advance, "advance") })
        {
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) => TraceInput(name + "-pointer-down", args.Pointer.PointerId)), true);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, args) => TraceInput(name + "-pointer-up", args.Pointer.PointerId)), true);
        }
        advance.Click += async (_, _) => { TraceInput("advance-invoked"); await AdvanceAsync(); };
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            try
            {
                await Task.Delay(100);
                var scale = XamlRoot.RasterizationScale;
                var rect = underneath.TransformToVisual(owner.Content).TransformBounds(new(0, 0, underneath.ActualWidth, underneath.ActualHeight));
                var origin = new NativePoint();
                if (ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(owner), ref origin) == 0) throw new InvalidOperationException("Client placement unavailable.");
                pin = new("native validation");
                pin.SurfaceBackground.CornerRadius = new CornerRadius(12);
                pin.ApplyFocusAppearance(ShellChromePalette.Resolve(null, AppearanceSettings.Default));
                pinnedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
                pinnedButton.VerticalAlignment = VerticalAlignment.Stretch;
                pin.SetContent(new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 96, 192)),
                    Children = { pinnedButton } });
                pinnedStyle = new(pinnedButton); pinnedStyle.Update(FocusStyles());
                mainStyle = new(underneath); mainStyle.Update(FocusStyles(), interaction: (true, false));
                pin.Place(new(origin.X + (int)Math.Round(rect.X * scale), origin.Y + (int)Math.Round(rect.Y * scale),
                    (int)Math.Round(rect.Width * scale), (int)Math.Round(rect.Height * scale)));
                var before = GetForegroundWindow();
                pin.Show();
                await Task.Delay(150);
                Check(pin.IsVisible && GetForegroundWindow() == before, "passive native peer appears without acquiring foreground");
                Check(pin.FocusIndicator.Visibility == Visibility.Collapsed && !pin.FocusIndicator.IsHitTestVisible,
                    "passive pin has no ownership outline or extra pointer target");
                pin.SetPlacementActive(true);
                Check(pin.FocusIndicator.Visibility == Visibility.Visible && !pin.Interactive && GetForegroundWindow() == before,
                    "host placement preview shows the themed outline without giving passive widget input or foreground");
                pin.SetPlacementActive(false);
                Check(pin.FocusIndicator.Visibility == Visibility.Collapsed,
                    "ending placement preview restores the passive ownership cue");
                Check(pin.ClientSize.Width == pin.Bounds.Width && pin.ClientSize.Height == pin.Bounds.Height,
                    "passive pinned peer gives its complete bounds to content without a native frame");
                Check((pin.ExtendedStyle & (ExtendedWindowStyle.Transparent | ExtendedWindowStyle.NoActivate | ExtendedWindowStyle.Layered)) ==
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
        if (advancing || phase == "failed") return;
        advancing = true;
        try
        {
            if (pin is null) throw new InvalidOperationException("No native pin.");
            if (stage == 0)
            {
                Check(underlyingClicks == 1 && pinnedClicks == 0, "real pointer click passed to the underlying target exactly once");
                var contentSize = new Windows.Foundation.Size(pin.Child!.ActualWidth, pin.Child.ActualHeight);
                pin.SetInteraction(true);
                // Removing WS_EX_NOACTIVATE can synchronously activate the peer.
                // The invariant is native foreground ownership, not whether this
                // fixture has reached its explicit Activate call yet.
                var nativeForeground = GetForegroundWindow() == pin.Handle;
                Check((pin.FocusIndicator.Visibility == Visibility.Visible) == nativeForeground,
                    "interaction request shows the ownership cue exactly when the peer already owns native foreground");
                Check((pin.ExtendedStyle & (ExtendedWindowStyle.Transparent | ExtendedWindowStyle.NoActivate)) == 0,
                    "interactive mode removes native click-through and noactivate flags");
                pin.SetOpacity(100); pin.Activate();
                Check(GetLayeredWindowAttributes(pin.Handle, out _, out var alpha, out _) != 0 && alpha == 255,
                    "interactive test surface restores full native opacity");
                await Task.Delay(100);
                Check(pinnedButton.Focus(FocusState.Keyboard), "interactive native peer admits keyboard focus");
                await Until(() => pin.FocusIndicator.Visibility == Visibility.Visible && GetForegroundWindow() == pin.Handle);
                await Until(() => pinnedStyle!.Interaction.Focused && FocusFill(pinnedButton));
                Check(Math.Abs(pin.Child.ActualWidth - contentSize.Width) < 1 && Math.Abs(pin.Child.ActualHeight - contentSize.Height) < 1,
                    "ownership outline overlays the existing content without shrinking its layout");
                Check(pin.FocusIndicator.BorderThickness.Left >= 4 && pin.FocusIndicator.CornerRadius == pin.SurfaceBackground.CornerRadius,
                    "interactive peer has a thick outline following its surface corners");
                var themed = ShellChromePalette.Resolve(null, AppearanceSettings.Default) with { Focus = Microsoft.UI.Colors.Magenta, FocusWidth = 3 };
                pin.ApplyFocusAppearance(themed);
                var focusBrush = (SolidColorBrush)pin.FocusIndicator.BorderBrush;
                Check(focusBrush.Color == themed.Focus, "pinned ownership outline follows the shared theme focus color");
                pin.InterfaceScale = AppearanceSettings.MaximumInterfaceScale;
                Check(pin.InterfaceScale == AppearanceSettings.MaximumInterfaceScale &&
                    Math.Abs(pin.FocusIndicator.BorderThickness.Left - 4 * AppearanceSettings.MaximumInterfaceScale) < .01,
                    "pinned ownership outline follows the supported maximum interface scale");
                pin.InterfaceScale = 1;
                var contrast = ShellChromePalette.Resolve(null, AppearanceSettings.Default with { Contrast = ContrastPreference.High });
                pin.ApplyFocusAppearance(contrast);
                Check(ReferenceEquals(focusBrush, pin.FocusIndicator.BorderBrush) && focusBrush.Color == contrast.Focus,
                    "high contrast updates the existing pinned outline brush using the system-aware palette");
                pin.ApplyFocusAppearance(themed);
                var logicalFocus = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(pin.AutomationRoot.XamlRoot);
                pin.SetInteraction(false);
                Check(!pinnedStyle!.Interaction.Focused && !pinnedStyle.Interaction.Pressed && pinnedStyle.FocusDecoration is null && BaseFill(pinnedButton) &&
                    !pinnedButton.UseSystemFocusVisuals && ReferenceEquals(logicalFocus,
                        Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(pin.AutomationRoot.XamlRoot)),
                    "explicit pin exit suppresses child focus paint while retaining the logical control");
                pin.SetInteraction(true); pin.Activate();
                await Until(() => GetForegroundWindow() == pin.Handle && pinnedStyle.Interaction.Focused && FocusFill(pinnedButton));
                owner.Activate();
                await Until(() => GetForegroundWindow() != pin.Handle && pin.FocusIndicator.Visibility == Visibility.Collapsed);
                Check(pin.Interactive, "activation loss hides the cue even before the coordinator revokes interaction");
                Check(!pinnedStyle.Interaction.Focused && BaseFill(pinnedButton) && !pinnedButton.UseSystemFocusVisuals &&
                    ReferenceEquals(logicalFocus, Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(pin.AutomationRoot.XamlRoot)) && FocusFill(underneath),
                    "AltTab-style deactivation suppresses only pinned focus presentation without changing logical focus or the main root style");
                pin.Activate();
                await Until(() => GetForegroundWindow() == pin.Handle && pin.FocusIndicator.Visibility == Visibility.Visible && pinnedStyle.Interaction.Focused);
                Check(true, "native foreground return restores the cue only while interaction remains admitted");
                status.Text = "Click the pinned target";
                ++stage; Write("interactive");
            }
            else if (stage == 1)
            {
                Check(pinnedClicks == 1 && underlyingClicks == 1, "interactive pointer click reaches the pin exactly once");
                var beforePassive = GetForegroundWindow();
                var logicalFocus = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(pin.AutomationRoot.XamlRoot);
                pin.SetInteraction(false);
                Check(GetForegroundWindow() == beforePassive && !pinnedStyle!.Interaction.Focused && BaseFill(pinnedButton) &&
                    ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(pin.AutomationRoot.XamlRoot), logicalFocus),
                    "passive presentation suppresses the remembered child style without acquiring foreground from the main window");
                var style = pinnedStyle ?? throw new InvalidOperationException("Pinned computed style was not initialized.");
                style.Update(FocusStyles(), interaction: (true, true));
                Check(style.Interaction == (false, false) && !pinnedButton.UseSystemFocusVisuals && BaseFill(pinnedButton),
                    "late nested-presenter focus and press overrides cannot rehighlight a passive pin");
                style.Update(FocusStyles());
                Check(pin.FocusIndicator.Visibility == Visibility.Collapsed,
                    "returning controller interaction to the overlay immediately removes the pinned ownership cue");
                Check(pinnedButton.IsEnabled, "returning to passive preserves authored enabled presentation");
                owner.AppWindow.Hide();
                Check(pin.IsVisible, "hiding the main overlay does not hide its pinned peer");
                owner.AppWindow.Show(); owner.Activate();
                pin.Hide(); Check(!pin.IsVisible && !pin.Interactive, "explicit pin hide withdraws its interaction mode");
                pin.Show(); Check(pin.IsVisible && !pin.Interactive, "pin reopens passively");
                Check(pin.FocusIndicator.Visibility == Visibility.Collapsed, "hide and passive reopen do not resurrect the ownership cue");
                Check(pin.ClientSize.Width == pin.Bounds.Width && pin.ClientSize.Height == pin.Bounds.Height,
                    "reopened pin does not restore the native nonclient outline");
                pin.SetOpacity(60);
                status.Text = "Passed pinned window checks";
                ++stage; Write("passed");
            }
        }
        catch (Exception error) { Fail(error); }
        finally { advancing = false; }
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Pinned focus ownership did not settle.");
    }
    private void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
    private void Fail(Exception error) { status.Text = "Failed: " + error.Message; Write("failed", error.ToString()); }
    private void TraceInput(string kind, uint? pointer = null)
    {
        if (inputTrace.Count >= 64) return;
        inputTrace.Add(new { kind, pointer, stage, phase, at = Environment.TickCount64, interactive = pin?.Interactive,
            foreground = GetForegroundWindow().ToInt64(), underlyingClicks, pinnedClicks });
    }
    private void Write(string nextPhase, string? error = null)
    {
        if (phase != "failed") phase = nextPhase;
        phaseError ??= error;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "pinned-window-result.json"), JsonSerializer.Serialize(new
        { processId = Environment.ProcessId, runId, publication = ++publication, phase, error = phaseError, checks, inputTrace, underlyingClicks, pinnedClicks, handle = pin?.Handle.ToInt64(), bounds = pin?.Bounds,
            nativeForeground = GetForegroundWindow().ToInt64(), interactive = pin?.Interactive,
            ownershipCue = pin?.FocusIndicator.Visibility.ToString() }));
    }
    private static bool BaseFill(Button button) => button.Background is SolidColorBrush brush && brush.Color == Windows.UI.Color.FromArgb(255, 22, 54, 80);
    private static bool FocusFill(Button button) => button.Background is SolidColorBrush brush && brush.Color == Windows.UI.Color.FromArgb(255, 160, 32, 240);
    private static BridgeNodeRenderStyles FocusStyles()
    {
        static Dictionary<string, BridgeComputedStyleValue> State(string color) => new()
        {
            ["background"] = new() { Kind = WrssValueKind.Color, Text = color },
        };
        var focused = State("#A020F0");
        focused["outline-color"] = new() { Kind = WrssValueKind.Color, Text = "#ffffff" };
        focused["outline-width"] = new() { Kind = WrssValueKind.Length, Text = "3px", Number = 3, Unit = "px" };
        return new() { Base = State("#163650"), Focused = focused, Pressed = State("#D03020") };
    }
    public ValueTask DisposeAsync()
    { pinnedStyle?.Dispose(); mainStyle?.Dispose(); pin?.Dispose(); pin = null; return ValueTask.CompletedTask; }
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
