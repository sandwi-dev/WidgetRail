using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed partial class MusicService
{
    private int Destination(string id) => id == "previous" ? _state.Index - 1 :
        MusicQueue.Next(_state.Queue.Count, _state.Index, _state.Repeat, ended: false);

    public TrayActionOffer? GetTrayOffer(MusicState snapshot, string id)
    {
        lock (_gate) return ReferenceEquals(snapshot, _state) ? OfferLocked(id) : null;
    }

    private TrayActionOffer? OfferLocked(string id)
    {
        if (!_state.Connected || _ratingAccountChanging || _state.Current is null || _player is null ||
            _playerGeneration != _generation || _state.Player.TrackId != _state.Current.Id)
            return null;
        var target = FormattableString.Invariant($"{_ratingEpoch}.{_generation}.{_state.Index}");
        if (id == "play-pause")
            return _recoveryStopped || _state.Player.Buffering || _state.Player.Error is not null ? null :
                new(id, target + (_state.Player.Playing ? ".pause" : ".play"));
        if (id is not ("next" or "previous")) return null;
        var index = Destination(id);
        return (uint)index < _state.Queue.Count ? new(id, FormattableString.Invariant($"{target}.{_queueRevision}.{index}")) : null;
    }

    public async ValueTask<bool> InvokeTrayAsync(TrayActionOffer offer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (offer.Id is "previous" or "next")
            return await SelectAsync(token, step: offer).ConfigureAwait(false);

        IMusicProcess player;
        string command;
        long generation;
        lock (_gate)
        {
            if (OfferLocked(offer.Id) != offer) return false;
            player = _player!;
            command = _state.Player.Playing ? "pause" : "play";
            generation = _generation;
        }
        var reply = await player.CallAsync(command, new { expectedGeneration = generation }, token).ConfigureAwait(false);
        return reply.TryGetProperty("applied", out var applied) && applied.GetBoolean();
    }
}
