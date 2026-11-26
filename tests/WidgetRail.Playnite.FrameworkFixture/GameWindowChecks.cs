using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WidgetRail.Playnite;

internal static partial class Program
{
    private static int RunGameWindowChild()
    {
        var window = new Window { Title = "WidgetRail game-window check", Width = 300, Height = 100, ShowActivated = false };
        window.Show();
        Console.WriteLine(new WindowInteropHelper(window).Handle.ToInt64());
        Console.Out.Flush();
        var ui = window.Dispatcher;
        Task.Run(() => { Console.ReadLine(); ui.BeginInvokeShutdown(DispatcherPriority.Background); });
        Dispatcher.Run();
        return 0;
    }

    private sealed class WindowChild : IDisposable
    {
        private readonly Process process;
        internal LiveGameWindow Window { get; private set; } = null!;
        private WindowChild(Process process) => this.process = process;
        internal static async Task<WindowChild> Start()
        {
            var child = new WindowChild(Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location, "--game-window-child")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true }));
            try
            {
                var ready = child.process.StandardOutput.ReadLineAsync();
                if (await Task.WhenAny(ready, Task.Delay(8000)) != ready || !long.TryParse(await ready, out var handle))
                    throw new InvalidOperationException("Native child window did not open.");
                child.Window = LiveGameWindow.Inspect(new IntPtr(handle)) ?? throw new InvalidOperationException("Native window was ineligible.");
                return child;
            }
            catch { child.Dispose(); throw; }
        }
        public void Dispose()
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(4000)) { process.Kill(); process.WaitForExit(2000); }
            process.Dispose();
        }
    }

    private static async Task VerifyGameWindows(PlayniteApiDispatcher api, Guid game)
    {
        var path = "/v1/games/" + game.ToString("D") + "/window";
        async Task<string> Session() => (string)Json(await api.DispatchAsync(Request("GET", path), default))["session"];
        Task<LocalApiResponse> Set(string session, string window) => api.DispatchAsync(Request("PUT", path,
            new JavaScriptSerializer().Serialize(new { session, window }), Guid.NewGuid().ToString("D")), default);
        async Task<string?> RunningProcess()
        {
            var state = Json(await api.DispatchAsync(Request("GET", "/v1/events", query: new System.Collections.Specialized.NameValueCollection
                { ["after"] = "now", ["wait"] = "0" }), default));
            var processes = (System.Collections.Generic.Dictionary<string, object>)state["runningProcesses"];
            return processes.TryGetValue(game.ToString("D"), out var key) ? (string)key : null;
        }
        string firstRun;
        int firstPid;
        using (var child = await WindowChild.Start())
        {
            api.CaptureGameStarting(game);
            api.CaptureGameStarted(game, 0);
            firstRun = await Session();
            firstPid = child.Window.ProcessId;
            Check((await Set(firstRun, child.Window.Key)).Status == 200 && await RunningProcess() != null,
                "Choosing the actual window replaces the missing launcher identity in the live game feed");
            using (var duplicate = await WindowChild.Start())
                Check(LiveGameWindow.FindUnique(child.Window.Rule) == null && LiveGameWindow.FindUnique(null, duplicate.Window.Key)?.Handle == duplicate.Window.Handle,
                    "Two visible windows from identical executables require a choice; exact selection distinguishes them");
            api.CaptureGameStopped(game);
        }
        using (var next = await WindowChild.Start())
        {
            api.CaptureGameStarting(game);
            api.CaptureGameStarted(game, 0);
            var session = await Session();
            for (var i = 0; i < 60 && await RunningProcess() == null; i++) await Task.Delay(80);
            var expected = new GameLaunchTracker();
            expected.Starting(game);
            expected.Started(game, next.Window.ProcessId, next.Window.Created);
            Check(firstPid != next.Window.ProcessId && firstRun != session && await RunningProcess() == expected.Running(game)!.ProcessKey,
                "A remembered rule binds the new visible process on a relaunch with a different PID");
            Check((await Set(firstRun, next.Window.Key)).Status == 409, "A stale picker cannot change the next game run");
            string staleKey;
            using (var gone = await WindowChild.Start()) staleKey = gone.Window.Key;
            Check((await Set(session, staleKey)).Status == 409, "A closed window cannot be confirmed");
            Check((await Set(session, "automatic")).Status == 200 && await RunningProcess() == null,
                "Clearing a remembered rule restores automatic detection for the current run");
            api.CaptureGameStopped(game);
        }
    }
}
