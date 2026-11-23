using System;
using System.Collections.Generic;
using System.Globalization;

namespace WidgetRail.Playnite;

public static class SteamGameIdentity
{
    // SteamLibrary passes this identifier to LibraryPluginBase in PlayniteExtensions.
    private static readonly Guid SteamPlugin = new Guid("cb91dfc9-b977-43bf-8e70-55f46e410fab");

    public static uint? Resolve(Guid pluginId, string? gameId, IEnumerable<string?>? links)
    {
        if (pluginId == SteamPlugin) return ParseId(gameId);
        if (links == null) return null;
        uint? result = null;
        var count = 0;
        foreach (var link in links)
        {
            if (++count > 64) return null;
            if (link == null || link.Length > 2048 || !Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.IdnHost != "store.steampowered.com" ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0) continue;
            var path = uri.AbsolutePath.Split('/');
            if (path.Length < 3 || path[1] != "app" || ParseId(path[2]) is not uint candidate) continue;
            if (result.HasValue && result != candidate) return null;
            result = candidate;
        }
        return result;
    }

    private static uint? ParseId(string? value) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 &&
        id.ToString(CultureInfo.InvariantCulture) == value ? id : (uint?)null;
}
