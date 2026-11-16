using WidgetRail.WidgetProtocol;
namespace WidgetRail.PlatformBroker;

public sealed partial class CaptureLibraryStore
{
    internal static CaptureLibraryMutationReference[] ValidateCopy(CaptureLibraryCopyRequest request)
    {
        if (request.Items is not { Count: > 0 and <= CaptureLibraryLimits.MaximumCopyItems } ||
            request.Items.Any(item => item is null || !CaptureLimits.ValidToken(item.Id) || item.ContentRevision < 0 || item.MetadataRevision < 0) ||
            request.Items.Select(item => item.Id).Distinct().Count() != request.Items.Count) throw Error("invalid_payload");
        return request.Items.ToArray();
    }

    // Destination is chosen by trusted host UI; there is no widget operation accepting a path.
    internal async Task<CaptureLibraryBatchResult> CopyAsync(CaptureLibraryCopyRequest request, string destination, CancellationToken token)
    {
        var items = ValidateCopy(request);
        if (!Path.IsPathFullyQualified(destination) || !Directory.Exists(destination)) throw Error("capture_library_copy_destination_unavailable");
        destination = Path.GetFullPath(destination); PlainPath(destination);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(BrokerTransportLimits.CaptureCopyTimeoutSeconds));
        return await BatchAsync(items, item => item.Id, async item => { await CopyOneAsync(item, destination, deadline.Token).ConfigureAwait(false); return null; }, deadline.Token).ConfigureAwait(false);
    }

    private async Task CopyOneAsync(CaptureLibraryMutationReference reference, string destination, CancellationToken token)
    {
        CaptureLibraryReadLease? source = null;
        string? temporary = null;
        var ownsTemporary = false;
        string fileName;
        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using var ownership = Own();
                RefreshEntry(reference.Id, forceMetadata: true);
                var saved = await RefreshEditedAsync(Read(reference.Id), token).ConfigureAwait(false);
                if (saved.Item.Revision != reference.ContentRevision || saved.Item.MetadataRevision != reference.MetadataRevision)
                    throw Error("capture_library_metadata_conflict");
                fileName = Path.GetFileName(MediaPath(saved));
                if (!CaptureLibraryLimits.ValidFileName(fileName)) throw Error("capture_library_invalid_media");
                source = await VerifiedAsync(saved, token).ConfigureAwait(false);
            }
            finally { gate.Release(); }
            // The verified read handle prevents source replacement during the copy;
            // release the library lock so browsing and unrelated metadata remain responsive.
            PlainPath(destination);
            var target = Path.Combine(destination, fileName); PlainPath(target);
            if (File.Exists(target) || Directory.Exists(target)) throw Error("capture_library_copy_exists");
            temporary = Path.Combine(destination, ".widgetrail-copy-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                ownsTemporary = true;
                await source.Stream.CopyToAsync(output, 65536, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            PlainPath(destination); PlainPath(target); PlainPath(temporary);
            try { File.Move(temporary, target, overwrite: false); }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { throw Error("capture_library_copy_exists"); }
            temporary = null;
        }
        catch (UnauthorizedAccessException) { throw Error("capture_library_access_denied"); }
        finally
        {
            if (source is not null) await source.DisposeAsync().ConfigureAwait(false);
            if (ownsTemporary && temporary is not null) DeleteOwned(temporary);
        }
    }
}
