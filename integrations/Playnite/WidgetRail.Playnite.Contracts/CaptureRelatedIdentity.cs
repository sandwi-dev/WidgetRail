using System;
using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.Playnite;

/// <summary>Domain-owned related-item identities; the host treats these values as opaque.</summary>
public static class CaptureRelatedIdentity
{
    public const string GameKind = "playnite.game";
    public const string AchievementKind = "playnite.achievement";
    public static string Game(string profileId, Guid gameId)
    {
        if (!Guid.TryParseExact(profileId, "D", out var profile) || profile == Guid.Empty || gameId == Guid.Empty)
            throw new ArgumentException("Invalid Playnite capture identity.");
        return profile.ToString("D") + "/" + gameId.ToString("D");
    }
    public static string Achievement(string profileId, Guid gameId, string providerKey, string apiName)
    {
        if (string.IsNullOrWhiteSpace(providerKey) || providerKey.Length > 64 || string.IsNullOrWhiteSpace(apiName) || apiName.Length > 256)
            throw new ArgumentException("Invalid achievement identity.");
        return Game(profileId, gameId) + "/" + Hash(providerKey + "\0" + apiName);
    }
    public static bool TryGame(string currentProfileId, string kind, string id, out Guid gameId)
    {
        gameId = Guid.Empty;
        if (id == null || kind != GameKind && kind != AchievementKind) return false;
        var parts = id.Split('/');
        if (parts.Length != (kind == GameKind ? 2 : 3) || !Guid.TryParseExact(currentProfileId, "D", out var current) ||
            !Guid.TryParseExact(parts[0], "D", out var profile) || profile != current ||
            !Guid.TryParseExact(parts[1], "D", out gameId) || gameId == Guid.Empty) return false;
        if (kind == AchievementKind)
        {
            if (parts[2].Length != 64) return false;
            foreach (var c in parts[2]) if (c < '0' || c > '9' && c < 'a' || c > 'f') return false;
        }
        return true;
    }
    public static string Hash(string value)
    {
        using (var algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
}
