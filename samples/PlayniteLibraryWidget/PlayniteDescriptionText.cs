using System.Net;
using System.Text.RegularExpressions;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteDescriptionText
{
    internal static string? Normalize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var bounded = html[..Math.Min(html.Length, 65536)];
        try
        {
            bounded = Regex.Replace(bounded, @"<(script|style)\b[^>]*>.*?</\1\s*>", " ",
                RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
            bounded = Regex.Replace(bounded, @"<[^>]*>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            bounded = WebUtility.HtmlDecode(bounded);
            bounded = string.Join(' ', bounded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            bounded = new string(bounded.Where(c => !char.IsControl(c)).ToArray());
            if (bounded.Length == 0) return null;
            if (bounded.Length > 4096) bounded = bounded[..4095].TrimEnd() + "\u2026";
            return bounded;
        }
        catch (RegexMatchTimeoutException) { return null; }
    }
}
