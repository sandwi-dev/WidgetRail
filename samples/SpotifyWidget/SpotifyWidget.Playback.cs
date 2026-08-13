using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

/// <summary>
/// Value-only playback/device reconciliation. The widget remains the sole
/// provider-call, task, committed-state, lock, and invalidation owner.
/// </summary>
internal static class SpotifyPlaybackPolicy
{
    internal static SpotifyPlaybackOperation ResolveToggle(
        SpotifyPlaybackSummary? playback) =>
        playback?.IsPlaying == true
            ? SpotifyPlaybackOperation.Pause
            : SpotifyPlaybackOperation.Play;

    internal static SpotifyPlaybackCommand? BuildCommand(
        SpotifyPlaybackSummary? playback,
        SpotifyPlaybackOperation operation,
        long? requestedPosition)
    {
        if (playback is not { IsAvailable: true }) return null;
        var blocked = playback.DisallowedActions;
        return operation switch
        {
            SpotifyPlaybackOperation.Play when !blocked.Resuming => new(operation),
            SpotifyPlaybackOperation.Pause when !blocked.Pausing => new(operation),
            SpotifyPlaybackOperation.Next when !blocked.SkippingNext => new(operation),
            SpotifyPlaybackOperation.Previous when !blocked.SkippingPrevious => new(operation),
            SpotifyPlaybackOperation.Seek when !blocked.Seeking && requestedPosition is { } value =>
                new(operation, PositionMilliseconds: Math.Clamp(value, 0,
                    Math.Max(0, playback.DurationMilliseconds))),
            SpotifyPlaybackOperation.SetShuffle when !blocked.TogglingShuffle =>
                new(operation, Enabled: !playback.ShuffleState),
            SpotifyPlaybackOperation.SetRepeat => NextRepeatCommand(playback),
            _ => null,
        };
    }

    internal static SpotifyPlaybackSummary ApplyOptimistic(
        SpotifyPlaybackSummary playback,
        SpotifyPlaybackCommand command,
        long nowUnixMilliseconds)
    {
        var projected = Project(playback, nowUnixMilliseconds)!;
        return command.Operation switch
        {
            SpotifyPlaybackOperation.Play => projected with
            {
                IsPlaying = true,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            SpotifyPlaybackOperation.Pause => projected with
            {
                IsPlaying = false,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            SpotifyPlaybackOperation.Seek => projected with
            {
                ProgressMilliseconds = command.PositionMilliseconds!.Value,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            SpotifyPlaybackOperation.SetShuffle => projected with
            {
                ShuffleState = command.Enabled!.Value,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            SpotifyPlaybackOperation.SetRepeat => projected with
            {
                RepeatState = command.RepeatState!.Value,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            _ => projected,
        };
    }

    internal static SpotifyPlaybackSummary? Project(
        SpotifyPlaybackSummary? playback,
        long nowUnixMilliseconds)
    {
        if (playback is not { IsAvailable: true, IsPlaying: true } ||
            playback.DurationMilliseconds <= 0) return playback;
        var elapsed = Math.Max(0,
            nowUnixMilliseconds - playback.CapturedAtUnixMilliseconds);
        return playback with
        {
            ProgressMilliseconds = Math.Clamp(playback.ProgressMilliseconds + elapsed,
                0, playback.DurationMilliseconds),
            CapturedAtUnixMilliseconds = nowUnixMilliseconds,
        };
    }

    internal static SpotifyDevicesSummary? MarkActiveDevice(
        SpotifyDevicesSummary? devices,
        string? deviceId) => devices is null ? null : devices with
        {
            Devices = devices.Devices.Select(device => device with
            {
                IsActive = deviceId is not null &&
                    string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal),
            }).ToArray(),
        };

    internal static string SafeMessage(Exception exception, string fallback)
    {
        var message = exception.Message?.Trim();
        return string.IsNullOrWhiteSpace(message) || message.Length > 160 ||
            message.Any(char.IsControl) ? fallback : message;
    }

    internal static string OperationStatus(SpotifyPlaybackOperation operation) =>
        operation switch
        {
            SpotifyPlaybackOperation.Play => "Resuming…",
            SpotifyPlaybackOperation.Pause => "Pausing…",
            SpotifyPlaybackOperation.Next => "Skipping forward…",
            SpotifyPlaybackOperation.Previous => "Going back…",
            SpotifyPlaybackOperation.Seek => "Seeking…",
            SpotifyPlaybackOperation.SetShuffle => "Updating shuffle…",
            SpotifyPlaybackOperation.SetRepeat => "Updating repeat…",
            _ => "Updating Spotify…",
        };

    private static SpotifyPlaybackCommand? NextRepeatCommand(
        SpotifyPlaybackSummary playback)
    {
        var blocked = playback.DisallowedActions;
        var next = playback.RepeatState switch
        {
            SpotifyRepeatState.Off when !blocked.TogglingRepeatContext =>
                SpotifyRepeatState.Context,
            SpotifyRepeatState.Context when !blocked.TogglingRepeatTrack =>
                SpotifyRepeatState.Track,
            SpotifyRepeatState.Track => SpotifyRepeatState.Off,
            _ => (SpotifyRepeatState?)null,
        };
        return next is null ? null : new(
            SpotifyPlaybackOperation.SetRepeat, RepeatState: next);
    }
}
