using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteDetailsTab { Description, Information, Achievements, Activity, Links }
internal sealed record PlayniteGameLink(string Name, string Url);
internal sealed record PlayniteGameDetails(WidgetAppLibraryItem Item, PlayniteBridgeGame? Game = null);
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
}
internal sealed record PlayniteAchievements(bool Available, int Total, int Unlocked,
    IReadOnlyList<PlayniteAchievement> Items)
{
    internal static PlayniteAchievements Unavailable { get; } = new(false, 0, 0, []);
    internal PlayniteAchievementProvider Provider { get; init; }
    internal string ProviderName => Provider == PlayniteAchievementProvider.PlayniteAchievements ? "PlayniteAchievements" : "SuccessStory";
}
internal sealed record PlayniteSession(DateTimeOffset? Date, long Seconds, string? Action);
internal sealed record PlayniteActivity(bool Available, long TotalSeconds, IReadOnlyList<PlayniteSession> Sessions)
{
    internal static PlayniteActivity Unavailable { get; } = new(false, 0, []);
}
internal sealed record PlayniteDetailsExtras
{
    internal PlayniteGameDetails? Full { get; init; }
    // A confirmed mutation wins over a detail read that may already be in flight.
    // Explicit Refresh creates new extras and reads the provider's current value.
    internal string? ConfirmedCompletionStatus { get; init; }
    internal IReadOnlyList<string>? CompletionStatuses { get; init; }
    internal bool CompletionStatusesLoading { get; init; }
    internal string? CompletionStatusesError { get; init; }
    internal string? ResolveCompletionStatus(string? catalogStatus) =>
        ConfirmedCompletionStatus ?? (Full?.Game is { } game ? game.CompletionStatus : catalogStatus);
    internal PlayniteDetailsTab Tab { get; init; }
    internal PlayniteAchievements? Achievements { get; init; }
    internal PlayniteActivity? Activity { get; init; }
    internal bool AchievementsLoading { get; init; }
    internal bool ActivityLoading { get; init; }
    internal string? AchievementsError { get; init; }
    internal string? ActivityError { get; init; }
    internal string? OperationMessage { get; init; }
    internal bool OperationBusy { get; init; }
    internal bool ConfirmUninstall { get; init; }
    internal IReadOnlySet<string> RevealedAchievements { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    internal AchievementFilter AchievementFilter { get; init; }
    internal AchievementSort AchievementSort { get; init; }
    internal string AchievementSearch { get; init; } = "";
    internal long AchievementSearchRequest { get; init; }
    internal int Page { get; init; }
}
