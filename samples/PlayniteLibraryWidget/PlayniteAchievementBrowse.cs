using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteAchievementEntry(string Key, PlayniteAchievement Item, bool Secret)
{
    internal static string KeyFor(PlayniteAchievement item) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(item.Id) ? item.Name + "\n" + item.Description : item.Id))).ToLowerInvariant();
    internal string Name => Secret ? "Secret achievement" : Item.Name;
}

internal sealed class PlayniteAchievementBrowse
{
    internal const string ListId = "playnite-library.details.achievements.list";
    internal const string Prefix = "playnite-library.details.";
    internal PlayniteAchievementBrowse(long generation, PlayniteDetailsExtras state)
    {
        Generation = generation;
        State = state;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<PlayniteAchievementEntry>();
        foreach (var item in state.Achievements?.Items ?? [])
        {
            var key = PlayniteAchievementEntry.KeyFor(item);
            var count = occurrences.GetValueOrDefault(key);
            occurrences[key] = count + 1;
            if (count > 0) key += "." + count.ToString(CultureInfo.InvariantCulture);
            entries.Add(new(key, item, item.Hidden && !item.Unlocked && !state.RevealedAchievements.Contains(key)));
        }
        All = entries;
        ByKey = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        IEnumerable<PlayniteAchievementEntry> filtered = entries.Where(entry => state.AchievementFilter switch
        {
            AchievementFilter.Locked => !entry.Item.Unlocked,
            AchievementFilter.Unlocked => entry.Item.Unlocked,
            _ => true,
        });
        var search = state.AchievementSearch.Trim();
        if (search.Length > 0) filtered = filtered.Where(entry => entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            !entry.Secret && entry.Item.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        Rows = (state.AchievementSort switch
        {
            AchievementSort.Name => filtered.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            AchievementSort.Rarity => filtered.OrderBy(entry => entry.Item.Percent ?? double.MaxValue).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            AchievementSort.Progress => filtered.OrderByDescending(entry => !entry.Secret && !entry.Item.Unlocked && entry.Item.Progress is { } progress
                ? (double)progress.Current / progress.Maximum : -1).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderByDescending(entry => entry.Item.Unlocked).ThenByDescending(entry => entry.Item.UnlockedAt),
        }).ToArray();
    }
    internal long Generation { get; }
    internal PlayniteDetailsExtras State { get; }
    internal IReadOnlyList<PlayniteAchievementEntry> All { get; }
    internal IReadOnlyDictionary<string, PlayniteAchievementEntry> ByKey { get; }
    internal IReadOnlyList<PlayniteAchievementEntry> Rows { get; }
    internal bool SameInputs(long generation, PlayniteDetailsExtras state) => Generation == generation &&
        ReferenceEquals(State.Achievements, state.Achievements) && ReferenceEquals(State.RevealedAchievements, state.RevealedAchievements) &&
        State.AchievementFilter == state.AchievementFilter && State.AchievementSort == state.AchievementSort &&
        State.AchievementSearch == state.AchievementSearch;
    internal static WidgetArtworkHandle IconHandle(string key) => new("achievement-icon." + key);
}
