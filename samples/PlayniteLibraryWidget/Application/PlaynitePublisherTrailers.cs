using System.Net;
using System.Text;
using HtmlAgilityPack;
using WidgetRail.Samples.YouTubeWidget;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlaynitePublisherTrailers
{
    internal static Uri? Website(string? source)
    {
        if (source is null || !Uri.TryCreate(source, UriKind.Absolute, out var uri) || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Scheme is not ("https" or "http")) return null;
        return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1, Fragment = "" }.Uri;
    }

    internal static string? Find(string name, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > 512 * 1024) return null;
        var title = Words(name);
        if (title.Length == 0) return null;
        var document = Parse(Encoding.UTF8.GetString(bytes.Span));
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var nodes = 0;
        foreach (var node in document.DocumentNode.Descendants())
        {
            if (++nodes > 16000) return null;
            if (node.Name is "iframe" or "a") Consider(node);
            else if (node.GetAttributeValue("data-html", "") is { Length: > 0 and <= 4096 } encoded)
            {
                // Some publisher sites store their video iframe as escaped markup.
                var fragment = Parse(WebUtility.HtmlDecode(encoded));
                foreach (var frame in fragment.DocumentNode.Descendants("iframe")) Consider(frame);
            }
        }
        return candidates.Count == 1 ? candidates.Single() : null;

        void Consider(HtmlNode node)
        {
            var label = Words(WebUtility.HtmlDecode(node.GetAttributeValue("title", node.Name == "a" ? node.InnerText : "")));
            if (!label.StartsWith(title + " ", StringComparison.Ordinal)) return;
            if (label[(title.Length + 1)..] is not ("trailer" or "release trailer" or "official trailer" or
                "official release trailer" or "launch trailer" or "official launch trailer")) return;
            var link = WebUtility.HtmlDecode(node.GetAttributeValue(node.Name == "iframe" ? "src" : "href", ""));
            if (YouTubeLinkParser.TryParse(link, out var id)) candidates.Add(id);
        }
    }

    private static HtmlDocument Parse(string html)
    {
        var document = new HtmlDocument { OptionMaxNestedChildNodes = 128 };
        document.LoadHtml(html);
        return document;
    }

    private static string Words(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) result.Append(c);
            else if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
        }
        return result.ToString().TrimEnd();
    }
}
