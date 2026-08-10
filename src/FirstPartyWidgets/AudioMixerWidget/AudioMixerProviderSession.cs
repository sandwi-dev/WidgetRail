using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.AudioMixer;

internal enum AudioMixerProviderSection
{
    Devices,
    Input,
}

internal enum AudioMixerProviderObservationKind
{
    RequiredFetchStarted,
    RequiredSnapshot,
    SessionsChanged,
    OutputChanged,
    DevicesLoading,
    DevicesChanged,
    DevicesFailed,
    InputLoading,
    InputChanged,
    InputCleared,
    InputFailed,
    RequiredFailed,
}

internal sealed record AudioMixerProviderObservation(
    AudioMixerProviderObservationKind Kind,
    IReadOnlyList<WidgetAudioSession>? Sessions = null,
    WidgetAudioOutput? Output = null,
    IReadOnlyList<WidgetAudioDevice>? Devices = null,
    WidgetAudioInput? Input = null,
    AudioOptionalSectionState OptionalState = AudioOptionalSectionState.Initial,
    AudioMixerViewState ViewState = AudioMixerViewState.Initial,
    string? Status = null)
{
    internal static AudioMixerProviderObservation RequiredFetchStarted() =>
        new(AudioMixerProviderObservationKind.RequiredFetchStarted);

    internal static AudioMixerProviderObservation RequiredSnapshot(
        IReadOnlyList<WidgetAudioSession> sessions,
        WidgetAudioOutput output) =>
        new(AudioMixerProviderObservationKind.RequiredSnapshot,
            Sessions: sessions.ToArray(), Output: output);

    internal static AudioMixerProviderObservation SessionsChanged(
        IReadOnlyList<WidgetAudioSession>? sessions) =>
        new(AudioMixerProviderObservationKind.SessionsChanged,
            Sessions: (sessions ?? []).ToArray());

    internal static AudioMixerProviderObservation OutputChanged(WidgetAudioOutput output) =>
        new(AudioMixerProviderObservationKind.OutputChanged, Output: output);

    internal static AudioMixerProviderObservation DevicesLoading() =>
        new(AudioMixerProviderObservationKind.DevicesLoading);

    internal static AudioMixerProviderObservation DevicesChanged(
        IReadOnlyList<WidgetAudioDevice>? devices) =>
        new(AudioMixerProviderObservationKind.DevicesChanged,
            Devices: (devices ?? []).ToArray());

    internal static AudioMixerProviderObservation DevicesFailed(
        AudioOptionalSectionState state) =>
        new(AudioMixerProviderObservationKind.DevicesFailed, OptionalState: state);

    internal static AudioMixerProviderObservation InputLoading() =>
        new(AudioMixerProviderObservationKind.InputLoading);

    internal static AudioMixerProviderObservation InputChanged(WidgetAudioInput input) =>
        new(AudioMixerProviderObservationKind.InputChanged, Input: input);

    internal static AudioMixerProviderObservation InputCleared(
        AudioOptionalSectionState state) =>
        new(AudioMixerProviderObservationKind.InputCleared, OptionalState: state);

    internal static AudioMixerProviderObservation InputFailed(
        AudioOptionalSectionState state) =>
        new(AudioMixerProviderObservationKind.InputFailed, OptionalState: state);

    internal static AudioMixerProviderObservation RequiredFailed(
        AudioMixerViewState state,
        string status) =>
        new(AudioMixerProviderObservationKind.RequiredFailed,
            ViewState: state, Status: status);
}

/// <summary>
/// Owns one Audio Mixer Active-lifetime provider session. The session controls
/// subscriptions, snapshot ordering, optional retries, and terminal task drain,
/// but publishes only immutable observations and never owns widget state.
/// </summary>
internal sealed class AudioMixerProviderSession
{
    private readonly object _gate = new();
    private readonly WidgetAudioService _audio;
    private readonly Action<AudioMixerProviderSession, AudioMixerProviderObservation> _publish;
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _devicesRetrySignal = new(0, 1);
    private readonly SemaphoreSlim _inputRetrySignal = new(0, 1);
    private CancellationTokenSource? _devicesAttemptLifetime;
    private CancellationTokenSource? _inputAttemptLifetime;
    private Task? _runTask;
    private bool _stopping;
    private bool _disposed;

    internal AudioMixerProviderSession(
        WidgetAudioService audio,
        CancellationToken activeLifetime,
        Action<AudioMixerProviderSession, AudioMixerProviderObservation> publish)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(activeLifetime);
    }

    internal CancellationToken CancellationToken => _lifetime.Token;

    internal void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null)
                throw new InvalidOperationException("The provider session has already started.");
            _runTask = RunAsync();
        }
    }

    internal bool Retry(AudioMixerProviderSection section)
    {
        lock (_gate)
        {
            if (_stopping || _disposed || _runTask is null || _runTask.IsCompleted)
                return false;
            CancellationTokenSource? attempt;
            SemaphoreSlim signal;
            if (section == AudioMixerProviderSection.Devices)
            {
                attempt = _devicesAttemptLifetime;
                signal = _devicesRetrySignal;
            }
            else
            {
                attempt = _inputAttemptLifetime;
                signal = _inputRetrySignal;
            }

            try { signal.Release(); }
            catch (SemaphoreFullException) { }
            attempt?.Cancel();
            return true;
        }
    }

    internal async ValueTask StopAsync()
    {
        Task task;
        lock (_gate)
        {
            if (_disposed) return;
            _stopping = true;
            task = _runTask ?? Task.CompletedTask;
        }

        // Both optional attempts are linked to this lifetime. One terminal
        // cancellation avoids retaining and re-canceling an attempt that may
        // finish and dispose while the root task drains.
        _lifetime.Cancel();
        await task.ConfigureAwait(false);

        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _devicesRetrySignal.Dispose();
        _inputRetrySignal.Dispose();
        _lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await RunCoreAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Active exit or replacement is a normal terminal path.
        }
        catch (WidgetCapabilityUnavailableException)
        {
            Publish(AudioMixerProviderObservation.RequiredFailed(
                AudioMixerViewState.ServiceUnavailable,
                "Host audio service unavailable"));
        }
        catch (WidgetCapabilityException exception)
        {
            var (state, status) = MapRequiredFailure(exception.ErrorCode);
            Publish(AudioMixerProviderObservation.RequiredFailed(state, status));
        }
        catch (Exception)
        {
            Publish(AudioMixerProviderObservation.RequiredFailed(
                AudioMixerViewState.Error,
                "Audio provider returned an unexpected error"));
        }
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        await using var sessionSubscription = await _audio
            .OpenSessionsSubscriptionAsync(cancellationToken).ConfigureAwait(false);
        await using var outputSubscription = await _audio
            .OpenOutputSubscriptionAsync(cancellationToken).ConfigureAwait(false);

        using var observers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var observerTasks = new List<Task>(4)
        {
            ObserveOptionalDevicesAsync(observers.Token),
            ObserveOptionalInputAsync(observers.Token),
        };

        try
        {
            Publish(AudioMixerProviderObservation.RequiredFetchStarted());
            var output = await _audio.GetOutputAsync(cancellationToken).ConfigureAwait(false);
            var sessions = await _audio.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Publish(AudioMixerProviderObservation.RequiredSnapshot(sessions, output));

            var sessionObserver = ObserveSessionChangesAsync(
                sessionSubscription, observers.Token);
            var outputObserver = ObserveOutputChangesAsync(
                outputSubscription, observers.Token);
            observerTasks.Add(sessionObserver);
            observerTasks.Add(outputObserver);

            await Task.WhenAny(sessionObserver, outputObserver).ConfigureAwait(false);
            observers.Cancel();
            try
            {
                await Task.WhenAll(observerTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (observers.IsCancellationRequested) { }
            cancellationToken.ThrowIfCancellationRequested();
            Publish(AudioMixerProviderObservation.RequiredFailed(
                AudioMixerViewState.ChannelClosed,
                "Audio service channel closed"));
        }
        finally
        {
            observers.Cancel();
            if (observerTasks.Count != 0)
            {
                try
                {
                    await Task.WhenAll(observerTasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (observers.IsCancellationRequested) { }
            }
        }
    }

    private async Task ObserveOptionalDevicesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Publish(AudioMixerProviderObservation.DevicesLoading());
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetOptionalAttempt(AudioMixerProviderSection.Devices, attempt);
            try
            {
                await using var subscription = await _audio
                    .OpenDevicesSubscriptionAsync(attempt.Token).ConfigureAwait(false);
                var devices = await _audio.GetDevicesAsync(attempt.Token).ConfigureAwait(false);
                attempt.Token.ThrowIfCancellationRequested();
                Publish(AudioMixerProviderObservation.DevicesChanged(devices));
                await ObserveDeviceChangesAsync(subscription, attempt.Token).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                    Publish(AudioMixerProviderObservation.DevicesFailed(
                        AudioOptionalSectionState.Unavailable));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                // An explicit section retry replaces only this attempt.
            }
            catch (Exception exception)
            {
                Publish(AudioMixerProviderObservation.DevicesFailed(
                    MapOptionalFailure(exception)));
            }
            finally
            {
                ClearOptionalAttempt(AudioMixerProviderSection.Devices, attempt);
            }
            await _devicesRetrySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ObserveOptionalInputAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Publish(AudioMixerProviderObservation.InputLoading());
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetOptionalAttempt(AudioMixerProviderSection.Input, attempt);
            try
            {
                await using var subscription = await _audio
                    .OpenInputSubscriptionAsync(attempt.Token).ConfigureAwait(false);
                var input = await _audio.GetInputAsync(attempt.Token).ConfigureAwait(false);
                attempt.Token.ThrowIfCancellationRequested();
                Publish(AudioMixerProviderObservation.InputChanged(input));
                await ObserveInputChangesAsync(subscription, attempt.Token).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                    Publish(AudioMixerProviderObservation.InputFailed(
                        AudioOptionalSectionState.Unavailable));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested)
            {
                // An explicit section retry replaces only this attempt.
            }
            catch (Exception exception)
            {
                Publish(AudioMixerProviderObservation.InputFailed(
                    MapOptionalFailure(exception)));
            }
            finally
            {
                ClearOptionalAttempt(AudioMixerProviderSection.Input, attempt);
            }
            await _inputRetrySignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ObserveSessionChangesAsync(
        IWidgetCapabilitySubscription<WidgetAudioSessionsChanged> subscription,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!change.IsAvailable)
            {
                Publish(AudioMixerProviderObservation.RequiredFailed(
                    AudioMixerViewState.ServiceUnavailable,
                    "Windows audio session provider unavailable"));
                continue;
            }
            Publish(AudioMixerProviderObservation.SessionsChanged(change.Sessions));
        }
    }

    private async Task ObserveOutputChangesAsync(
        IWidgetCapabilitySubscription<WidgetAudioOutputChanged> subscription,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!change.IsAvailable || change.Output is null)
            {
                Publish(AudioMixerProviderObservation.RequiredFailed(
                    AudioMixerViewState.ServiceUnavailable,
                    "Windows master output unavailable"));
                continue;
            }
            Publish(AudioMixerProviderObservation.OutputChanged(change.Output));
        }
    }

    private async Task ObserveDeviceChangesAsync(
        IWidgetCapabilitySubscription<WidgetAudioDevicesChanged> subscription,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Publish(change.IsAvailable
                ? AudioMixerProviderObservation.DevicesChanged(change.Devices)
                : AudioMixerProviderObservation.DevicesFailed(
                    AudioOptionalSectionState.Unavailable));
        }
    }

    private async Task ObserveInputChangesAsync(
        IWidgetCapabilitySubscription<WidgetAudioInputChanged> subscription,
        CancellationToken cancellationToken)
    {
        await foreach (var change in subscription.ReadAllAsync(cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!change.IsAvailable)
                Publish(AudioMixerProviderObservation.InputFailed(
                    AudioOptionalSectionState.Unavailable));
            else if (change.Input is null)
                Publish(AudioMixerProviderObservation.InputCleared(
                    AudioOptionalSectionState.Empty));
            else
                Publish(AudioMixerProviderObservation.InputChanged(change.Input));
        }
    }

    private void SetOptionalAttempt(
        AudioMixerProviderSection section,
        CancellationTokenSource attempt)
    {
        lock (_gate)
        {
            if (_stopping)
            {
                attempt.Cancel();
                return;
            }
            if (section == AudioMixerProviderSection.Devices)
                _devicesAttemptLifetime = attempt;
            else
                _inputAttemptLifetime = attempt;
        }
    }

    private void ClearOptionalAttempt(
        AudioMixerProviderSection section,
        CancellationTokenSource attempt)
    {
        lock (_gate)
        {
            if (section == AudioMixerProviderSection.Devices &&
                ReferenceEquals(_devicesAttemptLifetime, attempt))
                _devicesAttemptLifetime = null;
            else if (section == AudioMixerProviderSection.Input &&
                     ReferenceEquals(_inputAttemptLifetime, attempt))
                _inputAttemptLifetime = null;
        }
    }

    private void Publish(AudioMixerProviderObservation observation)
    {
        if (_lifetime.IsCancellationRequested) return;
        _publish(this, observation);
    }

    private static AudioOptionalSectionState MapOptionalFailure(Exception exception)
    {
        if (exception is not WidgetCapabilityException capability)
            return AudioOptionalSectionState.Unavailable;
        return capability.ErrorCode switch
        {
            "permission_denied" => AudioOptionalSectionState.PermissionDenied,
            "capability_revoked" => AudioOptionalSectionState.Revoked,
            _ => AudioOptionalSectionState.Unavailable,
        };
    }

    private static (AudioMixerViewState State, string Status) MapRequiredFailure(
        string errorCode) => errorCode switch
        {
            "permission_denied" =>
                (AudioMixerViewState.PermissionDenied, "Audio read permission denied"),
            "lifecycle_denied" =>
                (AudioMixerViewState.LifecycleDenied, "Audio request denied by widget lifecycle"),
            "channel_closed" =>
                (AudioMixerViewState.ChannelClosed, "Audio service channel closed"),
            "platform_unavailable" or "provider_unavailable" =>
                (AudioMixerViewState.ServiceUnavailable, "Windows audio provider unavailable"),
            _ => (AudioMixerViewState.Error, "Audio provider request failed"),
        };
}
