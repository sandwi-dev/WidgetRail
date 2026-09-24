using System.Diagnostics;

namespace WidgetRail.WindowsSpotifyProvider;

internal sealed class SpotifyPlaybackProfileDirectory : IDisposable
{
    private const string DirectoryPrefix = "WidgetRail.SpotifyPlayback.";
    private static readonly TimeSpan StaleAge = TimeSpan.FromDays(1);
    private readonly string _path;

    internal SpotifyPlaybackProfileDirectory(string? temporaryRoot = null)
    {
        var root = System.IO.Path.GetFullPath(temporaryRoot ?? System.IO.Path.GetTempPath());
        Directory.CreateDirectory(root);
        ScavengeStaleDirectories(root);
        _path = System.IO.Path.Combine(root, $"{DirectoryPrefix}{Environment.ProcessId}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_path);
    }

    internal string RootPath => _path;

    public void Dispose()
    {
        TryDeleteWithRetry(_path);
    }

    private static void ScavengeStaleDirectories(string root)
    {
        try
        {
            var tempRoot = new DirectoryInfo(root);
            foreach (var candidate in tempRoot.EnumerateDirectories(DirectoryPrefix + "*",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if ((candidate.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        DateTime.UtcNow - candidate.CreationTimeUtc < StaleAge ||
                        !HasExitedOwner(candidate.Name))
                        continue;
                    TryDeleteWithRetry(candidate.FullName);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool HasExitedOwner(string name)
    {
        // Prefix matches alone are insufficient deletion authority. Accept only the
        // exact generated PID/GUID shape, and preserve even an unrelated reused PID.
        var suffix = name.AsSpan(DirectoryPrefix.Length);
        var separator = suffix.IndexOf('.');
        if (separator <= 0 || !int.TryParse(suffix[..separator], out var ownerId) || ownerId <= 0 ||
            !Guid.TryParseExact(suffix[(separator + 1)..], "N", out _)) return false;
        try { using var owner = Process.GetProcessById(ownerId); return owner.HasExited; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static void TryDeleteWithRetry(string path)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return;
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(50 << attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 3)
            {
                Thread.Sleep(50 << attempt);
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }
    }
}
