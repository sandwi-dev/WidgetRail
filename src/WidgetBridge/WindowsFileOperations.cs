using System.Runtime.InteropServices;

namespace WidgetRail.WidgetBridge;

/// <summary>Internal OS primitive. Widget-facing callers must first resolve and authorize their own file IDs.</summary>
internal static class WindowsFileOperations
{
    // ITransferSource has a dedicated recycle operation. Do not fall back to
    // RemoveItem, File.Delete, or SHFileOperation when the volume cannot recycle.
    internal static Task<bool> RecycleAsync(string path, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || Directory.Exists(path)) return Task.FromResult(false);
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return Task.FromResult(false);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { token.ThrowIfCancellationRequested(); completion.TrySetResult(Recycle(path, token)); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
            catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException)
            { completion.TrySetResult(false); }
        }) { IsBackground = true, Name = "WidgetRail file recycling" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }

    private static bool Recycle(string path, CancellationToken token)
    {
        IShellItem? source = null, parent = null, bin = null, recycled = null;
        ITransferSource? transfer = null;
        try
        {
            var itemId = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, in itemId, out source));
            Marshal.ThrowExceptionForHR(source.GetParent(out parent));
            var handler = new Guid("d5e346a1-f753-4932-b403-4574800e2498");
            var transferId = typeof(ITransferSource).GUID;
            Marshal.ThrowExceptionForHR(parent.BindToHandler(0, in handler, in transferId, out transfer));
            var binId = new Guid("b7534046-3ecb-4c18-be4e-64cd4cb7d6ac");
            Marshal.ThrowExceptionForHR(SHGetKnownFolderItem(in binId, 0, 0, in itemId, out bin));
            token.ThrowIfCancellationRequested();
            var result = transfer.RecycleItem(source, bin, 0, out recycled);
            // Once Windows has moved the file, report the committed result even
            // if cancellation arrived while the synchronous operation was running.
            return result >= 0 && recycled is not null && !File.Exists(path);
        }
        finally
        {
            Release(recycled); Release(transfer); Release(bin); Release(parent); Release(source);
        }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, nint bindContext, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHGetKnownFolderItem(in Guid folder, uint flags, nint token, in Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint context, in Guid handler, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out ITransferSource transfer);
        [PreserveSig] int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint kind, out nint name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }
    [ComImport, Guid("00adb003-bde9-45c6-8e29-d09f9353e108"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITransferSource
    {
        [PreserveSig] int Advise(nint sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetProperties(nint properties);
        [PreserveSig] int OpenItem(IShellItem item, uint flags, in Guid iid, out nint result);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IShellItem moved);
        [PreserveSig] int RecycleItem(IShellItem item, IShellItem destination, uint flags, [MarshalAs(UnmanagedType.Interface)] out IShellItem recycled);
        [PreserveSig] int RemoveItem(IShellItem item, uint flags);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IShellItem renamed);
        [PreserveSig] int LinkItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, uint flags, out IShellItem linked);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item, out IShellItem updated);
        [PreserveSig] int GetDefaultDestinationName(IShellItem item, IShellItem destination, out nint name);
        [PreserveSig] int EnterFolder(IShellItem destination);
        [PreserveSig] int LeaveFolder(IShellItem destination);
    }
}
