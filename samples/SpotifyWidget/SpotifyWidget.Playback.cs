using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Samples.SpotifyWidget;

/// <summary>
/// Value-only playback/device reconciliation. The widget remains the sole
/// provider-call, task, committed-state, lock, and invalidation owner.
/// </summary>
internal static class SpotifyPlaybackPolicy
{
    internal static WidgetSpotifyPlaybackOperation ResolveToggle(
        WidgetSpotifyPlaybackSummary? playback) =>
        playback?.IsPlaying == true
            ? WidgetSpotifyPlaybackOperation.Pause
            : WidgetSpotifyPlaybackOperation.Play;

    internal static WidgetSpotifyPlaybackCommand? BuildCommand(
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

    internal static WidgetSpotifyPlaybackSummary ApplyOptimistic(
        WidgetSpotifyPlaybackSummary playback,
        WidgetSpotifyPlaybackCommand command,
        long nowUnixMilliseconds)
    {
        var projected = Project(playback, nowUnixMilliseconds)!;
        return command.Operation switch
        {
            WidgetSpotifyPlaybackOperation.Play => projected with
            {
                IsPlaying = true,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            WidgetSpotifyPlaybackOperation.Pause => projected with
            {
                IsPlaying = false,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            WidgetSpotifyPlaybackOperation.Seek => projected with
            {
                ProgressMilliseconds = command.PositionMilliseconds!.Value,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            WidgetSpotifyPlaybackOperation.SetShuffle => projected with
            {
                ShuffleState = command.Enabled!.Value,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            WidgetSpotifyPlaybackOperation.SetRepeat => projected with
            {
                RepeatState = command.RepeatState!.Value,
                CapturedAtUnixMilliseconds = nowUnixMilliseconds,
            },
            _ => projected,
        };
    }

    internal static WidgetSpotifyPlaybackSummary? Project(
        WidgetSpotifyPlaybackSummary? playback,
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

    internal static WidgetSpotifyDevicesSummary? MarkActiveDevice(
        WidgetSpotifyDevicesSummary? devices,
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

    internal static string OperationStatus(WidgetSpotifyPlaybackOperation operation) =>
        operation switch
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
}
