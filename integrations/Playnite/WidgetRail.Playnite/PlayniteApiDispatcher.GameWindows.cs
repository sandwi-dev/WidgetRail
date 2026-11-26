using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

internal sealed partial class PlayniteApiDispatcher
{
    private string RulePath(Guid game) => Path.Combine(api.Paths.ExtensionsDataPath, IntegrationIdentity.PluginId,
        "game-windows", connection.ProfileId, game.ToString("N") + ".json");

    private GameWindowRule? ReadWindowRule(Guid game)
    {
        try { return IntegrationFiles.Read<GameWindowRule>(RulePath(game)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SerializationException)
        {
            global::Playnite.SDK.LogManager.GetLogger().Warn("The saved game window could not be read (" + error.GetType().Name + ").");
            return null;
        }
    }

    private void TrackGameWindow(Guid game, int pid, long created, GameWindowRule? remembered)
    {
        launchTracker.Started(game, pid, created, waitForWindow: remembered != null);
        if (remembered != null && launchTracker.Running(game) is { } run)
            _ = ResolveRememberedWindowAsync(game, run, remembered, lifetime.Token);
    }

    private async Task ResolveRememberedWindowAsync(Guid game, GameLaunchTracker.Observation run, GameWindowRule rule,
        CancellationToken token)
    {
        try
        {
            // Launchers often return before the game creates its first window.
            // Only launches with a saved choice get these bounded, off-UI observations.
            foreach (var delay in new[] { 0, 500, 1500, 3000, 5000, 10000 })
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                if (!ReferenceEquals(launchTracker.Running(game), run)) return;
                var match = await Task.Run(() => LiveGameWindow.FindUnique(rule), token).ConfigureAwait(false);
                if (match == null) continue;
                await OnUiAsync(() =>
                {
                    if (!ReferenceEquals(launchTracker.Running(game), run)) return false;
                    return BindGameWindow(game, run, match, remember: false);
                }, token).ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SerializationException or ArgumentException)
        { global::Playnite.SDK.LogManager.GetLogger().Warn("The remembered game window could not be restored (" + error.GetType().Name + ")."); }
    }

    private object GameWindowStatus(Guid game)
    {
        var current = launchTracker.Running(game) ?? throw new ApiException(409, "game_not_running");
        return new { session = current.SessionId.ToString("N"), remembered = ReadWindowRule(game) != null };
    }

    private object ChooseGameWindow(Guid game, Dictionary<string, object> body)
    {
        if (body.Count != 2 || !body.TryGetValue("session", out var session) ||
            !Guid.TryParseExact(session as string, "N", out var runId) || !body.TryGetValue("window", out var value) ||
            !(value is string selection)) throw new ApiException(400, "invalid_game_window");
        var current = launchTracker.Running(game);
        if (current == null || current.SessionId != runId) throw new ApiException(409, "game_session_changed");
        if (selection == "automatic")
        {
            if (!launchTracker.TryRestoreAutomatic(game, runId, detected =>
                ChangeWindowRule(game, null, () =>
                {
                    var now = UtcNow;
                    if (detected.ProcessId > 0 && detected.CreatedFileTime > 0)
                        return sessions.BindWindow(game, (uint)detected.ProcessId, (ulong)detected.CreatedFileTime, now, now);
                    sessions.Stop(game, now);
                    return true;
                }))) throw new ApiException(409, "game_session_changed");
            GameStateChanged(game);
        }
        else
        {
            if (selection.Length != 64 || !selection.All(ch => ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'f'))
                throw new ApiException(400, "invalid_game_window");
            var selected = LiveGameWindow.FindUnique(null, selection);
            if (selected == null || !BindGameWindow(game, current, selected, remember: true))
                throw new ApiException(409, "window_unavailable");
        }
        return GameWindowStatus(game);
    }

    private bool BindGameWindow(Guid game, GameLaunchTracker.Observation run, LiveGameWindow window, bool remember)
    {
        var live = window.Revalidate();
        if (live == null) return false;
        bool BindCapture() => sessions.BindWindow(game, (uint)live.ProcessId, (ulong)live.Created, run.SessionStart, UtcNow);
        if (!launchTracker.TrySetWindow(game, run.SessionId, live.ProcessId, live.Created, live.Handle,
            () => remember ? ChangeWindowRule(game, live.Rule, BindCapture) : BindCapture())) return false;
        GameStateChanged(game);
        return true;
    }

    private bool ChangeWindowRule(Guid game, GameWindowRule? rule, Func<bool> bind)
    {
        var path = RulePath(game);
        var previous = IntegrationFiles.Read<GameWindowRule>(path);
        if (rule == null && previous == null || rule?.Matches(previous) == true) return bind();
        void Write(GameWindowRule? next)
        {
            if (next != null) IntegrationFiles.Write(path, next);
            else File.Delete(path);
        }
        Write(rule);
        var applied = false;
        try { applied = bind(); return applied; }
        finally { if (!applied) Write(previous); }
    }
}
