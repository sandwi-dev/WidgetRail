using System.Collections.Concurrent;
using System.Diagnostics;
using WidgetRail.SpotifyPlayback;

namespace WidgetRail.WindowsSpotifyProvider;

internal sealed record SpotifyPlaybackHostClientOptions(
    string ExecutablePath,
    TimeSpan StartupTimeout,
    TimeSpan CommandTimeout)
{
    internal static SpotifyPlaybackHostClientOptions CreateDefault(string executablePath) =>
        new(executablePath, TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(8));

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ExecutablePath);
        if (!Path.IsPathFullyQualified(ExecutablePath) ||
            !string.Equals(Path.GetExtension(ExecutablePath), ".exe",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "Spotify playback host path must be an absolute executable path.",
                nameof(ExecutablePath));
        ValidateTimeout(StartupTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1),
            nameof(StartupTimeout));
        ValidateTimeout(CommandTimeout, TimeSpan.FromMilliseconds(250), TimeSpan.FromMinutes(1),
            nameof(CommandTimeout));
    }

    private static void ValidateTimeout(
        TimeSpan value, TimeSpan minimum, TimeSpan maximum, string name)
    {
        if (value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }
}

internal interface ISpotifyPlaybackHostClient : IAsyncDisposable
{
    event EventHandler<SpotifyPlaybackEventEnvelope>? EventReceived;
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task<SpotifyPlaybackEventEnvelope> ConnectAsync(
        SpotifyPlaybackConnectOptions options, CancellationToken cancellationToken);
    Task<SpotifyPlaybackEventEnvelope> ProvideTokenAsync(
        string tokenRequestId,
        TrustedHostSpotifyAccessToken token,
        CancellationToken cancellationToken);
    Task<SpotifyPlaybackEventEnvelope> SendAsync(
        string type, object? payload, CancellationToken cancellationToken);
}

internal interface ISpotifyPlaybackHostProcess : IAsyncDisposable
{
    TextWriter Input { get; }
    TextReader Output { get; }
    TextReader Error { get; }
    bool HasExited { get; }
    void Terminate();
}

internal interface ISpotifyPlaybackHostProcessFactory
{
    ISpotifyPlaybackHostProcess Start(string executablePath, int parentProcessId);
}

internal sealed class SpotifyPlaybackHostClient : ISpotifyPlaybackHostClient
{
    private static readonly IReadOnlySet<string> NotificationTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "host_initialized",
            "lifecycle_changed",
            "sdk_loaded",
            "connect_succeeded",
            "ready",
            "not_ready",
            "disconnected",
            "autoplay_failed",
            "sdk_error",
            "token_requested",
            "player_state_changed",
            "protocol_error",
        };
    private static readonly IReadOnlySet<string> CommandTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "connect",
            "provide_token",
            "disconnect",
            "pause",
            "resume",
            "toggle_play",
            "previous_track",
            "next_track",
            "get_current_state",
            "get_volume",
            "seek",
            "set_name",
            "set_volume",
            "shutdown",
        };

    private readonly SpotifyPlaybackHostClientOptions _options;
    private readonly ISpotifyPlaybackHostProcessFactory _processFactory;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, PendingResponse> _pending = new();
    private CancellationTokenSource? _sessionLifetime;
    private ISpotifyPlaybackHostProcess? _process;
    private Task? _readerTask;
    private Task? _errorDrainTask;
    private TaskCompletionSource? _sdkLoaded;
    private long _requestId;
    private int _disposed;

    internal SpotifyPlaybackHostClient(SpotifyPlaybackHostClientOptions options)
        : this(options, new SpotifyPlaybackHostProcessFactory()) { }

    internal SpotifyPlaybackHostClient(
        SpotifyPlaybackHostClientOptions options,
        ISpotifyPlaybackHostProcessFactory processFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        _options.Validate();
    }

    public event EventHandler<SpotifyPlaybackEventEnvelope>? EventReceived;

    public bool IsRunning
    {
        get
        {
            var process = _process;
            if (process is null) return false;
            try { return !process.HasExited && _sessionLifetime?.IsCancellationRequested == false; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning && _sdkLoaded?.Task.IsCompletedSuccessfully == true) return;
            await StopSessionAsync().ConfigureAwait(false);

            if (!File.Exists(_options.ExecutablePath) ||
                (File.GetAttributes(_options.ExecutablePath) & FileAttributes.ReparsePoint) != 0)
                throw new SpotifyPlaybackHostClientException(
                    "host_unavailable", "The Spotify playback host is unavailable.");

            _sessionLifetime = new CancellationTokenSource();
            _sdkLoaded = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _process = _processFactory.Start(_options.ExecutablePath, Environment.ProcessId);
                _readerTask = ReadEventsAsync(_process, _sessionLifetime.Token);
                _errorDrainTask = DrainErrorsAsync(_process.Error, _sessionLifetime.Token);
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _sessionLifetime.Token);
                startup.CancelAfter(_options.StartupTimeout);
                await _sdkLoaded.Task.WaitAsync(startup.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                await StopSessionAsync().ConfigureAwait(false);
                throw new SpotifyPlaybackHostClientException(
                    "host_start_timeout",
                    "The Spotify playback host did not become ready in time.", exception);
            }
            catch
            {
                await StopSessionAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _startGate.Release(); }
    }

    public Task<SpotifyPlaybackEventEnvelope> ConnectAsync(
        SpotifyPlaybackConnectOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return SendAsync("connect", options, cancellationToken);
    }

    public Task<SpotifyPlaybackEventEnvelope> ProvideTokenAsync(
        string tokenRequestId,
        TrustedHostSpotifyAccessToken token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenRequestId);
        if (tokenRequestId.Length > SpotifyPlaybackProtocol.MaximumRequestIdCharacters ||
            tokenRequestId.Any(character => character is < '!' or > '~'))
            throw new ArgumentException(
                "Spotify token request identifier is invalid.", nameof(tokenRequestId));
        ArgumentNullException.ThrowIfNull(token);
        var lease = new SpotifyAccessTokenLease(
            token.AccessToken, token.ExpiresAt, token.GrantedScopes);
        lease.Validate(DateTimeOffset.UtcNow);
        return SendAsync("provide_token", new
        {
            tokenRequestId,
            accessToken = lease.AccessToken,
            expiresAtUnixMilliseconds = lease.ExpiresAt.ToUnixTimeMilliseconds(),
            grantedScopes = lease.GrantedScopes,
        }, cancellationToken);
    }

    public async Task<SpotifyPlaybackEventEnvelope> SendAsync(
        string type, object? payload, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!CommandTypes.Contains(type))
            throw new ArgumentException(
                "Spotify playback command is unsupported.", nameof(type));
        if (!IsRunning)
            throw new SpotifyPlaybackHostClientException(
                "host_not_running", "The Spotify playback host is not running.");
        if (_pending.Count >= SpotifyPlaybackProtocol.MaximumPendingCommands)
            throw new SpotifyPlaybackHostClientException(
                "too_many_pending_commands",
                "The Spotify playback host has too many pending commands.");

        var requestId = Interlocked.Increment(ref _requestId)
            .ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        var expectedType = type switch
        {
            "get_current_state" => "current_state",
            "get_volume" => "volume",
            _ => "command_completed",
        };
        var completion = new TaskCompletionSource<SpotifyPlaybackEventEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, new(expectedType, completion)))
            throw new SpotifyPlaybackHostClientException(
                "request_collision", "Spotify playback request could not be created.");

        try
        {
            var message = SpotifyPlaybackProtocolCodec.EncodeRequest(requestId, type, payload);
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var process = _process ?? throw new SpotifyPlaybackHostClientException(
                    "host_not_running", "The Spotify playback host is not running.");
                await process.Input.WriteLineAsync(message.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await process.Input.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { _writeGate.Release(); }

            using var commandLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _sessionLifetime?.Token ?? CancellationToken.None);
            commandLifetime.CancelAfter(_options.CommandTimeout);
            try
            {
                return await completion.Task.WaitAsync(commandLifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested &&
                      _sessionLifetime?.IsCancellationRequested != true)
            {
                throw new SpotifyPlaybackHostClientException(
                    "host_command_timeout",
                    "The Spotify playback host did not answer in time.", exception);
            }
        }
        finally { _pending.TryRemove(requestId, out _); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _startGate.WaitAsync().ConfigureAwait(false);
        try { await StopSessionAsync().ConfigureAwait(false); }
        finally
        {
            _startGate.Release();
            _startGate.Dispose();
            _writeGate.Dispose();
        }
    }

    private async Task ReadEventsAsync(
        ISpotifyPlaybackHostProcess process, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await BoundedTextLineReader.ReadAsync(
                    process.Output, SpotifyPlaybackProtocol.MaximumMessageCharacters,
                    cancellationToken).ConfigureAwait(false);
                if (line is null)
                    throw new SpotifyPlaybackHostClientException(
                        "host_exited", "The Spotify playback host stopped unexpectedly.");
                var value = SpotifyPlaybackProtocolCodec.DecodeEvent(line);
                if (value.RequestId is { } requestId)
                {
                    if (!_pending.TryRemove(requestId, out var pending) ||
                        value.Type != "command_failed" && value.Type != pending.ExpectedType)
                        throw new SpotifyPlaybackHostClientException(
                            "protocol_violation",
                            "The Spotify playback host returned an unexpected response.");
                    if (value.Type == "command_failed")
                    {
                        var error = SafeCommandError(value.Payload);
                        pending.Completion.TrySetException(new SpotifyPlaybackHostClientException(
                            error.Code, error.Message));
                    }
                    else pending.Completion.TrySetResult(value);
                    continue;
                }

                if (!NotificationTypes.Contains(value.Type))
                    throw new SpotifyPlaybackHostClientException(
                        "protocol_violation",
                        "The Spotify playback host returned an unknown event.");
                if (value.Type == "sdk_loaded") _sdkLoaded?.TrySetResult();
                else if (value.Type == "sdk_error" &&
                         _sdkLoaded?.Task.IsCompleted == false)
                {
                    var error = SafeCommandError(value.Payload);
                    _sdkLoaded.TrySetException(new SpotifyPlaybackHostClientException(
                        error.Code, "The Spotify playback host could not initialize."));
                }
                try { EventReceived?.Invoke(this, value); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // A trusted observer cannot be allowed to tear down the protocol reader.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is SpotifyPlaybackProtocolException or
            SpotifyPlaybackHostClientException or IOException or ObjectDisposedException)
        {
            failure = exception;
        }
        finally
        {
            if (failure is not null)
            {
                _sdkLoaded?.TrySetException(failure);
                FailPending(failure);
                _sessionLifetime?.Cancel();
                try { if (_process?.HasExited == false) _process.Terminate(); }
                catch (InvalidOperationException) { }
            }
        }
    }

    private static async Task DrainErrorsAsync(
        TextReader error, CancellationToken cancellationToken)
    {
        try
        {
            while (await BoundedTextLineReader.ReadAsync(error, 4_096, cancellationToken)
                       .ConfigureAwait(false) is not null)
            {
                // Intentionally discarded. Child diagnostics are never forwarded because
                // a future SDK/runtime regression must not turn token material into logs.
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or
            SpotifyPlaybackProtocolException or IOException or ObjectDisposedException) { }
    }

    private async Task StopSessionAsync()
    {
        var lifetime = Interlocked.Exchange(ref _sessionLifetime, null);
        var process = Interlocked.Exchange(ref _process, null);
        lifetime?.Cancel();
        FailPending(new SpotifyPlaybackHostClientException(
            "host_stopped", "The Spotify playback host stopped."));
        if (process is not null)
        {
            try { if (!process.HasExited) process.Terminate(); }
            catch (InvalidOperationException) { }
            await process.DisposeAsync().ConfigureAwait(false);
        }
        var tasks = new[] { _readerTask, _errorDrainTask }
            .Where(task => task is not null).Cast<Task>().ToArray();
        _readerTask = null;
        _errorDrainTask = null;
        if (tasks.Length != 0)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or
                ObjectDisposedException or IOException) { }
        }
        lifetime?.Dispose();
        _sdkLoaded = null;
    }

    private void FailPending(Exception exception)
    {
        foreach (var (requestId, pending) in _pending.ToArray())
            if (_pending.TryRemove(requestId, out _))
                pending.Completion.TrySetException(exception);
    }

    private static CommandError SafeCommandError(System.Text.Json.JsonElement payload)
    {
        try
        {
            var value = SpotifyPlaybackProtocolCodec.DecodePayload<CommandError>(payload);
            if (string.IsNullOrWhiteSpace(value.Code) || value.Code.Length > 64 ||
                value.Code.Any(character => character is < '!' or > '~'))
                throw new SpotifyPlaybackProtocolException(
                    "invalid_payload", "The playback-host payload is invalid.");
            return new(value.Code, "The Spotify playback host rejected the command.");
        }
        catch (SpotifyPlaybackProtocolException)
        {
            return new("host_command_failed",
                "The Spotify playback host rejected the command.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record PendingResponse(
        string ExpectedType,
        TaskCompletionSource<SpotifyPlaybackEventEnvelope> Completion);
    private sealed record CommandError(string Code, string Message);
}

internal sealed class SpotifyPlaybackHostProcessFactory : ISpotifyPlaybackHostProcessFactory
{
    public ISpotifyPlaybackHostProcess Start(string executablePath, int parentProcessId)
    {
        var startInfo = CreateStartInfo(executablePath, parentProcessId);
        var process = Process.Start(startInfo) ??
            throw new SpotifyPlaybackHostClientException(
                "host_unavailable", "The Spotify playback host could not be started.");
        return new SpotifyPlaybackHostProcess(process);
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, int parentProcessId)
    {
        if (parentProcessId <= 0) throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ErrorDialog = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ??
                Environment.CurrentDirectory,
        };
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(parentProcessId.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        return startInfo;
    }
}

internal sealed class SpotifyPlaybackHostProcess : ISpotifyPlaybackHostProcess
{
    private readonly Process _process;

    internal SpotifyPlaybackHostProcess(Process process) =>
        _process = process ?? throw new ArgumentNullException(nameof(process));

    public TextWriter Input => _process.StandardInput;
    public TextReader Output => _process.StandardOutput;
    public TextReader Error => _process.StandardError;
    public bool HasExited => _process.HasExited;

    public void Terminate()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
    }

    public ValueTask DisposeAsync()
    {
        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class SpotifyPlaybackHostClientException : Exception
{
    internal SpotifyPlaybackHostClientException(
        string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    internal string Code { get; }
}
