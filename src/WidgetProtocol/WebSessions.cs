using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<WebSessionSourceKind>))]
public enum WebSessionSourceKind { Remote, Package }

public sealed record WebSessionSource
{
    public required WebSessionSourceKind Kind { get; init; }
    public required string Location { get; init; }
    public IReadOnlyList<EmbeddedMediaResource> Resources { get; init; } = [];
    public IReadOnlyList<string> NetworkOrigins { get; init; } = [];
    public bool AllowAutoplay { get; init; }

    public static WebSessionSource Remote(string url) => new() { Kind = WebSessionSourceKind.Remote, Location = url };
    public static WebSessionSource Package(string entryAsset, IReadOnlyList<EmbeddedMediaResource> resources) =>
        new() { Kind = WebSessionSourceKind.Package, Location = entryAsset, Resources = resources };

    public bool IsWellFormed() => Enum.IsDefined(Kind) && Resources is not null && NetworkOrigins is not null &&
        (Kind == WebSessionSourceKind.Remote
            ? WebBrowserDocument.IsWebUrl(Location) && Resources.Count == 0 && NetworkOrigins.Count == 0 && !AllowAutoplay
            : WebSessionContract.IsAssetPath(Location) && Resources.Count is > 0 and <= 16 &&
              Resources.All(r => r is not null && WebSessionContract.IsAssetPath(r.Path) &&
                  r.ContentType is "text/html" or "text/javascript" or "application/javascript" or "text/css" or
                      "application/json" or "image/png" or "image/jpeg" or "image/webp" or "image/svg+xml" or "font/woff2") &&
              Resources.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Resources.Count &&
              Resources.Any(r => r.Path == Location && r.ContentType == "text/html") &&
              NetworkOrigins.Count <= 24 && NetworkOrigins.All(WebSessionContract.IsNetworkOrigin));
}

public sealed record WebSessionDefinition(string Key, string AccessibleName, WebSessionSource Source)
{
    public WebBrowserOptions Browser { get; init; } = new()
    { Chrome = BrowserChrome.OnDemand, Background = BrowserBackgroundMode.KeepAlive };
    public bool IsWellFormed() => WebSessionContract.IsKey(Key) &&
        AccessibleName is { Length: > 0 and <= 160 } && !string.IsNullOrWhiteSpace(AccessibleName) &&
        !AccessibleName.Any(char.IsControl) && Source?.IsWellFormed() == true && Browser?.IsWellFormed() == true;
}

/// <summary>Opaque, worker-lifetime reference. It carries no browser data or credentials.</summary>
public sealed record WebSessionReference(string Id)
{
    public bool IsWellFormed() => Id is { Length: 32 } && Id.All(char.IsAsciiHexDigit);
}

[JsonConverter(typeof(JsonStringEnumConverter<WebSessionOperation>))]
public enum WebSessionOperation { Open, PostMessage, ReadMessages, AuthorizeResource, Navigate, Close }

public sealed record WebSessionCommand(WebSessionOperation Operation)
{
    public WebSessionDefinition? Definition { get; init; }
    public WebSessionReference? Session { get; init; }
    public string? MessageBase64 { get; init; }
    public string? ResourceUrl { get; init; }
    public string? Destination { get; init; }
}

public sealed record WebSessionReply
{
    public WebSessionReference? Session { get; init; }
    public IReadOnlyList<string> MessagesBase64 { get; init; } = [];
}

public static class WebSessionContract
{
    public const int MaximumMessageBytes = 64 * 1024;
    // Two base64 messages plus their envelopes fit the 256 KiB broker frame.
    public const int MaximumMessagesPerBatch = 2;
    // Preserve small-event burst tolerance; worst-case decoded storage is 4 MiB.
    public const int MaximumQueuedMessages = 64;
    public static bool IsKey(string? key) => key is { Length: > 0 and <= 64 } &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    public static bool IsAssetPath(string? path) => path is { Length: > 0 and <= 160 } &&
        !path.StartsWith('/') && path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not ".." &&
            segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'));
    public static bool IsNetworkOrigin(string? value)
    {
        if (value is not { Length: > 0 and <= 256 }) return false;
        var normalized = value.StartsWith("https://*.", StringComparison.Ordinal) ? "https://" + value[10..] : value;
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            uri.IsDefaultPort && uri.HostNameType == UriHostNameType.Dns && uri.Host.Contains('.') &&
            !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) && !IPAddress.TryParse(uri.Host, out _) &&
            normalized == uri.GetLeftPart(UriPartial.Authority);
    }
    public static bool IsLocalResource(string? value) => value is { Length: > 0 and <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "http" &&
        uri.Host == "127.0.0.1" && uri.Port is >= 1024 and <= 65535 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        !value.Any(char.IsControl);
    public static byte[] DecodeMessage(string value)
    {
        if (value is null || value.Length > ((MaximumMessageBytes + 2) / 3) * 4)
            throw new ArgumentException("Web session message exceeds its bound.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch (FormatException error) { throw new ArgumentException("Invalid web session message.", error); }
        if (bytes.Length is 0 or > MaximumMessageBytes) throw new ArgumentException("Invalid web session message size.");
        try { using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 16 }); }
        catch (JsonException error) { throw new ArgumentException("Invalid web session JSON.", error); }
        return bytes;
    }
}
