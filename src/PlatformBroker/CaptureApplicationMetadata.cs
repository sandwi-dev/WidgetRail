using System.Globalization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

/// <summary>Match capture identity, never foreground order, to the Task Switcher naming source.</summary>
public static class CaptureApplicationMetadata
{
    public static CaptureApplicationContext? Resolve(NativeWindowPreviewTarget captured, IReadOnlyList<TaskWindowSummary> windows)
    {
        if (windows.Count > 64 || captured.ProcessId == 0 || !Hex(captured.Handle, out var handle) || handle == 0 ||
            !Hex(captured.ProcessCreated, out var created) || created == 0 || string.IsNullOrEmpty(captured.ClassName)) return null;
        var matches = windows.Where(window => window?.PreviewTarget is { } target && target.ProcessId == captured.ProcessId &&
            target.ClassName == captured.ClassName && Hex(target.Handle, out var hwnd) && hwnd == handle &&
            Hex(target.ProcessCreated, out var start) && start == created).Take(2).ToArray();
        if (matches.Length != 1) return null;
        var context = new CaptureApplicationContext(matches[0].ApplicationName, matches[0].Title);
        return context.IsWellFormed() ? context : null;
    }
    private static bool Hex(string text, out ulong value) => ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
