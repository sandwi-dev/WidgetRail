using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsDisplayProvider;

public sealed partial class WindowsDisplayProfilesBackend
{
    public async Task<DisplayControlsState> GetDisplayControlsAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StartObserving();
            return await Task.Run(_native.ReadControls, token).ConfigureAwait(false);
        }
        catch (Exception error) when (CanMap(error)) { throw Friendly(error); }
        finally { _gate.Release(); }
    }

    public async Task<DisplayProfilesState> ApplyDisplayControlAsync(DisplayControlRequest request,
        BrokerWidgetIdentity identity, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_restore is not null) throw new BrokerException("display_busy", "Keep or revert the current display change first.");
            var target = await Task.Run(() => _native.PrepareControl(request), token).ConfigureAwait(false);
            if (target.Matches(_native.Capture())) return ReadState();
            _native.Validate(target);
            _outcome = null;
            _restore = await _launcher.StartAsync(target, token).ConfigureAwait(false);
            _restoreOwner = identity;
            _restoreName = target.Summaries().FirstOrDefault(display => display.Id == request.DisplayId)?.Name ?? "Display";
            _ = ObserveRestoreAsync(_restore);
            return ReadState();
        }
        catch (Exception error) when (CanMap(error)) { throw Friendly(error); }
        finally { _gate.Release(); PublishChanged(); }
    }
}
