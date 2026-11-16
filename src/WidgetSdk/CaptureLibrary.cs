using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetCaptureLibraryCapabilities
{
    public const string ReadPermission = "media.capture-library.read.v1";
    public const string SavePermission = "media.capture-library.save.v1";
    public const string ManagePermission = "media.capture-library.manage.v1";
    public static WidgetCapabilityOperation<CaptureLibraryReference, CaptureLibraryItem> GetMetadata { get; } = new(ReadPermission, "capture-library.get-metadata");
    public static WidgetCapabilityOperation<CaptureLibraryQueryRequest, CaptureLibraryQueryPage> Query { get; } = new(ReadPermission, "capture-library.query");
    public static WidgetCapabilityOperation<CaptureLibraryApplicationsRequest, CaptureLibraryApplications> Applications { get; } = new(ReadPermission, "capture-library.applications");
    public static WidgetCapabilityOperation<CaptureLibraryGroupsRequest, CaptureLibraryGroups> Groups { get; } = new(ReadPermission, "capture-library.groups");
    public static WidgetCapabilityOperation<CaptureLibraryFindRequest, CaptureLibraryPosition> Find { get; } = new(ReadPermission, "capture-library.find");
    public static WidgetCapabilityOperation<CaptureLibraryMetadataRequest, CaptureLibraryItem> UpdateMetadata { get; } = new(ManagePermission, "capture-library.metadata");
    public static WidgetCapabilityOperation<CaptureLibraryBatchMetadataRequest, CaptureLibraryBatchResult> UpdateMetadataBatch { get; } = new(ManagePermission, "capture-library.metadata-batch");
    public static WidgetCapabilityOperation<CaptureLibraryRecycleRequest, CaptureLibraryBatchResult> Recycle { get; } = new(ManagePermission, "capture-library.recycle");
    public static WidgetCapabilityOperation<CaptureLibraryCopyRequest, CaptureLibraryBatchResult> Copy { get; } = new(ManagePermission, "capture-library.copy");
    public static WidgetCapabilityOperation<CaptureLibraryMutationReference, WidgetCapabilityAcknowledgement> RemoveUnavailable { get; } = new(ManagePermission, "capture-library.remove-unavailable");
    public static WidgetCapabilityOperation<CaptureLibraryReference, WidgetCapabilityAcknowledgement> Reveal { get; } = new(ManagePermission, "capture-library.reveal");
    public static WidgetCapabilityEvent<CaptureLibraryChanged> Changed { get; } = new(ReadPermission, "capture-library.changed");
    public static WidgetCapabilityOperation<CaptureLibraryListRequest, CaptureLibraryPage> List { get; } = new(ReadPermission, "capture-library.list");
    public static WidgetCapabilityOperation<CaptureLibraryOpenRequest, CaptureAttachment> Open { get; } = new(ReadPermission, "capture-library.open");
    public static WidgetCapabilityOperation<CaptureLibraryThumbnailRequest, CaptureLibraryThumbnailChunk> Thumbnail { get; } = new(ReadPermission, "capture-library.thumbnail");
    public static WidgetCapabilityOperation<CaptureReadRequest, CaptureReadChunk> Read { get; } = new(ReadPermission, "capture-library.read");
    public static WidgetCapabilityOperation<CaptureAttachmentRequest, WidgetCapabilityAcknowledgement> Release { get; } = new(ReadPermission, "capture-library.release");
    public static WidgetCapabilityOperation<CaptureLibrarySaveWithContextRequest, CaptureLibraryContextSaveResult> SaveWithContext { get; } = new(SavePermission, "capture-library.save-context");
    public static WidgetCapabilityOperation<CaptureLibraryContextsRequest, CaptureLibraryContextsPage> GetContexts { get; } = new(ReadPermission, "capture-library.contexts");
    public static WidgetCapabilityOperation<CaptureLibrarySaveRequest, CaptureLibraryItem> Save { get; } = new(SavePermission, "capture-library.save");
    public static WidgetCapabilityOperation<CaptureLibrarySaveRequest, CaptureLibraryItem> SaveCurrent { get; } = new(SavePermission, "capture-library.save-current");
    public static WidgetCapabilityOperation<CaptureLibraryReference, WidgetCapabilityAcknowledgement> Delete { get; } = new(ManagePermission, "capture-library.delete");
    public static WidgetCapabilityOperation<CaptureLibraryReference, CaptureLibraryExport> Export { get; } = new(ManagePermission, "capture-library.export");
    public static WidgetCapabilityOperation<WidgetCapabilityQuery, WidgetCapabilityAcknowledgement> OpenFolder { get; } = new(ManagePermission, "capture-library.open-folder");
}

/// <summary>Explicitly saved media shared by the user's widgets, with separate permissions.</summary>
public sealed partial class WidgetCaptureLibraryService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetCaptureLibraryService(IWidgetCapabilityClient client) => this.client = client;
    /// <summary>Subscribe before querying to avoid missing changes during the initial read. Events invalidate metadata, never contain media.</summary>
    public ValueTask<IWidgetCapabilitySubscription<CaptureLibraryChanged>> OpenChangesAsync(CancellationToken cancellationToken = default) =>
        client.OpenSubscriptionAsync(WidgetCaptureLibraryCapabilities.Changed, cancellationToken);

    /// <summary>Returns a bounded range from the complete indexed query. Reuse its revision for later ranges; a changed query must be refreshed.</summary>
    public async ValueTask<CaptureLibraryQueryPage> QueryAsync(CaptureLibraryQuery query, int offset = 0, int count = 40,
        string? revision = null, CancellationToken cancellationToken = default)
    {
        if (query?.IsWellFormed() != true || offset < 0 || count is < 1 or > CaptureLibraryLimits.MaximumQueryItems ||
            revision is not null && !CaptureLibraryLimits.ValidRevision(revision)) throw new ArgumentException("Invalid capture library query.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Query, new(query, offset, count, revision), cancellationToken).ConfigureAwait(false);
        if (result is null || result.Offset != offset || result.TotalCount < 0 || result.StoredBytes < 0 ||
            !CaptureLibraryLimits.ValidRevision(result.Revision) || revision is not null && result.Revision != revision ||
            result.Items is null || result.Items.Count > count || result.Items.Count > Math.Max(0, result.TotalCount - offset) ||
            result.Items.Any(item => !CaptureLibraryLimits.Valid(item)) || result.Items.Select(item => item.Id).Distinct().Count() != result.Items.Count) throw Malformed();
        return result with { Items = result.Items.ToArray() };
    }

    /// <summary>Lists bounded application facets for the query, ignoring its current application selection. An empty name is Unknown application.</summary>
    public async ValueTask<CaptureLibraryApplications> ApplicationsAsync(CaptureLibraryQuery query, string? search = null, int limit = 128,
        CancellationToken cancellationToken = default)
    {
        if (query?.IsWellFormed() != true || !CaptureLibraryLimits.ValidText(search, 120) || limit is < 1 or > 128) throw new ArgumentException("Invalid capture application query.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Applications, new(query, search, limit), cancellationToken).ConfigureAwait(false);
        if (result?.Items is null || result.Items.Count > limit || result.Items.Any(item => item is null || item.Name is null || !CaptureLibraryLimits.ValidText(item.Name, 120) || item.Count <= 0) ||
            result.Items.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Items.Count) throw Malformed();
        return result with { Items = result.Items.ToArray() };
    }

    /// <summary>Returns a complete bounded month partition for Newest/Oldest ordering using the host local calendar. Widgets supply localized labels.</summary>
    public async ValueTask<CaptureLibraryGroups> GroupsAsync(CaptureLibraryQuery query, string? revision = null, CancellationToken cancellationToken = default)
    {
        if (query?.IsWellFormed() != true || query.Sort is not (CaptureLibrarySort.Newest or CaptureLibrarySort.Oldest) ||
            revision is not null && !CaptureLibraryLimits.ValidRevision(revision)) throw new ArgumentException("Invalid capture grouping query.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Groups, new(query, revision), cancellationToken).ConfigureAwait(false);
        if (result?.Items is null || result.Items.Count > 256 || result.TotalCount < 0 || !CaptureLibraryLimits.ValidRevision(result.Revision) ||
            revision is not null && result.Revision != revision || result.Items.Any(group => group is null || group.Count <= 0 || !ValidGroupKey(group.Key)) ||
            result.Items.Sum(group => (long)group.Count) != result.TotalCount || result.Items.Select(group => group.Key).Distinct().Count() != result.Items.Count) throw Malformed();
        return result with { Items = result.Items.ToArray() };
    }
    private static bool ValidGroupKey(string? key) => key is "older" or "newer" or "unknown" || key is { Length: 7 } && key[4] == '-' &&
        key.Where((_, index) => index != 4).All(char.IsAsciiDigit) && int.TryParse(key[5..], out var month) && month is >= 1 and <= 12;

    /// <summary>Locates an existing capture in a query without downloading preceding ranges; a missing/nonmatching capture has no index.</summary>
    public async ValueTask<CaptureLibraryPosition> FindAsync(CaptureLibraryQuery query, string id, string? revision = null,
        CancellationToken cancellationToken = default)
    {
        DemandId(id);
        if (query?.IsWellFormed() != true || revision is not null && !CaptureLibraryLimits.ValidRevision(revision)) throw new ArgumentException("Invalid capture library query.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Find, new(query, id, revision), cancellationToken).ConfigureAwait(false);
        if (result is null || result.TotalCount < 0 || result.Index is < 0 || result.Index >= result.TotalCount ||
            !CaptureLibraryLimits.ValidRevision(result.Revision) || revision is not null && result.Revision != revision) throw Malformed();
        return result;
    }

    /// <summary>Refreshes one capture's metadata even when it no longer matches the current gallery filters. Does not open or decode original media.</summary>
    public async ValueTask<CaptureLibraryItem> GetMetadataAsync(string id, CancellationToken cancellationToken = default)
    {
        DemandId(id);
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.GetMetadata, new(id), cancellationToken).ConfigureAwait(false);
        if (!CaptureLibraryLimits.Valid(result) || result.Id != id) throw Malformed();
        return result;
    }

    /// <summary>Updates user metadata without changing or renaming the original media. A stale metadata revision is rejected.</summary>
    public async ValueTask<CaptureLibraryItem> UpdateMetadataAsync(string id, long expectedRevision, string? title, bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        DemandId(id);
        if (expectedRevision < 0 || !CaptureLibraryLimits.ValidText(title, 160)) throw new ArgumentException("Invalid capture metadata.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.UpdateMetadata, new(id, expectedRevision, title, isFavorite), cancellationToken).ConfigureAwait(false);
        if (!CaptureLibraryLimits.Valid(result) || result.Id != id || result.MetadataRevision < expectedRevision) throw Malformed();
        return result;
    }

    /// <summary>Edits at most 64 captures, reporting each outcome; successful items are not rolled back when another fails.</summary>
    public async ValueTask<CaptureLibraryBatchResult> UpdateMetadataBatchAsync(IReadOnlyList<CaptureLibraryMetadataRequest> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var captured = items.ToArray();
        if (captured.Length is < 1 or > CaptureLibraryLimits.MaximumQueryItems || captured.Any(item => item is null || !CaptureLimits.ValidToken(item.Id) || item.ExpectedRevision < 0 || !CaptureLibraryLimits.ValidText(item.Title, 160)) ||
            captured.Select(item => item.Id).Distinct().Count() != captured.Length) throw new ArgumentException("Invalid capture metadata batch.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.UpdateMetadataBatch, new(captured), cancellationToken).ConfigureAwait(false);
        return ValidateBatch(result, captured.Select(item => item.Id).ToArray());
    }
    /// <summary>Requests recoverable removal only. An unsupported recycle destination fails instead of permanently deleting files.</summary>
    public async ValueTask<CaptureLibraryBatchResult> RecycleAsync(IReadOnlyList<CaptureLibraryMutationReference> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var captured = items.ToArray();
        if (captured.Length is < 1 or > CaptureLibraryLimits.MaximumQueryItems || captured.Any(item => item is null || !CaptureLimits.ValidToken(item.Id) || item.ContentRevision < 0 || item.MetadataRevision < 0) ||
            captured.Select(item => item.Id).Distinct().Count() != captured.Length) throw new ArgumentException("Invalid capture recycle batch.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Recycle, new(captured), cancellationToken).ConfigureAwait(false);
        return ValidateBatch(result, captured.Select(item => item.Id).ToArray());
    }
    /// <summary>Removes stale library metadata only when the referenced original is still absent. Never permanently deletes a restored file.</summary>
    public async ValueTask RemoveUnavailableAsync(CaptureLibraryMutationReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference); DemandId(reference.Id);
        if (reference.ContentRevision < 0 || reference.MetadataRevision < 0) throw new ArgumentException("Invalid unavailable capture reference.");
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.RemoveUnavailable, reference, cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }

    /// <summary>Reveals a host-resolved original in Explorer; no widget-supplied path is accepted.</summary>
    public async ValueTask RevealAsync(string id, CancellationToken cancellationToken = default)
    {
        DemandId(id);
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Reveal, new(id), cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }
    private static CaptureLibraryBatchResult ValidateBatch(CaptureLibraryBatchResult? result, IReadOnlyList<string> ids)
    {
        if (result?.Results is null || result.Results.Count != ids.Count) throw Malformed();
        for (var i = 0; i < ids.Count; i++)
        {
            var item = result.Results[i];
            if (item is null || item.Id != ids[i] || item.Item is not null && (!CaptureLibraryLimits.Valid(item.Item) || item.Item.Id != item.Id) ||
                item.ErrorCode is { } error && (error.Length is < 1 or > 96 || error.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))) throw Malformed();
        }
        return result with { Results = result.Results.ToArray() };
    }

    /// <summary>Open the saved captures folder through the host's foreground handoff. No arbitrary path is accepted.</summary>
    public async ValueTask OpenFolderAsync(CancellationToken cancellationToken = default)
    {
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.OpenFolder,new(),cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }
    /// <summary>Returns bounded PNG artwork without importing an original or consuming a temporary capture slot.</summary>
    public async ValueTask<CaptureLibraryThumbnail> GetThumbnailAsync(string id, CancellationToken cancellationToken = default)
    {
        DemandId(id);
        byte[]? bytes = null; var offset = 0; var width = 0; var height = 0;
        do
        {
            var chunk = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Thumbnail, new(id, offset), cancellationToken).ConfigureAwait(false);
            if (chunk is null || chunk.TotalBytes is < 24 or > CaptureLibraryLimits.MaximumThumbnailBytes || chunk.Offset != offset ||
                chunk.Width is <= 0 or > CaptureLibraryLimits.ThumbnailWidth || chunk.Height is <= 0 or > CaptureLibraryLimits.ThumbnailHeight ||
                chunk.Bytes is not { Length: > 0 and <= CaptureLimits.ChunkBytes } || offset + chunk.Bytes.Length > chunk.TotalBytes ||
                chunk.Bytes.Length != Math.Min(CaptureLimits.ChunkBytes, chunk.TotalBytes - offset)) throw Malformed();
            if (bytes is null) { bytes = new byte[chunk.TotalBytes]; width = chunk.Width; height = chunk.Height; }
            else if (bytes.Length != chunk.TotalBytes || width != chunk.Width || height != chunk.Height) throw Malformed();
            chunk.Bytes.CopyTo(bytes, offset); offset += chunk.Bytes.Length;
        } while (offset < bytes.Length);
        var result = new CaptureLibraryThumbnail(width, height, bytes);
        if (!result.IsWellFormed()) throw Malformed();
        return result;
    }
    public async ValueTask<CaptureLibraryPage> ListAsync(int offset = 0, CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.List, new(offset), cancellationToken).ConfigureAwait(false);
        if (!CaptureLibraryLimits.Valid(result) || result.Offset != offset) throw Malformed();
        return result;
    }
    /// <summary>Explicitly reconcile external folder changes, then return a page. Ordinary ListAsync uses the index.</summary>
    public async ValueTask<CaptureLibraryPage> RefreshAsync(int offset = 0, CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.List, new(offset, true), cancellationToken).ConfigureAwait(false);
        if (!CaptureLibraryLimits.Valid(result) || result.Offset != offset) throw Malformed();
        return result;
    }
    /// <summary>Opens an owned media reference. Videos retain a read-only original lease; context resolution reduces images to 1920x1080 without changing the saved original.</summary>
    public async ValueTask<CaptureAttachment> OpenAsync(string id, CaptureImageResolution resolution = CaptureImageResolution.Original, CancellationToken cancellationToken = default)
    {
        DemandId(id); if (!Enum.IsDefined(resolution)) throw new ArgumentOutOfRangeException(nameof(resolution));
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Open, new(id, resolution), cancellationToken).ConfigureAwait(false);
        if (result is null || !result.IsWellFormed() || result.Origin != CaptureAttachmentOrigin.SavedLibrary) throw Malformed();
        return result;
    }
    public async ValueTask<CaptureLibraryItem> SaveAsync(CaptureAttachment attachment, CancellationToken cancellationToken = default)
    {
        if (attachment is null || !attachment.IsWellFormed()) throw new ArgumentException("Invalid capture.", nameof(attachment));
        CaptureLibraryItem result;
        try { result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.SaveCurrent, new(attachment), cancellationToken).ConfigureAwait(false); }
        catch (WidgetCapabilityException error) when (error.ErrorCode is "unsupported_capability" or "unsupported_operation")
        {
            // Older brokers reject an unknown operation before any mutation.
            // Do not retry permission, cancellation, transport or host failures.
            cancellationToken.ThrowIfCancellationRequested();
            result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Save, new(attachment), cancellationToken).ConfigureAwait(false);
        }
        if (!CaptureLibraryLimits.Valid(result)) throw Malformed();
        return result;
    }
    public async ValueTask DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        DemandId(id); var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Delete, new(id), cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }
    /// <summary>Compatibility API: returns the existing public file name; no duplicate is created. Prefer OpenFolderAsync.</summary>
    public async ValueTask<CaptureLibraryExport> ExportAsync(string id, CancellationToken cancellationToken = default)
    {
        DemandId(id); var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Export, new(id), cancellationToken).ConfigureAwait(false);
        if (result is null || result.ByteLength is <= 0 or > CaptureLibraryLimits.MaximumVideoBytes || result.FileName is not { Length: > 0 and <= 240 } ||
            result.FileName.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '"' or '<' or '>' or '|' or '?' or '*') ||
            !result.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !result.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) throw Malformed();
        return result;
    }
    public async ValueTask ReleaseAsync(CaptureAttachment attachment, CancellationToken cancellationToken = default)
    {
        DemandId(attachment.Id); var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Release, new(attachment.Id), cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }
    /// <summary>Reading does not upload media. Obtain separate user consent before sending it to a provider.</summary>
    public async Task<byte[]> ReadContentAsync(CaptureAttachment attachment, CancellationToken cancellationToken = default)
    {
        if (!attachment.IsWellFormed()) throw new ArgumentException("Invalid capture.", nameof(attachment));
        using var output = new MemoryStream((int)attachment.ByteLength);
        while (output.Length < attachment.ByteLength)
        {
            var chunk = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Read, new(attachment.Id, output.Length), cancellationToken).ConfigureAwait(false);
            if (chunk is null || chunk.Offset != output.Length || chunk.Bytes is not { Length: > 0 and <= CaptureLimits.ChunkBytes } ||
                output.Length + chunk.Bytes.Length > attachment.ByteLength || chunk.EndOfFile != (output.Length + chunk.Bytes.Length == attachment.ByteLength)) throw Malformed();
            output.Write(chunk.Bytes);
        }
        return output.GetBuffer(); // Capacity is the validated exact byte length; avoid a second large image allocation.
    }
    private static void DemandId(string id) { if (!CaptureLimits.ValidToken(id)) throw new ArgumentException("Invalid saved capture reference."); }
    private static WidgetCapabilityException Malformed() => new("malformed_response", "Saved capture response is invalid.");
}
