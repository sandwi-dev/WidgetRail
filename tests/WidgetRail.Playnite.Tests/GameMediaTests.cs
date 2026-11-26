using WidgetRail.Playnite;

namespace WidgetRail.Playnite.Tests;

[TestClass]
public sealed class GameMediaTests
{
    [TestMethod]
    public void GameMediaPrefersTheProfilesOwnFilesWithAnExactSteamFallback()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var game = Guid.NewGuid();
            var profile = Path.Combine(temp.FullName, "profile");
            var steam = Path.Combine(temp.FullName, "steam");
            var cache = Directory.CreateDirectory(Path.Combine(steam, "appcache", "librarycache", "123", "deadbeef"));
            var steamLogo = Path.Combine(cache.FullName, "logo.png");
            File.WriteAllBytes(steamLogo, [1]);
            Assert.AreEqual(steamLogo, PlayniteGameMedia.Find(profile, game, steam, 123).Logo);
            Assert.IsNull(PlayniteGameMedia.Find(profile, game, steam, 124).Logo);
            Assert.IsFalse(Directory.Exists(profile));

            var media = Directory.CreateDirectory(Path.Combine(profile, "ExtraMetadata", "games", game.ToString("D")));
            foreach (var file in new[] { "Logo.png", "VideoTrailer.mp4", "VideoMicrotrailer.mp4" })
                File.WriteAllBytes(Path.Combine(media.FullName, file), [1, 2]);
            var found = PlayniteGameMedia.Find(profile, game, steam, 123);
            Assert.AreEqual(Path.Combine(media.FullName, "Logo.png"), found.Logo);
            Assert.AreEqual(Path.Combine(media.FullName, "VideoTrailer.mp4"), found.Trailer);
            Assert.AreEqual(Path.Combine(media.FullName, "VideoMicrotrailer.mp4"), found.Microtrailer);
            Assert.IsNull(PlayniteGameMedia.Find(profile, Guid.NewGuid(), steam, 123).Trailer);
            Assert.IsNull(PlayniteGameMedia.Find(steam, game, null, null).Logo);
            File.WriteAllBytes(found.Logo!, []);
            Assert.AreEqual(steamLogo, PlayniteGameMedia.Find(profile, game, steam, 123).Logo);
        }
        finally { temp.Delete(true); }
    }
}
