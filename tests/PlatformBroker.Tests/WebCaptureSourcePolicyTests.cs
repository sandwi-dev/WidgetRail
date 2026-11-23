using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;

internal static class WebCaptureSourcePolicyTests
{
    internal static Task Run()
    {
        var valid = Window(1);
        var candidates = WebCaptureSourcePolicy.Candidates(new[]
        {
            valid, valid with { WindowId="duplicate", Title="Duplicate" },
            Window(2) with { IsMinimized=true }, Window(3) with { PreviewTarget=null },
            Window(4) with { PreviewTarget=valid.PreviewTarget! with { Handle="4",ProcessId=999 } },
            Window(5) with { PreviewTarget=valid.PreviewTarget! with { Handle="0" } },
            Window(6) with { PreviewTarget=valid.PreviewTarget! with { Handle="not-hex" } },
            Window(7) with { PreviewTarget=valid.PreviewTarget! with { Handle="7",ProcessCreated="0" } },
            Window(8) with { PreviewTarget=valid.PreviewTarget! with { Handle="8",ClassName="bad\nclass" } },
            Window(9) with { PreviewTarget=valid.PreviewTarget! with { Handle="9",ProcessId=0 } },
            Window(10) with { ApplicationName=" Game\r\n", Title=" Game " }
        },999);
        Check(candidates.Count==2,"Only distinct available non-self windows are offered.");
        Check(candidates[0].Target==valid.PreviewTarget&&candidates[0].Id=="source.0"&&candidates[1].Id=="source.1","Selection preserves exact host identity and provider order.");
        Check(candidates[1].Label=="Game"&&candidates[1].ApplicationName=="Game","Identical application/title are shown once; controls are removed.");
        var longName=new string('x',119)+"🎮"+new string('x',300);
        var shortened=WebCaptureSourcePolicy.Candidates(new[]{Window(1) with {ApplicationName=longName,Title=new string('y',400)}},999).Single();
        Check(shortened.ApplicationName.Length==119&&!char.IsSurrogate(shortened.ApplicationName[^1])&&shortened.Label.Length<=363,"Display fields are bounded without splitting surrogate pairs.");
        var scanned=0;
        IEnumerable<TaskWindowSummary> Many()
        {
            for(var index=1;index<1000;index++){scanned++;yield return Window(index);}
        }
        Check(WebCaptureSourcePolicy.Candidates(Many(),999).Count==64&&scanned==64,"Enumeration consumes at most 64 windows.");
        return Task.CompletedTask;
    }
    private static TaskWindowSummary Window(int id)=>new(id.ToString(),"Application "+id,"Window "+id,false)
        {PreviewTarget=new(id.ToString("X"),123,"ABCDEF","SyntheticWindow")};
    internal static Task AutomaticReplay()
    {
        var a = new ReplayApprovedApplication(new string('a', 64), "Game A");
        var b = new ReplayApprovedApplication(new string('b', 64), "Game B");
        var approvals = ReplayApplicationApprovals.Empty.Add(a).Add(b);
        var first = Window(1) with { ApplicationIdentity = a.Id.ToUpperInvariant() };
        var second = Window(2) with { ApplicationIdentity = b.Id };
        var other = Window(3);
        var sibling = Window(4) with { ApplicationIdentity = first.ApplicationIdentity };
        WebCaptureWindowChoice? Auto(params TaskWindowSummary[] windows) =>
            WebCaptureSourcePolicy.AutomaticReplayTarget(windows, 999, approvals);
        WebCaptureWindowChoice? Manual(NativeWindowPreviewTarget? focused, params TaskWindowSummary[] windows) =>
            WebCaptureSourcePolicy.FocusedApprovedTarget(WebCaptureSourcePolicy.Candidates(windows, 999), approvals, focused);
        Check(Auto(first, other)?.Target == first.PreviewTarget, "A unique approved source can start automatically.");
        Check(Auto(first, second) is null && Auto(second, first) is null, "Approval order or foreground must not resolve an automatic start with two open apps.");
        Check(Auto(first, second with { IsMinimized = true }) is null, "Another open but minimized approved app was ignored.");
        Check(Auto(first, sibling) is null, "Two eligible windows of one app are ambiguous.");
        Check(Auto(first with { IsMinimized = true }) is null && Auto(other) is null, "Automatic replay selected an ineligible source.");
        Check(Auto(Enumerable.Repeat(other, 64).Append(first).Append(second).ToArray()) is null, "Truncation hid a second app.");
        Check(Manual(first.PreviewTarget, second, first)?.Target == first.PreviewTarget, "Explicit start did not use the exact focused approved window.");
        Check(Manual(other.PreviewTarget, first, other) is null && Manual(other.PreviewTarget, first, second, other) is null,
            "An unapproved foreground must open the chooser even with only one approved alternative.");
        Check(Manual(null, first) is null && Manual(first.PreviewTarget! with { ProcessCreated = "FFFF" }, first) is null &&
            Manual(first.PreviewTarget, first with { IsMinimized = true }) is null, "Missing, stale or minimized foreground must open the chooser.");
        return Task.CompletedTask;
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
