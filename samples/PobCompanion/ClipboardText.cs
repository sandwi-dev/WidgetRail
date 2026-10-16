using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WidgetRail.Samples.PobCompanion;

/// <summary>Clipboard access occurs only in response to the user's Paste/Copy actions.</summary>
internal static partial class ClipboardText
{
    public static string Read()
    {
        if (!OpenClipboard(0)) throw new IOException("Clipboard is busy. Try Paste again.");
        try
        {
            var data = GetClipboardData(13);
            if (data == 0) throw new IOException("Copy a PoB share code, build link, or item first.");
            var size = GlobalSize(data);
            if (size > BuildDocument.MaximumBytes * 2u) throw new IOException("Clipboard text is too large.");
            var pointer = GlobalLock(data);
            if (pointer == 0) throw new IOException("Clipboard text could not be read.");
            try { return (Marshal.PtrToStringUni(pointer, checked((int)size / 2)) ?? "").TrimEnd('\0'); }
            finally { GlobalUnlock(data); }
        }
        finally { CloseClipboard(); }
    }

    public static void Write(string text)
    {
        if (text.Length > BuildDocument.MaximumBytes) throw new IOException("Export is too large for the clipboard.");
        var owner = CreateWindowExW(0, "STATIC", "WidgetRail build export", 0, 0, 0, 0, 0, new nint(-3), 0, 0, 0);
        if (owner == 0) throw new IOException("Could not prepare the clipboard.");
        nint memory = 0;
        try
        {
            if (!OpenClipboard(owner)) throw new IOException("Clipboard is busy. Try Copy again.");
            try
            {
                memory = GlobalAlloc(2, checked((nuint)(text.Length + 1) * 2));
                if (memory == 0) throw new OutOfMemoryException();
                var pointer = GlobalLock(memory);
                if (pointer == 0) throw new IOException("Could not prepare the export.");
                try { Marshal.Copy((text + '\0').ToCharArray(), 0, pointer, text.Length + 1); }
                finally { GlobalUnlock(memory); }
                if (!EmptyClipboard() || SetClipboardData(13, memory) == 0) throw new IOException("Could not copy the build.");
                memory = 0; // Clipboard owns the allocation now.
            }
            finally { CloseClipboard(); }
        }
        finally { if (memory != 0) GlobalFree(memory); DestroyWindow(owner); }
    }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool OpenClipboard(nint owner);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseClipboard();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EmptyClipboard();
    [LibraryImport("user32.dll")] private static partial nint GetClipboardData(uint format);
    [LibraryImport("user32.dll")] private static partial nint SetClipboardData(uint format, nint memory);
    [LibraryImport("kernel32.dll")] private static partial nuint GlobalSize(nint memory);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalLock(nint memory);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GlobalUnlock(nint memory);
    [LibraryImport("kernel32.dll")] private static partial nint GlobalFree(nint memory);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateWindowExW(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint window);
}
