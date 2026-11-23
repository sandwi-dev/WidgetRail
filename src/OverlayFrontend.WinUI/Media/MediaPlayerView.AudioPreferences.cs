using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal string? AudioPreferenceWidgetId { get; set; }

    private void ApplyAudioPreferences(MediaPlayerOptions options, bool useDefaults = true)
    {
        if (player is null) return;
        var choice = options.IsAnimation || AudioPreferenceWidgetId is not { } widgetId
            ? null : NativeMediaAudioPreferences.Find(this)?.Read(widgetId, options.Volume);
        if (choice is null && !useDefaults) return;
        player.IsMuted = ForcePreviewMute || (choice?.Muted ?? (AudioPreferenceWidgetId is not null && !options.IsAnimation || options.Muted));
        if (useDefaults && galleryDeclaration?.Gallery?[0].PublicThumbnail is not null && options is { AutoPlay: true, Muted: true })
            player.IsMuted = true;
        player.Volume = choice?.Volume ?? options.Volume;
    }

    private void RememberAudioPreferences()
    {
        if (player is null || !input || ForcePreviewMute || definition?.Options.IsAnimation == true ||
            AudioPreferenceWidgetId is not { } widgetId) return;
        NativeMediaAudioPreferences.Find(this)?.Remember(widgetId, player.IsMuted, player.Volume);
    }
}
