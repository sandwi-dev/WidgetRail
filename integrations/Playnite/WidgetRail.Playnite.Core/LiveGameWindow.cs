using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WidgetRail.Internal;

namespace WidgetRail.Playnite;

/// <summary>Observation of a live window on the desktop where Playnite runs.</summary>
public sealed class LiveGameWindow
{
    public long Handle { get; }
    public int ProcessId { get; }
    public long Created { get; }
    public GameWindowRule Rule { get; }
    public string Key => CaptureProcessKey.Window((uint)ProcessId, (ulong)Created, (ulong)Handle);

    private LiveGameWindow(long handle, uint pid, long created, GameWindowRule rule)
    { Handle = handle; ProcessId = checked((int)pid); Created = created; Rule = rule; }

    public static LiveGameWindow? Inspect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero ||
            GetAncestor(hwnd, 2) != hwnd || GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0) return null;
        if (DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid || !GetProcessTimes(process, out var created, out _, out _, out _) || created <= 0) return null;
        var path = new StringBuilder(32768);
        var length = (uint)path.Capacity;
        if (!QueryFullProcessImageNameW(process, 0, path, ref length)) return null;

        var rule = ReadRule(process, path.ToString());
        if (rule is null || GetWindowThreadProcessId(hwnd, out var currentPid) == 0 || currentPid != pid) return null;
        var handle = IntPtr.Size == 4 ? unchecked((uint)hwnd.ToInt32()) : hwnd.ToInt64();
        return new LiveGameWindow(handle, pid, created, rule);
    }

    private static GameWindowRule? ReadRule(SafeProcessHandle process, string path)
    {
        var family = new StringBuilder(128);
        var familyLength = (uint)family.Capacity;
        var status = GetPackageFamilyName(process, ref familyLength, family);
        if (status == 15700) return GameWindowRule.FromImage(path); // APPMODEL_ERROR_NO_PACKAGE
        if (status != 0) return null;
        var full = new StringBuilder(256);
        var fullLength = (uint)full.Capacity;
        if (GetPackageFullName(process, ref fullLength, full) != 0) return null;
        uint rootLength = 0;
        if (GetPackagePathByFullName(full.ToString(), ref rootLength, null) != 122 || rootLength is 0 or > 32768) return null;
        var root = new StringBuilder((int)rootLength);
        if (GetPackagePathByFullName(full.ToString(), ref rootLength, root) != 0) return null;
        var rule = GameWindowRule.FromImage(path, family.ToString(), root.ToString());
        if (rule != null) return rule;
        // Xbox installs can register a WindowsApps junction on C: while the live image is on another volume.
        // Resolve both objects through Windows before taking a package-relative path.
        var physicalRoot = FinalPath(root.ToString());
        var physicalImage = FinalPath(path);
        return physicalRoot == null || physicalImage == null ? null : GameWindowRule.FromImage(physicalImage, family.ToString(), physicalRoot);
    }

    private static string? FinalPath(string path)
    {
        using var file = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (file.IsInvalid) return null;
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) return null;
        var final = buffer.ToString();
        return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final.Substring(4) : final;
    }

    // The choice is confirmed against a fresh observation. Duplicate windows
    // for one saved executable require another explicit selection.
    public static LiveGameWindow? FindUnique(GameWindowRule? rule, string? exactWindowKey = null)
    {
        if (rule is null && string.IsNullOrEmpty(exactWindowKey)) return null;
        var candidates = new List<IntPtr>();
        if (!EnumWindows((hwnd, _) => { candidates.Add(hwnd); return true; }, IntPtr.Zero)) return null;
        LiveGameWindow? match = null;
        foreach (var hwnd in candidates)
        {
            var current = Inspect(hwnd);
            if (current is null || rule != null && !rule.Matches(current.Rule) ||
                exactWindowKey != null && !string.Equals(current.Key, exactWindowKey, StringComparison.Ordinal)) continue;
            if (match != null) return null;
            match = current;
        }
        return match?.Revalidate();
    }

    public LiveGameWindow? Revalidate()
    {
        var verified = Inspect(IntPtr.Size == 4 ? new IntPtr(unchecked((int)Handle)) : new IntPtr(Handle));
        return verified?.Key == Key && Rule.Matches(verified.Rule) ? verified : null;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool VisitWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(VisitWindow visit, IntPtr parameter);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("dwmapi.dll", ExactSpelling = true)] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref uint length);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int GetPackageFullName(SafeProcessHandle process, ref uint length, StringBuilder name);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder? path);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFileW(string path, uint access,
        uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle,
        StringBuilder path, uint length, uint flags);
}
