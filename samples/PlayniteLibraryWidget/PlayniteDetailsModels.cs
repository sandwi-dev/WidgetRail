using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteDetailsTab { Description, Information, Achievements, Captures, Activity, Links }
internal sealed record PlayniteGameLink(string Name, string Url);
internal sealed record PlayniteGameDetails(WidgetAppLibraryItem Item, PlayniteBridgeGame? Game = null)
{
    internal PlayniteStoreLookup? StoreLookup { get; } = PlayniteStoreLookup.From(Game);
}
internal sealed record PlayniteStoreLookup(uint? AppId, string Name, string? Website, string? LinkedTrailer)
{
    internal static PlayniteStoreLookup? From(PlayniteBridgeGame? game)
    {
        if (game is null) return null;
        var sites = game.Links.Where(link => link.Name.Equals("Official Website", StringComparison.OrdinalIgnoreCase))
            .Select(link => link.Url).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        var website = sites.Length == 1 ? sites[0] : null;
        var trailer = PlayniteTrailerPresentation.LinkedTrailer(game.Links);
        return game.SteamAppId is null && website is null && trailer is null ? null :
            new(game.SteamAppId, game.Name, website, trailer);
    }
}
internal enum PlayniteAchievementProvider { SuccessStory, PlayniteAchievements }
internal enum AchievementFilter { All, Locked, Unlocked }
internal enum AchievementSort { Recent, Rarity, Name, Progress }
internal sealed record PlayniteAchievementProgress(int Current, int Maximum);
internal sealed record PlayniteAchievement(string Name, string Description, bool Unlocked,
    DateTimeOffset? UnlockedAt, double? Percent, int? GamerScore, bool Hidden)
{
    internal string? Id { get; init; }
    internal PlayniteAchievementProgress? Progress { get; init; }
    internal string? Icon { get; init; }
    internal string? Category { get; init; }
    internal string? TrophyType { get; init; }
    internal bool IsGoal { get; init; }
    internal bool IsCapstone { get; init; }
}
internal sealed record PlayniteAchievements(bool Available, int Total, int Unlocked,
    IReadOnlyList<PlayniteAchievement> Items)
{
    internal static PlayniteAchievements Unavailable { get; } = new(false, 0, 0, []);
    internal PlayniteAchievementProvider Provider { get; init; }
    internal bool CanRefresh { get; init; }
    internal DateTimeOffset? ObservedAt { get; init; }
    internal string? ConnectionId { get; init; }
    internal string? ProviderKey { get; init; }
    internal string ProviderName => Provider == PlayniteAchievementProvider.PlayniteAchievements ? "PlayniteAchievements" : "SuccessStory";
}
internal sealed record PlayniteSession(DateTimeOffset? Date, long Seconds, string? Action);
internal sealed record PlayniteRecentActivity(IReadOnlyList<PlayniteRecentSession> Sessions, IReadOnlyList<PlayniteRecentAchievement> Unlocks)
{
    internal IReadOnlyDictionary<string, string>? HeroHandles { get; init; }
    internal static PlayniteRecentActivity Empty { get; } = new([], []);
}
internal sealed record PlayniteRecentSession(string GameId, string GameName, DateTimeOffset StartedAt, long Seconds);
internal sealed record PlayniteActivity(bool Available, long TotalSeconds, IReadOnlyList<PlayniteSession> Sessions)
{
    internal static PlayniteActivity Unavailable { get; } = new(false, 0, []);
}
internal sealed record PlayniteDetailsExtras
{
    internal PlayniteGameDetails? Full { get; init; }
    internal SteamStoreMedia? StoreMedia { get; init; }
    internal bool StoreMediaLoading { get; init; }
    internal PlayniteSetupView AddonSetup { get; init; } = new();
    internal string? UnavailablePublisherTrailer { get; init; }
    // A confirmed mutation wins over a detail read that may already be in flight.
    // Explicit Refresh creates new extras and reads the provider's current value.
    internal string? ConfirmedCompletionStatus { get; init; }
    internal IReadOnlyList<string>? CompletionStatuses { get; init; }
    internal bool CompletionStatusesLoading { get; init; }
    internal string? CompletionStatusesError { get; init; }
    internal string? ResolveCompletionStatus(string? catalogStatus) =>
        ConfirmedCompletionStatus ?? (Full?.Game is { } game ? game.CompletionStatus : catalogStatus);
    internal PlayniteDetailsTab Tab { get; init; }
    internal WidgetFocusRequest? TabFocus { get; init; }
    internal WidgetFocusRequest? TimelineFocus { get; init; }
    internal string? TimelineFocusKey { get; init; }
    internal string? CaptureAchievementId { get; init; }
    internal string? CaptureAchievementName { get; init; }
    internal int? CapturePickerOffset { get; init; }
    internal bool BrowsingLinkedCaptures { get; init; }
    internal WidgetFocusRequest? CapturePageEntry { get; init; }
    internal string? CaptureLinkMessage { get; init; }
    internal PlayniteSessionReview? ReviewSession { get; init; }
    internal RemovedCaptureLink? RemovedLink { get; init; }
    internal string? CaptureReturnId { get; init; }
    internal IReadOnlyList<WidgetRail.WidgetProtocol.MediaPlayerGalleryItem>? CaptureViewer { get; init; }
    internal bool CaptureViewerFromHome { get; init; }
    internal PlayniteAchievements? Achievements { get; init; }
    internal bool SessionsAll { get; init; }
    internal string? SessionPeriodKey { get; init; }
    internal string? ExpandedSession { get; init; }
    internal string? SessionFocusTarget { get; init; }
    internal WidgetFocusRequest? SessionFocus { get; init; }
    internal PlayniteActivity? Activity { get; init; }
    internal bool AchievementsLoading { get; init; }
    internal bool ActivityLoading { get; init; }
    internal string? AchievementsError { get; init; }
    internal string? ActivityError { get; init; }
    internal string? OperationMessage { get; init; }
    internal bool OperationBusy { get; init; }
    internal bool AchievementRefreshBusy { get; init; }
    internal string? AchievementRefreshMessage { get; init; }
    internal string? ProgressPinSelectionId { get; init; }
    internal string? TrackedAchievementId { get; init; }
    internal bool ChoosingCompletion { get; init; }
    internal bool ConfirmUninstall { get; init; }
    internal IReadOnlySet<string> RevealedAchievements { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    internal AchievementFilter AchievementFilter { get; init; }
    internal AchievementSort AchievementSort { get; init; }
    internal string AchievementSearch { get; init; } = "";
    internal long AchievementSearchRequest { get; init; }
    internal (string CardKey, string RowKey, WidgetFocusRequest Focus)? ShelfAchievement { get; init; }
    internal int Page { get; init; }
}

internal sealed record RemovedCaptureLink(string Connection, string Reference, CaptureDestinationChange Change);
