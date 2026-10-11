using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public static class WidgetCaptureLibraryCapabilities
{
    public const string ReadPermission = "media.capture-library.read.v1";
    public const string SavePermission = "media.capture-library.save.v1";
    public const string ManagePermission = "media.capture-library.manage.v1";
    public static WidgetCapabilityOperation<CaptureLibraryListRequest, CaptureLibraryPage> List { get; } = new(ReadPermission, "capture-library.list");
    public static WidgetCapabilityOperation<CaptureLibraryOpenRequest, CaptureAttachment> Open { get; } = new(ReadPermission, "capture-library.open");
    public static WidgetCapabilityOperation<CaptureLibraryThumbnailRequest, CaptureLibraryThumbnailChunk> Thumbnail { get; } = new(ReadPermission, "capture-library.thumbnail");
    public static WidgetCapabilityOperation<CaptureReadRequest, CaptureReadChunk> Read { get; } = new(ReadPermission, "capture-library.read");
    public static WidgetCapabilityOperation<CaptureAttachmentRequest, WidgetCapabilityAcknowledgement> Release { get; } = new(ReadPermission, "capture-library.release");
    public static WidgetCapabilityOperation<CaptureLibrarySaveRequest, CaptureLibraryItem> Save { get; } = new(SavePermission, "capture-library.save");
    public static WidgetCapabilityOperation<CaptureLibraryReference, WidgetCapabilityAcknowledgement> Delete { get; } = new(ManagePermission, "capture-library.delete");
    public static WidgetCapabilityOperation<CaptureLibraryReference, CaptureLibraryExport> Export { get; } = new(ManagePermission, "capture-library.export");
}

/// <summary>Explicitly saved media shared by the user's widgets, with separate permissions.</summary>
public sealed class WidgetCaptureLibraryService
{
    private readonly IWidgetCapabilityClient client;
    internal WidgetCaptureLibraryService(IWidgetCapabilityClient client) => this.client = client;
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
        if (offset is < 0 or > CaptureLibraryLimits.MaximumItems) throw new ArgumentOutOfRangeException(nameof(offset));
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.List, new(offset), cancellationToken).ConfigureAwait(false);
        if (!CaptureLibraryLimits.Valid(result) || result.Offset != offset) throw Malformed();
        return result;
    }
    /// <summary>Creates a temporary owned copy. Context resolution reduces images to 1920x1080 without changing the saved original.</summary>
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
        var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Save, new(attachment), cancellationToken).ConfigureAwait(false);
        if (!CaptureLibraryLimits.Valid(result)) throw Malformed();
        return result;
    }
    public async ValueTask DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        DemandId(id); var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Delete, new(id), cancellationToken).ConfigureAwait(false);
        if (result?.Acknowledged != true) throw Malformed();
    }
    public async ValueTask<CaptureLibraryExport> ExportAsync(string id, CancellationToken cancellationToken = default)
    {
        DemandId(id); var result = await client.InvokeAsync(WidgetCaptureLibraryCapabilities.Export, new(id), cancellationToken).ConfigureAwait(false);
        if (result is null || result.ByteLength is <= 0 or > CaptureLimits.MaximumAttachmentBytes || result.FileName is not { Length: > 0 and <= 120 } ||
            result.FileName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.') || result.FileName.Contains("..", StringComparison.Ordinal) ||
            !result.FileName.EndsWith(".png", StringComparison.Ordinal) && !result.FileName.EndsWith(".mp4", StringComparison.Ordinal)) throw Malformed();
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
