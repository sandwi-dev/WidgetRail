namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed partial class StandaloneMusicWidget
{
    private (long Account, string Song)? _observedLike;

    private void ObserveSongLike()
    {
        MusicItem item;
        lock (_gate)
        {
            var state = _service.State;
            if (!IsActive || _signingIn || !state.Connected || state.Current is not { } current) return;
            var identity = (state.RatingGeneration, current.Id);
            if (_observedLike == identity) return;
            _observedLike = identity;
            item = current;
        }
        Operations.RunLatest("music.like-read", async context =>
        {
            try { await _service.ReadSongLikeAsync(item, context.CancellationToken); }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            { /* Unknown status keeps the explicit Like action available; no retry loop. */ }
        }, WidgetRail.WidgetSdk.WidgetOperationLifetime.Active);
    }

    private void ChangeSongLike(MusicItem item, bool liked)
    {
        if (!_service.State.Connected || _signingIn) return;
        var account = _service.State.RatingGeneration;
        Operations.RunSingleFlight("music.like-write", async context =>
        {
            try { await _service.SetSongLikeAsync(item, liked, context.CancellationToken); }
            catch (Exception error) when (error is IOException or OperationCanceledException)
            { if (context.IsCurrent && account == _service.State.RatingGeneration) SetStatus("Could not update the song's like. Try again, or reconnect in Settings."); }
        }, WidgetRail.WidgetSdk.WidgetOperationLifetime.Widget);
    }
}
