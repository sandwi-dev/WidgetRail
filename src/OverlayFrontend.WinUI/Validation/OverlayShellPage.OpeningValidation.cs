using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.PlatformSettings;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableOpeningValidation(string path)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            try
            {
                if (startup is not null) await startup;
                var indicator = openingIndicator!;
                await SelectAsync("settings", true);
                await Task.Delay(250);
                var item = catalogItems.First(widget => widget.Id == "settings");
                var count = indicator.ShowCount;
                indicator.Begin(-1, item); indicator.Complete(-1);
                await Task.Delay(250);
                Check(!indicator.IsShowing && indicator.ShowCount == count, "fast completion never presents the badge");
                indicator.Begin(-2, item);
                await Task.Delay(300);
                Check(indicator.IsShowing && !indicator.IsTabStop && !indicator.IsHitTestVisible, "slow opening is visible and passive");
                var glyph = Descendants(indicator).OfType<Presentation.WidgetPackageIconView>().Single();
                var ring = Descendants(indicator).OfType<Microsoft.UI.Xaml.Shapes.Ellipse>().Single();
                var glyphCenter = glyph.TransformToVisual(indicator).TransformPoint(new(glyph.ActualWidth / 2, glyph.ActualHeight / 2));
                var ringCenter = ring.TransformToVisual(indicator).TransformPoint(new(ring.ActualWidth / 2, ring.ActualHeight / 2));
                Check(Math.Abs(glyphCenter.X - ringCenter.X) < .5 && Math.Abs(glyphCenter.Y - ringCenter.Y) < .5,
                    $"icon and halo share a center ({glyphCenter.X:F2},{glyphCenter.Y:F2}; {ringCenter.X:F2},{ringCenter.Y:F2})");
                Check(AutomationProperties.GetName(indicator) == "Opening " + item.Name, "accessible loading identity names the destination");
                indicator.ApplyAppearance(ShellPalette, Appearance with { Motion = MotionPreference.Reduced }, true);
                Check(!indicator.IsPulsing && indicator.IsShowing, "reduced motion retains static loading feedback");
                indicator.ApplyAppearance(ShellPalette, Appearance with { Motion = MotionPreference.Full, Contrast = ContrastPreference.High }, true);
                Check(!indicator.IsPulsing && indicator.IsShowing, "high contrast retains static loading feedback");
                indicator.ApplyAppearance(ShellPalette, Appearance with { Motion = MotionPreference.Full, Contrast = ContrastPreference.Standard }, true);
                Check(indicator.IsPulsing, "normal motion uses a compositor pulse");
                indicator.Complete(-3);
                Check(indicator.IsShowing, "stale completion cannot dismiss a newer opening");
                var loadingWidth = indicator.ActualWidth;
                indicator.Complete(-2);
                Check(indicator.IsCompleting && indicator.StatusText == "Ready" && !indicator.IsPulsing,
                    "completion immediately reports Ready and settles the loading pulse");
                indicator.UpdateLayout();
                Check(Math.Abs(indicator.ActualWidth - loadingWidth) < .5, "Ready label preserves the badge footprint");
                await WaitForCompletion();
                Check(!indicator.IsShowing && !indicator.IsPulsing, "completion retires fade and pulse");
                indicator.ApplyAppearance(ShellPalette, Appearance, systemUi.AnimationsEnabled);
                indicator.Begin(-6, item);
                var arrivalDeadline = Environment.TickCount64 + 1000;
                while (!indicator.IsShowing && Environment.TickCount64 < arrivalDeadline) await Task.Delay(5);
                await Task.Delay(40);
                indicator.Complete(-6);
                Check(indicator.IsCompleting && indicator.StatusText == "Ready", "approximately 250ms load retargets arrival directly into completion");
                var finishingPosition = indicator.Margin;
                var priorHeight = WidgetSurface.Height;
                WidgetSurface.Height = priorHeight + 100;
                PositionOpeningIndicator();
                Check(indicator.Margin == finishingPosition, "completion holds its position across destination resize");
                WidgetSurface.Height = priorHeight;
                await WaitForCompletion();
                Check(!indicator.IsShowing, "quick completion has no minimum display hold");

                // Delay admission, not the widget or provider. Exercise the real
                // shell selection transaction without invoking widget actions.
                await transitions.WaitAsync();
                Task first, latest;
                var readyWhileFading = false;
                void ObserveCompletion(object? sender, object args)
                { if (!switching && activeWidget == "settings" && indicator.IsCompleting) readyWhileFading = true; }
                CompositionTarget.Rendering += ObserveCompletion;
                try
                {
                    var outgoing = surface;
                    first = SelectAsync("widgetrail.samples.sdk-gallery", false);
                    var focus = FocusManager.GetFocusedElement(XamlRoot);
                    await Task.Delay(300);
                    Check(indicator.IsShowing && indicator.WidgetName == catalogItems.First(widget => widget.Id == "widgetrail.samples.sdk-gallery").Name,
                        "rail selection exposes the pending widget");
                    Check(ReferenceEquals(outgoing, surface) && surface!.Visibility == Visibility.Visible,
                        "old content remains presented during admission");
                    Check(ReferenceEquals(focus, FocusManager.GetFocusedElement(XamlRoot)), "badge never moves focus");
                    await Capture("rail");
                    latest = SelectAsync("settings", false);
                    await Task.Delay(300);
                    Check(indicator.IsShowing && indicator.WidgetName == item.Name, "rapid replacement shows only the latest destination");
                    PrepareRadialBackEntry(); RefreshRadialChooser(); PositionOpeningIndicator();
                    await Task.Delay(100);
                    if (RadialOpen)
                    {
                        var badge = indicator.TransformToVisual(ProductionLayout).TransformPoint(new(0, 0));
                        var wheel = radialView!.TransformToVisual(ProductionLayout).TransformPoint(new(0, 0));
                        await Capture("radial");
                        Check(badge.Y + indicator.ActualHeight <= wheel.Y, $"radial loading badge clears wheel controls ({badge.Y + indicator.ActualHeight:F1} <= {wheel.Y:F1})");
                    }
                }
                finally { transitions.Release(); }
                await Task.WhenAll(first, latest);
                await WaitForCompletion();
                CompositionTarget.Rendering -= ObserveCompletion;
                Check(readyWhileFading, "widget commits without waiting for completion feedback");
                Check(activeWidget == "settings" && !indicator.IsShowing && !switching, "committed latest surface clears loading");
                indicator.Begin(-4, item); await Task.Delay(300);
                SetVisible(false);
                Check(!indicator.IsShowing && !indicator.IsPulsing, "overlay hide retires feedback immediately");
                SetVisible(true);
                await Task.Delay(800);
                indicator.Begin(-5, item); await Task.Delay(300);
                ShowRecovery("Validation recovery", false);
                Check(!indicator.IsShowing && !indicator.IsPulsing, "recovery replaces pending feedback");
                ShowPresentationStatus(item.Name);
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }

            void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
            async Task WaitForCompletion()
            {
                var deadline = Environment.TickCount64 + 2000;
                while (openingIndicator!.IsShowing && Environment.TickCount64 < deadline) await Task.Delay(20);
                Check(!openingIndicator.IsShowing, "completion finishes within its animation budget");
            }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, error }));
            }
            async Task Capture(string name)
            {
                var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(ProductionLayout);
                var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
                await encoder.FlushAsync(); stream.Seek(0);
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
                await File.WriteAllBytesAsync(Path.ChangeExtension(path, name + ".png"), bytes);
            }
        };
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
