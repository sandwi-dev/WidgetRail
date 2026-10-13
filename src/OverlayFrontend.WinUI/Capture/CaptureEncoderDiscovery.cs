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
    private readonly ConcurrentDictionary<(string Id, bool Audio), Lazy<Task<bool>>> verified = new();
    private readonly Lazy<Task<bool>> available = new(() => Task.Run(supported));
    private readonly Lazy<Task<bool>> audio = new(() => Task.Run(probeAudio));
    // Never spin up several hardware encoders at once merely to discover capabilities.
    private readonly SemaphoreSlim encoderGate = new(1, 1);

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

    private Task<bool> VerifyAsync(CaptureRecordingProfile profile, bool withAudio) =>
        verified.GetOrAdd((profile.Id, withAudio), _ => new Lazy<Task<bool>>(async () =>
        {
            await encoderGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(() => probe(profile, profile.MaximumBitrate, withAudio) &&
                    probe(profile, profile.MinimumBitrate, withAudio)).ConfigureAwait(false);
            }
            finally { encoderGate.Release(); }
        })).Value;
}
