using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
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
                    var window = new Window
                    {
                        Width = logicalSize.Width,
                        Height = logicalSize.Height,
                    };
                    window.SetRenderScaling(scale);
                    using var shell = new PrototypeShellView();
                    window.Content = shell;
                    window.Show();
                    Assert.AreEqual(scale, window.RenderScaling, 0.001, "Fixture must exercise Avalonia's actual render scaling.");
                    foreach (var route in Enum.GetValues<PrototypeRoute>())
                    {
                        var result = await shell.NavigateAsync(route);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        using var rendered = window.CaptureRenderedFrame();

                        Assert.AreEqual(Navigation.NavigationOutcome.Committed, result.Outcome);
                        Assert.IsNotNull(rendered, "Headless backend must produce a rendered frame.");
                        Assert.AreEqual((int)Math.Ceiling(logicalSize.Width * scale), rendered.PixelSize.Width);
                        Assert.AreEqual((int)Math.Ceiling(logicalSize.Height * scale), rendered.PixelSize.Height);
                        var frame = shell.CaptureFrame(route, result.LoadDuration);
                        Assert.IsTrue(frame.TransparentRoot, $"{route} root was not transparent.");
                        Assert.IsTrue(frame.OpaqueBlackFallbackAbsent, $"{route} emitted an opaque black fallback.");
                        Assert.IsTrue(frame.RequiredElementsContained, $"{route} escaped {logicalSize} at {scale:P0}.");
                        Assert.IsTrue(frame.Tray.IsContainedBy(frame.Client), $"Tray escaped {logicalSize} at {scale:P0}.");
                        Assert.IsTrue(frame.AllVisibleRequiredElementsValid, $"{route} has an invalid authored Button/TextBlock bound at {scale:P0}.");
                        CollectionAssert.AreEquivalent(
                            ExpectedRequiredIds(route),
                            frame.VisibleRequiredElements.Select(element => element.AutomationId).ToArray(),
                            $"{route} must enumerate every authored required Button/TextBlock by stable AutomationId.");
                        foreach (var element in frame.VisibleRequiredElements)
                        {
                            Assert.IsTrue(element.LayoutBounds.HasArea, $"{element.AutomationId} emitted zero layout bounds.");
                            Assert.IsTrue(element.ClippedByScrollViewport || element.VisibleBounds is { HasArea: true },
                                $"{element.AutomationId} was neither visibly contained nor honestly clipped by a ScrollViewer.");
                        }
                    }

                    window.Close();
                }
            }
        });
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Every_transition_records_start_midpoint_and_completion_surface_diagnostics()
    {
        await RunOnUiThreadAsync(async () =>
        {
            FrameDiagnostics.Reset();
            var window = new Window { Width = 1280, Height = 720 };
            using var shell = new PrototypeShellView();
            window.Content = shell;
            window.Show();

            foreach (var route in Enum.GetValues<PrototypeRoute>())
            {
                await shell.NavigateAsync(route);
                var samples = FrameDiagnostics.RecordedTransitions.Where(sample => sample.Route == route).ToArray();
                CollectionAssert.AreEqual(Enum.GetValues<Navigation.TransitionPhase>(), samples.Select(sample => sample.Phase).ToArray());
                Assert.IsTrue(samples.All(sample => sample.TransparentRoot));
                Assert.IsTrue(samples.All(sample => sample.OpaqueBlackBrushAbsent));
                Assert.IsTrue(samples.All(sample => sample.AvaloniaSurfaceCoveragePresent));
                Assert.IsTrue(samples.All(sample => sample.VisualChildCount > 0));
            }

            window.Close();
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

    private static string[] ExpectedRequiredIds(PrototypeRoute route)
    {
        string[] tray = ["tray.settings", "tray.audio", "tray.spotify", "tray.launcher"];
        string[] page = route switch
        {
            PrototypeRoute.Settings =>
            [
                "settings.title", "settings.section.overlay", "settings.description", "settings.topmost",
                "settings.start-last", "settings.reduce-motion", "settings.scale.label",
                "settings.accessibility.label", "settings.high-contrast", "settings.read-hints", "settings.help",
            ],
            PrototypeRoute.AudioMixer =>
            [
                "audio.title", "audio.master.label", "audio.master.mute", "audio.game.label", "audio.game.mute",
                "audio.spotify.label", "audio.spotify.mute", "audio.voice.label", "audio.voice.mute", "audio.help",
            ],
            PrototypeRoute.SpotifyPlayer =>
            [
                "spotify.title", "spotify.artwork.icon", "spotify.artwork.label", "spotify.track", "spotify.artist",
                "spotify.previous", "spotify.play", "spotify.next", "spotify.queue", "spotify.readiness", "spotify.help",
            ],
            PrototypeRoute.GameLauncher =>
            [
                "launcher.title", "launcher.search", "launcher.collection.all", "launcher.collection.favorites",
                .. Enumerable.Range(1, 16).Select(index => $"launcher.game.{index:00}"),
                "launcher.help",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        return [.. tray, .. page];
    }
}
