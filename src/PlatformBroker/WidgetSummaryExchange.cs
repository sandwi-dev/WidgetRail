using System.Text;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

// One exchange per Bridge. A worker owns at most one volatile, addressed summary.
// Notifications carry no content. Readers fetch the latest value under their own consent lease.
internal sealed class WidgetSummaryExchange
{
    private readonly object gate = new();
    private readonly Dictionary<string, Publication> publishers = new(StringComparer.Ordinal);
    internal event Action<string, string>? Changed;
    internal sealed class Reader(BrokerWidgetIdentity identity)
    {
        internal BrokerWidgetIdentity Identity { get; } = identity;
    }

    internal sealed record ReaderGrant(string Proof, string Capability);
    internal sealed class Publication(BrokerWidgetIdentity identity)
    {
        internal BrokerWidgetIdentity Identity { get; } = identity;
        internal string Package => Identity.PackageId;
        internal string? Audience;
        internal JsonElement? Value;
        internal long Sequence;
        internal readonly Dictionary<Reader, ReaderGrant> Readers = [];
        internal ActionDelivery? Delivery;
    }

    internal Publication Register(BrokerWidgetIdentity identity)
    {
        string? previousAudience;
        var current = new Publication(identity);
        lock (gate)
        {
            previousAudience = publishers.GetValueOrDefault(identity.PackageId)?.Audience;
            publishers[identity.PackageId] = current;
        }
        if (previousAudience is not null) Changed?.Invoke(previousAudience, identity.PackageId);
        return current;
    }

    internal void Clear(Publication? publication, bool retire = false)
    {
        if (publication is null) return;
        string? audience;
        lock (gate)
        {
            if (publishers.GetValueOrDefault(publication.Package) != publication) return;
            audience = publication.Audience;
            publication.Audience = null;
            publication.Value = null;
            publication.Readers.Clear();
            if (retire) publishers.Remove(publication.Package);
        }
        if (audience is not null) Changed?.Invoke(audience, publication.Package);
    }

    internal void Forget(Reader? reader, IReadOnlySet<string>? retained = null)
    {
        if (reader is null) return;
        lock (gate)
            foreach (var publication in publishers.Values)
                if (publication.Readers.TryGetValue(reader, out var grant) && retained?.Contains(grant.Capability) != true)
                    publication.Readers.Remove(reader);
    }

    internal BrokerWidgetIdentity? ActionDestination(BrokerWidgetIdentity source, string proof)
    {
        lock (gate)
            return publishers.Values.FirstOrDefault(publication => publication.Audience == source.PackageId &&
                publication.Readers.Any(pair => pair.Key.Identity == source && pair.Value.Proof == proof))?.Identity;
    }

    internal sealed class ActionDelivery(WidgetSummaryExchange exchange, Publication publication, string reader, string token, bool wantsPin, string ticket) : IDisposable
    {
        internal string ReaderProof { get; } = reader;
        internal string ActionToken { get; } = token;
        internal string? Layout { get; set; }
        internal bool WantsPin { get; } = wantsPin;
        internal string Ticket { get; } = ticket;
        internal bool MicrophoneRequested { get; set; }
        internal string? Complete()
        {
            lock (exchange.gate)
                return publication.Delivery == this && exchange.publishers.GetValueOrDefault(publication.Package) == publication &&
                    publication.Readers.Values.Any(g => g.Proof == ReaderProof) ? Layout : null;
        }
        public void Dispose()
        {
            lock (exchange.gate)
                if (publication.Delivery == this) publication.Delivery = null;
        }
    }

    internal ActionDelivery? BeginDelivery(BrokerWidgetIdentity source, BrokerWidgetIdentity destination, string reader, string token, bool wantsPin, string ticket)
    {
        lock (gate)
        {
            var pub = publishers.GetValueOrDefault(destination.PackageId);
            if (pub?.Identity != destination || pub.Delivery is not null || ActionDestination(source, reader) != destination) return null;
            return pub.Delivery = new(this, pub, reader, token, wantsPin, ticket);
        }
    }

    internal string? ConsumeMicrophoneRequest(Publication publication, WidgetSummaryMicrophone request)
    {
        lock (gate)
        {
            if (publishers.GetValueOrDefault(publication.Package) != publication ||
                publication.Delivery is not { WantsPin: false, MicrophoneRequested: false } delivery ||
                delivery.ReaderProof != request.Reader || delivery.ActionToken != request.Token ||
                !publication.Readers.Values.Any(g => g.Proof == request.Reader)) return null;
            delivery.MicrophoneRequested = true;
            return delivery.Ticket;
        }
    }

    internal JsonElement SelectPin(Publication publication, JsonElement payload)
    {
        var request = BrokerJson.ParsePayload<WidgetSummaryPin>(payload);
        if (!new WidgetPinRequest(request.LayoutId).IsWellFormed()) throw new BrokerException("invalid_payload", "Invalid pin layout.");
        lock (gate)
        {
            var admitted = publishers.GetValueOrDefault(publication.Package) == publication && publication.Delivery is { Layout: null, WantsPin: true } pin &&
                pin.ReaderProof == request.Reader && pin.ActionToken == request.Token && publication.Readers.Values.Any(g => g.Proof == request.Reader);
            if (admitted) publication.Delivery!.Layout = request.LayoutId;
            return BrokerJson.ToElement(new WidgetPinReceipt(admitted));
        }
    }

    internal JsonElement Read(Reader reader, string capability, JsonElement payload, CancellationToken token)
    {
        var request = BrokerJson.ParsePayload<ReadWidgetSummary>(payload);
        if (!WidgetManifestValidator.IsValidPackageIdentity(request.Provider) ||
            capability != PlatformCapabilities.WidgetSummaryReadV1 && WidgetSummaryScope.Provider(capability) != request.Provider)
            throw new BrokerException("invalid_payload", "Choose a valid summary provider.");
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            var publication = publishers.GetValueOrDefault(request.Provider);
            if (publication?.Audience != reader.Identity.PackageId || publication.Value is null)
                return BrokerJson.ToElement(new WidgetSummaryValue(null) { ProviderRunning = publication is not null });
            if (!publication.Readers.TryGetValue(reader, out var grant) || grant.Capability != capability)
                publication.Readers[reader] = grant = new(Guid.NewGuid().ToString("N"), capability);
            return BrokerJson.ToElement(new WidgetSummaryValue(publication.Value, grant.Proof) { ProviderRunning = true });
        }
    }

    internal async Task<JsonElement> CheckReaderAsync(Publication publication, JsonElement payload, ConsentStore consent, CancellationToken token)
    {
        var proof = BrokerJson.ParsePayload<CheckWidgetSummaryReader>(payload).Proof;
        if (!Guid.TryParseExact(proof, "N", out _)) throw new BrokerException("invalid_payload", "Invalid summary reader reference.");
        KeyValuePair<Reader, ReaderGrant> entry;
        lock (gate) entry = publication.Readers.FirstOrDefault(pair => pair.Value.Proof == proof);
        var granted = entry.Key is not null && await consent.GetDecisionAsync(entry.Key.Identity, entry.Value.Capability, token).ConfigureAwait(false) == ConsentDecision.Grant;
        lock (gate)
            return BrokerJson.ToElement(new { acknowledged = granted && publishers.GetValueOrDefault(publication.Package) == publication &&
                publication.Readers.GetValueOrDefault(entry.Key!) == entry.Value });
    }

    internal JsonElement Publish(Publication publication, JsonElement payload, CancellationToken token)
    {
        var request = BrokerJson.ParsePayload<PublishWidgetSummary>(payload);
        if (request.Sequence <= 0 || !WidgetManifestValidator.IsValidPackageIdentity(request.Audience) ||
            request.Value is { } value && (value.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(value.GetRawText()) > 16 * 1024))
            throw new BrokerException("invalid_payload", "Publish one bounded summary to a valid recipient.");
        string? previous;
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            if (publishers.GetValueOrDefault(publication.Package) != publication)
                throw new BrokerException("stale_owner", "The summary owner has retired.");
            if (request.Sequence <= publication.Sequence) return BrokerJson.ToElement(new { acknowledged = false });
            publication.Sequence = request.Sequence;
            previous = publication.Audience;
            var next = request.Value.HasValue ? request.Audience : null;
            if (previous == next && publication.Value?.GetRawText() == request.Value?.GetRawText())
                return BrokerCapabilityDomains.Acknowledged();
            if (previous != next) publication.Readers.Clear();
            publication.Value = request.Value?.Clone();
            publication.Audience = next;
        }
        if (previous is not null && previous != request.Audience) Changed?.Invoke(previous, publication.Package);
        Changed?.Invoke(request.Audience, publication.Package);
        return BrokerCapabilityDomains.Acknowledged();
    }
}
