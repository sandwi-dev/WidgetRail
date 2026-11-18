using System.Collections.Concurrent;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

/// <summary>Bounded process-lifetime encoder qualification, shared by recording and the complete settings catalog.</summary>
internal sealed class CaptureEncoderDiscovery(Func<bool> supported, Func<CaptureRecordingProfile, int, bool, bool> probe, Func<bool> probeAudio)
{
    private static readonly CaptureRecordingProfile[] profiles = [
        new("1080p30-hardware", 1920, 1080, 30, 3_000_000, 12_000_000, CaptureEncoderMode.Hardware),
        new("720p15-hardware", 1280, 720, 15, 1_000_000, 6_000_000, CaptureEncoderMode.Hardware),
        new("1080p60-hardware", 1920, 1080, 60, 6_000_000, 20_000_000, CaptureEncoderMode.Hardware),
        new("2160p30-hardware", 3840, 2160, 30, 12_000_000, 30_000_000, CaptureEncoderMode.Hardware),
        new("720p15-software", 1280, 720, 15, 1_000_000, 6_000_000, CaptureEncoderMode.Software)];
    private readonly ConcurrentDictionary<(CaptureRecordingProfile Profile, bool Audio), Lazy<Task<bool>>> verified = new();
    private readonly object cacheGate = new();
    private string? environmentId;
    private Lazy<Task<bool>> available = new(() => Task.Run(supported));
    private Lazy<Task<bool>> audio = new(() => Task.Run(probeAudio));
    // Never spin up several hardware encoders at once merely to discover capabilities.
    private readonly SemaphoreSlim encoderGate = new(1, 1);
    internal Task<bool> ApplicationAudioAsync() { lock(cacheGate)return audio.Value; }
    internal Task<bool> AvailableAsync() { lock(cacheGate)return available.Value; }

    internal async Task<CaptureCapabilities> DiscoverAsync(CaptureRecordingQuery? query = null)
    {
        if (query is not null && !query.IsWellFormed()) throw new ArgumentException("Invalid recording query.");
        if (!await available.Value.ConfigureAwait(false)) return CaptureCapabilities.Unavailable;
        var candidates = query?.Recording is { } requested ? profiles.Where(p => p.Supports(requested)) : profiles;
        var selected = new List<CaptureRecordingProfile>();
        foreach (var profile in candidates)
        {
            if (!await VerifyAsync(profile, false).ConfigureAwait(false)) continue;
            selected.Add(profile);
            if (query is not null) break;
        }
        var includeAudio = query is null || query.Recording?.Audio == CaptureAudioMode.Application;
        var withAudio = includeAudio && selected.Count > 0 && await audio.Value.ConfigureAwait(false);
        if (withAudio)
            foreach (var profile in selected)
                if (!await VerifyAsync(profile, true).ConfigureAwait(false)) { withAudio = false; break; }
        return new(true, (CaptureImageFormat[])[CaptureImageFormat.Png], selected.ToArray(),
            withAudio ? (CaptureAudioMode[])[CaptureAudioMode.None, CaptureAudioMode.Application] : (CaptureAudioMode[])[CaptureAudioMode.None], true, CaptureLimits.MaximumRecordingSeconds);
    }

    /// <summary>Cheap adapter/driver observation invalidates cached qualification; a late old probe cannot publish into the new epoch.</summary>
    internal void ObserveEnvironment(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        lock (cacheGate)
        {
            if (environmentId == value) return;
            environmentId = value;
            verified.Clear();
            available = new(() => Task.Run(supported));
            audio = new(() => Task.Run(probeAudio));
        }
    }

    internal async Task<bool> VerifyEncodingAsync(CaptureRecordingProfile profile, bool withAudio,
        Func<CaptureRecordingProfile, int, bool, bool> encodingProbe)
    {
        if (profile.Encoding is null || profile.Adapter?.IsWellFormed() != true || !profile.IsWellFormed())
            throw new ArgumentException("An exact verified-profile candidate is required.", nameof(profile));
        if(!await AvailableAsync().ConfigureAwait(false))return false;
        Lazy<Task<bool>> audioProbe;
        lock(cacheGate)audioProbe=audio;
        if(withAudio && !await audioProbe.Value.ConfigureAwait(false))return false;
        Task<bool> verification;
        lock (cacheGate)
        {
            if (profile.Adapter.EnvironmentId != environmentId) return false;
            // Advanced qualification checks AAC independently above. Its video
            // probe is identical for silent/application-audio callers.
            verification=VerifyAsync(profile, false, encodingProbe);
        }
        return await verification.ConfigureAwait(false);
    }

    private Task<bool> VerifyAsync(CaptureRecordingProfile profile, bool withAudio,
        Func<CaptureRecordingProfile, int, bool, bool>? encodingProbe = null)
    {
        lock (cacheGate)
        {
            // Keep an advanced-options screen from retaining an unbounded set of
            // arbitrary resolutions/bitrates. In-flight work remains shared.
            if (verified.Count >= 64)
                foreach (var key in verified.Where(pair => pair.Value.IsValueCreated && pair.Value.Value.IsCompleted).Select(pair => pair.Key).Take(32))
                    verified.TryRemove(key, out _);
            if (verified.Count >= 64 && !verified.ContainsKey((profile, withAudio))) return Task.FromResult(false);
            var epoch = environmentId;
            return verified.GetOrAdd((profile, withAudio), _ => new Lazy<Task<bool>>(async () =>
        {
            await encoderGate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (cacheGate) if (epoch != environmentId) return false;
                var actualProbe = encodingProbe ?? probe;
                var result = await Task.Run(() => actualProbe(profile, profile.MaximumBitrate, withAudio) &&
                    (profile.MaximumBitrate == profile.MinimumBitrate || actualProbe(profile, profile.MinimumBitrate, withAudio))).ConfigureAwait(false);
                lock (cacheGate) return epoch == environmentId && result;
            }
            finally { encoderGate.Release(); }
        })).Value;
        }
    }
}
