using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>
/// The existing SDK adapter wire protocol. Each browser document has isolated authority,
/// strictly increasing events and at most one command in flight. This is not a script API.
/// </summary>
internal sealed class EmbeddedMediaTransport(string sessionId)
{
    private static long nextGeneration;
    private readonly long generation = Interlocked.Increment(ref nextGeneration);
    private long nextCommand;
    private long lastEvent;
    private double playbackRate = 1;
    private bool muted;
    private bool loop;
    private Pending? pending;
    public bool Ready { get; private set; }
    public bool Busy => pending is not null;
    public string MediaKey { get; private set; } = "";
    public long LastPlaybackSequence { get; private set; }
    public long PendingCommandId => pending?.Id ?? 0;

    private sealed record Pending(long Id, string Command, EmbeddedMediaPlaybackCommand? Playback)
    {
        public bool Armed { get; set; }
    }

    public string Initialize()
    {
        if (pending is not null || Ready) throw new InvalidOperationException("Adapter already initialized.");
        return Encode("initialize", null);
    }

    public string? Dispatch(EmbeddedMediaPlaybackCommand command)
    {
        if (!Ready || Busy || command.Sequence <= LastPlaybackSequence) return null;
        var name = command.Kind switch
        {
            EmbeddedMediaPlaybackCommandKind.Load => "load",
            EmbeddedMediaPlaybackCommandKind.Cue => "cue",
            EmbeddedMediaPlaybackCommandKind.Play => "arm-activate",
            EmbeddedMediaPlaybackCommandKind.Pause => "pause",
            EmbeddedMediaPlaybackCommandKind.Seek => "seek",
            EmbeddedMediaPlaybackCommandKind.SetVolume => "volume",
            EmbeddedMediaPlaybackCommandKind.SetPlaybackRate => "playback-rate",
            EmbeddedMediaPlaybackCommandKind.SetMuted => "muted",
            EmbeddedMediaPlaybackCommandKind.SetLoop => "loop",
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        LastPlaybackSequence = command.Sequence;
        return Encode(name, command);
    }

    public string? Dispatch(EmbeddedMediaCommand command)
    {
        if (!Ready || Busy) return null;
        return Encode(command switch
        {
            EmbeddedMediaCommand.Previous => "previous",
            EmbeddedMediaCommand.Next => "next",
            EmbeddedMediaCommand.Activate => "arm-activate",
            EmbeddedMediaCommand.Back => "back",
            EmbeddedMediaCommand.TogglePlayback => "toggle",
            EmbeddedMediaCommand.SeekBackward => "seek-back",
            EmbeddedMediaCommand.SeekForward => "seek-forward",
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        }, null);
    }

    private string Encode(string command, EmbeddedMediaPlaybackCommand? playback)
    {
        pending = new(++nextCommand, command, playback);
        var body = new Dictionary<string, object?>
        {
            ["command"] = command,
            ["environmentGeneration"] = generation,
            ["surfaceGeneration"] = generation,
            ["sessionGeneration"] = generation,
            ["controllerGeneration"] = generation,
            ["documentGeneration"] = generation,
            ["commandId"] = pending.Id,
        };
        if (playback is not null)
        {
            body["commandSequence"] = playback.Sequence;
            body["mediaKey"] = playback.MediaKey;
            if (playback.PositionSeconds is { } position) body["positionSeconds"] = position;
            if (playback.Volume is { } volume) body["volume"] = volume;
            if (playback.PlaybackRate is { } rate) body["playbackRate"] = rate;
            if (playback.Muted is { } muted) body["muted"] = muted;
            if (playback.Loop is { } loop) body["loop"] = loop;
        }
        return JsonSerializer.Serialize(body);
    }

    public bool TryAccept(string json, out EmbeddedMediaObservation? observation)
    {
        observation = null;
        if (json.Length is < 2 or > 512 || json[0] != '{' || json[^1] != '}') return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            string[] required = ["type", "environmentGeneration", "surfaceGeneration", "sessionGeneration",
                "controllerGeneration", "documentGeneration", "eventSequence", "commandId", "commandSequence", "focus", "playing", "bounds"];
            string[] playbackKeys = ["mediaKey", "playbackState", "positionSeconds", "durationSeconds", "volume"];
            string[] preferenceKeys = ["playbackRate", "muted", "loop"];
            var hasPlayback = playbackKeys.Any(key => root.TryGetProperty(key, out _));
            var hasPreferences = preferenceKeys.Any(key => root.TryGetProperty(key, out _));
            var keys = required.Concat(hasPlayback ? playbackKeys : []).Concat(hasPreferences ? preferenceKeys : [])
                .Concat(root.TryGetProperty("errorCode", out _) ? ["errorCode"] : []).ToHashSet(StringComparer.Ordinal);
            if (!ExactKeys(root, keys) || hasPreferences && !hasPlayback) return false;
            foreach (var key in required.AsSpan(1, 5)) if (Integer(root, key) != generation) return false;
            var sequence = Integer(root, "eventSequence");
            var commandId = Integer(root, "commandId");
            var commandSequence = Integer(root, "commandSequence");
            if (sequence <= lastEvent || sequence <= 0 || commandId < 0 || commandSequence < 0 ||
                (commandId == 0 ? commandSequence != 0 || !Ready : pending?.Id != commandId || commandSequence != (pending.Playback?.Sequence ?? 0))) return false;
            var type = root.GetProperty("type").GetString();
            if (type is not ("ready" or "focus" or "media" or "back" or "armed")) return false;
            if (type == "ready" && (Ready || pending?.Command != "initialize" || commandId == 0) ||
                !Ready && type != "ready" || type == "armed" && (pending?.Command != "arm-activate" || pending.Armed || commandId == 0)) return false;
            var focus = root.GetProperty("focus").GetString();
            if (!Identifier(focus, 128)) return false;
            var playing = root.GetProperty("playing").GetBoolean();
            var bounds = root.GetProperty("bounds");
            if (!ExactKeys(bounds, new HashSet<string>(["x", "y", "width", "height"], StringComparer.Ordinal))) return false;
            var x = Number(bounds, "x"); var y = Number(bounds, "y");
            var width = Number(bounds, "width"); var height = Number(bounds, "height");
            if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 8192 || y + height > 8192) return false;
            var error = root.TryGetProperty("errorCode", out var errorValue) ? errorValue.GetString() : null;
            if (errorValue.ValueKind != JsonValueKind.Undefined && !Identifier(error, 128)) return false;
            EmbeddedMediaPlaybackEvent? playback = null;
            if (hasPlayback)
            {
                var key = root.GetProperty("mediaKey").GetString();
                var stateName = root.GetProperty("playbackState").GetString();
                if (!Identifier(key, ProtocolConstants.MaximumEmbeddedMediaKeyLength) || stateName is not ("loading" or "ready" or "playing" or "paused" or "ended" or "error")) return false;
                var state = Enum.Parse<EmbeddedMediaPlaybackState>(stateName, ignoreCase: true);
                var position = Number(root, "positionSeconds"); var duration = Number(root, "durationSeconds"); var volume = Number(root, "volume");
                var rate = hasPreferences ? Number(root, "playbackRate") : playbackRate;
                var isMuted = hasPreferences ? root.GetProperty("muted").GetBoolean() : muted;
                var isLooping = hasPreferences ? root.GetProperty("loop").GetBoolean() : loop;
                if (position < 0 || duration < 0 || duration > 86400 || position > duration || volume < 0 || volume > 1 ||
                    rate < ProtocolConstants.MinimumEmbeddedMediaPlaybackRate || rate > ProtocolConstants.MaximumEmbeddedMediaPlaybackRate ||
                    playing != (state == EmbeddedMediaPlaybackState.Playing)) return false;
                if (commandId == 0 && key != MediaKey || pending?.Playback is { } expected && commandId != 0 && key != expected.MediaKey) return false;
                if (pending?.Playback is { } preference && commandId != 0 && error is null &&
                    (preference.Kind == EmbeddedMediaPlaybackCommandKind.SetPlaybackRate && preference.PlaybackRate != rate ||
                     preference.Kind == EmbeddedMediaPlaybackCommandKind.SetMuted && preference.Muted != isMuted ||
                     preference.Kind == EmbeddedMediaPlaybackCommandKind.SetLoop && preference.Loop != isLooping)) return false;
                playback = new()
                {
                    SessionId = sessionId, Sequence = sequence, CommandSequence = commandSequence, MediaKey = key!, State = state,
                    PositionSeconds = position, DurationSeconds = duration, Volume = volume, PlaybackRate = rate, Muted = isMuted, Loop = isLooping, ErrorCode = error,
                };
            }
            if (pending?.Playback is not null && commandId != 0 && !hasPlayback) return false;
            if (type == "armed" && error is not null) return false;
            lastEvent = sequence;
            if (type == "armed") pending!.Armed = true;
            else if (commandId != 0) pending = null;
            if (type == "ready") Ready = true;
            if (playback is not null && type != "armed")
            { MediaKey = playback.MediaKey; playbackRate = playback.PlaybackRate; muted = playback.Muted; loop = playback.Loop; }
            observation = new(type!, commandId, focus!, x, y, width, height, type == "armed" ? null : playback);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException or ArgumentException)
        { return false; }
    }

    private static bool ExactKeys(JsonElement element, HashSet<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject()) if (!expected.Contains(field.Name) || !seen.Add(field.Name)) return false;
        return seen.SetEquals(expected);
    }
    private static long Integer(JsonElement element, string key)
    {
        var value = element.GetProperty(key).GetInt64();
        if (value < 0 || value > 9007199254740991) throw new FormatException();
        return value;
    }
    private static double Number(JsonElement element, string key)
    {
        var value = element.GetProperty(key).GetDouble();
        return double.IsFinite(value) ? value : throw new FormatException();
    }
    private static bool Identifier(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum &&
        value.All(ch => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.');
}

internal sealed record EmbeddedMediaObservation(string Type, long CommandId, string Focus,
    double X, double Y, double Width, double Height, EmbeddedMediaPlaybackEvent? Playback);
