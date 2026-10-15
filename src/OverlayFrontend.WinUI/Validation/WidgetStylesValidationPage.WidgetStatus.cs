using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.OverlayFrontend.WinUI.Validation;
internal sealed partial class WidgetStylesValidationPage
{
    private async Task WidgetStatusAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        var fixtures = new[] { new WidgetStatus(8, Audio: true), new WidgetStatus(120), new WidgetStatus(Audio: true),
            new WidgetStatus(2, Stopped: true), new WidgetStatus(1, Failed: true), new WidgetStatus(4, Recording: true),
            new WidgetStatus(RemotePlayback: true), new WidgetStatus() };
        var names = new[] { "Discord", "Twitch", "Music", "Browser", "Video", "Capture", "Remote player", "Settings" };
        var glyphs = new[] { WidgetGlyph.Connection, WidgetGlyph.Play, WidgetGlyph.Music, WidgetGlyph.Connection,
            WidgetGlyph.Play, WidgetGlyph.Microphone, WidgetGlyph.Volume, WidgetGlyph.Settings };
        var catalog = names.Select((name, i) => new BridgeWidgetDescriptor { Id = "status." + i, Name = name,
            InstanceId = "fixture", RuntimeGeneration = "runtime", PresentationGeneration = "presentation",
            PackageContentDigest = "fixture", Icon = glyphs[i], IsStopped = fixtures[i].Stopped,
            PackageIcon = i < 2 ? new("brand", WidgetPackageIconColorMode.OriginalColor) : null }).ToArray();
        Task<WidgetPresentationPackageIcon> Resolve(BridgeWidgetDescriptor item, string asset, CancellationToken token)
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, item.Name.ToLowerInvariant() + ".svg"));
            return Task.FromResult(new WidgetPresentationPackageIcon(asset, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes));
        }
        try
        {
            foreach (var theme in new[] { "dark", "light", "contrast" })
            foreach (var scale in new[] { .8, 1.0, 1.4 })
            {
                var appearance = AppearanceSettings.Default with { Contrast = theme == "contrast" ? ContrastPreference.High : ContrastPreference.Standard };
                var styles = ShellChromeValidationPage.Palette("builtin-neon-circuit").ToDictionary(pair => pair.Key, pair => pair.Value);
                if (theme == "light")
                {
                    foreach (var role in new[] { "canvas", "panel", "tray", "tray-item", "body", "hint" })
                        styles[role] = new() { Focused = new Dictionary<string, BridgeComputedStyleValue>(), Pressed = new Dictionary<string, BridgeComputedStyleValue>(), Base = new Dictionary<string, BridgeComputedStyleValue>
                        {
                            ["background"] = new() { Kind = WidgetRail.WidgetStyling.WrssValueKind.Color, Text = "#F5F5F5" },
                            ["color"] = new() { Kind = WidgetRail.WidgetStyling.WrssValueKind.Color, Text = "#151515" },
                        } };
                    styles["tray-item:selected"] = new() { Focused = new Dictionary<string, BridgeComputedStyleValue>(), Pressed = new Dictionary<string, BridgeComputedStyleValue>(), Base = new Dictionary<string, BridgeComputedStyleValue>
                    {
                        ["background"] = new() { Kind = WidgetRail.WidgetStyling.WrssValueKind.Color, Text = "#202060" },
                        ["color"] = new() { Kind = WidgetRail.WidgetStyling.WrssValueKind.Color, Text = "#FFFFFF" },
                    } };
                }
                var palette = ShellChromePalette.Resolve(styles, appearance);
                if (theme == "light") palette = new(Microsoft.UI.Colors.WhiteSmoke, Microsoft.UI.Colors.Transparent, Microsoft.UI.Colors.Black,
                    Microsoft.UI.Colors.MidnightBlue, Microsoft.UI.Colors.White, Microsoft.UI.Colors.DimGray, Microsoft.UI.Colors.Black, 2, false);
                var board = new StackPanel { Width = 940, Spacing = 20, Padding = new(20), Background = ShellChromePalette.Brush(palette.Surface) };
                board.Children.Add(new TextBlock { Text = $"Widget status · {theme} · {scale:0.0}×", FontSize = 22, Foreground = ShellChromePalette.Brush(palette.Text) });
                var tiles = new List<WidgetCatalogItemContent>();
                foreach (var iconSize in new[] { 22d, 24d, 34d })
                {
                    var tileSize = iconSize == 22 ? 44 : 64;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    row.Children.Add(new FontIcon { Glyph = "\uE76B", FontSize = 14, Foreground = ShellChromePalette.Brush(palette.Text), Width = 32 });
                    for (var i = 0; i < catalog.Length; i++)
                    {
                        var tile = new WidgetCatalogItemContent(Resolve) { ShowLabel = false, IconSize = iconSize, TileSize = tileSize, Foreground = ShellChromePalette.Brush(palette.Text) };
                        tile.DataContext = catalog[i]; tile.SetStatus(fixtures[i], palette); tiles.Add(tile);
                        var button = new Button { Content = tile, Width = tileSize, Height = tileSize, Padding = new(0), Background = ShellChromePalette.Brush(palette.Item), BorderThickness = new(0) };
                        AutomationProperties.SetName(button, fixtures[i].Describe(names[i])); row.Children.Add(button);
                    }
                    row.Children.Add(new FontIcon { Glyph = "\uE76C", FontSize = 14, Foreground = ShellChromePalette.Brush(palette.Text), Width = 32 });
                    board.Children.Add(row);
                }
                var lower = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 35 };
                var radialExtent = scale < 1 ? 280 : 400;
                using var radial = new RadialChooserView(Resolve) { Width = radialExtent, Height = radialExtent, StatusFor = item => fixtures[Array.IndexOf(catalog, item)] };
                radial.Update(catalog, catalog[0].Id, 0, styles, appearance, false);
                lower.Children.Add(radial);
                lower.Children.Add(new TextBlock { Text = string.Join("\n\n", catalog.Select((item, i) => fixtures[i].Describe(item.Name))),
                    Width = 410, TextWrapping = TextWrapping.Wrap, FontSize = 16, Foreground = ShellChromePalette.Brush(palette.Text), VerticalAlignment = VerticalAlignment.Center });
                board.Children.Add(lower);
                var scaled = new Viewbox { Width = 940 * scale, Stretch = Stretch.Uniform, Child = board, HorizontalAlignment = HorizontalAlignment.Left };
                Content = new ScrollViewer { Content = scaled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
                await Task.Delay(500); UpdateLayout();
                Check(radial.ButtonFor(catalog[0].Id) is { IsLoaded: true }, "Radial realizes eight status-bearing widgets");
                foreach (var item in catalog)
                {
                    var button = radial.ButtonFor(item.Id)!;
                    var badges = Descendants(button).OfType<WidgetStatusBadges>().Single();
                    Check(Descendants(badges).OfType<Border>().Count(b => b.Visibility == Visibility.Visible) <= (radialExtent < 320 ? 1 : 2), "Radial badge density fits " + item.Name + " at " + radialExtent);
                    Check(!badges.IsHitTestVisible, "Badges do not intercept widget input");
                }
                radial.ButtonFor(catalog[0].Id)!.Focus(FocusState.Programmatic);
                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot);
                radial.RefreshBadges();
                Check(ReferenceEquals(focused, Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot)), "Status refresh preserves focus");
                await Task.Delay(250); // Let native focus visuals settle before rasterizing.
                await CaptureStatusAsync(scaled, Path.Combine(directory, $"{theme}-{scale:0.0}.png"));
                foreach (var tile in tiles) tile.Dispose();
            }
            await ValidateBrowserAudioAsync(directory);
            await ValidateNativeAudioAsync(directory);
        }
        catch (Exception error) { failure = error; }
        await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        if (failure is not null) throw failure;
    }
    private async Task ValidateNativeAudioAsync(string directory)
    {
        var path = Path.Combine(directory, "synthetic.wav");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            const int samples = 44100 * 3;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
            for (var i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 220 / 44100) * 100));
        }
        using var player = new Media.MediaPlayerView(); Content = player;
        await Task.Delay(100);
        await player.ValidateActivityAsync(path, checks);
    }
    private static async Task CaptureStatusAsync(FrameworkElement element, string path)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(element);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync(); stream.Seek(0);
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); await File.WriteAllBytesAsync(path, bytes);
    }
    private async Task ValidateBrowserAudioAsync(string directory)
    {
        BrowserSurface.FixturePage = _ => "<!doctype html><title>Synthetic audio</title><body>Synthetic audio badge validation</body>";
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(directory, "web-profile"), new());
        var browser = new BrowserSurface("status.audio", new("audio", 1, "https://badge.test/", "Synthetic audio")
            { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = new() { Popups = BrowserPopupMode.Hosted } }, () => Task.FromResult(env));
        Content = browser; browser.SetPresentation(true, true);
        try
        {
            var core = await browser.PrepareCoreAsync(CancellationToken.None);
            await Task.Delay(500);
            await browser.EvaluateGestureFixtureAsync("window.ac=new AudioContext();window.os=ac.createOscillator();window.g=ac.createGain();g.gain.value=.01;os.connect(g).connect(ac.destination);os.start();ac.resume();");
            await Wait(() => browser.PlayingAudio); Check(true, "WebView audio produces a speaker state");
            core.IsMuted = true; await Wait(() => !browser.PlayingAudio); Check(true, "WebView mute removes speaker state");
            core.IsMuted = false; await Wait(() => browser.PlayingAudio);
            browser.SetPresentation(false, false); Check(browser.PlayingAudio, "Hidden playback stays observable without waking a widget");
            browser.SetPresentation(true, true);
            await browser.EvaluateFixtureAsync("ac.close()"); await Wait(() => !browser.PlayingAudio);
            Check(true, "Audio stop clears the speaker state");
            await browser.EvaluateGestureFixtureAsync("window.open('https://badge.test/child')");
            await Wait(() => browser.PinnablePopup?.IsReady == true);
            var child = browser.PinnablePopup!;
            await Task.Delay(300);
            await child.EvaluateGestureFixtureAsync("window.ac=new AudioContext();window.os=ac.createOscillator();window.g=ac.createGain();g.gain.value=.01;os.connect(g).connect(ac.destination);os.start();ac.resume();");
            await Wait(() => browser.PlayingAudio);
            Check(true, "Hosted child audio contributes to parent status");
            await browser.ClosePopupAsync(); await Wait(() => !browser.PlayingAudio);
            Check(true, "Closing hosted child clears its audio contribution");
        }
        finally { await browser.DisposeAsync(); BrowserSurface.FixturePage = null; }
        Check(!browser.PlayingAudio, "Disposed browser cannot retain audio activity");
    }
}
