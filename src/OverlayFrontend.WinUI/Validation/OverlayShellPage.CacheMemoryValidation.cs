using System.Diagnostics;
using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Explicit validation mode only. Bounded, single-command-at-a-time probes
    // compare the same active UI before/after evicting hidden navigation views.
    internal void EnableCacheMemoryValidation(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        var ticks = 0; var busy = false;
        timer.Tick += async (_, _) =>
        {
            if (++ticks > 2400 || retired) { timer.Stop(); return; }
            if (busy || !File.Exists(output + ".request")) return;
            busy = true;
            try
            {
                var request = File.ReadAllText(output + ".request").Trim();
                File.Delete(output + ".request");
                if (request.Length > 80) throw new InvalidOperationException("Invalid memory probe request.");
                var command = request.Split(':', 2)[0];
                if (command is not ("sample" or "raw" or "evict")) throw new InvalidOperationException("Unknown memory probe command.");
                var before = WidgetViewPresenter.NavigationCacheInventoryForValidation();
                var evicted = command == "evict" ? await WidgetViewPresenter.EvictNavigationCachesForValidationAsync() : 0;
                // Apply the same collection/finalizer settling to both samples.
                // Do not trim the process working set or manufacture memory pressure.
                for (var pass = 0; command != "raw" && pass < 2; ++pass)
                {
                    await Task.Run(() => { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers(); });
                    await Task.Delay(1000);
                }
                using var process = Process.GetCurrentProcess(); process.Refresh();
                var record = JsonSerializer.Serialize(new { request, time = DateTimeOffset.UtcNow, activeWidget, before, evicted,
                    after = WidgetViewPresenter.NavigationCacheInventoryForValidation(),
                    privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64,
                    managedBytes = GC.GetTotalMemory(false), heapBytes = GC.GetGCMemoryInfo().HeapSizeBytes,
                    gcCommittedBytes = GC.GetGCMemoryInfo().TotalCommittedBytes, gcFragmentedBytes = GC.GetGCMemoryInfo().FragmentedBytes });
                File.AppendAllText(output, record + Environment.NewLine);
                File.WriteAllText(output + ".last.json", record);
            }
            catch (Exception error) { File.WriteAllText(output + ".last.json", JsonSerializer.Serialize(new { error = error.ToString() })); }
            finally { busy = false; }
        };
        Loaded += (_, _) => timer.Start(); Unloaded += (_, _) => timer.Stop();
        if (IsLoaded) timer.Start();
    }
}
