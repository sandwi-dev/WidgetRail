using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

public sealed partial class SpotifyWidget
{
    private async Task SelectDeviceAsync(int index, CancellationToken cancellationToken)
    {
        WidgetSpotifyDeviceSummary? device;
        lock (_gate) device = ItemAt(_devices?.Devices, index);
        if (device is null || device.IsRestricted) return;
        try
        {
            if (device.IsLocalHost)
            {
                await ControlLocalPlaybackAsync(
                    new(WidgetSpotifyLocalPlaybackOperation.StartAndTransfer,
                        ContinuePlaying: true), cancellationToken).ConfigureAwait(false);
                return;
            }
            await HostServices.Spotify.TransferPlaybackAsync(
                device.DeviceId, true, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _preferredPlaybackDeviceId = device.DeviceId;
                _devices = MarkActiveDevice(_devices, device.DeviceId);
                _devicesCachedAt = _timeProvider.GetUtcNow();
                _status = $"Playing on {device.Name}";
            }
            Invalidate();
        }
        catch (WidgetCapabilityException exception)
        {
            SetCommandStatus(SafeMessage(exception, "Spotify could not switch devices"));
        }
    }

    private async Task ControlLocalPlaybackAsync(
        WidgetSpotifyLocalPlaybackCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate) _pageLoading = true;
            Invalidate();
            var local = await HostServices.Spotify.ControlLocalPlaybackAsync(
                command, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _localPlayback = local;
                _pageLoading = false;
                _pageError = null;
                _status = local.DisplayMessage ?? "Local Spotify playback updated";
                var localDeviceId = _devices?.Devices
                    .FirstOrDefault(device => device.IsLocalHost)?.DeviceId;
                if (command.Operation == WidgetSpotifyLocalPlaybackOperation.Stop)
                {
                    if (_preferredPlaybackDeviceId == localDeviceId)
                        _preferredPlaybackDeviceId = null;
                    _devices = MarkActiveDevice(_devices, null);
                }
                else if (localDeviceId is not null)
                {
                    _preferredPlaybackDeviceId = localDeviceId;
                    _devices = MarkActiveDevice(_devices, localDeviceId);
                }
                _devicesCachedAt = _timeProvider.GetUtcNow();
            }
            Invalidate();
        }
        catch (WidgetCapabilityException exception)
        {
            lock (_gate)
            {
                _pageLoading = false;
                _pageError = null;
                _status = exception.ErrorCode switch
                {
                    "lifecycle_denied" =>
                        "Return focus to Devices, then try Play here again.",
                    "resource_not_found" =>
                        "Spotify could not activate this playback device.",
                    _ => SafeMessage(exception, "Local Spotify playback could not be updated"),
                };
            }
            Invalidate();
        }
    }

    private async Task PlayQueueItemAsync(int index, CancellationToken cancellationToken)
    {
        WidgetSpotifyMediaItemSummary? item;
        lock (_gate) item = ItemAt(_queue?.Items, index);
        if (item is null || !item.IsPlayable) return;
        await StartPlaybackAsync(new(null, [item.Uri], DeviceId: PlaybackDeviceId()),
            "Playing selected queue item",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PlayPlaylistAsync(CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaylistSummary? playlist;
        lock (_gate) playlist = _playlistSelection?.Playlist;
        if (playlist is null) return;
        await StartPlaybackAsync(new(playlist.Uri, null,
                DeviceId: PlaybackDeviceId()),
            $"Playing {playlist.Name}", cancellationToken).ConfigureAwait(false);
    }

    private async Task PlayPlaylistTrackAsync(
        int absoluteIndex,
        CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaylistSummary? playlist;
        WidgetSpotifyMediaItemSummary? item;
        lock (_gate)
        {
            playlist = _playlistSelection?.Playlist;
            item = playlist is not null &&
                _playlistItems.TryGetCurrentItem(absoluteIndex, out var current)
                    ? current
                    : null;
        }
        if (playlist is null || item is null || !item.IsPlayable) return;
        await StartPlaybackAsync(new(playlist.Uri, null,
                DeviceId: PlaybackDeviceId(), OffsetUri: item.Uri),
            $"Playing {item.Title}", cancellationToken).ConfigureAwait(false);
    }

    private async Task StartPlaybackAsync(
        StartWidgetSpotifyPlaybackRequest request,
        string successStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            await HostServices.Spotify.StartPlaybackAsync(request, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate) _queueCachedAt = null;
            SetCommandStatus(successStatus);
            await RefreshPlaybackAsync(Volatile.Read(ref _activeGeneration), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WidgetCapabilityException exception)
        {
            SetCommandStatus(exception.ErrorCode == "resource_not_found"
                ? "No active Spotify device. Open Devices and choose where to play."
                : SafeMessage(exception, "Spotify could not start playback"));
        }
    }

    private string? PlaybackDeviceId()
    {
        lock (_gate) return _preferredPlaybackDeviceId;
    }

    private static WidgetSpotifyDevicesSummary? MarkActiveDevice(
        WidgetSpotifyDevicesSummary? devices,
        string? activeDeviceId) => devices is null ? null : devices with
    {
        Devices = devices.Devices.Select(device => device with
        {
            IsActive = activeDeviceId is not null &&
                string.Equals(device.DeviceId, activeDeviceId, StringComparison.Ordinal),
        }).ToArray(),
    };

    private static bool TryParseIndexedAction(string action, string prefix, out int index)
    {
        index = -1;
        return action.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(action.AsSpan(prefix.Length), out index) && index >= 0;
    }

    private static T? ItemAt<T>(IReadOnlyList<T>? items, int index) where T : class =>
        items is not null && index >= 0 && index < items.Count ? items[index] : null;

    private static string PageError(WidgetCapabilityException exception) => exception.ErrorCode switch
    {
        "permission_denied" or "capability_revoked" =>
            "This optional Spotify capability is disabled in widget settings.",
        "authorization_scope_required" =>
            "Reconnect Spotify to grant the scope required for this page.",
        "premium_required" =>
            "Spotify Premium is required for playback and device transfer.",
        _ => SafeMessage(exception, "Spotify could not load this page. Try again."),
    };

    private static WidgetResourceError SpotifyResourceError(Exception exception) => new(
        "spotify_page_error",
        exception is WidgetCapabilityException capability
            ? PageError(capability)
            : "Spotify could not load this page. Try again.");

    private async Task ExecuteAsync(
        WidgetSpotifyPlaybackOperation operation,
        long? requestedPosition,
        CancellationToken cancellationToken)
    {
        WidgetSpotifyPlaybackSummary? before;
        WidgetSpotifyPlaybackCommand? command;
        lock (_gate)
        {
            before = _playback;
            command = BuildCommand(before, operation, requestedPosition);
            if (command is null || _pendingOperation is not null)
            {
                _status = "That Spotify control is not available";
                Invalidate();
                return;
            }
            _pendingOperation = operation;
            _playback = ApplyOptimistic(before!, command);
            _status = OperationStatus(operation);
        }
        Invalidate();
        try
        {
            await HostServices.Spotify.ControlPlaybackAsync(command, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                _pendingOperation = null;
                _status = "Updated in Spotify";
                if (operation is WidgetSpotifyPlaybackOperation.Next or
                    WidgetSpotifyPlaybackOperation.Previous)
                    _queueCachedAt = null;
            }
            Invalidate();
            await RefreshPlaybackAsync(Volatile.Read(ref _activeGeneration), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreOptimistic(before);
        }
        catch (WidgetCapabilityException exception)
        {
            RestoreOptimistic(before, SafeMessage(exception,
                exception.ErrorCode is "permission_denied" or "capability_revoked"
                    ? "Playback control permission is off"
                    : "Spotify rejected that control"));
        }
        catch (WidgetCapabilityUnavailableException)
        {
            RestoreOptimistic(before, "Spotify playback control is unavailable");
        }
    }

    private void RestoreOptimistic(WidgetSpotifyPlaybackSummary? playback, string? status = null)
    {
        lock (_gate)
        {
            _playback = playback;
            _pendingOperation = null;
            if (status is not null) _status = status;
        }
        Invalidate();
    }

    private WidgetSpotifyPlaybackOperation ResolveToggleOperation()
    {
        lock (_gate) return _playback?.IsPlaying == true
            ? WidgetSpotifyPlaybackOperation.Pause
            : WidgetSpotifyPlaybackOperation.Play;
    }

    private static WidgetSpotifyPlaybackCommand? BuildCommand(
        WidgetSpotifyPlaybackSummary? playback,
        WidgetSpotifyPlaybackOperation operation,
        long? requestedPosition)
    {
        if (playback is not { IsAvailable: true }) return null;
        var blocked = playback.DisallowedActions;
        return operation switch
        {
            WidgetSpotifyPlaybackOperation.Play when !blocked.Resuming => new(operation),
            WidgetSpotifyPlaybackOperation.Pause when !blocked.Pausing => new(operation),
            WidgetSpotifyPlaybackOperation.Next when !blocked.SkippingNext => new(operation),
            WidgetSpotifyPlaybackOperation.Previous when !blocked.SkippingPrevious => new(operation),
            WidgetSpotifyPlaybackOperation.Seek when !blocked.Seeking && requestedPosition is { } value =>
                new(operation, PositionMilliseconds: Math.Clamp(value, 0,
                    Math.Max(0, playback.DurationMilliseconds))),
            WidgetSpotifyPlaybackOperation.SetShuffle when !blocked.TogglingShuffle =>
                new(operation, Enabled: !playback.ShuffleState),
            WidgetSpotifyPlaybackOperation.SetRepeat => NextRepeatCommand(playback),
            _ => null,
        };
    }

    private static WidgetSpotifyPlaybackCommand? NextRepeatCommand(
        WidgetSpotifyPlaybackSummary playback)
    {
        var blocked = playback.DisallowedActions;
        var next = playback.RepeatState switch
        {
            WidgetSpotifyRepeatState.Off when !blocked.TogglingRepeatContext =>
                WidgetSpotifyRepeatState.Context,
            WidgetSpotifyRepeatState.Context when !blocked.TogglingRepeatTrack =>
                WidgetSpotifyRepeatState.Track,
            WidgetSpotifyRepeatState.Track => WidgetSpotifyRepeatState.Off,
            _ => (WidgetSpotifyRepeatState?)null,
        };
        return next is null ? null : new(
            WidgetSpotifyPlaybackOperation.SetRepeat, RepeatState: next);
    }

    private WidgetSpotifyPlaybackSummary ApplyOptimistic(
        WidgetSpotifyPlaybackSummary playback,
        WidgetSpotifyPlaybackCommand command)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var projected = ProjectPlayback(playback)!;
        return command.Operation switch
        {
            WidgetSpotifyPlaybackOperation.Play => projected with
            {
                IsPlaying = true,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.Pause => projected with
            {
                IsPlaying = false,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.Seek => projected with
            {
                ProgressMilliseconds = command.PositionMilliseconds!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.SetShuffle => projected with
            {
                ShuffleState = command.Enabled!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            WidgetSpotifyPlaybackOperation.SetRepeat => projected with
            {
                RepeatState = command.RepeatState!.Value,
                CapturedAtUnixMilliseconds = now,
            },
            _ => projected,
        };
    }

    private WidgetSpotifyPlaybackSummary? ProjectPlayback(WidgetSpotifyPlaybackSummary? playback)
    {
        if (playback is not { IsAvailable: true, IsPlaying: true } ||
            playback.DurationMilliseconds <= 0) return playback;
        var elapsed = Math.Max(0, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() -
            playback.CapturedAtUnixMilliseconds);
        return playback with
        {
            ProgressMilliseconds = Math.Clamp(playback.ProgressMilliseconds + elapsed,
                0, playback.DurationMilliseconds),
            CapturedAtUnixMilliseconds = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
        };
    }

    private void SetState(
        long generation,
        SpotifyWidgetViewState state,
        string status,
        WidgetSpotifyPlaybackSummary? playback)
    {
        if (generation != Volatile.Read(ref _activeGeneration)) return;
        lock (_gate)
        {
            _viewState = state;
            _status = status;
            _playback = playback;
            _pendingOperation = null;
        }
        Invalidate();
    }

    private void SetCommandStatus(string status)
    {
        lock (_gate) _status = status;
        Invalidate();
    }

    private static string SafeMessage(Exception exception, string fallback)
    {
        var message = exception.Message?.Trim();
        return string.IsNullOrWhiteSpace(message) || message.Length > 160 ||
            message.Any(char.IsControl) ? fallback : message;
    }

    private static string OperationStatus(WidgetSpotifyPlaybackOperation operation) => operation switch
    {
        WidgetSpotifyPlaybackOperation.Play => "Resuming…",
        WidgetSpotifyPlaybackOperation.Pause => "Pausing…",
        WidgetSpotifyPlaybackOperation.Next => "Skipping forward…",
        WidgetSpotifyPlaybackOperation.Previous => "Going back…",
        WidgetSpotifyPlaybackOperation.Seek => "Seeking…",
        WidgetSpotifyPlaybackOperation.SetShuffle => "Updating shuffle…",
        WidgetSpotifyPlaybackOperation.SetRepeat => "Updating repeat…",
        _ => "Updating Spotify…",
    };

}
