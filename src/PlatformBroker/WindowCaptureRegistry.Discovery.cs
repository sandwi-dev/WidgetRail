using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed record HostCaptureDiscovery(string Id, CaptureRecordingQuery Query);

internal sealed partial class WindowCaptureRegistry
{
    private sealed class Discovery(CaptureRecordingQuery query)
    {
        internal readonly HostCaptureDiscovery Request = new(Guid.NewGuid().ToString("N"), query);
        internal readonly TaskCompletionSource<CaptureCapabilities> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Waiters;
    }
    private readonly Dictionary<CaptureRecordingQuery, Discovery> recordingDiscoveries = [];
    internal IReadOnlyList<HostCaptureDiscovery> RecordingDiscoveries
    {
        get { lock (gate) return recordingDiscoveries.Values.Where(d => d.Waiters > 0 && !d.Ready.Task.IsCompleted).Select(d => d.Request).ToArray(); }
    }
    internal async Task<CaptureCapabilities> GetRecordingCapabilitiesAsync(CaptureRecordingQuery query, CancellationToken token)
    {
        if (query is null || !query.IsWellFormed()) throw Error("invalid_capture_options");
        query = query.Normalize();
        Discovery discovery;
        lock (gate)
        {
            ThrowIfDisposed();
            // The catalog's audio flag covers every advertised profile. A specific
            // profile may support audio even if a different catalog profile does not.
            if (encodingReady.Task.IsCompletedSuccessfully && (query.Recording is null || !capabilities.Available ||
                capabilities.Validate(new() { Recording = query.Recording }).Supported)) return capabilities;
            if (query.Recording is { } recording)
            {
                var request = new CaptureSessionRequest { Recording = recording };
                foreach (var previous in recordingDiscoveries.Values)
                    if (previous.Ready.Task.IsCompletedSuccessfully && previous.Ready.Task.Result.Validate(request).Supported)
                        return previous.Ready.Task.Result;
            }
            if (!recordingDiscoveries.TryGetValue(query, out discovery!))
            {
                // Bound both pending native work and retained replies across all widgets.
                if (recordingDiscoveries.Values.Count(d => !d.Ready.Task.IsCompleted) >= 16) throw Error("capture_busy");
                if (recordingDiscoveries.Count >= 32)
                {
                    var retiredQuery = recordingDiscoveries.FirstOrDefault(pair => pair.Value.Ready.Task.IsCompleted && pair.Value.Waiters == 0).Key;
                    if (retiredQuery is null) throw Error("capture_busy");
                    recordingDiscoveries.Remove(retiredQuery);
                }
                recordingDiscoveries.Add(query, discovery = new(query));
            }
            ++discovery.Waiters;
        }
        try { return await discovery.Ready.Task.WaitAsync(TimeSpan.FromSeconds(CapabilityDiscoveryTimeoutSeconds), token).ConfigureAwait(false); }
        catch (TimeoutException) { throw Error("capture_capabilities_unavailable"); }
        finally
        {
            lock (gate)
            {
                if (--discovery.Waiters == 0 && !discovery.Ready.Task.IsCompleted)
                    recordingDiscoveries.Remove(query);
            }
        }
    }
    internal void SetRecordingCapabilities(string id, CaptureCapabilities value)
    {
        if (value is null || !value.IsWellFormed()) throw Error("invalid_capture_capabilities");
        lock (gate)
        {
            ThrowIfDisposed();
            var discovery = recordingDiscoveries.Values.FirstOrDefault(d => d.Request.Id == id);
            // A cancelled caller may already have retired this request. Its reply cannot satisfy a replacement.
            discovery?.Ready.TrySetResult(value with { ImageFormats = value.ImageFormats.ToArray(), RecordingProfiles = value.RecordingProfiles.ToArray(), AudioModes = value.AudioModes.ToArray() });
        }
    }
}
