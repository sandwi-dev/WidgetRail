using System.Runtime.CompilerServices;
using System.Reflection;
using System.Threading.Channels;
using GameBarAlternative.FirstPartyWidgets.AudioMixer;
using GameBarAlternative.WidgetSdk;

internal static class AudioMixerProviderSessionScenarios
{
    internal static async Task SubscriptionPrecedesEverySnapshot()
    {
        var fixture = new ProviderSessionFixture
        {
            Sessions = [Session("old", "Old snapshot", 0.2)],
        };
        fixture.OnSessionsGet = () => fixture.EmitSessions(
            [Session("new", "Buffered event", 0.8)]);
        var observations = new ObservationLog();
        var session = CreateSession(fixture, observations);

        session.Start();
        await observations.WaitUntilAsync(items => items.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.SessionsChanged));

        AssertPrecedes(fixture.Operations, "open:sessions", "get:sessions");
        AssertPrecedes(fixture.Operations, "open:output", "get:output");
        AssertPrecedes(fixture.Operations, "get:output", "open:devices");
        AssertPrecedes(fixture.Operations, "get:sessions", "open:devices");
        AssertPrecedes(fixture.Operations, "get:output", "open:input");
        AssertPrecedes(fixture.Operations, "get:sessions", "open:input");
        AssertPrecedes(fixture.Operations, "open:devices", "get:devices");
        AssertPrecedes(fixture.Operations, "open:input", "get:input");
        var required = observations.Single(
            AudioMixerProviderObservationKind.RequiredSnapshot);
        Assert.Equal("old", required.Sessions!.Single().SessionId);
        var buffered = observations.Last(
            AudioMixerProviderObservationKind.SessionsChanged);
        Assert.Equal("new", buffered.Sessions!.Single().SessionId);

        await session.StopAsync();
        Assert.Equal(0, fixture.ActiveSubscriptions);

        var completionFixture = new ProviderSessionFixture
        {
            Sessions = [Session("game", "Game", 0.5)],
        };
        var completionObservations = new ObservationLog();
        var completionSession = CreateSession(completionFixture, completionObservations);
        completionSession.Start();
        await completionObservations.WaitUntilAsync(items => items.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.RequiredSnapshot));
        completionFixture.CompleteSessions();
        await completionObservations.WaitUntilAsync(items => items.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.RequiredFailed &&
            observation.ViewState == AudioMixerViewState.ChannelClosed));
        Assert.Equal("Audio service channel closed", completionObservations.Last(
            AudioMixerProviderObservationKind.RequiredFailed).Status);
        await completionSession.StopAsync();
        Assert.Equal(0, completionFixture.ActiveSubscriptions);
    }

    internal static async Task OptionalRetryIsIsolated()
    {
        var fixture = new ProviderSessionFixture
        {
            Sessions = [Session("game", "Game", 0.5)],
            DeviceSubscriptionException = new WidgetCapabilityException(
                "permission_denied", "private provider detail"),
        };
        var observations = new ObservationLog();
        var session = CreateSession(fixture, observations);

        session.Start();
        await observations.WaitUntilAsync(items => items.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.DevicesFailed));
        var denied = observations.Last(AudioMixerProviderObservationKind.DevicesFailed);
        Assert.Equal(AudioOptionalSectionState.PermissionDenied, denied.OptionalState);
        Assert.True(observations.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.InputChanged),
            "The independent input stream did not become healthy.");
        Assert.True(observations.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.RequiredSnapshot),
            "The required mixer snapshot did not remain healthy.");

        fixture.DeviceSubscriptionException = null;
        fixture.Devices = [new WidgetAudioDevice(
            "headset", "Headset", WidgetAudioDeviceDirection.Output, true)];
        var beforeRetry = observations.TotalCount;
        Assert.True(session.Retry(AudioMixerProviderSection.Devices),
            "The active session refused an optional retry.");
        await observations.WaitUntilAsync(items => fixture.DeviceSubscriptionCount == 2 &&
            items.Any(observation =>
                observation.Kind == AudioMixerProviderObservationKind.DevicesChanged &&
                observation.Devices?.SingleOrDefault()?.DeviceId == "headset"));
        Assert.Equal(1, fixture.InputSubscriptionCount);
        Assert.Equal(1, fixture.SessionsSubscriptionCount);
        Assert.Equal(1, fixture.OutputSubscriptionCount);
        var retryObservations = observations.Snapshot()
            .Skip(beforeRetry)
            .Where(observation => observation.Kind is
                AudioMixerProviderObservationKind.DevicesLoading or
                AudioMixerProviderObservationKind.DevicesChanged or
                AudioMixerProviderObservationKind.DevicesFailed)
            .ToArray();
        Assert.True(retryObservations.Length >= 2,
            "The retry did not publish its loading and terminal observations.");
        Assert.Equal(AudioMixerProviderObservationKind.DevicesLoading,
            retryObservations[0].Kind);
        Assert.Equal(AudioMixerProviderObservationKind.DevicesChanged,
            retryObservations[^1].Kind);

        await session.StopAsync();
        Assert.Equal(0, fixture.ActiveSubscriptions);
    }

    internal static async Task RetryAndStopAreAtomic()
    {
        var fixture = new ProviderSessionFixture
        {
            Sessions = [Session("game", "Game", 0.5)],
            BlockDeviceCancellation = true,
        };
        var observations = new ObservationLog();
        var session = CreateSession(fixture, observations);
        session.Start();
        await observations.WaitUntilAsync(items => items.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.DevicesChanged));

        var retryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = Task.Run(() =>
        {
            retryStarted.TrySetResult();
            return session.Retry(AudioMixerProviderSection.Devices);
        });
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.DeviceCancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = Task.Run(async () =>
        {
            stopStarted.TrySetResult();
            await session.StopAsync();
        });
        await stopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(!stop.IsCompleted,
            "Terminal stop overtook the admitted retry transition.");

        fixture.ReleaseDeviceCancellation.TrySetResult();
        Assert.True(await retry.WaitAsync(TimeSpan.FromSeconds(2)),
            "The admitted retry did not complete.");
        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, fixture.ActiveSubscriptions);
        Assert.True(!session.Retry(AudioMixerProviderSection.Devices),
            "A terminally stopped session admitted another retry.");

        var terminalObservationCount = observations.TotalCount;
        fixture.EmitDevices([new WidgetAudioDevice(
            "late", "Late device", WidgetAudioDeviceDirection.Output, true)]);
        Assert.Equal(terminalObservationCount, observations.TotalCount);

        var fields = typeof(AudioMixerProviderSession).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(((Task)fields.Single(field => field.Name == "_runTask")
                .GetValue(session)!).IsCompleted,
            "The stopped session retained continuing root work.");
        Assert.True(fields.Single(field => field.Name == "_devicesAttemptLifetime")
                .GetValue(session) is null &&
            fields.Single(field => field.Name == "_inputAttemptLifetime")
                .GetValue(session) is null,
            "The stopped session retained an optional attempt.");
        foreach (var name in new[] { "_devicesRetrySignal", "_inputRetrySignal" })
        {
            var disposed = false;
            try
            {
                ((SemaphoreSlim)fields.Single(field => field.Name == name)
                    .GetValue(session)!).Release();
            }
            catch (ObjectDisposedException)
            {
                disposed = true;
            }
            Assert.True(disposed, "The stopped session retained a retry signal.");
        }
    }

    internal static async Task CancellationIgnoringWorkDrains()
    {
        var resultFixture = new ProviderSessionFixture
        {
            Sessions = [Session("late", "Late result", 0.9)],
            HoldSessionsGet = true,
            IgnoreSessionsGetCancellation = true,
        };
        var resultObservations = new ObservationLog();
        var resultSession = CreateSession(resultFixture, resultObservations);
        resultSession.Start();
        await resultFixture.SessionsGetStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var resultStop = resultSession.StopAsync().AsTask();
        Assert.True(!resultStop.IsCompleted,
            "Stop completed before the cancellation-ignoring result was observed.");
        resultFixture.ReleaseSessionsGet.TrySetResult();
        await resultStop.WaitAsync(TimeSpan.FromSeconds(2));
        await resultSession.InitialPublication.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(!resultObservations.Any(observation =>
                observation.Kind == AudioMixerProviderObservationKind.RequiredSnapshot),
            "A canceled late snapshot was published.");
        Assert.Equal(0, resultFixture.ActiveSubscriptions);

        var eventFixture = new ProviderSessionFixture
        {
            Sessions = [Session("current", "Current", 0.4)],
            IgnoreSessionsEventCancellation = true,
        };
        var eventObservations = new ObservationLog();
        var eventSession = CreateSession(eventFixture, eventObservations);
        eventSession.Start();
        await eventObservations.WaitUntilAsync(items => items.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.RequiredSnapshot));
        var beforeStop = eventObservations.Count(
            AudioMixerProviderObservationKind.SessionsChanged);

        var eventStop = eventSession.StopAsync().AsTask();
        Assert.True(!eventStop.IsCompleted,
            "Stop did not retain the cancellation-ignoring event pump for drain.");
        eventFixture.EmitSessions([Session("late", "Late event", 0.95)]);
        await eventStop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(beforeStop, eventObservations.Count(
            AudioMixerProviderObservationKind.SessionsChanged));
        Assert.Equal(0, eventFixture.ActiveSubscriptions);
    }

    internal static async Task ReplacementRejectsLateSnapshot()
    {
        var oldFixture = new ProviderSessionFixture
        {
            Sessions = [Session("old", "Old", 0.2)],
            HoldSessionsGet = true,
            IgnoreSessionsGetCancellation = true,
        };
        var newFixture = new ProviderSessionFixture
        {
            Sessions = [Session("new", "New", 0.8)],
        };
        var applied = new ObservationLog();
        AudioMixerProviderSession? current = null;
        void Publish(
            AudioMixerProviderSession source,
            AudioMixerProviderObservation observation)
        {
            if (ReferenceEquals(current, source)) applied.Add(observation);
        }

        var oldSession = new AudioMixerProviderSession(
            oldFixture.BuildServices().Audio, CancellationToken.None, Publish);
        current = oldSession;
        oldSession.Start();
        await oldFixture.SessionsGetStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var newSession = new AudioMixerProviderSession(
            newFixture.BuildServices().Audio, CancellationToken.None, Publish);
        current = newSession;
        newSession.Start();
        var oldStop = oldSession.StopAsync().AsTask();
        await applied.WaitUntilAsync(items => items.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.RequiredSnapshot &&
            observation.Sessions?.SingleOrDefault()?.SessionId == "new"));

        oldFixture.ReleaseSessionsGet.TrySetResult();
        await oldStop.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(!applied.Any(observation =>
                observation.Kind == AudioMixerProviderObservationKind.RequiredSnapshot &&
                observation.Sessions?.SingleOrDefault()?.SessionId == "old"),
            "The replaced session published its late snapshot.");
        await newSession.StopAsync();
        Assert.Equal(0, oldFixture.ActiveSubscriptions);
        Assert.Equal(0, newFixture.ActiveSubscriptions);
    }

    internal static async Task RequiredFailureIsClassified()
    {
        var fixture = new ProviderSessionFixture
        {
            GetException = new WidgetCapabilityException(
                "permission_denied", "private provider detail"),
        };
        var observations = new ObservationLog();
        var session = CreateSession(fixture, observations);

        session.Start();
        await observations.WaitUntilAsync(items => items.Any(observation =>
            observation.Kind == AudioMixerProviderObservationKind.RequiredFailed));
        var failure = observations.Last(AudioMixerProviderObservationKind.RequiredFailed);
        Assert.Equal(AudioMixerViewState.PermissionDenied, failure.ViewState);
        Assert.Equal("Audio read permission denied", failure.Status);
        Assert.True(!failure.Status!.Contains("private", StringComparison.Ordinal),
            "Provider details escaped the bounded failure classification.");

        await session.StopAsync();
        Assert.Equal(0, fixture.ActiveSubscriptions);
    }

    internal static Task SessionOwnsProviderCoordination()
    {
        var widgetFields = typeof(AudioMixerWidget).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(!widgetFields.Any(field => field.FieldType == typeof(SemaphoreSlim)),
            "The widget still owns a provider retry semaphore.");
        Assert.True(!widgetFields.Any(field =>
                field.FieldType == typeof(CancellationTokenSource)),
            "The widget still owns a provider attempt or Active lifetime.");
        Assert.Equal(1, widgetFields.Count(field =>
            field.FieldType == typeof(AudioMixerProviderSession)));

        var widgetMethods = typeof(AudioMixerWidget).GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic);
        var providerPumpNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ObserveAudioAsync",
            "ObserveOptionalDevicesAsync",
            "ObserveOptionalInputAsync",
            "ObserveSessionChangesAsync",
            "ObserveOutputChangesAsync",
            "ObserveDeviceChangesAsync",
            "ObserveInputChangesAsync",
        };
        Assert.True(!widgetMethods.Any(method => providerPumpNames.Contains(method.Name)),
            "The widget still owns a provider event pump.");
        var sessionMethods = typeof(AudioMixerProviderSession).GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(4, sessionMethods.Count(method => method.Name is
            "ObserveSessionChangesAsync" or
            "ObserveOutputChangesAsync" or
            "ObserveDeviceChangesAsync" or
            "ObserveInputChangesAsync"));
        Assert.True(!typeof(AudioMixerProviderSession).IsPublic,
            "The provider session unexpectedly expanded the public API.");
        return Task.CompletedTask;
    }

    private static AudioMixerProviderSession CreateSession(
        ProviderSessionFixture fixture,
        ObservationLog observations) =>
        new(fixture.BuildServices().Audio, CancellationToken.None,
            (_, observation) => observations.Add(observation));

    private static WidgetAudioSession Session(
        string id,
        string name,
        double volume) => new(id, name, volume, false, false);

    private static void AssertPrecedes(
        IReadOnlyList<string> operations,
        string first,
        string second)
    {
        var firstIndex = operations.IndexOf(first);
        var secondIndex = operations.IndexOf(second);
        Assert.True(firstIndex >= 0 && secondIndex >= 0 && firstIndex < secondIndex,
            $"Expected {first} before {second}; actual: {string.Join(", ", operations)}");
    }

    private sealed class ObservationLog
    {
        private readonly object _gate = new();
        private readonly List<AudioMixerProviderObservation> _items = [];
        private TaskCompletionSource _changed = NewSignal();

        internal void Add(AudioMixerProviderObservation observation)
        {
            TaskCompletionSource changed;
            lock (_gate)
            {
                _items.Add(observation);
                changed = _changed;
                _changed = NewSignal();
            }
            changed.TrySetResult();
        }

        internal async Task WaitUntilAsync(
            Func<IReadOnlyList<AudioMixerProviderObservation>, bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (true)
            {
                Task changed;
                lock (_gate)
                {
                    if (condition(_items)) return;
                    changed = _changed.Task;
                }
                await changed.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        internal bool Any(Func<AudioMixerProviderObservation, bool> predicate)
        {
            lock (_gate) return _items.Any(predicate);
        }

        internal AudioMixerProviderObservation Last(AudioMixerProviderObservationKind kind)
        {
            lock (_gate) return _items.Last(item => item.Kind == kind);
        }

        internal AudioMixerProviderObservation Single(AudioMixerProviderObservationKind kind)
        {
            lock (_gate) return _items.Single(item => item.Kind == kind);
        }

        internal int Count(AudioMixerProviderObservationKind kind)
        {
            lock (_gate) return _items.Count(item => item.Kind == kind);
        }

        internal int TotalCount
        {
            get { lock (_gate) return _items.Count; }
        }

        internal IReadOnlyList<AudioMixerProviderObservation> Snapshot()
        {
            lock (_gate) return _items.ToArray();
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ProviderSessionFixture
    {
        private readonly object _gate = new();
        private readonly List<string> _operations = [];
        private readonly List<Channel<WidgetAudioSessionsChanged>> _sessions = [];
        private readonly List<Channel<WidgetAudioOutputChanged>> _outputs = [];
        private readonly List<Channel<WidgetAudioDevicesChanged>> _devices = [];
        private readonly List<Channel<WidgetAudioInputChanged>> _inputs = [];
        private int _sessionsSubscriptionCount;
        private int _outputSubscriptionCount;
        private int _deviceSubscriptionCount;
        private int _inputSubscriptionCount;
        private int _activeSubscriptions;

        internal IReadOnlyList<WidgetAudioSession> Sessions { get; set; } = [];
        internal WidgetAudioOutput Output { get; set; } = new(0.6, false);
        internal IReadOnlyList<WidgetAudioDevice> Devices { get; set; } =
        [new("speakers", "Speakers", WidgetAudioDeviceDirection.Output, true)];
        internal WidgetAudioInput Input { get; set; } = new(0.5, false);
        internal Exception? GetException { get; set; }
        internal Exception? DeviceSubscriptionException { get; set; }
        internal bool HoldSessionsGet { get; set; }
        internal bool IgnoreSessionsGetCancellation { get; set; }
        internal bool IgnoreSessionsEventCancellation { get; set; }
        internal bool BlockDeviceCancellation { get; set; }
        internal Action? OnSessionsGet { get; set; }
        internal TaskCompletionSource SessionsGetStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSessionsGet { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource DeviceCancellationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDeviceCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal IReadOnlyList<string> Operations
        {
            get { lock (_gate) return _operations.ToArray(); }
        }

        internal int SessionsSubscriptionCount =>
            Volatile.Read(ref _sessionsSubscriptionCount);
        internal int OutputSubscriptionCount =>
            Volatile.Read(ref _outputSubscriptionCount);
        internal int DeviceSubscriptionCount =>
            Volatile.Read(ref _deviceSubscriptionCount);
        internal int InputSubscriptionCount =>
            Volatile.Read(ref _inputSubscriptionCount);
        internal int ActiveSubscriptions => Volatile.Read(ref _activeSubscriptions);

        internal WidgetHostServices BuildServices() => new WidgetTestHostServicesBuilder()
            .WithHandler(WidgetAudioCapabilities.GetSessions,
                (_, token) => GetSessionsAsync(token))
            .WithHandler(WidgetAudioCapabilities.GetOutput,
                (_, token) => GetOutputAsync(token))
            .WithHandler(WidgetAudioCapabilities.GetDevices,
                (_, token) => GetDevicesAsync(token))
            .WithHandler(WidgetAudioCapabilities.GetInput,
                (_, token) => GetInputAsync(token))
            .WithEventStream(WidgetAudioCapabilities.SessionsChanged, OpenSessions)
            .WithEventStream(WidgetAudioCapabilities.OutputChanged, OpenOutput)
            .WithEventStream(WidgetAudioCapabilities.DevicesChanged, OpenDevices)
            .WithEventStream(WidgetAudioCapabilities.InputChanged, OpenInput)
            .Build();

        internal void EmitSessions(IReadOnlyList<WidgetAudioSession> value)
        {
            Sessions = value;
            Channel<WidgetAudioSessionsChanged>[] subscribers;
            lock (_gate) subscribers = _sessions.ToArray();
            foreach (var subscriber in subscribers)
                subscriber.Writer.TryWrite(new WidgetAudioSessionsChanged(value));
        }

        internal void CompleteSessions()
        {
            Channel<WidgetAudioSessionsChanged>[] subscribers;
            lock (_gate) subscribers = _sessions.ToArray();
            foreach (var subscriber in subscribers) subscriber.Writer.TryComplete();
        }

        internal void EmitDevices(IReadOnlyList<WidgetAudioDevice> value)
        {
            Devices = value;
            Channel<WidgetAudioDevicesChanged>[] subscribers;
            lock (_gate) subscribers = _devices.ToArray();
            foreach (var subscriber in subscribers)
                subscriber.Writer.TryWrite(new WidgetAudioDevicesChanged(value));
        }

        private async ValueTask<IReadOnlyList<WidgetAudioSession>> GetSessionsAsync(
            CancellationToken cancellationToken)
        {
            Record("get:sessions");
            if (GetException is not null) throw GetException;
            var snapshot = Sessions.ToArray();
            SessionsGetStarted.TrySetResult();
            if (HoldSessionsGet)
            {
                if (IgnoreSessionsGetCancellation)
                    await ReleaseSessionsGet.Task.ConfigureAwait(false);
                else
                    await ReleaseSessionsGet.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            OnSessionsGet?.Invoke();
            return snapshot;
        }

        private ValueTask<WidgetAudioOutput> GetOutputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record("get:output");
            if (GetException is not null) throw GetException;
            return ValueTask.FromResult(Output);
        }

        private ValueTask<IReadOnlyList<WidgetAudioDevice>> GetDevicesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record("get:devices");
            return ValueTask.FromResult<IReadOnlyList<WidgetAudioDevice>>(Devices.ToArray());
        }

        private ValueTask<WidgetAudioInput> GetInputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record("get:input");
            return ValueTask.FromResult(Input);
        }

        private IAsyncEnumerable<WidgetAudioSessionsChanged> OpenSessions(
            CancellationToken cancellationToken)
        {
            Record("open:sessions");
            Interlocked.Increment(ref _sessionsSubscriptionCount);
            var channel = CreateChannel<WidgetAudioSessionsChanged>();
            lock (_gate) _sessions.Add(channel);
            return ReadSessions(channel, cancellationToken);
        }

        private IAsyncEnumerable<WidgetAudioOutputChanged> OpenOutput(
            CancellationToken cancellationToken)
        {
            Record("open:output");
            Interlocked.Increment(ref _outputSubscriptionCount);
            var channel = CreateChannel<WidgetAudioOutputChanged>();
            lock (_gate) _outputs.Add(channel);
            return Read(channel, _outputs, cancellationToken);
        }

        private IAsyncEnumerable<WidgetAudioDevicesChanged> OpenDevices(
            CancellationToken cancellationToken)
        {
            Record("open:devices");
            Interlocked.Increment(ref _deviceSubscriptionCount);
            if (DeviceSubscriptionException is not null) throw DeviceSubscriptionException;
            var channel = CreateChannel<WidgetAudioDevicesChanged>();
            lock (_gate) _devices.Add(channel);
            return ReadDevices(channel, cancellationToken);
        }

        private IAsyncEnumerable<WidgetAudioInputChanged> OpenInput(
            CancellationToken cancellationToken)
        {
            Record("open:input");
            Interlocked.Increment(ref _inputSubscriptionCount);
            var channel = CreateChannel<WidgetAudioInputChanged>();
            lock (_gate) _inputs.Add(channel);
            return Read(channel, _inputs, cancellationToken);
        }

        private async IAsyncEnumerable<WidgetAudioSessionsChanged> ReadSessions(
            Channel<WidgetAudioSessionsChanged> channel,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _activeSubscriptions);
            try
            {
                if (IgnoreSessionsEventCancellation)
                {
                    await foreach (var item in channel.Reader.ReadAllAsync())
                        yield return item;
                }
                else
                {
                    await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                        yield return item;
                }
            }
            finally
            {
                lock (_gate) _sessions.Remove(channel);
                Interlocked.Decrement(ref _activeSubscriptions);
            }
        }

        private async IAsyncEnumerable<T> Read<T>(
            Channel<T> channel,
            List<Channel<T>> owner,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _activeSubscriptions);
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                    yield return item;
            }
            finally
            {
                lock (_gate) owner.Remove(channel);
                Interlocked.Decrement(ref _activeSubscriptions);
            }
        }

        private async IAsyncEnumerable<WidgetAudioDevicesChanged> ReadDevices(
            Channel<WidgetAudioDevicesChanged> channel,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _activeSubscriptions);
            using var registration = cancellationToken.Register(() =>
            {
                if (!BlockDeviceCancellation) return;
                DeviceCancellationEntered.TrySetResult();
                ReleaseDeviceCancellation.Task.GetAwaiter().GetResult();
            });
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
                    yield return item;
            }
            finally
            {
                lock (_gate) _devices.Remove(channel);
                Interlocked.Decrement(ref _activeSubscriptions);
            }
        }

        private void Record(string operation)
        {
            lock (_gate) _operations.Add(operation);
        }

        private static Channel<T> CreateChannel<T>() =>
            Channel.CreateBounded<T>(new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
    }
}

internal static class ReadOnlyListExtensions
{
    internal static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
        }
        return -1;
    }
}
