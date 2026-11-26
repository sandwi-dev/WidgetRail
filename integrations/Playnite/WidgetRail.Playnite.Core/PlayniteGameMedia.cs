using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace WidgetRail.Playnite;

/// <summary>Optional, existing media for one library record. Discovery never creates or modifies files.</summary>
public sealed class PlayniteGameMedia
{
    public string? Logo { get; private set; }
    public string? LogoRevision { get; private set; }
    public string? Trailer { get; private set; }
    public string? Microtrailer { get; private set; }

    public static PlayniteGameMedia Find(string profile, Guid game, string? steamRoot, uint? steamId)
    {
        var result = new PlayniteGameMedia();
        if (game == Guid.Empty) return result;
        var folder = new DirectoryInfo(Path.Combine(profile, "ExtraMetadata", "games", game.ToString("D")));
        if (IsLocalDirectory(folder))
        {
            result.Logo = FileIn(folder, "Logo.png");
            result.Trailer = FileIn(folder, "VideoTrailer.mp4");
            result.Microtrailer = FileIn(folder, "VideoMicrotrailer.mp4");
        }
        if (result.Logo == null && !string.IsNullOrWhiteSpace(steamRoot) && steamId is > 0)
        {
            try
            {
                var cache = new DirectoryInfo(Path.Combine(steamRoot, "appcache", "librarycache", steamId.Value.ToString(CultureInfo.InvariantCulture)));
                if (IsLocalDirectory(cache)) result.Logo = SteamLogo(cache);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        if (result.Logo is { } logo)
        {
            try
            {
                var file = new FileInfo(logo);
                result.LogoRevision = file.LastWriteTimeUtc.Ticks.ToString("x", CultureInfo.InvariantCulture) +
                    "-" + file.Length.ToString("x", CultureInfo.InvariantCulture);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { result.Logo = null; }
        }
        return result;
    }

    private static string? SteamLogo(DirectoryInfo cache)
    {
        var direct = FileIn(cache, "logo_2x.png") ?? FileIn(cache, "logo.png");
        if (direct != null) return direct;
        // Some Steam versions keep named assets in a hash directory below the app ID.
        // Avoid a recursive search or choosing a variant by enumeration order.
        var folders = cache.EnumerateDirectories().Take(65).ToArray();
        if (folders.Length > 64) return null;
        return folders.Where(folder => folder.Name.Length is >= 8 and <= 64 && folder.Name.All(Uri.IsHexDigit) && IsLocalDirectory(folder))
            .Select(folder => FileIn(folder, "logo_2x.png") ?? FileIn(folder, "logo.png")).Where(path => path != null)
            .OrderByDescending(path => new FileInfo(path!).Length).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static bool IsLocalDirectory(DirectoryInfo folder)
    {
        try
        {
            var path = folder.FullName;
            if (path.Length < 3 || !char.IsLetter(path[0]) || path[1] != ':' || !folder.Exists) return false;
            for (DirectoryInfo? node = folder; node != null; node = node.Parent)
                if ((node.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string? FileIn(DirectoryInfo folder, string name)
    {
        try
        {
            var file = new FileInfo(Path.Combine(folder.FullName, name));
            return file.Exists && (file.Attributes & FileAttributes.ReparsePoint) == 0 && file.Length > 0 ? file.FullName : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
