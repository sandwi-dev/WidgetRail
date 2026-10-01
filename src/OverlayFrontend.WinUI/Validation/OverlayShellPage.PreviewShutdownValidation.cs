using System.Text.Json;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePreviewShutdownValidation(string resultPath, string peerPath, Action? hide)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            object? capture = null;
            string? error = null;
            try
            {
                using var peer = JsonDocument.Parse(File.ReadAllText(peerPath));
                var target = peer.RootElement.GetProperty("target").Deserialize<TaskWindowTarget>()!;
                Check(new WindowsTaskWindowActivation().IsCurrent(target), "Owned source identity is current");
                if (startup is not null) await startup;
                await SelectAsync("task-switcher", true);
                var deadline = Environment.TickCount64 + 15000;
                while (true)
                {
                    var frame = retainedSurfaces.GetValueOrDefault("task-switcher")?.Frame;
                    var id = frame?.WindowPreviews.FirstOrDefault(pair => pair.Value.Handle == target.Handle && pair.Value.ProcessId == target.ProcessId).Key;
                    var samples = previewRenderer?.ShutdownValidationSamples() ?? [];
                    var sample = samples.FirstOrDefault(value => value.WindowId == id);
                    capture = new { id, samples = samples.Select(value => new { value.WindowId, value.Stats.State, value.Stats.Error, value.Stats.Frames, value.Stats.ActiveCount }).ToArray() };
                    if (!switching && id is not null && sample.Stats.State == 2 && sample.Stats.Frames > 0)
                    {
                        capture = new { id, sample.Stats.Frames, sample.Stats.ActiveCount, sample.Stats.TotalBytes, target };
                        Check(true, "Actual Task Switcher has live captured frames from the owned source");
                        break;
                    }
                    if (Environment.TickCount64 >= deadline) throw new TimeoutException("Task Switcher owned-source capture did not become live");
                    await Task.Delay(50);
                }
                if (hide is not null)
                {
                    hide();
                    await Task.Delay(250);
                    Check(!visible && !App.Window.AppWindow.IsVisible, "Overlay is hidden before orderly close");
                }
                else Check(visible && App.Window.AppWindow.IsVisible, "Task Switcher remains visible until orderly close");
            }
            catch (Exception failure) { error = failure.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { prepared = error is null, checks, capture, error }));
            void Check(bool valid, string text) { if (!valid) throw new InvalidOperationException(text); checks.Add(text); }
        };
    }
}
