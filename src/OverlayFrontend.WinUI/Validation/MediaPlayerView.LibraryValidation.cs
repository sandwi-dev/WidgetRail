using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal async Task ValidateLibraryPlaybackAsync(string output, string shortVideo, List<string> checks)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        var provider = Path.Combine(directory, "provider"); Directory.CreateDirectory(provider);
        var longVideo = Path.Combine(provider, "synthetic-two-hours.mp4");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        if (!File.Exists(longVideo)) throw new InvalidOperationException("Provide the synthetic two-hour MP4 in the fixture provider directory.");
        var identity = new BrokerWidgetIdentity("widgetrail.tests.playback", "widgetrail.tests", "native");
        var owner = new object();
        using var registry = new WindowCaptureRegistry();
        using var library = new CaptureLibraryStore(Path.Combine(directory, "library"), Path.Combine(directory, "owned"),
            WindowsCaptureImageCodec.CreateContextAsync, WindowsCaptureImageCodec.CreateThumbnailAsync,
            WindowsCaptureImageCodec.InspectAsync, WindowsFileOperations.RecycleAsync);
        await Progress("Opening linked original");
        var sourceGrant = await library.AddSourceAsync(provider, "Synthetic recordings", identity, token);
        var linked = (await library.LinkAsync(new(sourceGrant.Id, Path.GetFileName(longVideo)), identity, token)).Item;
        Check(linked.Media.DurationSeconds >= 7199 && linked.Media.Properties is null, "Real decoder inspects two-hour MP4 without capture encoder telemetry");
        var lease = await library.OpenAsync(linked.Id, CaptureImageResolution.Original, token);
        var attachment = registry.AdoptLibraryVideo(owner, identity, lease, token);
        var resolvedFile = registry.Resolve(identity, attachment.Id);
        Check(Path.GetFullPath(resolvedFile.Path) == longVideo && !Directory.EnumerateFiles(Path.Combine(directory, "owned")).Any(),
            "Playback retains exact linked original without a full-recording copy");
        var firstExpiry = resolvedFile.LeaseExpiresAtUnixMilliseconds;
        ApplyLease(attachment); SetActive(true, true);
        await Wait(() => player?.PlaybackSession.NaturalDuration.TotalSeconds >= 7199);
        Check(!IsPlaying && player!.IsMuted && position.Maximum >= 7199, "Two-hour native player opens muted and exposes its full timeline");
        var originalPlayer = player;
        player!.PlaybackSession.Position = TimeSpan.FromHours(1);
        await Wait(() => player.PlaybackSession.Position.TotalSeconds >= 3599);
        Toggle(); await Wait(() => IsPlaying && player.PlaybackSession.Position.TotalSeconds > 3600.05);
        Check(true, "Native decoder seeks into the second hour and plays");
        await Wait(() => resolved?.ExpiresAtUnixMilliseconds > firstExpiry);
        Check(ReferenceEquals(player, originalPlayer) && IsPlaying, "Periodic authority renewal retains the same active decoder");
        await Progress("Revoking linked source");
        await library.RemoveSourceAsync(sourceGrant.Id, identity, token);
        await Wait(() => player is null);
        Check(File.Exists(longVideo), "Source disconnect closes playback without removing original");

        await Progress("Releasing owned preview before recycle");
        var nativeMedia = await WindowsCaptureImageCodec.InspectAsync(Path.GetFullPath(shortVideo),
            new(Guid.NewGuid().ToString("N"), "video/mp4", 1, 1, new FileInfo(shortVideo).Length, 1, long.MaxValue) { Origin = CaptureAttachmentOrigin.SavedLibrary }, token);
        var owned = await library.SaveAsync(new(nativeMedia, Path.GetFullPath(shortVideo)), identity, token);
        var ownedLease = await library.OpenAsync(owned.Id, CaptureImageResolution.Original, token);
        var ownedAttachment = registry.AdoptLibraryVideo(owner, identity, ownedLease, token);
        var ownedPath = registry.Resolve(identity, ownedAttachment.Id).Path;
        ApplyLease(ownedAttachment); SetActive(true, true);
        await Wait(() => player?.PlaybackSession.NaturalDuration.TotalSeconds > 4.9);
        Toggle(); await Wait(() => IsPlaying);
        ReleasePlayer();
        registry.Discard(owner, ownedAttachment.Id, PlatformCapabilities.CaptureLibraryReadV1);
        var recycled = await library.RecycleAsync(new(new CaptureLibraryMutationReference[] { new(owned.Id, owned.Revision, owned.MetadataRevision) }), token);
        Check(recycled.Results.Single().ErrorCode is null && !File.Exists(ownedPath), "Disposing native decoder and releasing broker lease permits immediate Windows recycle without a fixed delay");
        Check(File.Exists(longVideo) && File.Exists(shortVideo), "Validation leaves both synthetic provider originals intact");

        void ApplyLease(CaptureAttachment value)
        {
            var definition = MediaPlayerDefinition.FromCapture(value);
            Apply(_ =>
            {
                var current = registry.Resolve(identity, value.Id);
                return Task.FromResult(new HostMediaPlayerSource(definition, new Uri(current.Path).AbsoluteUri, false,
                    current.Attachment.Width, current.Attachment.Height, current.Attachment.DurationSeconds,
                    current.LeaseExpiresAtUnixMilliseconds ?? current.Attachment.ExpiresAtUnixMilliseconds, RequiresRevalidation: true));
            }, definition, () => true);
        }
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        async Task Wait(Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (!condition()) await Task.Delay(30, timeout.Token);
        }
        Task Progress(string phase) => File.WriteAllTextAsync(Path.Combine(directory, "progress.json"), JsonSerializer.Serialize(new { phase, checks }), token);
    }
}
