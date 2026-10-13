using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureImageResolution>))]
public enum CaptureImageResolution { Context = 0, Original = 1 }
public sealed record CaptureLibraryListRequest(int Offset = 0, bool Refresh = false);
public sealed record CaptureLibraryReference(string Id);
public sealed record CaptureLibrarySaveRequest(CaptureAttachment Attachment);
public sealed record CaptureLibraryOpenRequest(string Id, CaptureImageResolution Resolution = CaptureImageResolution.Original);
public sealed record CaptureLibraryItem(string Id, CaptureAttachment Media, long SavedAtUnixMilliseconds, bool Available = true)
{
    /// <summary>Opaque content revision for invalidating derived artwork after an external edit.</summary>
    public long Revision { get; init; }
}
public sealed record CaptureLibraryPage(IReadOnlyList<CaptureLibraryItem> Items, int Offset, int TotalCount, long StoredBytes);
public sealed record CaptureLibraryExport(string FileName, long ByteLength);
public sealed record CaptureLibraryThumbnailRequest(string Id, int Offset = 0);
public sealed record CaptureLibraryThumbnailChunk(int Width, int Height, int TotalBytes, int Offset, byte[] Bytes);
/// <summary>A small derived PNG for lazy artwork. Never an original or temporary capture reference.</summary>
public sealed record CaptureLibraryThumbnail(int Width, int Height, byte[] Bytes)
{
    public bool IsWellFormed() => Width is > 0 and <= CaptureLibraryLimits.ThumbnailWidth &&
        Height is > 0 and <= CaptureLibraryLimits.ThumbnailHeight && Bytes is { Length: >= 24 and <= CaptureLibraryLimits.MaximumThumbnailBytes } &&
        Bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(Bytes.AsSpan(16, 4)) == Width &&
        System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(Bytes.AsSpan(20, 4)) == Height;
}

public static class CaptureLibraryLimits
{
    public const int PageSize = 20;
    public const int ThumbnailWidth = 256;
    public const int ThumbnailHeight = 144;
    public const int MaximumThumbnailBytes = 160 * 1024;
    public static bool Valid(CaptureLibraryItem? item) => item is not null && CaptureLimits.ValidToken(item.Id) &&
        item.Media is not null && item.Media.IsWellFormed() && item.Revision >= 0 && item.SavedAtUnixMilliseconds is > 0 and <= 253402300799999;
    public static bool Valid(CaptureLibraryPage? page) => page is not null && page.Items is not null &&
        page.Items.Count <= PageSize && page.Items.All(Valid) && page.Items.Select(i => i.Id).Distinct().Count() == page.Items.Count &&
        page.Offset >= 0 && page.TotalCount >= 0 && page.StoredBytes >= 0 &&
        page.Items.Count <= Math.Max(0, page.TotalCount - page.Offset);
}
