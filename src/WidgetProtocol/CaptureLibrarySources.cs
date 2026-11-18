namespace WidgetRail.WidgetProtocol;

/// <summary>A user-selected read-only folder. The path is disclosed only to its owning widget for provider configuration;
/// it is not file-access authority. Library operations always require the opaque source ID.</summary>
public sealed record CaptureLibrarySource(string Id, string Label, bool Available, int LinkedCount, int UnsupportedCount, string CanonicalFolderPath);
public sealed record CaptureLibrarySources(IReadOnlyList<CaptureLibrarySource> Items);
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<CaptureLibraryKnownLocation>))]
public enum CaptureLibraryKnownLocation { WindowsGameCaptures }
public sealed record CaptureLibraryChooseSourceRequest(string Label)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CaptureLibraryKnownLocation? KnownLocation { get; init; }
}
public sealed record CaptureLibrarySourceChoice(CaptureLibrarySource? Source);
public sealed record CaptureLibrarySourceReference(string SourceId);
/// <summary>A provider reference relative to an owned source grant; never an absolute path.</summary>
public sealed record CaptureLibraryLinkRequest(string SourceId, string RelativePath, CaptureSaveContext? Context = null);
public sealed record CaptureLibraryLinkResult(CaptureLibraryItem Item);

public static class CaptureLibrarySourceLimits
{
    public const int MaximumSources = 16;
    public const int MaximumRelativePath = 1024;
    public static bool ValidLabel(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 120 && !value.Any(char.IsControl);
    public static bool ValidRelativePath(string? value) => value is { Length: > 0 and <= MaximumRelativePath } &&
        value.Split('/', '\\').All(part => part.Length > 0 && part is not ("." or "..") && !part.EndsWith('.') && !part.EndsWith(' ') &&
            !part.Any(c => char.IsControl(c) || c is ':' or '"' or '<' or '>' or '|' or '?' or '*'));
    public static bool Valid(CaptureLibrarySource? value) => value is not null && CaptureLimits.ValidToken(value.Id) &&
        ValidLabel(value.Label) && value.LinkedCount >= 0 && value.UnsupportedCount >= 0 &&
        value.CanonicalFolderPath is { Length: > 0 and <= 32760 } && !value.CanonicalFolderPath.Any(char.IsControl);
}
