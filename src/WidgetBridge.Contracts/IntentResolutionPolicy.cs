using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetBridge;

internal sealed record IntentHandlerCandidate(string WidgetId, long Generation,
    CompiledWidgetIntentContract Contract, bool Enabled);

internal enum IntentResolutionKind
{
    Widget,
    ChooseHandler,
    ExternalBrowser,
    Unavailable,
    InvalidPayload,
    SchemaConflict,
    InvalidCatalog,
}

internal sealed record IntentResolution(IntentResolutionKind Kind,
    IReadOnlyList<IntentHandlerCandidate> Candidates);

/// <summary>
/// Pure selection policy for an already-admitted caller and catalog. Does not grant
/// consent, activate a window or deliver an intent. The dispatcher must revalidate
/// caller authority and the returned handler generation before any effect.
/// </summary>
internal static class IntentResolutionPolicy
{
    internal const int MaximumCandidates = 256;

    internal static IntentResolution Resolve(CompiledWidgetIntentContract requested,
        JsonElement payload, IReadOnlyList<IntentHandlerCandidate> catalog,
        string? preferredWidgetId = null)
    {
        if (!requested.Accepts(payload)) return Result(IntentResolutionKind.InvalidPayload);
        if (catalog.Count > MaximumCandidates) return Result(IntentResolutionKind.InvalidCatalog);
        if (requested.Id == WidgetIntentContracts.OpenWebPage && !IsWebUrl(payload))
            return Result(IntentResolutionKind.InvalidPayload);

        var matches = new List<IntentHandlerCandidate>();
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in catalog)
        {
            if (candidate is null || candidate.Contract is null) return Result(IntentResolutionKind.InvalidCatalog);
            if (!candidate.Enabled || candidate.Contract.Id != requested.Id || candidate.Contract.Version != requested.Version) continue;
            if (!WidgetManifestValidator.IsValidPackageIdentity(candidate.WidgetId) || candidate.Generation < 1 || !owners.Add(candidate.WidgetId))
                return Result(IntentResolutionKind.InvalidCatalog);
            if (candidate.Contract.SchemaDigest != requested.SchemaDigest) return Result(IntentResolutionKind.SchemaConflict);
            matches.Add(candidate);
        }
        matches.Sort((a, b) => StringComparer.Ordinal.Compare(a.WidgetId, b.WidgetId));
        if (matches.FirstOrDefault(candidate => candidate.WidgetId == preferredWidgetId) is { } preferred)
            return Result(IntentResolutionKind.Widget, preferred);
        if (matches.Count == 1) return Result(IntentResolutionKind.Widget, matches[0]);
        if (matches.Count > 1) return Result(IntentResolutionKind.ChooseHandler, matches.ToArray());

        // Only this built-in standard contract can fall back to a shell URL launch.
        var web = CompiledWidgetIntentContract.Create(WidgetIntentContracts.Web);
        return Result(requested.Id == web.Id && requested.Version == web.Version && requested.SchemaDigest == web.SchemaDigest
            ? IntentResolutionKind.ExternalBrowser : IntentResolutionKind.Unavailable);
    }

    private static bool IsWebUrl(JsonElement payload) =>
        payload.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String &&
        url.GetString() is { } text && !text.Any(char.IsControl) && text == text.Trim() &&
        Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host);

    private static IntentResolution Result(IntentResolutionKind kind, params IntentHandlerCandidate[] candidates) =>
        new(kind, Array.AsReadOnly(candidates));
}
