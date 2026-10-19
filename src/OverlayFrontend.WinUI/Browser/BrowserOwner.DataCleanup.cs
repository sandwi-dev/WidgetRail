using Microsoft.UI.Xaml;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    private BrowserProfileLifecycleStore profileLifecycle = null!;
    private FileSystemWatcher? profileWatcher;
    private DispatcherTimer? profileCleanupRetry;
    private bool profileLifecycleDirty = true;
    private (Type Type, int HResult)? lastCleanupFailure;
    private void InitializeProfileLifecycle(string settingsRoot)
    {
        profileLifecycle = new(settingsRoot);
        Directory.CreateDirectory(profileLifecycle.DirectoryPath);
        profileWatcher = new(profileLifecycle.DirectoryPath, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        void Changed(object? sender, FileSystemEventArgs args) => parking.DispatcherQueue.TryEnqueue(() => { profileLifecycleDirty = true; Refresh(); });
        profileWatcher.Changed += Changed; profileWatcher.Created += Changed; profileWatcher.Renamed += (s, e) => Changed(s, e);
        profileWatcher.Error += (_, _) => parking.DispatcherQueue.TryEnqueue(() => { profileLifecycleDirty = true; Refresh(); });
        profileWatcher.EnableRaisingEvents = true;
        profileCleanupRetry = new() { Interval = TimeSpan.FromSeconds(30) };
        profileCleanupRetry.Tick += (_, _) => { profileCleanupRetry.Stop(); profileLifecycleDirty = true; Refresh(); };
        parking.DispatcherQueue.TryEnqueue(Refresh);
    }
    private async Task ReconcileProfileCleanupAsync()
    {
        if (!profileLifecycleDirty || retired) return;
        profileLifecycleDirty = false;
        try
        {
            foreach (var owner in await profileLifecycle.ReadAllAsync(lifetime.Token))
            {
                foreach (var pending in owner.Pending)
                {
                    foreach (var pair in entries.Where(pair => pair.Key.Widget == owner.WidgetId && pair.Value.Surface.ProfileName == pending.Name).ToArray())
                        await RetireAsync(pair.Key, pair.Value);
                    await Task.WhenAll(retirements.Where(pair => pair.Value == owner.WidgetId).Select(pair => pair.Key).ToArray());
                    await Extensions.DeleteProfileAsync(pending.Name, lifetime.Token);
                    if (pending.LibraryFile is { } library)
                    {
                        // The store admits only a flat filename; never follow a redirected directory or file.
                        var folder = Path.Combine(profileRoot, "Library");
                        var path = Path.Combine(folder, library);
                        for (var part = new DirectoryInfo(folder); part is not null; part = part.Parent)
                            if (part.Exists && part.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Browser library path is redirected.");
                        if (File.Exists(path))
                        {
                            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Browser library file is redirected.");
                            File.Delete(path);
                        }
                    }
                    await profileLifecycle.CompleteAsync(owner.WidgetId, pending, lifetime.Token);
                }
            }
            lastCleanupFailure = null;
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var identity = (error.GetType(), error.HResult);
            if (lastCleanupFailure != identity) Diagnostics.FrontendFailureLog.Current.Write("browser-profile-cleanup", error);
            lastCleanupFailure = identity;
            if (!retired) profileCleanupRetry?.Start();
        }
    }
}
