using System.Globalization;
using System.Text.Json;

namespace WidgetRail.PlatformBroker;

internal sealed partial class AppLibraryCapabilityDomain
{
    private readonly object _launchOwner = new();
    private LaunchHandoffTicket? _launchTicket;
    private long _launchDeadline;

    internal void CancelLaunchHandoff()
    {
        lock (_launchOwner)
        {
            _launchTicket = null;
            WindowPreviewRegistry.Replace(_launchOwner, _identity, new Dictionary<string, NativeWindowPreviewTarget>());
        }
    }

    private async Task<JsonElement> LaunchHandoffAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (operation == PlatformCapabilities.LaunchHandoffBegin)
        {
            BrokerCapabilityDomains.DemandEmptyPayload(payload);
            lock (_launchOwner)
            {
                token.ThrowIfCancellationRequested();
                _launchTicket = new(Guid.NewGuid().ToString("N"));
                _launchDeadline = Environment.TickCount64 + 30000;
                return BrokerJson.ToElement(_launchTicket);
            }
        }
        var request = BrokerJson.ParsePayload<LaunchHandoffTarget>(payload);
        if (request.ProcessId == 0 || request.CreatedFileTime <= 0 || request.WindowHandle <= 0)
            throw new BrokerException("invalid_payload", "A process instance and window are required.");
        void DemandTicket()
        {
            token.ThrowIfCancellationRequested();
            if (_launchTicket is null || _launchTicket.Ticket != request.Ticket || Environment.TickCount64 > _launchDeadline)
                throw new BrokerException("launch_expired", "This launch no longer owns foreground activation.");
        }
        lock (_launchOwner) DemandTicket();
        var target = await _backend.InspectLaunchWindowAsync(request.WindowHandle, token).ConfigureAwait(false);
        lock (_launchOwner)
        {
            DemandTicket();
            if (target is null || target.ProcessId != request.ProcessId ||
                !long.TryParse(target.ProcessCreated, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var created) || created != request.CreatedFileTime ||
                !long.TryParse(target.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handle) || handle != request.WindowHandle)
                return BrokerJson.ToElement(new LaunchHandoffResult(null));
            _launchTicket = null;
            var id = "launch-" + Guid.NewGuid().ToString("N");
            WindowPreviewRegistry.Replace(_launchOwner, _identity, new Dictionary<string, NativeWindowPreviewTarget> { [id] = target });
            return BrokerJson.ToElement(new LaunchHandoffResult(id));
        }
    }
}
