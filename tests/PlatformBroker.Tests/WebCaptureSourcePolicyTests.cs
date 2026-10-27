using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.PlatformBroker;

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
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
