namespace WidgetRail.SpotifyPlaybackHost;

internal sealed class EphemeralUserDataDirectory : IDisposable
{
    private const string DirectoryPrefix = "WidgetRail.SpotifyPlayback.";
    private static readonly TimeSpan StaleAge = TimeSpan.FromDays(1);
    private readonly string _path;

    internal EphemeralUserDataDirectory()
    {
        ScavengeStaleDirectories();
        _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"{DirectoryPrefix}{Environment.ProcessId}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_path);
    }

    internal string RootPath => _path;

    public void Dispose()
    {
        TryDeleteWithRetry(_path);
    }

    private static void ScavengeStaleDirectories()
    {
        try
        {
            var tempRoot = new DirectoryInfo(System.IO.Path.GetTempPath());
            foreach (var candidate in tempRoot.EnumerateDirectories(DirectoryPrefix + "*",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if ((candidate.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        DateTime.UtcNow - candidate.CreationTimeUtc < StaleAge)
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
