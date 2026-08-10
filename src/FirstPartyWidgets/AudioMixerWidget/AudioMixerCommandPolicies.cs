using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.AudioMixer;

internal enum AudioMixerCommandTransitionKind
{
    Applied,
    NoPendingCommand,
    NewerRevision,
}

internal readonly record struct AudioMixerCommandAdmission<TState>(
    TState State,
    bool StartWorker);

internal readonly record struct AudioMixerCommandWork<TValue>(
    TValue Target,
    long Revision);

internal readonly record struct AudioMixerCommandAcknowledgement(
    AudioMixerCommandTransitionKind Kind,
    bool StartConfirmation);

internal readonly record struct AudioMixerCommandTransition<TState>(
    AudioMixerCommandTransitionKind Kind,
    TState State,
    bool Matched = false);

internal abstract class AudioMixerEndpointCommandPolicy<TState>
{
    private readonly AudioMixerScalarCommandPolicy<double> _volume = new();
    private readonly AudioMixerScalarCommandPolicy<bool> _mute = new();

    internal bool VolumeIsPending => _volume.IsPending;
    internal bool MuteIsPending => _mute.IsPending;
    internal bool VolumeAwaitingConfirmation => _volume.IsAwaitingConfirmation;
    internal bool MuteAwaitingConfirmation => _mute.IsAwaitingConfirmation;
    internal bool IsSending => _volume.IsSending || _mute.IsSending;

    protected abstract double Volume(TState state);
    protected abstract bool Muted(TState state);
    protected abstract TState WithVolume(TState state, double volume);
    protected abstract TState WithMuted(TState state, bool muted);

    protected void Seed(TState authoritative)
    {
        _volume.Seed(Volume(authoritative));
        _mute.Seed(Muted(authoritative));
    }

    internal AudioMixerCommandAdmission<TState> QueueVolume(TState current, double target) =>
        new(WithVolume(current, target), _volume.Queue(target));

    internal AudioMixerCommandAdmission<TState> QueueMute(TState current, bool target) =>
        new(WithMuted(current, target), _mute.Queue(target));

    internal TState ReconcileProvider(TState authoritative, Func<double, double, bool> volumesMatch) =>
        WithMuted(
            WithVolume(authoritative, _volume.Reconcile(Volume(authoritative), volumesMatch)),
            _mute.Reconcile(Muted(authoritative), static (left, right) => left == right));

    internal bool TryBeginVolumeWork(out AudioMixerCommandWork<double> work) =>
        _volume.TryBeginWork(out work);

    internal bool TryBeginMuteWork(out AudioMixerCommandWork<bool> work) =>
        _mute.TryBeginWork(out work);

    internal AudioMixerCommandAcknowledgement AcknowledgeVolume(long revision) =>
        _volume.Acknowledge(revision);

    internal AudioMixerCommandAcknowledgement AcknowledgeMute(long revision) =>
        _mute.Acknowledge(revision);

    internal AudioMixerCommandTransition<TState> ConfirmVolume(
        long revision,
        TState current,
        double authoritative,
        Func<double, double, bool> volumesMatch)
    {
        var transition = _volume.Confirm(revision, authoritative, volumesMatch);
        return new AudioMixerCommandTransition<TState>(
            transition.Kind,
            transition.Kind == AudioMixerCommandTransitionKind.Applied
                ? WithVolume(current, authoritative)
                : current,
            transition.Matched);
    }

    internal AudioMixerCommandTransition<TState> ConfirmMute(
        long revision,
        TState current,
        bool authoritative)
    {
        var transition = _mute.Confirm(
            revision, authoritative, static (left, right) => left == right);
        return new AudioMixerCommandTransition<TState>(
            transition.Kind,
            transition.Kind == AudioMixerCommandTransitionKind.Applied
                ? WithMuted(current, authoritative)
                : current,
            transition.Matched);
    }

    internal bool AbandonVolumeConfirmation(long revision) => _volume.Abandon(revision);
    internal bool AbandonMuteConfirmation(long revision) => _mute.Abandon(revision);

    internal AudioMixerCommandTerminal<double> FailVolume(long revision) =>
        _volume.Fail(revision);

    internal AudioMixerCommandTerminal<bool> FailMute(long revision) =>
        _mute.Fail(revision);

    internal AudioMixerCommandTerminal<double> CancelVolume() => _volume.Cancel();
    internal AudioMixerCommandTerminal<bool> CancelMute() => _mute.Cancel();

    internal TState RestoreAndReset(TState current)
    {
        if (_volume.TryGetAuthoritative(out var volume)) current = WithVolume(current, volume);
        if (_mute.TryGetAuthoritative(out var muted)) current = WithMuted(current, muted);
        Reset();
        return current;
    }

    internal void Reset()
    {
        _volume.Reset();
        _mute.Reset();
    }

}

internal sealed class AudioMixerOutputCommandPolicy :
    AudioMixerEndpointCommandPolicy<WidgetAudioOutput>
{
    protected override double Volume(WidgetAudioOutput state) => state.Volume;
    protected override bool Muted(WidgetAudioOutput state) => state.IsMuted;
    protected override WidgetAudioOutput WithVolume(WidgetAudioOutput state, double volume) =>
        state with { Volume = volume };
    protected override WidgetAudioOutput WithMuted(WidgetAudioOutput state, bool muted) =>
        state with { IsMuted = muted };
}

internal sealed class AudioMixerInputCommandPolicy :
    AudioMixerEndpointCommandPolicy<WidgetAudioInput>
{
    protected override double Volume(WidgetAudioInput state) => state.Volume;
    protected override bool Muted(WidgetAudioInput state) => state.IsMuted;
    protected override WidgetAudioInput WithVolume(WidgetAudioInput state, double volume) =>
        state with { Volume = volume };
    protected override WidgetAudioInput WithMuted(WidgetAudioInput state, bool muted) =>
        state with { IsMuted = muted };
}

internal sealed class AudioMixerSessionCommandPolicy :
    AudioMixerEndpointCommandPolicy<WidgetAudioSession>
{
    internal AudioMixerSessionCommandPolicy(WidgetAudioSession authoritative) => Seed(authoritative);

    internal bool CanDiscard => !IsSending;

    protected override double Volume(WidgetAudioSession state) => state.Volume;
    protected override bool Muted(WidgetAudioSession state) => state.IsMuted;
    protected override WidgetAudioSession WithVolume(WidgetAudioSession state, double volume) =>
        state with { Volume = volume };
    protected override WidgetAudioSession WithMuted(WidgetAudioSession state, bool muted) =>
        state with { IsMuted = muted };
}

internal readonly record struct AudioMixerScalarConfirmation(
    AudioMixerCommandTransitionKind Kind,
    bool Matched);

internal readonly record struct AudioMixerCommandTerminal<TValue>(
    AudioMixerCommandTransitionKind Kind,
    bool HasAuthoritative,
    TValue Authoritative);

internal sealed class AudioMixerScalarCommandPolicy<TValue>
{
    private TValue? _authoritative;
    private bool _hasAuthoritative;
    private TValue? _target;
    private bool _hasTarget;
    private long _revision;
    private bool _workerRunning;
    private bool _awaitingConfirmation;

    internal bool IsPending => _hasTarget;
    internal bool IsSending => _workerRunning;
    internal bool IsAwaitingConfirmation => _awaitingConfirmation;

    internal void Seed(TValue authoritative)
    {
        _authoritative = authoritative;
        _hasAuthoritative = true;
    }

    internal bool Queue(TValue target)
    {
        _target = target;
        _hasTarget = true;
        _revision++;
        _awaitingConfirmation = false;
        if (_workerRunning) return false;
        _workerRunning = true;
        return true;
    }

    internal TValue Reconcile(
        TValue authoritative,
        Func<TValue, TValue, bool> matches)
    {
        Seed(authoritative);
        if (!_hasTarget) return authoritative;
        var target = _target!;
        if (matches(authoritative, target)) ClearTarget();
        return target;
    }

    internal bool TryBeginWork(out AudioMixerCommandWork<TValue> work)
    {
        if (!_hasTarget)
        {
            _workerRunning = false;
            work = default;
            return false;
        }
        work = new AudioMixerCommandWork<TValue>(_target!, _revision);
        return true;
    }

    internal AudioMixerCommandAcknowledgement Acknowledge(long revision)
    {
        if (!_hasTarget)
        {
            _workerRunning = false;
            return new(AudioMixerCommandTransitionKind.NoPendingCommand, false);
        }
        if (_revision != revision)
            return new(AudioMixerCommandTransitionKind.NewerRevision, false);
        _workerRunning = false;
        _awaitingConfirmation = true;
        return new(AudioMixerCommandTransitionKind.Applied, true);
    }

    internal AudioMixerScalarConfirmation Confirm(
        long revision,
        TValue authoritative,
        Func<TValue, TValue, bool> matches)
    {
        if (!_hasTarget || _revision != revision)
            return new(AudioMixerCommandTransitionKind.NewerRevision, false);
        var matched = matches(authoritative, _target!);
        Seed(authoritative);
        ClearTarget();
        return new(AudioMixerCommandTransitionKind.Applied, matched);
    }

    internal bool Abandon(long revision)
    {
        if (!_hasTarget || _revision != revision) return false;
        ClearTarget();
        return true;
    }

    internal AudioMixerCommandTerminal<TValue> Fail(long revision)
    {
        if (_revision != revision)
            return Terminal(AudioMixerCommandTransitionKind.NewerRevision);
        _workerRunning = false;
        if (!_hasTarget)
            return Terminal(AudioMixerCommandTransitionKind.NoPendingCommand);
        ClearTarget();
        return Terminal(AudioMixerCommandTransitionKind.Applied);
    }

    internal AudioMixerCommandTerminal<TValue> Cancel()
    {
        _workerRunning = false;
        if (!_hasTarget)
            return Terminal(AudioMixerCommandTransitionKind.NoPendingCommand);
        ClearTarget();
        return Terminal(AudioMixerCommandTransitionKind.Applied);
    }

    internal bool TryGetAuthoritative(out TValue authoritative)
    {
        authoritative = _authoritative!;
        return _hasAuthoritative;
    }

    internal void Reset()
    {
        _authoritative = default;
        _hasAuthoritative = false;
        _target = default;
        _hasTarget = false;
        _workerRunning = false;
        _awaitingConfirmation = false;
        _revision++;
    }

    private AudioMixerCommandTerminal<TValue> Terminal(AudioMixerCommandTransitionKind kind) =>
        new(kind, _hasAuthoritative, _authoritative!);

    private void ClearTarget()
    {
        _target = default;
        _hasTarget = false;
        _awaitingConfirmation = false;
    }
}

internal sealed class AudioMixerSessionControlIds
{
    private AudioMixerSessionControlIds(string prefix)
    {
        Row = $"{prefix}.row";
        Heading = $"{prefix}.heading";
        Details = $"{prefix}.details";
        Name = $"{prefix}.name";
        Position = $"{prefix}.position";
        State = $"{prefix}.state";
        Value = $"{prefix}.volume.value";
        Controls = $"{prefix}.controls";
        VolumeSlider = $"{prefix}.volume.slider";
        VolumeSet = $"{prefix}.volume.set";
        Mute = $"{prefix}.mute";
        MuteIcon = $"{prefix}.mute.icon";
    }

    internal string Row { get; }
    internal string Heading { get; }
    internal string Details { get; }
    internal string Name { get; }
    internal string Position { get; }
    internal string State { get; }
    internal string Value { get; }
    internal string Controls { get; }
    internal string VolumeSlider { get; }
    internal string VolumeSet { get; }
    internal string Mute { get; }
    internal string MuteIcon { get; }

    internal static AudioMixerSessionControlIds For(WidgetAudioSession session)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(session.SessionId));
        return new AudioMixerSessionControlIds(
            $"audio.session.{Convert.ToHexString(hash).ToLowerInvariant()}");
    }
}
