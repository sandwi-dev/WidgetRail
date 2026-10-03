using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Capture;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Playback/focus checks using existing synthetic files; never records the desktop or uploads media.</summary>
internal sealed partial class CapturedMediaValidationPage : Page
{
    internal CapturedMediaValidationPage(string result, string png, string mp4)
    {
        var theme = new NativePopupTheme(); theme.Attach(this); theme.Update(null, AppearanceSettings.Default);
        var media = new MediaPlayerView { Height = 360, Margin = new(24), IsTabStop = true };
        Content = media;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            try { await media.ValidateCapturePlaybackAsync(png, mp4, checks); }
            catch (Exception failure) { error = failure.ToString(); }
            finally { media.Dispose(); }
            if (error is null && Environment.GetCommandLineArgs().Contains("--media-player-preview"))
            {
                var preview = new MediaPlayerView { Height = 440, Margin = new(24), IsTabStop = true };
                Content = preview; preview.SetActive(true,true);
                var source = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(mp4),new(Muted:true));
                preview.Apply(_ => Task.FromResult(new HostMediaPlayerSource(source,new Uri(mp4).AbsoluteUri)),source,()=>true);
            }
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, error }));
        };
    }
}
