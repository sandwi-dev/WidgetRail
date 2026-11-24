using WidgetRail.Internal;

namespace WidgetRail.PlatformBroker;

internal static class BrokerPipeRequestTimeoutPolicy
{
    internal static TimeSpan Resolve(
        BrokerPipeTransportOptions options, BrokerRequestEnvelope request)
    {
        if (request.CapabilityId == BrokerTransportOperations.WindowCaptureV1 && request.Operation is "capture.replay.configure" or "capture.replay.start" or "capture.replay.choose-approval")
            return options.RequestTimeout > TimeSpan.FromSeconds(130) ? options.RequestTimeout : TimeSpan.FromSeconds(130);
        if (request.CapabilityId == BrokerTransportOperations.WindowCaptureV1 && request.Operation == "capture.replay.save")
            return options.RequestTimeout > TimeSpan.FromSeconds(50) ? options.RequestTimeout : TimeSpan.FromSeconds(50);
        if (request.CapabilityId == BrokerTransportOperations.WindowCaptureV1 && request.Operation == "capture.replay.stop")
            return options.RequestTimeout > TimeSpan.FromSeconds(15) ? options.RequestTimeout : TimeSpan.FromSeconds(15);
        if (request.CapabilityId == "files.references.v1" && request.Operation == "files.choose")
            return options.RequestTimeout > TimeSpan.FromSeconds(125) ? options.RequestTimeout : TimeSpan.FromSeconds(125);
        if (request.CapabilityId == "media.capture-library.save.v1" && request.Operation == "capture-library.edit-copy")
        {
            var deadline = TimeSpan.FromSeconds(1205); // ten minutes editor + ten minutes bounded encoding + IPC
            return options.RequestTimeout > deadline ? options.RequestTimeout : deadline;
        }
        // The broker checks revisions and performs each disk or Shell mutation in sequence.
        if ((request.CapabilityId == BrokerTransportOperations.CaptureLibraryManageV1 &&
                request.Operation is "capture-library.recycle" or "capture-library.metadata-batch") ||
            (request.CapabilityId == "media.capture-library.recycle-linked.v1" && request.Operation == "capture-library.recycle-linked"))
        {
            var deadline = TimeSpan.FromMinutes(2);
            return options.RequestTimeout > deadline ? options.RequestTimeout : deadline;
        }
        if(request.CapabilityId==BrokerTransportOperations.WindowCaptureV1 && request.Operation is "capture.web-prepare" or "capture.web-prepare-constrained")
            return options.RequestTimeout>TimeSpan.FromSeconds(95)?options.RequestTimeout:TimeSpan.FromSeconds(95);
        if (request.CapabilityId == "web.sessions.v1" && request.Operation is "web-session.microphone" or "web-session.permission" ||
            request.CapabilityId == "widgets.summary.provider.v1" && request.Operation == "summary.microphone")
            return options.RequestTimeout > TimeSpan.FromSeconds(95) ? options.RequestTimeout : TimeSpan.FromSeconds(95);
        if (request.CapabilityId == "web.sessions.v1" && request.Operation == "web-session.command")
            return options.RequestTimeout > TimeSpan.FromSeconds(35) ? options.RequestTimeout : TimeSpan.FromSeconds(35);
        if (request.CapabilityId == BrokerTransportOperations.CaptureLibraryManageV1 && request.Operation == "capture-library.copy")
        {
            var deadline = TimeSpan.FromSeconds(BrokerTransportLimits.CaptureCopyPickerTimeoutSeconds + BrokerTransportLimits.CaptureCopyTimeoutSeconds + 5);
            return options.RequestTimeout > deadline ? options.RequestTimeout : deadline;
        }
        if (request.CapabilityId == BrokerTransportOperations.CaptureLibraryManageV1 && request.Operation == "capture-library.save-copy")
        {
            var deadline = TimeSpan.FromSeconds(BrokerTransportLimits.CaptureCopyTimeoutSeconds + 5);
            return options.RequestTimeout > deadline ? options.RequestTimeout : deadline;
        }
        if (request.CapabilityId == BrokerTransportOperations.CaptureLibraryManageV1 && request.Operation is "capture-library.choose-source" or "capture-library.change-destination")
            return options.RequestTimeout > TimeSpan.FromSeconds(125) ? options.RequestTimeout : TimeSpan.FromSeconds(125);
        // Opening Explorer uses the host's release-aware handoff, not the ordinary short read deadline.
        if (request.CapabilityId == BrokerTransportOperations.CaptureLibraryManageV1 && request.Operation is "capture-library.open-folder" or "capture-library.reveal")
            return options.RequestTimeout > TimeSpan.FromSeconds(20) ? options.RequestTimeout : TimeSpan.FromSeconds(20);
        // The first configured-capture request can wait for lazy encoder/audio
        // discovery in the frontend. Both pipe ends must outlive that bounded
        // registry wait; ordinary status/read/cancel requests remain short.
        if (request.CapabilityId == BrokerTransportOperations.WindowCaptureV1 &&
            request.Operation is "capture.capabilities" or "capture.recording-capabilities" or "capture.validate" or "capture.start" or "capture.save-screenshot" or "capture.record-clip")
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
             request.Operation is BrokerTransportOperations.DisplayControlsApply or BrokerTransportOperations.DisplayProfilesApply or
                 BrokerTransportOperations.DisplayProfilesKeep or BrokerTransportOperations.DisplayProfilesRevert) ||
            (request.CapabilityId == BrokerTransportOperations.DisplaysReadV1 &&
             request.Operation is BrokerTransportOperations.DisplayProfilesGet or BrokerTransportOperations.DisplayControlsGet))
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
