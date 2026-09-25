using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteDetailsTab { Overview, Achievements, Activity }
internal sealed record PlayniteGameLink(string Name, string Url);
internal sealed record PlayniteGameDetails(WidgetAppLibraryItem Item, PlayniteBridgeGame? Game = null);
internal sealed record PlayniteAchievement(string Name, string Description, bool Unlocked,
    DateTimeOffset? UnlockedAt, double? Percent, int? GamerScore, bool Hidden);
internal sealed record PlayniteAchievements(bool Available, int Total, int Unlocked,
    IReadOnlyList<PlayniteAchievement> Items)
{
    internal static PlayniteAchievements Unavailable { get; } = new(false, 0, 0, []);
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
    internal IReadOnlySet<int> RevealedAchievements { get; init; } = new HashSet<int>();
    internal int Page { get; init; }
}
