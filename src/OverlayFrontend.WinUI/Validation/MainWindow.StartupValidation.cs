using System.Text.Json.Nodes;

namespace WidgetRail.OverlayFrontend.WinUI
{
    public sealed partial class MainWindow
    {
        private async Task ValidateHiddenStartupAsync(string resultPath)
        {
            var checks = new JsonArray();
            var result = new JsonObject { ["pid"] = Environment.ProcessId, ["checks"] = checks };
            void Check(bool passed, string description)
            {
                if (!passed) throw new InvalidOperationException(description);
                checks.Add(description);
            }
            try
            {
                await Task.Delay(500);
                var page = (Shell.OverlayShellPage)RootFrame.Content;
                Check(startHidden && !AppWindow.IsVisible && desktopBackdrop?.IsVisible != true,
                    "Hidden startup leaves both native windows hidden");
                Check(page.NativeStartupIsIdle, "Hidden loaded tree does not start Bridge or widget initialization");
                Check(input is not null && !input.IsActive, "Guide adapter exists without visible navigation polling");
                ShowOverlay();
                await page.WaitForNativeStartupAsync().WaitAsync(TimeSpan.FromSeconds(45));
                Check(AppWindow.IsVisible && input!.IsActive, "First show activates window and navigation owner");
                Check(page.NativeStartupHasContent, "First show starts and commits the real initial widget");
                var identity = page.NativeStartupIdentity;
                HideOverlay();
                await Task.Delay(200);
                Check(!AppWindow.IsVisible && !input!.IsActive, "Hide suspends visible input");
                ShowOverlay();
                await Task.Delay(500);
                Check(page.NativeStartupIdentity == identity, "Reopen preserves the one Bridge startup");
                result["passed"] = true;
            }
            catch (Exception error) { result["passed"] = false; result["error"] = error.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
            await File.WriteAllTextAsync(resultPath, result.ToJsonString());
        }
    }
}

namespace WidgetRail.OverlayFrontend.WinUI.Shell
{
    internal sealed partial class OverlayShellPage
    {
        internal bool NativeStartupIsIdle => startup is null && owner is null;
        internal bool NativeStartupHasContent => owner is not null && activeWidget is not null && !switching;
        internal object? NativeStartupIdentity => owner;
        internal async Task WaitForNativeStartupAsync()
        {
            while (startup is null && !retired) await Task.Delay(20);
            if (startup is not null) await startup;
        }
    }
}
