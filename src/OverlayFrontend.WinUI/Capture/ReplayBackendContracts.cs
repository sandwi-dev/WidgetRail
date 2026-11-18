using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Capture;

internal static class ReplayBackendAuthority
{
    // Matches replay_worker::Worker::Current's existing heartbeat expiry.
    // This bounds transient bridge capacity retries; it never extends native authority.
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(10);
}

internal sealed record ResolvedCaptureRecording(CaptureRecordingConfiguration Configuration,ulong NativeAdapterLuid);

/// <summary>Trusted frontend/backend boundary; native handles and paths never enter widget IPC.</summary>
internal interface IReplayCaptureBackendFactory
{
    Task<IReplayCaptureBackend> StartAsync(NativeWindowPreviewTarget target,CaptureReplaySettings settings,CancellationToken cancellationToken);
}

internal sealed record ReplayBackendState(CaptureReplayPhase Phase,double BufferedDurationSeconds,long ResidentBytes,
    long DroppedFrames,CaptureReplayIssue Issue=CaptureReplayIssue.None,bool Ended=false,bool Saving=false);

internal sealed record ReplayBackendSaveResult(int Width,int Height,double ActualDurationSeconds,long ByteLength,
    CaptureMediaProperties Properties);

internal sealed class ReplayBackendException(CaptureReplayIssue issue,int nativeError=0) : Exception("Replay backend failed: "+issue)
{
    internal CaptureReplayIssue Issue { get; }=issue;
    internal int NativeError { get; }=nativeError;
}

internal interface IReplayCaptureBackend : IAsyncDisposable
{
    CaptureRecordingConfiguration? ResolvedConfiguration => null;
    Task<ReplayBackendState> ReadStateAsync(CancellationToken cancellationToken);
    /// <summary>Renew only after the coordinator verifies current broker session authority.</summary>
    void RenewAuthority();
    /// <summary>The destination is an exact broker-reserved library staging path, never widget input.</summary>
    Task<ReplayBackendSaveResult> SaveAsync(string trustedOutputPath,int durationSeconds,CancellationToken cancellationToken);
    /// <summary>Stop acquisition and allow an already accepted save to finish; disposal is emergency teardown.</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
