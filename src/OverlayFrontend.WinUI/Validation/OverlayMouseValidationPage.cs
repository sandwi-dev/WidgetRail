using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Real OS mouse routing against local XAML and HTML; no service calls.</summary>
internal sealed partial class OverlayMouseValidationPage : Page
{
    internal OverlayMouseValidationPage(string output)
    {
        var button = new Button { Content = "Hover target", Width = 280, Height = 80 };
        var other = new Button { Content = "Controller focus target", Width = 280, Height = 60 };
        var clicked = 0;
        button.Click += (_, _) => clicked++;
        Task<CoreWebView2Environment>? environment = null;
        BrowserSurface.FixturePage = _ => "<!doctype html><html><title>Mouse fixture</title><body style='margin:0'><button id='target' style='width:100vw;height:100vh'>Local hover target</button><script>window.moves=0;document.addEventListener('mousemove',()=>moves++);</script></body></html>";
        var web = new BrowserSurface("fixture.mouse", new("mouse", 1, "https://mouse.fixture.invalid/", "Mouse fixture")
            { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(Path.GetDirectoryName(output)!, "web-profile"), null).AsTask())
            { Height = 240, Width = 500, IsInteractionFocused = () => true };
        var root = new Grid { Children = { new StackPanel { Spacing = 16, Children = { button, other, web } } } };
        var paintMarker = new Border { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) };
        root.Children.Add(paintMarker);
        Content = root;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            string? failure = null;
            GetCursorPos(out var original);
            OverlayMouseSession? session = null;
            try
            {
                var owner = Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
                session = new(owner, DispatcherQueue);
                web.SetPresentation(true, true);
                var deadline = Environment.TickCount64 + 15000;
                while (!web.IsReady || web.IsNavigationInProgress || web.CurrentTitle != "Mouse fixture")
                {
                    if (Environment.TickCount64 > deadline) throw new TimeoutException("Browser fixture did not load");
                    await Task.Delay(50);
                }
                var foregroundDeadline = Environment.TickCount64 + 15000;
                while (!PlatformInputPump.IsForegroundProcess() && Environment.TickCount64 < foregroundDeadline)
                    await Task.Delay(50);
                await Task.Delay(200);
                // Establish browser mouse support before creating any guard.
                web.Enter();
                MoveTo(web.CompositionRoot);
                await Task.Delay(100);
                MoveBy(2);
                await Task.Delay(150);
                var nativeBrowserHover = await web.EvaluateFixtureAsync("document.querySelector('#target').matches(':hover')") == "true";
                Check(nativeBrowserHover, "Native browser hover works before guard creation");

                MoveTo(button);
                await Task.Delay(150);
                Check(button.IsPointerOver, "Native mouse initially hovers XAML");
                Check(Pixel(paintMarker) == 255, "Desktop paint probe sees fixture marker");
                await OpenAsync();
                await Task.Delay(200);
                Check(session.WaitingForMovement && !CursorVisible(), "Cursor hidden while waiting");
                Check(Pixel(paintMarker) == 255, "Guard paints nothing over the overlay");
                other.Focus(FocusState.Keyboard);
                await Task.Delay(100);
                Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), other) && session.WaitingForMovement,
                    "Controller-style focus transfer works while mouse is suppressed");
                ClickMouse();
                await Task.Delay(100);
                Check(clicked == 0 && session.WaitingForMovement && !CursorVisible() && ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), other),
                    "Hidden stationary mouse cannot click or steal focus");
                session.Begin();
                await Task.Delay(100);
                Check(session.WaitingForMovement, "Duplicate begin is harmless");
                MoveBy(2);
                await Task.Delay(150);
                Check(!session.WaitingForMovement && CursorVisible(), "First movement restores cursor");
                MoveBy(2);
                await Task.Delay(100);
                Check(button.IsPointerOver, "XAML hover resumes after movement");
                await OpenAsync();
                await Task.Delay(100);
                Check(session.WaitingForMovement && !CursorVisible(), "Every new opening hides again");
                session.End();
                session.End();
                await Task.Delay(100);
                Check(CursorVisible(), "Closing without movement restores cursor idempotently");

                web.Enter();
                MoveTo(web.CompositionRoot);
                await Task.Delay(100);
                MoveBy(2);
                await Task.Delay(200);
                await OpenAsync();
                await Task.Delay(200);
                Check(session.WaitingForMovement && !CursorVisible(), "Cursor hidden over WebView");
                Check(NativeHit().Contains("class=WidgetRail.MouseGuard", StringComparison.Ordinal), "Native guard owns stationary browser mouse hit testing");
                web.Enter();
                web.MovePointer(.05, .05);
                await Task.Delay(150);
                Check(await HtmlHover() && session.WaitingForMovement && !CursorVisible(), "Controller browser pointer works while desktop mouse stays hidden");
                await web.EvaluateFixtureAsync("document.body.style.paddingTop='1px'");
                await Task.Delay(100);
                Check(session.WaitingForMovement && !CursorVisible(), "Browser relayout does not restore cursor");
                MoveBy(2);
                await Task.Delay(100);
                MoveBy(2);
                await Task.Delay(100);
                Check(!session.WaitingForMovement && CursorVisible() && !NativeHit().Contains("class=WidgetRail.MouseGuard", StringComparison.Ordinal), "Movement restores browser mouse admission");
                Check(await HtmlHover(), "Native browser hover resumes after movement");
                await OpenAsync();
                session.Dispose(); session.Dispose();
                await Task.Delay(100);
                Check(CursorVisible(), "Disposal while waiting restores cursor");
                async Task<bool> HtmlHover() => await web.EvaluateFixtureAsync("document.querySelector('#target').matches(':hover')") == "true";
                async Task OpenAsync()
                {
                    web.SetPresentation(false, false);
                    App.Window.AppWindow.Hide();
                    await Task.Delay(150);
                    App.Window.AppWindow.Show();
                    App.Window.Activate();
                    session.Begin();
                    web.SetPresentation(true, true);
                }
            }
            catch (Exception error) { failure = error.ToString(); }
            finally
            {
                session?.Dispose();
                SetCursorPos(original.X, original.Y);
                await web.DisposeAsync();
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null, checks, failure }, new JsonSerializerOptions { WriteIndented = true }));
            }
            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException(name);
                checks.Add(name);
            }
        };
    }

    private static void MoveTo(FrameworkElement target)
    {
        if (!PlatformInputPump.IsForegroundProcess()) throw new InvalidOperationException("Probe no longer owns foreground");
        var bounds = target.TransformToVisual(null).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        var scale = target.XamlRoot.RasterizationScale;
        var point = new Point { X = (int)((bounds.X + bounds.Width / 2) * scale), Y = (int)((bounds.Y + bounds.Height / 2) * scale) };
        var owner = Microsoft.UI.Win32Interop.GetWindowFromWindowId(target.XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (ClientToScreen(owner, ref point) == 0 || SetCursorPos(point.X, point.Y) == 0) throw new InvalidOperationException("Cannot position probe mouse");
        MoveBy(1);
    }
    private static uint Pixel(FrameworkElement target)
    {
        var bounds = target.TransformToVisual(null).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        var scale = target.XamlRoot.RasterizationScale;
        var point = new Point { X = (int)((bounds.X + bounds.Width / 2) * scale), Y = (int)((bounds.Y + bounds.Height / 2) * scale) };
        var owner = Microsoft.UI.Win32Interop.GetWindowFromWindowId(target.XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (ClientToScreen(owner, ref point) == 0) throw new InvalidOperationException("Cannot locate paint probe");
        var dc = GetDC(0);
        if (dc == 0) throw new InvalidOperationException("Cannot sample desktop paint");
        try { return GetPixel(dc, point.X, point.Y); }
        finally { ReleaseDC(0, dc); }
    }
    private static unsafe void MoveBy(int pixels)
    {
        if (!PlatformInputPump.IsForegroundProcess()) throw new InvalidOperationException("Probe no longer owns foreground");
        var input = new NativeInput { Data = new() { X = pixels, Flags = 1 } };
        if (SendInput(1, &input, sizeof(NativeInput)) != 1) throw new InvalidOperationException("Cannot move probe mouse");
    }
    private static unsafe void ClickMouse()
    {
        if (!PlatformInputPump.IsForegroundProcess()) throw new InvalidOperationException("Probe no longer owns foreground");
        var inputs = stackalloc NativeInput[2];
        inputs[0] = new() { Data = new() { Flags = 2 } };
        inputs[1] = new() { Data = new() { Flags = 4 } };
        if (SendInput(2, inputs, sizeof(NativeInput)) != 2) throw new InvalidOperationException("Cannot click probe mouse");
    }
    private static unsafe string NativeHit()
    {
        GetCursorPos(out var point);
        var window = WindowFromPoint(point);
        var name = stackalloc char[256];
        var length = GetClassNameW(window, name, 256);
        return $"hwnd={window} class={new string(name, 0, length)}";
    }
    private static bool CursorVisible()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (GetCursorInfo(ref info) == 0) throw new InvalidOperationException("Cannot read cursor visibility");
        return (info.Flags & 1) != 0 && info.Cursor != 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { internal uint Type; internal MouseInput Data; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { internal int Size; internal uint Flags; internal nint Cursor; internal Point Position; }
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ClientToScreen(nint window, ref Point point);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int SetCursorPos(int x, int y);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetCursorInfo(ref CursorInfo info);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static unsafe partial uint SendInput(uint count, NativeInput* inputs, int size);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint WindowFromPoint(Point point);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static unsafe partial int GetClassNameW(nint window, char* name, int count);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("gdi32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint GetPixel(nint dc, int x, int y);
}
