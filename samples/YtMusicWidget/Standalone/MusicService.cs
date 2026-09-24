using System.Diagnostics;
using System.Text.Json;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed class MusicService : IMusicService
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _directory;
    private JsonProcess? _backend;
    private JsonProcess? _player;
    private string? _playerProfile;
    private MusicState _state = MusicState.Empty;
    private MusicItem[] _originalQueue = [];
    private CancellationTokenSource? _selection;
    private long _generation;
    public MusicState State { get { lock (_gate) return _state; } }
    public event Action? Changed;

    public MusicService(string directory, double initialVolume = .5)
    {
        if (!double.IsFinite(initialVolume) || initialVolume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(initialVolume));
        _directory = Path.GetFullPath(directory);
        _state = MusicState.Empty with { Player = new(Volume: initialVolume) };
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        await _initialization.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_backend is not null) return;
            var backend = new JsonProcess(Path.Combine(_directory, "python", "python.exe"),
                ["-I", "-u", Path.Combine(_directory, "service", "service.py")]);
            try
            {
                var result = await backend.CallAsync("status", null, token).ConfigureAwait(false);
                _backend = backend;
                lock (_gate) _state = _state with { Connected = result.GetProperty("connected").GetBoolean() };
                Changed?.Invoke();
            }
            catch { await backend.DisposeAsync().ConfigureAwait(false); throw; }
        }
        finally { _initialization.Release(); }
    }

    public async Task<string> SignInAsync(CancellationToken token)
    {
        await InitializeAsync(token).ConfigureAwait(false);
        await _backend!.CallAsync("signin", null, token).ConfigureAwait(false);
        lock (_gate) _state = _state with { Connected = true };
        Changed?.Invoke();
        return "Connected to YouTube Music";
    }

    public async Task DisconnectAsync(CancellationToken token)
    {
        lock (_gate) { _selection?.Cancel(); _generation++; }
        if (_player is not null) await _player.CallAsync("stop", null, token).ConfigureAwait(false);
        await InitializeAsync(token).ConfigureAwait(false);
        await _backend!.CallAsync("disconnect", null, token).ConfigureAwait(false);
        lock (_gate) { _state = MusicState.Empty; _originalQueue = []; }
        Changed?.Invoke();
    }

    public async Task<MusicPage> BrowseAsync(string kind, string value, CancellationToken token)
    {
        await InitializeAsync(token).ConfigureAwait(false);
        var result = await _backend!.CallAsync("browse", new { kind, value }, token).ConfigureAwait(false);
        return result.Deserialize<MusicPage>(JsonProcess.Json) ?? throw new IOException("The music page is unavailable.");
    }

    public async Task RadioAsync(MusicItem song, CancellationToken token)
    {
        var radio = await BrowseAsync("radio", song.Id, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (radio.Items.Count == 0) throw new IOException("No radio tracks were returned for this song.");
        await PlayAsync(radio.Items, 0, token).ConfigureAwait(false);
    }

    public async Task PlayAsync(IReadOnlyList<MusicItem> tracks, int index, CancellationToken token)
    {
        if (tracks.Count is < 1 or > 500 || index < 0 || index >= tracks.Count || tracks.Any(t => t.Kind != "song"))
            throw new ArgumentException("Select a song from a valid queue.");
        lock (_gate)
        {
            _originalQueue = tracks.ToArray();
            var queue = _state.Shuffle ? MusicQueue.Shuffled(tracks, index, Random.Shared) : tracks.ToArray();
            _state = _state with { Queue = queue, Index = index };
        }
        await SelectAsync(token).ConfigureAwait(false);
    }

    private async Task EnsurePlayerAsync(CancellationToken token)
    {
        await _initialization.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_player is null)
            {
                var profile = Path.Combine(Path.GetTempPath(), "WidgetRail", "ytmusic-player", Guid.NewGuid().ToString("N"));
                var player = new JsonProcess(Path.Combine(_directory, "YtMusicPlaybackHost.exe"),
                    ["--parent-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--profile", profile]);
                player.Event += OnPlayerEvent;
                try
                {
                    // Explicit handshake avoids losing a ready event before the subscriber is attached.
                    await player.CallAsync("ready", null, token).ConfigureAwait(false);
                    _player = player;
                    _playerProfile = profile;
                }
                catch
                {
                    player.Event -= OnPlayerEvent;
                    await player.DisposeAsync().ConfigureAwait(false);
                    await DeletePlayerProfileAsync(profile).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally { _initialization.Release(); }
    }

    private async Task SelectAsync(CancellationToken token)
    {
        MusicItem current;
        long generation;
        CancellationTokenSource selection;
        CancellationToken selectionToken;
        lock (_gate)
        {
            if (_state.Current is not { } item) return;
            current = item;
            _selection?.Cancel();
            _selection?.Dispose();
            selection = _selection = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            selectionToken = selection.Token;
            generation = ++_generation;
            _state = _state with { Player = new(current.Id, Buffering: true, Volume: _state.Player.Volume) };
        }
        Changed?.Invoke();
        try
        {
            await InitializeAsync(selectionToken).ConfigureAwait(false);
            await EnsurePlayerAsync(selectionToken).ConfigureAwait(false);
            var stream = await _backend!.CallAsync("resolve", new { videoId = current.Id }, selectionToken).ConfigureAwait(false);
            selectionToken.ThrowIfCancellationRequested();
            // Generation travels to the player; stale audio events cannot replace the selected song.
            await _player!.CallAsync("load", new
            {
                url = stream.GetProperty("url").GetString(), generation, trackId = current.Id,
                title = current.Title, artist = current.Subtitle, artwork = current.Artwork, volume = State.Player.Volume,
            }, selectionToken).ConfigureAwait(false);
            var next = State;
            if (next.Index + 1 < next.Queue.Count)
                _ = PrefetchAsync(next.Queue[next.Index + 1].Id, selectionToken);
        }
        catch (OperationCanceledException) when (selection.IsCancellationRequested) { }
        catch
        {
            lock (_gate)
                if (_generation == generation)
                    _state = _state with { Player = _state.Player with { Buffering = false, Playing = false,
                        Error = "Playback could not start. Retry the song or reconnect in Setup." } };
            Changed?.Invoke();
            throw;
        }
    }

    private async Task PrefetchAsync(string id, CancellationToken token)
    {
        try { await _backend!.CallAsync("prefetch", new { videoId = id }, token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or OperationCanceledException) { }
    }

    private void OnPlayerEvent(JsonElement value)
    {
        var kind = value.GetProperty("event").GetString();
        if (kind == "failed")
        {
            lock (_gate) _state = _state with { Player = _state.Player with { Playing = false, Buffering = false,
                Error = "The audio player stopped. Restart the widget in Settings → Widgets." } };
            Changed?.Invoke();
            return;
        }
        if (!value.TryGetProperty("generation", out var generation)) return;
        lock (_gate)
        {
            if (generation.GetInt64() != _generation) return;
            if (kind == "state")
                _state = _state with { Player = value.GetProperty("state").Deserialize<PlayerState>(JsonProcess.Json) ?? _state.Player };
        }
        Changed?.Invoke();
        if (kind == "ended") _ = HandlePlayerCommandAsync("ended");
        else if (kind == "command") _ = HandlePlayerCommandAsync(value.GetProperty("command").GetString() ?? "");
    }

    private async Task HandlePlayerCommandAsync(string command)
    {
        try { await CommandAsync(command, null, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or OperationCanceledException) { }
    }

    public async Task CommandAsync(string command, double? value, CancellationToken token)
    {
        if (command is "next" or "previous" or "ended" or "queue")
        {
            lock (_gate)
            {
                var index = command == "previous" ? Math.Max(0, _state.Index - 1) :
                    command == "queue" ? (int)(value ?? -1) : MusicQueue.Next(_state.Queue.Count, _state.Index, _state.Repeat, command == "ended");
                if (index < 0 || index >= _state.Queue.Count) return;
                _state = _state with { Index = index };
            }
            await SelectAsync(token).ConfigureAwait(false);
        }
        else if (command is "shuffle" or "repeat")
        {
            lock (_gate)
            {
                if (command == "repeat") _state = _state with { Repeat = _state.Repeat switch { "off" => "all", "all" => "one", _ => "off" } };
                else
                {
                    var enabled = !_state.Shuffle;
                    var queue = enabled ? MusicQueue.Shuffled(_state.Queue, _state.Index, Random.Shared) : _originalQueue;
                    var current = _state.Current;
                    _state = _state with { Shuffle = enabled, Queue = queue,
                        Index = enabled ? _state.Index : Array.FindIndex(queue, item => item == current) };
                }
            }
            Changed?.Invoke();
        }
        else if (_player is not null && command is "toggle" or "play" or "pause" or "seek" or "volume")
        {
            if (value is { } number && !double.IsFinite(number)) return;
            await _player.CallAsync(command, new { value }, token).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        lock (_gate) _selection?.Cancel();
        if (_player is not null)
        {
            _player.Event -= OnPlayerEvent;
            await _player.DisposeAsync().ConfigureAwait(false);
            await DeletePlayerProfileAsync(_playerProfile).ConfigureAwait(false);
        }
        if (_backend is not null) await _backend.DisposeAsync().ConfigureAwait(false);
        _selection?.Dispose();
        _lifetime.Dispose();
    }

    private static async Task DeletePlayerProfileAsync(string? profile)
    {
        // Only paths generated by this instance reach this method; no provider or child-supplied path.
        if (profile is null) return;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try { if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            // Browser utility processes can release their handles just after the host exits.
            await Task.Delay(100 << attempt).ConfigureAwait(false);
        }
    }
}
