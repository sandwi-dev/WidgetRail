using WidgetRail.PlatformBroker;

namespace WidgetRail.WidgetBridge;

internal static class CaptureEditPreviewFiles
{
    private static string Root => Path.Combine(Path.GetTempPath(), "WidgetRail", "CaptureEditPreviews");
    internal static async Task<CaptureEditPreview> CreateAsync(CaptureEditInput input, CancellationToken token)
    {
        if (input.Media.ContentType == "video/mp4") return new(new Uri(input.Path).AbsoluteUri);
        PlainPath(Root);
        Directory.CreateDirectory(Root);
        if (Directory.EnumerateDirectories(Root).Take(32).Count() >= 32)
            throw new BrokerException("capture_library_edit_busy", "Editor preview cleanup is still pending.");
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        PlainPath(directory);
        Directory.CreateDirectory(directory);
        var lifetime = new PreviewLifetime(directory);
        try
        {
            var file = Path.Combine(directory, "preview.png");
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
                65536, FileOptions.Asynchronous))
                await WindowsCaptureEditCodec.CreateImagePreviewAsync(input, output, token).ConfigureAwait(false);
            return new(new Uri(file).AbsoluteUri, lifetime);
        }
        catch { lifetime.Dispose(); throw; }
    }
    internal static void CleanupExpired()
    {
        if (!Directory.Exists(Root)) return;
        try
        {
            PlainPath(Root);
            foreach (var directory in Directory.EnumerateDirectories(Root).Take(128))
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                    Directory.GetLastWriteTimeUtc(directory) > DateTime.UtcNow.AddHours(-1)) continue;
                try
                {
                    PlainPath(directory); PlainPath(Path.Combine(directory, "preview.png")); PlainPath(Path.Combine(directory, ".owner"));
                    using (new FileStream(Path.Combine(directory, ".owner"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        File.Delete(Path.Combine(directory, "preview.png"));
                    File.Delete(Path.Combine(directory, ".owner")); Directory.Delete(directory);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private static void PlainPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Editor preview storage cannot use a reparse point.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
    }
    private sealed class PreviewLifetime : IDisposable
    {
        private readonly string directory;
        private CapturePinnedFile? owner;
        private int disposed;
        internal PreviewLifetime(string directory)
        {
            this.directory = directory;
            var path = Path.Combine(directory, ".owner"); PlainPath(path);
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            owner = CapturePinnedFile.Open(path);
        }
        public void Dispose()
        { if (Interlocked.Exchange(ref disposed, 1) == 0) _ = CleanupAsync(); }
        private async Task CleanupAsync()
        {
            // Keep the URI valid while the host observes terminal status and closes its decoder.
            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            try
            {
                for (var attempt = 0; attempt < 6; ++attempt)
                {
                    try { PlainPath(directory); PlainPath(Path.Combine(directory, "preview.png")); File.Delete(Path.Combine(directory, "preview.png")); break; }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                }
            }
            finally
            {
                Interlocked.Exchange(ref owner, null)?.Dispose();
                try { PlainPath(directory); if (!File.Exists(Path.Combine(directory, "preview.png"))) { PlainPath(Path.Combine(directory, ".owner")); File.Delete(Path.Combine(directory, ".owner")); Directory.Delete(directory); } }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
