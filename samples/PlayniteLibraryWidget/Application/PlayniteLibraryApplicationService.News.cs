using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;
using WidgetRail.RemoteMediaTransport;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed partial class PlayniteLibraryApplicationService
{
    // Public announcements belong to the Steam app, independent of the local Playnite profile.
    private readonly Dictionary<uint, (DateTimeOffset Refresh, SteamAnnouncements Feed)> announcements = [];

    public async ValueTask<SteamAnnouncements> GetAnnouncementsAsync(uint appId, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        lock (announcements)
            if (announcements.TryGetValue(appId, out var cached) && cached.Refresh > now) return cached.Feed;
        var uri = new Uri(FormattableString.Invariant($"https://store.steampowered.com/feeds/news/app/{appId}/?l=english"));
        var path = Path.Combine(_state.NewsCacheDirectory, appId.ToString(CultureInfo.InvariantCulture) + ".rss");
        SteamAnnouncements? lastRead = null;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length is > 0 and <= 1024 * 1024)
            {
                var bytes = new byte[checked((int)file.Length)];
                await file.ReadExactlyAsync(bytes, token);
                lastRead = ParseAnnouncements(appId, bytes);
                var refresh = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).AddHours(6);
                if (refresh > now && refresh <= now.AddHours(6))
                {
                    Remember(appId, lastRead, refresh);
                    return lastRead;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or FormatException) { }
        SteamAnnouncements feed;
        var retry = TimeSpan.FromHours(6);
        try
        {
            var result = await storeClient.DownloadAsync(uri, PublicMediaPolicy.SameOrigin(uri), 1024 * 1024, token);
            feed = ParseAnnouncements(appId, result.Bytes.ToArray());
            await StoreNewsAsync(path, result.Bytes, token);
        }
        catch (Exception error) when (error is IOException or HttpRequestException or XmlException or FormatException)
        {
            token.ThrowIfCancellationRequested();
            lock (announcements) feed = announcements.TryGetValue(appId, out var old) ? old.Feed : lastRead ?? new(appId, []);
            retry = TimeSpan.FromMinutes(10);
        }
        token.ThrowIfCancellationRequested();
        Remember(appId, feed, now + retry);
        return feed;
    }

    private void Remember(uint appId, SteamAnnouncements feed, DateTimeOffset refresh)
    {
        lock (announcements)
        {
            if (!announcements.ContainsKey(appId) && announcements.Count >= 8)
                announcements.Remove(announcements.MinBy(pair => pair.Value.Refresh).Key);
            announcements[appId] = (refresh, feed);
        }
    }

    private static async Task StoreNewsAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var directory = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await destination.WriteAsync(bytes, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            foreach (var old in directory.EnumerateFiles("*.rss").OrderByDescending(f => f.LastWriteTimeUtc).Skip(8))
                old.Delete();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static SteamAnnouncements ParseAnnouncements(uint appId, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var channel = XDocument.Load(reader).Root?.Element("channel");
        var articles = new Dictionary<string, SteamAnnouncement>(StringComparer.Ordinal);
        var prefix = FormattableString.Invariant($"https://store.steampowered.com/news/app/{appId}/view/");
        foreach (var item in channel?.Elements("item") ?? [])
        {
            var url = ((string?)item.Element("link"))?.Trim();
            if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var id = url[prefix.Length..];
            if (!ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _) || articles.ContainsKey(id)) continue;
            var title = HtmlEntity.DeEntitize(((string?)item.Element("title") ?? "").Trim());
            if (title.Length is 0 or > 256 || !DateTimeOffset.TryParse((string?)item.Element("pubDate"),
                CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) continue;
            var document = new HtmlDocument { OptionMaxNestedChildNodes = 128 };
            var html = (string?)item.Element("description") ?? "";
            if (html.Length > 100_000) continue;
            try { document.LoadHtml(html); }
            catch (ArgumentException) { continue; }
            var body = new StringBuilder();
            string? artwork = null;
            Read(document.DocumentNode);
            articles.Add(id, new(id, title, date, body.ToString().Trim(), artwork));
            if (articles.Count >= 40) break;

            void Read(HtmlNode node)
            {
                if (body.Length >= 16_000 || node.Name is "script" or "style" or "iframe" || node.NodeType == HtmlNodeType.Comment) return;
                if (node.NodeType == HtmlNodeType.Text)
                {
                    var text = HtmlEntity.DeEntitize(node.InnerText);
                    body.Append(text.AsSpan(0, Math.Min(text.Length, 16_000 - body.Length)));
                    return;
                }
                if (node.Name == "img" && artwork is null && Uri.TryCreate(node.GetAttributeValue("src", ""), UriKind.Absolute, out var img) &&
                    img.Scheme == "https" && img.UserInfo.Length == 0 && img.IsDefaultPort &&
                    (img.IdnHost.EndsWith(".steamstatic.com", StringComparison.Ordinal) || img.IdnHost.EndsWith(".steamusercontent.com", StringComparison.Ordinal)))
                    artwork = img.AbsoluteUri;
                foreach (var child in node.ChildNodes) Read(child);
                if (node.Name is "p" or "div" or "li" or "br" or "h2" or "h3" && body.Length > 0 && body[^1] != '\n') body.AppendLine();
            }
        }
        return new(appId, articles.Values.OrderByDescending(a => a.Published).ToArray());
    }
}
