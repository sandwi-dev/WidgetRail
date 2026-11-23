namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record SteamAnnouncement(string Id, string Title, DateTimeOffset Published, string Text, string? Artwork)
{
    internal string Excerpt { get; } = FirstExcerpt(Text, Title);

    private static string FirstExcerpt(string body, string title)
    {
        using var reader = new StringReader(body);
        while (reader.ReadLine() is { } line)
        {
            var text = line.Trim().TrimStart('•', '-', ' ').Trim();
            if (text.Length == 0 || text.Equals(title, StringComparison.OrdinalIgnoreCase) ||
                text.TrimEnd(':').Equals("Patch notes", StringComparison.OrdinalIgnoreCase)) continue;
            for (var i = 0; i < Math.Min(200, text.Length - 1); i++)
                if (text[i] is '.' or '?' or '!' && char.IsWhiteSpace(text[i + 1])) return text[..(i + 1)];
            if (text.Length <= 200) return text;
            var cut = text.LastIndexOf(' ', 199);
            return text[..(cut > 0 ? cut : char.IsHighSurrogate(text[199]) ? 199 : 200)] + "…";
        }
        return "";
    }
}
internal sealed record SteamAnnouncements(uint AppId, IReadOnlyList<SteamAnnouncement> Items)
{
    internal string Archive => $"https://store.steampowered.com/news/app/{AppId}";
    internal string Original(SteamAnnouncement article) => $"{Archive}/view/{article.Id}";
}
