using WidgetRail.PlatformBroker;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task ChooseCaptureSourceAsync(HostCaptureSourceRequest request, string? widgetId)
    {
        string? directory = null;
        var selection = selectionVersion;
        try
        {
            if (activeWidget != widgetId) return;
            directory = await PickCaptureCopyDestinationAsync(async token => !retired && visible && activeWidget == widgetId && selection == selectionVersion &&
                await owner!.Session.IsCaptureSourceCurrentAsync(request.Id, token), lifetime.Token, "Link this folder");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-source-picker", error); }
        finally
        {
            try { if (!retired) await owner!.Session.CompleteCaptureSourceAsync(request.Id, directory, lifetime.Token); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("capture-source-completion", error); }
        }
    }
}
