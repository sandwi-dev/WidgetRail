using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.AudioMixer;

internal abstract class AudioMixerEndpointCommandPolicy
{
    internal bool HasAuthoritative { get; set; }
    internal double AuthoritativeVolume { get; set; }
    internal bool AuthoritativeMuted { get; set; }
    internal double? VolumeTarget { get; set; }
    internal bool? MuteTarget { get; set; }
    internal long VolumeRevision { get; set; }
    internal long MuteRevision { get; set; }
    internal bool VolumeWorkerRunning { get; set; }
    internal bool MuteWorkerRunning { get; set; }
    internal bool VolumeAwaitingConfirmation { get; set; }
    internal bool MuteAwaitingConfirmation { get; set; }
    internal bool VolumeIsPending => VolumeTarget is not null;
    internal bool MuteIsPending => MuteTarget is not null;
    internal bool IsSending => VolumeWorkerRunning || MuteWorkerRunning;

    internal bool QueueVolume(double target)
    {
        VolumeTarget = target;
        VolumeRevision++;
        VolumeAwaitingConfirmation = false;
        if (VolumeWorkerRunning) return false;
        VolumeWorkerRunning = true;
        return true;
    }

    internal bool QueueMute(bool target)
    {
        MuteTarget = target;
        MuteRevision++;
        MuteAwaitingConfirmation = false;
        if (MuteWorkerRunning) return false;
        MuteWorkerRunning = true;
        return true;
    }

    internal double ReconcileVolume(double authoritative, Func<double, double, bool> matches)
    {
        AuthoritativeVolume = authoritative;
        HasAuthoritative = true;
        if (VolumeTarget is not { } target) return authoritative;
        if (matches(authoritative, target)) ConfirmVolumeTarget();
        return target;
    }

    internal bool ReconcileMute(bool authoritative)
    {
        AuthoritativeMuted = authoritative;
        HasAuthoritative = true;
        if (MuteTarget is not { } target) return authoritative;
        if (authoritative == target) ConfirmMuteTarget();
        return target;
    }

    internal void ConfirmVolumeTarget()
    {
        VolumeTarget = null;
        VolumeAwaitingConfirmation = false;
    }

    internal void ConfirmMuteTarget()
    {
        MuteTarget = null;
        MuteAwaitingConfirmation = false;
    }

    internal void FinishVolumeWorker() => VolumeWorkerRunning = false;
    internal void FinishMuteWorker() => MuteWorkerRunning = false;

    internal void Reset()
    {
        HasAuthoritative = false;
        AuthoritativeVolume = 0;
        AuthoritativeMuted = false;
        VolumeTarget = null;
        MuteTarget = null;
        VolumeWorkerRunning = false;
        MuteWorkerRunning = false;
        VolumeAwaitingConfirmation = false;
        MuteAwaitingConfirmation = false;
        VolumeRevision++;
        MuteRevision++;
    }
}

internal sealed class AudioMixerOutputCommandPolicy : AudioMixerEndpointCommandPolicy { }
internal sealed class AudioMixerInputCommandPolicy : AudioMixerEndpointCommandPolicy { }
internal sealed class AudioMixerSessionCommandPolicy : AudioMixerEndpointCommandPolicy { }

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
