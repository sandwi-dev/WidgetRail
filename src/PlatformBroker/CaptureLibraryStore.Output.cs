using System.Security.Cryptography;
using System.Text.Json;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    public Task<CaptureLibraryOutputSession> ReserveVideoOutputAsync(BrokerWidgetIdentity actor, long maximumBytes,
        double maximumDurationSeconds = 120, CancellationToken cancellationToken = default)
        =>ReserveVideoOutputCoreAsync(actor,maximumBytes,maximumDurationSeconds,cancellationToken,false);
    internal Task<CaptureLibraryOutputSession> ReserveDraftVideoOutputAsync(BrokerWidgetIdentity actor,long maximumBytes,
        double maximumDurationSeconds,CancellationToken token)
        =>ReserveVideoOutputCoreAsync(actor,maximumBytes,maximumDurationSeconds,token,true);
    private async Task<CaptureLibraryOutputSession> ReserveVideoOutputCoreAsync(BrokerWidgetIdentity actor,long maximumBytes,
        double maximumDurationSeconds,CancellationToken cancellationToken,bool draft)
    {
        actor.Validate();
        if (maximumBytes < 1024 || maximumBytes > (draft ? CaptureLimits.MaximumLibraryRecordingBytes : 256L * 1024 * 1024) || !double.IsFinite(maximumDurationSeconds) ||
            maximumDurationSeconds <= 0 || maximumDurationSeconds > CaptureLibraryLimits.MaximumVideoSeconds) throw Error("invalid_payload");
        RetainEditor(); FileStream? outputLock = null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(editStop.Token, cancellationToken);
        try
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                using var ownership = Own();
                PlainPath(EditDirectory); Directory.CreateDirectory(EditDirectory);
                outputLock = AcquireEditLock(); RecoverEditsOwned(); DemandFreeSpace(maximumBytes);
                var id = Guid.NewGuid().ToString("N");
                var path = EditTemporary(id, "video/mp4");
                var directory = System.IO.Path.GetDirectoryName(path)!; PlainPath(directory);
                if (!Directory.Exists(directory))
                { Directory.CreateDirectory(directory); File.SetAttributes(directory, File.GetAttributes(directory) | FileAttributes.Hidden); }
                File.WriteAllText(EditJournalPath(id), JsonSerializer.Serialize(new EditJournal(id, "video/mp4",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Standalone: true)));
                if(draft)
                {
                    var retained=new FileStream(EditOutputLockPath(id),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
                    outputLock.Dispose();outputLock=retained;
                }
                return new(this, id, path, actor, maximumBytes, maximumDurationSeconds, outputLock, editStop.Token, ReleaseEditor);
            }
            finally { gate.Release(); }
        }
        catch { outputLock?.Dispose(); ReleaseEditor(); throw; }
    }

    internal async Task<CaptureLibraryItem> CommitOutputAsync(CaptureLibraryOutputSession session, CaptureAttachment media,
        long capturedAt, Func<bool> isAuthorized, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(isAuthorized);
        void Current()
        {
            token.ThrowIfCancellationRequested();
            if (!session.IsCurrent || !isAuthorized()) throw Error("permission_denied");
        }
        Current();
        if (!media.IsWellFormed() || media.Id != session.Id || media.ContentType != "video/mp4" ||
            media.Origin != CaptureAttachmentOrigin.SavedLibrary || media.ByteLength > session.MaximumBytes ||
            !CaptureContextLimits.Timestamp(capturedAt)) throw Error("invalid_payload");
        PlainPath(session.Path);
        if (inspect is null) throw Error("capture_library_codec_unavailable");
        var inspected = await inspect(session.Path, media, token).ConfigureAwait(false);
        if (!inspected.IsWellFormed() || inspected.Id != media.Id || inspected.ContentType != "video/mp4" ||
            inspected.Origin != CaptureAttachmentOrigin.SavedLibrary || inspected.DurationSeconds > session.MaximumDurationSeconds ||
            inspected.Width != media.Width || inspected.Height != media.Height ||
            inspected.ByteLength != media.ByteLength || Math.Abs(inspected.DurationSeconds - media.DurationSeconds) > .25)
            throw Error("capture_library_invalid_media");
        media = media with { DurationSeconds = inspected.DurationSeconds };
        string hash;
        await using (var stream = new FileStream(session.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
        {
            if (stream.Length != media.ByteLength) throw Error("capture_library_invalid_media");
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        }
        await gate.WaitAsync(token).ConfigureAwait(false);
        Saved? saved = null;
        try
        {
            using var ownership = Own(); Current(); DemandFreeSpace(0);
            var journal = ReadEditJournal(EditJournalPath(session.Id));
            if (!journal.Standalone) throw Error("capture_library_invalid_media");
            var stamp = File.GetLastWriteTimeUtc(session.Path).Ticks;
            var item = new CaptureLibraryItem(session.Id, media with { ExpiresAtUnixMilliseconds = long.MaxValue }, journal.SavedAt)
            { Origin = OriginFor(session.Actor), CapturedAtUnixMilliseconds = capturedAt, Revision = stamp };
            saved = new(2, item, hash, PublicName(item), stamp);
            var pending = MetadataPath(item.Id) + ".pending";
            File.WriteAllText(pending, JsonSerializer.Serialize(saved));
            Current();
            File.Move(session.Path, MediaPath(saved), overwrite: false);
            session.RecoveryRequired=true;
            File.Move(pending, MetadataPath(item.Id), overwrite: false);
            session.Published = true;
            session.RecoveryRequired=false;
            IndexPut(saved, media.ByteLength);
            DeleteOwned(EditJournalPath(item.Id));
            return ItemView(saved);
        }
        catch
        {
            if (!session.Published && session.RecoveryRequired && saved is not null)
            {
                // A failed metadata publication must not destroy an unsaved
                // recording. Restore its exact stage; cleanup retains the
                // journal if a file handle prevents this rollback.
                File.Move(MediaPath(saved),session.Path,overwrite:false);
                session.RecoveryRequired=false;
            }
            throw;
        }
        finally { gate.Release(); }
    }

    internal void RetireOutput(CaptureLibraryOutputSession session)
    {
        // Metadata publication is durable. If indexing failed afterward, retain
        // its journal for the same recovery path used by copy/edit publication.
        if (session.Published || session.RecoveryRequired) return;
        // A locked worker output leaves its journal for the existing startup recovery.
        if (!DeleteOwned(session.Path)) return;
        if (!DeleteOwned(MetadataPath(session.Id) + ".pending")) return;
        DeleteOwned(EditJournalPath(session.Id));
    }
    private string EditOutputLockPath(string id)=>Path.Combine(EditDirectory,id+".lock");
    internal void ReleaseOutputLock(string id)=>DeleteOwned(EditOutputLockPath(id));
}
