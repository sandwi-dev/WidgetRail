using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.PlatformBroker;

internal sealed record CaptureFileIdentity(ulong VolumeSerialNumber, ulong FileIdLow, ulong FileIdHigh,
    long ChangeTime, long LastWriteTime, long Length);

/// <summary>Retains the original and its directory chain while another host process opens its path.</summary>
internal sealed class CapturePinnedFile : IDisposable
{
    private readonly List<SafeFileHandle> ancestors;
    internal FileStream Stream { get; }
    internal CaptureFileIdentity Identity { get; }
    private CapturePinnedFile(FileStream stream, List<SafeFileHandle> ancestors)
    { Stream = stream; this.ancestors = ancestors; Identity = ReadIdentity(stream); }
    internal static CapturePinnedFile Open(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new BrokerException("capture_library_codec_unavailable", "Pinned playback requires Windows file identity.");
        path = Path.GetFullPath(path);
        var handles = new List<SafeFileHandle>(); FileStream? stream = null;
        try
        {
            var parent = Path.GetDirectoryName(path) ?? throw new IOException("Missing media directory.");
            var chain = new Stack<string>();
            for (var directory = new DirectoryInfo(parent); directory != null; directory = directory.Parent) chain.Push(directory.FullName);
            if (chain.Count > 256) throw new IOException("Media path is too deep.");
            while (chain.TryPop(out var directory))
            {
                var handle = CreateFile(directory, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Could not retain media directory.", new Win32Exception(Marshal.GetLastWin32Error())); }
                handles.Add(handle);
                var info = Basic(handle);
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.Attributes & (uint)FileAttributes.Directory) == 0)
                    throw new IOException("Media directory contains a reparse point.");
            }
            // Deny writes and deletion of the actual original for the complete playback lease.
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            if ((Basic(stream.SafeFileHandle).Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new IOException("Media is a reparse point.");
            return new(stream, handles);
        }
        catch { stream?.Dispose(); foreach (var handle in handles) handle.Dispose(); throw; }
    }
    internal static CaptureFileIdentity ReadIdentity(FileStream stream)
    {
        var basic = Basic(stream.SafeFileHandle);
        if (!GetId(stream.SafeFileHandle, 18, out var id, Marshal.SizeOf<FileIdInfo>())) throw new IOException("Could not read media identity.", new Win32Exception(Marshal.GetLastWin32Error()));
        return new(id.Volume, id.Low, id.High, basic.ChangeTime, basic.LastWriteTime, stream.Length);
    }
    private static FileBasicInfo Basic(SafeFileHandle handle)
    {
        if (!GetBasic(handle, 0, out var info, Marshal.SizeOf<FileBasicInfo>())) throw new IOException("Could not read media identity.", new Win32Exception(Marshal.GetLastWin32Error()));
        return info;
    }
    public void Dispose() { Stream.Dispose(); foreach (var handle in ancestors) handle.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct FileIdInfo { internal ulong Volume, Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct FileBasicInfo { internal long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; internal uint Attributes; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetId(SafeFileHandle file, int informationClass, out FileIdInfo information, int size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetBasic(SafeFileHandle file, int informationClass, out FileBasicInfo information, int size);
}
