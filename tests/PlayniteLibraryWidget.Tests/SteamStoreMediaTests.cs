using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.Playnite;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Tests.PlayniteLibrary;

[TestClass]
public sealed class SteamStoreMediaTests
{
    private const string Link = "https://store.steampowered.com/app/1030300/Hollow_Knight_Silksong/";
    private const string Asset = "https://shared.akamai.steamstatic.com/photo.jpg";

    [TestMethod]
    public void CuratedTrailerLinksRequireOneSpecificVideo()
    {
        var link = new PlayniteGameLink("Official Trailer", "https://youtu.be/6XGeJwsUP9c");
        Assert.AreEqual("6XGeJwsUP9c", PlayniteTrailerPresentation.LinkedTrailer([link, link]));
        Assert.IsNull(PlayniteTrailerPresentation.LinkedTrailer([link with { Name = "YouTube channel" }]));
        Assert.IsNull(PlayniteTrailerPresentation.LinkedTrailer([link, link with { Url = "https://youtu.be/abcdefghijk" }]));
        Assert.IsNull(PlayniteTrailerPresentation.LinkedTrailer([link with { Url = "https://www.youtube.com/@TeamCherry" }]));
    }

    [TestMethod]
    public void IdentityRequiresAnAuthoritativeProviderOrAnUnambiguousStoreLink()
    {
        var steam = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        Assert.AreEqual(1030300u, SteamGameIdentity.Resolve(steam, "1030300", [Link.Replace("1030300", "101")]));
        Assert.IsNull(SteamGameIdentity.Resolve(steam, "not-an-id", [Link]));
        Assert.IsNull(SteamGameIdentity.Resolve(Guid.Empty, "1030300", []));
        Assert.AreEqual(1030300u, SteamGameIdentity.Resolve(Guid.Empty, null, [Link, Link]));
        Assert.IsNull(SteamGameIdentity.Resolve(Guid.Empty, null, [Link, Link.Replace("1030300", "101")]));
        Assert.IsNull(SteamGameIdentity.Resolve(Guid.Empty, null, [Link.Replace(".com", ".com.example")]));
        Assert.IsNull(SteamGameIdentity.Resolve(Guid.Empty, null, Enumerable.Repeat(Link, 65)));
        var links = new[] { new { url = Link } };
        Assert.AreEqual(1030300u, PlayniteBridgeClient.ParseSteamAppId(JsonSerializer.SerializeToElement(new { links })));
        Assert.IsNull(PlayniteBridgeClient.ParseSteamAppId(JsonSerializer.SerializeToElement(new { steamAppId = (uint?)null, links })));
        Assert.IsNull(PlayniteBridgeClient.ParseSteamAppId(JsonSerializer.SerializeToElement(new { links = Enumerable.Repeat(links[0], 16) })));
    }

    [TestMethod]
    public void ResponseIdentityAssetOriginOrderingAndLimitsAreIndependent()
    {
        var data = new { steam_appid = 42, movies = new[] {
            new { id = 1, thumbnail = Asset, hls_h264 = Asset + ".m3u8", highlight = false },
            new { id = 2, thumbnail = Asset, hls_h264 = Asset + ".m3u8", highlight = true },
            new { id = 3, thumbnail = Asset, hls_h264 = "https://unrelated.example/test.m3u8", highlight = true } },
            screenshots = Enumerable.Range(0, 20).Select(i => new { id = i, path_thumbnail = Asset, path_full = Asset }) };
        byte[] Json(int key) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            { [key.ToString()] = new { success = true, data } });
        Assert.AreEqual(SteamStoreMedia.Empty, SteamStoreMedia.Parse(41, Json(41)));
        Assert.AreEqual(SteamStoreMedia.Empty, SteamStoreMedia.Parse(42, Json(41)));
        var media = SteamStoreMedia.Parse(42, Json(42));
        CollectionAssert.AreEqual(new uint[] { 2, 1 }, media.Trailers.Select(t => t.Id).ToArray());
        Assert.AreEqual(12, media.Screenshots.Count);
    }

    [TestMethod]
    public void ThumbnailGalleryRequiresCompleteAndAdmissibleSources()
    {
        var first = new MediaPlayerGalleryItem("one", "One", MediaPlayerSource.PublicImage(Asset));
        var second = first with { Id = "two", PublicThumbnail = Asset };
        var gallery = new MediaPlayerDefinition(first.Source, new()) { Gallery = [first, second] };
        Assert.IsFalse(gallery.IsWellFormed());
        Assert.IsTrue((gallery with { Gallery = [first with { PublicThumbnail = Asset }, second] }).IsWellFormed());
        Assert.IsFalse((first with { PublicThumbnail = "file:///example.jpg" }).IsWellFormed());
    }
}

public sealed partial class PlayniteLibraryTests
{
    [TestMethod, Timeout(30000)]
    public async Task GameWithOnlyAnExplicitTrailerPublishesOneMatchingPrivateSession()
    {
        var host = new FakeHost(1);
        var widget = Create(host, out TestApplicationService app);
        app.DetailsHandler = async (id, token) => new((await app.ResolveSavedAsync([id], token)).Single(),
            new(id, "Game", "Other", true, false, false, null, [], [], [], 0, null)
            { Links = [new("Official trailer", "https://youtu.be/6XGeJwsUP9c")] });
        await Interactive(widget);
        try
        {
            await Ready(widget, host);
            var game = PageNodes(widget, Snapshot(widget, 170001).Root)
                .First(n => n.ActionId == PlayniteLibraryActions.DetailsOpen);
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, game.Id));
            await Bounded(widget.WhenDetailsIdleAsync(), "trailer-only details");
            var view = Snapshot(widget, 170002);
            var session = view.EmbeddedMediaSession!;
            var gallery = Nodes(view.Root).Single(n => n.Id == DetailAction + "store.gallery").MediaPlayer!;
            Assert.AreEqual(MediaPlayerSource.EmbeddedSession(session.Id, "6XGeJwsUP9c"), gallery.Gallery!.Single().Source);
            Assert.IsNull(session.PendingCommand);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(view).Count);
            Assert.IsTrue(ViewSnapshotValidator.Validate(view with { EmbeddedMediaSession = null }).Count > 0);
            Assert.IsTrue(ViewSnapshotValidator.Validate(view with { EmbeddedMediaSession = session with { Id = "unrelated" } }).Count > 0);
            await widget.OnEmbeddedMediaPlaybackEventAsync(new()
            {
                SessionId = session.Id, MediaKey = "6XGeJwsUP9c", Sequence = 1, CommandSequence = 0,
                State = EmbeddedMediaPlaybackState.Error, ErrorCode = "trailer-unavailable", PositionSeconds = 0,
                DurationSeconds = 0, Volume = 1, Muted = true,
            });
            var unavailable = Snapshot(widget, 170003);
            Assert.IsNull(unavailable.EmbeddedMediaSession);
            Assert.IsFalse(Nodes(unavailable.Root).Any(n => n.Id == DetailAction + "store.gallery"));
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
            Assert.IsNull(Snapshot(widget, 170004).EmbeddedMediaSession);
        }
        finally { await WidgetRail.WidgetSdk.WidgetTestHost.DestroyAsync(widget); }
    }

    [TestMethod, Timeout(30000)]
    public async Task SupersededStoreResponseCannotReplaceTheNewGamesGallery()
    {
        var host = new FakeHost(2);
        var widget = Create(host, out TestApplicationService app);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uint identity = 0;
        app.DetailsHandler = async (id, token) => new((await app.ResolveSavedAsync([id], token)).Single(),
            new(id, "Game", "Steam", true, false, false, null, [], [], [], 0, null) { SteamAppId = ++identity });
        app.StoreHandler = async (id, token) =>
        {
            if (id == 1)
            {
                using var registration = token.Register(() => retired.TrySetResult());
                started.SetResult();
                await release.Task;
            }
            var url = $"https://cdn.steamstatic.com/{id}";
            return new([new(id, "Release trailer", url + ".jpg", null, url + ".mpd", null, true)],
                [new(id, url + "-small.jpg", url + "-large.jpg")]);
        };
        await Interactive(widget);
        try
        {
            await Ready(widget, host);
            var games = PageNodes(widget, Snapshot(widget, 160001).Root)
                .Where(node => node.ActionId == PlayniteLibraryActions.DetailsOpen).Take(2).ToArray();
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, games[0].Id));
            await Bounded(started.Task, "first store read");
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsClose, "close"));
            await Bounded(retired.Task, "store request canceled on exit");
            await widget.OnActionAsync(new(PlayniteLibraryActions.DetailsOpen, games[1].Id));
            release.SetResult();
            await Bounded(widget.WhenDetailsIdleAsync(), "replacement store read");
            var snapshot = Snapshot(widget, 160002);
            Assert.AreEqual(0, ViewSnapshotValidator.Validate(snapshot).Count);
            var stage = Nodes(snapshot.Root).Single(node => node.Id == DetailAction + "store.gallery");
            Assert.AreEqual(DetailAction + "play", stage.Focus?.Down);
            var gallery = stage.MediaPlayer!.Gallery!;
            CollectionAssert.AreEqual(new[] { "movie-2", "image-2" }, gallery.Select(item => item.Id).ToArray());
            Assert.IsTrue(gallery[0].Source.IsAdaptiveDash);
            Assert.IsTrue(gallery[0].Options is { AutoPlay: true, Muted: true });
            Assert.AreEqual(stage.Id, snapshot.InitialFocusId);
        }
        finally { release.TrySetResult(); await WidgetRail.WidgetSdk.WidgetTestHost.DestroyAsync(widget); }
    }
}
