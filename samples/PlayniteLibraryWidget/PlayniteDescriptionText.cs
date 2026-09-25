using System.Net;
using System.Text.RegularExpressions;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteDescriptionText
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    // Bounded plain-text conversion, not HTML rendering. Keep structural breaks;
    // inline markup must not introduce spaces inside otherwise continuous words.
    internal static string? Normalize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var bounded = html[..Math.Min(html.Length, 65536)];
        try
        {
            var hasMarkup = Regex.IsMatch(bounded, @"</?[a-z][^>]*>",
                RegexOptions.IgnoreCase, MatchTimeout);
            if (hasMarkup) bounded = bounded.Replace('\r', ' ').Replace('\n', ' ');
            bounded = Replace(bounded, @"<(script|style)\b[^>]*>.*?</\1\s*>", "");
            bounded = Replace(bounded, @"<!--.*?-->", "");
            bounded = Replace(bounded, @"<br\b[^>]*>", "\n");
            bounded = Replace(bounded, @"</?(?:p|div|h[1-6]|section|article|blockquote|ul|ol|table|tr)\b[^>]*>", "\n\n");
            bounded = Replace(bounded, @"<li\b[^>]*>", "\n• ");
            bounded = Replace(bounded, @"</li\s*>", "\n");
            bounded = Replace(bounded, @"</t[dh]\s*>", " ");
            bounded = Replace(bounded, @"<[^>]*>", "");
            bounded = WebUtility.HtmlDecode(bounded).Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = bounded.Split('\n').Select(line => string.Join(' ',
                line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
            bounded = string.Join('\n', lines);
            bounded = Regex.Replace(bounded, @"\n{3,}", "\n\n", RegexOptions.None, MatchTimeout).Trim();
            bounded = new string(bounded.Where(c => c == '\n' || !char.IsControl(c)).ToArray());
            if (bounded.Length == 0) return null;
            if (bounded.Length > 4096)
            {
                var length = char.IsHighSurrogate(bounded[4094]) ? 4094 : 4095;
                bounded = bounded[..length].TrimEnd() + "\u2026";
            }
            return bounded;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    private static string Replace(string value, string pattern, string replacement) =>
        Regex.Replace(value, pattern, replacement,
            RegexOptions.IgnoreCase | RegexOptions.Singleline, MatchTimeout);
}
