using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureImageResolution>))]
public enum CaptureImageResolution { Context = 0, Original = 1 }
public sealed record CaptureLibraryListRequest(int Offset = 0);
public sealed record CaptureLibraryReference(string Id);
public sealed record CaptureLibrarySaveRequest(CaptureAttachment Attachment);
public sealed record CaptureLibraryOpenRequest(string Id, CaptureImageResolution Resolution = CaptureImageResolution.Original);
public sealed record CaptureLibraryItem(string Id, CaptureAttachment Media, long SavedAtUnixMilliseconds, bool Available = true);
public sealed record CaptureLibraryPage(IReadOnlyList<CaptureLibraryItem> Items, int Offset, int TotalCount, long StoredBytes);
public sealed record CaptureLibraryExport(string FileName, long ByteLength);

public static class CaptureLibraryLimits
{
    public const int PageSize = 20;
    public const int MaximumItems = 200;
    public const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    public static bool Valid(CaptureLibraryItem? item) => item is not null && CaptureLimits.ValidToken(item.Id) &&
        item.Media is not null && item.Media.IsWellFormed() && item.SavedAtUnixMilliseconds is > 0 and <= 253402300799999;
    public static bool Valid(CaptureLibraryPage? page) => page is not null && page.Items is not null &&
        page.Items.Count <= PageSize && page.Items.All(Valid) && page.Items.Select(i => i.Id).Distinct().Count() == page.Items.Count &&
        page.Offset is >= 0 and <= MaximumItems && page.TotalCount is >= 0 and <= MaximumItems &&
        page.StoredBytes is >= 0 and <= 128L * 1024 * 1024 * MaximumItems * 4 && page.Items.Count <= Math.Max(0, page.TotalCount - page.Offset);
}
