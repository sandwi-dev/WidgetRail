using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace WidgetRail.Playnite;

/// <summary>Optional public-service adapter. Never reads the provider's database or compiles scripts.</summary>
internal sealed partial class AchievementAdapter : IDisposable
{
    private static readonly Guid ProviderId = new Guid("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");
    private readonly IPlayniteAPI api;
    private readonly ChangeFeed changes;
    private readonly List<Action> unsubscribe = new List<Action>();
    private readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> properties = new ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>>();
    private readonly Dictionary<Guid, Cached> cached = new Dictionary<Guid, Cached>();
    private readonly object eventGate = new object();
    private readonly Queue<string> unlockOrder = new Queue<string>();
    private readonly HashSet<string> unlocks = new HashSet<string>(StringComparer.Ordinal);
    private object? plugin, service;
    private MethodInfo? read, refresh, overviewRead;
    private long revision = 1, useSequence;
    private volatile bool disposed;
    private sealed class Cached
    {
        internal long Revision, Used;
        internal DateTime Expires;
        internal object Data = null!;
    }
    internal AchievementAdapter(IPlayniteAPI api, ChangeFeed changes) { this.api = api; this.changes = changes; }
    private object? Value(object? instance, string name)
    {
        if (instance == null) return null;
        var table = properties.GetOrAdd(instance.GetType(), type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .GroupBy(property => property.Name).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal));
        return table.TryGetValue(name, out var property) ? property.GetValue(instance, null) : null;
    }
    // All binding and cache reads run on Playnite's UI dispatcher. Event callbacks
    // only update atomic revision / bounded event records and may arrive elsewhere.
    internal object Status()
    {
        try { EnsureBinding(); }
        catch (Exception error) when (error is TargetInvocationException || error is ArgumentException || error is InvalidOperationException || error is TypeLoadException)
        { return new { installed = plugin != null, readable = false, refreshable = false, code = "provider_unavailable" }; }
        return new { installed = plugin != null, readable = service != null && read != null, refreshable = refresh != null,
            assemblyVersion = plugin?.GetType().Assembly.GetName().Version is Version version && version != new Version(0, 0, 0, 0)
                ? version.ToString() : null };
    }
    internal bool CanRefresh { get { EnsureBinding(); return refresh != null; } }
    private void EnsureBinding()
    {
        if (disposed) throw new ObjectDisposedException(nameof(AchievementAdapter));
        var next = api.Addons.Plugins.FirstOrDefault(item => item.Id == ProviderId);
        var nextService = Value(next, "AchievementDataService");
        if (ReferenceEquals(plugin, next) && ReferenceEquals(service, nextService)) return;
        Detach(); plugin = next; service = nextService;
        cached.Clear(); properties.Clear(); Interlocked.Increment(ref revision);
        if (plugin == null || service == null) return;
        read = service.GetType().GetMethod("GetVisibleGameAchievementData", new[] { typeof(string) }) ??
            service.GetType().GetMethod("GetGameAchievementData", new[] { typeof(string) });
        overviewRead = FindOverviewRead(service.GetType());
        refresh = plugin.GetType().GetMethod("RequestSingleGameRefreshAsync", new[] { typeof(Guid) }) ??
            plugin.GetType().GetMethod("RequestSingleGameScanAsync", new[] { typeof(Guid) });
        if (refresh != null && !typeof(Task).IsAssignableFrom(refresh.ReturnType)) refresh = null;
        var manager = Value(plugin, "CacheManager");
        if (manager != null)
            foreach (var name in new[] { "GameCacheUpdated", "CacheDeltaUpdated", "CacheInvalidated" }) Subscribe(manager, manager.GetType(), name, nameof(CacheChanged));
        Subscribe(null, plugin.GetType(), "SettingsSaved", nameof(CacheChanged));
        Subscribe(null, plugin.GetType(), "AchievementUnlocked", nameof(AchievementUnlocked));
    }
    private void Subscribe(object? owner, Type type, string name, string handlerName)
    {
        var info = type.GetEvent(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        if (info?.EventHandlerType == null) return;
        try
        {
            var method = GetType().GetMethod(handlerName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var handler = Delegate.CreateDelegate(info.EventHandlerType, this, method);
            info.AddEventHandler(owner, handler);
            unsubscribe.Add(() => info.RemoveEventHandler(owner, handler));
        }
        catch (Exception error) when (error is ArgumentException || error is TargetInvocationException || error is MethodAccessException)
        { /* Optional event unavailable: short read-through cache still stays fresh. */ }
    }
    private void CacheChanged(object sender, EventArgs args)
    {
        if (disposed) return;
        Interlocked.Increment(ref revision);
        changes.Publish(new IntegrationEvent("achievements-changed"));
        CaptureStateChanged?.Invoke();
    }
    private void AchievementUnlocked(object sender, EventArgs args)
    {
        if (disposed) return;
        try
        {
            foreach (var flag in new[] { "IsPreview", "IsTestFire", "IsFriendUnlock", "IsProgressUpdate", "IsCompletionAchievement", "IsGameCompleted" })
                if (!(Value(args, flag) is bool value) || value) return;
            if (!(Value(args, "PlayniteGameId") is Guid gameId) || gameId == Guid.Empty ||
                !(Value(args, "ApiName") is string achievement) || string.IsNullOrEmpty(achievement) || achievement.Length > 256) return;
            var provider = WireJson.Text(Value(args, "ProviderKey"), 64);
            var stamp = Value(args, "UnlockTimeUtc") as DateTime? ?? Value(args, "ObservedUtc") as DateTime?;
            var key = gameId.ToString("D") + "|" + provider + "|" + achievement + "|" + stamp?.Ticks.ToString(CultureInfo.InvariantCulture);
            lock (eventGate)
            {
                if (!unlocks.Add(key)) return;
                unlockOrder.Enqueue(key);
                while (unlockOrder.Count > 512) unlocks.Remove(unlockOrder.Dequeue());
            }
            Interlocked.Increment(ref revision);
            // Names/descriptions are deliberately absent: consumers must obtain
            // the current visible projection before presenting an unlock.
            changes.Publish(new IntegrationEvent("achievement-unlocked", gameId.ToString("D"), provider, achievement));
            CaptureStateChanged?.Invoke();
        }
        catch (Exception error) when (error is TargetInvocationException || error is ArgumentException || error is InvalidOperationException)
        { /* A provider callback must never throw back into Playnite. */ }
    }
    internal object? Read(Guid gameId)
    {
        EnsureBinding();
        if (service == null || read == null) return null;
        var current = Interlocked.Read(ref revision);
        if (cached.TryGetValue(gameId, out var existing) && existing.Revision == current && existing.Expires > DateTime.UtcNow)
        { existing.Used = ++useSequence; return existing.Data; }
        var data = read.Invoke(service, new object[] { gameId.ToString("D") });
        if (!(Value(data, "Achievements") is IEnumerable source)) return null;
        var rows = new List<object>(); var unlockedCount = 0; var characters = 0;
        foreach (var item in source)
        {
            if (rows.Count >= 4000) throw new ApiException(413, "achievement_limit");
            if (Value(item, "IsFiltered") is bool filtered && filtered) continue;
            if (!(Value(item, "Unlocked") is bool unlocked) || !(Value(item, "Hidden") is bool hidden) || !(Value(item, "DisplayName") is string))
                return null;
            var name = WireJson.Text(Value(item, "DisplayName"));
            var description = WireJson.Text(Value(item, "Description"), 2048);
            var icon = WireJson.Text(Value(item, unlocked ? "UnlockedIconPath" : "LockedIconPath"), 2048);
            if (icon.Length == 0) icon = WireJson.Text(Value(item, "UnlockedIconPath"), 2048);
            var note = WireJson.Text(Value(item, "AchievementNote"), 2048);
            characters += name.Length + description.Length + icon.Length + note.Length;
            if (characters > 200000) throw new ApiException(413, "achievement_text_limit");
            if (unlocked) unlockedCount++;
            var percent = Value(item, "GlobalPercentUnlocked") as double?;
            var stamp = Value(item, "UnlockTimeUtc") as DateTime?;
            rows.Add(new
            {
                apiName = WireJson.Text(Value(item, "ApiName")), name, description, unlocked, isHidden = hidden,
                dateUnlocked = stamp?.ToString("o", CultureInfo.InvariantCulture),
                percent = percent.HasValue && percent >= 0 && percent <= 100 ? percent : null,
                gamerScore = Nonnegative(item, "Points"), progress = Nonnegative(item, "ProgressNum"), progressMax = Nonnegative(item, "ProgressDenom"), icon,
                category = WireJson.Text(Value(item, "Category")), trophyType = WireJson.Text(Value(item, "TrophyType"), 64),
                isGoal = Value(item, "IsGoal") as bool? ?? false, isCapstone = Value(item, "IsCapstone") as bool? ?? false, note,
            });
        }
        var projected = new { gameId = gameId.ToString("D"), installed = true, provider = "playnite-achievements",
            providerKey = WireJson.Text(Value(data, "EffectiveProviderKey") ?? Value(data, "ProviderKey"), 64),
            revision = current, canRefresh = refresh != null, observedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            total = rows.Count, unlocked = unlockedCount, achievements = rows.ToArray() };
        cached[gameId] = new Cached { Data = projected, Revision = current, Used = ++useSequence, Expires = DateTime.UtcNow.AddSeconds(2) };
        if (cached.Count > 8) cached.Remove(cached.OrderBy(pair => pair.Value.Used).First().Key);
        return projected;
    }
    private int? Nonnegative(object item, string property) => Value(item, property) is int value && value >= 0 ? (int?)value : null;
    internal Task Refresh(Guid gameId)
    {
        EnsureBinding();
        if (plugin == null || refresh == null) throw new ApiException(409, "achievement_refresh_unavailable");
        return refresh.Invoke(plugin, new object[] { gameId }) as Task ?? throw new ApiException(409, "achievement_refresh_unavailable");
    }
    private void Detach()
    {
        foreach (var action in unsubscribe)
            try { action(); } catch (Exception error) when (error is TargetInvocationException || error is ArgumentException || error is InvalidOperationException) { }
        unsubscribe.Clear(); read = refresh = overviewRead = null;
    }
    public void Dispose() { if (disposed) return; disposed = true; Detach(); cached.Clear(); properties.Clear(); }
}
