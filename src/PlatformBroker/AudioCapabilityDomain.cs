using System.Text.Json;

namespace GameBarAlternative.PlatformBroker;

internal sealed class AudioCapabilityDomain(IPlatformBrokerBackend backend)
{
    internal async Task<JsonElement> ExecuteAsync(
        string operation,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case PlatformCapabilities.AudioSessionsList:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateSessions(
                    await backend.GetAudioSessionsAsync(cancellationToken).ConfigureAwait(false)));
            case PlatformCapabilities.AudioSessionSetVolume:
            {
                var request = BrokerJson.ParsePayload<SetAudioSessionVolumeRequest>(payload);
                ContractValidation.OpaqueId(request.SessionId);
                ValidateVolume(request.Volume, "Audio volume");
                await backend.SetAudioSessionVolumeAsync(
                    request.SessionId, request.Volume, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.AudioSessionSetMuted:
            {
                var request = BrokerJson.ParsePayload<SetAudioSessionMutedRequest>(payload);
                ContractValidation.OpaqueId(request.SessionId);
                await backend.SetAudioSessionMutedAsync(
                    request.SessionId, request.IsMuted, cancellationToken).ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.AudioOutputGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateOutput(
                    await backend.GetAudioOutputAsync(cancellationToken).ConfigureAwait(false)));
            case PlatformCapabilities.AudioOutputSetVolume:
            {
                var request = BrokerJson.ParsePayload<SetAudioOutputVolumeRequest>(payload);
                ValidateVolume(request.Volume, "Audio output volume");
                await backend.SetAudioOutputVolumeAsync(request.Volume, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.AudioOutputSetMuted:
            {
                var request = BrokerJson.ParsePayload<SetAudioOutputMutedRequest>(payload);
                await backend.SetAudioOutputMutedAsync(request.IsMuted, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.AudioDevicesList:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateDevices(
                    await backend.GetAudioDevicesAsync(cancellationToken).ConfigureAwait(false)));
            case PlatformCapabilities.AudioInputGet:
                BrokerCapabilityDomains.DemandEmptyPayload(payload);
                return BrokerJson.ToElement(ValidateInput(
                    await backend.GetAudioInputAsync(cancellationToken).ConfigureAwait(false)));
            case PlatformCapabilities.AudioInputSetVolume:
            {
                var request = BrokerJson.ParsePayload<SetAudioInputVolumeRequest>(payload);
                ValidateVolume(request.Volume, "Audio input volume");
                await backend.SetAudioInputVolumeAsync(request.Volume, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            case PlatformCapabilities.AudioInputSetMuted:
            {
                var request = BrokerJson.ParsePayload<SetAudioInputMutedRequest>(payload);
                await backend.SetAudioInputMutedAsync(request.IsMuted, cancellationToken)
                    .ConfigureAwait(false);
                return BrokerCapabilityDomains.Acknowledged();
            }
            default:
                throw new BrokerException(
                    "unsupported_operation", "Audio operation is unsupported.");
        }
    }

    internal static JsonElement ProjectEvent(string eventType, object payload) =>
        eventType switch
        {
            PlatformCapabilities.AudioSessionsChanged when
                payload is AudioSessionsChangedEvent change =>
                BrokerJson.ToElement(new AudioSessionsChangedEvent(
                    ValidateSessions(change.Sessions), change.IsAvailable)),
            PlatformCapabilities.AudioOutputChanged when
                payload is AudioOutputChangedEvent change =>
                BrokerJson.ToElement(ValidateOutputEvent(change)),
            PlatformCapabilities.AudioDevicesChanged when
                payload is AudioDevicesChangedEvent change =>
                BrokerJson.ToElement(ValidateDevicesEvent(change)),
            PlatformCapabilities.AudioInputChanged when
                payload is AudioInputChangedEvent change =>
                BrokerJson.ToElement(ValidateInputEvent(change)),
            _ => throw new BrokerException(
                "invalid_backend_data", "Audio event payload is invalid."),
        };

    internal static IReadOnlyList<AudioSessionSummary> ValidateSessions(
        IReadOnlyList<AudioSessionSummary>? sessions)
    {
        if (sessions is null)
            throw new BrokerException("invalid_backend_data", "Audio session result is invalid.");
        if (sessions.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException("invalid_backend_data", "Too many audio sessions.");
        foreach (var session in sessions)
        {
            if (session is null)
                throw new BrokerException(
                    "invalid_backend_data", "Audio session result is invalid.");
            ContractValidation.OpaqueId(session.SessionId, "invalid_backend_data");
            ContractValidation.DisplayName(session.DisplayName);
            if (!double.IsFinite(session.Volume) || session.Volume is < 0 or > 1)
                throw new BrokerException("invalid_backend_data", "Audio volume is invalid.");
        }
        return sessions.ToArray();
    }

    internal static AudioOutputSummary ValidateOutput(AudioOutputSummary? output)
    {
        if (output is null || !double.IsFinite(output.Volume) ||
            output.Volume is < 0 or > 1)
            throw new BrokerException("invalid_backend_data", "Audio output result is invalid.");
        return output;
    }

    internal static IReadOnlyList<AudioDeviceSummary> ValidateDevices(
        IReadOnlyList<AudioDeviceSummary>? devices)
    {
        if (devices is null || devices.Count > BrokerJson.MaximumArrayItems)
            throw new BrokerException("invalid_backend_data", "Audio device result is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var defaults = new HashSet<AudioDeviceDirection>();
        foreach (var device in devices)
        {
            if (device is null || !Enum.IsDefined(device.Direction))
                throw new BrokerException("invalid_backend_data", "Audio device result is invalid.");
            ContractValidation.OpaqueId(device.DeviceId, "invalid_backend_data");
            ContractValidation.DisplayName(device.DisplayName);
            if (!ids.Add(device.DeviceId) || device.IsDefault && !defaults.Add(device.Direction))
                throw new BrokerException(
                    "invalid_backend_data", "Audio device result is inconsistent.");
        }
        return devices.ToArray();
    }

    internal static AudioInputSummary ValidateInput(AudioInputSummary? input)
    {
        if (input is null || !double.IsFinite(input.Volume) || input.Volume is < 0 or > 1)
            throw new BrokerException("invalid_backend_data", "Audio input result is invalid.");
        return input;
    }

    private static void ValidateVolume(double volume, string label)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new BrokerException(
                "invalid_payload", $"{label} must be between zero and one.");
    }

    private static AudioOutputChangedEvent ValidateOutputEvent(AudioOutputChangedEvent change)
    {
        if (change.IsAvailable != (change.Output is not null))
            throw new BrokerException(
                "invalid_backend_data", "Audio output availability is inconsistent.");
        return new AudioOutputChangedEvent(
            change.Output is null ? null : ValidateOutput(change.Output), change.IsAvailable);
    }

    private static AudioDevicesChangedEvent ValidateDevicesEvent(AudioDevicesChangedEvent change)
    {
        if (!change.IsAvailable && change.Devices.Count != 0)
            throw new BrokerException(
                "invalid_backend_data", "Audio device availability is inconsistent.");
        return new AudioDevicesChangedEvent(
            ValidateDevices(change.Devices), change.IsAvailable);
    }

    private static AudioInputChangedEvent ValidateInputEvent(AudioInputChangedEvent change)
    {
        if (change.IsAvailable != (change.Input is not null))
            throw new BrokerException(
                "invalid_backend_data", "Audio input availability is inconsistent.");
        return new AudioInputChangedEvent(
            change.Input is null ? null : ValidateInput(change.Input), change.IsAvailable);
    }
}
