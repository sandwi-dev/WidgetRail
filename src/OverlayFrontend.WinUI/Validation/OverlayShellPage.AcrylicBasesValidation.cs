using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableAcrylicBasesValidation(string output)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        var profile = Path.GetFullPath(options.SettingsRoot);
        if (!profile.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            profile == PlatformSettingsPaths.CreateDefault().RootDirectory)
            throw new InvalidOperationException("Acrylic validation requires an isolated profile under its result directory.");
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>();
            var samples = new List<object>();
            var store = new PlatformSettingsStore(new(profile));
            PlatformSettingsDocument? original = null;
            Window? background = null;
            string? failure = null, frameFailure = null;
            var observe = false;
            var frames = 0;
            double bare = 0;
            double testedScale = 0;
            void Frame(object? sender, object args)
            {
                if (!observe || !visible || WidgetSurface.Visibility != Visibility.Visible) return;
                ++frames;
                if (!WidgetSurface.HasDesktopAcrylic) frameFailure ??= "Desktop material detached during a transition.";
                var color = SampleBase();
                if (frames % 10 == 0) samples.Add(new { frames, activeWidget, color, switching });
                if (Brightness(color) >= bare - 10) frameFailure ??= "Base exposed the bright underlying window during a transition.";
            }
            try
            {
                original = await store.LoadAsync();
                if (startup is not null) await startup;
                var wide = Environment.GetCommandLineArgs().Contains("--acrylic-test-wide-display");
                if (wide)
                {
                    // Use indexed WinRT vector access; the optional IEnumerable
                    // projection is unavailable in this validation runtime.
                    var displays = Microsoft.UI.Windowing.DisplayArea.FindAll();
                    var display = displays[0];
                    for (var index = 1; index < displays.Count; index++)
                        if (displays[index].OuterBounds.Width > display.OuterBounds.Width) display = displays[index];
                    App.Window!.AppWindow.MoveAndResize(display.OuterBounds);
                    await Task.Delay(250);
                }
                await store.UpdateAsync(value => value with { Appearance = AppearanceSettings.Default with
                {
                    Contrast = ContrastPreference.Standard, AcrylicEffects = true,
                    BackdropOpacity = .3, Motion = MotionPreference.Full, WidgetAnimationSpeed = .5,
                    InterfaceScale = wide ? .9 : 1,
                } });
                await Wait(() => Appearance.AcrylicEffects && Appearance.WidgetAnimationSpeed == .5);
                testedScale = Appearance.InterfaceScale;
                background = new Window { Title = "Acrylic test background", Content = new Grid
                    { Background = new SolidColorBrush(Microsoft.UI.Colors.White) } };
                var main = App.Window!.AppWindow;
                background.AppWindow.MoveAndResize(new(main.Position.X, main.Position.Y, main.Size.Width, main.Size.Height));
                background.AppWindow.Show(false);
                await SelectAsync("audio-mixer", true);
                await Wait(() => !switching && WidgetSurface.HasDesktopAcrylic && surface?.WidgetResizeCompletion?.IsCompleted != false);
                await Task.Delay(250);
                WidgetSurface.Visibility = Visibility.Collapsed;
                await Task.Delay(100);
                bare = Brightness(SampleBase());
                WidgetSurface.Visibility = Visibility.Visible;
                await Task.Delay(200);
                Check(bare > 100 && Brightness(SampleBase()) < bare - 10, "Screen probe distinguishes desktop acrylic from a transparent hole");
                CompositionTarget.Rendering += Frame;
                observe = true;
                var widgets = Environment.GetCommandLineArgs().Contains("--acrylic-test-expanded-widgets")
                    ? new[] { "settings", "network-controls", "marketplace", "audio-mixer", "power", "media-sessions", "games-apps", "capture-library", "performance-monitor" }
                    : new[] { "settings", "network-controls", "marketplace", "audio-mixer" };
                foreach (var id in widgets)
                {
                    await SelectAsync(id, false);
                    await Wait(() => !switching && activeWidget == id && WidgetSurface.HasDesktopAcrylic);
                    if (surface!.WidgetResizeCompletion is { } resize) await resize.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(SurfaceHints?.Appearance == WidgetSurfaceAppearance.DesktopAcrylic, id + " declares the host-owned desktop base");
                    surface.ResizeWidgetContent(new((float)surface.Width * .8f, (float)surface.Height * .8f), WidgetSurface);
                    if (surface.WidgetResizeCompletion is { } testResize) await testResize.WaitAsync(TimeSpan.FromSeconds(5));
                    await Task.Delay(100);
                    Check(frameFailure is null, id + " keeps a painted base through switching and resize: " + frameFailure);
                }
                observe = false;
                await store.UpdateAsync(value => value with { Appearance = value.Appearance with { AcrylicEffects = false } });
                await Wait(() => !Appearance.AcrylicEffects && !WidgetSurface.HasDesktopAcrylic && SurfaceBackground.Background is SolidColorBrush { Color.A: 255 });
                Check(true, "Live preference update detaches the base effect and paints its solid fallback");
                await store.UpdateAsync(value => value with { Appearance = value.Appearance with { AcrylicEffects = true } });
                await Wait(() => Appearance.AcrylicEffects && WidgetSurface.HasDesktopAcrylic);
                Check(frames > 10, "Native compositor frames were sampled during production resize paths");
                SetVisible(false); await Task.Delay(100); SetVisible(true);
                await Wait(() => visible && !switching && WidgetSurface.HasDesktopAcrylic);
                Check(true, "Hide and reopen preserve the desktop material");
            }
            catch (Exception error) { failure = error.ToString(); }
            finally
            {
                observe = false;
                CompositionTarget.Rendering -= Frame;
                WidgetSurface.Visibility = Visibility.Visible;
                background?.Close();
                if (original is not null)
                    try { await store.UpdateAsync(_ => original); }
                    catch (Exception error) { failure ??= error.ToString(); }
                Directory.CreateDirectory(directory);
                File.WriteAllText(output, JsonSerializer.Serialize(new { passed = failure is null, checks, frames, bare, samples, testedScale,
                    displayWidth = App.Window!.AppWindow.Size.Width, rasterScale = XamlRoot.RasterizationScale, error = failure },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            async Task Wait(Func<bool> predicate)
            {
                var deadline = Environment.TickCount64 + 20000;
                while (!predicate())
                {
                    if (Environment.TickCount64 > deadline) throw new TimeoutException("Acrylic base did not settle: " + activeWidget);
                    await Task.Delay(25);
                }
            }
        };
    }

    private uint SampleBase()
    {
        var point = WidgetSurface.TransformToVisual(null).TransformPoint(new(WidgetSurface.ActualWidth / 2, WidgetSurface.ActualHeight - 4));
        var origin = App.Window!.AppWindow.Position;
        var dc = AcrylicScreenDc(0);
        if (dc == 0) throw new InvalidOperationException("Screen probe could not acquire a device context.");
        try { return AcrylicScreenPixel(dc, origin.X + (int)(point.X * XamlRoot.RasterizationScale), origin.Y + (int)(point.Y * XamlRoot.RasterizationScale)); }
        finally { AcrylicReleaseDc(0, dc); }
    }
    private static double Brightness(uint color) => ((color & 255) + ((color >> 8) & 255) + ((color >> 16) & 255)) / 3d;
    [LibraryImport("user32.dll", EntryPoint = "GetDC"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint AcrylicScreenDc(nint window);
    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int AcrylicReleaseDc(nint window, nint dc);
    [LibraryImport("gdi32.dll", EntryPoint = "GetPixel"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint AcrylicScreenPixel(nint dc, int x, int y);
}
