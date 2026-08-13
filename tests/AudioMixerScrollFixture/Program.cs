using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using GameBarAlternative.FirstPartyWidgets.AudioMixer;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.Tests.AudioMixerScrollFixture;

internal static class Program
{
    private static readonly JsonSerializerOptions EvidenceJson = new()
    {
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
    {
        var pipeName = RequiredValue(args, "--widget-pipe");
        var instanceId = RequiredValue(args, "--widget-instance");
        var sessionNonce = RequiredValue(args, "--widget-session-nonce");
        var maximumBytes = int.Parse(
            RequiredValue(args, "--max-message-bytes"),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var snapshotPath = RequiredValue(args, "--fixture-snapshot-path");
        var controlPath = RequiredValue(args, "--fixture-control-path");

        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            var capabilities = new DeterministicAudioClient();
            var widget = new AudioMixerWidget();
            long evidenceSequence = 0;
            widget.Invalidated += (_, _) =>
            {
                if (widget.ViewState is not (AudioMixerViewState.Ready or AudioMixerViewState.Empty))
                    return;
                var snapshot = widget.RenderSnapshot(
                    instanceId, Interlocked.Increment(ref evidenceSequence));
                WriteAtomically(snapshotPath, JsonSerializer.Serialize(snapshot, EvidenceJson));
            };

            using var controlLifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var controlTask = WatchControlAsync(
                capabilities, controlPath, controlLifetime.Token);
            try
            {
                await new WidgetWorkerServer(
                        widget, instanceId, pipeName, maximumBytes, capabilities,
                        sessionNonce)
                    .RunAsync(shutdown.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                controlLifetime.Cancel();
                try
                {
                    await controlTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (controlLifetime.IsCancellationRequested)
                {
                }
            }
            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task WatchControlAsync(
        DeterministicAudioClient capabilities,
        string controlPath,
        CancellationToken cancellationToken)
    {
        string? applied = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? requested = null;
            try
            {
                if (File.Exists(controlPath))
                    requested = (await File.ReadAllTextAsync(controlPath, cancellationToken)
                        .ConfigureAwait(false)).Trim();
            }
            catch (IOException)
            {
                // The native fixture replaces this tiny file atomically. A
                // sharing race is retried by the same bounded active lifetime.
            }
            if (!string.IsNullOrEmpty(requested) &&
                !string.Equals(requested, applied, StringComparison.Ordinal))
            {
                capabilities.Apply(requested);
                applied = requested;
            }
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void WriteAtomically(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }

    private static string RequiredValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"Missing required argument {name}.");
        return args[index + 1];
    }

    private sealed class DeterministicAudioClient : IWidgetCapabilityClient
    {
        private readonly object _gate = new();
        private readonly List<Channel<WidgetAudioSessionsChanged>> _sessionSubscribers = [];
        private IReadOnlyList<WidgetAudioSession> _sessions = SessionsFor("full");

        public bool IsAvailable => true;

        public void Apply(string state)
        {
            var sessions = SessionsFor(state);
            Channel<WidgetAudioSessionsChanged>[] subscribers;
            lock (_gate)
            {
                _sessions = sessions;
                subscribers = _sessionSubscribers.ToArray();
            }
            var update = new WidgetAudioSessionsChanged(sessions);
            foreach (var subscriber in subscribers) subscriber.Writer.TryWrite(update);
        }

        public ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
            WidgetCapabilityOperation<TRequest, TResponse> operation,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object response = operation.OperationId switch
            {
                "audio.sessions.list" => CurrentSessions(),
                "audio.output.get" => new WidgetAudioOutput(0.62, false),
                "audio.devices.list" => new WidgetAudioDevice[]
                {
                    new("output-fixture", "Living Room Speakers", WidgetAudioDeviceDirection.Output, true),
                    new("input-fixture", "Controller Headset", WidgetAudioDeviceDirection.Input, true),
                },
                "audio.input.get" => new WidgetAudioInput(0.48, false),
                "audio.session.set-volume" or "audio.session.set-muted" or
                "audio.output.set-volume" or "audio.output.set-muted" or
                "audio.input.set-volume" or "audio.input.set-muted" =>
                    new WidgetCapabilityAcknowledgement(true),
                _ => throw new WidgetCapabilityException(
                    "unsupported_operation", operation.OperationId),
            };
            return ValueTask.FromResult((TResponse)response);
        }

        public ValueTask<IWidgetCapabilitySubscription<TPayload>> OpenSubscriptionAsync<TPayload>(
            WidgetCapabilityEvent<TPayload> platformEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object subscription;
            if (platformEvent.EventType == WidgetAudioCapabilities.SessionsChanged.EventType)
            {
                var channel = Channel.CreateBounded<WidgetAudioSessionsChanged>(
                    new BoundedChannelOptions(1)
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.DropOldest,
                    });
                lock (_gate) _sessionSubscribers.Add(channel);
                subscription = new ChannelSubscription<WidgetAudioSessionsChanged>(
                    channel, () =>
                    {
                        lock (_gate) _sessionSubscribers.Remove(channel);
                    });
            }
            else if (platformEvent.EventType == WidgetAudioCapabilities.OutputChanged.EventType)
            {
                subscription = EmptySubscription<WidgetAudioOutputChanged>();
            }
            else if (platformEvent.EventType == WidgetAudioCapabilities.DevicesChanged.EventType)
            {
                subscription = EmptySubscription<WidgetAudioDevicesChanged>();
            }
            else if (platformEvent.EventType == WidgetAudioCapabilities.InputChanged.EventType)
            {
                subscription = EmptySubscription<WidgetAudioInputChanged>();
            }
            else
            {
                throw new WidgetCapabilityException(
                    "unsupported_subscription", platformEvent.EventType);
            }
            return ValueTask.FromResult((IWidgetCapabilitySubscription<TPayload>)subscription);
        }

        private IReadOnlyList<WidgetAudioSession> CurrentSessions()
        {
            lock (_gate) return _sessions.ToArray();
        }

        private static ChannelSubscription<T> EmptySubscription<T>()
        {
            var channel = Channel.CreateBounded<T>(1);
            return new ChannelSubscription<T>(channel, () => { });
        }

        private static IReadOnlyList<WidgetAudioSession> SessionsFor(string state)
        {
            IEnumerable<int> indices = state switch
            {
                // Exact provider-shaped state used by the DLV-049 native host fixture.
                "live-four" => Enumerable.Range(0, 4),
                "full" => Enumerable.Range(0, 12),
                "remove-unrelated" => Enumerable.Range(0, 11),
                "remove-focused" => Enumerable.Range(0, 12).Where(index => index != 5),
                "added" => Enumerable.Range(0, 13),
                _ => throw new InvalidOperationException($"Unknown fixture state '{state}'."),
            };
            return indices.Select(index => new WidgetAudioSession(
                $"fixture-session-{index:00}",
                $"Application {index:00}",
                Math.Clamp(0.28 + index * 0.045, 0, 1),
                index % 5 == 4,
                index % 3 == 0)).ToArray();
        }
    }

    private sealed class ChannelSubscription<T>(Channel<T> channel, Action dispose)
        : IWidgetCapabilitySubscription<T>
    {
        private int _disposed;

        public IAsyncEnumerable<T> ReadAllAsync(
            CancellationToken cancellationToken = default) =>
            channel.Reader.ReadAllAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
                channel.Writer.TryComplete();
            }
            return ValueTask.CompletedTask;
        }
    }
}
