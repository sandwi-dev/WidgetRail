using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GameBarAlternative.AvaloniaPrototype.Diagnostics;
using GameBarAlternative.AvaloniaPrototype.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class ResponsiveShellTests
{
    private static readonly Size[] LogicalFixtures =
    [
        new(1280, 720),
        new(1920, 1080),
        new(2560, 1440),
    ];

    private static readonly double[] Scales = [1d, 1.25d, 1.5d];

    [TestMethod]
    public async Task Window_contract_is_transparent_borderless_topmost_and_absent_from_taskbar()
    {
        await RunOnUiThreadAsync(() =>
        {
            var window = new MainWindow();
            Assert.AreEqual(WindowDecorations.None, window.WindowDecorations);
            Assert.IsTrue(window.Topmost);
            Assert.IsFalse(window.ShowInTaskbar);
            CollectionAssert.Contains(window.TransparencyLevelHint.ToArray(), WindowTransparencyLevel.Transparent);
            Assert.AreEqual(0, ((Avalonia.Media.ISolidColorBrush)window.Background!).Color.A);
            window.Close();
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [Timeout(45_000)]
    public async Task Every_page_emits_contained_required_bounds_at_all_fixtures_and_scales()
    {
        await RunOnUiThreadAsync(async () =>
        {
            foreach (var logicalSize in LogicalFixtures)
            {
                foreach (var scale in Scales)
                {
                    foreach (var route in Enum.GetValues<PrototypeRoute>())
                    {
                        using var shell = new PrototypeShellView
                        {
                            Width = logicalSize.Width,
                            Height = logicalSize.Height,
                        };
                        shell.Measure(logicalSize);
                        shell.Arrange(new Rect(logicalSize));
                        var result = await shell.NavigateAsync(route);
                        shell.Measure(logicalSize);
                        shell.Arrange(new Rect(logicalSize));

                        Assert.AreEqual(Navigation.NavigationOutcome.Committed, result.Outcome);
                        var frame = shell.CaptureFrame(route, result.LoadDuration);
                        Assert.IsTrue(frame.TransparentRoot, $"{route} root was not transparent.");
                        Assert.IsTrue(frame.OpaqueBlackFallbackAbsent, $"{route} emitted an opaque black fallback.");
                        Assert.IsTrue(frame.RequiredElementsContained, $"{route} escaped {logicalSize} at {scale:P0}.");
                        Assert.IsTrue(frame.Tray.IsContainedBy(frame.Client), $"Tray escaped {logicalSize} at {scale:P0}.");

                        foreach (var rect in frame.RequiredElements.Values)
                        {
                            var physical = new DiagnosticRect(
                                rect.X * scale,
                                rect.Y * scale,
                                rect.Width * scale,
                                rect.Height * scale);
                            var physicalClient = new DiagnosticRect(0, 0, logicalSize.Width * scale, logicalSize.Height * scale);
                            Assert.IsTrue(physical.IsContainedBy(physicalClient), $"Scaled {route} bound escaped at {scale:P0}.");
                        }
                    }
                }
            }
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Stationary_tray_bounds_do_not_change_across_page_switches()
    {
        await RunOnUiThreadAsync(async () =>
        {
            using var shell = new PrototypeShellView { Width = 1280, Height = 720 };
            shell.Measure(new Size(1280, 720));
            shell.Arrange(new Rect(0, 0, 1280, 720));
            DiagnosticRect? baseline = null;
            foreach (var route in Enum.GetValues<PrototypeRoute>())
            {
                await shell.NavigateAsync(route);
                shell.Measure(new Size(1280, 720));
                shell.Arrange(new Rect(0, 0, 1280, 720));
                var tray = shell.CaptureFrame(route, TimeSpan.Zero).Tray;
                baseline ??= tray;
                Assert.AreEqual(baseline, tray);
            }
        });
    }

    private static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
