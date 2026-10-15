using WidgetRail.Internal;

namespace WidgetRail.PlatformBroker;

internal static class BrokerPipeRequestTimeoutPolicy
{
    internal static TimeSpan Resolve(
        BrokerPipeTransportOptions options, BrokerRequestEnvelope request)
    {
        if (request.CapabilityId == "web.sessions.v1" && request.Operation == "web-session.command")
            return options.RequestTimeout > TimeSpan.FromSeconds(35) ? options.RequestTimeout : TimeSpan.FromSeconds(35);
        // Opening Explorer uses the host's release-aware handoff, not the ordinary short read deadline.
        if (request.CapabilityId == BrokerTransportOperations.CaptureLibraryManageV1 && request.Operation == "capture-library.open-folder")
            return options.RequestTimeout > TimeSpan.FromSeconds(20) ? options.RequestTimeout : TimeSpan.FromSeconds(20);
        // The first configured-capture request can wait for lazy encoder/audio
        // discovery in the frontend. Both pipe ends must outlive that bounded
        // registry wait; ordinary status/read/cancel requests remain short.
        if (request.CapabilityId == BrokerTransportOperations.WindowCaptureV1 &&
            request.Operation is "capture.capabilities" or "capture.recording-capabilities" or "capture.validate" or "capture.start")
        {
            var deadline = TimeSpan.FromSeconds(BrokerTransportLimits.CaptureCapabilityDiscoveryTimeoutSeconds + 5);
            return options.RequestTimeout > deadline ? options.RequestTimeout : deadline;
        }

        // An explicit inquiry owns a bounded scan, not a short status read.
        // Use the same duration as the provider on both ends of the pipe.
        if (request.CapabilityId == BrokerTransportOperations.NetworkBluetoothPairV1 &&
            request.Operation == BrokerTransportOperations.NetworkBluetoothScan)
        {
            var deadline = TimeSpan.FromSeconds(BluetoothDiscoveryLimits.ScanDurationSeconds +
                BluetoothDiscoveryLimits.CompletionBudgetSeconds);
            return options.RequestTimeout > deadline ? options.RequestTimeout : deadline;
        }

        // Display restore owns a bounded wake-up phase in a separate guard.
        // Reads can wait behind that transaction; unrelated display edits and
        // other capabilities keep their ordinary deadline on both pipe ends.
        if ((request.CapabilityId == BrokerTransportOperations.DisplaysControlV1 &&
             request.Operation is BrokerTransportOperations.DisplayProfilesApply or
                 BrokerTransportOperations.DisplayProfilesKeep or BrokerTransportOperations.DisplayProfilesRevert) ||
            (request.CapabilityId == BrokerTransportOperations.DisplaysReadV1 &&
             request.Operation == BrokerTransportOperations.DisplayProfilesGet))
            return options.RequestTimeout > TimeSpan.FromSeconds(30) ? options.RequestTimeout : TimeSpan.FromSeconds(30);

        if (!BrokerTransportOperations.TryGetLoopbackPort(request.CapabilityId, out _) ||
            request.Operation is not (BrokerTransportOperations.LoopbackHttpGetJson or
                BrokerTransportOperations.LoopbackHttpPostJson) ||
            !request.Payload.TryGetProperty("timeoutMilliseconds", out var timeoutProperty) ||
            !timeoutProperty.TryGetInt32(out var timeoutMilliseconds) ||
            timeoutMilliseconds is < 1 or
                > CommunityPlatformContractLimits.MaximumLoopbackTimeoutMilliseconds)
            return options.RequestTimeout;

        // Provider timeout plus a small, fixed IPC completion budget. Only the
        // exact-port loopback operations can request a payload-defined deadline.
        return TimeSpan.FromMilliseconds(timeoutMilliseconds + 2_000);
    }
}
