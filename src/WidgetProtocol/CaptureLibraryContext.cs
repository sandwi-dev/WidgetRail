using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

[JsonConverter(typeof(JsonStringEnumConverter<CaptureContextReferenceRole>))]
public enum CaptureContextReferenceRole { Subject, Event, Related }
/// <summary>Opaque author-owned identity. The host never interprets provider-specific kinds or identifiers.</summary>
public sealed record CaptureContextReference(CaptureContextReferenceRole Role, string Kind, string Id, string? Label = null)
{
    /// <summary>Author intent to offer the standard related-item action. Missing means display-only metadata;
    /// the host still resolves an installed handler and validates delivery.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasRelatedAction { get; init; }
    public bool IsWellFormed() => Enum.IsDefined(Role) && CaptureContextLimits.Kind(Kind) &&
        CaptureContextLimits.RequiredText(Id, 256) && CaptureLibraryLimits.ValidText(Label, 160);
}
/// <summary>Host-stamped authenticated source; this type is never accepted in a save request.</summary>
public sealed record CaptureContextOrigin(string PackageId, string PublisherId, string InstanceId)
{
    public bool IsWellFormed() => CaptureContextLimits.Identity(PackageId) && CaptureContextLimits.Identity(PublisherId) &&
        CaptureContextLimits.RequiredText(InstanceId, 128);
}
public sealed record CaptureSaveContext(string IdempotencyKey, IReadOnlyList<CaptureContextReference> References, long? EventAtUnixMilliseconds = null)
{
    public bool IsWellFormed() => CaptureContextLimits.RequiredText(IdempotencyKey, 128) &&
        References is { Count: > 0 and <= CaptureContextLimits.MaximumReferences } && References.All(reference => reference?.IsWellFormed() == true) &&
        References.Select(reference => (reference.Role, reference.Kind, reference.Id)).Distinct().Count() == References.Count &&
        CaptureContextLimits.Timestamp(EventAtUnixMilliseconds) && JsonSerializer.SerializeToUtf8Bytes(this).Length <= CaptureContextLimits.MaximumContextBytes;
}
public sealed record CaptureLibrarySaveWithContextRequest(CaptureAttachment Attachment, CaptureSaveContext Context);
public sealed record CaptureLibraryAttachContextRequest(string CaptureId, long ContentRevision, CaptureSaveContext Context);
public sealed record CaptureLibraryEnsureSubjectRequest(string CaptureId, long ContentRevision, string IdempotencyKey,
    CaptureContextReference Subject, bool RestoreDetached = false)
{
    public bool IsWellFormed() => CaptureLimits.ValidToken(CaptureId) && ContentRevision >= 0 &&
        CaptureContextLimits.RequiredText(IdempotencyKey, 128) && Subject is { Role: CaptureContextReferenceRole.Subject } && Subject.IsWellFormed();
}
public sealed record CaptureLibrarySubjectResult(CaptureContextSaveOutcome Outcome);
public sealed record CaptureDestinationChange(string CaptureId, long ContentRevision, string PackageId,
    string Kind, string DestinationId, string OperationId, string ExpectedRevision, bool Undo = false)
{
    public bool IsWellFormed() => CaptureLimits.ValidToken(CaptureId) && ContentRevision >= 0 &&
        CaptureContextLimits.Identity(PackageId) && CaptureContextLimits.Kind(Kind) && CaptureContextLimits.RequiredText(DestinationId, 256) &&
        CaptureLimits.ValidToken(OperationId) && CaptureLibraryLimits.ValidRevision(ExpectedRevision);
}
/// <summary>Find an unambiguous, caller-owned link for each visible capture in a bounded page.</summary>
public sealed record CaptureLibraryOwnedLinksRequest(IReadOnlyList<string> CaptureIds, CaptureLibraryReferenceFilter Reference)
{
    public bool IsWellFormed() => CaptureIds is { Count: > 0 and <= CaptureContextLimits.MaximumPageItems } &&
        CaptureIds.All(CaptureLimits.ValidToken) && CaptureIds.Distinct(StringComparer.Ordinal).Count() == CaptureIds.Count &&
        Reference?.IsWellFormed() == true;
}
public sealed record CaptureLibraryOwnedLinks(IReadOnlyList<CaptureLibraryAssociation> Items);
public sealed record CaptureLibrarySetContextLinkedRequest(string CaptureId, long ContentRevision, string AssociationId, long ExpectedLinkRevision, bool Linked)
{
    public bool IsWellFormed() => CaptureLimits.ValidToken(CaptureId) && ContentRevision >= 0 &&
        CaptureLimits.ValidToken(AssociationId) && ExpectedLinkRevision >= 0;
}

/// <summary>An inclusive UTC interval for one execution of an application. An interval query requires complete containment.</summary>
public sealed record CaptureProcessInterval(string Instance, long From, long Through)
{
    public static string InstanceId(uint processId, ulong creationFileTime) =>
        Internal.CaptureProcessKey.Create(processId, creationFileTime);
    public bool IsWellFormed() => Instance is { Length: 64 } && Instance.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        From > 0 && Through >= From && CaptureContextLimits.Timestamp(Through);
}
/// <summary>One immutable association; event time may precede actual capture time. Null capture time means a legacy source had no trusted timestamp.</summary>
public sealed record CaptureLibraryAssociation(string Id, string CaptureId, CaptureContextOrigin Origin,
    IReadOnlyList<CaptureContextReference> References, long? EventAtUnixMilliseconds, long? CapturedAtUnixMilliseconds, long RecordedAtUnixMilliseconds)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long LinkRevision { get; init; }
    public bool IsWellFormed() => LinkRevision >= 0 && CaptureLimits.ValidToken(Id) && CaptureLimits.ValidToken(CaptureId) && Origin?.IsWellFormed() == true &&
        References is { Count: > 0 and <= CaptureContextLimits.MaximumReferences } && References.All(reference => reference?.IsWellFormed() == true) &&
        References.Select(reference => (reference.Role, reference.Kind, reference.Id)).Distinct().Count() == References.Count &&
        CaptureContextLimits.Timestamp(EventAtUnixMilliseconds) && CaptureContextLimits.Timestamp(CapturedAtUnixMilliseconds) &&
        RecordedAtUnixMilliseconds is > 0 and <= 253402300799999;
}
[JsonConverter(typeof(JsonStringEnumConverter<CaptureContextSaveOutcome>))]
public enum CaptureContextSaveOutcome { Saved, AlreadySaved, Unavailable, Detached }
public sealed record CaptureLibraryContextSaveResult(CaptureContextSaveOutcome Outcome, CaptureLibraryItem? Item, CaptureLibraryAssociation Association);
public sealed record CaptureLibraryContextsRequest(string Id, int Offset = 0, int Count = 16, string? Revision = null);
public sealed record CaptureLibraryContextsPage(IReadOnlyList<CaptureLibraryAssociation> Items, int Offset, int TotalCount, string Revision);
public sealed record CaptureLibraryDestination(string PackageId, string Kind, string Id, string? Label, bool HasRelatedAction)
{
    public bool IsWellFormed() => CaptureContextLimits.Identity(PackageId) && CaptureContextLimits.Kind(Kind) &&
        CaptureContextLimits.RequiredText(Id, 256) && CaptureLibraryLimits.ValidText(Label, 160);
}
public sealed record CaptureLibraryDestinationsPage(IReadOnlyList<CaptureLibraryDestination> Items, int Offset, int TotalCount, string Revision);
public sealed record CaptureLibraryReferenceFilter(string Kind, string Id, CaptureContextReferenceRole? Role = null)
{
    public bool IsWellFormed() => CaptureContextLimits.Kind(Kind) && CaptureContextLimits.RequiredText(Id, 256) && (Role is null || Enum.IsDefined(Role.Value));
}
public static class CaptureContextLimits
{
    public const int MaximumReferences = 8;
    public const int MaximumContextBytes = 4096;
    public const int MaximumAssociationFileBytes = 8192;
    public const int MaximumPageItems = 16;
    public static bool RequiredText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    public static bool Kind(string? value) => value is { Length: > 0 and <= 48 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    public static bool Identity(string? value) => value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    public static bool Timestamp(long? value) => value is null or > 0 and <= 253402300799999;
}
