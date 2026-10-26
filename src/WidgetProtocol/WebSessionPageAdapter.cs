using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WebSessionPageAdapterMode>))]
public enum WebSessionPageAdapterMode { IsolatedPageActions, SessionBackend }

/// <summary>A sealed package adapter scoped to exact HTTPS origins.</summary>
public sealed record WebSessionPageAdapter
{
    public required string ScriptAsset { get; init; }
    /// <summary>SessionBackend runs in the document world, accepts correlated host requests
    /// in the background, and cannot publish unsolicited native actions or observe
    /// network responses. Full-trust only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WebSessionPageAdapterMode Mode { get; init; } = WebSessionPageAdapterMode.IsolatedPageActions;
    public IReadOnlyList<string> Origins { get; init; } = [];
    /// <summary>Optional observation of matching responses already requested by the website.</summary>
    public IReadOnlyList<WebSessionResponseRule> Responses { get; init; } = [];
    public bool IsWellFormed() => Enum.IsDefined(Mode) && WebSessionContract.IsAssetPath(ScriptAsset) &&
        Origins is { Count: > 0 and <= 8 } && Origins.All(IsExactOrigin) &&
        Origins.Distinct(StringComparer.Ordinal).Count() == Origins.Count &&
        Responses is { Count: <= 8 } && Responses.All(rule => rule?.IsWellFormed() == true && Origins.Contains(rule.Origin)) &&
        (Mode != WebSessionPageAdapterMode.SessionBackend || Responses.Count == 0);
    public static bool IsExactOrigin(string? value) => value is not null &&
        !value.Contains('*') && WebSessionContract.IsNetworkOrigin(value);
    public bool AllowsPage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.UserInfo.Length == 0 && Origins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal);
}

public sealed record WebSessionResponseRule(string Origin, string PathPrefix)
{
    public const int MaximumResponseBytes = 1024 * 1024;
    public int MaximumBytes { get; init; } = 256 * 1024;
    public bool IsWellFormed() => WebSessionPageAdapter.IsExactOrigin(Origin) &&
        PathPrefix is { Length: > 1 and <= 160 } && PathPrefix.StartsWith('/') && PathPrefix.EndsWith('/') &&
        PathPrefix.Split('/').All(segment => segment is not ("." or "..") &&
            segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) &&
        !PathPrefix.Contains("//") && MaximumBytes is > 0 and <= MaximumResponseBytes;
    public bool Matches(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.UserInfo.Length == 0 && uri.GetLeftPart(UriPartial.Authority) == Origin &&
        uri.AbsolutePath.StartsWith(PathPrefix, StringComparison.Ordinal);
}
