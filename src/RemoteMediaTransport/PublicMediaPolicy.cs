using System.Net;
using System.Net.Sockets;

namespace WidgetRail.RemoteMediaTransport;

/// <summary>Anonymous public HTTPS media, optionally restricted to exact origins.</summary>
public sealed class PublicMediaPolicy
{
    private readonly HashSet<string>? origins;
    public static PublicMediaPolicy PublicHttps { get; } = new();
    /// <summary>Pin anonymous acquisition, including redirects, to this source's exact HTTPS origin.</summary>
    public static PublicMediaPolicy SameOrigin(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsPublicHttpsUri(source)) throw new PublicMediaException(PublicMediaFailure.DestinationRejected);
        return new(new[] { Origin(source) });
    }

    public PublicMediaPolicy(IEnumerable<string>? allowedOrigins = null)
    {
        if (allowedOrigins is null) return;
        origins = new(StringComparer.Ordinal);
        foreach (var value in allowedOrigins)
        {
            if (origins.Count >= 16 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !IsPublicHttpsUri(uri) || uri.AbsolutePath != "/" || uri.Query.Length != 0 ||
                value != Origin(uri)) throw new ArgumentException("Media origins must be canonical HTTPS origins.", nameof(allowedOrigins));
            if (!origins.Add(value)) throw new ArgumentException("Media origins must be distinct.", nameof(allowedOrigins));
        }
        if (origins.Count == 0) throw new ArgumentException("At least one media origin is required.", nameof(allowedOrigins));
    }

    public bool Allows(Uri uri) => IsPublicHttpsUri(uri) && (origins is null || origins.Contains(Origin(uri)));

    internal void Demand(Uri uri)
    {
        if (!Allows(uri)) throw new PublicMediaException(PublicMediaFailure.DestinationRejected);
    }

    internal static bool IsPublicHttpsUri(Uri uri) => uri.IsAbsoluteUri && uri.OriginalString.Length <= 8192 &&
        !uri.OriginalString.Any(value => char.IsControl(value) || value == '\\') &&
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.HostNameType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6 &&
        !uri.IdnHost.EndsWith(".", StringComparison.Ordinal) &&
        (IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address) ? PublicMediaAddress.IsPublic(address) :
            uri.IdnHost.Contains('.') && !uri.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) &&
            !uri.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase));

    private static string Origin(Uri uri) => "https://" + (uri.HostNameType == UriHostNameType.IPv6
        ? "[" + uri.IdnHost.Trim('[', ']').ToLowerInvariant() + "]" : uri.IdnHost.ToLowerInvariant());
}

/// <summary>Fail-closed address policy for the actual pinned socket destination.</summary>
internal static class PublicMediaAddress
{
    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublic(address.MapToIPv4());
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var a = bytes[0]; var b = bytes[1]; var c = bytes[2];
            return a != 0 && a != 10 && a != 127 && a < 224 &&
                !(a == 100 && b is >= 64 and <= 127) && !(a == 169 && b == 254) &&
                !(a == 168 && b == 63 && c == 129 && bytes[3] == 16) && // Azure virtual host-service address.
                !(a == 172 && b is >= 16 and <= 31) &&
                !(a == 192 && (b == 168 || b == 0 && c is 0 or 2 || b == 88 && c == 99)) &&
                !(a == 198 && (b is 18 or 19 || b == 51 && c == 100)) && !(a == 203 && b == 0 && c == 113);
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId != 0) return false;
        // Only native global unicast. Excludes mapped/translation/tunnel paths
        // that could encode a private IPv4 destination, plus documentation and
        // IETF special-purpose ranges. CDN native v6 addresses remain eligible.
        return (bytes[0] & 0xe0) == 0x20 &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] < 2) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x02) &&
            !(bytes[0] == 0x3f && bytes[1] == 0xff && (bytes[2] & 0xf0) == 0);
    }
}

public enum PublicMediaFailure { DestinationRejected, Network, Timeout, ResponseRejected, TooLarge, Busy }

/// <summary>Contains no remote URL, query, response body or network exception text.</summary>
public sealed class PublicMediaException(PublicMediaFailure failure) : IOException(failure switch
{
    PublicMediaFailure.DestinationRejected => "The media destination is not permitted.",
    PublicMediaFailure.Timeout => "The media request timed out.",
    PublicMediaFailure.ResponseRejected => "The media server returned an unsupported response.",
    PublicMediaFailure.TooLarge => "The media exceeds the download limit.",
    PublicMediaFailure.Busy => "The media download limit is currently in use.",
    _ => "The media could not be downloaded."
})
{
    public PublicMediaFailure Failure { get; } = failure;
}
