using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.PlatformBroker;

// Host-only metadata, separate from widget-writable private state. Approved
// replacements share app-library IDs and registrations, never security identity.
internal sealed class AppLibraryContinuityStore(string consentRoot)
{
    private readonly PrivateStateStore _store = new(Path.Combine(consentRoot, "app-library-scopes"));

    internal async Task<BrokerWidgetIdentity> ResolveAsync(BrokerWidgetIdentity identity, CancellationToken token)
    {
        var snapshot = await _store.ReadAsync(identity, token).ConfigureAwait(false);
        if (!snapshot.Exists) return identity;
        try
        {
            var scope = JsonSerializer.Deserialize<Scope>(Convert.FromBase64String(snapshot.CanonicalJsonBase64!),
                BrokerJson.StrictOptions);
            if (scope is null || scope.Version != 1)
                throw new JsonException("Invalid app-library scope.");
            var resolved = identity with { PublisherId = scope.PublisherId };
            resolved.Validate();
            return resolved;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw new BrokerException("invalid_app_library_scope", "App library continuity data is invalid.", exception);
        }
    }

    internal async Task KeepAsync(BrokerWidgetIdentity source, BrokerWidgetIdentity destination, CancellationToken token,
        bool destinationHasState = false)
    {
        source.Validate();
        destination.Validate();
        if (source.PackageId != destination.PackageId)
            throw new BrokerException("invalid_payload", "App library continuity requires the same widget.");
        if (source.PublisherId == destination.PublisherId) return;
        // A retained destination already has its own data namespace. Reusing it
        // after rollback must not reinterpret its saved references.
        var existing = await _store.ReadAsync(destination, token).ConfigureAwait(false);
        if (existing.Exists)
        {
            _ = await ResolveAsync(destination, token).ConfigureAwait(false);
            return;
        }
        var origin = destinationHasState ? destination : await ResolveAsync(source, token).ConfigureAwait(false);
        var json = PrivateStateJsonCodec.CanonicalBase64(
            JsonSerializer.SerializeToUtf8Bytes(new Scope(1, origin.PublisherId), BrokerJson.StrictOptions), "invalid_app_library_scope");
        await _store.WriteAsync(destination, new WritePrivateStateRequest(json, existing.Revision), token).ConfigureAwait(false);
    }

    private sealed record Scope([property: JsonRequired] int Version, [property: JsonRequired] string PublisherId);
}
