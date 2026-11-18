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
    public string? Title { get; init; }
    public bool IsFavorite { get; init; }
    public long MetadataRevision { get; init; }
    public string? FileName { get; init; }
    public CaptureContextOrigin? Origin { get; init; }
    public long? CapturedAtUnixMilliseconds { get; init; }
    public int ContextCount { get; init; }
    /// <summary>Non-null for read-only media in a user-granted external folder.</summary>
    public string? SourceId { get; init; }
    /// <summary>Filesystem modification time for linked originals; never asserted to be actual capture time.</summary>
    public long? FileModifiedAtUnixMilliseconds { get; init; }
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

[JsonConverter(typeof(JsonStringEnumConverter<CaptureLibraryMediaKind>))]
public enum CaptureLibraryMediaKind { All, Screenshot, Video }
[JsonConverter(typeof(JsonStringEnumConverter<CaptureLibrarySort>))]
public enum CaptureLibrarySort { Newest, Oldest, Title, Application, Largest, Longest }
/// <summary>Library-wide metadata query; never reads or searches original media contents.</summary>
public sealed record CaptureLibraryQuery
{
    public string? Search { get; init; }
    public string? Application { get; init; }
    /// <summary>Null selects all folders; empty selects WidgetRail-owned captures; otherwise an opaque linked-source ID.</summary>
    public string? SourceId { get; init; }
    public CaptureLibraryMediaKind MediaKind { get; init; }
    public bool FavoritesOnly { get; init; }
    /// <summary>Select hidden linked captures instead of the normal visible library. Known-ID reads retain their existing authorization.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HiddenOnly { get; init; }
    public long? FromUnixMilliseconds { get; init; }
    public long? ToUnixMilliseconds { get; init; }
    public CaptureLibrarySort Sort { get; init; }
    public string? OriginPackageId { get; init; }
    public CaptureLibraryReferenceFilter? Reference { get; init; }
    public bool IsWellFormed() => Enum.IsDefined(MediaKind) && Enum.IsDefined(Sort) &&
        (SourceId is null or "" || CaptureLimits.ValidToken(SourceId)) &&
        (OriginPackageId is null || CaptureContextLimits.Identity(OriginPackageId)) &&
        (Reference is null || OriginPackageId is not null && Reference.IsWellFormed()) &&
        CaptureLibraryLimits.ValidText(Search, 160) && CaptureLibraryLimits.ValidText(Application, 120) &&
        FromUnixMilliseconds is null or >= 0 and <= 253402300799999 && ToUnixMilliseconds is null or >= 0 and <= 253402300799999 &&
        !(FromUnixMilliseconds > ToUnixMilliseconds);
}
/// <summary>Revision pins every range to one library ordering; mismatch requires a fresh query.</summary>
public sealed record CaptureLibraryQueryRequest(CaptureLibraryQuery Query, int Offset = 0, int Count = 40, string? Revision = null);
public sealed record CaptureLibraryQueryPage(IReadOnlyList<CaptureLibraryItem> Items, int Offset, int TotalCount, long StoredBytes, string Revision);
/// <summary>Ordering proof for the same query: only favorite-only metadata edits may retain it. FavoritesOnly uses the full query revision.</summary>
public sealed record CaptureLibraryContinuityPage(CaptureLibraryQueryPage Page, string OrderingRevision);
public sealed record CaptureLibraryApplicationsRequest(CaptureLibraryQuery Query, string? Search = null, int Limit = 128);
/// <summary>An empty name means a capture has no known source application.</summary>
public sealed record CaptureLibraryApplication(string Name, int Count);
public sealed record CaptureLibraryApplications(IReadOnlyList<CaptureLibraryApplication> Items, bool HasMore);
public sealed record CaptureLibraryFoldersRequest(CaptureLibraryQuery Query, string? Revision = null);
/// <summary>Read-only library facet. Empty ID means WidgetRail-owned captures. Label is presentation data, never file authority.</summary>
public sealed record CaptureLibraryFolder(string SourceId, string Label, int Count);
public sealed record CaptureLibraryFolders(IReadOnlyList<CaptureLibraryFolder> Items, string Revision);
public sealed record CaptureLibraryGroupsRequest(CaptureLibraryQuery Query, string? Revision = null);
/// <summary>Local-calendar month key (yyyy-MM), or older/newer for a bounded remainder. Labels belong to the widget.</summary>
public sealed record CaptureLibraryGroup(string Key, int Count);
public sealed record CaptureLibraryGroups(IReadOnlyList<CaptureLibraryGroup> Items, int TotalCount, string Revision);
public sealed record CaptureLibraryFindRequest(CaptureLibraryQuery Query, string Id, string? Revision = null);
public sealed record CaptureLibraryPosition(int? Index, int TotalCount, string Revision);
public sealed record CaptureLibraryMetadataRequest(string Id, long ExpectedRevision, string? Title, bool IsFavorite);
/// <summary>Coalesced invalidation. Query again to obtain the durable ordering revision.</summary>
public sealed record CaptureLibraryChanged(long Generation);
public sealed record CaptureLibraryBatchMetadataRequest(IReadOnlyList<CaptureLibraryMetadataRequest> Items);
public sealed record CaptureLibraryMutationReference(string Id, long ContentRevision, long MetadataRevision);
/// <summary>Copy verified originals to one user-picked destination; widgets never provide paths.</summary>
public sealed record CaptureLibraryCopyRequest(IReadOnlyList<CaptureLibraryMutationReference> Items);
public sealed record CaptureLibraryRecycleRequest(IReadOnlyList<CaptureLibraryMutationReference> Items);
public sealed record CaptureLibraryMutationResult(string Id, CaptureLibraryItem? Item = null, string? ErrorCode = null);
public sealed record CaptureLibraryBatchResult(IReadOnlyList<CaptureLibraryMutationResult> Results);

public static class CaptureLibraryLimits
{
    /// <summary>Existing videos only; capture creation keeps CaptureLimits budgets.</summary>
    public const long MaximumVideoBytes = 64L * 1024 * 1024 * 1024;
    public const int MaximumVideoSeconds = 8 * 60 * 60;
    public const int PageSize = 20;
    public const int MaximumQueryItems = 64;
    public const int MaximumCopyItems = 256;
    public static bool ValidText(string? text, int maximum) => text is null || text.Length <= maximum && !text.Any(char.IsControl);
    public static bool ValidFileName(string? name) => name is null || name.Length is > 0 and <= 240 &&
        !name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '"' or '<' or '>' or '|' or '?' or '*');
    public static bool ValidRevision(string? revision) => revision is { Length: > 0 and <= 64 } && revision.All(c => char.IsAsciiHexDigit(c) || c == '-');
    public const int ThumbnailWidth = 256;
    public const int ThumbnailHeight = 144;
    public const int MaximumThumbnailBytes = 160 * 1024;
    public static bool Valid(CaptureLibraryItem? item) => item is not null && CaptureLimits.ValidToken(item.Id) &&
        item.Media is not null && item.Media.IsWellFormed() && item.Revision >= 0 && item.MetadataRevision >= 0 &&
        ValidText(item.Title, 160) && ValidFileName(item.FileName) && item.ContextCount >= 0 && (item.SourceId is null || CaptureLimits.ValidToken(item.SourceId)) &&
        (item.Origin is null || item.Origin.IsWellFormed()) && CaptureContextLimits.Timestamp(item.CapturedAtUnixMilliseconds) &&
        CaptureContextLimits.Timestamp(item.FileModifiedAtUnixMilliseconds) && item.SavedAtUnixMilliseconds is > 0 and <= 253402300799999;
    public static bool Valid(CaptureLibraryPage? page) => page is not null && page.Items is not null &&
        page.Items.Count <= PageSize && page.Items.All(Valid) && page.Items.Select(i => i.Id).Distinct().Count() == page.Items.Count &&
        page.Offset >= 0 && page.TotalCount >= 0 && page.StoredBytes >= 0 &&
        page.Items.Count <= Math.Max(0, page.TotalCount - page.Offset);
}
