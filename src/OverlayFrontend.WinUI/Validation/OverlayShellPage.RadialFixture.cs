using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void ConfigureRadialFixtureScale(double zoom, SurfaceExtent viewport, double textScale = 1)
    {
        Appearance = Appearance with { InterfaceScale = zoom, TextScale = textScale };
        ConfigureProductionViewport(viewport);
    }
    internal async Task<IReadOnlyList<string>> ValidateRadialFixtureAsync()
    {
        if (owner is not null || startup is not null || surface?.CurrentBinding is not { } binding)
            throw new InvalidOperationException("Radial fixture requires a layout-only shell.");
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        var original = Appearance;
        ShellPalette = Validation.ShellChromeValidationPage.Palette("builtin-neon-circuit");
        RefreshShellChrome();
        catalogItems[0] = catalogItems[0] with { Name = "YouTube Music" };
        activeWidget = requestedWidget = binding.Frame.Descriptor.Id;
        Tray.SelectedIndex = 0;
        ShowPresentationStatus("Radial fixture");
        SetInteractive(true);
        FocusDirectionalTray();
        UpdateLayout();
        Check(!interactive && !RadialOpen && Tray.IsEnabled && RailControls.IsHitTestVisible,
            "directional root exit enters the rail");
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
        Check(RadialOpen, "Back from the rail opens the radial chooser");
        SetInteractive(true);
        var contentSize = new Windows.Foundation.Size(WidgetHost.ActualWidth, WidgetHost.ActualHeight);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
        UpdateLayout();
        Check(RadialOpen && radialView!.Visibility == Visibility.Visible, "unhandled widget Back opens radial chooser");
        Check(contentSize.Width == WidgetHost.ActualWidth && contentSize.Height == WidgetHost.ActualHeight,
            "wheel overlays the retained widget without changing its viewport");
        var deadline = Environment.TickCount64 + 5000;
        while (!catalogItems.Take(8).All(item => radialView!.ButtonFor(item.Id) is { IsLoaded: true }))
        {
            if (Environment.TickCount64 >= deadline) throw new TimeoutException("Radial native controls did not load.");
            await Task.Delay(16);
        }
        Check(catalogItems.Take(8).All(item => radialView!.ButtonFor(item.Id) is { IsLoaded: true }), "eight native widget buttons realize the current radial page");
        Check(catalogItems.Take(8).All(item => HasVisibleIcon(radialView!.ButtonFor(item.Id)!)), "each realized radial button contains a visible native icon");
        Check(radialView!.SectorSurface.VisibleSectorCount == 8 && radialView.SectorSurface.SelectedSectorCount == 1,
            "radial chrome has eight separated sectors and one selected wedge");
        var hint = Descendants(radialView).OfType<FontIcon>().Single(element => AutomationProperties.GetAutomationId(element) == "Overlay.Radial.PagingHint");
        Check(hint.FontSize == 48 && hint.Width == 48 && hint.Height == 48, "center controller hint uses a 48-DIP optical box");
        Check(Descendants(radialView).OfType<TextBlock>().Any(text => text.Text == radialSelection ||
            text.Text == catalogItems.First(item => item.Id == radialSelection).Name) &&
            ToolTipService.GetToolTip(radialView.ButtonFor(catalogItems[0].Id)!) is null,
            "the wheel shows the selected name in the hub and leaves the radial hints unobstructed");
        var beforeWidget = activeWidget;
        var beforeSelection = radialSelection;
        PageWithStick(24000); UpdateLayout();
        Check(radialPage == 1 && activeWidget == beforeWidget && radialSelection == beforeSelection,
            "radial page browsing preserves selection and current widget authority");
        Check(NeutralPagingFocus(), "right-stick forward paging focuses the neutral hub, never an arrow");
        PageWithStick(-24000); UpdateLayout();
        Check(radialPage == 0 && NeutralPagingFocus(), "right-stick backward paging also preserves neutral hub focus");
        PageWithStick(24000); UpdateLayout();
        StepRadialSelection(FocusNavigationDirection.Right); UpdateLayout();
        Check(radialSelection == catalogItems[8].Id, "D-pad enters the browsed page without falling into widget input");
        BrowseRadialPage(1); UpdateLayout();
        Check(radialPage == 2 && radialView!.ButtonFor(catalogItems[16].Id) is not null && radialView.ButtonFor(catalogItems[0].Id) is null,
            "short final page has only current native targets");
        Check(radialView!.SectorSurface.VisibleSectorCount == catalogItems.Count - 16,
            "short final radial page paints no empty decorative sectors");
        // This fixture has no worker/session. Runtime B/A admission and exact
        // focus restoration are covered by the actual-package SharedUx checks.
        SetInteractive(true);
        Check(interactive && !RadialOpen && activeWidget == beforeWidget, "entering widget presentation closes radial chrome without changing its identity");

        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
        UpdateLayout();
        Check(RadialOpen && radialPage == 0, "fresh radial entry restores the current widget page");
        Appearance = original with { Contrast = ContrastPreference.High, Motion = MotionPreference.Reduced };
        RefreshShellChrome(); UpdateLayout();
        radialView!.Open(WidgetRail.OverlayFrontend.WinUI.Motion.WidgetMotionOptions.From(Appearance, true));
        Check(radialView.ButtonFor(catalogItems[0].Id)!.UseSystemFocusVisuals && !radialView.HasElevation,
            "high-contrast radial uses native focus and removes decorative shadow");
        Check(radialView.LastEntranceDuration == TimeSpan.Zero, "reduced motion disables radial entrance animation");
        Appearance = original with { TextScale = 1.5 };
        ShellPalette = Validation.ShellChromeValidationPage.Palette("builtin-redline");
        RefreshShellChrome(); UpdateLayout();
        Check(radialView.HasElevation && radialView.ActualWidth > 0, "theme/text-scale changes retain the same elevated chooser controls");

        ShellPalette = Validation.ShellChromeValidationPage.Palette("builtin-neon-circuit");
        RefreshShellChrome(); UpdateLayout();
        radialView.Update(catalogItems.Take(8).ToArray(), catalogItems[0].Id, 0, ShellPalette, Appearance, true);
        UpdateLayout();
        Check(AutomationProperties.GetName(hint) == "Left stick chooses widget" &&
            Descendants(radialView).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) is "Overlay.Radial.Previous" or "Overlay.Radial.Next")
                .All(button => button.Visibility == Visibility.Collapsed),
            "single-page wheel keeps a useful large selection hint without paging arrows");
        RefreshRadialChooser(); UpdateLayout();
        // Pixel qualification is performed by Test-WinUiRadial.ps1 against
        // the composed window. RenderTargetBitmap omits the Z-translated chrome.
        if (Environment.GetCommandLineArgs().Contains("--tray-favorites-fixture"))
        {
            var assigned = ShellPreferences.Empty with { TrayActions = new(Up: new("widgetrail.samples.spotify", "play-pause",
                "widgetrail.samples", Package: "widgetrail.samples.spotify")) };
            preferences = assigned;
            foreach (var path in new[] { "samples/SpotifyWidget", "samples/YouTubeWidget", "samples/DiscordWebViewWidget", "src/FirstPartyWidgets/MediaSessionsWidget" })
            {
                var file = Path.Combine(Environment.GetEnvironmentVariable("WRAIL_TRAY_FIXTURE_ROOT")!, path, "manifest.json");
                var manifest = ManifestJson.Deserialize(File.ReadAllBytes(file));
                var descriptor = catalogItems[0] with { Id = manifest.Id, PackageId = manifest.Id, Name = manifest.Name, PublisherId = manifest.Publisher, TrayActions = manifest.TrayActions };
                favoriteProviders.Add(descriptor.Id, descriptor);
            }
            RefreshRadialChooser(); UpdateLayout();
            var top = Descendants(radialView).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Overlay.Radial.Favorite.Up");
            Check(top.Visibility == Visibility.Visible && AutomationProperties.GetName(top) == "D-pad Up, Play or pause" && !top.IsEnabled, "An assigned unavailable favorite stays visible");
            var upGlyph = Descendants(top).OfType<FontIcon>().Single();
            Check(upGlyph.ActualWidth > 0 && AutomationProperties.GetName(upGlyph) == "Directional pad up" &&
                upGlyph.TransformToVisual(top).TransformPoint(new(0,0)).X < Descendants(top).OfType<TextBlock>().Single(t => t.Text == "Play or pause").TransformToVisual(top).TransformPoint(new(0,0)).X,
                "The native controller glyph precedes the radial label");
            var center = radialView.ButtonFor(radialSelection!)!;
            Check(top.TransformToVisual(radialView).TransformPoint(new(0,top.ActualHeight)).Y <
                center.TransformToVisual(radialView).TransformPoint(new(0,0)).Y, "The Up label sits outside the top sector");
            var contour = Descendants(radialView).OfType<RadialContourStage>().Single();
            var builds = contour.GeometryBuilds;
            RefreshRadialChooser(); UpdateLayout();
            PageWithStick(24000); UpdateLayout();
            PageWithStick(-24000); UpdateLayout();
            Check(builds > 0 && contour.GeometryBuilds == builds, "Wheel refresh and page navigation reuse the measured contour");
            reordering = true; RefreshRadialChooser(); UpdateLayout();
            reordering = false; RefreshRadialChooser(); UpdateLayout();
            Check(contour.GeometryBuilds == builds, "Reordering fades and restores the cached contour");
            radialView.SetFavorite(TrayDirection.Up, "A much longer replay action", false); UpdateLayout();
            Check(contour.GeometryBuilds > builds, "A change to hint width rebuilds the contour");
            radialView.FitStage(true, true, 900, 600, false);
            radialView.SetFavorite(TrayDirection.Left, "Mute", true);
            radialView.SetFavorite(TrayDirection.Right, "Play or pause the current track", true);
            radialView.SetFavorite(TrayDirection.Down, "Stop", false);
            UpdateLayout();
            Button Hint(TrayDirection direction) => Descendants(contour).OfType<Button>().Single(b =>
                AutomationProperties.GetAutomationId(b) == "Overlay.Radial.Favorite." + direction);
            var left = Hint(TrayDirection.Left);
            var right = Hint(TrayDirection.Right);
            Check(left.ActualWidth == right.ActualWidth && left.ActualHeight == right.ActualHeight &&
                left.ActualWidth > 0, "Different-length side labels have equal pill dimensions");
            var sideBuilds = contour.GeometryBuilds;
            radialView.SetFavorite(TrayDirection.Left, "Take screenshot", true); UpdateLayout();
            Check(sideBuilds == contour.GeometryBuilds, "A side label change reuses fixed pill and contour geometry");
            var layers = Descendants(contour).OfType<Microsoft.UI.Xaml.Shapes.Path>().Where(path => path.Fill is AcrylicBrush).ToArray();
            Check(layers.Length > 1 && layers.All(path => ReferenceEquals(path.Fill, layers[0].Fill)) &&
                layers.All(path => path.Opacity is > 0 and < 1), "The feathered contour shares one in-app acrylic brush");
            var palette = ShellChromePalette.Resolve(ShellPalette, Appearance);
            contour.ApplyAppearance(palette, Appearance with { Transparency = TransparencyPreference.Reduced }, false);
            Check(layers.All(path => path.Fill is null && path.Visibility == Visibility.Collapsed),
                "Reduced transparency disconnects the acrylic layers");
            contour.ApplyAppearance(palette, Appearance, true);
            var faceCenter = radialView.SectorSurface.TransformToVisual(contour).TransformPoint(new(
                radialView.SectorSurface.ActualWidth / 2, radialView.SectorSurface.ActualHeight / 2));
            foreach (var direction in Enum.GetValues<TrayDirection>())
            {
                var button = Hint(direction);
                var origin = button.TransformToVisual(contour).TransformPoint(new(0, 0));
                var distance = direction switch
                {
                    TrayDirection.Up => faceCenter.Y - origin.Y - button.ActualHeight,
                    TrayDirection.Right => origin.X - faceCenter.X,
                    TrayDirection.Down => origin.Y - faceCenter.Y,
                    _ => faceCenter.X - origin.X - button.ActualWidth
                };
                Check(Math.Abs(distance - (184 + 18)) < 1, $"{direction} hint has an 18-DIP rim gap");
            }
            // Restore the shell-owned assignments after exercising the view directly.
            foreach (var direction in Enum.GetValues<TrayDirection>())
                radialView.SetFavorite(direction, null, false);
            Array.Clear(displayedFavorites);
            RefreshRadialChooser(); UpdateLayout();
            var frame = ControllerFrame.Create(); frame.Connected = 1; frame.State.Buttons = frame.PressedButtons = 8;
            frame.DpadNavigation = new() { Phase = NavigationPhase.Pressed, Direction = NavigationDirection.Right };
            foreground = true;
            if (Environment.GetCommandLineArgs().Contains("--tray-favorites-perf"))
            {
                var idle = ControllerFrame.Create(); idle.Connected = 1;
                var held = idle; held.State.LeftThumbY = 24000;
                foreach (var sample in new[] { idle, held })
                {
                    var mode = sample.State.LeftThumbY == 0 ? "idle" : "held-stick";
                    for (var pass = 0; pass < 6; pass++)
                    {
                        var enabled = pass % 2 == 0;
                        preferences = enabled ? assigned : ShellPreferences.Empty;
                        RefreshRadialChooser();
                        for (var warm = 0; warm < 100; warm++) Receive(sample);
                        var bytes = GC.GetAllocatedBytesForCurrentThread();
                        var begin = System.Diagnostics.Stopwatch.GetTimestamp();
                        for (var i = 0; i < 1000; i++) Receive(sample);
                        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(begin);
                        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                        checks.Add($"tray perf {mode} enabled={enabled} samples=1000 ms={elapsed.TotalMilliseconds:F2} bytes={allocated}");
                    }
                }
                preferences = assigned; RefreshRadialChooser();
            }
            if (Environment.GetCommandLineArgs().Contains("--tray-favorites-picker"))
            {
                var selectedBefore = radialSelection;
                Receive(frame);
                await Task.Delay(400);
                Check(HostChoiceActive && radialSelection == selectedBefore, "Empty Right opens assignment and preserves the selected widget");
            }
            else if (Environment.GetCommandLineArgs().Contains("--tray-favorites-rail"))
            {
                SetInteractive(true);
                FocusDirectionalTray();
                await Task.Delay(250);
                PublishControllerGuide(); UpdateLayout();
                Check(!RadialOpen && !interactive && FocusedTrayWidget() is not null, "Rail guide returns to its selected widget icon");
            }
        }
        return checks;

        void PageWithStick(short horizontal)
        {
            var frame = ControllerFrame.Create(); frame.Connected = 1;
            ReceiveRadialNavigation(frame);
            frame.State.RightThumbX = horizontal;
            ReceiveRadialNavigation(frame);
        }
        bool NeutralPagingFocus() => FocusManager.GetFocusedElement(XamlRoot) is Control focused &&
            AutomationProperties.GetAutomationId(focused) == "Overlay.Radial.Paging" && !focused.UseSystemFocusVisuals &&
            Descendants(radialView!).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) is "Overlay.Radial.Previous" or "Overlay.Radial.Next")
                .All(button => button.FocusState == FocusState.Unfocused);
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }
    private static bool HasVisibleIcon(DependencyObject parent)
    {
        if (parent is Microsoft.UI.Xaml.Controls.IconElement { ActualWidth: > 0, ActualHeight: > 0 }) return true;
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); ++index)
            if (HasVisibleIcon(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index))) return true;
        return false;
    }
}
