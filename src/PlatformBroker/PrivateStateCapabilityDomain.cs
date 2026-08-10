using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal sealed class PrivateStateCapabilityDomain(
    IPlatformBrokerBackend backend,
    BrokerWidgetIdentity identity)
{
    internal async Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case PlatformCapabilities.PrivateStateRead:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSnapshot(
                    await backend.ReadPrivateStateAsync(identity, cancellationToken)
                        .ConfigureAwait(false)));
            case PlatformCapabilities.PrivateStateWrite:
            {
                var request = BrokerJson.ParsePayload<WritePrivateStateRequest>(payload);
                ValidateExpectedRevision(request.ExpectedRevision);
                _ = PrivateStateJsonCodec.DecodeCanonicalBase64(
                    request.CanonicalJsonBase64, "invalid_payload");
                return BrokerJson.ToElement(ValidateMutation(
                    await backend.WritePrivateStateAsync(
                        identity, request, cancellationToken).ConfigureAwait(false)));
            }
            case PlatformCapabilities.PrivateStateClear:
            {
                var request = BrokerJson.ParsePayload<ClearPrivateStateRequest>(payload);
                ValidateExpectedRevision(request.ExpectedRevision);
                return BrokerJson.ToElement(ValidateMutation(
                    await backend.ClearPrivateStateAsync(
                        identity, request, cancellationToken).ConfigureAwait(false)));
            }
            default:
                throw new BrokerException(
                    "unsupported_operation", "Private-state operation is unsupported.");
        }
    }

    internal static PrivateStateSnapshotSummary ValidateSnapshot(
        PrivateStateSnapshotSummary? snapshot)
    {
        if (snapshot is null || snapshot.Revision < 0 ||
            snapshot.Exists != (snapshot.CanonicalJsonBase64 is not null))
            throw new BrokerException(
                "invalid_backend_data", "Private state result is invalid.");
        if (snapshot.CanonicalJsonBase64 is not null)
            _ = PrivateStateJsonCodec.DecodeCanonicalBase64(
                snapshot.CanonicalJsonBase64, "invalid_backend_data");
        return snapshot;
    }

    internal static PrivateStateMutationSummary ValidateMutation(
        PrivateStateMutationSummary? mutation)
    {
        if (mutation is null || mutation.Revision <= 0)
            throw new BrokerException(
                "invalid_backend_data", "Private state revision is invalid.");
        return mutation;
    }

    internal static void ValidateExpectedRevision(long? revision)
    {
        if (revision < 0)
            throw new BrokerException(
                "invalid_payload", "Expected state revision is invalid.");
    }
}
