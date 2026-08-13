using System.Text.Json.Serialization;

namespace GameBarAlternative.PlatformBroker;

public sealed class BrokerException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public enum AppLibraryKind { Unknown, Application, Game }
public enum AppLibraryAvailabilityState { Installed, Unavailable, StaleSource }
public enum AppLibrarySortOrder { DisplayName, DisplayNameDescending, SourceThenDisplayName }
public enum AppLibraryCursorDirection { Before, After }
public enum AppLibrarySourceHealth { Healthy, Degraded, Unavailable, Refreshing }
public enum AppLibrarySourceAccountState
{
    NotApplicable,
    SignedOut,
    SigningIn,
    Ready,
    Expired,
    Denied,
    Unavailable,
}
public enum AppLibraryLaunchObservationState
{
    RequestAccepted,
    LauncherStarted,
    Running,
    Ended,
}
public enum AppLibraryAction
{
    Launch,
    Install,
    Pause,
    Resume,
    Cancel,
    Update,
    Repair,
    Move,
    Import,
    Uninstall,
    CloudSync,
    OpenSourceClient,
    ManageAddOns,
}

public sealed record AppLibraryBackendQuery(
    bool InstalledOnly = true,
    AppLibraryKind? Kind = null,
    string? SourceAttribution = null,
    AppLibrarySortOrder Sort = AppLibrarySortOrder.DisplayName)
{
    public string? SearchText { get; init; }
    [JsonIgnore]
    public IReadOnlyList<string>? StableIdentityFilter { get; init; }
}

public sealed record AppLibraryBackendCursorRequest(
    AppLibraryBackendQuery Query,
    string? Cursor,
    AppLibraryCursorDirection? Direction,
    int Limit,
    bool Refresh = false);

public sealed record AppLibraryBackendItemSummary(
    [property: JsonIgnore] string ProviderAppId,
    [property: JsonIgnore] string StableProviderIdentity,
    string DisplayName,
    AppLibraryKind Kind,
    [property: JsonIgnore] string ArtworkRevision = "",
    string SourceAttribution = "Windows")
{
    [JsonIgnore]
    public string SourceIdentity { get; init; } = string.Empty;
    [JsonIgnore]
    public bool IsLaunchable { get; init; } = true;
    [JsonIgnore]
    public AppLibraryAvailabilityState AvailabilityState { get; init; } =
        AppLibraryAvailabilityState.Installed;
    [JsonIgnore]
    public string? AvailabilityStatusCode { get; init; }
    [JsonIgnore]
    public IReadOnlyList<AppLibraryAction>? SupportedActions { get; init; }
}

public sealed record AppLibraryBackendCursorPage(
    IReadOnlyList<AppLibraryBackendItemSummary> Items,
    string? Before,
    string? After,
    string Revision)
{
    public IReadOnlyList<AppLibrarySourceSummary> Sources { get; init; } = [];
}

public sealed record AppLibrarySourceSummary(
    string SourceId,
    string DisplayName,
    AppLibrarySourceHealth Health,
    long Revision,
    string StatusCode)
{
    public AppLibrarySourceAccountState AccountState { get; init; } =
        AppLibrarySourceAccountState.NotApplicable;
    public long? LastSuccessfulRefreshAtUnixMilliseconds { get; init; }
}

public sealed record AppLibraryLaunchObservationSummary(
    AppLibraryLaunchObservationState State,
    bool SupportsRunning,
    bool SupportsEnded);

public sealed record AppLibraryIconSummary(string? PngBase64);

public static class AppLibraryImageLimits
{
    public const int MaximumPngBytes = 12 * 1024;
    public const int MaximumPixelDimension = 64;
}

public sealed record RunningAppBackendObservation(
    [property: JsonIgnore] string StableProviderIdentity,
    [property: JsonIgnore] string InstanceEvidence,
    string DisplayName,
    AppLibraryKind Kind,
    string SourceAttribution);

public sealed record RunningAppBackendObservationPage(
    IReadOnlyList<RunningAppBackendObservation> Items,
    string Revision);

public interface IAppLibraryPlatformBrokerBackend
{
    Task<AppLibraryBackendCursorPage> QueryAppLibraryAsync(
        AppLibraryBackendCursorRequest request,
        CancellationToken cancellationToken);

    Task LaunchAppLibraryItemAsync(string appId, CancellationToken cancellationToken);

    Task<AppLibraryLaunchObservationSummary> LaunchAppLibraryItemObservedAsync(
        string appId,
        CancellationToken cancellationToken);

    Task<AppLibraryIconSummary> GetAppLibraryIconAsync(
        string appId,
        CancellationToken cancellationToken);

    Task<RunningAppBackendObservationPage> ObserveRunningAppsAsync(
        CancellationToken cancellationToken);
}
