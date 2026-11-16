using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YouTubeWidget;

public sealed partial class YouTubeVideoWidget
{
    private async ValueTask ManagePinsAsync(CancellationToken token)
    {
        _model.Update(state=>state with{PinManagementNotice=null});
        try{await HostServices.Pinning.RequestManageAsync(token);}
        catch(Exception error) when(error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
        {_model.Update(state=>state with{PinManagementNotice="Pin management requires a supporting WidgetRail host."});}
    }
}
