using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class LibraryMediaValidationPage : Page
{
    internal LibraryMediaValidationPage(string result, string shortVideo)
    {
        var media = new MediaPlayerView { Height = 480, Margin = new(24) };
        Content = media;
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>(); string? error = null;
            try { await media.ValidateLibraryPlaybackAsync(result, shortVideo, checks); }
            catch (Exception failure) { error = failure.ToString(); }
            finally { media.Dispose(); }
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
        };
    }
}
