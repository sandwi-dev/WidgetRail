using System.Text.Json.Nodes;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Validation-only and opt-in. Observes one native history request, qualifies
    // one anonymous poster and writes only fixed categories plus media metrics.
    internal void EnableDiscordPosterProbe(string output)
    {
        output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new InvalidOperationException("Choose a fresh poster evidence path.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (FrontendArguments.Value(Environment.GetCommandLineArgs(), "--discord-messaging-probe") is not null)
        {
            File.WriteAllText(output, BrowserSurface.PosterProbeResult("probe_conflict").ToJsonString()); return;
        }
        var timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(1);
        var samples = 0; var busy = false; var finished = false;
        timer.Tick += async (_, _) =>
        {
            if (finished || retired) { timer.Stop(); return; }
            if (busy) return;
            if (++samples > 900)
            {
                Finish(BrowserSurface.PosterProbeResult("expired")); return;
            }
            if (browserOwner is null) return;
            busy = true;
            try
            {
                var result = await browserOwner.ReadDiscordPosterProbeAsync(activeWidget, lifetime.Token);
                if (result["category"]?.GetValue<string>() != "waiting") Finish(result);
            }
            catch { Finish(BrowserSurface.PosterProbeResult("probe_failed")); }
            finally { busy = false; }
        };
        Loaded += (_, _) => { if (!finished) timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
        if (IsLoaded) timer.Start();

        void Finish(JsonObject result)
        {
            finished = true; timer.Stop();
            try { File.WriteAllText(output, result.ToJsonString()); }
            catch { /* No raw path/exception detail enters diagnostics. */ }
        }
    }
}
