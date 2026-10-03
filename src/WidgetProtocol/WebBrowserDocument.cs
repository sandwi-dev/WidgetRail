using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserInteractionMode>))]
public enum BrowserInteractionMode
{
    ActivateToInteract = 1,
    InteractOnFocus = 2,
}

/// <summary>
/// A host-owned browser session. Reuse Id across compatible presentations and
/// advance NavigationId only for a new authored navigation. Browser history and
/// page contents stay inside the host, never in widget IPC.
/// </summary>
public sealed record WebBrowserDocument(string Id, long NavigationId, string Url, string AccessibleName)
{
    /// <summary>Explicit entry policy supplied by UI.WebBrowser; also defines Back behavior.</summary>
    public BrowserInteractionMode InteractionMode { get; init; }
    /// <summary>Optional host-issued provider content; widget-authored HTML is never accepted.</summary>
    public ProviderDocumentReference? ProviderDocument { get; init; }
    public const string StartPage = "about:blank";
    public const long MaximumNavigationId = 9_007_199_254_740_991;
    public bool IsWellFormed() => Enum.IsDefined(InteractionMode) && ProtocolValidationIdentifierContext.IsSafeIdentifier(Id) &&
        NavigationId is >= 1 and <= MaximumNavigationId && (Url == StartPage || IsWebUrl(Url)) &&
        AccessibleName is { Length: > 0 and <= 256 } && !string.IsNullOrWhiteSpace(AccessibleName) &&
        !AccessibleName.Any(char.IsControl) &&
        (ProviderDocument is not { } provider || provider.IsWellFormed() && Url == provider.Url);

    /// <summary>Only ordinary HTTP(S) navigation; never a file, command or OS protocol.</summary>
    public static bool IsWebUrl(string? value) => value is { Length: > 0 and <= 2048 } && value == value.Trim() &&
        !value.Any(char.IsControl) && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host);
}
