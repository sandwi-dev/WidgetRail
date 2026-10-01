using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

internal static class BridgePinnedRequestValidation
{
    internal static void Validate(BridgePinnedActionRequest request)
    {
        Identity(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration);
        PinnedActionContract.Validate(request.Input);
    }
    internal static void Validate(BridgePinnedArtworkRequest request)
    {
        Identity(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration);
        if (request.Version != 1 || request.SnapshotSequence <= 0 ||
            !BridgeRequestKey.IsBoundedIdentifier(request.LayoutId) || !BridgeRequestKey.IsBoundedIdentifier(request.InputScopeId) ||
            !BridgeRequestKey.IsBoundedIdentifier(request.ArtworkHandle) || !BridgeRequestKey.IsBoundedIdentifier(request.DemandId) || request.DemandId.Length > 64)
            throw new BridgeProtocolException("Invalid pinned artwork authority.");
    }
    private static void Identity(params string[] values)
    {
        if (values.Any(value => !BridgeRequestKey.IsBoundedIdentifier(value)))
            throw new BridgeProtocolException("Invalid pinned widget identity.");
    }
}

internal sealed partial class BridgeClientRegistry
{
    internal async Task<BridgeClientPublication<WidgetOperationAdmission?>> AdmitPinnedActionAsync(
        BridgePinnedActionRequest request, CancellationToken sessionCancellation, CancellationToken cancellationToken)
    {
        BridgePinnedRequestValidation.Validate(request);
        var registration = await GetOrCreateAsync(request.WidgetId, cancellationToken).ConfigureAwait(false);
        using var heldRegistration = AdmitInputPublication(registration, registration, pinned: true);
        await registration.OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DemandCurrentInputRegistration(registration, pinned: true);
            DemandInputWorkerRun(registration, request.WorkerRun, pinned: true);
            DemandInteractionAllowed(registration);
            var current = DemandPinnedIdentity(registration, request.InstanceId, request.RuntimeGeneration,
                request.PresentationGeneration, request.Input.LayoutId);
            var origin = registration.FindInputOriginSnapshot(request.Input.SnapshotSequence)
                ?? throw new BridgeStalePinnedInputAuthorityException("Pinned action origin retired.");
            try
            {
                if (PinnedActionContract.Resolve(origin, request.Input) != PinnedActionContract.Resolve(current, request.Input))
                    throw new InvalidOperationException("Pinned action binding changed.");
            }
            catch (InvalidOperationException)
            { throw new BridgeStalePinnedInputAuthorityException("Pinned action binding retired."); }
            registration.CancelIdleUnload();
            var admission = await ExecuteClientOperationAsync(registration,
                (client, token) => client.AdmitPinnedActionAsync(request.Input with { SnapshotSequence = current.Sequence }, token, request.WorkerRun?.StartOrdinal),
                cancellationToken).ConfigureAwait(false);
            DemandCurrentInputRegistration(registration, pinned: true);
            ScheduleIdleUnload(registration, sessionCancellation);
            return AdmitInputPublication(registration, admission, pinned: true);
        }
        finally { registration.OperationGate.Release(); }
    }

    private static ViewSnapshot DemandPinnedIdentity(ClientRegistration registration, string instance, string runtime,
        string presentation, string layout)
    {
        var descriptor = registration.Configured.PublicDescriptor();
        if (descriptor.InstanceId != instance || descriptor.RuntimeGeneration != runtime || descriptor.PresentationGeneration != presentation ||
            !descriptor.PinningSupported || layout == PinnedSurfaceContract.FullWidgetLayoutId && !descriptor.FullWidgetPinningSupported ||
            !registration.HasCurrentSnapshotWorker || registration.CachedSnapshot is not { } current)
            throw new BridgeStalePinnedInputAuthorityException("Pinned widget authority retired.");
        return current;
    }

    private static void DemandPinnedArtwork(ClientRegistration registration, BridgePinnedArtworkRequest request)
    {
        BridgePinnedRequestValidation.Validate(request);
        var current = DemandPinnedIdentity(registration, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration, request.LayoutId);
        var origin = registration.FindInputOriginSnapshot(request.SnapshotSequence)
            ?? throw new BridgeStaleArtworkAuthorityException("Pinned artwork origin retired.");
        try
        {
            foreach (var snapshot in new[] { origin, current })
            {
                var projection = PinnedActionContract.Project(snapshot, request.LayoutId, inheritRootless: true);
                if (projection.ActiveInputScopeId != request.InputScopeId || !Contains(projection.Root, request.ArtworkHandle))
                    throw new InvalidOperationException();
            }
        }
        catch (InvalidOperationException)
        { throw new BridgeStaleArtworkAuthorityException("Pinned artwork layout or handle retired."); }
    }
}
