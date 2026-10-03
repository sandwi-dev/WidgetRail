using System.Runtime.CompilerServices;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Host-only provider markup, scoped to one authenticated worker lifetime.</summary>
internal sealed class ProviderDocumentRegistry
{
    internal static ProviderDocumentRegistry Shared { get; } = new();
    internal const int MaximumHtmlCharacters = ProviderDocumentContent.MaximumCharacters;
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> documents = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<object, Lifetime> lifetimes = new();
    private sealed class Lifetime { internal object Epoch = new(); internal bool Retired; }
    private long ordinal;
    private sealed record Entry(object Owner, BrokerWidgetIdentity Identity, IReadOnlyList<string> Html, long Ordinal);

    internal object Begin(object owner)
    {
        lock (gate)
        {
            var state = lifetimes.GetValue(owner, _ => new());
            if (state.Retired) throw new BrokerException("capability_revoked", "Provider document owner retired.");
            return state.Epoch;
        }
    }
    internal ProviderDocumentReference Register(object owner, BrokerWidgetIdentity identity, IReadOnlyList<string> html, object? epoch = null)
    {
        if (!ValidHtml(html)) throw new BrokerException("invalid_response", "Provider attribution is invalid.");
        lock (gate)
        {
            var state = lifetimes.GetValue(owner, _ => new());
            if (state.Retired || epoch is not null && !ReferenceEquals(state.Epoch, epoch))
                throw new BrokerException("capability_revoked", "Provider document owner retired.");
            var own = documents.Where(pair => ReferenceEquals(pair.Value.Owner, owner)).OrderBy(pair => pair.Value.Ordinal).ToArray();
            if (own.Length >= 24) documents.Remove(own[0].Key);
            if (documents.Count >= 64) throw new BrokerException("attribution_limit", "Close an unused document before creating more content.");
            var id = Guid.NewGuid().ToString("N");
            documents.Add(id, new(owner, identity, html.ToArray(), ++ordinal));
            return new(id, ProviderDocumentReference.RestrictedHtml);
        }
    }
    internal IReadOnlyList<string> Resolve(BrokerWidgetIdentity identity, ProviderDocumentReference reference)
    {
        lock (gate)
        {
            if (!reference.IsWellFormed() || !documents.TryGetValue(reference.Id, out var entry) || entry.Identity != identity)
                throw new BrokerException("attribution_unavailable", "Provider document is unavailable.");
            return entry.Html;
        }
    }
    internal void Discard(object owner, ProviderDocumentReference reference)
    {
        lock (gate)
            if (documents.TryGetValue(reference.Id, out var entry) && ReferenceEquals(entry.Owner, owner)) documents.Remove(reference.Id);
    }
    internal void Revoke(object owner) => Clear(owner, false);
    internal void Retire(object owner) => Clear(owner, true);
    private void Clear(object owner, bool retire)
    {
        lock (gate)
        {
            var state = lifetimes.GetValue(owner, _ => new());
            state.Epoch = new(); state.Retired |= retire;
            foreach (var key in documents.Where(pair => ReferenceEquals(pair.Value.Owner, owner)).Select(pair => pair.Key).ToArray()) documents.Remove(key);
        }
    }
    internal static bool ValidHtml(IReadOnlyList<string>? values) => ProviderDocumentContent.IsValid(values);
}
