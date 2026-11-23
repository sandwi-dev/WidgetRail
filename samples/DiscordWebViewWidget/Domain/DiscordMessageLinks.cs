namespace WidgetRail.Samples.DiscordWebView.Domain;

internal static class DiscordMessageLinks
{
    // Discord can unfurl a text-fragment link using its document URL. Associate
    // that metadata with one authored destination without rewriting the URL that
    // the user will open. No redirects, scraping or general URL canonicalization.
    internal static string? EmbedTarget(string embedUrl,IEnumerable<string> authoredUrls)
    {
        if(!DiscordMessageText.WebUrl(embedUrl))return null;
        var links=authoredUrls.Where(DiscordMessageText.WebUrl).Distinct(StringComparer.Ordinal).ToArray();
        if(links.Contains(embedUrl,StringComparer.Ordinal))return embedUrl;
        var candidates=links.Where(url=>WithoutTextDirective(url)==embedUrl).Take(2).ToArray();
        return candidates.Length switch {0=>embedUrl,1=>candidates[0],_=>null};
    }

    private static string WithoutTextDirective(string url)
    {
        var fragment=url.IndexOf('#');
        if(fragment<0)return url;
        var directive=url.IndexOf(":~:text=",fragment+1,StringComparison.Ordinal);
        if(directive<0||directive+8>=url.Length)return url;
        // An element anchor before the directive is meaningful and remains part
        // of the comparison. Encoded markers and unrelated hash routes stay exact.
        return url[..(directive==fragment+1?fragment:directive)];
    }
}
