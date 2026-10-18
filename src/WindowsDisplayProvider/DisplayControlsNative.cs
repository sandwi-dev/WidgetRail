using System.ComponentModel;
using System.Runtime.InteropServices;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsDisplayProvider;

internal sealed unsafe partial class WindowsDisplayNative
{
    public DisplayControlsState ReadControls()
    {
        var current = Capture();
        var paths = current.ReadPaths();
        var summaries = current.Summaries();
        var outputs = DxgiDisplayModes.Read();
        return new(paths.Select((path, index) =>
        {
            var hdr = ReadHdr(path.Target);
            var shared = paths.Count(item => item.Source.Adapter == path.Source.Adapter && item.Source.Id == path.Source.Id) > 1;
            var available = shared ? [] : ModesFor(path, outputs);
            var summary = summaries[index];
            var mode = available.FirstOrDefault(mode => mode.Width == summary.Width && mode.Height == summary.Height &&
                (ulong)mode.RefreshNumerator * path.Target.Refresh.Denominator ==
                (ulong)path.Target.Refresh.Numerator * mode.RefreshDenominator);
            var limit = Math.Min(128, 1024 / paths.Length); // Stay inside broker per-array and response-byte limits.
            var modes = available.Take(limit).ToList();
            if (mode is not null && !modes.Contains(mode)) modes[^1] = mode;
            return new DisplayControlMonitor(summary.Id, summary.Name, modes, mode?.Id,
                hdr.Supported, hdr.Enabled == true, hdr.Active,
                shared ? "Resolution and refresh controls require an extended or single-display setup." :
                modes.Count == 0 ? "Windows did not expose selectable modes for this connection." : null, available.Length > modes.Count);
        }).ToArray());
    }

    public DisplayConfiguration PrepareControl(DisplayControlRequest request)
    {
        // Enumerate again at admission; an old widget list cannot authorize a mode
        // belonging to a disconnected monitor or a changed driver configuration.
        var controls = ReadControls();
        var control = controls.Displays.SingleOrDefault(display => display.Id == request.DisplayId)
            ?? throw new BrokerException("display_monitor_missing", "This display is no longer connected.");
        var current = Capture();
        var index = current.Summaries().ToList().FindIndex(display => display.Id == request.DisplayId);
        if (index < 0) throw new BrokerException("display_monitor_missing", "This display is no longer connected.");
        if (request.HdrEnabled is { } enabled)
        {
            if (!control.HdrSupported) throw new BrokerException("display_control_unavailable", "HDR is unavailable on this display.");
            var hdr = new bool?[current.Targets.Length];
            hdr[index] = enabled;
            return current with { Hdr = hdr };
        }
        var mode = control.Modes.SingleOrDefault(mode => mode.Id == request.ModeId)
            ?? throw new BrokerException("display_mode_unavailable", "This mode is no longer available.");
        return DisplayControlConfiguration.WithMode(current, index, mode);
    }

    private static DisplayControlMode[] ModesFor(NativePath path, IReadOnlyDictionary<string, DxgiDisplayModes.Mode[]> outputs)
    {
        var source = new SourceName { Type = 1, Size = (uint)sizeof(SourceName), Adapter = path.Source.Adapter, Id = path.Source.Id };
        if (DisplayConfigGetDeviceInfo(&source) != 0 || !outputs.TryGetValue(ReadString(source.Device, 32), out var modes)) return [];
        var portrait = path.Target.Rotation is 2 or 4;
        return modes.Where(mode => mode.Width is > 0 and <= 32768 && mode.Height is > 0 and <= 32768 &&
                mode.Refresh.Numerator > 0 && mode.Refresh.Denominator > 0 && mode.Scanline is 0 or 1)
            .Select(mode =>
            {
                var divisor = Gcd(mode.Refresh.Numerator, mode.Refresh.Denominator);
                var width = (int)(portrait ? mode.Height : mode.Width);
                var height = (int)(portrait ? mode.Width : mode.Height);
                var numerator = mode.Refresh.Numerator / divisor;
                var denominator = mode.Refresh.Denominator / divisor;
                return new DisplayControlMode($"{width}-{height}-{numerator}-{denominator}", width, height, numerator, denominator);
            }).Distinct().OrderByDescending(mode => (long)mode.Width * mode.Height)
            .ThenByDescending(mode => (double)mode.RefreshNumerator / mode.RefreshDenominator).ToArray();
    }
    private static uint Gcd(uint a, uint b) { while (b != 0) { var next = a % b; a = b; b = next; } return a; }

    internal static (bool Supported, bool? Enabled, bool Active) ReadHdr(NativeTarget target)
    {
        var info = new AdvancedColorInfo { Type = 15, Size = (uint)sizeof(AdvancedColorInfo), Adapter = target.Adapter, Id = target.Id };
        if (DisplayConfigGetDeviceInfo(&info) != 0) return (false, null, false);
        // INFO_2 distinguishes HDR from WCG. Do not relabel legacy advanced-color support as HDR.
        return ((info.Flags & 0x18) == 0x10, (info.Flags & 0x10) == 0 ? null : (info.Flags & 0x20) != 0, info.ActiveMode == 2);
    }
    private static void SetHdr(NativeTarget target, bool enabled)
    {
        var info = new HdrState { Type = 16, Size = (uint)sizeof(HdrState), Adapter = target.Adapter, Id = target.Id, Flags = enabled ? 1u : 0 };
        Check(DisplayConfigSetDeviceInfo(&info));
    }
    [StructLayout(LayoutKind.Sequential)] internal struct AdvancedColorInfo
    { public uint Type, Size; public NativeLuid Adapter; public uint Id, Flags, Encoding, Bits, ActiveMode; }
    [StructLayout(LayoutKind.Sequential)] internal struct HdrState
    { public uint Type, Size; public NativeLuid Adapter; public uint Id, Flags; }
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int DisplayConfigGetDeviceInfo(AdvancedColorInfo* request);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int DisplayConfigSetDeviceInfo(HdrState* request);
}

internal static class DisplayControlConfiguration
{
    internal static DisplayConfiguration WithMode(DisplayConfiguration current, int index, DisplayControlMode mode)
    {
        var paths = current.ReadPaths(); var modes = current.ReadModes();
        if (index < 0 || index >= paths.Length || mode.Width is < 1 or > 32768 || mode.Height is < 1 or > 32768 ||
            mode.RefreshNumerator == 0 || mode.RefreshDenominator == 0)
            throw new BrokerException("display_mode_unavailable", "This mode is unavailable.");
        ref var path = ref paths[index];
        var sourceAdapter = path.Source.Adapter; var sourceId = path.Source.Id;
        if (paths.Count(item => item.Source.Adapter == sourceAdapter && item.Source.Id == sourceId) > 1)
            throw new BrokerException("display_control_unavailable", "Change duplicated displays through a saved profile.");
        DisplayControlLayout.Resize(paths, modes, path.Source.ModeIndex, (uint)mode.Width, (uint)mode.Height);
        path.Target.ModeIndex = uint.MaxValue;
        path.Target.ScanLine = 1; // Only progressive modes are offered; never retain prior interlacing.
        path.Target.Refresh = new() { Numerator = mode.RefreshNumerator, Denominator = mode.RefreshDenominator };
        // The old target signal's pixel clock/timings no longer describe this mode.
        // Let Windows choose its driver timings, and demand the requested exact readback.
        return DisplayConfiguration.Create(paths, modes, current.Targets) with { SelectMode = true };
    }
}

internal static class DisplayHdrRestoration
{
    internal static IReadOnlyList<(NativeTarget Target, bool Enabled)> ForAvailable(DisplayConfiguration baseline, DisplayConfiguration available)
    {
        baseline.Validate(); available.Validate();
        var result = new List<(NativeTarget, bool)>();
        if (baseline.Hdr is null) return result;
        var paths = available.ReadPaths();
        for (var index = 0; index < baseline.Targets.Length; ++index)
        {
            if (baseline.Hdr[index] is not { } enabled) continue;
            var identity = baseline.Targets[index];
            var matches = available.Targets.Select((target, targetIndex) => (target, targetIndex))
                .Where(item => item.target.DevicePath.Equals(identity.DevicePath, StringComparison.OrdinalIgnoreCase) &&
                    (identity.HardwareKey is null || item.target.HardwareKey is null || identity.HardwareKey == item.target.HardwareKey)).ToArray();
            if (matches.Length == 0 && identity.HardwareKey is not null)
                matches = available.Targets.Select((target, targetIndex) => (target, targetIndex))
                    .Where(item => item.target.HardwareKey == identity.HardwareKey).ToArray();
            if (matches.Length > 1) throw new BrokerException("display_monitor_ambiguous", "HDR rollback cannot identify the monitor reliably.");
            if (matches.Length == 1) result.Add((paths[matches[0].targetIndex].Target, enabled));
        }
        return result;
    }
}
