using System.Text.Json;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.PlatformBroker;

internal sealed class ProviderDocumentCapabilityDomain(BrokerWidgetIdentity identity) : IDisposable
{
    internal JsonElement Execute(string operation, JsonElement payload, CancellationToken token)
    {
        if (operation == PlatformCapabilities.ProviderDocumentCreate)
        {
            var content = BrokerJson.ParsePayload<ProviderDocumentContent>(payload);
            var registry = ProviderDocumentRegistry.Shared;
            var epoch = registry.Begin(this);
            token.ThrowIfCancellationRequested();
            var reference = registry.Register(this, identity, content.Html, epoch);
            try { token.ThrowIfCancellationRequested(); return BrokerJson.ToElement(reference); }
            catch { registry.Discard(this, reference); throw; }
        }
        if (operation == PlatformCapabilities.ProviderDocumentDiscard)
        {
            var reference = BrokerJson.ParsePayload<ProviderDocumentReference>(payload);
            if (!reference.IsWellFormed()) throw new BrokerException("invalid_payload", "Invalid document reference.");
            ProviderDocumentRegistry.Shared.Discard(this, reference);
            return BrokerCapabilityDomains.Acknowledged();
        }
        throw new BrokerException("unsupported_operation", "Unsupported document operation.");
    }
    internal void Revoke() => ProviderDocumentRegistry.Shared.Revoke(this);
    public void Dispose() => ProviderDocumentRegistry.Shared.Retire(this);
}
