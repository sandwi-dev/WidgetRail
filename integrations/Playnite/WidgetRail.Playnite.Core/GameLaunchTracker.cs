using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WidgetRail.Playnite;

public sealed class GameLaunchTracker
{
    public sealed class Observation
    {
        public string State { get; }
        public int ProcessId { get; }
        public long CreatedFileTime { get; }
        public string? Name { get; }
        public Guid SessionId { get; }
        public long WindowHandle { get; }
        public long SessionStart { get; }
        internal Observation? Detected { get; }
        public string? ProcessKey => ProcessId > 0 && CreatedFileTime > 0
            ? WidgetRail.Internal.CaptureProcessKey.Create((uint)ProcessId, (ulong)CreatedFileTime) : null;
        internal Observation(string state, int pid = 0, long created = 0, string? name = null,
            Guid sessionId = default, long windowHandle = 0, long sessionStart = 0, Observation? detected = null)
        {
            State = state; ProcessId = pid; CreatedFileTime = created; Name = name;
            SessionId = sessionId; WindowHandle = windowHandle;
            SessionStart = sessionStart; Detected = detected;
        }
    }

    private readonly object gate = new();
    private readonly Dictionary<Guid, Observation> sessions = new();
    private string sessionRevision = Guid.NewGuid().ToString("N");

    public Guid[] RunningGames()
    {
        lock (gate) return sessions.Where(pair => pair.Value.State == "running").Select(pair => pair.Key).ToArray();
    }

    public Dictionary<Guid, Observation> RunningSessions()
    {
        lock (gate) return sessions.Where(pair => pair.Value.State == "running")
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public sealed class LaunchConflict
    {
        internal LaunchConflict(Guid game, int count, string revision) { Game = game; Count = count; Revision = revision; }
        public Guid Game { get; }
        public int Count { get; }
        public string Revision { get; }
    }

    public LaunchConflict? Conflict(Guid target, string? approval)
    {
        lock (gate)
        {
            if (sessions.Count == 0 || sessions.ContainsKey(target) || approval == sessionRevision) return null;
            return new LaunchConflict(sessions.Keys.First(), sessions.Count, sessionRevision);
        }
    }
    private string? ticket;
    private Guid game;
    private bool starting;
    private long issued;
    private Observation observation = new("pending");

    public string Begin(Guid id, bool alreadyRunning, bool alreadyStarting)
    {
        lock (gate)
        {
            game = id;
            ticket = Guid.NewGuid().ToString("N");
            issued = Stopwatch.GetTimestamp();
            starting = alreadyStarting;
            observation = alreadyRunning
                ? sessions.TryGetValue(id, out var active) && active.State == "running" ? active : new Observation("running")
                : new Observation("pending");
            return ticket;
        }
    }

    public void Starting(Guid id, string? name = null)
    {
        lock (gate)
        {
            sessions[id] = new Observation("pending", name: name, sessionId: Guid.NewGuid(),
                sessionStart: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            sessionRevision = Guid.NewGuid().ToString("N");
            if (game != id) return;
            if (starting || observation.State != "pending") ticket = null;
            starting = true;
        }
    }

    public void Started(Guid id, int pid, long created, bool waitForWindow = false)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(id, out var session) || session.State != "pending") return;
            var detected = new Observation("running", pid, created, session.Name, session.SessionId, sessionStart: session.SessionStart);
            var result = waitForWindow ? new Observation("running", name: session.Name, sessionId: session.SessionId,
                sessionStart: session.SessionStart, detected: detected) : detected;
            sessions[id] = result;
            if (id == game && starting) observation = result;
        }
    }

    // A window choice belongs to one run even when two consecutive launches have
    // the same game ID. Retire a pending handoff that still targets the old window.
    public bool TrySetWindow(Guid id, Guid sessionId, int pid, long created, long windowHandle, Func<bool>? confirm = null)
    {
        if (sessionId == Guid.Empty || pid <= 0 || created <= 0 || windowHandle == 0) return false;
        lock (gate)
        {
            if (!sessions.TryGetValue(id, out var active) || active.State != "running" || active.SessionId != sessionId)
                return false;
            if (sessions.Any(pair => pair.Key != id && pair.Value.State == "running" &&
                pair.Value.ProcessId == pid && pair.Value.CreatedFileTime == created)) return false;
            if (confirm != null && !confirm()) return false;
            if (active.ProcessId == pid && active.CreatedFileTime == created && active.WindowHandle == windowHandle) return true;
            sessions[id] = new Observation("running", pid, created, active.Name, sessionId, windowHandle, active.SessionStart,
                active.Detected ?? active);
            if (game == id) ticket = null;
            return true;
        }
    }

    public Observation? Running(Guid id)
    {
        lock (gate) return sessions.TryGetValue(id, out var active) && active.State == "running" ? active : null;
    }

    public bool TryRestoreAutomatic(Guid id, Guid sessionId, Func<Observation, bool> confirm)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(id, out var active) || active.State != "running" || active.SessionId != sessionId)
                return false;
            var detected = active.Detected ?? active;
            if (!confirm(detected)) return false;
            sessions[id] = detected;
            if (game == id) ticket = null;
            return true;
        }
    }

    public void Stopped(Guid id)
    {
        lock (gate)
        {
            if (sessions.Remove(id)) sessionRevision = Guid.NewGuid().ToString("N");
            if (id == game)
            {
                starting = false;
                observation = new Observation("ended");
            }
        }
    }

    public Observation? Read(Guid id, string token)
    {
        lock (gate)
            return game == id && token == ticket && Stopwatch.GetTimestamp() - issued < 45L * Stopwatch.Frequency
                ? observation : null;
    }
}
