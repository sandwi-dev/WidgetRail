using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

internal sealed partial class PlayniteApiDispatcher
{
    private CaptureCompanionCoordinator captureCompanions=null!;
    private GameCaptureSessions sessions = null!;
    private readonly GameLaunchTracker launchTracker = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,long> launches = new();
    private static long UtcNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private void StartCaptureCompanions()
    {
        var settings=Path.Combine(api.Paths.ExtensionsDataPath,IntegrationIdentity.PluginId,"capture-context-roots.json");
        sessions = new(Path.Combine(Path.GetDirectoryName(settings)!, "capture-sessions", connection.ProfileId));
        captureCompanions=new CaptureCompanionCoordinator(settings,connection.ProfileId,ReadCaptureCompanionSnapshotAsync);
        achievements.CaptureStateChanged+=captureCompanions.Invalidate;
    }
    internal void CaptureGameStarting(Guid game, string? name = null)
    {
        launchTracker.Starting(game, name);
        foreach (var stale in launches.Where(item => UtcNow - item.Value > 3600000))
            ((ICollection<KeyValuePair<Guid,long>>)launches).Remove(stale);
        launches[game] = UtcNow;
    }
    internal void CaptureGameStarted(Guid game, int pid, string? installDirectory = null)
    {
        var captureRequested = launches.TryRemove(game, out var requested);
        var processId = 0;
        long created = 0;
        try
        {
            using var process = pid > 0 ? CaptureLaunchProcess.Resolve(pid, installDirectory) : null;
            if (process is null) return;
            var started = new DateTimeOffset(process.StartTime.ToUniversalTime());
            created = started.UtcDateTime.ToFileTimeUtc();
            processId = process.Id;
            if (!captureRequested) return;
            // Framework UtcNow can lag the kernel's process timestamp by a clock tick.
            // Reading StartTime on the live process itself establishes this lower bound.
            var observed = Math.Max(UtcNow, started.ToUnixTimeMilliseconds());
            sessions.Start(game, (uint)process.Id, (ulong)started.UtcDateTime.ToFileTimeUtc(), requested, observed);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or System.Management.ManagementException or System.Runtime.Serialization.SerializationException)
        { global::Playnite.SDK.LogManager.GetLogger().Warn("Capture session could not be identified (" + error.GetType().Name + ")."); }
        finally { launchTracker.Started(game, processId, created); }
    }
    internal void CaptureGameStopped(Guid game)
    {
        launchTracker.Stopped(game);
        launches.TryRemove(game, out _);
        try { sessions.Stop(game, UtcNow); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.Serialization.SerializationException)
        { global::Playnite.SDK.LogManager.GetLogger().Warn("Capture session could not be saved."); }
    }
    internal void GameStartCancelled(Guid game)
    {
        launches.TryRemove(game, out _);
        launchTracker.Stopped(game);
    }
    private object CaptureIntegration(string? gameId = null)
    {
        var game = gameId is null ? null : Guid.TryParseExact(gameId, "D", out var id) ? RequireGame(id) : throw new ApiException(400, "invalid_game_id");
        var history = Array.Empty<GameCaptureSession>();
        if (game is not null)
        {
            try { history = sessions.Snapshot(game.Id, UtcNow); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.Serialization.SerializationException)
            { global::Playnite.SDK.LogManager.GetLogger().Warn("Capture session history could not be read."); }
        }
        return new { profileId = connection.ProfileId, roots = captureCompanions.Roots,
            providerRoots = achievements.CaptureConfiguration().Roots.Take(256).ToArray(), sessionGameId = game?.Id,
            sessions = history };
    }
    private object ConfigureCaptureIntegration(Dictionary<string,object> body)
    {
        if(body.Count!=2||!body.TryGetValue("path",out var path)||!(path is string value)||!body.TryGetValue("enabled",out var enabled)||!(enabled is bool active))
            throw new ApiException(400,"invalid_capture_integration");
        var canonical=active?CaptureCompanionCoordinator.Canonical(value):captureCompanions.Roots.FirstOrDefault(root=>string.Equals(root,value,StringComparison.OrdinalIgnoreCase));
        if(canonical==null){if(!active)return CaptureIntegration();throw new ApiException(400,"invalid_capture_directory");}
        if(active&&!achievements.CaptureConfiguration().Roots.Contains(canonical,StringComparer.OrdinalIgnoreCase))throw new ApiException(409,"capture_root_not_configured");
        captureCompanions.Configure(canonical,active);
        return CaptureIntegration();
    }
    private async Task<CaptureCompanionSnapshot> ReadCaptureCompanionSnapshotAsync(string[] selected,CancellationToken token)
    {
        var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var root in selected)
        {
            token.ThrowIfCancellationRequested();
            if(CaptureCompanionCoordinator.Canonical(root)==null)continue;
            folders.Add(Path.GetFileName(root));
            foreach(var directory in Directory.EnumerateDirectories(root).Take(10000))folders.Add(Path.GetFileName(directory));
        }
        var configuration=await OnUiAsync(()=>achievements.CaptureConfiguration(),token,System.Windows.Threading.DispatcherPriority.Background).ConfigureAwait(false);
        var candidates=await OnUiAsync(()=>achievements.CaptureCandidateGames(selected,folders,configuration),token,System.Windows.Threading.DispatcherPriority.Background).ConfigureAwait(false);
        var games=new List<CaptureCompanionGame>();var rows=0;
        foreach(var id in candidates)
        {
            var game=await OnUiAsync(()=>achievements.CaptureGame(id,configuration),token,System.Windows.Threading.DispatcherPriority.Background).ConfigureAwait(false);
            if(game==null)continue;
            rows+=game.Achievements.Length;if(rows>100000)break;
            games.Add(game);
        }
        return new CaptureCompanionSnapshot{Roots=configuration.Roots,Suffixes=configuration.Suffixes,Folders=folders.ToArray(),Games=games.ToArray()};
    }
}
