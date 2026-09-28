using System.Globalization;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

/// <summary>Closed network policy for an already admitted, sealed adapter document.</summary>
internal sealed class EmbeddedMediaRequestPolicy
{
    private readonly Dictionary<string, WidgetPresentationEmbeddedMediaResource> resources;
    private readonly HashSet<string> origins;
    private readonly string[] families;
    public string Origin { get; } = "https://wrail-media-" + Guid.NewGuid().ToString("N") + ".invalid";
    public string EntryUri { get; }
    public string? ApplicationReferer { get; }

    public EmbeddedMediaRequestPolicy(WidgetPresentationEmbeddedMediaDocument document,
        EmbeddedMediaSession declaration, string? applicationIdentity)
    {
        // The public validator owns canonical origins and the PSL-backed family check.
        // Never silently broaden its policy using a second domain parser.
        var errors = ViewSnapshotValidator.Validate(new ViewSnapshot
        {
            Sequence = 1, WidgetInstanceId = "media-policy", ActiveInputScopeId = "media-policy",
            Root = new ViewNode { Id = "media-policy", Kind = ViewNodeKind.Stack },
            EmbeddedMediaSession = declaration,
        });
        if (errors.Count != 0) throw new ArgumentException("Invalid embedded media declaration.", nameof(declaration));
        EntryUri = Origin + "/" + document.EntryAsset;
        resources = document.Resources.ToDictionary(resource => Origin + "/" + resource.Path, StringComparer.Ordinal);
        origins = new(declaration.AllowedFrameOrigins, StringComparer.Ordinal);
        families = declaration.AllowedFrameDomainFamilies.ToArray();
        ApplicationReferer = CreateApplicationReferer(applicationIdentity);
    }

    public bool IsDocument(string uri) => string.Equals(uri, EntryUri, StringComparison.Ordinal);

    public bool AllowsFrame(string uri) => IsDocument(uri) ||
        TryHttps(uri, out var parsed) && origins.Contains(parsed.GetLeftPart(UriPartial.Authority));

    public bool AllowsRemoteResource(string uri)
    {
        if (ApplicationReferer is null || !TryHttps(uri, out var parsed)) return false;
        if (origins.Contains(parsed.GetLeftPart(UriPartial.Authority))) return true;
        var host = parsed.IdnHost;
        return families.Any(family => host.Equals(family, StringComparison.Ordinal) ||
            host.EndsWith("." + family, StringComparison.Ordinal));
    }

    public MediaResourceResponse? Resolve(string uri, string method, string? range)
    {
        if (!resources.TryGetValue(uri, out var resource)) return null;
        if (!string.Equals(method, "GET", StringComparison.Ordinal)) return new(405, "Method Not Allowed", null, "Allow: GET\r\n");
        var media = resource.ContentType.StartsWith("audio/", StringComparison.Ordinal) || resource.ContentType.StartsWith("video/", StringComparison.Ordinal);
        var headers = "Content-Type: " + resource.ContentType + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n";
        if (media) headers += "Accept-Ranges: bytes\r\n";
        if (!media || string.IsNullOrEmpty(range))
            return new(200, "OK", resource.OpenRead(), headers + "Content-Length: " + resource.Length + "\r\n");
        if (!TryRange(range, resource.Length, out var start, out var length))
            return new(416, "Range Not Satisfiable", null, headers + "Content-Range: bytes */" + resource.Length + "\r\nContent-Length: 0\r\n");
        var source = resource.OpenRead();
        // Range replies are bounded by the admitted resource size; no stream references escape.
        using (source)
        {
            source.Position = start;
            var bytes = new byte[length];
            source.ReadExactly(bytes);
            return new(206, "Partial Content", new MemoryStream(bytes, writable: false), headers +
                $"Content-Length: {length}\r\nContent-Range: bytes {start}-{start + length - 1}/{resource.Length}\r\n");
        }
    }

    internal static bool TryRange(string value, int size, out int start, out int length)
    {
        start = length = 0;
        if (size <= 0 || !value.StartsWith("bytes=", StringComparison.Ordinal)) return false;
        var body = value.AsSpan(6).Trim(" \t");
        var dash = body.IndexOf('-');
        if (dash < 0 || body[(dash + 1)..].Contains('-')) return false;
        var left = body[..dash];
        var right = body[(dash + 1)..];
        static bool Parse(ReadOnlySpan<char> token, out int number) => int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        if (left.IsEmpty)
        {
            if (!Parse(right, out length) || length <= 0 || length > size) return false;
            start = size - length;
            return true;
        }
        if (!Parse(left, out start) || start >= size) return false;
        var end = size - 1;
        if (!right.IsEmpty && (!Parse(right, out end) || end < start || end >= size)) return false;
        length = end - start + 1;
        return true;
    }

    internal static string? CreateApplicationReferer(string? identity)
    {
        if (identity is null || identity.Length is < 1 or > 253) return null;
        var canonical = identity.ToLowerInvariant();
        foreach (var label in canonical.Split('.'))
            if (label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' ||
                label.Any(ch => !(ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))) return null;
        return "https://" + canonical + "/";
    }

    private static bool TryHttps(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns) return false;
        // Reject Unicode/backslash authority tricks rather than normalizing them into access.
        return !value.Contains('\\') && uri.Host.Equals(uri.IdnHost, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record MediaResourceResponse(int Status, string Reason, Stream? Content, string Headers);
