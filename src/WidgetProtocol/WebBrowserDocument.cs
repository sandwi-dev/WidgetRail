namespace WidgetRail.WidgetProtocol;

/// <summary>
/// A host-owned browser session. Reuse Id across compatible presentations and
/// advance NavigationId only for a new authored navigation. Browser history and
/// page contents stay inside the host, never in widget IPC.
/// </summary>
public sealed record WebBrowserDocument(string Id, long NavigationId, string Url, string AccessibleName)
{
    public const long MaximumNavigationId = 9_007_199_254_740_991;
    public bool IsWellFormed() => ProtocolValidationIdentifierContext.IsSafeIdentifier(Id) &&
        NavigationId is >= 1 and <= MaximumNavigationId && IsWebUrl(Url) &&
        AccessibleName is { Length: > 0 and <= 256 } && !string.IsNullOrWhiteSpace(AccessibleName) &&
        !AccessibleName.Any(char.IsControl);

    /// <summary>Only ordinary HTTP(S) navigation; never a file, command or OS protocol.</summary>
    public static bool IsWebUrl(string? value) => value is { Length: > 0 and <= 2048 } && value == value.Trim() &&
        !value.Any(char.IsControl) && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host);
}
