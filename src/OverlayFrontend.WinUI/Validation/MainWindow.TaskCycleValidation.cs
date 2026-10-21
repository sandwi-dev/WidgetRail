using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WindowsWindowActivation;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void EnableTaskCycleValidation(OverlayShellPage page, string path, string targets)
    {
        var sequence = 0;
        var folderInput = Environment.GetCommandLineArgs().Contains("--task-cycle-folder-input");
        var folderScenario = folderInput || Environment.GetCommandLineArgs().Contains("--task-cycle-folder");
        async Task Reopen()
        {
            ShowOverlay();
            var until = Environment.TickCount64 + 4000;
            while (input?.IsForeground != true || input.IsActive != true)
            { if (Environment.TickCount64 >= until) throw new TimeoutException("Host foreground reentry failed."); await Task.Delay(20); }
        }
        async Task OpenFolder()
        {
            var folderPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "empty-folder-fixture");
            Directory.CreateDirectory(folderPath);
            // The widget input path refreshes the broker before the host's OS launch.
            page.RefreshHostTaskActivationPermission();
            if (!await OpenExternalAsync(page, () => Task.FromResult(LaunchCaptureFolderInExplorer(folderPath)), CancellationToken.None))
                throw new InvalidOperationException("Fixture folder launch failed.");
            // Process dispatch can return before Explorer creates/activates its
            // folder window. Let that external launch settle before reopening.
            await Task.Delay(2000);
            if (WindowsTaskWindowActivation.Observe(0).ForegroundProcess == Environment.ProcessId)
                throw new InvalidOperationException("Folder launch did not leave the host.");
            await Reopen();
        }
        page.EnableTaskCycleValidation(path, targets, Reopen, folderScenario && !folderInput ? null : async () =>
        {
            if (AppWindow.IsVisible || WindowsTaskWindowActivation.Observe(0).ForegroundProcess == Environment.ProcessId)
                throw new InvalidOperationException("External-input fixture requires the host to be hidden.");
            var previous = WindowsTaskWindowActivation.Observe(0).LastInputTick;
            // Input must originate outside this process: SendInput by the host
            // would itself confer foreground eligibility and mask the defect.
            var request = (++sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var signal = path + ".input";
            File.WriteAllText(signal + "." + request, "");
            var until = Environment.TickCount64 + 5000;
            while (!File.Exists(signal + ".done." + request))
            { if (Environment.TickCount64 >= until) throw new TimeoutException("External input helper did not respond."); await Task.Delay(20); }
            if (WindowsTaskWindowActivation.Observe(0).LastInputTick == previous)
                throw new InvalidOperationException("External input did not update the Windows input timestamp.");
        }, folderScenario ? OpenFolder : null);
    }
}
