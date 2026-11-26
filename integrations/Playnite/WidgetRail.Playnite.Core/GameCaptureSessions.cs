using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using WidgetRail.Internal;

namespace WidgetRail.Playnite;

[DataContract]
public sealed class GameCaptureSession
{
    [DataMember] public string Instance { get; set; } = "";
    [DataMember] public long From { get; set; }
    [DataMember] public long Through { get; set; }
}

/// <summary>Profile-local process evidence. Open intervals live in memory and are never recovered as running games.</summary>
public sealed class GameCaptureSessions
{
    public const int HistoryLimit = 64;
    private readonly string directory;
    private readonly Dictionary<Guid, GameCaptureSession> active = new();
    private readonly object gate = new();

    public GameCaptureSessions(string directory) => this.directory = directory;

    public void Start(Guid game, uint pid, ulong creationTime, long launchRequested, long observed)
    {
        lock (gate) StartLocked(game,pid,creationTime,launchRequested,observed);
    }

    private void StartLocked(Guid game, uint pid, ulong creationTime, long launchRequested, long observed)
    {
        if (game == Guid.Empty || pid == 0 || creationTime == 0 || launchRequested <= 0 || observed < launchRequested) return;
        var instance = CaptureProcessKey.Create(pid, creationTime);
        // A shared launcher or emulator already attached to another title is ambiguous from this point on.
        if (active.TryGetValue(game,out var previous) && previous.Instance == instance) return;
        StopLocked(game,launchRequested-1);
        var conflicts = active.Where(pair => pair.Value.Instance == instance).Select(pair => pair.Key).ToArray();
        foreach (var conflict in conflicts) StopLocked(conflict, launchRequested - 1);
        if (conflicts.Length != 0) return;
        long created = new DateTimeOffset(DateTime.FromFileTimeUtc(checked((long)creationTime))).ToUnixTimeMilliseconds();
        if (created < launchRequested || created > observed) return;
        var session = new GameCaptureSession { Instance = instance, From = created, Through = observed };
        // Persist only the observed prefix. An unexpected Playnite exit cannot leave an open-ended match on disk.
        IntegrationFiles.Write(PathFor(game), WithSession(game,session));
        active[game] = session;
    }

    public void Stop(Guid game, long through)
    {
        lock (gate) StopLocked(game, through);
    }

    // Only for a confirmed window in the current launch. An explicitly chosen
    // emulator can predate the game. Never claim captures before the game began.
    public bool BindWindow(Guid game, uint pid, ulong creationTime, long sessionStart, long observed)
    {
        if (game == Guid.Empty || pid == 0 || creationTime == 0 || sessionStart <= 0 || sessionStart > observed)
            return false;
        var created = new DateTimeOffset(DateTime.FromFileTimeUtc(checked((long)creationTime))).ToUnixTimeMilliseconds();
        var from = Math.Max(sessionStart, created);
        if (from > observed) return false;
        var key = CaptureProcessKey.Create(pid, creationTime);
        lock (gate)
        {
            if (active.Any(pair => pair.Key != game && pair.Value.Instance == key)) return false;
            active.TryGetValue(game, out var previous);
            if (previous?.Instance == key) return true;
            var history = History(game).ToList();
            if (previous != null)
            {
                history.RemoveAll(item => item.Instance == previous.Instance && item.From == previous.From);
                if (previous.From < from)
                    history.Add(new GameCaptureSession { Instance = previous.Instance, From = previous.From, Through = from - 1 });
            }
            var current = new GameCaptureSession { Instance = key, From = from, Through = observed };
            history.RemoveAll(item => item.Instance == key && item.From == from);
            history.Add(current);
            IntegrationFiles.Write(PathFor(game), history.Skip(Math.Max(0, history.Count - HistoryLimit)).ToArray());
            active[game] = current;
            return true;
        }
    }

    private void StopLocked(Guid game, long through)
    {
        if (!active.TryGetValue(game, out var session)) return;
        IntegrationFiles.Write(PathFor(game), WithSession(game,new() { Instance = session.Instance, From = session.From, Through = through }));
        active.Remove(game);
    }

    public GameCaptureSession[] Snapshot(Guid game, long now)
    {
        lock (gate)
        {
            if (!active.TryGetValue(game, out var session) || now < session.From) return History(game);
            return WithSession(game,new() { Instance = session.Instance, From = session.From, Through = now });
        }
    }

    public void Close(long now)
    {
        lock (gate)
            foreach (var game in active.Keys.ToArray()) StopLocked(game, now);
    }

    private string PathFor(Guid game) => Path.Combine(directory, game.ToString("N") + ".json");
    private GameCaptureSession[] History(Guid game) => IntegrationFiles.Read<GameCaptureSession[]>(PathFor(game)) ?? Array.Empty<GameCaptureSession>();
    private GameCaptureSession[] WithSession(Guid game, GameCaptureSession session)
    {
        var records = History(game).Where(item => item.Instance != session.Instance || item.From != session.From).ToList();
        if (session.Through >= session.From) records.Add(session);
        return records.Skip(Math.Max(0,records.Count-HistoryLimit)).ToArray();
    }
}
