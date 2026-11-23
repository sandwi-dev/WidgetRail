using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetCaptureReplayCapabilities
{
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, ReplayApprovalList> Approvals { get; } = new(WidgetCaptureCapabilities.Permission, "capture.replay.approvals");
    public static WidgetCapabilityOperation<ReplayApprovalRevision, CaptureReplayControlResult> ChooseApproval { get; } = new(WidgetCaptureCapabilities.Permission, "capture.replay.choose-approval");
    public static WidgetCapabilityOperation<ReplayApprovalRemoval, CaptureReplayControlResult> RemoveApproval { get; } = new(WidgetCaptureCapabilities.Permission, "capture.replay.remove-approval");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery,CaptureReplayStatus> Get { get; } = new(WidgetCaptureLibraryCapabilities.ReadPermission,"capture.replay.get");
    public static WidgetCapabilityOperation<CaptureReplaySettingsUpdate,CaptureReplayControlResult> Configure { get; } = new(WidgetCaptureCapabilities.Permission,"capture.replay.configure");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery,CaptureReplayControlResult> Start { get; } = new(WidgetCaptureCapabilities.Permission,"capture.replay.start");
    public static WidgetCapabilityOperation<CaptureReplaySessionRequest,CaptureReplayControlResult> Stop { get; } = new(WidgetCaptureCapabilities.Permission,"capture.replay.stop");
    public static WidgetCapabilityOperation<CaptureReplaySaveRequest,CaptureReplaySaveResult> Save { get; } = new(WidgetCaptureCapabilities.Permission,"capture.replay.save");
    public static WidgetCapabilityEvent<CaptureReplayStatus> Changed { get; } = new(WidgetCaptureLibraryCapabilities.ReadPermission,"capture.replay.changed");
    public static WidgetCapabilityOperation<CaptureReplayReadRequest,CaptureReplayStatus> DetailedGet { get; } = new(WidgetCaptureLibraryCapabilities.ReadPermission,"capture.replay.get");
    public static WidgetCapabilityOperation<CaptureReplayReadRequest,CaptureReplayControlResult> DetailedStart { get; } = new(WidgetCaptureCapabilities.Permission,"capture.replay.start");
    public static WidgetCapabilityEvent<CaptureReplayStatus> EncodingChanged { get; } = new(WidgetCaptureLibraryCapabilities.ReadPermission,"capture.replay.encoding-changed");
}

/// <summary>Explicit controls for a host-owned replay session. Disposing a widget does not stop an admitted recording.</summary>
public sealed class WidgetCaptureReplayService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetCaptureReplayService(IWidgetCapabilityClient client)=>this.client=client;
    public async ValueTask<ReplayApprovalList> GetApprovedApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.InvokeAsync(WidgetCaptureReplayCapabilities.Approvals, new(), cancellationToken).ConfigureAwait(false);
        return result is { Revision: >= 0 } && result.Approvals?.IsWellFormed() == true ? result : throw Malformed();
    }
    public async ValueTask<CaptureReplayControlResult> ChooseApprovedApplicationAsync(long expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        return Control(await client.InvokeAsync(WidgetCaptureReplayCapabilities.ChooseApproval, new(expectedRevision), cancellationToken).ConfigureAwait(false));
    }
    public async ValueTask<CaptureReplayControlResult> RemoveApprovedApplicationAsync(string applicationId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (applicationId is not { Length: 64 } || !applicationId.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Invalid approved application identifier.", nameof(applicationId));
        return Control(await client.InvokeAsync(WidgetCaptureReplayCapabilities.RemoveApproval, new(expectedRevision, applicationId), cancellationToken).ConfigureAwait(false));
    }
    /// <summary>Open before GetAsync, then consume host-coalesced updates without a widget polling timer.</summary>
    public ValueTask<IWidgetCapabilitySubscription<CaptureReplayStatus>> OpenChangesSubscriptionAsync(CancellationToken cancellationToken=default)=>
        client.OpenSubscriptionAsync(WidgetCaptureReplayCapabilities.EncodingChanged,cancellationToken);
    public async ValueTask<CaptureReplayStatus> GetAsync(CancellationToken cancellationToken=default)
    {
        var result=await client.InvokeAsync(WidgetCaptureReplayCapabilities.DetailedGet,new() { EncodingDetails = true },cancellationToken).ConfigureAwait(false);
        return result?.IsWellFormed()==true?result:throw Malformed();
    }
    public async ValueTask<CaptureReplayControlResult> ConfigureAsync(CaptureReplaySettings settings,long expectedSettingsRevision,CancellationToken cancellationToken=default)
    {
        var request=new CaptureReplaySettingsUpdate(expectedSettingsRevision,settings) { EncodingDetails = true };
        if(!request.IsWellFormed())throw new ArgumentException("Invalid replay settings.",nameof(settings));
        return Control(await client.InvokeAsync(WidgetCaptureReplayCapabilities.Configure,request with{Settings=settings.Normalize()},cancellationToken).ConfigureAwait(false));
    }
    public async ValueTask<CaptureReplayControlResult> StartAsync(CancellationToken cancellationToken=default)=>
        Control(await client.InvokeAsync(WidgetCaptureReplayCapabilities.DetailedStart,new() { EncodingDetails = true },cancellationToken).ConfigureAwait(false));
    public async ValueTask<CaptureReplayControlResult> StopAsync(string? expectedSessionId,CancellationToken cancellationToken=default)
    {
        var request=new CaptureReplaySessionRequest(expectedSessionId) { EncodingDetails = true };
        if(!request.IsWellFormed())throw new ArgumentException("Invalid replay session.",nameof(expectedSessionId));
        return Control(await client.InvokeAsync(WidgetCaptureReplayCapabilities.Stop,request,cancellationToken).ConfigureAwait(false));
    }
    /// <summary>One explicit save. The host deduplicates its receipt ID; this client never automatically retries a side effect.
    /// Cancellation ends the caller's wait, but an already-admitted save may still complete in the shared library.</summary>
    public async ValueTask<CaptureReplaySaveResult> SaveAsync(string expectedSessionId,CancellationToken cancellationToken=default)
    {
        var request=new CaptureReplaySaveRequest(expectedSessionId,Guid.NewGuid().ToString("N"));
        if(!request.IsWellFormed())throw new ArgumentException("Invalid replay session.",nameof(expectedSessionId));
        var result=await client.InvokeAsync(WidgetCaptureReplayCapabilities.Save,request,cancellationToken).ConfigureAwait(false);
        return result?.IsWellFormed()==true && result.RequestId==request.RequestId?result:throw Malformed();
    }
    private static CaptureReplayControlResult Control(CaptureReplayControlResult? result)=>result?.IsWellFormed()==true?result:throw Malformed();
    private static InvalidDataException Malformed()=>new("The host returned invalid replay data.");
}
