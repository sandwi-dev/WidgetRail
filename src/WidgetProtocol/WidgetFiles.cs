namespace WidgetRail.WidgetProtocol;

/// <summary>An owner-scoped read reference. A name is display metadata, never a filesystem path.</summary>
public sealed record WidgetFileReference(string Id,string Name,string ContentType,long ByteLength,long ExpiresAtUnixMilliseconds)
{
    /// <summary>Optional broker-issued identity for the same original/version within this recipient's lifetime.
    /// Display/deduplication only; never a read grant, path, or cross-recipient content hash.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentIdentity { get; init; }
    public bool IsWellFormed()=>WidgetFileContract.Token(Id)&&WidgetFileContract.Name(Name)&&WidgetFileContract.Type(ContentType)&&
        ByteLength is >=0 and <=WidgetFileContract.MaximumFileBytes&&ExpiresAtUnixMilliseconds>0 &&
        (ContentIdentity is null || WidgetFileContract.Token(ContentIdentity));
}
public sealed record WidgetFileMetadata(string Name,string ContentType,long ByteLength)
{
    public bool IsWellFormed()=>WidgetFileContract.Name(Name)&&WidgetFileContract.Type(ContentType)&&ByteLength is >=0 and <=WidgetFileContract.MaximumFileBytes;
}
public sealed record WidgetFileChoiceRequest(string Title="Choose files",bool Multiple=true)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CommitLabel { get; init; }
    public bool IsWellFormed()=>Title is {Length:>0 and <=120}&&!Title.Any(char.IsControl) &&
        (CommitLabel is null || !string.IsNullOrWhiteSpace(CommitLabel) && CommitLabel.Length <= 32 && !CommitLabel.Any(char.IsControl));
}
public sealed record WidgetFileSelection(IReadOnlyList<WidgetFileReference> Files);
public sealed record WidgetFileIdReference(string Id);
public sealed record WidgetFileFromCaptureRequest(CaptureAttachment Attachment,string? Name=null);
public sealed record WidgetFileReadRequest(string Id,long Offset,int Count=WidgetFileContract.ChunkBytes);
public sealed record WidgetFileReadChunk(long Offset,byte[] Bytes,bool EndOfFile);
public sealed record WidgetFileOfferRequest(IReadOnlyList<string> FileIds);
public sealed record WidgetFileShareOffer(string Id,IReadOnlyList<WidgetFileMetadata> Files,long ExpiresAtUnixMilliseconds)
{
    public bool IsWellFormed()=>WidgetFileContract.Token(Id)&&Files is {Count:>0 and <=WidgetFileContract.MaximumFiles}&&
        Files.All(file=>file?.IsWellFormed()==true)&&ExpiresAtUnixMilliseconds>0;
}
public static class WidgetFileContract
{
    public const int MaximumFiles=16;
    public const int ChunkBytes=64*1024;
    public const long MaximumFileBytes=64L*1024*1024*1024;
    public static bool Token(string? value)=>value is {Length:32}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool Name(string? value)=>value is {Length:>0 and <=255}&&!value.Any(c=>char.IsControl(c)||c is '/' or '\\');
    public static bool Type(string? value)=>value is {Length:>0 and <=128}&&value.Contains('/')&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '/' or '.' or '+' or '-');
}
