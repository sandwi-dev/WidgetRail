using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void EnableCaptureRevealValidation(string output)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        var text = new TextBlock { Text = "Checking capture reveal…", Margin = new(24) };
        RootFrame.Content = text;
        var started = false;
        text.Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>(); string? error = null;
            var folder = Path.Combine(directory, "capture-reveal-probe");
            var fileName = "capture, example.png";
            try
            {
                Directory.CreateDirectory(folder);
                await File.WriteAllBytesAsync(Path.Combine(folder, fileName), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWZkAAAAASUVORK5CYII="));
                Check(!LaunchCaptureFolderInExplorer(folder, "../outside.png"), "Reveal rejects path traversal");
                Check(!LaunchCaptureFolderInExplorer(folder, "missing.png"), "Reveal rejects a missing file");
                Check(LaunchCaptureFolderInExplorer(folder, fileName), "Explorer accepts the exact capture file including spaces and comma");
                text.Text = "Capture reveal launched; verify the selected Explorer item.";
            }
            catch (Exception failure) { error = failure.ToString(); text.Text = error; }
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = error is null, checks, folder, fileName, error }, new JsonSerializerOptions { WriteIndented = true }));
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        };
    }
}
