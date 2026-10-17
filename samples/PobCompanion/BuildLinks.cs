namespace WidgetRail.Samples.PobCompanion;

/// <summary>Translate supported share links to bounded PoB-code downloads, never arbitrary URLs.</summary>
internal static class BuildLinks
{
    internal static Uri DownloadUri(Uri uri, int game)
    {
        // poe.ninja documents its character and PoB APIs as internal and unavailable
        // for third-party use. Reject before BuildDocument starts any HTTP request.
        if (uri.Host == "poe.ninja")
            throw new IOException("Direct poe.ninja imports are unavailable. Open poe.ninja in your browser, copy the build's PoB code, then choose Paste build from clipboard.");
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new IOException("Use an HTTPS pobb.in share link, paste a PoB code, or choose a build file.");
        var path = uri.AbsolutePath.Trim('/').Split('/');
        if (uri.Host == "pobb.in" && uri.Query.Length == 0 && path.Length == 1 && Key(path[0]))
            return new("https://pobb.in/pob/" + path[0]);
        throw new IOException("Use a pobb.in share link, paste a PoB code, or choose a build file.");
    }

    private static bool Key(string value) => value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
