using System.Globalization;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Host-private immutable capture choice. Never serialize this target to a widget.</summary>
internal sealed record WebCaptureSourceSelection(NativeWindowPreviewTarget Target,string ApplicationName,
    bool ApplicationAudio,WebCaptureStreamOptions Options);

internal sealed record WebCaptureWindowChoice(string Id,string Label,string ApplicationName,NativeWindowPreviewTarget Target)
{
    internal string? ApplicationIdentity { get; init; }
}

internal static class WebCaptureSourcePolicy
{
    internal static WebCaptureWindowChoice? AutomaticReplayTarget(IReadOnlyList<TaskWindowSummary> windows, uint currentProcessId,
        ReplayApplicationApprovals approvals)
    {
        // A minimized second approved app is still open and makes the app choice ambiguous.
        string? application = null;
        foreach (var window in windows)
        {
            if (window.ApplicationIdentity is not { } id || !approvals.Contains(id)) continue;
            if (application is not null && !string.Equals(application, id, StringComparison.OrdinalIgnoreCase)) return null;
            application = id;
        }
        if (application is null) return null;
        var eligible = EligibleChoices(windows.Where(window => string.Equals(window.ApplicationIdentity, application, StringComparison.OrdinalIgnoreCase)), currentProcessId);
        return eligible.Count == 1 ? eligible[0] : null;
    }

    internal static WebCaptureWindowChoice? FocusedApprovedTarget(IReadOnlyList<WebCaptureWindowChoice> choices,
        ReplayApplicationApprovals approvals, NativeWindowPreviewTarget? focused) =>
        focused is null ? null : choices.FirstOrDefault(choice => choice.Target == focused && approvals.Contains(choice.ApplicationIdentity));

    internal static IReadOnlyList<WebCaptureWindowChoice> Candidates(IEnumerable<TaskWindowSummary> windows, uint currentProcessId) =>
        EligibleChoices(windows.Take(64), currentProcessId);

    private static IReadOnlyList<WebCaptureWindowChoice> EligibleChoices(IEnumerable<TaskWindowSummary> windows, uint currentProcessId)
    {
        var choices=new List<WebCaptureWindowChoice>();
        var handles=new HashSet<ulong>();
        foreach(var window in windows)
        {
            if(window is null || window.IsMinimized || window.PreviewTarget is not { } target || target.ProcessId==currentProcessId || !Valid(target,out var handle) || !handles.Add(handle))continue;
            var application=Clean(window.ApplicationName,120);
            var title=Clean(window.Title,240);
            if(application.Length==0)application=title.Length>0?Clean(title,120):"Application";
            var label=title.Length==0 || string.Equals(title,application,StringComparison.Ordinal)?application:application+" — "+title;
            choices.Add(new("source."+choices.Count,label,application,target) { ApplicationIdentity = window.ApplicationIdentity });
        }
        return choices;
    }

    private static bool Valid(NativeWindowPreviewTarget target,out ulong handle)
    {
        handle=0;
        return target.ProcessId!=0&&Hex(target.Handle,out handle)&&Hex(target.ProcessCreated,out _)&&
            target.ClassName is {Length:>0 and <=255}&&!target.ClassName.Any(char.IsControl);
    }
    private static bool Hex(string? value,out ulong number)
    {
        number=0;
        return value is {Length:>0 and <=16}&&value.All(char.IsAsciiHexDigit)&&
            ulong.TryParse(value,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out number)&&number!=0;
    }
    private static string Clean(string? value,int maximum)
    {
        var text=string.Join(' ',new string((value??"").Where(c=>!char.IsControl(c)).ToArray())
            .Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        if(text.Length<=maximum)return text;
        if(char.IsHighSurrogate(text[maximum-1])&&char.IsLowSurrogate(text[maximum]))maximum--;
        return text[..maximum];
    }
}
