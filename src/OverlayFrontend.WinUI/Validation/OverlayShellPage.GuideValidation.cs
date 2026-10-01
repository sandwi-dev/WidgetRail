using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableGuideHandoffValidation(string path, Func<bool> ownsForeground)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            var sequences = new List<object>();
            var original = Appearance.WidgetSwitcher;
            try
            {
                if (startup is not null) await startup;
                Appearance = Appearance with { WidgetSwitcher = WidgetSwitcherLayout.Radial };
                await SelectAsync("games-apps", true);
                await Until(() => MainFocusEnabled && ownsForeground() && !trayGuide.HasPendingHints);
                await Task.Delay(180);
                for (var cycle = 0; cycle < 5; ++cycle)
                {
                    await Handoff(true, cycle);
                    await Handoff(false, cycle);
                }
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }
            finally { Appearance = Appearance with { WidgetSwitcher = original }; }

            async Task Handoff(bool radial, int cycle)
            {
                var before = Guide();
                var frames = new List<string> { before };
                // Observe actual arranged chips, not controller authority or
                // guide proposals. No session locks inside XAML Rendering.
                void Observe(object? sender, object args)
                {
                    var shown = Guide();
                    if (frames.Count < 100 && shown != frames[^1]) frames.Add(shown);
                }
                CompositionTarget.Rendering += Observe;
                try
                {
                    var inputFrame = ControllerFrame.Create();
                    inputFrame.Connected = 1;
                    inputFrame.PressedButtons = inputFrame.State.Buttons = 0x2000;
                    Receive(inputFrame);
                    await Task.Delay(45);
                    inputFrame.PressedButtons = inputFrame.State.Buttons = 0;
                    inputFrame.ReleasedButtons = 0x2000;
                    Receive(inputFrame);
                    await Until(() => !switching && RadialOpen == radial && interactive != radial && !trayGuide.HasPendingHints);
                    await Task.Delay(180);
                }
                finally { CompositionTarget.Rendering -= Observe; }
                var after = Guide();
                sequences.Add(new { cycle, radial, before, after, frames });
                if (frames.Any(frame => frame != before && frame != after) || frames.Count > 2)
                    throw new InvalidOperationException("Guide displayed an intermediate or reversing state during " + (radial ? "radial entry" : "widget return"));
                checks.Add($"Cycle {cycle} {(radial ? "radial entry" : "widget return")} has one complete visible guide transition");
            }
            string Guide() => string.Join(" | ", trayGuide.DisplayedHints.Select(hint => hint.Prompt + ":" + hint.Label));
            async Task Until(Func<bool> ready)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!ready())
                {
                    if (validationFailure is not null || RecoveryVisible) throw new InvalidOperationException("Widget failed", validationFailure);
                    if (Environment.TickCount64 >= deadline) throw new TimeoutException("Guide handoff did not settle");
                    await Task.Delay(20);
                }
            }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, sequences, error }));
            }
        };
    }
}
