using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WidgetRail.OverlayFrontend.WinUI.Previews;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePreviewHealth
{
    public uint Size, Version, Phase, Reason, ProcessId;
    public int Error;
    public ulong Window, Slot, Frames;
    public long StartedAt;
    internal static NativePreviewHealth Empty => new() { Size = 56, Version = 1 };
}

/// <summary>Reports only slow close phases and their eventual completion, never frames.</summary>
internal sealed class PreviewCloseWatchdog
{
    private NativePreviewHealth? reported;

    internal string? Observe(NativePreviewHealth current, long now)
    {
        if (reported is { } previous && (current.Phase != previous.Phase ||
            current.Slot != previous.Slot || current.StartedAt != previous.StartedAt))
        {
            reported = null;
            return Describe("completed", previous, now);
        }
        if (current.Phase == 0 || current.StartedAt <= 0 || now < current.StartedAt ||
            reported is not null || Stopwatch.GetElapsedTime(current.StartedAt, now) < TimeSpan.FromSeconds(2)) return null;
        reported = current;
        return Describe("slow", current, now);
    }

    private static string Describe(string status, NativePreviewHealth value, long now)
    {
        var phase = value.Phase switch { 1 => "detach-callback", 2 => "close-session", 3 => "close-pool", _ => "unknown" };
        return FormattableString.Invariant($"status={status} phase={phase} reason={value.Reason} slot={value.Slot} sourcePid={value.ProcessId} sourceHwnd={value.Window} frames={value.Frames} hresult=0x{value.Error:X8} observedDurationMs={Stopwatch.GetElapsedTime(value.StartedAt, now).TotalMilliseconds:F0}");
    }
}
