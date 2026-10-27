using System.Globalization;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Host-private immutable capture choice. Never serialize this target to a widget.</summary>
internal sealed record WebCaptureSourceSelection(NativeWindowPreviewTarget Target,string ApplicationName,
    bool ApplicationAudio,WebCaptureStreamOptions Options);

internal sealed record WebCaptureWindowChoice(string Id,string Label,string ApplicationName,NativeWindowPreviewTarget Target);

internal static class WebCaptureSourcePolicy
{
    internal static IReadOnlyList<WebCaptureWindowChoice> Candidates(IEnumerable<TaskWindowSummary> windows,uint currentProcessId)
    {
        var choices=new List<WebCaptureWindowChoice>();
        var handles=new HashSet<ulong>();
        foreach(var window in windows.Take(64))
        {
            if(window is null || window.IsMinimized || window.PreviewTarget is not { } target || target.ProcessId==currentProcessId || !Valid(target,out var handle) || !handles.Add(handle))continue;
            var application=Clean(window.ApplicationName,120);
            var title=Clean(window.Title,240);
            if(application.Length==0)application=title.Length>0?Clean(title,120):"Application";
            var label=title.Length==0 || string.Equals(title,application,StringComparison.Ordinal)?application:application+" — "+title;
            choices.Add(new("source."+choices.Count,label,application,target));
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
