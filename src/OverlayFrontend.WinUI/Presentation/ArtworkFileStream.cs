using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal static class ArtworkFileStream
{
    private static readonly SemaphoreSlim Opens = new(4, 4);

    internal static async Task<(FileStream Stream, long Length)> OpenAsync(ArtworkFileReference source, CancellationToken token)
    {
        long length = 0;
        var stream = await OpenAsync(() => OpenCore(source, out length), token).ConfigureAwait(false);
        return (stream, length);
    }

    internal static async Task<FileStream> OpenAsync(Func<FileStream> open, CancellationToken token)
    {
        await Opens.WaitAsync(token).ConfigureAwait(false);
        var operation = Task.Run(() =>
        {
            try { token.ThrowIfCancellationRequested(); return open(); }
            finally { Opens.Release(); }
        });
        try { return await operation.WaitAsync(token).ConfigureAwait(false); }
        catch
        {
            // CreateFile can be slow on removable/cloud-backed drives. A retired
            // view must not wait for it or retain the stream that eventually opens.
            _ = operation.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    internal static FileStream Open(ArtworkFileReference source) => OpenCore(source, out _);

    private static FileStream OpenCore(ArtworkFileReference source, out long length)
    {
        if (source.PackageRelative || !ArtworkFileContract.IsValid(source))
            throw new InvalidDataException("Artwork file authority is invalid.");
        var file = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
        try
        {
            length = file.Length;
            if (length is <= 0 or > ProtocolConstants.MaximumEncodedArtworkBytes)
                throw new InvalidDataException("Artwork exceeds its encoded resource bound.");
            // Full-access local files need only the existing image bounds. Package
            // references additionally verify the opened target, preventing link swaps.
            if (source.AllowedRoot is { } root)
            {
                var final = FinalPath(file.SafeFileHandle);
                var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
                if (!final.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Artwork escaped its admitted package.");
            }
            return file;
        }
        catch { file.Dispose(); throw; }
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var path = new StringBuilder(1024);
        var length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
        if (length >= path.Capacity && length < 32768)
        {
            path.EnsureCapacity(checked((int)length + 1));
            length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
        }
        if (length == 0 || length >= path.Capacity)
            throw new IOException("Could not resolve the opened artwork file.", new Win32Exception(Marshal.GetLastWin32Error()));
        var value = path.ToString();
        return value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
}
