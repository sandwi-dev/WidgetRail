namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteAchievementGame(string Id, string Name, string Source, bool Installed,
    int Total, int Unlocked, int Trackable, DateTimeOffset? LastUnlockedAt, DateTimeOffset? LastPlayedAt)
{
    internal double Completion => Total == 0 ? 0 : (double)Unlocked / Total;
}
internal sealed record PlayniteRecentAchievement(string GameId, string GameName, string AchievementId,
    string Name, DateTimeOffset UnlockedAt, double? Percent);
internal sealed record PlayniteAchievementOverview(string Availability, string? ConnectionId, string Revision,
    DateTimeOffset? ObservedAt, IReadOnlyList<PlayniteAchievementGame> Games, IReadOnlyList<PlayniteRecentAchievement> Recent)
{
    internal static PlayniteAchievementOverview Unavailable { get; } = new("unsupported", null, "", null, [], []);
    internal int Total => Games.Sum(game => game.Total);
    internal int Unlocked => Games.Sum(game => game.Unlocked);
    internal int Completed => Games.Count(game => game.Total > 0 && game.Total == game.Unlocked);
}
