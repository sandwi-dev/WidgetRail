using System.Text;
using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal sealed class PrivateSecretCapabilityDomain(
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
            case PlatformCapabilities.PrivateSecretExists:
            {
                var request = ParseSlot(payload);
                var metadata = ValidateMetadata(
                    await backend.GetPrivateSecretMetadataAsync(
                        identity, request.Slot, cancellationToken).ConfigureAwait(false));
                return BrokerJson.ToElement(new PrivateSecretExistsSummary(metadata.Exists));
            }
            case PlatformCapabilities.PrivateSecretMetadata:
            {
                var request = ParseSlot(payload);
                return BrokerJson.ToElement(ValidateMetadata(
                    await backend.GetPrivateSecretMetadataAsync(
                        identity, request.Slot, cancellationToken).ConfigureAwait(false)));
            }
            case PlatformCapabilities.PrivateSecretSave:
            {
                var request = BrokerJson.ParsePayload<SavePrivateSecretRequest>(payload);
                ValidateSlot(request.Slot);
                ValidateSecret(request.Secret);
                await backend.SavePrivateSecretAsync(
                    identity, request.Slot, request.Secret, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.PrivateSecretDelete:
            {
                var request = ParseSlot(payload);
                await backend.DeletePrivateSecretAsync(
                    identity, request.Slot, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            default:
                throw new BrokerException(
                    "unsupported_operation", "Private-secret operation is unsupported.");
        }
    }

    internal static bool IsValidSlot(string? slot) =>
        !string.IsNullOrEmpty(slot) &&
        slot.Length <= CommunityPlatformLimits.MaximumPrivateSecretSlotCharacters &&
        slot.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '_' or '-');

    internal static void ValidateSlot(string? slot)
    {
        if (!IsValidSlot(slot))
            throw new BrokerException("invalid_payload", "Private secret slot is invalid.");
    }

    internal static void ValidateSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Contains('\0'))
            throw new BrokerException("invalid_payload", "Private secret is invalid.");
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(secret) >
                CommunityPlatformLimits.MaximumPrivateSecretUtf8Bytes)
                throw new BrokerException("invalid_payload", "Private secret is invalid.");
        }
        catch (EncoderFallbackException exception)
        {
            throw new BrokerException(
                "invalid_payload", "Private secret is invalid.", exception);
        }
    }

    internal static PrivateSecretMetadataSummary ValidateMetadata(
        PrivateSecretMetadataSummary? metadata)
    {
        if (metadata is null ||
            metadata.Exists != metadata.LastWrittenUnixMilliseconds.HasValue ||
            metadata.LastWrittenUnixMilliseconds is < 0)
            throw new BrokerException(
                "invalid_backend_data", "Private secret metadata is invalid.");
        return metadata;
    }

    private static PrivateSecretSlotRequest ParseSlot(JsonElement payload)
    {
        var request = BrokerJson.ParsePayload<PrivateSecretSlotRequest>(payload);
        ValidateSlot(request.Slot);
        return request;
    }
}
