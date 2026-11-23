using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

// Opt-in generated-fixture diagnostics only. This observes the owning window;
// it never invokes controls, changes focus/styles, injects input or captures pixels.
internal sealed partial class ValidationWindowObserver : IDisposable
{
    private readonly Window window;
    private readonly FrameworkElement root;
    private readonly string targetId, output;
    private readonly nint handle;
    private readonly DispatcherQueueTimer timer;
    private readonly List<object> samples = [];
    private readonly PointerEventHandler pointerHandler;
    private readonly KeyEventHandler keyHandler;
    private int pointerPresses, keyDowns, focusEvents;
    private string activation = "not-observed";
    private bool disposed;

    internal ValidationWindowObserver(Window window, FrameworkElement root, string targetId, string output)
    {
        this.window = window; this.root = root; this.targetId = targetId; this.output = Path.GetFullPath(output);
        if (string.IsNullOrWhiteSpace(targetId) || targetId.Length > 128 ||
            !Directory.Exists(Path.GetDirectoryName(this.output)) || File.Exists(this.output))
            throw new ArgumentException("Window diagnostics require a bounded target ID and fresh file in an existing fixture directory.");
        handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        pointerHandler = (_, _) => pointerPresses++;
        keyHandler = (_, _) => keyDowns++;
        root.AddHandler(UIElement.PointerPressedEvent, pointerHandler, true);
        root.AddHandler(UIElement.PreviewKeyDownEvent, keyHandler, true);
        root.GotFocus += FocusChanged;
        root.Loaded += Loaded;
        window.Activated += Activated;
        timer = root.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => Capture("periodic");
        Capture("configured");
        timer.Start();
    }

    private void Loaded(object sender, RoutedEventArgs args) => Capture("loaded");
    private void FocusChanged(object sender, RoutedEventArgs args) => focusEvents++;
    private void Activated(object sender, WindowActivatedEventArgs args) => activation = args.WindowActivationState.ToString();

    private void Capture(string reason)
    {
        if (disposed || samples.Count >= 64) { timer.Stop(); return; }
        object snapshot;
        try
        {
            var native = WindowsTaskWindowActivation.Observe(handle);
            var style = (ulong)ReadWindowLong(handle, -16);
            var extended = (ulong)ReadWindowLong(handle, -20);
            var windowRectRead = ReadWindowRect(handle, out var windowRect) != 0;
            var clientRectRead = ReadClientRect(handle, out var clientRect) != 0;
            var clientOrigin = new NativePoint();
            var originRead = ReadClientToScreen(handle, ref clientOrigin) != 0;
            var affinityRead = ReadDisplayAffinity(handle, out var affinity) != 0;
            var cloakResult = ReadDwmAttribute(handle, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(uint));
            var layeredRead = ReadLayeredAttributes(handle, out var colorKey, out var alpha, out var layeredFlags) != 0;
            var dpi = ReadDpi(handle);
            var focused = root.XamlRoot is { } xamlRoot ? FocusManager.GetFocusedElement(xamlRoot) as DependencyObject : null;
            var target = FindTarget();
            snapshot = new
            {
                reason, timestamp = DateTimeOffset.UtcNow, processId = Environment.ProcessId,
                handle = (long)handle, native, activation,
                styles = new { style = style.ToString("X"), extended = extended.ToString("X"),
                    transparent = (extended & 0x20) != 0, noActivate = (extended & 0x08000000) != 0,
                    layered = (extended & 0x80000) != 0, disabled = (style & 0x08000000) != 0 },
                geometry = new { windowRectRead, window = Rect(windowRect), clientRectRead, client = Rect(clientRect),
                    originRead, origin = new { x = clientOrigin.X, y = clientOrigin.Y }, dpi,
                    xamlRasterScale = root.XamlRoot?.RasterizationScale, hostVisible = root.XamlRoot?.IsHostVisible },
                capture = new { affinityRead, affinity, cloakResult, cloaked, layeredRead, alpha, colorKey, layeredFlags },
                inputObserved = new { pointerPresses, keyDowns, focusEvents,
                    focusedType = focused?.GetType().Name,
                    focusedId = focused is null ? null : AutomationProperties.GetAutomationId(focused) },
                root = Element(root), targetId, target = target is null ? null : Target(target, dpi),
                ancestorChain = target is null ? [] : Ancestors(target),
            };
        }
        catch (Exception error)
        {
            snapshot = new { reason, timestamp = DateTimeOffset.UtcNow, observationError = error.GetType().Name, hresult = error.HResult };
        }
        samples.Add(snapshot);
        try
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, samples,
                scope = "Read-only generated-window observation. Event counts only: no key codes, text, user-window titles, pixels, input injection or control invocation. Native state does not prove pixels were rendered."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { timer.Stop(); }
        catch (UnauthorizedAccessException) { timer.Stop(); }
    }

    private FrameworkElement? FindTarget()
    {
        var pending = new Stack<DependencyObject>(); pending.Push(root);
        var visited = 0;
        while (pending.Count != 0 && visited++ < 2048)
        {
            var node = pending.Pop();
            if (node is FrameworkElement element && AutomationProperties.GetAutomationId(element) == targetId) return element;
            for (var index = VisualTreeHelper.GetChildrenCount(node) - 1; index >= 0; index--) pending.Push(VisualTreeHelper.GetChild(node, index));
        }
        return null;
    }

    private object Target(FrameworkElement target, uint dpi)
    {
        var local = target.TransformToVisual(root).TransformPoint(new(0, 0));
        var scale = dpi / 96d;
        var origin = new NativePoint { X = (int)Math.Round(local.X * scale), Y = (int)Math.Round(local.Y * scale) };
        var originRead = ReadClientToScreen(handle, ref origin) != 0;
        var width = (int)Math.Round(target.ActualWidth * scale);
        var height = (int)Math.Round(target.ActualHeight * scale);
        var center = new NativePoint { X = origin.X + width / 2, Y = origin.Y + height / 2 };
        var hit = originRead && width > 0 && height > 0 ? ReadWindowFromPoint(center) : 0;
        var hitRoot = hit == 0 ? 0 : ReadAncestor(hit, 2 /* GA_ROOT */);
        _ = ReadWindowProcess(hitRoot, out var hitProcess);
        return new { element = Element(target), originRead,
            rootDipOrigin = new { x = local.X, y = local.Y },
            screenPixelBounds = new { x = origin.X, y = origin.Y, width, height },
            hitWindow = (long)hit, hitRoot = (long)hitRoot, hitProcess,
            hitBelongsToThisWindow = hitRoot == handle };
    }

    private static object Element(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        return new { type = element.GetType().Name, id = AutomationProperties.GetAutomationId(element),
            element.IsLoaded, element.ActualWidth, element.ActualHeight, visibility = element.Visibility.ToString(),
            element.IsHitTestVisible, element.Opacity, visualOpacity = visual.Opacity,
            visualSize = new { x = visual.Size.X, y = visual.Size.Y },
            isEnabled = (element as Control)?.IsEnabled, isTabStop = (element as Control)?.IsTabStop };
    }
    private static object[] Ancestors(DependencyObject element)
    {
        var result = new List<object>();
        for (DependencyObject? node = element; node is not null && result.Count < 16; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement framework) result.Add(Element(framework));
        return result.ToArray();
    }
    private static object Rect(NativeRect value) => new { left = value.Left, top = value.Top, right = value.Right, bottom = value.Bottom };

    public void Dispose()
    {
        if (disposed) return;
        Capture("retiring"); disposed = true; timer.Stop();
        root.RemoveHandler(UIElement.PointerPressedEvent, pointerHandler);
        root.RemoveHandler(UIElement.PreviewKeyDownEvent, keyHandler);
        root.GotFocus -= FocusChanged; root.Loaded -= Loaded;
        window.Activated -= Activated;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint ReadWindowLong(nint window, int index);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ReadWindowRect(nint window, out NativeRect rect);
    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ReadClientRect(nint window, out NativeRect rect);
    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ReadClientToScreen(nint window, ref NativePoint point);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowDisplayAffinity")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ReadDisplayAffinity(nint window, out uint affinity);
    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ReadDwmAttribute(nint window, uint attribute, out uint value, uint size);
    [LibraryImport("user32.dll", EntryPoint = "GetLayeredWindowAttributes")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ReadLayeredAttributes(nint window, out uint colorKey, out byte alpha, out uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint ReadDpi(nint window);
    [LibraryImport("user32.dll", EntryPoint = "WindowFromPoint")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint ReadWindowFromPoint(NativePoint point);
    [LibraryImport("user32.dll", EntryPoint = "GetAncestor")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint ReadAncestor(nint window, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint ReadWindowProcess(nint window, out uint process);
}
