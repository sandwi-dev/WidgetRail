namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    // Host editor opt-in; ordinary player consumers have no range and retain their existing controls.
    private double? editPreviewEnd;
    private bool editorPreviewPaused;
    // Keep source-authority renewal alive while the host editor owns interaction.
    // Releasing the decoder here would also stop renewal of a two-minute library lease.
    internal void SetEditorPreviewPaused(bool value)
    {
        editorPreviewPaused = value;
        if (value) { player?.Pause(); ReportAudio(); }
    }
    internal void ResetEditorInput() { adjustingVolume = false; backOwned = false; }
    internal void SeekForEdit(double seconds)
    {
        editPreviewEnd = null;
        if (!input || player?.PlaybackSession.CanSeek != true || !double.IsFinite(seconds)) return;
        player.Pause();
        player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Duration));
    }
    internal void PreviewEditRange(double start, double end)
    {
        if (!input || player?.PlaybackSession.CanSeek != true || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start) return;
        editPreviewEnd = Math.Min(end, Duration);
        player.IsLoopingEnabled = false;
        player.PlaybackSession.Position = TimeSpan.FromSeconds(start);
        player.Play();
    }
    private void RefreshEditRange()
    {
        if (editPreviewEnd is not { } end || player is null || player.PlaybackSession.Position.TotalSeconds < end) return;
        player.Pause(); editPreviewEnd = null;
    }
}
