namespace WidgetRail.Samples.PobCompanion;

/// <summary>Translate public share/profile links to bounded PoB-code downloads, never arbitrary URLs.</summary>
internal static class BuildLinks
{
    internal static Uri DownloadUri(Uri uri, int game)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new IOException("Use an HTTPS pobb.in or poe.ninja build link, or paste its PoB code.");
        var path = uri.AbsolutePath.Trim('/').Split('/');
        if (uri.Host == "pobb.in" && uri.Query.Length == 0 && path.Length == 1 && Key(path[0]))
            return new("https://pobb.in/pob/" + path[0]);
        if (uri.Host != "poe.ninja") throw new IOException("Use a pobb.in or poe.ninja build link, or paste its PoB code.");
        var linkedGame = path.FirstOrDefault() switch { "poe1" => 1, "poe2" => 2, _ => 1 };
        if (linkedGame != game) throw new IOException($"This link is for PoE{linkedGame}. Open it in PoE{linkedGame} Path of Building.");
        if (path[0] is "poe1" or "poe2") path = path[1..];
        if (path.Length == 2 && path[0] == "pob" && Key(path[1]) && uri.Query.Length == 0)
            return new($"https://poe.ninja/poe{game}/pob/raw/{path[1]}");
        // The public profile export uses account, league and character identity.
        if (path.Length == 5 && path[0] == "profile" && path[3] == "character" &&
            Segment(path[1]) && Segment(path[2]) && Segment(path[4]) && uri.Query.Length == 0)
            return new($"https://poe.ninja/poe{game}/pob/raw/profile/code/{path[1]}/{path[2]}/{path[4]}");
        throw new IOException("Use a poe.ninja character profile or PoB share link. For other pages, copy the PoB code and choose Paste build from clipboard.");
    }

    private static bool Key(string value) => value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool Segment(string value)
    {
        var decoded = Uri.UnescapeDataString(value);
        return decoded.Length is > 0 and <= 160 && decoded is not "." and not ".." &&
            !decoded.Any(c => char.IsControl(c) || c is '/' or '\\' or '?' or '#' or '%');
    }
}
