using System.Security.Cryptography;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.PlatformBroker;
public sealed partial class CaptureLibraryStore
{
    private async Task<Saved> RefreshLinkedMediaAsync(Saved saved, string path, CancellationToken token)
    {
        if (!File.Exists(path) || inspect is null) return saved;
        using var pinned = CapturePinnedFile.Open(path);
        // A known-ID open/thumbnail can beat the source watcher. It must not
        // refresh a hidden snapshot onto a different physical file and thereby
        // accidentally transfer the old exclusion to the replacement.
        if (ReleaseReplacedHidden(saved.Item.Id, pinned.Identity)) throw Error("capture_library_not_found");
        if (saved.ExternalIdentity == pinned.Identity && saved.Item.Media.ByteLength == pinned.Stream.Length) return saved;
        var media = await inspect(path, saved.Item.Media, token).ConfigureAwait(false);
        if (!media.IsWellFormed() || media.Id != saved.Item.Id || media.Origin != CaptureAttachmentOrigin.SavedLibrary ||
            media.ContentType != saved.Item.Media.ContentType || media.ByteLength != pinned.Stream.Length) throw Error("capture_library_invalid_media");
        var hash = media.ContentType == "video/mp4" ? new string('0', 64) : Convert.ToHexString(await SHA256.HashDataAsync(pinned.Stream, token).ConfigureAwait(false));
        var stamp = DateTime.FromFileTimeUtc(pinned.Identity.LastWriteTime);
        var updated = saved with { ExternalIdentity = pinned.Identity, Sha256 = hash, MediaStamp = stamp.Ticks,
            Item = saved.Item with { Media = media, Available = true, Revision = pinned.Identity.ChangeTime,
                FileModifiedAtUnixMilliseconds = FileModifiedTime(stamp) } };
        DeleteThumbnails(saved.Item.Id); WriteMetadata(updated); IndexPut(updated, media.ByteLength);
        verified.Remove(saved.Item.Id);
        return updated;
    }
    private async Task<CaptureLibraryReadLease> OpenPinnedLeaseAsync(Saved saved, CancellationToken token)
    {
        var pinned = CapturePinnedFile.Open(MediaPath(saved));
        try
        {
            if (pinned.Stream.Length != saved.Item.Media.ByteLength) throw Error("capture_library_invalid_media");
            if (saved.Item.SourceId is not null)
            {
                if (saved.ExternalIdentity is null || saved.ExternalIdentity != pinned.Identity) throw Error("capture_library_invalid_media");
            }
            else
            {
                var hash = await SHA256.HashDataAsync(pinned.Stream, token).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(saved.Sha256), hash)) throw Error("capture_library_invalid_media");
                pinned.Stream.Position = 0;
            }
            token.ThrowIfCancellationRequested();
            return new(saved.Item.Media, pinned.Stream, authority: pinned, current: () =>
                Volatile.Read(ref disposed) == 0 && File.Exists(MetadataPath(saved.Item.Id)) &&
                (saved.Item.SourceId is null || sourceGrants.TryGetValue(saved.Item.SourceId, out var grant) && SourceEnabled(grant)));
        }
        catch { pinned.Dispose(); throw; }
    }
}
