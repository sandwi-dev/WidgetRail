using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DisplayProfilesWidget.Tests")]

namespace WidgetRail.WindowsDisplayProvider;

internal sealed record DisplayIdentity(string DevicePath, string Name);
internal sealed record DisplayConfiguration(byte[] Paths, byte[] Modes, DisplayIdentity[] Targets)
{
    internal NativePath[] ReadPaths() => Read<NativePath>(Paths, 16);
    internal NativeMode[] ReadModes() => Read<NativeMode>(Modes, 64);
    private static T[] Read<T>(byte[] bytes, int maximum) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        if (bytes is null || bytes.Length == 0 || bytes.Length % size != 0 || bytes.Length / size > maximum)
            throw new BrokerException("display_profile_invalid", "This display profile is damaged. Save it again.");
        return MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    }
    internal void Validate()
    {
        var paths = ReadPaths();
        var modes = ReadModes();
        if (Targets is null || Targets.Length != paths.Length || Targets.Any(target => target is null ||
            string.IsNullOrWhiteSpace(target.DevicePath) || target.DevicePath.Length > 256 ||
            string.IsNullOrWhiteSpace(target.Name) || target.Name.Length > 128) ||
            Targets.Select(target => target.DevicePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Targets.Length)
            throw new BrokerException("display_profile_invalid", "This display profile has invalid monitors.");
        foreach (var path in paths)
        {
            if ((path.Flags & 1) == 0 || path.Source.ModeIndex >= modes.Length || path.Target.ModeIndex >= modes.Length ||
                modes[path.Source.ModeIndex].Type != 1 || modes[path.Target.ModeIndex].Type != 2 ||
                path.Target.Rotation is < 1 or > 4 || path.Target.Refresh.Denominator == 0)
                throw new BrokerException("display_profile_invalid", "This display profile has invalid modes.");
            var source = modes[path.Source.ModeIndex].Source;
            if (source.Width is 0 or > 32768 || source.Height is 0 or > 32768 ||
                source.X is < -65536 or > 65536 || source.Y is < -65536 or > 65536)
                throw new BrokerException("display_profile_invalid", "This display profile has invalid dimensions.");
        }
    }
    internal IReadOnlyList<DisplayProfileMonitor> Summaries()
    {
        Validate();
        var paths = ReadPaths(); var modes = ReadModes();
        return paths.Select((path, index) =>
        {
            var source = modes[path.Source.ModeIndex].Source;
            var identity = Targets[index];
            return new DisplayProfileMonitor(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.DevicePath.ToUpperInvariant())))[..24],
                identity.Name, (int)source.Width, (int)source.Height, source.X, source.Y,
                (double)path.Target.Refresh.Numerator / path.Target.Refresh.Denominator,
                path.Target.Rotation switch { 2 => "Portrait", 3 => "Landscape flipped", 4 => "Portrait flipped", _ => "Landscape" },
                source.X == 0 && source.Y == 0);
        }).ToArray();
    }
    internal string Mode
    {
        get
        {
            var paths = ReadPaths();
            var sources = paths.Select(path => (path.Source.Adapter, path.Source.Id)).Distinct().Count();
            return paths.Length == 1 ? "Single display" : sources == 1 ? "Duplicated" : sources == paths.Length ? "Extended" : "Mixed";
        }
    }
    internal bool Matches(DisplayConfiguration other) => Mode == other.Mode &&
        Summaries().OrderBy(display => display.Id).SequenceEqual(other.Summaries().OrderBy(display => display.Id));
    internal static DisplayConfiguration Create(NativePath[] paths, NativeMode[] modes, DisplayIdentity[] targets) =>
        new(MemoryMarshal.AsBytes(paths.AsSpan()).ToArray(), MemoryMarshal.AsBytes(modes.AsSpan()).ToArray(), targets);
}

[StructLayout(LayoutKind.Sequential)] internal record struct NativeLuid(uint Low, int High);
[StructLayout(LayoutKind.Sequential)] internal struct NativeRational { public uint Numerator, Denominator; }
[StructLayout(LayoutKind.Sequential)] internal struct NativeSource { public NativeLuid Adapter; public uint Id, ModeIndex, Status; }
[StructLayout(LayoutKind.Sequential)] internal struct NativeTarget
{
    public NativeLuid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling;
    public NativeRational Refresh; public uint ScanLine; public int Available; public uint Status;
}
[StructLayout(LayoutKind.Sequential)] internal struct NativePath { public NativeSource Source; public NativeTarget Target; public uint Flags; }
[StructLayout(LayoutKind.Sequential)] internal struct NativeSourceMode { public uint Width, Height, PixelFormat; public int X, Y; }
[StructLayout(LayoutKind.Explicit, Size = 64)] internal struct NativeMode
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(4)] public uint Id;
    [FieldOffset(8)] public NativeLuid Adapter;
    [FieldOffset(16)] public NativeSourceMode Source;
    // Preserve the entire target signal union, including the pixel clock and timings.
    [FieldOffset(16)] public ulong Signal0;
    [FieldOffset(24)] public ulong Signal1;
    [FieldOffset(32)] public ulong Signal2;
    [FieldOffset(40)] public ulong Signal3;
    [FieldOffset(48)] public ulong Signal4;
    [FieldOffset(56)] public ulong Signal5;
}
internal sealed record DisplayPathTarget(NativePath Path, DisplayIdentity Identity);
internal interface IDisplayNative
{
    DisplayConfiguration Capture();
    IReadOnlyList<DisplayPathTarget> ConnectedPaths();
    void Validate(DisplayConfiguration configuration);
    void Apply(DisplayConfiguration configuration, bool persist);
    void Restore(DisplayConfiguration configuration) => Apply(configuration, persist: false);
}

internal static class DisplayProfileMatching
{
    internal static DisplayConfiguration Remap(DisplayConfiguration saved, IReadOnlyList<DisplayPathTarget> connected)
    {
        saved.Validate();
        var paths = saved.ReadPaths(); var modes = saved.ReadModes();
        var groups = paths.Select((path, index) => (Key: (path.Source.Adapter, path.Source.Id), Index: index))
            .GroupBy(item => item.Key).Select(group => group.Select(item => item.Index).ToArray()).ToArray();
        var choices = new Dictionary<int, DisplayPathTarget[]>();
        foreach (var group in groups)
            foreach (var index in group)
            {
                var candidates = connected.Where(candidate => candidate.Path.Target.Available != 0 &&
                    candidate.Identity.DevicePath.Equals(saved.Targets[index].DevicePath, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length == 0)
                    throw new BrokerException("display_monitor_missing", $"Connect {saved.Targets[index].Name} to use this profile.");
                if (candidates.Select(candidate => (candidate.Path.Target.Adapter, candidate.Path.Target.Id)).Distinct().Count() != 1)
                    throw new BrokerException("display_monitor_ambiguous", "Windows reported an ambiguous monitor. Reconnect it and try again.");
                choices[index] = candidates;
            }
        var used = new HashSet<(NativeLuid, uint)>();
        var selected = new DisplayPathTarget[paths.Length];
        var attempts = 0;
        bool Assign(int position)
        {
            if (position == groups.Length) return true;
            if (++attempts > 512) return false;
            var group = groups[position];
            foreach (var candidate in choices[group[0]].OrderByDescending(candidate => candidate.Path.Source.Id == paths[group[0]].Source.Id))
            {
                var key = (candidate.Path.Source.Adapter, candidate.Path.Source.Id);
                if (used.Contains(key)) continue;
                var matched = group.Select(index => choices[index].FirstOrDefault(path =>
                    (path.Path.Source.Adapter, path.Path.Source.Id) == key)).ToArray();
                if (matched.Any(item => item is null)) continue;
                used.Add(key);
                for (var i = 0; i < group.Length; ++i) selected[group[i]] = matched[i]!;
                if (Assign(position + 1)) return true;
                used.Remove(key);
            }
            return false;
        }
        if (!Assign(0)) throw new BrokerException("display_topology_unavailable", "The saved display arrangement is not available with these connections.");
        for (var index = 0; index < paths.Length; ++index)
        {
            ref var path = ref paths[index];
            var current = selected[index].Path;
            path.Source.Adapter = current.Source.Adapter; path.Source.Id = current.Source.Id;
            path.Target.Adapter = current.Target.Adapter; path.Target.Id = current.Target.Id;
            path.Target.Available = 1;
            modes[path.Source.ModeIndex].Adapter = path.Source.Adapter;
            modes[path.Source.ModeIndex].Id = path.Source.Id;
            modes[path.Target.ModeIndex].Adapter = path.Target.Adapter;
            modes[path.Target.ModeIndex].Id = path.Target.Id;
        }
        return DisplayConfiguration.Create(paths, modes, saved.Targets);
    }
}

[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsDisplayNative : IDisplayNative
{
    public DisplayConfiguration Capture()
    {
        var (paths, modes) = Query(2); // Desktop compatibility view: no per-monitor DPI writes.
        var result = DisplayConfiguration.Create(paths, modes, paths.Select(path => Name(path.Target)).ToArray());
        result.Validate();
        return result;
    }
    public IReadOnlyList<DisplayPathTarget> ConnectedPaths()
    {
        var (paths, _) = Query(1);
        var names = new Dictionary<(NativeLuid, uint), DisplayIdentity>();
        var result = new List<DisplayPathTarget>();
        foreach (var path in paths.Where(path => path.Target.Available != 0))
        {
            var key = (path.Target.Adapter, path.Target.Id);
            try
            {
                if (!names.TryGetValue(key, out var identity)) names[key] = identity = Name(path.Target);
                result.Add(new(path, identity));
            }
            catch (Win32Exception) { } // A disconnected target cannot invalidate other monitors.
        }
        return result;
    }
    public void Validate(DisplayConfiguration configuration) => Set(configuration, 0x20 | 0x40);
    public void Apply(DisplayConfiguration configuration, bool persist) => Set(configuration, 0x20 | 0x80 | (persist ? 0x200u : 0));
    public void Restore(DisplayConfiguration configuration)
    {
        try { Apply(DisplayProfileMatching.Remap(configuration, ConnectedPaths()), persist: false); }
        catch (Exception error) when (error is Win32Exception or BrokerException)
        {
            // A monitor may have been unplugged during confirmation. Ask Windows
            // for its last saved topology instead of leaving an unusable layout.
            Check(SetDisplayConfig(0, null, 0, null, 0x80 | 0xF));
        }
    }
    private static void Set(DisplayConfiguration configuration, uint flags)
    {
        configuration.Validate();
        var paths = configuration.ReadPaths(); var modes = configuration.ReadModes();
        fixed (NativePath* path = paths) fixed (NativeMode* mode = modes)
            Check(SetDisplayConfig((uint)paths.Length, path, (uint)modes.Length, mode, flags));
    }
    private static (NativePath[], NativeMode[]) Query(uint flags)
    {
        for (var attempt = 0; attempt < 5; ++attempt)
        {
            Check(GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount));
            if (pathCount is 0 or > 512 || modeCount > 1024)
                throw new BrokerException("display_unavailable", "Windows did not report a supported desktop configuration.");
            var paths = new NativePath[pathCount]; var modes = new NativeMode[modeCount];
            int error;
            fixed (NativePath* path = paths) fixed (NativeMode* mode = modes)
                error = QueryDisplayConfig(flags, ref pathCount, path, ref modeCount, mode, 0);
            if (error == 122) continue;
            Check(error);
            return (paths[..(int)pathCount], modes[..(int)modeCount]);
        }
        throw new BrokerException("display_changing", "The displays are still changing. Try again shortly.");
    }
    private static DisplayIdentity Name(NativeTarget target)
    {
        var request = new TargetName { Type = 2, Size = (uint)sizeof(TargetName), Adapter = target.Adapter, Id = target.Id };
        Check(DisplayConfigGetDeviceInfo(&request));
        var name = ReadString(request.FriendlyName, 64).Trim();
        var path = ReadString(request.DevicePath, 128);
        if (string.IsNullOrWhiteSpace(path)) throw new Win32Exception(1168);
        return new(path, string.IsNullOrWhiteSpace(name) ? "Display" : name);
    }
    private static void Check(int result) { if (result != 0) throw new Win32Exception(result); }
    private static string ReadString(char* value, int capacity)
    {
        var text = new ReadOnlySpan<char>(value, capacity);
        var end = text.IndexOf('\0');
        return new string(end < 0 ? text : text[..end]);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct TargetName
    {
        public uint Type, Size; public NativeLuid Adapter; public uint Id, Flags, Technology;
        public ushort Manufacturer, Product; public uint Connector;
        public fixed char FriendlyName[64]; public fixed char DevicePath[128];
    }
    [LibraryImport("user32.dll")] private static partial int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [LibraryImport("user32.dll")] private static partial int QueryDisplayConfig(uint flags, ref uint pathCount,
        NativePath* paths, ref uint modeCount, NativeMode* modes, nint topology);
    [LibraryImport("user32.dll")] private static partial int SetDisplayConfig(uint pathCount, NativePath* paths,
        uint modeCount, NativeMode* modes, uint flags);
    [LibraryImport("user32.dll")] private static partial int DisplayConfigGetDeviceInfo(TargetName* request);
}
