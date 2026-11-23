using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal async Task ValidateFrameStylesAsync(string png, string mp4, List<string> checks)
    {
        var still = AttachmentFor(png, "image/png", 0);
        Apply(_ => Task.FromResult(new HostCaptureAttachment(still, png)), still, () => true);
        SetActive(true, true);
        await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
        Check(card.Padding == new Thickness(8) && card.BorderThickness == new Thickness(1),
            "Unstyled media retains the existing inline frame defaults");
        var adapter = new NativeComputedStyleAdapter(this);
        try
        {
            adapter.Update(Frame(0, 0, 0)); UpdateLayout();
            Check(card.Padding == new Thickness(0) && card.BorderThickness == new Thickness(0) &&
                card.Background is SolidColorBrush { Color.A: 0 } && card.CornerRadius == new CornerRadius(0),
                "Existing WRSS frame properties reach the actual media frame");
            Check(Padding == new Thickness(0) && BorderThickness == new Thickness(0) &&
                Math.Abs(stage.ActualWidth - card.ActualWidth) < 1 && Math.Abs(stage.ActualHeight - card.ActualHeight) < 1,
                "Borderless media uses the full inline frame without a second inset");
            Theme(false);
            Check(card.Padding == new Thickness(0) && card.BorderThickness == new Thickness(0) && card.CornerRadius == new CornerRadius(0),
                "Theme refresh preserves authored inline frame geometry");
            Check(imageTools.Background is SolidColorBrush { Color.A: > 0 },
                "Transparent media framing preserves readable floating transport chrome");
            var expansion = ToggleExpandedAsync();
            await Until(() => IsExpanded && !changingPlacement);
            adapter.Update(Frame(5, 2, 10));
            Check(card.Padding == new Thickness(0) && card.BorderThickness == new Thickness(0) && card.CornerRadius == new CornerRadius(0),
                "Inline frame changes do not add padding or a stroke to fullscreen");
            expandedHost!.Close(); await expansion;
            await Until(() => !IsExpanded && !changingPlacement);
            Check(card.Padding == new Thickness(5) && card.BorderThickness == new Thickness(2) && card.CornerRadius == new CornerRadius(10),
                "Fullscreen return restores the latest authored inline frame");
            adapter.Update(null);
            Check(card.Padding == new Thickness(8) && card.BorderThickness == new Thickness(1) && card.CornerRadius == new CornerRadius(9),
                "Removing frame declarations restores current theme defaults");
            adapter.Update(Frame(0, 0, 0));
            Theme(true); NativeComputedStyleAdapter.SetHighContrastOverride(true); adapter.Update(Frame(0, 0, 0));
            Check(card.Padding == new Thickness(0) && card.BorderThickness == new Thickness(0) &&
                imageTools.Background is SolidColorBrush { Color.A: 255 },
                "High contrast retains inset-free layout and opaque system control backing");
            NativeComputedStyleAdapter.SetHighContrastOverride(null); Theme(false); adapter.Update(Frame(0, 0, 0));
            var clip = AttachmentFor(mp4, "video/mp4", 5);
            Apply(_ => Task.FromResult(new HostCaptureAttachment(clip, mp4)), clip, () => true);
            await Until(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 0 && video.IsLoaded);
            var decoder = player;
            expansion = ToggleExpandedAsync(); await Until(() => IsExpanded && !changingPlacement);
            Check(ReferenceEquals(decoder, player) && card.Padding == new Thickness(0) && card.BorderThickness == new Thickness(0),
                "Borderless video expands with its existing decoder in-tree element");
            expandedHost!.Close(); await expansion; await Until(() => !IsExpanded && !changingPlacement);
            Check(ReferenceEquals(decoder, player) && card.Padding == new Thickness(0) && card.BorderThickness == new Thickness(0),
                "Video return retains both decoder identity and authored frame");
        }
        finally { NativeComputedStyleAdapter.SetHighContrastOverride(null); adapter.Dispose(); }
        Check(card.Padding == new Thickness(8) && card.BorderThickness == new Thickness(1),
            "Retiring the style owner restores legacy inline defaults");

        void Theme(bool contrast)
        {
            var background = new SolidColorBrush(Microsoft.UI.Colors.Black);
            var foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            ApplyTheme(background, foreground, foreground, background, foreground, foreground, 14,
                Microsoft.UI.Text.FontWeights.Normal, null, 9, contrast);
        }
        static CaptureAttachment AttachmentFor(string path, string type, double duration) =>
            new(Guid.NewGuid().ToString("N"), type, 640, 360, new FileInfo(path).Length, duration,
                DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds());
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks.Add(message); }
        static async Task Until(Func<bool> condition)
        {
            var deadline = Environment.TickCount64 + 10000;
            while (!condition())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("Media frame did not settle.");
                await Task.Delay(25);
            }
        }
        static BridgeNodeRenderStyles Frame(double padding, double border, double radius)
        {
            static BridgeComputedStyleValue Length(double value) => new() { Kind = WrssValueKind.Length, Number = value, Unit = "px", Text = value + "px" };
            var style = new Dictionary<string, BridgeComputedStyleValue>
            {
                ["padding"] = Length(padding), ["border-width"] = Length(border), ["corner-radius"] = Length(radius),
                ["background"] = new() { Kind = WrssValueKind.Color, Text = "#00000000" },
            };
            return new() { Base = style, Focused = style, Pressed = style };
        }
    }
}
